"""Linux-only disposable execution controller. Inputs/outputs are bounded JSON data."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import selectors
import signal
import socket
import subprocess
import sys
import time
import uuid

LIMIT = 24 * 1024 * 1024
OUTPUT_LIMIT = 2 * 1024 * 1024
RUNTIME = Path(__file__).resolve().parent


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def digest(value):
    return hashlib.sha256(canonical(value)).hexdigest()


def command(args, data=None, timeout=120, limit=OUTPUT_LIMIT):
    """Drain both pipes continuously; kill on time/output bounds, never print stderr."""
    with subprocess.Popen(args, stdin=subprocess.PIPE if data is not None else subprocess.DEVNULL,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, env={
                              "PATH": os.environ.get("PATH", "/usr/bin:/bin"), "HOME": "/nonexistent"}) as proc:
        # Input may be large: a writer thread avoids deadlock with stdout draining.
        import threading
        def write():
            try:
                proc.stdin.write(data)
                proc.stdin.close()
            except (BrokenPipeError, OSError):
                pass
        if data is not None:
            threading.Thread(target=write, daemon=True).start()
        selector = selectors.DefaultSelector()
        selector.register(proc.stdout, selectors.EVENT_READ)
        selector.register(proc.stderr, selectors.EVENT_READ)
        output, total, deadline = bytearray(), 0, time.monotonic() + timeout
        try:
            while selector.get_map():
                if time.monotonic() >= deadline:
                    raise ValueError("execution time limit")
                for key, _ in selector.select(.2):
                    chunk = os.read(key.fileobj.fileno(), 65536)
                    if not chunk:
                        selector.unregister(key.fileobj)
                        continue
                    total += len(chunk)
                    if total > limit:
                        raise ValueError("execution output limit")
                    if key.fileobj is proc.stdout:
                        output.extend(chunk)
            if proc.wait(timeout=2):
                raise ValueError("isolated command failed")
            return bytes(output)
        finally:
            selector.close()
            if proc.poll() is None:
                proc.kill()
                proc.wait()


def image(name):
    value = os.environ.get(name, "")
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9./:_-]*@sha256:[0-9a-f]{64}", value):
        raise ValueError("operator-approved digest image required")
    return value


def restricted(name, image_id, network="none"):
    return ["docker", "run", "--rm", "--name", name, "--pull=never", "--user", "65532:65532",
            "--read-only", "--cap-drop=ALL", "--security-opt=no-new-privileges", "--pids-limit=128",
            "--cpus=2", "--memory=4g", "--memory-swap=4g", "--network", network,
            "--sysctl", "net.ipv6.conf.all.disable_ipv6=1",
            "--tmpfs", "/work:rw,nosuid,nodev,size=1g,uid=65532,gid=65532",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=256m,uid=65532,gid=65532",
            "--workdir", "/work", "--env", "HOME=/tmp", "--env", "PATH=/usr/local/bin:/usr/bin:/bin",
            "--mount", f"type=bind,src={RUNTIME},dst=/runtime,readonly", "--entrypoint", "python3", "-i", image_id]


def safe_path(value):
    p = PurePosixPath(value)
    if not value or p.is_absolute() or "\\" in value or any(x in ("", ".", "..", ".git") for x in value.split("/")) or ":" in value or "\x00" in value:
        raise ValueError("unsafe source path")
    return value


def source_manifest(base):
    if not re.fullmatch(r"[0-9a-f]{40}", base):
        raise ValueError("invalid base")
    git = ["git", "--no-optional-locks", "--no-replace-objects", "-c", "safe.directory=" + str(Path.cwd())]
    tree = command(git + ["ls-tree", "-rz", "--full-tree", base], limit=LIMIT)
    files, total = [], 0
    for entry in tree.split(b"\0"):
        if not entry:
            continue
        metadata, raw_path = entry.split(b"\t", 1)
        mode, kind, oid = metadata.decode().split()
        path = safe_path(raw_path.decode("utf-8"))
        if mode not in ("100644", "100755") or kind != "blob":
            raise ValueError("unsupported baseline entry")
        # Never ingest checked-in credential/environment material.
        if any(x.lower() in (".env", ".aws", ".ssh", ".npmrc", ".netrc") for x in path.split("/")):
            raise ValueError("sensitive baseline entry")
        raw = command(git + ["cat-file", "blob", oid], limit=4 * 1024 * 1024)
        total += len(raw)
        if total > 16 * 1024 * 1024 or len(files) >= 5000:
            raise ValueError("source limit")
        files.append({"path": path, "data": base64.b64encode(raw).decode()})
    return files


def model_context(source, plan):
    from agent_contract import source_path
    context, context_bytes = [], 0
    ordered = sorted(source, key=lambda item: (item["path"] not in plan, item["path"]))
    for item in ordered:
        try:
            source_path(item["path"])
            content = base64.b64decode(item["data"]).decode("utf-8")
        except (ValueError, UnicodeDecodeError):
            continue
        if "\x00" not in content and len(content.encode()) <= 128 * 1024:
            entry = {"path": item["path"], "content": content}
            size = len(canonical(entry))
            if context_bytes + size <= 700 * 1024:
                context.append(entry)
                context_bytes += size
    return context


class Isolation:
    def __init__(self):
        self.tag = "agent-" + uuid.uuid4().hex[:16]
        self.networks, self.containers, self.rules = [], [], []

    def run(self, args, **kwargs):
        return command(args, **kwargs)

    def cleanup(self):
        # Cleanup must finish before the runner cancellation grace expires.
        deadline = time.monotonic() + 20
        def clean(args):
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                return
            try: self.run(args, timeout=min(3, remaining))
            except Exception: pass
        for name in reversed(self.containers):
            clean(["docker", "rm", "-f", name])
        for rule in reversed(self.rules):
            clean(["iptables", "-w", "1", "-D"] + rule)
        for name in reversed(self.networks):
            clean(["docker", "network", "rm", name])

    def generation_network(self, python_image):
        if self.run(["sysctl", "-n", "net.bridge.bridge-nf-call-iptables"]).strip() != b"1":
            raise ValueError("bridge firewall enforcement unavailable")
        internal, egress = self.tag + "-internal", self.tag + "-egress"
        for name, flags in ((internal, ["--internal"]), (egress, [])):
            self.run(["docker", "network", "create", *flags, "--ipv6=false", name])
            self.networks.append(name)
        info = json.loads(self.run(["docker", "network", "inspect", internal]))[0]
        if info.get("EnableIPv6") or not info.get("Internal"):
            raise ValueError("invalid network")
        subnet = info["IPAM"]["Config"][0]["Subnet"]
        if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+/\d+", subnet):
            raise ValueError("invalid subnet")
        gateway = self.tag + "-gateway"
        self.containers.append(gateway)
        secret = os.environ.get("DEEPSEEK_API_KEY", "")
        if not secret:
            raise ValueError("gateway credential unavailable")
        # Pass secret through stdin to docker create? Docker env-file is a secret file;
        # use inherited environment only for this fixed trusted gateway invocation.
        args = restricted(gateway, python_image, internal)
        args[2:2] = ["-d"]
        args[args.index("--entrypoint"):args.index("--entrypoint")] = ["--network-alias", "gateway", "--env", "DEEPSEEK_API_KEY"]
        # command() deliberately scrubs env; special gateway launch forwards only secret.
        result = subprocess.run(args + ["/runtime/agent_gateway.py"], env={"PATH": os.environ.get("PATH", ""), "DEEPSEEK_API_KEY": secret}, capture_output=True, timeout=30)
        if result.returncode:
            raise ValueError("gateway launch failed")
        self.run(["docker", "network", "connect", egress, gateway])
        gateway_info = json.loads(self.run(["docker", "inspect", gateway]))[0]
        gateway_ip = gateway_info["NetworkSettings"]["Networks"][internal]["IPAddress"]
        if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", gateway_ip):
            raise ValueError("invalid gateway address")
        gateway_egress_ip = gateway_info["NetworkSettings"]["Networks"][egress]["IPAddress"]
        if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", gateway_egress_ip):
            raise ValueError("invalid egress address")
        # Chain rules are inserted in reverse priority: allow exact gateway then deny all
        # other forwarded destinations; INPUT separately denies host services.
        for rule in (["INPUT", "-s", subnet, "-j", "DROP"],
                     ["DOCKER-USER", "-s", subnet, "-j", "DROP"],
                     ["DOCKER-USER", "-s", gateway_ip, "-d", subnet, "-p", "tcp", "--sport", "8080", "-m", "conntrack", "--ctstate", "ESTABLISHED", "-j", "ACCEPT"],
                     ["DOCKER-USER", "-s", subnet, "-d", gateway_ip, "-p", "tcp", "--dport", "8080", "-j", "ACCEPT"]):
            self.run(["iptables", "-w", "-I"] + rule)
            self.rules.append(rule)
            self.run(["iptables", "-w", "-C"] + rule)
        host_ip = info["IPAM"]["Config"][0]["Gateway"]
        # A listening host port makes the host-denial probe meaningful even on
        # a runner with no SSH/HTTP service; connection refusal alone proves little.
        listener = socket.socket()
        listener.bind(("0.0.0.0", 0))
        listener.listen(4)
        probe = ("import socket,sys,time\n"
                 "for attempt in range(10):\n"
                 " try:s=socket.create_connection(('gateway',8080),1);s.close();break\n"
                 " except OSError:\n"
                 "  if attempt==9:sys.exit(1)\n"
                 "  time.sleep(.3)\n" +
                 "targets=" + repr([(host_ip, listener.getsockname()[1]), (gateway_egress_ip, 8080)]) +
                 ";\nfor h,p in targets:\n s=socket.socket();s.settimeout(2)\n try:s.connect((h,p));sys.exit(1)\n except OSError:pass\n finally:s.close()\n")
        name = self.tag + "-probe"
        self.containers.append(name)
        try:
            self.run(restricted(name, python_image, internal) + ["-c", probe], timeout=20)
        finally:
            listener.close()
        return internal


def container_verify():
    data = json.loads(sys.stdin.buffer.read(LIMIT + 1))
    manifest = json.loads(Path("/opt/agent-cache-manifest.json").read_text())
    if manifest != {"schema": 1, "base_sha": data["base_sha"], "nuget": "/opt/offline-nuget", "npm": "/opt/npm-cache"}:
        raise ValueError("cache is not approved for base")
    for item in data["source"]:
        path = Path("/work") / safe_path(item["path"])
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(base64.b64decode(item["data"], validate=True))
    for item in data["files"]:
        path = Path("/work") / safe_path(item["path"])
        if item["content"] is None:
            path.unlink()
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(item["content"], encoding="utf-8")
    env = {"PATH": "/usr/local/bin:/usr/bin:/bin", "HOME": "/tmp", "CI": "true", "DOTNET_CLI_HOME": "/tmp", "NUGET_PACKAGES": "/work/.nuget", "NEXT_TELEMETRY_DISABLED": "1", "PLAYWRIGHT_BROWSERS_PATH": "/opt/playwright", "PLAYWRIGHT_BASE_URL": "http://127.0.0.1:3000"}
    Path("/work/site/.agent-playwright.config.cjs").write_text(
        "module.exports={testDir:'/work/site/e2e',testIgnore:['**/signature.spec.ts'],timeout:30000,"
        "use:{baseURL:'http://127.0.0.1:3000',browserName:'chromium'},"
        "webServer:{command:'python3 -m http.server 3000 --bind 127.0.0.1 --directory out',"
        "url:'http://127.0.0.1:3000',reuseExistingServer:false}};", encoding="utf-8")
    steps = [("api", ["dotnet", "restore", "CleaningSuite.Backend.sln", "--source", "/opt/offline-nuget", "--ignore-failed-sources"]),
             ("api", ["dotnet", "test", "CleaningSuite.Backend.sln", "-c", "Release", "--no-restore"]),
             ("site", ["npm", "ci", "--offline", "--cache", "/opt/npm-cache", "--no-audit", "--no-fund"]),
             ("site", ["npm", "run", "build"]),
             ("site", ["npm", "run", "test:e2e", "--", "--config=.agent-playwright.config.cjs"])]
    # Build output goes to bounded tmpfs, never trusted stdout or host logs.
    with open("/tmp/build.log", "wb") as log:
        for folder, args in steps:
            result = subprocess.run(args, cwd="/work/" + folder, env=env, stdout=log, stderr=log, timeout=600)
            if result.returncode:
                raise ValueError("verification failed")
    print('{"passed":true}')


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("generate", "verify"))
    parser.add_argument("--snapshot", required=True)
    parser.add_argument("--proposal")
    parser.add_argument("--output", required=True)
    args = parser.parse_args(argv)
    if sys.platform != "linux":
        raise ValueError("Linux isolation host required")
    from agent_contract import validate_snapshot, validate_proposal
    def read(path):
        raw = Path(path).read_bytes()
        if len(raw) > OUTPUT_LIMIT:
            raise ValueError("input limit")
        return json.loads(raw)
    snapshot = read(args.snapshot)
    validate_snapshot(snapshot)
    source = source_manifest(snapshot["base_sha"])
    isolation = Isolation()
    previous_term = signal.getsignal(signal.SIGTERM)
    def cancel(_signum, _frame):
        raise SystemExit("isolated execution cancelled")
    signal.signal(signal.SIGTERM, cancel)
    try:
        if args.action == "generate":
            python_image = image("AGENT_PYTHON_IMAGE")
            network = isolation.generation_network(python_image)
            context = model_context(source, snapshot["plan_body"])
            data = canonical({"plan": snapshot["plan_body"], "context": context})
            if len(data) > 1024 * 1024:
                raise ValueError("model context limit")
            name = isolation.tag + "-generate"
            isolation.containers.append(name)
            raw = isolation.run(restricted(name, python_image, network) + ["/runtime/agent_generate.py"], data=data, timeout=180)
            generated = json.loads(raw)
            if not isinstance(generated, dict) or set(generated) != {"files"}:
                raise ValueError("invalid model output")
            result = {"schema": 1, **{k: snapshot[k] for k in ("repository", "run_id", "run_attempt", "base_sha")}, "snapshot_sha256": digest(snapshot), "files": generated["files"]}
            validate_proposal(result, snapshot)
        else:
            if os.environ.get("AGENT_SOURCE_BASE_SHA") != snapshot["base_sha"]:
                raise ValueError("base-bound cache required")
            toolchain = image("AGENT_TOOLCHAIN_IMAGE")
            proposal = read(args.proposal)
            validate_proposal(proposal, snapshot)
            name = isolation.tag + "-verify"
            isolation.containers.append(name)
            data = canonical({"base_sha": snapshot["base_sha"], "source": source, "files": proposal["files"]})
            if len(data) > LIMIT:
                raise ValueError("source limit")
            raw = isolation.run(restricted(name, toolchain) + ["/runtime/agent_sandbox.py", "_container_verify"], data=data, timeout=1800)
            if json.loads(raw) != {"passed": True}:
                raise ValueError("invalid verification result")
            result = {"schema": 1, "status": "passed", **{k: snapshot[k] for k in ("repository", "run_id", "run_attempt", "base_sha")}, "snapshot_sha256": digest(snapshot), "proposal_sha256": digest(proposal)}
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(canonical(result))
    finally:
        # Further SIGTERM must not interrupt removal of credential-bearing gateway.
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        isolation.cleanup()
        signal.signal(signal.SIGTERM, previous_term)


if __name__ == "__main__":
    try:
        if sys.argv[1:] == ["_container_verify"]:
            container_verify()
        else:
            main()
    except Exception:
        sys.exit("isolated execution blocked or failed")

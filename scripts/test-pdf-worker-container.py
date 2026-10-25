"""Synthetic published Linux worker rehearsal. No customer data or API credentials."""
import base64, concurrent.futures, json, subprocess, time, urllib.error, urllib.request, uuid

def docker(*args, check=True):
    return subprocess.run(["docker", *args], check=check, capture_output=True, text=True).stdout.strip()

def pdf(count=2, box="0 0 595 842"):
    parts = [b"%PDF-1.4\n"]; offsets = [0]
    def obj(number, value):
        offsets.append(sum(map(len, parts)))
        parts.append(f"{number} 0 obj\n{value}\nendobj\n".encode())
    obj(1, "<< /Type /Catalog /Pages 2 0 R >>")
    kids = " ".join(f"{i} 0 R" for i in range(3, count + 3))
    obj(2, f"<< /Type /Pages /Count {count} /Kids [{kids}] >>")
    for i in range(count): obj(i+3, f"<< /Type /Page /Parent 2 0 R /MediaBox [{box}] /Resources << >> >>")
    xref = sum(map(len, parts))
    parts.append(f"xref\n0 {len(offsets)}\n0000000000 65535 f \n".encode())
    parts.extend(f"{offset:010} 00000 n \n".encode() for offset in offsets[1:])
    parts.append(f"trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())
    return b"".join(parts)

def request(port, operation="inspect", content=None):
    body = json.dumps({"operation": operation, "pdf": base64.b64encode(content or pdf()).decode(), "signers":
        [{"name":"Synthetic signer", "typedName":"Synthetic signer", "signedAtUtc":"2026-01-01T12:00:00Z", "image":None}] if operation=="sign" else []}).encode()
    try:
        with urllib.request.urlopen(urllib.request.Request(f"http://127.0.0.1:{port}/process", body,
                {"Content-Type":"application/json"}), timeout=15) as response:
            return response.status, response.read(100*1024*1024+1)
    except urllib.error.HTTPError as error: return error.code, error.read()

def ready(port):
    for _ in range(100):
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/healthz", timeout=1): return
        except (OSError, urllib.error.URLError): time.sleep(.2)
    raise AssertionError("worker did not recover")

def main():
    tag="pdf-security-rehearsal:"+uuid.uuid4().hex; name="pdf-security-"+uuid.uuid4().hex; network=name+"-private"
    docker("build", "-f", "api/docker/Dockerfile", "-t", tag, "api")
    docker("network", "create", "--internal", network)
    try:
        docker("run", "-d", "--name", name, "--network", network, "--restart", "on-failure",
            "--user", "1654:1654", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
            "--pids-limit", "64", "--memory", "1g", "--memory-swap", "1g", "--cpus", "1",
            "--tmpfs", "/tmp:rw,noexec,nosuid,size=384m,uid=1654,gid=1654,mode=700", "-e", "ASPNETCORE_URLS=http://+:8080",
            "-e", "PDF_WORKER_DEADLINE_SECONDS=5", "-e", "DOTNET_EnableDiagnostics=0", "-p", "127.0.0.1::8080", tag, "--pdf-worker-host")
        port=docker("port", name, "8080/tcp").rsplit(":",1)[1]; ready(port)
        info=json.loads(docker("inspect",name))[0]
        assert info["HostConfig"]["Memory"]==1024**3 and info["HostConfig"]["MemorySwap"]==1024**3
        assert info["HostConfig"]["ReadonlyRootfs"] and json.loads(docker("network","inspect",network))[0]["Internal"]
        assert request(port)[0]==204
        assert request(port,content=pdf(1,"0 0 10000000 842"))[0]==422
        assert request(port,content=pdf(501))[0]==422
        assert request(port,content=pdf(40))[0]==422
        status,result=request(port,"sign"); assert status==200 and result.startswith(b"%PDF-")
        assert request(port,"inspect-generated",result)[0]==204
        # Freeze the real child externally, with no production test hook.
        stopper=r"""for n in $(seq 1 500); do
for p in /proc/[0-9]*/cmdline; do
cmd=$(tr '\000' ' ' < "$p" 2>/dev/null) || continue
case "$cmd" in 'dotnet '*--pdf-worker-task*) pid=${p#/proc/}; pid=${pid%/cmdline}; kill -STOP "$pid"; echo "$pid"; exit 0;; esac
done
sleep .01
done
exit 1"""
        with concurrent.futures.ThreadPoolExecutor() as pool:
            stopped=pool.submit(docker,"exec",name,"bash","-c",stopper)
            timed=pool.submit(request,port,"sign",pdf(20))
            pid=stopped.result(timeout=10); assert pid.isdigit()
            assert request(port)[0]==429
            assert timed.result(timeout=10)[0]==408
        assert docker("exec",name,"bash","-c",f"test ! -e /proc/{pid} && echo reaped")=="reaped"
        assert docker("exec",name,"bash","-c","find /tmp -name 'pdf-job-*' -print")==""
        assert request(port)[0]==204
        # Real memory pressure in the same worker cgroup, never the API cgroup.
        before = docker("exec", name, "cat", "/sys/fs/cgroup/memory.events")
        restarts = json.loads(docker("inspect", name))[0]["RestartCount"]
        docker("exec",name,"bash","-c",r"x=$(head -c 1048576 /dev/zero | tr '\000' x); while true; do x=$x$x; done",check=False)
        ready(port); assert request(port)[0]==204
        after = docker("exec", name, "cat", "/sys/fs/cgroup/memory.events")
        events = lambda value: dict(line.split() for line in value.splitlines())
        assert int(events(after)["oom_kill"]) > int(events(before)["oom_kill"]) or json.loads(docker("inspect", name))[0]["RestartCount"] > restarts
        print("PASS: native published worker, budgets, timeout/reaping, admission, memory pressure and recovery")
    finally:
        docker("rm","-f",name,check=False); docker("network","rm",network,check=False); docker("image","rm",tag,check=False)

if __name__=="__main__": main()

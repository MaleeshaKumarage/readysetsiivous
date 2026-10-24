import json
import base64
import signal
import tempfile
from pathlib import Path
import sys
import unittest
from unittest.mock import patch, MagicMock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import agent_gateway as gateway
import agent_sandbox as sandbox


class GatewayTests(unittest.TestCase):
    def request(self):
        return {"model": gateway.MODEL, "max_tokens": 4096, "messages": [{"role": "user", "content": "literal $(echo unsafe)"}]}

    def test_fixed_request_and_sanitized_response(self):
        seen = []
        def transport(payload, secret):
            seen.append((payload, secret))
            return {"choices": [{"finish_reason": "stop", "message": {"content": '{"files":[]}'}}], "secret": "hidden"}
        g = gateway.Gateway("credential", transport)
        self.assertEqual(g.complete(self.request()), {"content": '{"files":[]}'})
        self.assertEqual(seen[0][1], "credential")

    def test_rejects_destination_tools_and_oversized_tokens(self):
        g = gateway.Gateway("credential", lambda *_: self.fail("transport called"))
        for extra in ({"url": "http://host"}, {"tools": []}, {"max_tokens": 4097}, {"model": "other"}, {"max_tokens": True}):
            request = self.request()
            request.update(extra)
            with self.assertRaises(ValueError): g.complete(request)

    def test_spacing_and_request_budget(self):
        now = [0]
        g = gateway.Gateway("credential", lambda *_: {"choices": [{"finish_reason": "stop", "message": {"content": "ok"}}]}, lambda: now[0])
        g.complete(self.request())
        with self.assertRaises(ValueError): g.complete(self.request())
        for i in range(1, 8):
            now[0] = i * 20
            g.complete(self.request())
        now[0] = 200
        with self.assertRaises(ValueError): g.complete(self.request())

    def test_transport_error_does_not_escape(self):
        def transport(*_): raise RuntimeError("credential and private prompt")
        with self.assertRaisesRegex(ValueError, "^provider unavailable$"):
            gateway.Gateway("credential", transport).complete(self.request())

    def test_redirects_rejected(self):
        with self.assertRaises(ValueError): gateway.NoRedirect().redirect_request(None, None, 302, None, None, "https://evil")

    def test_rejects_truncation_missing_finish_and_empty_content(self):
        for reason, content in (("length", "{}"), (None, "{}"), ("stop", ""), ("stop", "   ")):
            result = {"choices": [{"finish_reason": reason, "message": {"content": content}}]}
            with self.assertRaises(ValueError):
                gateway.Gateway("dummy", lambda *_: result).complete(self.request())

    def test_server_single_worker_and_connection_timeout(self):
        with patch.dict("os.environ", {"DEEPSEEK_API_KEY": "dummy"}), patch.object(gateway, "HTTPServer") as server:
            gateway.serve()
            handler = server.call_args.args[1]
            connection = MagicMock()
            connection.makefile.return_value = MagicMock()
            instance = object.__new__(handler)
            instance.request = connection
            instance.setup()
            connection.settimeout.assert_called_once_with(10)
            server.return_value.serve_forever.assert_called_once()


class SandboxTests(unittest.TestCase):
    def test_restricted_command_has_no_host_workspace_or_credentials(self):
        args = sandbox.restricted("test", "python@sha256:" + "a" * 64)
        text = " ".join(args)
        for flag in ("--read-only", "--cap-drop=ALL", "no-new-privileges", "--pids-limit=128", "--cpus=2", "--memory=4g", "--network none", "disable_ipv6=1"):
            self.assertIn(flag, text)
        self.assertEqual(args.count("--mount"), 1)
        for forbidden in ("docker.sock", "GITHUB_TOKEN", "DEEPSEEK_API_KEY", "/root", "--privileged", "--network host"):
            self.assertNotIn(forbidden, text)

    def test_images_must_be_pinned(self):
        for value in ("python:latest", "python@sha256:abc", ""):
            with patch.dict("os.environ", {"IMAGE": value}):
                with self.assertRaises(ValueError): sandbox.image("IMAGE")
        with patch.dict("os.environ", {"IMAGE": "registry/image@sha256:" + "a" * 64}):
            self.assertTrue(sandbox.image("IMAGE").startswith("registry/"))

    def test_paths_reject_escape(self):
        for value in ("../x", "/x", "a/../x", "a\\x", ".git/config", "C:/x", "a//x"):
            with self.assertRaises(ValueError): sandbox.safe_path(value)

    def test_network_fail_closed_if_bridge_filter_off(self):
        isolation = sandbox.Isolation()
        isolation.run = lambda *_args, **_kwargs: b"0\n"
        with self.assertRaisesRegex(ValueError, "bridge firewall"):
            isolation.generation_network("image")
        self.assertEqual(isolation.networks, [])

    def test_network_firewall_and_negative_probes(self):
        calls = []
        isolation = sandbox.Isolation()
        def run(args, **kwargs):
            calls.append((args, kwargs))
            if args[:2] == ["sysctl", "-n"]: return b"1"
            if args[:3] == ["docker", "network", "inspect"]:
                return json.dumps([{"EnableIPv6": False, "Internal": True, "IPAM": {"Config": [{"Subnet": "172.30.0.0/24", "Gateway": "172.30.0.1"}]}}]).encode()
            if args[:2] == ["docker", "inspect"]:
                return json.dumps([{"NetworkSettings": {"Networks": {isolation.tag + "-internal": {"IPAddress": "172.30.0.2"}, isolation.tag + "-egress": {"IPAddress": "172.31.0.2"}}}}]).encode()
            return b""
        isolation.run = run
        with patch.dict("os.environ", {"DEEPSEEK_API_KEY": "dummy"}), patch.object(sandbox.subprocess, "run") as launch:
            launch.return_value.returncode = 0
            isolation.generation_network("image")
        rules = [args for args, _ in calls if args[0] == "iptables"]
        self.assertTrue(any("INPUT" in x and "DROP" in x for x in rules))
        self.assertTrue(any("DOCKER-USER" in x and "8080" in x and "ACCEPT" in x for x in rules))
        probe = calls[-1][0][-1]
        self.assertIn("172.31.0.2", probe)
        self.assertIn("172.30.0.1", probe)
        self.assertIn("gateway", probe)
        isolation.cleanup()
        self.assertIn("range(10)", probe)
        self.assertTrue(any(args[:4] == ["iptables", "-w", "1", "-D"] for args, _ in calls))

    def test_model_context_excludes_control_and_config_files(self):
        paths = [".github/workflows/agent.yml", "scripts/agent_sandbox.py", "site/package.json", "site/src/main.tsx", "api/CleaningSuite.Api/Main.cs"]
        source = [{"path": path, "data": base64.b64encode(b"source text").decode()} for path in paths]
        self.assertEqual({x["path"] for x in sandbox.model_context(source, "main.tsx")}, set(paths[-2:]))

    def test_cleanup_is_bounded_even_when_commands_fail(self):
        isolation = sandbox.Isolation()
        isolation.containers = ["one", "two"]
        isolation.run = MagicMock(side_effect=ValueError("blocked"))
        isolation.cleanup()
        self.assertEqual(isolation.run.call_count, 2)
        for call in isolation.run.call_args_list:
            self.assertLessEqual(call.kwargs["timeout"], 3)

    def test_output_parent_created_and_sigterm_cleanup_runs(self):
        snapshot = {"repository": "owner/repo", "run_id": 1, "run_attempt": 1, "base_sha": "a" * 40, "plan_body": "plan"}
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "snapshot.json"
            source.write_text(json.dumps(snapshot))
            output = Path(tmp) / "nested" / "proposal.json"
            isolation = MagicMock()
            isolation.generation_network.return_value = "internal"
            isolation.tag = "test"
            isolation.run.return_value = b'{"files":[{"path":"site/src/main.tsx","content":"ok"}]}'
            previous = signal.getsignal(signal.SIGTERM)
            with patch.object(sandbox.sys, "platform", "linux"), patch.object(sandbox, "image", return_value="image"), patch.object(sandbox, "source_manifest", return_value=[]), patch.object(sandbox, "Isolation", return_value=isolation), patch("agent_contract.validate_snapshot"), patch("agent_contract.validate_proposal"):
                sandbox.main(["generate", "--snapshot", str(source), "--output", str(output)])
                self.assertTrue(output.is_file())
                isolation.cleanup.assert_called_once()
                isolation.cleanup.reset_mock()
                def terminate(*_): signal.getsignal(signal.SIGTERM)(signal.SIGTERM, None)
                isolation.generation_network.side_effect = terminate
                with self.assertRaises(SystemExit):
                    sandbox.main(["generate", "--snapshot", str(source), "--output", str(output)])
                isolation.cleanup.assert_called_once()
            self.assertEqual(signal.getsignal(signal.SIGTERM), previous)

    def test_wrong_cache_base_blocks_before_reconstructing_source(self):
        data = {"base_sha": "a" * 40, "source": [], "files": []}
        class Input:
            class Buffer:
                def read(self, *_): return json.dumps(data).encode()
            buffer = Buffer()
        with patch.object(sandbox.sys, "stdin", Input()), patch.object(Path, "read_text", return_value=json.dumps({"schema": 1, "base_sha": "b" * 40, "nuget": "/opt/offline-nuget", "npm": "/opt/npm-cache"})), patch.object(sandbox.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "cache"):
                sandbox.container_verify()
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()

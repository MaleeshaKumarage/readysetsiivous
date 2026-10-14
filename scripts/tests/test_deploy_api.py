"""Run the actual deployment script without a registry, Docker or network."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "deploy-api.sh"
SHA = "a" * 40
IMAGE = "ghcr.io/maleeshakumarage/readysetsiivous-api:sha-" + SHA
FAKE = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
p=pathlib.Path(os.environ['DEPLOY_DIR'])
name=pathlib.Path(sys.argv[0]).name
a=sys.argv[1:]
mode=os.environ['MODE']
with (p/'commands').open('a') as f: f.write(name+' '+' '.join(a)+'\n')
if name=='sleep': sys.exit(0)
if name=='curl':
    assert a==['--connect-timeout','2','--max-time','5','-fsS','http://127.0.0.1:8090/healthz']
    n=int((p/'attempts').read_text())+1 if (p/'attempts').exists() else 1
    (p/'attempts').write_text(str(n))
    if mode=='curl_error' or (mode=='retry' and n<3):
        print('{"status":"ok"}')
        sys.exit(22)
    print('broken' if mode=='bad_json' else json.dumps({'status':'not ok' if mode=='bad_status' else 'ok'}))
    sys.exit(0)
if a[:3]==['compose','ps','-q']:
    print('changed' if mode=='changed' and (p/'attempts').exists() else 'container')
elif a[:3]==['compose','config','--images']:
    print('wrong' if mode=='config' else os.environ['EXPECTED'])
elif a[0]=='login':
    assert sys.stdin.read()=='test-secret'
    if mode=='login': sys.exit(3)
elif a[:2]==['compose','pull']:
    assert a==['compose','pull','api']
    if mode=='pull': sys.exit(4)
elif a[:2]==['compose','up']:
    assert a==['compose','up','-d','--no-deps','api']
    if mode=='up': sys.exit(5)
    (p/'updated').touch()
elif a[:2]==['image','inspect']: print('sha256:expected')
elif a[0]=='inspect':
    fmt=a[-1]
    updated=(p/'updated').exists()
    if fmt=='{{.Config.Image}}': print(os.environ['EXPECTED'] if updated and mode!='tag' else 'old-image')
    elif fmt=='{{.Image}}': print('sha256:wrong' if mode=='image_id' else 'sha256:expected')
    elif fmt=='{{.State.Running}}': print('false' if mode=='stopped' or (mode=='stopped_after_health' and (p/'attempts').exists()) else 'true')
    else: sys.exit(90)
else: sys.exit(91)
'''


class DeploymentTests(unittest.TestCase):
    def run_deploy(self, mode="success", sha=SHA):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            initial = "IMAGE_TAG=sha-old\nDATABASE_PASSWORD=private-value\n"
            (root / ".env").write_text(initial)
            bin_dir = root / "bin"
            bin_dir.mkdir()
            for name in ["docker", "curl", "sleep"]:
                path = bin_dir / name
                path.write_text(FAKE)
                path.chmod(0o755)
            env = dict(os.environ, DEPLOY_DIR=tmp, RELEASE_SHA=sha,
                       REGISTRY_USER="test-user", REGISTRY_TOKEN="test-secret",
                       MODE=mode, EXPECTED=IMAGE,
                       PATH=str(bin_dir) + os.pathsep + os.environ["PATH"])
            result = subprocess.run(["sh", str(SCRIPT)], env=env,
                                    text=True, capture_output=True, timeout=15)
            commands = (root / "commands").read_text() if (root / "commands").exists() else ""
            attempts = int((root / "attempts").read_text()) if (root / "attempts").exists() else 0
            current = (root / ".env").read_text()
            self.assertFalse(list(root.glob(".deploy-env.*")))
            self.assertNotIn("test-secret", result.stdout + result.stderr + commands)
            self.assertNotIn("private-value", result.stdout + result.stderr + commands)
            self.assertNotIn("prune", commands)
            self.assertNotIn("remove", commands)
            self.assertNotIn("postgres", commands)
            self.assertNotIn("keycloak", commands)
            if result.returncode:
                self.assertEqual(initial, current)
            else:
                self.assertEqual(initial.replace("sha-old", "sha-" + SHA), current)
            return result, commands, attempts

    def test_success(self):
        result, commands, attempts = self.run_deploy()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Deployment verified: " + IMAGE, result.stdout)
        self.assertEqual(1, attempts)
        self.assertEqual(2, commands.count("{{.State.Running}}"))

    def test_invalid_sha(self):
        for sha in ["", "abc", "x" * 40, "a" * 40 + "\n"]:
            with self.subTest(sha=sha):
                result, commands, _ = self.run_deploy(sha=sha)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", commands)

    def test_failures_restore_environment(self):
        for mode in ["config", "login", "pull", "up", "tag", "image_id", "stopped",
                     "changed", "stopped_after_health", "bad_json", "bad_status", "curl_error"]:
            with self.subTest(mode=mode):
                result, commands, attempts = self.run_deploy(mode)
                self.assertNotEqual(0, result.returncode)
                self.assertNotIn("Deployment verified:", result.stdout)
                if mode in ["config", "login", "pull"]:
                    self.assertNotIn("compose up", commands)
                    self.assertEqual(0, attempts)
                if mode in ["bad_json", "bad_status", "curl_error"]:
                    self.assertEqual(30, attempts)
                if mode == "pull": self.assertEqual(4, result.returncode)
                if mode == "up": self.assertEqual(5, result.returncode)

    def test_health_retry(self):
        result, _, attempts = self.run_deploy("retry")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(3, attempts)


if __name__ == "__main__":
    unittest.main()

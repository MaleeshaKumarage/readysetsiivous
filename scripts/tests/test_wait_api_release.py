import importlib.util
import io
import json
import os
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("wait_api_release", Path(__file__).parents[1] / "wait-api-release.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
SHA = "a" * 40


class ReleaseGateTests(unittest.TestCase):
    def test_exact_release_is_accepted_and_missing_version_is_not(self):
        self.assertTrue(gate.contains_release(SHA, SHA))
        self.assertFalse(gate.contains_release(SHA, None))
        self.assertFalse(gate.contains_release(SHA, "development"))

    def test_newer_release_must_contain_required_commit(self):
        with patch.object(gate.subprocess, "run", side_effect=[SimpleNamespace(returncode=0), SimpleNamespace(returncode=0)]):
            self.assertTrue(gate.contains_release(SHA, "c" * 40))
        with patch.object(gate.subprocess, "run", side_effect=[SimpleNamespace(returncode=0), SimpleNamespace(returncode=1)]):
            self.assertFalse(gate.contains_release(SHA, "c" * 40))

    def test_frontend_only_change_does_not_block_on_api(self):
        with patch.dict(os.environ, {"GITHUB_SHA": SHA, "BEFORE_SHA": "b" * 40}), patch.object(gate.subprocess, "check_output", return_value="site/src/page.tsx\n"), patch.object(gate.urllib.request, "urlopen") as request:
            gate.main()
            request.assert_not_called()

    def test_api_change_waits_for_healthy_expected_release(self):
        payload = io.BytesIO(json.dumps({"status": "ok", "releaseSha": SHA}).encode())
        with patch.dict(os.environ, {"GITHUB_SHA": SHA, "BEFORE_SHA": "b" * 40}), patch.object(gate.subprocess, "check_output", return_value="api/Program.cs\n"), patch.object(gate.urllib.request, "urlopen", return_value=payload):
            gate.main()

    def test_timeout_fails_without_publishing(self):
        with patch.dict(os.environ, {"GITHUB_SHA": SHA, "BEFORE_SHA": "b" * 40}), patch.object(gate.subprocess, "check_output", return_value="api/Program.cs\n"), patch.object(gate.time, "monotonic", side_effect=[0, 3000]):
            with self.assertRaisesRegex(RuntimeError, "existing Pages deployment was retained"):
                gate.main()


if __name__ == "__main__":
    unittest.main()

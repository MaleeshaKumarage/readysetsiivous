"""Keep Pages from publishing a UI before its API release is healthy."""
import json
import os
import re
import subprocess
import time
import urllib.error
import urllib.request


def contains_release(expected, deployed):
    if not re.fullmatch(r"[0-9a-f]{40}", deployed or ""):
        return False
    if deployed == expected:
        return True
    if subprocess.run(["git", "cat-file", "-e", deployed], capture_output=True).returncode:
        subprocess.run(["git", "fetch", "--no-tags", "origin", deployed], check=True, capture_output=True)
    return subprocess.run(["git", "merge-base", "--is-ancestor", expected, deployed], capture_output=True).returncode == 0


def main():
    expected = os.environ["GITHUB_SHA"]
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        raise RuntimeError("Invalid release SHA")
    before = os.environ.get("BEFORE_SHA", "")
    base = before if re.fullmatch(r"[0-9a-f]{40}", before) and set(before) != {"0"} else expected + "^"
    changed = subprocess.check_output(["git", "diff", "--name-only", base, expected], text=True).splitlines()
    api_paths = ("api/", "global.json", ".github/workflows/docker-build-push.yml", ".github/workflows/ci-build-test.yml")
    if not any(path.startswith(api_paths) for path in changed):
        print("Frontend-only release: no API dependency to wait for.")
        return
    print("Waiting for the API release before publishing Pages.", flush=True)
    deadline = time.monotonic() + 45 * 60
    while time.monotonic() < deadline:
        try:
            request = urllib.request.Request("https://api.readysetsiivous.fi/healthz", headers={"Cache-Control": "no-cache"})
            with urllib.request.urlopen(request, timeout=10) as response:
                payload = json.load(response)
            if payload.get("status") == "ok" and contains_release(expected, payload.get("releaseSha")):
                print("The required API release is healthy. Pages may publish.")
                return
        except (OSError, ValueError, subprocess.CalledProcessError):
            pass
        time.sleep(15)
    raise RuntimeError("Required API release did not become healthy; existing Pages deployment was retained.")


if __name__ == "__main__":
    main()

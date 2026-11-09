#!/usr/bin/env python3
"""Decide whether a PR would trigger a production or Pages deploy when merged to main.

The deploy paths come from the workflows on origin/main, not from a hand-written list:
every workflow that pushes to main and whose name or file looks like a deploy
(deploy, docker, pages) contributes its `paths` filter. A PR file matching any
filter means the merge deploys, so the agent must not merge it automatically.

Usage: agent_deploy_guard.py <pr-number> [--repo owner/name] [--base origin/main]
Exit code: 0 = no deploy would trigger, 2 = deploy would trigger, 1 = error.
"""
import argparse, json, re, subprocess, sys
import yaml

DEPLOY_NAME = re.compile(r"deploy|docker|pages", re.IGNORECASE)


def glob_to_regex(pattern: str) -> re.Pattern:
    """GitHub path filter syntax: `**/` matches zero or more directories, `**` any text, `*` one path segment."""
    out, i = "", 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            out += "(?:.*/)?"
            i += 3
        elif pattern.startswith("**", i):
            out += ".*"
            i += 2
        elif pattern[i] == "*":
            out += "[^/]*"
            i += 1
        elif pattern[i] == "?":
            out += "[^/]"
            i += 1
        else:
            out += re.escape(pattern[i])
            i += 1
    return re.compile("^" + out + "$")


def run(cmd: list[str]) -> str:
    return subprocess.run(cmd, check=True, capture_output=True, text=True).stdout


def deploy_filters(base: str) -> dict[str, list[str]]:
    """Map each deploying workflow on `base` to its push-to-main path filters."""
    filters: dict[str, list[str]] = {}
    for path in run(["git", "ls-tree", "--name-only", base, ".github/workflows/"]).split():
        doc = yaml.safe_load(run(["git", "show", f"{base}:{path}"])) or {}
        name = doc.get("name") or path
        if not (DEPLOY_NAME.search(name) or DEPLOY_NAME.search(path)):
            continue
        on = doc.get("on", doc.get(True, {}))  # YAML 1.1 turns bare `on` into True
        push = on.get("push") if isinstance(on, dict) else None
        if not push or "main" not in (push.get("branches") or []):
            continue
        # A push trigger with no paths filter deploys on every push to main.
        filters[path] = push.get("paths") or ["**"]
    return filters


def pr_files(repo: str, number: int) -> list[str]:
    raw = run(["gh", "api", "--paginate", f"repos/{repo}/pulls/{number}/files", "--jq", ".[].filename"])
    return [line for line in raw.splitlines() if line]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("number", type=int)
    ap.add_argument("--repo", default=None)
    ap.add_argument("--base", default="origin/main")
    args = ap.parse_args()
    try:
        repo = args.repo or re.search(r"github\.com[:/](.+?)(?:\.git)?$", run(["git", "remote", "get-url", "origin"]).strip()).group(1)
        run(["git", "fetch", "-q", "origin", "main"])
        filters = deploy_filters(args.base)
        files = pr_files(repo, args.number)
    except (subprocess.CalledProcessError, OSError, AttributeError) as error:
        print(json.dumps({"error": str(error)}))
        return 1
    hits = {}
    for workflow, patterns in filters.items():
        matched = [f for f in files if any(glob_to_regex(p).match(f) for p in patterns)]
        if matched:
            hits[workflow] = matched
    print(json.dumps({"deploys": bool(hits), "workflows": hits}, indent=2))
    return 2 if hits else 0


if __name__ == "__main__":
    sys.exit(main())

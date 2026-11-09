#!/usr/bin/env python3
"""Decide whether a PR would trigger a production or Pages deploy when merged to main.

The deploy rules come from the workflows on origin/main, not from a hand-written list.
A workflow counts as deploying when its name or file looks like a deploy (deploy, docker,
pages) and its push trigger applies to main. A PR whose changed files would trigger that
push means the merge deploys, so the agent must not merge it automatically.

Trigger semantics follow GitHub's rules, and wherever they are ambiguous the guard
errs toward reporting a deploy, because a false "no deploy" is the dangerous direction.

Usage: agent_deploy_guard.py <pr-number> [--repo owner/name] [--base origin/main]
Exit code: 0 = no deploy would trigger, 2 = deploy would trigger, 1 = error.
"""
import argparse, json, re, subprocess, sys
import yaml

DEPLOY_NAME = re.compile(r"deploy|docker|pages", re.IGNORECASE)
BRANCH = "main"


def glob_to_regex(pattern: str) -> re.Pattern:
    """GitHub filter syntax: `**/` matches zero or more directories, `**` any text, `*` one segment."""
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


def _matches_any(patterns, value: str) -> bool:
    return any(glob_to_regex(p).match(value) for p in patterns)


def branch_applies(push: dict, branch: str = BRANCH) -> bool:
    """`branches` and `branches-ignore` decide whether the push trigger covers `branch`."""
    if push.get("branches") is not None:
        positive = [b for b in push["branches"] if not b.startswith("!")]
        negative = [b[1:] for b in push["branches"] if b.startswith("!")]
        return _matches_any(positive, branch) and not _matches_any(negative, branch)
    if push.get("branches-ignore") is not None:
        return not _matches_any(push["branches-ignore"], branch)
    return True  # no branch filter: every branch, including main


def files_triggering(push: dict, files: list[str]) -> list[str]:
    """Return the changed files that make this push trigger fire."""
    if push.get("paths") is not None:
        paths = push["paths"]
        positive = [p for p in paths if not p.startswith("!")]
        negative = [p[1:] for p in paths if p.startswith("!")]
        def hit(f):
            # Negation-only lists are treated as "anything not excluded" (conservative).
            included = _matches_any(positive, f) if positive else True
            return included and not _matches_any(negative, f)
        return [f for f in files if hit(f)]
    if push.get("paths-ignore") is not None:
        return [f for f in files if not _matches_any(push["paths-ignore"], f)]
    return list(files)  # no path filter: any change triggers


def deploy_workflows(base: str) -> dict[str, dict]:
    """Push-to-main trigger config of every deploying workflow on `base`."""
    found: dict[str, dict] = {}
    for path in run(["git", "ls-tree", "--name-only", base, ".github/workflows/"]).split():
        doc = yaml.safe_load(run(["git", "show", f"{base}:{path}"])) or {}
        name = doc.get("name") or path
        if not isinstance(name, str) or not (DEPLOY_NAME.search(name) or DEPLOY_NAME.search(path)):
            continue
        on = doc.get("on", doc.get(True))  # YAML 1.1 reads a bare `on` key as True
        if isinstance(on, str):
            on = [on]
        if isinstance(on, list):
            push = {} if "push" in on else None
        elif isinstance(on, dict):
            push = on.get("push")
        else:
            push = None
        if not isinstance(push, dict):
            continue
        if branch_applies(push):
            found[path] = push
    return found


def run(cmd: list[str]) -> str:
    return subprocess.run(cmd, check=True, capture_output=True, text=True).stdout


def pr_files(repo: str, number: int) -> list[str]:
    raw = run(["gh", "api", "--paginate", f"repos/{repo}/pulls/{number}/files", "--jq", ".[].filename"])
    return [line for line in raw.splitlines() if line]


def evaluate(base: str, repo: str, number: int) -> dict[str, list[str]]:
    files = pr_files(repo, number)
    hits = {}
    for workflow, push in deploy_workflows(base).items():
        matched = files_triggering(push, files)
        if matched:
            hits[workflow] = matched
    return hits


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("number", type=int)
    ap.add_argument("--repo", default=None)
    ap.add_argument("--base", default="origin/main")
    args = ap.parse_args()
    try:
        repo = args.repo or re.search(r"github\.com[:/](.+?)(?:\.git)?$", run(["git", "remote", "get-url", "origin"]).strip()).group(1)
        run(["git", "fetch", "-q", "origin", BRANCH])
        hits = evaluate(args.base, repo, args.number)
    except (subprocess.CalledProcessError, OSError, AttributeError, yaml.YAMLError, TypeError, ValueError) as error:
        print(json.dumps({"error": str(error)}))
        return 1
    print(json.dumps({"deploys": bool(hits), "workflows": hits}, indent=2))
    return 2 if hits else 0


if __name__ == "__main__":
    sys.exit(main())

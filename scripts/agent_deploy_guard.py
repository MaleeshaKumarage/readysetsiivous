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


def _included(patterns, value: str) -> bool:
    """GitHub applies patterns in order: a later match overrides an earlier one, and `!` excludes.

    Ambiguous cases count as included (conservative): an empty list, or a list with only
    negations that none of them excludes.
    """
    if not patterns:
        return True
    state = None
    for pattern in patterns:
        if pattern.startswith("!"):
            if glob_to_regex(pattern[1:]).match(value):
                state = False
        elif glob_to_regex(pattern).match(value):
            state = True
    if state is None:
        return not any(not p.startswith("!") for p in patterns)
    return state


def branch_applies(push: dict, branch: str = BRANCH) -> bool:
    """`branches` and `branches-ignore` decide whether the push trigger covers `branch`."""
    if push.get("branches") is not None:
        return _included(push["branches"], branch)
    if push.get("branches-ignore") is not None:
        return not any(glob_to_regex(p).match(branch) for p in push["branches-ignore"])
    return True  # no branch filter: every branch, including main


def files_triggering(push: dict, files: list[str]) -> list[str]:
    """Return the changed files that make this push trigger fire."""
    if push.get("paths") is not None:
        return [f for f in files if _included(push["paths"], f)]
    if push.get("paths-ignore") is not None:
        return [f for f in files if not any(glob_to_regex(p).match(f) for p in push["paths-ignore"])]
    return list(files)  # no path filter: any change triggers


def push_trigger(on):
    """The push trigger config from a workflow's `on:` value, or None if there is no push trigger.

    `on: push` and `on: {push: null}` both mean an unfiltered push trigger, so they return {}.
    """
    if isinstance(on, str):
        return {} if on == "push" else None
    if isinstance(on, list):
        return {} if "push" in on else None
    if isinstance(on, dict) and "push" in on:
        return on["push"] if isinstance(on["push"], dict) else {}
    return None


def deploy_workflows(base: str) -> dict[str, dict]:
    """Push-to-main trigger config of every deploying workflow in the commit `base`."""
    found: dict[str, dict] = {}
    for path in run(["git", "ls-tree", "--name-only", base, ".github/workflows/"]).split():
        doc = yaml.safe_load(run(["git", "show", f"{base}:{path}"])) or {}
        name = doc.get("name") or path
        if not isinstance(name, str) or not (DEPLOY_NAME.search(name) or DEPLOY_NAME.search(path)):
            continue
        push = push_trigger(doc.get("on", doc.get(True)))
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


def evaluate(base: str, head: str, repo: str, number: int) -> dict[str, list[str]]:
    """A deploy workflow may come from the base (today) or from the PR head (after the merge).

    Both are checked, so a PR that adds or edits a deploy trigger is inspected too.
    """
    files = pr_files(repo, number)
    configs = {}
    for source in (base, head):
        configs.update({f"{source}:{path}": push for path, push in deploy_workflows(source).items()})
    hits = {}
    for workflow, push in configs.items():
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
        run(["git", "fetch", "-q", "origin", f"pull/{args.number}/head"])
        hits = evaluate(args.base, "FETCH_HEAD", repo, args.number)
    except (subprocess.CalledProcessError, OSError, AttributeError, yaml.YAMLError, TypeError, ValueError) as error:
        print(json.dumps({"error": str(error)}))
        return 1
    print(json.dumps({"deploys": bool(hits), "workflows": hits}, indent=2))
    return 2 if hits else 0


if __name__ == "__main__":
    sys.exit(main())

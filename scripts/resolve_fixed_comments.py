#!/usr/bin/env python3
"""Resolve PR review threads whose file changed in the latest commit.

Deterministic (no LLM): on a new commit, list unresolved review threads, get
the files changed by the head commit, and resolve each thread whose file is
among them — the developer touched that file to address the comment. Posts a
short "Fixed in <sha>" reply and resolves via the GraphQL resolveReviewThread
mutation (same as pressing the resolve button). Unfixed threads stay open.

Usage:
  GH_TOKEN=... python3 resolve_fixed_comments.py <pr-number> <head-sha>
"""
import os, sys, json, urllib.request, urllib.error

pr = int(sys.argv[1])
sha = sys.argv[2]

token = os.environ.get("GH_TOKEN", os.environ.get("GITHUB_TOKEN", ""))
repo = os.environ.get("GITHUB_REPOSITORY", "MaleeshaKumarage/readysetsiivous")
owner, _, name = repo.partition("/")
api = f"https://api.github.com/repos/{repo}"
HDR = {
    "Authorization": f"Bearer {token}",
    "Content-Type": "application/json",
    "Accept": "application/vnd.github+json",
}


def http_json(url):
    req = urllib.request.Request(url, headers=HDR)
    return json.load(urllib.request.urlopen(req, timeout=30))


def gql(query, variables):
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query, "variables": variables}).encode(),
        headers=HDR,
        method="POST",
    )
    return json.load(urllib.request.urlopen(req, timeout=30))


# Files changed by the head commit. If it is a merge commit (empty file list),
# fall back to comparing against its first parent.
commit = http_json(f"{api}/commits/{sha}")
changed = set(f.get("filename", "") for f in commit.get("files", []) if f.get("filename"))
if not changed and commit.get("parents"):
    parent = commit["parents"][0]["sha"]
    cmp = http_json(f"{api}/compare/{parent}...{sha}")
    changed = set(f.get("filename", "") for f in cmp.get("files", []) if f.get("filename"))

q = """
query($owner:String!,$name:String!,$pr:Int!) {
  repository(owner:$owner,name:$name){
    pullRequest(number:$pr){
      reviewThreads(first:100){ nodes{
        id isResolved
        comments(first:5){ nodes{ databaseId path } }
      }}
    }
  }
}
"""
data = gql(q, {"owner": owner, "name": name, "pr": pr})
threads = data["data"]["repository"]["pullRequest"]["reviewThreads"]["nodes"]

unresolved = [t for t in threads if not t["isResolved"] and t["comments"]["nodes"]]

if not unresolved:
    print("no unresolved threads", file=sys.stderr)
    sys.exit(0)

resolved = 0
for t in unresolved:
    c = t["comments"]["nodes"][0]
    path = c.get("path", "")
    if path not in changed:
        continue

    # reply "Fixed in <sha>" so the thread records how it was addressed
    cid = c["databaseId"]
    try:
        reply = json.dumps({"body": f"Fixed in {sha[:7]}"}).encode()
        req = urllib.request.Request(
            f"{api}/pulls/{pr}/comments/{cid}/replies",
            data=reply, headers=HDR, method="POST",
        )
        urllib.request.urlopen(req, timeout=30)
    except urllib.error.HTTPError as e:
        print(f"reply failed {e.code} for {path}", file=sys.stderr)

    # mark the thread resolved on GitHub
    m = "mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ isResolved } } }"
    try:
        res = gql(m, {"id": t["id"]})
        if res["data"]["resolveReviewThread"]["thread"]["isResolved"]:
            resolved += 1
            print(f"resolved {path}", file=sys.stderr)
        else:
            print(f"thread {t['id']} not marked resolved", file=sys.stderr)
    except Exception as e:
        print(f"resolve failed for {path}: {e}", file=sys.stderr)

print(f"resolved {resolved} of {len(unresolved)} threads", file=sys.stderr)

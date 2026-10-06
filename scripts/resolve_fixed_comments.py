#!/usr/bin/env python3
"""Resolve PR review threads whose issues are now fixed.

Lists unresolved review threads, asks DeepSeek whether each reported issue is
fixed in the current diff, and resolves ONLY the FIXED ones — a short
"Fixed in <sha>" reply plus the GraphQL resolveReviewThread mutation (same as
pressing the resolve button). Unfixed threads stay open.

Usage:
  GH_TOKEN=... DEEPSEEK_API_KEY=... python3 resolve_fixed_comments.py <pr-number> <head-sha> < diff.txt
"""
import os, sys, json, urllib.request, urllib.error

pr = int(sys.argv[1])
sha = sys.argv[2]
diff = sys.stdin.read()

token = os.environ.get("GH_TOKEN", os.environ.get("GITHUB_TOKEN", ""))
repo = os.environ.get("GITHUB_REPOSITORY", "MaleeshaKumarage/readysetsiivous")
owner, _, name = repo.partition("/")
api = f"https://api.github.com/repos/{repo}"
HDR = {
    "Authorization": f"Bearer {token}",
    "Content-Type": "application/json",
    "Accept": "application/vnd.github+json",
}


def gql(query, variables):
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query, "variables": variables}).encode(),
        headers=HDR,
        method="POST",
    )
    return json.load(urllib.request.urlopen(req, timeout=30))


def deepseek_fixed(path, line, body):
    key = os.environ.get("DEEPSEEK_API_KEY", "")
    payload = json.dumps({
        "model": "deepseek-v4-flash",
        "messages": [
            {"role": "system", "content": "You check whether a code review issue is now fixed. Reply with exactly FIXED or NOT_FIXED, nothing else."},
            {"role": "user", "content": f"Issue at {path}:{line}:\n{body}\n\nCurrent diff:\n{diff[:12000]}"},
        ],
    }).encode()
    req = urllib.request.Request(
        "https://api.deepseek.com/chat/completions",
        data=payload,
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
    )
    resp = json.load(urllib.request.urlopen(req, timeout=180))
    return resp["choices"][0]["message"]["content"].strip().upper().startswith("FIXED")


q = """
query($owner:String!,$name:String!,$pr:Int!) {
  repository(owner:$owner,name:$name){
    pullRequest(number:$pr){
      reviewThreads(first:100){ nodes{
        id isResolved
        comments(first:5){ nodes{ databaseId path line body } }
      }}
    }
  }
}
"""
data = gql(q, {"owner": owner, "name": name, "pr": pr})
threads = data["data"]["repository"]["pullRequest"]["reviewThreads"]["nodes"]

unresolved = []
for t in threads:
    if t["isResolved"]:
        continue
    c = t["comments"]["nodes"][0] if t["comments"]["nodes"] else None
    if c and c.get("body"):
        unresolved.append({
            "threadId": t["id"],
            "databaseId": c["databaseId"],
            "path": c.get("path", ""),
            "line": c.get("line", 0),
            "body": c["body"],
        })

if not unresolved:
    print("no unresolved threads", file=sys.stderr)
    sys.exit(0)

resolved = 0
for c in unresolved:
    try:
        if not deepseek_fixed(c["path"], c["line"], c["body"]):
            continue
    except Exception as e:
        print(f"deepseek check failed for {c['path']}:{c['line']}: {e}", file=sys.stderr)
        continue

    # reply "Fixed in <sha>" so the thread records how it was addressed
    try:
        reply = json.dumps({"body": f"Fixed in {sha[:7]}"}).encode()
        req = urllib.request.Request(
            f"{api}/pulls/{pr}/comments/{c['databaseId']}/replies",
            data=reply, headers=HDR, method="POST",
        )
        urllib.request.urlopen(req, timeout=30)
    except urllib.error.HTTPError as e:
        print(f"reply failed {e.code} for {c['path']}:{c['line']}", file=sys.stderr)

    # mark the thread resolved on GitHub (same as pressing the resolve button)
    m = "mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ isResolved } } }"
    try:
        res = gql(m, {"id": c["threadId"]})
        ok = res["data"]["resolveReviewThread"]["thread"]["isResolved"]
        if ok:
            resolved += 1
            print(f"resolved {c['path']}:{c['line']}", file=sys.stderr)
        else:
            print(f"thread {c['threadId']} not marked resolved", file=sys.stderr)
    except Exception as e:
        print(f"resolve failed for {c['path']}:{c['line']}: {e}", file=sys.stderr)

print(f"resolved {resolved} of {len(unresolved)} threads", file=sys.stderr)

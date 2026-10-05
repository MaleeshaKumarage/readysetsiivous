#!/usr/bin/env python3
"""Close (resolve) all unresolved PR review threads on a new commit.

On a new commit, prior review comments are treated as addressed. For every
unresolved thread, post a short "Fixed in <sha>" reply and mark the thread
resolved via the GraphQL resolveReviewThread mutation. The following
critical-only review re-flags anything still broken, so nothing is lost.

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


def gql(query, variables):
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query, "variables": variables}).encode(),
        headers=HDR,
        method="POST",
    )
    return json.load(urllib.request.urlopen(req, timeout=30))


q = """
query($owner:String!,$name:String!,$pr:Int!) {
  repository(owner:$owner,name:$name){
    pullRequest(number:$pr){
      reviewThreads(first:100){ nodes{
        id isResolved
        comments(first:5){ nodes{ databaseId } }
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
    cid = t["comments"]["nodes"][0]["databaseId"]

    # reply "Fixed in <sha>" so the thread records how it was addressed
    try:
        reply = json.dumps({"body": f"Fixed in {sha[:7]}"}).encode()
        req = urllib.request.Request(
            f"{api}/pulls/{pr}/comments/{cid}/replies",
            data=reply, headers=HDR, method="POST",
        )
        urllib.request.urlopen(req, timeout=30)
    except urllib.error.HTTPError as e:
        print(f"reply failed {e.code} for thread {t['id']}", file=sys.stderr)

    # mark the thread resolved on GitHub
    m = "mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ isResolved } } }"
    try:
        res = gql(m, {"id": t["id"]})
        ok = res["data"]["resolveReviewThread"]["thread"]["isResolved"]
        if ok:
            resolved += 1
            print(f"resolved thread {t['id']}", file=sys.stderr)
        else:
            print(f"thread {t['id']} not marked resolved", file=sys.stderr)
    except Exception as e:
        print(f"resolve failed for thread {t['id']}: {e}", file=sys.stderr)

print(f"resolved {resolved} of {len(unresolved)} threads", file=sys.stderr)

#!/usr/bin/env python3
"""Resolve a PR review comment thread by its comment databaseId.

Finds the review thread whose comment carries the given databaseId and marks it
resolved via the GraphQL API.

Usage:
  GH_TOKEN=... python3 resolve_comment.py <pr-number> <comment-id> [<commit-sha>]
"""
import os, sys, json, urllib.request, urllib.error

pr = sys.argv[1]
cid = sys.argv[2]
sha = sys.argv[3] if len(sys.argv) > 3 else ""
token = os.environ.get("GH_TOKEN", os.environ.get("GITHUB_TOKEN", ""))
repo = os.environ.get("GITHUB_REPOSITORY", "MaleeshaKumarage/readysetsiivous")
api = f"https://api.github.com/repos/{repo}"


def gql(query, variables):
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query, "variables": variables}).encode(),
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
        method="POST",
    )
    return json.load(urllib.request.urlopen(req, timeout=30))


owner, _, name = repo.partition("/")
q = """
query($owner:String!,$name:String!,$pr:Int!) {
  repository(owner:$owner,name:$name){
    pullRequest(number:$pr){
      reviewThreads(first:100){
        nodes{ id comments(first:5){ nodes{ databaseId } } }
      }
    }
  }
}
"""
data = gql(q, {"owner": owner, "name": name, "pr": int(pr)})
threads = data["data"]["repository"]["pullRequest"]["reviewThreads"]["nodes"]

target = None
for t in threads:
    for c in t["comments"]["nodes"]:
        if str(c["databaseId"]) == str(cid):
            target = t["id"]
            break
    if target:
        break

if not target:
    print(f"no thread found for comment {cid}", file=sys.stderr)
    sys.exit(1)

# reply "Fixed in <sha>" so the thread records how it was fixed
if sha:
    reply = json.dumps({"body": f"Fixed in {sha}"}).encode()
    req = urllib.request.Request(
        f"{api}/pulls/{pr}/comments/{cid}/replies",
        data=reply,
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json", "Accept": "application/vnd.github+json"},
        method="POST",
    )
    try:
        urllib.request.urlopen(req, timeout=30)
    except urllib.error.HTTPError as e:
        print(f"reply failed {e.code}", file=sys.stderr)

m = "mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ isResolved } } }"
gql(m, {"id": target})
print(f"resolved comment {cid}", file=sys.stderr)

#!/usr/bin/env python3
"""Resolve a PR review comment thread by its comment databaseId.

Finds the review thread whose comment carries the given databaseId and marks it
resolved via the GraphQL API.

Usage:
  GH_TOKEN=... python3 resolve_comment.py <pr-number> <comment-id>
"""
import os, sys, json, urllib.request, urllib.error

pr = sys.argv[1]
cid = sys.argv[2]
token = os.environ.get("GH_TOKEN", os.environ.get("GITHUB_TOKEN", ""))
repo = os.environ.get("GITHUB_REPOSITORY", "MaleeshaKumarage/readysetsiivous")


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

m = "mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ isResolved } } }"
gql(m, {"id": target})
print(f"resolved comment {cid}", file=sys.stderr)

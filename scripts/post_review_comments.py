#!/usr/bin/env python3
"""Parse a review (FILE/LINE/FIX blocks) and post one inline PR comment per issue.

Reads the review text on stdin. Posts an inline comment on the given line via
the GitHub API, falling back to a general PR review comment when the line is not
part of the diff. Skips issues already reported at the same file:line (unresolved),
so re-running on a new commit does not duplicate comments. Also writes
/tmp/fixes.list (one `file\tline\tcomment_id\tfix` per line) so the fix loop can
feed aider one issue at a time and then resolve the comment.

Usage:
  GH_TOKEN=... python3 post_review_comments.py <pr-number> < head-sha < review.txt
"""
import os, sys, re, json, urllib.request, urllib.error

pr = sys.argv[1]
head_sha = sys.argv[2]
review = sys.stdin.read()

if "NO_ISSUES" in review:
    open("/tmp/fixes.list", "w").close()
    sys.exit(0)

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


def existing_unresolved_locations():
    """Return the set of (path, line) already reported on unresolved threads."""
    q = """
    query($owner:String!,$name:String!,$pr:Int!) {
      repository(owner:$owner,name:$name){
        pullRequest(number:$pr){
          reviewThreads(first:100){ nodes{
            isResolved
            comments(first:5){ nodes{ path line } }
          }}
        }
      }
    }
    """
    try:
        data = gql(q, {"owner": owner, "name": name, "pr": int(pr)})
        threads = data["data"]["repository"]["pullRequest"]["reviewThreads"]["nodes"]
    except Exception as e:
        print(f"dedup lookup failed: {e} — posting without dedup", file=sys.stderr)
        return set()
    locs = set()
    for t in threads:
        if t["isResolved"]:
            continue
        for c in t["comments"]["nodes"]:
            if c.get("path") and c.get("line"):
                locs.add((c["path"], c["line"]))
    return locs


blocks = re.split(r"\n\s*\n", review.strip())
issues = []
for block in blocks:
    m_file = re.search(r"###\s*FILE:\s*(.+)", block)
    m_line = re.search(r"###\s*LINE:\s*(\d+)", block)
    m_fix = re.search(r"###\s*FIX:\s*(.+)", block, re.S)
    if m_file and m_line and m_fix:
        issues.append({
            "path": m_file.group(1).strip(),
            "line": int(m_line.group(1)),
            "fix": m_fix.group(1).strip(),
        })

if not issues:
    print("no parseable FILE/LINE/FIX blocks found", file=sys.stderr)
    open("/tmp/fixes.list", "w").close()
    sys.exit(0)

existing = existing_unresolved_locations()

fixes = []
for it in issues:
    if (it["path"], it["line"]) in existing:
        print(f"skip duplicate: {it['path']}:{it['line']}", file=sys.stderr)
        continue
    body = it["fix"]
    cid = ""
    payload = json.dumps({
        "commit_id": head_sha,
        "path": it["path"],
        "line": it["line"],
        "side": "RIGHT",
        "body": body,
    }).encode()
    req = urllib.request.Request(f"{api}/pulls/{pr}/comments", data=payload, headers=HDR, method="POST")
    posted = False
    try:
        resp = json.load(urllib.request.urlopen(req, timeout=30))
        cid = resp.get("id", "")
        posted = True
        print(f"inline comment: {it['path']}:{it['line']} id={cid}", file=sys.stderr)
    except urllib.error.HTTPError as e:
        print(f"inline failed {e.code} for {it['path']}:{it['line']} — fallback", file=sys.stderr)
        fb = json.dumps({"body": f"**{it['path']}:{it['line']}** — {body}"}).encode()
        fbreq = urllib.request.Request(f"{api}/pulls/{pr}/reviews", data=fb, headers=HDR, method="POST")
        try:
            urllib.request.urlopen(fbreq, timeout=30)
            posted = True
        except urllib.error.HTTPError as e2:
            print(f"fallback also failed {e2.code}", file=sys.stderr)

    if posted:
        fixes.append((it["path"], it["line"], str(cid), body))

with open("/tmp/fixes.list", "w", encoding="utf-8") as f:
    for path, line, cid, body in fixes:
        f.write(f"{path}\t{line}\t{cid}\t{body}\n")

print(f"posted {len(fixes)} comments", file=sys.stderr)

#!/usr/bin/env python3
"""Parse a review (FILE/LINE/FIX blocks) and post one inline PR comment per issue.

Reads the review text on stdin. Posts an inline comment on the given line via
the GitHub API, falling back to a general PR review comment when the line is not
part of the diff. Also writes /tmp/fixes.list (one `file\tfix` per line) for the
fix loop to feed aider one issue at a time.

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
api = f"https://api.github.com/repos/{repo}"

# Split into issue blocks: ### FILE: ... ### LINE: ... ### FIX: ...
# Anchor on the FILE marker so blank lines inside FIX bodies don't split an issue.
blocks = re.split(r"(?m)^(?=###\s*FILE:)", review.strip())
issues = []
cur = {}
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

fixes = []
for it in issues:
    body = it["fix"]
    # Try inline comment on the line (RIGHT side of diff).
    payload = json.dumps({
        "commit_id": head_sha,
        "path": it["path"],
        "line": it["line"],
        "side": "RIGHT",
        "body": body,
    }).encode()
    req = urllib.request.Request(
        f"{api}/pulls/{pr}/comments",
        data=payload,
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
            "Accept": "application/vnd.github+json",
        },
        method="POST",
    )
    posted = False
    try:
        urllib.request.urlopen(req, timeout=30)
        posted = True
        print(f"inline comment: {it['path']}:{it['line']}", file=sys.stderr)
    except urllib.error.HTTPError as e:
        # Line not in diff or other inline failure — fall back to general comment.
        print(f"inline failed {e.code} for {it['path']}:{it['line']} — fallback", file=sys.stderr)
        fb = json.dumps({"body": f"**{it['path']}:{it['line']}** — {body}"}).encode()
        fbreq = urllib.request.Request(
            f"{api}/pulls/{pr}/reviews",
            data=fb,
            headers={
                "Authorization": f"Bearer {token}",
                "Content-Type": "application/json",
                "Accept": "application/vnd.github+json",
            },
            method="POST",
        )
        try:
            urllib.request.urlopen(fbreq, timeout=30)
            posted = True
        except urllib.error.HTTPError as e2:
            print(f"fallback also failed {e2.code}", file=sys.stderr)

    if posted:
        fixes.append(f"{it['path']}\t{it['line']}\t{body}")

with open("/tmp/fixes.list", "w", encoding="utf-8") as f:
    for fx in fixes:
        f.write(fx + "\n")

print(f"posted {len(fixes)} comments", file=sys.stderr)

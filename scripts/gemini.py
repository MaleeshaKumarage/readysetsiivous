#!/usr/bin/env python3
"""Call the Gemini API. Reads the user prompt from stdin, prints the reply.

Usage: GEMINI_API_KEY=... python3 gemini.py "<system prompt>" < prompt.txt
"""
import os, sys, json, urllib.request, urllib.error

system = sys.argv[1] if len(sys.argv) > 1 else ""
user = sys.stdin.read()
key = os.environ.get("GEMINI_API_KEY", "")

payload = json.dumps({
    "contents": [{"parts": [{"text": f"{system}\n\n{user}"}]}],
}).encode()

req = urllib.request.Request(
    "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent",
    data=payload,
    headers={"Content-Type": "application/json", "X-goog-api-key": key},
)
try:
    resp = json.load(urllib.request.urlopen(req, timeout=180))
except urllib.error.HTTPError as e:
    print(f"Gemini error {e.code}: {e.read().decode()[:500]}", file=sys.stderr)
    sys.exit(1)

print(resp["candidates"][0]["content"]["parts"][0]["text"])

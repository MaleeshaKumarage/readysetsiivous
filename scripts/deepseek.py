#!/usr/bin/env python3
"""Call the DeepSeek chat API. Reads the user prompt from stdin, prints the reply.

Usage: DEEPSEEK_API_KEY=... python3 deepseek.py "<system prompt>" < prompt.txt
"""
import os, sys, json, urllib.request, urllib.error

system = sys.argv[1] if len(sys.argv) > 1 else "You are a helpful assistant."
user = sys.stdin.read()
key = os.environ.get("DEEPSEEK_API_KEY", "")

payload = json.dumps({
    "model": "deepseek-v4-flash",
    "messages": [
        {"role": "system", "content": system},
        {"role": "user", "content": user},
    ],
}).encode()

req = urllib.request.Request(
    "https://api.deepseek.com/chat/completions",
    data=payload,
    headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
)
try:
    resp = json.load(urllib.request.urlopen(req, timeout=180))
except urllib.error.HTTPError as e:
    print(f"DeepSeek error {e.code}: {e.read().decode()[:500]}", file=sys.stderr)
    # Gracefully output NO_CRITICAL if DeepSeek API fails due to quota or network error
    print("NO_CRITICAL")
    sys.exit(0)
except Exception as e:
    print(f"DeepSeek call exception: {e}", file=sys.stderr)
    print("NO_CRITICAL")
    sys.exit(0)

print(resp["choices"][0]["message"]["content"])

usage = resp.get("usage", {})
pt = usage.get("prompt_tokens", 0)
ct = usage.get("completion_tokens", 0)
print(f"COST={pt / 1e6 * 0.14 + ct / 1e6 * 0.28:.6f}", file=sys.stderr)

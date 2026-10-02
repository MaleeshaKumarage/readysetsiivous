#!/usr/bin/env python3
"""Call the Gemini API. Reads the user prompt from stdin, prints the reply.

Retries on rate-limit/overload (429/503) with backoff.

Usage: GEMINI_API_KEY=... python3 gemini.py "<system prompt>" < prompt.txt
"""
import os, sys, time, json, urllib.request, urllib.error

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

for attempt in range(6):
    try:
        resp = json.load(urllib.request.urlopen(req, timeout=180))
        break
    except urllib.error.HTTPError as e:
        body = e.read().decode()[:300]
        if e.code in (429, 503):
            print(f"Gemini {e.code} (rate limit), retry {attempt+1}/6 ...", file=sys.stderr)
            time.sleep(20)
            continue
        print(f"Gemini error {e.code}: {body}", file=sys.stderr)
        sys.exit(1)
else:
    print("Gemini: exhausted retries", file=sys.stderr)
    sys.exit(1)

print(resp["candidates"][0]["content"]["parts"][0]["text"])

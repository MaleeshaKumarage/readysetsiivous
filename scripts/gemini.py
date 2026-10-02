#!/usr/bin/env python3
"""Call the Gemini API. Reads the user prompt from stdin, prints the reply.

Retries on rate-limit/overload (429/503) with exponential backoff + jitter,
honoring the Retry-After header. Falls back to DeepSeek when Gemini exhausts
retries so a rate-limited free key never blocks the pipeline.

Usage:
  GEMINI_API_KEY=... DEEPSEEK_API_KEY=... python3 gemini.py "<system prompt>" < prompt.txt
"""
import os, sys, time, json, random, subprocess, urllib.request, urllib.error

system = sys.argv[1] if len(sys.argv) > 1 else ""
user = sys.stdin.read()
key = os.environ.get("GEMINI_API_KEY", "")

MODEL = "gemini-flash-latest"

payload = json.dumps({
    "contents": [{"parts": [{"text": f"{system}\n\n{user}"}]}],
}).encode()

MAX_ATTEMPTS = 4

for attempt in range(MAX_ATTEMPTS):
    req = urllib.request.Request(
        f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
        data=payload,
        headers={"Content-Type": "application/json", "X-goog-api-key": key},
    )
    try:
        resp = json.load(urllib.request.urlopen(req, timeout=180))
        print(resp["candidates"][0]["content"]["parts"][0]["text"])
        sys.exit(0)
    except urllib.error.HTTPError as e:
        body = e.read().decode()[:300]
        if e.code not in (429, 503):
            print(f"Gemini error {e.code}: {body}", file=sys.stderr)
            sys.exit(1)
        # Honor Retry-After; otherwise exponential backoff + jitter.
        retry_after = e.headers.get("Retry-After") if e.headers else None
        if retry_after:
            try:
                delay = float(retry_after)
            except ValueError:
                delay = (2 ** attempt) + random.uniform(0, 1)
        else:
            delay = (2 ** attempt) + random.uniform(0, 1)
        print(f"Gemini {e.code} (rate limit), retry {attempt+1}/{MAX_ATTEMPTS} in {delay:.1f}s ...", file=sys.stderr)
        time.sleep(delay)
    except urllib.error.URLError as e:
        delay = (2 ** attempt) + random.uniform(0, 1)
        print(f"Gemini network error: {e.reason}; retry in {delay:.1f}s", file=sys.stderr)
        time.sleep(delay)

# Exhausted — fall back to DeepSeek rather than fail the pipeline.
print("Gemini exhausted retries — falling back to DeepSeek", file=sys.stderr)
if not os.environ.get("DEEPSEEK_API_KEY"):
    print("No DEEPSEEK_API_KEY set for fallback", file=sys.stderr)
    sys.exit(1)

deepseek = os.path.join(os.path.dirname(os.path.abspath(__file__)), "deepseek.py")
proc = subprocess.run(
    [sys.executable, deepseek, system],
    input=user,
    capture_output=True,
    text=True,
)
if proc.returncode == 0:
    sys.stdout.write(proc.stdout)
    sys.exit(0)
print(proc.stderr, file=sys.stderr)
sys.exit(1)

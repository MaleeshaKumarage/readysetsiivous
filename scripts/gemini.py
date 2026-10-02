#!/usr/bin/env python3
"""Call the Gemini API. Reads the user prompt from stdin, prints the reply.

Retries on rate-limit/overload (429/503) with exponential backoff + jitter,
honoring the Retry-After header. Falls back to DeepSeek when Gemini exhausts
retries so a rate-limited free key never blocks the pipeline.

Note: only 429/503 (rate-limit/overload) and network errors fall back to
DeepSeek. Other HTTP errors (e.g. 400/401/403) abort immediately, since they
indicate a bad request or invalid key that DeepSeek would not resolve.

Usage:
  GEMINI_API_KEY=... DEEPSEEK_API_KEY=... python3 gemini.py "<system prompt>" < prompt.txt
"""
import os, sys, time, json, random, subprocess, urllib.request, urllib.error

# Never sleep longer than this between retries, even if the server asks for more.
MAX_DELAY_SECONDS = 60.0

system = sys.argv[1] if len(sys.argv) > 1 else ""
user = sys.stdin.read()
key = os.environ.get("GEMINI_API_KEY", "")

MODEL = "gemini-flash-latest"

payload = json.dumps({
    "contents": [{"parts": [{"text": f"{system}\n\n{user}"}]}],
}).encode()

MAX_ATTEMPTS = 4


def backoff(attempt: int) -> float:
    """Exponential backoff with jitter, capped at MAX_DELAY_SECONDS."""
    return min((2 ** attempt) + random.uniform(0, 1), MAX_DELAY_SECONDS)


def extract_text(resp: dict) -> str | None:
    """Pull the first text part out of a Gemini response, or None if unusable."""
    try:
        candidates = resp.get("candidates") or []
        if not candidates:
            return None
        parts = candidates[0].get("content", {}).get("parts") or []
        if not parts:
            return None
        text = parts[0].get("text")
        return text if text else None
    except (AttributeError, TypeError, IndexError, KeyError):
        return None


for attempt in range(MAX_ATTEMPTS):
    req = urllib.request.Request(
        f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
        data=payload,
        headers={"Content-Type": "application/json", "X-goog-api-key": key},
    )
    try:
        resp = json.load(urllib.request.urlopen(req, timeout=180))
        text = extract_text(resp)
        if text is None:
            # SAFETY-blocked, empty, or otherwise unusable response — treat like a
            # transient failure and retry rather than crashing with KeyError/IndexError.
            print("Gemini returned no usable text; retrying", file=sys.stderr)
            if attempt < MAX_ATTEMPTS - 1:
                time.sleep(backoff(attempt))
                continue
            break
        print(text)
        sys.exit(0)
    except urllib.error.HTTPError as e:
        body = e.read().decode()[:300]
        if e.code not in (429, 503):
            print(f"Gemini error {e.code}: {body}", file=sys.stderr)
            sys.exit(1)
        # Honor Retry-After; otherwise exponential backoff + jitter.
        retry_after = e.headers.get("Retry-After") if e.headers else None
        delay = backoff(attempt)
        if retry_after:
            try:
                delay = min(float(retry_after), MAX_DELAY_SECONDS)
            except ValueError:
                # HTTP-date form is legal but not worth parsing; keep the backoff.
                pass
        print(f"Gemini {e.code} (rate limit), retry {attempt+1}/{MAX_ATTEMPTS} in {delay:.1f}s ...", file=sys.stderr)
        if attempt < MAX_ATTEMPTS - 1:
            time.sleep(delay)
    except urllib.error.URLError as e:
        delay = backoff(attempt)
        print(f"Gemini network error: {e.reason}; retry in {delay:.1f}s", file=sys.stderr)
        if attempt < MAX_ATTEMPTS - 1:
            time.sleep(delay)

# Exhausted — fall back to DeepSeek rather than fail the pipeline.
print("Gemini exhausted retries — falling back to DeepSeek", file=sys.stderr)
if not os.environ.get("DEEPSEEK_API_KEY", "").strip():
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

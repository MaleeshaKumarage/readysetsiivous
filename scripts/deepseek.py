#!/usr/bin/env python3
"""Call the DeepSeek chat API. Reads the user prompt from stdin, prints the reply.

Usage: DEEPSEEK_API_KEY=... python3 deepseek.py "<system prompt>" < prompt.txt
"""
import os, sys, json, urllib.request, urllib.error


def main():
    system = sys.argv[1] if len(sys.argv) > 1 else "You are a helpful assistant."
    user = sys.stdin.read()
    key = os.environ.get("DEEPSEEK_API_KEY", "")
    if not key.strip():
        print("DeepSeek review unavailable: API key is not configured", file=sys.stderr)
        return 1

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
        with urllib.request.urlopen(req, timeout=180) as response:
            resp = json.load(response)
        choice = resp["choices"][0]
        content = choice["message"]["content"]
        if not isinstance(content, str) or not content.strip():
            raise ValueError("empty response")
        if choice.get("finish_reason") != "stop":
            raise ValueError("incomplete response")
        usage = resp.get("usage") or {}
        pt = max(0, int(usage.get("prompt_tokens", 0)))
        ct = max(0, int(usage.get("completion_tokens", 0)))
    except urllib.error.HTTPError as error:
        # Provider bodies and exception text can contain credentials or prompts.
        print(f"DeepSeek review unavailable: HTTP {error.code}", file=sys.stderr)
        return 1
    except Exception:
        print("DeepSeek review unavailable: request failed or response was invalid", file=sys.stderr)
        return 1

    print(content.strip())
    print(f"COST={pt / 1e6 * 0.14 + ct / 1e6 * 0.28:.6f}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())

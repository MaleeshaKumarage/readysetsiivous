#!/usr/bin/env python3
"""Reject incomplete or ambiguous AI review output before posting to GitHub."""
import re
import sys


def validate(text):
    text = text.strip()
    if text == "NO_CRITICAL":
        return True
    if not text or "NO_CRITICAL" in text or "NO_ISSUES" in text:
        return False
    blocks = re.split(r"\n\s*\n", text)
    return all(re.fullmatch(
        r"### FILE: [^\r\n]+\r?\n### LINE: [1-9][0-9]*\r?\n### FIX: \S[^\r\n]*",
        block,
    ) for block in blocks)


if __name__ == "__main__":
    if not validate(sys.stdin.read()):
        print("AI review unavailable: output did not match the review contract", file=sys.stderr)
        sys.exit(1)

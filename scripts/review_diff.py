#!/usr/bin/env python3
"""Review a unified diff in size-capped chunks, one DeepSeek call per chunk.

Usage: python3 review_diff.py "<system prompt>" < diff.txt

Large PR diffs made single calls exceed the provider's response time. Splitting
the diff at file boundaries (and at hunks for very large files) keeps each call
small. Each chunk is retried once. If any chunk still fails, the whole review
fails, so a partial review is never reported as clean.
"""
import os
import runpy
import subprocess
import sys
import time
from pathlib import Path

DEEPSEEK = Path(__file__).resolve().parent / "deepseek.py"
validate_review = runpy.run_path(str(DEEPSEEK.with_name("validate_pr_review.py")))["validate"]
FORMAT_INSTRUCTIONS = """
No tools are available. Return only NO_CRITICAL or blank-line-separated blocks
with exactly these three lines:
### FILE: <path>
### LINE: <positive integer>
### FIX: <single-line explanation>
Do not add analysis, markdown fences, tool calls, or combine a clean verdict
with findings.
You see only part of the diff. Absence from this chunk is not evidence that an
import, translation, stylesheet, or test is missing from the repository.
"""
DEFAULT_CHUNK_BYTES = 4_000
ATTEMPTS = 2
RETRY_DELAY_SECONDS = 5


def chunk_limit():
    try:
        return max(1_000, int(os.environ.get("REVIEW_CHUNK_BYTES", DEFAULT_CHUNK_BYTES)))
    except ValueError:
        return DEFAULT_CHUNK_BYTES


def split_files(diff):
    """Split a diff into per-file sections. Text before the first file header is kept as its own section."""
    sections, current = [], []
    for line in diff.splitlines(keepends=True):
        if line.startswith("diff --git ") and current:
            sections.append("".join(current))
            current = []
        current.append(line)
    if current:
        sections.append("".join(current))
    return sections


def split_hunks(section, limit):
    """Split one oversized file section at hunk headers, repeating the file header in each piece."""
    lines = section.splitlines(keepends=True)
    header, hunks = [], []
    for line in lines:
        if line.startswith("@@"):
            hunks.append([line])
        elif hunks:
            hunks[-1].append(line)
        else:
            header.append(line)
    pieces, current = [], "".join(header)
    for hunk in hunks:
        text = "".join(hunk)
        if len(current.encode()) + len(text.encode()) > limit and current != "".join(header):
            pieces.append(current)
            current = "".join(header)
        current += text
    if current:
        pieces.append(current)
    return pieces


def make_chunks(diff, limit=None):
    """Group the diff into chunks of at most `limit` bytes, never splitting a file unless it is oversized."""
    limit = limit or chunk_limit()
    pieces = []
    for section in split_files(diff):
        if len(section.encode()) > limit:
            pieces.extend(split_hunks(section, limit))
        else:
            pieces.append(section)
    chunks, current = [], ""
    for piece in pieces:
        if current and len((current + piece).encode()) > limit:
            chunks.append(current)
            current = ""
        current += piece
    if current:
        chunks.append(current)
    return chunks


def review_chunk(system, chunk, run=subprocess.run, sleep=time.sleep):
    """Return the reviewer's output for one chunk, or None after all attempts fail."""
    prompt = system + FORMAT_INSTRUCTIONS
    for attempt in range(ATTEMPTS):
        result = run(
            [sys.executable, str(DEEPSEEK), prompt],
            input=chunk, capture_output=True, text=True,
        )
        if result.returncode == 0 and validate_review(result.stdout):
            return result.stdout.strip()
        if result.returncode == 0:
            prompt += "\nThe previous response failed format validation. Review the chunk again and return only the exact contract above.\n"
            sys.stderr.write("AI review response did not match the review contract; retrying if attempts remain\n")
        else:
            sys.stderr.write(result.stderr)
        if attempt + 1 < ATTEMPTS:
            sleep(RETRY_DELAY_SECONDS)
    return None


def review(diff, system, run=subprocess.run, sleep=time.sleep):
    """Review every chunk. Returns the merged findings, 'NO_CRITICAL', or None if any chunk failed."""
    chunks = make_chunks(diff)
    if not chunks:
        return "NO_CRITICAL"
    findings = []
    for number, chunk in enumerate(chunks, 1):
        sys.stderr.write(f"review chunk {number}/{len(chunks)} ({len(chunk.encode())} bytes)\n")
        output = review_chunk(system, chunk, run=run, sleep=sleep)
        if output is None:
            sys.stderr.write(f"DeepSeek review unavailable: chunk {number} of {len(chunks)} failed\n")
            return None
        if output and output != "NO_CRITICAL":
            findings.append(output)
    return "\n\n".join(findings) if findings else "NO_CRITICAL"


def main():
    system = sys.argv[1] if len(sys.argv) > 1 else "You are a helpful assistant."
    result = review(sys.stdin.read(), system)
    if result is None:
        return 1
    print(result)
    return 0


if __name__ == "__main__":
    sys.exit(main())

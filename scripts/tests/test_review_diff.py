import importlib.util
from pathlib import Path
import subprocess
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]


def load(name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


review_diff = load("review_diff")


def file_section(name, hunks=1, body_lines=20):
    lines = [f"diff --git a/{name} b/{name}\n", f"--- a/{name}\n", f"+++ b/{name}\n"]
    for h in range(hunks):
        lines.append(f"@@ -{h * 100 + 1},{body_lines} +{h * 100 + 1},{body_lines} @@\n")
        lines.extend(f"+synthetic line {h}.{i} in {name}\n" for i in range(body_lines))
    return "".join(lines)


class ChunkTests(unittest.TestCase):
    def test_small_diff_is_one_chunk_and_lossless(self):
        diff = file_section("a.ts") + file_section("b.ts")
        self.assertEqual(review_diff.make_chunks(diff, limit=10_000), [diff])

    def test_files_are_grouped_under_the_limit_without_losing_text(self):
        diff = "".join(file_section(f"f{i}.ts") for i in range(12))
        chunks = review_diff.make_chunks(diff, limit=2_000)
        self.assertGreater(len(chunks), 1)
        self.assertTrue(all(len(c.encode()) <= 2_000 for c in chunks))
        self.assertEqual("".join(chunks), diff)

    def test_oversized_file_splits_at_hunks_and_repeats_the_header(self):
        diff = file_section("big.cs", hunks=8, body_lines=30)
        chunks = review_diff.make_chunks(diff, limit=2_500)
        self.assertGreater(len(chunks), 1)
        for chunk in chunks:
            self.assertTrue(chunk.startswith("diff --git a/big.cs b/big.cs\n"))
            self.assertIn("@@", chunk)
        hunks_seen = sum(c.count("\n@@") + c.startswith("@@") for c in chunks)
        self.assertEqual(hunks_seen, 8)

    def test_non_ascii_hunks_respect_the_byte_limit(self):
        module = load("review_diff")
        header = "diff --git a/x.md b/x.md\n--- a/x.md\n+++ b/x.md\n"
        # Each hunk is about 300 characters but about 600 bytes, so a character budget would let two hunks share one piece.
        hunks = ["@@ -1 +1 @@\n" + "\u00e4" * 300 + "\n" for _ in range(4)]
        diff = header + "".join(hunks)
        limit = 1000
        pieces = module.split_hunks(diff, limit)
        self.assertTrue(all(len(piece.encode()) <= limit for piece in pieces if piece.count("@@ -1 +1 @@") == 1), pieces)
        self.assertEqual("".join(pieces).count("@@ -1 +1 @@"), 4)

    def test_empty_diff_has_no_chunks(self):
        self.assertEqual(review_diff.make_chunks("", limit=1_000), [])


class ReviewTests(unittest.TestCase):
    def fake_run(self, outputs):
        calls = []

        def run(args, input, capture_output, text):
            calls.append(input)
            status, out = outputs(len(calls))
            return subprocess.CompletedProcess(args, status, stdout=out, stderr="provider detail")
        return run, calls

    def test_clean_chunks_merge_to_no_critical(self):
        run, calls = self.fake_run(lambda n: (0, "NO_CRITICAL\n"))
        diff = "".join(file_section(f"f{i}.ts") for i in range(6))
        with _limit(2_000):
            result = review_diff.review(diff, "sys", run=run, sleep=lambda s: None)
        self.assertEqual(result, "NO_CRITICAL")
        self.assertGreater(len(calls), 1)

    def test_findings_from_several_chunks_are_joined_as_blocks(self):
        findings = ["### FILE: a.ts\n### LINE: 3\n### FIX: fix a", "### FILE: b.ts\n### LINE: 9\n### FIX: fix b"]
        run, _ = self.fake_run(lambda n: (0, findings[(n - 1) % 2] if n <= 2 else "NO_CRITICAL"))
        diff = "".join(file_section(f"f{i}.ts") for i in range(4))
        with _limit(2_000):
            result = review_diff.review(diff, "sys", run=run, sleep=lambda s: None)
        self.assertIn("### FILE: a.ts", result)
        self.assertIn("### FILE: b.ts", result)
        self.assertIn("\n\n", result)

    def test_a_chunk_that_fails_twice_fails_the_whole_review(self):
        run, _ = self.fake_run(lambda n: (1, "") if n in (2, 3) else (0, "NO_CRITICAL"))
        diff = "".join(file_section(f"f{i}.ts") for i in range(4))
        with _limit(2_000):
            self.assertIsNone(review_diff.review(diff, "sys", run=run, sleep=lambda s: None))

    def test_a_transient_chunk_failure_is_retried(self):
        run, calls = self.fake_run(lambda n: (1, "") if n == 1 else (0, "NO_CRITICAL"))
        result = review_diff.review(file_section("only.ts"), "sys", run=run, sleep=lambda s: None)
        self.assertEqual(result, "NO_CRITICAL")
        self.assertEqual(len(calls), 2)

    def test_malformed_success_is_retried_without_salvaging_a_verdict(self):
        for malformed in ("", "Looks good", "NO_CRITICAL\nBut fix this", "<tool_call>search</tool_call>"):
            with self.subTest(output=malformed):
                run, calls = self.fake_run(lambda n: (0, malformed if n == 1 else "NO_CRITICAL"))
                self.assertEqual(review_diff.review(file_section("x.ts"), "sys", run=run, sleep=lambda s: None), "NO_CRITICAL")
                self.assertEqual(len(calls), 2)

    def test_retry_quotes_the_rejected_output_as_data_for_reformatting(self):
        prompts = []
        def run(args, **kwargs):
            prompts.append(args[-1])
            return subprocess.CompletedProcess(args, 0, stdout="private invalid output" if len(prompts) == 1 else "NO_CRITICAL", stderr="")
        self.assertEqual(review_diff.review(file_section("x.ts"), "review instructions", run=run, sleep=lambda s: None), "NO_CRITICAL")
        self.assertIn("review instructions", prompts[1])
        self.assertIn("previous response failed format validation", prompts[1])
        self.assertIn("<previous_response>\nprivate invalid output\n</previous_response>", prompts[1])
        self.assertNotIn("<previous_response>", prompts[0])

    def test_findings_wrapped_in_prose_are_recovered_when_the_retry_follows_the_contract(self):
        finding = "### FILE: x.ts\n### LINE: 4\n### FIX: Guard the null case"
        run, calls = self.fake_run(lambda n: (0, f"Here is my review.\n\n{finding}\n\nHope this helps." if n == 1 else finding))
        self.assertEqual(review_diff.review(file_section("x.ts"), "sys", run=run, sleep=lambda s: None), finding)
        self.assertEqual(len(calls), 2)

    def test_oversized_rejected_output_is_truncated_in_the_retry_prompt(self):
        prompts = []
        def run(args, **kwargs):
            prompts.append(args[-1])
            return subprocess.CompletedProcess(args, 0, stdout="x" * 20_000 if len(prompts) == 1 else "NO_CRITICAL", stderr="")
        review_diff.review(file_section("x.ts"), "sys", run=run, sleep=lambda s: None)
        self.assertLess(len(prompts[1]), 9_000)

    def test_persistent_malformed_success_fails_closed(self):
        run, calls = self.fake_run(lambda n: (0, "Analysis: NO_CRITICAL"))
        self.assertIsNone(review_diff.review(file_section("x.ts"), "sys", run=run, sleep=lambda s: None))
        self.assertEqual(len(calls), 2)

    def test_valid_findings_before_invalid_chunk_cannot_be_reported_as_complete(self):
        finding = "### FILE: x.ts\n### LINE: 1\n### FIX: Fix the bug"
        run, _ = self.fake_run(lambda n: (0, finding if n == 1 else "prose"))
        with _limit(2_000):
            self.assertIsNone(review_diff.review("".join(file_section(f"f{i}.ts") for i in range(4)), "sys", run=run, sleep=lambda s: None))

    def test_corrected_retry_retains_findings(self):
        finding = "### FILE: x.ts\n### LINE: 1\n### FIX: Fix the bug"
        run, calls = self.fake_run(lambda n: (0, "NO_CRITICAL\n" + finding if n == 1 else finding))
        self.assertEqual(review_diff.review(file_section("x.ts"), "sys", run=run, sleep=lambda s: None), finding)
        self.assertEqual(len(calls), 2)

    def test_provider_details_are_not_echoed_to_stdout(self):
        run, _ = self.fake_run(lambda n: (1, ""))
        import io, contextlib
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            self.assertIsNone(review_diff.review(file_section("x.ts"), "sys", run=run, sleep=lambda s: None))
        self.assertEqual(out.getvalue(), "")


class _limit:
    def __init__(self, value):
        self.value = str(value)

    def __enter__(self):
        import os
        self.old = os.environ.get("REVIEW_CHUNK_BYTES")
        os.environ["REVIEW_CHUNK_BYTES"] = self.value

    def __exit__(self, *exc):
        import os
        if self.old is None:
            os.environ.pop("REVIEW_CHUNK_BYTES", None)
        else:
            os.environ["REVIEW_CHUNK_BYTES"] = self.old


if __name__ == "__main__":
    unittest.main()

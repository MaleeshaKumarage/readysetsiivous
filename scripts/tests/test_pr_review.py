import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
from urllib.error import HTTPError, URLError

SCRIPTS = Path(__file__).resolve().parents[1]


def load(name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


deepseek = load("deepseek")
review = load("validate_pr_review")


class ProviderReviewTests(unittest.TestCase):
    def call(self, response=None, error=None, key="synthetic-test-key"):
        output, errors = io.StringIO(), io.StringIO()
        with patch.dict("os.environ", {"DEEPSEEK_API_KEY": key}, clear=True), \
                patch.object(sys, "argv", ["deepseek.py", "review"]), \
                patch.object(sys, "stdin", io.StringIO("synthetic diff")), \
                patch.object(deepseek.urllib.request, "urlopen") as request, \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
            if error:
                request.side_effect = error
            else:
                request.return_value.__enter__.return_value = io.StringIO(json.dumps(response))
            status = deepseek.main()
        return status, output.getvalue(), errors.getvalue(), request

    def test_provider_failures_never_claim_clean_review_or_leak_details(self):
        for error in [HTTPError("https://example.invalid", 429, "private", {}, io.BytesIO(b"private")),
                      URLError("private"), TimeoutError("private")]:
            with self.subTest(error=type(error).__name__):
                status, output, errors, _ = self.call(error=error)
                self.assertNotEqual(status, 0)
                self.assertEqual(output, "")
                self.assertNotIn("private", errors)
                self.assertNotIn("COST=", errors)

    def test_missing_key_does_not_call_provider(self):
        status, output, _, request = self.call(key="")
        self.assertNotEqual(status, 0)
        self.assertEqual(output, "")
        request.assert_not_called()

    def test_malformed_or_truncated_responses_fail_without_output(self):
        for response in [{}, {"choices": []}, {"choices": [{"message": {"content": " "}}]},
                         {"choices": [{"message": {"content": "NO_CRITICAL"}}]},
                         {"choices": [{"message": {"content": "NO_CRITICAL"}, "finish_reason": None}]},
                         {"choices": [{"message": {"content": "NO_CRITICAL"}, "finish_reason": "length"}]}]:
            with self.subTest(response=response):
                status, output, _, _ = self.call(response=response)
                self.assertNotEqual(status, 0)
                self.assertEqual(output, "")

    def test_completed_clean_and_findings_responses_remain_available(self):
        for content in ["NO_CRITICAL", "### FILE: a.py\n### LINE: 1\n### FIX: Validate ownership"]:
            status, output, errors, _ = self.call(response={"choices": [{
                "message": {"content": content}, "finish_reason": "stop"}],
                "usage": {"prompt_tokens": 10, "completion_tokens": 5}})
            self.assertEqual(status, 0)
            self.assertEqual(output, content + "\n")
            self.assertIn("COST=", errors)


class ReviewContractTests(unittest.TestCase):
    def test_exact_clean_or_complete_findings_only(self):
        self.assertTrue(review.validate("NO_CRITICAL\n"))
        block = "### FILE: api/a.cs\n### LINE: 12\n### FIX: Check ownership."
        self.assertTrue(review.validate(block + "\n\n" + block))
        for text in ["", "Probably fine", "NO_CRITICAL but check this", "NO_CRITICAL\n\n" + block,
                     block + "\n\nunfinished finding", block.replace("12", "0"),
                     block.replace("Check ownership.", "NO_ISSUES")]:
            with self.subTest(text=text):
                self.assertFalse(review.validate(text))

    def test_workflow_preserves_human_review_threads_and_uses_exact_gate(self):
        workflow = (SCRIPTS.parent / ".github/workflows/pr-review.yml").read_text(encoding="utf-8")
        self.assertNotIn("resolve_fixed_comments", workflow)
        self.assertFalse((SCRIPTS / "resolve_fixed_comments.py").exists())
        self.assertIn("python3 scripts/validate_pr_review.py", workflow)
        self.assertNotIn('grep -q "NO_CRITICAL"', workflow)
        for path in (SCRIPTS.parent / ".github/workflows").glob("*.yml"):
            with self.subTest(workflow=path.name):
                text = path.read_text(encoding="utf-8")
                self.assertNotIn("scripts/resolve_comment.py", text)
                self.assertNotIn("scripts/resolve_fixed_comments.py", text)


if __name__ == "__main__":
    unittest.main()

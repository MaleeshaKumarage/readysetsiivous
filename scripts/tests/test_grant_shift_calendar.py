import contextlib
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("calendar_grant", Path(__file__).resolve().parents[1] / "grant-shift-calendar.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CalendarGrantTests(unittest.TestCase):
    def person(self, **changes):
        return {"id": "employee-one", "email": "initial@example.invalid", "role": "admin", "isActive": True,
                "keycloakUserId": "linked-login", "shiftCalendarEnabled": False, **changes}

    def test_explicit_grant_uses_audited_api_without_printing_token(self):
        requests = []

        def open_request(request, timeout):
            requests.append(request)
            result = [self.person()] if request.get_method() == "GET" else {"enabled": True}
            return io.BytesIO(json.dumps(result).encode())

        with patch.object(module.urllib.request, "build_opener") as opener, contextlib.redirect_stdout(io.StringIO()) as output:
            opener.return_value.open.side_effect = open_request
            module.grant("https://example.invalid", "INITIAL@example.invalid", "synthetic-token")
        self.assertEqual([request.get_method() for request in requests], ["GET", "PUT"])
        self.assertEqual(json.loads(requests[1].data), {"enabled": True})
        self.assertNotIn("synthetic-token", output.getvalue())

    def test_single_account_rollout_refuses_other_existing_grants(self):
        with patch.object(module.urllib.request, "build_opener") as opener:
            opener.return_value.open.return_value = io.BytesIO(json.dumps([self.person(), self.person(id="other", email="other@example.invalid", shiftCalendarEnabled=True)]).encode())
            with self.assertRaisesRegex(ValueError, "Another employee"):
                module.grant("https://example.invalid", "initial@example.invalid", "synthetic-token")
            self.assertEqual(opener.return_value.open.call_count, 1)

    def test_inactive_or_unlinked_admin_is_rejected(self):
        for changes in [{"isActive": False}, {"keycloakUserId": ""}, {"role": "employee"}, {"identitySyncPending": True}]:
            with self.subTest(changes=changes), patch.object(module.urllib.request, "build_opener") as opener:
                opener.return_value.open.return_value = io.BytesIO(json.dumps([self.person(**changes)]).encode())
                with self.assertRaisesRegex(ValueError, "active, synchronized admin"):
                    module.grant("https://example.invalid", "initial@example.invalid", "synthetic-token")
                self.assertEqual(opener.return_value.open.call_count, 1)

    def test_remote_plaintext_and_missing_token_are_rejected(self):
        for url, token in [("http://example.invalid", "synthetic-token"), ("https://example.invalid", "")]:
            with self.subTest(url=url), patch.object(module.urllib.request, "build_opener") as opener:
                with self.assertRaises(ValueError):
                    module.grant(url, "initial@example.invalid", token)
                opener.assert_not_called()


if __name__ == "__main__":
    unittest.main()

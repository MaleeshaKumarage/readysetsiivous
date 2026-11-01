"""Grant an explicit, audited, single-account calendar rollout through the admin API."""
import argparse
import json
import os
import urllib.request
from urllib.parse import urlparse


def grant(api_url, email, token):
    parsed = urlparse(api_url)
    if parsed.scheme != "https" and not (parsed.scheme == "http" and parsed.hostname in {"localhost", "127.0.0.1"}):
        raise ValueError("Use HTTPS, or HTTP only for localhost.")
    if not token:
        raise ValueError("Set CALENDAR_ADMIN_TOKEN to an authorized admin session token.")

    def request(path, body=None):
        req = urllib.request.Request(api_url.rstrip("/") + path,
            data=None if body is None else json.dumps(body).encode(),
            headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
            method="GET" if body is None else "PUT")
        # Redirects must not forward an admin bearer token to another endpoint.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        with urllib.request.build_opener(NoRedirect()).open(req, timeout=30) as response:
            return json.load(response)

    people = request("/api/v1/admin/employees?includeInactive=true")
    matches = [p for p in people if p["email"].strip().casefold() == email.strip().casefold()]
    if len(matches) != 1:
        raise ValueError("Expected exactly one matching employee; reconcile the roster first.")
    person = matches[0]
    if person["role"] != "admin" or not person["isActive"] or person.get("identitySyncPending") or not person.get("keycloakUserId"):
        raise ValueError("The target must be an active, synchronized admin with a linked login.")
    if any(p.get("shiftCalendarEnabled") and p["id"] != person["id"] for p in people):
        raise ValueError("Another employee already has access; review existing grants before a single-account rollout.")
    result = request("/api/v1/admin/employees/" + person["id"] + "/shift-calendar-access", {"enabled": True})
    if result.get("enabled") is not True:
        raise ValueError("The access grant was not confirmed.")
    print("Calendar access grant confirmed for employee " + person["id"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", required=True)
    parser.add_argument("--email", required=True)
    args = parser.parse_args()
    try:
        grant(args.api_url, args.email, os.environ.get("CALENDAR_ADMIN_TOKEN", ""))
    except Exception as error:
        # Do not print request objects, headers, or a session token.
        parser.exit(1, "Calendar grant failed: " + str(error) + "\n")

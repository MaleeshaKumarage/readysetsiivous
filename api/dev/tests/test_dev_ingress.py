#!/usr/bin/env python3
"""Validate actual Compose interpolation without starting services or contacting hosts."""
import ipaddress
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[3]
FILES = ("api/dev/docker-compose.yml", "api/deploy/docker-compose.dev.yml")


def safe_origin(value):
    try:
        if not isinstance(value, str) or any(ord(c) <= 32 or ord(c) == 127 for c in value):
            raise ValueError("Origin contains whitespace or control characters")
        url = urlsplit(value)
        port = url.port
        if not url.hostname or url.username or url.password or url.query or url.fragment or url.path not in ("", "/"):
            raise ValueError("Expected an origin without credentials, path, query or fragment")
        host = url.hostname
        try:
            local = ipaddress.ip_address(host).is_loopback
        except ValueError:
            local = host == "localhost"
        if url.scheme != "https" and not (url.scheme == "http" and local):
            raise ValueError("Remote public origins require HTTPS")
        if port == 0:
            raise ValueError("Invalid origin port")
    except (ValueError, TypeError) as error:
        raise ValueError("Invalid dev public origin") from error


def validate(config):
    for service in config["services"].values():
        for port in service.get("ports", []):
            if port.get("host_ip") != "127.0.0.1":
                raise ValueError("Every dev published port must explicitly bind 127.0.0.1")
    kc = config["services"]["keycloak"]["environment"]
    issuer = kc.get("KC_HOSTNAME_URL", kc.get("KC_HOSTNAME"))
    safe_origin(issuer)
    api = config["services"].get("api")
    if api:
        env = api["environment"]
        if env["Auth__Keycloak__ServerUrl"] != issuer:
            raise ValueError("Keycloak public hostname and API issuer must match exactly")
        safe_origin(env["App__FrontendBaseUrl"])
        if env["Cors__AllowedOrigins__0"] != env["App__FrontendBaseUrl"]:
            raise ValueError("Dev CORS origin must match the frontend origin")


def compose_config(filename, overrides=None):
    # Synthetic credentials; never use real secret inputs or print effective config.
    env = {**os.environ, "DEV_KC_DB_PASSWORD": "synthetic-db", "KEYCLOAK_ADMIN_USER": "synthetic-admin",
           "KEYCLOAK_ADMIN_PASSWORD": "synthetic-password"}
    for name in ("DEV_PUBLIC_AUTH_URL", "DEV_FRONTEND_URL"):
        env.pop(name, None)
    env.update(overrides or {})
    result = subprocess.run(["docker", "compose", "-p", "dev-ingress-config", "-f", str(ROOT / filename),
                             "config", "--format", "json"], env=env, check=True, capture_output=True, text=True, timeout=30)
    return json.loads(result.stdout)


class DevIngressTests(unittest.TestCase):
    def test_all_default_ports_are_explicit_loopback(self):
        for filename in FILES:
            with self.subTest(file=filename):
                config = compose_config(filename)
                validate(config)
                self.assertGreater(sum(len(s.get("ports", [])) for s in config["services"].values()), 0)

    def test_explicit_https_public_urls_are_consistent(self):
        for filename in FILES:
            with self.subTest(file=filename):
                config = compose_config(filename, {"DEV_PUBLIC_AUTH_URL": "https://auth.dev.example.invalid",
                                                   "DEV_FRONTEND_URL": "https://dev.example.invalid"})
                validate(config)
                kc = config["services"]["keycloak"]["environment"]
                self.assertEqual("https://auth.dev.example.invalid", kc.get("KC_HOSTNAME_URL", kc.get("KC_HOSTNAME")))

    def test_rejects_remote_cleartext_and_unsafe_origins(self):
        for origin in ("http://dev.example.invalid", "http://130.61.208.211:8081", "https://user:pass@example.invalid",
                       "https://example.invalid/path", "https://example.invalid?x=1", "https://example.invalid#x", "//example.invalid"):
            with self.subTest(origin=origin), self.assertRaises(ValueError):
                safe_origin(origin)
        for filename in FILES:
            with self.subTest(file=filename), self.assertRaises(ValueError):
                validate(compose_config(filename, {"DEV_PUBLIC_AUTH_URL": "http://remote.example.invalid"}))

    def test_operator_preflight_exit_codes(self):
        config = compose_config(FILES[1])
        command = [sys.executable, str(Path(__file__).resolve()), "--check-stdin"]
        success = subprocess.run(command, input=json.dumps(config), text=True, capture_output=True, timeout=10)
        self.assertEqual(0, success.returncode)
        config["services"]["api"]["ports"][0]["host_ip"] = "0.0.0.0"
        rejected = subprocess.run(command, input=json.dumps(config), text=True, capture_output=True, timeout=10)
        self.assertNotEqual(0, rejected.returncode)
        self.assertNotIn("synthetic-password", rejected.stderr)

    def test_rejects_widened_bind_or_mismatched_issuer(self):
        config = compose_config(FILES[1])
        config["services"]["api"]["ports"][0]["host_ip"] = "0.0.0.0"
        with self.assertRaises(ValueError):
            validate(config)
        config = compose_config(FILES[1])
        config["services"]["api"]["environment"]["Auth__Keycloak__ServerUrl"] = "https://other.example.invalid"
        with self.assertRaises(ValueError):
            validate(config)


if __name__ == "__main__":
    if sys.argv[1:] == ["--check-stdin"]:
        try:
            validate(json.load(sys.stdin))
        except (ValueError, KeyError, TypeError) as error:
            print(f"Dev ingress configuration rejected: {error}", file=sys.stderr)
            sys.exit(1)
        print("Dev ingress configuration passed (no services started)")
    else:
        unittest.main()

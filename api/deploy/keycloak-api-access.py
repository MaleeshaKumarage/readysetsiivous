#!/usr/bin/env python3
"""Scoped API audience migration. Check by default; credentials only via environment."""
import argparse
import json
import os
import re
import sys
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

PERMISSION = "platform.tenants.manage"


def mapper(name, audience):
    return {"name": name, "protocol": "openid-connect", "protocolMapper": "oidc-audience-mapper",
            "consentRequired": False, "config": {"included.custom.audience": audience,
            "access.token.claim": "true", "id.token.claim": "false"}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", required=True)
    parser.add_argument("--tenant", action="append", default=[])
    parser.add_argument("--tenant-client", default="cleaning-suite-web")
    parser.add_argument("--tenant-audience", default="cleaning-suite-api")
    parser.add_argument("--platform-realm", default="master")
    parser.add_argument("--platform-client")
    parser.add_argument("--platform-audience", default="cleaning-suite-platform-api")
    parser.add_argument("--grant-platform", action="store_true", help="Explicitly grant platform permission to the named service account")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    if not args.server.startswith(("http://", "https://")) or not (args.tenant or args.platform_client):
        parser.error("Provide an HTTP(S) Keycloak server and explicit tenant/client targets")
    if args.tenant_audience == args.platform_audience or any(a in ("account", "cleaning-suite-web", "") or a != a.strip() for a in (args.tenant_audience, args.platform_audience)):
        parser.error("API audiences must be distinct nonempty resource names")
    for realm in [*args.tenant, args.platform_realm]:
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]{2,62}", realm):
            parser.error("Invalid explicit realm name")
    if args.platform_realm in args.tenant or args.platform_client == args.tenant_client:
        parser.error("Platform realm/client must be separate from tenant targets")
    if args.grant_platform and not args.platform_client:
        parser.error("--grant-platform requires --platform-client")
    token = os.environ.get("KEYCLOAK_MIGRATION_ADMIN_TOKEN")
    if not token:
        parser.error("Set KEYCLOAK_MIGRATION_ADMIN_TOKEN (never pass the token on the command line)")
    changes = []
    missing_permission = False

    def api(method, path, body=None, missing=False):
        request = Request(args.server.rstrip("/") + "/admin/realms/" + path,
                          data=None if body is None else json.dumps(body).encode(), method=method,
                          headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"})
        try:
            with urlopen(request, timeout=30) as response:
                data = response.read()
                return json.loads(data) if data else None
        except HTTPError as error:
            if missing and error.code == 404:
                return None
            # Never print server response bodies or bearer-bearing request objects.
            raise RuntimeError(f"Keycloak {method} failed with HTTP {error.code}") from None

    def change(description, method, path, body):
        changes.append(description)
        print(("APPLY " if args.apply else "NEEDS ") + description)
        if args.apply:
            api(method, path, body)

    def client(realm, client_id):
        matches = api("GET", quote(realm) + "/clients?clientId=" + quote(client_id))
        if len(matches) > 1:
            raise RuntimeError("Ambiguous named client; no change made")
        return matches[0] if matches else None

    def ensure_mapper(realm, entry, desired):
        base = quote(realm) + "/clients/" + quote(entry["id"]) + "/protocol-mappers/models"
        matches = [m for m in api("GET", base) if m.get("name") == desired["name"]]
        if len(matches) > 1 or matches and (matches[0].get("protocolMapper") != "oidc-audience-mapper" or matches[0].get("protocol") != "openid-connect"):
            raise RuntimeError("Owned mapper name conflicts with another mapper; review manually")
        existing = matches[0] if matches else None
        if existing and all(existing.get("config", {}).get(k) == v for k, v in desired["config"].items()) and not existing.get("config", {}).get("included.client.audience"):
            return
        if existing:
            # Preserve unrelated mapper fields while owning exactly its resource emission.
            updated = {**existing, **desired, "config": {**existing.get("config", {}), **desired["config"]}}
            updated["config"].pop("included.client.audience", None)
            change(f"{realm}/{entry['clientId']} audience mapper", "PUT", base + "/" + quote(existing["id"]), updated)
        else:
            change(f"{realm}/{entry['clientId']} audience mapper", "POST", base, desired)

    for realm in dict.fromkeys(args.tenant):
        entry = client(realm, args.tenant_client)
        if not entry:
            raise RuntimeError(f"Named tenant client missing in {realm}; no client created")
        ensure_mapper(realm, entry, mapper("cleaning-suite-api-audience", args.tenant_audience))

    if args.platform_client:
        realm = args.platform_realm
        entry = client(realm, args.platform_client)
        if not entry:
            secret = os.environ.get("KEYCLOAK_PLATFORM_CLIENT_SECRET")
            if args.apply and not secret:
                raise RuntimeError("Set KEYCLOAK_PLATFORM_CLIENT_SECRET to create the explicitly named confidential client")
            desired = {"clientId": args.platform_client, "enabled": True, "publicClient": False,
                       "serviceAccountsEnabled": True, "standardFlowEnabled": False,
                       "directAccessGrantsEnabled": False, "fullScopeAllowed": False,
                       "protocol": "openid-connect", "defaultClientScopes": ["roles"],
                       "protocolMappers": [mapper("cleaning-suite-platform-audience", args.platform_audience)]}
            if args.apply:
                desired["secret"] = secret
            change(f"{realm}/{args.platform_client} confidential service client", "POST", quote(realm) + "/clients", desired)
            entry = client(realm, args.platform_client) if args.apply else None
        if entry:
            required = {"enabled": True, "publicClient": False, "serviceAccountsEnabled": True,
                        "standardFlowEnabled": False, "directAccessGrantsEnabled": False, "fullScopeAllowed": False}
            if any(entry.get(k) != v for k, v in required.items()):
                updated = {**entry, **required}
                updated.pop("secret", None)  # Never write a returned/masked secret back.
                change(f"{realm}/{args.platform_client} service-only settings", "PUT", quote(realm) + "/clients/" + quote(entry["id"]), updated)
            ensure_mapper(realm, entry, mapper("cleaning-suite-platform-audience", args.platform_audience))
        role = api("GET", quote(realm) + "/roles/" + quote(PERMISSION), missing=True)
        if not role:
            change(f"{realm} explicit platform permission definition", "POST", quote(realm) + "/roles", {"name": PERMISSION})
            role = api("GET", quote(realm) + "/roles/" + quote(PERMISSION)) if args.apply else None
        if entry and role:
            scope_path = quote(realm) + "/clients/" + quote(entry["id"]) + "/scope-mappings/realm"
            if not any(r.get("name") == PERMISSION for r in api("GET", scope_path)):
                change(f"{realm}/{args.platform_client} permission token scope", "POST", scope_path, [role])
            user = api("GET", quote(realm) + "/clients/" + quote(entry["id"]) + "/service-account-user")
            grants = quote(realm) + "/users/" + quote(user["id"]) + "/role-mappings/realm"
            assigned = any(r.get("name") == PERMISSION for r in api("GET", grants + "/composite"))
            if not assigned:
                if args.grant_platform:
                    change(f"{realm}/{args.platform_client} explicit platform permission grant", "POST", grants, [role])
                else:
                    print("MISSING platform permission grant; rerun with --grant-platform only after reviewing this identity")
                    changes.append("platform permission not granted")
                    missing_permission = True
        elif not args.apply:
            print("NEEDS platform role scope and explicit service-account grant after client/role creation")
    print(f"{'Applied' if args.apply else 'Checked'} {len(changes)} differences; credentials were not printed.")
    return 2 if missing_permission else 0 if args.apply or not changes else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RuntimeError, OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)

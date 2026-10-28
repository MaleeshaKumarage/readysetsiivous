#!/usr/bin/env python3
"""Real Docker dev-state rehearsal; synthetic data and uniquely owned projects only."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]


def run(args, env=None, check=True):
    result = subprocess.run(args, env=env, text=True, capture_output=True, timeout=180)
    if check and result.returncode:
        # Do not echo generated Compose JSON or secret-bearing command arguments.
        raise RuntimeError(f"Command failed ({result.returncode}): {result.stderr[-2000:]}")
    return result


def fixture_identity(directory):
    path = Path(directory) / "tenant-runtime.env"
    # Synthetic per-tenant pair only; mode applies atomically on POSIX.
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as output:
        output.write("Auth__Keycloak__TenantAccounts__fixture-tenant__ClientId=fixture-runtime\n"
                     "Auth__Keycloak__TenantAccounts__fixture-tenant__ClientSecret=synthetic-unusable-secret-123\n")
    return path


def rehearse(relative, unsafe=None):
    project = "devpersist-" + uuid.uuid4().hex[:16]
    assert project.startswith("devpersist-") and len(project) == 27
    env = {**os.environ, "DEV_KC_DB_PASSWORD": "synthetic'quoted-password",
           "KEYCLOAK_ADMIN_USER": "fixture-admin", "KEYCLOAK_ADMIN_PASSWORD": "fixture-password"}
    with tempfile.TemporaryDirectory(prefix=project) as directory:
        env["API_IDENTITY_ENV_FILE"] = str(fixture_identity(directory))
        source = ROOT / relative
        config = json.loads(run(["docker", "compose", "-p", project, "-f", str(source),
                                 "config", "--format", "json"], env).stdout)
        api = config["services"].pop("api", None)
        for service in config["services"].values():
            service.pop("ports", None)  # No shared/fixed host ports in the rehearsal.
            service.pop("restart", None)
        # Do not import the repository's sample realm; fixture creates its own.
        kc = config["services"]["keycloak"]
        kc["command"] = ["start-dev"]
        kc["volumes"] = []
        if api:
            config["services"]["state-helper"] = {
                "image": "postgres:16-alpine", "entrypoint": ["sleep", "infinity"],
                "volumes": api["volumes"]}
        # A malformed Compose config must not make cleanup target an existing project.
        config["name"] = project
        for volume in config.get("volumes", {}).values():
            assert not volume.get("external"), "No external volumes permitted"
            assert volume.get("name", "").startswith(project + "_"), "Unowned volume"
        if unsafe:
            # First-boot seed precedes the actual mounted initialization script.
            # One-shot initialization must fail; completed dependency keeps KC down.
            seed = Path(directory) / "00-unsafe-fixture.sql"
            seeds = {
                "role": "CREATE ROLE keycloak_dev LOGIN SUPERUSER;",
                "bypassrls": "CREATE ROLE keycloak_dev LOGIN BYPASSRLS;",
                "replication": "CREATE ROLE keycloak_dev LOGIN REPLICATION;",
                "owner": "CREATE ROLE keycloak_dev LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE; CREATE DATABASE keycloak_dev OWNER cleaning;",
            }
            seed.write_text(seeds[unsafe], encoding="utf-8")
            config["services"]["postgres"]["volumes"].append({
                "type": "bind", "source": str(seed),
                "target": "/docker-entrypoint-initdb.d/00-unsafe-fixture.sql", "read_only": True})
        path = Path(directory) / "compose.json"
        path.write_text(json.dumps(config), encoding="utf-8")
        compose = ["docker", "compose", "-p", project, "-f", str(path)]

        def dc(*args, check=True):
            return run([*compose, *args], env, check)

        def admin(*args, check=True):
            return dc("exec", "-T", "keycloak", "/opt/keycloak/bin/kcadm.sh", *args, check=check)

        def login():
            deadline = time.monotonic() + 150
            while time.monotonic() < deadline:
                result = admin("config", "credentials", "--server", "http://localhost:8080",
                               "--realm", "master", "--user", "fixture-admin",
                               "--password", "fixture-password", check=False)
                if result.returncode == 0:
                    return
                time.sleep(3)
            raise RuntimeError("Synthetic Keycloak did not become ready")

        try:
            if unsafe:
                failure = dc("up", "-d", "keycloak", check=False)
                assert failure.returncode != 0, "Unsafe initialization must fail Compose startup"
                pg_id = dc("ps", "-a", "-q", "keycloak-db-init").stdout.strip()
                assert pg_id, "Initialization fixture container missing"
                state = json.loads(run(["docker", "inspect", "--format", "{{json .State}}", pg_id]).stdout)
                assert state["Status"] == "exited" and state["ExitCode"] != 0
                logs = dc("logs", "--no-color", "keycloak-db-init").stdout
                marker = "unexpected privileges" if unsafe != "owner" else "unexpected owner"
                assert marker in logs, "Expected SQL validation failure was not reached"
                assert dc("ps", "--status", "running", "-q", "keycloak").stdout.strip() == ""
                print(f"PASS {relative}: unsafe {unsafe} fails nonzero before Keycloak starts")
                return
            # Pre-existing PostgreSQL volume has no Keycloak role/database. This
            # must work through normal up, without a manual exec or volume reset.
            dc("up", "-d", "--wait", "postgres")
            assert dc("exec", "-T", "postgres", "psql", "-U", "cleaning", "-d", "cleaning", "-Atc",
                      "SELECT count(*) FROM pg_database WHERE datname='keycloak_dev'").stdout.strip() == "0"
            dc("up", "-d")
            login()
            # Re-running init on an existing volume must not reset identity data.
            dc("run", "--rm", "keycloak-db-init")
            admin("create", "realms", "-s", "realm=persistence-fixture", "-s", "enabled=true")
            admin("create", "users", "-r", "persistence-fixture", "-s", "username=fixture-user", "-s", "enabled=true")
            before = json.loads(admin("get", "users", "-r", "persistence-fixture",
                                      "-q", "username=fixture-user", "-q", "exact=true").stdout)
            assert len(before) == 1
            if api:
                dc("exec", "-T", "state-helper", "sh", "-c",
                   "printf synthetic-upload > /app/uploads/persistence.txt; printf synthetic-key > /app/keys/entertainment.key")
            # Keep volumes; delete actual containers, not just restart processes.
            dc("down")
            dc("up", "-d")
            login()
            after = json.loads(admin("get", "users", "-r", "persistence-fixture",
                                     "-q", "username=fixture-user", "-q", "exact=true").stdout)
            assert len(after) == 1 and before[0]["id"] == after[0]["id"]
            role = dc("exec", "-T", "postgres", "psql", "-U", "cleaning", "-d", "cleaning", "-Atc",
                      "SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolbypassrls AND NOT rolreplication FROM pg_roles WHERE rolname='keycloak_dev'").stdout.strip()
            assert role == "t"
            if api:
                assert dc("exec", "-T", "state-helper", "cat", "/app/uploads/persistence.txt").stdout == "synthetic-upload"
                assert dc("exec", "-T", "state-helper", "cat", "/app/keys/entertainment.key").stdout == "synthetic-key"
            print(f"PASS {relative}: recreated realm/user identity and owned persistent state")
        finally:
            # Only this random project; no shared/dev/production volumes selected.
            dc("down", "--volumes", "--remove-orphans")


if __name__ == "__main__":
    for filename in ("api/dev/docker-compose.yml", "api/deploy/docker-compose.dev.yml"):
        for unsafe in ("role", "owner", "bypassrls", "replication"):
            rehearse(filename, unsafe)
        rehearse(filename)

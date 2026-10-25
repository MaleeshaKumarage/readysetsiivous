#!/usr/bin/env python3
"""Synthetic Docker rehearsal of PostgreSQL's sourced init wrappers."""
import json
from pathlib import Path
import stat
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[3]


def run(args):
    result = subprocess.run(args, capture_output=True, text=True, timeout=180)
    if result.returncode:
        # Fixtures contain synthetic values only; avoid printing full config/env.
        raise RuntimeError(f"Command failed ({result.returncode}): {result.stderr[-1500:]}")
    return result.stdout.strip()


def rehearse(wrapper, setup_keycloak):
    wrapper = ROOT / wrapper
    if wrapper.stat().st_mode & (stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH):
        raise AssertionError("Fixture must exercise the sourced, non-executable init path")
    project = "dbfirstboot-" + uuid.uuid4().hex[:16]
    assert project.startswith("dbfirstboot-") and len(project) == 28
    config = {
        "name": project,
        "services": {"postgres": {
            "image": "postgres:16-alpine",
            "environment": {
                "POSTGRES_USER": "fixture_bootstrap", "POSTGRES_DB": "cleaning",
                "POSTGRES_PASSWORD": "synthetic-bootstrap-only",
                "DB_SETUP_KEYCLOAK": str(setup_keycloak).lower(),
                "KC_DB_PASSWORD": "synthetic-keycloak-only",
                "DB_MIGRATOR_PASSWORD": "synthetic-migrator-only",
                "DB_RUNTIME_PASSWORD": "synthetic-runtime-only",
            },
            "volumes": ["data:/var/lib/postgresql/data",
                f"{wrapper.as_posix()}:/docker-entrypoint-initdb.d/01-fixture.sh:ro",
                f"{(ROOT / 'api/deploy/database').as_posix()}:/database:ro"],
            "healthcheck": {
                "test": ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U fixture_bootstrap -d cleaning"],
                "interval": "1s", "timeout": "3s", "retries": 60,
            },
        }},
        "volumes": {"data": {}},
    }
    with tempfile.TemporaryDirectory(prefix=project) as directory:
        path = Path(directory) / "compose.json"
        path.write_text(json.dumps(config), encoding="utf-8")
        compose = ["docker", "compose", "-p", project, "-f", str(path)]
        try:
            run([*compose, "up", "-d", "--wait", "--wait-timeout", "90"])
            # A healthy final TCP server proves control returned to PostgreSQL's
            # entrypoint after the actual non-executable wrapper was sourced.
            output = run([*compose, "exec", "-T", "-e", "PGPASSWORD=synthetic-runtime-only",
                "postgres", "psql", "-X", "-v", "ON_ERROR_STOP=1", "-h", "127.0.0.1",
                "-U", "cleaning_runtime", "-d", "cleaning", "-Atc", """
                SELECT current_user = 'cleaning_runtime'
                  AND has_schema_privilege(current_user, 'public', 'USAGE')
                  AND NOT has_schema_privilege(current_user, 'public', 'CREATE')
                  AND NOT pg_has_role(current_user, 'cleaning_owner', 'MEMBER')
                  AND NOT (SELECT rolsuper OR rolcreatedb OR rolcreaterole FROM pg_roles WHERE rolname = current_user);
                """])
            assert output == "t", "Runtime identity/grants were not initialized"
        finally:
            # Only the freshly generated fixture project/volume can be removed.
            run([*compose, "down", "--volumes", "--remove-orphans"])
    print(f"PASS sourced wrapper {wrapper.relative_to(ROOT)}")


if __name__ == "__main__":
    rehearse("api/deploy/init/01-databases.sh", True)
    rehearse("api/deploy/database/02-application.sh", False)

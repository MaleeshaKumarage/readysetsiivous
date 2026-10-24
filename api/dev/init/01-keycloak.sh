#!/usr/bin/env bash
# Dev only. Runs automatically on first boot; explicit existing-volume use is
# documented in docs/environments/dev-state-persistence.md. Never reset a volume.
set -euo pipefail
: "${DEV_KC_DB_PASSWORD:?Set DEV_KC_DB_PASSWORD}"
: "${POSTGRES_USER:?Set POSTGRES_USER}"
: "${POSTGRES_DB:?Set POSTGRES_DB}"

# psql literal quoting, never shell interpolation into SQL. Do not put the
# password in command arguments or output. Existing roles/passwords are retained.
psql -X --set=ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
\getenv dev_kc_password DEV_KC_DB_PASSWORD
SELECT format('CREATE ROLE keycloak_dev LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', :'dev_kc_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'keycloak_dev')
\gexec
SELECT 'CREATE DATABASE keycloak_dev OWNER keycloak_dev'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'keycloak_dev')
\gexec
SELECT (NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND rolcanlogin) AS safe_role
FROM pg_roles WHERE rolname = 'keycloak_dev'
\gset
\if :safe_role
\else
  \echo 'Existing keycloak_dev role has unexpected privileges; review manually.'
  \quit 1
\endif
SELECT pg_get_userbyid(datdba) = 'keycloak_dev' AS expected_owner
FROM pg_database WHERE datname = 'keycloak_dev'
\gset
\if :expected_owner
\else
  \echo 'Existing keycloak_dev database has unexpected owner; review manually.'
  \quit 1
\endif
SQL

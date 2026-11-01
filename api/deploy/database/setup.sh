#!/usr/bin/env bash
# Run only as the isolated stack's bootstrap operator. Never called by the API.
set -euo pipefail
: "${POSTGRES_USER:?}" "${POSTGRES_DB:?}"
export DB_SETUP_KEYCLOAK="${DB_SETUP_KEYCLOAK:-true}"
if [[ "$DB_SETUP_KEYCLOAK" == true ]]; then : "${KC_DB_PASSWORD:?}"; fi
: "${DB_MIGRATOR_PASSWORD:?}" "${DB_RUNTIME_PASSWORD:?}"
script_dir="$(cd -- "$(dirname -- "$0")" && pwd)"
psql -X -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$script_dir/roles.psql"
psql -X -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname cleaning -f "$script_dir/grants.psql"

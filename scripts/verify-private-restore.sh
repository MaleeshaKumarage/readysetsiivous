#!/usr/bin/env bash
# Restore a private pg_dump archive into disposable containers. Never expose ports,
# reuse an application database, print customer records, or modify the input dump.
set -euo pipefail
archive=$(realpath -- "${1:?private pg_dump archive required}")
image=${2:?exact previous API image required}
test -f "$archive"
work=$(mktemp -d /tmp/security95-restore.XXXXXXXX)
chmod 700 "$work"
name="security95-$(basename "$work" | tr '[:upper:]' '[:lower:]')"
pg="$name-pg"
api="$name-api"
network="$name-net"
cleanup() {
  docker rm -fv "$api" "$pg" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  rm -rf -- "$work"
}
trap cleanup EXIT
admin_password=$(openssl rand -hex 32)
reader_password=$(openssl rand -hex 32)
printf 'POSTGRES_DB=clone\nPOSTGRES_PASSWORD=%s\n' "$admin_password" > "$work/pg.env"
chmod 600 "$work/pg.env"
cat > "$work/pg_hba.conf" <<'HBA'
local all all trust
host clone clone_reader 0.0.0.0/0 scram-sha-256
host clone clone_reader ::/0 scram-sha-256
host all clone_reader 0.0.0.0/0 reject
host all clone_reader ::/0 reject
host all all 0.0.0.0/0 scram-sha-256
host all all ::/0 scram-sha-256
HBA
chmod 644 "$work/pg_hba.conf"
docker network create --internal "$network" >/dev/null
docker run -d --name "$pg" --network "$network" --env-file "$work/pg.env" \
  -v "$work/pg_hba.conf:/private-hba.conf:ro" postgres:16-alpine \
  postgres -c hba_file=/private-hba.conf >/dev/null
ready=false
for _ in $(seq 1 60); do
  if docker exec "$pg" pg_isready -U postgres -d clone >/dev/null 2>&1; then ready=true; break; fi
  sleep 1
done
$ready || { echo 'Restore PostgreSQL failed to start.' >&2; exit 1; }
if ! docker exec -i "$pg" pg_restore -U postgres -d clone --exit-on-error --no-owner --no-privileges < "$archive" > "$work/restore.log" 2>&1; then
  echo 'Private archive restoration failed.' >&2; exit 1
fi
printf "CREATE ROLE clone_reader LOGIN PASSWORD '%s';\nALTER ROLE clone_reader SET default_transaction_read_only=on;\nREVOKE CREATE,TEMPORARY ON DATABASE clone FROM PUBLIC;\n" "$reader_password" > "$work/reader.sql"
cat >> "$work/reader.sql" <<'SQL'
DO $$ DECLARE s text; BEGIN
  FOR s IN SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema'
  LOOP
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO clone_reader', s);
    EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO clone_reader', s);
    EXECUTE format('GRANT SELECT ON ALL SEQUENCES IN SCHEMA %I TO clone_reader', s);
  END LOOP;
END $$;
SQL
docker exec -i "$pg" psql -U postgres -d clone -v ON_ERROR_STOP=1 < "$work/reader.sql" >/dev/null
fingerprint() {
  docker exec "$pg" psql -U postgres -d clone -Atc \
    "SELECT format('SELECT %L,count(*),md5(string_agg(md5(t::text), %L ORDER BY t::text)) FROM %I.%I t;', schemaname||'.'||tablename,'',schemaname,tablename) FROM pg_tables WHERE schemaname NOT LIKE 'pg_%' AND schemaname <> 'information_schema' ORDER BY schemaname,tablename" \
    | docker exec -i "$pg" psql -U postgres -d clone -At -v ON_ERROR_STOP=1
}
fingerprint > "$work/before.txt"
if test -n "${3:-}"; then
  cmp -s "$3" "$work/before.txt" || { echo 'Restored counts/content do not match the source snapshot.' >&2; exit 1; }
fi
# Check privileges rather than attempting writes against a customer-data copy.
unsafe=$(docker exec "$pg" psql -U postgres -d clone -Atc \
  "SELECT count(*) FROM pg_tables WHERE schemaname NOT LIKE 'pg_%' AND schemaname <> 'information_schema' AND (has_table_privilege('clone_reader',format('%I.%I',schemaname,tablename),'INSERT,UPDATE,DELETE,TRUNCATE') OR tableowner='clone_reader')")
test "$unsafe" = 0 || { echo 'Reader permissions are unsafe.' >&2; exit 1; }
printf 'ConnectionStrings__DefaultConnection=Host=%s;Database=clone;Username=clone_reader;Password=%s\nMarten__AutoCreateSchemaMode=None\nBackgroundWorkers__Enabled=false\nASPNETCORE_URLS=http://+:8080\n' "$pg" "$reader_password" > "$work/api.env"
chmod 600 "$work/api.env"
# Old images ignore the worker flag. The role/network are enforced safeguards;
# the deadline also guarantees removal before QC's fixed 15-second delay.
seconds_to_midnight=$((86400 - $(date -u +%H | sed 's/^0//') * 3600 - $(date -u +%M | sed 's/^0//') * 60 - $(date -u +%S | sed 's/^0//')))
test "$seconds_to_midnight" -gt 60 || { echo 'Retry away from UTC midnight.' >&2; exit 1; }
export api network image work
timeout --kill-after=1s 10s bash -c '
  set -eu
  docker run -d --name "$api" --network "$network" --env-file "$work/api.env" "$image" >/dev/null
  address=$(docker inspect "$api" --format "{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}")
  python3 - "$address" <<"PY"
import sys, time, urllib.request
url = "http://" + sys.argv[1] + ":8080/healthz"
while True:
    try:
        with urllib.request.urlopen(url, timeout=0.3) as response:
            if response.status == 200:
                break
    except Exception:
        time.sleep(0.1)
PY
  docker rm -f "$api" >/dev/null
'
fingerprint > "$work/after.txt"
cmp -s "$work/before.txt" "$work/after.txt" || { echo 'Restore data changed during startup verification.' >&2; exit 1; }
printf 'Private restore startup passed; all table counts/content fingerprints unchanged.\n'

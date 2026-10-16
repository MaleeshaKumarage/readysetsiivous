#!/bin/sh
set -eu
case "${RELEASE_SHA:-}" in
  *[!0-9a-fA-F]*|'') echo 'Invalid release SHA' >&2; exit 1 ;;
esac
[ "${#RELEASE_SHA}" -eq 40 ] || { echo 'Invalid release SHA' >&2; exit 1; }
: "${REGISTRY_USER:?Registry user required}"
: "${REGISTRY_TOKEN:?Registry token required}"
GAME_GEMINI_API_KEY="${GAME_GEMINI_API_KEY:-}"
export GAME_GEMINI_API_KEY
cd "${DEPLOY_DIR:-/home/ubuntu/cleaning-suite}"
expected="ghcr.io/maleeshakumarage/readysetsiivous-api:sha-$RELEASE_SHA"
previous_container=$(docker compose ps -q api)
if [ -n "$previous_container" ]; then
  previous_image=$(docker inspect "$previous_container" --format '{{.Config.Image}}')
  printf 'Previous running API image: %s\n' "$previous_image"
fi
backup=$(mktemp .deploy-env.XXXXXX)
chmod 600 "$backup"
cp -p .env "$backup"
chmod 600 "$backup"
cleanup() {
  result=$?
  trap - EXIT
  if [ "$result" -ne 0 ]; then
    if ! cp -p "$backup" .env; then echo 'ERROR: Could not restore deployment environment' >&2; fi
    echo 'Deployment failed. Environment restored; a replaced container requires separate rollback.' >&2
  fi
  rm -f "$backup"
  exit "$result"
}
trap cleanup EXIT
python3 - <<'PY'
import os
from pathlib import Path

path = Path('.env')
lines = path.read_text().splitlines()
names = ['GAME_GEMINI_API_KEY', 'GAME_GEMINI_RPM', 'GAME_GEMINI_RPD'] + [f'GEMINI_KEY_{i}' for i in range(1, 11)]
retired_names = ['OPENAI_API_KEY', 'GAME_OPENAI_MODEL', 'GAME_OPENAI_RPM', 'GAME_OPENAI_RPD'] + [f'OPENAI_API_KEY_{i}' for i in range(1, 11)]
values = {name: os.environ.get(name, '') for name in names}
key_names = ['GAME_GEMINI_API_KEY'] + [f'GEMINI_KEY_{i}' for i in range(1, 11)]
keys = [values[name] for name in key_names if values[name]]
if not keys:
    raise SystemExit('No server-owned Gemini game AI key is configured')
if any(any(ord(c) < 32 or ord(c) == 127 for c in value) for value in keys):
    raise SystemExit('Invalid game AI key format')
for name, default, maximum in [('GAME_GEMINI_RPM', '3', 1000), ('GAME_GEMINI_RPD', '15', 100000)]:
    raw = values[name] or default
    if not raw.isdigit() or int(raw) < 1:
        raise SystemExit(f'Invalid positive integer rate limit: {name}')
    if int(raw) > maximum:
        raise SystemExit(f'Rate limit too high: {name} (maximum {maximum})')
    values[name] = raw
managed = set(names + retired_names)
updated = []
for line in lines:
    if not any(line.startswith(name + '=') for name in managed):
        updated.append(line)
for name in names:
    value = values[name]
    encoded = "'" + value.replace('\\', '\\\\').replace("'", "\\'") + "'"
    updated.append(f'{name}={encoded}')
path.write_text('\n'.join(updated) + '\n')
path.chmod(0o600)
PY
if grep -q '^IMAGE_TAG=' .env; then
  sed -i -E "s|^IMAGE_TAG=.*|IMAGE_TAG=sha-$RELEASE_SHA|" .env
else
  printf '\nIMAGE_TAG=sha-%s\n' "$RELEASE_SHA" >> .env
fi
config=$(docker compose config --format json)
resolved=$(printf '%s' "$config" | python3 -c 'import json,sys; print(json.load(sys.stdin)["services"]["api"]["image"])')
resolved_ai=$(printf '%s' "$config" | python3 -c 'import json,sys; e=json.load(sys.stdin)["services"]["api"]["environment"]; names=["Entertainment__Gemini__ApiKey"]+[f"Entertainment__Gemini__Keys__{i}" for i in range(1,11)]; sys.exit(0 if any(e.get(n) for n in names) else 1)' && echo yes || echo no)
unset config
[ "$resolved" = "$expected" ] || { echo 'Resolved API image differs from release' >&2; exit 1; }
[ "$resolved_ai" = yes ] || { echo 'Resolved game AI key pool is empty' >&2; exit 1; }
unset resolved_ai
printf '%s' "$REGISTRY_TOKEN" | docker login ghcr.io -u "$REGISTRY_USER" --password-stdin
docker compose pull api
docker compose up -d --no-deps api
release_container=$(docker compose ps -q api)
[ -n "$release_container" ] || { echo 'API container missing' >&2; exit 1; }
expected_id=$(docker image inspect "$expected" --format '{{.Id}}')
verify_container() {
  [ "$(docker compose ps -q api)" = "$release_container" ] &&
  [ "$(docker inspect "$release_container" --format '{{.Config.Image}}')" = "$expected" ] &&
  [ "$(docker inspect "$release_container" --format '{{.Image}}')" = "$expected_id" ] &&
  [ "$(docker inspect "$release_container" --format '{{.State.Running}}')" = true ]
}
verify_container || { echo 'API release identity/running-state check failed' >&2; exit 1; }
attempt=0
healthy=false
while [ "$attempt" -lt 30 ]; do
  attempt=$((attempt + 1))
  if response=$(curl --connect-timeout 2 --max-time 5 -fsS http://127.0.0.1:8090/healthz); then
    if printf '%s' "$response" |
      python3 -c 'import json,sys; data=json.load(sys.stdin); sys.exit(0 if isinstance(data,dict) and data.get("status")=="ok" else 1)' 2>/dev/null; then
      healthy=true
      break
    fi
  fi
  if [ "$attempt" -lt 30 ]; then sleep 2; fi
done
[ "$healthy" = true ] || { echo 'API health verification failed' >&2; exit 1; }
verify_container || { echo 'API changed or stopped after health verification' >&2; exit 1; }
printf 'Deployment verified: %s\n' "$expected"

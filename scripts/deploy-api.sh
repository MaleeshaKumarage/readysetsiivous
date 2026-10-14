#!/bin/sh
set -eu
case "${RELEASE_SHA:-}" in
  *[!0-9a-fA-F]*|'') echo 'Invalid release SHA' >&2; exit 1 ;;
esac
[ "${#RELEASE_SHA}" -eq 40 ] || { echo 'Invalid release SHA' >&2; exit 1; }
: "${REGISTRY_USER:?Registry user required}"
: "${REGISTRY_TOKEN:?Registry token required}"
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
if grep -q '^IMAGE_TAG=' .env; then
  sed -i -E "s|^IMAGE_TAG=.*|IMAGE_TAG=sha-$RELEASE_SHA|" .env
else
  printf '\nIMAGE_TAG=sha-%s\n' "$RELEASE_SHA" >> .env
fi
resolved=$(docker compose config --images api)
[ "$resolved" = "$expected" ] || { echo 'Resolved API image differs from release' >&2; exit 1; }
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

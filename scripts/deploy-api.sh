#!/bin/sh
# Executed from the exact selected checkout; staged helper bytes must match its authenticated manifest.
set -eu
case "${RELEASE_SHA:-}" in *[!0-9a-f]*|'') echo 'Invalid release SHA' >&2; exit 1;; esac
[ "${#RELEASE_SHA}" -eq 40 ] || exit 1
export RELEASE_SHA
exec python3 - <<'PY'
import hashlib, json, os, pathlib, re, runpy
root = pathlib.Path(os.environ.get('DEPLOY_DIR', '/home/ubuntu/cleaning-suite')).resolve()
stage = root / 'releases' / os.environ['RELEASE_SHA']
expected = os.environ.get('RELEASE_MANIFEST_HASH', '')
if not re.fullmatch('[0-9a-f]{64}', expected):
    raise SystemExit('Missing authenticated release manifest hash')
raw = (stage / 'release.json').read_bytes()
if len(raw) > 16384 or hashlib.sha256(raw).hexdigest() != expected:
    raise SystemExit('Staged manifest mismatch')
manifest = json.loads(raw)
paths = ('api/deploy/docker-compose.yml', 'api/deploy/release-policy.json', 'api/deploy/check-keycloak-runtime-env.py', 'scripts/deploy-api.sh', 'scripts/release_deploy.py')
if manifest.get('release_sha') != os.environ['RELEASE_SHA'] or set(manifest.get('assets', {})) != set(paths):
    raise SystemExit('Staged release binding mismatch')
for name in paths:
    path = stage / name
    if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != manifest['assets'][name]:
        raise SystemExit('Staged asset mismatch')
os.environ['RELEASE_STAGE'] = str(stage)
runpy.run_path(str(stage / 'scripts/release_deploy.py'), run_name='__main__')
PY

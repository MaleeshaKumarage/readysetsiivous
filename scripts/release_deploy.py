"""Release-bound API replacement; application rollback never reverses database changes.

A maintenance-gated identity release additionally requires external coordination
of Keycloak database/image recovery. Restoring an API artifact is not proof that
its identity-role model is compatible with the currently migrated Keycloak data.
The external coordinator owns ingress stop/start and identity backup/restore.

Compose project/file/env and no-dependency contracts:
https://docs.docker.com/reference/cli/docker/compose/
https://docs.docker.com/reference/cli/docker/compose/up/
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time

IMAGE = 'ghcr.io/maleeshakumarage/readysetsiivous-api'
SHA = re.compile('[0-9a-f]{40}')
IMMUTABLE = re.compile(re.escape(IMAGE) + '@sha256:[0-9a-f]{64}')
MAINTENANCE_SERVICE = 'cloudflared.service'


def require(value, message):
    if not value:
        raise RuntimeError(message)


def atomic(path, raw):
    fd, name = tempfile.mkstemp(prefix='.deploy-write-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as output:
            output.write(raw)
            output.flush()
            os.fsync(output.fileno())
        os.chmod(name, 0o600)
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


class Deployment:
    def __init__(self, root, stage, environment):
        self.root, self.stage, self.input = root, stage, environment
        # Compose interpolation must come from the selected env file, never inherited new secrets.
        # The production host uses its local Docker daemon, not an ambient remote context.
        self.env = {'PATH': environment.get('PATH', os.defpath), 'HOME': environment.get('HOME', str(Path.home()))}
        self.project = None
        self.timeout = 30
        self.maintenance_service = None

    def command(self, *args, stdin=None):
        if args[0] == 'docker':
            # Explicit CLI host wins over HOME/.docker/config currentContext on every call,
            # including preflight before the isolated registry-auth directory exists.
            args = ('docker', '--host', 'unix:///var/run/docker.sock', *args[1:])
        result = subprocess.run(args, input=stdin, text=True, capture_output=True, env=self.env,
                                cwd=self.root, timeout=120)
        require(result.returncode == 0, 'Deployment command failed: ' + args[0])
        return result.stdout.strip()

    def compose(self, config, env, *args, override=None):
        command = ['docker', 'compose', '--project-directory', str(self.root), '-f', str(config), '--env-file', str(env)]
        if self.project:
            command += ['-p', self.project]
        if override:
            command += ['-f', str(override)]
        return self.command(*command, *args)

    def health(self, release, attempts=30):
        for attempt in range(attempts):
            try:
                raw = self.command('curl', '--connect-timeout', '2', '--max-time', '5', '-fsS', 'http://127.0.0.1:8090/healthz')
                value = json.loads(raw)
                if isinstance(value, dict) and value.get('status') == 'ok' and value.get('releaseSha') == release:
                    return
            except (RuntimeError, ValueError, subprocess.TimeoutExpired):
                pass
            if attempt + 1 < attempts:
                self.command('sleep', '2')
        raise RuntimeError('API release health verification failed')

    def inspect(self, kind, value):
        args = ['docker'] + (['image'] if kind == 'image' else []) + ['inspect', value]
        result = json.loads(self.command(*args))
        require(isinstance(result, list) and len(result) == 1, 'Docker inspection failed')
        return result[0]

    def maintenance_gate(self):
        if self.maintenance_service is not None:
            require(self.maintenance_service == MAINTENANCE_SERVICE, 'Unsupported maintenance service in release policy')
            raw = self.command('systemctl', 'show', '--property=ActiveState', '--property=LoadState', self.maintenance_service)
            lines = raw.splitlines()
            # Missing units can report ActiveState=inactive. Require an actual
            # loaded unit as well; reject missing, duplicate or unexpected fields.
            details = dict(line.split('=', 1) for line in lines) if all('=' in line for line in lines) else {}
            require(len(lines) == 2 and details == {'ActiveState': 'inactive', 'LoadState': 'loaded'},
                    'Maintenance ingress must remain inactive with a loaded unit; external coordination is required')

    def verify(self, image, image_id, release):
        container = self.compose(self.root / 'docker-compose.yml', self.root / '.env', 'ps', '-q', 'api')
        require(container and '\n' not in container, 'API container missing or ambiguous')
        actual = self.inspect('container', container)
        require(actual['Config']['Image'] == image and actual['Image'] == image_id
                and actual['State']['Running'] is True, 'API image identity/running state mismatch')
        self.health(release)
        require(self.compose(self.root / 'docker-compose.yml', self.root / '.env', 'ps', '-q', 'api') == container,
                'API container changed during verification')
        actual = self.inspect('container', container)
        require(actual['Image'] == image_id and actual['State']['Running'] is True, 'API stopped or changed after health')

    def new_env(self, previous, image):
        names = ['GAME_GEMINI_API_KEY', 'GAME_GEMINI_RPM', 'GAME_GEMINI_RPD'] + [f'GEMINI_KEY_{i}' for i in range(1, 11)]
        retired = ['OPENAI_API_KEY', 'GAME_OPENAI_MODEL', 'GAME_OPENAI_RPM', 'GAME_OPENAI_RPD', 'IMAGE_TAG'] + [f'OPENAI_API_KEY_{i}' for i in range(1, 11)]
        values = {name: self.input.get(name, '') for name in names}
        require(any(values[n] for n in ['GAME_GEMINI_API_KEY'] + [f'GEMINI_KEY_{i}' for i in range(1, 11)]), 'No server-owned Gemini game AI key configured')
        require(not any(any(ord(c) < 32 or ord(c) == 127 for c in v) for v in values.values()), 'Invalid deployment value')
        for name, default, maximum in [('GAME_GEMINI_RPM', '3', 1000), ('GAME_GEMINI_RPD', '15', 100000)]:
            raw = values[name] or default
            require(raw.isascii() and raw.isdigit() and 1 <= int(raw) <= maximum, 'Invalid game rate limit')
            values[name] = raw
        values['API_IMAGE'] = image
        managed = set(names + retired + ['API_IMAGE'])
        kept = [line for line in previous.decode().splitlines() if not any(line.startswith(n + '=') for n in managed)]
        for name, value in values.items():
            kept.append(name + "='" + value.replace('\\', '\\\\').replace("'", "\\'") + "'")
        return ('\n'.join(kept) + '\n').encode()

    def run(self):
        manifest = json.loads((self.stage / 'release.json').read_bytes())
        release, image = manifest.get('release_sha', ''), manifest.get('image', '')
        require(SHA.fullmatch(release) and IMMUTABLE.fullmatch(image), 'Invalid release image')
        policy = json.loads((self.stage / 'api/deploy/release-policy.json').read_bytes())
        require(policy.get('schema') == 1 and policy.get('approved') is True
                and policy.get('schema_changes') == 'backward-compatible-reviewed'
                and type(policy.get('rollback_compatible_from')) is list
                and all(isinstance(s, str) and SHA.fullmatch(s) for s in policy['rollback_compatible_from']),
                'Release compatibility approval required before deployment')
        if 'maintenance_service' in policy:
            require(policy['maintenance_service'] == MAINTENANCE_SERVICE, 'Unsupported maintenance service in release policy')
            self.maintenance_service = MAINTENANCE_SERVICE
        # Read-only systemd proof precedes host file creation, chmod, login, pull,
        # or Compose mutation. No value can select an arbitrary unit/command.
        self.maintenance_gate()
        active_config, active_env = self.root / 'docker-compose.yml', self.root / '.env'
        require(active_config.is_file() and not active_config.is_symlink() and active_env.is_file() and not active_env.is_symlink(), 'Existing deployment inputs required')
        old_config, old_env = active_config.read_bytes(), active_env.read_bytes()
        # This path comes only from the protected host env file; inherited workflow values
        # cannot select a different identity store. Require an explicit canonical unquoted path.
        identity_paths = re.findall(r'^API_IDENTITY_ENV_FILE=(.*)$', old_env.decode(), re.MULTILINE)
        require(len(identity_paths) == 1 and re.fullmatch(r"/[^\s'\"$#]+", identity_paths[0]),
                'Set one canonical unquoted absolute API_IDENTITY_ENV_FILE in the protected host env file')
        identity_checker = self.stage / 'api/deploy/check-keycloak-runtime-env.py'
        # Checker bytes are an authenticated release asset; it enforces permissions,
        # canonical host path, outside-checkout location and complete tenant account pairs.
        self.command('python3', str(identity_checker), '--file', identity_paths[0])
        container = self.compose(active_config, active_env, 'ps', '-q', 'api')
        require(container and '\n' not in container, 'Existing API required for verified rollback')
        previous = self.inspect('container', container)
        self.project = previous.get('Config', {}).get('Labels', {}).get('com.docker.compose.project', '')
        require(re.fullmatch('[a-z0-9][a-z0-9_-]{0,62}', self.project), 'Stable Compose project identity required')
        old_release = next((s.split('=', 1)[1] for s in previous['Config']['Env'] if s.startswith('APP_RELEASE_SHA=')), '')
        require(SHA.fullmatch(old_release) and old_release in policy['rollback_compatible_from'], 'Previous release not approved for application rollback')
        previous_running = previous['State']['Running']
        require(previous_running is True or (self.maintenance_service == MAINTENANCE_SERVICE and previous_running is False),
                'Previous API is not running')
        old_id = previous['Image']
        old_metadata = self.inspect('image', old_id)
        old_image = next((s for s in old_metadata.get('RepoDigests', []) if IMMUTABLE.fullmatch(s)), '')
        require(old_metadata.get('Id') == old_id and old_image and self.inspect('image', old_image).get('Id') == old_id, 'Previous immutable image unavailable')
        # The coordinated identity migration stops this exact old API before
        # converting role data. Do not restart it merely to prove health against
        # the converted model: the coordinator proved old health before stopping.
        # Only the explicit closed-ingress policy allows this stopped baseline;
        # its exact release, image ID and immutable digest still remain required.
        if previous_running:
            self.health(old_release, attempts=1)

        # Backups contain credentials: private, host-only, retained for operator recovery.
        backups = self.root / '.deploy-backups'
        backups.mkdir(mode=0o700, exist_ok=True)
        os.chmod(backups, 0o700)
        backup = Path(tempfile.mkdtemp(prefix=release[:12] + '-', dir=backups))
        atomic(backup / '.env', old_env)
        atomic(backup / 'docker-compose.yml', old_config)
        atomic(backup / 'image.json', json.dumps({'services': {'api': {'image': old_image}}}).encode())
        atomic(backup / 'previous.json', json.dumps({'release_sha': old_release, 'image': old_image, 'image_id': old_id, 'project': self.project}).encode())
        os.chmod(self.stage, 0o700)
        atomic(self.stage / 'candidate.env', self.new_env(old_env, image))
        candidate = self.stage / 'api/deploy/docker-compose.yml'
        config = json.loads(self.compose(candidate, self.stage / 'candidate.env', 'config', '--format', 'json'))
        require(config.get('services', {}).get('api', {}).get('image') == image, 'Resolved image differs from immutable release')
        require(self.input.get('REGISTRY_USER') and self.input.get('REGISTRY_TOKEN'), 'Registry authentication required')
        replaced = False
        with tempfile.TemporaryDirectory(prefix='.deploy-auth-', dir=self.root) as auth:
            self.env['DOCKER_CONFIG'] = auth
            try:
                self.command('docker', 'login', 'ghcr.io', '-u', self.input['REGISTRY_USER'], '--password-stdin', stdin=self.input['REGISTRY_TOKEN'])
                self.command('docker', 'pull', image)
                new_metadata = self.inspect('image', image)
                new_id = new_metadata['Id']
                require('APP_RELEASE_SHA=' + release in new_metadata['Config']['Env'], 'Pulled image release differs from manifest')
                self.maintenance_gate()
                # This flag precedes both file replacement and compose up; even a partial up is recovered.
                replaced = True
                atomic(active_env, (self.stage / 'candidate.env').read_bytes())
                atomic(active_config, candidate.read_bytes())
                self.compose(active_config, active_env, 'up', '-d', '--no-deps', '--pull', 'never', 'api')
                self.verify(image, new_id, release)
                self.maintenance_gate()
            except BaseException:
                if replaced:
                    try:
                        # Do not expose the old API against converted identity
                        # data if ingress reopened. The coordinator must restore
                        # identity state before it reopens public traffic.
                        self.maintenance_gate()
                        atomic(active_env, old_env)
                        atomic(active_config, old_config)
                        self.compose(active_config, active_env, 'up', '-d', '--no-deps', '--pull', 'never', 'api', override=backup / 'image.json')
                        restored_container = self.compose(active_config, active_env, 'ps', '-q', 'api', override=backup / 'image.json')
                        actual = self.inspect('container', restored_container)
                        require(actual['Image'] == old_id and actual['Config']['Image'] == old_image and actual['State']['Running'] is True, 'Rollback image mismatch')
                        self.health(old_release)
                        require(self.compose(active_config, active_env, 'ps', '-q', 'api', override=backup / 'image.json') == restored_container, 'Rollback container changed')
                        final = self.inspect('container', restored_container)
                        require(final['Image'] == old_id and final['State']['Running'] is True, 'Rollback stopped after health')
                        self.maintenance_gate()
                        message = 'Deployment failed; previous API image and configuration restored and verified.'
                        if self.maintenance_service:
                            message += ' Identity recovery remains external; maintenance ingress must stay inactive.'
                        print(message, file=sys.stderr)
                    except BaseException:
                        raise RuntimeError('Deployment and rollback failed; private recovery inputs retained at ' + str(backup)) from None
                raise
            finally:
                self.env.pop('DOCKER_CONFIG', None)
        print('Deployment verified: ' + image)


def main():
    for name in ('SIGTERM', 'SIGHUP'):
        if hasattr(signal, name):
            signal.signal(getattr(signal, name), lambda *_: (_ for _ in ()).throw(RuntimeError('Deployment interrupted')))
    try:
        root = Path(os.environ.get('DEPLOY_DIR', '/home/ubuntu/cleaning-suite')).resolve()
        Deployment(root, Path(os.environ['RELEASE_STAGE']), os.environ).run()
    except BaseException as error:
        # Never print command output, resolved configuration, credentials or a traceback.
        message = str(error) if isinstance(error, RuntimeError) else 'Deployment failed; inspect private recovery state.'
        print(message, file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

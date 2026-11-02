"""Run image/cache retention around an existing dev deploy-and-health command."""
import argparse
from http.client import HTTPConnection
import json
from pathlib import Path
import re
import subprocess
import time
from urllib.parse import urlsplit

from release_cleanup import IMAGE_ID, REPOSITORIES, ReleaseCleanup, docker


def run(root, container, candidate, health_url, command, minimum_free_gib=6, project='cleaning-dev'):
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}', container):
        raise RuntimeError('Explicit dev API container name required')
    references = '|'.join(re.escape(repo) for repo in REPOSITORIES)
    if not re.fullmatch(f'(?:{references})(?::[A-Za-z0-9_][A-Za-z0-9_.-]{{0,127}}|@sha256:[0-9a-f]{{64}})', candidate):
        raise RuntimeError('Candidate must be a ReadySetSiivous API image')
    url = urlsplit(health_url)
    if url.scheme != 'http' or url.hostname != '127.0.0.1' or url.username or url.password or url.fragment:
        raise RuntimeError('Use a private 127.0.0.1 HTTP health endpoint')
    if not command or not 1 <= minimum_free_gib <= 1024:
        raise RuntimeError('Deployment command and valid free-space threshold required')
    cleanup = ReleaseCleanup(root)
    with cleanup.lock():
        before = cleanup.inspect('container', container)
        labels = before['Config'].get('Labels', {})
        if (labels.get('com.docker.compose.project') != project or project == 'cleaning-suite'
                or labels.get('com.docker.compose.service') != 'api' or not before['State']['Running']):
            raise RuntimeError('Existing running dev Compose API required')
        previous_id = before['Image']
        if not IMAGE_ID.fullmatch(previous_id):
            raise RuntimeError('Invalid dev API image identity')
        protected = [previous_id]
        try:
            protected.append(cleanup.inspect('image', candidate)['Id'])
        except RuntimeError:
            pass  # Builds/pulls performed by the command may create it later.
        cleanup.clean(protected, require_space=True, minimum_free=minimum_free_gib * 1024**3)
        cleanup.retain_rollback(previous_id)
        # No shell interpolation; do not expose command output or credential arguments.
        result = subprocess.run(command, cwd=Path(root), capture_output=True, timeout=1800)
        if result.returncode:
            raise RuntimeError('Dev deployment command failed; retained previous image, inspect deployment state')
        image = cleanup.inspect('image', candidate)
        expected = image['Id']
        if not IMAGE_ID.fullmatch(expected):
            raise RuntimeError('Invalid candidate image identity')
        release = next((value.split('=', 1)[1] for value in image['Config'].get('Env', [])
                        if value.startswith('APP_RELEASE_SHA=')), '')
        if release and not re.fullmatch('[0-9a-f]{40}', release):
            raise RuntimeError('Invalid candidate release SHA')

        def verify_container():
            current = cleanup.inspect('container', container)
            actual = current['Config'].get('Labels', {})
            if (not current['State']['Running'] or current['Image'] != expected
                    or actual.get('com.docker.compose.project') != project
                    or actual.get('com.docker.compose.service') != 'api'):
                raise RuntimeError('Dev API candidate/project verification failed')
            ports = current.get('NetworkSettings', {}).get('Ports', {})
            bindings = [binding for key, values in ports.items() if key.endswith('/tcp') for binding in (values or [])]
            if not any(binding.get('HostPort') == str(url.port or 80)
                       and binding.get('HostIp') in ('127.0.0.1', '0.0.0.0', '::', '::1', '') for binding in bindings):
                raise RuntimeError('Dev health endpoint does not match the candidate container port')

        verify_container()
        healthy = False
        for _ in range(30):
            try:
                connection = HTTPConnection('127.0.0.1', url.port or 80, timeout=5)
                try:
                    connection.request('GET', (url.path or '/') + ('?' + url.query if url.query else ''))
                    response = connection.getresponse()
                    if response.status != 200:
                        raise ValueError()
                    raw = response.read(16385)
                    if len(raw) > 16384:
                        raise ValueError()
                    health = json.loads(raw)
                finally:
                    connection.close()
                healthy = health.get('status') == 'ok' and (not release or health.get('releaseSha') == release)
            except Exception:
                healthy = False
            if healthy:
                break
            time.sleep(2)
        if not healthy:
            raise RuntimeError('Dev API health verification failed; previous image retained')
        verify_container()
        try:
            cleanup.record_success(expected)
            cleanup.clean([previous_id, expected])
        except Exception:
            print('Warning: post-release cleanup incomplete; healthy dev API retained')
        print('Dev release and storage hooks verified')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', required=True, type=Path)
    parser.add_argument('--container', required=True)
    parser.add_argument('--candidate-image', required=True)
    parser.add_argument('--health-url', required=True)
    parser.add_argument('--project', default='cleaning-dev')
    parser.add_argument('--minimum-free-gib', type=int, default=6)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    run(args.root, args.container, args.candidate_image, args.health_url, command, args.minimum_free_gib, args.project)


if __name__ == '__main__':
    try:
        main()
    except RuntimeError as error:
        raise SystemExit(str(error)) from None

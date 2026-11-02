"""Release-bound retention; never removes containers, volumes or application files."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

REPOSITORIES = ('ghcr.io/maleeshakumarage/readysetsiivous-api', 'cleaning-dev-api')
IMAGE_ID = re.compile(r'sha256:[0-9a-f]{64}')


def docker(*args):
    result = subprocess.run(['docker', '--host', 'unix:///var/run/docker.sock', *args],
                            capture_output=True, text=True, timeout=300)
    if result.returncode:
        raise RuntimeError('Docker cleanup command failed; no application data was removed')
    return result.stdout.strip()


class ReleaseCleanup:
    def __init__(self, root, command=docker, *, now=None, disk_usage=None):
        self.root = Path(root).resolve()
        self.command = command
        self.now = now or (lambda: datetime.now(timezone.utc))
        self.disk_usage = disk_usage or shutil.disk_usage
        self.state = self.root / '.release-image-retention.json'

    @contextmanager
    def lock(self):
        import fcntl
        path = self.root / '.release-image-cleanup.lock'
        fd = os.open(path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
        try:
            try:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise RuntimeError('Another release is using this cleanup history') from None
            yield
        finally:
            os.close(fd)

    def history(self):
        if not self.state.exists():
            if self.state.is_symlink():
                raise RuntimeError('Invalid image retention history')
            return None
        if self.state.is_symlink() or not self.state.is_file() or self.state.stat().st_size > 262144:
            raise RuntimeError('Invalid image retention history')
        try:
            data = json.loads(self.state.read_text())
            valid = (type(data) is dict and set(data) == {'schema', 'recent', 'known'} and data['schema'] == 1
                     and all(type(data[k]) is list and len(data[k]) <= 2000
                             and all(type(i) is str and IMAGE_ID.fullmatch(i) for i in data[k])
                             and len(set(data[k])) == len(data[k]) for k in ('recent', 'known'))
                     and len(data['recent']) <= 4 and set(data['recent']).issubset(data['known']))
            if not valid:
                raise ValueError()
            return data
        except (ValueError, TypeError, KeyError):
            raise RuntimeError('Invalid image retention history; cleanup stopped') from None

    def save(self, data):
        fd, name = tempfile.mkstemp(prefix='.image-retention-', dir=self.root)
        try:
            with os.fdopen(fd, 'w') as output:
                json.dump(data, output)
                output.flush()
                os.fsync(output.fileno())
            os.replace(name, self.state)
        finally:
            if os.path.exists(name):
                os.unlink(name)

    def inspect(self, kind, value):
        result = json.loads(self.command(kind, 'inspect', value))
        if type(result) is not list or len(result) != 1:
            raise RuntimeError('Ambiguous Docker cleanup inventory')
        return result[0]

    def inventory(self):
        containers = self.command('ps', '--all', '--quiet', '--no-trunc').split()
        protected = {self.inspect('container', item)['Image'] for item in containers}
        ids = set(self.command('image', 'ls', '--quiet', '--no-trunc').split())
        if any(not IMAGE_ID.fullmatch(item) for item in ids | protected):
            raise RuntimeError('Invalid Docker image identity')
        images = []
        for item in sorted(ids):
            image = self.inspect('image', item)
            if image.get('Id') != item:
                raise RuntimeError('Docker image identity changed during inventory')
            refs = (image.get('RepoTags') or []) + (image.get('RepoDigests') or [])
            if any(type(ref) is not str for ref in refs):
                raise RuntimeError('Invalid Docker image references')
            image['_allowed'] = bool(refs) and all(
                any(ref.startswith(repo + ':') or ref.startswith(repo + '@') for repo in REPOSITORIES)
                for ref in refs)
            image['_foreign'] = bool(refs) and not image['_allowed']
            images.append(image)
        return images, protected

    def stale_cache(self, minimum_free=0):
        help_text = self.command('builder', 'prune', '--help')
        option = '--max-used-space' if '--max-used-space' in help_text else '--keep-storage'
        if option not in help_text:
            raise RuntimeError('Docker client has no supported cache retention option')
        builder = ['--builder', 'default'] if '--builder' in help_text else []
        space = ['--min-free-space', str(minimum_free)] if minimum_free and '--min-free-space' in help_text else []
        self.command('builder', 'prune', *builder, '--force', '--filter', 'until=168h', option, '4GB', *space)

    def clean(self, protected=(), *, dry_run=False, require_space=False, minimum_free=6 * 1024**3):
        history = self.history()
        images, container_images = self.inventory()
        by_id = {image['Id']: image for image in images}
        protected = set(protected) | container_images
        if any(not IMAGE_ID.fullmatch(item) for item in protected):
            raise RuntimeError('Invalid protected image identity')
        owned = {image['Id'] for image in images if image['_allowed']}
        bootstrap = history is None
        if bootstrap:
            recent = sorted((image for image in images if image['_allowed']),
                            key=lambda image: image['Created'], reverse=True)[:4]
            history = {'schema': 1, 'recent': [image['Id'] for image in recent], 'known': sorted(owned)}
        protected.update(history['recent'])
        known = owned | set(history['known'])
        candidates = []
        for image in images:
            if bootstrap or image['Id'] not in known or image['Id'] in protected or image['_foreign']:
                continue
            try:
                # Docker emits nanoseconds; Ubuntu's Python 3.10 accepts only
                # microseconds in fromisoformat. Round creation upwards to keep
                # the 24-hour safety margin conservative at the boundary.
                stamp = re.sub(r'(\.\d{6})\d+(?=Z|[+-]|$)', r'\1', image['Created'])
                created = datetime.fromisoformat(stamp.replace('Z', '+00:00'))
                if stamp != image['Created']:
                    created += timedelta(microseconds=1)
                if created.tzinfo is None:
                    raise ValueError()
            except (ValueError, KeyError, TypeError, OverflowError):
                raise RuntimeError('Invalid image age; cleanup stopped') from None
            if created < self.now() - timedelta(hours=24):
                candidates.append(image['Id'])
        print(f'API image cleanup: {len(protected)} protected, {len(candidates)} obsolete, bootstrap={bootstrap}, dry_run={dry_run}')
        if dry_run:
            return candidates
        # Persist the conservative bootstrap before mutations. Only successful release
        # verification advances recent history; dangling dev image IDs remain known.
        history['recent'] = [item for item in history['recent'] if item in by_id]
        history['known'] = sorted(known & by_id.keys())
        self.save(history)
        for item in candidates:
            try:
                # No --force: Docker refuses images acquired by a container in a race.
                self.command('image', 'rm', item)
            except RuntimeError:
                print('API image removal refused; retained conservatively')
        try:
            self.stale_cache(minimum_free if require_space else 0)
        except RuntimeError:
            print('Warning: stale build-cache cleanup unavailable')
        storage = Path(self.command('info', '--format', '{{.DockerRootDir}}'))
        if not storage.is_absolute():
            raise RuntimeError('Invalid Docker storage path')
        free = self.disk_usage(storage).free
        print(f'Docker storage free: {free / 1024**3:.1f} GiB')
        if require_space and free < minimum_free:
            raise RuntimeError(f'Insufficient Docker storage after safe cleanup: need {minimum_free / 1024**3:.1f} GiB; live API unchanged')
        return candidates

    def record_success(self, image_id):
        if not IMAGE_ID.fullmatch(image_id):
            raise RuntimeError('Invalid successful image identity')
        data = self.history() or {'schema': 1, 'recent': [], 'known': []}
        data['recent'] = list(dict.fromkeys([image_id, *data['recent']]))[:4]
        data['known'] = sorted(set(data['known']) | {image_id})
        self.save(data)

    def retain_rollback(self, image_id):
        # An environment may have been deployed by an older controller without
        # retention hooks. Remember its actual rollback artifact before replacing it.
        self.record_success(image_id)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--apply', action='store_true', help='Default is a non-mutating image plan')
    parser.add_argument('--check-space', action='store_true', help='Enforce pre-release Docker storage headroom')
    parser.add_argument('--minimum-free-gib', type=int, default=6)
    args = parser.parse_args()
    if not 1 <= args.minimum_free_gib <= 1024 or (args.check_space and not args.apply):
        parser.error('Space checks require --apply and a threshold of 1–1024 GiB')
    cleanup = ReleaseCleanup(args.root)
    with cleanup.lock():
        cleanup.clean(dry_run=not args.apply, require_space=args.check_space,
                      minimum_free=args.minimum_free_gib * 1024**3)


if __name__ == '__main__':
    try:
        main()
    except RuntimeError as error:
        raise SystemExit(str(error)) from None

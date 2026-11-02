"""Retention safety checked without modifying a real Docker daemon."""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_cleanup import ReleaseCleanup


def identity(number):
    return 'sha256:' + f'{number:064x}'


class Docker:
    def __init__(self):
        self.calls = []
        self.images = {identity(n): {'Id': identity(n), 'Created': f'2026-10-{n:02d}T00:00:00Z',
                                    'RepoTags': [f'cleaning-dev-api:release-{n}'], 'RepoDigests': []}
                       for n in range(1, 9)}
        self.images[identity(20)] = {'Id': identity(20), 'Created': '2026-01-01T00:00:00Z',
                                     'RepoTags': ['postgres:16'], 'RepoDigests': []}
        self.containers = {'running': identity(8), 'stopped': identity(1)}
        self.refused = set()
        self.cache_help = '--max-used-space --builder'

    def __call__(self, *args):
        self.calls.append(args)
        if args[0] == 'ps': return '\n'.join(self.containers)
        if args[:2] == ('container', 'inspect'): return json.dumps([{'Image': self.containers[args[2]]}])
        if args[:2] == ('image', 'ls'): return '\n'.join(self.images)
        if args[:2] == ('image', 'inspect'): return json.dumps([self.images[args[2]]])
        if args[:2] == ('image', 'rm'):
            if args[2] in self.refused: raise RuntimeError('race refusal')
            del self.images[args[2]]
            return ''
        if args[:2] == ('builder', 'prune'): return self.cache_help if '--help' in args else ''
        if args[0] == 'info': return str(Path(tempfile.gettempdir()).resolve())
        raise AssertionError(args)


class CleanupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.docker = Docker()
        self.free = 20 * 1024**3
        self.cleanup = ReleaseCleanup(self.root, self.docker,
                                      now=lambda: datetime(2026, 11, 2, tzinfo=timezone.utc),
                                      disk_usage=lambda _: SimpleNamespace(free=self.free))

    def seed(self):
        self.cleanup.save({'schema': 1, 'recent': [identity(n) for n in (8, 7, 6, 5)],
                           'known': [identity(n) for n in range(1, 9)]})

    def test_bootstrap_and_dry_run_do_not_remove_images_or_cache(self):
        self.assertEqual([], self.cleanup.clean(dry_run=True))
        self.assertFalse(self.cleanup.state.exists())
        self.assertFalse(any(call[:2] == ('builder', 'prune') for call in self.docker.calls))
        self.cleanup.clean()
        self.assertEqual(9, len(self.docker.images))
        self.assertEqual(4, len(self.cleanup.history()['recent']))

    def test_keeps_running_stopped_current_and_three_recent_and_explicit_rollback(self):
        self.seed()
        self.cleanup.clean([identity(2)], require_space=True)
        removed = {call[2] for call in self.docker.calls if call[:2] == ('image', 'rm')}
        self.assertEqual({identity(3), identity(4)}, removed)
        self.assertIn(identity(20), self.docker.images)
        self.assertFalse(any('--force' in call for call in self.docker.calls if call[0] == 'image'))

    def test_recent_images_and_mixed_repository_references_are_never_deleted(self):
        self.seed()
        self.docker.images[identity(3)]['Created'] = '2026-11-02T00:00:00Z'
        self.docker.images[identity(4)]['RepoTags'].append('unrelated-api:keep')
        self.cleanup.clean()
        self.assertIn(identity(3), self.docker.images)
        self.assertIn(identity(4), self.docker.images)

    def test_known_dangling_dev_image_can_be_removed_but_unknown_dangling_is_preserved(self):
        self.seed()
        self.docker.images[identity(3)]['RepoTags'] = []
        self.docker.images[identity(20)]['RepoTags'] = []
        self.cleanup.clean()
        self.assertNotIn(identity(3), self.docker.images)
        self.assertIn(identity(20), self.docker.images)

    def test_docker_nanosecond_timestamps_work_on_python_310_release_hosts(self):
        self.seed()
        self.docker.images[identity(3)]['Created'] = '2026-10-03T00:00:00.123456789Z'
        self.cleanup.clean()
        self.assertNotIn(identity(3), self.docker.images)

    def test_malformed_history_prevents_every_docker_mutation(self):
        self.cleanup.state.write_text('{broken')
        with self.assertRaisesRegex(RuntimeError, 'history'):
            self.cleanup.clean()
        self.assertEqual([], self.docker.calls)

    def test_invalid_inventory_prevents_every_deletion(self):
        self.seed()
        self.docker.images[identity(3)]['Created'] = 'not-a-date'
        with self.assertRaisesRegex(RuntimeError, 'age'):
            self.cleanup.clean()
        self.assertFalse(any(call[:2] == ('image', 'rm') for call in self.docker.calls))

    def test_race_refusal_is_preserved_and_cache_is_only_old_unused_layers(self):
        self.seed()
        self.docker.refused.add(identity(3))
        self.cleanup.clean()
        self.assertIn(identity(3), self.docker.images)
        prune = next(call for call in self.docker.calls if call[:2] == ('builder', 'prune') and '--help' not in call)
        self.assertEqual(('builder', 'prune', '--builder', 'default', '--force', '--filter', 'until=168h', '--max-used-space', '4GB'), prune)
        self.assertFalse(any('volume' in call or 'system' in call or 'container' in call and 'rm' in call for call in self.docker.calls))

    def test_free_space_shortfall_has_clear_early_failure(self):
        self.seed()
        self.free = 1024**3
        with self.assertRaisesRegex(RuntimeError, 'Insufficient Docker storage.*live API unchanged'):
            self.cleanup.clean(require_space=True)

    def test_success_history_is_distinct_bounded_and_preserves_previous_release(self):
        self.seed()
        self.cleanup.record_success(identity(9))
        self.cleanup.record_success(identity(9))
        self.assertEqual([identity(n) for n in (9, 8, 7, 6)], self.cleanup.history()['recent'])

    def test_deployment_from_older_controller_retains_actual_rollback_in_recent_history(self):
        self.seed()
        self.cleanup.retain_rollback(identity(10))
        self.cleanup.record_success(identity(11))
        self.assertEqual([identity(n) for n in (11, 10, 8, 7)], self.cleanup.history()['recent'])

    def test_cache_headroom_and_legacy_docker_options_are_supported(self):
        self.docker.cache_help += ' --min-free-space'
        self.cleanup.stale_cache(6 * 1024**3)
        self.assertEqual(('--min-free-space', str(6 * 1024**3)), self.docker.calls[-1][-2:])
        self.docker.cache_help = '--keep-storage'
        self.cleanup.stale_cache()
        self.assertEqual(('--keep-storage', '4GB'), self.docker.calls[-1][-2:])
        self.assertNotIn('--builder', self.docker.calls[-1])

    @unittest.skipUnless(os.name == 'posix', 'POSIX release host locking')
    def test_concurrent_release_lock_and_symlink_lock_fail_closed(self):
        with self.cleanup.lock():
            with self.assertRaisesRegex(RuntimeError, 'Another release'):
                with self.cleanup.lock(): pass
        lock = self.root / '.release-image-cleanup.lock'
        lock.unlink()
        lock.symlink_to(self.root / 'never-touch')
        with self.assertRaises(OSError):
            with self.cleanup.lock(): pass
        self.assertFalse((self.root / 'never-touch').exists())


if __name__ == '__main__':
    unittest.main()

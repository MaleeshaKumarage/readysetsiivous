import contextlib
import json
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dev_release_cleanup as dev

OLD = 'sha256:' + '1' * 64
NEW = 'sha256:' + '2' * 64
SHA = 'a' * 40


class Cleanup:
    def __init__(self, mode):
        self.mode, self.after, self.records, self.cleanups = mode, False, [], []

    def lock(self): return contextlib.nullcontext()

    def inspect(self, kind, _):
        if kind == 'image': return {'Id': NEW, 'Config': {'Env': ['APP_RELEASE_SHA=' + SHA]}}
        return {'Image': (OLD if self.mode == 'wrong-image' else NEW) if self.after else OLD,
                'NetworkSettings': {'Ports': {'8080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '8099' if self.mode == 'wrong-port' else '8090'}]}},
                'State': {'Running': self.mode != 'stopped'},
                'Config': {'Labels': {'com.docker.compose.project': 'wrong' if self.mode == 'wrong-project' else 'cleaning-dev',
                                      'com.docker.compose.service': 'api'}}}

    def clean(self, protected, **kwargs):
        self.cleanups.append((protected, kwargs))
        if self.mode == 'low-space' and kwargs.get('require_space'): raise RuntimeError('Low disk space')
        if self.mode == 'post-failure' and self.after: raise RuntimeError('cleanup unavailable')

    def record_success(self, image): self.records.append(image)
    def retain_rollback(self, image): self.rollback = image


class DevTests(unittest.TestCase):
    def execute(self, mode='success', **overrides):
        cleanup = Cleanup(mode)
        commands, hosts = [], []

        def command(args, **kwargs):
            self.assertIsInstance(args, list)
            self.assertNotIn('shell', kwargs)
            self.assertTrue(kwargs['capture_output'])
            commands.append(args)
            cleanup.after = True
            return SimpleNamespace(returncode=1 if mode == 'command-failure' else 0)

        def connection(host, port, timeout):
            hosts.append((host, port))
            health = {'status': 'ok', 'releaseSha': 'wrong' if mode == 'wrong-health' else SHA}
            return SimpleNamespace(request=lambda *a: None, close=lambda: None,
                getresponse=lambda: SimpleNamespace(status=302 if mode == 'redirect' else 200,
                                                    read=lambda _: json.dumps(health).encode()))

        args = dict(root=Path('.'), container='cleaning-dev-api-1', candidate='cleaning-dev-api:dev',
                    health_url='http://127.0.0.1:8090/healthz', command=['existing-release', '--verify'])
        args.update(overrides)
        failure = None
        with patch.object(dev, 'ReleaseCleanup', return_value=cleanup), patch.object(dev.subprocess, 'run', command), \
                patch.object(dev, 'HTTPConnection', connection), patch.object(dev.time, 'sleep', lambda _: None):
            try: dev.run(**args)
            except RuntimeError as error: failure = error
        return cleanup, commands, hosts, failure

    def test_hooks_protect_old_and_verified_new_images(self):
        cleanup, commands, hosts, failure = self.execute()
        self.assertIsNone(failure)
        self.assertEqual([['existing-release', '--verify']], commands)
        self.assertEqual([NEW], cleanup.records)
        self.assertEqual([OLD, NEW], cleanup.cleanups[0][0])
        self.assertEqual([OLD, NEW], cleanup.cleanups[1][0])
        self.assertEqual([('127.0.0.1', 8090)], hosts)

    def test_failed_command_or_unverified_candidate_never_records_success(self):
        for mode in ['command-failure', 'wrong-image', 'wrong-health', 'redirect', 'wrong-port']:
            with self.subTest(mode=mode):
                cleanup, _, hosts, failure = self.execute(mode)
                self.assertIsNotNone(failure)
                self.assertEqual([], cleanup.records)
                self.assertEqual(1, len(cleanup.cleanups))
                self.assertTrue(all(host == '127.0.0.1' for host, _ in hosts))

    def test_low_space_or_wrong_project_cannot_run_deployment_command(self):
        for mode in ['low-space', 'wrong-project', 'stopped']:
            cleanup, commands, _, failure = self.execute(mode)
            self.assertIsNotNone(failure)
            self.assertEqual([], commands)
            self.assertEqual([], cleanup.records)

    def test_foreign_images_and_external_health_urls_are_rejected(self):
        for options in [{'candidate': 'postgres:16'}, {'candidate': 'cleaning-dev-api:bad\nvalue'},
                        {'health_url': 'https://example.invalid/healthz'},
                        {'health_url': 'http://user:secret@127.0.0.1:8090/healthz'}]:
            _, commands, _, failure = self.execute(**options)
            self.assertIsNotNone(failure)
            self.assertEqual([], commands)

    def test_post_cleanup_failure_retains_verified_deployment(self):
        cleanup, _, _, failure = self.execute('post-failure')
        self.assertIsNone(failure)
        self.assertEqual([NEW], cleanup.records)


if __name__ == '__main__':
    unittest.main()

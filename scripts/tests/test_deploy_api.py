"""Production deployment logic exercised with synthetic Docker/curl commands only."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import release_deploy as deploy
import release_manifest as manifest

SHA = 'a' * 40
OLD = 'b' * 40
IMAGE = manifest.IMAGE + '@sha256:' + 'c' * 64
OLD_IMAGE = manifest.IMAGE + '@sha256:' + 'd' * 64
NEW_ID = 'sha256:' + 'e' * 64
OLD_ID = 'sha256:' + 'f' * 64


class Commands:
    def __init__(self, root, mode, maintenance_states=None, maintenance_load_states=None):
        modes = {'maintenance_up': 'up', 'maintenance_stopped': 'previous_stopped'}
        self.root, self.mode, self.calls = root, modes.get(mode, mode), []
        self.current = 'old'
        self.recovering = False
        self.health_count = 0
        self.maintenance_states = maintenance_states or (['active'] if mode == 'maintenance_active' else ['inactive'])
        self.maintenance_load_states = maintenance_load_states or (['not-found'] if mode == 'maintenance_missing' else ['loaded'])
        self.maintenance_checks = 0
        self.maintenance_snapshots = []

    def __call__(self, args, **kwargs):
        args = list(args)
        self.calls.append(args.copy())
        if args[0] == 'docker':
            assert args[1:3] == ['--host', 'unix:///var/run/docker.sock']
            args = [args[0], *args[3:]]
        assert 'GAME_GEMINI_API_KEY' not in kwargs['env']
        assert 'API_IMAGE' not in kwargs['env']
        code, result = 0, ''
        if args[0] == 'systemctl':
            assert args == ['systemctl', 'show', '--property=ActiveState', '--property=LoadState', 'cloudflared.service']
            self.maintenance_snapshots.append({'backups': (self.root / '.deploy-backups').exists(),
                                               'candidate_env': bool(list(self.root.glob('releases/*/candidate.env'))),
                                               'current': self.current})
            active = self.maintenance_states[min(self.maintenance_checks, len(self.maintenance_states) - 1)]
            loaded = self.maintenance_load_states[min(self.maintenance_checks, len(self.maintenance_load_states) - 1)]
            result = 'ActiveState=' + active + '\nLoadState=' + loaded
            self.maintenance_checks += 1
        elif args[0] == 'python3':
            assert args[1].endswith('/api/deploy/check-keycloak-runtime-env.py') or args[1].endswith('\\api\\deploy\\check-keycloak-runtime-env.py')
            assert args[2:] == ['--file', '/protected/tenant-runtime.env']
            if self.mode == 'identity': code = 7
        elif args[0] == 'curl':
            assert args[1:] == ['--connect-timeout', '2', '--max-time', '5', '-fsS', 'http://127.0.0.1:8090/healthz']
            self.health_count += 1
            if self.current == 'old' and self.recovering and self.mode == 'rollback_health': code = 22
            elif self.current == 'new' and self.mode == 'curl_error': code = 22
            elif self.current == 'new' and self.mode == 'retry' and self.health_count < 4: code = 22
            elif self.current == 'new' and self.mode == 'bad_json': result = 'broken'
            else:
                result = json.dumps({'status': 'wrong' if self.current == 'new' and self.mode == 'bad_status' else 'ok',
                                     'releaseSha': OLD if self.current == 'old' or self.mode == 'wrong_release' else SHA})
        elif args[0] == 'sleep':
            assert args == ['sleep', '2']
        elif args[:2] == ['docker', 'compose']:
            assert args[2:4] == ['--project-directory', str(self.root)]
            assert '--env-file' in args and '-f' in args
            op = next(x for x in ['ps', 'config', 'up'] if x in args)
            if op == 'ps':
                result = self.current + '-container'
                if self.current == 'new' and self.mode == 'changed' and self.health_count > 1: result = 'other-container'
            elif op == 'config':
                assert args[-3:] == ['config', '--format', 'json']
                if self.mode == 'config': code = 2
                result = json.dumps({'services': {'api': {'image': 'wrong' if self.mode == 'resolved_image' else IMAGE}}})
            else:
                assert args[-6:] == ['up', '-d', '--no-deps', '--pull', 'never', 'api']
                assert args[args.index('-p') + 1] == 'cleaning-suite'
                rollback = any(x.endswith('image.json') for x in args)
                if rollback:
                    self.current, self.recovering = 'old', True
                    if self.mode == 'rollback_up': code = 6
                else:
                    self.current = 'new'
                    if self.mode in ['up', 'rollback_up', 'rollback_health']: code = 5
        elif args[:2] == ['docker', 'login']:
            assert kwargs['input'] == 'test-secret'
            if self.mode == 'login': code = 3
        elif args[:2] == ['docker', 'pull']:
            assert args == ['docker', 'pull', IMAGE]
            if self.mode == 'pull': code = 4
        elif args[:3] == ['docker', 'image', 'inspect']:
            value = args[-1]
            old = value in [OLD_ID, OLD_IMAGE]
            result = json.dumps([{'Id': OLD_ID if old else NEW_ID,
                                  'RepoDigests': [] if self.mode == 'no_previous_digest' and old else [OLD_IMAGE if old else IMAGE],
                                  'Config': {'Env': ['APP_RELEASE_SHA=' + (OLD if old or self.mode == 'wrong_image_release' else SHA)]}}])
        elif args[:2] == ['docker', 'inspect']:
            old = args[-1] == 'old-container'
            result = json.dumps([{'Image': OLD_ID if old else ('wrong' if self.mode == 'image_id' else NEW_ID),
                                  'Config': {'Image': OLD_IMAGE if self.recovering and old else ('old-tag' if old else IMAGE),
                                             'Env': ['APP_RELEASE_SHA=' + (OLD if old else SHA)],
                                             'Labels': {'com.docker.compose.project': 'cleaning-suite'}},
                                  'State': {'Running': not ((not old and self.mode == 'stopped')
                                                           or (old and not self.recovering and self.mode == 'previous_stopped'))}}])
        else:
            raise AssertionError(args)
        return SimpleNamespace(returncode=code, stdout=result, stderr='synthetic command failure')


class DeploymentTests(unittest.TestCase):
    def run_deploy(self, mode='success', approved=True, compatible=True, key='new-owner-ai-secret',
                   maintenance_policy=None, maintenance_states=None, maintenance_load_states=None, expect_preserved_candidate=False):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            old_env = b"API_IDENTITY_ENV_FILE=/protected/tenant-runtime.env\nIMAGE_TAG=sha-old\nDATABASE_PASSWORD=private-value\nGAME_GEMINI_API_KEY='old-owner-secret'\n"
            old_config = b'services:\n  api:\n    image: old-tag\n'
            docker_home = root / '.docker'
            docker_home.mkdir()
            (docker_home / 'config.json').write_text(json.dumps({'currentContext': 'remote-production'}))
            (root / '.env').write_bytes(old_env)
            (root / 'docker-compose.yml').write_bytes(old_config)
            stage = root / 'releases' / SHA
            (stage / 'api/deploy').mkdir(parents=True)
            (stage / 'release.json').write_text(json.dumps({'release_sha': SHA, 'image': IMAGE}))
            policy = {'schema': 1, 'approved': approved,
                      'schema_changes': 'backward-compatible-reviewed', 'rollback_compatible_from': [OLD] if compatible else []}
            if maintenance_policy is not None:
                policy.update(maintenance_policy)
            (stage / 'api/deploy/release-policy.json').write_text(json.dumps(policy))
            (stage / 'api/deploy/docker-compose.yml').write_bytes((SCRIPTS.parent / 'api/deploy/docker-compose.yml').read_bytes())
            env = dict(PATH=os.environ.get('PATH', ''), HOME=tmp, GAME_GEMINI_API_KEY=key,
                       REGISTRY_USER='test-user', REGISTRY_TOKEN='test-secret', API_IMAGE='ambient-override',
                       DOCKER_CONTEXT='hostile-context', DOCKER_HOST='tcp://unrelated-host:2375')
            commands = Commands(root, mode, maintenance_states, maintenance_load_states)
            output, errors = io.StringIO(), io.StringIO()
            failed = None
            with patch.object(deploy.subprocess, 'run', commands), contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
                try:
                    deploy.Deployment(root, stage, env).run()
                except BaseException as error:
                    failed = error
            calls = json.dumps(commands.calls)
            for secret in ['private-value', key, 'test-secret', 'old-owner-secret']:
                if secret:
                    self.assertNotIn(secret, output.getvalue() + errors.getvalue() + calls)
            self.assertNotIn('prune', calls)
            self.assertNotIn('down', calls)
            self.assertFalse(list(root.glob('.deploy-auth-*')))
            if failed and not expect_preserved_candidate:
                self.assertEqual(old_env, (root / '.env').read_bytes())
                self.assertEqual(old_config, (root / 'docker-compose.yml').read_bytes())
            else:
                self.assertIn(IMAGE, (root / '.env').read_text())
                if not failed:
                    self.assertIn('Deployment verified:', output.getvalue())
            return failed, commands, errors.getvalue()

    def test_all_docker_calls_pin_one_local_daemon_despite_existing_remote_context(self):
        failure, commands, _ = self.run_deploy()
        self.assertIsNone(failure)
        docker_calls = [call for call in commands.calls if call[0] == 'docker']
        self.assertTrue(docker_calls)
        self.assertTrue(all(call[1:3] == ['--host', 'unix:///var/run/docker.sock'] for call in docker_calls))

    def test_identity_preflight_failure_precedes_every_docker_operation(self):
        failure, commands, _ = self.run_deploy('identity')
        self.assertIsNotNone(failure)
        self.assertFalse(any(call[0] == 'docker' for call in commands.calls))

    def test_success(self):
        failure, commands, _ = self.run_deploy()
        self.assertIsNone(failure)
        self.assertEqual('new', commands.current)

    def test_active_maintenance_ingress_blocks_before_any_host_mutation(self):
        for state in ['active', 'activating', 'failed', 'unknown', '']:
            with self.subTest(state=state):
                failure, commands, _ = self.run_deploy(maintenance_policy={'maintenance_service': 'cloudflared.service'},
                                                       maintenance_states=[state])
                self.assertIn('Maintenance ingress must remain inactive', str(failure))
                self.assertEqual([['systemctl', 'show', '--property=ActiveState', '--property=LoadState', 'cloudflared.service']], commands.calls)
                self.assertEqual([{'backups': False, 'candidate_env': False, 'current': 'old'}], commands.maintenance_snapshots)

    def test_inactive_but_missing_or_unloaded_maintenance_unit_blocks_before_mutations(self):
        for load_state in ['not-found', 'error', 'masked', 'unloaded', '', 'loaded\nLoadState=loaded']:
            with self.subTest(load_state=load_state):
                failure, commands, _ = self.run_deploy(maintenance_policy={'maintenance_service': 'cloudflared.service'},
                                                       maintenance_states=['inactive'], maintenance_load_states=[load_state])
                self.assertIn('loaded unit', str(failure))
                self.assertEqual([['systemctl', 'show', '--property=ActiveState', '--property=LoadState', 'cloudflared.service']], commands.calls)
                self.assertEqual([{'backups': False, 'candidate_env': False, 'current': 'old'}], commands.maintenance_snapshots)

    def test_unknown_maintenance_unit_is_rejected_before_all_commands(self):
        for value in ['other.service', 'cloudflared.service --no-pager', None, False, ['cloudflared.service']]:
            with self.subTest(value=value):
                failure, commands, _ = self.run_deploy(maintenance_policy={'maintenance_service': value})
                self.assertIn('Unsupported maintenance service', str(failure))
                self.assertEqual([], commands.calls)

    def test_closed_maintenance_gate_is_checked_before_swap_and_after_verification(self):
        failure, commands, _ = self.run_deploy(maintenance_policy={'maintenance_service': 'cloudflared.service'})
        self.assertIsNone(failure)
        self.assertEqual(3, commands.maintenance_checks)
        self.assertEqual(['old', 'old', 'new'], [s['current'] for s in commands.maintenance_snapshots])
        second = [i for i, call in enumerate(commands.calls) if call[0] == 'systemctl'][1]
        first_up = next(i for i, call in enumerate(commands.calls) if 'up' in call)
        self.assertLess(second, first_up)
        self.assertFalse(any('start' in call or 'stop' in call or 'restart' in call for call in commands.calls))

    def test_gate_reopened_before_swap_preserves_previous_api_without_rollback(self):
        failure, commands, _ = self.run_deploy(maintenance_policy={'maintenance_service': 'cloudflared.service'},
                                               maintenance_states=['inactive', 'active'])
        self.assertIn('Maintenance ingress', str(failure))
        self.assertEqual('old', commands.current)
        self.assertFalse(any('up' in call for call in commands.calls))

    def test_gate_reopened_after_candidate_verification_prevents_unsafe_artifact_rollback(self):
        failure, commands, errors = self.run_deploy(maintenance_policy={'maintenance_service': 'cloudflared.service'},
                                                   maintenance_states=['inactive', 'inactive', 'active'],
                                                   expect_preserved_candidate=True)
        self.assertIn('Deployment and rollback failed', str(failure))
        self.assertEqual('new', commands.current)
        self.assertFalse(commands.recovering)
        self.assertNotIn('restored and verified', errors)

    def test_closed_gate_rollback_does_not_claim_external_identity_recovery(self):
        failure, commands, errors = self.run_deploy('up', maintenance_policy={'maintenance_service': 'cloudflared.service'})
        self.assertIsNotNone(failure)
        self.assertEqual(4, commands.maintenance_checks)
        self.assertTrue(commands.recovering)
        self.assertIn('Identity recovery remains external', errors)
        self.assertIn('maintenance ingress must stay inactive', errors)

    def test_reopened_gate_after_rollback_fails_instead_of_reporting_recovery_success(self):
        failure, commands, errors = self.run_deploy('up', maintenance_policy={'maintenance_service': 'cloudflared.service'},
                                                   maintenance_states=['inactive', 'inactive', 'inactive', 'active'])
        self.assertIn('Deployment and rollback failed', str(failure))
        self.assertEqual('old', commands.current)
        self.assertNotIn('restored and verified', errors)

    def test_policies_without_maintenance_key_retain_existing_behavior(self):
        failure, commands, _ = self.run_deploy()
        self.assertIsNone(failure)
        self.assertEqual(0, commands.maintenance_checks)

    def test_normal_deployment_rejects_a_stopped_previous_api(self):
        failure, commands, _ = self.run_deploy('previous_stopped')
        self.assertIn('Previous API is not running', str(failure))
        self.assertEqual(0, commands.health_count)
        self.assertFalse(any('pull' in call or 'up' in call for call in commands.calls))

    def test_closed_maintenance_accepts_exact_stopped_baseline_without_old_health(self):
        failure, commands, _ = self.run_deploy('previous_stopped', maintenance_policy={'maintenance_service': 'cloudflared.service'})
        self.assertIsNone(failure)
        self.assertEqual('new', commands.current)
        self.assertEqual(1, commands.health_count)
        self.assertEqual(3, commands.maintenance_checks)
        # Stopping does not skip the old immutable identity checks.
        inspected = [call[-1] for call in commands.calls if 'inspect' in call]
        self.assertIn(OLD_ID, inspected)
        self.assertIn(OLD_IMAGE, inspected)

    def test_active_maintenance_rejects_stopped_baseline_before_host_mutations(self):
        failure, commands, _ = self.run_deploy('previous_stopped', maintenance_policy={'maintenance_service': 'cloudflared.service'},
                                               maintenance_states=['active'])
        self.assertIn('Maintenance ingress', str(failure))
        self.assertEqual(1, len(commands.calls))
        self.assertEqual([{'backups': False, 'candidate_env': False, 'current': 'old'}], commands.maintenance_snapshots)

    def test_compatibility_gate_precedes_all_commands(self):
        failure, commands, _ = self.run_deploy(approved=False)
        self.assertIsNotNone(failure)
        self.assertEqual([], commands.calls)
        failure, commands, _ = self.run_deploy(compatible=False)
        self.assertIsNotNone(failure)
        self.assertFalse(any('pull' in c or 'up' in c for c in commands.calls))

    def test_pre_replacement_failures_preserve_existing_container(self):
        for mode in ['identity', 'config', 'resolved_image', 'login', 'pull', 'wrong_image_release', 'no_previous_digest']:
            with self.subTest(mode=mode):
                failure, commands, _ = self.run_deploy(mode)
                self.assertIsNotNone(failure)
                self.assertEqual('old', commands.current)
                self.assertFalse(commands.recovering)

    def test_every_post_replacement_failure_restores_and_verifies_previous_image(self):
        for mode in ['up', 'image_id', 'stopped', 'changed', 'bad_json', 'bad_status', 'wrong_release', 'curl_error']:
            with self.subTest(mode=mode):
                failure, commands, errors = self.run_deploy(mode)
                self.assertIsNotNone(failure)
                self.assertTrue(commands.recovering)
                self.assertEqual('old', commands.current)
                self.assertIn('restored and verified', errors)

    def test_rollback_failure_is_explicit_and_keeps_recovery_inputs(self):
        for mode in ['rollback_up', 'rollback_health']:
            with self.subTest(mode=mode):
                failure, _, errors = self.run_deploy(mode)
                self.assertIn('Deployment and rollback failed', str(failure))
                self.assertNotIn('restored and verified', errors)

    def test_retry_and_missing_keys(self):
        self.assertIsNone(self.run_deploy('retry')[0])
        failure, commands, _ = self.run_deploy(key='')
        self.assertIsNotNone(failure)
        self.assertFalse(any('pull' in c or 'up' in c for c in commands.calls))



class RealShellDeploymentTests(unittest.TestCase):
    @unittest.skipUnless(os.name == 'posix', 'real shell/command harness runs on Linux CI')
    def test_real_wrapper_and_external_fake_commands(self):
        for mode in ['success', 'up', 'wrong_release', 'pull', 'invalid_identity',
                     'maintenance_active', 'maintenance_inactive', 'maintenance_up', 'maintenance_unknown', 'maintenance_stopped', 'maintenance_missing']:
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as tmp, tempfile.TemporaryDirectory(prefix='release-identity-', dir=Path.home()) as identity:
                root = Path(tmp)
                identity_file = Path(identity).resolve() / 'tenant-runtime.env'
                identity_file.write_text('Auth__Keycloak__TenantAccounts__tenant-test__ClientId=tenant-runtime\nAuth__Keycloak__TenantAccounts__tenant-test__ClientSecret=synthetic-opaque-credential\n')
                identity_file.chmod(0o644 if mode == 'invalid_identity' else 0o600)
                (root / '.env').write_text('API_IDENTITY_ENV_FILE=' + str(identity_file) + '\nIMAGE_TAG=sha-old\nDATABASE_PASSWORD=private-value\n')
                (root / 'docker-compose.yml').write_text('services:\n  api:\n    image: old-tag\n')
                (root / 'mode').write_text(mode)
                stage = root / 'releases' / SHA
                for name in manifest.ASSETS:
                    destination = stage / name
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes((SCRIPTS.parent / name).read_bytes())
                policy = {'schema': 1, 'approved': True,
                          'schema_changes': 'backward-compatible-reviewed', 'rollback_compatible_from': [OLD]}
                if mode.startswith('maintenance_'):
                    policy['maintenance_service'] = 'unreviewed.service' if mode == 'maintenance_unknown' else 'cloudflared.service'
                (stage / 'api/deploy/release-policy.json').write_text(json.dumps(policy))
                value = manifest.create(stage, dict(GITHUB_REPOSITORY='owner/repo', GITHUB_SHA=SHA,
                    GITHUB_RUN_ID='10', GITHUB_RUN_ATTEMPT='1', BUILD_DIGEST='sha256:' + 'c' * 64))
                raw = json.dumps(value).encode()
                (stage / 'release.json').write_bytes(raw)
                binary = root / 'bin'
                binary.mkdir()
                fake = '''#!/usr/bin/env python3
import json, os, pathlib, sys
sys.path.insert(0, REPLACE_PATH)
from test_deploy_api import Commands
root = pathlib.Path.cwd()
state_path = root / 'command-state.json'
fake = Commands(root, (root / 'mode').read_text())
if state_path.exists():
    fake.__dict__.update(json.loads(state_path.read_text()))
args = [pathlib.Path(sys.argv[0]).name] + sys.argv[1:]
result = fake(args, env=dict(os.environ), input=sys.stdin.read() if 'login' in args else None)
state_path.write_text(json.dumps({k: v for k, v in fake.__dict__.items() if k != 'root'}))
sys.stdout.write(result.stdout)
raise SystemExit(result.returncode)
'''.replace('REPLACE_PATH', repr(str(SCRIPTS / 'tests')))
                for name in ['docker', 'curl', 'sleep', 'systemctl']:
                    path = binary / name
                    path.write_text(fake)
                    path.chmod(0o755)
                env = dict(os.environ, DEPLOY_DIR=str(root), RELEASE_SHA=SHA,
                    RELEASE_MANIFEST_HASH=hashlib.sha256(raw).hexdigest(),
                    REGISTRY_USER='test-user', REGISTRY_TOKEN='test-secret', GAME_GEMINI_API_KEY='new-owner-ai-secret',
                    PATH=str(binary) + os.pathsep + os.environ['PATH'])
                result = subprocess.run(['sh', str(stage / 'scripts/deploy-api.sh')], env=env, text=True, capture_output=True, timeout=60)
                state_file = root / 'command-state.json'
                state = json.loads(state_file.read_text()) if state_file.exists() else {'current': 'old', 'recovering': False}
                success = mode in ['success', 'maintenance_inactive', 'maintenance_stopped']
                self.assertEqual(success, result.returncode == 0, result.stderr)
                self.assertEqual('new' if success else 'old', state['current'])
                self.assertEqual(mode in ['up', 'wrong_release', 'maintenance_up'], state['recovering'])
                if mode in ['maintenance_active', 'maintenance_unknown', 'maintenance_missing']:
                    self.assertFalse((root / '.deploy-backups').exists())
                    self.assertFalse((stage / 'candidate.env').exists())
                if mode == 'maintenance_inactive':
                    self.assertEqual(3, state['maintenance_checks'])
                if mode == 'maintenance_stopped':
                    self.assertEqual(3, state['maintenance_checks'])
                    self.assertEqual(1, state['health_count'])
                if mode == 'maintenance_up':
                    self.assertEqual(4, state['maintenance_checks'])
                    self.assertIn('Identity recovery remains external', result.stderr)
                for secret in ['test-secret', 'new-owner-ai-secret', 'private-value']:
                    self.assertNotIn(secret, result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()

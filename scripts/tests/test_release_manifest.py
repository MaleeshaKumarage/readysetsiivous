"""Offline provenance, artifact byte, and workflow trust-order regressions."""
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import release_manifest as m

SHA = 'a' * 40
REPO = 'owner/repo'
PREFIX = '/repos/' + REPO


class Fixture:
    def __init__(self):
        self.manifest = m.create(SCRIPTS.parent, dict(GITHUB_REPOSITORY=REPO, GITHUB_SHA=SHA,
            GITHUB_RUN_ID='20', GITHUB_RUN_ATTEMPT='2', BUILD_DIGEST='sha256:' + 'c' * 64))
        self.repo = {'id': 1}
        self.workflow = {'id': 10, 'path': m.WORKFLOW}
        self.run = dict(id=20, workflow_id=10, path=m.WORKFLOW, head_repository={'id': 1}, head_sha=SHA,
                        head_branch='main', event='push', status='completed', conclusion='success', run_attempt=2)
        self.artifact = dict(id=30, name=f'api-release-{SHA}-2', expired=False,
                            workflow_run=dict(id=20, head_sha=SHA, repository_id=1, head_repository_id=1))
        self.requests = []
        self.refresh()

    def refresh(self, extra=False):
        output = io.BytesIO()
        with zipfile.ZipFile(output, 'w') as archive:
            archive.writestr('release.json', json.dumps(self.manifest))
            if extra:
                archive.writestr('../escape', 'bad')
        self.raw = output.getvalue()
        self.artifact.update(size_in_bytes=len(self.raw), digest='sha256:' + hashlib.sha256(self.raw).hexdigest())

    def request(self, method, path):
        self.requests.append(path)
        assert method == 'GET'
        if path == PREFIX: return self.repo
        if path == PREFIX + '/actions/workflows/docker-build-push.yml': return self.workflow
        if path.startswith(PREFIX + '/actions/workflows/docker-build-push.yml/runs?'):
            return {'total_count': 1, 'workflow_runs': [self.run]}
        if path == PREFIX + '/actions/runs/20': return self.run
        if path == PREFIX + '/actions/runs/20/artifacts?per_page=100': return {'total_count': 1, 'artifacts': [self.artifact]}
        if path == PREFIX + '/actions/artifacts/30': return self.artifact
        raise AssertionError(path)

    def get(self, url, authenticated):
        if authenticated:
            assert url == 'https://api.github.com' + PREFIX + '/actions/artifacts/30/zip'
            return 302, 'https://fixture.blob.core.windows.net/release', b''
        assert url == 'https://fixture.blob.core.windows.net/release'
        return 200, None, self.raw

    def resolve(self, run_id=20):
        return m.resolve(REPO, SHA, run_id, self, self)


class ManifestTests(unittest.TestCase):
    def test_required_identity_checker_is_an_exact_release_asset(self):
        fixture = Fixture()
        name = 'api/deploy/check-keycloak-runtime-env.py'
        self.assertIn(name, fixture.manifest['assets'])
        self.assertEqual(hashlib.sha256((SCRIPTS.parent / name).read_bytes()).hexdigest(), fixture.manifest['assets'][name])

    def test_success_and_manual_matching_build_resolution(self):
        for run_id in [20, 0]:
            fixture = Fixture()
            self.assertEqual(fixture.manifest, fixture.resolve(run_id))

    def test_run_provenance_rejections(self):
        changes = [('id', 21), ('workflow_id', 11), ('path', 'other.yml'), ('head_repository', {'id': 2}),
                   ('head_sha', 'b' * 40), ('head_branch', 'other'), ('event', 'pull_request'),
                   ('status', 'in_progress'), ('conclusion', 'failure'), ('run_attempt', 3)]
        for key, value in changes:
            with self.subTest(key=key):
                fixture = Fixture()
                fixture.run[key] = value
                with self.assertRaises(ValueError): fixture.resolve()

    def test_artifact_provenance_rejections(self):
        for key, value in [('expired', True), ('name', 'wrong'), ('digest', 'sha256:' + '0' * 64),
                           ('size_in_bytes', m.LIMIT + 1), ('size_in_bytes', 1),
                           ('workflow_run', dict(id=21, head_sha=SHA, repository_id=1, head_repository_id=1))]:
            with self.subTest(key=key):
                fixture = Fixture()
                fixture.artifact[key] = value
                with self.assertRaises(ValueError): fixture.resolve()

    def test_manifest_binding_and_immutable_image_rejections(self):
        for key, value in [('release_sha', 'b' * 40), ('repository', 'evil/repo'), ('run_id', 21),
                           ('run_attempt', 1), ('image', m.IMAGE + ':latest'), ('assets', {})]:
            with self.subTest(key=key):
                fixture = Fixture()
                fixture.manifest[key] = value
                fixture.refresh()
                with self.assertRaises(ValueError): fixture.resolve()

    def test_unexpected_zip_entries_are_not_extracted(self):
        fixture = Fixture()
        fixture.refresh(extra=True)
        with self.assertRaises(ValueError): fixture.resolve()

    def test_bundle_verifies_every_selected_checkout_byte(self):
        fixture = Fixture()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / 'checkout'
            root.mkdir()
            for name in m.ASSETS:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes((SCRIPTS.parent / name).read_bytes())
            raw = json.dumps(fixture.manifest).encode()
            source = Path(tmp) / 'release.json'
            source.write_bytes(raw)
            output = Path(tmp) / 'bundle'
            environment = dict(RELEASE_SHA=SHA, GITHUB_REPOSITORY=REPO, RELEASE_MANIFEST_HASH=hashlib.sha256(raw).hexdigest())
            args = ['release_manifest.py', 'bundle', '--root', str(root), '--manifest', str(source), '--output', str(output)]
            with patch.dict(os.environ, environment), patch.object(sys, 'argv', args):
                m.main()
            self.assertEqual(raw, (output / 'release.json').read_bytes())
            (root / m.ASSETS[0]).write_text('tampered')
            with patch.dict(os.environ, environment), patch.object(sys, 'argv', args[:-1] + [str(Path(tmp) / 'rejected')]):
                with self.assertRaises(ValueError): m.main()
            self.assertFalse((Path(tmp) / 'rejected').exists())


class WorkflowTests(unittest.TestCase):
    def test_trusted_controller_provenance_precedes_release_code_and_staging(self):
        source = (SCRIPTS.parent / '.github/workflows/docker-deploy-oracle.yml').read_text()
        self.assertLess(source.index('Validate release SHA'), source.index('actions/checkout'))
        self.assertLess(source.index('ref: ${{ github.workflow_sha }}'), source.index('release_manifest.py resolve'))
        self.assertLess(source.index('release_manifest.py resolve'), source.index('ref: ${{ env.RELEASE_SHA }}'))
        self.assertLess(source.index('release_manifest.py bundle'), source.index('appleboy/scp-action'))
        self.assertIn('target: /home/ubuntu/cleaning-suite/releases/${{ env.RELEASE_SHA }}', source)
        self.assertIn('source: bundle', source)
        self.assertIn('script_path: release/scripts/deploy-api.sh', source)
        self.assertIn('actions: read', source)
        self.assertNotIn('ref: main', source)
        self.assertEqual(2, source.count('persist-credentials: false'))

    def test_build_preserves_digest_and_all_release_assets_trigger_build(self):
        source = (SCRIPTS.parent / '.github/workflows/docker-build-push.yml').read_text()
        self.assertIn('BUILD_DIGEST: ${{ steps.build.outputs.digest }}', source)
        self.assertIn('name: api-release-${{ github.sha }}-${{ github.run_attempt }}', source)
        self.assertIn("'scripts/release_*.py'", source)
        self.assertIn("'scripts/deploy-api.sh'", source)
        self.assertIn('if-no-files-found: error', source)

    def test_default_policy_blocks_unreviewed_migration_and_compose_requires_digest_input(self):
        policy = json.loads((SCRIPTS.parent / 'api/deploy/release-policy.json').read_text())
        self.assertIs(policy['approved'], False)
        self.assertEqual([], policy['rollback_compatible_from'])
        compose = (SCRIPTS.parent / 'api/deploy/docker-compose.yml').read_text()
        self.assertIn('${API_IMAGE:?', compose)
        self.assertNotIn('${IMAGE_TAG:-latest}', compose)


if __name__ == '__main__':
    unittest.main()

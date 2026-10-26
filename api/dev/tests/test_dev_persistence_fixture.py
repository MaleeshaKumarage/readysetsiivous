"""Offline contract for the real recreate rehearsal's state-only service selection."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('recreate', Path(__file__).with_name('dev-persistence-recreate.py'))
recreate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recreate)


class StateFixtureTests(unittest.TestCase):
    def fixture(self):
        return {'services': {
            'postgres': {'image': 'postgres:16-alpine', 'ports': ['5435:5432']},
            'keycloak-db-init': {'image': 'postgres:16-alpine'},
            'keycloak': {'image': 'quay.io/keycloak/keycloak:24.0', 'command': ['start-dev', '--import-realm'], 'volumes': ['sample-realm:/import'], 'restart': 'unless-stopped'},
        }}

    def test_application_and_pdf_images_are_excluded_but_both_state_mounts_survive(self):
        config = self.fixture()
        mounts = [{'type': 'volume', 'source': 'owned_uploads', 'target': '/app/uploads'},
                  {'type': 'volume', 'source': 'owned_keys', 'target': '/app/keys'}]
        config['services']['api'] = {'image': 'cleaning-dev-api:dev', 'volumes': mounts, 'environment': {'API_ONLY': 'synthetic'}}
        config['services']['pdf-worker'] = {'image': 'cleaning-dev-api:dev', 'command': ['--pdf-worker-host']}
        original = recreate.prepare_state_services(config)
        self.assertEqual(mounts, original['volumes'])
        self.assertEqual({'postgres', 'keycloak-db-init', 'keycloak', 'state-helper'}, set(config['services']))
        helper = config['services']['state-helper']
        self.assertEqual('postgres:16-alpine', helper['image'])
        self.assertEqual(mounts, helper['volumes'])
        self.assertNotIn('environment', helper)
        self.assertEqual(['start-dev'], config['services']['keycloak']['command'])
        self.assertEqual([], config['services']['keycloak']['volumes'])
        for service in config['services'].values():
            self.assertNotIn('ports', service)
            self.assertNotIn('restart', service)

    def test_identity_only_stack_needs_no_application_helper(self):
        config = self.fixture()
        self.assertIsNone(recreate.prepare_state_services(config))
        self.assertEqual({'postgres', 'keycloak-db-init', 'keycloak'}, set(config['services']))

    def test_unreviewed_runtime_service_fails_closed(self):
        config = self.fixture()
        config['services']['unreviewed-worker'] = {'image': 'unreviewed:latest'}
        with self.assertRaisesRegex(AssertionError, 'Unexpected service'):
            recreate.prepare_state_services(config)

    def test_only_profiled_schema_migrator_is_excluded(self):
        config = self.fixture()
        config['services']['migrate'] = {'profiles': ['migration'], 'command': ['--migrate-schema']}
        recreate.prepare_state_services(config)
        self.assertNotIn('migrate', config['services'])
        config = self.fixture()
        config['services']['migrate'] = {'command': ['unreviewed']}
        with self.assertRaisesRegex(AssertionError, 'Unexpected migration'):
            recreate.prepare_state_services(config)


if __name__ == '__main__':
    unittest.main()

#!/usr/bin/env python3
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('identity_env',Path(__file__).with_name('check-keycloak-runtime-env.py'))
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)
VALID = 'Auth__Keycloak__TenantAccounts__tenant-a__ClientId=employee-runtime\nAuth__Keycloak__TenantAccounts__tenant-a__ClientSecret=synthetic-only-secret-123\n'


fixture_spec = importlib.util.spec_from_file_location('persistence_fixture', Path(__file__).resolve().parents[1] / 'dev/tests/dev-persistence-recreate.py')
fixture = importlib.util.module_from_spec(fixture_spec)
fixture_spec.loader.exec_module(fixture)


class IdentityEnvTests(unittest.TestCase):
    def test_persistence_fixture_identity_is_complete_and_protected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = fixture.fixture_identity(directory)
            self.assertEqual(1, checker.validate_content(path.read_text()))
            self.assertEqual({'Auth__Keycloak__TenantAccounts__fixture-tenant__ClientId',
                              'Auth__Keycloak__TenantAccounts__fixture-tenant__ClientSecret'},
                             {line.split('=', 1)[0] for line in path.read_text().splitlines()})
            if os.name == 'posix':
                self.assertEqual(0o600, path.stat().st_mode & 0o777)

    def test_tenant_pairs_supported(self):
        self.assertEqual(1,checker.validate_content(VALID))
        self.assertEqual(2,checker.validate_content(VALID + VALID.replace('tenant-a','tenant-b')))

    def test_incomplete_duplicate_and_non_tenant_values_rejected(self):
        for value in ['',VALID.splitlines()[0],VALID + VALID,VALID + 'Auth__Keycloak__AdminPassword=must-not-be-accepted\n',
                      VALID.replace('tenant-a','master'),VALID.replace('tenant-a','platform-realm'),
                      VALID.replace('synthetic-only-secret-123','${PLATFORM_SECRET}'),
                      VALID.replace('employee-runtime','realm-management')]:
            with self.subTest(value=value[:12]), self.assertRaises(ValueError):
                checker.validate_content(value,'platform-realm')

    def test_compose_delivers_required_file_only_to_api(self):
        for filename in ['docker-compose.yml','docker-compose.dev.yml']:
            source = Path(__file__).with_name(filename).read_text()
            api = source.split('\n  api:\n',1)[1]
            before_api = source.split('\n  api:\n',1)[0]
            self.assertIn('env_file:\n      - ${API_IDENTITY_ENV_FILE:?',api)
            self.assertNotIn('API_IDENTITY_ENV_FILE',before_api)
            self.assertNotIn('Auth__Keycloak__AdminPassword:',api)
            self.assertNotIn('Auth__Keycloak__AdminUser:',api)
            self.assertIn('Auth__Keycloak__ProvisioningSetupReviewed: "false"',api)

    @unittest.skipUnless(os.name == 'posix','POSIX deployment permission gate runs in Linux CI')
    def test_protected_file_modes_and_symlinks(self):
        # Home rather than /tmp: all parents must prohibit replacement by other users.
        with tempfile.TemporaryDirectory(dir=Path.home().resolve()) as directory:
            Path(directory).chmod(0o700)
            path = Path(directory) / 'identity.env'
            path.write_text(VALID)
            path.chmod(0o600)
            self.assertEqual(1,checker.validate_file(str(path)))
            path.chmod(0o644)
            with self.assertRaises(ValueError): checker.validate_file(str(path))
            path.chmod(0o600)
            link = path.with_name('linked.env'); link.symlink_to(path)
            with self.assertRaises(ValueError): checker.validate_file(str(link))
            # Parent write access also permits untrusted replacement.
            Path(directory).chmod(0o777)
            with self.assertRaises(ValueError): checker.validate_file(str(path))
            Path(directory).chmod(0o700)

    @unittest.skipUnless(os.name == 'posix','POSIX deployment permission gate runs in Linux CI')
    def test_empty_or_checkout_file_is_rejected(self):
        with tempfile.TemporaryDirectory(dir=Path.home().resolve()) as directory:
            path = Path(directory) / 'empty.env'; path.write_text(''); path.chmod(0o600)
            with self.assertRaises(ValueError): checker.validate_file(str(path))
        with self.assertRaises(ValueError): checker.validate_file(str(Path(__file__).resolve()))


if __name__ == '__main__': unittest.main()

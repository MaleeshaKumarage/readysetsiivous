"""Normalize both shipped Compose files and verify API proxy/limiter wiring without a daemon."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
MAPPINGS = {
    'ReverseProxy__KnownProxiesCsv': ('API_TRUSTED_PROXY_IPS', ''),
    'ReverseProxy__KnownNetworksCsv': ('API_TRUSTED_PROXY_NETWORKS', ''),
    'ReverseProxy__ForwardLimit': ('API_PROXY_FORWARD_LIMIT', '1'),
    'PublicRateLimit__Capacity': ('API_PUBLIC_RATE_LIMIT_CAPACITY', '10000'),
}


class ProxyComposeTests(unittest.TestCase):
    def normalized(self, filename, settings):
        self.assertIsNotNone(shutil.which('docker'), 'Docker Compose CLI is required; config normalization never starts containers')
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            identity = folder / 'tenant-runtime.env'
            identity.write_text('Auth__Keycloak__TenantAccounts__synthetic__ClientId=tenant-runtime\nAuth__Keycloak__TenantAccounts__synthetic__ClientSecret=synthetic-unusable-credential\n')
            values = dict(POSTGRES_PASSWORD='synthetic-unusable-db', KC_DB_PASSWORD='synthetic-unusable-kc', DEV_KC_DB_PASSWORD='synthetic-unusable-dev-kc',
                KEYCLOAK_ADMIN_USER='synthetic-admin', KEYCLOAK_ADMIN_PASSWORD='synthetic-unusable-password',
                API_IDENTITY_ENV_FILE=identity.as_posix(), API_IMAGE='example.invalid/synthetic@sha256:' + 'a' * 64,
                POSTGRES_RUNTIME_PASSWORD='synthetic-runtime-db', DB_MIGRATOR_PASSWORD='synthetic-migrator-db',
                DB_RUNTIME_PASSWORD='synthetic-runtime-db', DEV_IMAGE_TAG='synthetic-unused', IMAGE_TAG='synthetic-unused')
            values.update(settings)
            env_file = folder / '.env'
            env_file.write_text(''.join(name + '=' + value + '\n' for name, value in values.items()))
            environment = {name: value for name, value in os.environ.items() if name.upper() in
                ('PATH', 'SYSTEMROOT', 'WINDIR', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'HOME', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA')}
            result = subprocess.run(['docker', 'compose', '--env-file', str(env_file), '-f', str(ROOT / filename),
                'config', '--format', 'json'], env=environment, text=True, capture_output=True, timeout=30)
            self.assertEqual(0, result.returncode, 'Compose normalization failed (fixture values only): ' + result.stderr)
            return json.loads(result.stdout)

    def test_empty_defaults_reach_api_without_implicit_trust(self):
        for filename in ['api/deploy/docker-compose.yml', 'api/deploy/docker-compose.dev.yml']:
            with self.subTest(filename=filename):
                services = self.normalized(filename, {})['services']
                for key, (_, default) in MAPPINGS.items():
                    self.assertEqual(default, services['api']['environment'][key] or '')
                    for name, service in services.items():
                        if name != 'api': self.assertNotIn(key, service.get('environment', {}))

    def test_host_env_settings_reach_api_as_scalar_configuration(self):
        settings = dict(API_TRUSTED_PROXY_IPS='192.0.2.10,192.0.2.11', API_TRUSTED_PROXY_NETWORKS='198.51.100.0/24',
            API_PROXY_FORWARD_LIMIT='2', API_PUBLIC_RATE_LIMIT_CAPACITY='321')
        for filename in ['api/deploy/docker-compose.yml', 'api/deploy/docker-compose.dev.yml']:
            with self.subTest(filename=filename):
                api = self.normalized(filename, settings)['services']['api']['environment']
                for key, (source, _) in MAPPINGS.items():
                    self.assertEqual(settings[source], api[key])


if __name__ == '__main__':
    unittest.main()

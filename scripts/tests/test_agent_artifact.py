import hashlib
import io
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import agent_artifact as artifact


def zipped(entries=None):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w') as archive:
        for name, body in entries or [('proposal.json', b'{"schema":1}')]:
            archive.writestr(name, body)
    return stream.getvalue()


class FakeTransport:
    def __init__(self, raw):
        self.raw = raw
        self.digest = 'sha256:' + hashlib.sha256(raw).hexdigest()
        self.repo = {'id': 10, 'full_name': 'owner/repo'}
        self.artifact = {'id': 12, 'expired': False, 'size_in_bytes': len(raw), 'digest': self.digest, 'name': 'agent-proposal-2', 'workflow_run': {'id': 20, 'repository_id': 10, 'head_repository_id': 10, 'head_sha': 'a'*40}}
        self.run = {'id': 20, 'run_attempt': 2, 'head_sha': 'a'*40, 'repository': {'id': 10}, 'head_repository': {'id': 10}}
        self.calls = []
        self.location = 'https://store.blob.core.windows.net/archive?sig=secret'

    def get(self, url, authenticated=False):
        self.calls.append((url, authenticated))
        if url.endswith('/zip'):
            return 302, self.location, b''
        if url == self.location:
            return 200, None, self.raw
        value = self.repo if url.endswith('/owner/repo') else self.artifact if '/artifacts/' in url else self.run
        return 200, None, json.dumps(value).encode()


class DownloadTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.output = Path(self.tmp.name) / 'nested' / 'proposal.json'
        self.env = {'GITHUB_REPOSITORY': 'owner/repo', 'GITHUB_RUN_ID': '20', 'GITHUB_RUN_ATTEMPT': '2'}
        self.transport = FakeTransport(zipped())

    def download(self, digest=None):
        artifact.download('owner/repo', 12, digest or self.transport.digest, 'a'*40, 'proposal', self.output, self.env, self.transport)

    def rejected(self):
        with self.assertRaises((ValueError, zipfile.BadZipFile, json.JSONDecodeError)):
            self.download()
        self.assertFalse(self.output.exists())

    def test_success_and_no_auth_to_storage(self):
        self.download(self.transport.digest.removeprefix('sha256:'))
        self.assertEqual(self.output.read_bytes(), b'{"schema":1}')
        self.assertTrue(all(auth for url, auth in self.transport.calls if url.startswith('https://api.github.com/')))
        self.assertFalse(self.transport.calls[-1][1])

    def test_metadata_bindings(self):
        cases = [('repo', 'id', 11), ('repo', 'full_name', 'evil/repo'), ('artifact', 'id', 13), ('artifact', 'expired', True), ('artifact', 'size_in_bytes', 0), ('artifact', 'size_in_bytes', artifact.LIMIT+1), ('artifact', 'size_in_bytes', True), ('artifact', 'digest', 'sha256:'+'0'*64), ('artifact', 'name', 'agent-proposal-1'), ('run', 'id', 21), ('run', 'run_attempt', 1), ('run', 'head_sha', 'b'*40)]
        for target, key, value in cases:
            with self.subTest(target=target, key=key, value=value):
                self.transport = FakeTransport(zipped())
                getattr(self.transport, target)[key] = value
                self.rejected()
        for target, field in [('artifact', 'workflow_run'), ('run', 'repository'), ('run', 'head_repository')]:
            keys = ('id','repository_id','head_repository_id','head_sha') if field == 'workflow_run' else ('id',)
            for key in keys:
                with self.subTest(target=target, field=field, key=key):
                    self.transport = FakeTransport(zipped())
                    getattr(self.transport, target)[field][key] = 'b'*40 if key == 'head_sha' else 999
                    self.rejected()

    def test_context_required(self):
        for key in list(self.env):
            with self.subTest(key=key):
                original = self.env.pop(key)
                self.rejected()
                self.env[key] = original

    def test_wrong_zip_digest(self):
        self.transport.raw = zipped([('proposal.json', b'{"changed":true}')])
        self.rejected()
        self.assertFalse(self.output.parent.exists())

    def test_unsafe_zip_and_json(self):
        link = zipfile.ZipInfo('proposal.json')
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        special = zipfile.ZipInfo('proposal.json')
        special.external_attr = (stat.S_IFIFO | 0o600) << 16
        for entries in [[('../proposal.json', b'{}')], [('proposal.json/', b'{}')], [('other.json', b'{}')], [('proposal.json', b'{}'), ('other.json', b'{}')], [(link, b'{}')], [(special, b'{}')], [('proposal.json', b'x')], [('proposal.json', b'{"a":1,"a":2}')], [('proposal.json', b'[]')], [('proposal.json', b'{"a":NaN}')], [('proposal.json', b'x'*(artifact.LIMIT+1))]]:
            with self.subTest(entries=str(entries)[:100]):
                self.transport = FakeTransport(zipped(entries))
                self.rejected()

    def test_reject_storage_hosts(self):
        for url in ['https://evil.test/a', 'http://a.blob.core.windows.net/a', 'https://user:pass@a.blob.core.windows.net/a', 'https://a.blob.core.windows.net/a#fragment', 'https://a.blob.core.windows.net.evil.test/a', 'https://a.blob.core.windows.net:8080/a']:
            with self.subTest(url=url):
                self.transport.location = url
                self.rejected()

    def test_second_redirect_rejected(self):
        original = self.transport.get
        self.transport.get = lambda url, authenticated=False: (302, 'https://evil.test', b'') if url == self.transport.location else original(url, authenticated)
        self.rejected()

    def test_existing_output_preserved(self):
        self.output.parent.mkdir()
        self.output.write_text('existing')
        with self.assertRaises(FileExistsError):
            self.download()
        self.assertEqual(self.output.read_text(), 'existing')

    def test_direct_github_zip(self):
        original = self.transport.get
        self.transport.get = lambda url, authenticated=False: (200, None, self.transport.raw) if url.endswith('/zip') else original(url, authenticated)
        self.download()
        self.assertTrue(self.output.exists())

    def test_duplicate_zip_entries(self):
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', UserWarning)
            self.transport = FakeTransport(zipped([('proposal.json', b'{}'), ('proposal.json', b'{}')]))
        self.rejected()

    def test_encrypted_flag_and_null_filename(self):
        raw = bytearray(zipped())
        # Set encryption in both local and central headers; reject before reading.
        raw[6] |= 1
        central = raw.index(b'PK\x01\x02')
        raw[central + 8] |= 1
        self.transport = FakeTransport(bytes(raw))
        self.rejected()
        raw = zipped().replace(b'proposal.json', b'proposal.json\x00'.ljust(13, b'x'))
        # A filename containing NUL is rejected (or malformed archive fails closed).
        self.transport = FakeTransport(raw)
        self.rejected()

    def test_transport_rejects_redirect_and_oversize(self):
        class Response(io.BytesIO):
            code = 200
            headers = {}
            def geturl(self):
                return 'https://api.github.com/repos/owner/repo'
        with patch('urllib.request.build_opener') as build:
            transport = artifact.Transport('secret')
        transport.opener = build.return_value
        transport.opener.open.return_value = Response(b'x' * (artifact.LIMIT+1))
        with self.assertRaises(ValueError):
            transport.get('https://api.github.com/repos/owner/repo', True)
        transport.opener.open.return_value = Response(b'{}')
        with self.assertRaises(ValueError):
            transport.get('https://a.blob.core.windows.net/zip')

    def test_transport_authorization_and_no_proxies(self):
        class Response(io.BytesIO):
            code = 200
            headers = {}
            def geturl(self):
                return self.url
        class Opener:
            def __init__(self):
                self.requests = []
            def open(self, request, timeout):
                self.requests.append(request)
                response = Response(b'{}')
                response.url = request.full_url
                return response
        opener = Opener()
        with patch('urllib.request.build_opener', return_value=opener) as build:
            transport = artifact.Transport('secret')
        self.assertEqual(build.call_args.args[0].proxies, {})
        transport.get('https://api.github.com/repos/owner/repo', True)
        transport.get('https://a.blob.core.windows.net/zip')
        self.assertEqual(opener.requests[0].get_header('Authorization'), 'Bearer secret')
        self.assertIsNone(opener.requests[1].get_header('Authorization'))
        self.assertIsNone(artifact.NoRedirect().redirect_request(None, None, None, None, None, None))


if __name__ == '__main__':
    unittest.main()

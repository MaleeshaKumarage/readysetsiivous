"""Download authenticated, digest-bound data artifacts without extracting paths.

GitHub REST artifact metadata and ZIP redirect contract:
https://docs.github.com/en/rest/actions/artifacts#get-an-artifact
https://docs.github.com/en/rest/actions/artifacts#download-an-artifact
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import stat
import urllib.error
import urllib.parse
import urllib.request
import zipfile

from agent_contract import require

LIMIT = 2 * 1024 * 1024


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate JSON key')
        result[key] = value
    return result


def parse_json(raw):
    return json.loads(raw, object_pairs_hook=unique_object,
                      parse_constant=lambda value: require(False, 'nonfinite JSON'))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class Transport:
    def __init__(self, token):
        require(isinstance(token, str) and token and not any(c in token for c in '\r\n'), 'token')
        self.token = token
        # No ambient proxies, cookie jar or automatic redirects on either hop.
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def get(self, url, authenticated=False):
        headers = {'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'}
        if authenticated:
            require(url.startswith('https://api.github.com/repos/'), 'API host')
            headers['Authorization'] = 'Bearer ' + self.token
        request = urllib.request.Request(url, headers=headers, method='GET')
        try:
            response = self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as error:
            if error.code != 302:
                raise ValueError('artifact HTTP request failed') from None
            response = error
        with response:
            require(response.geturl() == url, 'unexpected redirect')
            status = response.code
            if status == 302:
                return status, response.headers.get('Location'), b''
            require(status == 200, 'artifact HTTP status')
            body = response.read(LIMIT + 1)
            require(len(body) <= LIMIT, 'response size')
            return status, None, body


class Client:
    def __init__(self, repository, transport):
        self.repository, self.transport = repository, transport

    def request(self, method, path, payload=None):
        require(method == 'GET' and payload is None, 'read only')
        require(path == '/repos/' + self.repository or path.startswith('/repos/' + self.repository + '/'), 'repository boundary')
        status, _, raw = self.transport.get('https://api.github.com' + path, True)
        require(status == 200, 'metadata redirect')
        value = parse_json(raw)
        require(type(value) is dict, 'metadata object')
        return value


def storage_url(url):
    require(isinstance(url, str) and not any(c.isspace() or ord(c) < 32 for c in url), 'storage URL')
    parsed = urllib.parse.urlsplit(url)
    require(parsed.scheme == 'https' and parsed.username is None and parsed.password is None and not parsed.fragment and parsed.port in (None, 443), 'storage URL')
    host = parsed.hostname or ''
    require(any(host.endswith(suffix) for suffix in ('.blob.core.windows.net', '.actions.githubusercontent.com', '.githubusercontent.com')), 'storage host')
    require('\\' not in url, 'storage URL')
    return url


def artifact_json(raw, kind):
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(len(entries) == 1, 'artifact entry count')
        entry = entries[0]
        require(entry.filename == kind + '.json' and entry.orig_filename == entry.filename, 'artifact filename')
        mode = entry.external_attr >> 16
        require(not entry.is_dir() and stat.S_IFMT(mode) in (0, stat.S_IFREG) and not entry.flag_bits & 1, 'artifact file type')
        require(not entry.external_attr & 0x10 and 0 < entry.file_size <= LIMIT, 'artifact file size')
        with archive.open(entry) as stream:
            body = stream.read(LIMIT + 1)
        require(len(body) <= LIMIT and len(body) == entry.file_size, 'artifact content size')
    require(type(parse_json(body)) is dict, 'artifact JSON object')
    return body


def download(repository, artifact_id, digest, workflow_sha, kind, output, environ=None, transport=None):
    env = os.environ if environ is None else environ
    require(isinstance(repository, str) and re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository), 'repository')
    require(repository == env.get('GITHUB_REPOSITORY'), 'trusted repository')
    require(kind in ('snapshot', 'proposal', 'verification'), 'artifact kind')
    require(type(artifact_id) is int and artifact_id > 0, 'artifact ID')
    require(isinstance(digest, str) and re.fullmatch(r'(?:sha256:)?[0-9a-f]{64}', digest), 'artifact digest')
    digest = digest.removeprefix('sha256:')
    for key in ('GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT'):
        require(re.fullmatch(r'[1-9][0-9]*', env.get(key, '')) is not None, 'trusted run context')
    transport = transport or Transport(env['GH_TOKEN'])
    client = Client(repository, transport)
    repo = client.request('GET', '/repos/' + repository)
    require(type(repo.get('id')) is int and repo['id'] > 0 and repo.get('full_name') == repository, 'repository identity')
    require(isinstance(workflow_sha, str) and re.fullmatch('[0-9a-f]{40}', workflow_sha), 'workflow SHA')
    run_id, attempt, repo_id = int(env['GITHUB_RUN_ID']), int(env['GITHUB_RUN_ATTEMPT']), repo['id']
    metadata = client.request('GET', '/repos/' + repository + '/actions/artifacts/' + str(artifact_id))
    run = client.request('GET', '/repos/' + repository + '/actions/runs/' + str(run_id))
    require(type(metadata.get('id')) is int and metadata['id'] == artifact_id and metadata.get('expired') is False, 'artifact identity')
    require(type(metadata.get('size_in_bytes')) is int and 0 < metadata['size_in_bytes'] <= LIMIT, 'artifact size')
    require(metadata.get('digest') == 'sha256:' + digest and metadata.get('name') == 'agent-' + kind + '-' + str(attempt), 'artifact binding')
    provenance = metadata.get('workflow_run', {})
    require(type(provenance) is dict, 'artifact provenance')
    for key, expected in (('id', run_id), ('repository_id', repo_id), ('head_repository_id', repo_id), ('head_sha', workflow_sha)):
        require(type(provenance.get(key)) is type(expected) and provenance[key] == expected, 'artifact run binding')
    for key, expected in (('id', run_id), ('run_attempt', attempt), ('head_sha', workflow_sha)):
        require(type(run.get(key)) is type(expected) and run[key] == expected, 'current run binding')
    for key in ('repository', 'head_repository'):
        identity = run.get(key, {})
        require(type(identity) is dict and type(identity.get('id')) is int and identity['id'] == repo_id, 'run repository binding')
    status, location, raw = transport.get('https://api.github.com/repos/' + repository + '/actions/artifacts/' + str(artifact_id) + '/zip', True)
    if status == 302:
        status, _, raw = transport.get(storage_url(location), False)
    require(status == 200 and len(raw) <= LIMIT, 'ZIP response')
    require(hashlib.sha256(raw).hexdigest() == digest, 'ZIP digest mismatch')
    body = artifact_json(raw, kind)
    target = Path(output)
    target.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation refuses existing paths and symlinks. Never overwrite.
    fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(body)
    except BaseException:
        target.unlink(missing_ok=True)
        raise


def main():
    parser = argparse.ArgumentParser()
    for name in ('repo', 'digest', 'workflow-sha', 'output'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--artifact-id', required=True, type=int)
    parser.add_argument('--kind', required=True, choices=('snapshot', 'proposal', 'verification'))
    args = parser.parse_args()
    try:
        download(args.repo, args.artifact_id, args.digest, args.workflow_sha, args.kind, args.output)
    except Exception:
        # Signed URLs, tokens and artifact content must not reach Actions logs.
        parser.exit(1, 'Artifact download rejected.\n')


if __name__ == '__main__':
    main()

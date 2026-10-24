"""Data-only contracts for the isolated agent. No proposal code is executed.

REST references: https://docs.github.com/en/rest/git
https://docs.github.com/en/rest/actions/artifacts
https://docs.github.com/en/rest/collaborators/collaborators#get-repository-permissions-for-a-user
"""
import argparse
import base64
import hashlib
import json
import os
import re
import unicodedata
import urllib.error
import urllib.request
import urllib.parse
from pathlib import Path

MAX_FILE = 128 * 1024
MAX_TOTAL = 1024 * 1024
SNAPSHOT_KEYS = set('schema repository repository_id run_id run_attempt issue comment_id plan_sha256 plan_body plan_author_id plan_updated_at base_sha approver approver_id'.split())
PROPOSAL_KEYS = set('schema repository run_id run_attempt base_sha snapshot_sha256 files'.split())


def require(condition, message):
    if not condition:
        raise ValueError(message)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False).encode('utf-8')


def snapshot_digest(snapshot):
    return hashlib.sha256(canonical(validate_snapshot(snapshot))).hexdigest()


def positive(value):
    return type(value) is int and value > 0


def hexhash(value, length):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{%d}' % length, value) is not None


def validate_snapshot(snapshot):
    require(type(snapshot) is dict and set(snapshot) == SNAPSHOT_KEYS, 'snapshot fields')
    require(type(snapshot['schema']) is int and snapshot['schema'] == 1, 'snapshot schema')
    require(isinstance(snapshot['repository'], str) and re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', snapshot['repository']), 'repository')
    for key in ('repository_id', 'run_id', 'run_attempt', 'issue', 'comment_id', 'plan_author_id', 'approver_id'):
        require(positive(snapshot[key]), key)
    require(hexhash(snapshot['base_sha'], 40) and hexhash(snapshot['plan_sha256'], 64), 'hash')
    require(isinstance(snapshot['plan_body'], str) and 0 < len(snapshot['plan_body'].encode('utf-8')) <= MAX_FILE, 'plan body')
    require(hashlib.sha256(snapshot['plan_body'].encode('utf-8')).hexdigest() == snapshot['plan_sha256'], 'plan hash')
    require(isinstance(snapshot['plan_updated_at'], str) and re.fullmatch(r'\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ', snapshot['plan_updated_at']), 'updated timestamp')
    require(isinstance(snapshot['approver'], str) and re.fullmatch(r'[A-Za-z0-9-]{1,39}', snapshot['approver']), 'approver')
    return snapshot


def source_path(path):
    require(isinstance(path, str) and len(path) <= 240, 'path')
    require(path == unicodedata.normalize('NFC', path) and path.isascii(), 'path normalization')
    require('\\' not in path and not any(ord(c) < 32 for c in path), 'path separators')
    parts = path.split('/')
    require(all(p and p not in ('.', '..') and not p.endswith((' ', '.')) and ':' not in p for p in parts), 'path alias')
    require(all(not re.fullmatch(r'(?i)(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\..*)?', p) for p in parts), 'device path')
    require(parts[0] in ('api', 'site') and len(parts) >= 3, 'source root')
    require(parts[1] == 'src' if parts[0] == 'site' else parts[1].startswith('CleaningSuite.'), 'source root')
    denied = {'deploy', 'dev', 'docker', 'node_modules', 'bin', 'obj', '.git', '.github', '.agents', '.codex'}
    require(not any(p.casefold() in denied or p.startswith('.') for p in parts), 'control path')
    require(Path(path).suffix.lower() in {'.cs', '.ts', '.tsx', '.js', '.jsx', '.css', '.scss', '.html'}, 'source extension')
    require(parts[-1].casefold() not in {'config.ts', 'config.js', 'config.cs', 'manifest.ts', 'manifest.js'}, 'configuration path')
    return path


def validate_proposal(proposal, snapshot):
    validate_snapshot(snapshot)
    require(type(proposal) is dict and set(proposal) == PROPOSAL_KEYS, 'proposal fields')
    require(type(proposal['schema']) is int and proposal['schema'] == 1, 'proposal schema')
    for key in ('repository', 'run_id', 'run_attempt', 'base_sha'):
        require(type(proposal[key]) is type(snapshot[key]) and proposal[key] == snapshot[key], 'proposal binding: ' + key)
    require(proposal['snapshot_sha256'] == snapshot_digest(snapshot), 'snapshot digest')
    require(type(proposal['files']) is list and 0 < len(proposal['files']) <= 20, 'file count')
    seen, prefixes, total = set(), {}, 0
    for entry in proposal['files']:
        require(type(entry) is dict and set(entry) == {'path', 'content'}, 'file fields')
        path = source_path(entry['path'])
        require(path.casefold() not in seen, 'duplicate path')
        seen.add(path.casefold())
        parts = path.split('/')
        for index in range(1, len(parts) + 1):
            prefix = '/'.join(parts[:index])
            folded = prefix.casefold()
            role = 'file' if index == len(parts) else 'directory'
            previous = prefixes.get(folded)
            require(previous is None or previous == (prefix, role) and role == 'directory', 'proposal prefix conflict')
            prefixes[folded] = (prefix, role)
        content = entry['content']
        require(content is None or isinstance(content, str), 'text content')
        if content is not None:
            require(not any(ord(c) < 32 and c not in '\n\r\t' for c in content), 'binary content')
            size = len(content.encode('utf-8'))
            require(size <= MAX_FILE, 'file size')
            total += size
    require(total <= MAX_TOTAL, 'total size')
    return proposal


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ValueError('GitHub redirect rejected')


class GitHubClient:
    def __init__(self, token, repository):
        require(re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository) is not None, 'repository')
        require(isinstance(token, str) and token and '\n' not in token and '\r' not in token, 'token')
        self.repository = repository
        self.token = token
        self.opener = urllib.request.build_opener(NoRedirect())

    def request(self, method, path, payload=None):
        require(method in {'GET', 'POST', 'PATCH', 'DELETE'}, 'method')
        require(path.startswith('/repos/' + self.repository + '/') or path == '/repos/' + self.repository, 'API repository boundary')
        route, separator, query = path.partition('?')
        require(not any(c in route for c in ('#', '\\', '\r', '\n', '%')) and '..' not in route, 'API path')
        if separator:
            valid_tree = query == 'recursive=1' and '/git/trees/' in route
            owner = re.escape(self.repository.split('/')[0])
            valid_pulls = route == '/repos/' + self.repository + '/pulls' and re.fullmatch(
                r'state=all&per_page=100&head=' + owner + r'%3Aagent%2Fissue-[1-9][0-9]*%2Frun-[1-9][0-9]*-[1-9][0-9]*', query)
            require(valid_tree or valid_pulls, 'API query')
        request = urllib.request.Request('https://api.github.com' + path, data=None if payload is None else canonical(payload), method=method, headers={'Authorization': 'Bearer ' + self.token, 'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28', 'Content-Type': 'application/json'})
        with self.opener.open(request, timeout=30) as response:
            require(response.geturl() == request.full_url, 'redirect')
            raw = response.read(2 * MAX_TOTAL + 1)
            require(len(raw) <= 2 * MAX_TOTAL, 'API response size')
            return json.loads(raw) if raw else {}


def api(client, suffix, method='GET', payload=None):
    return client.request(method, '/repos/' + client.repository + suffix, payload)


def permission(client, login, user_id):
    data = api(client, '/collaborators/' + login + '/permission')
    require(data.get('permission') in ('write', 'maintain', 'admin'), 'maintainer permission')
    require(data.get('user', {}).get('id') == user_id, 'user identity')


def capture_snapshot(client, issue, comment_id, plan_sha256, base_sha, run_id, run_attempt, actor):
    require(all(positive(v) for v in (issue, comment_id, run_id, run_attempt)), 'numeric request identity')
    require(isinstance(actor, str) and re.fullmatch(r'[A-Za-z0-9-]{1,39}', actor), 'actor')
    require(hexhash(base_sha, 40) and hexhash(plan_sha256, 64), 'approved hashes')
    repo = api(client, '')
    comment = api(client, '/issues/comments/' + str(comment_id))
    require(comment.get('issue_url') == 'https://api.github.com/repos/' + client.repository + '/issues/' + str(issue), 'comment issue')
    author = comment['user']
    require(re.fullmatch(r'[A-Za-z0-9-]{1,39}', author['login']) is not None, 'author login')
    permission(client, author['login'], author['id'])
    approver = api(client, '/collaborators/' + actor + '/permission')
    require(approver.get('permission') in ('write', 'maintain', 'admin'), 'approver permission')
    snapshot = dict(schema=1, repository=client.repository, repository_id=repo['id'], run_id=run_id, run_attempt=run_attempt, issue=issue, comment_id=comment_id, plan_sha256=plan_sha256, plan_body=comment['body'], plan_author_id=author['id'], plan_updated_at=comment['updated_at'], base_sha=base_sha, approver=actor, approver_id=approver['user']['id'])
    validate_snapshot(snapshot)
    require(api(client, '/git/ref/heads/' + repo['default_branch'])['object']['sha'] == base_sha, 'stale base')
    return snapshot


def revalidate_snapshot(client, snapshot):
    validate_snapshot(snapshot)
    require(client.repository == snapshot['repository'], 'client repository')
    current = capture_snapshot(client, snapshot['issue'], snapshot['comment_id'], snapshot['plan_sha256'], snapshot['base_sha'], snapshot['run_id'], snapshot['run_attempt'], snapshot['approver'])
    require(current == snapshot, 'snapshot changed')
    return snapshot


def verify_artifact(client, artifact_id, digest, snapshot, workflow_sha, kind="proposal"):
    validate_snapshot(snapshot)
    require(client.repository == snapshot['repository'] and positive(artifact_id), 'artifact identity')
    if hexhash(digest, 64):
        digest = 'sha256:' + digest
    require(hexhash(workflow_sha, 40) and isinstance(digest, str) and re.fullmatch(r'sha256:[0-9a-f]{64}', digest), 'artifact digest')
    require(kind in ('snapshot', 'proposal', 'verification'), 'artifact kind')
    artifact = api(client, '/actions/artifacts/' + str(artifact_id))
    run = api(client, '/actions/runs/' + str(snapshot['run_id']))
    provenance = artifact.get('workflow_run', {})
    require(artifact.get('id') == artifact_id and artifact.get('expired') is False, 'artifact unavailable')
    require(type(artifact.get('size_in_bytes')) is int and 0 < artifact['size_in_bytes'] <= 2 * MAX_TOTAL, 'artifact size')
    require(artifact.get('digest') == digest and artifact.get('name') == 'agent-' + kind + '-' + str(snapshot['run_attempt']), 'artifact binding')
    require(provenance.get('id') == snapshot['run_id'] and provenance.get('repository_id') == snapshot['repository_id'] and provenance.get('head_repository_id') == snapshot['repository_id'] and provenance.get('head_sha') == workflow_sha, 'artifact run')
    require(run.get('id') == snapshot['run_id'] and run.get('run_attempt') == snapshot['run_attempt'] and run.get('head_sha') == workflow_sha and run.get('repository', {}).get('id') == snapshot['repository_id'] and run.get('head_repository', {}).get('id') == snapshot['repository_id'], 'run binding')
    return artifact


def validate_verification(receipt, proposal, snapshot):
    validate_proposal(proposal, snapshot)
    expected = {'schema': 1, 'status': 'passed', 'repository': snapshot['repository'], 'run_id': snapshot['run_id'], 'run_attempt': snapshot['run_attempt'], 'base_sha': snapshot['base_sha'], 'snapshot_sha256': snapshot_digest(snapshot), 'proposal_sha256': hashlib.sha256(canonical(proposal)).hexdigest()}
    require(type(receipt) is dict and canonical(receipt) == canonical(expected), 'verification receipt')
    return receipt

def optional(client, suffix):
    try:
        return api(client, suffix)
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


def publish(client, proposal, snapshot, verification=None):
    validate_proposal(proposal, snapshot)
    validate_verification(verification, proposal, snapshot)
    revalidate_snapshot(client, snapshot)
    base = api(client, '/git/commits/' + snapshot['base_sha'])
    base_tree = base['tree']['sha']
    listing = api(client, '/git/trees/' + base_tree + '?recursive=1')
    require(listing.get('truncated') is False, 'truncated base tree')
    entries = {e['path']: e for e in listing['tree']}
    for item in proposal['files']:
        parts = item['path'].split('/')
        for index in range(1, len(parts)):
            ancestor = '/'.join(parts[:index])
            require(not any(p.casefold() == ancestor.casefold() and p != ancestor for p in entries), 'base ancestor alias')
            parent = entries.get(ancestor)
            require(parent is None or parent.get('type') == 'tree' and parent.get('mode') == '040000', 'non-directory ancestor')
        old = entries.get(item['path'])
        require(old is None or old.get('type') == 'blob' and old.get('mode') in ('100644', '100755'), 'nonregular source')
        require(item['content'] is not None or old is not None, 'delete missing file')
        require(not any(p.casefold() == item['path'].casefold() and p != item['path'] for p in entries), 'base path alias')
    digest = hashlib.sha256(canonical(proposal)).hexdigest()
    branch = 'agent/issue-%s/run-%s-%s' % (snapshot['issue'], snapshot['run_id'], snapshot['run_attempt'])
    message = 'Agent proposal sha256:' + digest + '\nsnapshot:' + snapshot_digest(snapshot)
    existing = optional(client, '/git/ref/heads/' + branch)
    # Preflight every publication blocker before the first blob/tree/ref write.
    pulls = api(client, '/pulls?' + urllib.parse.urlencode({'state': 'all', 'per_page': 100, 'head': client.repository.split('/')[0] + ':' + branch}))
    require(type(pulls) is list and len(pulls) < 100, 'PR listing requires reconciliation')
    matches = [p for p in pulls if p.get('head', {}).get('ref') == branch]
    if matches:
        require(existing is not None and len(matches) == 1, 'PR ownership conflict')
        candidate = matches[0]
        require(candidate.get('draft') is True and candidate.get('state') == 'open' and candidate['head'].get('repo', {}).get('id') == snapshot['repository_id'] and candidate.get('base', {}).get('sha') == snapshot['base_sha'] and candidate['head'].get('sha') == existing['object']['sha'] and candidate.get('body') == message, 'PR ownership conflict')
    if existing:
        commit = api(client, '/git/commits/' + existing['object']['sha'])
        require(commit.get('message') == message and [p['sha'] for p in commit['parents']] == [snapshot['base_sha']], 'branch ownership conflict')
        expected = dict(entries)
        for item in proposal['files']:
            if item['content'] is None:
                expected.pop(item['path'], None)
            else:
                raw = item['content'].encode('utf-8')
                sha = hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest()
                expected[item['path']] = {'path': item['path'], 'type': 'blob', 'mode': '100644', 'sha': sha}
        actual = api(client, '/git/trees/' + commit['tree']['sha'] + '?recursive=1')
        require(actual.get('truncated') is False, 'truncated branch tree')
        # Compare leaves; directory SHAs necessarily change with edited descendants.
        leaves = lambda values: {(e['path'], e['mode'], e['type'], e['sha']) for e in values if e['type'] != 'tree'}
        require(leaves(actual['tree']) == leaves(expected.values()), 'branch tree conflict')
        commit_sha = existing['object']['sha']
    else:
        tree = []
        for item in proposal['files']:
            sha = None
            if item['content'] is not None:
                sha = api(client, '/git/blobs', 'POST', {'content': base64.b64encode(item['content'].encode()).decode(), 'encoding': 'base64'})['sha']
            tree.append({'path': item['path'], 'mode': '100644', 'type': 'blob', 'sha': sha})
        created = api(client, '/git/trees', 'POST', {'base_tree': base_tree, 'tree': tree})
        commit_sha = api(client, '/git/commits', 'POST', {'message': message, 'tree': created['sha'], 'parents': [snapshot['base_sha']]})['sha']
        api(client, '/git/refs', 'POST', {'ref': 'refs/heads/' + branch, 'sha': commit_sha})
    if matches:
        pr = matches[0]
    else:
        repo = api(client, '')
        pr = api(client, '/pulls', 'POST', {'title': 'Agent draft for issue #' + str(snapshot['issue']), 'head': branch, 'base': repo['default_branch'], 'body': message, 'draft': True})
    return {'state': 'draft-pr-created', 'branch': branch, 'commit_sha': commit_sha, 'proposal_sha256': digest, 'pull_request_url': pr['html_url']}


def main():
    parser = argparse.ArgumentParser()
    subs = parser.add_subparsers(dest='command', required=True)
    prepare = subs.add_parser('prepare')
    for name in ('repo', 'plan-sha256', 'base-sha', 'actor', 'output'):
        prepare.add_argument('--' + name, required=True)
    for name in ('issue', 'comment', 'run-id', 'run-attempt'):
        prepare.add_argument('--' + name, required=True, type=int)
    publication = subs.add_parser('publish')
    publication.add_argument('--proposal', required=True)
    publication.add_argument('--verification', required=True)
    publication.add_argument('--output', required=True)
    artifact = subs.add_parser('check-artifact')
    artifact.add_argument('--kind', choices=('snapshot','proposal','verification'), required=True)
    artifact.add_argument('--artifact-id', required=True, type=int)
    artifact.add_argument('--digest', required=True)
    artifact.add_argument('--workflow-sha', required=True)
    for sub in (publication, artifact):
        sub.add_argument('--snapshot', required=True)
        sub.add_argument('--expected-snapshot-sha256', required=True)
    args = parser.parse_args()
    def read(path):
        with Path(path).open('rb') as stream:
            raw = stream.read(2 * MAX_TOTAL + 1)
        require(len(raw) <= 2 * MAX_TOTAL, 'input size')
        return json.loads(raw)
    if args.command == 'prepare':
        client = GitHubClient(os.environ['GH_TOKEN'], args.repo)
        result = capture_snapshot(client, args.issue, args.comment, args.plan_sha256, args.base_sha, args.run_id, args.run_attempt, args.actor)
    else:
        snapshot = validate_snapshot(read(args.snapshot))
        require(snapshot_digest(snapshot) == args.expected_snapshot_sha256, 'trusted snapshot digest')
        for key, env in (('repository', 'GITHUB_REPOSITORY'), ('run_id', 'GITHUB_RUN_ID'), ('run_attempt', 'GITHUB_RUN_ATTEMPT')):
            require(env in os.environ and str(snapshot[key]) == os.environ[env], 'trusted run context')
        client = GitHubClient(os.environ['GH_TOKEN'], snapshot['repository'])
        if args.command == 'check-artifact':
            verify_artifact(client, args.artifact_id, args.digest, snapshot, args.workflow_sha, args.kind)
            return
        result = publish(client, read(args.proposal), snapshot, read(args.verification))
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_bytes(canonical(result) + b'\n')


if __name__ == '__main__':
    main()







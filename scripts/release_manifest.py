"""Bind a deployment to a successful same-repository main build and exact artifact bytes.

API contracts: https://docs.github.com/en/rest/actions/workflow-runs
https://docs.github.com/en/rest/actions/artifacts
Build digest: https://github.com/docker/build-push-action#outputs
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import zipfile

from agent_artifact import Client, Transport, parse_json, storage_url

IMAGE = 'ghcr.io/maleeshakumarage/readysetsiivous-api'
ASSETS = ('api/deploy/docker-compose.yml', 'api/deploy/release-policy.json',
          'api/deploy/check-keycloak-runtime-env.py',
          'scripts/deploy-api.sh', 'scripts/release_deploy.py')
WORKFLOW = '.github/workflows/docker-build-push.yml'
LIMIT = 65536


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(value):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{40}', value) is not None


def validate(value, repository, release_sha, run_id=None, attempt=None):
    require(type(value) is dict and set(value) == {'schema', 'repository', 'release_sha', 'run_id', 'run_attempt', 'image', 'assets'}, 'manifest shape')
    require(value['schema'] == 1 and value['repository'] == repository and value['release_sha'] == release_sha and sha(release_sha), 'manifest release binding')
    require(type(value['run_id']) is int and value['run_id'] > 0 and type(value['run_attempt']) is int and value['run_attempt'] > 0, 'manifest run')
    require(run_id is None or value['run_id'] == run_id, 'manifest run binding')
    require(attempt is None or value['run_attempt'] == attempt, 'manifest attempt binding')
    require(isinstance(value['image'], str) and re.fullmatch(re.escape(IMAGE) + r'@sha256:[0-9a-f]{64}', value['image']), 'manifest immutable image')
    require(type(value['assets']) is dict and set(value['assets']) == set(ASSETS), 'manifest assets')
    require(all(isinstance(v, str) and re.fullmatch('[0-9a-f]{64}', v) for v in value['assets'].values()), 'asset hash')
    return value


def create(root, environment):
    value = dict(schema=1, repository=environment['GITHUB_REPOSITORY'], release_sha=environment['GITHUB_SHA'],
                 run_id=int(environment['GITHUB_RUN_ID']), run_attempt=int(environment['GITHUB_RUN_ATTEMPT']),
                 image=IMAGE + '@' + environment['BUILD_DIGEST'],
                 assets={p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in ASSETS})
    return validate(value, value['repository'], value['release_sha'])


def resolve(repository, release_sha, run_id, client, transport):
    require(re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository) and sha(release_sha), 'release inputs')
    prefix = '/repos/' + repository
    repo = client.request('GET', prefix)
    workflow = client.request('GET', prefix + '/actions/workflows/docker-build-push.yml')
    require(workflow.get('path') == WORKFLOW and type(workflow.get('id')) is int, 'workflow identity')
    if not run_id:
        runs = client.request('GET', prefix + '/actions/workflows/docker-build-push.yml/runs?head_sha=' + release_sha + '&branch=main&status=success&per_page=100')
        require(type(runs.get('workflow_runs')) is list and runs.get('total_count', 0) <= 100, 'bounded workflow lookup')
        candidates = [r for r in runs['workflow_runs'] if r.get('head_sha') == release_sha and r.get('conclusion') == 'success']
        require(candidates, 'no successful release build')
        run_id = max(r['id'] for r in candidates)
    require(type(run_id) is int and run_id > 0, 'run id')
    run = client.request('GET', prefix + '/actions/runs/' + str(run_id))
    require(run.get('id') == run_id and run.get('workflow_id') == workflow['id'] and run.get('path') == WORKFLOW, 'build workflow binding')
    require(run.get('head_repository', {}).get('id') == repo.get('id') and type(repo.get('id')) is int, 'build repository binding')
    require(run.get('head_sha') == release_sha and run.get('head_branch') == 'main' and run.get('event') == 'push'
            and run.get('status') == 'completed' and run.get('conclusion') == 'success', 'successful main build required')
    attempt = run.get('run_attempt')
    require(type(attempt) is int and attempt > 0, 'build attempt')
    listing = client.request('GET', prefix + '/actions/runs/' + str(run_id) + '/artifacts?per_page=100')
    require(listing.get('total_count', 0) <= 100 and type(listing.get('artifacts')) is list, 'bounded artifact lookup')
    name = f'api-release-{release_sha}-{attempt}'
    matches = [a for a in listing['artifacts'] if a.get('name') == name]
    require(len(matches) == 1, 'unique release artifact required')
    artifact_id = matches[0].get('id')
    require(type(artifact_id) is int and artifact_id > 0, 'artifact id')
    artifact = client.request('GET', prefix + '/actions/artifacts/' + str(artifact_id))
    binding = artifact.get('workflow_run', {})
    require(artifact.get('id') == artifact_id and artifact.get('name') == name and artifact.get('expired') is False, 'artifact identity')
    require(binding.get('id') == run_id and binding.get('head_sha') == release_sha
            and binding.get('repository_id') == repo['id'] and binding.get('head_repository_id') == repo['id'], 'artifact provenance')
    require(type(artifact.get('size_in_bytes')) is int and 0 < artifact['size_in_bytes'] <= LIMIT, 'artifact size')
    digest = artifact.get('digest', '')
    require(isinstance(digest, str) and re.fullmatch('sha256:[0-9a-f]{64}', digest), 'artifact digest')
    status, location, _ = transport.get('https://api.github.com' + prefix + '/actions/artifacts/' + str(artifact_id) + '/zip', True)
    require(status == 302, 'artifact redirect')
    status, _, raw = transport.get(storage_url(location), False)
    require(status == 200 and len(raw) <= LIMIT and len(raw) == artifact['size_in_bytes'], 'artifact bytes')
    require('sha256:' + hashlib.sha256(raw).hexdigest() == digest, 'artifact ZIP digest mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(len(entries) == 1 and entries[0].filename == 'release.json' and entries[0].file_size <= 16384 and not entries[0].flag_bits & 1, 'artifact contents')
        value = validate(parse_json(archive.read(entries[0])), repository, release_sha, run_id, attempt)
    return value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['create', 'resolve', 'bundle'])
    parser.add_argument('--output', required=True)
    parser.add_argument('--root')
    parser.add_argument('--manifest')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    if args.mode == 'create':
        result = create(root, os.environ)
    elif args.mode == 'resolve':
        transport = Transport(os.environ['GH_TOKEN'])
        repository = os.environ['GITHUB_REPOSITORY']
        result = resolve(repository, os.environ['RELEASE_SHA'], int(os.environ.get('BUILD_RUN_ID') or '0'), Client(repository, transport), transport)
    else:
        root = Path(args.root).resolve()
        raw = Path(args.manifest).read_bytes()
        require(hashlib.sha256(raw).hexdigest() == os.environ['RELEASE_MANIFEST_HASH'], 'resolved manifest hash')
        result = validate(parse_json(raw), os.environ['GITHUB_REPOSITORY'], os.environ['RELEASE_SHA'])
        for path, digest in result['assets'].items():
            source = root / path
            require(not source.is_symlink() and hashlib.sha256(source.read_bytes()).hexdigest() == digest, 'checked-out release asset mismatch')
        output = Path(args.output)
        require(not output.exists(), 'bundle output already exists')
        output.mkdir()
        for path in ASSETS:
            destination = output / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes((root / path).read_bytes())
        (output / 'release.json').write_bytes(raw)
        return
    raw = (json.dumps(result, sort_keys=True, separators=(',', ':')) + '\n').encode()
    Path(args.output).write_bytes(raw)
    if args.mode == 'resolve':
        with open(os.environ['GITHUB_ENV'], 'a') as output:
            output.write('RELEASE_MANIFEST_HASH=' + hashlib.sha256(raw).hexdigest() + '\n')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, zipfile.BadZipFile):
        raise SystemExit('Release provenance validation failed; no deployment performed.') from None

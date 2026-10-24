import copy
import hashlib
import sys
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import agent_contract as c


def snapshot():
    return dict(schema=1, repository='org/repo', repository_id=1, run_id=2, run_attempt=1, issue=3, comment_id=4, plan_sha256=hashlib.sha256(b'plan').hexdigest(), plan_body='plan', plan_author_id=5, plan_updated_at='2026-10-24T00:00:00Z', base_sha='a'*40, approver='maintainer', approver_id=6)


def proposal(s):
    return dict(schema=1, repository=s['repository'], run_id=2, run_attempt=1, base_sha=s['base_sha'], snapshot_sha256=c.snapshot_digest(s), files=[{'path':'site/src/page.tsx','content':'hello'}])


def receipt(s,p):
    return dict(schema=1,status='passed',repository=s['repository'],run_id=2,run_attempt=1,base_sha=s['base_sha'],snapshot_sha256=c.snapshot_digest(s),proposal_sha256=hashlib.sha256(c.canonical(p)).hexdigest())


class Fake:
    repository='org/repo'
    def __init__(self):
        self.calls=[]
        self.s=snapshot()
        self.existing=None
        self.prs=[]
    def request(self, method, path, payload=None):
        self.calls.append((method,path,payload))
        suffix=path[len('/repos/org/repo'):]
        if suffix=='': return {'id':1,'default_branch':'main'}
        if suffix=='/issues/comments/4': return {'body':'plan','updated_at':self.s['plan_updated_at'],'user':{'id':5,'login':'author'},'issue_url':'https://api.github.com/repos/org/repo/issues/3'}
        if suffix.startswith('/collaborators/'):
            return {'permission':'write','user':{'id':5 if '/author/' in suffix else 6}}
        if suffix=='/git/ref/heads/main': return {'object':{'sha':self.s['base_sha']}}
        if suffix.startswith('/git/ref/heads/agent/'):
            if self.existing: return self.existing
            raise urllib.error.HTTPError('',404,'missing',{},None)
        if suffix=='/git/commits/'+self.s['base_sha']: return {'tree':{'sha':'base-tree'}}
        if suffix=='/git/trees/base-tree?recursive=1': return {'truncated':False,'tree':[]}
        if suffix=='/git/blobs': return {'sha':'blob'}
        if suffix=='/git/trees': return {'sha':'tree'}
        if suffix=='/git/commits':
            self.commit=payload
            return {'sha':'commit'}
        if suffix=='/git/refs':
            self.existing={'object':{'sha':'commit'}}
            return {}
        if suffix=='/git/commits/commit': return {'message':self.commit['message'],'parents':[{'sha':self.s['base_sha']}],'tree':{'sha':'tree'}}
        if suffix=='/git/trees/tree?recursive=1':
            raw=b'hello'
            return {'truncated':False,'tree':[{'path':'site/src/page.tsx','mode':'100644','type':'blob','sha':hashlib.sha1(b'blob 5\0'+raw).hexdigest()}]}
        if suffix.startswith('/pulls?state=all&per_page=100&head=') and method=='GET':
            head=c.urllib.parse.parse_qs(suffix.split('?',1)[1])['head'][0].split(':',1)[1]
            return [p for p in self.prs if p.get('head',{}).get('ref')==head][:100]
        if suffix=='/pulls' and method=='POST':
            result=dict(payload,html_url='https://github.com/org/repo/pull/1',state='open',base={'sha':self.s['base_sha']},head={'ref':payload['head'],'sha':'commit','repo':{'id':1}})
            self.prs=[result]
            return result
        raise AssertionError((method,suffix,payload))


class Contracts(unittest.TestCase):
    def test_schema_and_binding(self):
        s=snapshot(); p=proposal(s)
        self.assertEqual(c.validate_proposal(p,s),p)
        for key,value in [('extra',1),('run_id',True),('base_sha','A'*40),('plan_body','edited')]:
            hostile=dict(s); hostile[key]=value
            with self.subTest(key=key),self.assertRaises(ValueError): c.validate_snapshot(hostile)
        p['run_id']=3
        with self.assertRaises(ValueError): c.validate_proposal(p,s)

    def test_hostile_paths(self):
        for path in ['site/src/../x.ts','site/src\\x.ts','/site/src/x.ts','site/src/CON.ts','site/src/a.ts.','site/src/é.ts','site/src/node_modules/x.ts','api/deploy/x.cs','.github/workflows/a.yml','site/package.json','site/src/config.ts','api/CleaningSuite.Api/a.csproj']:
            with self.subTest(path=path),self.assertRaises(ValueError): c.source_path(path)

    def test_binary_size_duplicate(self):
        s=snapshot()
        for content in ['\0', 'x'*(c.MAX_FILE+1), '\ud800']:
            p=proposal(s);p['files'][0]['content']=content
            with self.assertRaises((ValueError,UnicodeError)): c.validate_proposal(p,s)
        p=proposal(s);p['files'].append({'path':'site/src/PAGE.tsx','content':None})
        with self.assertRaises(ValueError): c.validate_proposal(p,s)

    def test_aggregate_cap_and_prefix_conflicts(self):
        s=snapshot();p=proposal(s)
        p['files']=[{'path':'site/src/file%s.ts'%i,'content':'x'*c.MAX_FILE} for i in range(9)]
        with self.assertRaises(ValueError): c.validate_proposal(p,s)
        for paths in [('site/src/foo.ts','site/src/foo.ts/child.ts'), ('site/src/Foo/a.ts','site/src/foo/b.ts')]:
            for ordered in (paths, tuple(reversed(paths))):
                p=proposal(s);p['files']=[{'path':path,'content':'text'} for path in ordered]
                f=Fake()
                with self.subTest(paths=ordered),self.assertRaises(ValueError): c.publish(f,p,s,receipt(s,p))
                self.assertEqual(f.calls,[])

    def test_pr_preflight_blockers_before_writes(self):
        s=snapshot();p=proposal(s)
        branch='agent/issue-3/run-2-1'
        for pulls in ([{'head':{'ref':branch}}]*100, [{'head':{'ref':branch},'draft':False}], [{'head':{'ref':branch},'state':'closed'}], [{'head':{'ref':branch}}]*2):
            f=Fake();f.prs=pulls
            with self.subTest(pulls=len(pulls)),self.assertRaises(ValueError): c.publish(f,p,s,receipt(s,p))
            self.assertFalse(any(call[0]!='GET' for call in f.calls))

    def test_unrelated_pr_history_does_not_block_publication(self):
        f=Fake();f.prs=[{'head':{'ref':'unrelated-'+str(i)}} for i in range(200)]
        s=snapshot();p=proposal(s)
        self.assertEqual(c.publish(f,p,s,receipt(s,p))['state'],'draft-pr-created')

    def test_successful_delete_uses_null_tree_sha(self):
        f=Fake();original=f.request
        def wrapped(method,path,payload=None):
            if path.endswith('base-tree?recursive=1'):
                return {'truncated':False,'tree':[{'path':'site/src/page.tsx','type':'blob','mode':'100644','sha':'old'}]}
            return original(method,path,payload)
        f.request=wrapped;s=snapshot();p=proposal(s);p['files'][0]['content']=None
        self.assertEqual(c.publish(f,p,s,receipt(s,p))['state'],'draft-pr-created')
        tree=[call[2] for call in f.calls if call[0]=='POST' and call[1].endswith('/git/trees')][0]
        self.assertEqual(tree,{'base_tree':'base-tree','tree':[{'path':'site/src/page.tsx','mode':'100644','type':'blob','sha':None}]})
        self.assertFalse(any(call[1].endswith('/git/blobs') for call in f.calls))

    def test_capture_and_revalidation(self):
        f=Fake();s=snapshot()
        self.assertEqual(c.revalidate_snapshot(f,s),s)
        for change in [('plan_author_id',9),('plan_updated_at','2026-10-23T00:00:00Z'),('base_sha','b'*40),('approver_id',9)]:
            hostile=dict(s);hostile[change[0]]=change[1]
            with self.subTest(change=change),self.assertRaises(ValueError): c.revalidate_snapshot(f,hostile)

    def test_no_writes_before_validation(self):
        f=Fake();s=snapshot();p=proposal(s)
        with self.assertRaises(ValueError): c.publish(f,p,s)
        self.assertEqual(f.calls,[])
        p['files'][0]['path']='.github/workflows/pwn.yml'
        with self.assertRaises(ValueError): c.publish(f,p,s,{})
        self.assertEqual(f.calls,[])
        p=proposal(s);s['approver_id']=99;p['snapshot_sha256']=c.snapshot_digest(s)
        with self.assertRaises(ValueError): c.publish(f,p,s,receipt(s,p))
        self.assertFalse(any(call[0]!='GET' for call in f.calls))

    def test_publication_replay_recovery_conflict(self):
        f=Fake();s=snapshot();p=proposal(s);r=receipt(s,p)
        first=c.publish(f,p,s,r)
        self.assertEqual(first['state'],'draft-pr-created')
        self.assertEqual([x[0] for x in f.calls].count('PATCH'),0)
        f.prs=[] # cancellation after branch creation, before PR creation
        f.calls=[]
        c.publish(f,p,s,r)
        self.assertEqual([x[1] for x in f.calls if x[0]=='POST'],['/repos/org/repo/pulls'])
        f.calls=[]
        c.publish(f,p,s,r)
        self.assertFalse(any(x[0]!='GET' for x in f.calls))
        f.commit['message']='unrelated'
        with self.assertRaises(ValueError): c.publish(f,p,s,r)

    def test_symlink_base_rejected_before_write(self):
        f=Fake();original=f.request
        def wrapped(method,path,payload=None):
            if path.endswith('base-tree?recursive=1'):
                return {'truncated':False,'tree':[{'path':'site/src/page.tsx','type':'blob','mode':'120000','sha':'evil'}]}
            return original(method,path,payload)
        f.request=wrapped;s=snapshot();p=proposal(s)
        with self.assertRaises(ValueError): c.publish(f,p,s,receipt(s,p))
        self.assertFalse(any(x[0]!='GET' for x in f.calls))

    def test_artifact_provenance(self):
        s=snapshot();f=Fake();digest='sha256:'+'d'*64;sha='f'*40
        artifact={'id':9,'expired':False,'size_in_bytes':100,'digest':digest,'name':'agent-proposal-1','workflow_run':{'id':2,'repository_id':1,'head_repository_id':1,'head_sha':sha}}
        run={'id':2,'run_attempt':1,'head_sha':sha,'repository':{'id':1},'head_repository':{'id':1}}
        f.request=lambda method,path,payload=None: artifact if '/artifacts/' in path else run
        self.assertEqual(c.verify_artifact(f,9,digest,s,sha),artifact)
        for key,value in [('expired',True),('size_in_bytes',c.MAX_TOTAL*3),('digest','sha256:'+'e'*64)]:
            old=artifact[key];artifact[key]=value
            with self.subTest(key=key),self.assertRaises(ValueError): c.verify_artifact(f,9,digest,s,sha)
            artifact[key]=old
        run['run_attempt']=2
        with self.assertRaises(ValueError): c.verify_artifact(f,9,digest,s,sha)

    def test_clock_source_and_ancestor_case_alias(self):
        self.assertEqual(c.source_path('api/CleaningSuite.Api/Clock.cs'), 'api/CleaningSuite.Api/Clock.cs')
        f=Fake();original=f.request
        def wrapped(method,path,payload=None):
            if path.endswith('base-tree?recursive=1'):
                return {'truncated':False,'tree':[{'path':'site/SRC','type':'tree','mode':'040000','sha':'existing'}]}
            return original(method,path,payload)
        f.request=wrapped;s=snapshot();p=proposal(s)
        with self.assertRaises(ValueError): c.publish(f,p,s,receipt(s,p))
        self.assertFalse(any(x[0]!='GET' for x in f.calls))
    def test_fixed_host_and_redirects(self):
        client=c.GitHubClient('dummy','org/repo')
        for path in ['https://evil.test','/repos/other/repo/issues','/repos/org/repo/../evil','/repos/org/repo/%2e']:
            with self.subTest(path=path),self.assertRaises(ValueError): client.request('GET',path)
        with self.assertRaises(ValueError): c.NoRedirect().redirect_request(None,None,None,None,None,None)

    def test_cli_output_parent_and_required_run_context(self):
        s=snapshot()
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'new'/'snapshot.json'
            args=['contract','prepare','--repo','org/repo','--issue','3','--comment','4','--plan-sha256',s['plan_sha256'],'--base-sha',s['base_sha'],'--run-id','2','--run-attempt','1','--actor','maintainer','--output',str(output)]
            with patch.object(sys,'argv',args),patch.dict(c.os.environ,{'GH_TOKEN':'dummy'},clear=True),patch.object(c,'GitHubClient',return_value=Fake()):
                c.main()
            self.assertEqual(c.json.loads(output.read_bytes()),s)
            args=['contract','check-artifact','--snapshot',str(output),'--expected-snapshot-sha256',c.snapshot_digest(s),'--artifact-id','9','--digest','d'*64,'--workflow-sha','f'*40,'--kind','snapshot']
            with patch.object(sys,'argv',args),patch.dict(c.os.environ,{'GH_TOKEN':'dummy'},clear=True),patch.object(c,'GitHubClient') as client:
                with self.assertRaisesRegex(ValueError,'trusted run context'): c.main()
                client.assert_not_called()


if __name__=='__main__': unittest.main()



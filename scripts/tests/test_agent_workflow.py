"""Offline regressions for the workflow's privilege and artifact boundaries."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def jobs(source):
    matches = list(re.finditer(r'^  ([a-z][a-z-]*):\n', source[source.index('jobs:\n') + 6:], re.M))
    body = source[source.index('jobs:\n') + 6:]
    return {m[1]: body[m.start():matches[i + 1].start() if i + 1 < len(matches) else len(body)] for i, m in enumerate(matches)}


class AgentWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = (ROOT / '.github/workflows/agent-pipeline.yml').read_text()
        cls.jobs = jobs(cls.source)

    def test_manual_exact_approval_inputs(self):
        triggers = self.source.split('permissions:', 1)[0]
        self.assertNotRegex(triggers, r'issue_comment:|schedule:|pull_request:|workflow_run:')
        self.assertIn('workflow_dispatch:', triggers)
        for name in ('issue-number', 'plan-comment-id', 'plan-sha256', 'base-sha'):
            self.assertRegex(triggers, rf'      {name}:\n(?:        .*\n)*?        required: true')
        self.assertEqual(set(self.jobs), {'control', 'generation', 'verification', 'publication'})

    def test_each_host_runs_trusted_control_and_default_branch_guard(self):
        for name, body in self.jobs.items():
            with self.subTest(job=name):
                self.assertIn("github.event_name == 'workflow_dispatch' && github.ref == format('refs/heads/{0}', github.event.repository.default_branch)", body)
                self.assertIn('runs-on: ubuntu-latest', body)
                self.assertIn('ref: ${{ github.sha }}', body)
                self.assertIn('persist-credentials: false', body)
                self.assertIn('timeout-minutes:', body)
        self.assertNotIn('self-hosted', self.source)
        self.assertNotRegex(self.source, r'\$\{\{[^\n]*\}\}[^\n]*\n(?=          (?:python|git))')

    def test_write_authority_only_in_publication(self):
        self.assertIn('permissions:\n  contents: read', self.source)
        for name in ('control', 'generation', 'verification'):
            self.assertNotIn(': write', self.jobs[name])
        publisher = self.jobs['publication']
        self.assertIn('contents: write', publisher)
        self.assertIn('pull-requests: write', publisher)
        self.assertNotIn('agent_sandbox.py', publisher)
        self.assertIn('--expected-snapshot-sha256 "$SNAPSHOT_HASH" --output publication.json', publisher)
        self.assertNotIn('secrets.', self.jobs['verification'])
        self.assertEqual(self.source.count('secrets.'), 1)
        self.assertIn('secrets.DEEPSEEK_API_KEY', self.jobs['generation'])

    def test_every_download_has_immutable_metadata_authentication(self):
        expected = {'generation': 1, 'verification': 2, 'publication': 3}
        for name, count in expected.items():
            body = self.jobs[name]
            with self.subTest(job=name):
                self.assertEqual(body.count('agent_artifact.py --repo'), count)
                self.assertNotIn('actions/download-artifact', body)
                self.assertEqual(body.count('--output snapshot/snapshot.json'), 1)
                self.assertEqual(body.count('agent_contract.py check-artifact'), count)
                self.assertEqual(body.count('--expected-snapshot-sha256 "$SNAPSHOT_HASH"'), count + (name == 'publication'))
                self.assertEqual(body.count('--workflow-sha "$WORKFLOW_SHA"'), count * 2)
        for kind in ('snapshot', 'proposal', 'verification'):
            self.assertIn(f'--kind {kind}', self.jobs['publication'])
        self.assertEqual(self.source.count('retention-days: 3'), 3)

    def test_generation_revalidates_before_secret_use_and_execution_is_container_wrapper(self):
        generation = self.jobs['generation']
        self.assertLess(generation.index('agent_contract.py prepare'), generation.index('secrets.DEEPSEEK_API_KEY'))
        self.assertIn("if actual != os.environ['SNAPSHOT_HASH']:", generation)
        self.assertIn('vars.AGENT_PYTHON_IMAGE', generation)
        self.assertIn('--actor "$GITHUB_TRIGGERING_ACTOR"', generation)
        self.assertIn("subprocess.run(['docker', 'pull', image], check=True)", generation)
        self.assertIn("@sha256:[0-9a-f]{64}", generation)
        self.assertIn('vars.AGENT_TOOLCHAIN_IMAGE', self.jobs['verification'])
        self.assertIn('AGENT_SOURCE_BASE_SHA:', self.jobs['verification'])
        for command in ('npm ci', 'dotnet restore', 'docker compose', 'seed-dev', 'deepseek_wrap', 'git push', '--force'):
            self.assertNotIn(command, self.source)
        self.assertIn('agent_sandbox.py generate --snapshot', generation)
        self.assertIn('agent_sandbox.py verify --snapshot', self.jobs['verification'])

    def test_security_tests_are_part_of_ci(self):
        ci = (ROOT / '.github/workflows/review-integrity-tests.yml').read_text()
        self.assertIn("-p 'test_agent*.py'", ci)


if __name__ == '__main__':
    unittest.main()

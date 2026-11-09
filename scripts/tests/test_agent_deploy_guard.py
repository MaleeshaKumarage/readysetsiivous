import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("agent_deploy_guard", ROOT / "scripts" / "agent_deploy_guard.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class GlobTests(unittest.TestCase):
    def matches(self, pattern, path):
        return bool(guard.glob_to_regex(pattern).match(path))

    def test_double_star_crosses_directories(self):
        self.assertTrue(self.matches("api/**", "api/CleaningSuite.Api/Controllers/PartnerController.cs"))
        self.assertFalse(self.matches("api/**", "site/src/x.tsx"))

    def test_single_star_stays_in_one_segment(self):
        self.assertTrue(self.matches("scripts/release_*.py", "scripts/release_x.py"))
        self.assertFalse(self.matches("scripts/release_*.py", "scripts/release/x.py"))

    def test_exact_file_and_leading_double_star(self):
        self.assertTrue(self.matches("global.json", "global.json"))
        self.assertFalse(self.matches("global.json", "api/global.json"))
        self.assertTrue(self.matches("**/Dockerfile", "api/Dockerfile"))
        self.assertTrue(self.matches("**/Dockerfile", "Dockerfile"))


class BranchTests(unittest.TestCase):
    def test_no_branch_filter_covers_main(self):
        self.assertTrue(guard.branch_applies({}))

    def test_branches_list_must_include_main(self):
        self.assertTrue(guard.branch_applies({"branches": ["main", "release/*"]}))
        self.assertFalse(guard.branch_applies({"branches": ["release/*"]}))

    def test_branches_ignore_excludes_only_named_branches(self):
        self.assertFalse(guard.branch_applies({"branches-ignore": ["main"]}))
        self.assertTrue(guard.branch_applies({"branches-ignore": ["feature/*"]}))


class PathTests(unittest.TestCase):
    files = ["docs/a.md", "api/x.cs"]

    def test_no_path_filter_triggers_on_any_file(self):
        self.assertEqual(guard.files_triggering({}, self.files), self.files)

    def test_positive_paths(self):
        self.assertEqual(guard.files_triggering({"paths": ["api/**"]}, self.files), ["api/x.cs"])

    def test_negated_path_excludes_docs_only(self):
        # "everything except docs" triggers on the API file, not on the docs file
        self.assertEqual(guard.files_triggering({"paths": ["!docs/**"]}, self.files), ["api/x.cs"])

    def test_paths_ignore_triggers_on_files_not_ignored(self):
        self.assertEqual(guard.files_triggering({"paths-ignore": ["docs/**"]}, self.files), ["api/x.cs"])
        self.assertEqual(guard.files_triggering({"paths-ignore": ["**"]}, self.files), [])


class DeployWorkflowTests(unittest.TestCase):
    def test_real_repo_has_the_api_deploy_workflow_on_main(self):
        workflows = guard.deploy_workflows("HEAD")
        self.assertIn(".github/workflows/docker-build-push.yml", workflows)

    def test_real_repo_ignores_workflows_without_a_main_push(self):
        self.assertNotIn(".github/workflows/proxy-config-tests.yml", guard.deploy_workflows("HEAD"))


if __name__ == "__main__":
    unittest.main()

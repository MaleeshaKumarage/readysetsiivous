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


class DeployFilterTests(unittest.TestCase):
    def test_push_to_main_deploy_workflow_contributes_its_paths(self):
        filters = guard.deploy_filters("origin/main")
        self.assertIn(".github/workflows/docker-build-push.yml", filters)
        self.assertIn("api/**", filters[".github/workflows/docker-build-push.yml"])

    def test_non_deploy_workflows_are_ignored(self):
        filters = guard.deploy_filters("origin/main")
        self.assertNotIn(".github/workflows/proxy-config-tests.yml", filters)


if __name__ == "__main__":
    unittest.main()

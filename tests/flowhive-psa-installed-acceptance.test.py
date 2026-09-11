"""Contract tests for the installed-release verification workflow.

These tests inspect the maintained entrypoint and workflow wiring only. They
never contact Test, consume secrets, approve an environment, or mutate an
installed release.
"""
from __future__ import annotations

import ast
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).parents[1]
WORKFLOW = ROOT / ".github/workflows/flowhive-psa-installed-acceptance.yml"
IDENTITY = ROOT / "scripts/release-test/verify-flowhive-installed-identity.py"
FLOWHIVE = ROOT / "scripts/release-test/run-flowhive-psa-live-uat.py"
ROLE = ROOT / "scripts/release-test/run-flowhive-my-role-browser.py"
MODULE025 = ROOT / "scripts/release-test/check-module025-installed-prerequisite.py"


class InstalledAcceptanceContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW.read_text()
        cls.identity = IDENTITY.read_text()
        cls.flowhive = FLOWHIVE.read_text()
        cls.role = ROLE.read_text()
        cls.module025 = MODULE025.read_text()

    def test_workflow_is_main_only_environment_protected_and_read_only(self):
        for token in (
            "workflow_dispatch:",
            "environment:\n      name: test",
            "permissions:\n  contents: read\n  actions: read",
            "TARGET_RELEASE_COMMIT: 95abbb0aa2445a33fda68e9de542f9446c3e2204",
            "INSTALLATION_RUN_ID: '34540010122'",
            "INSTALLATION_CONTROLLER_SHA: df6f7fc6d52495f83b0fd169047f5a249493db7e",
            "PREVIOUS_PLANNER_RUN_ID: 171e4430-95e4-4f80-be14-454dcc319ef2",
            'GITHUB_REF" == refs/heads/main',
            "git rev-parse origin/main",
        ):
            self.assertIn(token, self.workflow)
        for forbidden in (
            "azure/login",
            "azure/cli",
            "id-token: write",
            "docker build",
            "az containerapp",
            "az sql",
            "workflow enable",
            "workflow disable",
            "dispatch-flowhive-psa-test.mjs",
            "apply-flowhive-psa-migrations.sh",
            "build-and-run-flowhive-psa-migrations.sh",
        ):
            self.assertNotIn(forbidden, self.workflow)
        self.assertNotIn("EVIDENCE_DIR: ${{ runner.temp }}", self.workflow)
        self.assertIn("EVIDENCE_DIR: ${{ github.workspace }}/flowhive-installed-acceptance", self.workflow)
        self.assertIn("PSA_VERIFICATION_APPROVAL_FILE: ${{ github.workspace }}/flowhive-installed-acceptance/installed-approval.json", self.workflow)
        self.assertIn("if: always() && steps.identity.outcome != 'cancelled'", self.workflow)
        self.assertIn("path: ${{ github.workspace }}/flowhive-installed-acceptance", self.workflow)

    def test_installed_identity_is_immutable_and_server_checked(self):
        expected = {
            "applicationSha": "95abbb0aa2445a33fda68e9de542f9446c3e2204",
            "installationRunId": "34540010122",
            "controllerSha": "df6f7fc6d52495f83b0fd169047f5a249493db7e",
            "apiRevision": "ca-phd-test-api-westus3--m1bd-34540010122-1",
            "webRevision": "ca-phd-test-web-westus3--relw-34540010122-1",
        }
        for key, value in expected.items():
            self.assertIn(f'"{key}": "{value}"', self.identity)
        self.assertIn("/api/platform-operations/overview", self.identity)
        self.assertIn('observed == EXPECTED["applicationSha"]', self.identity)
        self.assertIn('"recorded_from_installation_evidence"', self.identity)
        self.assertIn('pm_local_login_fallback', self.identity)
        self.assertIn('PROJECTPULSE_M087_PASSWORD', self.workflow)
        self.assertIn('"productionMutation": False', self.identity)

    def test_flowhive_entrypoint_reconciles_before_one_generation_and_never_mocks(self):
        self.assertIn("PREVIOUS_PLANNER_RUN_ID", self.flowhive)
        self.assertIn("planner_run_snapshot", self.flowhive)
        self.assertIn("prior_planner_run_nonterminal", self.flowhive)
        self.assertIn("/ai-planner/runs/", self.flowhive)
        self.assertEqual(self.flowhive.count("client.start_posts += 1"), 1)
        self.assertNotIn("route.fulfill(", self.flowhive)
        self.assertNotIn("window.fetch =", self.flowhive)
        self.assertIn("/review-preview", self.flowhive)
        self.assertIn("/apply-reviewed", self.flowhive)

    def test_role_runner_is_real_browser_read_only_and_checks_reload_boundaries(self):
        for token in (
            "async_playwright",
            "#dashboard",
            "#project-intake",
            "#signed-handoff",
            "Work-task handoff",
            "Resource handoff",
            "await page.reload",
            "anonymous_handoff_access_not_denied",
            "browser_attempted_mutation",
            "browser_timeout_project_intake",
            "signed_handoff_navigation_leaked",
        ):
            self.assertIn(token, self.role)
        self.assertNotIn("route.fulfill(", self.role)
        self.assertNotIn("page.route", self.role)
        self.assertIn('parsed.method not in ("GET", "HEAD", "OPTIONS")', self.role)

    def test_module025_is_explicitly_blocking_without_fixture_mutation(self):
        self.assertIn("protectedTestUatRoleFixture", self.module025)
        self.assertIn("protected_module025_fixture_disabled_or_not_authorized", self.module025)
        self.assertIn('"fixtureMutation": False', self.module025)
        self.assertIn('request("/api/auth/local/login", "POST"', self.module025)
        self.assertIn('request("/api/auth/session/logout", "POST"', self.module025)
        for forbidden_path in (
            'request("/api/module025/sow-gsd/bootstrap", "POST"',
            'request("/api/module025/sow-gsd/bootstrap", "PUT"',
            'request("/api/module025/sow-gsd/bootstrap", "PATCH"',
            'request("/api/module025/sow-gsd/bootstrap", "DELETE"',
        ):
            self.assertNotIn(forbidden_path, self.module025)

    def test_scripts_parse_as_python(self):
        for source in (IDENTITY, FLOWHIVE, ROLE, MODULE025):
            ast.parse(source.read_text(), filename=str(source))


if __name__ == "__main__":
    unittest.main()

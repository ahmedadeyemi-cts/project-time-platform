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
DEPLOY = ROOT / ".github/workflows/projectpulse-deploy-test.yml"
IDENTITY = ROOT / "scripts/release-test/verify-flowhive-installed-identity.py"
FLOWHIVE = ROOT / "scripts/release-test/run-flowhive-psa-live-uat.py"
ROLE = ROOT / "scripts/release-test/run-flowhive-my-role-browser.py"
PLATFORM = ROOT / "src/backend/ProjectTime.Api/Modules/PlatformOperationsContracts.cs"
MODULE025 = ROOT / "scripts/release-test/check-module025-installed-prerequisite.py"
MODULE025_SA = ROOT / "scripts/release-test/run-module025-installed-sa-uat.py"
PLANNER = ROOT / "scripts/release-test/reconcile-flowhive-planner.py"


class InstalledAcceptanceContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW.read_text()
        cls.deploy = DEPLOY.read_text()
        cls.identity = IDENTITY.read_text()
        cls.flowhive = FLOWHIVE.read_text()
        cls.role = ROLE.read_text()
        cls.platform = PLATFORM.read_text()
        cls.module025 = MODULE025.read_text()
        cls.module025_sa = MODULE025_SA.read_text()
        cls.planner = PLANNER.read_text()

    def test_workflow_is_main_only_environment_protected_and_read_only(self):
        for token in (
            "workflow_dispatch:",
            "deployment_run_id:",
            "environment:\n      name: test",
            "permissions:\n  contents: read\n  actions: read",
            "DEPLOYMENT_RUN_ID: ${{ inputs.deployment_run_id }}",
            "INSTALLED_RELEASE_CONTEXT: ${{ github.workspace }}/flowhive-installed-acceptance/installed-release-context.json",
            "resolve-flowhive-installed-deployment.py",
            "PREVIOUS_PLANNER_RUN_ID: 171e4430-95e4-4f80-be14-454dcc319ef2",
            "PROJECTPULSE_TEST_UAT_ADMIN_EMAIL",
            "PROJECTPULSE_TEST_UAT_ADMIN_PASSWORD",
            "PROJECTPULSE_M025_SA_EMAIL",
            "PROJECTPULSE_M025_SA_PASSWORD",
            "PROJECTPULSE_M025_PM_EMAIL",
            "PROJECTPULSE_M025_PM_PASSWORD",
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
        for token in (
            "INSTALLED_RELEASE_CONTEXT",
            "installed_release_context_missing",
            "installed_api_source_mismatch",
            "installed_api_image_not_immutable",
            "installed_web_image_not_immutable",
        ):
            self.assertIn(token, self.identity)
        for historical in (
            "95abbb0aa2445a33fda68e9de542f9446c3e2204",
            "34540010122",
            "df6f7fc6d52495f83b0fd169047f5a249493db7e",
        ):
            self.assertNotIn(historical, self.identity)
        self.assertIn("/api/platform-operations/overview", self.identity)
        self.assertIn('observed == installed["applicationSha"]', self.identity)
        self.assertIn('"recorded_from_installation_evidence"', self.identity)
        self.assertIn('test_uat_session_missing', self.identity)
        self.assertIn('PROJECTPULSE_TEST_UAT_ADMIN_EMAIL', self.identity)
        self.assertIn('PROJECTPULSE_TEST_UAT_ADMIN_PASSWORD', self.identity)
        self.assertIn('/api/security/context', self.identity)
        self.assertIn('SYSTEM_ADMINISTRATION', self.identity)
        self.assertIn('MANAGE_ALL', self.identity)
        self.assertNotIn('pm_local_login_fallback', self.identity)
        self.assertIn('"productionMutation": False', self.identity)

    def test_release_marker_consumes_the_controller_written_source_variable(self):
        self.assertIn('"PROJECTPULSE_SOURCE_COMMIT"', self.platform)
        self.assertIn('"PROJECTPULSE_RELEASE_SHA",\n            "PROJECTPULSE_SOURCE_COMMIT"', self.platform)
        self.assertIn('PROJECTPULSE_SOURCE_COMMIT="$TARGET_RELEASE_COMMIT"', self.deploy)

    def test_planner_reconciliation_is_read_only_and_precedes_identity(self):
        for token in (
            "171e4430-95e4-4f80-be14-454dcc319ef2",
            "/ai-planner/runs/",
            "/ai-planner/runs/latest",
            '"generationPosts": 0',
            '"cancellationPosts": 0',
            '"businessWritesRequested": False',
            "/api/auth/session/logout",
        ):
            self.assertIn(token, self.planner)
        self.assertIn("Reconcile prior planner operation before any generation", self.workflow)
        self.assertIn("id: planner", self.workflow)
        self.assertIn("steps.planner.outcome", self.workflow)
        self.assertIn("id: sow", self.workflow)
        self.assertIn("steps.sow.outcome", self.workflow)
        self.assertNotIn('"/ai-planner/runs", "POST"', self.planner)

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
        self.assertIn("approvedSowScopeReady", self.flowhive)
        self.assertIn("readyForAiPlanner", self.flowhive)
        self.assertIn("approved_sow_changed_before_generation", self.flowhive)
        self.assertIn("/api/project-flowhive/portfolio", self.flowhive)
        self.assertNotIn("heather.schrock@ussignal.local", self.flowhive)
        self.assertNotIn("TEST_LOGIN_PASSWORD", self.flowhive)

    def test_role_runner_is_real_browser_read_only_and_checks_reload_boundaries(self):
        for token in (
            "async_playwright",
            "#user-guide",
            "#my-role-in-pulse",
            "My Role in Pulse",
            "data-role-journeys-launch",
            "rj-step-details",
            "#project-intake",
            "#signed-handoff",
            "Work-task handoff",
            "Resource handoff",
            "await page.reload",
            "anonymous_handoff_access_not_denied",
            "browser_attempted_mutation",
            "browser_timeout_role_welcome_dashboard",
            "signed_handoff_navigation_leaked",
        ):
            self.assertIn(token, self.role)
        self.assertNotIn("route.fulfill(", self.role)
        self.assertNotIn("page.route", self.role)
        self.assertIn('parsed.method not in ("GET", "HEAD", "OPTIONS")', self.role)
        self.assertIn('await page.goto(ORIGIN + "/#my-role-in-pulse"', self.role)
        self.assertIn('browserDiagnostics', self.role)
        self.assertIn('my-role-failure.png', self.role)
        self.assertNotIn("heather.schrock@ussignal.local", self.role)
        self.assertNotIn("TEST_LOGIN_PASSWORD", self.role)

    def test_planner_uses_pm_scope_without_generation_or_old_identity(self):
        for token in (
            "PROJECTPULSE_M025_PM_EMAIL",
            "PROJECTPULSE_M025_PM_PASSWORD",
            "/api/project-flowhive/portfolio",
            "pm_project_not_in_authorized_portfolio",
            '"generationPosts": 0',
        ):
            self.assertIn(token, self.planner)
        self.assertNotIn("heather.schrock@ussignal.local", self.planner)
        self.assertNotIn("TEST_LOGIN_PASSWORD", self.planner)

    def test_normal_solution_architect_sow_lifecycle_is_separate_from_fixture(self):
        for token in (
            'PROJECTPULSE_M025_SA_EMAIL',
            'PROJECTPULSE_M025_SA_PASSWORD',
            'protectedTestUatRoleFixture',
            'normal_solution_architect_role_missing',
            'report["generationPosts"] = 1',
            'module025_detailed_scope_generated',
            'sow.docx',
            'gsd.xlsx',
            'reopen',
            'browser_saved_edit_missing_after_reload',
            'fixtureMutation": False',
        ):
            self.assertIn(token, self.module025_sa)
        self.assertNotIn('PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_ENABLED', self.module025_sa)
        self.assertNotIn('route.fulfill(', self.module025_sa)

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

    def test_workflow_gates_normal_sa_evidence_not_exceptional_fixture(self):
        self.assertIn('module025-installed-sa-uat.json', self.workflow)
        self.assertIn('.normalAuthorizedSolutionArchitect == true', self.workflow)
        self.assertIn('.fixtureMutation == false', self.workflow)
        self.assertIn('.generationPosts == 1', self.workflow)
        self.assertIn('.retainedVersions.sowAndGsdDownloaded == true', self.workflow)
        self.assertIn('exceptional Module 025 fixture prerequisite remains informational', self.workflow)
        self.assertNotIn(".status == \"ready\"' \"$EVIDENCE_DIR/module025-installed-prerequisite.json\"", self.workflow)

    def test_scripts_parse_as_python(self):
        for source in (IDENTITY, FLOWHIVE, ROLE, MODULE025, MODULE025_SA, PLANNER):
            ast.parse(source.read_text(), filename=str(source))


if __name__ == "__main__":
    unittest.main()

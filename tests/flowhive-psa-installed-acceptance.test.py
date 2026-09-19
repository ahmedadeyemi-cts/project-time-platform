"""Contract tests for the installed-release verification workflow.

These tests inspect the maintained entrypoint and workflow wiring only. They
never contact Test, consume secrets, approve an environment, or mutate an
installed release.
"""
from __future__ import annotations

import ast
import asyncio
import copy
import hashlib
import json
import importlib.util
import io
import os
import secrets
import tempfile
import zipfile
from unittest.mock import patch

import yaml
from module025_qualification_workflow import deployment_projection
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
PREFLIGHT = ROOT / "scripts/release-test/check-flowhive-acceptance-inputs.py"
RESOLVER = ROOT / "scripts/release-test/resolve-flowhive-installed-deployment.py"


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
        cls.resolver = RESOLVER.read_text()
        cls.verification_job = yaml.safe_load(cls.workflow)["jobs"]["verify-installed-release"]
        cls.deployment_job = deployment_projection(yaml.safe_load(cls.deploy))["jobs"]["deploy"]

    @staticmethod
    def step(job, identifier):
        matches = [step for step in job["steps"] if step.get("id") == identifier]
        if len(matches) != 1:
            raise AssertionError("Expected one step: " + identifier)
        return matches[0]

    @staticmethod
    def condition(step):
        text = step.get("if", "").strip()
        if text.startswith("${{") and text.endswith("}}"):
            text = text[3:-2].strip()
        return " ".join(text.split())

    def test_workflow_is_main_only_environment_protected_and_read_only(self):
        for token in (
            "workflow_dispatch:",
            "deployment_run_id:",
            "acceptance_scope:",
            "sow_role",
            "ACCEPTANCE_SCOPE: ${{ inputs.acceptance_scope }}",
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

        self.assertIn("path: ${{ github.workspace }}/flowhive-installed-acceptance", self.workflow)

    def test_independent_business_checks_keep_identity_and_cancellation_gates(self):
        identity_gate = "!cancelled() && (inputs.acceptance_scope == 'full' || inputs.acceptance_scope == 'sow_role') && steps.identity.outcome == 'success'"
        for name in ("sow", "my_role", "module025"):
            with self.subTest(step=name):
                self.assertEqual(self.condition(self.step(self.verification_job, name)), identity_gate)
        self.assertEqual(self.condition(self.step(self.verification_job, "flowhive")),
                         "!cancelled() && inputs.acceptance_scope == 'full' && "
                         "steps.identity.outcome == 'success' && steps.planner.outcome == 'success'")
        self.assertEqual(self.condition(self.step(self.verification_job, "planner")),
                         "!cancelled() && inputs.acceptance_scope == 'full'")

    def test_sow_role_scope_skips_flowhive_without_weakening_other_gates(self):
        for token in (
            "case \"$ACCEPTANCE_SCOPE\" in",
            "full|sow_role",
            "inputs.acceptance_scope == 'full'",
            "FLOWHIVE_ACCEPTANCE_SCOPE=sow_role",
            "FlowHive generation and lifecycle checks were intentionally not invoked.",
            "Normal authorized Solution Architect SOW lifecycle did not pass.",
            "My Role in Pulse browser acceptance did not pass.",
        ):
            self.assertIn(token, self.workflow)
        self.assertIn("if [[ \"$ACCEPTANCE_SCOPE\" == full ]]; then", self.workflow)
        self.assertIn("steps.sow.outcome", self.workflow)
        self.assertIn("steps.my_role.outcome", self.workflow)

    def test_both_actual_flowhive_callers_receive_existing_pm_and_prior_context(self):
        callers = ((self.deployment_job, "psa_live_uat"), (self.verification_job, "flowhive"))
        for job, identifier in callers:
            with self.subTest(caller=identifier):
                step = self.step(job, identifier)
                environment = {**job.get("env", {}), **step.get("env", {})}
                self.assertIn("scripts/release-test/run-flowhive-psa-live-uat.py", step["run"])
                for suffix in ("EMAIL", "PASSWORD"):
                    name = "PROJECTPULSE_M025_PM_" + suffix
                    self.assertEqual(environment.get(name), "${{ secrets." + name + " }}")
                self.assertEqual(environment.get("PREVIOUS_PLANNER_RUN_ID"),
                                 "171e4430-95e4-4f80-be14-454dcc319ef2")
                self.assertNotIn("TEST_LOGIN_PASSWORD", step.get("env", {}))
                self.assertNotIn("PROJECTPULSE_M087_PASSWORD", str(step.get("env", {})))
        # Legacy fixtures still receive their own existing credential.
        self.assertIn("secrets.PROJECTPULSE_M087_PASSWORD", self.deploy)

    def test_pm_preflight_is_after_admission_before_expensive_or_mutating_work(self):
        job = self.deployment_job
        step = self.step(job, "psa_acceptance_preflight")
        self.assertEqual(self.condition(step), "steps.psa_admission.outputs.authorized == 'true'")
        self.assertEqual(step.get("working-directory"), "control")
        self.assertEqual(step.get("run"),
                         "python3 scripts/release-test/check-flowhive-acceptance-inputs.py --read-only")
        self.assertNotIn("continue-on-error", step)
        self.assertEqual(step["env"]["BASE"], "https://phd-west-test.onenecklab.com")
        for suffix in ("EMAIL", "PASSWORD"):
            name = "PROJECTPULSE_M025_PM_" + suffix
            self.assertEqual(step["env"].get(name), "${{ secrets." + name + " }}")
        self.assertEqual(step["env"]["PREVIOUS_PLANNER_RUN_ID"],
                         "171e4430-95e4-4f80-be14-454dcc319ef2")
        steps = job["steps"]
        position = steps.index(step)
        self.assertEqual(position, steps.index(self.step(job, "psa_admission")) + 1)
        for name in ("Install isolated live-browser acceptance dependencies",
                     "Guard exact source and validate release",
                     "Sign in to protected Test subscription",
                     "Build immutable API, web, and migration images",
                     "Apply and verify governed migrations through Module 025 project-name migration 109 inside Test private network",
                     "Deploy immutable Test API image", "Deploy immutable Test web image"):
            with self.subTest(operation=name):
                matches = [index for index, row in enumerate(steps) if row.get("name") == name]
                self.assertEqual(len(matches), 1)
                self.assertLess(position, matches[0])

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

    def test_installed_resolver_accepts_only_the_reviewed_candidate_or_successor_lane(self):
        self.assertIn('SUPPORTED_SUCCESSOR_RELEASE_BRANCH = "release/flowhive-sow-successor-20260908"', self.resolver)
        self.assertIn('if deployed_branch == "main":', self.resolver)
        self.assertIn('application == controller', self.resolver)
        self.assertIn('deployed_branch in {manifest.get("branch"), SUPPORTED_SUCCESSOR_RELEASE_BRANCH}', self.resolver)
        self.assertIn('manifest.get("sha") == application', self.resolver)

    def test_release_marker_consumes_the_controller_written_source_variable(self):
        self.assertIn('"PROJECTPULSE_SOURCE_COMMIT"', self.platform)
        self.assertIn('"PROJECTPULSE_RELEASE_SHA",\n            "PROJECTPULSE_SOURCE_COMMIT"', self.platform)
        self.assertIn('PROJECTPULSE_SOURCE_COMMIT="$TARGET_RELEASE_COMMIT"', self.deploy)

    def test_sow_acceptance_preserves_safe_terminal_provider_diagnostics(self):
        for token in (
            '"generationTerminal"',
            '"diagnosticCode"',
            '"failureStage"',
            '"targetDecisions"',
            'module025_generation_terminal_failure',
        ):
            self.assertIn(token, self.module025_sa)
        generation_block = self.module025_sa.split('report["generationTerminal"]', 1)[1]
        self.assertNotIn('generation.get("serviceOverview")', generation_block)
        self.assertNotIn('generation.get("sessionToken")', generation_block)
        self.assertNotIn('generation.get("password")', generation_block)

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
            "#project-flowhive",
            "#signed-handoff",
            '.sales-delivery-workflow-center[data-module="027"]',
            "my_role_access_boundary_missing",
            "await page.reload",
            "anonymous_handoff_access_not_denied",
            "browser_attempted_mutation",
            "/api/client-diagnostics",
            "observability_posts",
            "observabilityPosts",
            "browser_timeout_role_welcome_dashboard",
            "wait_for_assigned_role",
            'filter(has_text="Your role")',
        ):
            self.assertIn(token, self.role)
        for forbidden in ("#project-intake", 'data-module="020"', "Work-task handoff", "Resource handoff"):
            self.assertNotIn(forbidden, self.role)
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
        self.assertIn('TARGET_RELEASE_COMMIT', self.module025_sa)
        self.assertNotIn('95abbb0aa2445a33fda68e9de542f9446c3e2204', self.module025_sa)

    def test_my_role_verifier_follows_assigned_playbook_routes(self):
        for token in (
            'assigned_role_route_invalid',
            'my_role_access_boundary_missing',
            '#project-flowhive',
            '#signed-handoff',
            '.sales-delivery-workflow-center[data-module="027"]',
        ):
            self.assertIn(token, self.role)
        self.assertIn('flowHiveRouteVisited": False', self.role)
        self.assertIn('blockedWrites', self.role)
        self.assertIn('parsed.method == "POST"', self.role)
        for forbidden in ('#project-intake', 'data-module="020"', 'Work-task handoff', 'Resource handoff'):
            self.assertNotIn(forbidden, self.role)

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

    def test_sow_role_scope_gates_only_the_selected_installed_acceptance_slice(self):
        scope_gate = "(inputs.acceptance_scope == 'full' || inputs.acceptance_scope == 'sow_role')"
        self.assertGreaterEqual(self.workflow.count(scope_gate), 3)

    def test_scripts_parse_as_python(self):
        for source in (IDENTITY, FLOWHIVE, ROLE, MODULE025, MODULE025_SA, PLANNER, PREFLIGHT):
            ast.parse(source.read_text(), filename=str(source))


class AcceptanceInputTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("acceptance_input_test", PREFLIGHT)
        cls.preflight = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.preflight)

    def inputs(self):
        project = "00000000-0000-4000-8000-000000000001"
        environment = {
            "BASE": "https://phd-west-test.onenecklab.com",
            "TARGET_RELEASE_COMMIT": "a" * 40,
            "PROJECTPULSE_M025_PM_EMAIL": "pm@example.invalid",
            "PROJECTPULSE_M025_PM_PASSWORD": secrets.token_urlsafe(24),
            "PREVIOUS_PLANNER_RUN_ID": "00000000-0000-4000-8000-000000000002",
        }
        approval = {"sha": environment["TARGET_RELEASE_COMMIT"], "projectId": project,
                    "projectManagerLogin": environment["PROJECTPULSE_M025_PM_EMAIL"],
                    "environment": "test", "publicOrigin": environment["BASE"],
                    "allowCustomerPublication": False, "allowCanonicalTaskAdoption": False}
        return approval, environment

    def test_existing_credentials_need_no_new_secret_names(self):
        approval, environment = self.inputs()
        self.preflight.validate_inputs(approval, environment)

    def test_each_missing_pm_or_prior_input_fails_before_authentication(self):
        for name in ("PROJECTPULSE_M025_PM_EMAIL", "PROJECTPULSE_M025_PM_PASSWORD",
                     "PREVIOUS_PLANNER_RUN_ID"):
            with self.subTest(input=name):
                approval, environment = self.inputs()
                environment.pop(name)
                with self.assertRaises(self.preflight.InputError):
                    self.preflight.validate_inputs(approval, environment)

    def test_wrong_project_pm_release_or_scope_is_not_silently_reassigned(self):
        mutations = ({"projectId": ""}, {"projectManagerLogin": "another@example.invalid"},
                     {"sha": "b" * 40}, {"environment": "production"},
                     {"allowCustomerPublication": True}, {"allowCanonicalTaskAdoption": True})
        for change in mutations:
            with self.subTest(change=change):
                approval, environment = self.inputs()
                approval.update(change)
                with self.assertRaises(self.preflight.InputError):
                    self.preflight.validate_inputs(approval, environment)

    def test_real_entrypoint_missing_input_never_opens_a_client_or_leaks_password(self):
        approval, environment = self.inputs()
        password = environment.pop("PROJECTPULSE_M025_PM_PASSWORD")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            approval_file = root / "approval.json"
            approval_file.write_text(json.dumps(approval))
            environment.update(PSA_VERIFICATION_APPROVAL_FILE=str(approval_file), EVIDENCE_DIR=directory)
            with patch.dict(os.environ, environment, clear=True), \
                    patch.object(self.preflight, "load_flowhive") as load, \
                    patch("sys.stdout", new_callable=io.StringIO) as output:
                self.assertEqual(self.preflight.main(["--read-only"]), 1)
                load.assert_not_called()
            evidence_text = (root / "flowhive-acceptance-preflight.json").read_text()
            evidence = json.loads(evidence_text)
            self.assertEqual(evidence["diagnosticCode"], "pm_login_secret_missing")
            self.assertEqual(evidence["generationPosts"], 0)
            self.assertFalse(evidence["businessWritesRequested"])
            self.assertNotIn(password, evidence_text + output.getvalue())

    def test_input_only_entrypoint_passes_without_any_network_access(self):
        approval, environment = self.inputs()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            approval_file = root / "approval.json"
            approval_file.write_text(json.dumps(approval))
            environment.update(PSA_VERIFICATION_APPROVAL_FILE=str(approval_file), EVIDENCE_DIR=directory)
            with patch.dict(os.environ, environment, clear=True), \
                    patch.object(self.preflight, "load_flowhive") as load, \
                    patch("sys.stdout", new_callable=io.StringIO):
                self.assertEqual(self.preflight.main([]), 0)
                load.assert_not_called()
            report = json.loads((root / "flowhive-acceptance-preflight.json").read_text())
            self.assertTrue(report["inputContractVerified"])
            self.assertEqual(report["generationPosts"], 0)
            self.assertNotIn("authenticatedPmAndReadySow", report)


class SowReviewConfirmationTests(unittest.TestCase):
    """Exercise the verifier against source invalidation, not permissive HTTP stubs."""

    def run_lifecycle(self, invalidate_review=False, generation_timeout=False, missing_schema=False,
                      corrupt_document=False, changed_version=False, browser_preflight_failure=False):
        spec = importlib.util.spec_from_file_location("sow_review_test", MODULE025_SA)
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        uid = "00000000-0000-4000-8000-000000000001"
        engagement = {"engagementId": uid, "engagementNumber": "SOW-TEST", "revision": 1,
                      "status": "draft", "lastGeneratedAt": None}
        calls = []
        reviewed = []
        documents = {}
        for label, member in (("sow", "word/document.xml"), ("gsd", "xl/workbook.xml")):
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, "w") as archive:
                archive.writestr(member, '<synthetic>retained document test</synthetic>')
            documents[label] = stream.getvalue()
        version = {"versionId": uid, "versionNumber": 1, "sourceRevision": 3,
                   **{label + "Sha256": hashlib.sha256(data).hexdigest() for label, data in documents.items()}}

        def http(path, method="GET", payload=None, token=""):
            calls.append((path, method))
            if path == "/api/module025/sow-register":
                return (409, {}, {}) if missing_schema else (200, {"records": []}, {})
            if path.endswith('/versions'):
                result_version = dict(version)
                if changed_version and engagement['status'] == 'draft': result_version['sourceRevision'] = 9
                return 200, {"versions": [result_version], "latestVersionId": uid, "hasMore": False,
                    "currentContentReleased": engagement['status'] == 'confirmed',
                    "sellReadiness": {"ready": False, "diagnosticCode": "SELL_DOCUMENT_WRITE_ADAPTER_REQUIRED"}}, {}
            if '/versions/' in path:
                if not token: return 401, {}, {}
                label = 'sow' if path.endswith('.docx') else 'gsd'
                return 200, b'corrupt' if corrupt_document else documents[label], {
                    'x-content-sha256': version[label + 'Sha256'], 'x-sow-version': '1'}
            if path.endswith("/bootstrap"):
                return 200, {"access": {"isSolutionArchitect": True,
                    "protectedTestUatRoleFixture": False, "canCreate": True,
                    "canEditOwn": True, "isViewAs": False}, "currentUser": {"userId": uid},
                    "accountExecutives": [{"userId": uid}],
                    "insideSalesRepresentatives": [{"userId": "second-user"}]}, {}
            if path == "/api/module025/sow-gsd" and method == "POST":
                engagement.update(copy.deepcopy(payload))
                engagement["phases"] = [{"phaseCode": c, "objective": "Initial", "finalHours": 1,
                                         "acceptanceCriteria": []} for c in runner.PHASE_CODES]
                return 201, {"engagement": copy.deepcopy(engagement)}, {}
            if method == "PUT":
                source_changed = payload["serviceOverview"] != engagement["serviceOverview"]
                after_generation = bool(engagement["lastGeneratedAt"])
                if after_generation:
                    reviewed.append(copy.deepcopy(payload))
                engagement.update(copy.deepcopy(payload))
                engagement["revision"] += 1
                if source_changed or (invalidate_review and after_generation):
                    engagement.update(status="draft", lastGeneratedAt=None)
                return 200, {"engagement": copy.deepcopy(engagement)}, {}
            if path.endswith("/generate"):
                engagement.update(status="review_ready", lastGeneratedAt="2026-09-16T15:40:21Z")
                for phase in engagement["phases"]:
                    phase["objective"] = "Review generated technology scope"
                    phase["acceptanceCriteria"] = ["Verify configured service"]
                return 202, {"generationId": uid}, {}
            if "/generations/" in path and generation_timeout:
                return 200, {"terminal": False, "status": "module025_detailed_scope_generation_running",
                    "currentPhase": "Design", "currentProvider": "deepseek", "completedPhases": ["Plan"],
                    "serviceOverview": "PRIVATE_SOURCE_SHOULD_NOT_ESCAPE",
                    "progress": [{"stage": "provider_started", "phase": "Design", "attempt": 1,
                                  "requestedModel": "approved-test-model", "inputTokens": 120,
                                  "outputTokens": 6144, "reasoningTokens": 6000,
                                  "sowDiagnostics": {"responseStatus": "incomplete", "incompleteReason": "max_output_tokens",
                                      "outputValidationCategory": "unapproved_proper_nouns", "outputValidationField": "$.tasks[0].name",
                                      "rawResponse": "PRIVATE_RESPONSE_SHOULD_NOT_ESCAPE"},
                                  "result": {"rawDraft": "PRIVATE_DRAFT_SHOULD_NOT_ESCAPE"}}]}, {}
            if "/generations/" in path:
                return 200, {"terminal": True, "status": "module025_detailed_scope_generated"}, {}
            if path.endswith("/confirm"):
                if engagement["lastGeneratedAt"] is None:
                    return 409, {"status": "generation_required"}, {}
                engagement["status"] = "confirmed"
                return 200, {}, {}
            if path.endswith("/archive"):
                engagement["status"] = "archived"
                return 200, {}, {}
            if path.endswith("/logout"):
                return 200, {}, {}
            return 200, {"engagement": copy.deepcopy(engagement)}, {}

        async def register_browser(report, source_run_id):
            # Full UI behavior is exercised separately against the built app.
            # This API lifecycle test verifies ordering and proof propagation.
            self.assertEqual(engagement['status'], 'confirmed')
            self.assertEqual(sum(path.endswith('/generate') for path, _ in calls), 1)
            report['registerBrowser'] = {'status': 'passed', 'sourceRunId': source_run_id,
                                         'generationPosts': 0, 'businessWrites': 0}

        async def browser(session, number, marker, report, evidence, *, preflight=False):
            # The real browser journey also runs against the React fixture in CI.
            if preflight:
                self.assertFalse(any(path.endswith("/generate") for path, method in calls))
                if browser_preflight_failure:
                    report["browserPreflight"] = {"status": "failed", "stage": "select_record"}
                    raise runner.AcceptanceError("module025_browser_timeout_select_record")
                report["browserPreflight"] = {"status": "passed"}
                return
            # Emulate only its
            # resulting source edit after downloading and reopening here.
            self.assertEqual(engagement["status"], "confirmed")
            engagement.update(status="draft", lastGeneratedAt=None)

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {
            "EVIDENCE_DIR": directory, "TARGET_RELEASE_COMMIT": "a" * 40,
            "MODULE025_GENERATION_TIMEOUT_SECONDS": "-1" if generation_timeout else "1500",
            "PROJECTPULSE_M025_SA_EMAIL": "sa@example.local",
            "PROJECTPULSE_M025_SA_PASSWORD": "unit-test-only-password",
        }), patch.object(runner, "login", return_value={"sessionToken": "test-session"}), \
                patch.object(runner, "http", side_effect=http), \
                patch.object(runner, "browser_lifecycle", side_effect=browser), \
                patch.object(runner, "verify_normal_sa_register", side_effect=register_browser), \
                patch("sys.stdout", new_callable=io.StringIO) as output:
            result = asyncio.run(runner.main())
            report = json.loads((Path(directory) / "module025-installed-sa-uat.json").read_text())
            return result, report, reviewed, calls, output.getvalue()

    def test_generated_scope_review_confirms_without_changing_source_or_regenerating(self):
        result, report, reviewed, calls, output = self.run_lifecycle()
        self.assertEqual(result, 0, report)
        self.assertEqual(report["status"], "passed")
        self.assertEqual(len(reviewed), 1)
        self.assertNotIn("Browser reload acceptance marker", reviewed[0]["serviceOverview"])
        plan = next(p for p in reviewed[0]["phases"] if p["phaseCode"] == "plan")
        self.assertTrue(any("Browser reload acceptance marker" in a for a in plan["acceptanceCriteria"]))
        self.assertEqual(sum(path.endswith("/generate") for path, method in calls), 1)
        self.assertIn("MODULE025_INSTALLED_SA_UAT=PASS", output)
        self.assertEqual(report['registerBrowser']['status'], 'passed')
        self.assertEqual(report['registerBrowser']['generationPosts'], 0)
        self.assertTrue(report['retentionSchemaReady'])
        self.assertTrue(report['retainedVersions']['historicalHashesVerified'])
        self.assertTrue(report['retainedVersions']['unauthorizedDownloadsDenied'])
        self.assertFalse(report['fullRequestedScopePassed'])
        self.assertEqual(report['sellAcceptance']['status'], 'blocked')

    def test_missing_retention_schema_stops_before_creation_and_paid_generation(self):
        result, report, _, calls, _ = self.run_lifecycle(missing_schema=True)
        self.assertEqual(result, 1)
        self.assertEqual(report['diagnosticCode'], 'module025_register_prerequisite_http_409')
        self.assertFalse(any(method == 'POST' and not path.endswith('/logout') for path, method in calls))
        self.assertEqual(report['generationPosts'], 0)

    def test_corrupt_documents_and_mutated_retained_versions_fail_acceptance(self):
        for kwargs, diagnostic in [({'corrupt_document': True}, 'sow_download_empty'),
            ({'changed_version': True}, 'module025_retained_version_changed_sourceRevision')]:
            with self.subTest(kwargs=kwargs):
                result, report, _, _, _ = self.run_lifecycle(**kwargs)
                self.assertEqual(result, 1)
                self.assertEqual(report['diagnosticCode'], diagnostic)

    def test_generation_timeout_retains_identifiers_and_safe_last_phase(self):
        result, report, _, calls, _ = self.run_lifecycle(generation_timeout=True)
        self.assertEqual(result, 1)
        self.assertEqual(report["diagnosticCode"], "module025_generation_deadline_exceeded")
        self.assertTrue(report["engagementId"] and report["generationId"])
        self.assertEqual(report["lastGenerationState"]["currentPhase"], "Design")
        self.assertEqual(report["lastGenerationState"]["completedPhases"], ["Plan"])
        progress = report["lastGenerationState"]["progress"][0]
        self.assertEqual(progress["attempt"], 1)
        self.assertEqual(progress["requestedModel"], "approved-test-model")
        self.assertEqual((progress["inputTokens"], progress["outputTokens"], progress["reasoningTokens"]), (120, 6144, 6000))
        self.assertEqual(progress["sowDiagnostics"]["incompleteReason"], "max_output_tokens")
        self.assertEqual(progress["sowDiagnostics"]["outputValidationField"], "$.tasks[0].name")
        self.assertNotIn("PRIVATE_", json.dumps(report))
        self.assertEqual(sum(path.endswith("/generate") for path, _ in calls), 1)
        self.assertFalse(any(path.endswith("/confirm") for path, _ in calls))

    def test_invalidated_generation_still_fails_before_confirmation(self):
        result, report, reviewed, calls, output = self.run_lifecycle(invalidate_review=True)
        self.assertEqual(result, 1)
        self.assertEqual(report["diagnosticCode"], "module025_review_generation_invalidated")
        self.assertFalse(any(path.endswith("/confirm") for path, method in calls))
        self.assertIn("MODULE025_INSTALLED_SA_DIAGNOSTIC=module025_review_generation_invalidated", output)
        self.assertNotIn("unit-test-only-password", output)

    def test_browser_reopen_waits_for_review_ready_before_source_edit(self):
        source = MODULE025_SA.read_text()
        reopen = source.index('name="Reopen for editing"')
        ready = source.index('.m025-status-pill--review_ready', reopen)
        edit = source.index('await service.fill', reopen)
        self.assertLess(ready, edit)
        self.assertNotIn('.m025-status-pill--draft', source[reopen:edit])

    def test_browser_preflight_failure_stops_before_any_provider_request(self):
        result, report, _, calls, output = self.run_lifecycle(browser_preflight_failure=True)
        self.assertEqual(result, 1)
        self.assertEqual(report["generationPosts"], 0)
        self.assertEqual(report["browserPreflight"]["stage"], "select_record")
        self.assertFalse(any(path.endswith("/generate") or path.endswith("/confirm") for path, method in calls))
        self.assertTrue(any(path.endswith("/archive") for path, method in calls))
        self.assertIn("MODULE025_INSTALLED_SA_DIAGNOSTIC=module025_browser_timeout_select_record", output)


if __name__ == "__main__":
    unittest.main()

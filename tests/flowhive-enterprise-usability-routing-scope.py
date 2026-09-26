"""Exact governed scope for the enterprise FlowHive usability and local routing repair."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-enterprise-usability-routing-20260926"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    ".github/workflows/projectpulse-deploy-test.yml",
    "database/migrations/127_flowhive_pm_automatic_planning_defaults.sql",
    "database/rollback/127_flowhive_pm_automatic_planning_defaults_rollback.sql",
    "docs/production-readiness/foundation/initialization-review.json",
    "deployment/oracle-celar/gateway/wsgi.py",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh",
    "scripts/release-test/resolve-flowhive-installed-deployment.py",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAutomation.cs",
    "src/frontend/project-time-web/src/ProjectFlowHiveAutomation.jsx",
    "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx",
    "src/frontend/project-time-web/src/project-flowhive-automation.css",
    "tests/flowhive-automatic-browser.py",
    "tests/flowhive-enterprise-usability-routing-scope.py",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-psa-release-workflow.test.py",
    "tests/laya/processed-source-scope.py",
    "tests/test-celar-sow-runtime-deadlines.py",
    "tests/test-flowhive-pm-automatic-planning-migration-127.sh",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git", "fetch", "origin", "main", "--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base", "origin/main", "HEAD")
actual = sorted(filter(None, git("diff", "--name-only", f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected FlowHive enterprise usability scope: {sorted(set(actual) ^ set(EXPECTED))}"

for file in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    ".github/workflows/module-management-owner-drawer-ci.yml",
]:
    assert BRANCH in Path(file).read_text(), f"{BRANCH} is not registered in {file}"

print("FLOWHIVE_ENTERPRISE_USABILITY_ROUTING_SCOPE=PASS")

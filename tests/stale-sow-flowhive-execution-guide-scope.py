"""Exact governed scope for stale-SOW cleanup and FlowHive execution-guide usability."""
from pathlib import Path
import subprocess

BRANCH = "fix/stale-sow-cleanup-flowhive-execution-guide-20261002"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    ".github/workflows/pr1151-uat-supersession-ci.yml",
    ".github/workflows/protected-test-stale-sow-maintenance.yml",
    ".github/workflows/uat-migration-throttle-recovery-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/cleanup-stale-sows-protected-test.py",
    "scripts/validate-deployment-concurrency-governance.mjs",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx",
    "src/frontend/project-time-web/src/project-flowhive-center.css",
    "src/frontend/project-time-web/src/work-register-document-integrity.js",
    "tests/flowhive-psa-react-browser.py",
    "tests/stale-sow-flowhive-execution-guide-scope.py",
    "tests/test_stale_sow_cleanup.py",
    "tests/validate-work-register-document-continuity.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()
subprocess.run(["git", "fetch", "origin", "main", "--no-tags"],
               check=True, stdout=subprocess.DEVNULL)
base = git("merge-base", "origin/main", "HEAD")
actual = sorted(filter(None, git("diff", "--name-only", f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, (
    "Unexpected stale-SOW / FlowHive execution-guide scope: "
    f"{sorted(set(actual) ^ set(EXPECTED))}"
)

for file_name in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    ".github/workflows/uat-migration-throttle-recovery-ci.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    ".github/workflows/pr1151-uat-supersession-ci.yml",
]:
    assert BRANCH in Path(file_name).read_text(), f"{BRANCH} is not registered in {file_name}"

cleanup = Path("scripts/release-test/cleanup-stale-sows-protected-test.py").read_text()
workflow = Path(".github/workflows/protected-test-stale-sow-maintenance.yml").read_text()
integrity = Path("src/frontend/project-time-web/src/work-register-document-integrity.js").read_text()
flowhive = Path("src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx").read_text()
assert "https://phd-west-test.onenecklab.com" in cleanup
assert "DELETE STALE TEST SOWS" in cleanup
assert '"dry-run"' in cleanup and '"apply"' in cleanup
assert '"productionMutation": False' in cleanup
assert "environment: test" in workflow
assert "archiveButton = null" in integrity and "archiveButton?.className" in integrity
assert "if (!archiveButton) return;" not in integrity
for marker in [
    "Engineer execution guide",
    "How to complete this task",
    "1. Before you start",
    "2. Perform the work",
    "3. Verify the result",
    "4. Complete and accept",
    "5. Rollback and exceptions",
    "6. Ownership and open items",
]:
    assert marker in flowhive, f"Missing FlowHive execution-guide marker: {marker}"

print("STALE_SOW_FLOWHIVE_EXECUTION_GUIDE_SCOPE=PASS")

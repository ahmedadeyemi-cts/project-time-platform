"""Exact governed scope for stale-SOW cleanup and FlowHive execution-guide usability."""
from pathlib import Path
import subprocess

BRANCH = "fix/stale-sow-cleanup-flowhive-execution-guide-20261002"
EXPECTED = sorted({
    ".github/workflows/pr1139-uat-recovery-ci.yml",
    ".github/workflows/projectpulse-deploy-test.yml",
    ".github/workflows/protected-test-stale-sow-maintenance.yml",
    "scripts/release-test/recover-pr1139-uat-orphan.py",
    "scripts/release-test/recover-pr1140-migration-retry-orphan.py",
    "scripts/release-test/recover-pr1140-uat-orphan.py",
    "scripts/release-test/verify-module025-quarantine-controller.py",
    "scripts/release-test/verify-pr1151-uat-supersession.py",
    "scripts/release-test/verify-pr1204-uat-supersession.py",
    "tests/laya/processed-source-scope.py",
    "tests/module025_qualification_workflow.py",
    "tests/pulse-activation-release/controller.py",
    "tests/security-release/test_controller_registration.py",
    "tests/stale-sow-controller-registration.json",
    "tests/stale-sow-flowhive-execution-guide-scope.py",
    "tests/test_stale_sow_cleanup.py",
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
dispatch = Path(".github/workflows/protected-test-stale-sow-maintenance.yml").read_text()
deploy = Path(".github/workflows/projectpulse-deploy-test.yml").read_text()
integrity = Path("src/frontend/project-time-web/src/work-register-document-integrity.js").read_text()
flowhive = Path("src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx").read_text()
assert "https://phd-west-test.onenecklab.com" in cleanup
assert "DELETE STALE TEST SOWS" in cleanup
assert '"dry-run"' in cleanup and '"apply"' in cleanup
assert '"productionMutation": False' in cleanup
assert "actions: write" in dispatch and "environment: test" not in dispatch
assert "PROJECTPULSE_M087_PASSWORD" not in dispatch
assert "projectpulse-deploy-test.yml" in dispatch and "gh run watch" in dispatch
assert "stale_sow_cleanup_mode" in deploy and "stale_sow_cleanup_confirmation" in deploy
assert "PROJECTPULSE_M087_PASSWORD: ${{ secrets.PROJECTPULSE_M087_PASSWORD }}" in deploy
assert "cleanup-stale-sows-protected-test.py" in deploy
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

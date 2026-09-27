"""Exact governed scope for idempotent protected-Test FlowHive working-draft UAT."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-protected-uat-idempotent-20260927"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    ".github/workflows/projectpulse-deploy-test.yml",
    ".github/workflows/uat-migration-throttle-recovery-ci.yml",
    ".github/workflows/pr1151-uat-supersession-ci.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "tests/flowhive-protected-uat-idempotent-scope.py",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-psa-release-workflow.test.py",
    "tests/laya/processed-source-scope.py",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected UAT idempotency scope: {sorted(set(actual) ^ set(EXPECTED))}"

controller = Path(".github/workflows/projectpulse-deploy-test.yml").read_text()
for marker in [
    "FLOWHIVE_EXISTING_DRAFT_READY",
    "preserved_existing_working_draft",
    "candidate_review_required",
    "FlowHive preserved working copy",
    "FLOWHIVE_AI_PLANNER_UAT=PASSED mode=",
    ".workingCopy.schedule.valid == true",
    ".workingCopy.validation.valid == true",
]:
    assert marker in controller
assert "projectpulse-deploy-production.yml" not in "\n".join(actual)

for file in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    ".github/workflows/module-management-owner-drawer-ci.yml",
]:
    assert BRANCH in Path(file).read_text(), f"{BRANCH} is not registered in {file}"

print("FLOWHIVE_PROTECTED_UAT_IDEMPOTENT_SCOPE=PASS")

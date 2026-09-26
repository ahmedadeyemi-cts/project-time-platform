"""Exact governed scope for accepting source-grounded reviewable partial WBS output."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-reviewable-partial-wbs-20260926"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/module064-migration-rollout.test.py",
    "tests/flowhive-reviewable-partial-wbs-scope.py",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected reviewable-partial scope: {sorted(set(actual) ^ set(EXPECTED))}"

rag = Path("src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs").read_text()
orchestrator = Path("src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs").read_text()
assert "IsReviewableWholeWbs" in rag
assert "private_flowhive_reviewable_wbs_missing" in rag
assert "reviewablePartialPlanReady" in orchestrator
assert 'composition.Status == "celar_ai_solution_draft_partial"' in orchestrator
print("FLOWHIVE_REVIEWABLE_PARTIAL_WBS_SCOPE=PASS")

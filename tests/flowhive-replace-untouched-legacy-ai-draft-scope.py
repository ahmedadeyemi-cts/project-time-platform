"""Exact governed scope for replacing only untouched legacy Celar AI working drafts."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-replace-untouched-legacy-ai-draft-20260926"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-replace-untouched-legacy-ai-draft-scope.py",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected legacy replacement scope: {sorted(set(actual) ^ set(EXPECTED))}"

source = Path("src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs").read_text()
assert "IsReplaceableLegacyAiWorkingCopyAsync" in source
assert 'string.Equals(sourceKind, "celar_ai"' in source
assert 'percent > 0m' in source
assert 'Celar AI detailed Planner review' in source
print("FLOWHIVE_REPLACE_UNTOUCHED_LEGACY_AI_DRAFT_SCOPE=PASS")

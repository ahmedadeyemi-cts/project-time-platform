"""Exact governed scope for the final live whole-WBS deadline and fail-safe acceptance repair."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-whole-wbs-live-final-20260926"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutablePlanBuilder.cs",
    "tests/FlowHiveExecutablePlanTests/Program.cs",
    "tests/FlowHiveSequentialTests/Program.cs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/module064-migration-rollout.test.py",
    "tests/flowhive-whole-wbs-live-final-scope.py",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected final whole-WBS scope: {sorted(set(actual) ^ set(EXPECTED))}"

for file in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    ".github/workflows/module-management-owner-drawer-ci.yml",
]:
    assert BRANCH in Path(file).read_text(), f"{BRANCH} is not registered in {file}"

client = Path("src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs").read_text()
builder = Path("src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutablePlanBuilder.cs").read_text()
assert 'else if (request.FeatureCode == CelarAiCapabilityCatalog.ProjectFlowHivePlan)' in client
assert 'sourceGroundedFailSafe' in builder
print("FLOWHIVE_WHOLE_WBS_LIVE_FINAL_SCOPE=PASS")

"""Exact governed scope for the FlowHive Celar private transient-diagnostic retry repair."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-celar-private-transient-diagnostics-20261002"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    ".github/workflows/pr1151-uat-supersession-ci.yml",
    ".github/workflows/uat-migration-throttle-recovery-ci.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs",
    "tests/FlowHiveDetailedPlannerTests/Program.cs",
    "tests/module025_qualification_workflow.py",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-celar-private-transient-scope.py",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected FlowHive Celar transient scope: {sorted(set(actual) ^ set(EXPECTED))}"

for file in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    ".github/workflows/module-management-owner-drawer-ci.yml",
]:
    assert BRANCH in Path(file).read_text(), f"{BRANCH} is not registered in {file}"

source = Path("src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs").read_text()
tests = Path("tests/FlowHiveDetailedPlannerTests/Program.cs").read_text()
for code in [
    "celar_ai_private_http_502",
    "celar_ai_private_http_503",
    "celar_ai_private_http_504",
    "celar_ai_private_generation_timeout",
    "celar_ai_private_transport_failure",
]:
    assert code in source and code in tests, f"Missing reviewed transient diagnostic: {code}"
for code in [
    "celar_ai_private_http_401",
    "celar_ai_private_http_422",
    "celar_ai_private_model_not_configured",
    "celar_ai_private_empty_response",
    "celar_ai_output_budget_exhausted",
]:
    assert code not in source, f"Non-transient diagnostic was made retryable: {code}"
    assert code in tests, f"Negative regression coverage missing: {code}"
print("FLOWHIVE_CELAR_PRIVATE_TRANSIENT_SCOPE=PASS")

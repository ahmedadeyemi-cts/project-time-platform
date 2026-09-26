"""Exact governed scope for the FlowHive whole-WBS generation and source-grounded fail-safe repair."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-source-grounded-failsafe-20260926"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    "deployment/oracle-celar/gateway/wsgi.py",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Ai/FlowHiveSequentialGeneration.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/module064-migration-rollout.test.py",
    "tests/flowhive-whole-wbs-failsafe-scope.py",
    "tests/test-celar-sow-runtime-deadlines.py",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected FlowHive whole-WBS scope: {sorted(set(actual) ^ set(EXPECTED))}"

for file in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    ".github/workflows/module-management-owner-drawer-ci.yml",
]:
    assert BRANCH in Path(file).read_text(), f"{BRANCH} is not registered in {file}"

source = Path("src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs").read_text()
rag = Path("src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs").read_text()
assert "sequential: null" in source
assert "15 to 25 ordered executable tasks" in rag
assert "ParseFlowHiveCompactWbsContent" in rag
assert "source-grounded fail-safe" in rag
print("FLOWHIVE_WHOLE_WBS_FAILSAFE_SCOPE=PASS")

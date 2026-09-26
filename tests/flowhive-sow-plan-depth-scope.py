"""Exact governed scope for the FlowHive SOW-grounded plan-depth repair."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-sow-plan-depth-20260925"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Ai/FlowHiveSequentialGeneration.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs",
    "src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs",
    "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx",
    "src/frontend/project-time-web/src/ai/AiPhaseProgress.jsx",
    "src/frontend/project-time-web/src/project-flowhive-center.css",
    "tests/FlowHiveSequentialTests/Program.cs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-psa-planner-layout.py",
    "tests/flowhive-sow-plan-depth-scope.py",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})
def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git", "fetch", "origin", "main", "--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base", "origin/main", "HEAD")
actual = sorted(filter(None, git("diff", "--name-only", f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected FlowHive plan-depth scope: {sorted(set(actual) ^ set(EXPECTED))}"

controller = Path("scripts/release-test/validate-protected-test-controller-branches.sh").read_text()
release = Path(".github/workflows/flowhive-psa-release-control-ci.yml").read_text()
source = Path("scripts/ci/validate-celar-ai-enterprise-source-boundary.sh").read_text()
drawer = Path(".github/workflows/module-management-owner-drawer-ci.yml").read_text()
assert BRANCH in controller and BRANCH in release and BRANCH in source and BRANCH in drawer
print("FLOWHIVE_SOW_PLAN_DEPTH_SCOPE=PASS")

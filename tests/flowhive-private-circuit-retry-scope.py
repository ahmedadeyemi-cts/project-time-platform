"""Exact governed scope for FlowHive private-provider circuit-aware retry."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = "f803150214f7854acca75d5447d6888542259575"
BRANCH = "fix/flowhive-private-circuit-retry-20261003"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    ".github/workflows/pr1151-uat-supersession-ci.yml",
    ".github/workflows/uat-migration-throttle-recovery-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutionPolicy.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs",
    "tests/FlowHiveDetailedPlannerTests/Program.cs",
    "tests/FlowHiveExecutionTests/Program.cs",
    "tests/flowhive-private-circuit-retry-scope.py",
    "tests/flowhive-psa-admission.test.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()

branch = git("branch", "--show-current")
assert branch == BRANCH or not branch, f"Wrong private-circuit repair branch: {branch}"
assert git("merge-base", BASE, "HEAD") == BASE, "Private-circuit repair is not based on reviewed current main"
actual = sorted(filter(None, git("diff", "--name-only", f"{BASE}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected private-circuit retry scope: {sorted(set(actual) ^ set(EXPECTED))}"

orchestrator = (ROOT / "src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs").read_text()
execution = (ROOT / "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutionPolicy.cs").read_text()
durable = (ROOT / "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs").read_text()
for marker in (
    "provider_circuit_open",
    "IsRetryablePrivateProviderDecision",
    "RetryDelayForPrivateProviderDecisions",
    "CircuitOpenUntil",
):
    assert marker in orchestrator, marker
for marker in ("CircuitReopenGuard", "MinimumRetryExecutionBudget", "MaximumAttempts = 2", "OverallBudget = TimeSpan.FromMinutes(40)"):
    assert marker in execution, marker
assert "next_attempt_at=CASE WHEN @phase='ai_route_retry' THEN NOW()+@retry_delay" in durable
assert "retryDelay: retryDelay" in durable

for forbidden in (
    ".github/workflows/projectpulse-deploy-production.yml",
    ".github/workflows/projectpulse-deploy-test.yml",
    ".github/workflows/module025-protected-uat-control.yml",
):
    assert forbidden not in actual, f"Deployment authority changed: {forbidden}"

production = ".github/workflows/projectpulse-deploy-production.yml"
test_controller = ".github/workflows/projectpulse-deploy-test.yml"
assert (ROOT / production).read_bytes() == subprocess.check_output(["git","show",f"{BASE}:{production}"], cwd=ROOT)
assert (ROOT / test_controller).read_bytes() == subprocess.check_output(["git","show",f"{BASE}:{test_controller}"], cwd=ROOT)
print("FLOWHIVE_PRIVATE_CIRCUIT_RETRY_SCOPE=PASS productionMutation=false deploymentAuthorityMutation=false")

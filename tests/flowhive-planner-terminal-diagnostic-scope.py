"""Exact source scope for the read-only FlowHive terminal planner diagnostic."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = "163c949ad5f5be7e3b17cdf073f5923b92894892"
BRANCH = "fix/flowhive-planner-terminal-diagnostic-20261003"
EXPECTED = sorted({
    "scripts/release-test/reconcile-flowhive-planner.py",
    "tests/flowhive-psa-installed-acceptance.test.py",
    "tests/flowhive-psa-release-control.mjs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-planner-terminal-diagnostic-scope.py",
})
def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()
branch = git("branch", "--show-current")
assert branch == BRANCH or not branch
assert git("merge-base", BASE, "HEAD") == BASE
actual = sorted(filter(None, git("diff", "--name-only", f"{BASE}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected terminal diagnostic scope: {sorted(set(actual)^set(EXPECTED))}"
reconcile=(ROOT/"scripts/release-test/reconcile-flowhive-planner.py").read_text()
for marker in ('"latestPlannerStatus"','"latestPlannerPhase"','"latestPlannerEvidenceCitationCount"','"latestPlannerCompletedPhaseCount"'):
    assert marker in reconcile
for forbidden in ("original_file_name","fileName","document_reference","stored_file_path"):
    assert forbidden not in reconcile
production=".github/workflows/projectpulse-deploy-production.yml"
assert (ROOT/production).read_bytes()==subprocess.check_output(["git","show",f"{BASE}:{production}"],cwd=ROOT)
print("FLOWHIVE_PLANNER_TERMINAL_DIAGNOSTIC_SCOPE=PASS productionMutation=false businessMutation=false")

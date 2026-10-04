"""Exact source scope for the Protected Test FlowHive readiness diagnostic."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = "1d074305e3dee76500733eda19a9d4737954fd31"
BRANCH = "fix/flowhive-readiness-diagnostic-20261003"
EXPECTED = sorted({
    "scripts/release-test/reconcile-flowhive-planner.py",
    "tests/flowhive-psa-installed-acceptance.test.py",
    "tests/flowhive-psa-release-control.mjs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-readiness-diagnostic-scope.py",
})

def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()

branch = git("branch", "--show-current")
assert branch == BRANCH or not branch, f"Wrong diagnostic branch: {branch}"
assert git("merge-base", BASE, "HEAD") == BASE, "Diagnostic branch is not based on reviewed current main"
actual = sorted(filter(None, git("diff", "--name-only", f"{BASE}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected FlowHive readiness diagnostic scope: {sorted(set(actual) ^ set(EXPECTED))}"

reconcile = (ROOT / "scripts/release-test/reconcile-flowhive-planner.py").read_text()
for marker in (
    "/documents/readiness",
    '"documentReadiness"',
    '"categoryStatusCounts"',
    "FLOWHIVE_PLANNER_RECONCILIATION_SUMMARY",
):
    assert marker in reconcile, marker
for forbidden in (
    "original_file_name",
    "fileName",
    "document_reference",
    "stored_file_path",
):
    assert forbidden not in reconcile, forbidden

production = ".github/workflows/projectpulse-deploy-production.yml"
assert (ROOT / production).read_bytes() == subprocess.check_output(["git","show",f"{BASE}:{production}"], cwd=ROOT)
print("FLOWHIVE_READINESS_DIAGNOSTIC_SCOPE=PASS productionMutation=false businessMutation=false")

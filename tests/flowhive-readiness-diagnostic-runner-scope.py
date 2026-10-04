"""Exact source scope for the read-only FlowHive readiness diagnostic runner fix."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = "d62918eb786a3583f6e9d91577e1a85b6e8e5830"
BRANCH = "fix/flowhive-readiness-diagnostic-runner-20261003"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-installed-acceptance.yml",
    "tests/flowhive-psa-installed-acceptance.test.py",
    "tests/flowhive-psa-release-control.mjs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-readiness-diagnostic-runner-scope.py",
})

def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()

branch = git("branch", "--show-current")
assert branch == BRANCH or not branch, f"Wrong diagnostic runner branch: {branch}"
assert git("merge-base", BASE, "HEAD") == BASE
actual = sorted(filter(None, git("diff", "--name-only", f"{BASE}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected readiness diagnostic runner scope: {sorted(set(actual) ^ set(EXPECTED))}"
workflow = (ROOT / ".github/workflows/flowhive-psa-installed-acceptance.yml").read_text()
assert "python3 scripts/release-test/reconcile-flowhive-planner.py" in workflow
assert '"$RUNNER_TEMP/flowhive-psa-browser/bin/python" scripts/release-test/reconcile-flowhive-planner.py' not in workflow
production = ".github/workflows/projectpulse-deploy-production.yml"
assert (ROOT / production).read_bytes() == subprocess.check_output(["git","show",f"{BASE}:{production}"], cwd=ROOT)
print("FLOWHIVE_READINESS_DIAGNOSTIC_RUNNER_SCOPE=PASS productionMutation=false businessMutation=false")

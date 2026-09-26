"""Exact governed scope for updating the Protected-UAT FlowHive frontend convergence marker."""
from pathlib import Path
import subprocess

BRANCH = "fix/flowhive-frontend-convergence-marker-20260926"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module-management-owner-drawer-ci.yml",
    ".github/workflows/projectpulse-deploy-test.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "tests/flowhive-frontend-convergence-marker-scope.py",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/laya/processed-source-scope.py",
    "tests/flowhive-psa-release-workflow.test.py",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected frontend convergence scope: {sorted(set(actual) ^ set(EXPECTED))}"

controller = Path(".github/workflows/projectpulse-deploy-test.yml").read_text()
assert "This private generation phase can take several minutes" not in controller
assert "You can leave this page while FlowHive works. The plan continues on the server." in controller
assert git("diff","--name-only",f"{base}...HEAD","--",".github/workflows/projectpulse-deploy-production.yml") == ""

for file in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
    ".github/workflows/module-management-owner-drawer-ci.yml",
]:
    assert BRANCH in Path(file).read_text(), f"{BRANCH} is not registered in {file}"

print("FLOWHIVE_FRONTEND_CONVERGENCE_MARKER_SCOPE=PASS")

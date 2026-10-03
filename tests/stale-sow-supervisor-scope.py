from pathlib import Path
import subprocess

BRANCH = "fix/protected-test-stale-sow-supervisor-20261002"
EXPECTED = sorted({
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/module025-protected-uat-control.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    ".github/workflows/pr1151-uat-supersession-ci.yml",
    ".github/workflows/projectpulse-deploy-test.yml",
    ".github/workflows/protected-test-stale-sow-maintenance.yml",
    ".github/workflows/uat-migration-throttle-recovery-ci.yml",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "tests/stale-sow-flowhive-execution-guide-scope.py",
    "tests/stale-sow-supervisor-scope.py",
    "tests/test-stale-sow-supervisor-contract.py",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(["git","fetch","origin","main","--no-tags"], check=True, stdout=subprocess.DEVNULL)
base = git("merge-base","origin/main","HEAD")
actual = sorted(filter(None, git("diff","--name-only",f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, f"Unexpected stale-SOW supervisor scope: {sorted(set(actual) ^ set(EXPECTED))}"

for file in [
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/pr1140-uat-recovery-ci.yml",
    ".github/workflows/pr1151-uat-supersession-ci.yml",
    ".github/workflows/uat-migration-throttle-recovery-ci.yml",
]:
    assert BRANCH in Path(file).read_text(), f"{BRANCH} is not registered in {file}"

controller = Path(".github/workflows/projectpulse-deploy-test.yml").read_text()
supervisor = Path(".github/workflows/module025-protected-uat-control.yml").read_text()
assert "stale_sow_maintenance_mode" in controller
assert "Evaluate and maintain stale Protected-Test SOWs" in controller
assert "PROJECTPULSE_M087_PASSWORD: ${{ secrets.PROJECTPULSE_M087_PASSWORD }}" in controller
assert "stale_sow_mode='dry-run'" in supervisor
assert "stale_sow_mode='apply'" in supervisor
assert "stale_sow_maintenance_mode:$stale_sow_mode" in supervisor
assert not Path(".github/workflows/protected-test-stale-sow-maintenance.yml").exists()
print("STALE_SOW_SUPERVISOR_SCOPE=PASS")

#!/usr/bin/env python3
"""Exact governed scope for the stale-SOW current-authority follow-up."""
from pathlib import Path
import subprocess

BRANCH = "fix/stale-sow-authority-projection-20261003"
EXPECTED = sorted({
    "scripts/release-test/cleanup-stale-sows-protected-test.py",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs",
    "tests/flowhive-psa-admission.test.mjs",
    "tests/flowhive-psa-release-control.mjs",
    "tests/stale-sow-authority-projection-scope.py",
    "tests/test_stale_sow_cleanup.py",
})

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

subprocess.run(
    ["git", "fetch", "origin", "main", "--no-tags"],
    check=True,
    stdout=subprocess.DEVNULL,
)
base = git("merge-base", "origin/main", "HEAD")
actual = sorted(filter(None, git("diff", "--name-only", f"{base}...HEAD").splitlines()))
assert actual == EXPECTED, (
    "Unexpected stale-SOW authority follow-up scope: "
    f"{sorted(set(actual) ^ set(EXPECTED))}"
)
assert all(not path.startswith(".github/workflows/") for path in actual)
assert all(not path.startswith("database/") for path in actual)

cleanup = Path("scripts/release-test/cleanup-stale-sows-protected-test.py").read_text()
enterprise = Path("src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs").read_text()
unit = Path("tests/test_stale_sow_cleanup.py").read_text()
admission = Path("tests/flowhive-psa-admission.test.mjs").read_text()
release_control = Path("tests/flowhive-psa-release-control.mjs").read_text()

assert "documentAuthority" in cleanup
assert 'authority.get("currentSowDocumentId")' in cleanup
assert 'authority.get("currentSowWorkRegisterDocumentId")' in cleanup
assert "current_sow_work_register_id(readiness)" in cleanup
assert "current_planning_sow_missing" in cleanup
assert "currentSowDocumentId = resolution.StatementOfWork?.DocumentId" in enterprise
assert "currentSowWorkRegisterDocumentId = resolution.StatementOfWork?.WorkRegisterDocumentId" in enterprise
assert 'resolver = "ProjectPlanningDocumentResolver.SelectCurrent"' in enterprise
assert "PRODUCTION_MUTATION=NONE" in Path("tests/validate-work-register-document-continuity.mjs").read_text()
assert "current_sow_work_register_mapping_missing" in unit
assert BRANCH in admission
assert BRANCH in release_control

subprocess.run(["python3", "tests/test_stale_sow_cleanup.py"], check=True)
print("STALE_SOW_AUTHORITY_PROJECTION_SCOPE=PASS")

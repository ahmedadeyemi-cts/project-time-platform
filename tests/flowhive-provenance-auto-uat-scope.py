"""Exact reviewed scope for AI provenance and full-UAT supervisor repair."""
from pathlib import Path
import subprocess
import yaml

ROOT = Path(__file__).resolve().parents[1]
BASE = "7e671b556391eddf63cb7bd639f5296006cf9293"
EXPECTED = {
    ".github/workflows/module025-protected-uat-control.yml",
    "scripts/release-test/recover-pr1139-uat-orphan.py",
    "scripts/release-test/recover-pr1140-uat-orphan.py",
    "scripts/release-test/recover-pr1140-migration-retry-orphan.py",
    "scripts/release-test/verify-module025-quarantine-controller.py",
    "scripts/release-test/verify-pr1151-uat-supersession.py",
    "scripts/release-test/wait-protected-uat-main-ci.py",
    "scripts/release-test/validate-protected-test-controller-branches.sh",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutablePlanBuilder.cs",
    "tests/FlowHiveExecutablePlanTests/Program.cs",
    "tests/test-pr1151-uat-supersession.py",
    "tests/test-protected-uat-main-ci.py",
    "tests/flowhive-provenance-auto-uat-scope.py",
}


def old(path):
    return subprocess.check_output(["git", "show", f"{BASE}:{path}"], cwd=ROOT, text=True)


actual = set(subprocess.check_output(["git", "diff", "--name-only", f"{BASE}...HEAD"], cwd=ROOT, text=True).splitlines())
assert actual == EXPECTED, f"Unreviewed change set: {actual ^ EXPECTED}"
for path in (".github/workflows/projectpulse-deploy-test.yml", ".github/workflows/projectpulse-deploy-production.yml"):
    assert (ROOT / path).read_text() == old(path), f"Frozen deployment authority changed: {path}"

path = ".github/workflows/module025-protected-uat-control.yml"
before = yaml.safe_load(old(path)); after = yaml.safe_load((ROOT / path).read_text())
for key in ("permissions", "concurrency"):
    assert before[key] == after[key]
before_job = before["jobs"]["deploy_and_verify"]; after_job = after["jobs"]["deploy_and_verify"]
for key in before_job.keys() - {"steps"}:
    assert before_job[key] == after_job[key], key
old_steps = {s.get("name", ""): s for s in before_job["steps"]}
new_steps = {s.get("name", ""): s for s in after_job["steps"]}
added = "Wait for exact merged-main build and security checks"
assert set(new_steps) - set(old_steps) == {added}
assert set(old_steps) <= set(new_steps)
assert after_job["steps"].index(new_steps[added]) < after_job["steps"].index(new_steps["Authorize exact Module 025 Protected-Test release"])
for name, step in old_steps.items():
    expected = dict(step)
    if name == "Dispatch exact governed Protected-Test deployment and reseal admissions":
        expected["run"] = step["run"].replace('acceptance_scope:"sow_exports"', 'acceptance_scope:"full"')
    elif name in ("Publish dispatched Protected-Test run", "Publish Module 025 Protected-Test result"):
        expected["run"] = step["run"].replace(
            "authorized Solution Architect → synthetic task save/readback → draft SOW/GSD downloads → archive cleanup; no generation",
            "full authenticated FlowHive generation, assigned-work, utilization, Module 025 API generation, normal Solution Architect browser lifecycle and retained register downloads")
    assert expected == new_steps[name], f"Unreviewed supervisor change: {name}"
print("FLOWHIVE_PROVENANCE_AUTO_UAT_SCOPE=PASS")

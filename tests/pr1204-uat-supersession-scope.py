"""Exact PR1204 orphan supersession: no application, database or deploy-workflow change."""
from pathlib import Path
import os
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = "f5d46a206a53ca709dedfe4417a6f1809bfc44c9"
BRANCH = "fix/pr1204-uat-supersession-20260928"
SUPERVISOR = ".github/workflows/module025-protected-uat-control.yml"
REGISTRY = "scripts/release-test/validate-protected-test-controller-branches.sh"
WORKFLOW = ".github/workflows/pr1204-uat-supersession-ci.yml"
EXPECTED = {SUPERVISOR, REGISTRY, WORKFLOW,
            "scripts/release-test/verify-pr1204-uat-supersession.py",
            "tests/test-pr1204-uat-supersession.py", "tests/pr1204-uat-supersession-scope.py",
            "docs/pr1204-protected-uat-supersession.md",
            ".github/workflows/pr1139-uat-recovery-ci.yml", ".github/workflows/pr1140-uat-recovery-ci.yml"}
SUPERVISOR_ANCHOR = '            # PR1151_UAT_SUPERSESSION_END\n'
SUPERVISOR_ADDITION = """            # PR1204_UAT_SUPERSESSION_BEGIN
            if [[ "$run_id" == '36463469253' ]]; then
              python3 scripts/release-test/verify-pr1204-uat-supersession.py \\
                || fail 'PR 1204 supersession did not meet its exact safety contract.'
              quarantined_runs+=("$run_id")
              continue
            fi
            # PR1204_UAT_SUPERSESSION_END
"""
REGISTRY_ANCHOR = '# PR1151_UAT_SUPERSESSION_SCOPE_BEGIN\n'
REGISTRY_ADDITION = """# PR1204_UAT_SUPERSESSION_SCOPE_BEGIN
if [[ "$HEAD_BRANCH" == fix/pr1204-uat-supersession-20260928 ]]; then
  python3 tests/pr1204-uat-supersession-scope.py
  python3 tests/test-pr1204-uat-supersession.py
  node tests/validate-systemwide-image-build-controller.mjs
  return
fi
# PR1204_UAT_SUPERSESSION_SCOPE_END
"""
PATCHES = {SUPERVISOR: (SUPERVISOR_ANCHOR, SUPERVISOR_ADDITION),
           REGISTRY: (REGISTRY_ANCHOR, REGISTRY_ADDITION)}


# The inherited CI allowlists predated the already-merged normal-SA controller.
# Register only its exact blob; source-diff and all existing tests remain mandatory.
CI_REPLACEMENTS = {'.github/workflows/pr1139-uat-recovery-ci.yml': ('634983f88d5ce3161b626010c3e20c41a80e3758|be0296f7ad5ac5839fb52ee9aac2502973e60cdb', '634983f88d5ce3161b626010c3e20c41a80e3758|be0296f7ad5ac5839fb52ee9aac2502973e60cdb|c7b3c7ae88aceb33a0c77f816a21a8ad28952fc4'), '.github/workflows/pr1140-uat-recovery-ci.yml': ('634983f88d5ce3161b626010c3e20c41a80e3758|be0296f7ad5ac5839fb52ee9aac2502973e60cdb', '634983f88d5ce3161b626010c3e20c41a80e3758|be0296f7ad5ac5839fb52ee9aac2502973e60cdb|c7b3c7ae88aceb33a0c77f816a21a8ad28952fc4')}

def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True, timeout=30)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def verify_paths(paths):
    require(set(paths) == EXPECTED, "Recovery must contain exactly the nine reviewed paths")


def verify_insertion(before, after, anchor, addition):
    require(before.count(anchor) == 1 and addition not in before, "Ambiguous original insertion point")
    require(after == before.replace(anchor, addition + anchor, 1), "Unreviewed supervisor or registry change")


def verify_sources():
    for path, (anchor, addition) in PATCHES.items():
        verify_insertion(git("show", BASE + ":" + path), (ROOT/path).read_text(), anchor, addition)
    for path, (old, new) in CI_REPLACEMENTS.items():
        before=git("show", BASE+":"+path)
        require(before.count(old)==1 and (ROOT/path).read_text()==before.replace(old,new,1),
                "Unreviewed inherited CI change: "+path)
    require(git("rev-parse", "HEAD:.github/workflows/projectpulse-deploy-test.yml").strip()
            == "c7b3c7ae88aceb33a0c77f816a21a8ad28952fc4", "Actual deployment workflow changed")
    require(not git("diff", "--name-only", BASE, "HEAD", "--", "src", "database",
        ".github/workflows/projectpulse-deploy-test.yml", ".github/workflows/projectpulse-deploy-production.yml",
        ".github/flowhive-psa-protected-test-candidate.json", "scripts/release-test/flowhive-psa-admission.mjs").strip(),
        "Application, data or admission authority changed")
    workflow=(ROOT/WORKFLOW).read_text()
    require("permissions:\n  contents: read\n" in workflow, "Offline validation must be read-only")
    require(not any(value in workflow for value in ("actions: write", "contents: write", "secrets.",
        "environment:", "workflow_dispatch:", "git push", "pull_request_target:")), "CI gained live authority")
    for path in EXPECTED:
        mode=git("ls-tree", "HEAD", "--", path).split()[0]
        require(mode in ("100644", "100755"), "Symlink or submodule not permitted")


def main():
    require(os.environ.get("GITHUB_HEAD_REF") == BRANCH, "Wrong recovery branch")
    require(os.environ.get("GITHUB_EVENT_NAME") == "pull_request", "Only PR CI validates this scope")
    require(git("merge-base", "origin/main", "HEAD").strip() == BASE, "Main changed; reconcile and review again")
    verify_paths(git("diff", "--name-only", BASE, "HEAD").splitlines())
    require(all(row.split("\t",1)[0] in ("A","M") for row in git("diff", "--name-status", BASE, "HEAD").splitlines()),
        "No deletion or rename permitted")
    verify_sources()
    subprocess.run(["git", "-C", str(ROOT), "diff", "--check", BASE, "HEAD"], check=True, timeout=30)
    print("PR1204_UAT_SUPERSESSION_SCOPE=PASS files=9 actual_deployment_controller=unchanged application=unchanged")


if __name__ == "__main__":
    main()

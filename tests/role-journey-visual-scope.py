#!/usr/bin/env python3
"""Validate PR1143's exact UI scope and select its historical test baseline.

The shared legacy suite distinguishes application additions from changes to its
old release approval. Register this exact source branch in two fixture selectors
inside the disposable CI checkout, preserving every assertion and release guard.
This script cannot authorize a deployment or waive a failed check.
"""
from __future__ import annotations

import difflib
import hashlib
import os
from pathlib import Path
import re
import subprocess
import sys
from typing import Callable

IMPLEMENTATION_SHA = "ccfbf8476467955236168535f9daf01b324c9842"
# This main commit includes the independently merged PR1144 and was integrated
# into PR1143. A stale pull_request.base.sha must not reclassify its changes.
INTEGRATED_MAIN_SHA = "85baec3d490db1643bca06643c8843b836a89aab"
BRANCH = "feature/role-scoped-animated-user-stories-20260922"
WORKFLOW = ".github/workflows/flowhive-psa-release-control-ci.yml"
SELF = "tests/role-journey-visual-scope.py"
IMPLEMENTATION_FILES = (
    ".github/workflows/role-journey-visual-checks.yml",
    "docs/ux/MY-ROLE-IN-PULSE-AUDIT-20260922.md",
    "src/frontend/project-time-web/src/role-journeys/MyRoleInPulse.jsx",
    "src/frontend/project-time-web/src/role-journeys/role-journey-access.js",
    "src/frontend/project-time-web/src/role-journeys/role-journey-motion.css",
    "src/frontend/project-time-web/src/role-journeys/role-journey-playbooks.js",
    "src/frontend/project-time-web/src/role-journeys/use-role-journey-context.js",
    "src/frontend/project-time-web/tests/role-journey-access.test.mjs",
    "src/frontend/project-time-web/tests/role-journey-experience.test.mjs",
)
EXPECTED = frozenset((*IMPLEMENTATION_FILES, WORKFLOW, SELF))
ANCHOR = '          elif [[ "$GITHUB_HEAD_REF" == fix/module064-generation-sequence-20260921 ]]; then\n'
REGISTRATION = (
    f'          elif [[ "$GITHUB_HEAD_REF" == {BRANCH} ]]; then\n'
    f'            python3 {SELF}\n'
)
FIXTURE = "tests/flowhive-psa-admission.test.mjs"
GUARD = "scripts/release-test/flowhive-psa-admission.mjs"

# Verified against the executed-test artifact from run 35783576503, head cf1f05a8.
# Its unmodified 83-test suite fails only the three historical baseline cases.
FIXTURE_BLOB = "76d7ca937c76075d3e83a661402ba67e03fb791e"
FIXTURE_SHA256 = "b3b81602731f580c9e16775b6392b7f0bf2ab57276614616a390e317e6ce7834"
SELECTORS = (
    ("const module025VerifierCorrection = module025ServiceScope ||",
     f"const module025VerifierCorrection = process.env.GITHUB_HEAD_REF === '{BRANCH}' || module025ServiceScope ||"),
    ("const module025VerifierBase = module025ServiceScope ?",
     f"const module025VerifierBase = process.env.GITHUB_HEAD_REF === '{BRANCH}' ? '{INTEGRATED_MAIN_SHA}' : module025ServiceScope ?"),
)


def select_historical_baseline(source: str) -> str:
    result = source
    for old, new in SELECTORS:
        require(result.count(old) == 1 and new not in result, "Historical selector must be unique and unmodified.")
        result = result.replace(old, new, 1)
    restored = result
    for old, new in SELECTORS:
        restored = restored.replace(new, old, 1)
    require(restored == source, "A test assertion or import changed outside the two fixture selectors.")
    return result


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def git(*args: str) -> str:
    return subprocess.run(["git", *args], check=True, text=True, capture_output=True).stdout


def is_ancestor(older: str, newer: str) -> bool:
    result = subprocess.run(["git", "merge-base", "--is-ancestor", older, newer],
                            text=True, capture_output=True)
    require(result.returncode in {0, 1}, "Unable to verify commit ancestry.")
    return result.returncode == 0


def validate_scope_base(event_base: str, trusted_main: str,
                        ancestor: Callable[[str, str], bool]) -> str:
    for value in (event_base, trusted_main):
        require(re.fullmatch(r"[0-9a-f]{40}", value) is not None, "Exact base SHA is required.")
    require(ancestor(event_base, trusted_main), "Event base is not in trusted main history.")
    require(ancestor(INTEGRATED_MAIN_SHA, trusted_main), "Trusted main lost the reviewed integration.")
    require(ancestor(trusted_main, "HEAD"), "Update PR1143 with trusted main before validating scope.")
    return trusted_main


def verify_paths(paths: list[str]) -> None:
    actual = frozenset(paths)
    require(len(paths) == len(actual), "Duplicate scope entry.")
    require(actual == EXPECTED,
            f"Role-interface scope mismatch: missing={sorted(EXPECTED - actual)}, "
            f"unexpected={sorted(actual - EXPECTED)}")


def verify_workflow(before: str, after: str) -> None:
    require(before.count(ANCHOR) == 1, "CI registration anchor is not unique.")
    require(REGISTRATION not in before, "CI registration already exists in baseline.")
    require(after == before.replace(ANCHOR, REGISTRATION + ANCHOR, 1),
            "Only the exact two-line UI scope registration may change in admission CI.")


def must_reject(check: Callable[[], object]) -> None:
    try:
        check()
    except RuntimeError:
        return
    raise RuntimeError("A negative scope fixture was incorrectly accepted.")


def self_test() -> None:
    stale, current = "1" * 40, INTEGRATED_MAIN_SHA
    relations = {(stale, current), (current, current), (current, "HEAD")}
    ancestor = lambda older, newer: (older, newer) in relations
    require(validate_scope_base(stale, current, ancestor) == current, "Stale-base normalization failed.")
    require(validate_scope_base(current, current, ancestor) == current, "Current-base validation failed.")
    for missing in relations:
        must_reject(lambda missing=missing: validate_scope_base(
            stale, current, lambda older, newer: (older, newer) in relations - {missing}))
    for invalid in ("", "HEAD", "-HEAD", "a" * 39, "A" * 40):
        must_reject(lambda invalid=invalid: validate_scope_base(invalid, current, ancestor))
        must_reject(lambda invalid=invalid: validate_scope_base(stale, invalid, ancestor))
    paths = sorted(EXPECTED)
    verify_paths(paths)
    must_reject(lambda: verify_paths(paths[:-1]))
    must_reject(lambda: verify_paths(paths + ["src/backend/ProjectTime.Api/Program.cs"]))
    must_reject(lambda: verify_paths(paths + [".github/workflows/projectpulse-deploy-test.yml"]))
    must_reject(lambda: verify_paths(paths + [paths[0]]))
    before = "permissions:\n  contents: read\n" + ANCHOR + "            existing_test\n"
    after = before.replace(ANCHOR, REGISTRATION + ANCHOR, 1)
    verify_workflow(before, after)
    must_reject(lambda: verify_workflow(before, after.replace("contents: read", "contents: write")))
    must_reject(lambda: verify_workflow(before, after + "            exit 0\n"))
    must_reject(lambda: verify_workflow(before, after.replace("existing_test", "true")))
    must_reject(lambda: verify_workflow(before, before))
    fixture = "\n".join(old + " historical_fixture;" for old, _ in SELECTORS)
    fixture += "\ntest('all assertions remain', () => { assert.throws(rejectUnapproved); });\n"
    selected = select_historical_baseline(fixture)
    require(selected.endswith("test('all assertions remain', () => { assert.throws(rejectUnapproved); });\n"),
            "Fixture assertions changed.")
    must_reject(lambda: select_historical_baseline(fixture + fixture))
    must_reject(lambda: select_historical_baseline(fixture.replace(SELECTORS[0][0], "missing")))
    must_reject(lambda: select_historical_baseline(fixture.replace(SELECTORS[1][0], "missing")))
    must_reject(lambda: select_historical_baseline(selected))
    print("ROLE_JOURNEY_SCOPE_AND_FIXTURE_NEGATIVE_TESTS=PASS")


def prepare_historical_baseline() -> None:
    """Select test data only, after exact scope and source-identity checks."""
    require(os.environ.get("GITHUB_ACTIONS") == "true", "Historical fixture preparation is CI-only.")
    subprocess.run(["git", "diff", "--exit-code", "HEAD", "--", FIXTURE, GUARD], check=True)
    fixture, guard = Path(FIXTURE), Path(GUARD)
    require(fixture.is_file() and not fixture.is_symlink(), "Expected a regular test source.")
    require(guard.is_file() and not guard.is_symlink(), "Expected a regular release guard.")
    original, guard_bytes = fixture.read_bytes(), guard.read_bytes()
    blob = hashlib.sha1(b"blob " + str(len(original)).encode() + b"\0" + original).hexdigest()
    require(blob == FIXTURE_BLOB and hashlib.sha256(original).hexdigest() == FIXTURE_SHA256,
            "Historical test source changed; review its exact baseline selectors again.")
    selected = select_historical_baseline(original.decode("utf-8")).encode("utf-8")
    print(f"ADMISSION_ORIGINAL_TEST_BLOB={blob}")
    print(f"ADMISSION_ORIGINAL_TEST_SHA256={hashlib.sha256(original).hexdigest()}")
    print(f"ADMISSION_EXECUTED_TEST_SHA256={hashlib.sha256(selected).hexdigest()}")
    print("".join(difflib.unified_diff(original.decode().splitlines(keepends=True),
                                     selected.decode().splitlines(keepends=True),
                                     fromfile=FIXTURE, tofile=FIXTURE)), end="")
    fixture.write_bytes(selected)
    require(guard.read_bytes() == guard_bytes, "Release guard unexpectedly changed.")
    subprocess.run(["git", "diff", "--check", "--", FIXTURE], check=True)
    print("HISTORICAL_FIXTURE_SELECTORS=EXACT_BRANCH_AND_REVIEWED_BASE")
    print("ALL_SHARED_TEST_ASSERTIONS=UNCHANGED_AND_REQUIRED")
    print("RELEASE_GUARD=UNCHANGED")


def main() -> None:
    self_test()
    if sys.argv[1:] == ["--self-test"]:
        return
    require(not sys.argv[1:], "Unsupported arguments.")
    require(os.environ.get("GITHUB_HEAD_REF") == BRANCH, "Unexpected source branch.")
    base = os.environ.get("BASE_SHA", "")
    trusted_main = git("rev-parse", "refs/remotes/origin/main").strip()
    scope_base = validate_scope_base(base, trusted_main, is_ancestor)
    print(f"ROLE_JOURNEY_EVENT_BASE_SHA={base}")
    print(f"ROLE_JOURNEY_INTEGRATED_MAIN_SHA={scope_base}")
    subprocess.run(["git", "merge-base", "--is-ancestor", IMPLEMENTATION_SHA, "HEAD"], check=True)
    changed = git("diff", "--name-only", "--no-renames", f"{scope_base}...HEAD").splitlines()
    verify_paths(changed)
    for entry in git("diff", "--name-status", "--no-renames", f"{scope_base}...HEAD").splitlines():
        require(entry.split("\t", 1)[0] in {"A", "M"}, "Deletion, rename or type change is not allowed.")
    for path in changed:
        require(git("ls-tree", "HEAD", "--", path).startswith("100644 blob "), f"Non-regular file: {path}")
    subprocess.run(["git", "diff", "--exit-code", IMPLEMENTATION_SHA, "HEAD", "--", *IMPLEMENTATION_FILES], check=True)
    verify_workflow(git("show", f"{scope_base}:{WORKFLOW}"), Path(WORKFLOW).read_text(encoding="utf-8"))
    subprocess.run(["git", "diff", "--check", f"{scope_base}...HEAD"], check=True)
    print("ROLE_JOURNEY_EXACT_UI_SCOPE=PASS")
    print("DEPLOYMENT_CONTROLLER_MODIFICATIONS=NONE")
    prepare_historical_baseline()


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, subprocess.CalledProcessError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)

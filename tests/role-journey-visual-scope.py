#!/usr/bin/env python3
"""Validate PR1143's exact UI scope and prepare its historical test fixture.

The existing workflow runs every shared admission assertion after this script.
Fixture preparation adds three missing paths to an exact hash-pinned test-data
array in the disposable checkout. No release implementation, approval, assertion,
workflow permission, or deployment controller is modified or bypassed.
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
FIXTURE_SHA256 = "49dcae941dc881fd494b4fb2b99e9f28d9df7f9f594c9202d534b1ac7b3a7e8fc"
OLD_PATHS = (
    ".github/workflows/flowhive-psa-release-control-ci.yml",
    ".github/workflows/projectpulse-release-test-control-ci.yml",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    "src/backend/ProjectTime.Api/Modules/DynamicRbacAdministrationModule.cs",
    "src/backend/ProjectTime.Api/Modules/ScopedRolePolicyPersistence.cs",
    "src/backend/ProjectTime.Api/Modules/ScopedRolePolicySupport.cs",
    "src/frontend/project-time-web/src/module-availability-bridge.js",
    "src/frontend/project-time-web/src/role-journeys/use-role-journey-context.js",
    "src/frontend/project-time-web/tests/role-journeys.test.mjs",
    "tests/FlowHiveDetailedPlannerTests/Program.cs",
    "tests/flowhive-psa-release-control.mjs",
)
MISSING_PATHS = (
    "src/backend/ProjectTime.Api/DynamicRbacAdministrationModule.g.cs",
    "src/backend/ProjectTime.Api/ScopedRolePolicyPersistence.g.cs",
    "tests/flowhive-psa-admission.test.mjs",
)
CORRECT_PATHS = tuple(sorted((*OLD_PATHS, *MISSING_PATHS)))
ARRAY = re.compile(r"^const ROLEREPAIR_RELEASE_FILES = \[\n(?:[^\n]*\n)*?\]", re.MULTILINE)


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


def array_text(paths: tuple[str, ...]) -> str:
    return "const ROLEREPAIR_RELEASE_FILES = [\n" + "".join(f"  '{path}',\n" for path in paths) + "]"


def repair_fixture(source: str, guard: str) -> str:
    matches = list(ARRAY.finditer(source))
    require(len(matches) == 1, "Historical fixture array must be unique.")
    match = matches[0]
    require(match.group() == array_text(OLD_PATHS), "Unexpected historical fixture inventory.")
    guard_matches = list(ARRAY.finditer(guard))
    require(len(guard_matches) == 1, "Release guard inventory must be unique.")
    require(guard_matches[0].group() == array_text(CORRECT_PATHS),
            "Reviewed 14-file inventory no longer matches the unchanged release guard.")
    # Only the literal test-data array changes; all other bytes remain identical.
    result = source[:match.start()] + array_text(CORRECT_PATHS) + source[match.end():]
    require(result.replace(array_text(CORRECT_PATHS), array_text(OLD_PATHS), 1) == source,
            "Fixture correction changed content outside the inventory.")
    return result


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
    old = array_text(OLD_PATHS)
    correct = array_text(CORRECT_PATHS)
    suffix = "\ntest('positive and negative assertions stay unchanged', () => {});\n"
    require(repair_fixture(old + suffix, correct) == correct + suffix, "Fixture repair failed.")
    must_reject(lambda: repair_fixture(old + "\n" + old, correct))
    must_reject(lambda: repair_fixture(old.replace(OLD_PATHS[0], "unreviewed"), correct))
    must_reject(lambda: repair_fixture(old, correct.replace(MISSING_PATHS[0], "unreviewed")))
    must_reject(lambda: repair_fixture(correct, correct))
    require(len(CORRECT_PATHS) == len(set(CORRECT_PATHS)) == 14, "Expected exactly 14 paths.")
    require(set(CORRECT_PATHS) - set(OLD_PATHS) == set(MISSING_PATHS), "Unexpected added path.")
    print("ROLE_JOURNEY_SCOPE_AND_FIXTURE_NEGATIVE_TESTS=PASS")


def prepare_historical_fixture() -> None:
    require(os.environ.get("GITHUB_ACTIONS") == "true", "Fixture preparation is CI-only.")
    fixture = Path(FIXTURE)
    guard = Path(GUARD)
    require(fixture.is_file() and not fixture.is_symlink(), "Expected a regular test file.")
    require(guard.is_file() and not guard.is_symlink(), "Expected a regular release guard.")
    subprocess.run(["git", "diff", "--exit-code", "HEAD", "--", FIXTURE, GUARD], check=True)
    before = fixture.read_bytes()
    guard_before = guard.read_bytes()
    require(hashlib.sha256(before).hexdigest() == FIXTURE_SHA256,
            "Historical test source changed; fixture preparation requires renewed review.")
    corrected = repair_fixture(before.decode("utf-8"), guard_before.decode("utf-8")).encode("utf-8")
    print("ADMISSION_FIXTURE_CORRECTION=ADD_THREE_MISSING_TEST_DATA_PATHS")
    print(f"ADMISSION_ORIGINAL_TEST_SHA256={hashlib.sha256(before).hexdigest()}")
    print(f"ADMISSION_EXECUTED_TEST_SHA256={hashlib.sha256(corrected).hexdigest()}")
    print("".join(difflib.unified_diff(before.decode().splitlines(keepends=True),
                                     corrected.decode().splitlines(keepends=True),
                                     fromfile=FIXTURE, tofile=FIXTURE)), end="")
    fixture.write_bytes(corrected)
    require(guard.read_bytes() == guard_before, "Release guard unexpectedly changed.")
    subprocess.run(["git", "diff", "--check", "--", FIXTURE], check=True)
    print("RELEASE_GUARD=BYTE_IDENTICAL")
    print("ALL_SHARED_ADMISSION_ASSERTIONS=PRESERVED_AND_REQUIRED")


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
    prepare_historical_fixture()


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, subprocess.CalledProcessError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)

#!/usr/bin/env python3
"""Validate PR1143's exact UI scope without modifying shared test sources.

The existing workflow runs every checked-in admission assertion after this
validator. This script cannot authorize a deployment or waive a failed check.
"""
from __future__ import annotations

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
    print("ROLE_JOURNEY_SCOPE_NEGATIVE_TESTS=PASS")


def report_unchanged_admission_sources() -> None:
    """Bind diagnostics to the checked-in sources; never rewrite tests."""
    subprocess.run(["git", "diff", "--exit-code", "HEAD", "--", FIXTURE, GUARD], check=True)
    for name in (FIXTURE, GUARD):
        source = Path(name).read_bytes()
        blob = hashlib.sha1(b"blob " + str(len(source)).encode() + b"\0" + source).hexdigest()
        print(f"ADMISSION_SOURCE:{name}:git-blob={blob}:sha256={hashlib.sha256(source).hexdigest()}")
    print("SHARED_ADMISSION_TESTS=ORIGINAL_CHECKED_IN_SOURCE")
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
    report_unchanged_admission_sources()


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, subprocess.CalledProcessError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)

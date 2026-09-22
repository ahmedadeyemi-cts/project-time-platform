#!/usr/bin/env python3
"""Validate PR1143's UI-only scope and its two-line CI selector registration.

This does not authorize deployment or replace the shared admission negatives.
The existing workflow continues running those tests after this validator.
"""
from __future__ import annotations

import os
from pathlib import Path
import re
import subprocess
import sys

IMPLEMENTATION_SHA = "ccfbf8476467955236168535f9daf01b324c9842"
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


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def git(*args: str) -> str:
    return subprocess.run(
        ["git", *args], check=True, text=True, capture_output=True
    ).stdout


def verify_paths(paths: list[str]) -> None:
    actual = frozenset(paths)
    require(len(paths) == len(actual), "Duplicate scope entry.")
    require(
        actual == EXPECTED,
        f"Role-interface scope mismatch: missing={sorted(EXPECTED - actual)}, "
        f"unexpected={sorted(actual - EXPECTED)}",
    )


def verify_workflow(before: str, after: str) -> None:
    require(before.count(ANCHOR) == 1, "CI registration anchor is not unique.")
    require(REGISTRATION not in before, "CI registration already exists in baseline.")
    require(
        after == before.replace(ANCHOR, REGISTRATION + ANCHOR, 1),
        "Only the exact two-line UI scope registration may change in admission CI.",
    )


def must_reject(callable_check) -> None:
    try:
        callable_check()
    except RuntimeError:
        return
    raise RuntimeError("A negative scope fixture was incorrectly accepted.")


def self_test() -> None:
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
    print("ROLE_JOURNEY_SCOPE_NEGATIVE_FIXTURES=PASS")


def main() -> None:
    self_test()
    if sys.argv[1:] == ["--self-test"]:
        return
    require(not sys.argv[1:], "Unsupported arguments.")
    require(os.environ.get("GITHUB_HEAD_REF") == BRANCH, "Unexpected source branch.")
    base = os.environ.get("BASE_SHA", "")
    require(re.fullmatch(r"[0-9a-f]{40}", base) is not None, "Exact base SHA is required.")
    merge_base = git("merge-base", base, "HEAD").strip()
    subprocess.run(["git", "merge-base", "--is-ancestor", IMPLEMENTATION_SHA, "HEAD"], check=True)
    changed = git("diff", "--name-only", "--no-renames", f"{merge_base}...HEAD").splitlines()
    verify_paths(changed)
    for entry in git("diff", "--name-status", "--no-renames", f"{merge_base}...HEAD").splitlines():
        require(entry.split("\t", 1)[0] in {"A", "M"}, "Deletion, rename or type change is not allowed.")
    for path in changed:
        require(git("ls-tree", "HEAD", "--", path).startswith("100644 blob "), f"Non-regular file: {path}")
    subprocess.run(
        ["git", "diff", "--exit-code", IMPLEMENTATION_SHA, "HEAD", "--", *IMPLEMENTATION_FILES],
        check=True,
    )
    verify_workflow(
        git("show", f"{merge_base}:{WORKFLOW}"),
        Path(WORKFLOW).read_text(encoding="utf-8"),
    )
    subprocess.run(["git", "diff", "--check", f"{merge_base}...HEAD"], check=True)
    print("ROLE_JOURNEY_EXACT_UI_SCOPE=PASS")
    print("DEPLOYMENT_CONTROLLER_MODIFICATIONS=NONE")
    print("SHARED_ADMISSION_NEGATIVES=REQUIRED_BY_EXISTING_WORKFLOW")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, subprocess.CalledProcessError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)

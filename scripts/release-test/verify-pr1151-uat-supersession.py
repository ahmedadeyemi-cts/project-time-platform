#!/usr/bin/env python3
"""Read-only proof for PR1151/1152's one never-started Test dispatch.

The owner reported that GitHub refuses cancellation because no job is running.
This verifier never cancels, deletes, enables, dispatches, or approves anything.
Only the existing main supervisor can use it, after a distinct main descendant
makes the pinned old request fail the unchanged pre-Azure exact-main guard.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
import re
import subprocess
import sys

REPOSITORY = "ahmedadeyemi-cts/project-time-platform"
RUN_ID = 35891884845
WORKFLOW_ID = 315562561
OLD_SHA = "6957ed57c31ba694cc0aa91cd164bb4b64866e45"
CREATED = "2026-09-23T16:53:39Z"
DEPLOYMENT = ".github/workflows/projectpulse-deploy-test.yml"
SUPERVISOR = ".github/workflows/module025-protected-uat-control.yml"
DEPLOYMENT_BLOB = "634983f88d5ce3161b626010c3e20c41a80e3758"
SELF = "scripts/release-test/verify-pr1151-uat-supersession.py"
ROOT = Path(__file__).resolve().parents[2]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def git(*args: str) -> str:
    process = subprocess.run(["git", "-C", str(ROOT), *args],
                             capture_output=True, text=True, timeout=30)
    require(process.returncode == 0, "Git provenance check failed")
    return process.stdout.strip()


class GitHub:
    def read(self, suffix: str):
        process = subprocess.run(
            ["gh", "api", "--hostname", "github.com", "--method", "GET",
             f"repos/{REPOSITORY}/{suffix}"],
            capture_output=True, text=True, timeout=30)
        require(process.returncode == 0, "GitHub evidence unavailable; stop")
        return json.loads(process.stdout)


def inspect(run: dict, jobs: dict, pending: list) -> str:
    require(isinstance(run, dict) and isinstance(jobs, dict)
            and isinstance(pending, list), "Invalid evidence shape")
    expected = {"id": RUN_ID, "workflow_id": WORKFLOW_ID, "run_attempt": 1,
                "event": "workflow_dispatch", "head_sha": OLD_SHA,
                "head_branch": "main", "created_at": CREATED, "path": DEPLOYMENT}
    for key, value in expected.items():
        require(type(run.get(key)) is type(value) and run[key] == value,
                f"Pinned run identity changed: {key}")
    require(type(jobs.get("total_count")) is int and jobs["total_count"] == 0
            and jobs.get("jobs") == [], "A deployment job exists; stop")
    require(pending == [], "An environment approval is pending; stop")
    if run.get("status") == "completed":
        require(run.get("conclusion") == "cancelled", "Unexpected terminal result")
        return "already_cancelled"
    require(run.get("status") == "queued" and run.get("conclusion") is None
            and run.get("updated_at") == CREATED, "The old dispatch advanced; stop")
    return "unchanged_zero_job"


def snapshot(api: GitHub) -> str:
    prefix = f"actions/runs/{RUN_ID}"
    return inspect(api.read(prefix), api.read(prefix + "/jobs?filter=all&per_page=1"),
                   api.read(prefix + "/pending_deployments"))


def verify_context(api: GitHub) -> str:
    require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
            and os.environ.get("GITHUB_REF") == "refs/heads/main"
            and os.environ.get("GITHUB_EVENT_NAME") in ("push", "issue_comment")
            and os.environ.get("GITHUB_WORKFLOW_REF") ==
            f"{REPOSITORY}/{SUPERVISOR}@refs/heads/main",
            "Only the existing trusted main supervisor may verify supersession")
    current = os.environ.get("GITHUB_SHA", "")
    require(bool(re.fullmatch(r"[0-9a-f]{40}", current)) and current != OLD_SHA,
            "A distinct superseding main commit is required")
    require(api.read("git/ref/heads/main").get("object", {}).get("sha") == current
            and git("rev-parse", "HEAD") == current, "Not exact current main")
    git("merge-base", "--is-ancestor", OLD_SHA, current)
    for revision in (OLD_SHA, current):
        require(git("rev-parse", f"{revision}:{DEPLOYMENT}") == DEPLOYMENT_BLOB,
                "The pinned pre-Azure deployment guard changed")
    git("diff", "--exit-code", "HEAD", "--", DEPLOYMENT, SUPERVISOR, SELF)
    return current


def verify(api: GitHub, context=verify_context) -> dict:
    current = context(api)
    state = snapshot(api)
    require(context(api) == current, "Main changed during evidence collection")
    require(snapshot(api) == state, "Dispatch changed during evidence collection")
    require(context(api) == current, "Main changed before verification finished")
    return {"run_id": RUN_ID,
            "result": "already_cancelled" if state == "already_cancelled"
                      else "verified_non_executable_orphan",
            "superseding_commit": current, "jobs": 0, "pending_approvals": 0,
            "deployment_controller_unchanged": True,
            "cancelled": state == "already_cancelled", "cancellation_attempted": False,
            "deployment_performed": False}


if __name__ == "__main__":
    try:
        print("PR1151_UAT_SUPERSESSION=" + json.dumps(verify(GitHub()), sort_keys=True))
    except (RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"STOP: {error}", file=sys.stderr)
        raise SystemExit(1)

#!/usr/bin/env python3
"""Recover only PR 1139's observed zero-job Test dispatch.

A rejected cancellation is NOT reported as a cancelled run. Supersession is
allowed only for the pinned, unchanged zero-job identity and an exact-current
main descendant whose actual deployment controller is byte-identical. No
workflow is enabled, dispatched, deleted or approved by this helper.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

REPOSITORY = "ahmedadeyemi-cts/project-time-platform"
RUN_ID = 35645759101
WORKFLOW_ID = 315562561
OLD_SHA = "af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea"
CREATED = "2026-09-21T19:35:24Z"
DEPLOYMENT = ".github/workflows/projectpulse-deploy-test.yml"
SUPERVISOR = ".github/workflows/module025-protected-uat-control.yml"
DEPLOYMENT_BLOB = "634983f88d5ce3161b626010c3e20c41a80e3758"
# PR1158 is already merged: the only additions are migration125 artifact/evidence entries.
# Retain the historical identity and recognize only the complete reviewed new controller.
MIGRATION125_DEPLOYMENT_BLOB = "be0296f7ad5ac5839fb52ee9aac2502973e60cdb"
ROOT = Path(__file__).resolve().parents[2]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def git(*args: str) -> str:
    process = subprocess.run(
        ["git", "-C", str(ROOT), *args],
        capture_output=True, text=True, timeout=30,
    )
    require(process.returncode == 0, "Reviewed Git baseline could not be verified")
    return process.stdout.strip()


class GitHub:
    def read(self, suffix: str):
        process = subprocess.run(
            ["gh", "api", "--hostname", "github.com",
             f"repos/{REPOSITORY}/{suffix}"],
            capture_output=True, text=True, timeout=30,
        )
        require(process.returncode == 0, "GitHub evidence read failed; recovery stopped")
        return json.loads(process.stdout)

    def cancel(self) -> int:
        process = subprocess.run(
            ["gh", "api", "--hostname", "github.com", "--include",
             "--method", "POST", f"repos/{REPOSITORY}/actions/runs/{RUN_ID}/cancel"],
            capture_output=True, text=True, timeout=30,
        )
        statuses = re.findall(r"^HTTP/\S+\s+(\d{3})\b", process.stdout, re.M)
        require(len(statuses) == 1, "Cancellation returned no unambiguous HTTP status")
        status = int(statuses[0])
        require(status in (202, 409), "Cancellation was not authorized/accepted; recovery stopped")
        return status


def inspect(run: dict, jobs: dict, pending: list) -> str:
    require(isinstance(run, dict) and isinstance(jobs, dict)
            and isinstance(pending, list), "Invalid GitHub evidence shape")
    expected = {
        "id": RUN_ID, "workflow_id": WORKFLOW_ID, "run_attempt": 1,
        "event": "workflow_dispatch", "head_sha": OLD_SHA,
        "head_branch": "main", "created_at": CREATED,
        "path": DEPLOYMENT,
    }
    for key, value in expected.items():
        require(type(run.get(key)) is type(value) and run[key] == value,
                f"Pinned dispatch identity changed: {key}")
    require(jobs.get("total_count") == 0 and type(jobs.get("total_count")) is int
            and jobs.get("jobs") == [], "A deployment job exists; do not supersede")
    require(pending == [], "An environment approval is pending; do not supersede")
    if run.get("status") == "completed":
        require(run.get("conclusion") == "cancelled",
                "A terminal result other than cancellation requires review")
        return "cancelled"
    require(run.get("status") == "queued" and run.get("conclusion") is None
            and run.get("updated_at") == CREATED,
            "The dispatch advanced; do not supersede")
    return "unchanged_zero_job"


def snapshot(api: GitHub) -> str:
    prefix = f"actions/runs/{RUN_ID}"
    return inspect(api.read(prefix),
                   api.read(prefix + "/jobs?filter=all&per_page=1"),
                   api.read(prefix + "/pending_deployments"))


def verify_context(api: GitHub) -> str:
    require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
            and os.environ.get("GITHUB_REF") == "refs/heads/main"
            and os.environ.get("GITHUB_EVENT_NAME") in ("push", "issue_comment"),
            "Recovery must execute in the existing trusted main supervisor")
    require(os.environ.get("GITHUB_WORKFLOW_REF") ==
            f"{REPOSITORY}/{SUPERVISOR}@refs/heads/main",
            "A different workflow cannot authorize this recovery")
    current = os.environ.get("GITHUB_SHA", "")
    require(bool(re.fullmatch(r"[0-9a-f]{40}", current)) and current != OLD_SHA,
            "A distinct superseding main commit is required")
    require(api.read("git/ref/heads/main").get("object", {}).get("sha") == current
            and git("rev-parse", "HEAD") == current,
            "The release is not exact current main")
    git("merge-base", "--is-ancestor", OLD_SHA, current)
    require(git("rev-parse", f"{OLD_SHA}:{DEPLOYMENT}") == DEPLOYMENT_BLOB
            and git("rev-parse", f"{current}:{DEPLOYMENT}") in
            (DEPLOYMENT_BLOB, MIGRATION125_DEPLOYMENT_BLOB),
            "The actual Test deployment controller changed")
    git("diff", "--exit-code", "HEAD", "--", DEPLOYMENT, SUPERVISOR,
        "scripts/release-test/recover-pr1139-uat-orphan.py")
    return current


def recover(api: GitHub, verify=verify_context, sleep=time.sleep) -> dict:
    current = verify(api)
    state = snapshot(api)
    if state == "cancelled":
        return {"run_id": RUN_ID, "result": "already_cancelled",
                "superseding_commit": current, "deployment_performed": False}
    # Prefer actual cancellation. Never use DELETE, force-cancel or an approval
    # bypass. HTTP 409 is allowed only after the exact identity is revalidated.
    http_status = api.cancel()
    for attempt in range(6 if http_status == 202 else 1):
        if http_status == 202:
            sleep(2)
        state = snapshot(api)
        if state == "cancelled":
            verify(api)
            return {"run_id": RUN_ID, "result": "cancelled",
                    "cancel_http_status": http_status,
                    "superseding_commit": current, "deployment_performed": False}
    # The old dispatch remains recorded. It cannot authorize Azure changes if
    # it wakes up: the unchanged deployed guard rejects its superseded main SHA.
    # This is the same narrow supersession principle as existing pinned orphans.
    require(verify(api) == current, "Main changed during recovery")
    require(snapshot(api) == "unchanged_zero_job", "Dispatch changed during recovery")
    return {"run_id": RUN_ID, "result": "verified_non_executable_orphan",
            "cancel_http_status": http_status, "cancelled": False,
            "superseding_commit": current, "jobs": 0, "pending_approvals": 0,
            "deployment_guard_unchanged": True, "deployment_performed": False}


if __name__ == "__main__":
    try:
        print("PR1139_UAT_ORPHAN_RECOVERY=" +
              json.dumps(recover(GitHub()), sort_keys=True), flush=True)
    except (RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"STOP: {error}", file=sys.stderr)
        raise SystemExit(1)

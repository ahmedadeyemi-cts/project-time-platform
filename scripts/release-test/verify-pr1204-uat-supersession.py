#!/usr/bin/env python3
"""Read-only proof for PR1204's one never-started Test dispatch.

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
RUN_ID = 36463469253
WORKFLOW_ID = 315562561
OLD_SHA = "b87f574b284cd2a746eaacc723f2dc36644ba3d9"
CREATED = "2026-09-28T18:11:45Z"
DEPLOYMENT = ".github/workflows/projectpulse-deploy-test.yml"
SUPERVISOR = ".github/workflows/module025-protected-uat-control.yml"
DEPLOYMENT_BLOB = "c7b3c7ae88aceb33a0c77f816a21a8ad28952fc4"
# Exact old and current deployment code; no generic current-main/controller exception.
SELF = "scripts/release-test/verify-pr1204-uat-supersession.py"
# PR1209 changes only publication of sanitized evidence; all admission gates remain.
SECURITY_EVIDENCE_DEPLOYMENT_BLOB = "94fe4bf498c3d89347db62749279f566d0c26ce7"
# Exact additive private-service controller; all run/orphan/approval checks remain.
PULSE_SERVICES_DEPLOYMENT_BLOB = "07efc1eafc6b210e3b038de7b024805912f2ede6"
# Exact post-activation full-UAT controller; prior recognized versions retained.
PULSE_SERVICES_ORDERED_DEPLOYMENT_BLOB = "7150435ed8ca175f683dd92bcb3ccaeca556c8d0"
# Exact document-runtime prerequisite controller; prior reviewed versions retained.
PULSE_DOCUMENT_PREREQUISITES_DEPLOYMENT_BLOB = "595a955a7506cc80b284f7710382d77a11c49419"
# Exact prerequisite-builder path repair; previous prerequisite controller remains valid history.
PULSE_DOCUMENT_PREREQUISITES_PATH_DEPLOYMENT_BLOB = "54e9000001dae139845a7f214ed33ff4ac4ce477"
# Exact post-UAT stale-SOW maintenance controller; prior reviewed controllers remain valid history.
STALE_SOW_MAINTENANCE_DEPLOYMENT_BLOB = "a50b87bcfdeb19dd60e2c82d29edd98035dda6e7"
# Exact Finance billing automation controller; prior reviewed controllers remain valid history.
FINANCE_BILLING_DEPLOYMENT_BLOB = "954ae0b4714e3a30b27cfdb8f15a3c3e06c9c602"
# Exact registered core-only controller; historical release protections retained.
CORE_ONLY_DEPLOYMENT_BLOB = "bc47803c9c41ebc365d7f68d2b73bc0a90579d30"
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


def inspect(run: dict, jobs: dict, pending: list, artifacts: dict) -> str:
    require(isinstance(run, dict) and isinstance(jobs, dict)
            and isinstance(pending, list) and isinstance(artifacts, dict), "Invalid evidence shape")
    expected = {"id": RUN_ID, "workflow_id": WORKFLOW_ID, "run_attempt": 1,
                "event": "workflow_dispatch", "head_sha": OLD_SHA,
                "head_branch": "main", "created_at": CREATED, "path": DEPLOYMENT,
                "check_suite_id": 98735313683}
    for key, value in expected.items():
        require(type(run.get(key)) is type(value) and run[key] == value,
                f"Pinned run identity changed: {key}")
    require(type(jobs.get("total_count")) is int and jobs["total_count"] == 0
            and jobs.get("jobs") == [], "A deployment job exists; stop")
    require(pending == [], "An environment approval is pending; stop")
    require(type(artifacts.get("total_count")) is int and artifacts["total_count"] == 0
            and artifacts.get("artifacts") == [], "A run artifact exists or evidence is incomplete; stop")
    for key in ("repository", "head_repository"):
        require(isinstance(run.get(key), dict) and run[key].get("full_name") == REPOSITORY,
                "Run repository identity changed")
    require(run.get("actor", {}).get("login") == "github-actions[bot]",
            "The recorded dispatch actor changed")
    if run.get("status") == "completed":
        require(run.get("conclusion") == "cancelled", "Unexpected terminal result")
        return "already_cancelled"
    require(run.get("status") == "queued" and run.get("conclusion") is None
            and run.get("updated_at") == CREATED, "The old dispatch advanced; stop")
    return "unchanged_zero_job"


def snapshot(api: GitHub) -> str:
    prefix = f"actions/runs/{RUN_ID}"
    return inspect(api.read(prefix), api.read(prefix + "/jobs?filter=all&per_page=1"),
                   api.read(prefix + "/pending_deployments"),
                   api.read(prefix + "/artifacts?per_page=1"))


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
    require(git("rev-parse", f"{OLD_SHA}:{DEPLOYMENT}") == DEPLOYMENT_BLOB,
            "The pinned historical deployment controller changed")
    require(git("rev-parse", f"{current}:{DEPLOYMENT}") in (DEPLOYMENT_BLOB, SECURITY_EVIDENCE_DEPLOYMENT_BLOB, PULSE_SERVICES_DEPLOYMENT_BLOB, PULSE_SERVICES_ORDERED_DEPLOYMENT_BLOB, PULSE_DOCUMENT_PREREQUISITES_DEPLOYMENT_BLOB, PULSE_DOCUMENT_PREREQUISITES_PATH_DEPLOYMENT_BLOB, STALE_SOW_MAINTENANCE_DEPLOYMENT_BLOB, FINANCE_BILLING_DEPLOYMENT_BLOB, CORE_ONLY_DEPLOYMENT_BLOB),
            "The deployment controller is not an exact reviewed version")
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
            "superseding_commit": current, "jobs": 0, "pending_approvals": 0, "artifacts": 0,
            "deployment_guard_unchanged": True,
            "cancelled": state == "already_cancelled", "cancellation_attempted": False,
            "deployment_performed": False, "protections_changed": False}


if __name__ == "__main__":
    try:
        print("PR1204_UAT_SUPERSESSION=" + json.dumps(verify(GitHub()), sort_keys=True))
    except (RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"STOP: {error}", file=sys.stderr)
        raise SystemExit(1)

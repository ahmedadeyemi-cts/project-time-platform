#!/usr/bin/env python3
"""Read-only reconciliation of the prior installed-Test planner operation.

This entrypoint authenticates as the existing PM, reads the exact prior run,
the latest-run projection, and the current FlowHive working-copy identity. It
never starts, cancels, edits, publishes, adopts, or retries a planner run.
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, build_opener

ORIGIN = "https://phd-west-test.onenecklab.com"
PROJECT = "0ea25cb8-1a7f-4baf-ba7b-2dd76215be49"
LOGIN = "heather.schrock@ussignal.local"
PREVIOUS_RUN = "171e4430-95e4-4f80-be14-454dcc319ef2"


class ReconciliationError(Exception):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ReconciliationError(code)


def safe_id(value: object) -> bool:
    return isinstance(value, str) and re.fullmatch(
        r"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}", value
    ) is not None


def request(path: str, method: str = "GET", payload: dict | None = None, token: str = "") -> tuple[int, object]:
    require(path.startswith("/") and "#" not in path and not path.startswith("//"), "invalid_request_path")
    data = json.dumps(payload).encode() if payload is not None else None
    headers = {
        "Accept": "application/json",
        "Cache-Control": "no-cache, no-store, max-age=0",
        "Origin": ORIGIN,
        "Sec-Fetch-Site": "same-origin",
    }
    if payload is not None:
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"
        headers["X-ProjectPulse-Session"] = token
    try:
        with build_opener().open(Request(ORIGIN + path, data=data, headers=headers, method=method), timeout=30) as response:
            raw = response.read(2_000_001)
            require(len(raw) <= 2_000_000, "response_too_large")
            return response.status, json.loads(raw) if raw else None
    except HTTPError as error:
        return error.code, None
    except (URLError, TimeoutError, OSError, ValueError):
        raise ReconciliationError("planner_read_failed") from None


def logout(token: str) -> None:
    if not token:
        return
    try:
        request("/api/auth/session/logout", "POST", {}, token)
    except ReconciliationError:
        pass


def main() -> int:
    evidence_dir = Path(os.environ.get("EVIDENCE_DIR", "/tmp/flowhive-psa-evidence"))
    evidence_dir.mkdir(parents=True, exist_ok=True)
    report = {
        "status": "blocked",
        "environment": "test",
        "projectId": PROJECT,
        "priorRunId": PREVIOUS_RUN,
        "verificationCodeSha": os.environ.get("GITHUB_SHA", ""),
        "productionMutation": False,
        "businessWritesRequested": False,
        "generationPosts": 0,
        "cancellationPosts": 0,
    }
    token = ""
    try:
        password = os.environ.get("TEST_LOGIN_PASSWORD", "")
        require(len(password) >= 12, "pm_login_secret_missing")
        status, session = request(
            "/api/auth/local/login",
            "POST",
            {"username": LOGIN, "password": password},
        )
        password = ""
        require(status == 200 and isinstance(session, dict), "pm_login_failed")
        require(session.get("provider") == "LOCAL" and session.get("mustChangePassword") is False, "pm_login_contract_failed")
        token = session.get("sessionToken") or ""
        require(isinstance(token, str) and len(token) >= 20, "pm_session_missing")

        base = f"/api/project-flowhive/projects/{PROJECT}"
        status, workspace = request(base + "/enterprise", token=token)
        require(status == 200 and isinstance(workspace, dict), "flowhive_workspace_read_failed")
        project = workspace.get("project") or {}
        access = workspace.get("access") or {}
        require(project.get("projectId") == PROJECT, "flowhive_project_identity_mismatch")
        require(access.get("isViewAs") is False and access.get("actualUserId") == access.get("effectiveUserId"), "flowhive_actor_mismatch")
        require(access.get("isProjectManagerOwner") is True and access.get("canEditPlanner") is True, "assigned_pm_authority_missing")
        working = workspace.get("workingCopy") or {}
        plan = working.get("plan") or {}
        require(plan.get("projectId") == PROJECT and safe_id(working.get("rowVersion")), "working_copy_identity_missing")
        report["assignedPmVerified"] = True
        report["workingCopy"] = {
            "rowVersion": working.get("rowVersion"),
            "workingRevision": working.get("workingRevision"),
            "taskCount": len(plan.get("tasks") or []),
            "milestoneCount": len(plan.get("milestones") or []),
            "sowEvidencePresent": bool(workspace.get("sowEvidence")),
        }

        status, prior = request(base + f"/ai-planner/runs/{PREVIOUS_RUN}", token=token)
        require(status in (200, 202) and isinstance(prior, dict), "prior_planner_status_read_failed")
        require(prior.get("runId") == PREVIOUS_RUN, "prior_planner_identity_mismatch")
        terminal = prior.get("terminal") is True
        require((status == 200 and terminal) or (status == 202 and not terminal), "prior_planner_status_pair_invalid")
        report["priorPlanner"] = {
            "httpStatus": status,
            "runId": PREVIOUS_RUN,
            "terminal": terminal,
            "status": re.sub(r"[^a-z0-9_]", "", str(prior.get("status") or "").lower())[:80],
            "phase": re.sub(r"[^a-z0-9_]", "", str(prior.get("phase") or "").lower())[:80],
            "candidateAvailable": prior.get("candidateAvailable") is True,
            "workingDraftPersisted": (prior.get("workingDraft") or {}).get("persisted") is True,
        }
        if not terminal:
            raise ReconciliationError("prior_planner_run_nonterminal")
        status, latest = request(base + "/ai-planner/runs/latest", token=token)
        require(status in (200, 409), "latest_planner_read_failed")
        if status == 200:
            require(isinstance(latest, dict), "latest_planner_payload_invalid")
            latest_run = latest.get("runId")
            require(not latest_run or latest.get("terminal") is True, "another_planner_operation_active")
            report["latestPlanner"] = {
                "httpStatus": status,
                "runIdPresent": bool(latest_run),
                "terminal": latest.get("terminal") is True,
            }
        else:
            report["latestPlanner"] = {"httpStatus": status, "projection": "stale_or_unavailable"}
        report["status"] = "passed"
        report["plannerReconciled"] = True
    except ReconciliationError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:  # pragma: no cover - type-only evidence
        report["diagnosticCode"] = "unexpected_" + type(error).__name__
    finally:
        password = ""
        logout(token)
        (evidence_dir / "flowhive-planner-reconciliation.json").write_text(json.dumps(report, indent=2) + "\n")
    print("FLOWHIVE_PLANNER_RECONCILIATION=" + ("PASS" if report["status"] == "passed" else "BLOCKED"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Check existing PM inputs before deployment; optionally perform read-only UAT.

No new account or secret is introduced. The read-only mode reuses FlowHive's
actual project/SOW checks and never submits, cancels, applies, or publishes a
planner operation. Authentication creates and closes only its own session.
"""
from __future__ import annotations
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import sys

ORIGIN = "https://phd-west-test.onenecklab.com"


class InputError(Exception):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise InputError(code)


def validate_inputs(approval: dict, environment: dict) -> None:
    require(environment.get("BASE") == ORIGIN, "unapproved_public_origin")
    target = environment.get("TARGET_RELEASE_COMMIT", "")
    require(re.fullmatch(r"[0-9a-f]{40}", target) is not None and approval.get("sha") == target, "unapproved_release")
    email = environment.get("PROJECTPULSE_M025_PM_EMAIL", "").strip()
    password = environment.get("PROJECTPULSE_M025_PM_PASSWORD", "")
    require(bool(email), "pm_login_email_missing")
    require(len(password) >= 12, "pm_login_secret_missing")
    require(approval.get("projectManagerLogin") == email, "approved_pm_identity_mismatch")
    require(re.fullmatch(r"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}", str(approval.get("projectId") or "")) is not None, "approved_project_missing")
    require(re.fullmatch(r"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}", environment.get("PREVIOUS_PLANNER_RUN_ID", "")) is not None, "prior_planner_run_id_missing")
    require(approval.get("environment") == "test" and approval.get("publicOrigin") == ORIGIN and approval.get("allowCustomerPublication") is False and approval.get("allowCanonicalTaskAdoption") is False, "acceptance_scope_invalid")


def load_flowhive():
    path = Path(__file__).with_name("run-flowhive-psa-live-uat.py")
    spec = importlib.util.spec_from_file_location("flowhive_live_preflight", path)
    require(spec is not None and spec.loader is not None, "flowhive_verifier_missing")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_only_preflight(approval: dict, report: dict) -> None:
    module = load_flowhive()
    require(approval["projectId"] == module.PROJECT, "approved_project_contract_mismatch")
    client = module.Client()
    try:
        status, session = client.request("/api/auth/local/login", "POST", {
            "username": os.environ["PROJECTPULSE_M025_PM_EMAIL"].strip(),
            "password": os.environ["PROJECTPULSE_M025_PM_PASSWORD"],
        }, authenticated=False)
        require(status == 200 and isinstance(session, dict) and session.get("provider") == "LOCAL" and session.get("mustChangePassword") is False, "pm_login_failed")
        client.token = session.get("sessionToken") or ""
        require(isinstance(client.token, str) and len(client.token) >= 20, "pm_session_missing")
        workspace, _, _ = module.authorized_project_and_sow(client, approval["projectId"], report)
        base = "/api/project-flowhive/projects/" + approval["projectId"]
        prior_id = os.environ["PREVIOUS_PLANNER_RUN_ID"]
        status, prior = client.request(base + "/ai-planner/runs/" + prior_id)
        module.planner_run_snapshot(status, prior, prior_id, report)
        status, latest = client.request(base + "/ai-planner/runs/latest")
        require(status in (200, 409), "latest_run_read_failed")
        if status == 200:
            require(isinstance(latest, dict) and (not latest.get("runId") or latest.get("terminal") is True), "another_planner_operation_active")
        working = workspace.get("workingCopy") or {}
        require(module.uid(working.get("rowVersion")), "starting_revision_missing")
        require(isinstance(working.get("plan"), dict) and working["plan"].get("projectId") == approval["projectId"], "stored_plan_wrong_project")
        report["workingRowVersion"] = working["rowVersion"]
        report["authenticatedPmAndReadySow"] = True
    except module.GateError as error:
        code = str(error)
        raise InputError(code if re.fullmatch(r"[a-z0-9_]{1,120}", code) else "flowhive_read_only_preflight_failed") from None
    finally:
        report["generationPosts"] = client.start_posts
        if client.token:
            try:
                status, _ = client.request("/api/auth/session/logout", "POST", {})
                report["sessionCleanup"] = "passed" if status == 200 else "failed"
            except Exception:
                report["sessionCleanup"] = "failed"
            client.token = ""
    require(report.get("sessionCleanup") == "passed", "pm_session_cleanup_failed")
    require(report["generationPosts"] == 0, "preflight_unexpected_generation")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--read-only", action="store_true")
    args = parser.parse_args(argv)
    report = {"status": "failed", "environment": "test", "generationPosts": 0,
              "businessWritesRequested": False, "productionMutation": False,
              "verificationCodeSha": os.environ.get("GITHUB_SHA", "")}
    try:
        approval_path = Path(os.environ.get("PSA_VERIFICATION_APPROVAL_FILE") or ".github/flowhive-psa-protected-test-candidate.json")
        approval = json.loads(approval_path.read_text())
        require(isinstance(approval, dict), "approval_invalid")
        validate_inputs(approval, os.environ)
        if args.read_only:
            read_only_preflight(approval, report)
        report["status"] = "passed"
        report["inputContractVerified"] = True
    except InputError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:
        report["diagnosticCode"] = "preflight_" + type(error).__name__
    evidence = Path(os.environ.get("EVIDENCE_DIR", "/tmp/flowhive-psa-evidence"))
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence / "flowhive-acceptance-preflight.json").write_text(json.dumps(report, indent=2) + "\n")
    print("FLOWHIVE_ACCEPTANCE_PREFLIGHT=" + ("PASS" if report["status"] == "passed" else "FAIL"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())

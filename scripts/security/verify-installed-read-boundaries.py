#!/usr/bin/env python3
"""Bounded, read-only role acceptance on the existing protected Test release.

Only local login/logout change session state. No business writes, fixture
enablement, Azure permissions, redirects, or response-content publication.
"""
import json
import os
import re
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener

ORIGIN = "https://phd-west-test.onenecklab.com"
ACCOUNTS = (
    ("engineer", "demo.engineer@ussignal.local", {"ENGINEER", "ENGINEERING"}),
    ("coordinator", "project.team.coordinator@ussignal.local", {"PROJECT_TEAM_COORDINATOR"}),
    ("project_manager", "heather.schrock@ussignal.local", {"PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGER", "PROJECT_MANAGER_I", "PROJECT_MANAGER_II", "PROJECT_MANAGER_III", "PROJECT_MANAGER_LEAD", "SENIOR_PROJECT_MANAGER"}),
    ("accounting", "juli.cambron@ussignal.local", {"ACCOUNTING"}),
)
ADMIN_READS = (
    "/api/db-config-check", "/api/db-health", "/api/schema/tables",
    "/api/role-policy/summary", "/api/role-policy/matrix",
    "/api/runtime/role-policy/summary", "/api/runtime/v2/role-policy/matrix",
    "/api/auth/local-accounts",
    "/api/production-data-readiness", "/api/production/data-readiness",
)
FINANCE_READS = ("/api/expenses/summary", "/api/invoicing/summary")
RETIRED_READS = ("/api/reports/030/preview", "/api/reports/030/filter-options", "/api/project-closeout/email/audit")


class CheckError(Exception):
    pass


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def require(value, code):
    if not value:
        raise CheckError(code)


def variants(path):
    # Keep the proxy's lowercase /api/ prefix; test backend route canonicalization.
    return (path, path + "/", "/api/" + path[5:].upper() + "/")


def request(path, token="", payload=None, parse=False):
    require(path.startswith("/api/") and "?" not in path and "#" not in path, "invalid_path")
    require(payload is None or path in {"/api/auth/local/login", "/api/auth/session/logout"}, "write_forbidden")
    headers = {"Accept": "application/json", "Cache-Control": "no-store", "Origin": ORIGIN, "Sec-Fetch-Site": "same-origin"}
    if token:
        headers.update({"Authorization": "Bearer " + token, "X-ProjectPulse-Session": token})
    data = None if payload is None else json.dumps(payload).encode()
    if data is not None:
        headers["Content-Type"] = "application/json"
    req = Request(ORIGIN + path, data=data, headers=headers, method="GET" if payload is None else "POST")
    try:
        with build_opener(NoRedirect).open(req, timeout=20) as response:
            if not parse:
                return response.status, None
            if parse == "container":
                require("application/json" in response.headers.get("Content-Type", ""), "response_not_json")
                prefix = response.read(1024).lstrip()
                require(prefix.startswith((b"{", b"[")), "allowed_response_not_json")
                return response.status, {}
            raw = response.read(131073)
            require(len(raw) <= 131072, "response_budget_exceeded")
            require("application/json" in response.headers.get("Content-Type", ""), "response_not_json")
            return response.status, json.loads(raw)
    except HTTPError as error:
        code = error.code
        error.close()
        return code, None
    except (URLError, TimeoutError, OSError, ValueError):
        raise CheckError("request_failed") from None


def validate_context(body, expected):
    require(isinstance(body, dict), "context_invalid")
    rows = body.get("roles")
    require(isinstance(rows, list) and all(isinstance(row, dict) for row in rows), "context_roles_missing")
    roles = {str(row.get("roleCode", "")).upper() for row in rows}
    require(bool(roles & expected), "account_role_mismatch")
    require(not roles & {"ADMINISTRATOR", "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR"}, "account_is_administrator")


def exercise_account(label, username, expected, password, report):
    token = ""
    try:
        status, body = request("/api/auth/local/login", payload={"username": username, "password": password}, parse=True)
        require(status == 200 and isinstance(body, dict), "login_failed")
        token = body.get("sessionToken", "")
        require(isinstance(token, str) and len(token) >= 20, "session_missing")
        require(body.get("provider") == "LOCAL" and body.get("mustChangePassword") is False, "login_contract_failed")
        status, context = request("/api/security/context", token, parse=True)
        require(status == 200, "session_context_failed")
        validate_context(context, expected)
        checks = [(path, 403) for path in ADMIN_READS]
        if label != "coordinator":
            checks += [(path, 200 if label == "accounting" else 403) for path in FINANCE_READS]
        checks += [(path, 410) for path in RETIRED_READS]
        for index, (path, expected_status) in enumerate(checks):
            for variant, candidate in enumerate(variants(path)):
                status, body = request(candidate, token, parse="container" if expected_status == 200 else False)
                if expected_status == 200:
                    require(isinstance(body, (dict, list)), "allowed_response_not_json")
                report["checks"].append({"actor": label, "case": index, "variant": variant, "expected": expected_status, "observed": status, "passed": status == expected_status})
        # A stale/expired session must never turn an authorization test into a pass.
        status, context = request("/api/security/context", token, parse=True)
        require(status == 200, "session_expired_during_checks")
        validate_context(context, expected)
    finally:
        if token:
            status, _ = request("/api/auth/session/logout", token, payload={})
            require(status in {200, 204}, "session_cleanup_failed")


def main():
    report = {"schema": 1, "status": "failed", "environment": "test", "productionMutation": False, "businessMutation": False, "fixtureMutation": False, "rawResponsesPublished": False, "checks": []}
    try:
        identity = json.loads((Path(os.environ["EVIDENCE_DIR"]) / "flowhive-installed-identity.json").read_text())
        require(identity.get("status") == "passed", "identity_gate_not_passed")
        installed = identity.get("installed", {})
        source = installed.get("applicationSha", "")
        require(re.fullmatch(r"[0-9a-f]{40}", source) is not None, "source_missing")
        report["sourceCommit"] = source
        report["deploymentRunId"] = installed.get("deploymentRunId")
        password = os.environ.pop("TEST_LOGIN_PASSWORD", "")
        require(len(password) >= 12, "test_login_secret_missing")
        for actor in ACCOUNTS:
            exercise_account(*actor, password, report)
        password = ""
        require(len(report["checks"]) == 174, "matrix_incomplete")
        require(all(row["passed"] for row in report["checks"]), "boundary_assertion_failed")
        report["status"] = "passed"
    except CheckError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:
        report["diagnosticCode"] = "unexpected_" + type(error).__name__
    destination = Path(os.environ["SAFE_EVIDENCE_DIR"])
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "installed-read-boundaries.json").write_text(json.dumps(report, indent=2) + "\n")
    print("INSTALLED_READ_BOUNDARIES=" + report["status"].upper())
    print("CHECKS=" + str(len(report["checks"])))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())

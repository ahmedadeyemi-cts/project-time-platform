#!/usr/bin/env python3
"""Pre-write identity gate for the installed Protected-Test candidate.

The verifier has no Azure credentials and cannot mutate infrastructure. The
API release marker is read from the live authenticated diagnostics surface;
the web/API image and revision values are immutable evidence from installation
run 34540010122 and are recorded here for correlation, never accepted from
workflow inputs.
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
EXPECTED = {
    "applicationSha": "95abbb0aa2445a33fda68e9de542f9446c3e2204",
    "installationRunId": "34540010122",
    "controllerSha": "df6f7fc6d52495f83b0fd169047f5a249493db7e",
    "apiRevision": "ca-phd-test-api-westus3--m1bd-34540010122-1",
    "webRevision": "ca-phd-test-web-westus3--relw-34540010122-1",
    "apiImage": "acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:e5f95466a01be8e9f38e36f6a2474391372b1a63f41115f1b7f63810e6017afc",
    "webImage": "acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:7b1dbab4a0a57c7993d3ec8d7c3cd51613bcb463f7971a1268db5afb935d6964",
}


class IdentityError(Exception):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise IdentityError(code)


def request_json(path: str, method: str = "GET", payload: dict | None = None, token: str = "") -> tuple[int, object]:
    headers = {
        "Accept": "application/json",
        "Cache-Control": "no-cache, no-store, max-age=0",
        "Authorization": f"Bearer {token}",
        "X-ProjectPulse-Session": token,
        "Origin": ORIGIN,
        "Sec-Fetch-Site": "same-origin",
    }
    data = json.dumps(payload).encode() if payload is not None else None
    if data is not None:
        headers["Content-Type"] = "application/json"
    try:
        with build_opener().open(Request(ORIGIN + path, data=data, headers=headers, method=method), timeout=45) as response:
            raw = response.read(4_000_001)
            require(len(raw) <= 4_000_000, "identity_response_too_large")
            return response.status, json.loads(raw)
    except HTTPError as error:
        return error.code, None
    except (URLError, TimeoutError, OSError, ValueError):
        raise IdentityError("identity_read_failed") from None


def open_supported_identity_session() -> tuple[str, str, bool]:
    """Prefer the sealed session; fall back only to a fresh supported local login.

    The fallback is deliberately credential-name based. Values remain in the
    job secret store and are never written to evidence or diagnostics.
    """
    username = os.environ.get("PROJECTPULSE_TEST_UAT_ADMIN_EMAIL", "")
    password = os.environ.get("PROJECTPULSE_TEST_UAT_ADMIN_PASSWORD", "")
    if username or password:
        require(username.endswith(".local") or username.endswith("@ussignal.local"), "test_uat_admin_email_missing")
        require(len(password) >= 12, "test_uat_admin_password_missing")
        status, body = request_json("/api/auth/local/login", "POST", {"username": username, "password": password})
        password = ""
        require(status == 200 and isinstance(body, dict), "test_uat_admin_login_http_" + str(status))
        require(body.get("provider") == "LOCAL" and body.get("mustChangePassword") is False, "test_uat_admin_login_contract_failed")
        token = body.get("sessionToken")
        require(isinstance(token, str) and len(token) >= 20, "test_uat_admin_session_missing")
        return token, "PROJECTPULSE_TEST_UAT_ADMIN_EMAIL", True

    sealed = os.environ.get("PROJECTPULSE_TEST_UAT_SESSION", "")
    require(len(sealed) >= 20, "test_uat_session_missing")
    return sealed, "PROJECTPULSE_TEST_UAT_SESSION", False


def logout(token: str) -> None:
    if token:
        request_json("/api/auth/session/logout", "POST", {}, token)


def main() -> int:
    evidence_dir = Path(os.environ.get("EVIDENCE_DIR", "/tmp/flowhive-psa-evidence"))
    evidence_dir.mkdir(parents=True, exist_ok=True)
    report = {
        "status": "failed",
        "environment": "test",
        "productionMutation": False,
        "businessWritesPermitted": False,
        "verificationCodeSha": os.environ.get("GITHUB_SHA", ""),
        "installed": EXPECTED,
    }
    token = ""
    cleanup = False
    try:
        token, credential_source, cleanup = open_supported_identity_session()
        context_status, context = request_json("/api/security/context", token=token)
        require(context_status == 200 and isinstance(context, dict), "identity_security_context_http_" + str(context_status))
        permissions = {str(value).upper() for value in (context.get("permissions") or [])}
        require(bool(permissions & {"SYSTEM_ADMINISTRATION", "MANAGE_ALL"}), "identity_admin_permission_missing")
        status, body = request_json("/api/platform-operations/overview", token=token)
        require(status == 200 and isinstance(body, dict), "platform_identity_http_" + str(status))
        observed = str(((body.get("runtime") or {}).get("releaseSha")) or "")
        require(re.fullmatch(r"[0-9a-f]{40}", observed) is not None, "platform_release_marker_missing")
        require(observed == EXPECTED["applicationSha"], "installed_api_source_mismatch")
        report["serverConfirmed"] = {"apiReleaseSha": observed, "observedAtUtc": body.get("generatedAt")}
        report["identityCredentialSource"] = credential_source
        report["identityPermissions"] = sorted(permissions & {"SYSTEM_ADMINISTRATION", "MANAGE_ALL"})
        report["webIdentity"] = {
            "status": "recorded_from_installation_evidence",
            "revision": EXPECTED["webRevision"],
            "image": EXPECTED["webImage"],
        }
        report["status"] = "passed"
        report["identityGate"] = "api_source_match_before_business_writes"
    except IdentityError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:  # pragma: no cover - safe type-only diagnostic
        report["diagnosticCode"] = "unexpected_" + type(error).__name__
    finally:
        if cleanup:
            logout(token)
    (evidence_dir / "flowhive-installed-identity.json").write_text(json.dumps(report, indent=2) + "\n")
    print("FLOWHIVE_INSTALLED_IDENTITY=" + ("PASS" if report["status"] == "passed" else "FAIL"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())

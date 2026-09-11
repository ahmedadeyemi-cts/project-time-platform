#!/usr/bin/env python3
"""Read the installed Module 025 authorization prerequisite without enabling it."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, build_opener

ORIGIN = "https://phd-west-test.onenecklab.com"
LOGIN = "demo.manager@ussignal.local"


class CheckError(Exception):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise CheckError(code)


def request(path: str, method: str = "GET", payload: dict | None = None, token: str = "") -> tuple[int, object]:
    headers = {"Accept": "application/json", "Cache-Control": "no-cache"}
    if token:
        headers.update({"Authorization": f"Bearer {token}", "X-ProjectPulse-Session": token, "Origin": ORIGIN})
    data = json.dumps(payload).encode() if payload is not None else None
    if data is not None:
        headers["Content-Type"] = "application/json"
    try:
        with build_opener().open(Request(ORIGIN + path, data=data, headers=headers, method=method), timeout=30) as response:
            raw = response.read(2_000_001)
            require(len(raw) <= 2_000_000, "response_too_large")
            return response.status, json.loads(raw) if raw else None
    except HTTPError as error:
        return error.code, None
    except (URLError, TimeoutError, OSError, ValueError):
        raise CheckError("module025_prerequisite_read_failed") from None


def main() -> int:
    evidence_dir = Path(os.environ.get("EVIDENCE_DIR", "/tmp/flowhive-psa-evidence"))
    evidence_dir.mkdir(parents=True, exist_ok=True)
    report = {
        "status": "blocked",
        "environment": "test",
        "productionMutation": False,
        "fixtureMutation": False,
        "sourceCommit": os.environ.get("TARGET_RELEASE_COMMIT", ""),
    }
    password = os.environ.get("TEST_LOGIN_PASSWORD", "")
    try:
        require(len(password) >= 12, "test_login_secret_missing")
        status, body = request("/api/auth/local/login", "POST", {"username": LOGIN, "password": password})
        require(status == 200 and isinstance(body, dict), "module025_manager_login_failed")
        token = body.get("sessionToken")
        require(isinstance(token, str) and len(token) >= 20, "module025_manager_session_missing")
        status, bootstrap = request("/api/module025/sow-gsd/bootstrap", token=token)
        require(status == 200 and isinstance(bootstrap, dict), "module025_bootstrap_http_" + str(status))
        access = bootstrap.get("access") or {}
        fixture = access.get("protectedTestUatRoleFixture") is True
        report["access"] = {
            "manager": access.get("isManager") is True,
            "solutionArchitect": access.get("isSolutionArchitect") is True,
            "protectedTestUatRoleFixture": fixture,
            "canCreate": access.get("canCreate") is True,
        }
        if fixture and access.get("canCreate") is True:
            report["status"] = "ready"
        else:
            report["diagnosticCode"] = "protected_module025_fixture_disabled_or_not_authorized"
        request("/api/auth/session/logout", "POST", {}, token)
    except CheckError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:  # pragma: no cover - safe type-only diagnostic
        report["diagnosticCode"] = "unexpected_" + type(error).__name__
    finally:
        password = ""
    (evidence_dir / "module025-installed-prerequisite.json").write_text(json.dumps(report, indent=2) + "\n")
    print("MODULE025_INSTALLED_PREREQUISITE=" + report["status"].upper())
    return 0 if report["status"] == "ready" else 2


if __name__ == "__main__":
    sys.exit(main())

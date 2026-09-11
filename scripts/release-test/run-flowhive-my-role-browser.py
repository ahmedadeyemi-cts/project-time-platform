#!/usr/bin/env python3
"""Authenticated, non-mutating browser acceptance for My Role in Pulse.

This script uses the installed Test origin and the normal local-login/session
path. It never submits a form, creates intake data, changes a role, or mocks a
response. It is an installed-release verifier, not a deployment check.
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, build_opener

ORIGIN = "https://phd-west-test.onenecklab.com"
LOGIN = "heather.schrock@ussignal.local"


class VerificationError(Exception):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise VerificationError(code)


def request(path: str, method: str = "GET", payload: dict | None = None, token: str = "") -> tuple[int, object]:
    require(path.startswith("/") and "#" not in path, "invalid_request_path")
    data = json.dumps(payload).encode() if payload is not None else None
    headers = {"Accept": "application/json", "Cache-Control": "no-cache"}
    if payload is not None:
        headers["Content-Type"] = "application/json"
    if token:
        headers["X-ProjectPulse-Session"] = token
        headers["Authorization"] = f"Bearer {token}"
    try:
        with build_opener().open(Request(ORIGIN + path, data=data, headers=headers, method=method), timeout=30) as response:
            raw = response.read(2_000_001)
            require(len(raw) <= 2_000_000, "response_too_large")
            return response.status, json.loads(raw) if raw else None
    except HTTPError as error:
        return error.code, None
    except (URLError, TimeoutError, OSError, ValueError):
        raise VerificationError("network_or_json_failure") from None


def login(password: str) -> dict:
    status, body = request("/api/auth/local/login", "POST", {"username": LOGIN, "password": password})
    require(status == 200 and isinstance(body, dict), "pm_login_failed")
    require(body.get("provider") == "LOCAL" and body.get("mustChangePassword") is False, "pm_login_contract_failed")
    token = body.get("sessionToken")
    require(isinstance(token, str) and len(token) >= 20, "pm_session_missing")
    return body


async def browser_check(session: dict, report: dict) -> None:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1600, "height": 1000})
        writes: list[str] = []
        page_errors: list[str] = []

        async def guard(route):
            parsed = route.request
            if parsed.method not in ("GET", "HEAD", "OPTIONS"):
                writes.append(parsed.method + " " + parsed.url.split("?", 1)[0])
                await route.abort()
                return
            await route.continue_()

        await context.route("**/*", guard)
        await context.add_init_script(
            "window.localStorage.setItem('projectPulseAuthSession', "
            + json.dumps(json.dumps(session))
            + "); window.localStorage.removeItem('projectPulseViewAsUser');"
        )
        page = await context.new_page()
        page.on("pageerror", lambda _: page_errors.append("browser_page_error"))
        page.set_default_timeout(45_000)
        try:
            await page.goto(ORIGIN + "/#dashboard", wait_until="domcontentloaded")
            workspace = page.locator('section[aria-label="Role-based workspace"]')
            await workspace.wait_for(state="visible")
            cards = workspace.locator(".role-feature-card")
            card_count = await cards.count()
            require(card_count > 0, "role_workspace_has_no_authorized_steps")
            hrefs = await cards.evaluate_all("nodes => nodes.map(node => node.getAttribute('href') || '')")
            require(all(re.fullmatch(r"#[a-z0-9-]+", value) for value in hrefs), "role_step_route_invalid")

            await page.goto(ORIGIN + "/#project-intake", wait_until="domcontentloaded")
            intake = page.locator('[data-module="020"]')
            await intake.wait_for(state="visible")
            await page.get_by_role("button", name="Work-task handoff", exact=True).click()
            await page.locator('[aria-label="Work-task handoff"]').wait_for(state="visible")
            await page.get_by_role("button", name="Resource handoff", exact=True).click()
            await page.locator('[aria-label="Resource handoff"]').wait_for(state="visible")

            await page.goto(ORIGIN + "/#signed-handoff", wait_until="domcontentloaded")
            signed = page.locator('.sales-delivery-workflow-center[data-module="027"]')
            await signed.wait_for(state="visible")
            await signed.get_by_text("Submit the signed customer package", exact=True).wait_for(state="visible")
            await page.reload(wait_until="domcontentloaded")
            await signed.wait_for(state="visible")
            require(not writes, "browser_attempted_mutation")
            require(not page_errors, "browser_runtime_error")
            report.update({
                "status": "passed",
                "roleWorkspace": {"authorizedStepCount": card_count, "reloadVerified": True},
                "handoffs": {"workTask": True, "resource": True, "signedPackage": True},
                "writesBlocked": len(writes),
                "pageErrors": len(page_errors),
            })
        finally:
            await context.close()
            await browser.close()


def main() -> int:
    report = {
        "status": "failed",
        "environment": "test",
        "sourceCommit": os.environ.get("TARGET_RELEASE_COMMIT", ""),
        "productionMutation": False,
        "businessWritesRequested": False,
        "mockedResponses": False,
    }
    evidence_dir = Path(os.environ.get("EVIDENCE_DIR", "/tmp/flowhive-psa-evidence"))
    evidence_dir.mkdir(parents=True, exist_ok=True)
    try:
        password = os.environ.get("TEST_LOGIN_PASSWORD", "")
        require(len(password) >= 12, "test_login_secret_missing")
        session = login(password)
        password = ""
        anonymous_status, _ = request("/api/project-intake/resource-assignment-handoff")
        require(anonymous_status in (401, 403), "anonymous_handoff_access_not_denied")
        report["anonymousHandoffDenied"] = True
        asyncio.run(browser_check(session, report))
        request("/api/auth/session/logout", "POST", {}, session.get("sessionToken", ""))
    except VerificationError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:  # pragma: no cover - safe type-only diagnostic
        report["diagnosticCode"] = "unexpected_" + type(error).__name__
    finally:
        (evidence_dir / "flowhive-my-role-browser.json").write_text(json.dumps(report, indent=2) + "\n")
    print("FLOWHIVE_MY_ROLE_BROWSER=" + ("PASS" if report["status"] == "passed" else "FAIL"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())

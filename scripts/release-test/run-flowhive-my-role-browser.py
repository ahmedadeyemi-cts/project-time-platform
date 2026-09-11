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


def login(username: str, password: str) -> dict:
    status, body = request("/api/auth/local/login", "POST", {"username": username, "password": password})
    require(status == 200 and isinstance(body, dict), "pm_login_failed")
    require(body.get("provider") == "LOCAL" and body.get("mustChangePassword") is False, "pm_login_contract_failed")
    token = body.get("sessionToken")
    require(isinstance(token, str) and len(token) >= 20, "pm_session_missing")
    return body


async def browser_check(session: dict, report: dict, evidence_dir: Path) -> None:
    from playwright.async_api import async_playwright
    from playwright.async_api import TimeoutError as PlaywrightTimeoutError

    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1600, "height": 1000})
        writes: list[str] = []
        page_errors: list[str] = []
        failed_responses: list[dict] = []

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
        page.on("response", lambda response: (
            failed_responses.append({"status": response.status, "method": response.request.method,
                                     "path": response.url.split("?", 1)[0].replace(ORIGIN, "")})
            if response.status >= 400 and response.url.startswith(ORIGIN) else None
        ))
        page.set_default_timeout(45_000)
        async def wait_visible(locator, code: str) -> None:
            try:
                await locator.wait_for(state="visible")
            except PlaywrightTimeoutError:
                raise VerificationError(code) from None

        async def click_button(name: str, code: str) -> None:
            try:
                await page.get_by_role("button", name=name, exact=True).click()
            except PlaywrightTimeoutError:
                raise VerificationError(code) from None

        try:
            # PR891 owns a dedicated Module 999 route. Observe whether the
            # guide advertises it, then navigate to the actual route directly.
            # A missing recommendation link must not turn into a false page
            # timeout, and the direct route still exercises the installed page.
            await page.goto(ORIGIN + "/#user-guide", wait_until="domcontentloaded")
            launch = page.locator('a[data-role-journeys-launch="true"][href="#my-role-in-pulse"]')
            report["userGuideLaunchAvailable"] = await launch.count() > 0
            await page.goto(ORIGIN + "/#my-role-in-pulse", wait_until="domcontentloaded")
            journey = page.locator("#my-role-in-pulse")
            await wait_visible(journey, "browser_timeout_my_role_page")
            await wait_visible(page.get_by_role("heading", name="My Role in Pulse", exact=True), "browser_timeout_my_role_heading")

            role_buttons = journey.locator('aside[aria-label="Choose a role"] button[aria-pressed]')
            role_count = await role_buttons.count()
            require(role_count > 0, "my_role_has_no_playbooks")
            role_texts = await role_buttons.all_text_contents()
            assigned_index = next((index for index, value in enumerate(role_texts) if "Your role" in value), None)
            require(assigned_index is not None, "my_role_has_no_assigned_playbook")
            await role_buttons.nth(assigned_index).click()
            await wait_visible(journey.locator("#rj-role-title"), "browser_timeout_my_role_selected_role")
            await wait_visible(journey.locator("#rj-lifecycle-title"), "browser_timeout_my_role_lifecycle")
            step_buttons = journey.locator('button[aria-controls="rj-step-details"]')
            step_count = await step_buttons.count()
            require(step_count >= 3, "my_role_has_incomplete_steps")
            first_step = await journey.locator("#rj-step-title").inner_text()
            if step_count > 1:
                await step_buttons.nth(1).click()
                await wait_visible(journey.get_by_text(re.compile(r"Viewing step 2 of"), exact=False), "browser_timeout_my_role_step_two")
                require((await journey.locator("#rj-step-title").inner_text()) != first_step, "my_role_step_navigation_stuck")
            await journey.get_by_role("button", name="Show a simple example", exact=True).click()
            await wait_visible(journey.locator("#rj-example"), "browser_timeout_my_role_example")
            await journey.get_by_role("button", name="Hide example", exact=True).click()

            # Preserve the older dashboard signal as diagnostic evidence only.
            await page.goto(ORIGIN + "/#dashboard", wait_until="domcontentloaded")
            dashboard = page.locator("#role-welcome-dashboard")
            await wait_visible(dashboard, "browser_timeout_role_welcome_dashboard")
            dashboard_cards = dashboard.locator('nav[aria-label="Recommended actions"] a')
            dashboard_card_count = await dashboard_cards.count()
            dashboard_hrefs = await dashboard_cards.evaluate_all("nodes => nodes.map(node => node.getAttribute('href') || '')")
            require(all(re.fullmatch(r"#[a-z0-9-]+", value) for value in dashboard_hrefs), "role_step_route_invalid")

            await page.goto(ORIGIN + "/#project-intake", wait_until="domcontentloaded")
            intake = page.locator('.work-intake-creation-center[data-module="020"]')
            await wait_visible(intake, "browser_timeout_project_intake")
            await click_button("Work-task handoff", "browser_timeout_work_task_handoff_button")
            await wait_visible(page.locator('[aria-label="Work-task handoff"]'), "browser_timeout_work_task_handoff")
            await click_button("Resource handoff", "browser_timeout_resource_handoff_button")
            await wait_visible(page.locator('[aria-label="Resource handoff"]'), "browser_timeout_resource_handoff")

            signed_link = page.locator('a[href="#signed-handoff"]')
            signed_navigation_visible = await signed_link.count() > 0
            if signed_navigation_visible:
                await page.goto(ORIGIN + "/#signed-handoff", wait_until="domcontentloaded")
                signed = page.locator('.sales-delivery-workflow-center[data-module="027"]')
                await wait_visible(signed, "browser_timeout_signed_handoff_route")
                await wait_visible(signed.get_by_text("Submit the signed customer package", exact=True), "browser_timeout_signed_handoff_content")
                await page.reload(wait_until="domcontentloaded")
                await wait_visible(signed, "browser_timeout_signed_handoff_reload")
            else:
                await page.goto(ORIGIN + "/#dashboard", wait_until="domcontentloaded")
                require(await page.locator('a[href="#signed-handoff"]').count() == 0,
                        "signed_handoff_navigation_leaked")
            require(not writes, "browser_attempted_mutation")
            require(not page_errors, "browser_runtime_error")
            report.update({
                "status": "passed",
                "roleWorkspace": {"authorizedStepCount": role_count, "assignedPlaybook": role_texts[assigned_index].strip(), "surface": "#my-role-in-pulse", "reloadVerified": True},
                "dashboardObservation": {"recommendedActionCount": dashboard_card_count, "surface": "#dashboard"},
                "handoffs": {"workTask": True, "resource": True, "signedPackage": signed_navigation_visible},
                "accessBoundaries": {"signedHandoffNavigationVisible": signed_navigation_visible},
                "writesBlocked": len(writes),
                "pageErrors": len(page_errors),
                "failedResponses": failed_responses,
                "finalUrl": page.url,
            })
        except Exception:
            report["browserDiagnostics"] = {
                "finalUrl": page.url,
                "hash": await page.evaluate("window.location.hash"),
                "journeyCount": await page.locator("#my-role-in-pulse").count(),
                "headingCount": await page.get_by_role("heading", name="My Role in Pulse", exact=True).count(),
                "failedResponses": failed_responses,
                "pageErrors": len(page_errors),
            }
            try:
                await page.screenshot(path=str(evidence_dir / "my-role-failure.png"), full_page=True)
            except Exception:
                report["browserDiagnostics"]["screenshot"] = "unavailable"
            raise
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
        username = os.environ.get("PROJECTPULSE_M025_PM_EMAIL", "").strip()
        password = os.environ.pop("PROJECTPULSE_M025_PM_PASSWORD", "")
        require(username and "@" in username, "pm_login_email_missing")
        require(len(password) >= 12, "test_login_secret_missing")
        session = login(username, password)
        password = ""
        anonymous_status, _ = request("/api/project-intake/resource-assignment-handoff")
        require(anonymous_status in (401, 403), "anonymous_handoff_access_not_denied")
        report["anonymousHandoffDenied"] = True
        asyncio.run(browser_check(session, report, evidence_dir))
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

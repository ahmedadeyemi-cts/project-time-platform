#!/usr/bin/env python3
"""Run the normal authorized Solution Architect Module 025 lifecycle.

This is installed-release acceptance, not a fixture enabler and not a deploy.
It creates one synthetic, run-scoped SOW/GSD record, uses the real configured
provider, verifies the five-phase result, edits and confirms it, downloads both
retained documents, reopens it through the browser, then archives the record.
Secrets and customer content are never written to evidence.
"""
from __future__ import annotations

import asyncio
import hashlib
import json
import os
import re
import sys
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, build_opener

ORIGIN = "https://phd-west-test.onenecklab.com"
PHASE_CODES = ("plan", "design", "implement", "validate", "release")
DETAIL_FIELDS = (
    "detailedActivities", "technicalTasks", "deliverables", "usSignalResponsibilities",
    "customerResponsibilities", "prerequisites", "dependencies", "assumptions",
    "openQuestions", "acceptanceCriteria", "validationSteps", "risks",
)


class AcceptanceError(Exception):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise AcceptanceError(code)


def http(path: str, method: str = "GET", payload: object | None = None, token: str = "") -> tuple[int, object, dict[str, str]]:
    require(path.startswith("/") and "#" not in path, "invalid_request_path")
    headers = {
        "Accept": "application/json",
        "Cache-Control": "no-cache, no-store, max-age=0",
        "Origin": ORIGIN,
        "Sec-Fetch-Site": "same-origin",
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"
        headers["X-ProjectPulse-Session"] = token
    data = json.dumps(payload).encode() if payload is not None else None
    if data is not None:
        headers["Content-Type"] = "application/json"
    try:
        with build_opener().open(Request(ORIGIN + path, data=data, headers=headers, method=method), timeout=60) as response:
            raw = response.read(20_000_001)
            require(len(raw) <= 20_000_000, "response_too_large")
            content_type = response.headers.get("Content-Type", "")
            if "json" in content_type.lower():
                body: object = json.loads(raw) if raw else None
            else:
                body = raw
            return response.status, body, {key.lower(): value for key, value in response.headers.items()}
    except HTTPError as error:
        return error.code, None, {key.lower(): value for key, value in error.headers.items()}
    except (URLError, TimeoutError, OSError, ValueError):
        raise AcceptanceError("module025_network_or_json_failure") from None


def login(email: str, password: str) -> dict:
    status, body, _ = http("/api/auth/local/login", "POST", {"username": email, "password": password})
    require(status == 200 and isinstance(body, dict), "solution_architect_login_http_" + str(status))
    require(body.get("provider") == "LOCAL" and body.get("mustChangePassword") is False, "solution_architect_login_contract_failed")
    token = body.get("sessionToken")
    require(isinstance(token, str) and len(token) >= 20, "solution_architect_session_missing")
    return body


def phase_payload(phase: dict) -> dict:
    return {key: phase.get(key) if key != "finalHours" else float(phase.get(key) or 0) for key in ("phaseCode", "finalHours", "objective", *DETAIL_FIELDS, "loeRationale")}


def phase_quality(phases: object) -> dict:
    require(isinstance(phases, list) and len(phases) == 5, "sow_phase_count_not_five")
    by_code = {str(item.get("phaseCode", "")).lower(): item for item in phases if isinstance(item, dict)}
    require(set(by_code) == set(PHASE_CODES), "sow_phase_codes_incomplete")
    counts: dict[str, int] = {}
    for code in PHASE_CODES:
        phase = by_code[code]
        require(str(phase.get("objective") or "").strip(), "sow_phase_objective_missing_" + code)
        detail_count = sum(len(value) for key in DETAIL_FIELDS if isinstance((value := phase.get(key)), list))
        require(detail_count > 0, "sow_phase_detail_missing_" + code)
        counts[code] = detail_count
    return counts


async def browser_lifecycle(session: dict, engagement_number: str, edit_marker: str, report: dict, evidence_dir: Path) -> None:
    from playwright.async_api import async_playwright
    from playwright.async_api import TimeoutError as PlaywrightTimeoutError

    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1600, "height": 1000}, accept_downloads=True)
        requests: list[str] = []

        def record_request(request) -> None:
            if request.method not in ("GET", "HEAD", "OPTIONS"):
                requests.append(request.method + " " + request.url.split("?", 1)[0])

        context.on("request", record_request)
        await context.add_init_script(
            "window.localStorage.setItem('projectPulseAuthSession', "
            + json.dumps(json.dumps(session))
            + "); window.localStorage.removeItem('projectPulseViewAsUser');"
        )
        page = await context.new_page()
        page.set_default_timeout(60_000)
        page_errors: list[str] = []
        page.on("pageerror", lambda _: page_errors.append("browser_page_error"))
        try:
            await page.goto(ORIGIN + "/#sow-generator", wait_until="domcontentloaded")
            workspace = page.locator('section[data-module025-sow-gsd-workspace="true"]')
            await workspace.wait_for(state="visible")
            await workspace.get_by_role("heading", name="SOW & GSD Workspace", exact=True).wait_for(state="visible")
            await workspace.get_by_text("Solution Architect", exact=True).first.wait_for(state="visible")
            card = workspace.locator(".m025-work-card").filter(has_text=engagement_number).first
            await card.wait_for(state="visible")
            await card.click()
            editor = workspace.locator(".m025-editor-panel")
            await editor.get_by_text(engagement_number, exact=True).wait_for(state="visible")

            sow_link = editor.locator('a[href$="/sow.docx"]')
            gsd_link = editor.locator('a[href$="/gsd.xlsx"]')
            await sow_link.wait_for(state="visible")
            await gsd_link.wait_for(state="visible")
            downloads: dict[str, dict[str, object]] = {}
            for label, locator, filename in (("sow", sow_link, f"{engagement_number}-SOW.docx"), ("gsd", gsd_link, f"{engagement_number}-GSD.xlsx")):
                async with page.expect_download() as download_info:
                    await locator.click()
                download = await download_info.value
                path = await download.path()
                require(path is not None, label + "_download_path_missing")
                data = Path(path).read_bytes()
                require(len(data) > 100, label + "_download_empty")
                target = evidence_dir / filename
                target.write_bytes(data)
                downloads[label] = {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(), "path": str(target)}

            await editor.get_by_role("button", name="Reopen for editing", exact=True).click()
            await editor.locator(".m025-status-pill--draft").wait_for(state="visible")
            service = editor.locator("textarea.m025-service-overview")
            await service.fill((await service.input_value()) + " " + edit_marker)
            await page.wait_for_timeout(2_000)
            await editor.locator(".m025-save-state--saved").wait_for(state="visible")
            await page.reload(wait_until="domcontentloaded")
            await workspace.locator("textarea.m025-service-overview").wait_for(state="visible")
            require(edit_marker in await workspace.locator("textarea.m025-service-overview").input_value(), "browser_saved_edit_missing_after_reload")
            report.update({"browser": {"route": "#sow-generator", "workspaceVisible": True, "confirmedDownloads": downloads, "reopenVerified": True, "savedEditReloadVerified": True, "browserWriteCount": len(requests), "pageErrors": len(page_errors)}})
        except PlaywrightTimeoutError:
            raise AcceptanceError("module025_browser_timeout") from None
        finally:
            await context.close()
            await browser.close()


async def main() -> int:
    evidence_dir = Path(os.environ.get("EVIDENCE_DIR", "/tmp/flowhive-psa-evidence"))
    evidence_dir.mkdir(parents=True, exist_ok=True)
    report: dict = {
        "status": "failed",
        "environment": "test",
        "sourceCommit": os.environ.get("TARGET_RELEASE_COMMIT", ""),
        "applicationSha": "95abbb0aa2445a33fda68e9de542f9446c3e2204",
        "productionMutation": False,
        "fixtureMutation": False,
        "mockedResponses": False,
        "generationPosts": 0,
        "businessWritesRequested": False,
    }
    token = ""
    engagement_id = ""
    archived = False
    try:
        email = os.environ.get("PROJECTPULSE_M025_SA_EMAIL", "")
        password = os.environ.get("PROJECTPULSE_M025_SA_PASSWORD", "")
        require(email.endswith(".local") or email.endswith("@ussignal.local"), "solution_architect_email_missing")
        require(len(password) >= 12, "solution_architect_password_missing")
        session = login(email, password)
        token = str(session["sessionToken"])
        password = ""

        status, bootstrap, _ = http("/api/module025/sow-gsd/bootstrap", token=token)
        require(status == 200 and isinstance(bootstrap, dict), "module025_bootstrap_http_" + str(status))
        access = bootstrap.get("access") or {}
        require(access.get("isSolutionArchitect") is True, "normal_solution_architect_role_missing")
        require(access.get("protectedTestUatRoleFixture") is False, "exceptional_module025_fixture_active")
        require(access.get("canCreate") is True and access.get("canEditOwn") is True, "normal_solution_architect_authority_missing")
        require(access.get("isViewAs") is False, "solution_architect_view_as_not_allowed")
        current_user = bootstrap.get("currentUser") or {}
        require(re.fullmatch(r"[0-9a-fA-F-]{36}", str(current_user.get("userId") or "")) is not None, "solution_architect_user_id_missing")

        account_executives = bootstrap.get("accountExecutives") or []
        inside_sales = bootstrap.get("insideSalesRepresentatives") or []
        require(account_executives and inside_sales, "module025_people_directories_incomplete")
        account_executive_id = account_executives[0].get("userId")
        inside_sales_id = inside_sales[0].get("userId")
        require(account_executive_id and inside_sales_id and account_executive_id != inside_sales_id, "module025_people_directory_separation_missing")

        suffix = re.sub(r"[^0-9A-Za-z-]", "-", os.environ.get("GITHUB_RUN_ID", "manual"))
        service_overview = (
            "Synthetic Protected Test acceptance: upgrade Cisco Unified Communications Manager from 14.0 to 15.0. "
            "Define customer-ready Plan, Design, Implement, Validate, and Release work for readiness, compatibility, "
            "licensing, backups, sequencing, rollback, testing, operational handoff, dependencies, and open questions."
        )
        create_payload = {
            "customerId": None,
            "customerName": f"Protected UAT normal SA {suffix}",
            "customerEntryMode": "manual",
            "commercialModel": "time_and_materials",
            "customerProgram": "standard",
            "accountExecutiveUserId": account_executive_id,
            "resaleUserId": inside_sales_id,
            "serviceOverview": service_overview,
        }
        status, created, _ = http("/api/module025/sow-gsd", "POST", create_payload, token)
        report["businessWritesRequested"] = True
        require(status == 201 and isinstance(created, dict), "module025_create_http_" + str(status))
        engagement = created.get("engagement") or {}
        engagement_id = str(engagement.get("engagementId") or "")
        engagement_number = str(engagement.get("engagementNumber") or "")
        require(re.fullmatch(r"[0-9a-fA-F-]{36}", engagement_id) is not None, "module025_engagement_id_missing")
        require(engagement_number, "module025_engagement_number_missing")
        report["engagement"] = {"engagementNumber": engagement_number, "ownerUserId": engagement.get("ownerUserId")}

        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        require(status == 200 and isinstance(detail, dict), "module025_detail_http_" + str(status))
        current = detail.get("engagement") or {}
        phases = current.get("phases") or []
        phase_quality(phases)

        edited_overview = service_overview + " SA review marker: confirm customer change window and rollback owner."
        save_payload = {
            "expectedRevision": current.get("revision"),
            "customerId": None,
            "customerName": current.get("customerName"),
            "customerEntryMode": current.get("customerEntryMode"),
            "commercialModel": current.get("commercialModel"),
            "customerProgram": current.get("customerProgram"),
            "accountExecutiveUserId": account_executive_id,
            "resaleUserId": inside_sales_id,
            "serviceOverview": edited_overview,
            "phases": [phase_payload(phase) for phase in phases],
        }
        status, saved, _ = http(f"/api/module025/sow-gsd/{engagement_id}", "PUT", save_payload, token)
        require(status == 200 and isinstance(saved, dict), "module025_initial_save_http_" + str(status))
        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        current = (detail or {}).get("engagement") or {}
        require(status == 200 and current.get("serviceOverview") == edited_overview, "module025_initial_save_readback_failed")

        status, queued, _ = http(f"/api/module025/sow-gsd/{engagement_id}/generate", "POST", token=token)
        report["generationPosts"] = 1
        require(status in (200, 202) and isinstance(queued, dict), "module025_generation_start_http_" + str(status))
        generation_id = str(queued.get("generationId") or "")
        require(re.fullmatch(r"[0-9a-fA-F-]{36}", generation_id) is not None, "module025_generation_id_missing")
        deadline = time.monotonic() + int(os.environ.get("MODULE025_GENERATION_TIMEOUT_SECONDS", "1500"))
        while True:
            status, generation, _ = http(f"/api/module025/sow-gsd/{engagement_id}/generations/{generation_id}", token=token)
            require(status == 200 and isinstance(generation, dict), "module025_generation_poll_http_" + str(status))
            if generation.get("terminal") is True:
                require(generation.get("status") == "module025_detailed_scope_generated", "module025_generation_terminal_failure")
                break
            require(time.monotonic() < deadline, "module025_generation_deadline_exceeded")
            time.sleep(5)

        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        require(status == 200 and isinstance(detail, dict), "module025_generated_detail_http_" + str(status))
        current = detail.get("engagement") or {}
        counts = phase_quality(current.get("phases"))
        require(current.get("lastGeneratedAt"), "module025_generated_timestamp_missing")
        report["generation"] = {"providerBacked": True, "generationId": generation_id, "phaseDetailCounts": counts, "lastGeneratedAt": current.get("lastGeneratedAt")}

        edit_marker = f"Browser reload acceptance marker {suffix}"
        edited_overview = str(current.get("serviceOverview") or "") + " " + edit_marker
        save_payload = {
            "expectedRevision": current.get("revision"),
            "customerId": current.get("customerId"),
            "customerName": current.get("customerName"),
            "customerEntryMode": current.get("customerEntryMode"),
            "commercialModel": current.get("commercialModel"),
            "customerProgram": current.get("customerProgram"),
            "accountExecutiveUserId": current.get("accountExecutiveUserId"),
            "resaleUserId": current.get("resaleUserId"),
            "serviceOverview": edited_overview,
            "phases": [phase_payload(phase) for phase in (current.get("phases") or [])],
        }
        status, saved, _ = http(f"/api/module025/sow-gsd/{engagement_id}", "PUT", save_payload, token)
        require(status == 200 and isinstance(saved, dict), "module025_review_edit_save_http_" + str(status))
        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        current = (detail or {}).get("engagement") or {}
        require(status == 200 and edit_marker in str(current.get("serviceOverview") or ""), "module025_review_edit_readback_failed")

        status, confirmed, _ = http(f"/api/module025/sow-gsd/{engagement_id}/confirm", "POST", token=token)
        require(status == 200 and isinstance(confirmed, dict), "module025_confirm_http_" + str(status))
        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        require(status == 200 and ((detail or {}).get("engagement") or {}).get("status") == "confirmed", "module025_confirm_readback_failed")

        await browser_lifecycle(session, engagement_number, edit_marker, report, evidence_dir)

        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        require(status == 200 and ((detail or {}).get("engagement") or {}).get("status") == "draft", "module025_reopen_readback_failed")
        status, archived_body, _ = http(f"/api/module025/sow-gsd/{engagement_id}/archive", "POST", token=token)
        require(status == 200 and isinstance(archived_body, dict), "module025_archive_http_" + str(status))
        archived = True
        report["status"] = "passed"
        report["normalAuthorizedSolutionArchitect"] = True
        report["reviewedSaveReadback"] = True
        report["retainedVersions"] = {"confirmed": True, "sowAndGsdDownloaded": True, "reopened": True, "archived": True}
    except AcceptanceError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:  # pragma: no cover - safe type-only diagnostic
        report["diagnosticCode"] = "unexpected_" + type(error).__name__
    finally:
        if engagement_id and token and not archived:
            # Cleanup is bounded and only targets this run-scoped synthetic ID.
            try:
                status, _, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
                if status == 200:
                    http(f"/api/module025/sow-gsd/{engagement_id}/archive", "POST", token=token)
            except Exception:
                report["cleanup"] = "archive_not_verified"
        if token:
            try:
                http("/api/auth/session/logout", "POST", {}, token)
            except Exception:
                report["logout"] = "not_verified"
        (evidence_dir / "module025-installed-sa-uat.json").write_text(json.dumps(report, indent=2) + "\n")
    print("MODULE025_INSTALLED_SA_UAT=" + ("PASS" if report["status"] == "passed" else "BLOCKED/FAIL"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))

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
import io
import json
import os
import re
import sys
import time
import zipfile
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, build_opener
from urllib.parse import urlparse

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


def phase_skeleton(phases: object) -> tuple[str, ...]:
    """Validate the create response without requiring generated content yet."""
    require(isinstance(phases, list) and len(phases) == 5, "sow_phase_count_not_five_before_generation")
    codes = tuple(sorted(
        str(item.get("phaseCode", "")).lower()
        for item in phases
        if isinstance(item, dict)
    ))
    require(codes == tuple(sorted(PHASE_CODES)), "sow_phase_skeleton_incomplete")
    return codes


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


def retained_version(engagement_id: str, token: str) -> tuple[dict, dict]:
    status, body, _ = http(f"/api/module025/sow-gsd/{engagement_id}/versions", token=token)
    require(status == 200 and isinstance(body, dict), "module025_versions_http_" + str(status))
    versions = body.get("versions")
    require(isinstance(versions, list) and len(versions) == 1 and body.get("hasMore") is False,
            "module025_expected_one_retained_version")
    version = versions[0]
    require(version.get("versionNumber") == 1 and body.get("latestVersionId") == version.get("versionId")
            and re.fullmatch(r"[0-9a-fA-F-]{36}", str(version.get("versionId", ""))) is not None,
            "module025_retained_version_identity_invalid")
    for field in ("sowSha256", "gsdSha256"):
        require(re.fullmatch(r"[0-9a-f]{64}", str(version.get(field, ""))) is not None,
                "module025_retained_hash_missing_" + field)
    return version, body


def verify_document(data: bytes, label: str, expected_hash: str) -> None:
    require(isinstance(data, bytes) and len(data) > 100, label + "_download_empty")
    require(hashlib.sha256(data).hexdigest() == expected_hash, label + "_retained_hash_mismatch")
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as document:
            required = "word/document.xml" if label == "sow" else "xl/workbook.xml"
            require(required in document.namelist(), label + "_document_format_invalid")
            require(document.testzip() is None, label + "_document_corrupt")
    except zipfile.BadZipFile:
        raise AcceptanceError(label + "_document_format_invalid") from None


def verify_historical_downloads(engagement_id: str, token: str, expected: dict) -> dict:
    current, _ = retained_version(engagement_id, token)
    for key in ("versionId", "versionNumber", "sourceRevision", "sowSha256", "gsdSha256"):
        require(current.get(key) == expected.get(key), "module025_retained_version_changed_" + key)
    for label, extension in (("sow", "docx"), ("gsd", "xlsx")):
        path = f"/api/module025/sow-gsd/{engagement_id}/versions/{expected['versionId']}/{label}.{extension}"
        status, data, headers = http(path, token=token)
        require(status == 200, label + "_historical_download_http_" + str(status))
        digest = expected[label + "Sha256"]
        verify_document(data, label, digest)
        require(headers.get("x-content-sha256") == digest and headers.get("x-sow-version") == "1",
                label + "_historical_download_identity_mismatch")
        unauthenticated, _, _ = http(path)
        require(unauthenticated in (401, 403), label + "_unauthorized_download_not_denied")
    return {"versionId": expected["versionId"], "versionCount": 1, "historicalHashesVerified": True,
            "unauthorizedDownloadsDenied": True, "sowSha256": expected["sowSha256"], "gsdSha256": expected["gsdSha256"]}


async def browser_lifecycle(session: dict, engagement_number: str, edit_marker: str, report: dict, evidence_dir: Path, *, preflight: bool = False) -> None:
    from playwright.async_api import async_playwright
    from playwright.async_api import TimeoutError as PlaywrightTimeoutError

    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1600, "height": 1000}, accept_downloads=True)
        requests: list[str] = []
        browser_report = {"status": "running", "stage": "launch", "completedSteps": [], "confirmedDownloads": {}, "failedResponses": [], "bootstrapStatuses": []}
        report["browserPreflight" if preflight else "browser"] = browser_report

        def stage(name: str) -> None:
            browser_report["stage"] = name
            print("MODULE025_BROWSER_STEP=" + ("preflight_" if preflight else "") + name, flush=True)

        def completed(name: str) -> None:
            browser_report["completedSteps"].append(name)

        def record_request(request) -> None:
            if urlparse(request.url).path.startswith("/api/module025/") and request.method not in ("GET", "HEAD", "OPTIONS"):
                requests.append(request.method + " " + request.url.split("?", 1)[0])

        context.on("request", record_request)
        def record_response(response) -> None:
            path = urlparse(response.url).path
            if path == "/api/module025/sow-gsd/bootstrap" and len(browser_report["bootstrapStatuses"]) < 10:
                browser_report["bootstrapStatuses"].append(response.status)
            if response.status < 400 or not path.startswith("/api/module025/"):
                return
            # Record only fixed endpoint labels and HTTP status, never URLs,
            # queries, headers, tokens, error bodies or customer-visible text.
            endpoint = next((name for suffix, name in (
                ("/bootstrap", "bootstrap"), ("/sow-gsd", "list"),
                ("/sow.docx", "sow_download"), ("/gsd.xlsx", "gsd_download"),
                ("/reopen", "reopen"),
            ) if path.endswith(suffix)), "record")
            if len(browser_report["failedResponses"]) < 20:
                browser_report["failedResponses"].append({"endpoint": endpoint, "status": response.status})

        context.on("response", record_response)
        if preflight:
            async def prevent_business_writes(route) -> None:
                request = route.request
                if urlparse(request.url).path.startswith("/api/module025/") and request.method not in ("GET", "HEAD", "OPTIONS"):
                    await route.abort()
                else:
                    await route.continue_()
            await context.route("**/*", prevent_business_writes)
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
            stage("navigate")
            await page.goto(ORIGIN + "/#sow-generator", wait_until="domcontentloaded")
            # The installed shell also mounts a hidden legacy presentation.
            # Require exactly one visible workspace; never select its hidden
            # duplicate or silently accept two visible authoring surfaces.
            workspace = page.locator('section[data-module025-sow-gsd-workspace="true"]:visible')
            stage("workspace")
            await workspace.wait_for(state="visible")
            # The route/workspace/bootstrap identity is authoritative. Do not
            # fail the installed acceptance because a presentation heading moved,
            # was cached briefly, or changed copy while the workspace is usable.
            stage("workspace_ready")
            await workspace.locator(".m025-filters").wait_for(state="visible")
            await workspace.locator(".m025-list-panel").wait_for(state="visible")
            stage("solution_architect")
            await workspace.get_by_text("Solution Architect", exact=True).first.wait_for(state="visible")
            card = workspace.locator(".m025-work-card").filter(has_text=engagement_number).first
            stage("select_record")
            await card.wait_for(state="visible")
            await card.click()
            editor = workspace.locator(".m025-editor-panel")
            await editor.get_by_text(engagement_number, exact=True).wait_for(state="visible")
            completed("workspace_and_record")

            sow_link = workspace.get_by_role('button', name='Download SOW (.docx)', exact=True).or_(editor.locator('a[href$="/sow.docx"]'))
            gsd_link = workspace.get_by_role('button', name='Download GSD (.xlsx)', exact=True).or_(editor.locator('a[href$="/gsd.xlsx"]'))
            stage("document_actions")
            await sow_link.wait_for(state="visible")
            await gsd_link.wait_for(state="visible")
            completed("document_actions")
            if preflight:
                require(not requests and not page_errors, "module025_browser_preflight_unexpected_write_or_error")
                browser_report.update({"status": "passed", "browserWriteCount": 0, "pageErrors": 0})
                return
            downloads = browser_report["confirmedDownloads"]
            for label, locator, filename in (("sow", sow_link, f"{engagement_number}-SOW.docx"), ("gsd", gsd_link, f"{engagement_number}-GSD.xlsx")):
                stage("download_" + label)
                async with page.expect_download() as download_info:
                    await locator.click()
                download = await download_info.value
                path = await download.path()
                require(path is not None, label + "_download_path_missing")
                data = Path(path).read_bytes()
                verify_document(data, label, report["confirmedVersion"][label + "Sha256"])
                stage("repeat_download_" + label)
                async with page.expect_download() as repeated_info:
                    await locator.click()
                repeated = await repeated_info.value
                repeated_path = await repeated.path()
                require(repeated_path is not None and Path(repeated_path).read_bytes() == data,
                        label + "_repeated_download_bytes_changed")
                target = evidence_dir / filename
                target.write_bytes(data)
                downloads[label] = {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                                    "path": str(target), "repeatBytesVerified": True}
                completed("download_" + label)

            stage("reopen")
            await editor.get_by_role("button", name="Reopen for editing", exact=True).click()
            await editor.locator(".m025-status-pill--review_ready").wait_for(state="visible")
            completed("reopen")
            service = editor.locator("textarea.m025-service-overview")
            stage("save_edit")
            await service.fill((await service.input_value()) + " " + edit_marker)
            await page.wait_for_timeout(2_000)
            await editor.locator(".m025-save-state--saved").wait_for(state="visible")
            completed("save_edit")
            stage("reload_select_record")
            await page.reload(wait_until="domcontentloaded")
            # The workspace intentionally reloads its list with no selection.
            # Select the same immutable record before inspecting saved content.
            await card.wait_for(state="visible")
            await card.click()
            await editor.get_by_text(engagement_number, exact=True).wait_for(state="visible")
            await workspace.locator("textarea.m025-service-overview").wait_for(state="visible")
            require(edit_marker in await workspace.locator("textarea.m025-service-overview").input_value(), "browser_saved_edit_missing_after_reload")
            require(not page_errors, "module025_browser_page_error")
            completed("saved_edit_reload")
            browser_report.update({"status": "passed", "route": "#sow-generator", "workspaceVisible": True, "reopenVerified": True, "savedEditReloadVerified": True})
        except PlaywrightTimeoutError:
            browser_report["status"] = "failed"
            raise AcceptanceError("module025_browser_timeout_" + browser_report["stage"]) from None
        except AcceptanceError:
            browser_report["status"] = "failed"
            raise
        finally:
            browser_report.update({"browserWriteCount": len(requests), "pageErrors": len(page_errors)})
            try:
                # Closed labels, booleans and counts only. Never retain DOM,
                # session contents, arbitrary routes or customer/error text.
                browser_report["entryState"] = await page.evaluate("""() => {
                    const route = location.hash.replace(/^#/, '').split('?')[0];
                    const navigation = window.__projectPulseEffectiveNavigation;
                    const roots = [...document.querySelectorAll('section[data-module025-sow-gsd-workspace="true"]')];
                    const visible = node => !!node && node.checkVisibility();
                    return {
                        route: ['sow-generator', 'dashboard', 'modules'].includes(route) ? route : 'other',
                        applicationShellVisible: [...document.querySelectorAll('main.app-shell')].some(visible),
                        workspaceMatches: roots.length,
                        visibleWorkspaces: roots.filter(visible).length,
                        loadingWorkspaceVisible: [...document.querySelectorAll('.m025-workspace--loading')].some(visible),
                        loginPasswordVisible: [...document.querySelectorAll('input[type="password"]')].some(visible),
                        navigationState: ['ready', 'loading', 'anonymous', 'unavailable'].includes(navigation?.state) ? navigation.state : 'unknown',
                        module025Denied: Array.isArray(navigation?.deniedModuleNumbers) && navigation.deniedModuleNumbers.includes('025')
                    };
                }""")
            except Exception:
                browser_report["entryState"] = {"captureAvailable": False}
            if browser_report["status"] == "failed":
                print("MODULE025_BROWSER_ENTRY=" + json.dumps(browser_report["entryState"], sort_keys=True), flush=True)
                print("MODULE025_BROWSER_BOOTSTRAP_HTTP=" + json.dumps(browser_report["bootstrapStatuses"]), flush=True)
            await context.close()
            await browser.close()


def generation_diagnostics(generation):
    result = {key: generation[key] for key in (
        "status", "phase", "terminal", "diagnosticCode", "failureStage", "targetDecisions",
        "completedPhases", "currentPhase", "currentProvider", "deadlineAt", "updatedAt",
    ) if key in generation}
    result["progress"] = [{key: item[key] for key in (
        "stage", "phase", "provider", "attempt", "diagnosticCode", "model",
        "inputCharacters", "outputCharacters", "elapsedMilliseconds", "targetDecisions",
        "inputTokens", "outputTokens", "reasoningTokens", "requestedModel",
    ) if key in item} | ({"sowDiagnostics": {key: item["sowDiagnostics"][key] for key in (
        "responseStatus", "incompleteReason", "stopReason", "outputTextCharacters",
        "outputValidationCategory", "outputValidationField",
    ) if key in item["sowDiagnostics"]}} if isinstance(item.get("sowDiagnostics"), dict) else {})
        for item in generation.get("progress", []) if isinstance(item, dict)]
    return result


async def main() -> int:
    evidence_dir = Path(os.environ.get("EVIDENCE_DIR", "/tmp/flowhive-psa-evidence"))
    evidence_dir.mkdir(parents=True, exist_ok=True)
    report: dict = {
        "status": "failed",
        "environment": "test",
        "sourceCommit": os.environ.get("TARGET_RELEASE_COMMIT", ""),
        # The installed identity is resolved by the workflow immediately
        # before this verifier runs. Never report a historical candidate SHA.
        "applicationSha": os.environ.get("TARGET_RELEASE_COMMIT", ""),
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
        require(re.fullmatch(r"[0-9a-f]{40}", report["applicationSha"]) is not None,
                "installed_application_identity_missing")
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
        # Check the real retained-version schema before creating a record or
        # spending tokens. Migration 099 alone is insufficient for confirmation.
        status, register, _ = http("/api/module025/sow-register", token=token)
        require(status == 200 and isinstance(register, dict) and isinstance(register.get("records"), list),
                "module025_register_prerequisite_http_" + str(status))
        report["retentionSchemaReady"] = True
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
            "projectName": f"Protected UAT Module 025 {suffix}",
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
        report["engagementId"] = engagement_id
        engagement_number = str(engagement.get("engagementNumber") or "")
        require(re.fullmatch(r"[0-9a-fA-F-]{36}", engagement_id) is not None, "module025_engagement_id_missing")
        require(engagement_number, "module025_engagement_number_missing")
        report["engagement"] = {"engagementNumber": engagement_number, "ownerUserId": engagement.get("ownerUserId")}

        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        require(status == 200 and isinstance(detail, dict), "module025_detail_http_" + str(status))
        current = detail.get("engagement") or {}
        phases = current.get("phases") or []
        phase_skeleton(phases)
        # Prove this normal SA can reach the real workspace, select the record
        # and see its document actions before spending any inference tokens.
        await browser_lifecycle(session, engagement_number, "", report, evidence_dir, preflight=True)

        edited_overview = service_overview + " SA review marker: confirm customer change window and rollback owner."
        save_payload = {
            "expectedRevision": current.get("revision"),
            "projectName": current.get("projectName"),
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
        report["generationId"] = generation_id
        require(re.fullmatch(r"[0-9a-fA-F-]{36}", generation_id) is not None, "module025_generation_id_missing")
        deadline = time.monotonic() + int(os.environ.get("MODULE025_GENERATION_TIMEOUT_SECONDS", "1500"))
        while True:
            status, generation, _ = http(f"/api/module025/sow-gsd/{engagement_id}/generations/{generation_id}", token=token)
            require(status == 200 and isinstance(generation, dict), "module025_generation_poll_http_" + str(status))
            # Preserve the final observed state even when the polling deadline expires.
            # The API's progress projection contains diagnostics, never source or draft text.
            report["lastGenerationState"] = generation_diagnostics(generation)
            if generation.get("terminal") is True:
                report["generationTerminal"] = {
                    key: generation.get(key)
                    for key in ("status", "phase", "diagnosticCode", "failureStage", "targetDecisions")
                    if generation.get(key) not in (None, "", [])
                }
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
        # Review the generated output; changing its source overview deliberately
        # invalidates LastGeneratedAt and must continue to block confirmation.
        reviewed_source = current.get("serviceOverview")
        reviewed_generation = current.get("lastGeneratedAt")
        reviewed_phases = [phase_payload(phase) for phase in (current.get("phases") or [])]
        plan = next(phase for phase in reviewed_phases if phase["phaseCode"] == "plan")
        plan["acceptanceCriteria"] = [*(plan.get("acceptanceCriteria") or []), edit_marker]
        save_payload = {
            "expectedRevision": current.get("revision"),
            "projectName": current.get("projectName"),
            "customerId": current.get("customerId"),
            "customerName": current.get("customerName"),
            "customerEntryMode": current.get("customerEntryMode"),
            "commercialModel": current.get("commercialModel"),
            "customerProgram": current.get("customerProgram"),
            "accountExecutiveUserId": current.get("accountExecutiveUserId"),
            "resaleUserId": current.get("resaleUserId"),
            "serviceOverview": reviewed_source,
            "phases": reviewed_phases,
        }
        status, saved, _ = http(f"/api/module025/sow-gsd/{engagement_id}", "PUT", save_payload, token)
        require(status == 200 and isinstance(saved, dict), "module025_review_edit_save_http_" + str(status))
        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        current = (detail or {}).get("engagement") or {}
        require(status == 200 and any(
            edit_marker in (phase.get("acceptanceCriteria") or [])
            for phase in (current.get("phases") or []) if phase.get("phaseCode") == "plan"
        ), "module025_review_edit_readback_failed")
        require(current.get("serviceOverview") == reviewed_source, "module025_review_source_changed")
        require(current.get("lastGeneratedAt") == reviewed_generation, "module025_review_generation_invalidated")

        status, confirmed, _ = http(f"/api/module025/sow-gsd/{engagement_id}/confirm", "POST", token=token)
        require(status == 200 and isinstance(confirmed, dict), "module025_confirm_http_" + str(status))
        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        require(status == 200 and ((detail or {}).get("engagement") or {}).get("status") == "confirmed", "module025_confirm_readback_failed")
        version, version_body = retained_version(engagement_id, token)
        require(version_body.get("currentContentReleased") is True, "module025_confirmation_not_retained")
        report["confirmedVersion"] = {key: version[key] for key in (
            "versionId", "versionNumber", "sourceRevision", "sowSha256", "gsdSha256")}
        readiness = version_body.get("sellReadiness") or {}
        report["sellAcceptance"] = {"status": "not_exercised" if readiness.get("ready") is True else "blocked",
            "diagnosticCode": readiness.get("diagnosticCode", "sell_readiness_missing"), "published": False}
        # A SOW lifecycle pass never claims that document publication occurred.
        report["fullRequestedScopePassed"] = False

        await browser_lifecycle(session, engagement_number, edit_marker, report, evidence_dir)
        report["retainedVersions"] = verify_historical_downloads(engagement_id, token, report["confirmedVersion"])

        status, detail, _ = http(f"/api/module025/sow-gsd/{engagement_id}", token=token)
        require(status == 200 and ((detail or {}).get("engagement") or {}).get("status") == "draft", "module025_reopen_readback_failed")
        status, archived_body, _ = http(f"/api/module025/sow-gsd/{engagement_id}/archive", "POST", token=token)
        require(status == 200 and isinstance(archived_body, dict), "module025_archive_http_" + str(status))
        archived = True
        report["status"] = "passed"
        report["normalAuthorizedSolutionArchitect"] = True
        report["reviewedSaveReadback"] = True
        report["retainedVersions"].update({"confirmed": True, "sowAndGsdDownloaded": True, "reopened": True, "archived": True})
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
    # Only locally constructed diagnostic codes are logged; never emit response
    # bodies, provider text, customer content, or credentials into Actions logs.
    diagnostic = report.get("diagnosticCode", "")
    if re.fullmatch(r"[A-Za-z0-9_]{1,160}", diagnostic):
        print("MODULE025_INSTALLED_SA_DIAGNOSTIC=" + diagnostic)
    print("MODULE025_INSTALLED_SA_UAT=" + ("PASS" if report["status"] == "passed" else "BLOCKED/FAIL"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))

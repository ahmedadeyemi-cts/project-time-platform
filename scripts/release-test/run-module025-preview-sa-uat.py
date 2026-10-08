#!/usr/bin/env python3
"""Signed-in Solution Architect acceptance for the Module 025 in-app SOW/GSD preview.

Handoff verification for the dark-launched Stage 3 preview slice. It drives the
LIVE Protected Test app as the real Solution Architect and proves the preview
surface actually renders: the DRAFT badge + document body for SOW, and the full
12-worksheet tab set + a populated cell grid for GSD.

Cost/safety model, mirroring run-module025-export-uat.py:
  * No model generation is requested for the draft path. Phase content is saved
    as deterministic fixtures, which is all the draft preview needs.
  * Exactly one run-scoped synthetic record is created and archived in finally.
  * The browser phase is read-only: every non-GET /api/module025 request is
    aborted, so opening a preview can never mutate business data.
  * The confirmed badge requires a generated+confirmed record. By default this
    is discovered read-only from the SA's existing records and skipped (not
    failed) if none exist. Set MODULE025_PREVIEW_GENERATE_CONFIRMED=true to make
    the verifier generate+confirm its own synthetic record (slow, uses the real
    provider) so the Confirmed badge is checked deterministically.
  * Secrets, customer content and provider text are never written to evidence.
"""
from __future__ import annotations

import asyncio
import importlib.util
import json
import os
import re
import time
import uuid
from pathlib import Path
from urllib.parse import urlparse

# Reuse the registered, hash-pinned SA verifier for its login, HTTP transport and
# phase helpers without editing it (same pattern as run-module025-export-uat.py).
_spec = importlib.util.spec_from_file_location("sa", Path(__file__).with_name("run-module025-installed-sa-uat.py"))
sa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sa)
transport = sa.http

# The standard GSD workbook tab set. The in-app GSD preview must expose every one
# of these as a worksheet tab (hidden sheets render with a "(hidden)" suffix).
EXPECTED_SHEETS = (
    "Summary", "Phase Breakdown", "Totals Sheet", "SELL SKUs", "Plan", "Design",
    "Implement", "Validate", "Release", "Architect Notes", "Gotcha Items",
    "Assumptions Responsibilities",
)
RECORD = re.compile(r"/api/module025/sow-gsd/[0-9a-fA-F-]{36}(?:/.*)?$")

created_id = ""


def http(path: str, method: str = "GET", payload: object | None = None, token: str = ""):
    """Request allowlist for this verifier and every imported helper.

    Reads are permitted against any Module 025 record (needed to discover an
    existing confirmed record). Writes are permitted only against the single
    synthetic record this run created.
    """
    bare = path.split("?", 1)[0]
    fixed = {
        ("POST", "/api/auth/local/login"), ("POST", "/api/auth/session/logout"),
        ("GET", "/api/module025/sow-gsd/bootstrap"), ("GET", "/api/module025/sow-gsd"),
        ("POST", "/api/module025/sow-gsd"),
    }
    permitted = (method, bare) in fixed
    if not permitted and method == "GET" and RECORD.fullmatch(bare):
        permitted = True
    if not permitted and created_id and method in ("PUT", "POST"):
        root = "/api/module025/sow-gsd/" + created_id
        permitted = bare in (root, root + "/generate", root + "/confirm", root + "/archive")
    sa.require(permitted, "preview_request_not_allowed")
    return transport(path, method, payload, token)


sa.http = http


def fixture_phases(current_phases: object) -> list[dict]:
    """Deterministic, model-free phase content so the draft preview has sections
    and the GSD grid has populated cells."""
    phases = [sa.phase_payload(phase) for phase in current_phases]
    sa.require(len(phases) == 5 and [p["phaseCode"] for p in phases] == list(sa.PHASE_CODES), "five_phases_required")
    for phase in phases:
        code = phase["phaseCode"]
        phase.update(
            objective="Synthetic preview " + code + " objective",
            detailedActivities=["Synthetic " + code + " activity"],
            deliverables=["Synthetic " + code + " deliverable"],
            acceptanceCriteria=["Synthetic " + code + " acceptance"],
            finalHours=2,
        )
        phase["tasks"] = [{
            "taskId": str(uuid.uuid4()),
            "description": "Synthetic preview " + code + " task",
            "hours": 2, "notes": "Synthetic preview cell",
        }]
    return phases


async def _close_preview(modal) -> None:
    await modal.get_by_role("button", name="Close preview").click()
    await modal.wait_for(state="hidden")


async def drive_preview(session: dict, number: str, variant: str, report: dict, report_key: str) -> None:
    """Open one record's preview in a read-only browser and assert the surface.

    variant 'draft' exercises both SOW and GSD draft previews (badge, document
    marker, the full 12-worksheet tab set and a populated cell grid). variant
    'confirmed' exercises the confirmed SOW preview (Confirmed badge, no DRAFT
    marker). Every non-GET /api/module025 request is aborted, so the preview can
    never mutate business data.
    """
    from playwright.async_api import async_playwright
    from playwright.async_api import TimeoutError as PlaywrightTimeoutError

    leg = {"status": "running", "stage": "launch", "variant": variant, "steps": [], "writeAttempts": 0}
    report[report_key] = leg

    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1600, "height": 1000})
        writes: list[str] = []

        async def read_only(route) -> None:
            request = route.request
            if urlparse(request.url).path.startswith("/api/module025/") and request.method not in ("GET", "HEAD", "OPTIONS"):
                writes.append(request.method)
                await route.abort()
            else:
                await route.continue_()

        await context.route("**/*", read_only)
        await context.add_init_script(
            "window.localStorage.setItem('projectPulseAuthSession', "
            + json.dumps(json.dumps(session))
            + "); window.localStorage.removeItem('projectPulseViewAsUser');"
        )
        page = await context.new_page()
        page.set_default_timeout(60_000)
        page_errors: list[str] = []
        page.on("pageerror", lambda _: page_errors.append("browser_page_error"))
        modal = page.locator(".m025-preview-modal")

        def stage(name: str) -> None:
            leg["stage"] = name
            print("MODULE025_PREVIEW_STEP=" + variant + ":" + name, flush=True)

        try:
            stage("navigate")
            await page.goto(sa.ORIGIN + "/#sow-generator", wait_until="domcontentloaded")
            workspace = page.locator('section[data-module025-sow-gsd-workspace="true"]:visible')
            await workspace.wait_for(state="visible")
            card = workspace.locator(".m025-work-card").filter(has_text=number).first
            await card.wait_for(state="visible")
            await card.click()
            editor = workspace.locator(".m025-editor-panel")
            await editor.get_by_text(number, exact=True).wait_for(state="visible")
            leg["steps"].append("select")

            if variant == "draft":
                stage("draft_sow")
                await workspace.get_by_role("button", name="Preview draft SOW", exact=True).click()
                await modal.wait_for(state="visible")
                await modal.locator(".m025-preview-badge--draft").wait_for(state="visible")
                doc = modal.locator(".m025-preview-doc")
                await doc.wait_for(state="visible")
                sa.require("DRAFT" in (await doc.inner_text()), "draft_sow_marker_missing")
                await modal.get_by_text("Plan", exact=False).first.wait_for(state="visible")
                await _close_preview(modal)
                leg["steps"].append("draft_sow")

                stage("draft_gsd")
                await workspace.get_by_role("button", name="Preview draft GSD", exact=True).click()
                await modal.wait_for(state="visible")
                tabs = modal.locator(".m025-preview-tabs")
                await tabs.wait_for(state="visible")
                for name in EXPECTED_SHEETS:
                    await tabs.get_by_role("tab", name=re.compile("^" + re.escape(name))).first.wait_for(state="visible")
                # Switch to a phase sheet and require a populated cell grid.
                await tabs.get_by_role("tab", name=re.compile("^Plan")).first.click()
                await modal.locator(".m025-preview-grid").wait_for(state="visible")
                await _close_preview(modal)
                leg["steps"].append("draft_gsd")
                leg["gsdTabsVerified"] = len(EXPECTED_SHEETS)
            else:
                stage("confirmed_sow")
                await workspace.get_by_role("button", name="Preview SOW", exact=True).click()
                await modal.wait_for(state="visible")
                await modal.locator(".m025-preview-badge--confirmed").wait_for(state="visible")
                doc = modal.locator(".m025-preview-doc")
                await doc.wait_for(state="visible")
                sa.require("DRAFT - Not approved" not in (await doc.inner_text()), "confirmed_preview_shows_draft_marker")
                await _close_preview(modal)
                leg["steps"].append("confirmed_sow")

            sa.require(not writes, "preview_browser_attempted_write")
            sa.require(not page_errors, "preview_browser_page_error")
            leg.update(status="passed", writeAttempts=len(writes))
        except PlaywrightTimeoutError:
            leg["status"] = "failed"
            raise sa.AcceptanceError("preview_browser_timeout_" + variant + "_" + leg["stage"]) from None
        except sa.AcceptanceError:
            leg["status"] = "failed"
            raise
        finally:
            leg["writeAttempts"] = len(writes)
            await context.close()
            await browser.close()


def create_fixture_record(token: str, bootstrap: dict) -> dict:
    global created_id
    ae = bootstrap.get("accountExecutives") or []
    inside = bootstrap.get("insideSalesRepresentatives") or []
    sa.require(bool(ae) and bool(inside), "people_directory_missing")
    suffix = os.environ.get("GITHUB_RUN_ID", "manual") + "-" + os.environ.get("GITHUB_RUN_ATTEMPT", "1")
    payload = {
        "projectName": "Synthetic preview UAT " + suffix,
        "customerId": None,
        "customerName": "Synthetic preview customer " + suffix,
        "customerEntryMode": "manual",
        "commercialModel": "time_and_materials",
        "customerProgram": "standard",
        "accountExecutiveUserId": ae[0]["userId"],
        "resaleUserId": inside[0]["userId"],
        "serviceOverview": "Synthetic in-app preview acceptance. Draft path requests no AI generation.",
    }
    status, created, _ = http("/api/module025/sow-gsd", "POST", payload, token)
    sa.require(status == 201 and isinstance(created, dict), "create_failed")
    engagement = created["engagement"]
    created_id = str(engagement["engagementId"])
    sa.require(re.fullmatch(r"[0-9a-fA-F-]{36}", created_id) is not None, "record_identity_invalid")
    root = "/api/module025/sow-gsd/" + created_id
    status, detail, _ = http(root, token=token)
    sa.require(status == 200 and isinstance(detail, dict), "initial_read_failed")
    current = detail["engagement"]
    payload.update(expectedRevision=current["revision"], phases=fixture_phases(current["phases"]))
    status, _, _ = http(root, "PUT", payload, token)
    sa.require(status == 200, "fixture_save_failed")
    status, detail, _ = http(root, token=token)
    sa.require(status == 200, "fixture_readback_failed")
    current = detail["engagement"]
    sa.require(current.get("status") == "draft", "unexpected_status_after_fixture")
    return current


def generate_and_confirm(token: str, current: dict) -> None:
    root = "/api/module025/sow-gsd/" + created_id
    status, queued, _ = http(root + "/generate", "POST", token=token)
    sa.require(status in (200, 202) and isinstance(queued, dict), "generation_start_failed")
    generation_id = str(queued.get("generationId") or "")
    sa.require(re.fullmatch(r"[0-9a-fA-F-]{36}", generation_id) is not None, "generation_id_missing")
    deadline = time.monotonic() + int(os.environ.get("MODULE025_GENERATION_TIMEOUT_SECONDS", "2520"))
    while True:
        status, generation, _ = http(root + "/generations/" + generation_id, token=token)
        sa.require(status == 200 and isinstance(generation, dict), "generation_poll_failed")
        if generation.get("terminal") is True:
            sa.require(generation.get("status") == "module025_detailed_scope_generated", "generation_terminal_failure")
            break
        sa.require(time.monotonic() < deadline, "generation_deadline_exceeded")
        time.sleep(5)
    status, detail, _ = http(root, token=token)
    sa.require(status == 200, "generated_read_failed")
    current = detail["engagement"]
    phases = [sa.phase_payload(phase) for phase in (current.get("phases") or [])]
    for phase in phases:
        phase["tasks"] = [{"taskId": str(uuid.uuid4()), "description": phase["objective"],
                           "hours": phase["finalHours"], "notes": "Synthetic reviewed task"}]
    save = {k: current.get(k) for k in (
        "projectName", "customerId", "customerName", "customerEntryMode", "commercialModel",
        "customerProgram", "accountExecutiveUserId", "resaleUserId", "serviceOverview")}
    save.update(expectedRevision=current.get("revision"), phases=phases)
    status, _, _ = http(root, "PUT", save, token)
    sa.require(status == 200, "review_save_failed")
    status, confirmed, _ = http(root + "/confirm", "POST", token=token)
    sa.require(status == 200 and isinstance(confirmed, dict), "confirm_failed")
    status, detail, _ = http(root, token=token)
    sa.require(status == 200 and detail["engagement"].get("status") == "confirmed", "confirm_readback_failed")


def discover_confirmed(token: str, owner_user_id: str, exclude_id: str) -> str:
    """Read-only lookup of an existing confirmed record owned by this SA.

    The list endpoint has no status filter, so page the active records and match
    status client-side. The synthetic record this run created is excluded.
    """
    from urllib.parse import urlencode
    query = urlencode({"state": "active", "ownerUserId": owner_user_id})
    status, body, _ = http("/api/module025/sow-gsd?" + query, token=token)
    if status != 200 or not isinstance(body, dict):
        return ""
    for record in body.get("engagements") or []:
        if not isinstance(record, dict):
            continue
        if record.get("status") == "confirmed" and str(record.get("engagementId")) != exclude_id:
            number = str(record.get("engagementNumber") or "")
            if number:
                return number
    return ""


async def main() -> int:
    evidence = Path(os.environ.get("EVIDENCE_DIR", "/tmp/module025-preview-evidence"))
    evidence.mkdir(parents=True, exist_ok=True)
    report: dict = {
        "status": "failed", "environment": "test", "feature": "module025-in-app-preview",
        "sourceCommit": os.environ.get("TARGET_RELEASE_COMMIT", ""),
        "generationPosts": 0, "productionMutation": False, "mockedResponses": False,
        "draftPreviewVerified": False,
    }
    token = ""
    archived = False
    confirmed_number = ""
    try:
        email = os.environ.get("PROJECTPULSE_M025_SA_EMAIL", "")
        password = os.environ.get("PROJECTPULSE_M025_SA_PASSWORD", "")
        sa.require(email.endswith(".local") or email.endswith("@ussignal.local"), "solution_architect_email_missing")
        sa.require(len(password) >= 12, "solution_architect_password_missing")
        session = sa.login(email, password)
        token = str(session["sessionToken"])
        password = ""

        status, bootstrap, _ = http("/api/module025/sow-gsd/bootstrap", token=token)
        sa.require(status == 200 and isinstance(bootstrap, dict), "bootstrap_failed")
        capabilities = bootstrap.get("capabilities") or {}
        sa.require(capabilities.get("preview") is True, "preview_capability_disabled")
        access = bootstrap.get("access") or {}
        sa.require(access.get("isSolutionArchitect") is True and access.get("canCreate") is True
                   and access.get("canEditOwn") is True and access.get("isViewAs") is False
                   and access.get("protectedTestUatRoleFixture") is False, "normal_sa_authority_required")

        current = create_fixture_record(token, bootstrap)
        draft_number = str(current.get("engagementNumber") or "")
        sa.require(bool(draft_number), "engagement_number_missing")
        report["engagement"] = {"engagementNumber": draft_number}

        # Draft preview first, while the synthetic record is still a draft.
        await drive_preview(session, draft_number, "draft", report, "draftPreview")
        report["draftPreviewVerified"] = True

        # Confirmed preview: either generate+confirm our own record (deterministic,
        # uses the provider) or discover an existing confirmed record read-only.
        if os.environ.get("MODULE025_PREVIEW_GENERATE_CONFIRMED", "").lower() == "true":
            generate_and_confirm(token, current)
            report["generationPosts"] = 1
            report["confirmedSource"] = "generated"
            await drive_preview(session, draft_number, "confirmed", report, "confirmedPreview")
        else:
            owner_user_id = str((bootstrap.get("currentUser") or {}).get("userId") or "")
            confirmed_number = discover_confirmed(token, owner_user_id, created_id)
            if confirmed_number:
                report["confirmedSource"] = "discovered"
                await drive_preview(session, confirmed_number, "confirmed", report, "confirmedPreview")
            else:
                report["confirmedSource"] = "none"
                report["confirmedPreview"] = {"status": "skipped", "reason": "no_confirmed_record_available"}

        status, _, _ = http("/api/module025/sow-gsd/" + created_id + "/archive", "POST", token=token)
        sa.require(status == 200, "synthetic_cleanup_failed")
        archived = True
        report["status"] = "passed"
    except sa.AcceptanceError as error:
        report["diagnosticCode"] = str(error)
    except Exception as error:  # pragma: no cover - type-only diagnostic
        report["diagnosticCode"] = "unexpected_" + type(error).__name__
    finally:
        if created_id and token and not archived:
            try:
                status, _, _ = http("/api/module025/sow-gsd/" + created_id, token=token)
                if status == 200:
                    http("/api/module025/sow-gsd/" + created_id + "/archive", "POST", token=token)
                    archived = True
            except Exception:
                report["cleanup"] = "archive_not_verified"
        report["syntheticRecordArchived"] = archived
        if token:
            try:
                http("/api/auth/session/logout", "POST", {}, token)
            except Exception:
                report["logout"] = "not_verified"
        (evidence / "module025-preview-uat.json").write_text(json.dumps(report, indent=2) + "\n")
    diagnostic = report.get("diagnosticCode", "none")
    if re.fullmatch(r"[A-Za-z0-9_]{1,160}", diagnostic):
        print("MODULE025_PREVIEW_DIAGNOSTIC=" + diagnostic)
    print("MODULE025_PREVIEW_SA_UAT=" + ("PASS" if report["status"] == "passed" else "BLOCKED/FAIL"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))

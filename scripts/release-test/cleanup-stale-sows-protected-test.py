#!/usr/bin/env python3
"""Find and optionally delete stale SOWs in Protected Test through canonical APIs."""
from __future__ import annotations
import json
import os
from pathlib import Path
import re
import sys
from typing import Any
from urllib import error, request

BASE = "https://phd-west-test.onenecklab.com"
LOGIN = "project.team.coordinator@ussignal.local"
CONFIRMATION = "DELETE STALE TEST SOWS"
UUID = re.compile(r"^[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$")
DELETE_REASON = (
    "Owner-requested stale/superseded SOW cleanup; preserved the current "
    "FlowHive-authoritative Work Register SOW."
)

def text(value: Any) -> str:
    return str(value or "").strip()

def is_uuid(value: Any) -> bool:
    return bool(UUID.fullmatch(text(value)))

def is_sow_type(value: Any) -> bool:
    return text(value).lower().replace("_", " ") in {"sow", "statement of work"}

class Client:
    def __init__(self) -> None:
        self.token = ""

    def call(self, path: str, method: str = "GET", body: dict | None = None,
             module: str = "055C") -> tuple[int, Any]:
        payload = None if body is None else json.dumps(body).encode()
        headers = {"Cache-Control": "no-cache", "Origin": BASE,
                   "Sec-Fetch-Site": "same-origin", "Accept": "application/json"}
        if payload is not None:
            headers["Content-Type"] = "application/json"
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
            headers["X-ProjectPulse-Session"] = self.token
            headers["X-ProjectPulse-Module-Number"] = module
        req = request.Request(BASE + path, data=payload, headers=headers, method=method)
        try:
            with request.urlopen(req, timeout=120) as response:
                raw = response.read()
                return response.status, json.loads(raw) if raw else {}
        except error.HTTPError as exc:
            raw = exc.read()
            try:
                parsed = json.loads(raw) if raw else {}
            except json.JSONDecodeError:
                parsed = {"message": "non_json_response"}
            return exc.code, parsed

    def login(self, secret: str) -> None:
        status, body = self.call("/api/auth/local/login", "POST",
                                 {"username": LOGIN, "password": secret})
        if status != 200 or not isinstance(body, dict):
            raise RuntimeError(f"login_failed_http_{status}")
        if body.get("provider") != "LOCAL" or body.get("mustChangePassword") is not False:
            raise RuntimeError("login_contract_invalid")
        token = text(body.get("sessionToken"))
        if len(token) < 20:
            raise RuntimeError("session_token_missing")
        self.token = token

    def logout(self) -> None:
        if not self.token:
            return
        try:
            self.call("/api/auth/session/logout", "POST", {}, "055C")
        finally:
            self.token = ""

def current_sow_work_register_id(readiness: dict) -> tuple[str, str]:
    authority = readiness.get("documentAuthority") or {}
    planning_id = text(authority.get("currentSowDocumentId")).lower()
    work_register_id = text(authority.get("currentSowWorkRegisterDocumentId")).lower()
    if not is_uuid(planning_id):
        return "", "current_planning_sow_missing"
    if not is_uuid(work_register_id):
        return "", "current_sow_work_register_mapping_missing"
    return work_register_id, ""

def classify_stale(current_id: str, documents: list[dict],
                   evidence: list[dict]) -> tuple[list[dict], str]:
    by_id = {
        text(row.get("documentId")).lower(): row
        for row in documents
        if isinstance(row, dict) and is_uuid(row.get("documentId"))
    }
    if current_id not in by_id:
        return [], "current_sow_not_present_in_work_register"
    evidence_sow_ids = {
        text(row.get("workRegisterDocumentId")).lower()
        for row in evidence
        if isinstance(row, dict)
        and is_sow_type(row.get("category"))
        and is_uuid(row.get("workRegisterDocumentId"))
    }
    candidates = []
    for doc_id, row in by_id.items():
        if doc_id == current_id:
            continue
        if is_sow_type(row.get("documentType")) or doc_id in evidence_sow_ids:
            candidates.append({
                "documentId": doc_id,
                "fileName": text(row.get("fileName")),
                "documentType": text(row.get("documentType")),
                "status": text(row.get("status")),
                "effectiveDate": text(row.get("effectiveDate")),
                "versionLabel": text(row.get("versionLabel")),
            })
    return sorted(candidates, key=lambda row: (row["fileName"].lower(), row["documentId"])), ""

def project_rows(overview: dict) -> list[dict]:
    rows = []
    for item in overview.get("workItems") or []:
        if not isinstance(item, dict) or text(item.get("workType")).lower() != "project":
            continue
        project_id = text(item.get("workId"))
        if not is_uuid(project_id):
            continue
        rows.append({
            "projectId": project_id.lower(),
            "projectName": text(item.get("workName") or item.get("projectName") or project_id),
            "lifecycle": text(item.get("lifecycle")),
        })
    return rows

def require_ok(client: Client, path: str, module: str) -> dict:
    status, body = client.call(path, module=module)
    if status != 200 or not isinstance(body, dict):
        raise RuntimeError(f"read_failed_{status}")
    return body

def run() -> dict:
    mode = text(os.environ.get("PROJECTPULSE_STALE_SOW_MODE") or "dry-run").lower()
    if mode not in {"dry-run", "apply"}:
        raise RuntimeError("unsupported_mode")
    if mode == "apply" and text(os.environ.get("PROJECTPULSE_STALE_SOW_CONFIRMATION")) != CONFIRMATION:
        raise RuntimeError("apply_confirmation_missing")
    secret = os.environ.get("PROJECTPULSE_M087_PASSWORD", "")
    if len(secret) < 12:
        raise RuntimeError("test_login_secret_missing")

    report: dict[str, Any] = {
        "environment": "test", "origin": BASE, "mode": mode,
        "productionMutation": False, "projects": [], "summary": {}
    }
    client = Client()
    client.login(secret)
    try:
        overview = require_ok(client, "/api/work-register/overview", "055C")
        for project in project_rows(overview):
            project_id = project["projectId"]
            item = {**project, "status": "evaluated", "currentSowDocumentId": "",
                    "staleSows": [], "deletedDocumentIds": []}
            try:
                readiness = require_ok(
                    client, f"/api/project-flowhive/projects/{project_id}/documents/readiness", "066")
                enterprise = require_ok(
                    client, f"/api/project-flowhive/projects/{project_id}/enterprise", "066")
                canonical = require_ok(
                    client, f"/api/work-register/projects/{project_id}/documents", "055C")
                current_id, reason = current_sow_work_register_id(readiness)
                if reason:
                    item.update(status="skipped", diagnostic=reason)
                    report["projects"].append(item)
                    continue
                item["currentSowDocumentId"] = current_id
                stale, reason = classify_stale(
                    current_id, canonical.get("documents") or [], enterprise.get("sowEvidence") or [])
                if reason:
                    item.update(status="skipped", diagnostic=reason)
                    report["projects"].append(item)
                    continue
                item["staleSows"] = stale
                if mode == "apply":
                    for stale_row in stale:
                        doc_id = stale_row["documentId"]
                        status, body = client.call(
                            f"/api/work-register/projects/{project_id}/documents/{doc_id}",
                            "DELETE", {"reason": DELETE_REASON}, "055C")
                        if status != 200 or body.get("status") not in {
                            "document_deleted", "document_already_deleted"
                        }:
                            raise RuntimeError(f"delete_failed_{doc_id}_{status}")
                        item["deletedDocumentIds"].append(doc_id)
                    verify = require_ok(
                        client, f"/api/work-register/projects/{project_id}/documents", "055C")
                    remaining = {
                        text(row.get("documentId")).lower()
                        for row in (verify.get("documents") or [])
                        if isinstance(row, dict)
                    }
                    if current_id not in remaining:
                        raise RuntimeError("current_sow_missing_after_cleanup")
                    undeleted = [row["documentId"] for row in stale if row["documentId"] in remaining]
                    if undeleted:
                        raise RuntimeError("stale_sow_remained_after_cleanup")
                report["projects"].append(item)
            except Exception as exc:
                item.update(status="skipped", diagnostic=str(exc)[:160])
                report["projects"].append(item)
    finally:
        client.logout()

    projects = report["projects"]
    stale_count = sum(len(row.get("staleSows") or []) for row in projects)
    deleted_count = sum(len(row.get("deletedDocumentIds") or []) for row in projects)
    report["summary"] = {
        "projectsEvaluated": len(projects),
        "projectsSkipped": sum(row.get("status") == "skipped" for row in projects),
        "projectsWithStaleSows": sum(bool(row.get("staleSows")) for row in projects),
        "staleSowsFound": stale_count,
        "staleSowsDeleted": deleted_count,
    }
    return report

def main() -> int:
    output = Path(os.environ.get(
        "PROJECTPULSE_STALE_SOW_REPORT", "/tmp/projectpulse-stale-sow-report.json"))
    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        report = run()
        output.write_text(json.dumps(report, indent=2) + "\n")
        print("STALE_SOW_MAINTENANCE=PASS")
        print(json.dumps(report["summary"], sort_keys=True))
        return 0
    except Exception as exc:
        failure = {
            "environment": "test", "origin": BASE,
            "status": "failed", "diagnostic": str(exc)[:160],
            "productionMutation": False,
        }
        output.write_text(json.dumps(failure, indent=2) + "\n")
        print(f"STALE_SOW_MAINTENANCE=FAIL diagnostic={failure['diagnostic']}", file=sys.stderr)
        return 1

if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

repo_root = Path(__file__).resolve().parents[1]
route_json_path = repo_root / "docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.json"
report_path = repo_root / "docs/production-readiness/021_ROLE_BASED_PRODUCTION_READINESS_RUNBOOKS.md"
json_path = repo_root / "docs/production-readiness/021_ROLE_BASED_PRODUCTION_READINESS_RUNBOOKS.json"

if not route_json_path.exists():
    raise SystemExit(f"Missing route inventory: {route_json_path}")

route_inventory = json.loads(route_json_path.read_text())
routes = route_inventory.get("routes", [])

def route_matches(route: dict, route_terms: list[str], group_terms: list[str], permission_terms: list[str]) -> bool:
    haystack = " ".join([
        str(route.get("route", "")),
        str(route.get("href", "")),
        str(route.get("title", "")),
        str(route.get("navLabel", "")),
        str(route.get("group", "")),
        str(route.get("status", "")),
        str(route.get("description", "")),
        " ".join(route.get("permissions") or []),
    ]).lower()

    return any(term.lower() in haystack for term in route_terms + group_terms + permission_terms)

def pick_routes(route_terms: list[str], group_terms: list[str] | None = None, permission_terms: list[str] | None = None, limit: int = 10) -> list[dict]:
    group_terms = group_terms or []
    permission_terms = permission_terms or []

    matched = [
        route for route in routes
        if route_matches(route, route_terms, group_terms, permission_terms)
    ]

    seen = set()
    unique = []
    for route in matched:
        key = route.get("route")
        if key in seen:
            continue
        seen.add(key)
        unique.append(route)

    return sorted(unique, key=lambda item: (str(item.get("group") or ""), str(item.get("route") or "")))[:limit]

def route_row(route: dict) -> str:
    label = route.get("navLabel") or route.get("title") or route.get("route")
    route_key = route.get("route") or ""
    group = route.get("group") or "Ungrouped"
    status = route.get("status") or "Missing"
    perms = ", ".join(route.get("permissions") or []) or "No explicit permission listed"
    return f"| `#{route_key}` | {label} | {group} | {status} | {perms} |"

personas = [
    {
        "key": "administrator",
        "name": "Administrator / System Owner",
        "objective": "Validate platform governance, role enforcement, production readiness command center, operational controls, and auditability.",
        "route_terms": [
            "dashboard", "production", "operations", "role", "admin", "security",
            "audit", "navigation", "registry", "user", "settings", "email", "view"
        ],
        "group_terms": ["Security", "Audit", "System", "Operations", "Administration"],
        "permission_terms": ["MANAGE_ALL", "SYSTEM_ADMINISTRATION", "USER_ADMINISTRATION", "ROLE"],
        "steps": [
            "Confirm the user is authenticated with Administrator-level access.",
            "Open the production readiness command center and verify operational status cards are visible.",
            "Open role/security administration and confirm the role matrix aligns with expected access boundaries.",
            "Review user administration and confirm inactive or restricted users do not appear as active production operators.",
            "Use the View-As preview only as a read-only access verification tool.",
            "Open audit history and confirm administrative and workflow activities are traceable.",
            "Record any route, permission, or visibility gap as a production readiness issue."
        ],
        "acceptance": [
            "Administrator can access governance, audit, production operations, and readiness surfaces.",
            "View-As behavior remains read-only for write actions.",
            "Protected routes do not expose data to unauthenticated users.",
            "Audit evidence is available for sensitive workflow and administrative areas."
        ]
    },
    {
        "key": "project_manager",
        "name": "Project Manager",
        "objective": "Validate customer-to-project intake, resource assignment readiness, project allocation, task handoff, and approval preparation.",
        "route_terms": [
            "project", "intake", "resource", "assignment", "allocation", "work",
            "task", "handoff", "approval", "customer"
        ],
        "group_terms": ["Projects", "Allocations", "Time", "Approvals"],
        "permission_terms": ["PROJECT", "RESOURCE", "APPROVAL", "MANAGE_PROJECTS"],
        "steps": [
            "Confirm the user can access customer and project intake context appropriate to the role.",
            "Review project intake summary and verify records have clear status and ownership.",
            "Open resource assignment or allocation views and validate staffing/capacity information is understandable.",
            "Review work-task handoff and confirm planning details can move into execution.",
            "Confirm approval readiness indicators are visible before work moves to downstream approval or export handling.",
            "Record any missing data, unclear empty state, or route visibility mismatch."
        ],
        "acceptance": [
            "Project intake data is understandable and operationally actionable.",
            "Resource assignment views support staffing decisions.",
            "Project handoff information is visible without requiring manual spreadsheet reconciliation.",
            "Approval readiness is clear before downstream workflow actions."
        ]
    },
    {
        "key": "manager",
        "name": "Manager / Approver",
        "objective": "Validate approval queue visibility, review controls, exception handling, unlock decisions, and audit accountability.",
        "route_terms": [
            "approval", "manager", "unlock", "timesheet", "workflow", "pending",
            "review", "exception", "audit"
        ],
        "group_terms": ["Time", "Approvals", "Security", "Audit"],
        "permission_terms": ["APPROVE", "MANAGER", "TIMESHEET", "WORKFLOW"],
        "steps": [
            "Confirm the user can access approval queues and pending review counts.",
            "Review one approval-ready item and verify the displayed information supports an approval decision.",
            "Validate exception or unlock controls are restricted and clearly labeled.",
            "Confirm approval actions produce traceable audit evidence.",
            "Verify unauthenticated access to approval routes returns the expected protected response.",
            "Record any missing approval evidence, confusing label, or role mismatch."
        ],
        "acceptance": [
            "Manager can identify pending approvals quickly.",
            "Approval and unlock actions are controlled by role.",
            "Exception handling is visible without bypassing accountability.",
            "Audit history can support review of approval activity."
        ]
    },
    {
        "key": "engineer",
        "name": "Engineer / Contributor",
        "objective": "Validate time entry, assigned work visibility, project/non-project activity handling, and submission readiness.",
        "route_terms": [
            "timesheet", "time", "task", "assignment", "work", "project",
            "activity", "engineer", "calendar"
        ],
        "group_terms": ["Time", "Projects", "Allocations"],
        "permission_terms": ["TIME", "ENGINEER", "TASK", "ASSIGNMENT"],
        "steps": [
            "Confirm the user can access the correct time-entry or assigned-work area.",
            "Review assigned project work and validate it is distinguishable from non-project activity.",
            "Confirm week/date context is understandable and supports accurate entry.",
            "Check whether validation, holidays, preferences, or hidden-row behavior improves entry accuracy.",
            "Confirm submission readiness is clear before work enters approval routing.",
            "Record any work visibility gap, unclear validation message, or missing route permission."
        ],
        "acceptance": [
            "Engineer can find assigned work and time-entry context.",
            "Project and non-project work are clearly separated.",
            "Submission readiness is understandable.",
            "The contributor experience supports accurate downstream approval and export."
        ]
    },
    {
        "key": "accounting",
        "name": "Accounting / Export Reviewer",
        "objective": "Validate export package readiness, reconciliation evidence, protected export actions, and downstream reporting controls.",
        "route_terms": [
            "export", "accounting", "reconciliation", "package", "download",
            "billing", "approval", "workflow", "audit"
        ],
        "group_terms": ["Time", "Approvals", "Security", "Audit", "Operations"],
        "permission_terms": ["EXPORT", "ACCOUNTING", "FINANCE", "RECONCILIATION"],
        "steps": [
            "Confirm the user can access export readiness and accounting review surfaces.",
            "Review export package status and validate readiness indicators are understandable.",
            "Confirm reconciliation evidence is visible before export actions.",
            "Validate export/download actions are protected by role.",
            "Review audit history for export-related traceability.",
            "Record any missing export evidence, unclear reconciliation status, or role enforcement issue."
        ],
        "acceptance": [
            "Accounting can identify export-ready work.",
            "Export actions are controlled and traceable.",
            "Reconciliation evidence is available before downstream handoff.",
            "Audit history supports accounting review."
        ]
    },
    {
        "key": "read_only_stakeholder",
        "name": "Read-Only Stakeholder",
        "objective": "Validate safe visibility for leadership, auditors, or stakeholders who need status awareness without write access.",
        "route_terms": [
            "dashboard", "summary", "report", "audit", "production", "readiness",
            "customer", "project", "workflow"
        ],
        "group_terms": ["Projects", "Time", "Security", "Audit", "Operations"],
        "permission_terms": ["VIEW", "READ", "REPORT"],
        "steps": [
            "Confirm the user can access only appropriate read-oriented surfaces.",
            "Open the production readiness or reporting dashboard and review high-level status.",
            "Open project or workflow summary views without performing write actions.",
            "Confirm restricted actions are hidden, disabled, or rejected according to role enforcement.",
            "Review audit/reporting visibility for transparency.",
            "Record any overexposed action, missing status summary, or route visibility issue."
        ],
        "acceptance": [
            "Stakeholder can view appropriate status information.",
            "Write actions are unavailable or denied.",
            "Production readiness status is understandable without operational permissions.",
            "Role-based visibility supports transparency without weakening control."
        ]
    }
]

output = {
    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    "sourceRouteReport": str(route_json_path.relative_to(repo_root)),
    "personaCount": len(personas),
    "personas": []
}

lines = []
lines.append("# 021 Role-Based Production Readiness Runbooks")
lines.append("")
lines.append(f"Generated UTC: `{output['generatedAtUtc']}`")
lines.append("")
lines.append("## Purpose")
lines.append("")
lines.append("These runbooks define role-based production readiness validation paths for ProjectPulse / ChangePoint. They are designed to confirm that each major persona can access the correct workflows, that restricted actions remain controlled, and that production-critical evidence is visible before release candidate validation.")
lines.append("")
lines.append("## Execution Guidance")
lines.append("")
lines.append("Use these runbooks during production readiness validation. For each persona, validate route visibility, role enforcement, workflow clarity, evidence availability, and auditability. Record findings as release-hardening issues before final release candidate validation.")
lines.append("")

for persona in personas:
    selected = pick_routes(
        persona["route_terms"],
        persona.get("group_terms", []),
        persona.get("permission_terms", []),
        limit=10
    )

    output["personas"].append({
        "key": persona["key"],
        "name": persona["name"],
        "objective": persona["objective"],
        "recommendedRoutes": selected,
        "validationSteps": persona["steps"],
        "acceptanceCriteria": persona["acceptance"],
    })

    lines.append(f"## {persona['name']}")
    lines.append("")
    lines.append(f"**Objective:** {persona['objective']}")
    lines.append("")
    lines.append("### Recommended Route Review")
    lines.append("")
    if selected:
        lines.append("| Route | Label | Group | Status | Permission Signal |")
        lines.append("|---|---|---|---|---|")
        for route in selected:
            lines.append(route_row(route))
    else:
        lines.append("- No matching routes were found by the static scanner. Validate manually during production readiness review.")
    lines.append("")
    lines.append("### Validation Steps")
    lines.append("")
    for index, step in enumerate(persona["steps"], start=1):
        lines.append(f"{index}. {step}")
    lines.append("")
    lines.append("### Acceptance Criteria")
    lines.append("")
    for item in persona["acceptance"]:
        lines.append(f"- {item}")
    lines.append("")

lines.append("## Cross-Role Production Readiness Sequence")
lines.append("")
lines.append("1. Start with Administrator / System Owner to validate governance, access, and readiness command-center controls.")
lines.append("2. Validate Project Manager workflow from customer/project intake through resource assignment and handoff.")
lines.append("3. Validate Engineer / Contributor workflow for assigned work and time-entry readiness.")
lines.append("4. Validate Manager / Approver workflow for approvals, exceptions, and auditability.")
lines.append("5. Validate Accounting / Export Reviewer workflow for export readiness and reconciliation evidence.")
lines.append("6. Validate Read-Only Stakeholder access to confirm transparency without write capability.")
lines.append("")
lines.append("## 021D Validation Notes")
lines.append("")
lines.append("- This is a production readiness documentation and static-route mapping pass.")
lines.append("- Full browser validation remains deferred until the final 021 release-candidate validation.")
lines.append("- Recommended routes are generated from the 021B route integrity inventory.")
lines.append("- If a recommended route appears under the wrong role, update route metadata or permission mapping in a later 021 hardening pass.")
lines.append("")

report_path.write_text("\n".join(lines))
json_path.write_text(json.dumps(output, indent=2))

print(f"Generated {report_path}")
print(f"Generated {json_path}")
print(f"Personas: {len(personas)}")

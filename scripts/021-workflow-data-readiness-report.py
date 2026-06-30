#!/usr/bin/env python3
from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from pathlib import Path

repo_root = Path(__file__).resolve().parents[1]
program_path = repo_root / "src/backend/ProjectTime.Api/Program.cs"
app_path = repo_root / "src/frontend/project-time-web/src/App.jsx"
route_json_path = repo_root / "docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.json"
report_path = repo_root / "docs/production-readiness/021_WORKFLOW_DATA_READINESS_REPORT.md"
json_path = repo_root / "docs/production-readiness/021_WORKFLOW_DATA_READINESS_REPORT.json"
sql_path = repo_root / "database/reports/021-workflow-data-readiness-probe.sql"

program_text = program_path.read_text() if program_path.exists() else ""
app_text = app_path.read_text() if app_path.exists() else ""

if not route_json_path.exists():
    raise SystemExit(f"Missing route inventory: {route_json_path}")

route_inventory = json.loads(route_json_path.read_text())
routes = route_inventory.get("routes", [])

workflows = [
    {
        "area": "Customer Directory",
        "purpose": "Customer, contact, and account context is available before project intake and downstream workflow activity.",
        "endpoints": ["/api/customers/overview", "/api/customers"],
        "routeTerms": ["customer"],
        "tables": ["customers", "customer_contacts", "customer_locations"],
        "readinessChecks": [
            "At least one active customer exists.",
            "Customer records have usable names and ownership context.",
            "Customer contact data is available where required by intake workflows."
        ],
    },
    {
        "area": "Project Intake",
        "purpose": "Intake records can be reviewed, linked, promoted, and prepared for project execution.",
        "endpoints": [
            "/api/project-intake/summary",
            "/api/project-intake/overview",
            "/api/project-intake/work-task-handoff",
            "/api/project-intake/project-link-options",
            "/api/project-intake/resource-assignment-handoff",
            "/api/project-intake/resource-assignment-promotions"
        ],
        "routeTerms": ["project", "intake"],
        "tables": ["project_intake_requests", "project_intakes", "project_intake_supporting_documents", "project_intake_work_tasks"],
        "readinessChecks": [
            "Open and recently completed intake records are available for review.",
            "Intake records can be associated with projects or project-link candidates.",
            "Supporting documents and handoff details are visible where applicable."
        ],
    },
    {
        "area": "Resource Assignment",
        "purpose": "Project demand can be matched to available resource and capacity signals.",
        "endpoints": [
            "/api/resource-scheduling/capacity",
            "/api/project-allocation-info/source-projects",
            "/api/project-allocation-info/engineers",
            "/api/project-allocation-info/projects"
        ],
        "routeTerms": ["resource", "assignment", "allocation", "engineer"],
        "tables": ["resource_assignments", "project_resource_assignments", "project_allocations", "app_users"],
        "readinessChecks": [
            "Active users include assignable delivery resources.",
            "Project allocation records can be reviewed.",
            "Capacity or assignment views provide enough information to support staffing decisions."
        ],
    },
    {
        "area": "Approval Workflow",
        "purpose": "Submitted work can be reviewed, approved, declined, unlocked, and audited through controlled workflow actions.",
        "endpoints": [
            "/api/manager/approvals",
            "/api/workflow/approval-items",
            "/api/workflow/approval-items/action",
            "/api/workflow/action-capabilities",
            "/api/workflow/approval-export-summary"
        ],
        "routeTerms": ["approval", "manager", "workflow"],
        "tables": ["manager_approval_requests", "time_approval_requests", "time_entries", "time_workflow_locks"],
        "readinessChecks": [
            "Pending and completed approval records are available for review.",
            "Approval actions are role-gated.",
            "Workflow summary data clearly separates pending review, approved, and blocked items."
        ],
    },
    {
        "area": "Export Package",
        "purpose": "Approved work can move into controlled export and reconciliation readiness.",
        "endpoints": [
            "/api/time-exports",
            "/api/export-packages/readiness-summary",
            "/api/workflow/reconciliation-workbench",
            "/api/workflow/lock-evidence"
        ],
        "routeTerms": ["export", "package", "reconciliation", "accounting"],
        "tables": ["time_workflow_exports", "time_export_packages", "time_export_package_items", "time_entries"],
        "readinessChecks": [
            "Approved or export-ready time exists.",
            "Export package history or readiness evidence is visible.",
            "Reconciliation and lock evidence can be reviewed before downstream handoff."
        ],
    },
    {
        "area": "Audit Evidence",
        "purpose": "Security, workflow, approval, export, and administrative actions are traceable.",
        "endpoints": [
            "/api/audit/history",
            "/api/audit-history/summary",
            "/api/audit-history/events"
        ],
        "routeTerms": ["audit", "history", "evidence"],
        "tables": ["audit_logs", "audit_events", "system_email_provider_test_events"],
        "readinessChecks": [
            "Audit history is populated.",
            "Audit filters support operator, action, and workflow review.",
            "Export, approval, administrative, and notification activity is traceable."
        ],
    },
    {
        "area": "Production Readiness Command Center",
        "purpose": "Production readiness status can be reviewed from consolidated operational indicators.",
        "endpoints": [
            "/api/production/readiness-command-center",
            "/api/navigation/registry-integrity",
            "/api/dashboard/module-visibility-smoke"
        ],
        "routeTerms": ["dashboard", "production", "readiness", "operations"],
        "tables": ["dashboard_module_visibility_expectations", "app_users", "projects", "time_entries", "audit_logs"],
        "readinessChecks": [
            "Production readiness endpoint is available.",
            "Route registry and module visibility evidence are available.",
            "Readiness indicators cover users, projects, time, audit, and route contracts."
        ],
    },
]

def count_table_mentions(table_name: str) -> int:
    return len(re.findall(rf"\b{re.escape(table_name)}\b", program_text, flags=re.IGNORECASE))

def endpoint_present(endpoint: str) -> bool:
    return endpoint in program_text or endpoint in app_text

def route_matches_terms(route: dict, terms: list[str]) -> bool:
    haystack = " ".join([
        str(route.get("route", "")),
        str(route.get("href", "")),
        str(route.get("title", "")),
        str(route.get("navLabel", "")),
        str(route.get("group", "")),
        str(route.get("description", "")),
        " ".join(route.get("permissions") or []),
    ]).lower()
    return any(term.lower() in haystack for term in terms)

results = []
for workflow in workflows:
    endpoint_results = [{"endpoint": item, "present": endpoint_present(item)} for item in workflow["endpoints"]]
    table_results = [{"table": item, "mentions": count_table_mentions(item)} for item in workflow["tables"]]
    route_results = [
        {
            "route": item.get("route"),
            "title": item.get("title") or item.get("navLabel") or item.get("route"),
            "group": item.get("group"),
            "status": item.get("status"),
        }
        for item in routes
        if route_matches_terms(item, workflow["routeTerms"])
    ]

    endpoint_ready = sum(1 for item in endpoint_results if item["present"])
    table_signal_count = sum(1 for item in table_results if item["mentions"] > 0)
    route_signal_count = len(route_results)

    if endpoint_ready == len(endpoint_results) and table_signal_count > 0 and route_signal_count > 0:
        status = "ready_for_live_validation"
    elif endpoint_ready > 0 and route_signal_count > 0:
        status = "needs_data_confirmation"
    else:
        status = "needs_review"

    results.append({
        "area": workflow["area"],
        "purpose": workflow["purpose"],
        "status": status,
        "endpointResults": endpoint_results,
        "tableResults": table_results,
        "routeResults": route_results[:12],
        "readinessChecks": workflow["readinessChecks"],
        "summary": {
            "endpointCount": len(endpoint_results),
            "presentEndpointCount": endpoint_ready,
            "tableSignalCount": table_signal_count,
            "routeSignalCount": route_signal_count,
        }
    })

output = {
    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    "sourceRouteReport": str(route_json_path.relative_to(repo_root)),
    "workflowCount": len(results),
    "results": results,
}

json_path.write_text(json.dumps(output, indent=2))

sql_entries = []
for workflow in workflows:
    area = workflow["area"].replace("'", "''")
    for table in workflow["tables"]:
        sql_entries.append((area, f"public.{table}"))

sql_lines = []
sql_lines.append("-- 021E Workflow Data Readiness Probe")
sql_lines.append("-- Run against the target ProjectPulse database during production readiness validation.")
sql_lines.append("-- This probe does not modify data.")
sql_lines.append("")
sql_lines.append("CREATE TEMP TABLE IF NOT EXISTS workflow_data_readiness_probe (")
sql_lines.append("    area text,")
sql_lines.append("    table_name text,")
sql_lines.append("    table_exists boolean,")
sql_lines.append("    row_count bigint")
sql_lines.append(");")
sql_lines.append("")
sql_lines.append("TRUNCATE workflow_data_readiness_probe;")
sql_lines.append("")
sql_lines.append("DO $$")
sql_lines.append("DECLARE")
sql_lines.append("    table_area text;")
sql_lines.append("    table_item text;")
sql_lines.append("    row_total bigint;")
sql_lines.append("BEGIN")
sql_lines.append("    FOR table_area, table_item IN")
sql_lines.append("        VALUES")
for index, (area, table) in enumerate(sql_entries):
    suffix = "," if index < len(sql_entries) - 1 else ""
    sql_lines.append(f"        ('{area}', '{table}'){suffix}")
sql_lines.append("    LOOP")
sql_lines.append("        IF to_regclass(table_item) IS NULL THEN")
sql_lines.append("            INSERT INTO workflow_data_readiness_probe(area, table_name, table_exists, row_count)")
sql_lines.append("            VALUES (table_area, table_item, false, NULL);")
sql_lines.append("        ELSE")
sql_lines.append("            EXECUTE format('SELECT COUNT(*)::bigint FROM %s', table_item) INTO row_total;")
sql_lines.append("            INSERT INTO workflow_data_readiness_probe(area, table_name, table_exists, row_count)")
sql_lines.append("            VALUES (table_area, table_item, true, row_total);")
sql_lines.append("        END IF;")
sql_lines.append("    END LOOP;")
sql_lines.append("END $$;")
sql_lines.append("")
sql_lines.append("SELECT *")
sql_lines.append("FROM workflow_data_readiness_probe")
sql_lines.append("ORDER BY area, table_name;")
sql_path.write_text("\n".join(sql_lines))

lines = []
lines.append("# 021 Workflow Data Readiness Report")
lines.append("")
lines.append(f"Generated UTC: `{output['generatedAtUtc']}`")
lines.append("")
lines.append("## Purpose")
lines.append("")
lines.append("This report validates whether each production-critical workflow has backend endpoint signals, route visibility signals, and database table references that can support release-candidate validation.")
lines.append("")
lines.append("## Validation Model")
lines.append("")
lines.append("- `ready_for_live_validation`: expected endpoints are present, table references exist, and related routes are visible in the route inventory.")
lines.append("- `needs_data_confirmation`: route and endpoint signals exist, but live database counts should be confirmed.")
lines.append("- `needs_review`: route or endpoint mapping needs additional review before release-candidate validation.")
lines.append("")
lines.append("## Workflow Readiness Matrix")
lines.append("")
lines.append("| Workflow Area | Status | Endpoints Present | Table Signals | Route Signals |")
lines.append("|---|---|---:|---:|---:|")
for item in results:
    summary = item["summary"]
    lines.append(
        f"| {item['area']} | `{item['status']}` | "
        f"{summary['presentEndpointCount']}/{summary['endpointCount']} | "
        f"{summary['tableSignalCount']} | {summary['routeSignalCount']} |"
    )
lines.append("")
for item in results:
    lines.append(f"## {item['area']}")
    lines.append("")
    lines.append(item["purpose"])
    lines.append("")
    lines.append("### Readiness Checks")
    lines.append("")
    for check in item["readinessChecks"]:
        lines.append(f"- {check}")
    lines.append("")
    lines.append("### Endpoint Signals")
    lines.append("")
    lines.append("| Endpoint | Present |")
    lines.append("|---|---|")
    for endpoint in item["endpointResults"]:
        lines.append(f"| `{endpoint['endpoint']}` | {'Yes' if endpoint['present'] else 'No'} |")
    lines.append("")
    lines.append("### Table Signals")
    lines.append("")
    lines.append("| Table | Static Mentions |")
    lines.append("|---|---:|")
    for table in item["tableResults"]:
        lines.append(f"| `{table['table']}` | {table['mentions']} |")
    lines.append("")
    lines.append("### Route Signals")
    lines.append("")
    if item["routeResults"]:
        lines.append("| Route | Title | Group | Status |")
        lines.append("|---|---|---|---|")
        for route in item["routeResults"]:
            lines.append(f"| `#{route['route']}` | {route['title']} | {route['group'] or 'Ungrouped'} | {route['status'] or 'Missing'} |")
    else:
        lines.append("- No route signals found in the static route inventory.")
    lines.append("")
lines.append("## Live Database Probe")
lines.append("")
lines.append("The generated SQL probe can be run during release-candidate validation:")
lines.append("")
lines.append("- `database/reports/021-workflow-data-readiness-probe.sql`")
lines.append("")
lines.append("The probe is read-only and reports whether expected workflow tables exist and how many records each table contains.")
lines.append("")

report_path.write_text("\n".join(lines))

print(f"Generated {report_path}")
print(f"Generated {json_path}")
print(f"Generated {sql_path}")
print(f"Workflow areas: {len(results)}")

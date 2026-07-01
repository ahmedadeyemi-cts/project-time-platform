#!/usr/bin/env python3
from __future__ import annotations

import json
import re
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path

repo_root = Path(__file__).resolve().parents[1]
app_path = repo_root / "src/frontend/project-time-web/src/App.jsx"
production_ops_path = repo_root / "src/frontend/project-time-web/src/ProductionOperationsPanel.jsx"
report_path = repo_root / "docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.md"
json_path = repo_root / "docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.json"

if not app_path.exists():
    raise SystemExit(f"Missing App.jsx at {app_path}")

app_lines = app_path.read_text().splitlines()
production_text = production_ops_path.read_text() if production_ops_path.exists() else ""

def find_first(pattern: str, block: str) -> str:
    match = re.search(pattern, block, re.S)
    return match.group(1).strip() if match else ""

def normalize_permissions(raw: str) -> list[str]:
    if not raw:
        return []
    return re.findall(r"['\"]([^'\"]+)['\"]", raw)

routes: list[dict[str, object]] = []

for index, line in enumerate(app_lines):
    route_match = re.search(r"\broute:\s*['\"]([^'\"]+)['\"]", line)
    if not route_match:
        continue

    route = route_match.group(1).strip()
    block = "\n".join(app_lines[index:index + 42])

    href = find_first(r"\bhref:\s*['\"]([^'\"]+)['\"]", block)
    title = find_first(r"\btitle:\s*['\"]([^'\"]+)['\"]", block)
    nav_label = find_first(r"\bnavLabel:\s*['\"]([^'\"]+)['\"]", block)
    group = find_first(r"\bgroup:\s*['\"]([^'\"]+)['\"]", block)
    status = find_first(r"\bstatus:\s*['\"]([^'\"]+)['\"]", block)
    description = find_first(r"\bdescription:\s*['\"]([^'\"]+)['\"]", block)
    permission_block = find_first(r"\bpermissions:\s*\[([^\]]*)\]", block)
    permissions = normalize_permissions(permission_block)

    routes.append({
        "route": route,
        "href": href,
        "title": title,
        "navLabel": nav_label,
        "group": group,
        "status": status,
        "description": description,
        "permissions": permissions,
        "sourceLine": index + 1,
    })

route_counts = Counter(item["route"] for item in routes)
href_counts = Counter(item["href"] for item in routes if item["href"])

duplicate_routes = sorted(route for route, count in route_counts.items() if count > 1)
duplicate_hrefs = sorted(href for href, count in href_counts.items() if count > 1)

missing_href = [item for item in routes if not item["href"]]
missing_title = [item for item in routes if not item["title"]]
missing_nav_label = [item for item in routes if not item["navLabel"]]
missing_group = [item for item in routes if not item["group"]]
href_mismatch = [
    item for item in routes
    if item["href"] and item["href"] != f"#{item['route']}"
]

groups: dict[str, list[dict[str, object]]] = defaultdict(list)
for item in routes:
    groups[str(item["group"] or "Ungrouped")].append(item)

production_route_keys = sorted(set(re.findall(r"^\s*([a-zA-Z0-9_-]+):\s*\{", production_text, re.M)))
production_routes_seen = sorted(route for route in production_route_keys if route in {"dashboard", "workflow", "role-admin"})

status_counts = Counter(str(item["status"] or "Missing") for item in routes)

issues = []
if duplicate_routes:
    issues.append(f"Duplicate route keys: {', '.join(duplicate_routes)}")
if duplicate_hrefs:
    issues.append(f"Duplicate hrefs: {', '.join(duplicate_hrefs)}")
if href_mismatch:
    issues.append(f"Href mismatches: {len(href_mismatch)}")
if missing_href:
    issues.append(f"Missing href: {len(missing_href)}")
if missing_title:
    issues.append(f"Missing title: {len(missing_title)}")
if missing_nav_label:
    issues.append(f"Missing navLabel: {len(missing_nav_label)}")
if missing_group:
    issues.append(f"Missing group: {len(missing_group)}")

summary = {
    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    "routeCount": len(routes),
    "groupCount": len(groups),
    "duplicateRoutes": duplicate_routes,
    "duplicateHrefs": duplicate_hrefs,
    "missingHrefCount": len(missing_href),
    "missingTitleCount": len(missing_title),
    "missingNavLabelCount": len(missing_nav_label),
    "missingGroupCount": len(missing_group),
    "hrefMismatchCount": len(href_mismatch),
    "statusCounts": dict(sorted(status_counts.items())),
    "productionOperationsRouteConfigs": production_routes_seen,
    "issues": issues,
    "routes": routes,
}

json_path.write_text(json.dumps(summary, indent=2))

def route_link(route: object) -> str:
    return f"`#{route}`"

lines: list[str] = []
lines.append("# 021 Route Integrity Report")
lines.append("")
lines.append(f"Generated UTC: `{summary['generatedAtUtc']}`")
lines.append("")
lines.append("## Summary")
lines.append("")
lines.append(f"- Route definitions found: **{summary['routeCount']}**")
lines.append(f"- Navigation groups found: **{summary['groupCount']}**")
lines.append(f"- Duplicate routes: **{len(duplicate_routes)}**")
lines.append(f"- Duplicate hrefs: **{len(duplicate_hrefs)}**")
lines.append(f"- Missing href: **{len(missing_href)}**")
lines.append(f"- Missing title: **{len(missing_title)}**")
lines.append(f"- Missing navLabel: **{len(missing_nav_label)}**")
lines.append(f"- Missing group: **{len(missing_group)}**")
lines.append(f"- Href mismatches: **{len(href_mismatch)}**")
lines.append("")
lines.append("## Status Counts")
lines.append("")
for status, count in sorted(status_counts.items()):
    lines.append(f"- {status}: **{count}**")
lines.append("")
lines.append("## Production Operations Route Configs")
lines.append("")
if production_routes_seen:
    for route in production_routes_seen:
        lines.append(f"- `{route}`")
else:
    lines.append("- No production operations route configs found by static scan.")
lines.append("")
lines.append("## Integrity Findings")
lines.append("")
if issues:
    for issue in issues:
        lines.append(f"- {issue}")
else:
    lines.append("- No static route integrity findings detected.")
lines.append("")
lines.append("## Navigation Groups")
lines.append("")
for group_name in sorted(groups):
    lines.append(f"### {group_name}")
    lines.append("")
    lines.append("| Route | Label | Title | Status | Permissions | Source |")
    lines.append("|---|---|---|---|---|---|")
    for item in sorted(groups[group_name], key=lambda row: str(row["route"])):
        perms = ", ".join(item["permissions"]) if item["permissions"] else "None listed"
        lines.append(
            f"| {route_link(item['route'])} | {item['navLabel'] or 'Missing'} | "
            f"{item['title'] or 'Missing'} | {item['status'] or 'Missing'} | "
            f"{perms} | App.jsx:{item['sourceLine']} |"
        )
    lines.append("")
lines.append("## Href Mismatches")
lines.append("")
if href_mismatch:
    lines.append("| Route | Href | Expected | Source |")
    lines.append("|---|---|---|---|")
    for item in href_mismatch:
        lines.append(f"| `{item['route']}` | `{item['href']}` | `#{item['route']}` | App.jsx:{item['sourceLine']} |")
else:
    lines.append("- None.")
lines.append("")
lines.append("## Missing Metadata")
lines.append("")
metadata_rows = [
    ("Missing href", missing_href),
    ("Missing title", missing_title),
    ("Missing navLabel", missing_nav_label),
    ("Missing group", missing_group),
]
for label, rows in metadata_rows:
    lines.append(f"### {label}")
    lines.append("")
    if rows:
        for item in rows:
            lines.append(f"- `{item['route']}` at App.jsx:{item['sourceLine']}")
    else:
        lines.append("- None.")
    lines.append("")

report_path.write_text("\n".join(lines))

print(f"Route definitions found: {len(routes)}")
print(f"Navigation groups found: {len(groups)}")
print(f"Integrity findings: {len(issues)}")
print(f"Report: {report_path}")
print(f"JSON: {json_path}")

if len(routes) == 0:
    raise SystemExit("No route definitions found. Static scan failed.")

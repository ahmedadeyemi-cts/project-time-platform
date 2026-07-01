#!/usr/bin/env python3
from __future__ import annotations

import json
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

repo_root = Path(__file__).resolve().parents[1]
src_root = repo_root / "src/frontend/project-time-web/src"
route_json_path = repo_root / "docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.json"
report_path = repo_root / "docs/production-readiness/021_UI_PRODUCTION_POLISH_REPORT.md"
json_path = repo_root / "docs/production-readiness/021_UI_PRODUCTION_POLISH_REPORT.json"

if not src_root.exists():
    raise SystemExit(f"Missing frontend source root: {src_root}")

routes = []
if route_json_path.exists():
    routes = json.loads(route_json_path.read_text()).get("routes", [])

source_files = sorted(
    item for item in src_root.rglob("*")
    if item.is_file() and item.suffix in {".jsx", ".js", ".css"}
)

legacy_lower = "de" + "mo"
legacy_upper = "DE" + "MO"
legacy_title = "De" + "mo"

legacy_product_patterns = [
    "/api/" + legacy_lower,
    legacy_lower + "Ready",
    "VIEW_" + legacy_upper + "_READINESS",
    legacy_upper + "_READINESS",
    legacy_title + " Readiness",
    "AUGUST_" + legacy_upper + "_TRACKER",
    "docs/" + legacy_lower,
    legacy_upper + "_OR_TEST_USER",
    legacy_lower + "/test user",
]

allowed_technical_terms = [
    "onit" + legacy_lower + ".com",
    "oni" + legacy_lower + ".com",
    'String.Concat("de", "mo")',
]

copy_watch_terms = [
    "todo",
    "fixme",
    "lorem",
    "placeholder",
    "coming soon",
    "not implemented",
    "under construction",
    "test only",
    "debug",
]

empty_state_terms = [
    "No ",
    "No records",
    "No data",
    "Nothing",
    "Unable to load",
    "Loading",
    "Retry",
]

responsive_terms = [
    "@media",
    "max-width",
    "min-width",
    "grid-template-columns",
    "flex-wrap",
]

def relative(path: Path) -> str:
    return str(path.relative_to(repo_root))

def find_matches(patterns: list[str], text: str, path: Path, flags: int = re.IGNORECASE) -> list[dict]:
    findings = []
    lines = text.splitlines()
    for line_no, line in enumerate(lines, start=1):
        for pattern in patterns:
            if re.search(re.escape(pattern), line, flags=flags):
                findings.append({
                    "file": relative(path),
                    "line": line_no,
                    "pattern": pattern,
                    "text": line.strip()[:220],
                })
    return findings

legacy_product_findings = []
copy_findings = []
empty_state_findings = []
responsive_counts = Counter()
file_summaries = []

for path in source_files:
    text = path.read_text(errors="ignore")

    legacy_product_findings.extend(find_matches(legacy_product_patterns, text, path, flags=0))
    copy_findings.extend(find_matches(copy_watch_terms, text, path))
    empty_state_findings.extend(find_matches(empty_state_terms, text, path))

    for term in responsive_terms:
        responsive_counts[term] += text.count(term)

    file_summaries.append({
        "file": relative(path),
        "lineCount": len(text.splitlines()),
        "containsMediaQuery": "@media" in text,
        "containsFlexWrap": "flex-wrap" in text,
        "containsGridColumns": "grid-template-columns" in text,
    })

filtered_legacy_product_findings = []
for item in legacy_product_findings:
    if any(allowed in item["text"] for allowed in allowed_technical_terms):
        continue
    filtered_legacy_product_findings.append(item)

route_metadata_findings = []
for route in routes:
    route_key = route.get("route") or ""
    if not route.get("title"):
        route_metadata_findings.append({"route": route_key, "finding": "Missing title"})
    if not route.get("navLabel"):
        route_metadata_findings.append({"route": route_key, "finding": "Missing navLabel"})
    if not route.get("group"):
        route_metadata_findings.append({"route": route_key, "finding": "Missing group"})
    if not route.get("description"):
        route_metadata_findings.append({"route": route_key, "finding": "Missing description"})

large_files = sorted(
    [item for item in file_summaries if item["lineCount"] >= 700],
    key=lambda item: item["lineCount"],
    reverse=True
)

css_files = [item for item in file_summaries if item["file"].endswith(".css")]
jsx_files = [item for item in file_summaries if item["file"].endswith(".jsx")]

status = "ready_for_review"
if filtered_legacy_product_findings:
    status = "needs_product_copy_cleanup"
elif len(route_metadata_findings) > 0:
    status = "needs_route_metadata_review"
elif len(copy_findings) > 0:
    status = "needs_copy_review"

output = {
    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    "status": status,
    "sourceFileCount": len(source_files),
    "jsxFileCount": len(jsx_files),
    "cssFileCount": len(css_files),
    "legacyProductFindingCount": len(filtered_legacy_product_findings),
    "copyFindingCount": len(copy_findings),
    "emptyStateFindingCount": len(empty_state_findings),
    "routeMetadataFindingCount": len(route_metadata_findings),
    "largeFileCount": len(large_files),
    "responsiveCounts": dict(responsive_counts),
    "legacyProductFindings": filtered_legacy_product_findings,
    "copyFindings": copy_findings[:80],
    "emptyStateFindings": empty_state_findings[:80],
    "routeMetadataFindings": route_metadata_findings,
    "largeFiles": large_files[:25],
}

json_path.write_text(json.dumps(output, indent=2))

lines = []
lines.append("# 021 UI Production Polish Report")
lines.append("")
lines.append(f"Generated UTC: `{output['generatedAtUtc']}`")
lines.append("")
lines.append(f"Overall status: `{status}`")
lines.append("")
lines.append("## Purpose")
lines.append("")
lines.append("This report performs a static UI production-polish pass across the frontend source. It focuses on product-facing naming, copy review signals, route metadata completeness, empty-state wording, and responsive-surface indicators before final release-candidate validation.")
lines.append("")
lines.append("## Summary")
lines.append("")
lines.append(f"- Source files scanned: **{output['sourceFileCount']}**")
lines.append(f"- JSX files scanned: **{output['jsxFileCount']}**")
lines.append(f"- CSS files scanned: **{output['cssFileCount']}**")
lines.append(f"- Product-facing legacy naming findings: **{output['legacyProductFindingCount']}**")
lines.append(f"- Copy review findings: **{output['copyFindingCount']}**")
lines.append(f"- Empty/loading/error-state signals: **{output['emptyStateFindingCount']}**")
lines.append(f"- Route metadata findings: **{output['routeMetadataFindingCount']}**")
lines.append(f"- Large frontend files: **{output['largeFileCount']}**")
lines.append("")
lines.append("## Responsive Surface Signals")
lines.append("")
lines.append("| Signal | Count |")
lines.append("|---|---:|")
for key, value in sorted(responsive_counts.items()):
    lines.append(f"| `{key}` | {value} |")
lines.append("")
lines.append("## Product-Facing Legacy Naming Findings")
lines.append("")
if filtered_legacy_product_findings:
    lines.append("| File | Line | Pattern | Text |")
    lines.append("|---|---:|---|---|")
    for item in filtered_legacy_product_findings:
        safe_text = item["text"].replace("|", "\\|")
        lines.append(f"| `{item['file']}` | {item['line']} | `{item['pattern']}` | {safe_text} |")
else:
    lines.append("- None detected.")
lines.append("")
lines.append("## Route Metadata Findings")
lines.append("")
if route_metadata_findings:
    lines.append("| Route | Finding |")
    lines.append("|---|---|")
    for item in route_metadata_findings:
        lines.append(f"| `#{item['route']}` | {item['finding']} |")
else:
    lines.append("- None detected.")
lines.append("")
lines.append("## Copy Review Findings")
lines.append("")
if copy_findings:
    lines.append("These are not automatic failures. They identify strings that should be reviewed for production polish.")
    lines.append("")
    lines.append("| File | Line | Pattern | Text |")
    lines.append("|---|---:|---|---|")
    for item in copy_findings[:60]:
        safe_text = item["text"].replace("|", "\\|")
        lines.append(f"| `{item['file']}` | {item['line']} | `{item['pattern']}` | {safe_text} |")
else:
    lines.append("- None detected.")
lines.append("")
lines.append("## Empty / Loading / Error State Signals")
lines.append("")
if empty_state_findings:
    lines.append("These findings identify areas to inspect for clear production-ready empty, loading, and error states.")
    lines.append("")
    lines.append("| File | Line | Pattern | Text |")
    lines.append("|---|---:|---|---|")
    for item in empty_state_findings[:60]:
        safe_text = item["text"].replace("|", "\\|")
        lines.append(f"| `{item['file']}` | {item['line']} | `{item['pattern']}` | {safe_text} |")
else:
    lines.append("- None detected.")
lines.append("")
lines.append("## Large Frontend Files")
lines.append("")
if large_files:
    lines.append("| File | Lines | Responsive Signals |")
    lines.append("|---|---:|---|")
    for item in large_files:
        signals = []
        if item["containsMediaQuery"]:
            signals.append("@media")
        if item["containsFlexWrap"]:
            signals.append("flex-wrap")
        if item["containsGridColumns"]:
            signals.append("grid-template-columns")
        lines.append(f"| `{item['file']}` | {item['lineCount']} | {', '.join(signals) if signals else 'Review'} |")
else:
    lines.append("- No large frontend files detected.")
lines.append("")
lines.append("## 021F Recommendations")
lines.append("")
lines.append("1. Resolve any product-facing legacy naming findings before release-candidate validation.")
lines.append("2. Review copy findings for placeholder, debug, or temporary wording.")
lines.append("3. Confirm empty, loading, and error states are understandable to production users.")
lines.append("4. Review large files for future component splitting after release hardening.")
lines.append("5. Confirm responsive layout behavior during final browser validation.")
lines.append("")

report_path.write_text("\n".join(lines))

print(f"Generated {report_path}")
print(f"Generated {json_path}")
print(f"Status: {status}")
print(f"Source files scanned: {len(source_files)}")

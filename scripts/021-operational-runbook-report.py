#!/usr/bin/env python3
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

repo_root = Path(__file__).resolve().parents[1]
report_path = repo_root / "docs/production-readiness/021_OPERATIONAL_RUNBOOK.md"
json_path = repo_root / "docs/production-readiness/021_OPERATIONAL_RUNBOOK.json"
smoke_script = repo_root / "scripts/021-production-readiness-smoke.sh"

services = [
    {
        "name": "projecttime-api.service",
        "purpose": "Backend API service",
        "healthCommand": "systemctl is-active projecttime-api.service",
        "restartCommand": "sudo systemctl restart projecttime-api.service",
        "logCommand": "journalctl -u projecttime-api.service -n 120 --no-pager",
    },
    {
        "name": "projecttime-frontend-public.service",
        "purpose": "Frontend static public service",
        "healthCommand": "systemctl is-active projecttime-frontend-public.service",
        "restartCommand": "sudo systemctl restart projecttime-frontend-public.service",
        "logCommand": "journalctl -u projecttime-frontend-public.service -n 120 --no-pager",
    },
    {
        "name": "nginx.service",
        "purpose": "Reverse proxy and TLS endpoint",
        "healthCommand": "systemctl is-active nginx.service",
        "restartCommand": "sudo systemctl restart nginx.service",
        "logCommand": "journalctl -u nginx.service -n 120 --no-pager",
    },
    {
        "name": "postgresql.service",
        "purpose": "Database service",
        "healthCommand": "systemctl is-active postgresql.service",
        "restartCommand": "sudo systemctl restart postgresql.service",
        "logCommand": "journalctl -u postgresql.service -n 120 --no-pager",
    },
]

endpoints = [
    {"name": "API health", "url": "http://127.0.0.1:5080/health", "expected": "200"},
    {"name": "API version", "url": "http://127.0.0.1:5080/api/version", "expected": "200"},
    {"name": "Production readiness command center protected access", "url": "http://127.0.0.1:5080/api/production/readiness-command-center", "expected": "401"},
    {"name": "Workflow operational readiness protected access", "url": "http://127.0.0.1:5080/api/workflow/operational-readiness", "expected": "401"},
    {"name": "Manager approvals protected access", "url": "http://127.0.0.1:5080/api/manager/approvals", "expected": "401"},
    {"name": "Audit history protected access", "url": "http://127.0.0.1:5080/api/audit/history", "expected": "401"},
    {"name": "Public frontend", "url": "https://projectpulse-test.onenecklab.com", "expected": "200"},
]

artifacts = [
    "docs/production-readiness/021_RELEASE_HARDENING_TRACKER.md",
    "docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.md",
    "docs/production-readiness/021_ROLE_BASED_PRODUCTION_READINESS_RUNBOOKS.md",
    "docs/production-readiness/021_WORKFLOW_DATA_READINESS_REPORT.md",
    "docs/production-readiness/021_UI_PRODUCTION_POLISH_REPORT.md",
    "docs/production-readiness/021_OPERATIONAL_RUNBOOK.md",
]

backup_locations = [
    "/opt/project-time-platform/backups",
    "/tmp/projectpulse-*",
]

deployment_paths = [
    "/opt/project-time-platform/app/project-time-platform",
    "/opt/project-time-platform/runtime/backend",
    "/opt/project-time-platform/runtime/frontend",
]

rollback_steps = [
    "Identify the last known-good backup under `/opt/project-time-platform/backups`.",
    "Stop the frontend and API services.",
    "Restore the backend published output from the selected backup.",
    "Restore the frontend published output from the selected backup.",
    "Restart API, frontend, and nginx services.",
    "Run the production-readiness smoke script.",
    "Capture service status, endpoint status, and git revision evidence."
]

validation_sequence = [
    "Confirm the working branch is clean before deployment.",
    "Build backend and frontend artifacts.",
    "Create a timestamped backup before replacing runtime files.",
    "Deploy backend and frontend build outputs.",
    "Restart services in dependency-safe order.",
    "Run service status checks.",
    "Run endpoint smoke checks.",
    "Review production-readiness reports.",
    "Capture logs and final evidence before closing the release-candidate validation."
]

output = {
    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    "services": services,
    "endpoints": endpoints,
    "artifacts": artifacts,
    "backupLocations": backup_locations,
    "deploymentPaths": deployment_paths,
    "rollbackSteps": rollback_steps,
    "validationSequence": validation_sequence,
}

json_path.write_text(json.dumps(output, indent=2))

smoke_lines = []
smoke_lines.append("#!/usr/bin/env bash")
smoke_lines.append("set -Eeuo pipefail")
smoke_lines.append("")
smoke_lines.append('echo "============================================================"')
smoke_lines.append('echo "021 Production Readiness Smoke"')
smoke_lines.append('echo "============================================================"')
smoke_lines.append("date -u")
smoke_lines.append("echo")
smoke_lines.append("")
smoke_lines.append('OVERALL_STATUS=0')
smoke_lines.append("")
smoke_lines.append('check_service() {')
smoke_lines.append('  local service_name="$1"')
smoke_lines.append('  echo "Service: ${service_name}"')
smoke_lines.append('  if systemctl is-active --quiet "${service_name}"; then')
smoke_lines.append('    echo "  OK: active"')
smoke_lines.append('  else')
smoke_lines.append('    echo "  FAIL: not active"')
smoke_lines.append('    OVERALL_STATUS=1')
smoke_lines.append('  fi')
smoke_lines.append('}')
smoke_lines.append("")
smoke_lines.append('check_endpoint() {')
smoke_lines.append('  local name="$1"')
smoke_lines.append('  local url="$2"')
smoke_lines.append('  local expected="$3"')
smoke_lines.append('  local code')
smoke_lines.append('  code="$(curl -k -s -o /dev/null -w "%{http_code}" "${url}" || true)"')
smoke_lines.append('  echo "Endpoint: ${name}"')
smoke_lines.append('  echo "  URL: ${url}"')
smoke_lines.append('  echo "  Expected: ${expected}; Actual: ${code}"')
smoke_lines.append('  if [ "${code}" != "${expected}" ]; then')
smoke_lines.append('    OVERALL_STATUS=1')
smoke_lines.append('  fi')
smoke_lines.append('}')
smoke_lines.append("")
smoke_lines.append('echo "============================================================"')
smoke_lines.append('echo "Service checks"')
smoke_lines.append('echo "============================================================"')
for service in services:
    smoke_lines.append(f'check_service "{service["name"]}"')
smoke_lines.append("")
smoke_lines.append('echo "============================================================"')
smoke_lines.append('echo "Endpoint checks"')
smoke_lines.append('echo "============================================================"')
for endpoint in endpoints:
    smoke_lines.append(f'check_endpoint "{endpoint["name"]}" "{endpoint["url"]}" "{endpoint["expected"]}"')
smoke_lines.append("")
smoke_lines.append('echo "============================================================"')
smoke_lines.append('echo "Git revision"')
smoke_lines.append('echo "============================================================"')
smoke_lines.append("git -C /opt/project-time-platform/app/project-time-platform branch --show-current || true")
smoke_lines.append("git -C /opt/project-time-platform/app/project-time-platform log --oneline -5 || true")
smoke_lines.append("")
smoke_lines.append('echo "============================================================"')
smoke_lines.append('echo "Final smoke status"')
smoke_lines.append('echo "============================================================"')
smoke_lines.append('if [ "${OVERALL_STATUS}" = "0" ]; then')
smoke_lines.append('  echo "PASS: production readiness smoke checks passed."')
smoke_lines.append("else")
smoke_lines.append('  echo "FAIL: one or more production readiness smoke checks failed."')
smoke_lines.append("fi")
smoke_lines.append("")
smoke_lines.append('exit "${OVERALL_STATUS}"')
smoke_script.write_text("\n".join(smoke_lines) + "\n")

lines = []
lines.append("# 021 Operational Runbook")
lines.append("")
lines.append(f"Generated UTC: `{output['generatedAtUtc']}`")
lines.append("")
lines.append("## Purpose")
lines.append("")
lines.append("This runbook defines the operational process for release hardening, production readiness validation, backup, deployment, rollback, smoke testing, and evidence capture for ProjectPulse / ChangePoint.")
lines.append("")
lines.append("## Runtime Services")
lines.append("")
lines.append("| Service | Purpose | Health Check | Restart | Logs |")
lines.append("|---|---|---|---|---|")
for service in services:
    lines.append(f"| `{service['name']}` | {service['purpose']} | `{service['healthCommand']}` | `{service['restartCommand']}` | `{service['logCommand']}` |")
lines.append("")
lines.append("## Deployment Paths")
lines.append("")
for path in deployment_paths:
    lines.append(f"- `{path}`")
lines.append("")
lines.append("## Backup Locations")
lines.append("")
for path in backup_locations:
    lines.append(f"- `{path}`")
lines.append("")
lines.append("## Production Readiness Artifacts")
lines.append("")
for artifact in artifacts:
    lines.append(f"- `{artifact}`")
lines.append("")
lines.append("## Standard Validation Sequence")
lines.append("")
for index, step in enumerate(validation_sequence, start=1):
    lines.append(f"{index}. {step}")
lines.append("")
lines.append("## Endpoint Smoke Matrix")
lines.append("")
lines.append("| Endpoint | URL | Expected HTTP Status |")
lines.append("|---|---|---:|")
for endpoint in endpoints:
    lines.append(f"| {endpoint['name']} | `{endpoint['url']}` | `{endpoint['expected']}` |")
lines.append("")
lines.append("## Rollback Sequence")
lines.append("")
for index, step in enumerate(rollback_steps, start=1):
    lines.append(f"{index}. {step}")
lines.append("")
lines.append("## Evidence Capture Checklist")
lines.append("")
lines.append("- Current branch and commit hash.")
lines.append("- Backend build output.")
lines.append("- Frontend build output.")
lines.append("- Service active status for API, frontend, nginx, and PostgreSQL.")
lines.append("- Endpoint smoke output.")
lines.append("- Backup folder path.")
lines.append("- Any release-candidate validation findings.")
lines.append("- Rollback decision, if rollback is required.")
lines.append("")
lines.append("## Smoke Script")
lines.append("")
lines.append("Use the generated smoke script during production readiness validation:")
lines.append("")
lines.append("```bash")
lines.append("scripts/021-production-readiness-smoke.sh")
lines.append("```")
lines.append("")
lines.append("The script checks service status, endpoint status, and git revision evidence.")
lines.append("")

report_path.write_text("\n".join(lines))

print(f"Generated {report_path}")
print(f"Generated {json_path}")
print(f"Generated {smoke_script}")

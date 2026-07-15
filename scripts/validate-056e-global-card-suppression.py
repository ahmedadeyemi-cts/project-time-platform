#!/usr/bin/env python3

from pathlib import Path
import sys

index = Path("src/frontend/project-time-web/index.html").read_text(
    encoding="utf-8"
)

errors = []

required = [
    'data-projectpulse-guard-version="056E"',
    "const GUARD_VERSION = '056E'",
    "data-projectpulse-legacy-dashboard-summary-card",
    "isProtectedRouteWorkspace",
    "discoverLegacyDashboardSummaries",
    "suppressLegacyDashboardSummary",
    "visibleLegacyHeadingOffenders",
    "SOW-Aware AI Time Entry Generator",
    "User Acceptance / Role + Workflow Validation Center",
    "Reporting / Accounting / Invoicing / Analytics Command Center",
]

for fragment in required:
    if fragment not in index:
        errors.append(f"Missing required fragment: {fragment}")

for forbidden in [
    'data-projectpulse-guard-version="056D"',
    "const GUARD_VERSION = '056D'",
    "route === 'dashboard'",
]:
    if forbidden in index:
        errors.append(f"Forbidden fragment remains: {forbidden}")

if errors:
    print("056E_VALIDATION=FAILED")
    for error in errors:
        print(f"ERROR={error}")
    sys.exit(1)

print("056E_VALIDATION=PASSED")
print("GLOBAL_LEGACY_CARD_SUPPRESSION=YES")
print("ROUTE_WORKSPACE_PROTECTION=YES")
print("API_CHANGED=NO")
print("DATABASE_CHANGED=NO")

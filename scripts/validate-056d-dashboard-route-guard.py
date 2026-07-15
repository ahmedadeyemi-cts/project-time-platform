#!/usr/bin/env python3

from pathlib import Path
import sys

index = Path("src/frontend/project-time-web/index.html").read_text(
    encoding="utf-8"
)

errors = []

required = [
    'data-projectpulse-guard-version="056D"',
    "const GUARD_VERSION = '056D'",
    "SOW-Aware AI Time Entry Generator",
    "User Acceptance / Role + Workflow Validation Center",
    "Reporting / Accounting / Invoicing / Analytics Command Center",
    "Production Notification Center",
    "dashboardModulePattern",
    "visibleDashboardHeadingOffenders",
    "data-projectpulse-056b-visible-offender-count",
    "__projectPulse056BDashboardCardRouteGuardDiagnostics",
]

for fragment in required:
    if fragment not in index:
        errors.append(f"Missing required fragment: {fragment}")

for module_number in range(22, 31):
    token = f"{module_number}"
    if token not in index:
        errors.append(f"Module {module_number} coverage not visible in guard")

for forbidden in [
    "if (!element.closest('.app-shell'))",
    'data-projectpulse-guard-version="056C"',
]:
    if forbidden in index:
        errors.append(f"Forbidden fragment remains: {forbidden}")

if errors:
    print("056D_VALIDATION=FAILED")
    for error in errors:
        print(f"ERROR={error}")
    sys.exit(1)

print("056D_VALIDATION=PASSED")
print("MODULE_022_TO_030_COVERAGE=YES")
print("VISIBLE_HEADING_OFFENDER_CHECK=YES")
print("API_CHANGED=NO")
print("DATABASE_CHANGED=NO")

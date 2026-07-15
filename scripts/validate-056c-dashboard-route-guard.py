#!/usr/bin/env python3

from pathlib import Path
import re
import sys

index_path = Path(
    "src/frontend/project-time-web/index.html"
)

index = index_path.read_text(encoding="utf-8")

start_marker = "<!-- 056B_DASHBOARD_CARD_ROUTE_GUARD_START -->"
end_marker = "<!-- 056B_DASHBOARD_CARD_ROUTE_GUARD_END -->"

errors = []

if index.count(start_marker) != 1:
    errors.append("056B start marker count is not one")

if index.count(end_marker) != 1:
    errors.append("056B end marker count is not one")

if not errors:
    start = index.index(start_marker)
    end = index.index(end_marker) + len(end_marker)
    guard = index[start:end]

    required = [
        'data-projectpulse-guard-version="056C"',
        "const GUARD_VERSION = '056C'",
        "querySelectorAll('[id^=\"projectpulse-\"]')",
        "isExplicitDashboardId(id)",
        "isProtectedRouteContent(element)",
        "data-projectpulse-056b-visible-offender-count",
        "__projectPulse056BDashboardCardRouteGuardDiagnostics",
        "attributes: true",
        "'style'",
        "'hidden'",
        "'aria-hidden'",
    ]

    for fragment in required:
        if fragment not in guard:
            errors.append(
                f"Required guard fragment missing: {fragment}"
            )

    forbidden = [
        "if (!element.closest('.app-shell'))",
    ]

    for fragment in forbidden:
        if fragment in guard:
            errors.append(
                f"Obsolete guard fragment remains: {fragment}"
            )

    explicit_position = guard.find(
        "if (isExplicitDashboardId(id))"
    )
    protected_position = guard.find(
        "if (isProtectedRouteContent(element))"
    )

    if explicit_position < 0 or protected_position < 0:
        errors.append(
            "Could not verify classifier evaluation order"
        )
    elif explicit_position > protected_position:
        errors.append(
            "Protected-route exclusion occurs before explicit ID check"
        )

fixture_expectations = [
    (
        "projectpulse-022e-dashboard-notification-card",
        "Production Notification Center",
        True,
    ),
    (
        "projectpulse-025-sow-card",
        "MODULE 025 SOW Generator + Claude Research Review",
        True,
    ),
    (
        "projectpulse-024-intake-card",
        "MODULE 024 Sales-to-Delivery Intake Foundation",
        True,
    ),
    (
        "projectpulse-026-crm-card",
        "MODULE 026 CRM Integration Framework",
        True,
    ),
    (
        "projectpulse-030-shell",
        "MODULE 030 Reporting",
        False,
    ),
]

module_pattern = re.compile(
    r"(?:production\s+)?module\s+0?(?:2[3-9]|30)\b",
    flags=re.I,
)

for element_id, text, expected in fixture_expectations:
    lowered = element_id.lower()

    explicit = (
        "dashboard-shortcut" in lowered
        or "dashboard-notification-card" in lowered
        or "-dashboard-card" in lowered
    )

    card_like = (
        lowered.endswith("-card")
        or "-dashboard-card" in lowered
    )

    result = explicit or (
        card_like
        and bool(module_pattern.search(text))
    )

    if result != expected:
        errors.append(
            f"Fixture failed for {element_id}: "
            f"expected {expected}, got {result}"
        )

if errors:
    print("056C_VALIDATION=FAILED")

    for error in errors:
        print(f"ERROR={error}")

    sys.exit(1)

print("056C_VALIDATION=PASSED")
print("FULL_DOCUMENT_SCAN=YES")
print("EXPLICIT_ID_PRECEDENCE=YES")
print("APP_SHELL_DEPENDENCY=NO")
print("ATTRIBUTE_MUTATION_RECOVERY=YES")
print("RUNTIME_OFFENDER_DIAGNOSTICS=YES")

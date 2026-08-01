#!/usr/bin/env python3
"""Create verified ordinary snapshots for PR #393's two workflow repairs."""

from pathlib import Path

REPAIRS = (
    (
        Path('.github/workflows/celar-ai-runtime-rebrand-ci.yml'),
        'CELAR_AI_RUNTIME_REBRAND_VALIDATION_MODE',
        'CELAR_AI_DEPENDENCY_ONLY_FRONTEND_BUILD=PASSED',
    ),
    (
        Path('.github/workflows/pulse-ai-system-intelligence-ci.yml'),
        'PULSE_AI_SYSTEM_INTELLIGENCE_VALIDATION_MODE',
        'PULSE_AI_SYSTEM_INTELLIGENCE_DEPENDENCY_ONLY_FRONTEND_BUILD=PASSED',
    ),
)

for path, mode_variable, success_marker in REPAIRS:
    source = path.read_text(encoding='utf-8')
    old = (
        "          if [[ \"$STATUS\" != '0' ]]; then exit \"$STATUS\"; fi\n"
        "          if [[ \"${PROJECTPULSE_DEPENDENCY_ONLY:-false}\" == 'true' ]]; then\n"
        "            test -s dist/index.html"
    )
    new = (
        "          if [[ \"$STATUS\" != '0' ]]; then exit \"$STATUS\"; fi\n"
        "          if [[ \"${PROJECTPULSE_DEPENDENCY_ONLY:-false}\" == 'true' "
        f"|| \"${{{mode_variable}:-}}\" == 'WORKFLOW_CONVERGENCE' ]]; then\n"
        "            test -s dist/index.html"
    )

    count = source.count(old)
    if count != 1:
        raise SystemExit(
            f'{path}: expected one compiled-output applicability target, found {count}'
        )
    if success_marker not in source:
        raise SystemExit(
            f'{path}: required successful dependency-only build marker is missing'
        )
    if f'{mode_variable}=WORKFLOW_CONVERGENCE' not in source:
        raise SystemExit(f'{path}: workflow-convergence mode assignment is missing')

    updated = source.replace(old, new, 1)
    if updated.count("test -s dist/index.html") != source.count("test -s dist/index.html"):
        raise SystemExit(f'{path}: compiled-output validation count changed unexpectedly')
    if updated.count("PROJECTPULSE_DEPENDENCY_ONLY") != source.count(
        "PROJECTPULSE_DEPENDENCY_ONLY"
    ):
        raise SystemExit(f'{path}: dependency-only contract count changed unexpectedly')

    path.write_text(updated, encoding='utf-8')
    print(f'WORKFLOW_CONVERGENCE_BLOB_READY={path}')

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
    old = "          if [[ \"${PROJECTPULSE_DEPENDENCY_ONLY:-false}\" == 'true' ]]; then"
    new = (
        "          if [[ \"${PROJECTPULSE_DEPENDENCY_ONLY:-false}\" == 'true' "
        f"|| \"${{{mode_variable}:-}}\" == 'WORKFLOW_CONVERGENCE' ]]; then"
    )

    count = source.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one applicability target, found {count}')
    if success_marker not in source:
        raise SystemExit(f'{path}: required successful dependency-only build marker is missing')
    if f'{mode_variable}=WORKFLOW_CONVERGENCE' not in source:
        raise SystemExit(f'{path}: workflow-convergence mode assignment is missing')

    path.write_text(source.replace(old, new, 1), encoding='utf-8')
    print(f'WORKFLOW_CONVERGENCE_BLOB_READY={path}')

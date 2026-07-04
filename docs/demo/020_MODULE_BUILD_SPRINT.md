# 020 Module Build Sprint

## Purpose

This sprint builds the next set of ProjectPulse modules from the merged 019M foundation. The build approach is intentionally different from the 019M validation sequence: each module will be built and committed, but full end-to-end browser validation will be deferred until the module group is complete.

## Build Strategy

1. Build each module in the shared branch `feature/020-module-build-sprint`.
2. Commit each module independently.
3. Run lightweight compile/build checks when source files change.
4. Avoid repeated full browser validation after every module.
5. Run one consolidated end-to-end validation after all planned modules are complete.

## Module Sequence

| Sprint ID | Module | Goal |
| --- | --- | --- |
| 020A | Module Shell / Navigation Cleanup | Normalize module grouping, navigation order, and build documentation. |
| 020B | Customer Directory | Complete customer/account record management flow. |
| 020C | Project Intake Full Workflow | Complete intake request lifecycle from customer selection through triage readiness. |
| 020D | Resource Assignment Full Workflow | Complete engineering resource request, capacity, assignment, and promotion workflow. |
| 020E | Approval Workflow | Complete approval inbox, workflow actions, and status progression. |
| 020F | Export Package Workflow | Complete export package generation, detail, download, and evidence readiness. |
| 020G | Audit Evidence Workflow | Complete audit evidence visibility across approval, export, lock, and admin workflows. |
| 020H | Production Operations / Admin Controls | Complete admin-facing operational controls and readiness guardrails. |
| 020I | Reporting / Dashboard Consolidation | Consolidate dashboard, status, and module visibility reporting. |
| 020J | Final End-to-End Validation | Validate all modules together after the build sprint is complete. |

## Validation Rule

Do not repeat full-stack browser validation after every module unless a module introduces a risky runtime change. Prefer compile checks and targeted source review during the build phase, then one final browser validation at 020J.

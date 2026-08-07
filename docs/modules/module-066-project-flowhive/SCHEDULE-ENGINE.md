# Module 066C — Schedule Engine Contract

## Preview calculations

The side-effect-free engine calculates task start offsets using a directed
acyclic graph and these precedence inequalities:

- FS: successor start ≥ predecessor start + predecessor duration + lag
- SS: successor start ≥ predecessor start + lag
- FF: successor start ≥ predecessor start + predecessor duration − successor duration + lag
- SF: successor start ≥ predecessor start + 1 − successor duration + lag

It then performs a reverse pass for latest starts. Total float is latest start
minus earliest start. Free float is the smallest unused successor constraint.
Tasks with zero total float are reported as critical.

Milestones accept duration 0 but consume one schedule index so dates remain
representable. Positive lag delays; negative lag is lead. Cycles, self-edges,
duplicates, missing tasks, and out-of-range values are rejected.

## Phase summary rows and target window

AI Planner phase summaries are structural WBS rows, not executable tasks. They
must be root rows with duration zero, cannot be milestones, cannot own direct
dependencies or assignments, and must have at least one executable descendant.
They are excluded from topology and critical-path calculations. After child
tasks are scheduled, each summary rolls up its descendant start, finish,
working-day duration, progress, planned effort, status, and critical flag.

When `projectEndDate` is supplied, the engine treats it as the PM-selected
target boundary. A schedule that finishes after that date fails with
`project_end_exceeded`; FlowHive does not conceal the conflict by moving dates
or shortening reviewed tasks during schedule calculation.

## Calendar boundary

The current engine skips Saturday and Sunday only and labels every result
`weekday_preview_module_057_not_applied`. It must not be described as the live
schedule until Module 057 supplies company holidays, resource calendars,
timezone policy, and working-time exceptions.

## Required test fixtures before activation

- linear FS chain;
- parallel paths and critical-path selection;
- SS, FF, and SF examples;
- positive lag and negative lead;
- milestone predecessor/successor;
- cycle and self-edge rejection;
- parent hierarchy validation;
- phase-summary validation and rollup;
- PM target-end enforcement;
- weekend project start normalization;
- Module 057 holiday crossing after integration;
- maximum-size performance and cancellation.

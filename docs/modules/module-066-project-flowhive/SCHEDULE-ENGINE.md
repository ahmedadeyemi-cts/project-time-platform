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
- weekend project start normalization;
- Module 057 holiday crossing after integration;
- maximum-size performance and cancellation.

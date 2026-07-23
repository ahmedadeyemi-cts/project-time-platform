# Module 001 Phase 0 and integration UAT plan

## Phase 0 checks

- Pure rounding utility passes all approved boundary examples.
- Timer prototype is unregistered and calls no invented production API.
- Mobile preference is presentation-only.
- Preparation package contains no migration, RBAC, Module 002, App.jsx, package, CI, or deployment changes.

## Integration regression

Weekly Grid, Daily Focus, Quick Entry List, week navigation, draft saving, normal/afterhours values, totals, activity categories, authentication, View-As, and Module 059 remain unchanged.

## Work Queue and Calendar

Assigned tasks appear; unassigned tasks do not. Add to Timesheet stores durable identifiers. Start Timer preselects the same assignment. Calendar entries display task association and incomplete-description state. Changes synchronize across all views.

## Timer

Timer survives refresh, logout/login, browser closure, and device switching. A second timer is blocked. Raw and rounded durations are retained. 4:00:01 rounds to 4.25 hours. Timer stops at 12 hours. Missing description blocks submission. Another user and View-As cannot mutate the timer.

## Submission

Drafts may remain incomplete. Submission is blocked for a running timer, missing descriptions, or missing task associations. Valid submission enters Module 002 and submitted records cannot be silently edited.

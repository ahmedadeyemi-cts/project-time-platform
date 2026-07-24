# Module 001 Timesheet timer and submission requirements

## Phase 0 status

This is a source-only preparation contract. It is deliberately unregistered and does not modify `App.jsx`, package wiring, database migrations, RBAC, Module 002, CI, or deployment files.

## Preserved baseline

Weekly Grid, Daily Focus, Quick Entry List, normal/afterhours entry, draft saving, weekly totals, activity categories, week navigation, Module 059, authentication, View-As read-only behavior, and current submission behavior must remain intact.

## Approved additions

- Rename the user-facing Module 001 title from **Time Entry** to **Timesheet** after integration.
- Associate My Work Queue and Calendar / Timeline entries with durable customer, project, task/work-item, assignment, and activity identifiers.
- Add a sixth view named **Start / Stop Timer**.
- Add an optional **Mobile mode** presentation preference.
- Keep one canonical weekly draft shared by all views.

## Timer rules

- One running timer per authenticated user, enforced server-side.
- Official timestamps stored in UTC and calculated by the backend.
- Browser refresh, logout/login, closed browser, and device switching must not lose the timer.
- Timer auto-stops at 12 hours, even when the browser is closed.
- Raw elapsed seconds and rounded minutes are both retained.
- Timer-generated duration rounds upward to the next quarter hour.
- Stopping a timer creates or updates a draft; it does not submit the week.
- A positive-hour entry requires a meaningful description before submission.
- View-As cannot start, stop, discard, or edit a timer.
- Engineering users may access only their own timer and assignment-scoped work.

## Submission contract

Integration must inspect current Save Week semantics before changing labels. A valid submission must save the draft, confirm no running timer, validate descriptions and task associations, display a review summary, require confirmation, set the week to Submitted, and route through Module 002.

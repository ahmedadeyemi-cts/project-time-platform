# Time Compliance & Notification Center

## Purpose

The Time Compliance & Notification Center helps ProjectPulse administrators preview missing-time reminders before anything is sent.

## Demo Scope

- Weekly engineer reminder default: Monday 6:00 AM Central
- Weekly escalation default: Monday 8:00 AM Central
- Dry-run mode only
- Preview missing submissions before any real send
- Show manager and Project Team Coordinator copy status
- Show holiday reminder windows for weekday company holidays
- Show notification history from dry-run queue records
- Provide month-end reminder configuration shell

## Required Permission

`VIEW_TIME_COMPLIANCE`

Future write actions should require:

`MANAGE_TIME_COMPLIANCE_NOTIFICATIONS`

## Audit Expectations

Every dry-run queue action should create a traceable notification event and an audit placeholder. Real-send behavior must not be enabled until SMTP/provider integration, role enforcement, and approval controls are complete.

## Known Demo Gaps

- If reporting relationships are missing, the preview falls back to `app_users.manager_email`.
- If no Project Team Coordinator record exists, the preview displays a configuration gap instead of inventing an email recipient.
- Real email sending is intentionally disabled for the August demo foundation.

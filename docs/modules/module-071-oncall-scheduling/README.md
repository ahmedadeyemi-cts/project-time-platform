# Module 071 — On-Call Scheduling

Module 071 is the governed Pulse source package for the US Signal Professional Services on-call schedule.

## Confirmed behavior

- The direct public schedule is `https://oncall.onenecklab.com/` and requires no authentication.
- The engineer payment action uses the approved Microsoft form and is labeled **OnCall Pay Form**.
- The public schedule exposes on-call assignment details only; it never exposes OneAssist PINs.
- Every authenticated Pulse user can view the schedule, roster, and schedule history.
- `MANAGER`, `PROJECT_TEAM_COORDINATOR`, canonical `ENGINEERING_LEAD`, legacy `ENGINEERING_TEAM_LEAD`, `SUPER_ADMINISTRATOR`, and legacy `ADMINISTRATOR` can change assignments, dates, the roster, generated rotations, and history.
- View-As is read-only and never transfers management authority.
- Coverage starts Friday at 4:00 PM America/Chicago and ends the following Friday at 7:00 AM America/Chicago.
- Engineer selection uses Module 062 stable `app_users.user_id` values.
- Public GET APIs expose the current assignment and schedule for reminder and routing consumers.
- Schedule, roster, acknowledgement, and history persistence remains in Pulse PostgreSQL under migration 031.

## Governed links

- Public schedule: `https://oncall.onenecklab.com/`
- OnCall Pay Form: `https://forms.cloud.microsoft/Pages/ResponsePage.aspx?id=2kFZU3Lai0qDeJg6VL7DQtvfUo2dqAlEkjfnG3izqQFUQ0NXTlQ5TEtERzE0RzNHN0tNMjJNWThWRSQlQCN0PWcu`
- Authenticated OneAssist directory: Module 072 inside Pulse

## Reminder contract

On-call reminders use the direct public schedule URL. The page must remain usable without sign-in and must not request or render OneAssist customer or PIN data.

## Security and release boundary

- OneAssist PINs require an authenticated Pulse session.
- Schedule and roster mutations are server-authorized from the actual session.
- No new database migration, secret, infrastructure, deployment, or environment change is introduced by this follow-up.
- This source remains draft until separately authorized for merge and deployment.

# 027 Signed SOW Handoff + Assignment Trigger

## Status
Applied as complete Module 027 pending validation and commit.

## 027A Signed SOW/GSD Handoff Package
Creates the signed handoff package using intake, CRM, SOW, Sales, Solution Architect, PTC, and project estimate context.

## 027B PTC + Executive Notification Preview
Adds notification preview for PTC and Executive stakeholders when signed SOW/GSD readiness is confirmed.

## 027C PM/Engineer Assignment Trigger Preview
Adds PTC assignment preview for Project Manager, Engineering Team, Primary Engineer, Secondary Engineer, Backup Engineer, target start date, and assignment notes.

## 027D Shared Email Provider + Recipient Safety Readiness
Positions notification delivery behind the existing shared email provider and recipient safety gate. No direct email secrets are stored in the repository.

## 027E Assignment Audit + Role Visibility
Adds audit model and role visibility model for Sales, Solution Architect, PTC, Executive, PM, and Engineer views.

## 027F Closeout
Adds readiness checklist and module closeout evidence.

## Database Foundation
Adds:

- `signed_sow_handoff_packages`
- `signed_sow_handoff_artifacts`
- `signed_sow_handoff_notification_templates`
- `signed_sow_handoff_notification_events`
- `signed_sow_assignment_previews`
- `signed_sow_assignment_events`
- `signed_sow_handoff_readiness_reviews`
- `signed_sow_handoff_visibility_rules`

## Workflow Placement
- Module 024 validates signed SOW/GSD intake readiness.
- Module 025 supplies SOW review/generation context.
- Module 026 supplies CRM-originated context.
- Module 027 prepares PTC/Executive notification and PM/Engineer assignment trigger.
- Module 028 will use assigned signed SOW/GSD context for SOW-aware AI time entry.

# 024 Sales-to-Delivery Intake Foundation

## Status
Applied as complete Module 024 foundation pending validation and commit.

## 024A Intake Package Foundation
Creates the Sales-to-Delivery Intake Center and standalone `#sales-intake` route.

## 024B Required SOW/GSD Artifact Gate
Signed SOW and GSD are required artifacts before intake can be marked ready for PTC assignment. Artifacts are positioned to reuse the existing Project Hours / SOW-GSD / Engineer Allocation document area.

## 024C Intake Readiness + Assignment Preview
Adds intake readiness checklist, handoff email preview, PTC assignment readiness preview, and role visibility.

## 024D Closeout
Adds module closeout evidence and workflow placement for Modules 025, 026, 027, and 028.

## Database Foundation
Adds:

- `sales_delivery_intake_packages`
- `sales_delivery_intake_artifacts`
- `sales_delivery_intake_readiness_reviews`
- `sales_delivery_intake_assignment_previews`
- `sales_delivery_intake_activity_events`
- `sales_delivery_intake_visibility_rules`

## Workflow Placement
- Module 026 CRM Integration can seed intake.
- Module 025 SOW Generator can feed SOW context.
- Module 027 uses validated intake for signed SOW handoff and PM/Engineer assignment trigger.
- Module 028 can use signed SOW/GSD scope for SOW-aware time entry.

# Consolidated enterprise validation release — 2026-08-02

## Candidate identity

- Release branch: `release/consolidated-enterprise-validation-20260802`
- Base main commit: `2dc7c8604dd0f3775986564563b2eed92104166f`
- Analytics regression correction: `a4fdd44faf9ecaaea8d62d507fda3bd0dd1aec6a`
- Celar AI source head: `da3d2efd6e58d8906ad54346a63ddfe1590d213d`
- Guided Module 040 source head: `52f37be19500afd573ccee042b5c826552f1102c`
- Role-access source head: `6d2b5430d9c2fe634089bb37585e8f48e4a5b886`
- Module 065 notification source head: `00e21019bf525b627ed5eb292adacba1e3a9037b`

## Included work

The candidate packages the following changes for one Test release validation cycle:

1. PR #424, already merged into the base, providing the enterprise Analytics Center and migration 060.
2. PR #430, already merged into the base, correcting Celar AI regression validation.
3. PR #432 source, correcting Analytics owned-source versus regression validation.
4. PR #418 source, providing the Celar AI enterprise platform interface and migration 061.
5. PR #425 source, providing the guided Module 040 project-closeout correction.
6. PR #429 source, providing Super Administrator, Project Management, Billing, View-As, Module 026, Module 065, and Module 069 access corrections through migrations 062 and 063.
7. Module 065 enterprise notification orchestration and migration 064.

## Reviewed migration order

Apply only through the governed migration workflow, in numeric order, and only when pending:

1. `060_analytics_center_enterprise_experience.sql`
2. `061_celar_ai_capability_routing.sql`
3. `062_super_administrator_permanent_full_control.sql`
4. `063_project_management_billing_role_access_repair.sql`
5. `064_module_065_enterprise_notification_orchestration.sql`

The source integration commit does not apply any connected migration.

## Required validation before Test deployment

- Repository security posture and source-isolation checks.
- Exact-head .NET Release build and complete frontend production build.
- Focused migration apply, idempotence, rollback, and reapply tests for migrations 060–064.
- Celar AI enterprise, capability-routing, contextual-chat, and Module 064 boundary validation.
- Analytics Center base and enterprise compiled-bundle validation.
- Guided Module 040 closeout and Module 055C handoff validation.
- Super Administrator, Project Management, Billing, Accounting, View-As, Module 026, Module 065, and Module 069 role-matrix validation.
- Module 065 notification recipient, template, schedule, escalation, immutable-evidence, and migration validation.
- Production web-container build.

## Authenticated Test UAT

After an exact-SHA Test deployment, validate at minimum:

- actual Super Administrator full control while View-As remains read-only;
- Project Management workspace and project-scoped approvals;
- Billing workspace without Module 008 Audit History;
- Module 026 and Module 065 configuration access;
- Celar AI global chat, Module 011, and Module 064 provider-boundary experience;
- Analytics Center filters, branded PDF/Excel exports, and scheduling;
- Module 040 guided closeout from Module 055C;
- Timesheet submission, approval, rejection, aging, utilization, expense, closeout, and secret-expiration notification event creation and routing.

## Deployment boundary

This release manifest and its integration commits do not deploy Test or Production, apply database migrations, send email, change provider secrets, rotate credentials, or modify Azure resources. Production remains explicitly excluded until Test validation is complete and separately authorized.

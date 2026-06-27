# ProjectPulse August Demo Tracker

## Completed Demo Foundation

### 019M-O Time Compliance & Notification Center
- Dry-run Time Compliance page created.
- Session-authenticated API calls fixed.
- Route isolation fixed.
- Weekly reminder default shown as Monday 6:00 AM Central.
- Weekly escalation default shown as Monday 8:00 AM Central.
- Holiday reminder windows shown for 7-day and 1-day reminders.
- Manager and Project Team Coordinator copy visibility working.
- Dry-run notification history working.
- Real send intentionally locked.

## Come Back / Production Hardening Items

### Time Compliance
- Replace UI-only month-end selector with persisted settings.
- Add editable reminder templates backed by database.
- Add real notification provider integration only after approval.
- Add notification approval workflow before real send.
- Add notification_log entries in addition to notification_outbox history.
- Add deduplication controls so repeated dry-runs are grouped by run ID.
- Add run batch table for notification preview/send batches.
- Add export of dry-run preview to CSV/PDF.
- Add real timezone scheduling logic for Central Time.
- Add business-day adjustment for holiday reminders that fall on weekends.
- Remove duplicate Ahmed demo identities or mark one inactive after testing.

### Role Enforcement / User Switcher
- Confirm all roles and permissions are database-driven.
- Add admin-safe user switcher for demo mode only.
- Add visible role banner showing current user, role, and permissions.
- Add route guard messaging when a user lacks access.
- Add audit entry when demo user switcher is used.

### Project Intake / Resource Assignment
- Add Project Intake shell.
- Add Engineering Resource Request workflow shell.
- Add PM request submission.
- Add manager/resource assignment review.
- Add project workspace shell.
- Add seeded sample clients, projects, tasks, and assignments.

### Expense / Emburse Certify Foundation
- Add expense shell.
- Add Certify import placeholder.
- Add sample expenses.
- Add approval/reporting visibility.

### Reporting & Accountability Dashboard
- Add executive dashboard shell.
- Add missing time, approvals, utilization, expense, and project health cards.
- Add drill-down links to source modules.

### Backup / DR / Restore / Replication
- Keep current demo modules.
- Add clearer readiness badges.
- Add exportable DR readiness summary later.

### Help Center
- Add article index.
- Add per-module help article links.
- Add admin support guidance.

## Items Not in August Demo Scope
- Full production Azure migration.
- Salesforce API integration.
- Outlook calendar sync.
- Multi-server communication.
- Real email sending.

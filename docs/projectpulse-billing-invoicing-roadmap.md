# ProjectPulse Billing, Invoicing, Closeout, and Balance Roadmap

## Project Closeout
Project closeout belongs inside the Work Register project detail page because it is part of the project lifecycle.

Required workflow:
- PM/PTC can request closeout.
- Attach final documentation.
- Confirm unbilled time.
- Submit for final invoice/closure.
- Admin/PTC can override with audit.
- Closed project can be reopened only through controlled role-based action with audit.

## Partial Invoicing
Partial invoicing should have a Billing / Invoice Workbench page.

Required workflow:
- Project detail page has Request Partial Invoice.
- Billing page tracks invoice batches.
- Track billed hours, unbilled hours, invoice reference numbers, approvals, exports, and invoice history.
- Partial invoice must tie back to project, tasks, time entries, rate snapshot, and customer balance ledger.

## Customer Balance Ledger
T&M, TM, and Block of Hours balances require a Customer Balance Ledger.

Required workflow:
- Track prepaid hours.
- Track purchased hours.
- Track consumed hours.
- Track remaining balance.
- Track project-level drawdowns.
- Generate monthly balance emails to Sales and Project Team Coordinator.
- Alert before BoH balance runs out.
- Block or warn on negative balance depending on approval rules.

## Additional Controls
- Change order tracking.
- Invoice approval history.
- Negative-balance controls.
- Rate override approvals.
- Project reopen controls.
- Customer-level billing alerts.
- Full audit trail for every billing/invoicing action.

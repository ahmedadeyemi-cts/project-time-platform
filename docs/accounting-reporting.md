# Accounting reporting and milestone invoicing

Open Invoice / Billing Center, select an engagement, and expand **Accounting reports, milestones and monthly revenue**. Finance, Accounting, Billing and administrators maintain these records in their own sessions. View-As cannot change or inspect this ledger. The reports also appear in Analytics Center.

1. Record the approved original contract amount and contract/SOW number. Enter the customer's Salesforce Account ID separately from the engagement's existing opportunity/record ID. Customer identity is shared across that customer's engagements. The original contract amount is preserved; approved changes use signed contract-change entries.
2. Record prepaid funding and usage, with effective dates and authorization references. Corrections use signed adjusting entries. Balances are recorded activity, not automatically inferred from invoices or timesheets.
3. Schedule fixed-price milestones, then record their actual acceptance date and reference. Select an accepted milestone in the existing manual partial/full invoice form. Review the commercial document, cumulative billing, invoice type and any required billing exception. The new charge must equal the milestone amount and the agreed total must match the current verified contract. The invoice and milestone link commit together. A linked milestone cannot be invoiced again, including after voiding; Billing must review any replacement authorization separately.
4. Confirm dated rates for approved, unbilled time. Invoiced work uses saved invoice date, hours, employee/task descriptions and rate snapshots. Other time uses Finance-confirmed snapshots; missing rates remain blank instead of using today's rate. Fixed-price time reports describe effort and do not create extra charges.
5. Record Finance-approved recognized revenue using its accounting effective date. Prior months are supported. Signed corrections preserve the original entry. Pulse does not infer recognition from invoicing or payment and does not select an accounting recognition policy.
6. Export the engagement summary, invoice ledger, milestone detail, billable time and monthly revenue to Excel. Analytics Center also supports CSV/JSON and saved report views. Monthly date filters select whole accounting months; cumulative recognition includes prior periods.

## Totals and reconciliation

Amounts are USD, matching existing Pulse invoicing. Other currencies require a separately agreed extension. The engagement summary is current as of the UTC date shown by the system. Contract amounts remain unknown until verified. Recognized revenue remains unknown if Finance has not posted any entries.

Invoice totals use Pulse billing invoices, excluding drafts, voids and cancellations. Invoice net, adjustments/credits, taxes and gross totals are shown separately. External invoice amounts use the existing manual reconciliation amount and are counted once using its cumulative high-water mark; they must exclude invoices already recorded in Pulse. Older client-invoice records remain available in the existing Accounting Invoice Detail Report and must be reconciled before entering an external cumulative amount. This avoids adding the same legacy or imported invoice twice. External tax breakdowns and payment/outstanding balances are not inferred.

The remaining unbilled net basis subtracts Pulse net invoices and the recorded external amount. Verify the treatment of external taxes with Accounting before using this value. Missing or limited report sources are identified; exports with incomplete source coverage are rejected by the billing panel. More than 5,000 source rows require a narrower scope. Exported revenue reports are Finance-maintained evidence, not an automatic general-ledger posting.

## Deployment and recovery

Migration 135 is additive and rerunnable. Financial entries, rate snapshots, operations and profile audit history reject updates and deletion. Application rollback retains the accounting schema and evidence. Protected Test releases retain exact-main CI/security admission, native Test environment protection, serialized queueing and workflow resealing. This feature uses the existing generation-free export acceptance lane plus accounting-specific installed checks. Celar-dependent acceptance remains pending and no Oracle or Production change is authorized.

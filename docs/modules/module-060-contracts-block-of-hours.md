# Module 060 — Contracts / Block of Hours

## Purpose

Module 060 manages customer prepaid Block of Hours agreements. A Block of
Hours contract normally lasts one year. A Project Team Coordinator may extend
the effective expiration date case by case.

A work request may remain classified as T&M, Service Request, Fixed Price, or
IQS while separately using a Block of Hours contract as its funding source.

No sample customer, Account Executive, manager, balance, quote, or contract
record from the reference workbook may be inserted into the application.

## Authoritative hour calculations

- **Purchased Hours** — Hours purchased in the original agreement.
- **Credit Awarded** — Approved customer-satisfaction or service-recovery
  credit hours.
- **Credit Reversal** — A correction that removes previously awarded credit.
- **Total Available Hours** —
  `Purchased Hours + Credit Awarded - Credit Reversal`.
- **Entered Hours** — Draft BoH-linked labor hours.
- **Submitted Hours** — Submitted BoH-linked labor hours awaiting final
  approval.
- **Consumed Hours** — Approved BoH-linked labor hours.
- **Remaining Balance** —
  `Total Available Hours - Consumed Hours`.
- **Projected Remaining** —
  `Total Available Hours - Entered Hours - Submitted Hours - Consumed Hours`.
- **BoH Overage** — Approved labor beyond the remaining BoH balance.

Approved time permanently consumes the contract. Draft, submitted, rejected,
declined, voided, or reversed time must not permanently reduce the
authoritative remaining balance.

## Expiration rules

- Default original expiration: one year from the contract start date.
- The Project Team Coordinator may extend the contract with a reason.
- Eligibility is determined by work date, not approval date.
- Historical eligible time remains eligible when approved after expiration.
- New work after effective expiration is blocked unless an extension covers the
  work date.
- Original and extended expiration history must remain auditable.

## Work request integration

Work request and task creation must support:

- Billing classification: T&M, Service Request, Fixed Price, or IQS.
- `Use Block of Hours` checkbox.
- Customer-filtered active contract selector.
- Current balance and effective expiration preview.
- Explicit contract selection when a customer has multiple active contracts.
- Contract inheritance by tasks and time entries.
- Normal invoice processing remains available.
- Expenses do not consume BoH.
- Insufficient balance does not reject engineer time:
  remaining hours consume BoH and excess hours become billable overage.
- The application must never silently transfer consumption to another contract.

## Invoice treatment

The recommended default preserves normal labor value and invoice reporting,
then applies the prepaid BoH amount as an offset. This prevents double billing
while preserving the value of delivered services.

Fixed Price invoicing remains controlled by its existing contract or milestone
rules. BoH-linked hours may still be tracked for usage and utilization.

## Contract fields

- Contract / Engagement Name
- Customer
- Customer Address from Customer Directory
- Primary Account Executive from active users
- Optional secondary Sales recipients
- Project Team Coordinator
- Eligible work types
- Purchased Hours
- Credit Awarded
- Total Available Hours
- Entered Hours
- Submitted Hours
- Consumed Hours
- Remaining Balance
- Projected Remaining
- BoH Overage
- Start Date
- Original Expiration
- Extended Through / Effective Expiration
- Status
- Certinia ID
- SELL Quote
- Salesforce ID
- Purchase Order / Quote reference
- Notes
- Last Updated
- Audit history

## Credits, extensions, and notes

Credits and extensions are immutable ledger records. Corrections are reversals,
not destructive updates.

Credit record:

- Hours
- Reason
- Award date
- Created by
- Related customer-satisfaction reference
- Created and modified timestamps

Extension record:

- Previous expiration
- New expiration
- Reason
- Created by
- Timestamp

Notes are chronological entries showing author and timestamp.

## Permissions

- Sales, Account Executive/Sales, and Executive users may view.
- Project Team Coordinator users may view, create, edit, award credits,
  reverse credits, extend expiration, add notes, and manage report scheduling.
- Server-side permission enforcement is required.
- Administrative emergency access must remain explicit and audited.

Proposed permission codes:

- `VIEW_CONTRACTS`
- `MANAGE_CONTRACTS`
- `MANAGE_CONTRACT_CREDITS`
- `MANAGE_CONTRACT_EXTENSIONS`
- `MANAGE_CONTRACT_EMAIL_SCHEDULE`
- `EXPORT_CONTRACTS_EXCEL`

## Contracts page

The page must be intuitive and include:

- Active contracts
- Purchased hours
- Credits
- Remaining balance
- Low-balance contracts
- Expiring contracts
- Expired contracts
- Exhausted contracts
- Search, filters, sorting, and status chips
- Detail drawer for contract, usage, credits, extensions, notes, linked work,
  email history, and audit history
- Persistent embedded `? Help` launcher
- Searchable help drawer
- Hover, keyboard-focus, and tap explanations for every sourced or calculated
  header
- Formula, source, included statuses, balance impact, and refresh timing in
  each header explanation

## Weekly Excel report

The weekly report must be a real `.xlsx` workbook. CSV is prohibited because
the report must preserve color integrity and workbook functionality.

Workbook behavior:

- Preserve branded colors and status colors.
- Freeze panes.
- Enable Excel filters.
- Use real Excel Tables.
- Use date, hour, currency, and identifier formats.
- Add header explanations as Excel cell notes where supported.
- Group and sort by Account Executive, Customer, and Contract.
- Include an AE Summary sheet.
- Include one worksheet per Account Executive.
- Include a consolidated BoH Balance Detail sheet.
- Include Usage Detail.
- Include Credits and Extensions.
- Include Report Information.
- Avoid duplicate totals when a secondary Sales recipient is present.
- File naming:
  `BoH_Balance_Summary_YYYY-MM-DD.xlsx`.

## Email recipients and SMTP

Recipient addresses are resolved from active system users for every run.

To:

- Active Account Executive / Sales users with valid email addresses.

Cc:

- Active Project Team Coordinator users.
- Active Executive users.

Rules:

- Deduplicate recipients.
- Exclude inactive, blank, or invalid addresses.
- Continue sending to valid recipients when one user is missing an address.
- Record excluded users and notify the Project Team Coordinator.
- Show recipient preview before saving or sending.
- Use the platform global SMTP configuration and global SMTP secrets.
- Do not create module-specific SMTP credentials.
- Attach the grouped `.xlsx` workbook.

## Schedule controls

Only the Project Team Coordinator may manage:

- Enabled / disabled
- Weekday
- Send time
- Time zone
- Subject
- Body introduction
- Active-only or include-expired filter
- Low-balance threshold
- Expiration-warning window
- Recipient preview
- Generate workbook preview
- Send test
- Send now
- Last successful run
- Last failed run
- Next scheduled run
- Prior report download

Recommended initial defaults:

- Monday
- 8:00 AM
- Configured business time zone
- 24-month workbook retention

## Delivery audit

Every report run stores:

- Trigger type
- Resolved To and Cc recipients
- Excluded recipients
- Workbook filename
- Workbook hash
- Account Executive count
- Contract count
- Data cutoff
- Generation result
- SMTP result
- Error details
- Started and completed timestamps

## Reference workbook mapping

The reference workbook provides the visual and grouping baseline:

- Account Executive
- Customer
- Engagement
- Manager
- PO / Quote
- Contract Amount
- Start Date
- End Date
- Entered
- Submitted
- Approved
- Total Hours
- Expenses
- Invoiced Services
- Invoiced Expenses
- Fixed Fee
- Contract Amount minus Invoiced Amount
- Remaining Balance

Module 060 extends that structure with hour-based BoH calculations, credits,
effective expiration, external identifiers, notes, status, audit information,
and AE-specific worksheets.

## Implementation sequence

1. Source discovery and forward branch.
2. Database schema and permission seed.
3. Contracts API and balance ledger.
4. Contracts page, help drawer, tooltips, and detail drawer.
5. Work request and task BoH selection.
6. Time-entry approval consumption and reversal logic.
7. XLSX generation with AE grouping, filters, and colors.
8. Global SMTP delivery, scheduler, history, and retry.
9. Tests, migration validation, API/web build, and controlled deployment.

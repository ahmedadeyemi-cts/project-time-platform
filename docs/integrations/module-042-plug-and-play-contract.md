# Module 042 Billing and External Integration Contract

## Non-negotiable data rule

Operational values must come from ProjectPulse or an explicitly configured
external system. Missing values remain missing and must never be generated as
sample operational data.

## Billing candidate contract

Each billing candidate must contain:

- Project ID and canonical project code
- Customer ID and customer name
- Project name and contract type
- Project Manager and Project Team Coordinator
- Assigned resources
- SELL Quote ID
- Salesforce ID
- Certinia ID
- Purchase-order requirement and active PO
- Billing period
- Billing blockers
- Previously invoiced totals
- Approved unbilled totals

## Billing line contract

One qualifying time entry normally becomes one invoice evidence line:

- Time-entry ID
- Work date
- Engineer, PM, or PTC identity
- Project
- Task
- Time type
- Labor category
- Customer-facing description
- Approved hours
- Effective rate card and rate line
- Rate
- Amount
- Approval evidence

The system may offer a summarized export layout, but the immutable invoice
snapshot must preserve the underlying entry-level lines.

## Purchase-order rules

- A project can require no PO, one PO, or multiple POs.
- A project can have only one active primary PO.
- A required PO with no active primary record blocks billing.
- Expired, exhausted, replaced, and cancelled POs cannot be selected.
- PO number and authorized amount are snapshotted into a finalized invoice.
- Later PO edits cannot change a finalized invoice.

## Rate-resolution rules

Rate resolution must consider:

1. Customer-specific rate card
2. Project/default rate card
3. Standard company rate card
4. Labor category
5. Time type
6. Work date and card effective dates
7. Minimum-billing rules
8. Travel, emergency, onsite, and after-hours rules

Missing or ambiguous rate matches block invoice preparation.

## External connector interface

Each connector must implement:

- Configuration readiness
- Authentication readiness
- Connection test
- Field mapping
- Incremental read
- Idempotent upsert
- Retry and backoff
- Rate-limit handling
- Audit event creation
- Last-success and last-error reporting

## Supported connector registrations

- SALESFORCE
- CERTINIA
- SELL

These are separate adapters. Shared authentication may be configured only when
the external-system owners confirm that it is appropriate.

## Secret handling

ProjectPulse stores only:

- Environment-variable names
- Secret-store references
- Service-account references
- Non-secret connection metadata

ProjectPulse must not store API secrets or access tokens in GitHub, browser
storage, logs, ordinary JSON configuration, or ordinary database text fields.

## Meeting checklist

For each external system, confirm:

1. Sandbox and production URLs
2. Authentication method
3. Service account
4. Client/application ID
5. Required scopes and permissions
6. API version
7. Customer/account object
8. Opportunity/deal object
9. Quote object
10. Project object
11. Invoice and invoice-line objects
12. Purchase-order object
13. Custom-field API names
14. External-ID field
15. System of record for each field
16. Inbound and outbound ownership
17. Webhook, event, polling, or batch options
18. Rate limits
19. Retry rules
20. Test records
21. Acceptance criteria
22. Production cutover and rollback process

## Implementation sequence

1. Apply the sidecar migration.
2. Add Work Register PO administration.
3. Return the canonical project code from Work Register.
4. Add the billing-candidate API.
5. Resolve approved time entries and effective rates.
6. Add invoice creation and immutable snapshots.
7. Add connector-readiness APIs.
8. Configure sandbox connectors.
9. Validate mappings with business owners.
10. Enable controlled inbound and outbound synchronization.

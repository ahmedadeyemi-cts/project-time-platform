# Module 060 approval totals and Module 055D funding

Module 060 reads canonical time entries for live pending and approved hours. Submitted time is pending. Manager-approved time stays pending when its project has an assigned PM. Without an assigned PM, manager approval (including the existing PTC manager-stage authority) completes time approval. A PC assignment does not introduce PM review. Draft, declined and returned time is excluded. The v2 approval queue and final approval guard use the same assigned-PM requirement.

Contract lists, portfolio totals and contract details distinguish hours from monetary usage. Imported monetary opening balances and manual financial ledger records are retained, not converted into fabricated hours. Live financial additions after an import use the original time-entry creation timestamp to avoid counting imported time again. Imported pending money remains a historical snapshot; it cannot be reclassified without reconciling the imported source.

In the final Module 055D review, authorized project creators may select a funded, active customer contract eligible for the project's T&M or Fixed Price billing type. They explicitly enter the agreed labor drawdown rate. This is labor funding, independent of customer billing type: it does not issue invoices or allocate a fixed-price milestone payment. Submission reserves the labor value; approval moves the same value to approved usage without deducting it twice. Contract selection is optional; no contract is chosen automatically.

The server validates customer, dates, balance and eligibility when committing. The link, rate, audit history and project creation commit atomically. Retries use the existing intake idempotency behavior. An existing explicit time ledger mapping takes precedence over automatic project funding, avoiding duplicate counting. Funding is shown in contract details. This version selects funding for newly created projects; changing a saved funding source is intentionally outside this workflow.

## Deployment

1. The existing Protected Test private-network migration job now packages and applies 060, 060b and 060c in order, verifies their checksums, and runs `verify-module060-contract-funding.sql` before application deployment. Its receipt identifies the exact release and image. No separate manual SQL step is needed. All three migrations can be reapplied.
2. Deploy the reviewed source through the normal protected-UAT process.
3. Check a new T&M and a new Fixed Price project against the same customer's contract. Validate draft → submitted → manager approval → PM approval; also test no PM, coordinator only, rejection, edit and deletion. Verify rates and balances against the customer's agreement.
4. If reverting, restore the old API/UI, then apply `060c-contract-approval-funding-rollback.sql`. Funding links and audit data are retained for recovery.

Contract funding does not reserve the full estimated project value at creation or block legitimate time entry when funds run out. Balances can therefore become negative as work exceeds available funds; existing low-balance monitoring remains relevant. Fixed-price invoice/milestone drawdown is not implemented by this labor-funding change.

## Validation

Run `tests/contracts-approval-funding.test.mjs` using PGlite 0.5.8 as documented in its header. It executes the real migrations and eligibility SQL against PostgreSQL semantics, tests the approval matrix and transitions, snapshot preservation, duplicate prevention and funding eligibility. Build the backend with .NET 10 and compile the three changed JSX components. Protected-UAT verification remains a deployment step.

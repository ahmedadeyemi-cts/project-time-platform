# Fixed-price billing and connector continuity

Manual partial/full invoices use the existing immutable ledger, deduct all prior local and documented external charges, and preserve PDF/Excel output, transaction locks, replay protection, project access and PO limits.

## Eligibility

Manual project amounts are restricted to Fixed Price contracts. T&M retains approved-time/governed-package requirements. Ordinary partial invoices require submitted time and a referenced milestone or authorized progress instruction; unfinished approvals remain visible. No submitted time requires an explicit exception. Ordinary final invoices require recorded delivery completion, submitted time and no pending time review. Billing, Finance, Accounting or an administrator may explicitly approve an exception in their own session, providing the instruction and reason. The actual signed-in approver and evidence are retained. A partial invoice cannot silently charge the full agreed amount. Billing never approves time, records payment or closes the project.

## Commercial continuity

The form displays the existing SELL commercial read model, linked quote and last successful synchronization. The immutable invoice captures that snapshot together with the approved commercial document/version used to verify the fixed-price total. Current integration data does not supply an approved fixed-price milestone schedule; operators must verify the referenced signed terms. An unavailable/unlinked SELL source requires documented fallback verification and is flagged for later reconciliation. When synchronized quote information becomes available, Billing can record the reconciliation reference; original invoice values are never overwritten. Live SELL access is not claimed by this change.

## Certinia continuity and recovery

Pending or failed Certinia deliveries do not prevent subsequent local invoices: they are already included in the local balance and must not be counted again as external charges. In Billing reconciliation and offline tracking, Billing/Finance/Accounting/Admin may hold delivery, release an unused hold, record an actual manual handoff, or match an existing Certinia invoice. These are human-verified references, not an external-system verification or payment confirmation.

Holds, handoffs and matches atomically stop pending retries under the same project lock as the sender. In-flight delivery cannot be overridden. Both queue creation and worker claims honor the evidence. Releasing a hold permits existing queued work to retry and is rejected after a manual handoff or external match. Exact retries reuse the same audit record; altered retries conflict. Original amounts and time records remain unchanged. Reconcile the external customer, currency, invoice number and amount before recording a match. The existing completion checklist still controls final billing and closeout separately.

Amount-based billing remains the reconciliation path while a nonvoid manual invoice or external billed balance exists, preventing time/package duplication. Voided invoices without external charges no longer permanently select that path. Additional charges after a final invoice still require a governed correction/change-order decision.

## Validation

Disposable PostgreSQL tests exercise real handlers, eligibility, identity/role checks, amount arithmetic, immutable evidence, connector outage continuity, hold/release/handoff/match and worker duplicate protection. Browser checks cover responsive invoice creation, exception evidence, replay, stale balances, permissions, and readable light/dark themes. No live customer invoice is created by these tests. No database migration or connector activation is required.

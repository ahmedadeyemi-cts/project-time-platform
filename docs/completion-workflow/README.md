# Delivery, acceptance and manual Certinia billing evidence

## Release boundary

New source-only implementation based on main `4c31007053359b1cf41681d686742f6c08fbb3b6`. Review in a draft PR; do not merge or deploy until owner approval and exact-head validation. No production activity, live invoice transmission, customer notification, credential, role grant, AI provider setting, deployment-controller edit or payment action is included.

## Using the checklist

In the PM workspace (018), select the assigned project and open **Delivery & closeout**. The same **Delivery, acceptance & billing** panel is mounted in Project Closeout (040) and on a selected project in Invoice Billing (042). These are the same persisted record, not three separate checklists.

1. **Delivery complete:** the assigned PM or PTC records the actual completion date, deliverable/version reference, evidence and audit reason, then confirms. This starts closeout immediately without requiring final billing. The project is not yet marked closed.
2. **Customer acceptance:** record the customer representative, decision date and evidence of the actual decision. Conditions or requested corrections remain unresolved; a plan view/link click is never acceptance. A new delivery receipt invalidates its old acceptance.
3. **Sent to Certinia:** PTC confirms a package already handed over outside Pulse. Record the date, package reference and supporting document/email/ticket. Distinguish partial billing from the final handoff. Select any existing Pulse invoices covered by a partial handoff; a final handoff covers the project's current invoices and reconciliation of prior partial billing. This saves evidence only and sends nothing externally.
4. **Fully billed:** PTC or Billing confirms that Billing has processed the final charges and no authorized charges remain. Record the actual final invoice/Billing completion reference. A partial handoff cannot satisfy this step. Connected Certinia delivery may supply the sent evidence, but a successful API send still does not establish billing completion or payment collection.

The saved author, date, reference and supporting evidence are visible. Corrections append history rather than erasing receipts. Reopening billing evidence does not cancel a real external invoice and cannot erase historic duplicate-send protection for a mapped invoice. When a save result is uncertain, retry the same operation or refresh to verify; never send charges again merely because the checkbox save timed out.

Finish final time/expense review and remaining task checks in Project Closeout. Its **Request project closeout** action now saves incomplete progress; only the final close action requires every applicable server check. Existing PTC/administrator final-close authority is preserved. Fully billed is not an automatic close or archive action.

## SELL boundary and simpler presentation

Manual evidence recording does not query SELL or require a rate card, commercial readiness, an invented price or a fabricated local invoice. The PM completion tab is evaluated before optional financial-detail loading, so failure of that supporting detail does not hide the checklist. Invoice generation in Pulse retains its existing commercial/PO/rate/approval controls. The billing page separates these two paths, collapses commercial details and makes external-reference columns optional by default. An empty eligible-time list is no longer labeled “fully invoiced.”

This is not a universal SELL outage repair. Module 042 still uses its existing project-candidate service, and local database/source availability remains necessary. Use the PM completion tab or Project Closeout when invoice candidate/commercial detail is unavailable. No fixed-bid pricing, milestone policy, time approval routing, utilization denominator/PTO policy, HR payout calculation or leaver exception has been changed. Those broader policy decisions remain separate from this evidence workflow.

## Persistence and security

Existing migration-038 `work_lifecycle_audit_events` stores versioned `completion_checklist_recorded` envelopes with contract `project-completion-evidence-v1`. Its existing immutable audit trigger remains authoritative; no new database migration is introduced. Operational billing/expense tables already used by the application must be installed. The closeout flags are mirrors, not sufficient proof: final closeout reloads the receipts and rechecks their current charge basis.

Each mutation reloads active roles and project assignment, rejects View-As, locks the project row and uses a serializable transaction. The expected revision, operation ID/request hash and expected financial fingerprint protect against stale forms, payload reuse and concurrent saves. The fingerprint covers local project commercial identity, time, readiness packages, billing profile, PO, current expenses, nonvoid invoices and lines. Changed evidence invalidates the prior fully-billed attestation; it does not silently re-price a contract. Approved externally billed charges are resolved for closeout only by a current final manual reconciliation. Pending time, open project tasks, customer acceptance and final review still apply.

The Certinia outbox queue uses the same project lock. Queued/processing/retryable transmissions block a manual billing attestation; recorded final handoff and invoice-specific historic manual receipts block a duplicate automatic queue request. This is not a universal cross-system duplicate-charge detector. Externally created packages without local invoice IDs require the PTC's documented reconciliation; the system cannot inspect an external package merely from a typed reference.

The React component reloads capabilities from the server, invalidates on session/View-As changes, checks identity again before saving, serializes submissions and reuses the operation ID after an uncertain network response. Internal evidence is outside the customer-invoice container and hidden in print. No evidence HTML or external link is executed.

## Validation

`node --test src/frontend/project-time-web/tests/completion-checklist.test.mjs` covers presentation semantics, capability fail-closed behavior, expected revisions, source identity/retry boundaries and print isolation. These are model/source checks, not browser behavior tests.

`tests/CompletionWorkflowTests` builds against the actual API and exercises real handlers, role queries and the real 001/038 migrations in a disposable PostgreSQL database. The other operational tables are explicit synthetic fixtures, not a claim of full production-schema parity. Tests cover unauthorized/inactive/unassigned/View-As writes, delivery-triggered closeout, evidence persistence, conditional acceptance, partial/final/connected billing, uncertain retry idempotency, changed-charge invalidation, pending time, actual manual-billing closeout/reopen, audit immutability, duplicate-send protection and concurrent writers. They never transmit an external invoice.

The dedicated read-only CI additionally builds the entire frontend and checks generated-source stability. Before merge/release, review compiled/database results on the exact head; test keyboard, mobile and light/dark behavior with authenticated PM/PTC/Billing accounts in Protected UAT. Verify actual installed table privileges, header/session middleware, existing integration state and representative historical projects. These source and fixture tests do not establish live Certinia operation, customer acceptance identity, exhaustive status-edit paths, notifications or payment processing.

## Rollback

Revert the application PR through the existing governed release process. No schema rollback is needed; retained audit receipts must not be deleted. An older application does not understand the new structured evidence or manually reconciled closeout path, so assess those projects before reverting. Neither a rollback nor reopening Pulse evidence cancels a real Certinia invoice.

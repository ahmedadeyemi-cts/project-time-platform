# Manual partial and full billing

Module 042 now supports authorized amount-based invoice creation without Certinia, SELL, rate cards, or time-entry approval. It creates a real immutable Pulse invoice and uses the existing PDF/Excel downloads. It does not transmit invoices, mark time approved, confirm payment, or close projects.

Choose a project, open **Manual partial / full invoice**, and enter the agreed project total. For a partial invoice enter the cumulative amount to bill through this invoice. Full/final uses the full agreed total. Enter all prior external billing, excluding invoices already recorded in Pulse, with its references. Review the new charge, supply the approved SOW/PO/billing reference and audit reason, then confirm.

Example: agreed total $10,000, Pulse invoices $4,000, external invoices $1,000. A cumulative partial target of $7,000 creates a $2,000 invoice. A later full invoice creates the remaining $3,000.

The server deducts prior Pulse invoices and externally billed amounts. It rejects zero/negative residuals, overbilling, fractional cents, stale balances, reductions to prior external billing, repeated final invoices, missing project/PO requirements, archived/closed projects, and unresolved automatic transmissions. Exact retries return the same invoice and number. Concurrent requests share the existing project-first transaction lock. Once a manual amount invoice exists, further time-based creation is blocked for the project; continue manual billing to avoid billing the same work twice.

Existing Billing/PTC/Admin and assigned PM invoice permissions apply. View-As cannot create invoices. Manual handoff wording is connector-independent, and Billing can record the handoff as well as final processing. Final billing evidence and project closeout remain governed separately, including pending time approvals.

This path invoices a reconciled project amount in USD. It does not split a time entry, create credit notes, infer taxes, or reconcile undocumented external billing automatically. Additional charges after a final invoice require a separate governed correction/change-order decision. No database migration, provider activation, or production data change is included.

Validation: `tests/ManualBillingTests` runs actual handlers against disposable PostgreSQL using the existing invoice foundation and lifecycle migrations. Frontend tests verify cents and cumulative arithmetic; real-component browser tests cover partial/full flow, retry identity, stale balance, project switches, permissions, and themes.

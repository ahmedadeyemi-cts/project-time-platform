# Module 025 assembled acceptance correction

Base: `d53b9451d7b2eebda686e8bdb84b46d53dc9e7f9` (PR #1071).

## Evidence and correction

The September 17 qualification run `35177281331` received a complete Claude response in 57,333 ms, then rejected `$.objective` as `unapproved_proper_nouns`. The rejected response was deliberately not retained, so its exact offending word is unknown. Offline reproduction demonstrated that normal technical proposals containing Baseline, Map, NTP, SIP, and Version were rejected by capitalization heuristics. This is evidence of a validator defect, not evidence that Claude timed out.

Module 025 now uses a dedicated output policy for its closed public-technology input. It accepts natural technical prose while scanning every complete JSON string, including unknown fields, for known customer identities, explicit identity labels, credentials, network addresses, contact details, and other concrete sensitive values. The general external-output policy is unchanged. This policy does not claim arbitrary-name recognition or authorize forwarding private source prose. Detailed task validation, source binding, provider budgets, durable checkpoints, and user review remain required.

The Protected Test `main` / `sow_role` lane now applies and verifies existing migration 106 before application installation, using the existing private-network migration runner and immutable image digest. Normal-SA acceptance checks register/schema readiness before creating a SOW or requesting generation. It verifies retained version identity, valid DOCX/XLSX containers, repeated identical download bytes, historical hashes after reopening and editing, and denial of unauthenticated historical downloads.

My Role runs after a SOW failure and retains its own result. Cancellation still stops the sequence. The combined acceptance remains failed if either check fails. Provider qualification compares API environment bindings semantically, reports only closed categories for differences, and still rejects image, command, resource, configuration, or other template changes.

PR CI runs the existing React document-action browser test and PostgreSQL migration-106 test. Additional local tests exercise the actual scoped shell step under success, failure and cancellation, and the migration builder with a fake Azure transport. The separate register browser test uses asynchronous download context managers correctly.

## Acceptance limits that must remain visible

- A passing build or isolated provider qualification does not establish a working SOW/GSD lifecycle. A Protected Test deployment and normal-SA live acceptance are still required.
- Automatic SELL document publishing remains blocked by `SELL_DOCUMENT_WRITE_ADAPTER_REQUIRED`. The current Module 026 integration has read operations; its publisher lacks an approved document-write/upload and receipt-reconciliation contract. Do not fabricate upload receipts or issue a success notification. The vendor's documented Documents API currently exposes list/get operations: https://developer.zendesk.com/api-reference/sales-crm/resources/documents/ . A supported write contract is a prerequisite for that adapter.
- The cloud input capsule supports its existing closed technology/operation vocabulary. Ambiguous exclusions, negation, and unknown technology remain ineligible; version/count associations are only emitted for unambiguous single-technology input. This repair does not claim general extraction of arbitrary customer scope. Preserve the private path and explicit unanswered requirements rather than silently dropping exclusions or sending raw source externally.
- SELL status is recorded separately in acceptance evidence. `fullRequestedScopePassed` stays false because this workflow does not prove a completed SELL publication.
- Celar runtime performance is deferred. No runtime deployment, model change, Production operation, or FlowHive generation is part of this correction.

## Release control

The existing native protected `test` environment gate, manual trigger, exact release identity, concurrency, production isolation, and rollback commands are unchanged. The exact-file validator pins this repair to the base above and checks that deployment authorization manifests, SQL, provider configuration, secret stores and the Celar runtime are unchanged. The two intentional deployment-script additions are independently checked against the prior workflow contract.

Repository write/merge permission does not create a workflow-dispatch or environment-approval capability. The connected GitHub methods available during this repair include repository and PR operations but no dispatch or deployment approval operation. Do not rerun an old commit, repurpose another privileged dispatcher, or remove the protected environment to compensate.

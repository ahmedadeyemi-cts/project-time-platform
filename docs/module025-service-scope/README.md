# Service Scope authoring and generated SOW overview

## Feature contract
Service Scope is the author-entered, versioned source. Service Overview is an AI
proposal that the Solution Architect can edit. Editing an overview must not
change the source. Editing the source marks the previous package stale and
retains its contents in the existing engagement event history. Five-phase
checkpoints still require the exact saved source/version. A failed generation
never creates a completed SOW or fabricates tasks or hours.

In the new mode, each phase receives the complete Service Scope as a single
`serviceScope` JSON field, plus server-owned task-schema/phase instructions and
bounded prior-phase references. No keyword extraction or exclusion-word filter
is applied. The provider DTO is explicitly constructed; it is never the
serialized engagement record. The full text can contain anything typed into
that field. It is not anonymized, sanitized, or asserted to be nonconfidential.
Customer-record fields, project/engagement identifiers, pricing, commercial
settings, people, attachments and other internal documents are not automatically
included. There is no external browsing or tool execution in this generation
path. Final identity, branding and commercial assembly remains local.

Module 064's stored `sow_gsd_planning` route is the only sequence authority.
`serviceScopeFullTextApproved` is a separate, audited, revision-checked approval
with a false migration default. Legacy sanitized-generation approval never
implies it. The existing deployment-level external-provider switches still
apply. Private providers can process scope locally without external disclosure
approval. External target skips, failures and terminal refusals remain explicit;
no target is reordered, silently inserted, or replayed after its position.

Plan proposes a whole-engagement overview (600–4000 characters, two to four
paragraphs requested) alongside substantive Plan tasks. The remaining four
phases use the same source and the existing sequential contract. Final assembly
uses the Plan overview without making another large full-document AI call.
All phase output remains subject to the canonical typed task contract, source
citation binding, completeness and effort checks. Shape/length checks do not
prove semantic correctness or vendor compatibility: exclusions, quantities,
versions, scope coverage and effort require human review.

A shared task record still supplies the SOW narrative and GSD estimate. Existing
AI-suggested vs. SA-final hours, after-hours designations, confirmation, retained
versions and archive/reopen behavior remain in use. Manually edited overviews
are not replaced by regeneration: the latest AI proposal is stored separately
and can be explicitly applied. Prior sections/phases are retained in event
history before replacement; this change does not implement a general document
diff/merge editor. FlowHive's provider policy and detailed WBS generation are
unchanged; the reviewed SOW/GSD remains the intended downstream input.

## Migration and compatibility
`124_module025_service_scope.sql` is additive and idempotent. It requires the
existing 099 workspace and 123 consent schema. The guarded private-network
planning migration runner packages, hashes, applies and verifies it after 123.
This PR does not run that migration or deploy anything.

Existing rows retain NULL `service_scope`, their old Service Overview source
semantics, and their existing generated/reviewed content. The UI provides an
explicit **Use existing input as Service Scope** action; ordinary legacy saves
do not adopt the new mode. New records use the separate input when bootstrap
reports the new schema. Without migration124, new-mode saves fail with an
explicit migration message and old records remain readable.

In Module 064, explicitly save **Allow configured AI providers to process the
complete Service Scope for SOW/GSD generation**. Keep the existing order. The
legacy checkbox remains separately labeled. Reload to confirm persistence. This
permits external processing of the complete field and can incur provider charges.
Revoking this approval does not change the provider order. No approval is enabled
by this PR, migration, record creation, or test.

Rollback: roll back application images using the normal protected controller;
retain additive columns and event history to avoid losing entered scopes and
reviewed overviews. Revoke full-text approval through Module 064 before returning
to old workflows. Do not drop the new columns or reinterpret the generated
narrative as original source. Old software ignores nullable additions but cannot
manage new-mode records correctly; protect new-mode records from legacy writes
during an operational rollback. No automatic destructive down migration is supplied.

## Tests and release gates
Run from repository root:

```
node --test tests/service-scope/generation-feedback.test.mjs
python3 tests/service-scope/source_boundaries.py
dotnet run --project tests/Module025ServiceScopeTests --configuration Release
```

The .NET suite requires `MODULE025_SCOPE_TEST_DATABASE` pointing at a disposable
loopback PostgreSQL admin database and creates/drops a random database. Tests
inject synthetic providers and do not exercise paid or live private models.
The dedicated CI is read-only, has no protected environment or production/UAT
secrets, builds the API and frontend, and runs the new test suite. Existing
regression suites and release-controller scope gates must also be reviewed;
passing this new suite does not bypass them.

Keep this PR unmerged until reviewed. UAT acceptance after a separately approved
deployment must establish: a new-mode record saved/reloaded; unchanged Module064
order; no external attempt with only legacy consent; exact full scope field and
no automatically included record fields at the provider boundary; all five
phases with substantive tasks and timers; generated overview distinct from the
input; source/overview/proposal independently persisted; manual overview and
reviewed hours preserved; exclusions/quantities/versions verified by a reviewer;
consistent SOW/GSD downloads; archive/reopen; changed-source cancellation; and
refusal/invalid-output paths. No live generation success is established by this
PR. The prior DeepSeek output-budget exhaustion and Celar 504 failures in #1138
remain unresolved until actual runtime acceptance succeeds.

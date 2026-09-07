# Bounded FlowHive planner validation

Status: bounded execution and readback hardening implemented; live acceptance remains required.

Initial bounded-execution source commit: 9b11ee7462ef5e49593cd2352b6e157162deef12.
Current-main integration baseline: 56d455b128a6cc509408338cff5354a97f08dbbe.
Use the PR head and the matching CI run for subsequent validation; these are not deployment identifiers.

The planner now captures an immutable five-minute operation deadline and a starting working-copy row version, limits orchestration attempts to two, and independently observes cancellations and expired work. Final draft and terminal status are committed together only while the deadline, current project/document identity and optimistic-concurrency preconditions remain satisfied. Project Forge and the legacy FlowHive route use the durable queue rather than starting a competing synchronous request. Returning to the page resumes observation; changed dates and unsaved input are submitted explicitly; reading a saved working copy calculates its schedule without another inference call.

## Saved-result integrity hardening

The worker now records the committed working-copy row version and revision in the same transaction as its completion state. The readback UI compares that receipt with the loaded working copy before applying it. Missing, wrong-project, failed and newer-revision readbacks preserve the displayed plan, report the unresolved condition, and offer an explicit read-only recovery rather than launching inference again. A saved receipt cannot subsequently be rewritten.

Status observation rejects malformed terminal flags and wrong identities before updating the UI. Permission, stale-source and validation responses are terminal for observation. Only transient network/status failures receive bounded read retries; status observation cannot start another model job.

The source scope is checked against a sorted exact manifest. This work does not authorize changes to deployment controllers, provider secrets or Module 025. CI-validation workflows remain read-only. Temporary source-transport and SDK-download workflows are removed before the final verification run.

## Automated evidence recorded during implementation

- Real React component exercised with explicitly synthetic API fixtures: edited dates, saved schedule readback, project switching, immutable-view preservation and identity-change fences.
- Added failed-readback, newer-remote-revision and wrong-project scenarios; each recovers through working-copy reads without a second AI start.
- Twenty JavaScript observer/scope assertions pass in the isolated local runner.
- Native executable WBS: 45 assertions pass against the actual compiled builder and scheduler.
- Existing detailed-planner/Module 025 regressions pass against the compiled backend after current-main integration.
- The new PostgreSQL receipt transaction tests must run in the matching CI database job. They are not counted as locally executed without PostgreSQL.
- Complete post-change build/CI evidence must be read from the final revision, not inferred from an earlier green run.

## Required automated evidence

- Actual PostgreSQL migration 104 apply/reapply and retained-evidence rollback protection.
- Actual backend queue deduplication, immutable input/deadline constraints, cancellation, late-completion rejection and competing optimistic-concurrency writes.
- Native executable WBS and pre-existing detailed-planner/Module 025 regressions.
- Frontend observation, aborted requests, changed project/run identity, response-body timeout and edited-state application guards.
- Complete frontend build and real browser integration tests, not only static source matching.

## Required runtime evidence

Use the real configured private provider, current authorized SOW and PM session in Protected UAT/Test. Record provider/model, source versions, stage timing, run IDs, actual inference attempts, saved revision and browser readback. A synthetic-model, fixture-browser or isolated-database test cannot be counted as live SOW-to-plan acceptance. Cancellation and deadlines prevent persistence of late output; their configured budgets are not successful-generation latency measurements.

The full enterprise PSA acceptance matrix remains open. No deployment, real-customer notification/publication, or full product-parity claim is made by this change.

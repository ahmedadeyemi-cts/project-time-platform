# Bounded FlowHive planner validation

Status: implementation applied; CI and live acceptance must be recorded separately.

Applied source commit: 9b11ee7462ef5e49593cd2352b6e157162deef12.

The planner now captures an immutable five-minute operation deadline and a starting working-copy row version, limits orchestration attempts to two, and independently observes cancellations and expired work. Final draft and terminal status are committed together only while the deadline, current project/document identity and optimistic-concurrency preconditions remain satisfied. Project Forge and the legacy FlowHive route use the durable queue rather than starting a competing synchronous request. Returning to the page resumes observation; changed dates and unsaved input are submitted explicitly; reading a saved working copy calculates its schedule without another inference call.

## Required automated evidence

- Actual PostgreSQL migration 104 apply/reapply and retained-evidence rollback protection.
- Actual backend queue deduplication, immutable input/deadline constraints, cancellation, late-completion rejection and competing optimistic-concurrency writes.
- Native executable WBS and pre-existing detailed-planner/Module 025 regressions.
- Frontend observation, aborted requests, changed project/run identity, response-body timeout and edited-state application guards.
- Complete frontend build and real browser integration tests, not only static source matching.

## Required runtime evidence

Use the real configured private provider, current authorized SOW and PM session in Protected UAT/Test. Record provider/model, source versions, stage timing, run IDs, actual inference attempts, saved revision and browser readback. A synthetic-model, fixture-browser or isolated-database test cannot be counted as live SOW-to-plan acceptance. Cancellation and deadlines prevent persistence of late output; their configured budgets are not successful-generation latency measurements.

The full enterprise PSA acceptance matrix remains open. No deployment, real-customer notification/publication, or full product-parity claim is made by this change.

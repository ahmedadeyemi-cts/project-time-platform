# Shared scope and five-phase generation

The SA enters and saves one project scope in Service Overview. Generate uses that
scope to produce Plan, Design, Implement, Validate and Release sequentially. Each
request combines the saved scope with a different phase instruction; the SA does
not need to write five prompts or manually start each phase.

For a scope such as “Upgrade Cisco CUCM from 14.0 to 15.0,” the instructions ask:

| Phase | Focus |
| --- | --- |
| 1. Plan | Identify discovery, readiness checks, dependencies, scope and missing facts. |
| 2. Design | Define the target approach, architecture decisions, upgrade sequence and prerequisites. |
| 3. Implement | Describe the execution work, change window, safeguards and rollback preparation. |
| 4. Validate | Define verification, acceptance tests and evidence for the requested outcome. |
| 5. Release | Prepare handover, operating documentation, knowledge transfer and closure. |

The scope anchors the technology, source/target versions, outcomes and constraints.
Missing environment facts remain questions or assumptions for SA review. An AI
draft is not confirmation that a vendor supports a specific upgrade path.

Each phase supplies high-level customer-facing SOW steps and detailed GSD tasks,
dependencies and proposed effort. Existing task review exposes after-hours
suggestions and lets the SA confirm the required allocation. SA review and
confirmation remain required. Managers, ownership transfers, templates, SELL,
downloads, draft deletion, archive and reopening retain their existing behavior.

## Progress and recovery

The workspace shows five phase cards, elapsed time for each phase and overall
elapsed time. Timers use persisted server timestamps and include retries; they
are elapsed time, not estimated completion times. Reused phases are identified
without inventing timing data. After the fifth phase, a separate “Preparing SOW
and GSD” state remains until the final package is validated and saved.

Opening or refreshing a record discovers its latest authorized generation and
reattaches to monitoring. This read does not start an AI call. A status-read error
offers another check; it does not silently start a duplicate generation.

After an interrupted attempt, an explicit resume can reuse the contiguous prefix
of saved phases when scope, revision and generation contract still match. The
first missing phase and everything after it are generated again. Editing the
scope invalidates old checkpoints. Completed work remains visible as historical
generation evidence after later review, confirmation or archive revisions.

## Implementation and validation boundaries

Private generation reuses the same bounded scope representation across all five
requests. External-provider requests retain the existing sanitized technical
capsule and privacy restrictions. Prior-phase continuity carries validated phase
and WBS references, rather than treating earlier AI prose as authoritative facts.
The new generation contract rejects checkpoints written under the older prompt.

The latest-status endpoint uses the existing record permissions and a consistent
database snapshot. It returns progress metadata, not partial draft content.
Provider routing, the 40-minute job budget and protected-release controls are
unchanged. No database migration is required.

Regression coverage includes shared-scope prompts, private/cloud boundaries,
checkpoint ordering and resume, timestamp projection, refresh recovery, explicit
retry, read-only access and retained document actions. Provider responses in unit
tests and browser API responses are controlled fixtures. These checks do not
establish the quality or latency of a live AI-generated CUCM package; that needs
an authorized UAT generation and SA review after deployment.

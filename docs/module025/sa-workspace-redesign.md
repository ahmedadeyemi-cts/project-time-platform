# Module 025: SA workspace redesign preparation

This PR establishes one workflow for Systems SAs and Collaboration and Networking SAs, each with their own reporting manager. It prepares source and migrations for review; it does not authorize or execute a deployment.

## Working model

One immutable SOW/GSD identity carries an editable working revision, current responsible SA, append-only events, retained confirmed document versions, and existing ConnectWise SELL submissions. Immutable means released versions and audit evidence cannot be overwritten through the application. Draft content can change, and each ownership handoff increments its revision. This is not a claim of external WORM storage or protection against a privileged database administrator.

The interface presents My Work, Team Work, and Templates. A selected record shows Define request, Generate draft, Review tasks and hours, Confirm package, and Download and hand off. The current backend lifecycle remains draft, review_ready, confirmed, and archived; the guided stages do not introduce a second source of lifecycle truth.

## Two SA teams and ownership

Current, active reporting relationships govern access, not editable department/team labels or inferred job titles. Each manager sees their scoped SAs. Team Work shows owner, team, customer, AE, delivery LOE, status, and last update. Target completion date, priority, blocker and remaining SA authoring effort provide separate operational context; the queue can highlight work needing attention. The bounded queue reports its total and tells the user when filters are needed; it does not present a limited page as a complete team workload.

An SA may transfer their own active, editable package to an active SA sharing the same reporting manager. A manager may transfer a scoped member's work to another scoped member under that reporting manager. Cross-team handoffs are not silently granted by the selector. Administrators retain existing support authority, but destinations still require the source owner's authoritative reporting-team relationship. A team lead's read visibility does not implicitly grant manager ownership-transfer power.

Transfers require an explicit destination, handoff reason, and expected revision. The operation serializes against generation, locks the record, revalidates ownership, and writes the previous/new owner and reporting-team evidence in the same transaction. The previous owner loses write authority. Pending AI work and unsettled SELL publishing block transfer so work cannot continue under a stale owner. Confirmed packages must be reopened; archived packages must first return to active. Retained versions and SELL receipts remain unchanged.

Untouched, ungenerated drafts remain deletable. Once a transfer has been recorded, archive is used instead of deletion to preserve ownership evidence. This is an intentional refinement of the existing draft-delete exception, not removal of draft deletion.

## Work tracking and coverage

Target date, priority, blocker explanation/responsible person and remaining authoring hours are stored as separate, append-only scheduling snapshots. Tracking has its own revision and records the document identity, owner, document revision, actor and timestamp. Scheduling changes do not invalidate a confirmed document package or rewrite delivery LOE. Empty authoring effort means unknown; zero is an explicit estimate. A competing tracking change requires refresh rather than overwriting another person's update. Retained tracking history survives otherwise eligible draft deletion.

Temporary coverage records a planned return date and the same explicit ownership-transfer evidence as a permanent handoff. A return date is a prompt, not an automatic reassignment. Coverage starts and returns only on active draft/review-ready packages. Confirmed or archived packages still require the existing owner/administrator reopen or return-to-active action; managers do not gain content-editing authority. A second handoff is blocked until active temporary coverage is explicitly returned. Acknowledgement and return append new events; previous ownership, document versions and handoff history remain intact. Current reporting relationships and expected revision are rechecked when ownership changes. The current assigned SA acknowledges receipt; viewing the record alone does not count as acknowledgement.

Handoff, coverage start, coverage return and acknowledgement create durable Module065 events in the same transaction as the corresponding action. New-owner and responsible-manager recipients are resolved using current identity/reporting scope; transport, permissions, retries and delivery boundaries remain Module065's responsibility. Migration 120 adds policies with the existing test-only default and preserves administrator configuration on replay. No email or Teams message is sent by applying a migration or running a PR test.

## Generation and review

The current governed generation engine, configured provider order, durable phase checkpoints, progress reporting, and retry behavior remain in place. No extra AI request is required to materialize tasks from a successful generation result.

Detailed work-package steps become task proposals. Explicit task estimates are retained when they reconcile with package effort. Where only package effort exists, an exactly rounded initial distribution is shown with its estimate basis; it is not represented as an independently measured task estimate. Missing/conflicting effort stays unknown. New proposals require explicit SA review. Previously saved task descriptions, notes, and allocations survive regeneration, with review acknowledgement reset for the changed scope. A separate proposal preview allows deliberate replacement of a phase's task estimates.

Legacy saved scope can propose task rows and allocations from its existing phase estimate. Manual task editing remains available. Review errors identify the phase/task and provide a direct link instead of only saying that all five phases need review.

The SOW contains customer-facing scope, high-level execution approach, deliverables, responsibilities, and acceptance. Detailed task instructions and estimating notes belong in the GSD. Standard and Toyota/Hyundai exports carry detailed task allocations. Existing legacy Toyota/Hyundai phase-only records remain compatible; new task-based records require complete task review.

## After-hours

Each task has total hours, an optional regular/after-hours split, a required-work-window checkbox, rationale, and review status. Suggestions identify possible disruptive work but do not approve a customer schedule. Regular plus after-hours must equal total labor; summaries count the hours once. Premium billing, overtime employment policy, and maintenance-window scheduling are distinct and are not inferred from the checkbox.

## Template administration

Managers and administrators can stage versioned SOW DOCX and GSD XLSX candidates for Standard, Toyota, or Hyundai. The stored original bytes, SHA-256, uploader, program, version, and change notes are immutable. Visibility follows the template-owning manager's reporting scope; administrator-staged organization candidates are explicitly labeled shared.

Staged candidates are marked **awaiting mapping** and never replace an active exporter. A read-only original-content preview shows Word paragraphs, tables and content controls, or workbook sheets, cell addresses, formulas and stored cached values. It does not evaluate formulas or render the final exported layout, and reports truncation for large originals. The same scoped access and stored SHA-256 protect download and preview. Bounded OOXML validation rejects macros, external relationships, malformed packages, and excessive archive expansion. It validates document structure, not business correctness, formulas, rendering, or customer approval.

Current production exporters remain the source of documents: Standard uses the bundled workbook with its existing mapping; Toyota/Hyundai uses the existing generated profile. This preparation does not claim complete formula preservation by those exporters. Activating manager-maintained templates requires the approved originals, input-cell/field mapping, formula and row-expansion validation, document previews, authorized publication rules, and template-version pinning at confirmation. SOW layout/template substitution is also pending that mapping stage.

## Features retained

| Existing capability | Treatment |
| --- | --- |
| ConnectWise SELL readiness, submit, receipts and retries | Retained; no changes to SELL dispatch implementation or immutable version binding |
| Confirmed SOW DOCX and GSD XLSX downloads | Retained |
| Draft downloads | Retained and authenticated |
| Delete untouched, ungenerated draft | Retained; transferred audit evidence must be archived |
| Archive and return to active | Retained |
| Reopen confirmed package and reconfirm | Retained; earlier document versions unchanged |
| Version/register views | Retained |
| Autosave and conflict handling | Retained; transfer revision prevents stale-owner saves |
| Administrator View As | Read-only, including transfer and template staging |
| Existing Module065 email/Teams integration | Retained as the transport authority; handoff events use its governed queue and delivery policies |

## Rollout and acceptance

The prepared runner extends the existing protected Test migration chain with migrations 116–120 while retaining the original release authority, source identity, image integrity and private-network job controls. The PR does not change candidate admission or approval controls, and does not apply migrations. New SQL inventory entries remain review_required for Production.

See [the concrete UAT rollout and recovery plan](sa-workspace-uat-rollout.md). PR checks execute the generated migration entrypoint and feature behavior in disposable PostgreSQL. Installed UAT still needs real Systems and Collaboration/Networking identity scopes, the retained confirm/download/reopen/archive/SELL lifecycle, and configured Module065 transport. User approval must precede merge or dispatch because the existing main-branch supervisor may initiate UAT after merge.

## Remaining requirements

- Approved Standard and Toyota/Hyundai originals, reviewed writable cell/field mappings and formula validation before any uploaded template becomes active.
- Controlled template activation, rollback to a prior approved version, per-team versus organization publication authority, and exact template-version pinning at confirmation.
- Structured missing-input questions before generation, and targeted phase revision with a full scope comparison, building on existing checkpoints.
- Dedicated stage-duration reporting based on lifecycle events, beyond existing status and last-update information.

These remain explicit follow-up requirements. Target dates, priorities, blockers, separate authoring effort, temporary coverage, receipt acknowledgement and governed handoff events are included in the current increment.

# Module 025: SA workspace redesign preparation

This PR establishes one workflow for Systems SAs and Collaboration and Networking SAs, each with their own reporting manager. It prepares source and migrations for review; it does not authorize or execute a deployment.

## Working model

One immutable SOW/GSD identity carries an editable working revision, current responsible SA, append-only events, retained confirmed document versions, and existing ConnectWise SELL submissions. Immutable means released versions and audit evidence cannot be overwritten through the application. Draft content can change, and each ownership handoff increments its revision. This is not a claim of external WORM storage or protection against a privileged database administrator.

The interface presents My Work, Team Work, and Templates. A selected record shows Define request, Generate draft, Review tasks and hours, Confirm package, and Download and hand off. The current backend lifecycle remains draft, review_ready, confirmed, and archived; the guided stages do not introduce a second source of lifecycle truth.

## Two SA teams and ownership

Current, active reporting relationships govern access, not editable department/team labels or inferred job titles. Each manager sees their scoped SAs. Team Work shows owner, team, customer, AE, delivery LOE, status, and last update. The bounded queue reports its total and tells the user when filters are needed; it does not present a limited page as a complete team workload.

An SA may transfer their own active, editable package to an active SA sharing the same reporting manager. A manager may transfer a scoped member's work to another scoped member under that reporting manager. Cross-team handoffs are not silently granted by the selector. Administrators retain existing support authority, but destinations still require the source owner's authoritative reporting-team relationship. A team lead's read visibility does not implicitly grant manager ownership-transfer power.

Transfers require an explicit destination, handoff reason, and expected revision. The operation serializes against generation, locks the record, revalidates ownership, and writes the previous/new owner and reporting-team evidence in the same transaction. The previous owner loses write authority. Pending AI work and unsettled SELL publishing block transfer so work cannot continue under a stale owner. Confirmed packages must be reopened; archived packages must first return to active. Retained versions and SELL receipts remain unchanged.

Untouched, ungenerated drafts remain deletable. Once a transfer has been recorded, archive is used instead of deletion to preserve ownership evidence. This is an intentional refinement of the existing draft-delete exception, not removal of draft deletion.

## Generation and review

The current governed generation engine, configured provider order, durable phase checkpoints, progress reporting, and retry behavior remain in place. No extra AI request is required to materialize tasks from a successful generation result.

Detailed work-package steps become task proposals. Explicit task estimates are retained when they reconcile with package effort. Where only package effort exists, an exactly rounded initial distribution is shown with its estimate basis; it is not represented as an independently measured task estimate. Missing/conflicting effort stays unknown. New proposals require explicit SA review. Previously saved task descriptions, notes, and allocations survive regeneration, with review acknowledgement reset for the changed scope. A separate proposal preview allows deliberate replacement of a phase's task estimates.

Legacy saved scope can propose task rows and allocations from its existing phase estimate. Manual task editing remains available. Review errors identify the phase/task and provide a direct link instead of only saying that all five phases need review.

The SOW contains customer-facing scope, high-level execution approach, deliverables, responsibilities, and acceptance. Detailed task instructions and estimating notes belong in the GSD. Standard and Toyota/Hyundai exports carry detailed task allocations. Existing legacy Toyota/Hyundai phase-only records remain compatible; new task-based records require complete task review.

## After-hours

Each task has total hours, an optional regular/after-hours split, a required-work-window checkbox, rationale, and review status. Suggestions identify possible disruptive work but do not approve a customer schedule. Regular plus after-hours must equal total labor; summaries count the hours once. Premium billing, overtime employment policy, and maintenance-window scheduling are distinct and are not inferred from the checkbox.

## Template administration

Managers and administrators can stage versioned SOW DOCX and GSD XLSX candidates for Standard, Toyota, or Hyundai. The stored original bytes, SHA-256, uploader, program, version, and change notes are immutable. Visibility follows the template-owning manager's reporting scope; administrator-staged organization candidates are explicitly labeled shared.

Staged candidates are marked **awaiting mapping** and never replace an active exporter. Bounded OOXML validation rejects macros, external relationships, malformed packages, and excessive archive expansion. It validates document structure, not business correctness, formulas, rendering, or customer approval.

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
| Existing Microsoft 065 integration | Retained; this PR does not enable delivery or send messages |

## Rollout and acceptance

1. Review the feature source, migration116 transfer guard, and migration117 candidate catalog. Run behavioral and export tests, especially negative team access, concurrent transfer, draft deletion, and immutable evidence.
2. Apply116/117 through a separately reviewed protected-UAT migration runner update before enabling these capabilities. Existing deployment/candidate/approval control files and migration runners are unchanged in this preparation PR. Missing migrations result in explicit unavailable states rather than partial ownership writes.
3. Validate with an actual Systems SA/manager and Collaboration/Networking SA/manager: transfer, previous-owner stale save, cross-team read/transfer rejection, proposal review, after-hours allocation, confirm/download/reopen/archive and SELL version retention.
4. Validate approved customer templates and implement activation/version pinning before promising arbitrary template exports.

## Recommended next increments

- Target completion date, priority, blocker owner, and stage aging so managers can act on delays.
- Optional temporary coverage/return date and a handoff acknowledgement; notify the new owner and responsible manager through Module065 after an explicit delivery policy is approved.
- Authoring workload estimates kept separate from engineering delivery LOE.
- Controlled template activation, rollback to a prior approved version, and per-team versus organization publication authority.
- Structured missing-input questions before generation, and targeted phase revision with a full scope comparison, building on existing checkpoints rather than replacing the AI router.

These are explicit follow-up requirements, not capabilities claimed by this preparation PR.

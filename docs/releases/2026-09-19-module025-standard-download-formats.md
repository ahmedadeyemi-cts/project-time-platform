# Module 025 standard download formats

Standard GSD uses a sanitized copy of the supplied 12-sheet General Services
Delivery workbook. Summary C11:C16 map customer, project, contract (FP/T&M), AE,
SA and SAA from saved engagement fields. Each delivery phase receives its saved
activities and technical tasks. Reviewed phase hours appear once, with formulas
linking phase totals to the summary and phase breakdown. Fractional hours remain
exact, and task lists expand beyond the original 96-row capacity.

Reviewed task descriptions, hours and internal notes are stored per phase. Legacy
phase-only estimates remain available for draft review until task allocations are
completed. Engineering roles, rates, overtime, travel and reserve inputs remain
blank when unknown. No sample contacts, prices, SKUs or automatic management
surcharges are inherited.
The supplied workbook's layout is retained; commercial calculations are withheld
where the application has no authoritative input. Existing HAEA exports retain
their separate profile.

SOW Word downloads embed the existing US Signal logo in a repeating letterhead,
use consistent black Arial text and headings, and include page-numbered footers.
Saved scope and authorization rules are unchanged.

Formatting uses saved data and makes no model calls. Retained immutable versions
remain byte-for-byte unchanged; new export formatting applies when creating new
artifacts, including newly confirmed versions. Downloading an already retained
version will still return its original format.

Validation: standalone offline C# export tests cover metadata, both contract
mappings, unknown contacts, formula-looking text, fractional totals, long task
lists, template data removal, HAEA isolation and DOCX package branding. The test
project links the real exporter code and embedded assets, without API or model
clients. Rendered synthetic documents are used for local visual review.

Deployment hold: do not merge automatically. Application changes under
Module025 can trigger Protected UAT generation on main. This PR prepares exports
only; the user's no-generation hold still applies.

## Reviewed task estimates and customer documents

Task estimates are now editable within each phase: stable task ID, description,
reviewed hours and internal notes. The existing sow_sections JSON stores reviewed
allocations by phase, within the same authorized revision-checked save transaction;
no schema migration is needed. Complete task hours calculate final phase hours on
the server. Null hours remain incomplete; explicit zero is distinct from unknown.
Legacy clients omitting task allocations preserve existing tasks. A new generation
replaces source sections and therefore requires task allocations to be reviewed
again. The generation engine itself is unchanged.

The standard profile now requires complete task allocations that reconcile to
phase totals before final confirmation. Draft downloads are separate authenticated
read-only endpoints, carry a DRAFT label, and do not create retained versions or
call providers. Existing confirmed and archived documents keep their retained
bytes. Missing project/customer/contract/SA/AE/SAA information appears in the editor
readiness checklist; draft downloads remain available after autosave.

The customer SOW contains reviewed task scope and phase totals, without AI-suggested
hours, internal estimate rationale, internal task notes or open-question review
lists. The GSD retains these estimating details. Resource roles, overtime, reserve,
travel, billing rates and SKU pricing still require authoritative inputs; they
are not inferred from task hours or the reference workbook.

Synthetic UAT fixtures now explicitly review phase work packages and preserve
project names before confirmation. No generation or deployed acceptance run was
started. The deployment hold above continues to apply: a generation-free deployment
path must be reviewed before merging this application change.

## Authorized generation-free Protected UAT release

The owner authorized merging and prioritizing Protected UAT deployment on September 19, then approved the recommended generation-free deployment repair. The automatic Module025 supervisor now selects `sow_exports`. It retains exact-main admission, the protected Test environment, immutable API/web images, migration verification, deployment identity and health checks, concurrency, sealed admissions and existing rollback behavior.

The new acceptance verifies a normal SA can create one synthetic draft, save two fractional-hour tasks in each phase, read back reconciled totals, open the actual workspace, download authenticated branded draft files, reject anonymous downloads, and archive its own record. Its API request allowlist blocks generation, confirmation, SELL publication and unrelated records. My Role browser acceptance remains required. This scope does not qualify generation quality, final confirmation or retained-version publication. Existing `full` and `sow_role` acceptance modes remain available by explicit selection.

No AI provider calls are required by this deployment path. Runtime recovery and provider qualification are rejected with `sow_exports`. Production and provider configuration are unchanged.

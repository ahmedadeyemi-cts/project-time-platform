# Contract: module025-output-standards

- **Status:** frozen v1
- **Owner:** Module 025 SOW/GSD document exporters
  (`Module025SowGsdDocumentExporter`, `Module025StandardGsdExporter`) and the
  golden-sample/standards test guardrail (`tests/Module025ExportTests`).

## Purpose

Freezes the **observable output-format standards** of the generated customer
deliverables so that later work — an in-app preview, and (future phases) making
uploaded template files actually drive the exporters — cannot silently regress
"what a US Signal SOW/GSD looks like." This is a *format/fidelity* contract, distinct
from the *content/citation* contract in [module025-reference-source].

These facts were verified against the live exporters (Sept 2026); this contract records
them as the frozen baseline, it does not invent new formatting.

## Exposes

Guarantees any generated SOW `.docx` / GSD `.xlsx` MUST satisfy (v1 baseline):

**SOW `.docx`** (hand-built OpenXML WordprocessingML, zipped):
- Body/Normal font **Arial**, color **black `000000`**, body size **11pt** (`w:sz=22`
  half-points). Headings: Title 19pt, Subtitle 13pt, Heading1 15pt, Heading2 12pt.
- US Signal **letterhead** (embedded PNG logo in the header) and a right-aligned
  **footer** "US Signal | Statement of Work | Page N".
- US Letter page size, one Heading1 section per phase in the order
  **Plan → Design → Implement → Validate → Release**, each with the standard subsections
  (Execution Approach / Deliverables / Responsibilities / Prerequisites / Dependencies /
  Assumptions / Acceptance / Validation / Risks).
- Draft output carries a visible **"DRAFT - Not approved"** marker; confirmed output does not.
- No customer-identifying leakage beyond the engagement's own fields; formula-injection-safe
  (customer text stored as literals, never spreadsheet/Word field formulas).

**GSD `.xlsx`** (ClosedXML):
- Default `standard_gsd` program fills the bundled **12-sheet** `Module025StandardGsd.xlsx`
  template: `Summary`, `Phase Breakdown`, `Totals Sheet`, `SELL SKUs`, `Plan`, `Design`,
  `Implement`, `Validate`, `Release`, `Architect Notes`, `Gotcha Items`,
  `Assumptions Responsibilities`. Non-standard (HAEA) programs generate from scratch.
- Whole-workbook font **Arial**; reviewed hours reconcile with no invented
  allocations/rates; role/pricing cells carry the "REQUIRES REVIEW / APPROVED RATES"
  guards; draft output blanks partial totals and marks `E1` "DRAFT".

## Consumes

- The live exporter output (this contract asserts *on* it; it does not call anything new).
- `Assets/Templates/Module025StandardGsd.xlsx` (embedded manifest resource).

## Schema / wire

- A machine-checkable **standards spec** at `docs/module025-sow-gsd-output-standards.md`
  enumerates each guarantee above with a stable id.
- **Golden reference samples** produced from the live exporters, checked into the repo
  under a stable path, regenerated deterministically.
- `tests/Module025ExportTests` asserts every guarantee id against freshly generated output.

## Versioning

Frozen at **v1**. Changes are **additive only** — a breaking change is a NEW contract,
not an edit (framework-spec §4.3). A deliberate, approved change to the letterhead, fonts,
sheet set, or phase structure is a **v2 contract** plus a regenerated golden sample and an
ADR — never a silent edit. Consumers that must not break this: the exporters themselves, the
in-app preview (must render the same artifact bytes it will download), and any future
"uploaded template drives the export" work.

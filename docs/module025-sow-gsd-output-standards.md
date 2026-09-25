# Module 025 SOW/GSD Output Standards

Machine-checkable enumeration of the frozen v1 output-format guarantees in
[`contracts/module025-output-standards.md`](../contracts/module025-output-standards.md).
Each guarantee has a **stable id**. Every id maps to a passing assertion in
`tests/Module025ExportTests` (search the id tag, e.g. `// [SOW-FONT-01]`), which
runs the live exporters offline (`modelCalls=0`) and fails the build on drift.

All values below were sourced from the live exporter code, not invented:

- SOW `.docx`: `src/backend/ProjectTime.Api/Modules/Module025SowGsdDocumentExporter.cs`
- GSD `.xlsx` (standard program): `src/backend/ProjectTime.Api/Modules/Module025StandardGsdExporter.cs`
- GSD bundled template: `src/backend/ProjectTime.Api/Assets/Templates/Module025StandardGsd.xlsx`

Golden reference samples produced by the live exporters live under
[`docs/samples/module025/`](samples/module025/) and are regenerated deterministically
via `dotnet run --project tests/Module025ExportTests -- --emit-samples docs/samples/module025`.

Word half-point note: `w:sz` values are in half-points, so a point size *P* is `w:sz = 2P`
(e.g. 11pt body = `w:sz="22"`).

---

## SOW `.docx` guarantees

### SOW-FONT-01 — Typography
- **Guarantee:** Body/Normal font is **Arial**, color black **`000000`**, body size **11pt**
  (`w:sz="22"`). Headings: **Title 19pt** (`w:sz="38"`), **Subtitle 13pt** (`w:sz="26"`),
  **Heading1 15pt** (`w:sz="30"`), **Heading2 12pt** (`w:sz="24"`).
- **Observable value:** `word/styles.xml` `docDefaults` and `Normal` carry
  `w:rFonts w:ascii="Arial"`, `w:color w:val="000000"`, `w:sz w:val="22"`; the Title/Subtitle/
  Heading1/Heading2 styles carry `w:sz` of `38`/`26`/`30`/`24` respectively.
- **Provenance:** `StylesXml()` in `Module025SowGsdDocumentExporter.cs`.

### SOW-LETTERHEAD-01 — US Signal letterhead header
- **Guarantee:** The header embeds the US Signal PNG logo.
- **Observable value:** package part `word/media/us-signal.png` exists; `word/header1.xml`
  is a `w:hdr` with a `w:drawing` picture referencing relationship `rIdLogo`, and
  `word/_rels/header1.xml.rels` maps `rIdLogo` to `media/us-signal.png`.
- **Provenance:** `LetterheadXml()` + header rels + the `word/media/us-signal.png` write in
  `CreateSowDocx` (`Module025SowGsdDocumentExporter.cs`); bytes from `InvoiceBrandingAssets.LoadPng()`.

### SOW-FOOTER-01 — Right-aligned footer
- **Guarantee:** A right-aligned footer reads **"US Signal | Statement of Work | Page N"**
  (the `N` is the Word `PAGE` field).
- **Observable value:** `word/footer1.xml` is a `w:ftr` with `w:jc w:val="right"`, literal text
  `US Signal | Statement of Work | Page ` followed by `<w:fldSimple w:instr="PAGE"/>`.
- **Provenance:** the `word/footer1.xml` entry in `CreateSowDocx`.

### SOW-PAGE-01 — US Letter page size
- **Guarantee:** US Letter page size (8.5in x 11in).
- **Observable value:** `word/document.xml` `w:sectPr/w:pgSz` is `w:w="12240" w:h="15840"` (twips).
- **Provenance:** the `sectPr` block in `CreateSowDocx`.

### SOW-PHASES-01 — Phase order and standard subsections
- **Guarantee:** One Heading1 section per phase in the order
  **Plan → Design → Implement → Validate → Release**, each with the standard subsections:
  Execution Approach / Deliverables / Responsibilities (US Signal + Customer) / Prerequisites /
  Dependencies / Assumptions / Acceptance / Validation / Risks.
- **Observable value:** the phase Heading1 paragraphs appear in Plan, Design, Implement, Validate,
  Release order; per-phase Heading2 labels are drawn from
  `Execution Approach`, `Deliverables`, `US Signal Responsibilities`, `Customer Responsibilities`,
  `Prerequisites`, `Dependencies`, `Assumptions`, `Acceptance Criteria`, `Validation Steps`,
  `Risks / Considerations` (each rendered only when the phase has content for it).
- **Provenance:** the `model.Phases.OrderBy(SortOrder)` loop, `PhaseLabel(...)`, and the
  `AppendDetailedSection(...)` calls in `CreateSowDocx`.

### SOW-DRAFT-01 — Draft marker
- **Guarantee:** Draft output carries a visible **"DRAFT - Not approved"** marker; confirmed
  output does not.
- **Observable value:** `word/document.xml` from `CreateSowDocx(model, draft: true)` contains
  `DRAFT - Not approved for customer acceptance`; the same for `draft: false` does not contain
  `DRAFT - Not approved`.
- **Provenance:** the `if (draft) BodyParagraph(body, "DRAFT - Not approved for customer acceptance", "Strong")`
  line in `CreateSowDocx`.

---

## GSD `.xlsx` guarantees

### GSD-SHEETS-01 — Standard 12-sheet layout
- **Guarantee:** The default `standard_gsd` program fills the bundled 12-sheet template with sheets,
  in order: `Summary`, `Phase Breakdown`, `Totals Sheet`, `SELL SKUs`, `Plan`, `Design`,
  `Implement`, `Validate`, `Release`, `Architect Notes`, `Gotcha Items`,
  `Assumptions Responsibilities`.
- **Observable value:** the generated workbook's worksheet names equal that sequence verbatim.
- **Provenance:** `Module025StandardGsdExporter.Create` fills the bundled
  `Assets/Templates/Module025StandardGsd.xlsx`; dispatched from `CreateGsdXlsx` when
  `GsdTemplateKey == StandardGsdTemplateKey`.

### GSD-FONT-01 — Whole-workbook Arial
- **Guarantee:** Every sheet in the workbook uses font **Arial**.
- **Observable value:** each worksheet's `Style.Font.FontName == "Arial"`.
- **Provenance:** the `foreach (var sheet in book.Worksheets) sheet.Style.Font.FontName = "Arial"`
  loop in `Module025StandardGsdExporter.Create`.

### GSD-GUARDS-01 — Review/rate guard cells
- **Guarantee:** Role/pricing cells carry the "REQUIRES REVIEW / APPROVED RATES" guards; no
  invented allocations or rates.
- **Observable value:** `Totals Sheet!B3` == `RESOURCE ALLOCATION REQUIRES REVIEW`,
  `Totals Sheet!B29` == `PRICING REQUIRES APPROVED RATES`; `SELL SKUs` carries
  `No SKU, rate or price is inferred from the reference workbook.`; `Phase Breakdown` role cells
  read `Role allocation pending`.
- **Provenance:** the `totals.Cell("B3")` / `totals.Cell("B29")`, `skus.Cell("D5")`, and
  `breakdown.Cell(...).Value = "Role allocation pending"` writes in `Module025StandardGsdExporter.Create`.

### GSD-DRAFT-01 — Draft marker and blanked partial totals
- **Guarantee:** Draft output marks `Summary!E1` "DRAFT" and blanks partial/unreconciled totals so
  they never read as a final project number.
- **Observable value:** `CreateGsdXlsx(model, draft: true)` yields `Summary!E1` starting with
  `DRAFT`; when task allocations are only partial, `Summary!F4` evaluates to an empty string.
- **Provenance:** `if (draft) summary.Cell("E1").Value = "DRAFT - General Services Delivery Worksheet"`
  and the `F4` guard formula `IF(COUNT('Totals Sheet'!C15:C19)=5,SUM(...),"")` in
  `Module025StandardGsdExporter.Create`.

### GSD-RECONCILE-01 — Reviewed hours reconcile without surcharges
- **Guarantee:** Reviewed hours reconcile with no invented template project-management/reserve/travel
  allowance added to the SA's reviewed phase totals.
- **Observable value:** for a reconciled standard export, `Summary!F4` equals the sum of the phase
  `FinalHours` (`model.FinalHours`), not a template-inflated number.
- **Provenance:** the `summary.Cell("F4").FormulaA1` sum-of-phase-totals formula in
  `Module025StandardGsdExporter.Create` (evaluated via `EvaluateFormulasBeforeSaving`).

### GSD-HAEA-01 — HAEA from-scratch layout
- **Guarantee:** Non-standard (HAEA) programs generate a from-scratch layout, not the 12-sheet template.
- **Observable value:** `CreateGsdXlsx` for `GsdTemplateKey == HaeaGsdTemplateKey` yields a workbook
  whose first worksheet is named `HAEA GSD`.
- **Provenance:** the non-standard branch of `CreateGsdXlsx` (`workbook.AddWorksheet(special ? "HAEA GSD" ...)`)
  in `Module025SowGsdDocumentExporter.cs`.

---

## Cross-cutting safety guarantees

### SAFETY-INJECTION-01 — Formula-injection-safe literals
- **Guarantee:** Customer-supplied text is stored as literal cell values, never as spreadsheet/Word
  field formulas.
- **Observable value:** a customer name of `=HYPERLINK("https://example.invalid")` lands in
  `Summary!C11` with `HasFormula == false` (literal text).
- **Provenance:** ClosedXML `.Value =` string assignment in `Module025StandardGsdExporter.Create`
  (never `.FormulaA1`); verified by the injection case in `tests/Module025ExportTests`.

### SAFETY-LEAK-01 — No reference/customer data leakage
- **Guarantee:** No customer-identifying leakage beyond the engagement's own fields, including in
  package metadata and reference-template residue.
- **Observable value:** the generated GSD package XML and cell text contain none of the known
  reference-workbook markers (`Poudre`, `Stephanie`, `McDonald`, `Shaffer`, `206140`,
  `sharepoint.com`, `OneNeck`, `Dashboard 1 migration`).
- **Provenance:** the `--prepare-template` sanitization plus the leakage sweeps in
  `tests/Module025ExportTests`.

---

## Id → test traceability

| Stable id | Assertion tag in `tests/Module025ExportTests/Program.cs` |
|---|---|
| SOW-FONT-01 | `// [SOW-FONT-01]` |
| SOW-LETTERHEAD-01 | `// [SOW-LETTERHEAD-01]` |
| SOW-FOOTER-01 | `// [SOW-FOOTER-01]` |
| SOW-PAGE-01 | `// [SOW-PAGE-01]` |
| SOW-PHASES-01 | `// [SOW-PHASES-01]` |
| SOW-DRAFT-01 | `// [SOW-DRAFT-01]` |
| GSD-SHEETS-01 | `// [GSD-SHEETS-01]` |
| GSD-FONT-01 | `// [GSD-FONT-01]` |
| GSD-GUARDS-01 | `// [GSD-GUARDS-01]` |
| GSD-DRAFT-01 | `// [GSD-DRAFT-01]` |
| GSD-RECONCILE-01 | `// [GSD-RECONCILE-01]` |
| GSD-HAEA-01 | `// [GSD-HAEA-01]` |
| SAFETY-INJECTION-01 | `// [SAFETY-INJECTION-01]` |
| SAFETY-LEAK-01 | `// [SAFETY-LEAK-01]` |
</content>
</invoke>

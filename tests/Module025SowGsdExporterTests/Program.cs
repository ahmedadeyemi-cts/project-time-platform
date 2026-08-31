using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using ProjectTime.Api.Modules;

var assertionCount = 0;

void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"ASSERTION_FAILED {label}");
    assertionCount++;
    Console.WriteLine($"ASSERTION_PASSED {label}");
}

var ownerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
var emptyJson = JsonDocument.Parse("{}").RootElement;

Module025EngagementRow MakeEngagement(
    string engagementNumber = "SOW-2026-000123",
    string customerName = "Acme Corp",
    string commercialModel = "time_and_materials",
    string customerProgram = "standard",
    string gsdTemplateKey = "standard_gsd",
    string serviceOverview = "Deploy a resilient hybrid cloud backup solution.",
    JsonElement? sowSections = null) => new(
    EngagementId: Guid.NewGuid(),
    EngagementNumber: engagementNumber,
    OwnerUserId: ownerId,
    OwnerDisplayName: "Jordan Rivera",
    OwnerDepartmentName: "Solutions Architecture",
    OwnerTeamName: "Enterprise",
    CustomerId: null,
    CustomerName: customerName,
    CustomerEntryMode: "directory",
    CommercialModel: commercialModel,
    CustomerProgram: customerProgram,
    GsdTemplateKey: gsdTemplateKey,
    AccountExecutiveUserId: null,
    AccountExecutiveName: "Sam Patel",
    ResaleUserId: null,
    ResaleName: "Morgan Lee",
    ServiceOverview: serviceOverview,
    SowSections: sowSections ?? emptyJson,
    AiMetadata: emptyJson,
    Status: "review_ready",
    IsActive: true,
    Revision: 2,
    LastGeneratedAt: DateTimeOffset.Parse("2026-08-20T00:00:00Z"),
    ConfirmedAt: null,
    ArchivedAt: null,
    CreatedAt: DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
    UpdatedAt: DateTimeOffset.Parse("2026-08-20T00:00:00Z"),
    Phases: Array.Empty<Module025PhaseRow>());

IReadOnlyList<Module025PhaseRow> MakeFivePhases()
{
    var codes = new[] { "plan", "design", "implement", "validate", "release" };
    var list = new List<Module025PhaseRow>();
    for (var i = 0; i < codes.Length; i++)
    {
        var code = codes[i];
        list.Add(new Module025PhaseRow(
            PhaseCode: code,
            SortOrder: i + 1,
            SuggestedHours: (i + 1) * 10m,
            FinalHours: (i + 1) * 10m + 2m,
            Objective: $"{code} objective",
            DetailedActivities: new[] { $"{code} activity" },
            TechnicalTasks: new[] { $"{code} tech task" },
            Deliverables: new[] { $"{code} deliverable" },
            CustomerResponsibilities: new[] { $"{code} customer responsibility" },
            UsSignalResponsibilities: new[] { $"{code} us signal responsibility" },
            Prerequisites: Array.Empty<string>(),
            Dependencies: new[] { $"{code} dependency" },
            Assumptions: new[] { $"{code} assumption" },
            OpenQuestions: Array.Empty<string>(),
            AcceptanceCriteria: new[] { $"{code} acceptance" },
            ValidationSteps: new[] { $"{code} validation" },
            Risks: Array.Empty<string>(),
            LoeRationale: $"{code} rationale",
            SourceCitationIds: Array.Empty<int>(),
            AiGenerated: true,
            UpdatedAt: DateTimeOffset.Parse("2026-08-20T00:00:00Z")));
    }
    return list;
}

string ReadZipEntry(byte[] docx, string path)
{
    using var stream = new MemoryStream(docx);
    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
    var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"missing zip entry {path}");
    using var reader = new StreamReader(entry.Open());
    return reader.ReadToEnd();
}

byte[] Docx(Module025EngagementRow engagement, IReadOnlyList<Module025PhaseRow> phases) =>
    Module025SowGsdDocumentExporter.CreateSowDocx(new Module025DocumentModel(
        engagement, phases, phases.Sum(p => p.SuggestedHours), phases.Sum(p => p.FinalHours)));

byte[] Xlsx(Module025EngagementRow engagement, IReadOnlyList<Module025PhaseRow> phases) =>
    Module025SowGsdDocumentExporter.CreateGsdXlsx(new Module025DocumentModel(
        engagement, phases, phases.Sum(p => p.SuggestedHours), phases.Sum(p => p.FinalHours)));

// ---- CreateSowDocx: structural shape ----

var phases = MakeFivePhases();
var engagement = MakeEngagement();
var documentXml = ReadZipEntry(Docx(engagement, phases), "word/document.xml");

Assert(documentXml.Contains(engagement.EngagementNumber), "docx_contains_engagement_number");
Assert(documentXml.Contains("Acme Corp"), "docx_contains_customer_name");
Assert(documentXml.Contains("Standard GSD"), "docx_default_gsd_profile_label_is_standard");

var planIdx = documentXml.IndexOf(">Plan<", StringComparison.Ordinal);
var designIdx = documentXml.IndexOf(">Design<", StringComparison.Ordinal);
var implementIdx = documentXml.IndexOf(">Implement<", StringComparison.Ordinal);
var validateIdx = documentXml.IndexOf(">Validate<", StringComparison.Ordinal);
var releaseIdx = documentXml.IndexOf(">Release<", StringComparison.Ordinal);
Assert(planIdx >= 0 && planIdx < designIdx && designIdx < implementIdx
    && implementIdx < validateIdx && validateIdx < releaseIdx,
    "docx_phase_headings_appear_in_plan_design_implement_validate_release_order");

Assert(documentXml.Contains("Reviewed level of effort: 12 hour(s). AI suggestion: 10 hour(s)."),
    "docx_plan_phase_shows_final_and_suggested_hours");

Assert(documentXml.Contains("This scope is configured as Time &amp; Materials."),
    "docx_time_and_materials_commercial_language");

Assert(documentXml.Contains("plan deliverable") && documentXml.Contains("release deliverable"),
    "docx_top_level_deliverables_fall_back_to_phase_deliverables_when_no_override");

// ---- CreateSowDocx: fixed-price commercial language ----

var fixedEngagement = MakeEngagement(commercialModel: "fixed");
var fixedDocumentXml = ReadZipEntry(Docx(fixedEngagement, phases), "word/document.xml");
Assert(fixedDocumentXml.Contains("This scope is configured as Fixed Price."),
    "docx_fixed_price_commercial_language");

// ---- CreateSowDocx: HAEA program template label ----

var haeaEngagement = MakeEngagement(
    customerProgram: "toyota",
    gsdTemplateKey: Module025SowGsdDocumentExporter.HaeaGsdTemplateKey);
var haeaDocumentXml = ReadZipEntry(Docx(haeaEngagement, phases), "word/document.xml");
Assert(haeaDocumentXml.Contains(Module025SowGsdDocumentExporter.HaeaGsdDisplayName),
    "docx_haea_program_shows_haea_gsd_profile_label");

// ---- CreateSowDocx: blank Service Overview falls back to "To be confirmed" ----

var blankOverviewEngagement = MakeEngagement(serviceOverview: "");
var blankOverviewXml = ReadZipEntry(Docx(blankOverviewEngagement, phases), "word/document.xml");
Assert(blankOverviewXml.Contains("To be confirmed"),
    "docx_blank_service_overview_falls_back_to_to_be_confirmed");

// ---- CreateSowDocx: SowSections override replaces phase-derived deliverables ----

var overrideSections = JsonDocument.Parse("""{"deliverables":["Custom deliverable A","Custom deliverable B"]}""").RootElement;
var overrideEngagement = MakeEngagement(sowSections: overrideSections);
var overrideXml = ReadZipEntry(Docx(overrideEngagement, phases), "word/document.xml");
Assert(overrideXml.Contains("Custom deliverable A") && overrideXml.Contains("Custom deliverable B"),
    "docx_sow_sections_override_supplies_top_level_deliverables");

// ---- CreateSowDocx: special characters are escaped into well-formed XML ----

var hostileEngagement = MakeEngagement(
    customerName: "Acme & <Sons> \"Co\"",
    serviceOverview: "<script>alert(1)</script> & more scope notes");
var hostileXml = ReadZipEntry(Docx(hostileEngagement, phases), "word/document.xml");
var parsesAsXml = true;
try
{
    XDocument.Parse(hostileXml);
}
catch (Exception)
{
    parsesAsXml = false;
}
Assert(parsesAsXml, "docx_escapes_special_characters_into_well_formed_xml");
Assert(!hostileXml.Contains("<script>"), "docx_does_not_emit_raw_script_tag");

// ---- CreateGsdXlsx: summary sheet ----

using (var workbook = new XLWorkbook(new MemoryStream(Xlsx(engagement, phases))))
{
    Assert(workbook.Worksheets.Any(sheet => sheet.Name == "GSD Summary"), "xlsx_default_summary_sheet_name");
    var summary = workbook.Worksheet("GSD Summary");
    Assert(summary.Cell("B4").GetString() == engagement.EngagementNumber, "xlsx_summary_engagement_number");
    Assert(summary.Cell("B5").GetString() == engagement.CustomerName, "xlsx_summary_customer_name");
    Assert(summary.Cell("B6").GetString() == "Time & Materials", "xlsx_summary_commercial_model_label");

    Assert(summary.Cell(14, 1).GetString() == "Plan", "xlsx_summary_first_phase_row_label");
    Assert(summary.Cell(14, 2).GetDouble() == 10d, "xlsx_summary_plan_suggested_hours");
    Assert(summary.Cell(14, 3).GetDouble() == 12d, "xlsx_summary_plan_final_hours");
    Assert(summary.Cell(14, 4).FormulaA1 == "C14-B14", "xlsx_summary_plan_variance_formula");

    Assert(summary.Cell(19, 1).GetString() == "Total", "xlsx_summary_total_row_label");
    Assert(summary.Cell(19, 2).FormulaA1 == "SUM(B14:B18)", "xlsx_summary_total_suggested_hours_formula");
    Assert(summary.Cell(19, 3).FormulaA1 == "SUM(C14:C18)", "xlsx_summary_total_final_hours_formula");
    Assert(summary.Cell(19, 4).FormulaA1 == "C19-B19", "xlsx_summary_total_variance_formula");

    var details = workbook.Worksheet("P-D-I-V-R Detail");
    Assert(details.Cell(4, 1).GetString() == "Plan", "xlsx_detail_first_row_phase_label");
    Assert(details.Cell(4, 2).GetString() == "Objective", "xlsx_detail_first_row_category");
    Assert(details.Cell(4, 3).GetString() == "plan objective", "xlsx_detail_first_row_value");
    Assert(details.Cell(4, 4).GetDouble() == 12d, "xlsx_detail_first_row_reviewed_hours");

    var scope = workbook.Worksheet("Scope & Assumptions");
    Assert(scope.Cell("B1").GetString() == engagement.ServiceOverview, "xlsx_scope_service_overview");
    var deliverableLines = scope.Cell("B3").GetString().Split('\n');
    Assert(deliverableLines.Length == 5, "xlsx_scope_deliverables_fall_back_to_all_five_phases");
}

// ---- CreateGsdXlsx: HAEA program uses the HAEA sheet name and title ----

using (var haeaWorkbook = new XLWorkbook(new MemoryStream(Xlsx(haeaEngagement, phases))))
{
    Assert(haeaWorkbook.Worksheets.Any(sheet => sheet.Name == "HAEA GSD"), "xlsx_haea_program_summary_sheet_name");
    var haeaSummary = haeaWorkbook.Worksheet("HAEA GSD");
    Assert(haeaSummary.Cell("A1").GetString() == Module025SowGsdDocumentExporter.HaeaGsdDisplayName,
        "xlsx_haea_program_summary_title");
}

// ---- CreateGsdXlsx: phase with no AI-supported detail falls back to a review-required row ----

var blankPhase = new Module025PhaseRow(
    PhaseCode: "plan",
    SortOrder: 1,
    SuggestedHours: 0m,
    FinalHours: 5m,
    Objective: "",
    DetailedActivities: Array.Empty<string>(),
    TechnicalTasks: Array.Empty<string>(),
    Deliverables: Array.Empty<string>(),
    CustomerResponsibilities: Array.Empty<string>(),
    UsSignalResponsibilities: Array.Empty<string>(),
    Prerequisites: Array.Empty<string>(),
    Dependencies: Array.Empty<string>(),
    Assumptions: Array.Empty<string>(),
    OpenQuestions: Array.Empty<string>(),
    AcceptanceCriteria: Array.Empty<string>(),
    ValidationSteps: Array.Empty<string>(),
    Risks: Array.Empty<string>(),
    LoeRationale: "",
    SourceCitationIds: Array.Empty<int>(),
    AiGenerated: false,
    UpdatedAt: DateTimeOffset.Parse("2026-08-20T00:00:00Z"));

using (var blankWorkbook = new XLWorkbook(new MemoryStream(Xlsx(engagement, new[] { blankPhase }))))
{
    var blankDetails = blankWorkbook.Worksheet("P-D-I-V-R Detail");
    Assert(blankDetails.Cell(4, 1).GetString() == "Plan", "xlsx_blank_phase_fallback_row_label");
    Assert(blankDetails.Cell(4, 2).GetString() == "Scope requires review", "xlsx_blank_phase_fallback_category");
    Assert(blankDetails.Cell(4, 3).GetString().Contains("must define the scope"), "xlsx_blank_phase_fallback_message");
    Assert(blankDetails.Cell(4, 4).GetDouble() == 5d, "xlsx_blank_phase_fallback_reviewed_hours");
}

// ---- CreateSowDocx: fractional hours are formatted without a trailing zero suffix ----

var fractionalPhase = new Module025PhaseRow(
    PhaseCode: "plan",
    SortOrder: 1,
    SuggestedHours: 3m,
    FinalHours: 7.25m,
    Objective: "fractional objective",
    DetailedActivities: Array.Empty<string>(),
    TechnicalTasks: Array.Empty<string>(),
    Deliverables: Array.Empty<string>(),
    CustomerResponsibilities: Array.Empty<string>(),
    UsSignalResponsibilities: Array.Empty<string>(),
    Prerequisites: Array.Empty<string>(),
    Dependencies: Array.Empty<string>(),
    Assumptions: Array.Empty<string>(),
    OpenQuestions: Array.Empty<string>(),
    AcceptanceCriteria: Array.Empty<string>(),
    ValidationSteps: Array.Empty<string>(),
    Risks: Array.Empty<string>(),
    LoeRationale: "",
    SourceCitationIds: Array.Empty<int>(),
    AiGenerated: false,
    UpdatedAt: DateTimeOffset.Parse("2026-08-20T00:00:00Z"));

var fractionalXml = ReadZipEntry(Docx(engagement, new[] { fractionalPhase }), "word/document.xml");
Assert(fractionalXml.Contains("Reviewed level of effort: 7.25 hour(s). AI suggestion: 3 hour(s)."),
    "docx_fractional_hours_formatted_without_trailing_zeros");

Console.WriteLine($"MODULE_025_SOW_GSD_EXPORTER_TEST=PASS assertions={assertionCount}");

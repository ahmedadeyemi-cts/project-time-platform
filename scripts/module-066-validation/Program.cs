using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using ProjectTime.Api.Modules;

const string ExpectedLogoHash = "c4fc4b33f744d065deeec531f393aa39996273e51eb946a452b1319e6e529183";
var monday = new DateOnly(2026, 7, 20);
var assertions = 0;

void Assert(string name, bool condition, string detail)
{
    assertions++;
    Console.WriteLine($"{name}={(condition ? "PASSED" : "FAILED")} — {detail}");
    if (!condition) throw new InvalidOperationException($"{name}: {detail}");
}

ProjectFlowHivePlanTaskInput Task(
    string wbs,
    int duration,
    string? parent = null,
    string? constraint = null,
    DateOnly? constraintDate = null,
    bool milestone = false,
    bool summary = false) =>
    new(
        Guid.NewGuid(),
        null,
        wbs,
        parent,
        $"Task {wbs}",
        null,
        milestone || summary ? 0 : duration,
        milestone,
        constraint ?? "ASAP",
        constraintDate,
        0m,
        summary ? 0m : duration * 8m,
        "not_started",
        IsSummary: summary,
        Phase: summary ? "Plan" : null);

ProjectFlowHivePlanRequest Plan(
    IReadOnlyList<ProjectFlowHivePlanTaskInput> tasks,
    IReadOnlyList<ProjectFlowHiveDependencyInput>? dependencies = null,
    IReadOnlyList<ProjectFlowHivePlanAssignmentInput>? assignments = null) =>
    new(
        Guid.NewGuid(),
        "PP-066",
        "Project FlowHive validation",
        "US Signal",
        "Governed schedule validation",
        "Draft 1",
        monday,
        new DateOnly(2026, 8, 31),
        tasks,
        dependencies ?? [],
        assignments ?? [],
        "GSD-1",
        "SOW-1",
        null);

ProjectFlowHiveScheduleResult Relationship(
    string? type,
    int lag = 0,
    int predecessorDuration = 4,
    int successorDuration = 2,
    string predecessorWbs = "1",
    string successorWbs = "2",
    string? predecessorConstraint = null,
    DateOnly? predecessorConstraintDate = null)
{
    return ProjectFlowHiveScheduleEngine.Calculate(Plan(
        [
            Task("1", predecessorDuration, constraint: predecessorConstraint, constraintDate: predecessorConstraintDate),
            Task("2", successorDuration)
        ],
        [new ProjectFlowHiveDependencyInput(predecessorWbs, successorWbs, type, lag)]));
}

var linearPlan = Plan(
    [Task("1", 2), Task("2", 3)],
    [new ProjectFlowHiveDependencyInput(" 1 ", " 2 ", null, 0)]);
var linear = ProjectFlowHiveScheduleEngine.Calculate(linearPlan);
Assert("MODULE_066_TEST_LINEAR_VALID", linear.Valid, "trimmed WBS and blank type calculate as FS");
Assert("MODULE_066_TEST_LINEAR_FINISH", linear.ProjectFinishDate == new DateOnly(2026, 7, 24), "two-day plus three-day chain finishes Friday");
Assert("MODULE_066_TEST_LINEAR_DURATION", linear.ScheduledWorkingDays == 5, "linear chain spans five working days");
Assert("MODULE_066_TEST_LINEAR_CRITICAL", linear.Tasks.Count == 2 && linear.Tasks.All(task => task.IsCritical), "both linear tasks are critical");
Assert("MODULE_066_TEST_LINEAR_SUCCESSOR", linear.Tasks.Single(task => task.WbsNumber == "2").StartDate == new DateOnly(2026, 7, 22), "FS successor begins after predecessor finish");

var summaryPlan = Plan(
    [Task("1", 0, summary: true), Task("1.1", 2, parent: "1")]);
var summarySchedule = ProjectFlowHiveScheduleEngine.Calculate(summaryPlan);
var summaryRow = summarySchedule.Tasks.Single(task => task.WbsNumber == "1");
Assert("MODULE_066_TEST_PHASE_SUMMARY", summarySchedule.Valid && summaryRow.IsSummary, "phase summary is retained as a schedule rollup row");
Assert("MODULE_066_TEST_PHASE_ROLLUP_DATES", summaryRow.StartDate == monday && summaryRow.EndDate == new DateOnly(2026, 7, 21), "phase dates roll up from executable children");

var windowExceeded = ProjectFlowHiveScheduleEngine.Calculate(
    Plan([Task("1", 2)]) with { ProjectEndDate = monday });
Assert("MODULE_066_TEST_SELECTED_END_DATE", !windowExceeded.Valid && windowExceeded.Issues.Any(issue => issue.Code == "project_end_exceeded"), "selected PM end date is enforced by the deterministic schedule");

var fs = Relationship("FS");
Assert("MODULE_066_TEST_FS", fs.Tasks.Single(task => task.WbsNumber == "2").StartDate == new DateOnly(2026, 7, 24), "FS offset uses predecessor duration");

var ss = Relationship("SS", lag: 1);
Assert("MODULE_066_TEST_SS", ss.Tasks.Single(task => task.WbsNumber == "2").StartDate == new DateOnly(2026, 7, 21), "SS lead/lag is start-relative");

var ff = Relationship("FF");
var ffSuccessor = ff.Tasks.Single(task => task.WbsNumber == "2");
Assert("MODULE_066_TEST_FF", ffSuccessor.StartDate == new DateOnly(2026, 7, 22) && ffSuccessor.EndDate == new DateOnly(2026, 7, 23), "FF aligns successor finish to predecessor finish");

var sf = Relationship(
    "SF",
    predecessorConstraint: "MSO",
    predecessorConstraintDate: new DateOnly(2026, 7, 22));
var sfSuccessor = sf.Tasks.Single(task => task.WbsNumber == "2");
Assert("MODULE_066_TEST_SF", sfSuccessor.StartDate == new DateOnly(2026, 7, 21) && sfSuccessor.EndDate == new DateOnly(2026, 7, 22), "SF aligns successor finish to predecessor start");

var cycle = ProjectFlowHiveScheduleEngine.Validate(Plan(
    [Task("1", 1), Task("2", 1)],
    [
        new ProjectFlowHiveDependencyInput("1", "2", "FS", 0),
        new ProjectFlowHiveDependencyInput("2", "1", "FS", 0)
    ]));
Assert("MODULE_066_TEST_CYCLE", !cycle.Valid && cycle.Issues.Any(issue => issue.Code == "dependency_cycle"), "cycle is rejected");

var hierarchy = ProjectFlowHiveScheduleEngine.Validate(Plan(
    [
        Task("1", 1),
        Task("1.2", 1),
        Task("1.2.3", 1, parent: "1")
    ]));
Assert("MODULE_066_TEST_PARENT_REQUIRED", hierarchy.Issues.Any(issue => issue.Code == "parent_required"), "nested WBS requires an immediate parent");
Assert("MODULE_066_TEST_PARENT_IMMEDIATE", hierarchy.Issues.Any(issue => issue.Code == "parent_hierarchy_mismatch"), "grandchild cannot skip its immediate parent");

Assert("MODULE_066_TEST_WEEKEND_FORWARD", ProjectFlowHiveScheduleEngine.AddWorkingDays(new DateOnly(2026, 7, 24), 1) == new DateOnly(2026, 7, 27), "Friday plus one working day is Monday");
Assert("MODULE_066_TEST_WEEKEND_REVERSE", ProjectFlowHiveScheduleEngine.AddWorkingDays(new DateOnly(2026, 7, 20), -1) == new DateOnly(2026, 7, 17), "Monday minus one working day is Friday");

var moduleAssembly = typeof(ProjectFlowHiveScheduleEngine).Assembly;
var artifactType = moduleAssembly.GetType("ProjectTime.Api.Modules.ProjectFlowHiveArtifactRenderer", throwOnError: true)!;
var brandType = moduleAssembly.GetType("ProjectTime.Api.Modules.ProjectFlowHiveBrandAssets", throwOnError: true)!;
var aiType = moduleAssembly.GetType("ProjectTime.Api.Modules.ProjectFlowHiveAiRequestFactory", throwOnError: true)!;
var artifactPlan = linearPlan with
{
    Tasks = linearPlan.Tasks!.Select((task, index) => task with
    {
        Comments = index == 0 ? "Review comment" : "Confirm dependency",
        Notes = index == 0 ? "Execution note" : "Validation note"
    }).ToArray(),
    Assignments = [new ProjectFlowHivePlanAssignmentInput("1", Guid.NewGuid(), "Engineer One", 100m, 16m)]
};
var artifactSchedule = ProjectFlowHiveScheduleEngine.Calculate(artifactPlan);
var artifactRequest = new ProjectFlowHiveArtifactRequest(artifactPlan, "Project FlowHive validation", "internal", false, true);

byte[] InvokeArtifact(string methodName) => (byte[])artifactType
    .GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, [artifactRequest, artifactSchedule])!;

var logo = (byte[])brandType
    .GetProperty("LogoJpeg", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
    .GetValue(null)!;
var logoHash = Convert.ToHexString(SHA256.HashData(logo)).ToLowerInvariant();
Assert("MODULE_066_TEST_LOGO_HASH", logoHash == ExpectedLogoHash, "embedded US Signal logo matches governed checksum");

var pdf = InvokeArtifact("BuildPdf");
Assert("MODULE_066_TEST_PDF_HEADER", Encoding.ASCII.GetString(pdf, 0, Math.Min(pdf.Length, 32)).StartsWith("%PDF-1.7", StringComparison.Ordinal), "PDF preview has a valid governed header");
Assert("MODULE_066_TEST_PDF_LOGO", Contains(pdf, logo), "PDF embeds the exact governed US Signal logo bytes");
var pdfText = Encoding.ASCII.GetString(pdf);
Assert("MODULE_066_TEST_PDF_PLANNER_COLUMNS", pdfText.Contains("TASK NAME", StringComparison.Ordinal) && pdfText.Contains("DURATION IN DAYS", StringComparison.Ordinal) && pdfText.Contains("PROGRESS", StringComparison.Ordinal) && pdfText.Contains("PREDECESSOR", StringComparison.Ordinal) && pdfText.Contains("COMMENTS", StringComparison.Ordinal) && pdfText.Contains("NOTES", StringComparison.Ordinal) && pdfText.Contains("ASSIGNED IDENTITY", StringComparison.Ordinal), "PDF contains the approved Planner columns");
Assert("MODULE_066_TEST_PDF_COMMENTS_NOTES_IDENTITY", pdfText.Contains("Review comment", StringComparison.Ordinal) && pdfText.Contains("Execution note", StringComparison.Ordinal) && pdfText.Contains("Engineer One", StringComparison.Ordinal), "PDF exports task comments, notes, and assigned identity");

var excel = InvokeArtifact("BuildExcel");
Assert("MODULE_066_TEST_XLSX_HEADER", excel.Length > 4 && excel[0] == (byte)'P' && excel[1] == (byte)'K', "Excel preview is an Open XML ZIP package");
using (var workbookStream = new MemoryStream(excel, writable: false))
using (var archive = new ZipArchive(workbookStream, ZipArchiveMode.Read, leaveOpen: false))
{
    var imageEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase));
    Assert("MODULE_066_TEST_XLSX_LOGO_ENTRY", imageEntry is not null, "Excel preview contains a media entry");
    using var imageStream = imageEntry!.Open();
    using var imageBytes = new MemoryStream();
    imageStream.CopyTo(imageBytes);
    var imageHash = Convert.ToHexString(SHA256.HashData(imageBytes.ToArray())).ToLowerInvariant();
    Assert("MODULE_066_TEST_XLSX_LOGO_HASH", imageHash == ExpectedLogoHash, "Excel embeds the exact governed US Signal logo bytes");
}
using (var workbookStream = new MemoryStream(excel, writable: false))
using (var workbook = new XLWorkbook(workbookStream))
{
    var worksheet = workbook.Worksheet("Schedule");
    var headers = string.Join("|", Enumerable.Range(1, 11).Select(column => worksheet.Cell(1, column).GetString()));
    Assert("MODULE_066_TEST_XLSX_PLANNER_COLUMNS", headers == "WBS|Task Name|Start Date|End Date|Duration in Days|Progress|Predecessor|Type|Comments|Notes|Assigned Identity", "Excel Schedule uses the exact approved Planner column order");
    Assert("MODULE_066_TEST_XLSX_COMMENTS_NOTES_IDENTITY", worksheet.Cell(2, 9).GetString() == "Review comment" && worksheet.Cell(2, 10).GetString() == "Execution note" && worksheet.Cell(2, 11).GetString() == "Engineer One", "Excel exports task comments, notes, and assigned identity");
}

var aiPreview = aiType
    .GetMethod("Preview", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, [new ProjectFlowHiveAiDraftPreviewRequest(linearPlan, "GSD source", "SOW source", "Prepare a governed draft")])!;
var aiJson = JsonSerializer.Serialize(aiPreview);
Assert("MODULE_066_TEST_AI_ROUTE", aiJson.Contains("claude", StringComparison.Ordinal) && aiJson.Contains("openai", StringComparison.Ordinal) && aiJson.Contains("local_template", StringComparison.Ordinal), "AI preview records shared Module 064 provider order");
Assert("MODULE_066_TEST_AI_LOCK", aiJson.Contains("private_model_execution_not_registered", StringComparison.Ordinal) && aiJson.Contains("\"executionEnabled\":false", StringComparison.Ordinal), "AI preview cannot execute a provider");

Console.WriteLine();
Console.WriteLine($"MODULE_066_EXECUTABLE_CHECKS={assertions}");
Console.WriteLine("MODULE_066_EXECUTABLE_CONTRACT=PASSED");

static bool Contains(byte[] haystack, byte[] needle)
{
    if (needle.Length == 0 || haystack.Length < needle.Length) return false;
    for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
    {
        if (haystack.AsSpan(offset, needle.Length).SequenceEqual(needle)) return true;
    }
    return false;
}

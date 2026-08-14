using System.Reflection;
using System.Text;
using ProjectTime.Api.Modules;

var plan = new ProjectFlowHivePlanRequest(
    ProjectId: Guid.NewGuid(),
    ProjectCode: "PRO-E875C783",
    ProjectName: "Commvault Cleanroom",
    CustomerName: "Summit Manufacturing",
    PlanName: "Project Management Plan",
    RevisionLabel: "Version 4",
    ProjectStartDate: new DateOnly(2026, 8, 17),
    ProjectEndDate: new DateOnly(2026, 9, 4),
    Tasks:
    [
        new ProjectFlowHivePlanTaskInput(
            ClientTaskId: Guid.NewGuid(),
            CanonicalTaskId: null,
            WbsNumber: "1.1",
            ParentWbsNumber: "1",
            Name: "Review approved scope and design",
            Description: "Validate the approved SOW scope and implementation design.",
            DurationWorkingDays: 2,
            IsMilestone: false,
            ConstraintType: null,
            ConstraintDate: null,
            PercentComplete: 25m,
            RemainingEffortHours: 12m,
            Status: "in_progress",
            Phase: "Plan",
            Comments: "Owner confirmed — schedule ready…",
            Notes: "Executive review uses approved evidence only."),
        new ProjectFlowHivePlanTaskInput(
            ClientTaskId: Guid.NewGuid(),
            CanonicalTaskId: null,
            WbsNumber: "2.1",
            ParentWbsNumber: "2",
            Name: "Implement validated configuration",
            Description: "Execute the approved implementation steps.",
            DurationWorkingDays: 3,
            IsMilestone: false,
            ConstraintType: null,
            ConstraintDate: null,
            PercentComplete: 0m,
            RemainingEffortHours: 24m,
            Status: "not_started",
            Phase: "Implement")
    ],
    Dependencies:
    [
        new ProjectFlowHiveDependencyInput("1.1", "2.1", "FS", 0)
    ],
    Assignments:
    [
        new ProjectFlowHivePlanAssignmentInput("1.1", Guid.NewGuid(), "Jason Mosier", 100m, 16m),
        new ProjectFlowHivePlanAssignmentInput("2.1", Guid.NewGuid(), "Kevin Damisch", 100m, 24m)
    ],
    GsdVersion: "GSD-4",
    SowVersion: "SOW-3",
    Notes: "This executive summary explains the approved scope, current schedule, accountable owners, delivery progress, dependencies, and RAID considerations before the governed customer baseline is established.");

var schedule = new ProjectFlowHiveScheduleResult(
    Valid: true,
    Status: "scheduled",
    ProjectStartDate: new DateOnly(2026, 8, 17),
    ProjectTargetEndDate: new DateOnly(2026, 9, 4),
    ProjectFinishDate: new DateOnly(2026, 8, 21),
    ScheduledWorkingDays: 5,
    CriticalTaskCount: 2,
    PlannedHours: 40m,
    Tasks:
    [
        new ProjectFlowHiveScheduledTask(
            "1.1", "1", "Review approved scope and design",
            new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 18), 2,
            0, 0, 0, 0, true, false, 25m, 12m, "in_progress", false, "Plan"),
        new ProjectFlowHiveScheduledTask(
            "2.1", "2", "Implement validated configuration",
            new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 21), 3,
            2, 2, 0, 0, true, false, 0m, 24m, "not_started", false, "Implement")
    ],
    Issues: [],
    CalendarMode: "Monday-Friday",
    ContractVersion: "project-flowhive-planning-v1");

var request = new ProjectFlowHiveArtifactRequest(
    Plan: plan,
    ArtifactTitle: "PRO-E875C783 Governed Project Management Plan",
    Audience: "Executive and delivery leadership",
    ExcludeNotes: false,
    AcknowledgeInternalDraft: true);

var renderer = typeof(ProjectFlowHivePlanRequest).Assembly.GetType(
    "ProjectTime.Api.Modules.ProjectFlowHiveArtifactRenderer",
    throwOnError: true)!;
var buildPdf = renderer.GetMethod(
    "BuildPdf",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("FlowHive PDF renderer was not found.");
var pdf = (byte[])(buildPdf.Invoke(null, [request, schedule])
    ?? throw new InvalidOperationException("FlowHive PDF renderer returned no artifact."));
var pdfText = Encoding.ASCII.GetString(pdf);

Require(pdf.Length > 2_000, "production PDF contains a complete artifact");
Require(pdfText.StartsWith("%PDF-1.7", StringComparison.Ordinal), "artifact is a PDF 1.7 document");
Require(pdfText.Contains("PROJECT MANAGEMENT PLAN", StringComparison.Ordinal), "production document label is present");
Require(pdfText.Contains("EXECUTIVE SUMMARY", StringComparison.Ordinal), "dedicated executive summary section is present");
Require(pdfText.Contains("US Signal internal governed Project FlowHive artifact", StringComparison.Ordinal), "governed production footer is present");
Require(!pdfText.Contains("REVIEW REQUIRED", StringComparison.Ordinal), "draft review banner is removed");
Require(!pdfText.Contains("Logo SHA-256", StringComparison.Ordinal), "user-facing checksum text is removed");

var normalize = renderer.GetMethod(
    "NormalizePdfText",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("FlowHive PDF normalizer was not found.");
var normalized = (string)(normalize.Invoke(null, ["Plan — owner’s update…\nReady"])
    ?? throw new InvalidOperationException("FlowHive PDF normalizer returned no result."));
Require(normalized == "Plan - owner's update... Ready", "Unicode punctuation is normalized without question-mark corruption");
Require(normalized.All(character => character is >= ' ' and <= '~'), "normalized PDF text is ASCII safe");

Console.WriteLine("PROJECT_FLOWHIVE_PRODUCTION_ARTIFACT_TESTS=PASS");

static void Require(bool condition, string evidence)
{
    if (!condition)
        throw new InvalidOperationException($"Project FlowHive artifact assertion failed: {evidence}.");
    Console.WriteLine($"PASS: {evidence}");
}

using System.Reflection;
using System.Text;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"ASSERTION_FAILED {label}");
    Console.WriteLine($"ASSERTION_PASSED {label}");
}

var sourcePlan = new ProjectFlowHivePlanRequest(
    ProjectId: Guid.NewGuid(),
    ProjectCode: "TEST-066",
    ProjectName: "Detailed FlowHive planning test",
    CustomerName: "Test customer",
    PlanName: "Detailed governed plan",
    RevisionLabel: "test",
    ProjectStartDate: new DateOnly(2026, 8, 24),
    ProjectEndDate: new DateOnly(2026, 10, 30),
    Tasks: [],
    Dependencies: [],
    Assignments: [],
    GsdVersion: "GSD-1",
    SowVersion: "SOW-1",
    Notes: string.Empty);

var first = new PulseAiPrivateFlowHiveTask(
    Wbs: "SOW-1",
    Name: "Migrate the collaboration platform",
    Description: "Migrate the authorized collaboration platform services to the approved target environment.",
    EstimatedDurationDays: 15m,
    RequiredRoles: ["Project Manager", "Collaboration Engineer", "Customer Technical Owner"],
    Predecessors: [],
    CitationIds: [1],
    IsAssumption: false,
    Phase: "Implement",
    DetailedSteps:
    [
        "Export the approved source configuration and retain the pre-change evidence.",
        "Migrate the authorized configuration in the reviewed sequence.",
        "Record the resulting target configuration and exceptions."
    ],
    Inputs:
    [
        "Approved source and target platform inventory",
        "Administrative access through the approved customer process",
        "Approved licensing and maintenance window"
    ],
    Outputs: ["Migrated target configuration", "As-built configuration record"],
    AcceptanceCriteria: ["Authorized services operate in the approved target environment and are supported by retained evidence."],
    ValidationSteps: ["Validate service registration, connectivity, functional behavior, monitoring, and rollback readiness."],
    CustomerResponsibilities: ["Provide the approved maintenance window, licensing, access, and acceptance participants."],
    UsSignalResponsibilities: ["Execute the reviewed migration and retain objective evidence without recording secrets."],
    Prerequisites: ["Approved design, backup, access, licensing, and rollback plan are available."],
    Risks: ["An unsupported version or unavailable dependency can delay the migration."],
    OpenQuestions: ["Which source and target versions require final confirmation?"],
    EstimatedHours: 120m,
    Priority: "high");

var second = new PulseAiPrivateFlowHiveTask(
    Wbs: "SOW-2",
    Name: "Enable operational monitoring and handoff",
    Description: "Enable the source-backed monitoring, alerting, documentation, and support handoff required by the approved scope.",
    EstimatedDurationDays: 8m,
    RequiredRoles: ["Project Manager", "Monitoring Engineer", "Customer Operations Owner"],
    Predecessors: ["SOW-1"],
    CitationIds: [2],
    IsAssumption: false,
    Phase: "Implement",
    DetailedSteps:
    [
        "Configure the approved monitoring targets and alert routes.",
        "Validate alert generation and escalation behavior.",
        "Complete the operating runbook and support handoff."
    ],
    Inputs: ["Approved monitoring requirements", "Alert recipient and escalation information"],
    Outputs: ["Monitoring configuration", "Validated alerts", "Operating runbook"],
    AcceptanceCriteria: ["Approved monitoring checks and alert routes pass with documented evidence."],
    ValidationSteps: ["Generate approved test conditions and verify monitoring, notification, and escalation results."],
    CustomerResponsibilities: ["Confirm alert recipients, escalation ownership, and operating acceptance."],
    UsSignalResponsibilities: ["Configure, validate, document, and hand off the approved monitoring scope."],
    Prerequisites: ["The migrated platform is available and the approved monitoring interfaces are reachable."],
    Risks: ["Incomplete recipient or escalation information can prevent operational acceptance."],
    OpenQuestions: ["Which support groups and notification channels require final approval?"],
    EstimatedHours: 64m,
    Priority: "normal");

var privatePlan = new PulseAiPrivateFlowHivePlan(
    Objective: "Create a complete implementation plan from the authorized SOW.",
    Tasks: [first, second],
    Milestones: [],
    Dependencies: ["SOW-2 follows SOW-1."],
    RequiredRoles: ["Project Manager", "Engineering reviewer"],
    Assumptions: [],
    Risks: [],
    OutOfScopeItems: [],
    OpenQuestions: [],
    Conflicts: [],
    CitationIds: [1, 2],
    Confidence: 0.90m,
    ConfidenceExplanation: "Test evidence is complete.");

var generated = ProjectFlowHiveDetailedPlanBuilder.Build(sourcePlan, privatePlan);
var tasks = generated.Tasks?.ToArray() ?? [];
var executable = tasks.Where(task => !task.IsSummary).ToArray();
var summaries = tasks.Where(task => task.IsSummary).ToArray();
var dependencies = generated.Dependencies?.ToArray() ?? [];

Assert(ProjectFlowHiveDetailedPlanBuilder.DetailContract.Contains("five-phase-detailed-work-package"), "detail_contract_declared");
Assert(summaries.Length == 5, "five_phase_summaries_created");
Assert(executable.Length == 10, "two_work_packages_expand_to_ten_detailed_tasks");
Assert(tasks.Length == 15, "summaries_and_detailed_tasks_total");

foreach (var phase in new[] { "Plan", "Design", "Implement", "Validate", "Release" })
{
    var phaseTasks = executable.Where(task => task.Phase == phase).ToArray();
    Assert(phaseTasks.Length == 2, $"{phase.ToLowerInvariant()}_contains_each_work_package");
}

foreach (var task in executable)
{
    Assert(!string.IsNullOrWhiteSpace(task.Name), $"{task.WbsNumber}_name_present");
    Assert(!string.IsNullOrWhiteSpace(task.Description) && task.Description!.Length > 120, $"{task.WbsNumber}_description_detailed");
    Assert((task.DetailedSteps?.Count ?? 0) >= 6, $"{task.WbsNumber}_ordered_steps_detailed");
    Assert((task.Inputs?.Count ?? 0) >= 4, $"{task.WbsNumber}_inputs_and_used_items_detailed");
    Assert((task.Outputs?.Count ?? 0) >= 2, $"{task.WbsNumber}_outputs_detailed");
    Assert((task.AcceptanceCriteria?.Count ?? 0) >= 3, $"{task.WbsNumber}_acceptance_detailed");
    Assert((task.ValidationSteps?.Count ?? 0) >= 3, $"{task.WbsNumber}_validation_detailed");
    Assert((task.CustomerResponsibilities?.Count ?? 0) >= 3, $"{task.WbsNumber}_customer_responsibilities_detailed");
    Assert((task.UsSignalResponsibilities?.Count ?? 0) >= 3, $"{task.WbsNumber}_ussignal_responsibilities_detailed");
    Assert((task.Prerequisites?.Count ?? 0) >= 3, $"{task.WbsNumber}_prerequisites_detailed");
    Assert((task.Risks?.Count ?? 0) >= 3, $"{task.WbsNumber}_risks_detailed");
    Assert((task.OpenQuestions?.Count ?? 0) >= 3, $"{task.WbsNumber}_open_questions_detailed");
    Assert(task.DurationWorkingDays >= 1, $"{task.WbsNumber}_duration_present");
    Assert(task.RemainingEffortHours > 0, $"{task.WbsNumber}_effort_present");
    Assert((task.CitationIds?.Count ?? 0) == 1, $"{task.WbsNumber}_citation_preserved");
    Assert(task.Notes?.Contains("Required roles:", StringComparison.Ordinal) == true, $"{task.WbsNumber}_roles_recorded");
}

foreach (var phaseNumber in Enumerable.Range(1, 5))
{
    Assert(executable.Single(task => task.WbsNumber == $"{phaseNumber}.1").CitationIds!.SequenceEqual([1]), $"package_one_phase_{phaseNumber}_citation_one");
    Assert(executable.Single(task => task.WbsNumber == $"{phaseNumber}.2").CitationIds!.SequenceEqual([2]), $"package_two_phase_{phaseNumber}_citation_two");
}

foreach (var packageNumber in new[] { 1, 2 })
{
    for (var phaseNumber = 1; phaseNumber < 5; phaseNumber++)
    {
        Assert(dependencies.Any(dependency =>
            dependency.PredecessorWbs == $"{phaseNumber}.{packageNumber}"
            && dependency.SuccessorWbs == $"{phaseNumber + 1}.{packageNumber}"
            && dependency.Type == "FS"),
            $"package_{packageNumber}_phase_{phaseNumber}_to_{phaseNumber + 1}_dependency");
    }
}

Assert(dependencies.Any(dependency => dependency.PredecessorWbs == "5.1" && dependency.SuccessorWbs == "1.2"),
    "source_predecessor_connects_release_to_next_plan");
Assert(executable.Single(task => task.WbsNumber == "1.1").Inputs!.Any(value => value.Contains("Approved licensing", StringComparison.OrdinalIgnoreCase)),
    "source_backed_used_items_preserved");
Assert(executable.Single(task => task.WbsNumber == "3.1").DetailedSteps!.Any(value => value.Contains("Export the approved source configuration", StringComparison.OrdinalIgnoreCase)),
    "source_backed_implementation_steps_preserved");
Assert(generated.Notes?.Contains("Generated executable tasks: 10", StringComparison.Ordinal) == true,
    "plan_notes_explain_detailed_task_count");
Assert((generated.Milestones?.Count ?? 0) >= 2, "work_package_release_milestones_created");
Assert(generated.Milestones!.All(item => item.CitationIds.Count > 0), "milestone_citations_preserved");
Assert(generated.Milestones!.All(item => item.PredecessorWbs.StartsWith("5.", StringComparison.Ordinal)), "milestones_follow_release_tasks");
Assert((generated.Assignments?.Count ?? 0) == executable.Length, "role_resources_populated_for_every_executable_task");
Assert(generated.Assignments!.All(item => !string.IsNullOrWhiteSpace(item.ResourceDisplayName)), "role_resource_names_populated");

Assert(
    PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Contains(".doc", StringComparer.OrdinalIgnoreCase),
    "legacy_doc_admitted_by_immutable_snapshot_policy");
Assert(
    PulseAiPrivateDocumentPipelinePolicy.ExplicitlyBlockedExtensions.Contains(".docm", StringComparer.OrdinalIgnoreCase),
    "macro_enabled_word_remains_blocked");

var boundedReader = typeof(PulseAiLegacyBinaryWordExtraction).GetMethod(
    "ReadBoundedAsync",
    BindingFlags.NonPublic | BindingFlags.Static);
Assert(boundedReader is not null, "legacy_word_bounded_reader_available_for_regression");

var oversizedOutput = new string('X', (16 * 1024 * 8) + 317);
using var oversizedStream = new MemoryStream(Encoding.UTF8.GetBytes(oversizedOutput));
using var oversizedReader = new StreamReader(
    oversizedStream,
    Encoding.UTF8,
    detectEncodingFromByteOrderMarks: false,
    bufferSize: 1_024,
    leaveOpen: true);
var boundedTask = boundedReader!.Invoke(
    null,
    new object?[] { oversizedReader, 1_024, CancellationToken.None }) as Task<string>;
Assert(boundedTask is not null, "legacy_word_bounded_reader_invoked");
var boundedOutput = await boundedTask!;
Assert(boundedOutput.Length == 1_024, "legacy_word_retained_output_is_bounded");
Assert(oversizedReader.EndOfStream, "legacy_word_excess_output_is_fully_drained");

var shortWindow = sourcePlan with { ProjectEndDate = sourcePlan.ProjectStartDate!.Value.AddDays(4) };
var shortGenerated = ProjectFlowHiveDetailedPlanBuilder.Build(shortWindow, privatePlan);
var shortSchedule = ProjectFlowHiveScheduleEngine.Calculate(shortGenerated);
Assert(shortSchedule.Valid, "short_selected_window_remains_saveable_working_draft");
Assert(shortSchedule.Status == "calculated_preview_window_exceeded", "short_selected_window_reports_overrun_status");
Assert(shortSchedule.Issues.Any(issue => issue.Code == "project_end_exceeded" && issue.Severity == "warning"), "project_end_exceeded_is_explicit_warning");
Assert(shortSchedule.Tasks.Any(task => task.IsCritical && !task.IsSummary), "critical_path_is_identified");
var normalDurations = generated.Tasks!.Where(task => !task.IsSummary).ToDictionary(task => task.WbsNumber!, task => task.DurationWorkingDays);
var shortDurations = shortGenerated.Tasks!.Where(task => !task.IsSummary).ToDictionary(task => task.WbsNumber!, task => task.DurationWorkingDays);
Assert(normalDurations.OrderBy(pair => pair.Key).SequenceEqual(shortDurations.OrderBy(pair => pair.Key)), "selected_window_does_not_compress_estimates");
Assert(shortGenerated.Tasks!.Where(task => !task.IsSummary).All(task => task.RequiredRoles?.Count > 0), "required_roles_are_structured");
Assert(shortGenerated.Tasks!.Where(task => !task.IsSummary).All(task => task.OpenQuestions?.Count > 0), "missing_technical_information_becomes_open_questions");

Console.WriteLine("FLOWHIVE_DETAILED_PLANNER_TESTS=PASS");

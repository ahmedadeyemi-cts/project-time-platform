using System.Reflection;
using System.Text;
using System.Text.Json;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"ASSERTION_FAILED {label}");
    Console.WriteLine($"ASSERTION_PASSED {label}");
}

var module025Parser = typeof(PulseAiPrivateRagService).GetMethod(
    "ParseModule025DetailedPlan",
    BindingFlags.NonPublic | BindingFlags.Static);
Assert(module025Parser is not null, "module025_detailed_parser_available");

var module025Source = new PulseAiPrivateRetrievedChunk(
    ChunkId: "module025-service-overview",
    DocumentVersionId: Guid.NewGuid(),
    DocumentId: Guid.NewGuid(),
    ProjectId: null,
    ProjectCode: "SOW-TEST-025",
    ProjectName: "Module 025 detailed parser test",
    CustomerName: "Test customer",
    DocumentCategory: "module025_service_overview",
    DocumentVersion: "revision-1",
    Classification: "internal",
    OriginalFileName: "Saved Service Overview",
    CitationAnchor: "Module 025 Service Overview",
    PageNumber: null,
    SheetName: null,
    SectionTitle: "Service Overview",
    Text: "Upgrade Cisco Unified Communications Manager from version 14.0 to version 15.0.",
    SourceSha256: new string('a', 64),
    TextSha256: new string('b', 64),
    LexicalScore: 1m,
    SemanticScore: 1m,
    CombinedScore: 1m,
    ProcessedAt: DateTimeOffset.UtcNow,
    RankOrder: 1,
    SourceType: "module025_saved_service_overview",
    SourceModule: "025");
var module025Retrieval = new PulseAiPrivateRetrievalResult(
    Status: "completed",
    RetrievalMode: "module025_saved_service_overview",
    ResolvedProjectId: null,
    ResolvedProjectCode: "SOW-TEST-025",
    ResolvedProjectName: "Module 025 detailed parser test",
    CandidateCount: 1,
    AuthorizedCandidateCount: 1,
    Chunks: [module025Source],
    MissingEvidence: [],
    Conflicts: [],
    CoverageScore: 1m,
    DataAsOf: DateTimeOffset.UtcNow,
    DiagnosticCode: string.Empty);

var module025Phases = new[] { "Planning", "Architecture and Design", "Implementation", "Testing and Validation", "Operational Handoff" };
var module025Payload = JsonSerializer.Serialize(new
{
    summary = "Upgrade Cisco Unified Communications Manager from version 14.0 to 15.0 through a controlled readiness, architecture, implementation, validation, and operational transition that keeps customer decisions and unknown environment facts explicit.",
    phases = module025Phases.Select((phase, phaseIndex) => new
    {
        name = phase,
        acceptance = new[] { $"The customer and delivery reviewers can trace the {phase.ToLowerInvariant()} phase to retained evidence and its completion decision." },
        tests = new[] { $"Review the retained {phase.ToLowerInvariant()} evidence and confirm that the expected result and decision gate are recorded." },
        customerActions = new[] { "Provide environment facts, authorized access, decisions, and acceptance participation through the approved project process." },
        providerResponsibilities = new[] { "Perform the reviewed technical procedure, protect credentials, retain evidence, and escalate deviations before expanding scope." },
        readinessRequirements = new[] { "Required approvals, inputs, access, backup or rollback evidence, and customer contacts are available before work begins." },
        riskConsiderations = new[] { "Unsupported compatibility, missing entitlement, unavailable access, or an unapproved change condition can pause delivery and require replanning." },
        customerDecisions = new[] { "Confirm the actual CUCM topology, installed options, target compatibility, licensing entitlement, and approved change window." },
        workPackages = Enumerable.Range(1, 2).Select(packageIndex => new
        {
            wbsNumber = $"{phaseIndex + 1}.{packageIndex}",
            title = $"{phase} CUCM work package {packageIndex}",
            outcome = $"Complete technology-specific {phase.ToLowerInvariant()} work package {packageIndex} for Cisco Unified Communications Manager 14.0 to 15.0, record the customer-visible evidence, and stop at the documented decision gate when a customer topology, compatibility, licensing, access, or maintenance-window fact remains unresolved.",
            hours = 8 + phaseIndex + packageIndex,
            roles = new[] { "Cisco Collaboration Engineer", "Solution Architect" },
            dependsOn = phaseIndex == 0 && packageIndex == 1 ? Array.Empty<string>() : new[] { $"{Math.Max(1, phaseIndex)}.{packageIndex}" },
            assumption = true,
            steps = new object[]
            {
                new { text = $"Inspect the approved CUCM inputs for {phase.ToLowerInvariant()} work package {packageIndex}, record the observed result, and resolve or document every blocking discrepancy before proceeding." },
                $"Execute the reviewed {phase.ToLowerInvariant()} procedure for work package {packageIndex}, retain objective before-and-after evidence, and verify the stated completion condition."
            },
            requiredInputs = new[] { "Customer-approved CUCM inventory, compatibility evidence, access plan, and change constraints." },
            deliverables = new[] { $"Documented {phase.ToLowerInvariant()} output and evidence package {packageIndex}." },
            acceptance = packageIndex == 1
                ? new[] { $"Record the task-specific completion decision for {phase.ToLowerInvariant()} work package {packageIndex}." }
                : Array.Empty<string>(),
            customerActions = packageIndex == 1
                ? new[] { $"Confirm the task-specific customer decision for {phase.ToLowerInvariant()} work package {packageIndex}." }
                : Array.Empty<string>()
        })
    }),
    milestones = new[]
    {
        new
        {
            name = "CUCM production upgrade decision gate",
            description = "Confirm that the Cisco Unified Communications Manager production cluster is ready for the approved upgrade window and that every blocking readiness condition has a documented disposition.",
            proposedTiming = "After readiness and design approval, before production implementation begins.",
            acceptanceEvidence = new[] { "Approved readiness record, implementation decision, rollback authority, and customer change-window confirmation are retained." },
            assumption = true
        }
    },
    roles = new[] { "Solution Architect", "Cisco Collaboration Engineer", "Project Manager" },
    assumptions = new[] { "Technical procedures and effort remain proposals until the Solution Architect validates the customer environment." },
    risks = new[] { "Unsupported components or unresolved customer prerequisites can prevent a safe upgrade." },
    outOfScope = new[] { "Work outside the confirmed CUCM upgrade boundary requires separate review and approval." },
    questions = new[] { "What is the confirmed customer topology, node count, licensing state, integration inventory, and maintenance window?" },
    conflicts = Array.Empty<string>(),
    confidence = 0.82m,
    confidenceExplanation = "The saved Service Overview establishes the requested upgrade boundary; customer-specific facts still require validation."
});

var parsedModule025 = module025Parser!.Invoke(
    null,
    new object?[] { module025Payload, module025Retrieval }) as PulseAiPrivateFlowHivePlan;
Assert(parsedModule025 is not null, "module025_grouped_work_packages_parse");
Assert(parsedModule025!.Tasks.Count == 10, "module025_ten_detailed_work_packages_preserved");
foreach (var phase in new[] { "Plan", "Design", "Implement", "Validate", "Release" })
{
    Assert(parsedModule025.Tasks.Count(task => task.Phase == phase) == 2, $"module025_{phase.ToLowerInvariant()}_coverage");
}
Assert(parsedModule025.Tasks.All(task => task.CitationIds.SequenceEqual(new[] { 1 })), "module025_server_authorized_citation_bound");
Assert(parsedModule025.Tasks.All(task => task.EstimatedDurationDays > 0m && task.EstimatedHours is > 0m), "module025_duration_and_effort_normalized");
Assert(parsedModule025.Tasks.All(task => (task.DetailedSteps?.Count ?? 0) >= 2), "module025_structured_and_string_steps_preserved");
Assert(parsedModule025.Tasks.All(task => (task.CustomerResponsibilities?.Count ?? 0) > 0), "module025_phase_level_responsibilities_inherited");
Assert(parsedModule025.Tasks.All(task => (task.AcceptanceCriteria?.Count ?? 0) > 0), "module025_phase_level_acceptance_inherited");
Assert(parsedModule025.Tasks.First().CustomerResponsibilities!.Count == 2, "module025_task_and_phase_responsibilities_merged");
Assert(parsedModule025.Tasks.First().AcceptanceCriteria!.Count == 2, "module025_task_and_phase_acceptance_merged");
Assert(parsedModule025.Tasks.All(task => (task.ValidationSteps?.Count ?? 0) > 0), "module025_every_task_has_validation");
Assert(parsedModule025.Tasks.All(task => (task.Prerequisites?.Count ?? 0) > 0), "module025_every_task_has_prerequisites");
Assert(parsedModule025.Tasks.All(task => (task.Risks?.Count ?? 0) > 0), "module025_every_task_has_risks");
Assert(parsedModule025.Tasks.All(task => task.Description.Length >= 80), "module025_customer_ready_descriptions_preserved");
Assert(parsedModule025.Milestones.Count == 1, "module025_model_milestone_preserved");
Assert(parsedModule025.Milestones[0].CitationIds.SequenceEqual(new[] { 1 }), "module025_milestone_citation_bound");
Assert(parsedModule025.Milestones[0].Name.Contains("CUCM", StringComparison.Ordinal), "module025_milestone_content_preserved");

// Exercise orchestration with actual parser-valid model payloads. A failed
// phase must never produce an assembled draft; refusals must not be retried.
var phaseGenerator = typeof(PulseAiPrivateRagService).GetMethod(
    "GenerateModule025PhasesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
var phaseRequest = new PulseAiPrivateModelRequest(CelarAiCapabilityCatalog.SowGsdPlanning,
    "sow_draft", "comprehensive", "Return at least two tasks for every phase and at least ten tasks total.",
    "Generate the requested service", [module025Source], "PulseAiPrivateFlowHivePlan", 12000, 0.05m, "phase-test");
var phaseNames = new[] { "Plan", "Design", "Implement", "Validate", "Release" };
var phaseCalls = 0;
Func<PulseAiPrivateModelRequest, CancellationToken, Task<PulseAiPrivateModelResult>> phaseModel = (request, token) =>
{
    var phase = phaseNames[phaseCalls++];
    Assert(request.MaximumOutputTokens == 4096, "module025_phase_completion_bounded");
    Assert(request.SystemInstruction.Contains($"Return ONLY {phase} tasks"), "module025_phase_request_scoped");
    Assert(request.Sources.Single() == module025Source, "module025_phase_source_authority_preserved");
    var payload = JsonSerializer.Serialize(parsedModule025 with
    { Tasks = parsedModule025.Tasks.Where(task => task.Phase == phase).ToArray() });
    return Task.FromResult(new PulseAiPrivateModelResult("private_model_completed", "celar_ai", "test-model",
        payload, 100, payload.Length, "", DateTimeOffset.UtcNow));
};
async Task<PulseAiPrivateModelResult> RunPhases(Func<PulseAiPrivateModelRequest, CancellationToken, Task<PulseAiPrivateModelResult>> model,
    CancellationToken token = default) => await (Task<PulseAiPrivateModelResult>)phaseGenerator.Invoke(null,
        new object[] { phaseRequest, module025Retrieval, model, token })!;
var phasedResult = await RunPhases(phaseModel);
Assert(phasedResult.Succeeded && phaseCalls == 5, "module025_five_validated_phases_complete");
var phasedPlan = (PulseAiPrivateFlowHivePlan)module025Parser.Invoke(null, new object[] { phasedResult.Content, module025Retrieval })!;
Assert(phasedPlan.Tasks.Count == 10, "module025_assembled_contract_passes");
var invalidCalls = 0;
var invalidResult = await RunPhases((request, token) =>
{
    invalidCalls++;
    return Task.FromResult(new PulseAiPrivateModelResult("private_model_completed", "celar_ai", "test-model",
        "{\"tasks\":[]}", 100, 12, "", DateTimeOffset.UtcNow));
});
Assert(!invalidResult.Succeeded && invalidCalls == 2 && invalidResult.Content.Length == 0,
    "module025_invalid_phase_bounded_retry_no_draft");
var refusalCalls = 0;
var refusalResult = await RunPhases((request, token) =>
{
    refusalCalls++;
    return Task.FromResult(new PulseAiPrivateModelResult("private_model_refused", "celar_ai", "test-model",
        "", 100, 0, "private_model_safety_refusal", DateTimeOffset.UtcNow));
});
Assert(!refusalResult.Succeeded && refusalCalls == 1 && refusalResult.Status == "private_model_refused",
    "module025_refusal_terminal");
phaseCalls = 0;
var repairCalls = 0;
var repairedResult = await RunPhases((request, token) =>
{
    repairCalls++;
    if (repairCalls == 1)
        return Task.FromResult(new PulseAiPrivateModelResult("private_model_completed", "celar_ai", "test-model",
            "{\"tasks\":[]}", 100, 12, "", DateTimeOffset.UtcNow));
    if (repairCalls == 2) Assert(request.UserInstruction.Contains("prior response failed validation"), "module025_repair_feedback");
    return phaseModel(request, token);
});
Assert(repairedResult.Succeeded && repairCalls == 6, "module025_invalid_phase_repaired_then_complete");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    var cancellationObserved = false;
    try { await RunPhases(phaseModel, cancelled.Token); }
    catch (OperationCanceledException) { cancellationObserved = true; }
    Assert(cancellationObserved, "module025_phase_cancellation_preserved");
}

var rejectedGenericModule025 = false;
try
{
    _ = module025Parser.Invoke(
        null,
        new object?[]
        {
            module025Payload.Replace(
                "Complete technology-specific",
                "Prepare the cited scope for the",
                StringComparison.Ordinal),
            module025Retrieval
        });
}
catch (TargetInvocationException exception) when (exception.InnerException is JsonException)
{
    rejectedGenericModule025 = true;
}
Assert(rejectedGenericModule025, "module025_generic_cited_scope_language_rejected");

var rejectedNonTextModule025 = false;
try
{
    _ = module025Parser.Invoke(
        null,
        new object?[]
        {
            module025Payload.Replace(
                "\"Cisco Collaboration Engineer\"",
                "{\"text\":true}",
                StringComparison.Ordinal),
            module025Retrieval
        });
}
catch (TargetInvocationException exception) when (exception.InnerException is JsonException)
{
    rejectedNonTextModule025 = true;
}
Assert(rejectedNonTextModule025, "module025_non_string_text_object_rejected");

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

// Reproduce the Protected-Test SMB processing boundary with a structurally valid
// sealed local immutable snapshot. This exercises the compiled extractor rather
// than the standalone adapter, so CI fails if the generated-source rewrite is
// skipped and the runtime falls back to blocked_by_document_safety_policy.
var snapshotProcessingRoot = Path.Combine(
    Path.GetTempPath(),
    "projectpulse-private-document-processing");
var snapshotJobId = Guid.NewGuid();
var snapshotLeaseToken = Guid.NewGuid();
var snapshotFence = Convert.ToHexString(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
    .ToLowerInvariant();
var legacySowBytes = Encoding.UTF8.GetBytes(
    "Scope of Services\n\nPlan the approved collaboration migration.\n\nValidate service readiness and complete customer handoff.");
var legacySowSha256 = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(legacySowBytes))
    .ToLowerInvariant();
var snapshotFileName = $"{legacySowSha256}.doc";
var snapshotJobDirectory = Path.Combine(snapshotProcessingRoot, snapshotJobId.ToString("N"));
var snapshotAttemptDirectory = Path.Combine(
    snapshotJobDirectory,
    $"1-{snapshotLeaseToken:N}-{snapshotFence}");
var snapshotPath = Path.Combine(snapshotAttemptDirectory, snapshotFileName);
var unrelatedUploadRoot = Path.Combine(
    Path.GetTempPath(),
    $"flowhive-legacy-word-upload-root-{Guid.NewGuid():N}");
Directory.CreateDirectory(snapshotAttemptDirectory);
Directory.CreateDirectory(unrelatedUploadRoot);
await File.WriteAllBytesAsync(snapshotPath, legacySowBytes);

try
{
    var legacySowSource = new PulseAiAuthorizedDocumentSource(
        DocumentId: Guid.NewGuid(),
        ProjectId: Guid.NewGuid(),
        ProjectCode: "TEST-066",
        ProjectName: "Detailed FlowHive planning test",
        CustomerName: "Test customer",
        DocumentType: "sow",
        DocumentCategory: "scope_of_services",
        OriginalFileName: "SOW_Sample.doc",
        StoredFileName: snapshotFileName,
        StoragePath: snapshotPath,
        ContentType: "application/msword",
        SizeBytes: legacySowBytes.LongLength,
        EngineeringVisible: true,
        AiTimesheetContextEnabled: true,
        ExtractionStatus: "queued",
        ExistingContextSummaryReady: false,
        ContextLastProcessedAt: null,
        UploadedAt: DateTimeOffset.UtcNow,
        UploadSource: "work_register",
        AccessScope: "project",
        Classification: "internal",
        RoleCodes: ["project_manager"]);

    var legacySowOptions = new PulseAiDocumentPipelineOptions(
        UploadRoot: unrelatedUploadRoot,
        ExtractionPreviewEnabled: true,
        MalwareScanAttested: true,
        MalwareScannerMode: "compiled_regression_attested",
        OcrEndpointConfigured: false,
        PrivateEmbeddingEndpointConfigured: false,
        PrivateVectorIndexConfigured: false,
        MaximumFileBytes: 25L * 1024L * 1024L,
        MaximumPages: 500,
        MaximumCharacters: 2_000_000,
        MaximumSections: 1_000,
        MaximumChunks: 1_500,
        ChunkCharacters: 2_400,
        ChunkOverlapCharacters: 280);

    var compiledExtractor = new PulseAiPrivateDocumentExtractionService(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<PulseAiPrivateDocumentExtractionService>.Instance);
    var legacySowExtraction = await compiledExtractor.ExtractAsync(
        legacySowSource,
        legacySowOptions,
        CancellationToken.None);

    Assert(legacySowExtraction.Status == "extraction_preview_ready", "legacy_word_compiled_extractor_ready");
    Assert(legacySowExtraction.Safety.PathConfined, "legacy_word_sealed_snapshot_path_is_trusted");
    Assert(legacySowExtraction.Safety.SignatureMatchesExtension, "legacy_word_signature_is_admitted");
    Assert(legacySowExtraction.Safety.DetectedFormat == "legacy_doc_text", "legacy_word_text_compatible_format_detected");
    Assert(legacySowExtraction.ExtractionMethod == "legacy_doc_private_text_reader", "legacy_word_compiled_route_uses_private_adapter");
    Assert(legacySowExtraction.SourceSha256 == legacySowSha256, "legacy_word_compiled_route_preserves_source_sha");
    Assert(legacySowExtraction.Sections.Count > 0, "legacy_word_compiled_route_produces_citation_sections");
}
finally
{
    if (Directory.Exists(snapshotJobDirectory))
        Directory.Delete(snapshotJobDirectory, recursive: true);
    if (Directory.Exists(unrelatedUploadRoot))
        Directory.Delete(unrelatedUploadRoot, recursive: true);
}

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

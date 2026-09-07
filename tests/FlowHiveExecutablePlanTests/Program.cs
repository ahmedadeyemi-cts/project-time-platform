using System.Text.Json;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

var assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAILED: {name}");
    assertions++;
    Console.WriteLine($"PASSED: {name}");
}
void Reject(Action action, string code)
{
    try { action(); }
    catch (InvalidOperationException exception)
    {
        Check(exception.Message.StartsWith($"flowhive_wbs_{code}:", StringComparison.Ordinal), code);
        return;
    }
    throw new InvalidOperationException($"FAILED: expected {code} rejection");
}

var phases = new[] { "Plan", "Design", "Implement", "Validate", "Release" };
var seed = new ProjectFlowHivePlanRequest(
    ProjectId: Guid.Parse("aa48a0ba-1c14-4cb2-a5fc-de7db4c996cd"),
    ProjectCode: "TEST-FLOWHIVE", ProjectName: "Synthetic collaboration migration",
    CustomerName: "Test customer", PlanName: "Native executable WBS regression", RevisionLabel: "draft",
    ProjectStartDate: new DateOnly(2026, 9, 8), ProjectEndDate: new DateOnly(2026, 10, 30),
    Tasks: [], Dependencies: [], Assignments: [], GsdVersion: null, SowVersion: "sow-v1", Notes: "PM note");
var sources = phases.Select((phase, index) => new PulseAiPrivateFlowHiveTask(
    Wbs: $"source-{index + 1}", Name: $"{phase}: CUCM migration activity {index + 1}",
    Description: $"Produce the CUCM {phase.ToLowerInvariant()} deliverable for the migration and record the specific completion evidence for review.",
    EstimatedDurationDays: 1.5m, RequiredRoles: ["Collaboration Engineer", "Project Manager"],
    Predecessors: index == 0 ? [] : [$"source-{index}"], CitationIds: [1], IsAssumption: true,
    Phase: phase,
    DetailedSteps: [$"Inspect the CUCM {phase.ToLowerInvariant()} prerequisites and record outstanding conditions.",
        $"Complete the reviewed CUCM {phase.ToLowerInvariant()} activity and retain its output for acceptance."],
    Inputs: ["Approved CUCM migration requirements"], Outputs: [$"CUCM {phase} evidence package"],
    AcceptanceCriteria: [$"Review and approve the CUCM {phase} output against migration requirements."],
    ValidationSteps: [$"Compare the CUCM {phase} evidence with the approved completion criteria."],
    CustomerResponsibilities: ["Confirm the environment facts and the change window."],
    UsSignalResponsibilities: ["Retain delivery evidence and escalate deviations."],
    Prerequisites: ["Approved access and change authority"], Risks: ["Unconfirmed compatibility can delay the migration."],
    OpenQuestions: ["Confirm the customer node inventory."], EstimatedHours: index + 2m, Priority: "normal",
    Products: ["Cisco Unified Communications Manager"], Quantities: ["1 node; proposed test fixture only"])).ToArray();
var plan = new PulseAiPrivateFlowHivePlan(
    Objective: "Prepare a synthetic project-specific migration for review.", Tasks: sources,
    Milestones: [new PulseAiPrivateFlowHiveMilestone("AI-invented milestone", "Do not automatically publish this as a project milestone.",
        "After release", ["PM review"], [1], true)],
    Dependencies: [], RequiredRoles: ["Collaboration Engineer"], Assumptions: ["Estimates require review"], Risks: [],
    OutOfScopeItems: ["Unapproved additional sites"], OpenQuestions: ["Confirm the production node inventory."],
    Conflicts: [], CitationIds: [1], Confidence: 0.8m, ConfidenceExplanation: "Synthetic test evidence");
var authorized = new HashSet<int> { 1, 2 };
ProjectFlowHivePlanRequest Build(PulseAiPrivateFlowHivePlan value) => ProjectFlowHiveExecutablePlanBuilder.Build(seed, value, authorized);
var before = JsonSerializer.Serialize(new { seed, plan });
var result = Build(plan);
var tasks = result.Tasks!.Where(task => !task.IsSummary).ToArray();
Check(tasks.Length == 5, "five native tasks are not multiplied into twenty-five");
Check(result.Tasks!.Count(task => task.IsSummary) == 5, "exactly five phase summary rows");
Check(tasks.Select(task => task.Phase).SequenceEqual(phases), "lifecycle order preserved");
Check(tasks.Select(task => task.WbsNumber).SequenceEqual(new[] { "1.1", "2.1", "3.1", "4.1", "5.1" }), "phase-local WBS assignment");
Check(tasks.Select(task => task.Name).SequenceEqual(sources.Select(task => task.Name)), "specific task names retained");
Check(tasks.Select(task => task.Description).SequenceEqual(sources.Select(task => task.Description)), "specific task outcomes retained");
Check(result.Milestones!.Count == 0, "no automatic milestone generation");
Check(result.Dependencies!.Count == 4 && result.Dependencies[0].PredecessorWbs == "1.1" && result.Dependencies[0].SuccessorWbs == "2.1", "source predecessors remapped");
Check(tasks.Sum(task => task.RemainingEffortHours) == 20m, "effort preserved once rather than phase-multiplied");
Check(result.Assignments!.Sum(item => item.PlannedHours) == 20m, "schedule estimate rows reconcile with task effort");
Check(result.Assignments!.All(item => item.ResourceUserId is null), "no person assigned by the AI");
Check(result.Assignments!.Count == 5, "multiple required roles do not double-count labor");
Check(tasks.All(task => task.DurationWorkingDays == 2), "fractional duration rounds without changing effort");
Check(tasks.All(task => task.DetailedSteps!.Count == 2 && task.Products!.Single() == "Cisco Unified Communications Manager"), "technical details preserved");
Check(JsonSerializer.Serialize(new { seed, plan }) == before, "input plan and seed unchanged");
Check(Build(plan).Tasks!.Select(task => task.ClientTaskId).SequenceEqual(result.Tasks!.Select(task => task.ClientTaskId)), "repeat assembly has stable task identifiers");
var differentProject = ProjectFlowHiveExecutablePlanBuilder.Build(seed with { ProjectId = Guid.NewGuid() }, plan, authorized);
Check(!differentProject.Tasks!.Select(task => task.ClientTaskId).Intersect(result.Tasks!.Select(task => task.ClientTaskId)).Any(), "task identity scoped to project");
var differentVersion = ProjectFlowHiveExecutablePlanBuilder.Build(seed with { SowVersion = "sow-v2" }, plan, authorized);
Check(!differentVersion.Tasks!.Select(task => task.ClientTaskId).Intersect(result.Tasks!.Select(task => task.ClientTaskId)).Any(), "task identity scoped to SOW version");
var scheduled = ProjectFlowHiveScheduleEngine.Calculate(result);
Check(scheduled.Valid && scheduled.PlannedHours == 20m, "actual schedule engine accepts native plan and reconciles hours");
Check(scheduled.Tasks.All(task => task.StartDate >= seed.ProjectStartDate!.Value && task.EndDate >= task.StartDate), "task dates populated by deterministic engine");
var overrun = ProjectFlowHiveScheduleEngine.Calculate(result with { ProjectEndDate = seed.ProjectStartDate });
Check(overrun.ProjectFinishDate > seed.ProjectStartDate && overrun.PlannedHours == 20m, "target-date overrun does not compress effort");

var duplicateSources = sources.ToList();
duplicateSources.Add(sources[0] with { Wbs = "duplicate-plan", CitationIds = [2] });
duplicateSources[1] = sources[1] with { Predecessors = ["duplicate-plan"] };
var deduplicated = Build(plan with { Tasks = duplicateSources });
Check(deduplicated.Tasks!.Count(task => !task.IsSummary) == 5, "exact duplicate source task consolidated");
Check(deduplicated.Tasks!.Single(task => task.WbsNumber == "1.1").CitationIds!.SequenceEqual(new[] { 1, 2 }), "duplicate source citations retained");
Check(deduplicated.Dependencies!.Any(item => item.PredecessorWbs == "1.1" && item.SuccessorWbs == "2.1"), "duplicate predecessor aliases remapped safely");
var quantityVariant = Build(plan with { Tasks = [.. sources, sources[2] with { Wbs = "site-b", Quantities = ["2 nodes"] }] });
Check(quantityVariant.Tasks!.Count(task => !task.IsSummary) == 6, "different quantities are not merged");

Reject(() => Build(plan with { Tasks = [sources[0] with { CitationIds = [99] }, .. sources.Skip(1)] }), "task_citations");
Reject(() => Build(plan with { Tasks = [sources[0] with { CitationIds = [] }, .. sources.Skip(1)] }), "task_citations");
Reject(() => Build(plan with { Tasks = [sources[0] with { EstimatedHours = null }, .. sources.Skip(1)] }), "task_effort");
Reject(() => Build(plan with { Tasks = [sources[0] with { EstimatedHours = 0m }, .. sources.Skip(1)] }), "task_effort");
Reject(() => Build(plan with { Tasks = [sources[0] with { EstimatedDurationDays = 0m }, .. sources.Skip(1)] }), "task_effort");
Reject(() => Build(plan with { Tasks = [sources[0] with { DetailedSteps = ["Repeated", "Repeated"] }, .. sources.Skip(1)] }), "execution_steps");
Reject(() => Build(plan with { Tasks = [sources[0] with { Name = "Plan" }, .. sources.Skip(1)] }), "task_name");
Reject(() => Build(plan with { Tasks = [sources[0] with { Description = "Too brief" }, .. sources.Skip(1)] }), "task_description");
Reject(() => Build(plan with { Tasks = [sources[0] with { Name = "Recommended delivery process steps" }, .. sources.Skip(1)] }), "generic_scaffold");
Reject(() => Build(plan with { Tasks = [sources[0] with { Phase = "Delivery" }, .. sources.Skip(1)] }), "phase");
Reject(() => Build(plan with { Tasks = sources.Take(4).ToArray() }), "phase_coverage");
Reject(() => Build(plan with { Tasks = [sources[0] with { Predecessors = ["missing"] }, .. sources.Skip(1)] }), "unknown_predecessor");
Reject(() => Build(plan with { Tasks = [sources[0] with { Predecessors = [sources[0].Wbs] }, .. sources.Skip(1)] }), "self_dependency");
Reject(() => Build(plan with { Tasks = [sources[0] with { Predecessors = [sources[^1].Wbs] }, .. sources.Skip(1)] }), "dependency_cycle");
Reject(() => Build(plan with { Tasks = [sources[0], sources[1] with { Wbs = sources[0].Wbs }, .. sources.Skip(2)] }), "ambiguous_wbs");
Reject(() => Build(plan with { Tasks = Enumerable.Repeat(sources[0], 251).ToArray() }), "task_count");
Reject(() => ProjectFlowHiveExecutablePlanBuilder.Build(seed with { ProjectStartDate = null }, plan, authorized), "project_context");
Reject(() => ProjectFlowHiveExecutablePlanBuilder.Build(seed with { ProjectEndDate = new DateOnly(2026, 1, 1) }, plan, authorized), "project_dates");
Reject(() => ProjectFlowHiveExecutablePlanBuilder.Build(seed with { Milestones = [new(Guid.NewGuid(), "Reviewed gate", "Existing reviewed gate", "1.1", null, ["PM review"], [1], false)] }, plan, authorized), "milestone_merge_required");
Check(JsonSerializer.Serialize(new { seed, plan }) == before, "rejected generation cannot mutate the working input");
Console.WriteLine($"FLOWHIVE_EXECUTABLE_WBS_ASSERTIONS_PASSED={assertions}");

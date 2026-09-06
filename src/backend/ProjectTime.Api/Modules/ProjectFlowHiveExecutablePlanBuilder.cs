using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Maps approved-evidence AI tasks into their own lifecycle phase exactly once.
/// Unlike the legacy scope scaffold, this never expands every task five times,
/// invents technical detail, or creates milestones as a side effect of planning.
/// </summary>
public static class ProjectFlowHiveExecutablePlanBuilder
{
    public const string Contract = "flowhive-executable-wbs-v2-20260906";
    private static readonly string[] Phases = ["Plan", "Design", "Implement", "Validate", "Release"];
    private static readonly string[] ScaffoldPhrases =
    [
        "Convert this cited scope evidence into one controlled delivery work package",
        "Recommended delivery process steps",
        "Source-backed work package:",
        "Prepare the cited scope",
        "Translate the cited scope"
    ];

    public static ProjectFlowHivePlanRequest Build(
        ProjectFlowHivePlanRequest seed,
        PulseAiPrivateFlowHivePlan plan,
        IReadOnlySet<int> authorizedCitationIds)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(authorizedCitationIds);
        if (seed.ProjectId is null || seed.ProjectId == Guid.Empty || seed.ProjectStartDate is null)
            throw Invalid("project_context", "A project identity and explicit start date are required.");
        if (seed.ProjectEndDate.HasValue && seed.ProjectEndDate.Value < seed.ProjectStartDate.Value)
            throw Invalid("project_dates", "The target finish precedes the project start.");
        if (plan.Tasks is null || plan.Tasks.Count is < 1 or > 250)
            throw Invalid("task_count", "Return 1 to 250 detailed tasks; oversized output must not be silently truncated.");
        if ((seed.Milestones?.Count ?? 0) > 0)
            throw Invalid("milestone_merge_required", "Existing milestones require an explicit reviewed merge before regenerating their predecessor tasks.");

        var unique = new List<WorkItem>();
        var byFingerprint = new Dictionary<string, WorkItem>(StringComparer.Ordinal);
        var bySourceWbs = new Dictionary<string, WorkItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in plan.Tasks)
        {
            if (task is null) throw Invalid("task_null", "The task list contains an empty item.");
            var phase = Phases.FirstOrDefault(value => string.Equals(value, task.Phase?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (phase is null) throw Invalid("phase", "Every executable task must identify a supported lifecycle phase.");
            if (string.IsNullOrWhiteSpace(task.Wbs)) throw Invalid("wbs", "Every source task must have a unique WBS reference.");
            if (string.IsNullOrWhiteSpace(task.Name) || task.Name.Length > 300 || task.Name.Trim().Equals(phase, StringComparison.OrdinalIgnoreCase))
                throw Invalid("task_name", "Use an activity-specific task name, not a phase heading.");
            if (string.IsNullOrWhiteSpace(task.Description) || task.Description.Trim().Length < 40 || task.Description.Length > 4000)
                throw Invalid("task_description", "Every task needs a specific delivery outcome between 40 and 4000 characters.");
            if (task.CitationIds is null || task.CitationIds.Count == 0 || task.CitationIds.Any(id => !authorizedCitationIds.Contains(id)))
                throw Invalid("task_citations", "Every task must cite current project-authorized evidence.");
            if (task.EstimatedHours is null or <= 0m or > 4000m || task.EstimatedDurationDays is <= 0m or > 365m)
                throw Invalid("task_effort", "Every task needs positive, bounded effort and duration estimates.");
            Require(task.DetailedSteps, 2, "execution_steps");
            Require(task.Inputs, 1, "inputs");
            Require(task.Outputs, 1, "outputs");
            Require(task.AcceptanceCriteria, 1, "acceptance");
            Require(task.ValidationSteps, 1, "validation");
            Require(task.RequiredRoles, 1, "roles");
            var prose = new[] { task.Name, task.Description }
                .Concat(task.DetailedSteps ?? []).Concat(task.Outputs ?? []).Concat(task.AcceptanceCriteria ?? []);
            if (prose.Any(text => ScaffoldPhrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase))))
                throw Invalid("generic_scaffold", "The AI returned a scope scaffold, not executable project-specific work. The current working copy is unchanged.");

            var normalized = task with { Phase = phase, Name = task.Name.Trim(), Description = task.Description.Trim() };
            // Only exact semantic duplicates are merged. Quantities, environments,
            // versions, estimates and all detail fields participate in the key.
            var fingerprint = JsonSerializer.Serialize(normalized with { Wbs = "", Predecessors = [], CitationIds = [] });
            if (!byFingerprint.TryGetValue(fingerprint, out var item))
            {
                item = new WorkItem(normalized, fingerprint);
                unique.Add(item);
                byFingerprint.Add(fingerprint, item);
            }
            var sourceWbs = task.Wbs.Trim();
            if (bySourceWbs.TryGetValue(sourceWbs, out var prior) && !ReferenceEquals(prior, item))
                throw Invalid("ambiguous_wbs", "Distinct source tasks share a WBS identifier; dependencies cannot be safely resolved.");
            bySourceWbs[sourceWbs] = item;
            item.Citations.UnionWith(task.CitationIds);
            item.Predecessors.UnionWith((task.Predecessors ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
        }

        foreach (var phase in Phases)
            if (!unique.Any(item => item.Task.Phase == phase))
                throw Invalid("phase_coverage", $"The {phase} phase has no executable work. Complete the AI result instead of manufacturing placeholder tasks.");

        var tasks = new List<ProjectFlowHivePlanTaskInput>();
        for (var phaseIndex = 0; phaseIndex < Phases.Length; phaseIndex++)
        {
            var phase = Phases[phaseIndex];
            var parent = (phaseIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            tasks.Add(new(StableId(seed, "summary:" + phase), null, parent, null, phase,
                $"{phase} summary; dates and effort roll up from child tasks.", 0, false, "ASAP", null, 0m, 0m,
                "not_started", IsSummary: true, Phase: phase, Priority: "summary"));
            var index = 0;
            foreach (var item in unique.Where(item => item.Task.Phase == phase))
            {
                item.Wbs = $"{parent}.{++index}";
                var task = item.Task;
                tasks.Add(new ProjectFlowHivePlanTaskInput(
                    ClientTaskId: StableId(seed, item.Fingerprint), CanonicalTaskId: null,
                    WbsNumber: item.Wbs, ParentWbsNumber: parent, Name: task.Name, Description: task.Description,
                    DurationWorkingDays: checked((int)decimal.Ceiling(task.EstimatedDurationDays)),
                    IsMilestone: false, ConstraintType: "ASAP", ConstraintDate: null,
                    PercentComplete: 0m, RemainingEffortHours: task.EstimatedHours!.Value, Status: "not_started",
                    IsSummary: false, Phase: phase, DetailedSteps: task.DetailedSteps,
                    Inputs: task.Inputs, Outputs: task.Outputs, AcceptanceCriteria: task.AcceptanceCriteria,
                    ValidationSteps: task.ValidationSteps, CustomerResponsibilities: task.CustomerResponsibilities,
                    UsSignalResponsibilities: task.UsSignalResponsibilities, Prerequisites: task.Prerequisites,
                    Risks: task.Risks, OpenQuestions: task.OpenQuestions, Priority: task.Priority,
                    CitationIds: item.Citations.Order().ToArray(),
                    Notes: "AI-proposed work and estimates require PM and Engineering review. Duration uses whole working days; effort is preserved independently.",
                    Products: task.Products, Platforms: task.Platforms, Manufacturers: task.Manufacturers,
                    Models: task.Models, SoftwareVersions: task.SoftwareVersions, FirmwareVersions: task.FirmwareVersions,
                    LicensingRequirements: task.LicensingRequirements, Quantities: task.Quantities,
                    Tools: task.Tools, Systems: task.Systems, Interfaces: task.Interfaces,
                    IntegrationPoints: task.IntegrationPoints, AccessRequirements: task.AccessRequirements,
                    RollbackSteps: task.RollbackSteps, Assumptions: task.Assumptions, RequiredRoles: task.RequiredRoles));
            }
        }

        var dependencies = new List<ProjectFlowHiveDependencyInput>();
        foreach (var item in unique)
        {
            foreach (var predecessor in item.Predecessors)
            {
                if (!bySourceWbs.TryGetValue(predecessor, out var prior))
                    throw Invalid("unknown_predecessor", "A predecessor reference does not identify a returned executable task.");
                if (ReferenceEquals(prior, item))
                    throw Invalid("self_dependency", "A source task depends on itself or on an exact duplicate of itself.");
                dependencies.Add(new(prior.Wbs, item.Wbs, "FS", 0));
            }
        }
        dependencies = dependencies.Distinct().ToList();
        RejectCycles(unique, dependencies);

        return seed with
        {
            Tasks = tasks,
            Dependencies = dependencies,
            // One unassigned estimate row per task preserves the existing schedule
            // hours contract without assigning a person or counting each role twice.
            Assignments = tasks.Where(task => !task.IsSummary).Select(task =>
                new ProjectFlowHivePlanAssignmentInput(task.WbsNumber, null,
                    "Unassigned — " + string.Join(", ", task.RequiredRoles ?? []),
                    100m, task.RemainingEffortHours)).ToArray(),
            Milestones = [],
            RevisionLabel = Contract,
            CelarAiCitationIds = unique.SelectMany(item => item.Citations).Distinct().Order().ToArray(),
            Notes = string.Join("\n", new[]
            {
                seed.Notes ?? "", "AI-proposed delivery work; review before baseline or customer publication.",
                "No milestones, named-person assignments, capacity reservations, or customer commitments were created.",
                "Whole-working-day scheduling is retained; intraday/calendar authority is not implied.",
                "Scope exclusions: " + string.Join("; ", plan.OutOfScopeItems ?? []),
                "Open questions: " + string.Join("; ", plan.OpenQuestions ?? []),
                "Assumptions: " + string.Join("; ", plan.Assumptions ?? [])
            }.Where(text => !string.IsNullOrWhiteSpace(text)))
        };
    }

    private static void Require(IReadOnlyList<string>? values, int minimum, string field)
    {
        if (values is null || values.Count > 60 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 2000)
            || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() < minimum)
            throw Invalid(field, $"Each task needs {minimum} or more distinct, nonempty {field} entries within the detail limits.");
    }

    private static void RejectCycles(IReadOnlyList<WorkItem> items, IReadOnlyList<ProjectFlowHiveDependencyInput> dependencies)
    {
        var remaining = items.ToDictionary(item => item.Wbs, _ => 0, StringComparer.Ordinal);
        var next = items.ToDictionary(item => item.Wbs, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            remaining[dependency.SuccessorWbs!]++;
            next[dependency.PredecessorWbs!].Add(dependency.SuccessorWbs!);
        }
        var ready = new Queue<string>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (ready.TryDequeue(out var wbs))
        {
            visited++;
            foreach (var successor in next[wbs]) if (--remaining[successor] == 0) ready.Enqueue(successor);
        }
        if (visited != items.Count) throw Invalid("dependency_cycle", "The generated task network contains a dependency cycle.");
    }

    private static Guid StableId(ProjectFlowHivePlanRequest seed, string key) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{Contract}|{seed.ProjectId:D}|{seed.SowVersion}|{key}"))[..16]);
    private static InvalidOperationException Invalid(string code, string message) => new($"flowhive_wbs_{code}: {message}");
    private sealed class WorkItem(PulseAiPrivateFlowHiveTask task, string fingerprint)
    {
        public PulseAiPrivateFlowHiveTask Task { get; } = task;
        public string Fingerprint { get; } = fingerprint;
        public HashSet<int> Citations { get; } = [];
        public HashSet<string> Predecessors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Wbs { get; set; } = "";
    }
}

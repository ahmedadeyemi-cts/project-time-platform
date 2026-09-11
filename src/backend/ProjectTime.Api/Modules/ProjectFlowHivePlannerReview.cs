using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

public sealed record ProjectFlowHiveExistingTaskDecision(string ExistingWbs, string? CandidateWbs);
public sealed record ProjectFlowHivePlannerReviewRequest(
    Guid? ExpectedWorkingRowVersion,
    IReadOnlyList<ProjectFlowHiveExistingTaskDecision>? Decisions,
    string? ReviewNote,
    string? PreviewFingerprint = null);

/// <summary>
/// Deterministic reviewed reconciliation, never an AI mutation of existing work.
/// Each existing activity is explicitly retained or mapped one-to-one to a proposed
/// activity. Mapping retains delivery identity, progress, constraints, notes and
/// assignments. Existing milestone gates and dependency edges are never discarded.
/// </summary>
public static class ProjectFlowHivePlannerReview
{
    public const string Contract = "flowhive-reviewed-regeneration-v1";
    public const string Migration = "105_flowhive_reviewed_regeneration";
    private static readonly string[] Phases = ["Plan", "Design", "Implement", "Validate", "Release"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool RequiresReview(ProjectFlowHivePlanRequest? seed, Guid? existingVersion) =>
        existingVersion.HasValue || (seed?.Tasks?.Count ?? 0) > 0 || (seed?.Milestones?.Count ?? 0) > 0;

    public static ProjectFlowHivePlanRequest Merge(Guid runId, ProjectFlowHivePlanRequest current,
        ProjectFlowHivePlanRequest candidate, IReadOnlyList<ProjectFlowHiveExistingTaskDecision>? decisions)
    {
        if (runId == Guid.Empty || current.ProjectId is null || current.ProjectId != candidate.ProjectId)
            throw Invalid("project", "The current working plan and AI candidate must belong to the same project.");
        var prior = (current.Tasks ?? []).ToArray();
        var proposed = (candidate.Tasks ?? []).ToArray();
        var native = ProjectFlowHiveScheduleEngine.Validate(candidate);
        if (!native.Valid || (candidate.Milestones?.Count ?? 0) != 0 || proposed.Any(t => t.IsMilestone))
            throw Invalid("candidate", "Review requires a valid native AI work breakdown without generated milestones.");
        if (prior.Any(t => string.IsNullOrWhiteSpace(t.WbsNumber)) ||
            prior.Select(t => t.WbsNumber).Distinct(StringComparer.Ordinal).Count() != prior.Length)
            throw Invalid("existing_wbs", "Existing task references must be unique before reconciliation.");
        var oldByWbs = prior.ToDictionary(t => t.WbsNumber!, StringComparer.Ordinal);
        var activities = prior.Where(t => !t.IsSummary).ToArray();
        if (decisions is null || decisions.Count != activities.Length ||
            decisions.Any(d => d is null || string.IsNullOrWhiteSpace(d.ExistingWbs)) ||
            decisions.Select(d => d.ExistingWbs).Distinct(StringComparer.Ordinal).Count() != decisions.Count)
            throw Invalid("decisions", "Choose retain or an AI-task mapping for every existing activity.");
        var choice = decisions.ToDictionary(d => d.ExistingWbs, StringComparer.Ordinal);
        if (activities.Any(t => !choice.ContainsKey(t.WbsNumber!)))
            throw Invalid("decisions", "The review contains missing or unknown existing activities.");
        var candidateLeaves = proposed.Where(t => !t.IsSummary).ToDictionary(t => t.WbsNumber!, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in activities)
        {
            var selected = choice[task.WbsNumber!].CandidateWbs;
            if (selected is null) continue;
            if (!candidateLeaves.ContainsKey(selected) || !used.Add(selected))
                throw Invalid("mapping", "Each AI activity may be mapped to at most one existing activity.");
            if (task.IsMilestone || prior.Any(t => t.ParentWbsNumber == task.WbsNumber))
                throw Invalid("protected_structure", "Retain an existing milestone or parent task rather than mapping it to an AI leaf.");
        }
        string PhaseRoot(ProjectFlowHivePlanTaskInput task)
        {
            var phase = task.Phase;
            if (string.IsNullOrWhiteSpace(phase) && task.ParentWbsNumber is not null && oldByWbs.TryGetValue(task.ParentWbsNumber, out var parent))
                return PhaseRoot(parent);
            if (string.IsNullOrWhiteSpace(phase)) phase = task.IsSummary ? task.Name : null;
            var index = Array.FindIndex(Phases, p => string.Equals(p, phase, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw Invalid("existing_phase", "Each retained task must identify Plan, Design, Implement, Validate or Release.");
            return (index + 1).ToString(CultureInfo.InvariantCulture);
        }
        // Validate parent references before recursion, including malformed old drafts.
        foreach (var task in prior)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { task.WbsNumber! };
            var parent = task.ParentWbsNumber;
            while (parent is not null)
            {
                if (!seen.Add(parent) || !oldByWbs.TryGetValue(parent, out var ancestor))
                    throw Invalid("existing_hierarchy", "Repair missing or circular existing parent references before applying the candidate.");
                parent = ancestor.ParentWbsNumber;
            }
        }
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var summary in prior.Where(t => t.IsSummary)) mapping[summary.WbsNumber!] = PhaseRoot(summary);
        var tasks = proposed.Select(t => t with { ClientTaskId = NewId(runId, t.ClientTaskId?.ToString("D") ?? t.WbsNumber!) }).ToList();
        foreach (var old in activities.Where(t => choice[t.WbsNumber!].CandidateWbs is not null))
        {
            var wbs = choice[old.WbsNumber!].CandidateWbs!;
            mapping[old.WbsNumber!] = wbs;
            var index = tasks.FindIndex(t => t.WbsNumber == wbs);
            var target = tasks[index];
            tasks[index] = target with
            {
                ClientTaskId = old.ClientTaskId ?? NewId(runId, "retained:" + old.WbsNumber),
                CanonicalTaskId = old.CanonicalTaskId, Status = old.Status, PercentComplete = old.PercentComplete,
                DurationWorkingDays = old.DurationWorkingDays, RemainingEffortHours = old.RemainingEffortHours,
                ConstraintType = old.ConstraintType, ConstraintDate = old.ConstraintDate,
                Comments = old.Comments, Notes = old.Notes
            };
        }
        foreach (var old in activities.Where(t => choice[t.WbsNumber!].CandidateWbs is null)
                     .OrderBy(t => t.WbsNumber!.Count(c => c == '.')).ThenBy(t => t.WbsNumber, StringComparer.Ordinal))
        {
            var parent = old.ParentWbsNumber is not null && mapping.TryGetValue(old.ParentWbsNumber, out var mappedParent)
                ? mappedParent : PhaseRoot(old);
            var next = tasks.Where(t => t.ParentWbsNumber == parent)
                .Select(t => int.Parse(t.WbsNumber!.Split('.')[^1], CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;
            var wbs = parent + "." + next.ToString(CultureInfo.InvariantCulture);
            mapping[old.WbsNumber!] = wbs;
            tasks.Add(old with { WbsNumber = wbs, ParentWbsNumber = parent,
                ClientTaskId = old.ClientTaskId ?? NewId(runId, "retained:" + old.WbsNumber) });
        }
        string Remap(string? wbs)
        {
            if (wbs is not null && mapping.TryGetValue(wbs, out var value)) return value;
            throw Invalid("predecessor", "An existing dependency or milestone references missing work. Correct it in the working plan before applying.");
        }
        var milestones = (current.Milestones ?? []).Select(m => m with { PredecessorWbs = Remap(m.PredecessorWbs) }).ToArray();
        var dependencies = (candidate.Dependencies ?? []).Concat((current.Dependencies ?? [])
            .Select(d => d with { PredecessorWbs = Remap(d.PredecessorWbs), SuccessorWbs = Remap(d.SuccessorWbs) })).Distinct().ToArray();
        if (dependencies.Any(d => d.PredecessorWbs == d.SuccessorWbs))
            throw Invalid("dependency", "The selected mapping would collapse an existing dependency into a self-reference.");
        var assignments = (candidate.Assignments ?? []).Where(a => !used.Contains(a.TaskWbs!))
            .Concat((current.Assignments ?? []).Select(a => a with { TaskWbs = Remap(a.TaskWbs) })).ToArray();
        if (tasks.Where(t => t.ClientTaskId.HasValue).Select(t => t.ClientTaskId).Distinct().Count() != tasks.Count(t => t.ClientTaskId.HasValue))
            throw Invalid("identity", "Task identity collisions must be resolved before applying the candidate.");
        var merged = candidate with
        {
            PlanId = current.PlanId, PlanName = current.PlanName ?? candidate.PlanName,
            Notes = current.Notes, Tasks = tasks, Milestones = milestones, Dependencies = dependencies, Assignments = assignments
        };
        var validation = ProjectFlowHiveScheduleEngine.Validate(merged);
        if (!validation.Valid)
            throw Invalid("schedule", "The reviewed merge does not form a valid task network: " + string.Join(" ", validation.Issues.Where(i => i.Severity == "error").Take(5).Select(i => i.Message)));
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(merged);
        if (!schedule.Valid) throw Invalid("schedule", "The reviewed network could not be scheduled without changing its constraints.");
        return merged with { Tasks = tasks.Select(t =>
        {
            var scheduled = schedule.Tasks.Single(s => s.WbsNumber == t.WbsNumber);
            return t with { EstimatedStartDate = scheduled.StartDate, EstimatedFinishDate = scheduled.EndDate };
        }).ToArray() };
    }

    public static string Fingerprint(Guid runId, Guid? rowVersion, ProjectFlowHivePlanRequest merged,
        IReadOnlyList<ProjectFlowHiveExistingTaskDecision> decisions, string note) => Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { contract = Contract, runId, rowVersion, merged,
                decisions = decisions.OrderBy(d => d.ExistingWbs, StringComparer.Ordinal), note }, Json))));

    public static bool MatchesAppliedReview(string fingerprint, string note, Guid? expectedVersion,
        IReadOnlyList<ProjectFlowHiveExistingTaskDecision> storedDecisions, ProjectFlowHivePlannerReviewRequest request) =>
        fingerprint == request.PreviewFingerprint && note == request.ReviewNote?.Trim() &&
        expectedVersion == request.ExpectedWorkingRowVersion && request.Decisions is not null &&
        request.Decisions.All(d => d is not null && !string.IsNullOrWhiteSpace(d.ExistingWbs)) &&
        storedDecisions.OrderBy(d => d.ExistingWbs, StringComparer.Ordinal)
            .SequenceEqual(request.Decisions.OrderBy(d => d.ExistingWbs, StringComparer.Ordinal));

    private static Guid NewId(Guid run, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Contract}|{run:D}|{key}"))[..16]);
    private static InvalidOperationException Invalid(string code, string message) => new($"flowhive_review_{code}: {message}");
}

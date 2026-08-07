using System.Text.RegularExpressions;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Deterministic, side-effect-free schedule validation and calculation for
/// Module 066C. Live schedule persistence and holiday-calendar authority remain
/// outside this engine.
/// </summary>
public static partial class ProjectFlowHiveScheduleEngine
{
    public const string ContractVersion = "066.2-ai-planner";
    private const int MaximumTasks = 500;
    private const int MaximumDependencies = 4000;
    private const int MaximumAssignments = 5000;
    private static readonly HashSet<string> DependencyTypes =
        new(StringComparer.OrdinalIgnoreCase) { "FS", "SS", "FF", "SF" };
    private static readonly HashSet<string> ConstraintTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ASAP", "SNET", "SNLT", "MSO", "MFO"
        };

    [GeneratedRegex(@"^\d+(?:\.\d+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex WbsPattern();

    public static ProjectFlowHivePlanValidationResult Validate(ProjectFlowHivePlanRequest? request)
    {
        var issues = ValidateCore(request, out _, out _, out _);
        var tasks = request?.Tasks ?? [];
        var dependencies = request?.Dependencies ?? [];
        var assignments = request?.Assignments ?? [];

        return new ProjectFlowHivePlanValidationResult(
            !issues.Any(issue => issue.Severity == "error"),
            issues,
            tasks.Count,
            dependencies.Count,
            assignments.Count,
            assignments.Sum(row => Math.Max(0m, row.PlannedHours)),
            ContractVersion);
    }

    public static ProjectFlowHiveScheduleResult Calculate(ProjectFlowHivePlanRequest? request)
    {
        var issues = ValidateCore(request, out var taskByWbs, out var dependencies, out var order);
        var hasErrors = issues.Any(issue => issue.Severity == "error");

        if (request is null || hasErrors || request.ProjectStartDate is null)
        {
            return new ProjectFlowHiveScheduleResult(
                false,
                "validation_failed",
                request?.ProjectStartDate,
                request?.ProjectEndDate,
                null,
                0,
                0,
                request?.Assignments?.Sum(row => Math.Max(0m, row.PlannedHours)) ?? 0m,
                [],
                issues,
                "weekday_preview_module_057_not_applied",
                ContractVersion);
        }

        var earliest = taskByWbs.Keys.ToDictionary(key => key, _ => 0, StringComparer.OrdinalIgnoreCase);
        var duration = taskByWbs.ToDictionary(
            pair => pair.Key,
            pair => EffectiveDuration(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        foreach (var wbs in order)
        {
            var task = taskByWbs[wbs];
            earliest[wbs] = Math.Max(
                earliest[wbs],
                ConstraintFloor(request.ProjectStartDate.Value, task, duration[wbs]));

            foreach (var dependency in dependencies.Where(row =>
                         string.Equals(Clean(row.SuccessorWbs), wbs, StringComparison.OrdinalIgnoreCase)))
            {
                var predecessor = Clean(dependency.PredecessorWbs)!;
                var candidate = earliest[predecessor]
                    + StartOffset(
                        dependency.Type,
                        duration[predecessor],
                        duration[wbs],
                        dependency.LagWorkingDays);
                earliest[wbs] = Math.Max(earliest[wbs], candidate);
            }
        }

        foreach (var pair in taskByWbs.Where(pair => !pair.Value.IsSummary))
        {
            var task = pair.Value;
            if (task.ConstraintDate is null) continue;
            var type = (Clean(task.ConstraintType) ?? "ASAP").ToUpperInvariant();
            var targetIndex = WorkingDayDistance(request.ProjectStartDate.Value, task.ConstraintDate.Value);
            var actualStart = earliest[pair.Key];
            var actualFinish = actualStart + duration[pair.Key] - 1;
            var conflict = type switch
            {
                "MSO" => actualStart != targetIndex,
                "MFO" => actualFinish != targetIndex,
                "SNLT" => actualStart > targetIndex,
                _ => false
            };
            if (conflict)
            {
                Error(
                    issues,
                    "constraint_conflict",
                    $"tasks[{pair.Key}].constraintDate",
                    $"Dependency logic conflicts with the {type} constraint on WBS {pair.Key}.");
            }
        }

        if (issues.Any(issue => issue.Severity == "error"))
        {
            return new ProjectFlowHiveScheduleResult(
                false,
                "validation_failed",
                request.ProjectStartDate,
                request.ProjectEndDate,
                null,
                0,
                0,
                request.Assignments?.Sum(row => Math.Max(0m, row.PlannedHours)) ?? 0m,
                [],
                issues,
                "weekday_preview_module_057_not_applied",
                ContractVersion);
        }

        var projectFinishIndex = order.Max(wbs => earliest[wbs] + duration[wbs] - 1);
        var latest = order.ToDictionary(
            key => key,
            key => projectFinishIndex - duration[key] + 1,
            StringComparer.OrdinalIgnoreCase);

        foreach (var predecessor in order.AsEnumerable().Reverse())
        {
            foreach (var dependency in dependencies.Where(row =>
                         string.Equals(Clean(row.PredecessorWbs), predecessor, StringComparison.OrdinalIgnoreCase)))
            {
                var successor = Clean(dependency.SuccessorWbs)!;
                var weight = StartOffset(
                    dependency.Type,
                    duration[predecessor],
                    duration[successor],
                    dependency.LagWorkingDays);
                latest[predecessor] = Math.Min(latest[predecessor], latest[successor] - weight);
            }
        }

        var scheduledDetails = order.Select(wbs =>
        {
            var task = taskByWbs[wbs];
            var outgoing = dependencies
                .Where(row => string.Equals(Clean(row.PredecessorWbs), wbs, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var freeFloat = outgoing.Length == 0
                ? projectFinishIndex - (earliest[wbs] + duration[wbs] - 1)
                : outgoing.Min(row =>
                {
                    var successor = Clean(row.SuccessorWbs)!;
                    var weight = StartOffset(
                        row.Type,
                        duration[wbs],
                        duration[successor],
                        row.LagWorkingDays);
                    return earliest[successor] - earliest[wbs] - weight;
                });
            var totalFloat = Math.Max(0, latest[wbs] - earliest[wbs]);

            return new ProjectFlowHiveScheduledTask(
                wbs,
                Clean(task.ParentWbsNumber),
                Clean(task.Name) ?? "Untitled task",
                AddWorkingDays(request.ProjectStartDate.Value, earliest[wbs]),
                AddWorkingDays(request.ProjectStartDate.Value, earliest[wbs] + duration[wbs] - 1),
                task.IsMilestone ? 0 : task.DurationWorkingDays,
                earliest[wbs],
                latest[wbs],
                totalFloat,
                Math.Max(0, freeFloat),
                totalFloat == 0,
                task.IsMilestone,
                task.PercentComplete,
                task.RemainingEffortHours,
                Clean(task.Status) ?? "not_started",
                false,
                Clean(task.Phase) ?? string.Empty);
        }).ToDictionary(task => task.WbsNumber, StringComparer.OrdinalIgnoreCase);

        var scheduled = (request.Tasks ?? []).Select(task =>
        {
            var wbs = Clean(task.WbsNumber)!;
            if (!task.IsSummary) return scheduledDetails[wbs];

            var descendants = scheduledDetails.Values
                .Where(candidate => candidate.WbsNumber.StartsWith($"{wbs}.", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var startIndex = descendants.Min(candidate => candidate.EarliestStartIndex);
            var endIndex = descendants.Max(candidate => candidate.EarliestStartIndex + Math.Max(1, candidate.DurationWorkingDays) - 1);
            var completed = descendants.All(candidate => candidate.Status.Equals("complete", StringComparison.OrdinalIgnoreCase));
            var started = descendants.Any(candidate => candidate.PercentComplete > 0m
                || candidate.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase));
            return new ProjectFlowHiveScheduledTask(
                wbs,
                Clean(task.ParentWbsNumber),
                Clean(task.Name) ?? "Untitled phase",
                AddWorkingDays(request.ProjectStartDate.Value, startIndex),
                AddWorkingDays(request.ProjectStartDate.Value, endIndex),
                endIndex - startIndex + 1,
                startIndex,
                descendants.Min(candidate => candidate.LatestStartIndex),
                descendants.Min(candidate => candidate.TotalFloatWorkingDays),
                descendants.Min(candidate => candidate.FreeFloatWorkingDays),
                descendants.Any(candidate => candidate.IsCritical),
                false,
                descendants.Average(candidate => candidate.PercentComplete),
                descendants.Sum(candidate => candidate.RemainingEffortHours),
                completed ? "complete" : started ? "in_progress" : "not_started",
                true,
                Clean(task.Phase) ?? Clean(task.Name) ?? string.Empty);
        }).ToArray();

        var projectFinishDate = AddWorkingDays(request.ProjectStartDate.Value, projectFinishIndex);
        if (request.ProjectEndDate.HasValue && projectFinishDate > request.ProjectEndDate.Value)
        {
            Error(
                issues,
                "project_end_exceeded",
                "projectEndDate",
                $"The generated schedule finishes on {projectFinishDate:yyyy-MM-dd}, after the selected end date {request.ProjectEndDate.Value:yyyy-MM-dd}.");
        }
        var valid = !issues.Any(issue => issue.Severity == "error");

        return new ProjectFlowHiveScheduleResult(
            valid,
            valid ? "calculated_preview" : "selected_window_exceeded",
            request.ProjectStartDate,
            request.ProjectEndDate,
            projectFinishDate,
            projectFinishIndex + 1,
            scheduled.Count(task => task.IsCritical && !task.IsSummary),
            request.Assignments?.Sum(row => Math.Max(0m, row.PlannedHours)) ?? 0m,
            scheduled,
            issues,
            "weekday_preview_module_057_not_applied",
            ContractVersion);
    }

    private static List<ProjectFlowHiveValidationIssue> ValidateCore(
        ProjectFlowHivePlanRequest? request,
        out Dictionary<string, ProjectFlowHivePlanTaskInput> taskByWbs,
        out ProjectFlowHiveDependencyInput[] dependencies,
        out string[] order)
    {
        var issues = new List<ProjectFlowHiveValidationIssue>();
        taskByWbs = new Dictionary<string, ProjectFlowHivePlanTaskInput>(StringComparer.OrdinalIgnoreCase);
        dependencies = request?.Dependencies?.ToArray() ?? [];
        order = [];

        if (request is null)
        {
            Error(issues, "plan_required", "$", "A plan request is required.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.PlanName))
        {
            Error(issues, "plan_name_required", "planName", "Plan name is required.");
        }

        if (request.ProjectStartDate is null)
        {
            Error(issues, "project_start_required", "projectStartDate", "Project start date is required.");
        }
        if (request.ProjectStartDate.HasValue
            && request.ProjectEndDate.HasValue
            && request.ProjectEndDate.Value < request.ProjectStartDate.Value)
        {
            Error(issues, "project_end_before_start", "projectEndDate", "Project end date must be on or after the project start date.");
        }

        var tasks = request.Tasks?.ToArray() ?? [];
        if (tasks.Length == 0)
        {
            Error(issues, "tasks_required", "tasks", "At least one task is required.");
        }
        if (tasks.Length > MaximumTasks)
        {
            Error(issues, "task_limit", "tasks", $"No more than {MaximumTasks} tasks can be validated at once.");
        }

        for (var index = 0; index < tasks.Length; index++)
        {
            var task = tasks[index];
            var path = $"tasks[{index}]";
            var wbs = Clean(task.WbsNumber);

            if (wbs is null || !WbsPattern().IsMatch(wbs))
            {
                Error(issues, "invalid_wbs", $"{path}.wbsNumber", "WBS numbers must use numeric dotted hierarchy such as 1 or 1.2.3.");
                continue;
            }
            if (!taskByWbs.TryAdd(wbs, task))
            {
                Error(issues, "duplicate_wbs", $"{path}.wbsNumber", $"WBS {wbs} is duplicated.");
            }
            if (string.IsNullOrWhiteSpace(task.Name))
            {
                Error(issues, "task_name_required", $"{path}.name", "Task name is required.");
            }
            if (task.IsSummary && task.DurationWorkingDays != 0)
            {
                Error(issues, "summary_duration", $"{path}.durationWorkingDays", "Phase summary rows must have zero duration; their dates roll up from child tasks.");
            }
            else if (task.IsMilestone && task.DurationWorkingDays != 0)
            {
                Error(issues, "milestone_duration", $"{path}.durationWorkingDays", "Milestones must have zero duration.");
            }
            if (!task.IsSummary && !task.IsMilestone && (task.DurationWorkingDays < 1 || task.DurationWorkingDays > 730))
            {
                Error(issues, "invalid_duration", $"{path}.durationWorkingDays", "Task duration must be between 1 and 730 working days.");
            }
            if (task.IsSummary && task.IsMilestone)
            {
                Error(issues, "summary_milestone_conflict", path, "A phase summary cannot also be a milestone.");
            }
            if (task.IsSummary && !string.IsNullOrWhiteSpace(task.ParentWbsNumber))
            {
                Error(issues, "summary_must_be_root", $"{path}.parentWbsNumber", "Plan, Design, Implement, Validate, and Release summaries must be root WBS rows.");
            }
            if (task.PercentComplete is < 0m or > 100m)
            {
                Error(issues, "invalid_percent_complete", $"{path}.percentComplete", "Percent complete must be from 0 through 100.");
            }
            if (task.RemainingEffortHours < 0m)
            {
                Error(issues, "invalid_remaining_effort", $"{path}.remainingEffortHours", "Remaining effort cannot be negative.");
            }
            var constraint = Clean(task.ConstraintType) ?? "ASAP";
            if (!ConstraintTypes.Contains(constraint))
            {
                Error(issues, "invalid_constraint", $"{path}.constraintType", "Constraint type must be ASAP, SNET, SNLT, MSO, or MFO.");
            }
            if (!constraint.Equals("ASAP", StringComparison.OrdinalIgnoreCase) && task.ConstraintDate is null)
            {
                Error(issues, "constraint_date_required", $"{path}.constraintDate", "The selected constraint requires a date.");
            }
        }

        foreach (var pair in taskByWbs)
        {
            var parent = Clean(pair.Value.ParentWbsNumber);
            var expectedParent = ImmediateParentWbs(pair.Key);
            if (parent is null)
            {
                if (expectedParent is not null)
                {
                    Error(issues, "parent_required", $"tasks[{pair.Key}].parentWbsNumber", $"WBS {pair.Key} requires immediate parent {expectedParent}.");
                }
                continue;
            }
            if (!taskByWbs.ContainsKey(parent))
            {
                Error(issues, "parent_not_found", $"tasks[{pair.Key}].parentWbsNumber", $"Parent WBS {parent} does not exist.");
            }
            else if (!string.Equals(parent, expectedParent, StringComparison.OrdinalIgnoreCase))
            {
                Error(issues, "parent_hierarchy_mismatch", $"tasks[{pair.Key}].parentWbsNumber", $"WBS {pair.Key} requires immediate parent {expectedParent ?? "none"}, not {parent}.");
            }
        }

        var executableTaskCount = taskByWbs.Count(pair => !pair.Value.IsSummary);
        if (executableTaskCount == 0)
        {
            Error(issues, "executable_tasks_required", "tasks", "At least one executable task is required beneath the phase summary rows.");
        }
        foreach (var summary in taskByWbs.Where(pair => pair.Value.IsSummary))
        {
            if (!taskByWbs.Keys.Any(wbs => wbs.StartsWith($"{summary.Key}.", StringComparison.OrdinalIgnoreCase)))
            {
                Error(issues, "summary_children_required", $"tasks[{summary.Key}]", $"Phase WBS {summary.Key} requires at least one child task.");
            }
        }

        if (dependencies.Length > MaximumDependencies)
        {
            Error(issues, "dependency_limit", "dependencies", $"No more than {MaximumDependencies} dependencies can be validated at once.");
        }

        var dependencyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < dependencies.Length; index++)
        {
            var dependency = dependencies[index];
            var path = $"dependencies[{index}]";
            var predecessor = Clean(dependency.PredecessorWbs);
            var successor = Clean(dependency.SuccessorWbs);
            var type = (Clean(dependency.Type) ?? "FS").ToUpperInvariant();

            if (predecessor is null || !taskByWbs.ContainsKey(predecessor))
            {
                Error(issues, "predecessor_not_found", $"{path}.predecessorWbs", "Dependency predecessor must reference an existing WBS.");
            }
            if (successor is null || !taskByWbs.ContainsKey(successor))
            {
                Error(issues, "successor_not_found", $"{path}.successorWbs", "Dependency successor must reference an existing WBS.");
            }
            if (predecessor is not null && successor is not null
                && predecessor.Equals(successor, StringComparison.OrdinalIgnoreCase))
            {
                Error(issues, "self_dependency", path, "A task cannot depend on itself.");
            }
            if (predecessor is not null
                && taskByWbs.TryGetValue(predecessor, out var predecessorTask)
                && predecessorTask.IsSummary)
            {
                Error(issues, "summary_dependency_not_allowed", $"{path}.predecessorWbs", "Phase summaries cannot be dependency endpoints; use executable child tasks.");
            }
            if (successor is not null
                && taskByWbs.TryGetValue(successor, out var successorTask)
                && successorTask.IsSummary)
            {
                Error(issues, "summary_dependency_not_allowed", $"{path}.successorWbs", "Phase summaries cannot be dependency endpoints; use executable child tasks.");
            }
            if (!DependencyTypes.Contains(type))
            {
                Error(issues, "invalid_dependency_type", $"{path}.type", "Dependency type must be FS, SS, FF, or SF.");
            }
            if (dependency.LagWorkingDays is < -365 or > 365)
            {
                Error(issues, "invalid_lag", $"{path}.lagWorkingDays", "Lead or lag must be between -365 and 365 working days.");
            }
            var key = $"{predecessor}|{successor}|{type}";
            if (!dependencyKeys.Add(key))
            {
                Error(issues, "duplicate_dependency", path, "The same dependency is listed more than once.");
            }
        }

        var assignments = request.Assignments?.ToArray() ?? [];
        if (assignments.Length > MaximumAssignments)
        {
            Error(issues, "assignment_limit", "assignments", $"No more than {MaximumAssignments} assignments can be validated at once.");
        }
        for (var index = 0; index < assignments.Length; index++)
        {
            var assignment = assignments[index];
            var path = $"assignments[{index}]";
            var wbs = Clean(assignment.TaskWbs);
            if (wbs is null || !taskByWbs.ContainsKey(wbs))
            {
                Error(issues, "assignment_task_not_found", $"{path}.taskWbs", "Assignment task must reference an existing WBS.");
            }
            else if (taskByWbs[wbs].IsSummary)
            {
                Error(issues, "summary_assignment_not_allowed", $"{path}.taskWbs", "Assignments belong to executable child tasks, not phase summary rows.");
            }
            if (assignment.ResourceUserId is null || assignment.ResourceUserId == Guid.Empty)
            {
                Error(issues, "assignment_identity_required", $"{path}.resourceUserId", "Assignments require a Module 062-backed ProjectPulse identity ID.");
            }
            if (assignment.AllocationPercent is <= 0m or > 100m)
            {
                Error(issues, "invalid_allocation", $"{path}.allocationPercent", "Allocation must be greater than zero and no more than 100 percent.");
            }
            if (assignment.PlannedHours < 0m)
            {
                Error(issues, "invalid_planned_hours", $"{path}.plannedHours", "Planned hours cannot be negative.");
            }
        }

        if (!issues.Any(issue => issue.Severity == "error"))
        {
            order = TopologicalOrder(
                taskByWbs.Where(pair => !pair.Value.IsSummary).Select(pair => pair.Key),
                dependencies,
                issues);
        }

        return issues;
    }

    private static string[] TopologicalOrder(
        IEnumerable<string> taskKeys,
        IReadOnlyList<ProjectFlowHiveDependencyInput> dependencies,
        List<ProjectFlowHiveValidationIssue> issues)
    {
        var keys = taskKeys.ToArray();
        var indegree = keys.ToDictionary(key => key, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outgoing = keys.ToDictionary(
            key => key,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in dependencies)
        {
            var predecessor = Clean(dependency.PredecessorWbs);
            var successor = Clean(dependency.SuccessorWbs);
            if (predecessor is null || successor is null
                || !indegree.ContainsKey(predecessor) || !indegree.ContainsKey(successor)) continue;
            outgoing[predecessor].Add(successor);
            indegree[successor]++;
        }

        var queue = new PriorityQueue<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Where(key => indegree[key] == 0)) queue.Enqueue(key, SortableWbs(key));
        var order = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);
            foreach (var successor in outgoing[current])
            {
                indegree[successor]--;
                if (indegree[successor] == 0) queue.Enqueue(successor, SortableWbs(successor));
            }
        }

        if (order.Count != keys.Length)
        {
            Error(issues, "dependency_cycle", "dependencies", "The dependency network contains a cycle.");
            return [];
        }

        return order.ToArray();
    }

    private static int ConstraintFloor(
        DateOnly projectStart,
        ProjectFlowHivePlanTaskInput task,
        int duration)
    {
        var type = (Clean(task.ConstraintType) ?? "ASAP").ToUpperInvariant();
        if (task.ConstraintDate is null || type is "ASAP" or "SNLT") return 0;
        var targetIndex = WorkingDayDistance(projectStart, task.ConstraintDate.Value);
        return Math.Max(0, type == "MFO" ? targetIndex - duration + 1 : targetIndex);
    }

    private static int EffectiveDuration(ProjectFlowHivePlanTaskInput task) =>
        task.IsMilestone ? 1 : Math.Max(1, task.DurationWorkingDays);

    private static int StartOffset(string? type, int predecessorDuration, int successorDuration, int lag)
    {
        return Clean(type)?.ToUpperInvariant() switch
        {
            "SS" => lag,
            "FF" => predecessorDuration - successorDuration + lag,
            "SF" => 1 - successorDuration + lag,
            _ => predecessorDuration + lag
        };
    }

    public static DateOnly AddWorkingDays(DateOnly date, int offset)
    {
        var current = NormalizeToWorkingDay(date, offset >= 0 ? 1 : -1);
        var remaining = Math.Abs(offset);
        var direction = offset >= 0 ? 1 : -1;

        while (remaining > 0)
        {
            current = current.AddDays(direction);
            if (IsWorkingDay(current)) remaining--;
        }
        return current;
    }

    private static int WorkingDayDistance(DateOnly start, DateOnly end)
    {
        var normalizedStart = NormalizeToWorkingDay(start, 1);
        var normalizedEnd = NormalizeToWorkingDay(end, end >= normalizedStart ? 1 : -1);
        if (normalizedEnd == normalizedStart) return 0;
        var direction = normalizedEnd > normalizedStart ? 1 : -1;
        var distance = 0;
        var current = normalizedStart;
        while (current != normalizedEnd)
        {
            current = current.AddDays(direction);
            if (IsWorkingDay(current)) distance += direction;
        }
        return distance;
    }

    private static DateOnly NormalizeToWorkingDay(DateOnly date, int direction)
    {
        var current = date;
        while (!IsWorkingDay(current)) current = current.AddDays(direction);
        return current;
    }

    private static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static string SortableWbs(string wbs) => string.Join(
        '.',
        wbs.Split('.').Select(part => int.TryParse(part, out var value) ? value.ToString("D8") : part));

    private static string? ImmediateParentWbs(string wbs)
    {
        var lastSeparator = wbs.LastIndexOf('.');
        return lastSeparator > 0 ? wbs[..lastSeparator] : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Error(
        ICollection<ProjectFlowHiveValidationIssue> issues,
        string code,
        string path,
        string message)
    {
        issues.Add(new ProjectFlowHiveValidationIssue(code, "error", path, message));
    }
}

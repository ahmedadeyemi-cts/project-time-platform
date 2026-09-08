namespace ProjectTime.Api.Modules;

public sealed record ProjectFlowHiveCanonicalTaskFinancialSource(
    Guid TaskId,
    string TaskCode,
    decimal? EstimatedHours,
    decimal? HourlyRate);

public sealed record ProjectFlowHiveApprovedTimeSource(
    Guid TimeEntryId,
    Guid? TaskId,
    decimal Hours,
    string Status);

public sealed record ProjectFlowHiveTaskFinancialReadback(
    Guid TaskId,
    string TaskCode,
    decimal? EstimatedHours,
    decimal ApprovedHours,
    decimal? EstimatedEffortRemainingHours,
    decimal? HourlyRate,
    decimal? ActualCost);

public sealed record ProjectFlowHiveFinancialReadbackResult(
    decimal? ApprovedBudget,
    decimal ApprovedHours,
    decimal ActualHours,
    decimal? ApprovedLaborCost,
    decimal? BudgetRemaining,
    decimal? EstimatedEffortRemainingHours,
    decimal? ForecastAtCompletion,
    string ForecastSource,
    IReadOnlyList<ProjectFlowHiveTaskFinancialReadback> Tasks,
    IReadOnlyList<string> UnknownReasons);

/// <summary>
/// Reconciles the authoritative canonical task, approved-time, and project-control
/// snapshots without manufacturing a rate, budget, or forecast. The API adapter
/// supplies only rows the caller is authorized to read.
/// </summary>
public static class ProjectFlowHiveFinancialReadback
{
    private static readonly HashSet<string> ApprovedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pm_approved", "accounting_ready", "reconciled", "locked"
    };

    public static ProjectFlowHiveFinancialReadbackResult Calculate(
        decimal? approvedBudget,
        decimal? recordedForecastAtCompletion,
        IReadOnlyList<ProjectFlowHiveCanonicalTaskFinancialSource> tasks,
        IReadOnlyList<ProjectFlowHiveApprovedTimeSource> timeEntries)
    {
        var taskById = tasks.ToDictionary(task => task.TaskId);
        var approved = timeEntries.Where(entry => ApprovedStatuses.Contains(entry.Status)).ToArray();
        var hoursByTask = approved
            .Where(entry => entry.TaskId.HasValue && taskById.ContainsKey(entry.TaskId.Value))
            .GroupBy(entry => entry.TaskId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Hours));
        var unknownReasons = new HashSet<string>(StringComparer.Ordinal);
        if (approved.Any(entry => !entry.TaskId.HasValue || !taskById.ContainsKey(entry.TaskId.Value)))
            unknownReasons.Add("approved_time_without_a_known_canonical_task");

        var taskReadback = tasks.Select(task =>
        {
            var approvedHours = hoursByTask.GetValueOrDefault(task.TaskId);
            var remaining = task.EstimatedHours.HasValue
                ? Math.Max(task.EstimatedHours.Value - approvedHours, 0m)
                : (decimal?)null;
            var actualCost = approvedHours == 0m
                ? 0m
                : task.HourlyRate.HasValue
                    ? Math.Round(approvedHours * task.HourlyRate.Value, 2, MidpointRounding.AwayFromZero)
                    : null;
            if (approvedHours > 0m && !task.HourlyRate.HasValue)
                unknownReasons.Add($"missing_rate:{task.TaskCode}");
            return new ProjectFlowHiveTaskFinancialReadback(task.TaskId, task.TaskCode,
                task.EstimatedHours, approvedHours, remaining, task.HourlyRate, actualCost);
        }).ToArray();

        var approvedHoursTotal = approved.Sum(entry => entry.Hours);
        var approvedCost = approvedHoursTotal == 0m
            ? 0m
            : taskReadback.All(task => task.ActualCost.HasValue)
                ? taskReadback.Sum(task => task.ActualCost!.Value)
                : null;
        if (approvedHoursTotal > 0m && !approvedCost.HasValue)
            unknownReasons.Add("approved_labor_cost_requires_a_rate_for_each_approved_task");

        var remainingEffort = taskReadback.All(task => task.EstimatedEffortRemainingHours.HasValue)
            ? taskReadback.Sum(task => task.EstimatedEffortRemainingHours!.Value)
            : null;
        if (!remainingEffort.HasValue)
            unknownReasons.Add("estimated_effort_remaining_requires_task_estimates");

        decimal? forecast;
        string forecastSource;
        if (recordedForecastAtCompletion.HasValue)
        {
            forecast = recordedForecastAtCompletion.Value;
            forecastSource = "project_flowhive_project_controls";
        }
        else if (approvedCost.HasValue && remainingEffort.HasValue && taskReadback.All(task =>
                     task.EstimatedEffortRemainingHours is null
                     || task.EstimatedEffortRemainingHours.Value == 0m
                     || task.HourlyRate.HasValue))
        {
            var remainingCost = taskReadback.Sum(task =>
                task.EstimatedEffortRemainingHours.GetValueOrDefault() * task.HourlyRate.GetValueOrDefault());
            forecast = Math.Round(approvedCost.Value + remainingCost, 2, MidpointRounding.AwayFromZero);
            forecastSource = "canonical_task_estimate_and_rate_derivation";
        }
        else
        {
            forecast = null;
            forecastSource = "unknown";
            unknownReasons.Add("forecast_requires_a_recorded_forecast_or_complete_task_rates");
        }

        var budgetRemaining = approvedBudget.HasValue && forecast.HasValue
            ? Math.Round(approvedBudget.Value - forecast.Value, 2, MidpointRounding.AwayFromZero)
            : null;
        if (!budgetRemaining.HasValue)
            unknownReasons.Add("budget_remaining_requires_approved_budget_and_forecast");

        return new ProjectFlowHiveFinancialReadbackResult(
            approvedBudget, approvedHoursTotal, approvedHoursTotal, approvedCost,
            budgetRemaining, remainingEffort, forecast, forecastSource,
            taskReadback, unknownReasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray());
    }
}

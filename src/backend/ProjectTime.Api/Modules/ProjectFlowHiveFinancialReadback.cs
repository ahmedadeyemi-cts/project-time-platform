namespace ProjectTime.Api.Modules;

public sealed record ProjectFlowHiveRateSource(
    decimal? Amount,
    string Purpose,
    string? CurrencyCode,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string Authority,
    bool IsInternalLaborCost)
{
    public ProjectFlowHiveRateSource(
        decimal? amount,
        string purpose,
        string? currencyCode,
        DateOnly? effectiveFrom,
        string authority,
        bool isInternalLaborCost)
        : this(amount, purpose, currencyCode, effectiveFrom, null, authority, isInternalLaborCost)
    {
    }

    public bool HasCompleteInternalCostBasis =>
        Amount.HasValue
        && IsInternalLaborCost
        && string.Equals(Purpose, "internal_labor_cost", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(CurrencyCode)
        && CurrencyCode.Trim().Length == 3
        && EffectiveFrom.HasValue
        && !string.IsNullOrWhiteSpace(Authority);
}

public sealed record ProjectFlowHiveCanonicalTaskFinancialSource(
    Guid TaskId,
    string TaskCode,
    decimal? OriginalEstimateHours,
    decimal? ApprovedEstimateHours,
    decimal? CurrentEstimateToCompleteHours,
    bool IsActive,
    ProjectFlowHiveRateSource? RateSource)
{
    // Compatibility constructor for deterministic synthetic fixtures. New
    // callers must provide the distinct estimate and rate provenance fields.
    public ProjectFlowHiveCanonicalTaskFinancialSource(
        Guid taskId,
        string taskCode,
        decimal? estimatedHours,
        decimal? hourlyRate)
        : this(
            taskId,
            taskCode,
            estimatedHours,
            estimatedHours,
            estimatedHours,
            true,
            new ProjectFlowHiveRateSource(
                hourlyRate,
                "internal_labor_cost",
                "USD",
                new DateOnly(2026, 1, 1),
                "synthetic_fixture",
                true))
    {
    }
}

public sealed record ProjectFlowHiveApprovedTimeSource(
    Guid TimeEntryId,
    Guid? TaskId,
    decimal Hours,
    string Status,
    DateOnly? WorkDate)
{
    public ProjectFlowHiveApprovedTimeSource(
        Guid timeEntryId,
        Guid? taskId,
        decimal hours,
        string status)
        : this(timeEntryId, taskId, hours, status, new DateOnly(2026, 9, 8))
    {
    }
}

public sealed record ProjectFlowHiveTaskFinancialReadback(
    Guid TaskId,
    string TaskCode,
    bool IsActive,
    decimal? OriginalEstimateHours,
    decimal? ApprovedEstimateHours,
    decimal LoggedHours,
    decimal ApprovedHours,
    decimal? BudgetHoursRemaining,
    decimal? CurrentEstimateToCompleteHours,
    decimal? Rate,
    string RatePurpose,
    string? RateCurrency,
    DateOnly? RateEffectiveFrom,
    DateOnly? RateEffectiveTo,
    string RateAuthority,
    bool RateVerified,
    decimal? ActualLaborCost,
    string CostStatus);

public sealed record ProjectFlowHiveFinancialCompleteness(
    string Status,
    decimal KnownLoggedHours,
    decimal KnownApprovedHours,
    decimal UnmatchedApprovedHours,
    decimal? KnownApprovedLaborCost,
    int ActiveTaskCount,
    int InactiveTaskCount,
    IReadOnlyList<string> UnknownReasons,
    IReadOnlyList<string> DerivedAssumptions);

public sealed record ProjectFlowHiveFinancialReadbackResult(
    string? CurrencyCode,
    decimal? ApprovedBudget,
    decimal? OriginalEstimateHours,
    decimal? ApprovedEstimateHours,
    decimal LoggedHours,
    decimal ApprovedHours,
    decimal? BudgetHoursRemaining,
    decimal? CurrentEstimateToCompleteHours,
    decimal? KnownApprovedLaborCost,
    decimal? ApprovedLaborCost,
    decimal? BudgetRemainingAfterKnownActualCosts,
    decimal? BudgetRemainingAfterActualCosts,
    decimal? ForecastAtCompletion,
    string ForecastSource,
    string ForecastProvenance,
    decimal? ForecastVariance,
    ProjectFlowHiveFinancialCompleteness Completeness,
    IReadOnlyList<ProjectFlowHiveTaskFinancialReadback> Tasks,
    IReadOnlyList<string> UnknownReasons,
    IReadOnlyList<string> DerivedAssumptions);

/// <summary>
/// Reconciles authoritative task, time, rate, and project-control snapshots.
/// Billing or unclassified task rates never become internal labor cost without
/// purpose, currency, effective-date, and authority evidence.
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
        IReadOnlyList<ProjectFlowHiveApprovedTimeSource> timeEntries,
        string? projectCurrencyCode = "USD",
        string recordedForecastProvenance = "project_flowhive_project_controls")
    {
        var normalizedCurrency = NormalizeCurrency(projectCurrencyCode);
        var taskById = tasks.ToDictionary(task => task.TaskId);
        var approved = timeEntries.Where(entry => ApprovedStatuses.Contains(entry.Status)).ToArray();
        var loggedHours = timeEntries.Sum(entry => entry.Hours);
        var approvedHours = approved.Sum(entry => entry.Hours);
        var loggedByTask = timeEntries
            .Where(entry => entry.TaskId.HasValue && taskById.ContainsKey(entry.TaskId.Value))
            .GroupBy(entry => entry.TaskId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Hours));
        var approvedByTask = approved
            .Where(entry => entry.TaskId.HasValue && taskById.ContainsKey(entry.TaskId.Value))
            .GroupBy(entry => entry.TaskId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var unknownReasons = new HashSet<string>(StringComparer.Ordinal);
        var derivedAssumptions = new HashSet<string>(StringComparer.Ordinal);

        var unmatchedApproved = approved
            .Where(entry => !entry.TaskId.HasValue || !taskById.ContainsKey(entry.TaskId.Value))
            .Sum(entry => entry.Hours);
        if (unmatchedApproved > 0m)
            unknownReasons.Add("approved_time_without_a_known_canonical_task");

        var taskReadback = tasks.Select(task =>
        {
            var taskApprovedEntries = approvedByTask.GetValueOrDefault(task.TaskId) ?? [];
            var taskApprovedHours = taskApprovedEntries.Sum(entry => entry.Hours);
            var taskLoggedHours = loggedByTask.GetValueOrDefault(task.TaskId);
            var budgetHoursRemaining = task.ApprovedEstimateHours.HasValue
                ? task.ApprovedEstimateHours.Value - taskApprovedHours
                : (decimal?)null;
            if (!task.OriginalEstimateHours.HasValue)
                unknownReasons.Add($"missing_original_estimate:{task.TaskCode}");
            if (!task.ApprovedEstimateHours.HasValue)
                unknownReasons.Add($"missing_approved_estimate:{task.TaskCode}");
            if (!task.CurrentEstimateToCompleteHours.HasValue)
                unknownReasons.Add($"missing_current_estimate_to_complete:{task.TaskCode}");

            var rate = task.RateSource;
            var rateCurrencyMismatch = rate?.CurrencyCode is not null
                && normalizedCurrency is not null
                && !string.Equals(NormalizeCurrency(rate.CurrencyCode), normalizedCurrency, StringComparison.Ordinal);
            var dateOutOfRange = taskApprovedEntries.Any(entry =>
                (entry.WorkDate.HasValue
                 && rate?.EffectiveFrom.HasValue == true
                 && entry.WorkDate.Value < rate.EffectiveFrom.Value)
                || (entry.WorkDate.HasValue
                    && rate?.EffectiveTo.HasValue == true
                    && entry.WorkDate.Value > rate.EffectiveTo.Value));
            var hasUsableRate = rate?.HasCompleteInternalCostBasis == true
                && !rateCurrencyMismatch
                && !dateOutOfRange;
            if (taskApprovedHours > 0m && !hasUsableRate)
            {
                unknownReasons.Add($"internal_cost_rate_not_verified:{task.TaskCode}");
                if (rate is null || !rate.Amount.HasValue)
                    unknownReasons.Add($"missing_rate:{task.TaskCode}");
                else if (!string.Equals(rate.Purpose, "internal_labor_cost", StringComparison.OrdinalIgnoreCase))
                    unknownReasons.Add($"rate_purpose_not_internal_cost:{task.TaskCode}");
                if (rateCurrencyMismatch)
                    unknownReasons.Add($"rate_currency_mismatch:{task.TaskCode}");
                if (dateOutOfRange)
                    unknownReasons.Add($"rate_not_effective_for_time:{task.TaskCode}");
            }

            decimal? actualLaborCost = taskApprovedHours == 0m
                ? 0m
                : hasUsableRate
                    ? Math.Round(taskApprovedHours * rate!.Amount!.Value, 2, MidpointRounding.AwayFromZero)
                    : null;
            return new ProjectFlowHiveTaskFinancialReadback(
                task.TaskId,
                task.TaskCode,
                task.IsActive,
                task.OriginalEstimateHours,
                task.ApprovedEstimateHours,
                taskLoggedHours,
                taskApprovedHours,
                budgetHoursRemaining,
                task.CurrentEstimateToCompleteHours,
                rate?.Amount,
                rate?.Purpose ?? "unknown",
                rate?.CurrencyCode,
                rate?.EffectiveFrom,
                rate?.EffectiveTo,
                rate?.Authority ?? "unknown",
                hasUsableRate,
                actualLaborCost,
                taskApprovedHours == 0m ? "no_approved_hours" : hasUsableRate ? "verified" : "unknown");
        }).ToArray();

        var knownApprovedLaborCost = taskReadback
            .Where(task => task.ActualLaborCost.HasValue)
            .Sum(task => task.ActualLaborCost!.Value);
        var completeLaborCost = unmatchedApproved == 0m
            && taskReadback.All(task => task.ApprovedHours == 0m || task.ActualLaborCost.HasValue);
        var approvedLaborCost = completeLaborCost ? knownApprovedLaborCost : null;
        if (!completeLaborCost)
            unknownReasons.Add("approved_labor_cost_is_incomplete");

        var originalEstimateHours = SumIfComplete(tasks.Select(task => task.OriginalEstimateHours), "original_estimate_hours", unknownReasons);
        var approvedEstimateHours = SumIfComplete(tasks.Select(task => task.ApprovedEstimateHours), "approved_estimate_hours", unknownReasons);
        var budgetHoursRemaining = SumIfComplete(taskReadback.Select(task => task.BudgetHoursRemaining), "budget_hours_remaining", unknownReasons);
        var currentEstimateToCompleteHours = SumIfComplete(taskReadback.Select(task => task.CurrentEstimateToCompleteHours), "current_estimate_to_complete_hours", unknownReasons);
        var budgetRemainingAfterKnownActualCosts = approvedBudget.HasValue
            ? Math.Round(approvedBudget.Value - knownApprovedLaborCost, 2, MidpointRounding.AwayFromZero)
            : null;
        if (!approvedBudget.HasValue)
            unknownReasons.Add("budget_remaining_requires_approved_budget");
        var budgetRemainingAfterActualCosts = approvedBudget.HasValue && approvedLaborCost.HasValue
            ? Math.Round(approvedBudget.Value - approvedLaborCost.Value, 2, MidpointRounding.AwayFromZero)
            : null;

        decimal? forecast;
        string forecastSource;
        string forecastProvenance;
        if (recordedForecastAtCompletion.HasValue)
        {
            forecast = recordedForecastAtCompletion.Value;
            forecastSource = "recorded";
            forecastProvenance = string.IsNullOrWhiteSpace(recordedForecastProvenance)
                ? "recorded_authoritative_source"
                : recordedForecastProvenance;
        }
        else if (approvedLaborCost.HasValue
                 && currentEstimateToCompleteHours.HasValue
                 && taskReadback.All(task => task.CurrentEstimateToCompleteHours.GetValueOrDefault() == 0m || task.RateVerified))
        {
            var remainingCost = taskReadback.Sum(task =>
                task.CurrentEstimateToCompleteHours.GetValueOrDefault() * task.Rate.GetValueOrDefault());
            forecast = Math.Round(approvedLaborCost.Value + remainingCost, 2, MidpointRounding.AwayFromZero);
            forecastSource = "derived";
            forecastProvenance = "canonical_task_current_estimate_to_complete_and_verified_internal_rate";
            derivedAssumptions.Add("Derived forecast excludes expenses and commitments because no authoritative expense or commitment snapshot was supplied.");
        }
        else
        {
            forecast = null;
            forecastSource = "unknown";
            forecastProvenance = "unknown";
            unknownReasons.Add("forecast_requires_recorded_forecast_or_complete_current_estimate_and_rate_basis");
        }

        var forecastVariance = approvedBudget.HasValue && forecast.HasValue
            ? Math.Round(approvedBudget.Value - forecast.Value, 2, MidpointRounding.AwayFromZero)
            : null;
        if (!forecastVariance.HasValue)
            unknownReasons.Add("forecast_variance_requires_approved_budget_and_forecast");
        if (!normalizedCurrency.HasValue)
            unknownReasons.Add("project_currency_unknown");
        if (unmatchedApproved > 0m)
            derivedAssumptions.Add("Known task subtotals exclude approved time that cannot be matched to a canonical task.");
        if (tasks.Any(task => !task.IsActive))
            derivedAssumptions.Add("Inactive canonical tasks remain included because historical approved time must remain attributable.");

        var activeCount = tasks.Count(task => task.IsActive);
        var inactiveCount = tasks.Count - activeCount;
        var status = unknownReasons.Count == 0 ? "complete" : "incomplete";
        var completeness = new ProjectFlowHiveFinancialCompleteness(
            status,
            loggedHours,
            approvedHours - unmatchedApproved,
            unmatchedApproved,
            knownApprovedLaborCost,
            activeCount,
            inactiveCount,
            unknownReasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray(),
            derivedAssumptions.OrderBy(reason => reason, StringComparer.Ordinal).ToArray());

        return new ProjectFlowHiveFinancialReadbackResult(
            normalizedCurrency,
            approvedBudget,
            originalEstimateHours,
            approvedEstimateHours,
            loggedHours,
            approvedHours,
            budgetHoursRemaining,
            currentEstimateToCompleteHours,
            knownApprovedLaborCost,
            approvedLaborCost,
            budgetRemainingAfterKnownActualCosts,
            budgetRemainingAfterActualCosts,
            forecast,
            forecastSource,
            forecastProvenance,
            forecastVariance,
            completeness,
            taskReadback,
            completeness.UnknownReasons,
            completeness.DerivedAssumptions);
    }

    private static decimal? SumIfComplete(
        IEnumerable<decimal?> values,
        string field,
        ISet<string> unknownReasons)
    {
        var materialized = values.ToArray();
        if (materialized.Any(value => !value.HasValue))
        {
            unknownReasons.Add($"{field}_is_incomplete");
            return null;
        }
        return materialized.Sum(value => value!.Value);
    }

    private static string? NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? null
            : currency.Trim().ToUpperInvariant() is { Length: 3 } normalized ? normalized : null;
}

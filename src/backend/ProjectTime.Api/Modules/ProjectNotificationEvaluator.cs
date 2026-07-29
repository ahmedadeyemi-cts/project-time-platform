using System.Net.Mail;
using System.Net;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class ProjectNotificationEvaluator
{
    internal static readonly HashSet<string> AllowedMetricCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "hours_used_percent",
        "labor_budget_used_percent",
        "expenses_used_percent",
        "forecasted_total_cost",
        "approaching_budget",
        "over_budget",
        "missing_financial_information",
        "failed_project_data_refresh"
    };

    internal static readonly HashSet<string> AllowedRecipientRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "project_manager",
        "assigned_engineers",
        "solution_architect",
        "account_executive",
        "project_team_coordinator",
        "escalation_manager"
    };

    internal static bool CanQueueCloseout(ProjectNotificationActor actor) => !actor.IsViewAs && (
        actor.CanDeliver
        || actor.Permissions.Contains("VIEW_CLOSEOUT_NOTIFICATION_ROUTING")
        || actor.Roles.Contains("PROJECT_MANAGER")
        || actor.Roles.Contains("PROJECT_MANAGEMENT")
        || actor.Roles.Contains("PROJECT_MANAGEMENT_LEAD")
        || actor.Roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
        || actor.Roles.Contains("PM_TEAM_LEAD"));

    internal static bool CanAccessProject(
        ProjectNotificationActor actor,
        ProjectNotificationFinancialSnapshot project) =>
        ProjectNotificationRepository.IsBroad(actor)
        || project.ProjectManager?.UserId == actor.EffectiveUserId
        || project.ProjectTeamCoordinator?.UserId == actor.EffectiveUserId
        || project.SolutionArchitect?.UserId == actor.EffectiveUserId
        || project.AccountExecutive?.UserId == actor.EffectiveUserId
        || project.Engineers.Any(engineer => engineer.UserId == actor.EffectiveUserId);

    internal static ProjectNotificationMetricEvaluation EvaluateRule(
        ProjectCostRoutingRule rule,
        ProjectNotificationFinancialSnapshot project)
    {
        decimal? observed = null;
        decimal? comparison = rule.ThresholdValue;
        var unit = rule.ThresholdUnit;
        var triggered = false;
        var reason = string.Empty;

        switch (rule.MetricCode)
        {
            case "hours_used_percent":
                observed = project.PlannedHours > 0
                    ? Math.Round(project.UsedHours / project.PlannedHours * 100m, 2)
                    : null;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? $"Used hours are {observed.Value:0.##}% of planned hours."
                    : "Planned hours are not recorded.";
                break;
            case "labor_budget_used_percent":
                observed = project.LaborBudget is > 0 && project.LaborCost.HasValue
                    ? Math.Round(project.LaborCost.Value / project.LaborBudget.Value * 100m, 2)
                    : null;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? $"Calculated labor cost is {observed.Value:0.##}% of the known labor budget."
                    : "Labor budget or governed rate evidence is missing.";
                break;
            case "expenses_used_percent":
                observed = project.ExpenseBudget is > 0 && project.UploadedExpenses.HasValue
                    ? Math.Round(project.UploadedExpenses.Value / project.ExpenseBudget.Value * 100m, 2)
                    : null;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? $"Current Module 005 expenses are {observed.Value:0.##}% of the known expense budget."
                    : "Expense budget or current Module 005 expense evidence is missing.";
                break;
            case "forecasted_total_cost":
                var totalBudget = SumKnown(project.LaborBudget, project.ExpenseBudget);
                observed = rule.ThresholdUnit == "percent"
                    && totalBudget is > 0
                    && project.ForecastedFinalCost.HasValue
                        ? Math.Round(project.ForecastedFinalCost.Value / totalBudget.Value * 100m, 2)
                        : project.ForecastedFinalCost;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? rule.ThresholdUnit == "percent"
                        ? $"Forecasted final cost is {observed.Value:0.##}% of the known project budget."
                        : $"Forecasted final cost is {observed.Value:C}."
                    : "Forecasted final cost is incomplete.";
                break;
            case "approaching_budget":
                triggered = project.BudgetStatus == "approaching_budget";
                reason = triggered
                    ? "The authoritative project financial status is approaching budget."
                    : "The project is not currently approaching budget.";
                break;
            case "over_budget":
                triggered = project.BudgetStatus == "over_budget";
                reason = triggered
                    ? "The authoritative project financial status is over budget."
                    : "The project is not currently over budget.";
                break;
            case "missing_financial_information":
                observed = project.MissingFinancialInformation.Length;
                comparison = 0m;
                unit = "count";
                triggered = project.MissingFinancialInformation.Length > 0;
                reason = triggered
                    ? $"Missing project financial information: {string.Join(", ", project.MissingFinancialInformation)}."
                    : "Required project financial information is recorded.";
                break;
        }

        return new(triggered, observed, comparison, unit, reason);
    }

    internal static async Task<ProjectNotificationUser[]> DeriveRecipientsAsync(
        NpgsqlConnection connection,
        ProjectNotificationFinancialSnapshot project,
        ProjectCostRoutingRule rule,
        CancellationToken cancellationToken)
    {
        ProjectNotificationUser? escalation = null;
        if (rule.OptionalEscalationManagerUserId.HasValue)
        {
            escalation = await ProjectNotificationRepository.LoadUserAsync(
                connection,
                rule.OptionalEscalationManagerUserId.Value,
                "escalation_manager",
                "routing_rule.optional_escalation_manager_user_id",
                "cc",
                cancellationToken);
        }

        return DeriveRecipients(project, rule.RecipientRoles, escalation);
    }

    internal static ProjectNotificationUser[] DeriveRecipients(
        ProjectNotificationFinancialSnapshot project,
        IEnumerable<string> roles,
        ProjectNotificationUser? escalation)
    {
        var requested = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recipients = new List<ProjectNotificationUser>();

        if (requested.Contains("project_manager") && project.ProjectManager is not null)
            recipients.Add(project.ProjectManager with { RecipientType = "to" });

        if (requested.Contains("assigned_engineers"))
        {
            recipients.AddRange(project.Engineers.Select(engineer => new ProjectNotificationUser(
                engineer.UserId,
                engineer.DisplayName,
                engineer.Email,
                "assigned_engineer",
                "project_assignments.user_id",
                "to")));
        }

        if (requested.Contains("solution_architect") && project.SolutionArchitect is not null)
            recipients.Add(project.SolutionArchitect with { RecipientType = "cc" });

        if (requested.Contains("account_executive") && project.AccountExecutive is not null)
            recipients.Add(project.AccountExecutive with { RecipientType = "cc" });

        if (requested.Contains("project_team_coordinator") && project.ProjectTeamCoordinator is not null)
            recipients.Add(project.ProjectTeamCoordinator with { RecipientType = "cc" });

        if (requested.Contains("escalation_manager") && escalation is not null)
            recipients.Add(escalation with { RecipientType = "cc" });

        return recipients
            .Where(recipient => IsEmail(recipient.Email))
            .GroupBy(recipient => recipient.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(recipient => recipient.RecipientType == "to" ? 0 : 1)
                .First())
            .ToArray();
    }

    internal static async Task<ProjectNotificationUser[]> LoadGlobalRecipientsAsync(
        NpgsqlConnection connection,
        ProjectCostRoutingRule rule,
        CancellationToken cancellationToken)
    {
        var rows = await ProjectNotificationRepository.LoadUsersInRolesAsync(
            connection,
            ["PROJECT_TEAM_COORDINATOR"],
            "project_team_coordinator",
            cancellationToken);
        var recipients = rows.ToList();

        if (rule.OptionalEscalationManagerUserId.HasValue)
        {
            var escalation = await ProjectNotificationRepository.LoadUserAsync(
                connection,
                rule.OptionalEscalationManagerUserId.Value,
                "escalation_manager",
                "routing_rule.optional_escalation_manager_user_id",
                "cc",
                cancellationToken);
            if (escalation is not null) recipients.Add(escalation);
        }

        return recipients
            .Where(recipient => IsEmail(recipient.Email))
            .GroupBy(recipient => recipient.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    internal static string? ValidateRule(
        string metric,
        string comparison,
        decimal? threshold,
        string unit,
        string[] recipients,
        int? escalationAfterMinutes)
    {
        if (!AllowedMetricCodes.Contains(metric))
            return "Choose a supported project cost metric.";

        if (metric is "approaching_budget"
            or "over_budget"
            or "missing_financial_information"
            or "failed_project_data_refresh")
        {
            if (comparison is not ("state" or "event"))
                return "State and event rules must use a state or event comparison.";
        }
        else if (comparison is not ("gt" or "gte" or "lt" or "lte" or "eq"))
        {
            return "Numeric rules must use a numeric comparison.";
        }

        if (metric is "hours_used_percent"
            or "labor_budget_used_percent"
            or "expenses_used_percent"
            or "forecasted_total_cost"
            && !threshold.HasValue)
        {
            return "A numeric threshold is required for this rule.";
        }

        if (threshold is < 0) return "Threshold values cannot be negative.";
        if (unit == "percent" && threshold is > 10000)
            return "Percentage thresholds must be reasonable.";
        if (recipients.Length == 0)
            return "Select at least one automatically derived recipient role.";
        if (recipients.Any(role => !AllowedRecipientRoles.Contains(role)))
            return "One or more recipient roles are not supported.";
        if (escalationAfterMinutes is < 0 or > 43200)
            return "Escalation timing must be between zero and 43,200 minutes.";

        return null;
    }

    internal static string? ValidateSchedule(
        string scheduleType,
        int? dayOfWeek,
        int? daysBeforeMonthEnd,
        int? escalationAfterMinutes,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd)
    {
        if (scheduleType is not (
            "cost_alert_evaluation"
            or "weekly_reminder"
            or "monday_reminder"
            or "month_end_reminder"
            or "escalation"))
        {
            return "Choose a supported notification schedule type.";
        }

        if (scheduleType is "weekly_reminder"
            or "monday_reminder"
            or "cost_alert_evaluation"
            && (!dayOfWeek.HasValue || dayOfWeek is < 0 or > 6))
        {
            return "A valid day of week is required for this schedule.";
        }

        if (scheduleType == "month_end_reminder"
            && (!daysBeforeMonthEnd.HasValue || daysBeforeMonthEnd is < 0 or > 31))
        {
            return "Month-end schedules require zero to 31 days before month end.";
        }

        if (escalationAfterMinutes is < 0 or > 43200)
            return "Escalation timing must be between zero and 43,200 minutes.";
        if (quietHoursStart.HasValue != quietHoursEnd.HasValue)
            return "Quiet hours require both a start and an end time.";

        return null;
    }

    internal static string NormalizeMetric(string? value, string fallback)
    {
        var normalized = (value ?? fallback).Trim().ToLowerInvariant();
        return AllowedMetricCodes.Contains(normalized) ? normalized : fallback;
    }

    internal static string NormalizeComparison(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "gt" or "gte" or "lt" or "lte" or "eq" or "state" or "event" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    internal static string NormalizeUnit(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "percent" or "currency" or "state" or "event" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    internal static string NormalizeSeverity(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "informational" or "warning" or "high" or "critical" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    internal static string NormalizeBoundary(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "test_only" or "production_governed" or "locked" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    internal static string NormalizeScheduleType(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "cost_alert_evaluation"
                or "weekly_reminder"
                or "monday_reminder"
                or "month_end_reminder"
                or "escalation" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    internal static string NormalizeTimezone(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(candidate);
            return candidate;
        }
        catch
        {
            return fallback;
        }
    }

    internal static string[] NormalizeRecipientRoles(
        string[]? values,
        string[] fallback)
    {
        var normalized = (values ?? fallback)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(AllowedRecipientRoles.Contains)
            .ToArray();
        return normalized.Length == 0 ? fallback : normalized;
    }

    internal static string MoreRestrictiveBoundary(string rule, string module065)
    {
        var ranking = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["locked"] = 0,
            ["test_only"] = 1,
            ["production_governed"] = 2
        };
        var normalizedRule = NormalizeBoundary(rule, "locked");
        var normalizedModule = NormalizeBoundary(module065, "locked");
        return ranking[normalizedRule] <= ranking[normalizedModule]
            ? normalizedRule
            : normalizedModule;
    }

    internal static string BuildCostAlertSubject(
        ProjectCostRoutingRule rule,
        ProjectNotificationFinancialSnapshot project) =>
        $"Project cost {SeverityLabel(rule.AlertSeverity)}: {project.ProjectCode} — {project.ProjectName}";

    internal static string BuildCostAlertBody(
        ProjectCostRoutingRule rule,
        ProjectNotificationFinancialSnapshot project,
        ProjectNotificationMetricEvaluation evaluation) =>
        "Project cost routing rule triggered\n\n"
        + $"Rule: {rule.RuleName}\n"
        + $"Customer: {project.CustomerName}\n"
        + $"Project: {project.ProjectCode} — {project.ProjectName}\n"
        + $"Project Manager: {project.ProjectManager?.DisplayName ?? "Not assigned"}\n"
        + $"Financial status: {Label(project.BudgetStatus)}\n"
        + $"Reason: {evaluation.Reason}\n\n"
        + $"Planned hours: {project.PlannedHours:0.##}\n"
        + $"Used hours: {project.UsedHours:0.##}\n"
        + $"Remaining hours: {project.RemainingHours:0.##}\n"
        + $"Labor budget: {Money(project.LaborBudget)}\n"
        + $"Expense budget: {Money(project.ExpenseBudget)}\n"
        + $"Calculated labor cost: {Money(project.LaborCost)}\n"
        + $"Uploaded expenses: {Money(project.UploadedExpenses)}\n"
        + $"Forecasted final cost: {Money(project.ForecastedFinalCost)}\n"
        + $"Current variance: {Money(project.CurrentVariance)}\n\n"
        + "Open ProjectPulse to review the authoritative calculation and source evidence.";

    internal static string BuildCloseoutBody(ProjectNotificationFinancialSnapshot project) =>
        $"Project {project.ProjectCode} — {project.ProjectName} for {project.CustomerName} is ready for closeout communication.\n\n"
        + $"Project Manager: {project.ProjectManager?.DisplayName ?? "Not assigned"}\n"
        + $"Project status: {project.ProjectStatus}\n"
        + $"Planned hours: {project.PlannedHours:0.##}\n"
        + $"Used hours: {project.UsedHours:0.##}\n"
        + $"Remaining hours: {project.RemainingHours:0.##}\n"
        + $"Financial status: {Label(project.BudgetStatus)}\n\n"
        + "Project Manager: schedule the customer lessons-learned session and complete the governed closeout checklist.\n"
        + "This notification does not finalize accounting, send an invoice, or replace customer acceptance evidence.";

    internal static string BuildScheduledReminderBody(
        ProjectNotificationSchedule schedule,
        ProjectNotificationFinancialSnapshot project) =>
        $"Schedule: {schedule.ScheduleName}\n"
        + $"Project: {project.ProjectCode} — {project.ProjectName}\n"
        + $"Customer: {project.CustomerName}\n"
        + $"Financial status: {Label(project.BudgetStatus)}\n"
        + $"Planned hours: {project.PlannedHours:0.##}\n"
        + $"Used hours: {project.UsedHours:0.##}\n"
        + $"Remaining hours: {project.RemainingHours:0.##}\n"
        + $"Forecasted final cost: {Money(project.ForecastedFinalCost)}\n"
        + $"Current variance: {Money(project.CurrentVariance)}\n\n"
        + "Review the authoritative ProjectPulse financial workspace and resolve missing information before the next governed reminder.";

    internal static string Html(string text) =>
        "<div style=\"font-family:Arial,sans-serif;line-height:1.5\">"
        + WebUtility.HtmlEncode(text)
            .Replace("\r\n", "<br />", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal)
        + "</div>";

    internal static string Clean(string? value, int max, string fallback)
    {
        var cleaned = (value ?? string.Empty).Replace('\0', ' ').Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = fallback;
        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }

    internal static TimeOnly? ParseTimeOrExisting(string? value, TimeOnly? existing) =>
        string.IsNullOrWhiteSpace(value)
            ? existing
            : TimeOnly.TryParse(value, out var parsed) ? parsed : existing;

    internal static bool IsEmail(string value)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(value)
                && new MailAddress(value).Address.Equals(
                    value.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool Compare(
        decimal? observed,
        decimal? threshold,
        string comparison)
    {
        if (!observed.HasValue || !threshold.HasValue) return false;
        return comparison switch
        {
            "gt" => observed.Value > threshold.Value,
            "gte" => observed.Value >= threshold.Value,
            "lt" => observed.Value < threshold.Value,
            "lte" => observed.Value <= threshold.Value,
            "eq" => observed.Value == threshold.Value,
            _ => false
        };
    }

    private static decimal? SumKnown(params decimal?[] values)
    {
        var known = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static string SeverityLabel(string severity) => severity switch
    {
        "critical" => "critical alert",
        "high" => "high-priority alert",
        "warning" => "warning",
        _ => "notice"
    };

    private static string Money(decimal? value) => value.HasValue
        ? value.Value.ToString("C2")
        : "Not recorded";

    private static string Label(string value) => value.Replace('_', ' ');
}

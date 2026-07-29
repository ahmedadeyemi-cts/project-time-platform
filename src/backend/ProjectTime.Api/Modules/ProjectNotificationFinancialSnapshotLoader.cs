using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Group 4 consumes the same authoritative project, assignment, time-entry,
/// Module 005 expense, and governed rate-card sources used by Group 3. Optional
/// source failures are returned independently so one unavailable source does not
/// erase the otherwise usable project and recipient context.
/// </summary>
internal static class ProjectNotificationFinancialSnapshotLoader
{
    internal static async Task<ProjectNotificationSnapshotResult> LoadAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var sources = new List<ProjectNotificationSourceState>();
        var projects = await LoadProjectsAsync(connection, cancellationToken);
        sources.Add(ProjectNotificationSourceState.Healthy(
            "projects",
            "Authoritative projects and accountable owners",
            true,
            projects.Count));

        var assignments = await OptionalAsync(
            "assignments",
            "Project assignments, engineers, and planned hours",
            true,
            () => LoadAssignmentsAsync(connection, cancellationToken));
        sources.Add(assignments.State);

        var expenses = await OptionalAsync(
            "module_005_expenses",
            "Current non-deleted Module 005 project expenses",
            false,
            () => LoadExpensesAsync(connection, cancellationToken));
        sources.Add(expenses.State);

        var rates = await OptionalAsync(
            "governed_rates",
            "Module 026 governed project rate context",
            false,
            () => LoadRatesAsync(connection, cancellationToken));
        sources.Add(rates.State);

        var assignmentByProject = assignments.Value
            .GroupBy(item => item.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var expensesByProject = expenses.Value
            .ToDictionary(item => item.ProjectId, item => item);
        var rateByProject = rates.Value
            .ToDictionary(item => item.ProjectId, item => item.AverageRate);

        var snapshots = projects.Select(project =>
        {
            var projectAssignments = assignmentByProject.TryGetValue(project.ProjectId, out var rows)
                ? rows
                : Array.Empty<AssignmentRow>();
            var plannedHours = projectAssignments.Sum(row => row.AssignedHours);
            var usedHours = projectAssignments.Sum(row => row.UsedHours);
            var remainingHours = Math.Max(plannedHours - usedHours, 0m);
            var completionPercentage = plannedHours > 0
                ? Math.Min(100m, Math.Round(usedHours / plannedHours * 100m, 2))
                : (decimal?)null;

            var laborBudget = FirstKnown(
                JsonDecimal(project.Json, "laborBudget", "labor_budget", "plannedLaborBudget", "planned_labor_budget"),
                JsonDecimal(project.Json, "plannedEngineeringCost", "planned_engineering_cost")
                    + JsonDecimal(project.Json, "plannedPmCost", "planned_pm_cost"));
            var expenseBudget = JsonDecimal(
                project.Json,
                "expenseBudget",
                "expense_budget",
                "plannedExpenseBudget",
                "planned_expense_budget");
            var contractedValue = JsonDecimal(
                project.Json,
                "contractedValue",
                "contracted_value",
                "contractValue",
                "contract_value",
                "sowValue",
                "sow_value",
                "sellAmount",
                "sell_amount");
            var knownTotalBudget = SumKnown(laborBudget, expenseBudget)
                ?? JsonDecimal(
                    project.Json,
                    "plannedTotalProjectCost",
                    "planned_total_project_cost",
                    "projectBudget",
                    "project_budget");

            var uploadedExpenses = expensesByProject.TryGetValue(project.ProjectId, out var expense)
                ? expense.TotalAmount
                : expenses.State.Status == "healthy" ? 0m : (decimal?)null;
            var effectiveRate = rateByProject.TryGetValue(project.ProjectId, out var rate)
                ? rate
                : laborBudget.HasValue && plannedHours > 0
                    ? laborBudget.Value / plannedHours
                    : (decimal?)null;
            var laborCost = effectiveRate.HasValue
                ? Math.Round(usedHours * effectiveRate.Value, 2)
                : (decimal?)null;
            var committedCost = SumKnown(laborCost, uploadedExpenses);
            var forecastedFinalCost = effectiveRate.HasValue
                ? SumKnown(Math.Round((usedHours + remainingHours) * effectiveRate.Value, 2), uploadedExpenses)
                : committedCost;
            var currentVariance = knownTotalBudget.HasValue && forecastedFinalCost.HasValue
                ? Math.Round(knownTotalBudget.Value - forecastedFinalCost.Value, 2)
                : (decimal?)null;

            var missing = new List<string>();
            if (plannedHours <= 0) missing.Add("planned_hours");
            if (!laborBudget.HasValue) missing.Add("labor_budget");
            if (!expenseBudget.HasValue) missing.Add("expense_budget");
            if (!contractedValue.HasValue) missing.Add("contracted_value");
            if (!effectiveRate.HasValue) missing.Add("governed_rate");
            if (!uploadedExpenses.HasValue) missing.Add("module_005_expenses");
            if (project.ProjectManager is null || string.IsNullOrWhiteSpace(project.ProjectManager.Email))
                missing.Add("project_manager_email");
            if (project.ProjectTeamCoordinator is null || string.IsNullOrWhiteSpace(project.ProjectTeamCoordinator.Email))
                missing.Add("project_team_coordinator_email");

            var budgetStatus = DetermineBudgetStatus(
                knownTotalBudget,
                forecastedFinalCost,
                missing.Count);

            var engineers = projectAssignments
                .GroupBy(row => new { row.UserId, row.DisplayName, row.Email })
                .Select(group => new ProjectNotificationEngineer(
                    group.Key.UserId,
                    group.Key.DisplayName,
                    group.Key.Email,
                    group.Sum(row => row.AssignedHours),
                    group.Sum(row => row.UsedHours)))
                .OrderBy(engineer => engineer.DisplayName)
                .ToArray();

            return new ProjectNotificationFinancialSnapshot(
                project.ProjectId,
                project.CustomerName,
                project.ProjectCode,
                project.ProjectName,
                project.ProjectStatus,
                project.ContractType,
                project.ProjectManager,
                project.ProjectTeamCoordinator,
                project.SolutionArchitect,
                project.AccountExecutive,
                engineers,
                contractedValue,
                laborBudget,
                expenseBudget,
                plannedHours,
                usedHours,
                remainingHours,
                laborCost,
                uploadedExpenses,
                committedCost,
                forecastedFinalCost,
                currentVariance,
                completionPercentage,
                budgetStatus,
                missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                DateTimeOffset.UtcNow);
        })
        .OrderBy(snapshot => BudgetOrder(snapshot.BudgetStatus))
        .ThenBy(snapshot => snapshot.CustomerName)
        .ThenBy(snapshot => snapshot.ProjectCode)
        .ToArray();

        return new(snapshots, sources.ToArray(), DateTimeOffset.UtcNow);
    }

    private static async Task<List<ProjectRow>> LoadProjectsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project.project_id,
                COALESCE(client.client_name, 'Customer not linked') AS customer_name,
                COALESCE(project.project_code, '') AS project_code,
                COALESCE(project.project_name, '') AS project_name,
                COALESCE(project.status, 'unknown') AS project_status,
                COALESCE(project.contract_type, '') AS contract_type,
                project.project_manager_user_id,
                COALESCE(project_manager.display_name, project_manager.email, '') AS project_manager_name,
                COALESCE(project_manager.email, '') AS project_manager_email,
                project.project_coordinator_user_id,
                COALESCE(coordinator.display_name, coordinator.email, '') AS coordinator_name,
                COALESCE(coordinator.email, '') AS coordinator_email,
                project.solution_architect_user_id,
                COALESCE(solution_architect.display_name, solution_architect.email, '') AS solution_architect_name,
                COALESCE(solution_architect.email, '') AS solution_architect_email,
                project.account_executive_user_id,
                COALESCE(account_executive.display_name, account_executive.email, '') AS account_executive_name,
                COALESCE(account_executive.email, '') AS account_executive_email,
                to_jsonb(project)::text AS project_json
            FROM projects project
            LEFT JOIN clients client ON client.client_id = project.client_id
            LEFT JOIN app_users project_manager ON project_manager.user_id = project.project_manager_user_id
            LEFT JOIN app_users coordinator ON coordinator.user_id = project.project_coordinator_user_id
            LEFT JOIN app_users solution_architect ON solution_architect.user_id = project.solution_architect_user_id
            LEFT JOIN app_users account_executive ON account_executive.user_id = project.account_executive_user_id
            WHERE lower(COALESCE(project.status, '')) NOT IN ('cancelled', 'deleted', 'archived')
            ORDER BY client.client_name, project.project_code;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var json = JsonDocument.Parse(reader.GetString(18)).RootElement.Clone();
            rows.Add(new ProjectRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                NotificationUser(reader, 6, 7, 8, "project_manager", "projects.project_manager_user_id"),
                NotificationUser(reader, 9, 10, 11, "project_team_coordinator", "projects.project_coordinator_user_id"),
                NotificationUser(reader, 12, 13, 14, "solution_architect", "projects.solution_architect_user_id"),
                NotificationUser(reader, 15, 16, 17, "account_executive", "projects.account_executive_user_id"),
                json));
        }
        return rows;
    }

    private static ProjectNotificationUser? NotificationUser(
        NpgsqlDataReader reader,
        int idOrdinal,
        int nameOrdinal,
        int emailOrdinal,
        string role,
        string source)
    {
        var userId = reader.IsDBNull(idOrdinal) ? (Guid?)null : reader.GetGuid(idOrdinal);
        var name = reader.GetString(nameOrdinal);
        var email = reader.GetString(emailOrdinal);
        return userId.HasValue || !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(email)
            ? new(userId, name, email, role, source)
            : null;
    }

    private static async Task<List<AssignmentRow>> LoadAssignmentsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<AssignmentRow>();
        await using var command = new NpgsqlCommand("""
            WITH used_time AS (
                SELECT
                    entry.project_id,
                    entry.user_id,
                    SUM(entry.hours)::numeric AS used_hours
                FROM time_entries entry
                WHERE entry.project_id IS NOT NULL
                  AND lower(COALESCE(entry.status, '')) NOT IN ('voided', 'rejected', 'declined')
                GROUP BY entry.project_id, entry.user_id
            )
            SELECT
                assignment.project_id,
                assignment.user_id,
                COALESCE(app_user.display_name, app_user.email, '') AS display_name,
                COALESCE(app_user.email, '') AS email,
                COALESCE(SUM(assignment.assigned_hours), 0)::numeric AS assigned_hours,
                COALESCE(MAX(used_time.used_hours), 0)::numeric AS used_hours
            FROM project_assignments assignment
            JOIN app_users app_user ON app_user.user_id = assignment.user_id
            LEFT JOIN used_time
              ON used_time.project_id = assignment.project_id
             AND used_time.user_id = assignment.user_id
            WHERE assignment.effective_start_date <= CURRENT_DATE
              AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
            GROUP BY assignment.project_id, assignment.user_id, app_user.display_name, app_user.email
            ORDER BY assignment.project_id, display_name;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5)));
        }
        return rows;
    }

    private static async Task<List<ExpenseRow>> LoadExpensesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<ExpenseRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                upload.project_id,
                SUM(upload.total_amount)::numeric AS total_amount,
                COUNT(*)::integer AS upload_count
            FROM project_expense_uploads upload
            WHERE upload.is_current = TRUE
              AND upload.deleted_at IS NULL
            GROUP BY upload.project_id;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetDecimal(1), reader.GetInt32(2)));
        return rows;
    }

    private static async Task<List<RateRow>> LoadRatesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<RateRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project.project_id,
                AVG(rate_line.rate_amount)::numeric AS average_rate
            FROM projects project
            JOIN project_billing_profiles profile ON profile.project_id = project.project_id
            JOIN work_rate_cards rate_card ON rate_card.rate_card_id = profile.default_rate_card_id
            JOIN work_rate_card_lines rate_line ON rate_line.rate_card_id = rate_card.rate_card_id
            WHERE rate_line.is_active = TRUE
              AND rate_line.billable_default = TRUE
              AND lower(COALESCE(rate_card.status, '')) IN ('active', 'published', 'approved')
            GROUP BY project.project_id;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetDecimal(1)));
        return rows;
    }

    private static async Task<OptionalResult<T>> OptionalAsync<T>(
        string key,
        string name,
        bool required,
        Func<Task<T>> loader)
    {
        try
        {
            var value = await loader();
            return new(
                value,
                ProjectNotificationSourceState.Healthy(key, name, required, Count(value)));
        }
        catch (Exception exception)
        {
            return new(
                Empty<T>(),
                ProjectNotificationSourceState.Unavailable(
                    key,
                    name,
                    required,
                    Diagnostic(exception),
                    required
                        ? "A required project financial source is unavailable. Retry after the source is restored."
                        : "This optional project financial source is unavailable; other project data remains usable."));
        }
    }

    private static string DetermineBudgetStatus(
        decimal? totalBudget,
        decimal? forecast,
        int missingCount)
    {
        if (!totalBudget.HasValue || !forecast.HasValue)
            return missingCount > 0 ? "missing_financial_information" : "not_recorded";
        if (forecast.Value > totalBudget.Value) return "over_budget";
        if (totalBudget.Value > 0 && forecast.Value / totalBudget.Value >= 0.8m)
            return "approaching_budget";
        return "on_track";
    }

    private static int BudgetOrder(string status) => status switch
    {
        "over_budget" => 0,
        "approaching_budget" => 1,
        "missing_financial_information" => 2,
        _ => 3
    };

    private static decimal? JsonDecimal(JsonElement element, params string[] keys)
    {
        var expected = keys.Select(JsonKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return JsonDecimalCore(element, expected, 0);
    }

    private static decimal? JsonDecimalCore(
        JsonElement element,
        HashSet<string> expected,
        int depth)
    {
        if (depth > 5 || element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(JsonKey(property.Name))) continue;
            if (property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDecimal(out var number)) return number;
            if (property.Value.ValueKind == JsonValueKind.String
                && decimal.TryParse(property.Value.GetString(), out var parsed)) return parsed;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            var nested = JsonDecimalCore(property.Value, expected, depth + 1);
            if (nested.HasValue) return nested;
        }
        return null;
    }

    private static string JsonKey(string value) => new(
        value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static decimal? FirstKnown(params decimal?[] values) =>
        values.FirstOrDefault(value => value.HasValue);

    private static decimal? SumKnown(params decimal?[] values)
    {
        var known = values.Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static int Count<T>(T value) => value switch
    {
        System.Collections.ICollection collection => collection.Count,
        _ => 0
    };

    private static T Empty<T>()
    {
        var type = typeof(T);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return (T)Activator.CreateInstance(type)!;
        if (type.IsArray) return (T)(object)Array.CreateInstance(type.GetElementType()!, 0);
        return default!;
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"POSTGRES_{postgres.SqlState}",
        NpgsqlException => "POSTGRES_CONNECTION_UNAVAILABLE",
        _ => exception.GetType().Name
    };

    private sealed record ProjectRow(
        Guid ProjectId,
        string CustomerName,
        string ProjectCode,
        string ProjectName,
        string ProjectStatus,
        string ContractType,
        ProjectNotificationUser? ProjectManager,
        ProjectNotificationUser? ProjectTeamCoordinator,
        ProjectNotificationUser? SolutionArchitect,
        ProjectNotificationUser? AccountExecutive,
        JsonElement Json);

    private sealed record AssignmentRow(
        Guid ProjectId,
        Guid UserId,
        string DisplayName,
        string Email,
        decimal AssignedHours,
        decimal UsedHours);

    private sealed record ExpenseRow(Guid ProjectId, decimal TotalAmount, int UploadCount);
    private sealed record RateRow(Guid ProjectId, decimal AverageRate);
    private sealed record OptionalResult<T>(T Value, ProjectNotificationSourceState State);
}

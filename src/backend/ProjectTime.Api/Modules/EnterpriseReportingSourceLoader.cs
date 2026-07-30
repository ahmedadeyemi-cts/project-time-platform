using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseReportingSourceLoader
{
    private sealed record SourceSpec(
        string Key,
        string Name,
        string Table,
        bool OrganizationOnly = false,
        string? AdditionalPredicate = null);

    private static readonly IReadOnlyDictionary<string, SourceSpec> Specs =
        new Dictionary<string, SourceSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["time_entries"] = new("time_entries", "Time entries", "time_entries"),
            ["approved_time_entries"] = new(
                "approved_time_entries", "Approved time entries", "time_entries", false,
                "lower(COALESCE(status, '')) IN ('pm_approved','manager_approved','project_approved','project_validated','accounting_ready','reconciled','locked')"),
            ["project_expenses"] = new(
                "project_expenses", "Current Module 005 project expenses", "project_expense_uploads", false,
                "COALESCE(is_current, TRUE) = TRUE AND deleted_at IS NULL"),
            ["billing_readiness_reviews"] = new("billing_readiness_reviews", "Billing-readiness reviews", "work_billing_readiness_reviews"),
            ["project_closeout_records"] = new("project_closeout_records", "Project closeout records", "work_closeout_records"),
            ["project_notification_dispatches"] = new("project_notification_dispatches", "Group 4 notification dispatches", "project_notification_dispatches"),
            ["resource_qualifications"] = new("resource_qualifications", "Qualifications and certifications", "resource_qualifications"),
            ["qualification_renewals"] = new("qualification_renewals", "Qualification renewal acknowledgements", "qualification_renewal_records"),
            ["utilization_targets"] = new("utilization_targets", "Utilization targets", "utilization_targets"),
            ["oncall_schedule"] = new("oncall_schedule", "Governed on-call schedule", "governed_oncall_assignments"),
            ["oncall_roster"] = new("oncall_roster", "Governed on-call roster", "governed_oncall_roster"),
            ["oncall_imports"] = new("oncall_imports", "Operational-directory import evidence", "governed_directory_import_batches"),
            ["module076_items"] = new("module076_items", "Module 076 issues and feature requests", "module076_items"),
            ["module076_transitions"] = new("module076_transitions", "Module 076 immutable transitions", "module076_transitions"),
            ["integration_gateway_events"] = new("integration_gateway_events", "Module 075 event gateway evidence", "integration_gateway_events"),
            ["operational_control_history"] = new("operational_control_history", "Operational-control history", "operational_control_history", true),
            ["release_controls"] = new("release_controls", "Release and deployment control configuration", "operational_control_configurations", true),
            ["deployment_evidence"] = new("deployment_evidence", "Release and deployment evidence", "operational_control_evidence", true),
            ["platform_health"] = new("platform_health", "Provider-neutral platform health", "operational_control_observations", true),
            ["service_inventory"] = new("service_inventory", "Service inventory", "operational_service_inventory", true),
            ["slo_definitions"] = new("slo_definitions", "SLI and SLO definitions", "operational_slo_definitions", true),
            ["alert_history"] = new("alert_history", "Operational alert history", "operational_alert_history", true),
            ["data_governance_domains"] = new("data_governance_domains", "Data-governance domains", "data_governance_domains", true),
            ["retention_policies"] = new("retention_policies", "Retention policies", "data_retention_policies", true),
            ["legal_holds"] = new("legal_holds", "Legal holds", "data_legal_holds", true),
            ["purge_jobs"] = new("purge_jobs", "Governed purge jobs", "data_purge_jobs", true),
            ["customer_acceptance_engagements"] = new("customer_acceptance_engagements", "Customer acceptance engagements", "customer_acceptance_engagements"),
            ["acceptance_templates"] = new("acceptance_templates", "Acceptance templates", "customer_acceptance_templates"),
            ["acceptance_evidence"] = new("acceptance_evidence", "Acceptance evidence", "customer_acceptance_evidence"),
            ["acceptance_decisions"] = new("acceptance_decisions", "Immutable acceptance decisions", "customer_acceptance_decisions"),
            ["secure_project_information_requests"] = new("secure_project_information_requests", "Secure project-information requests", "secure_project_information_requests"),
            ["secure_project_information_audit"] = new("secure_project_information_audit", "Secure project-information audit", "secure_project_information_audit"),
            ["pmo_projects"] = new("pmo_projects", "Enterprise PMO project records", "pmo_project_records"),
            ["pmo_controls"] = new("pmo_controls", "Enterprise PMO control register", "pmo_control_items"),
            ["project_flowhive_plans"] = new("project_flowhive_plans", "Persistent Project FlowHive plans", "project_flowhive_plans")
        };

    internal static async Task<EnterpriseReportingSupplemental> LoadAsync(
        EnterpriseReportingContext seed,
        EnterpriseReportDefinition definition,
        CancellationToken cancellationToken)
    {
        var keys = definition.RequiredSources
            .Concat(definition.OptionalSources)
            .Where(key => Specs.ContainsKey(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keys.Length == 0)
            return new EnterpriseReportingSupplemental(
                new Dictionary<string, JsonElement[]>(),
                Array.Empty<EnterpriseReportSourceState>());

        var connectionString = ProjectFinancialTruthModule.FinancialOperationsConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return UnavailableAll(keys, definition, "DATABASE_CONFIGURATION_UNAVAILABLE");

        await using var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            return UnavailableAll(keys, definition, Diagnostic(exception));
        }

        var data = new Dictionary<string, JsonElement[]>(StringComparer.OrdinalIgnoreCase);
        var states = new List<EnterpriseReportSourceState>();
        foreach (var key in keys)
        {
            var spec = Specs[key];
            var required = definition.RequiredSources.Contains(key, StringComparer.OrdinalIgnoreCase);
            if (spec.OrganizationOnly && !seed.Actor.Broad
                && !seed.Actor.HasPermission("MANAGE_ALL", "SYSTEM_ADMINISTRATION", "VIEW_OPERATIONAL_CONTROL_REPORTS"))
            {
                data[key] = Array.Empty<JsonElement>();
                states.Add(new EnterpriseReportSourceState(
                    key, spec.Name, "restricted", required, 0,
                    "This organization-control source is outside the current role scope.",
                    "SOURCE_SCOPE_RESTRICTED", DateTimeOffset.UtcNow));
                continue;
            }

            try
            {
                var exists = await TableExistsAsync(connection, spec.Table, cancellationToken);
                if (!exists)
                {
                    data[key] = Array.Empty<JsonElement>();
                    states.Add(Unavailable(spec, required, "SOURCE_TABLE_NOT_AVAILABLE"));
                    continue;
                }

                var columns = await LoadColumnsAsync(connection, spec.Table, cancellationToken);
                var rows = await LoadRowsAsync(connection, spec, columns, seed, cancellationToken);
                data[key] = rows;
                states.Add(new EnterpriseReportSourceState(
                    key, spec.Name, "healthy", required, rows.Length,
                    rows.Length == 0
                        ? "The source is available and contains no rows in the current role scope."
                        : "The source loaded successfully in the current role scope.",
                    "", DateTimeOffset.UtcNow));
            }
            catch (Exception exception)
            {
                data[key] = Array.Empty<JsonElement>();
                states.Add(Unavailable(spec, required, Diagnostic(exception)));
            }
        }

        return new EnterpriseReportingSupplemental(data, states.ToArray());
    }

    private static EnterpriseReportingSupplemental UnavailableAll(
        string[] keys,
        EnterpriseReportDefinition definition,
        string diagnostic)
    {
        var data = keys.ToDictionary(
            key => key,
            _ => Array.Empty<JsonElement>(),
            StringComparer.OrdinalIgnoreCase);
        var states = keys.Select(key =>
        {
            var spec = Specs[key];
            return Unavailable(
                spec,
                definition.RequiredSources.Contains(key, StringComparer.OrdinalIgnoreCase),
                diagnostic);
        }).ToArray();
        return new EnterpriseReportingSupplemental(data, states);
    }

    private static EnterpriseReportSourceState Unavailable(
        SourceSpec spec,
        bool required,
        string diagnostic) => new(
            spec.Key,
            spec.Name,
            "unavailable",
            required,
            0,
            "This report source is unavailable. Results from other healthy sources remain visible.",
            diagnostic,
            DateTimeOffset.UtcNow);

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass(@qualified_name) IS NOT NULL;", connection);
        command.Parameters.AddWithValue("qualified_name", $"public.{table}");
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<HashSet<string>> LoadColumnsAsync(
        NpgsqlConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table_name;
            """, connection);
        command.Parameters.AddWithValue("table_name", table);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task<JsonElement[]> LoadRowsAsync(
        NpgsqlConnection connection,
        SourceSpec spec,
        HashSet<string> columns,
        EnterpriseReportingContext context,
        CancellationToken cancellationToken)
    {
        var predicates = new List<string>();
        var projectColumn = First(columns, "project_id", "affected_project_id");
        var visibleProjectIds = context.Projects.Select(project => project.ProjectId).Distinct().ToArray();
        if (projectColumn is not null)
        {
            predicates.Add($"source.{Quote(projectColumn)} = ANY(@project_ids)");
        }
        else if (!context.Actor.Broad && IsProjectBound(spec.Key))
        {
            return Array.Empty<JsonElement>();
        }

        var userColumns = new[]
        {
            "user_id", "engineer_user_id", "resource_user_id", "owner_user_id",
            "expense_owner_user_id", "reported_by_user_id", "raised_by_user_id",
            "assignee_user_id", "created_by_user_id"
        }.Where(columns.Contains).ToArray();
        var allowedUserIds = AllowedUserIds(context);
        if (!context.Actor.Broad && userColumns.Length > 0)
        {
            predicates.Add("(" + string.Join(" OR ", userColumns.Select(column =>
                $"source.{Quote(column)} = ANY(@user_ids)")) + ")");
        }

        if (!string.IsNullOrWhiteSpace(spec.AdditionalPredicate))
            predicates.Add(spec.AdditionalPredicate);

        var where = predicates.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", predicates);
        var sql = $"SELECT row_to_json(source)::text FROM {Quote(spec.Table)} source{where} LIMIT 5000;";
        await using var command = new NpgsqlCommand(sql, connection);
        if (projectColumn is not null)
        {
            command.Parameters.Add(new NpgsqlParameter(
                "project_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = visibleProjectIds.Length == 0 ? Array.Empty<Guid>() : visibleProjectIds
            });
        }
        if (!context.Actor.Broad && userColumns.Length > 0)
        {
            command.Parameters.Add(new NpgsqlParameter(
                "user_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = allowedUserIds
            });
        }

        var rows = new List<JsonElement>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var document = JsonDocument.Parse(reader.GetString(0));
            rows.Add(document.RootElement.Clone());
        }
        return rows.ToArray();
    }

    private static Guid[] AllowedUserIds(EnterpriseReportingContext context)
    {
        var roles = context.Actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engineerOnly = roles.Overlaps(["ENGINEER", "ENGINEERING"])
            && !context.Actor.PmLead
            && !roles.Overlaps(["MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD"]);
        if (engineerOnly) return [context.Actor.EffectiveUserId];

        return context.Projects
            .SelectMany(project => project.Engineers.Select(engineer => engineer.UserId)
                .Concat(project.ProjectManagerUserId.HasValue ? [project.ProjectManagerUserId.Value] : Array.Empty<Guid>())
                .Concat(project.ProjectTeamCoordinator is null ? Array.Empty<Guid>() : [project.ProjectTeamCoordinator.UserId])
                .Concat(project.SolutionArchitect is null ? Array.Empty<Guid>() : [project.SolutionArchitect.UserId])
                .Concat(project.AccountExecutive is null ? Array.Empty<Guid>() : [project.AccountExecutive.UserId]))
            .Append(context.Actor.EffectiveUserId)
            .Distinct()
            .ToArray();
    }

    private static bool IsProjectBound(string key) => key is
        "project_expenses" or "billing_readiness_reviews" or "project_closeout_records"
        or "project_notification_dispatches" or "customer_acceptance_engagements"
        or "acceptance_evidence" or "acceptance_decisions"
        or "secure_project_information_requests" or "secure_project_information_audit"
        or "pmo_projects" or "pmo_controls" or "project_flowhive_plans";

    private static string? First(HashSet<string> columns, params string[] candidates) =>
        candidates.FirstOrDefault(columns.Contains);

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";

    internal static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres when postgres.SqlState == "42P01" => "SOURCE_TABLE_NOT_AVAILABLE",
        PostgresException postgres when postgres.SqlState == "42703" => "SOURCE_COLUMN_NOT_AVAILABLE",
        PostgresException postgres => $"POSTGRES_{postgres.SqlState}",
        TimeoutException => "SOURCE_TIMEOUT",
        OperationCanceledException => "SOURCE_CANCELLED",
        _ => exception.GetType().Name
    };
}

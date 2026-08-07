using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Group 3 read-only project financial truth for Modules 018, 019, 036, and
/// 055B. SELL context is read through Module 026's governed commercial model;
/// this module never stores or reads a second provider credential.
/// </summary>
public static class ProjectFinancialTruthModule
{
    private const string ContractVersion = "2026-07-28.1";
    private static readonly string[] BroadRoles =
    [
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "PROJECT_TEAM_COORDINATOR",
        "ACCOUNTING", "ACCOUNTING_BILLING", "BILLING", "FINANCE", "EXECUTIVE"
    ];
    private static readonly string[] PmLeadRoles =
    [
        "PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD"
    ];
    private static readonly string[] SalesRoles =
    [
        "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE",
        "SOLUTION_ARCHITECT", "SALES_ENGINEERING"
    ];

    public static IEndpointRouteBuilder MapProjectFinancialTruthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/project-financials/portfolio",
            (Func<HttpContext, Task<IResult>>)GetPortfolioAsync);
        endpoints.MapGet("/api/project-financials/projects/{projectId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)GetProjectAsync);
        endpoints.MapGet("/api/project-financials/sources",
            (Func<HttpContext, Task<IResult>>)GetSourcesAsync);
        endpoints.MapGet("/api/project-financials/reporting-summary",
            (Func<HttpContext, Task<IResult>>)GetReportingSummaryAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPortfolioAsync(HttpContext context)
    {
        var workspace = Workspace(context.Request.Query["workspace"]);
        var result = await BuildAsync(context, workspace);
        if (result.Failure is not null) return result.Failure;

        var data = result.Data!;
        var search = Clean(context.Request.Query["search"], 160);
        var status = Clean(context.Request.Query["status"], 80).ToLowerInvariant();
        var limit = Math.Clamp(
            int.TryParse(context.Request.Query["limit"], out var requested) ? requested : 100,
            1,
            250);

        var projects = data.Projects
            .Where(project => string.IsNullOrWhiteSpace(search)
                || SearchText(project).Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(project => string.IsNullOrWhiteSpace(status)
                || status == "all"
                || project.BudgetStatus.Equals(status, StringComparison.OrdinalIgnoreCase)
                || project.ProjectStatus.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToArray();

        return Results.Ok(new
        {
            module = "GROUP_3",
            modules = new[] { "018", "019", "036", "055B" },
            status = "authoritative_project_financial_portfolio_loaded",
            contractVersion = ContractVersion,
            generatedAt = data.GeneratedAt,
            workspace,
            access = Access(data.Actor, workspace),
            filters = new { search, status, limit },
            summary = Summary(projects),
            projects,
            sources = data.Sources,
            calculations = CalculationContract(),
            governedDependencies = Dependencies(),
            security = Security()
        });
    }

    private static async Task<IResult> GetProjectAsync(Guid projectId, HttpContext context)
    {
        var workspace = Workspace(context.Request.Query["workspace"]);
        var result = await BuildAsync(context, workspace);
        if (result.Failure is not null) return result.Failure;

        var project = result.Data!.Projects.FirstOrDefault(row => row.ProjectId == projectId);
        if (project is null)
        {
            return Results.NotFound(new
            {
                module = "GROUP_3",
                status = "project_not_found_or_outside_scope",
                message = "The requested project was not found in the current workspace scope."
            });
        }

        return Results.Ok(new
        {
            module = "GROUP_3",
            status = "authoritative_project_financial_detail_loaded",
            contractVersion = ContractVersion,
            generatedAt = result.Data.GeneratedAt,
            workspace,
            access = Access(result.Data.Actor, workspace),
            project,
            sources = result.Data.Sources,
            calculations = CalculationContract(),
            governedDependencies = Dependencies(),
            security = Security()
        });
    }

    private static async Task<IResult> GetSourcesAsync(HttpContext context)
    {
        var workspace = Workspace(context.Request.Query["workspace"]);
        var result = await BuildAsync(context, workspace);
        if (result.Failure is not null) return result.Failure;
        return Results.Ok(new
        {
            module = "GROUP_3",
            status = "project_financial_sources_loaded",
            contractVersion = ContractVersion,
            generatedAt = result.Data!.GeneratedAt,
            workspace,
            sources = result.Data.Sources,
            retry = new
            {
                portfolio = $"/api/project-financials/portfolio?workspace={workspace}",
                sources = $"/api/project-financials/sources?workspace={workspace}"
            },
            security = Security()
        });
    }

    private static async Task<IResult> GetReportingSummaryAsync(HttpContext context)
    {
        var workspace = Workspace(context.Request.Query["workspace"]);
        var result = await BuildAsync(context, workspace);
        if (result.Failure is not null) return result.Failure;
        var projects = result.Data!.Projects;

        return Results.Ok(new
        {
            module = "GROUP_3",
            status = "project_financial_reporting_summary_loaded",
            contractVersion = ContractVersion,
            generatedAt = result.Data.GeneratedAt,
            workspace,
            access = Access(result.Data.Actor, workspace),
            portfolio = Summary(projects),
            customers = projects
                .GroupBy(project => new { project.ClientId, project.CustomerName })
                .Select(group => new
                {
                    group.Key.ClientId,
                    group.Key.CustomerName,
                    projectCount = group.Count(),
                    plannedHours = group.Sum(project => project.PlannedHours),
                    usedHours = group.Sum(project => project.UsedHours),
                    uploadedExpenses = SumKnown(group.Select(project => project.UploadedExpenses)),
                    forecastedFinalCost = SumKnown(group.Select(project => project.ForecastedFinalCost)),
                    currentVariance = SumKnown(group.Select(project => project.CurrentVariance)),
                    overBudgetCount = group.Count(project => project.BudgetStatus == "over_budget"),
                    approachingBudgetCount = group.Count(project => project.BudgetStatus == "approaching_budget")
                })
                .OrderByDescending(customer => customer.overBudgetCount)
                .ThenByDescending(customer => customer.approachingBudgetCount)
                .ThenBy(customer => customer.CustomerName),
            budgetStatus = projects.GroupBy(project => project.BudgetStatus)
                .Select(group => new { status = group.Key, count = group.Count() }),
            sellReadiness = projects.GroupBy(project => project.Sell.ReadinessStatus)
                .Select(group => new { status = group.Key, count = group.Count() }),
            sources = result.Data.Sources,
            calculations = CalculationContract(),
            security = Security()
        });
    }

    private static async Task<BuildOutcome> BuildAsync(HttpContext context, string workspace)
    {
        var actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effectiveUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId")
            ?? actualUserId;

        if (!actualUserId.HasValue || !effectiveUserId.HasValue)
        {
            return BuildOutcome.Fail(Results.Json(new
            {
                module = "GROUP_3",
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return BuildOutcome.Fail(Results.Json(new
            {
                module = "GROUP_3",
                status = "financial_data_configuration_unavailable",
                message = "Project financial data is temporarily unavailable because the database connection is not configured."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(context.RequestAborted);
        }
        catch (Exception exception)
        {
            return BuildOutcome.Fail(Results.Json(new
            {
                module = "GROUP_3",
                status = "financial_data_source_unavailable",
                source = "projectpulse_database",
                diagnosticCode = Diagnostic(exception),
                message = "Project financial data could not be loaded. Retry after the database connection is restored."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var actor = await LoadActorAsync(
            connection,
            actualUserId.Value,
            effectiveUserId.Value,
            ProjectPulseActualSessionAuthority.IsViewAs(context),
            context.RequestAborted);
        if (actor is null)
        {
            return BuildOutcome.Fail(Results.Json(new
            {
                module = "GROUP_3",
                status = "financial_workspace_access_unavailable",
                message = "The current user could not be resolved for project financial access."
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        List<ProjectSeed> projectSeeds;
        try
        {
            projectSeeds = await LoadProjectsAsync(connection, context.RequestAborted);
        }
        catch (Exception exception)
        {
            return BuildOutcome.Fail(Results.Json(new
            {
                module = "GROUP_3",
                status = "required_project_source_unavailable",
                source = "projects",
                diagnosticCode = Diagnostic(exception),
                message = "The authoritative project source is unavailable. No financial values were fabricated."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var sources = new List<SourceState>
        {
            SourceState.Healthy("projects", "Projects and customer ownership", true, projectSeeds.Count)
        };

        var assignments = await TryLoadAsync(
            "assignments", "Project assignments and allocated hours", true,
            () => LoadAssignmentsAsync(connection, context.RequestAborted));
        sources.Add(assignments.State);

        var permittedPmIds = await TryLoadAsync(
            "pm_scope", "Project Manager selection scope", false,
            () => LoadPmScopeAsync(connection, actor, projectSeeds, context.RequestAborted));
        sources.Add(permittedPmIds.State);

        var requestedPmId = Guid.TryParse(
            context.Request.Query["projectManagerUserId"],
            out var parsedPmId)
            ? parsedPmId
            : (Guid?)null;

        var visibleSeeds = FilterProjects(
            projectSeeds,
            assignments.Value,
            actor,
            workspace,
            requestedPmId,
            permittedPmIds.Value).ToArray();
        var visibleIds = visibleSeeds.Select(project => project.ProjectId).ToHashSet();
        var visibleAssignments = assignments.Value
            .Where(row => visibleIds.Contains(row.ProjectId)).ToList();

        var users = await TryLoadAsync(
            "users", "Project team identities", false,
            () => LoadUsersAsync(
                connection,
                visibleSeeds.SelectMany(OwnerIds)
                    .Concat(visibleAssignments.Select(row => row.UserId))
                    .Distinct().ToArray(),
                context.RequestAborted));
        sources.Add(users.State);

        var time = await TryLoadAsync(
            "time_entries", "Approved and in-flight project time", true,
            () => LoadTimeAsync(connection, visibleIds, context.RequestAborted));
        sources.Add(time.State);

        var expenses = await TryLoadAsync(
            "project_expenses", "Module 005 current expense uploads", false,
            () => LoadExpensesAsync(connection, visibleIds, context.RequestAborted));
        sources.Add(expenses.State);

        var alerts = await TryLoadAsync(
            "cost_alerts", "Module 022 cost alert evidence", false,
            () => LoadAlertsAsync(connection, visibleIds, context.RequestAborted));
        sources.Add(alerts.State);

        var documents = await TryLoadAsync(
            "project_documents", "Project Workspace documents", false,
            () => LoadDocumentsAsync(connection, visibleIds, context.RequestAborted));
        sources.Add(documents.State);

        var metadata = await TryLoadAsync(
            "project_metadata", "Work Register commercial metadata", false,
            () => LoadMetadataAsync(connection, visibleIds, context.RequestAborted));
        sources.Add(metadata.State);

        var sell = await LoadSellAsync(connection, visibleSeeds, context.RequestAborted);
        sources.Add(sell.State);

        var projects = visibleSeeds.Select(seed => Calculate(
                seed,
                actor,
                workspace,
                visibleAssignments.Where(row => row.ProjectId == seed.ProjectId).ToArray(),
                time.Value.Where(row => row.ProjectId == seed.ProjectId).ToArray(),
                expenses.Value.Where(row => row.ProjectId == seed.ProjectId).ToArray(),
                alerts.Value.Where(row => row.ProjectId == seed.ProjectId).ToArray(),
                documents.Value.Where(row => row.ProjectId == seed.ProjectId).ToArray(),
                metadata.Value.TryGetValue(seed.ProjectId, out var metadataJson) ? metadataJson : null,
                sell.Value.TryGetValue(seed.ProjectId, out var commercial)
                    ? commercial
                    : SellCommercialProjectSummary.Missing(seed.ProjectId),
                users.Value,
                sources))
            .OrderBy(project => StatusOrder(project.BudgetStatus))
            .ThenBy(project => project.CustomerName)
            .ThenBy(project => project.ProjectName)
            .ToArray();

        return BuildOutcome.Success(new PortfolioData(
            actor,
            projects,
            sources.GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(source => source.Required ? 0 : 1)
                .ThenBy(source => source.Name).ToArray(),
            DateTimeOffset.UtcNow));
    }

    private static async Task<Actor?> LoadActorAsync(
        NpgsqlConnection connection,
        Guid actualUserId,
        Guid effectiveUserId,
        bool isViewAs,
        CancellationToken cancellationToken)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string email = "";
        string displayName = "";
        var found = false;

        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(app_user.email, ''),
                   COALESCE(NULLIF(app_user.display_name, ''), app_user.email, ''),
                   COALESCE(role.role_code, ''),
                   COALESCE(permission.permission_code, '')
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id = app_user.user_id AND assignment.is_active = TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id = assignment.app_role_id AND role.is_active = TRUE
            LEFT JOIN app_role_permissions role_permission
              ON role_permission.app_role_id = role.app_role_id
            LEFT JOIN app_permissions permission
              ON permission.app_permission_id = role_permission.app_permission_id
            WHERE app_user.user_id = @user_id AND app_user.is_active = TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", effectiveUserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found = true;
            email = reader.GetString(0);
            displayName = reader.GetString(1);
            if (!reader.IsDBNull(2) && !string.IsNullOrWhiteSpace(reader.GetString(2)))
                roles.Add(reader.GetString(2));
            if (!reader.IsDBNull(3) && !string.IsNullOrWhiteSpace(reader.GetString(3)))
                permissions.Add(reader.GetString(3));
        }

        return found
            ? new Actor(actualUserId, effectiveUserId, email, displayName, roles, permissions, isViewAs)
            : null;
    }

    private static async Task<List<ProjectSeed>> LoadProjectsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectSeed>();
        await using var command = new NpgsqlCommand("""
            SELECT project.project_id,
                   project.client_id,
                   COALESCE(client.client_name, ''),
                   COALESCE(project.project_code, ''),
                   COALESCE(project.project_name, ''),
                   COALESCE(project.status, 'unknown'),
                   project.start_date,
                   project.end_date,
                   COALESCE(project.billable, FALSE),
                   project.project_manager_user_id,
                   COALESCE(manager.display_name, manager.email, ''),
                   COALESCE(manager.email, ''),
                   to_jsonb(project)::text
            FROM projects project
            LEFT JOIN clients client ON client.client_id = project.client_id
            LEFT JOIN app_users manager ON manager.user_id = project.project_manager_user_id
            WHERE lower(COALESCE(project.status, '')) NOT IN ('cancelled', 'deleted')
            ORDER BY client.client_name, project.project_name
            LIMIT 1000;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var json = JsonDocument.Parse(reader.GetString(12));
            rows.Add(new ProjectSeed(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                DateOnlyOrNull(reader, 6),
                DateOnlyOrNull(reader, 7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.GetString(10),
                reader.GetString(11),
                json.RootElement.Clone()));
        }
        return rows;
    }

    private static async Task<List<Assignment>> LoadAssignmentsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<Assignment>();
        await using var command = new NpgsqlCommand("""
            WITH request_hours AS (
                SELECT request.project_id,
                       assignment.user_id,
                       SUM(assignment.allocated_hours)::numeric AS allocated_hours
                FROM engineering_resource_requests request
                JOIN engineering_resource_request_assignments assignment
                  ON assignment.engineering_resource_request_id =
                     request.engineering_resource_request_id
                WHERE request.project_id IS NOT NULL
                GROUP BY request.project_id, assignment.user_id
            ),
            assignment_counts AS (
                SELECT project_id, user_id, COUNT(*)::numeric AS assignment_count
                FROM project_assignments
                GROUP BY project_id, user_id
            )
            SELECT project_assignment.project_assignment_id,
                   project_assignment.project_id,
                   project_assignment.user_id,
                   COALESCE(app_user.display_name, app_user.email, ''),
                   COALESCE(app_user.email, ''),
                   COALESCE(task.task_code, ''),
                   COALESCE(task.task_name, ''),
                   COALESCE(
                       NULLIF(project_assignment.assigned_hours, 0),
                       request_hours.allocated_hours / NULLIF(assignment_counts.assignment_count, 0),
                       0
                   )::numeric,
                   project_assignment.allocation_percent
            FROM project_assignments project_assignment
            JOIN app_users app_user ON app_user.user_id = project_assignment.user_id
            LEFT JOIN project_tasks task ON task.task_id = project_assignment.task_id
            LEFT JOIN request_hours
              ON request_hours.project_id = project_assignment.project_id
             AND request_hours.user_id = project_assignment.user_id
            LEFT JOIN assignment_counts
              ON assignment_counts.project_id = project_assignment.project_id
             AND assignment_counts.user_id = project_assignment.user_id
            WHERE project_assignment.effective_end_date IS NULL
               OR project_assignment.effective_end_date >= CURRENT_DATE
            ORDER BY project_assignment.project_id, app_user.display_name;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Assignment(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6),
                reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8)));
        }
        return rows;
    }

    private static async Task<HashSet<Guid>> LoadPmScopeAsync(
        NpgsqlConnection connection,
        Actor actor,
        IReadOnlyList<ProjectSeed> projects,
        CancellationToken cancellationToken)
    {
        if (actor.Broad)
            return projects.Where(project => project.ProjectManagerUserId.HasValue)
                .Select(project => project.ProjectManagerUserId!.Value).ToHashSet();

        var ids = new HashSet<Guid> { actor.EffectiveUserId };
        if (!actor.PmLead) return ids;

        await using var command = new NpgsqlCommand("""
            WITH lead_teams AS (
                SELECT DISTINCT team_id
                FROM team_memberships
                WHERE user_id = @user_id
                  AND effective_start_date <= CURRENT_DATE
                  AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE)
            )
            SELECT DISTINCT membership.user_id
            FROM team_memberships membership
            JOIN lead_teams ON lead_teams.team_id = membership.team_id
            JOIN app_user_role_assignments assignment
              ON assignment.user_id = membership.user_id AND assignment.is_active = TRUE
            JOIN app_roles role
              ON role.app_role_id = assignment.app_role_id AND role.is_active = TRUE
            WHERE membership.effective_start_date <= CURRENT_DATE
              AND (membership.effective_end_date IS NULL
                   OR membership.effective_end_date >= CURRENT_DATE)
              AND upper(role.role_code) IN ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT');
            """, connection);
        command.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static async Task<Dictionary<Guid, User>> LoadUsersAsync(
        NpgsqlConnection connection,
        Guid[] userIds,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<Guid, User>();
        if (userIds.Length == 0) return rows;
        await using var command = new NpgsqlCommand("""
            SELECT user_id,
                   COALESCE(NULLIF(display_name, ''), email, ''),
                   COALESCE(email, ''),
                   COALESCE(job_title, '')
            FROM app_users
            WHERE user_id = ANY(@user_ids) AND is_active = TRUE;
            """, connection);
        command.Parameters.Add(new NpgsqlParameter(
            "user_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = userIds });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var user = new User(
                reader.GetGuid(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3));
            rows[user.UserId] = user;
        }
        return rows;
    }

    private static async Task<List<TimeUse>> LoadTimeAsync(
        NpgsqlConnection connection,
        HashSet<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<TimeUse>();
        if (projectIds.Count == 0) return rows;
        await using var command = new NpgsqlCommand("""
            SELECT project_id, user_id, SUM(hours)::numeric
            FROM time_entries
            WHERE project_id = ANY(@project_ids)
              AND lower(COALESCE(status, '')) NOT IN (
                  'voided', 'rejected', 'declined',
                  'manager_declined', 'pm_declined'
              )
            GROUP BY project_id, user_id;
            """, connection);
        AddUuidArray(command, "project_ids", projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new TimeUse(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2)));
        return rows;
    }

    private static async Task<List<Expense>> LoadExpensesAsync(
        NpgsqlConnection connection,
        HashSet<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<Expense>();
        if (projectIds.Count == 0) return rows;
        await using var command = new NpgsqlCommand("""
            SELECT upload.project_expense_upload_id,
                   upload.project_id,
                   upload.expense_owner_user_id,
                   COALESCE(owner.display_name, owner.email, ''),
                   upload.source_mode,
                   upload.source_format,
                   upload.original_file_name,
                   upload.period_start,
                   upload.period_end,
                   upload.currency,
                   upload.line_count,
                   upload.total_amount,
                   upload.reimbursable_amount,
                   upload.billing_treatment,
                   upload.uploaded_at,
                   upload.notification_status
            FROM project_expense_uploads upload
            LEFT JOIN app_users owner ON owner.user_id = upload.expense_owner_user_id
            WHERE upload.project_id = ANY(@project_ids)
              AND upload.is_current = TRUE
              AND upload.deleted_at IS NULL
            ORDER BY upload.project_id, upload.uploaded_at DESC;
            """, connection);
        AddUuidArray(command, "project_ids", projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Expense(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DateOnlyOrNull(reader, 7), DateOnlyOrNull(reader, 8),
                reader.GetString(9), reader.GetInt32(10),
                reader.GetDecimal(11), reader.GetDecimal(12),
                reader.GetString(13), DateTimeOffsetValue(reader, 14),
                reader.GetString(15)));
        }
        return rows;
    }

    private static async Task<List<Alert>> LoadAlertsAsync(
        NpgsqlConnection connection,
        HashSet<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<Alert>();
        if (projectIds.Count == 0) return rows;
        await using var command = new NpgsqlCommand("""
            SELECT project_cost_alert_id, project_id, alert_type,
                   alert_severity, alert_status, alert_summary,
                   last_detected_at, notification_queued_at,
                   notification_recipient_count
            FROM project_cost_alerts
            WHERE project_id = ANY(@project_ids)
              AND lower(alert_status) <> 'resolved'
            ORDER BY project_id, last_detected_at DESC;
            """, connection);
        AddUuidArray(command, "project_ids", projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Alert(
                reader.GetGuid(0), reader.GetGuid(1),
                reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5),
                DateTimeOffsetValue(reader, 6),
                reader.IsDBNull(7) ? null : DateTimeOffsetValue(reader, 7),
                reader.GetInt32(8)));
        }
        return rows;
    }

    private static async Task<List<Document>> LoadDocumentsAsync(
        NpgsqlConnection connection,
        HashSet<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<Document>();
        if (projectIds.Count == 0) return rows;
        await using var command = new NpgsqlCommand("""
            SELECT project_intake_document_id, project_id,
                   COALESCE(document_type, ''),
                   COALESCE(document_category, 'supporting'),
                   COALESCE(original_file_name, ''),
                   COALESCE(content_type, ''),
                   COALESCE(size_bytes, 0)::bigint,
                   COALESCE(engineering_visible, FALSE),
                   uploaded_at
            FROM project_intake_documents
            WHERE project_id = ANY(@project_ids) AND is_active = TRUE
            ORDER BY project_id, uploaded_at DESC
            LIMIT 2000;
            """, connection);
        AddUuidArray(command, "project_ids", projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            rows.Add(new Document(
                id, reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetInt64(6),
                reader.GetBoolean(7), DateTimeOffsetValue(reader, 8),
                $"/api/project-workspace/documents/{id}/download"));
        }
        return rows;
    }

    private static async Task<Dictionary<Guid, JsonElement>> LoadMetadataAsync(
        NpgsqlConnection connection,
        HashSet<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<Guid, JsonElement>();
        if (projectIds.Count == 0) return rows;
        await using var command = new NpgsqlCommand("""
            SELECT project_id, to_jsonb(metadata)::text
            FROM work_register_project_metadata metadata
            WHERE project_id = ANY(@project_ids);
            """, connection);
        AddUuidArray(command, "project_ids", projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var json = JsonDocument.Parse(reader.GetString(1));
            rows[reader.GetGuid(0)] = json.RootElement.Clone();
        }
        return rows;
    }

    private static async Task<Load<Dictionary<Guid, SellCommercialProjectSummary>>> LoadSellAsync(
        NpgsqlConnection connection,
        IReadOnlyList<ProjectSeed> projects,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<Guid, SellCommercialProjectSummary>();
        try
        {
            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows[project.ProjectId] =
                    await SellCommercialReadModelModule.LoadProjectCommercialSummaryAsync(
                        connection, project.ProjectId);
            }

            return Load<Dictionary<Guid, SellCommercialProjectSummary>>.Success(
                rows,
                SourceState.Healthy(
                    "sell_commercial",
                    "Module 026 governed SELL commercial read model",
                    false,
                    rows.Count));
        }
        catch (Exception exception)
        {
            var state = rows.Count == 0
                ? SourceState.Unavailable(
                    "sell_commercial",
                    "Module 026 governed SELL commercial read model",
                    false,
                    exception)
                : SourceState.Partial(
                    "sell_commercial",
                    "Module 026 governed SELL commercial read model",
                    false,
                    exception,
                    rows.Count,
                    "SELL context loaded for part of the visible portfolio.");
            return Load<Dictionary<Guid, SellCommercialProjectSummary>>.Success(rows, state);
        }
    }

    private static async Task<Load<T>> TryLoadAsync<T>(
        string key,
        string name,
        bool required,
        Func<Task<T>> loader)
    {
        try
        {
            var value = await loader();
            return Load<T>.Success(
                value,
                SourceState.Healthy(key, name, required, Count(value)));
        }
        catch (Exception exception)
        {
            return Load<T>.Success(
                Empty<T>(),
                SourceState.Unavailable(key, name, required, exception));
        }
    }

    private static ProjectFinancial Calculate(
        ProjectSeed seed,
        Actor actor,
        string workspace,
        Assignment[] assignments,
        TimeUse[] time,
        Expense[] expenses,
        Alert[] alerts,
        Document[] documents,
        JsonElement? metadata,
        SellCommercialProjectSummary commercial,
        IReadOnlyDictionary<Guid, User> users,
        IReadOnlyList<SourceState> sources)
    {
        var coordinatorId = JsonGuid(seed.Json,
            "project_coordinator_user_id", "project_team_coordinator_user_id", "ptc_user_id");
        var solutionArchitectId = JsonGuid(seed.Json,
            "solution_architect_user_id", "sales_architect_user_id");
        var accountExecutiveId = JsonGuid(seed.Json,
            "account_executive_user_id", "sales_executive_user_id");
        var contractType = Recorded(
            JsonString(seed.Json, "contract_type", "contractType"),
            commercial.ContractType);

        var plannedEngineering = JsonDecimal([seed.Json, metadata], "planned_engineering_cost");
        var plannedPm = JsonDecimal([seed.Json, metadata], "planned_pm_cost");
        var laborBudget = First(
            JsonDecimal([seed.Json, metadata],
                "labor_budget", "planned_total_project_cost", "project_labor_budget"),
            plannedEngineering.HasValue || plannedPm.HasValue
                ? plannedEngineering.GetValueOrDefault() + plannedPm.GetValueOrDefault()
                : null);
        var expenseBudget = JsonDecimal([seed.Json, metadata],
            "expense_budget", "project_expense_budget",
            "travel_expense_budget", "expenses_budget", "planned_travel_cost");
        var contractedValue = JsonDecimal([seed.Json, metadata],
            "contracted_value", "contract_value", "contract_value_amount",
            "customer_contract_value", "project_value", "sow_amount",
            "total_sow_amount", "quote_amount", "quoted_amount", "sell_total_amount",
            "project_list_price");

        var plannedHours = assignments.Sum(row => row.AssignedHours);
        var usedHours = time.Sum(row => row.UsedHours);
        var remainingHours = Math.Max(plannedHours - usedHours, 0m);
        var completion = plannedHours > 0
            ? Math.Round(usedHours / plannedHours * 100m, 2)
            : (decimal?)null;

        var expenseSourceDown = sources.Any(source =>
            source.Key == "project_expenses" && source.Status == "unavailable");
        decimal? uploadedExpenses = expenseSourceDown ? null : expenses.Sum(row => row.TotalAmount);

        var rates = commercial.Rates
            .Where(rate => rate.BillableDefault
                && rate.UnitType.Equals("hour", StringComparison.OrdinalIgnoreCase)
                && rate.UnitRate > 0)
            .Select(rate => rate.UnitRate).ToArray();
        decimal? rate = rates.Length > 0
            ? Math.Round(rates.Average(), 2)
            : laborBudget is > 0 && plannedHours > 0
                ? Math.Round(laborBudget.Value / plannedHours, 2)
                : null;
        var rateSource = rates.Length > 0
            ? commercial.CommercialSource == "SELL"
                ? "module_026_sell_governed_rate_card_average"
                : "current_governed_rate_card_average"
            : rate.HasValue
                ? "labor_budget_divided_by_planned_hours"
                : "not_available";

        decimal? laborCost = rate.HasValue
            ? Math.Round(usedHours * rate.Value, 2)
            : null;
        decimal? forecastLabor = rate.HasValue
            ? Math.Round((usedHours + remainingHours) * rate.Value, 2)
            : null;
        decimal? committed = laborCost.HasValue && uploadedExpenses.HasValue
            ? laborCost + uploadedExpenses
            : null;
        decimal? forecast = forecastLabor.HasValue && uploadedExpenses.HasValue
            ? forecastLabor + uploadedExpenses
            : null;
        decimal? budget = laborBudget.HasValue
            ? laborBudget + expenseBudget.GetValueOrDefault()
            : null;
        decimal? variance = budget.HasValue && forecast.HasValue
            ? budget - forecast
            : null;
        var budgetBasis = !laborBudget.HasValue
            ? "labor_budget_missing"
            : expenseBudget.HasValue
                ? "labor_and_expense_budget"
                : "labor_budget_only_expense_budget_missing";
        var budgetStatus = FinancialStatus(budget, forecast, laborBudget, expenseBudget);

        var visibility = Visibility(
            actor, workspace, seed, coordinatorId,
            solutionArchitectId, accountExecutiveId, assignments);

        var engineers = assignments.GroupBy(row => row.UserId)
            .Select(group => new Engineer(
                group.Key,
                group.First().DisplayName,
                group.First().Email,
                group.Sum(row => row.AssignedHours),
                time.Where(row => row.UserId == group.Key).Sum(row => row.UsedHours),
                group.Select(row => row.TaskName)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value).ToArray()))
            .OrderBy(engineer => engineer.DisplayName).ToArray();

        var expenseDetails = expenses.Take(20).Select(row => new ExpenseView(
            row.UploadId, row.OwnerUserId, row.OwnerName,
            row.SourceMode, row.SourceFormat, row.OriginalFileName,
            row.PeriodStart, row.PeriodEnd, row.Currency, row.LineCount,
            visibility.FullAmounts ? row.TotalAmount : null,
            visibility.FullAmounts ? row.ReimbursableAmount : null,
            row.BillingTreatment, row.UploadedAt, row.NotificationStatus)).ToArray();

        var documentGroups = documents
            .Where(document => workspace is "engineering" or "pm")
            .Where(document => workspace != "engineering" || document.EngineeringVisible)
            .GroupBy(DocumentGroup)
            .Select(group => new DocumentGroupView(
                group.Key,
                group.Count(),
                group.OrderByDescending(document => document.UploadedAt).Take(12)
                    .Select(document => new DocumentView(
                        document.DocumentId, document.DocumentType,
                        document.DocumentCategory, document.OriginalFileName,
                        document.ContentType, document.SizeBytes,
                        document.EngineeringVisible, document.UploadedAt,
                        document.DownloadUrl)).ToArray()))
            .OrderBy(group => DocumentOrder(group.Group)).ToArray();

        var missing = new List<string>();
        if (!contractedValue.HasValue) missing.Add("contracted_value");
        if (!laborBudget.HasValue || laborBudget <= 0) missing.Add("labor_budget");
        if (!expenseBudget.HasValue) missing.Add("expense_budget");
        if (plannedHours <= 0) missing.Add("planned_hours");
        if (!rate.HasValue) missing.Add("governed_labor_rate_basis");
        if (commercial.ReadinessStatus is "sell_connector_not_ready" or "sell_quote_missing")
            missing.Add("sell_association");
        missing.AddRange(sources.Where(source => source.Status == "unavailable")
            .Select(source => $"source:{source.Key}"));

        var notificationStatus = alerts.Length == 0
            ? "no_open_alerts"
            : alerts.Any(alert => alert.NotificationQueuedAt.HasValue)
                ? "notification_queued"
                : "alert_open_delivery_not_queued";

        return new ProjectFinancial(
            seed.ProjectId, seed.ClientId, seed.CustomerName,
            seed.ProjectCode, seed.ProjectName, seed.ProjectStatus,
            seed.StartDate, seed.EndDate, seed.Billable,
            seed.ProjectManagerUserId, seed.ProjectManagerName,
            seed.ProjectManagerEmail,
            UserFor(coordinatorId, users),
            UserFor(solutionArchitectId, users),
            UserFor(accountExecutiveId, users),
            engineers, contractType, visibility,
            visibility.Commercial ? contractedValue : null,
            visibility.Commercial ? laborBudget : null,
            visibility.Commercial ? expenseBudget : null,
            plannedHours, usedHours, remainingHours,
            visibility.FullAmounts ? laborCost : null,
            visibility.Commercial ? uploadedExpenses : null,
            visibility.FullAmounts ? committed : null,
            visibility.Commercial ? forecast : null,
            visibility.Commercial ? variance : null,
            completion, budgetStatus, budgetBasis,
            notificationStatus, alerts.Length,
            alerts.Count(alert => alert.Severity.Equals("high", StringComparison.OrdinalIgnoreCase)),
            new SellView(
                "Module 026", commercial.CommercialSource,
                commercial.ReadinessStatus, commercial.SellQuoteNumber,
                commercial.BillingMethod, commercial.ConnectorReady,
                commercial.LiveSyncEnabled, commercial.CommercialCutoverEnabled,
                commercial.CutoverReady, commercial.LastSuccessfulSyncAt,
                commercial.RateCard is null ? null : new RateCardView(
                    commercial.RateCard.RateCardId,
                    commercial.RateCard.RateCardCode,
                    commercial.RateCard.RateCardName,
                    commercial.RateCard.Status,
                    commercial.RateCard.EffectiveStartDate,
                    commercial.RateCard.EffectiveEndDate,
                    commercial.RateCard.IsCustomerSpecific,
                    commercial.Rates.Count),
                "Connection and credential ownership remain in Module 026."),
            expenseDetails, documentGroups,
            Explanations(rateSource, budgetBasis),
            missing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray(),
            DateTimeOffset.UtcNow);
    }

    private static IEnumerable<ProjectSeed> FilterProjects(
        IReadOnlyList<ProjectSeed> projects,
        IReadOnlyList<Assignment> assignments,
        Actor actor,
        string workspace,
        Guid? requestedPmId,
        HashSet<Guid> permittedPmIds)
    {
        var assignedProjects = assignments
            .Where(row => row.UserId == actor.EffectiveUserId)
            .Select(row => row.ProjectId).ToHashSet();

        foreach (var project in projects)
        {
            var coordinator = JsonGuid(project.Json,
                "project_coordinator_user_id", "project_team_coordinator_user_id", "ptc_user_id");
            var ae = JsonGuid(project.Json,
                "account_executive_user_id", "sales_executive_user_id");
            var sa = JsonGuid(project.Json,
                "solution_architect_user_id", "sales_architect_user_id");
            var manager = project.ProjectManagerUserId == actor.EffectiveUserId;
            var salesOwner = ae == actor.EffectiveUserId || sa == actor.EffectiveUserId;
            var related = manager
                || coordinator == actor.EffectiveUserId
                || salesOwner
                || assignedProjects.Contains(project.ProjectId);

            var visible = workspace switch
            {
                "pm" when requestedPmId.HasValue =>
                    permittedPmIds.Contains(requestedPmId.Value)
                    && project.ProjectManagerUserId == requestedPmId,
                "pm" when actor.Broad => true,
                "pm" when actor.PmLead =>
                    project.ProjectManagerUserId.HasValue
                    && permittedPmIds.Contains(project.ProjectManagerUserId.Value),
                "pm" => manager,
                "sales" => actor.Broad || salesOwner,
                "rate-card" => actor.Broad || actor.RateAdmin || related,
                _ => actor.Broad || related
            };
            if (visible) yield return project;
        }
    }

    private static VisibilityView Visibility(
        Actor actor,
        string workspace,
        ProjectSeed project,
        Guid? coordinator,
        Guid? sa,
        Guid? ae,
        IReadOnlyList<Assignment> assignments)
    {
        if (actor.Broad || actor.RateAdmin
            || project.ProjectManagerUserId == actor.EffectiveUserId
            || coordinator == actor.EffectiveUserId)
            return new("full_project_financials", true, true, true,
                "Full financial visibility is granted by role or project ownership.");

        if (workspace == "sales" || sa == actor.EffectiveUserId
            || ae == actor.EffectiveUserId || actor.Sales)
            return new("commercial_summary", false, true, true,
                "Sales sees commercial status, forecast, variance, team, and SELL context without detailed labor-cost basis.");

        if (assignments.Any(row => row.UserId == actor.EffectiveUserId))
            return new("hours_and_progress", false, false, false,
                "Engineering sees allocated, used, and remaining hours; commercial amounts are restricted.");

        return new("identity_only", false, false, false,
            "Financial amounts are restricted for the current project relationship.");
    }

    private static Calculation[] Explanations(string rateSource, string budgetBasis) =>
    [
        new("remaining_hours", "Remaining hours",
            "max(planned hours - used hours, 0)",
            "Project assignments and non-declined project time entries."),
        new("labor_cost", "Calculated labor cost",
            "used hours × effective governed hourly rate",
            rateSource == "not_available"
                ? "No governed rate basis is available."
                : $"Rate basis: {rateSource}. This is a project-cost estimate, not payroll cost."),
        new("committed_cost", "Committed cost",
            "calculated labor cost + current non-deleted Module 005 expenses",
            "Deleted and superseded expense uploads are excluded."),
        new("forecasted_final_cost", "Forecasted final cost",
            "(used hours + remaining hours) × effective rate + current expenses",
            "Remaining work is forecast at the current governed rate basis."),
        new("current_variance", "Current variance",
            "known budget - forecasted final cost",
            $"Budget completeness: {budgetBasis}."),
        new("completion_percentage", "Completion percentage",
            "used hours ÷ planned hours × 100",
            "Values over 100% indicate hours exceeded the assignment plan.")
    ];

    private static object Summary(IReadOnlyCollection<ProjectFinancial> projects) => new
    {
        projectCount = projects.Count,
        customerCount = projects.Select(project =>
                project.ClientId?.ToString() ?? project.CustomerName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        plannedHours = projects.Sum(project => project.PlannedHours),
        usedHours = projects.Sum(project => project.UsedHours),
        remainingHours = projects.Sum(project => project.RemainingHours),
        uploadedExpenses = SumKnown(projects.Select(project => project.UploadedExpenses)),
        committedCost = SumKnown(projects.Select(project => project.CommittedCost)),
        forecastedFinalCost = SumKnown(projects.Select(project => project.ForecastedFinalCost)),
        currentVariance = SumKnown(projects.Select(project => project.CurrentVariance)),
        approachingBudgetCount = projects.Count(project => project.BudgetStatus == "approaching_budget"),
        overBudgetCount = projects.Count(project => project.BudgetStatus == "over_budget"),
        missingFinancialInformationCount = projects.Count(project => project.Missing.Length > 0),
        openAlertCount = projects.Sum(project => project.OpenAlertCount),
        notificationQueuedCount = projects.Count(project => project.NotificationStatus == "notification_queued")
    };

    private static object Access(Actor actor, string workspace) => new
    {
        actualUserId = actor.ActualUserId,
        effectiveUserId = actor.EffectiveUserId,
        actor.Email,
        actor.DisplayName,
        roles = actor.Roles.OrderBy(value => value),
        workspace,
        actor.IsViewAs,
        readOnly = true,
        viewAsTransfersMutationAuthority = false,
        projectScope = actor.Broad ? "organization" : workspace switch
        {
            "pm" => "managed_projects",
            "sales" => "sales_owned_projects",
            "rate-card" => "authorized_commercial_context",
            _ => "role_and_assignment_scope"
        }
    };

    private static object[] CalculationContract() =>
    [
        new { field = "plannedHours", authority = "Project assignments and engineering resource allocations." },
        new { field = "usedHours", authority = "Non-voided and non-declined ProjectPulse time entries." },
        new { field = "laborCost", authority = "Module 026 SELL/current governed rate model, with a labeled budget-derived fallback." },
        new { field = "uploadedExpenses", authority = "Current, non-deleted Module 005 expense uploads." },
        new { field = "forecastedFinalCost", authority = "Assignments, time entries, governed rates, and Module 005 expenses." },
        new { field = "currentVariance", authority = "Known project budget minus calculated forecast." }
    ];

    private static object Dependencies() => new
    {
        module005 = "Current uploaded project expenses",
        module026 = new
        {
            responsibility = "SELL connection, health, commercial rates, and credentials",
            connectionReused = true,
            secondCredentialSystemCreated = false
        },
        assignments = "Project team and planned hours",
        documents = "Module 019 project documents and working download route",
        timeEntries = "Used hours",
        rateCards = "Governed labor-rate basis",
        notifications = "Existing Module 022 evidence only; Group 4 owns configurable routing and schedules."
    };

    private static object Security() => new
    {
        readOnly = true,
        mutationsEnabled = false,
        providerCredentialsReturned = false,
        rawDatabaseErrorsReturned = false,
        costVisibilityServerEnforced = true,
        viewAsTransfersMutationAuthority = false,
        module011Dependency = false
    };

    private static string FinancialStatus(
        decimal? budget,
        decimal? forecast,
        decimal? laborBudget,
        decimal? expenseBudget)
    {
        if (!laborBudget.HasValue || laborBudget <= 0 || !forecast.HasValue)
            return "missing_financial_information";
        if (forecast > budget) return "over_budget";
        if (budget > 0 && forecast >= budget * 0.85m) return "approaching_budget";
        return expenseBudget.HasValue ? "on_track" : "on_track_partial_expense_budget";
    }

    private static int StatusOrder(string status) => status switch
    {
        "over_budget" => 0,
        "approaching_budget" => 1,
        "missing_financial_information" => 2,
        "on_track_partial_expense_budget" => 3,
        _ => 4
    };

    private static string SearchText(ProjectFinancial project) =>
        $"{project.CustomerName} {project.ProjectCode} {project.ProjectName} "
        + $"{project.ProjectManagerName} {project.ContractType} "
        + $"{project.Sell.SellQuoteNumber} {project.BudgetStatus}";

    private static string Workspace(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized is "pm" or "engineering" or "sales" or "rate-card"
            ? normalized
            : "engineering";
    }

    private static string Clean(string? value, int length)
    {
        var clean = (value ?? "").Replace('\0', ' ').Trim();
        return clean.Length <= length ? clean : clean[..length];
    }

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue)
            .Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static decimal? First(params decimal?[] values) =>
        values.FirstOrDefault(value => value.HasValue);

    private static string Recorded(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)
            && !value.Equals("not_recorded", StringComparison.OrdinalIgnoreCase))
        ?? "not_recorded";

    private static Guid? JsonGuid(JsonElement element, params string[] keys)
    {
        var value = JsonValue(element, keys);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.Value.GetString(), out var id)
            ? id
            : null;
    }

    private static string? JsonString(JsonElement element, params string[] keys)
    {
        var value = JsonValue(element, keys);
        if (!value.HasValue) return null;
        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.Value.ToString(),
            _ => null
        };
    }

    private static decimal? JsonDecimal(
        IEnumerable<JsonElement?> elements,
        params string[] keys)
    {
        foreach (var element in elements)
        {
            if (!element.HasValue) continue;
            var value = JsonValue(element.Value, keys);
            if (!value.HasValue) continue;
            if (value.Value.ValueKind == JsonValueKind.Number
                && value.Value.TryGetDecimal(out var number)) return number;
            if (value.Value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.Value.GetString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static JsonElement? JsonValue(
        JsonElement element,
        IEnumerable<string> keys,
        int depth = 0)
    {
        if (depth > 5 || element.ValueKind != JsonValueKind.Object) return null;
        var expected = keys.Select(JsonKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (expected.Contains(JsonKey(property.Name))) return property.Value;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            var nested = JsonValue(property.Value, expected, depth + 1);
            if (nested.HasValue) return nested;
        }
        return null;
    }

    private static string JsonKey(string value) =>
        new(value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());

    private static IEnumerable<Guid> OwnerIds(ProjectSeed project)
    {
        if (project.ProjectManagerUserId.HasValue)
            yield return project.ProjectManagerUserId.Value;
        foreach (var id in new[]
        {
            JsonGuid(project.Json, "project_coordinator_user_id", "project_team_coordinator_user_id", "ptc_user_id"),
            JsonGuid(project.Json, "solution_architect_user_id", "sales_architect_user_id"),
            JsonGuid(project.Json, "account_executive_user_id", "sales_executive_user_id")
        })
            if (id.HasValue) yield return id.Value;
    }

    private static User? UserFor(Guid? id, IReadOnlyDictionary<Guid, User> users) =>
        id.HasValue && users.TryGetValue(id.Value, out var user)
            ? user
            : id.HasValue ? new User(id.Value, "User not resolved", "", "") : null;

    private static string DocumentGroup(Document document)
    {
        var value = $"{document.DocumentType} {document.DocumentCategory} {document.OriginalFileName}"
            .ToLowerInvariant();
        if (value.Contains("iqs")) return "IQS files";
        if (value.Contains("service request") || value.Contains("service_request"))
            return "Service requests";
        if (value.Contains("customer")) return "Customer documents";
        return "Project documents";
    }

    private static int DocumentOrder(string group) => group switch
    {
        "IQS files" => 0,
        "Service requests" => 1,
        "Project documents" => 2,
        "Customer documents" => 3,
        _ => 4
    };

    private static void AddUuidArray(
        NpgsqlCommand command,
        string name,
        IEnumerable<Guid> values) =>
        command.Parameters.Add(new NpgsqlParameter(
            name, NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = values.ToArray()
        });

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
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return (T)Activator.CreateInstance(type)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
            return (T)Activator.CreateInstance(type)!;
        return default!;
    }

    private static string ConnectionString()
    {
        foreach (var name in new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return "";

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10,
            Timeout = 5,
            CommandTimeout = 15
        }.ConnectionString;
    }

    private static string Diagnostic(Exception exception) =>
        exception is PostgresException postgres
            ? $"POSTGRES_{postgres.SqlState}"
            : exception.GetType().Name;

    private static DateOnly? DateOnlyOrNull(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    private static DateTimeOffset DateTimeOffsetValue(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? "")
        };
    }

    private sealed record Actor(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string Email,
        string DisplayName,
        HashSet<string> Roles,
        HashSet<string> Permissions,
        bool IsViewAs)
    {
        public bool Broad => Roles.Any(role => BroadRoles.Contains(
                role, StringComparer.OrdinalIgnoreCase))
            || Permissions.Contains("SYSTEM_ADMINISTRATION")
            || Permissions.Contains("MANAGE_ALL");
        public bool PmLead => Roles.Any(role => PmLeadRoles.Contains(
            role, StringComparer.OrdinalIgnoreCase));
        public bool Sales => Roles.Any(role => SalesRoles.Contains(
            role, StringComparer.OrdinalIgnoreCase));
        public bool RateAdmin => Permissions.Contains("MANAGE_RATE_CARDS")
            || Permissions.Contains("MANAGE_COMMERCIAL_RATES")
            || Permissions.Contains("MANAGE_ALL");
    }

    private sealed record ProjectSeed(
        Guid ProjectId, Guid? ClientId, string CustomerName,
        string ProjectCode, string ProjectName, string ProjectStatus,
        DateOnly? StartDate, DateOnly? EndDate, bool Billable,
        Guid? ProjectManagerUserId, string ProjectManagerName,
        string ProjectManagerEmail, JsonElement Json);

    private sealed record Assignment(
        Guid AssignmentId, Guid ProjectId, Guid UserId,
        string DisplayName, string Email, string TaskCode,
        string TaskName, decimal AssignedHours, decimal? AllocationPercent);

    private sealed record TimeUse(Guid ProjectId, Guid UserId, decimal UsedHours);

    private sealed record Expense(
        Guid UploadId, Guid ProjectId, Guid OwnerUserId, string OwnerName,
        string SourceMode, string SourceFormat, string? OriginalFileName,
        DateOnly? PeriodStart, DateOnly? PeriodEnd, string Currency,
        int LineCount, decimal TotalAmount, decimal ReimbursableAmount,
        string BillingTreatment, DateTimeOffset UploadedAt, string NotificationStatus);

    private sealed record Alert(
        Guid AlertId, Guid ProjectId, string Type, string Severity,
        string Status, string Summary, DateTimeOffset LastDetectedAt,
        DateTimeOffset? NotificationQueuedAt, int RecipientCount);

    private sealed record Document(
        Guid DocumentId, Guid ProjectId, string DocumentType,
        string DocumentCategory, string OriginalFileName, string ContentType,
        long SizeBytes, bool EngineeringVisible, DateTimeOffset UploadedAt,
        string DownloadUrl);

    private sealed record User(Guid UserId, string DisplayName, string Email, string JobTitle);

    private sealed record Engineer(
        Guid UserId, string DisplayName, string Email,
        decimal AssignedHours, decimal UsedHours, string[] Tasks);

    private sealed record VisibilityView(
        string Level, bool FullAmounts, bool Commercial,
        bool RateContext, string Explanation);

    private sealed record ExpenseView(
        Guid UploadId, Guid OwnerUserId, string OwnerName,
        string SourceMode, string SourceFormat, string? OriginalFileName,
        DateOnly? PeriodStart, DateOnly? PeriodEnd, string Currency,
        int LineCount, decimal? TotalAmount, decimal? ReimbursableAmount,
        string BillingTreatment, DateTimeOffset UploadedAt, string NotificationStatus);

    private sealed record DocumentView(
        Guid DocumentId, string DocumentType, string DocumentCategory,
        string OriginalFileName, string ContentType, long SizeBytes,
        bool EngineeringVisible, DateTimeOffset UploadedAt, string DownloadUrl);

    private sealed record DocumentGroupView(string Group, int Count, DocumentView[] Documents);

    private sealed record Calculation(string Key, string Label, string Formula, string Explanation);

    private sealed record RateCardView(
        Guid RateCardId, string RateCardCode, string RateCardName,
        string Status, DateOnly EffectiveStartDate, DateOnly? EffectiveEndDate,
        bool IsCustomerSpecific, int RateLineCount);

    private sealed record SellView(
        string ConnectionOwner, string CommercialSource, string ReadinessStatus,
        string SellQuoteNumber, string BillingMethod, bool ConnectorReady,
        bool LiveSyncEnabled, bool CommercialCutoverEnabled, bool CutoverReady,
        DateTimeOffset? LastSuccessfulSyncAt, RateCardView? RateCard, string GovernanceNote);

    private sealed record ProjectFinancial(
        Guid ProjectId, Guid? ClientId, string CustomerName,
        string ProjectCode, string ProjectName, string ProjectStatus,
        DateOnly? StartDate, DateOnly? EndDate, bool Billable,
        Guid? ProjectManagerUserId, string ProjectManagerName,
        string ProjectManagerEmail, User? ProjectTeamCoordinator,
        User? SolutionArchitect, User? AccountExecutive, Engineer[] Engineers,
        string ContractType, VisibilityView Visibility,
        decimal? ContractedValue, decimal? LaborBudget, decimal? ExpenseBudget,
        decimal PlannedHours, decimal UsedHours, decimal RemainingHours,
        decimal? LaborCost, decimal? UploadedExpenses, decimal? CommittedCost,
        decimal? ForecastedFinalCost, decimal? CurrentVariance,
        decimal? CompletionPercentage, string BudgetStatus,
        string VarianceCompleteness, string NotificationStatus,
        int OpenAlertCount, int HighAlertCount, SellView Sell,
        ExpenseView[] Expenses, DocumentGroupView[] DocumentGroups,
        Calculation[] Calculations, string[] Missing, DateTimeOffset CalculatedAt);

    private sealed record SourceState(
        string Key, string Name, string Status, bool Required,
        string Message, string DiagnosticCode, int RecordCount,
        DateTimeOffset ObservedAt)
    {
        public static SourceState Healthy(
            string key, string name, bool required, int count) =>
            new(key, name, "healthy", required,
                "The source loaded successfully.", "", count, DateTimeOffset.UtcNow);

        public static SourceState Unavailable(
            string key, string name, bool required, Exception exception) =>
            new(key, name, "unavailable", required,
                "This source is unavailable; other project data remains usable.",
                Diagnostic(exception), 0, DateTimeOffset.UtcNow);

        public static SourceState Partial(
            string key, string name, bool required, Exception exception,
            int count, string message) =>
            new(key, name, "partial", required, message,
                Diagnostic(exception), count, DateTimeOffset.UtcNow);
    }

    private sealed record Load<T>(T Value, SourceState State)
    {
        public static Load<T> Success(T value, SourceState state) => new(value, state);
    }

    private sealed record PortfolioData(
        Actor Actor, ProjectFinancial[] Projects,
        SourceState[] Sources, DateTimeOffset GeneratedAt);

    private sealed record BuildOutcome(PortfolioData? Data, IResult? Failure)
    {
        public static BuildOutcome Success(PortfolioData data) => new(data, null);
        public static BuildOutcome Fail(IResult failure) => new(null, failure);
    }
}

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Enterprise Project Management extensions for Module 066. These endpoints add
/// PM-owned working copies, project controls, RAID, status reports, SOW evidence
/// readiness, and reviewed customer sharing without changing canonical tasks.
/// </summary>
internal static class ProjectFlowHiveEnterpriseModule
{
    private const string MigrationId = "086_module_066_flowhive_enterprise_pm";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static WebApplication MapProjectFlowHiveEnterpriseEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/project-flowhive/projects/{projectId:guid}/enterprise",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetEnterpriseWorkspaceAsync);
        app.MapPut(
            "/api/project-flowhive/projects/{projectId:guid}/working-copy",
            (Func<Guid, ProjectFlowHiveWorkingCopyRequest, HttpContext, CancellationToken, Task<IResult>>)SaveWorkingCopyAsync);
        app.MapPut(
            "/api/project-flowhive/projects/{projectId:guid}/controls",
            (Func<Guid, ProjectFlowHiveProjectControlsRequest, HttpContext, CancellationToken, Task<IResult>>)SaveControlsAsync);
        app.MapPost(
            "/api/project-flowhive/projects/{projectId:guid}/raid",
            (Func<Guid, ProjectFlowHiveRaidRequest, HttpContext, CancellationToken, Task<IResult>>)CreateRaidAsync);
        app.MapPut(
            "/api/project-flowhive/projects/{projectId:guid}/raid/{raidItemId:guid}",
            (Func<Guid, Guid, ProjectFlowHiveRaidRequest, HttpContext, CancellationToken, Task<IResult>>)UpdateRaidAsync);
        app.MapDelete(
            "/api/project-flowhive/projects/{projectId:guid}/raid/{raidItemId:guid}",
            (Func<Guid, Guid, HttpContext, CancellationToken, Task<IResult>>)DeleteRaidAsync);
        app.MapPost(
            "/api/project-flowhive/projects/{projectId:guid}/status-reports",
            (Func<Guid, ProjectFlowHiveStatusReportRequest, HttpContext, CancellationToken, Task<IResult>>)CreateStatusReportAsync);
        app.MapPost(
            "/api/project-flowhive/projects/{projectId:guid}/customer-shares",
            (Func<Guid, ProjectFlowHiveCustomerShareRequest, HttpContext, CancellationToken, Task<IResult>>)CreateCustomerShareAsync);
        app.MapDelete(
            "/api/project-flowhive/projects/{projectId:guid}/customer-shares/{shareId:guid}",
            (Func<Guid, Guid, ProjectFlowHiveCustomerShareRevokeRequest, HttpContext, CancellationToken, Task<IResult>>)RevokeCustomerShareAsync);
        app.MapPost(
            "/api/project-flowhive/projects/{projectId:guid}/sow-evidence/{documentId:guid}/prepare",
            (Func<Guid, Guid, ProjectFlowHiveSowEvidencePrepareRequest, HttpContext, CancellationToken, Task<IResult>>)PrepareSowEvidenceAsync);
        app.MapGet(
                "/api/project-flowhive/share/{token}",
                (Func<string, HttpContext, CancellationToken, Task<IResult>>)ViewCustomerShareAsync)
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> GetEnterpriseWorkspaceAsync(
        Guid projectId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: false, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;

        var workingCopy = await LoadWorkingCopyAsync(connection, projectId, cancellationToken);
        var controls = await LoadControlsAsync(connection, projectId, cancellationToken);
        var raid = await LoadRaidAsync(connection, projectId, cancellationToken);
        var statusReports = await LoadStatusReportsAsync(connection, projectId, cancellationToken);
        var shares = await LoadSharesAsync(connection, projectId, cancellationToken);
        var evidence = await LoadSowEvidenceAsync(connection, projectId, cancellationToken);

        return Results.Ok(new
        {
            module = "066",
            status = "flowhive_enterprise_workspace_loaded",
            project = new
            {
                access.ProjectId,
                access.ProjectCode,
                access.ProjectName,
                access.CustomerName,
                access.ProjectManagerUserId,
                access.ProjectManagerName
            },
            access = new
            {
                access.ActualUserId,
                access.EffectiveUserId,
                access.DisplayName,
                access.IsViewAs,
                access.IsProjectManagerOwner,
                access.IsAdministrator,
                access.CanView,
                access.CanManage,
                access.CanShare,
                access.CanViewFinancials,
                managementRule = "A Project Manager may mutate only projects for which they are the assigned Project Manager. Administrator support authority is non-transferable and unavailable in View-As."
            },
            workingCopy,
            controls,
            raidItems = raid,
            statusReports,
            customerShares = shares,
            sowEvidence = evidence,
            sowEvidenceSummary = new
            {
                candidateCount = evidence.Count,
                readyCount = evidence.Count(item => item.ReadyForAiPlanner),
                approvedSowScopeReady = evidence.Any(item => item.ReadyForAiPlanner),
                explanation = evidence.Any(item => item.ReadyForAiPlanner)
                    ? "At least one approved, citation-ready SOW scope source is available to AI Planner."
                    : "AI Planner requires a project SOW that is visible, privately processed, active, approved or canonical, indexed, and supported by scope citations."
            },
            financials = new
            {
                route = $"/api/project-financials/projects/{projectId}?workspace=project_management",
                authoritative = true,
                duplicateLedgerCreated = false
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> SaveWorkingCopyAsync(
        Guid projectId,
        ProjectFlowHiveWorkingCopyRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request.Plan is null)
            return Validation("A FlowHive plan is required.");
        if (request.Plan.ProjectId != projectId)
            return Validation("The working copy project does not match the selected project.");

        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;

        var validation = ProjectFlowHiveScheduleEngine.Validate(request.Plan);
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(request.Plan);
        var payload = JsonSerializer.Serialize(request.Plan, Json);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            INSERT INTO project_flowhive_working_copies(
                project_id,plan_id,working_payload,updated_by_user_id)
            VALUES(@project_id,@plan_id,@payload::jsonb,@actor)
            ON CONFLICT(project_id) DO UPDATE
            SET plan_id=EXCLUDED.plan_id,
                working_payload=EXCLUDED.working_payload,
                updated_by_user_id=EXCLUDED.updated_by_user_id
            WHERE @expected_row_version::uuid IS NULL
               OR project_flowhive_working_copies.row_version=@expected_row_version
            RETURNING working_revision,row_version,updated_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("plan_id", NpgsqlDbType.Uuid).Value =
            request.Plan.PlanId.HasValue ? request.Plan.PlanId.Value : DBNull.Value;
        command.Parameters.AddWithValue("payload", payload);
        command.Parameters.AddWithValue("actor", access.ActualUserId);
        command.Parameters.Add("expected_row_version", NpgsqlDbType.Uuid).Value =
            request.ExpectedRowVersion.HasValue ? request.ExpectedRowVersion.Value : DBNull.Value;

        int revision;
        Guid rowVersion;
        DateTimeOffset updatedAt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Conflict(new
                {
                    status = "working_copy_version_conflict",
                    message = "The FlowHive working copy changed after it was loaded. Reload before saving again.",
                    stateChanged = false
                });
            }
            revision = reader.GetInt32(0);
            rowVersion = reader.GetGuid(1);
            updatedAt = reader.GetFieldValue<DateTimeOffset>(2);
        }

        await InsertAuditAsync(
            connection,
            transaction,
            projectId,
            request.Plan.PlanId,
            null,
            "working_copy_saved",
            access,
            new { revision, rowVersion, validation.Valid, schedule.Valid },
            request.Plan.CelarAiCorrelationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "flowhive_working_copy_saved",
            workingRevision = revision,
            rowVersion,
            updatedAt,
            validation,
            schedule,
            message = "The PM working copy was saved. Create an immutable version when the plan is ready for formal review.",
            stateChanged = true
        });
    }

    private static async Task<IResult> SaveControlsAsync(
        Guid projectId,
        ProjectFlowHiveProjectControlsRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;

        var contractType = ContractType(request.ContractType);
        var currency = Currency(request.CurrencyCode);
        var method = PercentMethod(request.PercentCompleteMethod);
        var cadence = StatusCadence(request.StatusReportCadence);
        var note = Clean(request.FinancialNotes, 12_000);

        const string sql = """
            INSERT INTO project_flowhive_project_controls(
                project_id,contract_type,currency_code,approved_budget,expense_budget,
                contingency_budget,forecast_at_completion,percent_complete_method,
                status_report_cadence,customer_sharing_enabled,financial_notes,updated_by_user_id)
            VALUES(
                @project_id,@contract_type,@currency,@approved_budget,@expense_budget,
                @contingency_budget,@forecast,@method,@cadence,@sharing,@notes,@actor)
            ON CONFLICT(project_id) DO UPDATE
            SET contract_type=EXCLUDED.contract_type,
                currency_code=EXCLUDED.currency_code,
                approved_budget=EXCLUDED.approved_budget,
                expense_budget=EXCLUDED.expense_budget,
                contingency_budget=EXCLUDED.contingency_budget,
                forecast_at_completion=EXCLUDED.forecast_at_completion,
                percent_complete_method=EXCLUDED.percent_complete_method,
                status_report_cadence=EXCLUDED.status_report_cadence,
                customer_sharing_enabled=EXCLUDED.customer_sharing_enabled,
                financial_notes=EXCLUDED.financial_notes,
                updated_by_user_id=EXCLUDED.updated_by_user_id
            RETURNING updated_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("contract_type", contractType);
        command.Parameters.AddWithValue("currency", currency);
        AddNullableMoney(command, "approved_budget", request.ApprovedBudget);
        AddNullableMoney(command, "expense_budget", request.ExpenseBudget);
        AddNullableMoney(command, "contingency_budget", request.ContingencyBudget);
        AddNullableMoney(command, "forecast", request.ForecastAtCompletion);
        command.Parameters.AddWithValue("method", method);
        command.Parameters.AddWithValue("cadence", cadence);
        command.Parameters.AddWithValue("sharing", request.CustomerSharingEnabled);
        command.Parameters.AddWithValue("notes", note);
        command.Parameters.AddWithValue("actor", access.ActualUserId);
        var updatedAt = (DateTimeOffset)(await command.ExecuteScalarAsync(cancellationToken)
            ?? DateTimeOffset.UtcNow);

        return Results.Ok(new
        {
            status = "flowhive_project_controls_saved",
            controls = new
            {
                projectId,
                contractType,
                currencyCode = currency,
                request.ApprovedBudget,
                request.ExpenseBudget,
                request.ContingencyBudget,
                request.ForecastAtCompletion,
                percentCompleteMethod = method,
                statusReportCadence = cadence,
                request.CustomerSharingEnabled,
                financialNotes = note,
                updatedAt
            },
            stateChanged = true
        });
    }

    private static async Task<IResult> CreateRaidAsync(
        Guid projectId,
        ProjectFlowHiveRaidRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        var title = Clean(request.Title, 240);
        if (title.Length < 3) return Validation("RAID item title must contain at least three characters.");

        var id = Guid.NewGuid();
        const string sql = """
            INSERT INTO project_flowhive_raid_items(
                raid_item_id,project_id,plan_id,item_type,title,description,status,priority,
                probability,impact,owner_user_id,due_date,mitigation,source_kind,
                source_reference,created_by_user_id,updated_by_user_id)
            VALUES(
                @id,@project_id,@plan_id,@item_type,@title,@description,@status,@priority,
                @probability,@impact,@owner,@due_date,@mitigation,@source_kind,
                @source_reference,@actor,@actor)
            RETURNING created_at,updated_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        AddRaidParameters(command, id, projectId, request, access.ActualUserId, title);
        DateTimeOffset createdAt;
        DateTimeOffset updatedAt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            createdAt = reader.GetFieldValue<DateTimeOffset>(0);
            updatedAt = reader.GetFieldValue<DateTimeOffset>(1);
        }
        return Results.Json(new
        {
            status = "flowhive_raid_item_created",
            raidItemId = id,
            createdAt,
            updatedAt,
            stateChanged = true
        }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateRaidAsync(
        Guid projectId,
        Guid raidItemId,
        ProjectFlowHiveRaidRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        var title = Clean(request.Title, 240);
        if (title.Length < 3) return Validation("RAID item title must contain at least three characters.");

        const string sql = """
            UPDATE project_flowhive_raid_items
            SET plan_id=@plan_id,item_type=@item_type,title=@title,description=@description,
                status=@status,priority=@priority,probability=@probability,impact=@impact,
                owner_user_id=@owner,due_date=@due_date,mitigation=@mitigation,
                source_kind=@source_kind,source_reference=@source_reference,
                updated_by_user_id=@actor
            WHERE raid_item_id=@id AND project_id=@project_id
            RETURNING updated_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        AddRaidParameters(command, raidItemId, projectId, request, access.ActualUserId, title);
        var updated = await command.ExecuteScalarAsync(cancellationToken);
        return updated is null
            ? Results.NotFound(new { status = "raid_item_not_found", message = "The RAID item was not found in the selected project." })
            : Results.Ok(new { status = "flowhive_raid_item_updated", raidItemId, updatedAt = updated, stateChanged = true });
    }

    private static async Task<IResult> DeleteRaidAsync(
        Guid projectId,
        Guid raidItemId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        await using var command = new NpgsqlCommand(
            "DELETE FROM project_flowhive_raid_items WHERE raid_item_id=@id AND project_id=@project_id;",
            connection);
        command.Parameters.AddWithValue("id", raidItemId);
        command.Parameters.AddWithValue("project_id", projectId);
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        return count == 0
            ? Results.NotFound(new { status = "raid_item_not_found", message = "The RAID item was not found in the selected project." })
            : Results.Ok(new { status = "flowhive_raid_item_deleted", raidItemId, stateChanged = true });
    }

    private static async Task<IResult> CreateStatusReportAsync(
        Guid projectId,
        ProjectFlowHiveStatusReportRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        var executiveSummary = Clean(request.ExecutiveSummary, 24_000);
        if (executiveSummary.Length < 20)
            return Validation("An executive summary of at least 20 characters is required.");

        var id = Guid.NewGuid();
        const string sql = """
            INSERT INTO project_flowhive_status_reports(
                status_report_id,project_id,plan_id,plan_version_number,status_date,
                period_start,period_end,overall_health,schedule_health,financial_health,
                scope_health,executive_summary,accomplishments,next_steps,decisions_needed,
                key_risks,financial_snapshot,schedule_snapshot,generated_source,
                celar_ai_correlation_id,created_by_user_id)
            VALUES(
                @id,@project_id,@plan_id,@plan_version,@status_date,@period_start,@period_end,
                @overall,@schedule,@financial,@scope,@summary,@accomplishments::jsonb,
                @next_steps::jsonb,@decisions::jsonb,@risks::jsonb,@financial_snapshot::jsonb,
                @schedule_snapshot::jsonb,@source,@correlation,@actor)
            RETURNING created_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("plan_id", NpgsqlDbType.Uuid).Value = request.PlanId.HasValue ? request.PlanId.Value : DBNull.Value;
        command.Parameters.Add("plan_version", NpgsqlDbType.Integer).Value = request.PlanVersionNumber.HasValue ? request.PlanVersionNumber.Value : DBNull.Value;
        command.Parameters.AddWithValue("status_date", request.StatusDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.Add("period_start", NpgsqlDbType.Date).Value = request.PeriodStart.HasValue ? request.PeriodStart.Value : DBNull.Value;
        command.Parameters.Add("period_end", NpgsqlDbType.Date).Value = request.PeriodEnd.HasValue ? request.PeriodEnd.Value : DBNull.Value;
        command.Parameters.AddWithValue("overall", OverallHealth(request.OverallHealth));
        command.Parameters.AddWithValue("schedule", DimensionHealth(request.ScheduleHealth));
        command.Parameters.AddWithValue("financial", DimensionHealth(request.FinancialHealth));
        command.Parameters.AddWithValue("scope", DimensionHealth(request.ScopeHealth));
        command.Parameters.AddWithValue("summary", executiveSummary);
        command.Parameters.AddWithValue("accomplishments", JsonSerializer.Serialize(CleanList(request.Accomplishments, 60, 2000), Json));
        command.Parameters.AddWithValue("next_steps", JsonSerializer.Serialize(CleanList(request.NextSteps, 60, 2000), Json));
        command.Parameters.AddWithValue("decisions", JsonSerializer.Serialize(CleanList(request.DecisionsNeeded, 60, 2000), Json));
        command.Parameters.AddWithValue("risks", JsonSerializer.Serialize(CleanList(request.KeyRisks, 60, 2000), Json));
        command.Parameters.AddWithValue("financial_snapshot", SafeObjectJson(request.FinancialSnapshot));
        command.Parameters.AddWithValue("schedule_snapshot", SafeObjectJson(request.ScheduleSnapshot));
        command.Parameters.AddWithValue("source", GeneratedSource(request.GeneratedSource));
        command.Parameters.AddWithValue("correlation", Clean(request.CelarAiCorrelationId, 180));
        command.Parameters.AddWithValue("actor", access.ActualUserId);
        var createdAt = await command.ExecuteScalarAsync(cancellationToken);

        return Results.Json(new
        {
            status = "flowhive_status_report_created",
            statusReportId = id,
            createdAt,
            immutable = true,
            stateChanged = true
        }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> CreateCustomerShareAsync(
        Guid projectId,
        ProjectFlowHiveCustomerShareRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanShare)
            return Forbidden("The assigned Project Manager does not have customer-sharing permission for this project.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string guardSql = """
            SELECT plan.baseline_version_number,
                   COALESCE(control.customer_sharing_enabled,FALSE),
                   COALESCE(client.client_name,''),
                   project.project_code,
                   project.project_name
            FROM project_flowhive_plans plan
            JOIN projects project ON project.project_id=plan.project_id
            LEFT JOIN clients client ON client.client_id=project.client_id
            LEFT JOIN project_flowhive_project_controls control ON control.project_id=project.project_id
            WHERE plan.plan_id=@plan_id AND plan.project_id=@project_id;
            """;
        int? baselineVersion;
        bool sharingEnabled;
        string customer;
        string projectCode;
        string projectName;
        await using (var guard = new NpgsqlCommand(guardSql, connection, transaction))
        {
            guard.Parameters.AddWithValue("plan_id", request.PlanId);
            guard.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await guard.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Results.NotFound(new { status = "flowhive_plan_not_found", message = "The selected plan does not belong to this project." });
            baselineVersion = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            sharingEnabled = reader.GetBoolean(1);
            customer = reader.GetString(2);
            projectCode = reader.GetString(3);
            projectName = reader.GetString(4);
        }
        if (!sharingEnabled)
            return Results.Json(new
            {
                status = "customer_sharing_not_enabled",
                message = "Enable customer sharing in Project Controls before creating a link.",
                stateChanged = false
            }, statusCode: StatusCodes.Status423Locked);
        if (!baselineVersion.HasValue || baselineVersion.Value != request.VersionNumber)
            return Results.Json(new
            {
                status = "reviewed_baseline_required",
                message = "Customer sharing requires the exact reviewer-approved baseline version.",
                baselineVersion,
                stateChanged = false
            }, statusCode: StatusCodes.Status423Locked);

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Sha256(token);
        var shareId = Guid.NewGuid();
        var expirationDays = Math.Clamp(request.ExpirationDays <= 0 ? 30 : request.ExpirationDays, 1, 90);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(expirationDays);
        var allowedArtifacts = (request.AllowedArtifacts ?? ["view", "pdf"])
            .Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(value => value is "view" or "pdf")
            .Distinct()
            .DefaultIfEmpty("view")
            .ToArray();
        const string insertSql = """
            INSERT INTO project_flowhive_customer_shares(
                share_id,project_id,plan_id,version_number,token_sha256,customer_label,
                share_note,allowed_artifacts,expires_at,created_by_user_id)
            VALUES(@share_id,@project_id,@plan_id,@version,@token_hash,@customer,
                   @note,@artifacts,@expires_at,@actor);
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("share_id", shareId);
            insert.Parameters.AddWithValue("project_id", projectId);
            insert.Parameters.AddWithValue("plan_id", request.PlanId);
            insert.Parameters.AddWithValue("version", request.VersionNumber);
            insert.Parameters.AddWithValue("token_hash", tokenHash);
            insert.Parameters.AddWithValue("customer", Clean(request.CustomerLabel, 240, customer));
            insert.Parameters.AddWithValue("note", Clean(request.ShareNote, 4000));
            insert.Parameters.AddWithValue("artifacts", allowedArtifacts);
            insert.Parameters.AddWithValue("expires_at", expiresAt);
            insert.Parameters.AddWithValue("actor", access.ActualUserId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertAuditAsync(connection, transaction, projectId, request.PlanId, request.VersionNumber,
            "customer_share_created", access,
            new { shareId, expiresAt, allowedArtifacts, customer, projectCode, projectName },
            string.Empty, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var shareUrl = $"{context.Request.Scheme}://{context.Request.Host}/api/project-flowhive/share/{token}";
        return Results.Json(new
        {
            status = "flowhive_customer_share_created",
            share = new
            {
                shareId,
                request.PlanId,
                request.VersionNumber,
                shareUrl,
                expiresAt,
                allowedArtifacts,
                customerLabel = Clean(request.CustomerLabel, 240, customer),
                active = true
            },
            warning = "The full token is returned only in this response. Store or send it through an approved customer communication channel.",
            stateChanged = true
        }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RevokeCustomerShareAsync(
        Guid projectId,
        Guid shareId,
        ProjectFlowHiveCustomerShareRevokeRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanShare) return Forbidden("Customer-sharing permission is required.");

        const string sql = """
            UPDATE project_flowhive_customer_shares
            SET revoked_at=NOW(),revoked_by_user_id=@actor,revocation_reason=@reason
            WHERE share_id=@share_id AND project_id=@project_id AND revoked_at IS NULL
            RETURNING revoked_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actor", access.ActualUserId);
        command.Parameters.AddWithValue("reason", Clean(request.Reason, 500, "Revoked by Project Manager."));
        command.Parameters.AddWithValue("share_id", shareId);
        command.Parameters.AddWithValue("project_id", projectId);
        var revokedAt = await command.ExecuteScalarAsync(cancellationToken);
        return revokedAt is null
            ? Results.NotFound(new { status = "customer_share_not_found", message = "The active customer share was not found." })
            : Results.Ok(new { status = "flowhive_customer_share_revoked", shareId, revokedAt, stateChanged = true });
    }

    private static async Task<IResult> PrepareSowEvidenceAsync(
        Guid projectId,
        Guid documentId,
        ProjectFlowHiveSowEvidencePrepareRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAuthorizedAsync(projectId, context, requireManage: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string loadSql = """
            SELECT COALESCE(document_category,''),COALESCE(pulse_ai_processing_status,''),
                   pulse_ai_active_version_id,COALESCE(original_file_name,'')
            FROM project_intake_documents
            WHERE project_intake_document_id=@document_id AND project_id=@project_id AND is_active=TRUE
            FOR UPDATE;
            """;
        string category;
        string processing;
        Guid? activeVersion;
        string fileName;
        await using (var load = new NpgsqlCommand(loadSql, connection, transaction))
        {
            load.Parameters.AddWithValue("document_id", documentId);
            load.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await load.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Results.NotFound(new { status = "project_document_not_found", message = "The selected document was not found in the project." });
            category = reader.GetString(0);
            processing = reader.GetString(1);
            activeVersion = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            fileName = reader.GetString(3);
        }

        var looksLikeSow = category.Equals("sow", StringComparison.OrdinalIgnoreCase)
            || category.Equals("statement_of_work", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("statement of work", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(^|[^a-z])sow([^a-z]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!looksLikeSow)
            return Validation("Only a document identified as a Statement of Work can be prepared as FlowHive SOW evidence.");

        await using (var normalize = new NpgsqlCommand("""
            UPDATE project_intake_documents
            SET document_category='sow',engineering_visible=TRUE,
                pulse_ai_processing_updated_at=NOW()
            WHERE project_intake_document_id=@document_id;
            """, connection, transaction))
        {
            normalize.Parameters.AddWithValue("document_id", documentId);
            await normalize.ExecuteNonQueryAsync(cancellationToken);
        }

        var queued = false;
        if (!processing.Equals("ready", StringComparison.OrdinalIgnoreCase))
        {
            const string queueSql = """
                INSERT INTO pulse_ai_document_processing_jobs(
                    project_intake_document_id,project_id,actual_user_id,effective_user_id,
                    requested_by_user_id,requested_purpose,correlation_id)
                SELECT @document_id,@project_id,@actor,@effective,@actor,
                       'flowhive_sow_evidence',@correlation
                WHERE NOT EXISTS(
                    SELECT 1 FROM pulse_ai_document_processing_jobs
                    WHERE project_intake_document_id=@document_id
                      AND job_status IN ('queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait','cancel_requested'));
                """;
            await using var queue = new NpgsqlCommand(queueSql, connection, transaction);
            queue.Parameters.AddWithValue("document_id", documentId);
            queue.Parameters.AddWithValue("project_id", projectId);
            queue.Parameters.AddWithValue("actor", access.ActualUserId);
            queue.Parameters.AddWithValue("effective", access.EffectiveUserId);
            queue.Parameters.AddWithValue("correlation", Clean(request.CorrelationId, 160, Guid.NewGuid().ToString("N")));
            queued = await queue.ExecuteNonQueryAsync(cancellationToken) > 0;
            await using var mark = new NpgsqlCommand("""
                UPDATE project_intake_documents
                SET pulse_ai_processing_status=CASE WHEN pulse_ai_processing_status='ready' THEN 'ready' ELSE 'queued' END,
                    pulse_ai_processing_updated_at=NOW()
                WHERE project_intake_document_id=@document_id;
                """, connection, transaction);
            mark.Parameters.AddWithValue("document_id", documentId);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        var approved = false;
        if (request.ApproveCurrentVersion && activeVersion.HasValue && processing.Equals("ready", StringComparison.OrdinalIgnoreCase))
        {
            if (Clean(request.ApprovalNote, 4000).Length < 10)
                return Validation("Enter an approval note of at least 10 characters before approving the current SOW version.");
            await using var approve = new NpgsqlCommand("""
                UPDATE pulse_ai_document_versions
                SET authority_status='approved'
                WHERE pulse_ai_document_version_id=@version
                  AND project_intake_document_id=@document_id
                  AND authority_status='candidate';
                """, connection, transaction);
            approve.Parameters.AddWithValue("version", activeVersion.Value);
            approve.Parameters.AddWithValue("document_id", documentId);
            approved = await approve.ExecuteNonQueryAsync(cancellationToken) > 0;
        }

        await InsertAuditAsync(connection, transaction, projectId, null, null,
            "sow_evidence_prepared", access,
            new { documentId, fileName, queued, approved, request.ApprovalNote },
            request.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new
        {
            status = queued ? "flowhive_sow_processing_queued" : approved ? "flowhive_sow_version_approved" : "flowhive_sow_evidence_metadata_ready",
            documentId,
            queued,
            approved,
            message = queued
                ? "Private document processing was queued. AI Planner will become available after processing, approval, and citation indexing complete."
                : approved
                    ? "The current processed SOW version was approved. Refresh evidence readiness before running AI Planner."
                    : "SOW metadata is aligned. Refresh evidence readiness to see any remaining blocker.",
            stateChanged = true
        });
    }

    private static async Task<IResult> ViewCustomerShareAsync(
        string token,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        token = token?.Trim() ?? string.Empty;
        if (token.Length < 20) return CustomerShareUnavailable();
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0) return CustomerShareUnavailable();
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        try { await connection.OpenAsync(cancellationToken); }
        catch { return CustomerShareUnavailable(); }

        const string sql = """
            SELECT share.share_id,share.project_id,share.plan_id,share.version_number,
                   share.expires_at,share.revoked_at,share.customer_label,share.share_note,
                   share.allowed_artifacts,project.project_code,project.project_name,
                   COALESCE(client.client_name,''),version.plan_payload::text,
                   version.schedule_payload::text,
                   COALESCE((SELECT report.executive_summary
                             FROM project_flowhive_status_reports report
                             WHERE report.project_id=share.project_id
                             ORDER BY report.status_date DESC,report.created_at DESC LIMIT 1),'')
            FROM project_flowhive_customer_shares share
            JOIN projects project ON project.project_id=share.project_id
            LEFT JOIN clients client ON client.client_id=project.client_id
            JOIN project_flowhive_plan_versions version
              ON version.plan_id=share.plan_id AND version.version_number=share.version_number
            WHERE share.token_sha256=@token_hash;
            """;
        Guid shareId;
        Guid projectId;
        string customerLabel;
        string note;
        string projectCode;
        string projectName;
        string customerName;
        string planJson;
        string scheduleJson;
        string executiveSummary;
        DateTimeOffset expiresAt;
        DateTimeOffset? revokedAt;
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("token_hash", Sha256(token));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return CustomerShareUnavailable();
            shareId = reader.GetGuid(0);
            projectId = reader.GetGuid(1);
            expiresAt = reader.GetFieldValue<DateTimeOffset>(4);
            revokedAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5);
            customerLabel = reader.GetString(6);
            note = reader.GetString(7);
            projectCode = reader.GetString(9);
            projectName = reader.GetString(10);
            customerName = reader.GetString(11);
            planJson = reader.GetString(12);
            scheduleJson = reader.GetString(13);
            executiveSummary = reader.GetString(14);
        }
        if (revokedAt.HasValue || expiresAt <= DateTimeOffset.UtcNow) return CustomerShareUnavailable();

        ProjectFlowHivePlanRequest? plan;
        ProjectFlowHiveScheduleResult? schedule;
        try
        {
            plan = JsonSerializer.Deserialize<ProjectFlowHivePlanRequest>(planJson, Json);
            schedule = JsonSerializer.Deserialize<ProjectFlowHiveScheduleResult>(scheduleJson, Json);
        }
        catch { return CustomerShareUnavailable(); }
        if (plan is null || schedule is null) return CustomerShareUnavailable();

        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using var update = new NpgsqlCommand("""
                UPDATE project_flowhive_customer_shares
                SET last_accessed_at=NOW(),access_count=access_count+1
                WHERE share_id=@share_id;
                INSERT INTO project_flowhive_share_access_events(
                    share_id,project_id,event_code,client_fingerprint_sha256,user_agent_sha256)
                VALUES(@share_id,@project_id,'viewed',@client,@agent);
                """, connection, transaction);
            update.Parameters.AddWithValue("share_id", shareId);
            update.Parameters.AddWithValue("project_id", projectId);
            update.Parameters.AddWithValue("client", Sha256(context.Connection.RemoteIpAddress?.ToString() ?? string.Empty));
            update.Parameters.AddWithValue("agent", Sha256(context.Request.Headers.UserAgent.ToString()));
            await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var html = BuildCustomerShareHtml(
            projectCode,
            projectName,
            string.IsNullOrWhiteSpace(customerLabel) ? customerName : customerLabel,
            note,
            executiveSummary,
            expiresAt,
            plan,
            schedule);
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, StatusCodes.Status200OK);
    }

    private static string BuildCustomerShareHtml(
        string projectCode,
        string projectName,
        string customer,
        string note,
        string executiveSummary,
        DateTimeOffset expiresAt,
        ProjectFlowHivePlanRequest plan,
        ProjectFlowHiveScheduleResult schedule)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var scheduled = schedule.Tasks.ToDictionary(task => task.WbsNumber, StringComparer.OrdinalIgnoreCase);
        var rows = new StringBuilder();
        foreach (var task in (plan.Tasks ?? []).Where(task => !task.IsSummary))
        {
            var wbs = task.WbsNumber?.Trim() ?? string.Empty;
            scheduled.TryGetValue(wbs, out var dates);
            rows.Append("<tr>")
                .Append($"<td>{H(wbs)}</td>")
                .Append($"<td><strong>{H(task.Name)}</strong><small>{H(task.Description)}</small></td>")
                .Append($"<td>{H((dates?.StartDate ?? task.EstimatedStartDate)?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture))}</td>")
                .Append($"<td>{H((dates?.EndDate ?? task.EstimatedFinishDate)?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture))}</td>")
                .Append($"<td>{Math.Round(task.PercentComplete, 0, MidpointRounding.AwayFromZero)}%</td>")
                .Append($"<td>{H(task.Status?.Replace('_', ' '))}</td>")
                .Append("</tr>");
        }
        var summary = string.IsNullOrWhiteSpace(executiveSummary)
            ? "This reviewed project baseline presents the current authorized schedule and delivery status."
            : executiveSummary;
        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{H(projectCode)} Project Status</title>
            <style>
            :root{{--navy:#082b4c;--blue:#057aa8;--ink:#17324a;--muted:#617286;--line:#d8e2ea;--soft:#f3f8fb}}
            *{{box-sizing:border-box}}body{{margin:0;font-family:Inter,Segoe UI,Arial,sans-serif;color:var(--ink);background:var(--soft)}}
            main{{max-width:1180px;margin:32px auto;padding:0 20px}}header{{padding:28px;border-radius:18px;color:#fff;background:linear-gradient(135deg,#061d35,#0b5276)}}
            .brand{{font-weight:900;letter-spacing:.08em;text-transform:uppercase;color:#82ddf6}}h1{{margin:.35rem 0 .25rem}}.meta{{display:flex;gap:18px;flex-wrap:wrap;color:#d9edf7}}
            section{{margin-top:18px;padding:22px;border:1px solid var(--line);border-radius:16px;background:#fff;box-shadow:0 8px 24px rgba(7,35,59,.07)}}
            h2{{margin-top:0;color:var(--navy)}}p{{line-height:1.55}}table{{width:100%;border-collapse:collapse;font-size:14px}}th{{text-align:left;background:var(--navy);color:#fff;padding:11px}}td{{padding:11px;border-bottom:1px solid var(--line);vertical-align:top}}td small{{display:block;margin-top:4px;color:var(--muted);line-height:1.4}}footer{{padding:20px 0;color:var(--muted);font-size:12px}}@media(max-width:760px){{table{{display:block;overflow:auto}}}}
            </style></head><body><main>
            <header><div class="brand">US Signal Project FlowHive</div><h1>{H(projectCode)} · {H(projectName)}</h1><div class="meta"><span>Customer: {H(customer)}</span><span>Reviewed baseline version {plan.PlanId}</span><span>Link expires {expiresAt:MMM d, yyyy}</span></div></header>
            <section><h2>Executive summary</h2><p>{H(summary)}</p>{(string.IsNullOrWhiteSpace(note) ? string.Empty : $"<p><strong>Project Manager note:</strong> {H(note)}</p>")}</section>
            <section><h2>Reviewed schedule</h2><p>{H(schedule.ProjectStartDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture))} through {H(schedule.ProjectFinishDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture))} · {schedule.CriticalTaskCount} critical task(s)</p>
            <table><thead><tr><th>WBS</th><th>Task</th><th>Start</th><th>Finish</th><th>Progress</th><th>Status</th></tr></thead><tbody>{rows}</tbody></table></section>
            <footer>Customer-safe, read-only Project FlowHive view. Internal notes, private citations, assignments, financial details, and provider data are not included.</footer>
            </main></body></html>
            """;
    }

    private static async Task<OpenOutcome> OpenAuthorizedAsync(
        Guid projectId,
        HttpContext context,
        bool requireManage,
        CancellationToken cancellationToken)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId") ?? actual;
        if (!actual.HasValue || !effective.HasValue)
            return OpenOutcome.Fail(Results.Json(new { status = "session_required", message = "A valid ProjectPulse session is required." }, statusCode: 401));

        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
            return OpenOutcome.Fail(Results.Json(new { status = "configuration_missing", message = "Project FlowHive database configuration is incomplete." }, statusCode: 503));
        var connection = new NpgsqlConnection(config.ConnectionString);
        try { await connection.OpenAsync(cancellationToken); }
        catch
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Results.Json(new { status = "persistence_dependency_unavailable", message = "Project FlowHive persistence is temporarily unavailable." }, statusCode: 503));
        }

        if (!await EnterpriseSchemaReadyAsync(connection, cancellationToken))
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Results.Json(new
            {
                status = "migration_086_required",
                message = "Project FlowHive enterprise persistence requires Migration 086.",
                stateChanged = false
            }, statusCode: 503));
        }

        var access = await LoadAccessAsync(connection, context, projectId, actual.Value, effective.Value, cancellationToken);
        if (access is null || !access.CanView)
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Forbidden("The project is outside the current FlowHive scope."));
        }
        if (requireManage && !access.CanManage)
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Forbidden("Only the assigned Project Manager can manage this project's FlowHive working plan. View-As is read-only."));
        }
        return new OpenOutcome(connection, access, null);
    }

    private static async Task<bool> EnterpriseSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration)
               AND to_regclass('public.project_flowhive_working_copies') IS NOT NULL
               AND to_regclass('public.project_flowhive_project_controls') IS NOT NULL
               AND to_regclass('public.project_flowhive_raid_items') IS NOT NULL
               AND to_regclass('public.project_flowhive_status_reports') IS NOT NULL
               AND to_regclass('public.project_flowhive_customer_shares') IS NOT NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migration", MigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<ProjectFlowHiveEnterpriseAccess?> LoadAccessAsync(
        NpgsqlConnection connection,
        HttpContext context,
        Guid projectId,
        Guid actualUserId,
        Guid effectiveUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT project.project_id,project.project_code,project.project_name,
                   COALESCE(client.client_name,''),project.project_manager_user_id,
                   COALESCE(NULLIF(manager.display_name,''),manager.email,'Unassigned'),
                   COALESCE(NULLIF(actor.display_name,''),actor.email,''),
                   EXISTS(SELECT 1 FROM project_assignments assignment
                          WHERE assignment.project_id=project.project_id AND assignment.user_id=@effective),
                   EXISTS(SELECT 1 FROM app_user_role_assignments assignment
                          JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                          WHERE assignment.user_id=@effective AND assignment.is_active=TRUE
                            AND role.role_code IN ('SUPER_ADMINISTRATOR','SYSTEM_ADMINISTRATOR','ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','PROJECT_COORDINATOR','PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD','EXECUTIVE')),
                   EXISTS(SELECT 1 FROM app_user_role_assignments assignment
                          JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                          WHERE assignment.user_id=@actual AND assignment.is_active=TRUE
                            AND role.role_code IN ('SUPER_ADMINISTRATOR','SYSTEM_ADMINISTRATOR','ADMINISTRATOR')),
                   EXISTS(SELECT 1 FROM app_user_role_assignments assignment
                          JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                          JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id
                          JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
                          WHERE assignment.user_id=@effective AND assignment.is_active=TRUE
                            AND permission.permission_code='MANAGE_FLOWHIVE_PM_WORKSPACE_066'),
                   EXISTS(SELECT 1 FROM app_user_role_assignments assignment
                          JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                          JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id
                          JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
                          WHERE assignment.user_id=@effective AND assignment.is_active=TRUE
                            AND permission.permission_code='CREATE_FLOWHIVE_CUSTOMER_SHARE_066'),
                   EXISTS(SELECT 1 FROM app_user_role_assignments assignment
                          JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                          JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id
                          JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
                          WHERE assignment.user_id=@effective AND assignment.is_active=TRUE
                            AND permission.permission_code='VIEW_FLOWHIVE_FINANCIALS_066')
            FROM projects project
            LEFT JOIN clients client ON client.client_id=project.client_id
            LEFT JOIN app_users manager ON manager.user_id=project.project_manager_user_id
            JOIN app_users actor ON actor.user_id=@effective AND actor.is_active=TRUE
            WHERE project.project_id=@project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("actual", actualUserId);
        command.Parameters.AddWithValue("effective", effectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var managerId = reader.IsDBNull(4) ? null : reader.GetGuid(4);
        var owner = managerId.HasValue && managerId.Value == effectiveUserId;
        var assigned = reader.GetBoolean(7);
        var broad = reader.GetBoolean(8);
        var administrator = reader.GetBoolean(9)
            || (context.Items.TryGetValue("ProjectPulsePermanentFullControl", out var permanent) && permanent is true);
        var hasManage = reader.GetBoolean(10);
        var hasShare = reader.GetBoolean(11);
        var hasFinancial = reader.GetBoolean(12);
        var viewAs = ProjectPulseActualSessionAuthority.IsViewAs(context) || actualUserId != effectiveUserId;
        var ownSession = !viewAs && actualUserId == effectiveUserId;
        var canView = owner || assigned || broad || administrator;
        var canManage = ownSession && ((owner && hasManage) || administrator);
        var canShare = ownSession && ((owner && hasShare) || administrator);
        return new ProjectFlowHiveEnterpriseAccess(
            actualUserId,effectiveUserId,reader.GetString(6),viewAs,reader.GetGuid(0),reader.GetString(1),
            reader.GetString(2),reader.GetString(3),managerId,reader.GetString(5),owner,administrator,
            canView,canManage,canShare,canView && (hasFinancial || owner || administrator));
    }

    private static async Task<object?> LoadWorkingCopyAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT plan_id,working_payload::text,working_revision,row_version,updated_by_user_id,created_at,updated_at
            FROM project_flowhive_working_copies WHERE project_id=@project_id;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new
        {
            planId = reader.IsDBNull(0) ? null : reader.GetGuid(0),
            plan = ParseJson(reader.GetString(1)),
            workingRevision = reader.GetInt32(2),
            rowVersion = reader.GetGuid(3),
            updatedByUserId = reader.GetGuid(4),
            createdAt = reader.GetFieldValue<DateTimeOffset>(5),
            updatedAt = reader.GetFieldValue<DateTimeOffset>(6)
        };
    }

    private static async Task<object> LoadControlsAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT contract_type,currency_code,approved_budget,expense_budget,contingency_budget,
                   forecast_at_completion,percent_complete_method,status_report_cadence,
                   customer_sharing_enabled,financial_notes,updated_at
            FROM project_flowhive_project_controls WHERE project_id=@project_id;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new
            {
                projectId,
                contractType = "unknown",
                currencyCode = "USD",
                approvedBudget = (decimal?)null,
                expenseBudget = (decimal?)null,
                contingencyBudget = (decimal?)null,
                forecastAtCompletion = (decimal?)null,
                percentCompleteMethod = "task_weighted",
                statusReportCadence = "weekly",
                customerSharingEnabled = false,
                financialNotes = string.Empty,
                updatedAt = (DateTimeOffset?)null
            };
        }
        return new
        {
            projectId,
            contractType = reader.GetString(0),
            currencyCode = reader.GetString(1),
            approvedBudget = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            expenseBudget = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            contingencyBudget = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
            forecastAtCompletion = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            percentCompleteMethod = reader.GetString(6),
            statusReportCadence = reader.GetString(7),
            customerSharingEnabled = reader.GetBoolean(8),
            financialNotes = reader.GetString(9),
            updatedAt = reader.GetFieldValue<DateTimeOffset>(10)
        };
    }

    private static async Task<IReadOnlyList<object>> LoadRaidAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT raid.raid_item_id,raid.plan_id,raid.item_type,raid.title,raid.description,
                   raid.status,raid.priority,raid.probability,raid.impact,raid.owner_user_id,
                   COALESCE(NULLIF(owner.display_name,''),owner.email,''),raid.due_date,
                   raid.mitigation,raid.source_kind,raid.source_reference,raid.created_at,raid.updated_at
            FROM project_flowhive_raid_items raid
            LEFT JOIN app_users owner ON owner.user_id=raid.owner_user_id
            WHERE raid.project_id=@project_id
            ORDER BY CASE raid.priority WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                     raid.due_date NULLS LAST,raid.created_at DESC;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                raidItemId = reader.GetGuid(0),
                planId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                itemType = reader.GetString(2),
                title = reader.GetString(3),
                description = reader.GetString(4),
                status = reader.GetString(5),
                priority = reader.GetString(6),
                probability = reader.IsDBNull(7) ? null : reader.GetInt16(7),
                impact = reader.IsDBNull(8) ? null : reader.GetInt16(8),
                ownerUserId = reader.IsDBNull(9) ? null : reader.GetGuid(9),
                ownerName = reader.GetString(10),
                dueDate = reader.IsDBNull(11) ? null : ReadDate(reader, 11),
                mitigation = reader.GetString(12),
                sourceKind = reader.GetString(13),
                sourceReference = reader.GetString(14),
                createdAt = reader.GetFieldValue<DateTimeOffset>(15),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(16)
            });
        }
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadStatusReportsAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT status_report_id,plan_id,plan_version_number,status_date,period_start,period_end,
                   overall_health,schedule_health,financial_health,scope_health,executive_summary,
                   accomplishments::text,next_steps::text,decisions_needed::text,key_risks::text,
                   financial_snapshot::text,schedule_snapshot::text,generated_source,
                   celar_ai_correlation_id,created_by_user_id,created_at
            FROM project_flowhive_status_reports
            WHERE project_id=@project_id
            ORDER BY status_date DESC,created_at DESC LIMIT 100;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                statusReportId = reader.GetGuid(0),
                planId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                planVersionNumber = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                statusDate = ReadDate(reader, 3),
                periodStart = reader.IsDBNull(4) ? null : ReadDate(reader, 4),
                periodEnd = reader.IsDBNull(5) ? null : ReadDate(reader, 5),
                overallHealth = reader.GetString(6),
                scheduleHealth = reader.GetString(7),
                financialHealth = reader.GetString(8),
                scopeHealth = reader.GetString(9),
                executiveSummary = reader.GetString(10),
                accomplishments = ParseJson(reader.GetString(11)),
                nextSteps = ParseJson(reader.GetString(12)),
                decisionsNeeded = ParseJson(reader.GetString(13)),
                keyRisks = ParseJson(reader.GetString(14)),
                financialSnapshot = ParseJson(reader.GetString(15)),
                scheduleSnapshot = ParseJson(reader.GetString(16)),
                generatedSource = reader.GetString(17),
                celarAiCorrelationId = reader.GetString(18),
                createdByUserId = reader.GetGuid(19),
                createdAt = reader.GetFieldValue<DateTimeOffset>(20)
            });
        }
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadSharesAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT share_id,plan_id,version_number,customer_label,share_note,allowed_artifacts,
                   expires_at,revoked_at,revocation_reason,last_accessed_at,access_count,created_at
            FROM project_flowhive_customer_shares
            WHERE project_id=@project_id ORDER BY created_at DESC LIMIT 100;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var expiresAt = reader.GetFieldValue<DateTimeOffset>(6);
            var revokedAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7);
            rows.Add(new
            {
                shareId = reader.GetGuid(0),
                planId = reader.GetGuid(1),
                versionNumber = reader.GetInt32(2),
                customerLabel = reader.GetString(3),
                shareNote = reader.GetString(4),
                allowedArtifacts = reader.GetFieldValue<string[]>(5),
                expiresAt,
                revokedAt,
                revocationReason = reader.GetString(8),
                lastAccessedAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                accessCount = reader.GetInt32(10),
                createdAt = reader.GetFieldValue<DateTimeOffset>(11),
                active = !revokedAt.HasValue && expiresAt > DateTimeOffset.UtcNow
            });
        }
        return rows;
    }

    private static async Task<List<ProjectFlowHiveSowEvidenceState>> LoadSowEvidenceAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectFlowHiveSowEvidenceState>();
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT document.project_intake_document_id,COALESCE(document.original_file_name,''),
                       COALESCE(document.document_category,''),COALESCE(document.pulse_ai_processing_status,''),
                       COALESCE(document.engineering_visible,FALSE),document.pulse_ai_active_version_id,
                       COALESCE(version.authority_status,''),COALESCE(version.index_status,''),
                       COUNT(chunk.chunk_id) FILTER(WHERE chunk.is_active=TRUE AND chunk.index_status IN ('lexical_ready','embedding_ready','ready'))::int,
                       COUNT(chunk.chunk_id) FILTER(WHERE chunk.is_active=TRUE AND chunk.index_status IN ('lexical_ready','embedding_ready','ready')
                           AND (chunk.section_title ILIKE '%scope%' OR chunk.section_title ILIKE '%service%'
                                OR chunk.citation_anchor ILIKE '%scope%' OR chunk.citation_anchor ILIKE '%service%'))::int,
                       COALESCE(version.document_version,'')
                FROM project_intake_documents document
                LEFT JOIN pulse_ai_document_versions version
                  ON version.pulse_ai_document_version_id=document.pulse_ai_active_version_id
                LEFT JOIN pulse_ai_document_chunks chunk
                  ON chunk.pulse_ai_document_version_id=version.pulse_ai_document_version_id
                WHERE document.project_id=@project_id AND document.is_active=TRUE
                  AND (LOWER(COALESCE(document.document_category,'')) IN ('sow','statement_of_work','gsd','global_solution_design')
                       OR document.original_file_name ILIKE '%statement%of%work%'
                       OR document.original_file_name ~* '(^|[^a-z])sow([^a-z]|$)')
                GROUP BY document.project_intake_document_id,document.original_file_name,document.document_category,
                         document.pulse_ai_processing_status,document.engineering_visible,
                         document.pulse_ai_active_version_id,version.authority_status,version.index_status,version.document_version
                ORDER BY CASE WHEN LOWER(COALESCE(document.document_category,'')) IN ('sow','statement_of_work') THEN 0 ELSE 1 END,
                         document.original_file_name;
                """, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var documentId = reader.GetGuid(0);
                var file = reader.GetString(1);
                var category = reader.GetString(2);
                var processing = reader.GetString(3);
                var visible = reader.GetBoolean(4);
                var activeVersion = reader.IsDBNull(5) ? null : reader.GetGuid(5);
                var authority = reader.GetString(6);
                var index = reader.GetString(7);
                var citations = reader.GetInt32(8);
                var scopeCitations = reader.GetInt32(9);
                var version = reader.GetString(10);
                var isSow = category.Equals("sow", StringComparison.OrdinalIgnoreCase)
                    || category.Equals("statement_of_work", StringComparison.OrdinalIgnoreCase);
                var blockers = new List<string>();
                if (!isSow) blockers.Add("Document category must be SOW or Statement of Work.");
                if (!visible) blockers.Add("Engineering visibility is disabled.");
                if (!processing.Equals("ready", StringComparison.OrdinalIgnoreCase)) blockers.Add($"Private processing is {processing}.");
                if (!activeVersion.HasValue) blockers.Add("No active private document version exists.");
                if (authority is not ("approved" or "canonical")) blockers.Add("The active version is not approved or canonical.");
                if (index is not ("lexical_ready" or "embedding_ready" or "ready")) blockers.Add("The active version is not citation indexed.");
                if (citations == 0) blockers.Add("No citation-ready chunks are available.");
                if (scopeCitations == 0) blockers.Add("No Scope of Services citation was located.");
                rows.Add(new ProjectFlowHiveSowEvidenceState(
                    documentId,file,category,processing,visible,activeVersion,authority,index,version,
                    citations,scopeCitations,blockers.Count == 0,blockers));
            }
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return [];
        }
        return rows;
    }

    private static void AddRaidParameters(
        NpgsqlCommand command,
        Guid id,
        Guid projectId,
        ProjectFlowHiveRaidRequest request,
        Guid actor,
        string title)
    {
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("plan_id", NpgsqlDbType.Uuid).Value = request.PlanId.HasValue ? request.PlanId.Value : DBNull.Value;
        command.Parameters.AddWithValue("item_type", RaidType(request.ItemType));
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("description", Clean(request.Description, 12_000));
        command.Parameters.AddWithValue("status", RaidStatus(request.Status));
        command.Parameters.AddWithValue("priority", Priority(request.Priority));
        command.Parameters.Add("probability", NpgsqlDbType.Smallint).Value = request.Probability.HasValue ? Math.Clamp(request.Probability.Value, 1, 5) : DBNull.Value;
        command.Parameters.Add("impact", NpgsqlDbType.Smallint).Value = request.Impact.HasValue ? Math.Clamp(request.Impact.Value, 1, 5) : DBNull.Value;
        command.Parameters.Add("owner", NpgsqlDbType.Uuid).Value = request.OwnerUserId.HasValue ? request.OwnerUserId.Value : DBNull.Value;
        command.Parameters.Add("due_date", NpgsqlDbType.Date).Value = request.DueDate.HasValue ? request.DueDate.Value : DBNull.Value;
        command.Parameters.AddWithValue("mitigation", Clean(request.Mitigation, 12_000));
        command.Parameters.AddWithValue("source_kind", SourceKind(request.SourceKind));
        command.Parameters.AddWithValue("source_reference", Clean(request.SourceReference, 240));
        command.Parameters.AddWithValue("actor", actor);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid? planId,
        int? version,
        string eventCode,
        ProjectFlowHiveEnterpriseAccess access,
        object metadata,
        string? correlation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_flowhive_audit_events(
                project_id,plan_id,version_number,event_code,actual_actor_user_id,
                effective_actor_user_id,event_metadata,correlation_id)
            VALUES(@project_id,@plan_id,@version,@event,@actual,@effective,@metadata::jsonb,@correlation);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("plan_id", NpgsqlDbType.Uuid).Value = planId.HasValue ? planId.Value : DBNull.Value;
        command.Parameters.Add("version", NpgsqlDbType.Integer).Value = version.HasValue ? version.Value : DBNull.Value;
        command.Parameters.AddWithValue("event", eventCode);
        command.Parameters.AddWithValue("actual", access.ActualUserId);
        command.Parameters.AddWithValue("effective", access.EffectiveUserId);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata, Json));
        command.Parameters.AddWithValue("correlation", Clean(correlation, 180));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableMoney(NpgsqlCommand command, string name, decimal? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Numeric).Value = value.HasValue ? Math.Max(0, value.Value) : DBNull.Value;
    }

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static string SafeObjectJson(JsonElement? element)
    {
        return element.HasValue && element.Value.ValueKind == JsonValueKind.Object
            ? element.Value.GetRawText()
            : "{}";
    }

    private static string[] CleanList(IReadOnlyList<string>? values, int maximumItems, int maximumLength) =>
        (values ?? [])
            .Select(value => Clean(value, maximumLength))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .ToArray();

    private static string Clean(string? value, int maximumLength, string fallback = "")
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string ContractType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "fixed_price" => "fixed_price",
        "time_and_materials" or "t&m" or "tm" => "time_and_materials",
        "hybrid" => "hybrid",
        "internal" => "internal",
        "not_billable" or "non_billable" => "not_billable",
        _ => "unknown"
    };

    private static string Currency(string? value)
    {
        var clean = Clean(value, 3, "USD").ToUpperInvariant();
        return clean.Length == 3 && clean.All(char.IsLetter) ? clean : "USD";
    }

    private static string PercentMethod(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "effort_weighted" => "effort_weighted",
        "manual" => "manual",
        "earned_value" => "earned_value",
        _ => "task_weighted"
    };

    private static string StatusCadence(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "biweekly" => "biweekly",
        "monthly" => "monthly",
        "milestone" => "milestone",
        "manual" => "manual",
        _ => "weekly"
    };

    private static string RaidType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "issue" => "issue",
        "action" => "action",
        "decision" => "decision",
        "assumption" => "assumption",
        "dependency" => "dependency",
        "change" => "change",
        _ => "risk"
    };

    private static string RaidStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "monitoring" => "monitoring",
        "blocked" => "blocked",
        "in_progress" => "in_progress",
        "accepted" => "accepted",
        "mitigated" => "mitigated",
        "resolved" => "resolved",
        "closed" => "closed",
        "deferred" => "deferred",
        "rejected" => "rejected",
        _ => "open"
    };

    private static string Priority(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => "low",
        "high" => "high",
        "critical" => "critical",
        _ => "medium"
    };

    private static string SourceKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "celar_ai" => "celar_ai",
        "plan" => "plan",
        "financial" => "financial",
        "customer" => "customer",
        "engineering" => "engineering",
        _ => "manual"
    };

    private static string OverallHealth(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "amber" => "amber",
        "red" => "red",
        "complete" => "complete",
        "not_started" => "not_started",
        _ => "green"
    };

    private static string DimensionHealth(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "amber" => "amber",
        "red" => "red",
        "unknown" => "unknown",
        _ => "green"
    };

    private static string GeneratedSource(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "celar_ai" => "celar_ai",
        "pm_edited" => "pm_edited",
        _ => "deterministic"
    };

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DateOnly ReadDate(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? string.Empty, CultureInfo.InvariantCulture)
        };
    }

    private static IResult Validation(string message) => Results.BadRequest(new { status = "validation_failed", message, stateChanged = false });
    private static IResult Forbidden(string message) => Results.Json(new { status = "forbidden", message, stateChanged = false }, statusCode: 403);
    private static IResult CustomerShareUnavailable() => Results.Content(
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Project link unavailable</title></head><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:3rem\"><h1>Project link unavailable</h1><p>This Project FlowHive link is invalid, expired, or revoked. Contact the Project Manager for a current reviewed link.</p></body></html>",
        "text/html; charset=utf-8", Encoding.UTF8, StatusCodes.Status404NotFound);

    private sealed record OpenOutcome(NpgsqlConnection? Connection, ProjectFlowHiveEnterpriseAccess? Access, IResult? Error)
    {
        public static OpenOutcome Fail(IResult error) => new(null, null, error);
    }
}

public sealed record ProjectFlowHiveWorkingCopyRequest(ProjectFlowHivePlanRequest? Plan, Guid? ExpectedRowVersion);

public sealed record ProjectFlowHiveProjectControlsRequest(
    string? ContractType,
    string? CurrencyCode,
    decimal? ApprovedBudget,
    decimal? ExpenseBudget,
    decimal? ContingencyBudget,
    decimal? ForecastAtCompletion,
    string? PercentCompleteMethod,
    string? StatusReportCadence,
    bool CustomerSharingEnabled,
    string? FinancialNotes);

public sealed record ProjectFlowHiveRaidRequest(
    Guid? PlanId,
    string? ItemType,
    string? Title,
    string? Description,
    string? Status,
    string? Priority,
    short? Probability,
    short? Impact,
    Guid? OwnerUserId,
    DateOnly? DueDate,
    string? Mitigation,
    string? SourceKind,
    string? SourceReference);

public sealed record ProjectFlowHiveStatusReportRequest(
    Guid? PlanId,
    int? PlanVersionNumber,
    DateOnly? StatusDate,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string? OverallHealth,
    string? ScheduleHealth,
    string? FinancialHealth,
    string? ScopeHealth,
    string? ExecutiveSummary,
    IReadOnlyList<string>? Accomplishments,
    IReadOnlyList<string>? NextSteps,
    IReadOnlyList<string>? DecisionsNeeded,
    IReadOnlyList<string>? KeyRisks,
    JsonElement? FinancialSnapshot,
    JsonElement? ScheduleSnapshot,
    string? GeneratedSource,
    string? CelarAiCorrelationId);

public sealed record ProjectFlowHiveCustomerShareRequest(
    Guid PlanId,
    int VersionNumber,
    int ExpirationDays,
    string? CustomerLabel,
    string? ShareNote,
    IReadOnlyList<string>? AllowedArtifacts);

public sealed record ProjectFlowHiveCustomerShareRevokeRequest(string? Reason);

public sealed record ProjectFlowHiveSowEvidencePrepareRequest(
    bool ApproveCurrentVersion,
    string? ApprovalNote,
    string? CorrelationId);

internal sealed record ProjectFlowHiveEnterpriseAccess(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string DisplayName,
    bool IsViewAs,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    Guid? ProjectManagerUserId,
    string ProjectManagerName,
    bool IsProjectManagerOwner,
    bool IsAdministrator,
    bool CanView,
    bool CanManage,
    bool CanShare,
    bool CanViewFinancials);

internal sealed record ProjectFlowHiveSowEvidenceState(
    Guid DocumentId,
    string OriginalFileName,
    string DocumentCategory,
    string ProcessingStatus,
    bool EngineeringVisible,
    Guid? ActiveVersionId,
    string AuthorityStatus,
    string IndexStatus,
    string DocumentVersion,
    int CitationCount,
    int ScopeCitationCount,
    bool ReadyForAiPlanner,
    IReadOnlyList<string> Blockers);

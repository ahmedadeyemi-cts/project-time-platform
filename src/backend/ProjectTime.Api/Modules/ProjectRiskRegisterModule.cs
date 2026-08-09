using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class ProjectRiskRegisterModule
{
    private const string Module = "082";
    private const string ContractVersion = "082-enterprise-v1";
    private const string Migration = "077_module_082_enterprise_project_risk_register";
    private static readonly string[] RiskStatuses = ["proposed","open","monitoring","response_in_progress","accepted","realized","closed","retired"];
    private static readonly string[] Strategies = ["avoid","mitigate","transfer","accept","escalate","exploit","enhance","share"];

    public static IEndpointRouteBuilder MapProjectRiskRegisterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/project-risk-register");
        group.MapGet("/capabilities", Capabilities);
        group.MapGet("/access", GetAccessAsync);
        group.MapGet("/summary", GetSummaryAsync);
        group.MapGet("/projects", ListProjectsAsync);
        group.MapGet("/directory/users", ListProjectUsersAsync);
        group.MapGet("/risks", ListRisksAsync);
        group.MapPost("/risks", CreateRiskAsync);
        group.MapPut("/risks/{riskId:guid}", UpdateRiskAsync);
        group.MapPost("/risks/{riskId:guid}/close", CloseRiskAsync);
        group.MapPost("/risks/{riskId:guid}/realize", RealizeRiskAsync);
        group.MapGet("/heatmap", GetHeatmapAsync);
        group.MapGet("/actions", ListActionsAsync);
        group.MapPost("/risks/{riskId:guid}/actions", CreateActionAsync);
        group.MapPut("/actions/{actionId:guid}", UpdateActionAsync);
        group.MapGet("/review-calendar", GetReviewCalendarAsync);
        group.MapGet("/history", ListHistoryAsync);
        group.MapGet("/exports/{format}", ExportAsync);
        return endpoints;
    }

    private static IResult Capabilities() => Results.Ok(new
    {
        module = Module, contractVersion = ContractVersion, route = "project-risk-register",
        views = new[] { "my-project-risks", "project-register", "heatmap", "actions", "portfolio", "review-calendar", "history-audit" },
        controls = new[] { "authoritative-project-scope", "cross-pm-denial", "view-as-read-only", "active-owner-validation", "immutable-versions", "closed-risk-immutability", "residual-exposure", "formula-neutralization" },
        ratingScale = new { low = "1–4", moderate = "5–9", high = "10–16", critical = "17–25" },
        exportFormats = new[] { "xlsx", "pdf" }
    });

    private static async Task<IResult> GetAccessAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await RequireAccessAsync(context, connection, false);
            if (authorization.Error is not null) return authorization.Error;
            var dataReady = await RuntimeReadyAsync(connection, context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                scope = Scope(authorization.Value!),
                permissions = Permissions(authorization.Value!),
                dataReady,
                status = dataReady ? "ready" : "migration_required",
                migration = Migration,
                message = dataReady
                    ? "Module 082 access and data foundations are ready."
                    : "Module 082 data foundations are not ready. Migration 077 must be applied and verified before records can be changed.",
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "resolve access"); }
    }

    private static async Task<IResult> GetSummaryAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            var sql = ScopedProjectsCte + """
                SELECT
                  COUNT(*) FILTER(WHERE risk.risk_status NOT IN ('closed','retired'))::bigint,
                  COUNT(*) FILTER(WHERE risk.risk_status NOT IN ('closed','retired') AND risk.inherent_exposure>=17)::bigint,
                  COUNT(*) FILTER(WHERE risk.risk_status NOT IN ('closed','retired') AND risk.inherent_exposure BETWEEN 10 AND 16)::bigint,
                  COUNT(*) FILTER(WHERE risk.risk_status NOT IN ('closed','retired') AND risk.next_review_date<CURRENT_DATE)::bigint,
                  COUNT(DISTINCT risk.project_id) FILTER(WHERE risk.risk_status NOT IN ('closed','retired'))::bigint,
                  COALESCE((SELECT COUNT(*) FROM project_risk_actions action
                    JOIN project_risks action_risk ON action_risk.risk_id=action.risk_id
                    JOIN scoped_projects action_project ON action_project.project_id=action_risk.project_id
                    WHERE action.action_status NOT IN ('completed','cancelled') AND action.due_date<CURRENT_DATE),0)::bigint,
                  COALESCE((SELECT COUNT(*) FROM project_risk_actions action
                    JOIN project_risks action_risk ON action_risk.risk_id=action.risk_id
                    JOIN scoped_projects action_project ON action_project.project_id=action_risk.project_id
                    WHERE action.owner_user_id=@user_id AND action.action_status NOT IN ('completed','cancelled') AND action.due_date<CURRENT_DATE),0)::bigint
                FROM project_risks risk
                JOIN scoped_projects project ON project.project_id=risk.project_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, authorization.Value!);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted); await reader.ReadAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = Module, contractVersion = ContractVersion, scope = Scope(authorization.Value!), permissions = Permissions(authorization.Value!),
                kpis = new { open = reader.GetInt64(0), critical = reader.GetInt64(1), high = reader.GetInt64(2), overdueReviews = reader.GetInt64(3), projects = reader.GetInt64(4), overdueActions = reader.GetInt64(5), myOverdueActions = reader.GetInt64(6) },
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load summary"); }
    }

    private static async Task<IResult> ListProjectsAsync(HttpContext context, string? search = null)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            var sql = ScopedProjectsCte + """
                SELECT project.project_id,project.project_code,project.project_name,COALESCE(client.client_name,''),project.status,
                  COALESCE(manager.display_name,manager.email,'Unassigned'),project.start_date,project.end_date,
                  COUNT(risk.risk_id) FILTER(WHERE risk.risk_status NOT IN ('closed','retired'))::bigint,
                  COUNT(risk.risk_id) FILTER(WHERE risk.risk_status NOT IN ('closed','retired') AND risk.inherent_exposure>=10)::bigint
                FROM scoped_projects project
                LEFT JOIN clients client ON client.client_id=project.client_id
                LEFT JOIN app_users manager ON manager.user_id=project.project_manager_user_id
                LEFT JOIN project_risks risk ON risk.project_id=project.project_id
                WHERE @search='' OR project.project_code ILIKE '%'||@search||'%' OR project.project_name ILIKE '%'||@search||'%' OR client.client_name ILIKE '%'||@search||'%'
                GROUP BY project.project_id,project.project_code,project.project_name,client.client_name,project.status,manager.display_name,manager.email,project.start_date,project.end_date
                ORDER BY project.project_name LIMIT 500;
                """;
            await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, authorization.Value!); command.Parameters.AddWithValue("search", Clean(search, 120));
            var rows = new List<object>(); await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted)) rows.Add(new { projectId = reader.GetGuid(0), projectCode = reader.GetString(1), projectName = reader.GetString(2), customer = reader.GetString(3), status = reader.GetString(4), projectManager = reader.GetString(5), startDate = Date(reader, 6), endDate = Date(reader, 7), openRisks = reader.GetInt64(8), highCritical = reader.GetInt64(9) });
            return Results.Ok(new { module = Module, scope = Scope(authorization.Value!), projects = rows });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list projects"); }
    }

    private static async Task<IResult> ListProjectUsersAsync(HttpContext context, Guid projectId)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            if (await LoadProjectAsync(connection, authorization.Value!, projectId, context.RequestAborted) is null)
                return EnterpriseGovernanceResults.Forbidden(Module, "The selected project is outside your authoritative project scope.");
            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT user_record.user_id,COALESCE(user_record.display_name,user_record.email,''),user_record.email
                FROM app_users user_record
                WHERE user_record.is_active=TRUE AND (
                  EXISTS(SELECT 1 FROM projects project WHERE project.project_id=@project_id AND project.project_manager_user_id=user_record.user_id)
                  OR EXISTS(SELECT 1 FROM project_assignments assignment WHERE assignment.project_id=@project_id AND assignment.user_id=user_record.user_id)
                  OR @broad_scope=TRUE
                )
                ORDER BY COALESCE(user_record.display_name,user_record.email,''),user_record.email LIMIT 1000;
                """, connection);
            EnterpriseGovernanceAccessResolver.AddScopeParameters(command, authorization.Value!); command.Parameters.AddWithValue("project_id", projectId);
            var users = new List<object>(); await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted)) users.Add(new { userId = reader.GetGuid(0), displayName = reader.GetString(1), email = reader.GetString(2) });
            return Results.Ok(new { module = Module, projectId, users });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list project users"); }
    }

    private static async Task<IResult> ListRisksAsync(HttpContext context, Guid? projectId = null, string? search = null, string? owner = null, string? category = null, string? status = null, string? rating = null, string? review = null, int limit = 500)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            var sql = ScopedProjectsCte + RiskSelect + $"""
                WHERE (@project_id IS NULL OR risk.project_id=@project_id)
                  AND (@search='' OR risk.risk_title ILIKE '%'||@search||'%' OR risk.description ILIKE '%'||@search||'%' OR risk.trigger_indicator ILIKE '%'||@search||'%')
                  AND (@owner='' OR owner.display_name ILIKE '%'||@owner||'%' OR owner.email ILIKE '%'||@owner||'%')
                  AND (@category='' OR lower(risk.category)=lower(@category))
                  AND (@status='' OR risk.risk_status=@status)
                  AND (@rating='' OR {RatingSql("risk.inherent_exposure")}=@rating)
                  AND (@review='' OR (@review='overdue' AND risk.next_review_date<CURRENT_DATE AND risk.risk_status NOT IN ('closed','retired'))
                    OR (@review='upcoming' AND risk.next_review_date BETWEEN CURRENT_DATE AND CURRENT_DATE+INTERVAL '30 days' AND risk.risk_status NOT IN ('closed','retired')))
                ORDER BY CASE WHEN risk.risk_status NOT IN ('closed','retired') THEN 0 ELSE 1 END,risk.inherent_exposure DESC,risk.next_review_date,risk.updated_at DESC
                LIMIT @limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection); AddRiskFilters(command, authorization.Value!, projectId, search, owner, category, status, rating, review, limit);
            var rows = new List<object>(); await using var reader = await command.ExecuteReaderAsync(context.RequestAborted); while (await reader.ReadAsync(context.RequestAborted)) rows.Add(ReadRisk(reader));
            return Results.Ok(new { module = Module, scope = Scope(authorization.Value!), permissions = Permissions(authorization.Value!), risks = rows });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list risks"); }
    }

    private static async Task<IResult> CreateRiskAsync(ProjectRiskRequest request, HttpContext context)
    {
        try
        {
            var validation = ValidateRisk(request); if (validation is not null) return validation;
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await RequireAccessAsync(context, connection, true); if (authorization.Error is not null) return authorization.Error;
            var project = await LoadProjectAsync(connection, authorization.Value!, request.ProjectId, context.RequestAborted); if (project is null) return EnterpriseGovernanceResults.Forbidden(Module, "The selected project is outside your authoritative project scope.");
            if (!await ValidProjectOwnerAsync(connection, request.ProjectId, request.RiskOwnerUserId, authorization.Value!, context.RequestAborted)) return Bad("RISK_OWNER_INVALID", "Risk owner must be an active user assigned to the project or authorized by an organization-wide administrator.");
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                var id = Guid.NewGuid();
                await using var command = new NpgsqlCommand(InsertRiskSql, connection, transaction); AddRiskParameters(command, request, authorization.Value!.EffectiveUserId, project); command.Parameters.AddWithValue("id", id);
                var number = Convert.ToInt32(await command.ExecuteScalarAsync(context.RequestAborted), CultureInfo.InvariantCulture);
                await InsertRiskVersionAsync(connection, transaction, id, request.ProjectId, 1, "Initial risk identification", authorization.Value.EffectiveUserId, context.RequestAborted);
                await AuditAsync(connection, transaction, authorization.Value, request.ProjectId, id, null, "risk_created", null, request, context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
                return Results.Created($"/api/project-risk-register/risks/{id}", new { module = Module, riskId = id, riskNumber = RiskNumber(number), status = "created" });
            }
            catch { await transaction.RollbackAsync(context.RequestAborted); throw; }
        }
        catch (PostgresException exception) when (exception.SqlState is "23505" or "P0001") { return Conflict(exception.MessageText); }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "create risk"); }
    }

    private static async Task<IResult> UpdateRiskAsync(Guid riskId, ProjectRiskRequest request, HttpContext context)
    {
        try
        {
            var validation = ValidateRisk(request); if (validation is not null) return validation;
            if (string.IsNullOrWhiteSpace(request.ChangeReason)) return Bad("CHANGE_REASON_REQUIRED", "Provide a change reason for the immutable risk history.");
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await RequireAccessAsync(context, connection, true); if (authorization.Error is not null) return authorization.Error;
            var scoped = await LoadRiskIdentityAsync(connection, authorization.Value!, riskId, context.RequestAborted); if (scoped is null) return EnterpriseGovernanceResults.Forbidden(Module, "The risk is outside your authoritative project scope.");
            if (scoped.Value.Status is "closed" or "retired") return Conflict("Closed and retired risks are immutable. Create a new risk if additional uncertainty must be tracked.");
            if (scoped.Value.ProjectId != request.ProjectId) return Bad("PROJECT_ID_IMMUTABLE", "A risk cannot be moved to another project.");
            if (!await ValidProjectOwnerAsync(connection, request.ProjectId, request.RiskOwnerUserId, authorization.Value!, context.RequestAborted)) return Bad("RISK_OWNER_INVALID", "Risk owner must be an active user assigned to the project.");
            var project = await LoadProjectAsync(connection, authorization.Value!, request.ProjectId, context.RequestAborted); if (project is null) return EnterpriseGovernanceResults.Forbidden(Module, "The project is outside your scope.");
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                var prior = await SnapshotAsync(connection, transaction, "project_risks", "risk_id", riskId, context.RequestAborted);
                await using var command = new NpgsqlCommand(UpdateRiskSql, connection, transaction); AddRiskParameters(command, request, authorization.Value!.EffectiveUserId, project); command.Parameters.AddWithValue("id", riskId); command.Parameters.AddWithValue("revision", request.Revision);
                if (await command.ExecuteNonQueryAsync(context.RequestAborted) != 1) return Conflict("The risk changed since it was loaded. Refresh before trying again.");
                await InsertRiskVersionAsync(connection, transaction, riskId, request.ProjectId, request.Revision + 1, Clean(request.ChangeReason, 2000), authorization.Value.EffectiveUserId, context.RequestAborted);
                await AuditAsync(connection, transaction, authorization.Value, request.ProjectId, riskId, null, "risk_reassessed", prior, request, context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted); return Results.Ok(new { module = Module, riskId, status = "updated", revision = request.Revision + 1 });
            }
            catch { await transaction.RollbackAsync(context.RequestAborted); throw; }
        }
        catch (PostgresException exception) when (exception.SqlState is "23505" or "P0001") { return Conflict(exception.MessageText); }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "update risk"); }
    }

    private static async Task<IResult> CloseRiskAsync(Guid riskId, RiskDecisionRequest request, HttpContext context)
        => await ApplyDecisionAsync(riskId, request, context, close: true);

    private static async Task<IResult> RealizeRiskAsync(Guid riskId, RiskDecisionRequest request, HttpContext context)
        => await ApplyDecisionAsync(riskId, request, context, close: false);

    private static async Task<IResult> ApplyDecisionAsync(Guid riskId, RiskDecisionRequest request, HttpContext context, bool close)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Decision)) return Bad("DECISION_EVIDENCE_REQUIRED", "Provide the decision and supporting evidence.");
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, true); if (authorization.Error is not null) return authorization.Error;
            var scoped = await LoadRiskIdentityAsync(connection, authorization.Value!, riskId, context.RequestAborted); if (scoped is null) return EnterpriseGovernanceResults.Forbidden(Module, "The risk is outside your authoritative project scope.");
            if (scoped.Value.Status is "closed" or "retired") return Conflict("Closed and retired risks are immutable.");
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                var prior = await SnapshotAsync(connection, transaction, "project_risks", "risk_id", riskId, context.RequestAborted);
                var sql = close ? """
                    UPDATE project_risks SET risk_status='closed',closed_at=NOW(),closed_by_user_id=@actor,
                      escalation_decision=@decision,updated_by_user_id=@actor WHERE risk_id=@id AND revision_number=@revision AND risk_status NOT IN ('closed','retired');
                    """ : """
                    UPDATE project_risks SET risk_status='realized',realized_at=NOW(),issue_reference=@issue,
                      escalation_decision=@decision,updated_by_user_id=@actor WHERE risk_id=@id AND revision_number=@revision AND risk_status NOT IN ('closed','retired');
                    """;
                await using var command = new NpgsqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("actor", authorization.Value!.EffectiveUserId); command.Parameters.AddWithValue("decision", Clean(request.Decision, 4000)); command.Parameters.AddWithValue("id", riskId); command.Parameters.AddWithValue("revision", request.Revision); if (!close) command.Parameters.AddWithValue("issue", Clean(request.IssueReference, 180));
                if (await command.ExecuteNonQueryAsync(context.RequestAborted) != 1) return Conflict("The risk changed since it was loaded. Refresh before trying again.");
                await InsertRiskVersionAsync(connection, transaction, riskId, scoped.Value.ProjectId, request.Revision + 1, close ? "Risk closed" : "Risk realized and linked to issue", authorization.Value.EffectiveUserId, context.RequestAborted);
                await AuditAsync(connection, transaction, authorization.Value, scoped.Value.ProjectId, riskId, null, close ? "risk_closed" : "risk_realized", prior, request, context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted); return Results.Ok(new { module = Module, riskId, status = close ? "closed" : "realized", issueReference = close ? null : Clean(request.IssueReference, 180) });
            }
            catch { await transaction.RollbackAsync(context.RequestAborted); throw; }
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001") { return Conflict(exception.MessageText); }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, close ? "close risk" : "realize risk"); }
    }

    private static async Task<IResult> GetHeatmapAsync(HttpContext context, Guid? projectId = null)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            var sql = ScopedProjectsCte + $"""
                SELECT risk.probability_score,risk.overall_impact_score,COUNT(*)::bigint,
                  COUNT(*) FILTER(WHERE risk.risk_type='threat')::bigint,COUNT(*) FILTER(WHERE risk.risk_type='opportunity')::bigint
                FROM project_risks risk JOIN scoped_projects project ON project.project_id=risk.project_id
                WHERE risk.risk_status NOT IN ('closed','retired') AND (@project_id IS NULL OR risk.project_id=@project_id)
                GROUP BY risk.probability_score,risk.overall_impact_score ORDER BY risk.probability_score,risk.overall_impact_score;
                """;
            await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, authorization.Value!); AddNullableGuid(command, "project_id", projectId);
            var cells = new List<object>(); await using var reader = await command.ExecuteReaderAsync(context.RequestAborted); while (await reader.ReadAsync(context.RequestAborted)) { var probability = reader.GetInt16(0); var impact = reader.GetInt16(1); var exposure = probability * impact; cells.Add(new { probability, impact, exposure, rating = Rating(exposure), count = reader.GetInt64(2), threats = reader.GetInt64(3), opportunities = reader.GetInt64(4) }); }
            return Results.Ok(new { module = Module, scale = 5, cells });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load heatmap"); }
    }

    private static async Task<IResult> ListActionsAsync(HttpContext context, Guid? projectId = null, Guid? riskId = null, string? state = null, bool mine = false, int limit = 500)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            var sql = ScopedProjectsCte + """
                SELECT action.risk_action_id,action.risk_id,risk.risk_number,project.project_id,project.project_code,project.project_name,
                  action.action_title,action.action_description,action.owner_user_id,COALESCE(owner.display_name,owner.email,''),
                  action.due_date,action.action_status,action.completion_evidence,action.notes,action.completed_at,
                  action.created_at,action.updated_at,action.revision_number,
                  (action.action_status NOT IN ('completed','cancelled') AND action.due_date<CURRENT_DATE) AS overdue,
                  action.owner_user_id=@user_id AS is_mine
                FROM project_risk_actions action JOIN project_risks risk ON risk.risk_id=action.risk_id
                JOIN scoped_projects project ON project.project_id=action.project_id JOIN app_users owner ON owner.user_id=action.owner_user_id
                WHERE (@project_id IS NULL OR action.project_id=@project_id) AND (@risk_id IS NULL OR action.risk_id=@risk_id)
                  AND (@mine=FALSE OR action.owner_user_id=@user_id)
                  AND (@state='' OR (@state='overdue' AND action.action_status NOT IN ('completed','cancelled') AND action.due_date<CURRENT_DATE)
                    OR (@state='due' AND action.action_status NOT IN ('completed','cancelled') AND action.due_date>=CURRENT_DATE)
                    OR action.action_status=@state)
                ORDER BY CASE WHEN action.action_status NOT IN ('completed','cancelled') AND action.due_date<CURRENT_DATE THEN 0 ELSE 1 END,action.due_date,action.updated_at DESC LIMIT @limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, authorization.Value!); AddNullableGuid(command, "project_id", projectId); AddNullableGuid(command, "risk_id", riskId); command.Parameters.AddWithValue("mine", mine); command.Parameters.AddWithValue("state", Clean(state, 30)); command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 2000));
            var rows = new List<object>(); await using var reader = await command.ExecuteReaderAsync(context.RequestAborted); while (await reader.ReadAsync(context.RequestAborted)) rows.Add(new { actionId = reader.GetGuid(0), riskId = reader.GetGuid(1), riskNumber = RiskNumber(reader.GetInt32(2)), projectId = reader.GetGuid(3), projectCode = reader.GetString(4), projectName = reader.GetString(5), title = reader.GetString(6), description = reader.GetString(7), ownerUserId = reader.GetGuid(8), owner = reader.GetString(9), dueDate = Date(reader, 10), status = reader.GetString(11), completionEvidence = reader.GetString(12), notes = reader.GetString(13), completedAt = NullableDateTime(reader, 14), createdAt = reader.GetFieldValue<DateTimeOffset>(15), updatedAt = reader.GetFieldValue<DateTimeOffset>(16), revision = reader.GetInt32(17), overdue = reader.GetBoolean(18), isMine = reader.GetBoolean(19) });
            return Results.Ok(new { module = Module, permissions = Permissions(authorization.Value!), actions = rows });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list risk actions"); }
    }

    private static async Task<IResult> CreateActionAsync(Guid riskId, RiskActionRequest request, HttpContext context)
    {
        try
        {
            var validation = ValidateAction(request); if (validation is not null) return validation;
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, true); if (authorization.Error is not null) return authorization.Error;
            var risk = await LoadRiskIdentityAsync(connection, authorization.Value!, riskId, context.RequestAborted); if (risk is null) return EnterpriseGovernanceResults.Forbidden(Module, "The risk is outside your authoritative project scope."); if (risk.Value.Status is "closed" or "retired") return Conflict("Actions cannot be added to a closed or retired risk.");
            if (!await ValidProjectOwnerAsync(connection, risk.Value.ProjectId, request.OwnerUserId, authorization.Value!, context.RequestAborted)) return Bad("ACTION_OWNER_INVALID", "Action owner must be an active user assigned to the project.");
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                var actionId = Guid.NewGuid(); await using var command = new NpgsqlCommand("""
                    INSERT INTO project_risk_actions(risk_action_id,risk_id,project_id,action_title,action_description,owner_user_id,due_date,
                      action_status,completion_evidence,notes,completed_at,created_by_user_id,updated_by_user_id)
                    VALUES(@id,@risk,@project,@title,@description,@owner,@due,@status,@evidence,@notes,
                      CASE WHEN @status='completed' THEN NOW() ELSE NULL END,@actor,@actor);
                    """, connection, transaction); AddActionParameters(command, request, authorization.Value!.EffectiveUserId); command.Parameters.AddWithValue("id", actionId); command.Parameters.AddWithValue("risk", riskId); command.Parameters.AddWithValue("project", risk.Value.ProjectId); await command.ExecuteNonQueryAsync(context.RequestAborted);
                await InsertActionHistoryAsync(connection, transaction, actionId, riskId, risk.Value.ProjectId, 1, "Action created", authorization.Value.EffectiveUserId, context.RequestAborted);
                await AuditAsync(connection, transaction, authorization.Value, risk.Value.ProjectId, riskId, actionId, "risk_action_created", null, request, context.RequestAborted); await transaction.CommitAsync(context.RequestAborted);
                return Results.Created($"/api/project-risk-register/actions/{actionId}", new { module = Module, actionId, status = "created" });
            }
            catch { await transaction.RollbackAsync(context.RequestAborted); throw; }
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001") { return Conflict(exception.MessageText); }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "create risk action"); }
    }

    private static async Task<IResult> UpdateActionAsync(Guid actionId, RiskActionRequest request, HttpContext context)
    {
        try
        {
            var validation = ValidateAction(request); if (validation is not null) return validation; if (string.IsNullOrWhiteSpace(request.ChangeReason)) return Bad("CHANGE_REASON_REQUIRED", "Provide a reason for the immutable action history.");
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error; if (authorization.Value!.IsViewAs) return EnterpriseGovernanceResults.ViewAsReadOnly(Module);
            var identity = await LoadActionIdentityAsync(connection, authorization.Value, actionId, context.RequestAborted); if (identity is null) return EnterpriseGovernanceResults.Forbidden(Module, "The risk action is outside your project scope.");
            var isRiskManager = authorization.Value.CanManageRiskRegister;
            var isAssignedOwner = authorization.Value.CanUpdateAssignedActions && identity.Value.OwnerUserId == authorization.Value.EffectiveUserId;
            if (!isRiskManager && !isAssignedOwner) return EnterpriseGovernanceResults.Forbidden(Module, "Only the assigned action owner or an authorized risk manager can update this action.");
            if (!isRiskManager && request.OwnerUserId != identity.Value.OwnerUserId) return EnterpriseGovernanceResults.Forbidden(Module, "Assigned action owners cannot reassign the action. Ask an authorized risk manager to change ownership.");
            if (!await ValidProjectOwnerAsync(connection, identity.Value.ProjectId, request.OwnerUserId, authorization.Value, context.RequestAborted)) return Bad("ACTION_OWNER_INVALID", "Action owner must be an active user assigned to the project.");
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                var prior = await SnapshotAsync(connection, transaction, "project_risk_actions", "risk_action_id", actionId, context.RequestAborted);
                await using var command = new NpgsqlCommand("""
                    UPDATE project_risk_actions SET action_title=@title,action_description=@description,owner_user_id=@owner,due_date=@due,
                      action_status=@status,completion_evidence=@evidence,notes=@notes,
                      completed_at=CASE WHEN @status='completed' THEN COALESCE(completed_at,NOW()) ELSE NULL END,updated_by_user_id=@actor
                    WHERE risk_action_id=@id AND revision_number=@revision;
                    """, connection, transaction); AddActionParameters(command, request, authorization.Value.EffectiveUserId); command.Parameters.AddWithValue("id", actionId); command.Parameters.AddWithValue("revision", request.Revision);
                if (await command.ExecuteNonQueryAsync(context.RequestAborted) != 1) return Conflict("The action changed since it was loaded. Refresh before trying again.");
                await InsertActionHistoryAsync(connection, transaction, actionId, identity.Value.RiskId, identity.Value.ProjectId, request.Revision + 1, Clean(request.ChangeReason, 2000), authorization.Value.EffectiveUserId, context.RequestAborted);
                await AuditAsync(connection, transaction, authorization.Value, identity.Value.ProjectId, identity.Value.RiskId, actionId, "risk_action_updated", prior, request, context.RequestAborted); await transaction.CommitAsync(context.RequestAborted);
                return Results.Ok(new { module = Module, actionId, status = "updated", revision = request.Revision + 1 });
            }
            catch { await transaction.RollbackAsync(context.RequestAborted); throw; }
        }
        catch (PostgresException exception) when (exception.SqlState == "P0001") { return Conflict(exception.MessageText); }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "update risk action"); }
    }

    private static async Task<IResult> GetReviewCalendarAsync(HttpContext context, DateOnly? start = null, DateOnly? end = null)
    {
        try
        {
            start ??= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)); end ??= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)); if (end < start) return Bad("DATE_RANGE_INVALID", "End date must be on or after start date.");
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            var sql = ScopedProjectsCte + $"""
                SELECT risk.risk_id,risk.risk_number,project.project_code,project.project_name,risk.risk_title,risk.next_review_date,
                  risk.review_cadence,risk.inherent_exposure,{RatingSql("risk.inherent_exposure")} AS rating,
                  risk.next_review_date<CURRENT_DATE AS overdue,COALESCE(owner.display_name,owner.email,'')
                FROM project_risks risk JOIN scoped_projects project ON project.project_id=risk.project_id
                JOIN app_users owner ON owner.user_id=risk.risk_owner_user_id
                WHERE risk.risk_status NOT IN ('closed','retired') AND risk.next_review_date BETWEEN @start AND @end
                ORDER BY risk.next_review_date,risk.inherent_exposure DESC;
                """;
            await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, authorization.Value!); command.Parameters.AddWithValue("start", start.Value); command.Parameters.AddWithValue("end", end.Value);
            var rows = new List<object>(); await using var reader = await command.ExecuteReaderAsync(context.RequestAborted); while (await reader.ReadAsync(context.RequestAborted)) rows.Add(new { riskId = reader.GetGuid(0), riskNumber = RiskNumber(reader.GetInt32(1)), projectCode = reader.GetString(2), projectName = reader.GetString(3), title = reader.GetString(4), reviewDate = Date(reader, 5), cadence = reader.GetString(6), exposure = reader.GetInt16(7), rating = reader.GetString(8), overdue = reader.GetBoolean(9), owner = reader.GetString(10) });
            return Results.Ok(new { module = Module, start, end, reviews = rows });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load review calendar"); }
    }

    private static async Task<IResult> ListHistoryAsync(HttpContext context, Guid? projectId = null, Guid? riskId = null, int limit = 500)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            var sql = ScopedProjectsCte + """
                SELECT audit.audit_event_id,audit.project_id,audit.risk_id,audit.risk_action_id,audit.event_code,
                  COALESCE(actor.display_name,actor.email,''),audit.event_metadata,audit.occurred_at,
                  COALESCE(project.project_code,'EXPORT'),risk.risk_number
                FROM project_risk_audit_events audit
                LEFT JOIN scoped_projects project ON project.project_id=audit.project_id
                LEFT JOIN project_risks risk ON risk.risk_id=audit.risk_id
                LEFT JOIN app_users actor ON actor.user_id=audit.effective_actor_user_id
                WHERE (project.project_id IS NOT NULL OR audit.effective_actor_user_id=@user_id OR @broad_scope=TRUE)
                  AND (@project_id IS NULL OR audit.project_id=@project_id)
                  AND (@risk_id IS NULL OR audit.risk_id=@risk_id)
                ORDER BY audit.occurred_at DESC LIMIT @limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, authorization.Value!); AddNullableGuid(command, "project_id", projectId); AddNullableGuid(command, "risk_id", riskId); command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 2000));
            var rows = new List<object>(); await using var reader = await command.ExecuteReaderAsync(context.RequestAborted); while (await reader.ReadAsync(context.RequestAborted)) rows.Add(new { auditId = reader.GetGuid(0), projectId = NullableGuid(reader, 1), riskId = NullableGuid(reader, 2), actionId = NullableGuid(reader, 3), eventCode = reader.GetString(4), actor = reader.GetString(5), metadata = JsonDocument.Parse(reader.GetString(6)).RootElement.Clone(), occurredAt = reader.GetFieldValue<DateTimeOffset>(7), projectCode = reader.GetString(8), riskNumber = reader.IsDBNull(9) ? "EXPORT" : RiskNumber(reader.GetInt32(9)) });
            return Results.Ok(new { module = Module, history = rows });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load history"); }
    }

    private static async Task<IResult> ExportAsync(string format, HttpContext context, Guid? projectId = null, string? status = null, string? rating = null)
    {
        try
        {
            format = Clean(format, 10).ToLowerInvariant(); if (format is not ("xlsx" or "pdf")) return Bad("EXPORT_FORMAT_INVALID", "Use xlsx or pdf.");
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted); var authorization = await RequireAccessAsync(context, connection, false); if (authorization.Error is not null) return authorization.Error;
            if (authorization.Value!.IsViewAs) return EnterpriseGovernanceResults.ViewAsReadOnly(Module); if (!authorization.Value.CanExport(Module)) return EnterpriseGovernanceResults.Forbidden(Module, "Your role does not allow Module 082 exports.");
            var risks = await LoadRiskExportAsync(connection, authorization.Value, projectId, status, rating, context.RequestAborted); var actions = await LoadActionExportAsync(connection, authorization.Value, projectId, context.RequestAborted); var history = await LoadHistoryExportAsync(connection, authorization.Value, projectId, context.RequestAborted);
            var scope = ScopeLabel(authorization.Value); var filters = $"project={projectId?.ToString() ?? "all"}; status={Clean(status, 32)}; rating={Clean(rating, 16)}";
            var bytes = format == "xlsx" ? EnterpriseGovernanceExports.BuildRiskExcel(risks, actions, history, scope, filters) : EnterpriseGovernanceExports.BuildRiskPdf(risks, actions, scope, filters);
            await AuditAsync(connection, null, authorization.Value, projectId, null, null, $"{format}_export_created", null, new { filters, risks = risks.Count, actions = actions.Count }, context.RequestAborted);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture); return Results.File(bytes, format == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf", $"US-Signal-Project-Risk-Register-{timestamp}.{format}");
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "create export"); }
    }

    private static async Task<(EnterpriseGovernanceAccess? Value, IResult? Error)> RequireAccessAsync(HttpContext context, NpgsqlConnection connection, bool manage)
    {
        var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted); if (access is null) return (null, EnterpriseGovernanceResults.Unauthorized(Module));
        if (!access.CanViewRiskRegister) return (access, EnterpriseGovernanceResults.Forbidden(Module, "Your role does not have Enterprise Project Risk Register access."));
        if (manage && access.IsViewAs) return (access, EnterpriseGovernanceResults.ViewAsReadOnly(Module));
        if (manage && !access.CanManageRiskRegister) return (access, EnterpriseGovernanceResults.Forbidden(Module, "Your role has read-only project risk access."));
        return (access, null);
    }

    private static async Task<bool> RuntimeReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration)
               AND to_regclass('public.project_risks') IS NOT NULL
               AND to_regclass('public.project_risk_versions') IS NOT NULL
               AND to_regclass('public.project_risk_actions') IS NOT NULL
               AND to_regclass('public.project_risk_action_history') IS NOT NULL
               AND to_regclass('public.project_risk_audit_events') IS NOT NULL;
            """, connection);
        command.Parameters.AddWithValue("migration", Migration);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string ScopedProjectsCte => "WITH " + EnterpriseGovernanceAccessResolver.TeamMembersCte + ", scoped_projects AS (SELECT project.* FROM projects project WHERE " + EnterpriseGovernanceAccessResolver.ProjectScopePredicate + ") ";

    private static readonly string RiskSelect = $"""
        SELECT risk.risk_id,risk.risk_number,risk.project_id,project.project_code,project.project_name,
          COALESCE(client.client_name,risk.customer_name_snapshot,''),risk.risk_title,risk.cause_statement,
          risk.uncertain_event_statement,risk.impact_statement,risk.description,risk.risk_type,risk.category,risk.subcategory,
          risk.date_identified,COALESCE(identifier.display_name,identifier.email,''),risk.risk_owner_user_id,
          COALESCE(owner.display_name,owner.email,''),risk.probability_score,risk.schedule_impact_score,risk.cost_impact_score,
          risk.scope_impact_score,risk.quality_impact_score,risk.customer_impact_score,risk.security_impact_score,
          risk.compliance_impact_score,risk.resource_impact_score,risk.operational_impact_score,risk.overall_impact_score,
          risk.inherent_exposure,{RatingSql("risk.inherent_exposure")} AS inherent_rating,risk.proximity,risk.velocity,
          risk.response_strategy,risk.response_plan,risk.mitigation_actions,risk.contingency_plan,risk.trigger_indicator,
          risk.response_cost,risk.response_schedule_impact_days,risk.target_response_date,risk.next_review_date,risk.review_cadence,
          risk.risk_status,risk.residual_probability_score,risk.residual_impact_score,risk.residual_exposure,
          {RatingSql("risk.residual_exposure")} AS residual_rating,risk.escalation_level,risk.escalation_decision,
          risk.issue_reference,risk.realized_at,risk.assumptions,risk.dependencies,risk.evidence_references,
          risk.created_at,risk.updated_at,risk.closed_at,risk.revision_number,
          (risk.next_review_date<CURRENT_DATE AND risk.risk_status NOT IN ('closed','retired')) AS review_overdue,
          COALESCE(action_summary.overdue_actions,0)::bigint AS overdue_actions
        FROM project_risks risk JOIN scoped_projects project ON project.project_id=risk.project_id
        LEFT JOIN clients client ON client.client_id=project.client_id JOIN app_users owner ON owner.user_id=risk.risk_owner_user_id
        JOIN app_users identifier ON identifier.user_id=risk.identified_by_user_id
        LEFT JOIN LATERAL (
          SELECT COUNT(*)::bigint AS overdue_actions
          FROM project_risk_actions action
          WHERE action.risk_id=risk.risk_id
            AND action.action_status NOT IN ('completed','cancelled')
            AND action.due_date<CURRENT_DATE
        ) action_summary ON TRUE
        """;

    private static object ReadRisk(NpgsqlDataReader reader) => new
    {
        riskId = reader.GetGuid(0), riskNumber = RiskNumber(reader.GetInt32(1)), projectId = reader.GetGuid(2), projectCode = reader.GetString(3), projectName = reader.GetString(4), customer = reader.GetString(5), title = reader.GetString(6), cause = reader.GetString(7), uncertainEvent = reader.GetString(8), impactStatement = reader.GetString(9), description = reader.GetString(10), type = reader.GetString(11), category = reader.GetString(12), subcategory = reader.GetString(13), dateIdentified = Date(reader, 14), identifiedBy = reader.GetString(15), riskOwnerUserId = reader.GetGuid(16), owner = reader.GetString(17), probability = reader.GetInt16(18), impacts = new { schedule = reader.GetInt16(19), cost = reader.GetInt16(20), scope = reader.GetInt16(21), quality = reader.GetInt16(22), customer = reader.GetInt16(23), security = reader.GetInt16(24), compliance = reader.GetInt16(25), resource = reader.GetInt16(26), operational = reader.GetInt16(27), overall = reader.GetInt16(28) }, inherentExposure = reader.GetInt16(29), inherentRating = reader.GetString(30), proximity = reader.GetString(31), velocity = reader.GetString(32), responseStrategy = reader.GetString(33), responsePlan = reader.GetString(34), mitigationActions = reader.GetString(35), contingencyPlan = reader.GetString(36), trigger = reader.GetString(37), responseCost = NullableDecimal(reader, 38), responseScheduleImpactDays = NullableInt(reader, 39), targetResponseDate = Date(reader, 40), nextReviewDate = Date(reader, 41), reviewCadence = reader.GetString(42), status = reader.GetString(43), residualProbability = NullableShort(reader, 44), residualImpact = NullableShort(reader, 45), residualExposure = NullableShort(reader, 46), residualRating = reader.GetString(47), escalationLevel = reader.GetString(48), escalationDecision = reader.GetString(49), issueReference = reader.GetString(50), realizedAt = NullableDateTime(reader, 51), assumptions = reader.GetString(52), dependencies = reader.GetString(53), evidenceReferences = JsonDocument.Parse(reader.GetString(54)).RootElement.Clone(), createdAt = reader.GetFieldValue<DateTimeOffset>(55), updatedAt = reader.GetFieldValue<DateTimeOffset>(56), closedAt = NullableDateTime(reader, 57), revision = reader.GetInt32(58), reviewOverdue = reader.GetBoolean(59), overdueActions = reader.GetInt64(60)
    };

    private static readonly string InsertRiskSql = """
        INSERT INTO project_risks(risk_id,risk_number,project_id,project_code_snapshot,project_name_snapshot,customer_name_snapshot,
          risk_title,cause_statement,uncertain_event_statement,impact_statement,description,risk_type,category,subcategory,date_identified,
          identified_by_user_id,risk_owner_user_id,probability_score,schedule_impact_score,cost_impact_score,scope_impact_score,
          quality_impact_score,customer_impact_score,security_impact_score,compliance_impact_score,resource_impact_score,operational_impact_score,
          proximity,velocity,response_strategy,response_plan,mitigation_actions,contingency_plan,trigger_indicator,response_cost,
          response_schedule_impact_days,target_response_date,next_review_date,review_cadence,risk_status,residual_probability_score,
          residual_impact_score,escalation_level,escalation_decision,issue_reference,realized_at,assumptions,dependencies,evidence_references,
          created_by_user_id,updated_by_user_id)
        VALUES(@id,0,@project,@project_code,@project_name,@customer,@title,@cause,@event,@impact,@description,@type,@category,@subcategory,
          @identified_date,@actor,@owner,@probability,@schedule,@cost,@scope,@quality,@customer_impact,@security,@compliance,@resource,@operational,
          @proximity,@velocity,@strategy,@response,@mitigation,@contingency,@trigger,@response_cost,@schedule_days,@target_date,@review_date,@cadence,
          @status,@residual_probability,@residual_impact,@escalation_level,@escalation_decision,@issue,@realized_at,@assumptions,@dependencies,
          @evidence::jsonb,@actor,@actor) RETURNING risk_number;
        """;

    private static readonly string UpdateRiskSql = """
        UPDATE project_risks SET risk_title=@title,cause_statement=@cause,uncertain_event_statement=@event,impact_statement=@impact,
          description=@description,risk_type=@type,category=@category,subcategory=@subcategory,date_identified=@identified_date,
          risk_owner_user_id=@owner,probability_score=@probability,schedule_impact_score=@schedule,cost_impact_score=@cost,
          scope_impact_score=@scope,quality_impact_score=@quality,customer_impact_score=@customer_impact,
          security_impact_score=@security,compliance_impact_score=@compliance,resource_impact_score=@resource,
          operational_impact_score=@operational,proximity=@proximity,velocity=@velocity,response_strategy=@strategy,
          response_plan=@response,mitigation_actions=@mitigation,contingency_plan=@contingency,trigger_indicator=@trigger,
          response_cost=@response_cost,response_schedule_impact_days=@schedule_days,target_response_date=@target_date,
          next_review_date=@review_date,review_cadence=@cadence,risk_status=@status,residual_probability_score=@residual_probability,
          residual_impact_score=@residual_impact,escalation_level=@escalation_level,escalation_decision=@escalation_decision,
          issue_reference=@issue,realized_at=@realized_at,assumptions=@assumptions,dependencies=@dependencies,
          evidence_references=@evidence::jsonb,updated_by_user_id=@actor
        WHERE risk_id=@id AND project_id=@project AND revision_number=@revision AND risk_status NOT IN ('closed','retired');
        """;

    private static void AddRiskParameters(NpgsqlCommand command, ProjectRiskRequest request, Guid actor, ProjectIdentity project)
    {
        command.Parameters.AddWithValue("project", request.ProjectId); command.Parameters.AddWithValue("project_code", project.Code); command.Parameters.AddWithValue("project_name", project.Name); command.Parameters.AddWithValue("customer", project.Customer);
        command.Parameters.AddWithValue("title", Required(request.Title, 240)); command.Parameters.AddWithValue("cause", Required(request.Cause, 8000)); command.Parameters.AddWithValue("event", Required(request.UncertainEvent, 8000)); command.Parameters.AddWithValue("impact", Required(request.ImpactStatement, 8000)); command.Parameters.AddWithValue("description", Clean(request.Description, 12000)); command.Parameters.AddWithValue("type", Choice(request.Type, ["threat","opportunity"], "threat")); command.Parameters.AddWithValue("category", Required(request.Category, 100)); command.Parameters.AddWithValue("subcategory", Clean(request.Subcategory, 120)); command.Parameters.AddWithValue("identified_date", request.DateIdentified); command.Parameters.AddWithValue("owner", request.RiskOwnerUserId); command.Parameters.AddWithValue("probability", request.Probability); command.Parameters.AddWithValue("schedule", request.ScheduleImpact); command.Parameters.AddWithValue("cost", request.CostImpact); command.Parameters.AddWithValue("scope", request.ScopeImpact); command.Parameters.AddWithValue("quality", request.QualityImpact); command.Parameters.AddWithValue("customer_impact", request.CustomerImpact); command.Parameters.AddWithValue("security", request.SecurityImpact); command.Parameters.AddWithValue("compliance", request.ComplianceImpact); command.Parameters.AddWithValue("resource", request.ResourceImpact); command.Parameters.AddWithValue("operational", request.OperationalImpact); command.Parameters.AddWithValue("proximity", Clean(request.Proximity, 80)); command.Parameters.AddWithValue("velocity", Choice(request.Velocity, ["low","normal","high","immediate"], "normal")); command.Parameters.AddWithValue("strategy", Choice(request.ResponseStrategy, Strategies, "mitigate")); command.Parameters.AddWithValue("response", Clean(request.ResponsePlan, 12000)); command.Parameters.AddWithValue("mitigation", Clean(request.MitigationActions, 12000)); command.Parameters.AddWithValue("contingency", Clean(request.ContingencyPlan, 12000)); command.Parameters.AddWithValue("trigger", Clean(request.Trigger, 8000)); AddNullableDecimal(command, "response_cost", request.ResponseCost); AddNullableInt(command, "schedule_days", request.ResponseScheduleImpactDays); AddNullableDate(command, "target_date", request.TargetResponseDate); command.Parameters.AddWithValue("review_date", request.NextReviewDate); command.Parameters.AddWithValue("cadence", Choice(request.ReviewCadence, ["weekly","biweekly","monthly","quarterly","event_driven"], "monthly")); command.Parameters.AddWithValue("status", Choice(request.Status, RiskStatuses, "proposed")); AddNullableInt(command, "residual_probability", request.ResidualProbability); AddNullableInt(command, "residual_impact", request.ResidualImpact); command.Parameters.AddWithValue("escalation_level", Choice(request.EscalationLevel, ["project","pmo","executive","security","compliance","customer"], "project")); command.Parameters.AddWithValue("escalation_decision", Clean(request.EscalationDecision, 8000)); command.Parameters.AddWithValue("issue", Clean(request.IssueReference, 180)); AddNullableDateTime(command, "realized_at", string.Equals(request.Status, "realized", StringComparison.OrdinalIgnoreCase) ? request.RealizedAt ?? DateTimeOffset.UtcNow : null); command.Parameters.AddWithValue("assumptions", Clean(request.Assumptions, 8000)); command.Parameters.AddWithValue("dependencies", Clean(request.Dependencies, 8000)); command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(request.EvidenceReferences ?? [])); command.Parameters.AddWithValue("actor", actor);
    }

    private static IResult? ValidateRisk(ProjectRiskRequest request)
    {
        if (request.ProjectId == Guid.Empty || request.RiskOwnerUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Cause) || string.IsNullOrWhiteSpace(request.UncertainEvent) || string.IsNullOrWhiteSpace(request.ImpactStatement) || string.IsNullOrWhiteSpace(request.Category)) return Bad("RISK_FIELDS_REQUIRED", "Project, title, cause, uncertain event, impact, category, and risk owner are required.");
        var scores = new[] { request.Probability, request.ScheduleImpact, request.CostImpact, request.ScopeImpact, request.QualityImpact, request.CustomerImpact, request.SecurityImpact, request.ComplianceImpact, request.ResourceImpact, request.OperationalImpact }; if (scores.Any(score => score is < 1 or > 5)) return Bad("RISK_SCORE_INVALID", "Probability and every impact score must be between 1 and 5.");
        if (request.ResidualProbability is < 1 or > 5 || request.ResidualImpact is < 1 or > 5) return Bad("RESIDUAL_SCORE_INVALID", "Residual probability and impact must be between 1 and 5.");
        if (request.NextReviewDate < request.DateIdentified) return Bad("REVIEW_DATE_INVALID", "Next review cannot be before the identified date.");
        var type = Choice(request.Type, ["threat","opportunity"], "threat");
        var strategy = Choice(request.ResponseStrategy, Strategies, "mitigate");
        var validStrategy = type == "threat"
            ? strategy is "avoid" or "mitigate" or "transfer" or "accept" or "escalate"
            : strategy is "exploit" or "enhance" or "share" or "accept" or "escalate";
        if (!validStrategy) return Bad("RESPONSE_STRATEGY_INVALID", "Select an approved strategy for the chosen threat or opportunity type.");
        if (Choice(request.Status, RiskStatuses, "proposed") is "closed" or "retired") return Bad("GOVERNED_DECISION_REQUIRED", "Use the governed close action so decision evidence is preserved.");
        return null;
    }

    private static IResult? ValidateAction(RiskActionRequest request)
    {
        if (request.OwnerUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title)) return Bad("ACTION_FIELDS_REQUIRED", "Action title, owner, and due date are required.");
        var status = Choice(request.Status, ["not_started","in_progress","blocked","completed","cancelled"], "not_started"); if (status == "completed" && string.IsNullOrWhiteSpace(request.CompletionEvidence)) return Bad("COMPLETION_EVIDENCE_REQUIRED", "Completed actions require completion evidence."); return null;
    }

    private static async Task<ProjectIdentity?> LoadProjectAsync(NpgsqlConnection connection, EnterpriseGovernanceAccess access, Guid projectId, CancellationToken cancellationToken)
    {
        var sql = ScopedProjectsCte + "SELECT project.project_id,project.project_code,project.project_name,COALESCE(client.client_name,'') FROM scoped_projects project LEFT JOIN clients client ON client.client_id=project.client_id WHERE project.project_id=@project_id;";
        await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access); command.Parameters.AddWithValue("project_id", projectId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? new ProjectIdentity(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)) : null;
    }

    private static async Task<(Guid ProjectId, string Status)?> LoadRiskIdentityAsync(NpgsqlConnection connection, EnterpriseGovernanceAccess access, Guid riskId, CancellationToken cancellationToken)
    {
        var sql = ScopedProjectsCte + "SELECT risk.project_id,risk.risk_status FROM project_risks risk JOIN scoped_projects project ON project.project_id=risk.project_id WHERE risk.risk_id=@id;"; await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access); command.Parameters.AddWithValue("id", riskId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? (reader.GetGuid(0), reader.GetString(1)) : null;
    }

    private static async Task<(Guid RiskId, Guid ProjectId, Guid OwnerUserId)?> LoadActionIdentityAsync(NpgsqlConnection connection, EnterpriseGovernanceAccess access, Guid actionId, CancellationToken cancellationToken)
    {
        var sql = ScopedProjectsCte + "SELECT action.risk_id,action.project_id,action.owner_user_id FROM project_risk_actions action JOIN scoped_projects project ON project.project_id=action.project_id WHERE action.risk_action_id=@id;"; await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access); command.Parameters.AddWithValue("id", actionId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)) : null;
    }

    private static async Task<bool> ValidProjectOwnerAsync(NpgsqlConnection connection, Guid projectId, Guid userId, EnterpriseGovernanceAccess access, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM app_users app_user WHERE app_user.user_id=@owner AND app_user.is_active=TRUE
              AND (@broad_scope=TRUE OR EXISTS(SELECT 1 FROM projects project WHERE project.project_id=@project AND project.project_manager_user_id=@owner)
                OR EXISTS(SELECT 1 FROM project_assignments assignment WHERE assignment.project_id=@project AND assignment.user_id=@owner
                  AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date>=CURRENT_DATE))));
            """, connection); command.Parameters.AddWithValue("owner", userId); command.Parameters.AddWithValue("project", projectId); command.Parameters.AddWithValue("broad_scope", access.IsBroadScope); return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task InsertRiskVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid riskId, Guid projectId, int version, string reason, Guid actor, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO project_risk_versions(risk_id,project_id,version_number,risk_snapshot,change_reason,created_by_user_id)
            SELECT risk_id,project_id,@version,to_jsonb(risk),@reason,@actor FROM project_risks risk WHERE risk_id=@id;
            """, connection, transaction); command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("reason", reason); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("id", riskId); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertActionHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actionId, Guid riskId, Guid projectId, int version, string reason, Guid actor, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO project_risk_action_history(risk_action_id,risk_id,project_id,version_number,action_snapshot,change_reason,created_by_user_id)
            SELECT risk_action_id,risk_id,project_id,@version,to_jsonb(action),@reason,@actor FROM project_risk_actions action WHERE risk_action_id=@id;
            """, connection, transaction); command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("reason", reason); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("id", actionId); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, EnterpriseGovernanceAccess access, Guid? projectId, Guid? riskId, Guid? actionId, string eventCode, object? prior, object? next, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO project_risk_audit_events(project_id,risk_id,risk_action_id,event_code,actual_actor_user_id,effective_actor_user_id,prior_state,new_state,event_metadata)
            VALUES(@project,@risk,@action,@event,@actual,@effective,@prior::jsonb,@next::jsonb,@metadata::jsonb);
            """, connection, transaction); AddNullableGuid(command, "project", projectId); AddNullableGuid(command, "risk", riskId); AddNullableGuid(command, "action", actionId); command.Parameters.AddWithValue("event", eventCode); command.Parameters.AddWithValue("actual", access.ActualUserId); command.Parameters.AddWithValue("effective", access.EffectiveUserId); command.Parameters.AddWithValue("prior", prior is null ? "null" : JsonSerializer.Serialize(prior)); command.Parameters.AddWithValue("next", next is null ? "null" : JsonSerializer.Serialize(next)); command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new { module = Module, contractVersion = ContractVersion })); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<JsonElement?> SnapshotAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string key, Guid id, CancellationToken cancellationToken)
    { await using var command = new NpgsqlCommand($"SELECT row_to_json(snapshot)::text FROM (SELECT * FROM {table} WHERE {key}=@id) snapshot;", connection, transaction); command.Parameters.AddWithValue("id", id); var raw = await command.ExecuteScalarAsync(cancellationToken) as string; return string.IsNullOrWhiteSpace(raw) ? null : JsonDocument.Parse(raw).RootElement.Clone(); }

    private static void AddActionParameters(NpgsqlCommand command, RiskActionRequest request, Guid actor)
    { command.Parameters.AddWithValue("title", Required(request.Title, 240)); command.Parameters.AddWithValue("description", Clean(request.Description, 8000)); command.Parameters.AddWithValue("owner", request.OwnerUserId); command.Parameters.AddWithValue("due", request.DueDate); command.Parameters.AddWithValue("status", Choice(request.Status, ["not_started","in_progress","blocked","completed","cancelled"], "not_started")); command.Parameters.AddWithValue("evidence", Clean(request.CompletionEvidence, 8000)); command.Parameters.AddWithValue("notes", Clean(request.Notes, 8000)); command.Parameters.AddWithValue("actor", actor); }

    private static void AddRiskFilters(NpgsqlCommand command, EnterpriseGovernanceAccess access, Guid? projectId, string? search, string? owner, string? category, string? status, string? rating, string? review, int limit)
    { EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access); AddNullableGuid(command, "project_id", projectId); command.Parameters.AddWithValue("search", Clean(search, 120)); command.Parameters.AddWithValue("owner", Clean(owner, 120)); command.Parameters.AddWithValue("category", Clean(category, 100)); command.Parameters.AddWithValue("status", Choice(status, RiskStatuses, string.Empty, true)); command.Parameters.AddWithValue("rating", Choice(rating, ["low","moderate","high","critical"], string.Empty, true)); command.Parameters.AddWithValue("review", Choice(review, ["overdue","upcoming"], string.Empty, true)); command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 2000)); }

    private static async Task<List<RiskExportRow>> LoadRiskExportAsync(NpgsqlConnection connection, EnterpriseGovernanceAccess access, Guid? projectId, string? status, string? rating, CancellationToken cancellationToken)
    {
        var rows = new List<RiskExportRow>(); var sql = ScopedProjectsCte + $"""
            SELECT risk.risk_number,project.project_code,project.project_name,COALESCE(client.client_name,''),risk.risk_title,risk.risk_type,risk.category,
              COALESCE(owner.display_name,owner.email,''),risk.probability_score,risk.overall_impact_score,risk.inherent_exposure,
              {RatingSql("risk.inherent_exposure")},risk.residual_exposure,{RatingSql("risk.residual_exposure")},
              risk.response_strategy,risk.risk_status,risk.next_review_date::text,risk.trigger_indicator,risk.updated_at::text
            FROM project_risks risk JOIN scoped_projects project ON project.project_id=risk.project_id LEFT JOIN clients client ON client.client_id=project.client_id JOIN app_users owner ON owner.user_id=risk.risk_owner_user_id
            WHERE (@project_id IS NULL OR risk.project_id=@project_id) AND (@status='' OR risk.risk_status=@status) AND (@rating='' OR {RatingSql("risk.inherent_exposure")}=@rating) ORDER BY project.project_code,risk.risk_number;
            """;
        await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access); AddNullableGuid(command, "project_id", projectId); command.Parameters.AddWithValue("status", Choice(status, RiskStatuses, string.Empty, true)); command.Parameters.AddWithValue("rating", Choice(rating, ["low","moderate","high","critical"], string.Empty, true)); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new RiskExportRow(RiskNumber(reader.GetInt32(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt16(8), reader.GetInt16(9), reader.GetInt16(10), reader.GetString(11), NullableShort(reader, 12), reader.GetString(13), reader.GetString(14), reader.GetString(15), reader.GetString(16), reader.GetString(17), reader.GetString(18))); return rows;
    }

    private static async Task<List<RiskActionExportRow>> LoadActionExportAsync(NpgsqlConnection connection, EnterpriseGovernanceAccess access, Guid? projectId, CancellationToken cancellationToken)
    {
        var rows = new List<RiskActionExportRow>(); var sql = ScopedProjectsCte + """
            SELECT risk.risk_number,project.project_code,action.action_title,COALESCE(owner.display_name,owner.email,''),action.due_date::text,
              action.action_status,(action.action_status NOT IN ('completed','cancelled') AND action.due_date<CURRENT_DATE),action.completion_evidence,action.updated_at::text
            FROM project_risk_actions action JOIN project_risks risk ON risk.risk_id=action.risk_id JOIN scoped_projects project ON project.project_id=action.project_id JOIN app_users owner ON owner.user_id=action.owner_user_id
            WHERE (@project_id IS NULL OR action.project_id=@project_id) ORDER BY action.due_date;
            """; await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access); AddNullableGuid(command, "project_id", projectId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) rows.Add(new RiskActionExportRow(RiskNumber(reader.GetInt32(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetString(7), reader.GetString(8))); return rows;
    }

    private static async Task<List<string[]>> LoadHistoryExportAsync(NpgsqlConnection connection, EnterpriseGovernanceAccess access, Guid? projectId, CancellationToken cancellationToken)
    {
        var rows = new List<string[]>(); var sql = ScopedProjectsCte + """
            SELECT audit.occurred_at::text,COALESCE('R-'||lpad(risk.risk_number::text,4,'0'),'EXPORT'),audit.event_code,COALESCE(actor.display_name,actor.email,''),audit.event_metadata::text
            FROM project_risk_audit_events audit
            LEFT JOIN scoped_projects project ON project.project_id=audit.project_id
            LEFT JOIN project_risks risk ON risk.risk_id=audit.risk_id
            LEFT JOIN app_users actor ON actor.user_id=audit.effective_actor_user_id
            WHERE (project.project_id IS NOT NULL OR audit.effective_actor_user_id=@user_id OR @broad_scope=TRUE)
              AND (@project_id IS NULL OR audit.project_id=@project_id)
            ORDER BY audit.occurred_at DESC LIMIT 2000;
            """; await using var command = new NpgsqlCommand(sql, connection); EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access); AddNullableGuid(command, "project_id", projectId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) rows.Add([reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)]); return rows;
    }

    private static string RatingSql(string expression) => $"CASE WHEN {expression} IS NULL THEN '' WHEN {expression}>=17 THEN 'critical' WHEN {expression}>=10 THEN 'high' WHEN {expression}>=5 THEN 'moderate' ELSE 'low' END";
    private static string Rating(int exposure) => exposure >= 17 ? "critical" : exposure >= 10 ? "high" : exposure >= 5 ? "moderate" : "low";
    private static string RiskNumber(int number) => $"R-{number:0000}";
    private static object Scope(EnterpriseGovernanceAccess access) => new { mode = access.IsBroadScope ? "organization" : access.CanManageTeam ? "assigned_team_projects" : "assigned_projects", effectiveUserId = access.EffectiveUserId, effectiveUser = access.DisplayName, team = access.TeamName, isViewAs = access.IsViewAs };
    private static string ScopeLabel(EnterpriseGovernanceAccess access) => access.IsBroadScope ? "Organization-wide" : access.CanManageTeam ? "Authorized team projects" : "Assigned projects";
    private static object Permissions(EnterpriseGovernanceAccess access) => new { canManage = access.CanManageRiskRegister, canUpdateAssignedActions = access.CanUpdateAssignedActions, canExport = access.CanExport(Module), viewAsReadOnly = access.IsViewAs };
    private static string Clean(string? value, int max) { var text = string.Concat((value ?? string.Empty).Where(ch => !char.IsControl(ch) || ch is '\n' or '\t')).Trim(); return text.Length > max ? text[..max] : text; }
    private static string Required(string? value, int max) => Clean(value, max);
    private static string Choice(string? value, IReadOnlyList<string> allowed, string fallback, bool allowEmpty = false) { var normalized = Clean(value, 40).ToLowerInvariant().Replace('-', '_').Replace(' ', '_'); if (allowEmpty && normalized == "") return ""; return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : fallback; }
    private static IResult Bad(string code, string message) => Results.BadRequest(new { module = Module, code, message });
    private static IResult Conflict(string message) => Results.Conflict(new { module = Module, code = "RISK_CONFLICT", message });
    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value) => command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = value.HasValue ? value.Value : DBNull.Value;
    private static void AddNullableInt(NpgsqlCommand command, string name, int? value) => command.Parameters.Add(name, NpgsqlDbType.Integer).Value = value.HasValue ? value.Value : DBNull.Value;
    private static void AddNullableDecimal(NpgsqlCommand command, string name, decimal? value) => command.Parameters.Add(name, NpgsqlDbType.Numeric).Value = value.HasValue ? value.Value : DBNull.Value;
    private static void AddNullableDate(NpgsqlCommand command, string name, DateOnly? value) => command.Parameters.Add(name, NpgsqlDbType.Date).Value = value.HasValue ? value.Value : DBNull.Value;
    private static void AddNullableDateTime(NpgsqlCommand command, string name, DateTimeOffset? value) => command.Parameters.Add(name, NpgsqlDbType.TimestampTz).Value = value.HasValue ? value.Value : DBNull.Value;
    private static Guid? NullableGuid(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetGuid(index);
    private static int? NullableInt(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static short? NullableShort(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt16(index);
    private static decimal? NullableDecimal(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetDecimal(index);
    private static DateOnly? Date(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : DateOnly.FromDateTime(reader.GetDateTime(index));
    private static DateTimeOffset? NullableDateTime(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetFieldValue<DateTimeOffset>(index);
    private sealed record ProjectIdentity(Guid Id, string Code, string Name, string Customer);
}

public sealed record ProjectRiskRequest(
    Guid ProjectId,string? Title,string? Cause,string? UncertainEvent,string? ImpactStatement,string? Description,
    string? Type,string? Category,string? Subcategory,DateOnly DateIdentified,Guid RiskOwnerUserId,
    int Probability,int ScheduleImpact,int CostImpact,int ScopeImpact,int QualityImpact,int CustomerImpact,
    int SecurityImpact,int ComplianceImpact,int ResourceImpact,int OperationalImpact,string? Proximity,string? Velocity,
    string? ResponseStrategy,string? ResponsePlan,string? MitigationActions,string? ContingencyPlan,string? Trigger,
    decimal? ResponseCost,int? ResponseScheduleImpactDays,DateOnly? TargetResponseDate,DateOnly NextReviewDate,
    string? ReviewCadence,string? Status,int? ResidualProbability,int? ResidualImpact,string? EscalationLevel,
    string? EscalationDecision,string? IssueReference,DateTimeOffset? RealizedAt,string? Assumptions,string? Dependencies,
    IReadOnlyList<string>? EvidenceReferences,string? ChangeReason,int Revision=1);

public sealed record RiskDecisionRequest(string? Decision,string? IssueReference,int Revision);

public sealed record RiskActionRequest(
    string? Title,string? Description,Guid OwnerUserId,DateOnly DueDate,string? Status,
    string? CompletionEvidence,string? Notes,string? ChangeReason,int Revision=1);

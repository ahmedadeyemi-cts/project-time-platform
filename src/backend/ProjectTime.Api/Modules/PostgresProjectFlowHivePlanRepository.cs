using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Production persistence boundary for Project FlowHive. Every saved change is
/// an immutable version. A baseline always names an exact version and records
/// an immutable reviewer decision; no canonical project task is changed here.
/// </summary>
public sealed class PostgresProjectFlowHivePlanRepository : IProjectFlowHivePlanRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool WritesEnabled => ProjectFlowHiveDatabaseConfig.FromEnvironment().Missing.Count == 0;

    public async Task<ProjectFlowHiveRepositoryReadiness> GetReadinessAsync(
        CancellationToken cancellationToken)
    {
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
            return new(false, "configuration_missing", config.Missing, DateTimeOffset.UtcNow);

        try
        {
            await using var connection = new NpgsqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    to_regclass('public.project_flowhive_plans') IS NOT NULL,
                    to_regclass('public.project_flowhive_plan_versions') IS NOT NULL,
                    to_regclass('public.project_flowhive_plan_reviews') IS NOT NULL,
                    to_regclass('public.project_flowhive_audit_events') IS NOT NULL,
                    EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='074_module_066_project_flowhive_production');
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new(false, "readiness_unavailable", ["FlowHive readiness returned no evidence."], DateTimeOffset.UtcNow);

            var missing = new List<string>();
            if (!reader.GetBoolean(0)) missing.Add("project_flowhive_plans");
            if (!reader.GetBoolean(1)) missing.Add("project_flowhive_plan_versions");
            if (!reader.GetBoolean(2)) missing.Add("project_flowhive_plan_reviews");
            if (!reader.GetBoolean(3)) missing.Add("project_flowhive_audit_events");
            if (!reader.GetBoolean(4)) missing.Add("074_module_066_project_flowhive_production");
            return new(
                missing.Count == 0,
                missing.Count == 0 ? "project_flowhive_production_ready" : "migration_074_required",
                missing,
                DateTimeOffset.UtcNow);
        }
        catch
        {
            return new(false, "persistence_dependency_unavailable", ["The FlowHive persistence dependency could not be verified."], DateTimeOffset.UtcNow);
        }
    }

    public async Task<IReadOnlyList<ProjectFlowHivePersistedPlanSummary>> ListAsync(
        Guid actorUserId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            WITH actor AS (
                SELECT
                    EXISTS(
                        SELECT 1 FROM app_user_role_assignments assignment
                        JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                        JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id
                        JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
                        WHERE assignment.user_id=@actor AND assignment.is_active=TRUE
                          AND permission.permission_code='VIEW_PROJECT_FLOWHIVE_066') AS can_view,
                    EXISTS(
                        SELECT 1 FROM app_user_role_assignments assignment
                        JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                        WHERE assignment.user_id=@actor AND assignment.is_active=TRUE
                          AND role.role_code IN (
                              'SUPER_ADMINISTRATOR','SYSTEM_ADMINISTRATOR','ADMINISTRATOR',
                              'PROJECT_TEAM_COORDINATOR','PROJECT_COORDINATOR',
                              'PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD')) AS broad_scope
            )
            SELECT plan.plan_id,plan.project_id,plan.plan_name,plan.plan_status,
                   plan.current_version_number,plan.baseline_version_number,
                   project.project_code,project.project_name,
                   COALESCE(NULLIF(updater.display_name,''),updater.email,'Unknown user') AS updated_by,
                   plan.updated_at
            FROM project_flowhive_plans plan
            JOIN projects project ON project.project_id=plan.project_id
            LEFT JOIN app_users updater ON updater.user_id=plan.updated_by_user_id
            CROSS JOIN actor
            WHERE (@project_id::uuid IS NULL OR plan.project_id=@project_id)
              AND actor.can_view
              AND (
                  actor.broad_scope
                  OR project.project_manager_user_id=@actor
                  OR EXISTS(SELECT 1 FROM project_assignments assignment
                            WHERE assignment.project_id=plan.project_id AND assignment.user_id=@actor)
              )
            ORDER BY plan.updated_at DESC,plan.plan_name
            LIMIT 500;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actor", actorUserId);
        command.Parameters.Add(new NpgsqlParameter("project_id", NpgsqlDbType.Uuid)
        {
            Value = projectId.HasValue ? projectId.Value : DBNull.Value
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ProjectFlowHivePersistedPlanSummary>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadSummary(reader));
        return rows;
    }

    public async Task<ProjectFlowHivePersistedPlan?> LoadAsync(
        Guid actorUserId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await CanViewPlanAsync(connection, actorUserId, planId, cancellationToken)) return null;
        const string sql = """
            SELECT plan.plan_id,plan.project_id,plan.plan_name,plan.plan_status,
                   plan.current_version_number,plan.baseline_version_number,
                   project.project_code,project.project_name,
                   COALESCE(NULLIF(updater.display_name,''),updater.email,'Unknown user') AS updated_by,
                   plan.updated_at,
                   version.plan_payload::text,version.schedule_payload::text,version.validation_payload::text,
                   version.source_kind,version.celar_ai_provider_code,version.celar_ai_correlation_id,
                   version.celar_ai_confidence,version.created_at
            FROM project_flowhive_plans plan
            JOIN projects project ON project.project_id=plan.project_id
            JOIN project_flowhive_plan_versions version
              ON version.plan_id=plan.plan_id AND version.version_number=plan.current_version_number
            LEFT JOIN app_users updater ON updater.user_id=plan.updated_by_user_id
            WHERE plan.plan_id=@plan_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("plan_id", planId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var summary = ReadSummary(reader);
        var plan = JsonSerializer.Deserialize<ProjectFlowHivePlanRequest>(reader.GetString(10), Json);
        var schedule = JsonSerializer.Deserialize<ProjectFlowHiveScheduleResult>(reader.GetString(11), Json);
        var validation = JsonSerializer.Deserialize<ProjectFlowHivePlanValidationResult>(reader.GetString(12), Json);
        if (plan is null || schedule is null || validation is null) return null;
        return new(
            summary,
            plan with { PlanId = summary.PlanId },
            schedule,
            validation,
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetDecimal(16),
            reader.GetFieldValue<DateTimeOffset>(17));
    }

    public async Task<ProjectFlowHivePersistenceResult> SaveDraftAsync(
        Guid actorUserId,
        ProjectFlowHivePlanRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ProjectFlowHiveScheduleEngine.Validate(request);
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(request);
        if (!validation.Valid || !schedule.Valid)
            return new(false, "plan_validation_failed", request.PlanId, null, "Correct every validation issue before saving the FlowHive draft.");
        if (!request.ProjectId.HasValue || request.ProjectId.Value == Guid.Empty)
            return new(false, "project_required", request.PlanId, null, "An authorized canonical project is required.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await CanManageProjectAsync(connection, transaction, actorUserId, request.ProjectId.Value, cancellationToken))
            return new(false, "forbidden", request.PlanId, null, "The current user cannot manage FlowHive plans for this project.");

        var planId = request.PlanId.GetValueOrDefault();
        var priorState = "null";
        var currentVersion = 0;
        if (planId == Guid.Empty)
        {
            planId = Guid.NewGuid();
            const string insertPlan = """
                INSERT INTO project_flowhive_plans(
                    plan_id,project_id,plan_name,plan_status,current_version_number,
                    created_by_user_id,updated_by_user_id)
                VALUES(@plan_id,@project_id,@plan_name,'draft',0,@actor,@actor);
                """;
            await using var insert = new NpgsqlCommand(insertPlan, connection, transaction);
            AddPlanIdentity(insert, planId, request.ProjectId.Value, request.PlanName, actorUserId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string lockPlan = """
                SELECT current_version_number,to_jsonb(plan)::text
                FROM project_flowhive_plans plan
                WHERE plan_id=@plan_id AND project_id=@project_id
                FOR UPDATE;
                """;
            await using var select = new NpgsqlCommand(lockPlan, connection, transaction);
            select.Parameters.AddWithValue("plan_id", planId);
            select.Parameters.AddWithValue("project_id", request.ProjectId.Value);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new(false, "plan_not_found", planId, null, "The FlowHive plan is not available in the authorized project.");
            currentVersion = reader.GetInt32(0);
            priorState = reader.GetString(1);
            await reader.CloseAsync();
        }

        var nextVersion = currentVersion + 1;
        var persistedRequest = request with { PlanId = planId };
        const string insertVersion = """
            INSERT INTO project_flowhive_plan_versions(
                plan_id,project_id,version_number,revision_label,source_kind,
                plan_payload,schedule_payload,validation_payload,
                celar_ai_provider_code,celar_ai_correlation_id,celar_ai_confidence,
                created_by_user_id)
            VALUES(
                @plan_id,@project_id,@version,@revision_label,@source_kind,
                @plan_payload,@schedule_payload,@validation_payload,
                @provider,@correlation,@confidence,@actor);
            """;
        await using (var insert = new NpgsqlCommand(insertVersion, connection, transaction))
        {
            insert.Parameters.AddWithValue("plan_id", planId);
            insert.Parameters.AddWithValue("project_id", request.ProjectId.Value);
            insert.Parameters.AddWithValue("version", nextVersion);
            insert.Parameters.AddWithValue("revision_label", Clean(request.RevisionLabel, 160));
            insert.Parameters.AddWithValue("source_kind", SourceKind(request.SourceKind));
            AddJson(insert, "plan_payload", persistedRequest);
            AddJson(insert, "schedule_payload", schedule);
            AddJson(insert, "validation_payload", validation);
            insert.Parameters.AddWithValue("provider", Clean(request.CelarAiProviderCode, 120));
            insert.Parameters.AddWithValue("correlation", Clean(request.CelarAiCorrelationId, 180));
            insert.Parameters.Add(new NpgsqlParameter("confidence", NpgsqlDbType.Numeric)
            {
                Value = request.CelarAiConfidence.HasValue ? request.CelarAiConfidence.Value : DBNull.Value
            });
            insert.Parameters.AddWithValue("actor", actorUserId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        const string updatePlan = """
            UPDATE project_flowhive_plans
            SET plan_name=@plan_name,plan_status='draft',current_version_number=@version,updated_by_user_id=@actor
            WHERE plan_id=@plan_id;
            """;
        await using (var update = new NpgsqlCommand(updatePlan, connection, transaction))
        {
            update.Parameters.AddWithValue("plan_name", Clean(request.PlanName, 240, "Governed project plan"));
            update.Parameters.AddWithValue("version", nextVersion);
            update.Parameters.AddWithValue("actor", actorUserId);
            update.Parameters.AddWithValue("plan_id", planId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertAuditAsync(connection, transaction, request.ProjectId.Value, planId, nextVersion,
            "draft_version_saved", actorUserId, priorState, JsonSerializer.Serialize(new { nextVersion, request.PlanName }, Json),
            request.CelarAiCorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, "flowhive_draft_version_saved", planId, nextVersion,
            $"FlowHive draft version {nextVersion} was saved with immutable validation and schedule evidence.");
    }

    public async Task<ProjectFlowHivePersistenceResult> EstablishBaselineAsync(
        Guid actorUserId,
        Guid planId,
        string? approvalNote,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var note = Clean(approvalNote, 4000);
        if (note.Length < 10)
            return new(false, "review_note_required", planId, null, "Enter a review note of at least 10 characters before establishing a baseline.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string lockSql = """
            SELECT plan.project_id,plan.current_version_number,to_jsonb(plan)::text
            FROM project_flowhive_plans plan WHERE plan.plan_id=@plan_id FOR UPDATE;
            """;
        Guid projectId;
        int version;
        string prior;
        await using (var select = new NpgsqlCommand(lockSql, connection, transaction))
        {
            select.Parameters.AddWithValue("plan_id", planId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new(false, "plan_not_found", planId, null, "The FlowHive plan was not found.");
            projectId = reader.GetGuid(0);
            version = reader.GetInt32(1);
            prior = reader.GetString(2);
            await reader.CloseAsync();
        }
        if (expectedVersion.HasValue && expectedVersion.Value != version)
            return new(false, "version_conflict", planId, version, "The plan changed after review. Reload and review the current version before baselining.");
        if (!await CanBaselineProjectAsync(connection, transaction, actorUserId, projectId, cancellationToken))
            return new(false, "forbidden", planId, version, "The current user cannot approve a FlowHive baseline for this project.");

        const string reviewSql = """
            INSERT INTO project_flowhive_plan_reviews(
                plan_id,project_id,version_number,decision,review_note,
                actual_reviewer_user_id,effective_reviewer_user_id)
            VALUES(@plan_id,@project_id,@version,'approved_for_baseline',@note,@actor,@actor)
            ON CONFLICT(plan_id,version_number,decision) DO NOTHING;

            UPDATE project_flowhive_plans
            SET plan_status='baselined',baseline_version_number=@version,
                baselined_by_user_id=@actor,baselined_at=NOW(),updated_by_user_id=@actor
            WHERE plan_id=@plan_id;
            """;
        await using (var command = new NpgsqlCommand(reviewSql, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("note", note);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertAuditAsync(connection, transaction, projectId, planId, version,
            "baseline_established", actorUserId, prior,
            JsonSerializer.Serialize(new { baselineVersion = version, approvalNoteRecorded = true }, Json),
            string.Empty, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, "flowhive_baseline_established", planId, version,
            $"FlowHive version {version} is now the reviewer-approved baseline.");
    }

    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
            throw new InvalidOperationException("Project FlowHive database configuration is incomplete.");
        var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> CanViewPlanAsync(
        NpgsqlConnection connection, Guid actor, Guid planId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM project_flowhive_plans plan
                JOIN projects project ON project.project_id=plan.project_id
                WHERE plan.plan_id=@plan_id
                  AND EXISTS(
                      SELECT 1 FROM app_user_role_assignments ura
                      JOIN app_roles role ON role.app_role_id=ura.app_role_id AND role.is_active=TRUE
                      JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id
                      JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
                      WHERE ura.user_id=@actor AND ura.is_active=TRUE
                        AND permission.permission_code='VIEW_PROJECT_FLOWHIVE_066')
                  AND (
                    project.project_manager_user_id=@actor
                    OR EXISTS(SELECT 1 FROM project_assignments a WHERE a.project_id=project.project_id AND a.user_id=@actor)
                    OR EXISTS(SELECT 1 FROM app_user_role_assignments ura
                              JOIN app_roles role ON role.app_role_id=ura.app_role_id AND role.is_active=TRUE
                              WHERE ura.user_id=@actor AND ura.is_active=TRUE AND role.role_code IN (
                                  'SUPER_ADMINISTRATOR','SYSTEM_ADMINISTRATOR','ADMINISTRATOR','PROJECT_TEAM_COORDINATOR',
                                  'PROJECT_COORDINATOR','PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD'))));
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("actor", actor);
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    private static Task<bool> CanManageProjectAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actor, Guid projectId,
        CancellationToken cancellationToken) =>
        HasProjectPermissionAsync(connection, transaction, actor, projectId, "MANAGE_PROJECT_FLOWHIVE_066", cancellationToken);

    private static Task<bool> CanBaselineProjectAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actor, Guid projectId,
        CancellationToken cancellationToken) =>
        HasProjectPermissionAsync(connection, transaction, actor, projectId, "BASELINE_PROJECT_FLOWHIVE_066", cancellationToken);

    private static async Task<bool> HasProjectPermissionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actor, Guid projectId,
        string permission, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM projects project
                WHERE project.project_id=@project_id
                  AND EXISTS(
                      SELECT 1 FROM app_user_role_assignments ura
                      JOIN app_roles role ON role.app_role_id=ura.app_role_id AND role.is_active=TRUE
                      JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id
                      JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
                      WHERE ura.user_id=@actor AND ura.is_active=TRUE
                        AND permission.permission_code=@permission)
                  AND (
                      project.project_manager_user_id=@actor
                      OR EXISTS(
                          SELECT 1 FROM app_user_role_assignments ura
                          JOIN app_roles role ON role.app_role_id=ura.app_role_id AND role.is_active=TRUE
                          JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id
                          JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
                          WHERE ura.user_id=@actor AND ura.is_active=TRUE
                            AND permission.permission_code=@permission
                            AND role.role_code IN (
                                'SUPER_ADMINISTRATOR','SYSTEM_ADMINISTRATOR','ADMINISTRATOR',
                                'PROJECT_TEAM_COORDINATOR','PROJECT_COORDINATOR',
                                'PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD'))));
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("permission", permission);
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    private static ProjectFlowHivePersistedPlanSummary ReadSummary(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
        reader.GetFieldValue<DateTimeOffset>(9));

    private static void AddPlanIdentity(NpgsqlCommand command, Guid planId, Guid projectId, string? name, Guid actor)
    {
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("plan_name", Clean(name, 240, "Governed project plan"));
        command.Parameters.AddWithValue("actor", actor);
    }

    private static void AddJson(NpgsqlCommand command, string name, object value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, Json)
        });

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid projectId, Guid planId, int version,
        string eventCode, Guid actor, string priorState, string newState, string? correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_flowhive_audit_events(
                project_id,plan_id,version_number,event_code,
                actual_actor_user_id,effective_actor_user_id,
                prior_state,new_state,correlation_id)
            VALUES(@project_id,@plan_id,@version,@event,@actor,@actor,
                   @prior::jsonb,@next::jsonb,@correlation);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("event", eventCode);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("prior", string.IsNullOrWhiteSpace(priorState) ? "null" : priorState);
        command.Parameters.AddWithValue("next", string.IsNullOrWhiteSpace(newState) ? "null" : newState);
        command.Parameters.AddWithValue("correlation", Clean(correlationId, 180));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string SourceKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "celar_ai" => "celar_ai",
        "canonical_snapshot" => "canonical_snapshot",
        _ => "manual"
    };

    private static string Clean(string? value, int maximumLength, string fallback = "")
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}

using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

internal sealed record FlowHiveAutomationRequest(bool Enabled, Guid? ExpectedVersion);

internal static partial class ProjectFlowHiveAiPlannerOrchestrationModule
{
    internal const string AutomationMigration = "122_flowhive_automatic_first_draft";
    internal const string AutomationNotification = "FLOWHIVE_FIRST_DRAFT_READY";

    private static void MapAutomationEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/project-flowhive/projects/{projectId:guid}/ai-planner/automation",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetAutomationAsync);
        endpoints.MapPut("/api/project-flowhive/projects/{projectId:guid}/ai-planner/automation",
            (Guid projectId, FlowHiveAutomationRequest request, HttpContext context, CancellationToken token) =>
                SetAutomationAsync(projectId, request, context, false, token));
        endpoints.MapPut("/api/project-flowhive/projects/{projectId:guid}/ai-planner/automation/default",
            (Guid projectId, FlowHiveAutomationRequest request, HttpContext context, CancellationToken token) =>
                SetAutomationAsync(projectId, request, context, true, token));
    }

    internal static async Task<bool> AutomationReadyAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration);", connection);
        command.Parameters.AddWithValue("migration", AutomationMigration);
        return await command.ExecuteScalarAsync(token) is true;
    }

    private static IResult AutomationUnavailable() => Results.Json(new
    {
        status = "flowhive_automation_migration_required", stateChanged = false,
        message = "Automatic planning is not installed yet. You can still start AI Planner manually."
    }, statusCode: 503);

    private static async Task<IResult> GetAutomationAsync(Guid projectId, HttpContext context, CancellationToken token)
    {
        var opened = await OpenAsync(projectId, context, requireEdit: false, token);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await AutomationReadyAsync(connection, token)) return AutomationUnavailable();
        var access = await ProjectPlanningAccessResolver.ResolveAsync(connection, context, projectId, "066", token);
        var actor = opened.Access!;
        // A view-as session is always read-only, including an administrator viewing another role.
        var own = actor.ActualUserId == actor.EffectiveUserId && !access.IsViewAs;
        var durableAccess = own
            ? await ProjectPlanningAccessResolver.ResolveForActorAsync(connection, actor.ActualUserId, projectId, "066", token)
            : access;
        var state = await ReadAutomationAsync(connection, projectId, token);
        return Results.Ok(new
        {
            projectId, state.Enabled, state.RowVersion, state.Status, message = AutomationMessage(state.Status),
            state.RunId, state.CreatedAt, state.CompletedAt, state.Phases,
            canManage = own && durableAccess.CanAdministerPlanner,
            defaults = new { enabled = state.DefaultEnabled, rowVersion = state.DefaultVersion,
                appliesAfter = state.AppliesAfter, canManage = own && durableAccess.IsAdministrator }
        });
    }

    private static async Task<IResult> SetAutomationAsync(Guid projectId, FlowHiveAutomationRequest request,
        HttpContext context, bool defaults, CancellationToken token)
    {
        var opened = await OpenAsync(projectId, context, requireEdit: true, token);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await AutomationReadyAsync(connection, token)) return AutomationUnavailable();
        var actor = opened.Access!;
        if (actor.ActualUserId != actor.EffectiveUserId || ProjectPulseActualSessionAuthority.IsViewAs(context))
            return Results.Json(new { status = "view_as_read_only", stateChanged = false }, statusCode: 403);
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(connection, actor.ActualUserId, projectId, "066", token);
        if (!access.CanAdministerPlanner || (defaults && !access.IsAdministrator))
            return Results.Json(new { status = "flowhive_automation_forbidden", stateChanged = false,
                message = defaults ? "Only an administrator can change the default for new projects."
                    : "Only the assigned PM, authorized PM lead or administrator can change automatic planning." }, statusCode: 403);
        try
        {
            await SetAutomationPreferenceAsync(connection, projectId, actor.ActualUserId, request, defaults, token);
        }
        catch (PlannerConflict exception)
        {
            return Results.Conflict(new { status = exception.Code, message = exception.Message, stateChanged = false });
        }
        return await GetAutomationAsync(projectId, context, token);
    }

    internal static async Task SetAutomationPreferenceAsync(NpgsqlConnection connection, Guid projectId,
        Guid actor, FlowHiveAutomationRequest request, bool defaults, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(token);
        await LockAutomationProjectAsync(connection, transaction, projectId, token);
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(connection, actor, projectId, "066", token);
        if (!access.CanAdministerPlanner || (defaults && !access.IsAdministrator))
            throw new PlannerConflict("flowhive_automation_forbidden", "Project management permission changed. Reload before changing this setting.");
        if (!await ProjectFlowHiveLifecycle.LockActiveAsync(connection, transaction, projectId, token))
            throw new PlannerConflict("flowhive_project_archived", "Archived projects cannot enable automatic planning.");
        Guid? currentVersion;
        await using (var current = new NpgsqlCommand(defaults
            ? "SELECT row_version FROM project_flowhive_auto_plan_defaults WHERE singleton=TRUE FOR UPDATE;"
            : "SELECT row_version FROM project_flowhive_auto_plans WHERE project_id=@project FOR UPDATE;", connection, transaction))
        {
            current.Parameters.AddWithValue("project", projectId);
            currentVersion = await current.ExecuteScalarAsync(token) as Guid?;
        }
        if (currentVersion != request.ExpectedVersion)
            throw new PlannerConflict("flowhive_automation_version_conflict", "The automatic planning setting changed. Reload it before saving.");
        await using (var save = new NpgsqlCommand(defaults ? """
            UPDATE project_flowhive_auto_plan_defaults SET enabled=@enabled,authorized_by_user_id=@actor,
                applies_after=CASE WHEN @enabled AND NOT enabled THEN clock_timestamp() ELSE applies_after END,
                row_version=gen_random_uuid(),updated_at=NOW() WHERE singleton=TRUE;
            """ : """
            INSERT INTO project_flowhive_auto_plans(project_id,enabled,authorized_by_user_id,source,status)
            VALUES(@project,@enabled,@actor,'project_setting',CASE WHEN @enabled THEN 'waiting_documents' ELSE 'disabled' END)
            ON CONFLICT(project_id) DO UPDATE SET enabled=@enabled,authorized_by_user_id=@actor,source='project_setting',
                status=CASE WHEN project_flowhive_auto_plans.run_id IS NOT NULL THEN project_flowhive_auto_plans.status
                    WHEN @enabled THEN 'waiting_documents' ELSE 'disabled' END,
                row_version=gen_random_uuid(),updated_at=NOW(),checked_at=NULL;
            """, connection, transaction))
        {
            save.Parameters.AddWithValue("project", projectId);
            save.Parameters.AddWithValue("actor", actor);
            save.Parameters.AddWithValue("enabled", request.Enabled);
            await save.ExecuteNonQueryAsync(token);
        }
        if (!defaults && !request.Enabled)
        {
            await using var stop = new NpgsqlCommand($"""
                UPDATE {RunTable} r SET status='needs_attention',phase='cancelled',progress_percent=100,
                    completed_at=NOW(),updated_at=NOW(),row_version=gen_random_uuid(),
                    blockers='["Automatic planning was turned off. Existing work was preserved."]'::jsonb
                FROM project_flowhive_auto_plans a WHERE a.project_id=@project AND a.run_id=r.run_id
                    AND r.status IN ('queued','processing','generating');
                """, connection, transaction);
            stop.Parameters.AddWithValue("project", projectId);
            await stop.ExecuteNonQueryAsync(token);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO project_flowhive_auto_plan_events(project_id,actor_user_id,event_code,enabled)
            VALUES(@project,@actor,@event,@enabled);
            """, connection, transaction))
        {
            audit.Parameters.Add("project", NpgsqlDbType.Uuid).Value = defaults ? DBNull.Value : projectId;
            audit.Parameters.AddWithValue("actor", actor);
            audit.Parameters.AddWithValue("event", defaults ? "new_project_default_changed" : "project_setting_changed");
            audit.Parameters.AddWithValue("enabled", request.Enabled);
            await audit.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    private static async Task LockAutomationProjectAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid project, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='15s'; SELECT pg_advisory_xact_lock(hashtextextended(@project::text,734));", connection, transaction);
        command.Parameters.AddWithValue("project", project);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task ProcessAutomationsAsync(CancellationToken token)
    {
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0) return;
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(token);
        if (!await AutomationReadyAsync(connection, token)) return;
        await EnrollNewProjectsAsync(connection, token);
        var projects = new List<Guid>();
        await using (var query = new NpgsqlCommand("""
            SELECT project_id FROM project_flowhive_auto_plans WHERE enabled=TRUE AND run_id IS NULL
                AND status NOT IN ('existing_plan','needs_attention','archived')
            ORDER BY checked_at NULLS FIRST,project_id LIMIT 25;
            """, connection))
        await using (var reader = await query.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) projects.Add(reader.GetGuid(0));
        foreach (var project in projects) await TryQueueAutomaticPlanAsync(connection, project, token);
        await QueueAutomaticReadyNotificationsAsync(connection, token);
    }

    internal static async Task EnrollNewProjectsAsync(NpgsqlConnection connection, CancellationToken token)
    {
        // Prospective opt-in only. ON CONFLICT also preserves a PM's explicit opt-out.
        // Current administrator authority is required; a revoked default cannot enroll projects.
        await using var command = new NpgsqlCommand("""
            WITH enrolled AS (
              INSERT INTO project_flowhive_auto_plans(project_id,enabled,authorized_by_user_id,source)
              SELECT p.project_id,TRUE,d.authorized_by_user_id,'new_project_default'
              FROM projects p CROSS JOIN project_flowhive_auto_plan_defaults d
              JOIN app_users u ON u.user_id=d.authorized_by_user_id AND u.is_active=TRUE
              WHERE d.singleton=TRUE AND d.enabled=TRUE AND p.created_at>=d.applies_after
                AND lower(trim(COALESCE(p.status,''))) NOT IN ('closed','completed','cancelled','canceled','archived')
                AND EXISTS(SELECT 1 FROM app_user_role_assignments a JOIN app_roles r USING(app_role_id)
                  WHERE a.user_id=u.user_id AND a.is_active=TRUE AND r.is_active=TRUE
                    AND upper(r.role_code) IN ('ADMINISTRATOR','SYSTEM_ADMINISTRATOR','SUPER_ADMINISTRATOR'))
              ON CONFLICT(project_id) DO NOTHING RETURNING project_id,authorized_by_user_id
            )
            INSERT INTO project_flowhive_auto_plan_events(project_id,actor_user_id,event_code,enabled)
              SELECT project_id,authorized_by_user_id,'new_project_default_applied',TRUE FROM enrolled;
            """, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<Guid?> TryQueueAutomaticPlanAsync(NpgsqlConnection connection, Guid project,
        CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(token);
        await LockAutomationProjectAsync(connection, transaction, project, token);
        Guid actor;
        await using (var query = new NpgsqlCommand("""
            SELECT authorized_by_user_id FROM project_flowhive_auto_plans
            WHERE project_id=@project AND enabled=TRUE AND run_id IS NULL
                AND status NOT IN ('existing_plan','needs_attention','archived') FOR UPDATE;
            """, connection, transaction))
        {
            query.Parameters.AddWithValue("project", project);
            if (await query.ExecuteScalarAsync(token) is not Guid user) return null;
            actor = user;
        }
        async Task<Guid?> Wait(string status)
        {
            await using var update = new NpgsqlCommand("UPDATE project_flowhive_auto_plans SET status=@status,checked_at=NOW() WHERE project_id=@project;", connection, transaction);
            update.Parameters.AddWithValue("status", status); update.Parameters.AddWithValue("project", project);
            await update.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
            return null;
        }
        if (!await ProjectFlowHiveLifecycle.LockActiveAsync(connection, transaction, project, token)) return await Wait("archived");
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(connection, actor, project, "066", token);
        if (!access.CanAdministerPlanner) return await Wait("needs_attention");
        await using (var existing = new NpgsqlCommand($"""
            SELECT EXISTS(SELECT 1 FROM project_flowhive_working_copies WHERE project_id=@project)
                OR EXISTS(SELECT 1 FROM project_flowhive_plans WHERE project_id=@project)
                OR EXISTS(SELECT 1 FROM {RunTable} WHERE project_id=@project);
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue("project", project);
            if (await existing.ExecuteScalarAsync(token) is true) return await Wait("existing_plan");
        }
        await using (var manager = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM projects p JOIN app_users u ON u.user_id=p.project_manager_user_id AND u.is_active=TRUE WHERE p.project_id=@project);", connection, transaction))
        {
            manager.Parameters.AddWithValue("project", project);
            if (await manager.ExecuteScalarAsync(token) is not true) return await Wait("waiting_pm");
        }
        var plan = await LoadProjectSeedAsync(connection, project, token);
        if (plan?.ProjectStartDate is null) return await Wait("waiting_start_date");
        if (plan.ProjectEndDate < plan.ProjectStartDate) return await Wait("needs_attention");
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (release.IsCandidate && !PulseAiProtectedTestCandidatePolicy.AllowsPrivateDocumentProcessing(release))
            return await Wait("preparation_paused");
        await ProjectPlanningDocumentPreparation.QueueAsync(connection, transaction, actor, project, null, token);
        var documents = await ProjectPlanningDocumentResolver.ReadCurrentAsync(connection, project, token);
        if (documents.HasTerminalProcessingFailure || documents.SelectedDocuments.Any(d => !d.EngineeringVisible))
            return await Wait("needs_attention");
        if (!documents.ReadyForGeneration) return await Wait("waiting_documents");
        var run = await GetOrCreateRunInTransactionAsync(connection, transaction, project,
            new ProjectFlowHiveAiPlannerRunRequest(plan, "Use the current SOW Service Overview and Scope of Services, supported by the GSD and attached project documents, to create the first detailed five-phase WBS for PM review.",
                HasWorkingCopyExpectation: true), new PlannerAccess(actor, actor), $"auto-first-draft-{project:N}", token);
        await using (var update = new NpgsqlCommand("UPDATE project_flowhive_auto_plans SET run_id=@run,status='generating',checked_at=NOW(),updated_at=NOW() WHERE project_id=@project AND run_id IS NULL;", connection, transaction))
        {
            update.Parameters.AddWithValue("run", run); update.Parameters.AddWithValue("project", project);
            if (await update.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Automatic run consent changed.");
        }
        await transaction.CommitAsync(token);
        return run;
    }

    private sealed record AutomationState(bool Enabled, Guid? RowVersion, string Status, Guid? RunId,
        DateTimeOffset? CreatedAt, DateTimeOffset? CompletedAt, object? Phases,
        bool DefaultEnabled, Guid DefaultVersion, DateTimeOffset? AppliesAfter);

    private static async Task<AutomationState> ReadAutomationAsync(NpgsqlConnection connection, Guid project, CancellationToken token)
    {
        bool enabled, defaults; Guid? version, run; Guid defaultVersion;
        string status; DateTimeOffset? applies;
        await using (var query = new NpgsqlCommand("""
            SELECT COALESCE(a.enabled,FALSE),a.row_version,COALESCE(a.status,'disabled'),a.run_id,
                d.enabled,d.row_version,d.applies_after,p.status
            FROM projects p CROSS JOIN project_flowhive_auto_plan_defaults d
            LEFT JOIN project_flowhive_auto_plans a ON a.project_id=p.project_id
            WHERE p.project_id=@project AND d.singleton=TRUE;
            """, connection))
        {
            query.Parameters.AddWithValue("project", project);
            await using var reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Automatic planner project was not found.");
            enabled = reader.GetBoolean(0); version = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            status = reader.GetString(2); run = reader.IsDBNull(3) ? null : reader.GetGuid(3);
            defaults = reader.GetBoolean(4); defaultVersion = reader.GetGuid(5);
            applies = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6);
            if (ProjectFlowHiveLifecycle.IsArchived(reader.GetString(7))) status = "archived";
        }
        var stored = run.HasValue ? await LoadRunAsync(connection, project, run.Value, token) : null;
        if (status != "archived") status = stored is null ? enabled ? status : "disabled"
            : !stored.Terminal ? "generating" : stored.Phase == "working_draft_ready" ? "ready_for_review"
            : stored.Phase == "cancelled" ? "cancelled" : "needs_attention";
        // The project team sees only status/timers. No raw scope, partial plan, provider diagnostics or actor-private run is exposed.
        return new(enabled, version, status, run, stored?.CreatedAt, stored?.CompletedAt,
            stored is null ? null : (stored.PhaseCheckpoint ?? FlowHiveSequentialState.Empty(stored.SourceVersionFingerprint)).Progress(stored.Terminal, stored.CompletedAt),
            defaults, defaultVersion, applies);
    }

    internal static string AutomationMessage(string status) => status switch
    {
        "disabled" => "Automatic planning is off. Enable it to create this project's first AI draft when its documents are ready.",
        "waiting_documents" => "Waiting for the current SOW and supporting documents to be prepared and approved. You can leave this page.",
        "waiting_start_date" => "Add the project start date in Work Register so the AI plan can forecast its schedule.",
        "waiting_pm" => "Assign a Project Manager in Work Register before automatic planning starts.",
        "preparation_paused" => "Document preparation is unavailable in this release. An administrator needs to restore it.",
        "generating" => "Creating the first AI draft. Each phase is saved before the next begins.",
        "ready_for_review" => "Your first AI draft is ready. Load the working copy to review tasks, estimates and dates before approving a baseline.",
        "existing_plan" => "This project already has a plan or planning run. Automatic planning will not replace it. Use AI Planner for an explicit reviewed update.",
        "cancelled" => "Automatic generation was cancelled. Use AI Planner when you are ready to start a new reviewed run.",
        "archived" => "This project is closed. Its plan and history are archived and no automatic generation will start.",
        _ => "Automatic planning needs attention. Check document readiness, project dates and PM permissions. It will not start another run automatically."
    };

    internal static async Task<bool> AutomaticRunAllowedAsync(NpgsqlConnection connection, Guid runId,
        Guid actor, Guid project, CancellationToken token)
    {
        if (!await AutomationReadyAsync(connection, token)) return true; // Legacy/manual runs do not require opt-in.
        await using var query = new NpgsqlCommand("SELECT enabled FROM project_flowhive_auto_plans WHERE run_id=@run;", connection);
        query.Parameters.AddWithValue("run", runId);
        var enabled = await query.ExecuteScalarAsync(token);
        if (enabled is null) return true;
        if (enabled is not true) return false;
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(connection, actor, project, "066", token);
        return access.CanAdministerPlanner;
    }

    private static async Task<bool> AutomaticDraftWouldReplacePlanAsync(NpgsqlConnection connection, Guid run,
        Guid project, CancellationToken token)
    {
        if (!await AutomationReadyAsync(connection, token)) return false;
        await using var query = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM project_flowhive_auto_plans WHERE run_id=@run)
                AND (EXISTS(SELECT 1 FROM project_flowhive_plans WHERE project_id=@project)
                    OR EXISTS(SELECT 1 FROM project_flowhive_working_copies WHERE project_id=@project));
            """, connection);
        query.Parameters.AddWithValue("run", run); query.Parameters.AddWithValue("project", project);
        return await query.ExecuteScalarAsync(token) is true;
    }

    internal static async Task QueueAutomaticReadyNotificationsAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand($"""
            WITH accepted AS (
              INSERT INTO enterprise_notification_events(policy_code,source_module,source_event_id,idempotency_key,
                entity_type,entity_id,project_id,subject_user_id,occurred_at,available_at,payload,ingestion_source,event_status)
              SELECT '{AutomationNotification}','066',r.run_id::text,'flowhive:first-draft:'||r.run_id::text,
                'flowhive_auto_plan',r.run_id,p.project_id,p.project_manager_user_id,r.completed_at,NOW(),
                jsonb_build_object('projectCode',p.project_code,'deepLink','#project-flowhive?projectId='||p.project_id::text),
                'native_bridge',CASE WHEN policy.enabled THEN 'pending' ELSE 'suppressed' END
              FROM project_flowhive_auto_plans a JOIN {RunTable} r ON r.run_id=a.run_id
              JOIN projects p ON p.project_id=a.project_id
              JOIN app_users pm ON pm.user_id=p.project_manager_user_id AND pm.is_active=TRUE
              JOIN enterprise_notification_policies policy ON policy.policy_code='{AutomationNotification}'
              WHERE r.phase='working_draft_ready' AND r.completed_at IS NOT NULL
                AND lower(trim(p.status)) NOT IN ('closed','completed','cancelled','canceled','archived')
              ON CONFLICT(idempotency_key) DO NOTHING RETURNING enterprise_notification_event_id,event_status
            )
            INSERT INTO enterprise_notification_event_history(enterprise_notification_event_id,history_code,event_status,history_metadata)
            SELECT enterprise_notification_event_id,'EVENT_ACCEPTED',event_status,
                jsonb_build_object('sourceModule','066','deliveryAuthority','module_065','providerInvoked',FALSE) FROM accepted;
            """, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<bool> AutomaticNotificationCurrentAsync(NpgsqlConnection connection,
        EnterpriseNotificationEventRow item, CancellationToken token)
    {
        if (item.IngestionSource != "native_bridge" || item.EntityType != "flowhive_auto_plan"
            || item.EntityId is not Guid run || item.ProjectId is not Guid project || item.SubjectUserId is not Guid recipient
            || !await AutomationReadyAsync(connection, token)) return false;
        await using var command = new NpgsqlCommand($"""
            SELECT EXISTS(SELECT 1 FROM project_flowhive_auto_plans a JOIN {RunTable} r ON r.run_id=a.run_id
              JOIN projects p ON p.project_id=a.project_id JOIN app_users pm ON pm.user_id=p.project_manager_user_id
              JOIN project_flowhive_working_copies w ON w.project_id=p.project_id
              WHERE r.run_id=@run AND a.project_id=@project AND r.phase='working_draft_ready'
                AND r.completed_at IS NOT NULL AND pm.user_id=@recipient AND pm.is_active=TRUE
                AND w.row_version=r.saved_working_row_version
                AND lower(trim(p.status)) NOT IN ('closed','completed','cancelled','canceled','archived'));
            """, connection);
        command.Parameters.AddWithValue("run", run); command.Parameters.AddWithValue("project", project);
        command.Parameters.AddWithValue("recipient", recipient);
        return await command.ExecuteScalarAsync(token) is true;
    }
}

using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public sealed record Module025WorkTrackingRequest(int ExpectedRevision, DateOnly? TargetDate, string? Priority,
    string? BlockerReason, Guid? BlockerOwnerUserId, decimal? AuthoringHours);

public static partial class Module025SowGsdModule
{
    private const string WorkTrackingMigration = "118_module025_work_tracking";
    private sealed record Module025BlockerOwner(Guid UserId, string DisplayName);
    private sealed record Module025WorkTrackingSnapshot(Guid EngagementId, int Revision, DateOnly? TargetDate,
        string Priority, string BlockerReason, Guid? BlockerOwnerUserId, string BlockerOwnerDisplayName,
        decimal? AuthoringHours, DateTimeOffset? UpdatedAt, string ActorDisplayName);

    private static void MapModule025WorkTrackingEndpoints(WebApplication app)
    {
        app.MapGet("/api/module025/sow-gsd/work-tracking", (Func<string?, HttpContext, CancellationToken, Task<IResult>>)WorkTrackingBulkAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/work-tracking", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)WorkTrackingAsync);
        app.MapPut("/api/module025/sow-gsd/{engagementId:guid}/work-tracking", (Func<Guid, Module025WorkTrackingRequest, HttpContext, CancellationToken, Task<IResult>>)SaveWorkTrackingAsync);
    }

    private static async Task<bool> WorkTrackingSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration) AND to_regclass('public.module025_work_tracking_events') IS NOT NULL;", connection);
        command.Parameters.AddWithValue("migration", WorkTrackingMigration);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<IResult> WorkTrackingAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        context.Response.Headers.CacheControl = "no-store";
        if (!await WorkTrackingSchemaReadyAsync(connection, cancellationToken)) return Results.Ok(new
        {
            schemaReady = false, canEdit = false, migration = WorkTrackingMigration,
            message = "Work tracking is awaiting its governed database migration.",
            tracking = EmptyWorkTracking(engagementId), blockerOwners = Array.Empty<object>(), history = Array.Empty<object>(), stateChanged = false
        });
        return Results.Ok(await WorkTrackingPanelAsync(connection, null, state.Engagement!, state.Access!, false, cancellationToken));
    }

    private static async Task<object> WorkTrackingPanelAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Module025EngagementRow engagement, Module025AccessContext access, bool stateChanged, CancellationToken cancellationToken)
    {
        var history = await LoadWorkTrackingHistoryAsync(connection, transaction, engagement.EngagementId, cancellationToken);
        var canEdit = engagement.IsActive && engagement.Status != "archived"
            && await CanEditWorkTrackingAsync(connection, transaction, access, engagement.OwnerUserId, cancellationToken);
        var owners = await LoadWorkTrackingBlockerOwnersAsync(connection, transaction, engagement.OwnerUserId, cancellationToken);
        var lastActivity = await WorkTrackingLastActivityAsync(connection, transaction, engagement.EngagementId, engagement.CreatedAt, cancellationToken);
        return new { schemaReady = true, canEdit, tracking = history.FirstOrDefault() ?? EmptyWorkTracking(engagement.EngagementId),
            blockerOwners = owners, history, historyLimit = 30, lastWorkflowActivityAt = lastActivity,
            workflowIdleDays = WorkTrackingIdleDays(lastActivity), stateChanged };
    }

    private static Module025WorkTrackingSnapshot EmptyWorkTracking(Guid engagementId) => new(engagementId, 0, null, "normal", "", null, "", null, null, "");

    // A manager's read scope may include team-lead relationships. Write authority
    // specifically requires this owner's current direct manager relationship.
    private static async Task<bool> CanEditWorkTrackingAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Module025AccessContext access, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (access.IsViewAs) return false;
        if (access.IsAdministrator) return true;
        if (access.IsSolutionArchitect && access.EffectiveUserId == ownerUserId) return true;
        if (!access.IsManager || !access.VisibleSolutionArchitectIds.Contains(ownerUserId)) return false;
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM reporting_relationships relationship
                JOIN app_users manager ON manager.user_id=relationship.manager_user_id AND manager.is_active=TRUE
                WHERE relationship.employee_user_id=@owner AND relationship.manager_user_id=@actor
                  AND relationship.effective_start_date<=CURRENT_DATE
                  AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE));
            """, connection, transaction);
        command.Parameters.AddWithValue("owner", ownerUserId);
        command.Parameters.AddWithValue("actor", access.EffectiveUserId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<IReadOnlyList<Module025BlockerOwner>> LoadWorkTrackingBlockerOwnersAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid ownerUserId, CancellationToken cancellationToken)
    {
        // An arbitrary teammate may not see this owner's record. Assign blockers
        // only to the current author or their current active reporting manager.
        await using var command = new NpgsqlCommand("""
            SELECT person.user_id,COALESCE(NULLIF(person.display_name,''),person.email,'')
            FROM app_users person WHERE person.is_active=TRUE
                AND EXISTS(SELECT 1 FROM app_user_role_assignments assignment JOIN app_roles role ON role.app_role_id=assignment.app_role_id
                    WHERE assignment.user_id=person.user_id AND assignment.is_active=TRUE AND role.is_active=TRUE AND upper(role.role_code)=ANY(@view_roles))
                AND (person.user_id=@owner OR EXISTS(
                SELECT 1 FROM reporting_relationships relationship
                WHERE relationship.employee_user_id=@owner AND relationship.manager_user_id=person.user_id
                  AND relationship.effective_start_date<=CURRENT_DATE
                  AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)))
            ORDER BY 2,person.user_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("owner", ownerUserId);
        command.Parameters.AddWithValue("view_roles", ViewRoles);
        var owners = new List<Module025BlockerOwner>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) owners.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return owners;
    }

    private static string? ValidateWorkTracking(Module025WorkTrackingRequest request)
    {
        if (request.ExpectedRevision < 0) return "Reload the current work tracking revision before saving.";
        if (request.Priority is not ("low" or "normal" or "high" or "urgent")) return "Select Low, Normal, High or Urgent priority.";
        if (request.TargetDate is { } date && (date.Year < 2000 || date.Year > 2100)) return "Enter a target date from 2000 through 2100.";
        var reason = (request.BlockerReason ?? "").Trim();
        if (reason.Length > 2000) return "Limit the blocker description to 2,000 characters.";
        if ((reason.Length > 0) != request.BlockerOwnerUserId.HasValue || request.BlockerOwnerUserId == Guid.Empty)
            return "A blocker needs both a description and an accountable owner. Clear both fields to resolve it.";
        if (request.AuthoringHours is { } hours && (hours < 0 || hours > 1000 || decimal.Round(hours, 2) != hours))
            return "Remaining SA authoring hours must be between 0 and 1,000, with at most two decimal places.";
        return null;
    }

    private static async Task<IResult> SaveWorkTrackingAsync(Guid engagementId, Module025WorkTrackingRequest request,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        if (context.Request.ContentLength is > MaximumRequestBytes) return RequestTooLarge();
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        return await SaveWorkTrackingCoreAsync(connection, engagementId, request, state.Access!, cancellationToken);
    }

    private static async Task<IResult> SaveWorkTrackingCoreAsync(NpgsqlConnection connection, Guid engagementId,
        Module025WorkTrackingRequest request, Module025AccessContext access, CancellationToken cancellationToken)
    {
        if (ValidateWorkTracking(request) is { } invalid) return Results.BadRequest(new { status = "module025_work_tracking_invalid", message = invalid });
        if (access.IsViewAs) return Forbidden("view_as_read_only");
        if (!await WorkTrackingSchemaReadyAsync(connection, cancellationToken))
            return StateConflict("work_tracking_migration_required", "Work tracking is awaiting migration 118. Reload after the governed database update.");
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id::text,725));", connection, transaction))
        {
            guard.Parameters.AddWithValue("id", engagementId);
            await guard.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var recordLock = new NpgsqlCommand("SELECT engagement_id FROM module025_sow_gsd_engagements WHERE engagement_id=@id FOR UPDATE;", connection, transaction))
        {
            recordLock.Parameters.AddWithValue("id", engagementId);
            if (await recordLock.ExecuteScalarAsync(cancellationToken) is null) return Results.NotFound();
        }
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken, transaction);
        if (engagement is null) return Results.NotFound();
        if (!await CanEditWorkTrackingAsync(connection, transaction, access, engagement.OwnerUserId, cancellationToken)) return Forbidden("module025_work_tracking_edit");
        if (!engagement.IsActive || engagement.Status == "archived") return StateConflict("archived_record", "Unarchive this SOW/GSD before changing work tracking.");
        var current = (await LoadWorkTrackingHistoryAsync(connection, transaction, engagementId, cancellationToken)).FirstOrDefault() ?? EmptyWorkTracking(engagementId);
        if (current.Revision != request.ExpectedRevision) return Results.Conflict(new {
            status = "module025_work_tracking_revision_conflict", revision = current.Revision,
            message = "Work tracking changed since you opened it. Reload the latest tracking details before saving." });
        var reason = (request.BlockerReason ?? "").Trim();
        var owners = await LoadWorkTrackingBlockerOwnersAsync(connection, transaction, engagement.OwnerUserId, cancellationToken);
        var blockerOwner = owners.FirstOrDefault(owner => owner.UserId == request.BlockerOwnerUserId);
        if (request.BlockerOwnerUserId.HasValue && blockerOwner is null) return Forbidden("module025_blocker_owner_scope");
        if (current.TargetDate == request.TargetDate && current.Priority == request.Priority && current.BlockerReason == reason
            && current.BlockerOwnerUserId == request.BlockerOwnerUserId && current.AuthoringHours == request.AuthoringHours)
            return Results.Ok(await WorkTrackingPanelAsync(connection, transaction, engagement, access, false, cancellationToken));
        var eventId = Guid.NewGuid();
        await using (var command = new NpgsqlCommand("""
            INSERT INTO module025_work_tracking_events(tracking_event_id,engagement_id,engagement_number,tracking_revision,
                document_revision,owner_user_id,owner_display_name,target_date,priority,blocker_reason,blocker_owner_user_id,
                blocker_owner_display_name,authoring_hours,actor_user_id,actor_display_name)
            VALUES(@event,@id,@number,@revision,@document_revision,@owner,@owner_name,@date,@priority,@reason,
                @blocker_owner,@blocker_name,@hours,@actor,@actor_name);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("event", eventId);
            command.Parameters.AddWithValue("id", engagementId);
            command.Parameters.AddWithValue("number", engagement.EngagementNumber);
            command.Parameters.AddWithValue("revision", checked(current.Revision + 1));
            command.Parameters.AddWithValue("document_revision", engagement.Revision);
            command.Parameters.AddWithValue("owner", engagement.OwnerUserId);
            command.Parameters.AddWithValue("owner_name", engagement.OwnerDisplayName);
            command.Parameters.AddWithValue("date", NpgsqlDbType.Date, request.TargetDate is { } date ? date : DBNull.Value);
            command.Parameters.AddWithValue("priority", request.Priority!);
            command.Parameters.AddWithValue("reason", reason);
            command.Parameters.AddWithValue("blocker_owner", NpgsqlDbType.Uuid, (object?)request.BlockerOwnerUserId ?? DBNull.Value);
            command.Parameters.AddWithValue("blocker_name", blockerOwner?.DisplayName ?? "");
            command.Parameters.AddWithValue("hours", NpgsqlDbType.Numeric, (object?)request.AuthoringHours ?? DBNull.Value);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            command.Parameters.AddWithValue("actor_name", access.DisplayName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, engagement.Revision,
            "work_tracking_updated", "SA work tracking updated without changing the SOW/GSD document revision.",
            new { trackingEventId = eventId, previousTrackingRevision = current.Revision, trackingRevision = current.Revision + 1 }, cancellationToken);
        var response = await WorkTrackingPanelAsync(connection, transaction, engagement, access, true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IReadOnlyList<Module025WorkTrackingSnapshot>> LoadWorkTrackingHistoryAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid engagementId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT engagement_id,tracking_revision,target_date,priority,blocker_reason,blocker_owner_user_id,
                blocker_owner_display_name,authoring_hours,created_at,actor_display_name
            FROM module025_work_tracking_events WHERE engagement_id=@id ORDER BY tracking_revision DESC LIMIT 30;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", engagementId);
        var rows = new List<Module025WorkTrackingSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadWorkTracking(reader));
        return rows;
    }

    // Called only inside the already-authorized ownership-transfer transaction,
    // after the owner changes under the shared advisory/record locks. A blocker
    // assigned to the previous author follows the work; a manager-owned blocker
    // retains its accountable manager. Earlier snapshots remain immutable.
    private static async Task ReassignWorkTrackingAfterTransferAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid engagementId, Guid previousOwnerUserId, Guid newOwnerUserId, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken)
    {
        await using (var readiness = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration) AND to_regclass('public.module025_work_tracking_events') IS NOT NULL;", connection, transaction))
        {
            readiness.Parameters.AddWithValue("migration", WorkTrackingMigration);
            if (await readiness.ExecuteScalarAsync(cancellationToken) is not true) return;
        }
        var eventId = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO module025_work_tracking_events(tracking_event_id,engagement_id,engagement_number,tracking_revision,
                document_revision,owner_user_id,owner_display_name,target_date,priority,blocker_reason,blocker_owner_user_id,
                blocker_owner_display_name,authoring_hours,actor_user_id,actor_display_name)
            SELECT @event,e.engagement_id,e.engagement_number,t.tracking_revision+1,e.revision,e.owner_user_id,e.owner_display_name,
                t.target_date,t.priority,t.blocker_reason,e.owner_user_id,e.owner_display_name,t.authoring_hours,@actor,@actor_name
            FROM module025_sow_gsd_engagements e
            JOIN LATERAL (SELECT * FROM module025_work_tracking_events history WHERE history.engagement_id=e.engagement_id
                ORDER BY tracking_revision DESC LIMIT 1) t ON TRUE
            WHERE e.engagement_id=@id AND e.owner_user_id=@new_owner AND t.blocker_owner_user_id=@previous_owner
            RETURNING document_revision;
            """, connection, transaction);
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("id", engagementId);
        command.Parameters.AddWithValue("new_owner", newOwnerUserId);
        command.Parameters.AddWithValue("previous_owner", previousOwnerUserId);
        command.Parameters.AddWithValue("actor", actorUserId);
        command.Parameters.AddWithValue("actor_name", actorDisplayName);
        if (await command.ExecuteScalarAsync(cancellationToken) is int documentRevision)
            await InsertEventAsync(connection, transaction, engagementId, actorUserId, documentRevision,
                "work_tracking_updated", "The unresolved author-owned blocker followed the SOW/GSD ownership transfer.",
                new { trackingEventId = eventId, reason = "ownership_transfer", previousBlockerOwnerUserId = previousOwnerUserId, blockerOwnerUserId = newOwnerUserId }, cancellationToken);
    }

    private static Module025WorkTrackingSnapshot ReadWorkTracking(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
        reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetDecimal(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8), reader.GetString(9));

    private static int WorkTrackingIdleDays(DateTimeOffset activity) => Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - activity).TotalDays));

    private static async Task<DateTimeOffset> WorkTrackingLastActivityAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid engagementId, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT max(created_at) FROM module025_sow_gsd_events WHERE engagement_id=@id AND event_type<>'work_tracking_updated';", connection, transaction);
        command.Parameters.AddWithValue("id", engagementId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DateTime date ? new DateTimeOffset(date) : createdAt;
    }

    private static async Task<IResult> WorkTrackingBulkAsync(string? engagementIds, HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var tokens = (engagementIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length is < 1 or > 300 || tokens.Any(token => !Guid.TryParse(token, out _)))
            return Results.BadRequest(new { message = "Request tracking for between 1 and 300 SOW/GSD identifiers." });
        var ids = tokens.Select(Guid.Parse).Distinct().ToArray();
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        context.Response.Headers.CacheControl = "no-store";
        if (!await WorkTrackingSchemaReadyAsync(connection, cancellationToken)) return Results.Ok(new
        { schemaReady = false, tracking = Array.Empty<object>(), count = 0, migration = WorkTrackingMigration, stateChanged = false });
        var tracking = await LoadScopedWorkTrackingAsync(connection, ids, access, cancellationToken);
        return Results.Ok(new { schemaReady = true, tracking, count = tracking.Count, stateChanged = false });
    }

    private static async Task<IReadOnlyList<object>> LoadScopedWorkTrackingAsync(NpgsqlConnection connection,
        Guid[] ids, Module025AccessContext access, CancellationToken cancellationToken)
    {
        // One bounded query for the visible cards. No client-supplied team label
        // can widen the same owner scope used by the canonical workspace list.
        await using var command = new NpgsqlCommand("""
            SELECT engagement.engagement_id,COALESCE(tracking.tracking_revision,0),tracking.target_date,
                COALESCE(tracking.priority,'normal'),COALESCE(tracking.blocker_reason,''),tracking.blocker_owner_user_id,
                COALESCE(tracking.blocker_owner_display_name,''),tracking.authoring_hours,tracking.created_at,
                COALESCE(tracking.actor_display_name,''),COALESCE(activity.created_at,engagement.created_at)
            FROM module025_sow_gsd_engagements engagement
            LEFT JOIN LATERAL (SELECT * FROM module025_work_tracking_events t WHERE t.engagement_id=engagement.engagement_id
                ORDER BY tracking_revision DESC LIMIT 1) tracking ON TRUE
            LEFT JOIN LATERAL (SELECT max(e.created_at) created_at FROM module025_sow_gsd_events e
                WHERE e.engagement_id=engagement.engagement_id AND e.event_type<>'work_tracking_updated') activity ON TRUE
            WHERE engagement.engagement_id=ANY(@ids)
                AND (@administrator OR engagement.owner_user_id=@user OR engagement.owner_user_id=ANY(@visible))
            ORDER BY engagement.engagement_id LIMIT 300;
            """, connection);
        command.Parameters.AddWithValue("ids", ids.Take(300).ToArray());
        command.Parameters.AddWithValue("administrator", access.IsAdministrator);
        command.Parameters.AddWithValue("user", access.EffectiveUserId);
        command.Parameters.AddWithValue("visible", access.VisibleSolutionArchitectIds.ToArray());
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tracking = ReadWorkTracking(reader);
            var activity = reader.GetFieldValue<DateTimeOffset>(10);
            rows.Add(new { tracking.EngagementId, tracking.Revision, tracking.TargetDate, tracking.Priority,
                tracking.BlockerReason, tracking.BlockerOwnerUserId, tracking.BlockerOwnerDisplayName,
                tracking.AuthoringHours, tracking.UpdatedAt, lastWorkflowActivityAt = activity, workflowIdleDays = WorkTrackingIdleDays(activity) });
        }
        return rows;
    }
}

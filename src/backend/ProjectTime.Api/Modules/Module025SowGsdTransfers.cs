using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public sealed record Module025SowGsdTransferRequest(Guid TargetOwnerUserId, int ExpectedRevision, string? Reason,
    string? Mode = "permanent", DateOnly? ReturnDate = null);
public sealed record Module025HandoffAcknowledgeRequest(Guid HandoffId, int ExpectedRevision);
public sealed record Module025HandoffReturnRequest(Guid HandoffId, int ExpectedRevision, string? Reason);

public static partial class Module025SowGsdModule
{
    private const string TransferMigrationId = "119_module025_temporary_handoffs";

    private sealed record Module025TransferDestination(Guid UserId, string DisplayName, string DepartmentName,
        string TeamName, IReadOnlyList<Guid> ManagerUserIds);

    // Team names are display labels only. The same active reporting manager is
    // the authoritative team boundary, including for administrator transfers.
    private static bool CanRequestTransfer(Module025AccessContext access, Guid ownerUserId) =>
        !access.IsViewAs && (access.IsAdministrator
            || (access.IsSolutionArchitect && access.EffectiveUserId == ownerUserId)
            || (access.IsManager && access.VisibleSolutionArchitectIds.Contains(ownerUserId)));

    private static async Task<bool> TransferSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT count(*)=2 FROM schema_migrations WHERE migration_id IN (@migration,'116_module025_governed_ownership_transfer');", connection);
        command.Parameters.AddWithValue("migration", TransferMigrationId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<IResult> TransferOptionsAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        var engagement = state.Engagement!;
        var access = state.Access!;
        var allowed = CanRequestTransfer(access, engagement.OwnerUserId);
        var ready = await TransferSchemaReadyAsync(connection, cancellationToken);
        var destinations = allowed && ready
            ? await LoadTransferDestinationsAsync(connection, null, access, engagement.OwnerUserId, cancellationToken)
            : [];
        var latestHandoff = ready ? await LoadHandoffAsync(connection, null, engagementId, null, cancellationToken) : null;
        var activeCoverage = ready ? await LoadActiveCoverageAsync(connection, null, engagementId, cancellationToken) : null;
        var activityBlocker = ready ? TransferStateBlocker(engagement)
            ?? await TransferActivityBlockerAsync(connection, null, engagementId, cancellationToken) : null;
        var returnBlockedReason = activeCoverage is null ? "There is no active temporary coverage to return."
            : !allowed ? "Your current role cannot return this record."
            : activityBlocker
                ?? (activeCoverage.NewOwnerUserId != engagement.OwnerUserId ? "Ownership changed after this coverage began. Ask an administrator to reconcile the handoff."
                    : !destinations.Any(person => person.UserId == activeCoverage.PreviousOwnerUserId)
                        ? "The original owner is no longer an active Solution Architect in this reporting team. The return cannot restore expired access."
                        : null);
        var blockedReason = !allowed ? "Your role or current view does not allow transferring this record."
            : !ready ? "Ownership transfer is awaiting the governed database migration."
            : activeCoverage is not null ? "Return the active temporary coverage before starting another transfer."
            : activityBlocker ?? (destinations.Count == 0 ? "No other active Solution Architect shares this owner's reporting manager. Ask an administrator to verify reporting relationships." : null);
        return Results.Ok(new
        {
            engagementId, revision = engagement.Revision,
            canTransfer = blockedReason is null,
            managerTransfer = access.IsManager && engagement.OwnerUserId != access.EffectiveUserId,
            blockedReason, scope = "same_reporting_manager", destinations, coverageReady = ready,
            latestHandoff, activeCoverage,
            canAcknowledge = ready && latestHandoff is not null && latestHandoff.AcknowledgedAt is null
                && engagement.IsActive && engagement.OwnerUserId == access.EffectiveUserId && access.IsSolutionArchitect && !access.IsViewAs
                && latestHandoff.NewOwnerUserId == engagement.OwnerUserId,
            canReturn = ready && returnBlockedReason is null, returnBlockedReason, stateChanged = false
        });
    }

    private static async Task<IResult> TransferAsync(Guid engagementId, Module025SowGsdTransferRequest request,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        if (context.Request.ContentLength is > MaximumRequestBytes) return RequestTooLarge();
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length is < 5 or > 1000 || request.TargetOwnerUserId == Guid.Empty || request.ExpectedRevision < 1)
            return Results.BadRequest(new { status = "module025_transfer_invalid", message = "Select a teammate, supply the current revision, and enter a handoff reason between 5 and 1,000 characters." });
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!CanRequestTransfer(state.Access!, state.Engagement!.OwnerUserId)) return Forbidden("module025_transfer");
        if (!await TransferSchemaReadyAsync(connection, cancellationToken))
            return StateConflict("transfer_migration_required", "Ownership transfer is awaiting migration 119. Reload after the database update.");
        return await TransferOwnershipAsync(connection, engagementId, request with { Reason = reason }, state.Access!, cancellationToken);
    }

    // A single transaction locks the working record, revalidates the destination,
    // appends the immutable event, and changes only ownership plus its revision.
    // Retained versions, SELL identifiers, task IDs, and original authors survive.
    private static Task<IResult> TransferOwnershipAsync(NpgsqlConnection connection, Guid engagementId,
        Module025SowGsdTransferRequest request, Module025AccessContext access, CancellationToken cancellationToken) =>
        TransferOwnershipCoreAsync(connection, engagementId, request, access, null, cancellationToken);

    private static async Task<IResult> TransferOwnershipCoreAsync(NpgsqlConnection connection, Guid engagementId,
        Module025SowGsdTransferRequest request, Module025AccessContext access, Guid? returnHandoffId, CancellationToken cancellationToken)
    {
        var mode = (request.Mode ?? "permanent").Trim().ToLowerInvariant();
        if (mode is not ("permanent" or "temporary") || (mode == "temporary") != request.ReturnDate.HasValue
            || (request.ReturnDate.HasValue && request.ReturnDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)))
            return Results.BadRequest(new { status = "module025_coverage_invalid", message = "Choose permanent transfer, or temporary coverage with a return date of today or later." });
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length is < 5 or > 1000 || request.ExpectedRevision < 1)
            return Results.BadRequest(new { status = "module025_transfer_invalid", message = "Provide the current revision and a handoff reason between 5 and 1,000 characters." });
        request = request with { Reason = reason, Mode = mode };
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var generationLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id::text,725));", connection, transaction))
        {
            generationLock.Parameters.AddWithValue("id", engagementId);
            await generationLock.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var recordLock = new NpgsqlCommand("SELECT engagement_id FROM module025_sow_gsd_engagements WHERE engagement_id=@id FOR UPDATE;", connection, transaction))
        {
            recordLock.Parameters.AddWithValue("id", engagementId);
            if (await recordLock.ExecuteScalarAsync(cancellationToken) is null) return Results.NotFound();
        }
        var current = await LoadEngagementAsync(connection, engagementId, cancellationToken, transaction);
        if (current is null) return Results.NotFound();
        if (!CanRequestTransfer(access, current.OwnerUserId)) return Forbidden("module025_transfer");
        if (current.Revision != request.ExpectedRevision) return RevisionConflict(current.Revision);
        var blockedReason = TransferStateBlocker(current)
            ?? await TransferActivityBlockerAsync(connection, transaction, engagementId, cancellationToken);
        if (blockedReason is not null) return StateConflict("transfer_blocked", blockedReason);
        var activeCoverage = await LoadActiveCoverageAsync(connection, transaction, engagementId, cancellationToken);
        if (returnHandoffId.HasValue)
        {
            if (activeCoverage is null || activeCoverage.HandoffId != returnHandoffId.Value || activeCoverage.NewOwnerUserId != current.OwnerUserId)
                return StateConflict("coverage_not_current", "This temporary coverage is no longer current. Reload the latest handoff before returning it.");
            request = request with { TargetOwnerUserId = activeCoverage.PreviousOwnerUserId };
        }
        else if (activeCoverage is not null)
            return StateConflict("coverage_already_active", "Return the active temporary coverage before starting another transfer.");
        var destinations = await LoadTransferDestinationsAsync(connection, transaction, access, current.OwnerUserId, cancellationToken);
        var destination = destinations.FirstOrDefault(person => person.UserId == request.TargetOwnerUserId);
        if (destination is null) return Forbidden("module025_transfer_destination");

        var nextRevision = checked(current.Revision + 1);
        var handoffId = Guid.NewGuid();
        long transferEventId;
        // The trigger accepts only an exact event created in THIS transaction.
        // An event from an earlier handoff cannot authorize another owner change.
        const string insertEvent = """
            INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json)
            VALUES(@id,'ownership_transferred',@actor,@revision,@summary,
                @evidence::jsonb || jsonb_build_object('transferTransactionId',txid_current()::text)) RETURNING event_id;
            """;
        await using (var audit = new NpgsqlCommand(insertEvent, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", engagementId);
            audit.Parameters.AddWithValue("actor", access.ActualUserId);
            audit.Parameters.AddWithValue("revision", nextRevision);
            audit.Parameters.AddWithValue("summary", $"SOW/GSD transferred from {current.OwnerDisplayName} to {destination.DisplayName}.");
            audit.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(new
            {
                previousOwnerUserId = current.OwnerUserId, previousOwnerDisplayName = current.OwnerDisplayName,
                previousOwnerDepartmentName = current.OwnerDepartmentName, previousOwnerTeamName = current.OwnerTeamName,
                newOwnerUserId = destination.UserId, newOwnerDisplayName = destination.DisplayName,
                newOwnerDepartmentName = destination.DepartmentName, newOwnerTeamName = destination.TeamName,
                reportingManagerUserIds = destination.ManagerUserIds, reason = request.Reason,
                previousRevision = current.Revision, newRevision = nextRevision,
                transferredByUserId = access.ActualUserId, transferredByDisplayName = access.DisplayName,
                managerTransfer = access.IsManager && current.OwnerUserId != access.EffectiveUserId,
                scope = "same_reporting_manager", current.EngagementNumber,
                handoffId, mode, returnDate = request.ReturnDate, returnOfHandoffId = returnHandoffId
            }));
            transferEventId = (long)(await audit.ExecuteScalarAsync(cancellationToken))!;
        }
        await using (var handoff = new NpgsqlCommand("""
            INSERT INTO module025_sow_gsd_handoffs(handoff_id,engagement_id,transfer_event_id,previous_owner_user_id,
                previous_owner_display_name,new_owner_user_id,new_owner_display_name,handoff_mode,return_date,return_of_handoff_id)
            VALUES(@handoff,@id,@event,@previous,@previous_name,@target,@target_name,@mode,@date,@return_of);
            """, connection, transaction))
        {
            handoff.Parameters.AddWithValue("handoff", handoffId); handoff.Parameters.AddWithValue("id", engagementId);
            handoff.Parameters.AddWithValue("event", transferEventId); handoff.Parameters.AddWithValue("previous", current.OwnerUserId);
            handoff.Parameters.AddWithValue("previous_name", current.OwnerDisplayName); handoff.Parameters.AddWithValue("target", destination.UserId);
            handoff.Parameters.AddWithValue("target_name", destination.DisplayName); handoff.Parameters.AddWithValue("mode", mode);
            handoff.Parameters.AddWithValue("date", NpgsqlTypes.NpgsqlDbType.Date, (object?)request.ReturnDate ?? DBNull.Value);
            handoff.Parameters.AddWithValue("return_of", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)returnHandoffId ?? DBNull.Value);
            await handoff.ExecuteNonQueryAsync(cancellationToken);
        }
        if (returnHandoffId.HasValue)
            await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, nextRevision,
                "handoff_returned", "Temporary coverage returned explicitly with the current working content preserved.",
                new { handoffId = returnHandoffId.Value, newHandoffId = handoffId, previousOwnerUserId = current.OwnerUserId,
                    newOwnerUserId = destination.UserId, reason = request.Reason }, cancellationToken);
        const string update = """
            UPDATE module025_sow_gsd_engagements
            SET owner_user_id=@target,owner_display_name=@name,owner_department_name=@department,owner_team_name=@team,
                revision=revision+1,updated_at=now()
            WHERE engagement_id=@id AND owner_user_id=@previous AND revision=@expected
            RETURNING revision;
            """;
        await using (var command = new NpgsqlCommand(update, connection, transaction))
        {
            command.Parameters.AddWithValue("target", destination.UserId);
            command.Parameters.AddWithValue("name", destination.DisplayName);
            command.Parameters.AddWithValue("department", destination.DepartmentName);
            command.Parameters.AddWithValue("team", destination.TeamName);
            command.Parameters.AddWithValue("id", engagementId);
            command.Parameters.AddWithValue("previous", current.OwnerUserId);
            command.Parameters.AddWithValue("expected", request.ExpectedRevision);
            if (await command.ExecuteScalarAsync(cancellationToken) is null) return RevisionConflict(current.Revision);
        }
        await ReassignWorkTrackingAfterTransferAsync(connection, transaction, engagementId, current.OwnerUserId,
            destination.UserId, access.ActualUserId, access.DisplayName, cancellationToken);
        var notification = await QueueHandoffNotificationAsync(connection, transaction, engagementId, nextRevision,
            returnHandoffId.HasValue ? "coverage_returned" : mode == "temporary" ? "coverage_started" : "ownership_transferred",
            destination.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = returnHandoffId.HasValue ? "module025_coverage_returned" : "module025_ownership_transferred",
            engagementId, revision = nextRevision, handoffId, mode, returnDate = request.ReturnDate,
            ownerUserId = destination.UserId, ownerDisplayName = destination.DisplayName, notification, stateChanged = true });
    }

    private sealed record Module025Handoff(Guid HandoffId, string Mode, DateOnly? ReturnDate,
        Guid PreviousOwnerUserId, string PreviousOwnerDisplayName, Guid NewOwnerUserId, string NewOwnerDisplayName,
        Guid? AcknowledgedByUserId, DateTimeOffset? AcknowledgedAt, DateTimeOffset? ReturnedAt,
        bool ReturnDue, bool ReturnOverdue, Guid? ReturnOfHandoffId)
    {
        public bool Active => Mode == "temporary" && ReturnedAt is null;
    }

    private static async Task<Module025Handoff?> LoadHandoffAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid engagementId, Guid? handoffId, CancellationToken cancellationToken, bool activeCoverage = false)
    {
        const string sql = """
            SELECT h.handoff_id,h.handoff_mode,h.return_date,h.previous_owner_user_id,h.previous_owner_display_name,
                h.new_owner_user_id,h.new_owner_display_name,a.actor_user_id,a.created_at,r.created_at,
                h.return_date<=(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::date AND r.created_at IS NULL,h.return_date<(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::date AND r.created_at IS NULL,h.return_of_handoff_id
            FROM module025_sow_gsd_handoffs h
            LEFT JOIN LATERAL(SELECT actor_user_id,created_at FROM module025_sow_gsd_events e
                WHERE e.engagement_id=h.engagement_id AND e.event_type='handoff_acknowledged'
                  AND e.evidence_json->>'handoffId'=h.handoff_id::text ORDER BY e.event_id LIMIT 1) a ON TRUE
            LEFT JOIN LATERAL(SELECT created_at FROM module025_sow_gsd_events e
                WHERE e.engagement_id=h.engagement_id AND e.event_type='handoff_returned'
                  AND e.evidence_json->>'handoffId'=h.handoff_id::text ORDER BY e.event_id LIMIT 1) r ON TRUE
            WHERE h.engagement_id=@id AND (@handoff::uuid IS NULL OR h.handoff_id=@handoff)
                AND (NOT @active OR (h.handoff_mode='temporary' AND r.created_at IS NULL))
            ORDER BY h.transfer_event_id DESC LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", engagementId); command.Parameters.AddWithValue("active", activeCoverage);
        command.Parameters.AddWithValue("handoff", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)handoffId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
            reader.GetGuid(3),reader.GetString(4),reader.GetGuid(5),reader.GetString(6),reader.IsDBNull(7) ? null : reader.GetGuid(7),
            NullableTimestamp(reader,8),NullableTimestamp(reader,9),!reader.IsDBNull(10) && reader.GetBoolean(10),
            !reader.IsDBNull(11) && reader.GetBoolean(11),reader.IsDBNull(12) ? null : reader.GetGuid(12));
    }

    private static Task<Module025Handoff?> LoadActiveCoverageAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid engagementId, CancellationToken cancellationToken) => LoadHandoffAsync(connection,transaction,engagementId,null,cancellationToken,true);

    private static async Task<IResult> ReturnHandoffAsync(Guid engagementId, Module025HandoffReturnRequest request,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        if (context.Request.ContentLength is > MaximumRequestBytes) return RequestTooLarge();
        var state = await LoadReadableStateAsync(engagementId,context,cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!await TransferSchemaReadyAsync(connection,cancellationToken)) return StateConflict("transfer_migration_required","Temporary coverage requires migration 119.");
        return await TransferOwnershipCoreAsync(connection,engagementId,
            new(Guid.Empty,request.ExpectedRevision,request.Reason),state.Access!,request.HandoffId,cancellationToken);
    }

    private static async Task<IResult> AcknowledgeHandoffAsync(Guid engagementId, Module025HandoffAcknowledgeRequest request,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        if (context.Request.ContentLength is > MaximumRequestBytes) return RequestTooLarge();
        var state = await LoadReadableStateAsync(engagementId,context,cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!await TransferSchemaReadyAsync(connection,cancellationToken)) return StateConflict("transfer_migration_required","Handoff acknowledgement requires migration 119.");
        return await AcknowledgeHandoffCoreAsync(connection,engagementId,request,state.Access!,cancellationToken);
    }

    private static async Task<IResult> AcknowledgeHandoffCoreAsync(NpgsqlConnection connection, Guid engagementId,
        Module025HandoffAcknowledgeRequest request, Module025AccessContext access, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using(var command=new NpgsqlCommand("SELECT engagement_id FROM module025_sow_gsd_engagements WHERE engagement_id=@id FOR UPDATE;",connection,transaction))
        {
            command.Parameters.AddWithValue("id",engagementId);
            if(await command.ExecuteScalarAsync(cancellationToken) is null) return Results.NotFound();
        }
        var current=await LoadEngagementAsync(connection,engagementId,cancellationToken,transaction);
        if(current is null) return Results.NotFound();
        if(access.IsViewAs || !access.IsSolutionArchitect || current.OwnerUserId!=access.EffectiveUserId) return Forbidden("module025_handoff_acknowledge");
        await using(var activeOwner = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM app_users u JOIN app_user_role_assignments a ON a.user_id=u.user_id AND a.is_active=TRUE
                JOIN app_roles r ON r.app_role_id=a.app_role_id AND r.is_active=TRUE
                WHERE u.user_id=@owner AND u.is_active=TRUE AND upper(r.role_code)=ANY(@roles));
            """,connection,transaction))
        {
            activeOwner.Parameters.AddWithValue("owner",current.OwnerUserId); activeOwner.Parameters.AddWithValue("roles",SolutionArchitectRoles.ToArray());
            if(await activeOwner.ExecuteScalarAsync(cancellationToken) is not true) return Forbidden("module025_handoff_acknowledge");
        }
        if(current.Revision!=request.ExpectedRevision) return RevisionConflict(current.Revision);
        if(!current.IsActive) return StateConflict("archived_record","Unarchive the working record before acknowledging its handoff.");
        var handoff=await LoadHandoffAsync(connection,transaction,engagementId,null,cancellationToken);
        if(handoff is null || handoff.HandoffId!=request.HandoffId || handoff.NewOwnerUserId!=current.OwnerUserId)
            return StateConflict("handoff_not_current","Reload the latest handoff before acknowledging it.");
        if(handoff.AcknowledgedAt.HasValue)
            return Results.Ok(new {status="module025_handoff_already_acknowledged",engagementId,revision=current.Revision,stateChanged=false});
        await InsertEventAsync(connection,transaction,engagementId,access.ActualUserId,current.Revision,
            "handoff_acknowledged","Assigned Solution Architect acknowledged receipt of the working SOW/GSD; this does not confirm its technical review.",
            new {handoffId=handoff.HandoffId,acknowledgedByUserId=access.ActualUserId,previousOwnerUserId=handoff.PreviousOwnerUserId,
                newOwnerUserId=handoff.NewOwnerUserId,mode=handoff.Mode},cancellationToken);
        var notification=await QueueHandoffNotificationAsync(connection,transaction,engagementId,current.Revision,
            "coverage_acknowledged",current.OwnerUserId,cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new {status="module025_handoff_acknowledged",engagementId,revision=current.Revision,
            handoffId=handoff.HandoffId,acknowledgedByUserId=access.ActualUserId,notification,stateChanged=true});
    }

    private static string? TransferStateBlocker(Module025EngagementRow engagement) =>
        !engagement.IsActive || engagement.Status == "archived" ? "Unarchive this SOW/GSD before transferring it."
        : engagement.Status == "confirmed" ? "Reopen the confirmed SOW/GSD for edits before transferring the working draft. Retained document versions will remain unchanged."
        : null;

    private static async Task<string?> TransferActivityBlockerAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid engagementId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM module025_sow_gsd_events queued
                WHERE queued.engagement_id=@id AND queued.event_type='ai_generation_queued'
                  AND NOT EXISTS(SELECT 1 FROM module025_sow_gsd_events terminal
                    WHERE terminal.engagement_id=queued.engagement_id
                      AND terminal.event_type IN ('ai_generation_completed','ai_generation_failed','ai_generation_obsolete')
                      AND terminal.evidence_json->>'generationId'=queued.evidence_json->>'generationId')),
                EXISTS(SELECT 1 FROM module025_sow_sell_dispatch WHERE engagement_id=@id
                    AND sell_status IN ('queued','publishing','needs_reconciliation'));
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", engagementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.GetBoolean(0) ? "Wait for the active scope generation to finish before transferring this SOW/GSD."
            : reader.GetBoolean(1) ? "Complete or reconcile the pending ConnectWise SELL handoff before transferring this SOW/GSD." : null;
    }

    private static async Task<IReadOnlyList<Module025TransferDestination>> LoadTransferDestinationsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Module025AccessContext access,
        Guid ownerUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT teammate.user_id,COALESCE(NULLIF(teammate.display_name,''),teammate.email,''),
                COALESCE(NULLIF(teammate.department_name,''),teammate.department,''),COALESCE(teammate.team_name,''),
                array_agg(DISTINCT source.manager_user_id)
            FROM app_users owner
            JOIN reporting_relationships source ON source.employee_user_id=owner.user_id
                AND source.effective_start_date<=CURRENT_DATE
                AND (source.effective_end_date IS NULL OR source.effective_end_date>=CURRENT_DATE)
            JOIN app_users manager ON manager.user_id=source.manager_user_id AND manager.is_active=TRUE
            JOIN reporting_relationships target ON target.manager_user_id=source.manager_user_id
                AND target.effective_start_date<=CURRENT_DATE
                AND (target.effective_end_date IS NULL OR target.effective_end_date>=CURRENT_DATE)
            JOIN app_users teammate ON teammate.user_id=target.employee_user_id AND teammate.is_active=TRUE
            WHERE owner.user_id=@owner AND owner.is_active=TRUE AND teammate.user_id<>owner.user_id
                AND EXISTS(SELECT 1 FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id
                    WHERE a.user_id=owner.user_id AND a.is_active=TRUE AND r.is_active=TRUE AND upper(r.role_code)=ANY(@roles))
                AND EXISTS(SELECT 1 FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id
                    WHERE a.user_id=teammate.user_id AND a.is_active=TRUE AND r.is_active=TRUE AND upper(r.role_code)=ANY(@roles))
                AND (@administrator OR (@self AND owner.user_id=@actor)
                    OR (teammate.user_id=ANY(@visible_ids) AND owner.user_id=ANY(@visible_ids)
                        AND source.manager_user_id=@actor
                        AND target.manager_user_id=@actor))
            GROUP BY teammate.user_id,teammate.display_name,teammate.email,teammate.department_name,teammate.department,teammate.team_name
            ORDER BY 2,teammate.user_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("owner", ownerUserId);
        command.Parameters.AddWithValue("actor", access.EffectiveUserId);
        command.Parameters.AddWithValue("roles", SolutionArchitectRoles.ToArray());
        command.Parameters.AddWithValue("administrator", access.IsAdministrator && !access.IsViewAs);
        command.Parameters.AddWithValue("self", access.IsSolutionArchitect && !access.IsViewAs);
        command.Parameters.AddWithValue("visible_ids", access.IsViewAs ? Array.Empty<Guid>() : access.VisibleSolutionArchitectIds.ToArray());
        var rows = new List<Module025TransferDestination>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<Guid[]>(4)));
        return rows;
    }
}

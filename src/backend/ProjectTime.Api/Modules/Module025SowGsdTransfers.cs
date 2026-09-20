using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public sealed record Module025SowGsdTransferRequest(Guid TargetOwnerUserId, int ExpectedRevision, string? Reason);

public static partial class Module025SowGsdModule
{
    private const string TransferMigrationId = "116_module025_governed_ownership_transfer";

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
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration);", connection);
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
        var blockedReason = !allowed ? "Your role or current view does not allow transferring this record."
            : !ready ? "Ownership transfer is awaiting the governed database migration."
            : TransferStateBlocker(engagement)
                ?? await TransferActivityBlockerAsync(connection, null, engagementId, cancellationToken)
                ?? (destinations.Count == 0 ? "No other active Solution Architect shares this owner's reporting manager. Ask an administrator to verify reporting relationships." : null);
        return Results.Ok(new
        {
            engagementId, revision = engagement.Revision,
            canTransfer = blockedReason is null,
            managerTransfer = access.IsManager && engagement.OwnerUserId != access.EffectiveUserId,
            blockedReason, scope = "same_reporting_manager", destinations, stateChanged = false
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
            return StateConflict("transfer_migration_required", "Ownership transfer is awaiting migration 116. Reload after the database update.");
        return await TransferOwnershipAsync(connection, engagementId, request with { Reason = reason }, state.Access!, cancellationToken);
    }

    // A single transaction locks the working record, revalidates the destination,
    // appends the immutable event, and changes only ownership plus its revision.
    // Retained versions, SELL identifiers, task IDs, and original authors survive.
    private static async Task<IResult> TransferOwnershipAsync(NpgsqlConnection connection, Guid engagementId,
        Module025SowGsdTransferRequest request, Module025AccessContext access, CancellationToken cancellationToken)
    {
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
        var destinations = await LoadTransferDestinationsAsync(connection, transaction, access, current.OwnerUserId, cancellationToken);
        var destination = destinations.FirstOrDefault(person => person.UserId == request.TargetOwnerUserId);
        if (destination is null) return Forbidden("module025_transfer_destination");

        var nextRevision = checked(current.Revision + 1);
        // The trigger accepts only an exact event created in THIS transaction.
        // An event from an earlier handoff cannot authorize another owner change.
        const string insertEvent = """
            INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json)
            VALUES(@id,'ownership_transferred',@actor,@revision,@summary,
                @evidence::jsonb || jsonb_build_object('transferTransactionId',txid_current()::text));
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
                scope = "same_reporting_manager", current.EngagementNumber
            }));
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
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
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_ownership_transferred", engagementId, revision = nextRevision,
            ownerUserId = destination.UserId, ownerDisplayName = destination.DisplayName, stateChanged = true });
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

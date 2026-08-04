using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private sealed record ExpenseLifecycleConflict(int StatusCode, string Code, string Message) : Exception(Message);

    private static IResult LifecycleConflictResult(ExpenseLifecycleConflict conflict) =>
        Results.Json(new { status = conflict.Code, message = conflict.Message }, statusCode: conflict.StatusCode);

    private static async Task<bool> ExpenseAcceptanceSchemaAvailableAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.project_expense_upload_acceptances') IS NOT NULL;", connection, transaction);
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task<bool> IsExpenseUploadAcceptedAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid uploadId)
    {
        if (!await ExpenseAcceptanceSchemaAvailableAsync(connection, transaction)) return false;
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM project_expense_upload_acceptances
                WHERE project_expense_upload_id = @upload_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("upload_id", uploadId);
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task ThrowIfExpenseUploadImmutableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid uploadId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT is_current, deleted_at IS NOT NULL
            FROM project_expense_uploads
            WHERE project_expense_upload_id = @upload_id
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("upload_id", uploadId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new ExpenseLifecycleConflict(404, "upload_not_found", "The expense upload was not found.");
        var isCurrent = reader.GetBoolean(0);
        var isDeleted = reader.GetBoolean(1);
        await reader.DisposeAsync();
        if (isDeleted)
            throw new ExpenseLifecycleConflict(409, "expense_upload_deleted", "Deleted expense evidence cannot be modified or restored.");
        if (!isCurrent)
            throw new ExpenseLifecycleConflict(409, "expense_upload_superseded", "Only the current expense upload can be deleted or replaced.");
        if (await IsExpenseUploadAcceptedAsync(connection, transaction, uploadId))
            throw new ExpenseLifecycleConflict(409, "expense_upload_approved_locked", "The assigned Project Manager accepted this expense version. It is now immutable.");
    }

    private static async Task SuppressDeletedExpenseNotificationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid uploadId)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE project_expense_mail_outbox
            SET delivery_status = 'suppressed',
                last_error = 'Upload deleted before PM acceptance; delivery permanently suppressed.',
                updated_at = NOW()
            WHERE project_expense_upload_id = @upload_id
              AND delivery_status IN ('queued', 'configuration_pending', 'failed');
            UPDATE project_expense_uploads
            SET notification_status = 'suppressed_deleted',
                notification_detail = 'Notification suppressed because the expense upload was deleted.'
            WHERE project_expense_upload_id = @upload_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("upload_id", uploadId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IResult> GetExpenseUploadLifecycleAsync(HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (!HasRole(actor, SelfRoles) && !HasRole(actor, BillingRoles))
            return AccessDenied("The current role cannot view project expense lifecycle evidence.");
        var projects = await LoadAccessibleProjectsAsync(connection, actor, true);
        var allowedProjects = projects.ToDictionary(project => project.ProjectId);
        var rows = new List<object>();
        var schemaReady = await ExpenseAcceptanceSchemaAvailableAsync(connection);
        await using var command = new NpgsqlCommand("""
            SELECT upload.project_expense_upload_id,
                   upload.project_id,
                   upload.expense_owner_user_id,
                   upload.uploaded_by_user_id,
                   upload.version_number,
                   upload.is_current,
                   upload.deleted_at IS NOT NULL,
                   CASE WHEN @schema_ready THEN EXISTS (
                       SELECT 1 FROM project_expense_upload_acceptances acceptance
                       WHERE acceptance.project_expense_upload_id = upload.project_expense_upload_id
                   ) ELSE FALSE END AS pm_accepted
            FROM project_expense_uploads upload
            ORDER BY upload.uploaded_at DESC;
            """, connection);
        command.Parameters.AddWithValue("schema_ready", schemaReady);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var projectId = reader.GetGuid(1);
            if (!allowedProjects.TryGetValue(projectId, out var project)) continue;
            var uploadId = reader.GetGuid(0);
            var ownerId = reader.GetGuid(2);
            var uploaderId = reader.GetGuid(3);
            var isCurrent = reader.GetBoolean(5);
            var isDeleted = reader.GetBoolean(6);
            var accepted = reader.GetBoolean(7);
            var ownsEvidence = ownerId == actor.EffectiveUserId || uploaderId == actor.ActualUserId;
            var elevated = HasRole(actor, OnBehalfRoles) || HasRole(actor, BillingRoles);
            var canMutate = !actor.IsViewAs && isCurrent && !isDeleted && !accepted && (ownsEvidence || elevated);
            var assignedPm = project.ProjectManagerUserId == actor.EffectiveUserId;
            var canAccept = !actor.IsViewAs && isCurrent && !isDeleted && !accepted
                && (assignedPm || actor.RoleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase));
            rows.Add(new
            {
                uploadId,
                versionNumber = reader.GetInt32(4),
                approvalStatus = accepted ? "pm_accepted" : "pending_pm_acceptance",
                canDelete = canMutate,
                canReplace = canMutate,
                canAccept,
                lockReason = accepted
                    ? "Accepted by the assigned Project Manager. Delete and replacement are disabled."
                    : isDeleted ? "Deleted audit evidence is immutable."
                    : !isCurrent ? "A newer active version exists."
                    : actor.IsViewAs ? "Administrator View-As is read-only."
                    : string.Empty,
                isCurrent,
                isDeleted
            });
        }
        return Results.Ok(new { status = "project_expense_lifecycle_loaded", acceptanceSchemaReady = schemaReady, count = rows.Count, uploads = rows });
    }

    private static async Task<IResult> AcceptExpenseUploadAsync(Guid uploadId, HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs) return ViewAsReadOnly();
        if (!await ExpenseAcceptanceSchemaAvailableAsync(connection))
            return Results.Json(new { status = "expense_acceptance_schema_unavailable", message = "The PM expense-acceptance migration has not been applied." }, statusCode: 503);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await ThrowIfExpenseUploadImmutableAsync(connection, transaction, uploadId);
            await using var load = new NpgsqlCommand("""
                SELECT upload.project_id, upload.expense_owner_user_id, upload.version_number, project.project_manager_user_id
                FROM project_expense_uploads upload
                JOIN projects project ON project.project_id = upload.project_id
                WHERE upload.project_expense_upload_id = @upload_id
                FOR UPDATE OF upload, project;
                """, connection, transaction);
            load.Parameters.AddWithValue("upload_id", uploadId);
            await using var reader = await load.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new ExpenseLifecycleConflict(404, "upload_not_found", "The expense upload was not found.");
            var projectId = reader.GetGuid(0);
            var ownerId = reader.GetGuid(1);
            var versionNumber = reader.GetInt32(2);
            var assignedPm = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
            await reader.DisposeAsync();
            var isSuperAdministrator = actor.RoleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase);
            if (assignedPm != actor.EffectiveUserId && !isSuperAdministrator)
                throw new ExpenseLifecycleConflict(403, "assigned_pm_required", "Only the assigned Project Manager can accept this expense version.");
            await using var insert = new NpgsqlCommand("""
                INSERT INTO project_expense_upload_acceptances (
                    project_expense_upload_id, project_id, expense_owner_user_id,
                    accepted_version_number, accepted_by_user_id, acceptance_reason
                ) VALUES (
                    @upload_id, @project_id, @owner_id, @version_number, @actor_id,
                    'Assigned Project Manager accepted the current expense evidence.'
                ) ON CONFLICT (project_expense_upload_id) DO NOTHING;
                """, connection, transaction);
            insert.Parameters.AddWithValue("upload_id", uploadId);
            insert.Parameters.AddWithValue("project_id", projectId);
            insert.Parameters.AddWithValue("owner_id", ownerId);
            insert.Parameters.AddWithValue("version_number", versionNumber);
            insert.Parameters.AddWithValue("actor_id", actor.ActualUserId);
            await insert.ExecuteNonQueryAsync();
            await InsertExpenseEventAsync(connection, transaction, uploadId, projectId, "PM_EXPENSE_VERSION_ACCEPTED", actor.ActualUserId, ownerId,
                "Assigned Project Manager accepted this exact expense version.", new { versionNumber, versionSpecific = true });
            await transaction.CommitAsync();
            return Results.Ok(new { status = "project_expense_version_accepted", uploadId, versionNumber, message = "The expense version was accepted and is now locked from deletion or replacement." });
        }
        catch (ExpenseLifecycleConflict conflict)
        {
            await transaction.RollbackAsync();
            return LifecycleConflictResult(conflict);
        }
    }

    private static async Task<bool> IsExpenseUploadDeletedAsync(NpgsqlConnection connection, Guid uploadId)
    {
        await using var command = new NpgsqlCommand("SELECT deleted_at IS NOT NULL FROM project_expense_uploads WHERE project_expense_upload_id=@upload_id;", connection);
        command.Parameters.AddWithValue("upload_id", uploadId);
        return await command.ExecuteScalarAsync() is true;
    }
}

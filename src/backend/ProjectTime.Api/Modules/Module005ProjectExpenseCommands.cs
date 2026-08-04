using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private static async Task<IResult> UploadFileAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
            return Results.BadRequest(new { status = "multipart_form_required", message = "Use multipart/form-data with project, expense owner, and file." });

        var form = await context.Request.ReadFormAsync();
        if (!Guid.TryParse(form["projectId"], out var projectId))
            return Results.BadRequest(new { status = "project_required", message = "Select a customer and project before uploading." });
        if (!Guid.TryParse(form["expenseOwnerUserId"], out var ownerId))
            return Results.BadRequest(new { status = "expense_owner_required", message = "Select the person whose expenses are being uploaded." });
        Guid? replaceUploadId = Guid.TryParse(form["replaceUploadId"], out var parsedReplaceUploadId)
            ? parsedReplaceUploadId
            : null;
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return Results.BadRequest(new { status = "file_required", message = "Select an Excel or CSV file." });
        if (file.Length > MaximumUploadBytes) return Results.BadRequest(new { status = "file_too_large", message = "Expense files are limited to 15 MB." });

        byte[] bytes;
        await using (var memory = new MemoryStream())
        {
            await file.CopyToAsync(memory, context.RequestAborted);
            bytes = memory.ToArray();
        }

        ParsedExpenseFile parsed;
        try { parsed = ParseExpenseFile(file.FileName, bytes); }
        catch (Exception exception) { return Results.BadRequest(new { status = "expense_file_invalid", message = exception.Message }); }

        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        var project = await LoadProjectAsync(connection, projectId);
        if (project is null) return Results.NotFound(new { status = "project_not_found", message = "The selected project no longer exists." });
        var authorization = await AuthorizeUploadAsync(connection, null, actor, project, ownerId);
        if (authorization is not null) return authorization;

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Guid uploadId;
        try
        {
            uploadId = await PersistUploadAsync(
                connection, actor, project, ownerId, "excel_csv", parsed.FormatCode,
                parsed.SourceReportId, file.FileName, file.ContentType, bytes, hash, parsed,
                new { originalFileName = file.FileName, fileLength = file.Length }, replaceUploadId);
        }
        catch (ExpenseLifecycleConflict conflict)
        {
            return LifecycleConflictResult(conflict);
        }
        var notification = await DeliverExpenseNotificationAsync(connection, uploadId, actor.ActualUserId);

        return Results.Ok(new
        {
            status = "project_expense_upload_completed",
            message = $"Imported {parsed.Lines.Count} expense line(s) totaling {parsed.TotalAmount:C}.",
            uploadId,
            sourceFormat = parsed.FormatCode,
            parsed.PeriodStart,
            parsed.PeriodEnd,
            lineCount = parsed.Lines.Count,
            parsed.TotalAmount,
            parsed.ReimbursableAmount,
            billingTreatment = BillingTreatment(project.ContractType),
            notification
        });
    }

    private static async Task<Guid> PersistUploadAsync(
        NpgsqlConnection connection,
        ExpenseActor actor,
        ExpenseProject project,
        Guid ownerId,
        string sourceMode,
        string sourceFormat,
        string? reportId,
        string? fileName,
        string? contentType,
        byte[] sourceBytes,
        string sourceSha,
        ParsedExpenseFile parsed,
        object sourceMetadata,
        Guid? replaceUploadId = null)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 44));", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", $"project-expense:{project.ProjectId}:{ownerId}:{parsed.PeriodStart}:{parsed.PeriodEnd}");
            await lockCommand.ExecuteNonQueryAsync();
        }

        var currentScopeUploadIds = new List<Guid>();
        await using (var currentScope = new NpgsqlCommand("""
            SELECT project_expense_upload_id
            FROM project_expense_uploads
            WHERE project_id=@project_id AND expense_owner_user_id=@owner_id
              AND period_start IS NOT DISTINCT FROM @period_start
              AND period_end IS NOT DISTINCT FROM @period_end
              AND is_current=TRUE AND deleted_at IS NULL
            FOR UPDATE;
            """, connection, transaction))
        {
            currentScope.Parameters.AddWithValue("project_id", project.ProjectId);
            currentScope.Parameters.AddWithValue("owner_id", ownerId);
            currentScope.Parameters.Add(new NpgsqlParameter("period_start", NpgsqlDbType.Date) { Value = parsed.PeriodStart is null ? DBNull.Value : parsed.PeriodStart.Value });
            currentScope.Parameters.Add(new NpgsqlParameter("period_end", NpgsqlDbType.Date) { Value = parsed.PeriodEnd is null ? DBNull.Value : parsed.PeriodEnd.Value });
            await using var reader = await currentScope.ExecuteReaderAsync();
            while (await reader.ReadAsync()) currentScopeUploadIds.Add(reader.GetGuid(0));
        }
        foreach (var currentScopeUploadId in currentScopeUploadIds)
        {
            if (await IsExpenseUploadAcceptedAsync(connection, transaction, currentScopeUploadId))
                throw new ExpenseLifecycleConflict(
                    409,
                    "expense_upload_approved_locked",
                    "The assigned Project Manager accepted the current expense version. Delete, replacement, and same-period re-upload are disabled.");
        }

        if (replaceUploadId is not null)
        {
            var replacementTarget = await LoadUploadForUpdateAsync(connection, transaction, replaceUploadId.Value)
                ?? throw new ExpenseLifecycleConflict(404, "upload_not_found", "The expense upload selected for replacement was not found.");
            var replacementAuthorization = await AuthorizeExistingUploadActionAsync(connection, transaction, actor, replacementTarget);
            if (replacementAuthorization is not null)
                throw new ExpenseLifecycleConflict(403, "access_denied", "The current user cannot replace this expense upload.");
            if (replacementTarget.ProjectId != project.ProjectId || replacementTarget.OwnerUserId != ownerId)
                throw new ExpenseLifecycleConflict(409, "replacement_scope_mismatch", "The replacement must use the same project and expense owner.");
            if (replacementTarget.PeriodStart != parsed.PeriodStart || replacementTarget.PeriodEnd != parsed.PeriodEnd)
                throw new ExpenseLifecycleConflict(409, "replacement_period_mismatch", "The replacement period must match the selected expense version.");
            await ThrowIfExpenseUploadImmutableAsync(connection, transaction, replaceUploadId.Value);
        }

        var nextVersion = 1;
        await using (var versionCommand = new NpgsqlCommand("""
            SELECT COALESCE(MAX(version_number),0)+1
            FROM project_expense_uploads
            WHERE project_id=@project_id AND expense_owner_user_id=@owner_id
              AND period_start IS NOT DISTINCT FROM @period_start
              AND period_end IS NOT DISTINCT FROM @period_end;
            """, connection, transaction))
        {
            versionCommand.Parameters.AddWithValue("project_id", project.ProjectId);
            versionCommand.Parameters.AddWithValue("owner_id", ownerId);
            versionCommand.Parameters.Add(new NpgsqlParameter("period_start", NpgsqlDbType.Date) { Value = parsed.PeriodStart is null ? DBNull.Value : parsed.PeriodStart.Value });
            versionCommand.Parameters.Add(new NpgsqlParameter("period_end", NpgsqlDbType.Date) { Value = parsed.PeriodEnd is null ? DBNull.Value : parsed.PeriodEnd.Value });
            nextVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync() ?? 1);
        }

        var supersededIds = new List<Guid>();
        await using (var supersede = new NpgsqlCommand("""
            UPDATE project_expense_uploads
            SET is_current=FALSE
            WHERE project_id=@project_id AND expense_owner_user_id=@owner_id
              AND period_start IS NOT DISTINCT FROM @period_start
              AND period_end IS NOT DISTINCT FROM @period_end
              AND is_current=TRUE AND deleted_at IS NULL
            RETURNING project_expense_upload_id;
            """, connection, transaction))
        {
            supersede.Parameters.AddWithValue("project_id", project.ProjectId);
            supersede.Parameters.AddWithValue("owner_id", ownerId);
            supersede.Parameters.Add(new NpgsqlParameter("period_start", NpgsqlDbType.Date) { Value = parsed.PeriodStart is null ? DBNull.Value : parsed.PeriodStart.Value });
            supersede.Parameters.Add(new NpgsqlParameter("period_end", NpgsqlDbType.Date) { Value = parsed.PeriodEnd is null ? DBNull.Value : parsed.PeriodEnd.Value });
            await using var reader = await supersede.ExecuteReaderAsync();
            while (await reader.ReadAsync()) supersededIds.Add(reader.GetGuid(0));
        }

        var uploadId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO project_expense_uploads (
                project_expense_upload_id, project_id, customer_name, project_code, project_name,
                expense_owner_user_id, uploaded_by_user_id, source_mode, source_format, source_report_id,
                original_file_name, content_type, source_file_bytes, source_sha256, source_metadata,
                period_start, period_end, currency, line_count, total_amount, reimbursable_amount,
                contract_type_snapshot, billing_treatment, version_number, is_current
            ) VALUES (
                @upload_id, @project_id, @customer_name, @project_code, @project_name,
                @owner_id, @uploader_id, @source_mode, @source_format, @report_id,
                @file_name, @content_type, @source_bytes, @sha, @metadata::jsonb,
                @period_start, @period_end, @currency, @line_count, @total, @reimbursable,
                @contract_type, @billing_treatment, @version, TRUE
            );
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("upload_id", uploadId);
            insert.Parameters.AddWithValue("project_id", project.ProjectId);
            insert.Parameters.AddWithValue("customer_name", project.CustomerName);
            insert.Parameters.AddWithValue("project_code", project.ProjectCode);
            insert.Parameters.AddWithValue("project_name", project.ProjectName);
            insert.Parameters.AddWithValue("owner_id", ownerId);
            insert.Parameters.AddWithValue("uploader_id", actor.ActualUserId);
            insert.Parameters.AddWithValue("source_mode", sourceMode);
            insert.Parameters.AddWithValue("source_format", sourceFormat);
            insert.Parameters.Add(new NpgsqlParameter("report_id", NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(reportId) ? DBNull.Value : reportId });
            insert.Parameters.Add(new NpgsqlParameter("file_name", NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(fileName) ? DBNull.Value : fileName });
            insert.Parameters.Add(new NpgsqlParameter("content_type", NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(contentType) ? DBNull.Value : contentType });
            insert.Parameters.AddWithValue("source_bytes", sourceBytes);
            insert.Parameters.AddWithValue("sha", sourceSha);
            insert.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(sourceMetadata));
            insert.Parameters.Add(new NpgsqlParameter("period_start", NpgsqlDbType.Date) { Value = parsed.PeriodStart is null ? DBNull.Value : parsed.PeriodStart.Value });
            insert.Parameters.Add(new NpgsqlParameter("period_end", NpgsqlDbType.Date) { Value = parsed.PeriodEnd is null ? DBNull.Value : parsed.PeriodEnd.Value });
            insert.Parameters.AddWithValue("currency", parsed.Currency);
            insert.Parameters.AddWithValue("line_count", parsed.Lines.Count);
            insert.Parameters.AddWithValue("total", parsed.TotalAmount);
            insert.Parameters.AddWithValue("reimbursable", parsed.ReimbursableAmount);
            insert.Parameters.AddWithValue("contract_type", project.ContractType);
            insert.Parameters.AddWithValue("billing_treatment", BillingTreatment(project.ContractType));
            insert.Parameters.AddWithValue("version", nextVersion);
            await insert.ExecuteNonQueryAsync();
        }

        const string lineSql = """
            INSERT INTO project_expense_lines (
                project_expense_line_id, project_expense_upload_id, line_number,
                employee_name, employee_email, department_name, department_code,
                expense_date, expense_category, gl_code, amount, reimbursable,
                reimbursable_amount, currency, reason, is_summary_line, source_row
            ) VALUES (
                gen_random_uuid(), @upload_id, @line_number,
                @employee_name, @employee_email, @department_name, @department_code,
                @expense_date, @category, @gl_code, @amount, @reimbursable,
                @reimbursable_amount, @currency, @reason, @is_summary, @source_row::jsonb
            );
            """;
        foreach (var line in parsed.Lines)
        {
            await using var lineCommand = new NpgsqlCommand(lineSql, connection, transaction);
            lineCommand.Parameters.AddWithValue("upload_id", uploadId);
            lineCommand.Parameters.AddWithValue("line_number", line.LineNumber);
            lineCommand.Parameters.AddWithValue("employee_name", line.EmployeeName);
            lineCommand.Parameters.AddWithValue("employee_email", line.EmployeeEmail);
            lineCommand.Parameters.AddWithValue("department_name", line.DepartmentName);
            lineCommand.Parameters.AddWithValue("department_code", line.DepartmentCode);
            lineCommand.Parameters.Add(new NpgsqlParameter("expense_date", NpgsqlDbType.Date) { Value = line.ExpenseDate is null ? DBNull.Value : line.ExpenseDate.Value });
            lineCommand.Parameters.AddWithValue("category", line.Category);
            lineCommand.Parameters.AddWithValue("gl_code", line.GlCode);
            lineCommand.Parameters.AddWithValue("amount", line.Amount);
            lineCommand.Parameters.AddWithValue("reimbursable", line.Reimbursable);
            lineCommand.Parameters.AddWithValue("reimbursable_amount", line.ReimbursableAmount);
            lineCommand.Parameters.AddWithValue("currency", line.Currency);
            lineCommand.Parameters.AddWithValue("reason", line.Reason);
            lineCommand.Parameters.AddWithValue("is_summary", line.IsSummaryLine);
            lineCommand.Parameters.AddWithValue("source_row", line.SourceJson);
            await lineCommand.ExecuteNonQueryAsync();
        }

        foreach (var supersededId in supersededIds)
            await InsertExpenseEventAsync(connection, transaction, supersededId, project.ProjectId, "UPLOAD_SUPERSEDED", actor.ActualUserId, ownerId, "Replaced by a newer upload version.", new { replacementUploadId = uploadId });

        await InsertExpenseEventAsync(connection, transaction, uploadId, project.ProjectId,
            sourceMode == "certify" ? "CERTIFY_IMPORTED" : "UPLOAD_CREATED",
            actor.ActualUserId, ownerId, string.Empty,
            new { sourceMode, sourceFormat, nextVersion, parsed.LineCount, parsed.TotalAmount, billingTreatment = BillingTreatment(project.ContractType) });
        await QueueExpenseNotificationAsync(connection, transaction, uploadId, project, actor, ownerId, parsed);
        await transaction.CommitAsync();
        return uploadId;
    }

    private static async Task<IResult> DeleteUploadAsync(Guid uploadId, ExpenseDeleteRequest request, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Results.BadRequest(new { status = "reason_required", message = "A deletion reason is required." });
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs) return ViewAsReadOnly();

        await using var transaction = await connection.BeginTransactionAsync();
        var upload = await LoadUploadForUpdateAsync(connection, transaction, uploadId);
        if (upload is null) return Results.NotFound(new { status = "upload_not_found", message = "The expense upload was not found." });
        var project = await LoadProjectAsync(connection, upload.ProjectId, transaction);
        if (project is null) return Results.NotFound(new { status = "project_not_found", message = "The related project was not found." });
        var authorization = await AuthorizeExistingUploadActionAsync(connection, transaction, actor, upload);
        if (authorization is not null) return authorization;

        try
        {
            await ThrowIfExpenseUploadImmutableAsync(connection, transaction, uploadId);
        }
        catch (ExpenseLifecycleConflict conflict)
        {
            await transaction.RollbackAsync();
            return LifecycleConflictResult(conflict);
        }

        await using (var delete = new NpgsqlCommand("""
            UPDATE project_expense_uploads
            SET is_current=FALSE, deleted_at=NOW(), deleted_by_user_id=@actor_id,
                deletion_reason=@reason, notification_status='suppressed_deleted',
                notification_detail='Upload deleted before PM acceptance; notifications suppressed.'
            WHERE project_expense_upload_id=@upload_id;
            """, connection, transaction))
        {
            delete.Parameters.AddWithValue("actor_id", actor.ActualUserId);
            delete.Parameters.AddWithValue("reason", request.Reason.Trim());
            delete.Parameters.AddWithValue("upload_id", uploadId);
            await delete.ExecuteNonQueryAsync();
        }

        await SuppressDeletedExpenseNotificationAsync(connection, transaction, uploadId);
        await InsertExpenseEventAsync(connection, transaction, uploadId, upload.ProjectId, "UPLOAD_DELETED", actor.ActualUserId,
            upload.OwnerUserId, request.Reason.Trim(), new { priorVersionRestored = false, deletedEvidenceImmutable = true });
        await transaction.CommitAsync();
        return Results.Ok(new
        {
            status = "project_expense_upload_deleted",
            message = "The upload was deleted. No prior version was restored; upload or import a replacement explicitly when required.",
            uploadId,
            restoredUploadId = (Guid?)null
        });
    }

    private static async Task<ExpenseUploadRecord?> LoadUploadForUpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid uploadId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT project_expense_upload_id, project_id, expense_owner_user_id,
                   uploaded_by_user_id, period_start, period_end
            FROM project_expense_uploads WHERE project_expense_upload_id=@upload_id FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("upload_id", uploadId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ExpenseUploadRecord(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), ReadDate(reader, 4), ReadDate(reader, 5));
    }

    private static async Task InsertExpenseEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid? uploadId, Guid projectId, string code, Guid actorId, Guid targetId, string reason, object metadata)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO project_expense_events (
                project_expense_event_id, project_expense_upload_id, project_id,
                event_code, actor_user_id, target_user_id, reason, event_metadata
            ) VALUES (gen_random_uuid(), @upload_id, @project_id, @code, @actor_id, @target_id, @reason, @metadata::jsonb);
            """, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("upload_id", NpgsqlDbType.Uuid) { Value = uploadId is null ? DBNull.Value : uploadId.Value });
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("actor_id", actorId);
        command.Parameters.AddWithValue("target_id", targetId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync();
    }
}

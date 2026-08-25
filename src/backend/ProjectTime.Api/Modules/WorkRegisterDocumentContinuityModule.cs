using System.Globalization;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Canonical Module 055C document continuity handler. This is invoked from the
/// existing Work Register authorization middleware so project read/edit scope is
/// established before any canonical document is returned or deleted.
/// </summary>
internal static class WorkRegisterDocumentContinuityModule
{
    internal const string Contract = "WORK_REGISTER_DOCUMENT_CONTINUITY_V2";

    internal static async Task<bool> TryHandleAsync(HttpContext context)
    {
        var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 5
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("work-register", StringComparison.OrdinalIgnoreCase)
            || !segments[2].Equals("projects", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[3], out var projectId)
            || projectId == Guid.Empty
            || !segments[4].Equals("documents", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && segments.Length == 5)
        {
            var result = await ListDocumentsAsync(projectId, context);
            await result.ExecuteAsync(context);
            return true;
        }

        if (HttpMethods.IsDelete(context.Request.Method)
            && segments.Length == 6
            && Guid.TryParse(segments[5], out var documentId)
            && documentId != Guid.Empty)
        {
            var result = await DeleteDocumentAsync(projectId, documentId, context);
            await result.ExecuteAsync(context);
            return true;
        }

        return false;
    }

    private static async Task<IResult> ListDocumentsAsync(Guid projectId, HttpContext context)
    {
        if (ReadSessionUserId(context) is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                module = "055C",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            await using var connection = await OpenAsync(context.RequestAborted);
            if (!await ProjectExistsAsync(connection, projectId, context.RequestAborted))
            {
                return Results.NotFound(new
                {
                    status = "project_not_found",
                    module = "055C",
                    message = "Project was not found."
                });
            }

            var access = await WorkRegisterAuthorization.GetAccessAsync(
                connection,
                context,
                cancellationToken: context.RequestAborted);
            var scopedCanEdit = context.Items.TryGetValue("ProjectPulseWorkRegisterCanEdit", out var canEditValue)
                && canEditValue is true;
            var canDelete = access.CanEditAll || scopedCanEdit;

            var documents = new List<WorkRegisterDocumentProjection>();
            await using var command = new NpgsqlCommand("""
                SELECT
                    document.work_register_document_id,
                    COALESCE(document.document_name, ''),
                    COALESCE(document.document_type, 'Other'),
                    COALESCE(document.visibility, 'project_team'),
                    COALESCE(document.created_at::text, ''),
                    COALESCE(document.document_reference, ''),
                    COALESCE(document.upload_source, 'link'),
                    COALESCE(document.original_file_name, ''),
                    COALESCE(document.stored_file_path, ''),
                    COALESCE(document.content_type, ''),
                    GREATEST(COALESCE(document.file_size_bytes, 0), 0),
                    COALESCE(document.version_label, ''),
                    COALESCE(document.status, 'active'),
                    COALESCE(document.effective_date::text, ''),
                    COALESCE(document.notes, ''),
                    COALESCE(document.archive_reason, ''),
                    COALESCE(document.archived_at::text, '')
                FROM work_register_documents document
                WHERE document.project_id = @project_id
                  AND lower(COALESCE(document.status, 'active')) NOT IN ('deleted', 'removed', 'purged')
                ORDER BY document.created_at DESC, document.work_register_document_id;
                """, connection);
            command.Parameters.AddWithValue("project_id", projectId);

            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                var documentId = reader.GetGuid(0);
                var status = reader.GetString(12);
                var storedFilePath = reader.GetString(8);
                var active = IsActive(status);
                documents.Add(new WorkRegisterDocumentProjection(
                    documentId.ToString(),
                    FirstNonBlank(reader.GetString(1), reader.GetString(7), "Untitled document"),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    active && storedFilePath.Length > 0
                        ? $"/api/work-register/projects/documents/{documentId}/download"
                        : string.Empty,
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(9),
                    reader.GetInt64(10),
                    reader.GetString(11),
                    status,
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.GetString(15),
                    reader.GetString(16),
                    "work_register_documents",
                    active && canDelete,
                    canDelete,
                    canDelete ? $"/api/work-register/projects/{projectId}/documents/{documentId}" : string.Empty));
            }

            return Results.Ok(new
            {
                module = "055C",
                contract = Contract,
                projectId,
                summary = new
                {
                    total = documents.Count,
                    active = documents.Count(document => IsActive(document.Status)),
                    archived = documents.Count(document =>
                        string.Equals(document.Status, "archived", StringComparison.OrdinalIgnoreCase)),
                    localFiles = documents.Count(document =>
                        string.Equals(document.UploadSource, "local_file", StringComparison.OrdinalIgnoreCase))
                },
                documents
            });
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("WorkRegisterDocumentContinuityModule")
                .LogWarning(
                    "Work Register canonical document read was unavailable ({ExceptionType}).",
                    exception.GetType().Name);
            return Results.Json(new
            {
                status = "work_register_document_continuity_unavailable",
                module = "055C",
                correlationId = context.TraceIdentifier,
                message = "Project documents could not be loaded from the canonical Work Register document store."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> DeleteDocumentAsync(
        Guid projectId,
        Guid documentId,
        HttpContext context)
    {
        var actorUserId = ReadActualUserId(context);
        if (actorUserId is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                module = "055C",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var reasonResult = await ReadDeleteReasonAsync(context);
        if (!reasonResult.Valid)
        {
            return Results.Json(new
            {
                status = "delete_reason_required",
                module = "055C",
                message = reasonResult.Message
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var reason = reasonResult.Reason!;
        string storedFilePath;
        string documentName;
        bool alreadyDeleted;

        try
        {
            await using var connection = await OpenAsync(context.RequestAborted);
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            string oldSnapshot;
            string status;

            await using (var readCommand = new NpgsqlCommand("""
                SELECT
                    COALESCE(to_jsonb(document), '{}'::jsonb)::text,
                    COALESCE(document.document_name, ''),
                    COALESCE(document.stored_file_path, ''),
                    COALESCE(document.status, 'active')
                FROM work_register_documents document
                WHERE document.work_register_document_id = @document_id
                  AND document.project_id = @project_id
                FOR UPDATE;
                """, connection, transaction))
            {
                readCommand.Parameters.AddWithValue("document_id", documentId);
                readCommand.Parameters.AddWithValue("project_id", projectId);
                await using var reader = await readCommand.ExecuteReaderAsync(context.RequestAborted);
                if (!await reader.ReadAsync(context.RequestAborted))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.NotFound(new
                    {
                        status = "document_not_found",
                        module = "055C",
                        message = "The canonical Work Register document was not found for this project."
                    });
                }
                oldSnapshot = reader.GetString(0);
                documentName = reader.GetString(1);
                storedFilePath = reader.GetString(2);
                status = reader.GetString(3);
            }

            alreadyDeleted = IsDeleted(status);
            if (!alreadyDeleted)
            {
                await using (var updateCommand = new NpgsqlCommand("""
                    UPDATE work_register_documents
                    SET status = 'deleted',
                        archived_by_user_id = @actor_user_id,
                        archived_at = NOW(),
                        archive_reason = @delete_reason
                    WHERE work_register_document_id = @document_id
                      AND project_id = @project_id;
                    """, connection, transaction))
                {
                    updateCommand.Parameters.AddWithValue("actor_user_id", actorUserId.Value);
                    updateCommand.Parameters.AddWithValue("delete_reason", reason);
                    updateCommand.Parameters.AddWithValue("document_id", documentId);
                    updateCommand.Parameters.AddWithValue("project_id", projectId);
                    await updateCommand.ExecuteNonQueryAsync(context.RequestAborted);
                }

                await DeactivateSharedDocumentBridgeAsync(
                    connection,
                    transaction,
                    projectId,
                    documentId,
                    context.RequestAborted);

                string newSnapshot;
                await using (var snapshotCommand = new NpgsqlCommand("""
                    SELECT COALESCE(to_jsonb(document), '{}'::jsonb)::text
                    FROM work_register_documents document
                    WHERE document.work_register_document_id = @document_id
                      AND document.project_id = @project_id;
                    """, connection, transaction))
                {
                    snapshotCommand.Parameters.AddWithValue("document_id", documentId);
                    snapshotCommand.Parameters.AddWithValue("project_id", projectId);
                    newSnapshot = Convert.ToString(
                        await snapshotCommand.ExecuteScalarAsync(context.RequestAborted),
                        CultureInfo.InvariantCulture) ?? "{}";
                }

                await using (var auditCommand = new NpgsqlCommand("""
                    INSERT INTO work_register_change_history (
                        work_register_change_history_id,
                        source_table,
                        work_id,
                        action,
                        change_summary,
                        changed_fields_csv,
                        changed_by_user_id,
                        old_value_json,
                        new_value_json,
                        changed_at
                    )
                    VALUES (
                        @history_id,
                        'projects',
                        @project_id,
                        'document_deleted',
                        @change_summary,
                        @changed_fields_csv,
                        @actor_user_id,
                        CAST(@old_value_json AS jsonb),
                        CAST(@new_value_json AS jsonb),
                        NOW()
                    );
                    """, connection, transaction))
                {
                    auditCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
                    auditCommand.Parameters.AddWithValue("project_id", projectId);
                    auditCommand.Parameters.AddWithValue(
                        "change_summary",
                        $"Deleted document '{DisplayName(documentName)}': {reason}");
                    auditCommand.Parameters.AddWithValue(
                        "changed_fields_csv",
                        "Document Status, Delete Reason, Shared Document Visibility, Stored File");
                    auditCommand.Parameters.AddWithValue("actor_user_id", actorUserId.Value);
                    auditCommand.Parameters.AddWithValue("old_value_json", oldSnapshot);
                    auditCommand.Parameters.AddWithValue("new_value_json", newSnapshot);
                    await auditCommand.ExecuteNonQueryAsync(context.RequestAborted);
                }
            }

            await transaction.CommitAsync(context.RequestAborted);
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("WorkRegisterDocumentContinuityModule")
                .LogWarning(
                    "Work Register shared document deletion was unavailable ({ExceptionType}).",
                    exception.GetType().Name);
            return Results.Json(new
            {
                status = "work_register_document_delete_unavailable",
                module = "055C",
                correlationId = context.TraceIdentifier,
                message = "The document was not deleted because the governed metadata transaction could not be completed."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var cleanup = alreadyDeleted
            ? new StorageCleanupResult("not_applicable", string.Empty)
            : DeleteManagedStoredFile(storedFilePath, projectId, documentId);
        return Results.Ok(new
        {
            status = alreadyDeleted ? "document_already_deleted" : "document_deleted",
            module = "055C",
            contract = Contract,
            projectId,
            documentId,
            sharedVisibility = new
            {
                workRegister055C = "removed",
                projectWorkspace019 = "removed",
                flowHiveProjectForgeAuthority = "removed"
            },
            storageCleanup = cleanup.Status,
            storageCleanupWarning = cleanup.Warning,
            message = alreadyDeleted
                ? "Document was already deleted; shared visibility remains removed."
                : cleanup.Status == "failed"
                    ? "Document metadata was deleted from Modules 055C and 019, but physical-file cleanup failed."
                    : "Document was deleted from Modules 055C and 019 and the audit history was saved."
        });
    }

    private static async Task DeactivateSharedDocumentBridgeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var columns = await TableColumnsAsync(connection, "project_intake_documents", transaction, cancellationToken);
        if (!columns.Contains("work_register_document_id")) return;

        var assignments = new List<string>();
        if (columns.Contains("is_active")) assignments.Add("is_active = FALSE");
        if (columns.Contains("document_status")) assignments.Add("document_status = 'archived'");
        if (columns.Contains("pulse_ai_processing_updated_at")) assignments.Add("pulse_ai_processing_updated_at = NOW()");
        if (assignments.Count == 0) return;

        var sql = $"""
            UPDATE project_intake_documents
            SET {string.Join(", ", assignments)}
            WHERE work_register_document_id = @document_id
              AND project_id = @project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("document_id", documentId);
        command.Parameters.AddWithValue("project_id", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> TableColumnsAsync(
        NpgsqlConnection connection,
        string tableName,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table_name;
            """, connection, transaction);
        command.Parameters.AddWithValue("table_name", tableName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task<bool> ProjectExistsAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM projects WHERE project_id = @project_id
            );
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<(bool Valid, string? Reason, string Message)> ReadDeleteReasonAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("reason", out var reasonElement)
                || reasonElement.ValueKind != JsonValueKind.String)
            {
                return (false, null, "A delete reason is required for immutable audit history.");
            }
            var reason = reasonElement.GetString()?.Trim() ?? string.Empty;
            if (reason.Length == 0) return (false, null, "A delete reason is required for immutable audit history.");
            return reason.Length <= 2000
                ? (true, reason, string.Empty)
                : (false, null, "Delete reason must be 2,000 characters or fewer.");
        }
        catch (JsonException)
        {
            return (false, null, "Delete request must contain valid JSON with a reason.");
        }
        finally
        {
            if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;
        }
    }

    private static StorageCleanupResult DeleteManagedStoredFile(string storedFilePath, Guid projectId, Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(storedFilePath))
            return new StorageCleanupResult("not_applicable", string.Empty);

        var resolved = ProjectPulseUploadStorage.ResolveExistingStoredFile(storedFilePath, projectId, documentId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return new StorageCleanupResult(
                "not_found_on_current_storage",
                "The governed metadata was removed, but no matching managed file was proven on the current shared upload mount.");
        }

        try
        {
            File.Delete(resolved);
            return File.Exists(resolved)
                ? new StorageCleanupResult("failed", "The governed metadata was removed, but the stored file still exists after the delete attempt.")
                : new StorageCleanupResult("deleted", string.Empty);
        }
        catch
        {
            return new StorageCleanupResult("failed", "The governed metadata was removed, but physical-file cleanup failed on the shared upload mount.");
        }
    }

    private static Guid? ReadSessionUserId(HttpContext context)
    {
        if (!context.Items.TryGetValue("ProjectPulseSessionUserId", out var value)) return null;
        if (value is Guid id && id != Guid.Empty) return id;
        return Guid.TryParse(value?.ToString(), out var parsed) && parsed != Guid.Empty ? parsed : null;
    }

    private static Guid? ReadActualUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id && id != Guid.Empty) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed) && parsed != Guid.Empty) return parsed;
        }
        return null;
    }

    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString()
            ?? throw new InvalidOperationException("ProjectPulse database configuration is missing.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
        {
            "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private static bool IsActive(string value) => string.Equals(value, "active", StringComparison.OrdinalIgnoreCase);
    private static bool IsDeleted(string value) =>
        value.Equals("deleted", StringComparison.OrdinalIgnoreCase)
        || value.Equals("removed", StringComparison.OrdinalIgnoreCase)
        || value.Equals("purged", StringComparison.OrdinalIgnoreCase);
    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    private static string DisplayName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Untitled document" : value.Trim();

    private sealed record StorageCleanupResult(string Status, string Warning);
    private sealed record WorkRegisterDocumentProjection(
        string DocumentId,
        string FileName,
        string DocumentType,
        string Visibility,
        string UploadedAt,
        string DocumentReference,
        string DownloadUrl,
        string UploadSource,
        string OriginalFileName,
        string ContentType,
        long FileSizeBytes,
        string VersionLabel,
        string Status,
        string EffectiveDate,
        string Notes,
        string ArchiveReason,
        string ArchivedAt,
        string SourceTable,
        bool CanArchive,
        bool CanDelete,
        string DeleteUrl);
}

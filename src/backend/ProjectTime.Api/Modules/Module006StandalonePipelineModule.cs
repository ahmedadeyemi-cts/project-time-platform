using System.Text.RegularExpressions;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class Module006StandalonePipelineModule
{
    private static readonly HashSet<string> EditorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "SALES",
        "SALES_LEAD"
    };

    private static readonly Regex ProjectCodePattern = new(
        "^P\\.[A-Z0-9_-]{1,30}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static WebApplication MapModule006StandalonePipelineEndpoints(this WebApplication app)
    {
        app.MapGet("/api/module-006/pipeline", (Func<HttpContext, Task<IResult>>)GetPipelineAsync);
        app.MapPost("/api/module-006/pipeline", (Func<Module006CreateRequest, HttpContext, Task<IResult>>)CreateRecordAsync);
        app.MapPut("/api/module-006/pipeline/{recordId:guid}", (Func<Guid, Module006UpdateRequest, HttpContext, Task<IResult>>)UpdateRecordAsync);
        app.MapPost("/api/module-006/pipeline/{recordId:guid}/updates", (Func<Guid, Module006StatusUpdateRequest, HttpContext, Task<IResult>>)AppendUpdateAsync);
        app.MapPost("/api/module-006/pipeline/{recordId:guid}/archive", (Func<Guid, Module006ArchiveRequest, HttpContext, Task<IResult>>)ArchiveRecordAsync);
        return app;
    }

    public sealed record Module006CreateRequest(
        string? SourceProjectCode,
        string? Customer,
        string? BusinessUnit,
        string? UssOwner,
        string? ProjectName,
        string? QuoteText,
        decimal? EstimatedValue,
        string? Status,
        DateOnly? UpdateDate,
        DateOnly? NextReviewDate,
        string? Note);

    public sealed record Module006UpdateRequest(
        string? SourceProjectCode,
        string? SourceKind,
        string? Customer,
        string? BusinessUnit,
        string? UssOwner,
        string? ProjectName,
        string? QuoteText,
        decimal? EstimatedValue,
        string? Status,
        string? Lifecycle,
        DateOnly? UpdateDate,
        DateOnly? NextReviewDate,
        int ExpectedRevision);

    public sealed record Module006StatusUpdateRequest(
        string? Note,
        string? Status,
        DateOnly? UpdateDate,
        DateOnly? NextReviewDate,
        int ExpectedRevision);

    public sealed record Module006ArchiveRequest(string? Reason, int ExpectedRevision, bool Archive = true);

    private sealed record Module006Actor(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string DisplayName,
        string[] RoleCodes,
        bool IsViewAs)
    {
        public bool CanEdit => !IsViewAs && RoleCodes.Any(role => EditorRoles.Contains(role));
    }

    private static async Task<IResult> GetPipelineAsync(HttpContext context)
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            var actor = await LoadActorAsync(connection, context);
            if (actor is null) return SessionRequired();

            if (!await RuntimeReadyAsync(connection))
            {
                return Results.Json(new
                {
                    status = "module006_migration_required",
                    message = "Module 006 standalone editing requires migration 068 before records and updates can be loaded."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var records = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT record.module006_pipeline_record_id,
                       record.source_project_code,
                       record.source_kind,
                       record.customer,
                       record.business_unit,
                       record.uss_owner,
                       record.project_name,
                       record.quote_text,
                       record.estimated_value,
                       record.status,
                       record.lifecycle,
                       record.update_date,
                       record.next_review_date,
                       record.latest_note,
                       record.revision,
                       record.is_archived,
                       record.created_at,
                       record.updated_at,
                       COALESCE(created_by.display_name, created_by.email, ''),
                       COALESCE(updated_by.display_name, updated_by.email, '')
                FROM module006_pipeline_records record
                JOIN app_users created_by ON created_by.user_id = record.created_by_user_id
                JOIN app_users updated_by ON updated_by.user_id = record.updated_by_user_id
                ORDER BY record.is_archived, record.next_review_date NULLS LAST,
                         upper(record.customer), upper(record.source_project_code);
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(context.RequestAborted))
            {
                while (await reader.ReadAsync(context.RequestAborted))
                {
                    records.Add(new
                    {
                        recordId = reader.GetGuid(0),
                        sourceProjectCode = reader.GetString(1),
                        sourceKind = reader.GetString(2),
                        customer = reader.GetString(3),
                        businessUnit = reader.GetString(4),
                        ussOwner = reader.GetString(5),
                        projectName = reader.GetString(6),
                        quoteText = reader.GetString(7),
                        estimatedValue = reader.GetDecimal(8),
                        status = reader.GetString(9),
                        lifecycle = reader.GetString(10),
                        updateDate = ReadDate(reader, 11),
                        nextReviewDate = ReadDate(reader, 12),
                        latestNote = reader.GetString(13),
                        revision = reader.GetInt32(14),
                        isArchived = reader.GetBoolean(15),
                        createdAt = reader.GetFieldValue<DateTimeOffset>(16),
                        updatedAt = reader.GetFieldValue<DateTimeOffset>(17),
                        createdBy = reader.GetString(18),
                        updatedBy = reader.GetString(19)
                    });
                }
            }

            var updates = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT update.module006_pipeline_update_id,
                       update.module006_pipeline_record_id,
                       update.note_text,
                       update.status,
                       update.update_date,
                       update.next_review_date,
                       update.created_at,
                       COALESCE(actor.display_name, actor.email, '')
                FROM module006_pipeline_updates update
                JOIN app_users actor ON actor.user_id = update.created_by_user_id
                ORDER BY update.created_at DESC;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(context.RequestAborted))
            {
                while (await reader.ReadAsync(context.RequestAborted))
                {
                    updates.Add(new
                    {
                        updateId = reader.GetGuid(0),
                        recordId = reader.GetGuid(1),
                        note = reader.GetString(2),
                        status = reader.GetString(3),
                        updateDate = ReadDate(reader, 4),
                        nextReviewDate = ReadDate(reader, 5),
                        createdAt = reader.GetFieldValue<DateTimeOffset>(6),
                        createdBy = reader.GetString(7)
                    });
                }
            }

            return Results.Ok(new
            {
                status = "module006_pipeline_loaded",
                contractVersion = "module006-standalone-pipeline-v1",
                authority = "module006",
                linkedToModule055C = false,
                actor = new
                {
                    actor.DisplayName,
                    actor.IsViewAs,
                    actor.CanEdit,
                    actor.RoleCodes
                },
                records,
                updates
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "load");
        }
    }

    private static async Task<IResult> CreateRecordAsync(Module006CreateRequest request, HttpContext context)
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            var actor = await LoadActorAsync(connection, context);
            if (actor is null) return SessionRequired();
            if (actor.IsViewAs) return ViewAsReadOnly();
            if (!actor.CanEdit) return AccessDenied();
            if (!await RuntimeReadyAsync(connection)) return MigrationRequired();

            var validation = ValidateCommon(request.Customer, request.ProjectName, request.EstimatedValue);
            if (validation is not null) return validation;

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await AcquireCodeLockAsync(connection, transaction, context.RequestAborted);

            var code = NormalizeProjectCode(request.SourceProjectCode);
            if (string.IsNullOrWhiteSpace(code))
                code = await NextProjectCodeAsync(connection, transaction, context.RequestAborted);
            else if (IsReservedSnapshotCode(code))
                return Results.Conflict(new
                {
                    status = "module006_snapshot_code_reserved",
                    message = $"{code} belongs to the reviewed Module 006 snapshot. Open that row and save an update instead of creating a duplicate."
                });

            var recordId = Guid.NewGuid();
            var customer = NormalizeCustomer(request.Customer);
            var note = Clean(request.Note);
            var updateDate = request.UpdateDate ?? (string.IsNullOrWhiteSpace(note) ? null : DateOnly.FromDateTime(DateTime.UtcNow));

            await using (var command = new NpgsqlCommand("""
                INSERT INTO module006_pipeline_records (
                    module006_pipeline_record_id,
                    source_project_code,
                    source_kind,
                    customer,
                    business_unit,
                    uss_owner,
                    project_name,
                    quote_text,
                    estimated_value,
                    status,
                    lifecycle,
                    update_date,
                    next_review_date,
                    latest_note,
                    revision,
                    is_archived,
                    created_by_user_id,
                    updated_by_user_id
                ) VALUES (
                    @record_id,
                    @source_project_code,
                    'manual',
                    @customer,
                    @business_unit,
                    @uss_owner,
                    @project_name,
                    @quote_text,
                    @estimated_value,
                    @status,
                    'active',
                    @update_date,
                    @next_review_date,
                    @latest_note,
                    1,
                    FALSE,
                    @actor_id,
                    @actor_id
                );
                """, connection, transaction))
            {
                AddCommonParameters(command, recordId, code, customer, request.BusinessUnit, request.UssOwner,
                    request.ProjectName, request.QuoteText, request.EstimatedValue, request.Status,
                    updateDate, request.NextReviewDate, note, actor.EffectiveUserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                await InsertUpdateAsync(connection, transaction, recordId, note, Clean(request.Status),
                    updateDate, request.NextReviewDate, actor.EffectiveUserId, context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Created($"/api/module-006/pipeline/{recordId}", new
            {
                status = "module006_pipeline_record_created",
                message = $"{code} was added to the standalone Toyota & Hyundai Pipelines workspace.",
                recordId,
                sourceProjectCode = code,
                revision = 1,
                authority = "module006",
                linkedToModule055C = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new
            {
                status = "module006_project_code_exists",
                message = "That Module 006 Project ID already exists. Open the existing row to make changes."
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "create");
        }
    }

    private static async Task<IResult> UpdateRecordAsync(
        Guid recordId,
        Module006UpdateRequest request,
        HttpContext context)
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            var actor = await LoadActorAsync(connection, context);
            if (actor is null) return SessionRequired();
            if (actor.IsViewAs) return ViewAsReadOnly();
            if (!actor.CanEdit) return AccessDenied();
            if (!await RuntimeReadyAsync(connection)) return MigrationRequired();

            var validation = ValidateCommon(request.Customer, request.ProjectName, request.EstimatedValue);
            if (validation is not null) return validation;
            var code = NormalizeProjectCode(request.SourceProjectCode);
            if (string.IsNullOrWhiteSpace(code) || !ProjectCodePattern.IsMatch(code))
                return Invalid("Provide a valid Module 006 Project ID such as P.0052.");

            var customer = NormalizeCustomer(request.Customer);
            var lifecycle = NormalizeLifecycle(request.Lifecycle);
            var sourceKind = NormalizeSourceKind(request.SourceKind, code);

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            int? currentRevision;
            await using (var command = new NpgsqlCommand("""
                SELECT revision
                FROM module006_pipeline_records
                WHERE module006_pipeline_record_id = @record_id
                FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("record_id", recordId);
                currentRevision = await command.ExecuteScalarAsync(context.RequestAborted) as int?;
            }

            int nextRevision;
            if (currentRevision is null)
            {
                if (request.ExpectedRevision > 0)
                    return Results.Conflict(new
                    {
                        status = "module006_record_missing",
                        message = "The editable Module 006 record no longer exists. Refresh and try again."
                    });

                nextRevision = 1;
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO module006_pipeline_records (
                        module006_pipeline_record_id,
                        source_project_code,
                        source_kind,
                        customer,
                        business_unit,
                        uss_owner,
                        project_name,
                        quote_text,
                        estimated_value,
                        status,
                        lifecycle,
                        update_date,
                        next_review_date,
                        latest_note,
                        revision,
                        is_archived,
                        created_by_user_id,
                        updated_by_user_id
                    ) VALUES (
                        @record_id,
                        @source_project_code,
                        @source_kind,
                        @customer,
                        @business_unit,
                        @uss_owner,
                        @project_name,
                        @quote_text,
                        @estimated_value,
                        @status,
                        @lifecycle,
                        @update_date,
                        @next_review_date,
                        '',
                        1,
                        @is_archived,
                        @actor_id,
                        @actor_id
                    );
                    """, connection, transaction);
                AddCommonParameters(insert, recordId, code, customer, request.BusinessUnit, request.UssOwner,
                    request.ProjectName, request.QuoteText, request.EstimatedValue, request.Status,
                    request.UpdateDate, request.NextReviewDate, string.Empty, actor.EffectiveUserId);
                insert.Parameters.AddWithValue("source_kind", sourceKind);
                insert.Parameters.AddWithValue("lifecycle", lifecycle);
                insert.Parameters.AddWithValue("is_archived", lifecycle == "historical");
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }
            else
            {
                if (request.ExpectedRevision > 0 && request.ExpectedRevision != currentRevision.Value)
                    return Results.Conflict(new
                    {
                        status = "module006_revision_conflict",
                        message = "Someone else updated this Module 006 row. Refresh before saving your changes.",
                        currentRevision
                    });

                nextRevision = currentRevision.Value + 1;
                await using var update = new NpgsqlCommand("""
                    UPDATE module006_pipeline_records
                    SET source_project_code = @source_project_code,
                        source_kind = @source_kind,
                        customer = @customer,
                        business_unit = @business_unit,
                        uss_owner = @uss_owner,
                        project_name = @project_name,
                        quote_text = @quote_text,
                        estimated_value = @estimated_value,
                        status = @status,
                        lifecycle = @lifecycle,
                        update_date = @update_date,
                        next_review_date = @next_review_date,
                        is_archived = @is_archived,
                        revision = revision + 1,
                        updated_by_user_id = @actor_id,
                        updated_at = NOW()
                    WHERE module006_pipeline_record_id = @record_id;
                    """, connection, transaction);
                AddCommonParameters(update, recordId, code, customer, request.BusinessUnit, request.UssOwner,
                    request.ProjectName, request.QuoteText, request.EstimatedValue, request.Status,
                    request.UpdateDate, request.NextReviewDate, string.Empty, actor.EffectiveUserId);
                update.Parameters.AddWithValue("source_kind", sourceKind);
                update.Parameters.AddWithValue("lifecycle", lifecycle);
                update.Parameters.AddWithValue("is_archived", lifecycle == "historical");
                await update.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "module006_pipeline_record_saved",
                message = $"{code} was updated in Module 006.",
                recordId,
                sourceProjectCode = code,
                revision = nextRevision,
                authority = "module006",
                linkedToModule055C = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new
            {
                status = "module006_project_code_exists",
                message = "That Module 006 Project ID is already assigned to another row."
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "update");
        }
    }

    private static async Task<IResult> AppendUpdateAsync(
        Guid recordId,
        Module006StatusUpdateRequest request,
        HttpContext context)
    {
        try
        {
            var note = Clean(request.Note);
            if (note.Length < 3) return Invalid("Enter a status note of at least three characters.");

            await using var connection = await OpenConnectionAsync();
            var actor = await LoadActorAsync(connection, context);
            if (actor is null) return SessionRequired();
            if (actor.IsViewAs) return ViewAsReadOnly();
            if (!actor.CanEdit) return AccessDenied();
            if (!await RuntimeReadyAsync(connection)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            int? currentRevision;
            await using (var command = new NpgsqlCommand("""
                SELECT revision
                FROM module006_pipeline_records
                WHERE module006_pipeline_record_id = @record_id
                FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("record_id", recordId);
                currentRevision = await command.ExecuteScalarAsync(context.RequestAborted) as int?;
            }

            if (currentRevision is null)
                return Results.NotFound(new
                {
                    status = "module006_record_not_found",
                    message = "Save the Module 006 row before adding a history note."
                });
            if (request.ExpectedRevision > 0 && request.ExpectedRevision != currentRevision.Value)
                return Results.Conflict(new
                {
                    status = "module006_revision_conflict",
                    message = "Someone else updated this Module 006 row. Refresh before adding the note.",
                    currentRevision
                });

            var updateDate = request.UpdateDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            await InsertUpdateAsync(connection, transaction, recordId, note, Clean(request.Status),
                updateDate, request.NextReviewDate, actor.EffectiveUserId, context.RequestAborted);

            await using (var command = new NpgsqlCommand("""
                UPDATE module006_pipeline_records
                SET latest_note = @note,
                    status = CASE WHEN btrim(@status) = '' THEN status ELSE @status END,
                    update_date = @update_date,
                    next_review_date = COALESCE(@next_review_date, next_review_date),
                    revision = revision + 1,
                    updated_by_user_id = @actor_id,
                    updated_at = NOW()
                WHERE module006_pipeline_record_id = @record_id;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("record_id", recordId);
                command.Parameters.AddWithValue("note", note);
                command.Parameters.AddWithValue("status", Clean(request.Status));
                command.Parameters.AddWithValue("update_date", updateDate);
                command.Parameters.AddWithValue("next_review_date", (object?)request.NextReviewDate ?? DBNull.Value);
                command.Parameters.AddWithValue("actor_id", actor.EffectiveUserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "module006_pipeline_update_added",
                message = "The Module 006 status note was added to the append-only history.",
                recordId,
                revision = currentRevision.Value + 1
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "append_update");
        }
    }

    private static async Task<IResult> ArchiveRecordAsync(
        Guid recordId,
        Module006ArchiveRequest request,
        HttpContext context)
    {
        try
        {
            var reason = Clean(request.Reason);
            if (reason.Length < 5) return Invalid("Enter a lifecycle reason of at least five characters.");

            await using var connection = await OpenConnectionAsync();
            var actor = await LoadActorAsync(connection, context);
            if (actor is null) return SessionRequired();
            if (actor.IsViewAs) return ViewAsReadOnly();
            if (!actor.CanEdit) return AccessDenied();
            if (!await RuntimeReadyAsync(connection)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            int? revision;
            await using (var command = new NpgsqlCommand("""
                SELECT revision
                FROM module006_pipeline_records
                WHERE module006_pipeline_record_id = @record_id
                FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("record_id", recordId);
                revision = await command.ExecuteScalarAsync(context.RequestAborted) as int?;
            }
            if (revision is null)
                return Results.NotFound(new { status = "module006_record_not_found", message = "The Module 006 row was not found." });
            if (request.ExpectedRevision > 0 && request.ExpectedRevision != revision.Value)
                return Results.Conflict(new
                {
                    status = "module006_revision_conflict",
                    message = "Someone else updated this Module 006 row. Refresh before changing its lifecycle.",
                    currentRevision = revision
                });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var action = request.Archive ? "Archived" : "Restored";
            var nextStatus = request.Archive ? "Archived" : "Active";
            var note = $"{action}: {reason}";
            await InsertUpdateAsync(connection, transaction, recordId, note, nextStatus,
                today, null, actor.EffectiveUserId, context.RequestAborted);

            await using (var command = new NpgsqlCommand("""
                UPDATE module006_pipeline_records
                SET lifecycle = @lifecycle,
                    is_archived = @archive,
                    status = @status,
                    latest_note = @note,
                    update_date = @update_date,
                    revision = revision + 1,
                    updated_by_user_id = @actor_id,
                    updated_at = NOW()
                WHERE module006_pipeline_record_id = @record_id;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("record_id", recordId);
                command.Parameters.AddWithValue("lifecycle", request.Archive ? "historical" : "active");
                command.Parameters.AddWithValue("archive", request.Archive);
                command.Parameters.AddWithValue("status", nextStatus);
                command.Parameters.AddWithValue("note", note);
                command.Parameters.AddWithValue("update_date", today);
                command.Parameters.AddWithValue("actor_id", actor.EffectiveUserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = request.Archive ? "module006_pipeline_record_archived" : "module006_pipeline_record_restored",
                message = request.Archive
                    ? "The Module 006 row was archived and its history was preserved."
                    : "The Module 006 row was restored to the active pipeline.",
                recordId,
                revision = revision.Value + 1,
                authority = "module006",
                linkedToModule055C = false
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, request.Archive ? "archive" : "restore");
        }
    }

    private static async Task InsertUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid recordId,
        string note,
        string status,
        DateOnly? updateDate,
        DateOnly? nextReviewDate,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module006_pipeline_updates (
                module006_pipeline_record_id,
                note_text,
                status,
                update_date,
                next_review_date,
                created_by_user_id
            ) VALUES (
                @record_id,
                @note,
                @status,
                @update_date,
                @next_review_date,
                @actor_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("record_id", recordId);
        command.Parameters.AddWithValue("note", note);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("update_date", (object?)updateDate ?? DBNull.Value);
        command.Parameters.AddWithValue("next_review_date", (object?)nextReviewDate ?? DBNull.Value);
        command.Parameters.AddWithValue("actor_id", actorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonParameters(
        NpgsqlCommand command,
        Guid recordId,
        string code,
        string customer,
        string? businessUnit,
        string? ussOwner,
        string? projectName,
        string? quoteText,
        decimal? estimatedValue,
        string? status,
        DateOnly? updateDate,
        DateOnly? nextReviewDate,
        string latestNote,
        Guid actorId)
    {
        command.Parameters.AddWithValue("record_id", recordId);
        command.Parameters.AddWithValue("source_project_code", code);
        command.Parameters.AddWithValue("customer", customer);
        command.Parameters.AddWithValue("business_unit", Clean(businessUnit));
        command.Parameters.AddWithValue("uss_owner", Clean(ussOwner));
        command.Parameters.AddWithValue("project_name", Clean(projectName));
        command.Parameters.AddWithValue("quote_text", Clean(quoteText));
        command.Parameters.AddWithValue("estimated_value", estimatedValue ?? 0m);
        command.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(status) ? "No Status" : Clean(status));
        command.Parameters.AddWithValue("update_date", (object?)updateDate ?? DBNull.Value);
        command.Parameters.AddWithValue("next_review_date", (object?)nextReviewDate ?? DBNull.Value);
        command.Parameters.AddWithValue("latest_note", latestNote);
        command.Parameters.AddWithValue("actor_id", actorId);
    }

    private static IResult? ValidateCommon(string? customer, string? projectName, decimal? estimatedValue)
    {
        if (NormalizeCustomer(customer) is not ("Toyota" or "Hyundai"))
            return Invalid("Module 006 accepts only Toyota or Hyundai pipeline records.");
        if (string.IsNullOrWhiteSpace(projectName))
            return Invalid("Project name is required.");
        if ((estimatedValue ?? 0m) < 0m)
            return Invalid("Estimated value cannot be negative.");
        return null;
    }

    private static string NormalizeCustomer(string? value)
    {
        var normalized = Clean(value).ToLowerInvariant();
        if (normalized == "toyota") return "Toyota";
        if (normalized == "hyundai") return "Hyundai";
        return Clean(value);
    }

    private static string NormalizeProjectCode(string? value) => Clean(value).ToUpperInvariant();

    private static string NormalizeLifecycle(string? value) =>
        string.Equals(Clean(value), "historical", StringComparison.OrdinalIgnoreCase)
            ? "historical"
            : "active";

    private static string NormalizeSourceKind(string? value, string code)
    {
        if (IsReservedSnapshotCode(code)) return "snapshot_overlay";
        return string.Equals(Clean(value), "snapshot_overlay", StringComparison.OrdinalIgnoreCase)
            ? "snapshot_overlay"
            : "manual";
    }

    private static bool IsReservedSnapshotCode(string code)
    {
        var match = Regex.Match(code, "^P\\.([0-9]+)$", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) && value <= 51;
    }

    private static async Task<string> NextProjectCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(
                MAX(
                    CASE
                        WHEN source_project_code ~ '^P\.[0-9]+$'
                        THEN replace(source_project_code, 'P.', '')::integer
                        ELSE NULL
                    END
                ),
                51
            )
            FROM module006_pipeline_records;
            """, connection, transaction);
        var current = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return $"P.{current + 1:0000}";
    }

    private static async Task AcquireCodeLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('module006-standalone-project-code'));",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> RuntimeReadyAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.module006_pipeline_records') IS NOT NULL
               AND to_regclass('public.module006_pipeline_updates') IS NOT NULL
               AND EXISTS (
                    SELECT 1 FROM schema_migrations
                    WHERE migration_id = '068_module006_standalone_pipeline_management'
               );
            """, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task<Module006Actor?> LoadActorAsync(NpgsqlConnection connection, HttpContext context)
    {
        var actual = ReadGuid(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (actual is null) return null;
        var effective = ReadGuid(context, "ProjectPulseEffectiveUserId") ?? actual.Value;
        var isViewAs = context.Items.TryGetValue("ProjectPulseIsViewAs", out var flag)
            && flag is bool value
            && value;

        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(user_account.display_name, user_account.email, ''),
                   COALESCE(
                       array_agg(DISTINCT upper(role.role_code))
                           FILTER (WHERE role.role_code IS NOT NULL),
                       ARRAY[]::text[]
                   )
            FROM app_users user_account
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id = user_account.user_id
             AND assignment.is_active = TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id = assignment.app_role_id
             AND role.is_active = TRUE
            WHERE user_account.user_id = @user_id
              AND user_account.is_active = TRUE
            GROUP BY user_account.user_id;
            """, connection);
        command.Parameters.AddWithValue("user_id", effective);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted)) return null;
        var roles = reader.GetFieldValue<string[]>(1)
            .Select(ScopedRolePolicyModule.CanonicalRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new Module006Actor(actual.Value, effective, reader.GetString(0), roles, isViewAs);
    }

    private static Guid? ReadGuid(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid guid) return guid;
            if (Guid.TryParse(Convert.ToString(value), out var parsed)) return parsed;
        }
        return null;
    }

    private static DateOnly? ReadDate(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid Project Health Dashboard session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult ViewAsReadOnly() => Results.Json(new
    {
        status = "view_as_read_only",
        message = "Exit Administrator View-As before changing Module 006 pipeline data."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult AccessDenied() => Results.Json(new
    {
        status = "module006_edit_access_required",
        message = "Your current role can view Module 006 but cannot create or change its pipeline records."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult MigrationRequired() => Results.Json(new
    {
        status = "module006_migration_required",
        message = "Migration 068 must be applied before Module 006 records can be changed."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult Invalid(string message) => Results.BadRequest(new
    {
        status = "module006_invalid_request",
        message
    });

    private static IResult RuntimeFailure(Exception exception, string operation) => Results.Json(new
    {
        status = "module006_runtime_unavailable",
        operation,
        errorType = exception.GetType().Name,
        message = "Module 006 could not complete the request. Use the displayed reference when contacting an administrator."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var user = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        string? connectionString = null;

        if (!string.IsNullOrWhiteSpace(host)
            && !string.IsNullOrWhiteSpace(database)
            && !string.IsNullOrWhiteSpace(user))
        {
            connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.TryParse(port, out var parsedPort) ? parsedPort : 5432,
                Database = database,
                Username = user,
                Password = password,
                Pooling = true,
                MaxPoolSize = 20,
                IncludeErrorDetail = false
            }.ConnectionString;
        }

        connectionString ??= new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        }.Select(Environment.GetEnvironmentVariable)
         .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Project database connection is not configured.");

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}

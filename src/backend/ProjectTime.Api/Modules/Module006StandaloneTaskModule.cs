using Npgsql;

namespace ProjectTime.Api.Modules;

public static class Module006StandaloneTaskModule
{
    private static readonly HashSet<string> AuthorizedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    };

    private static readonly HashSet<string> TaskStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "not_started", "in_progress", "blocked", "completed", "cancelled"
    };

    public static WebApplication MapModule006StandaloneTaskEndpoints(this WebApplication app)
    {
        app.MapGet("/api/module-006/tasks", (Func<HttpContext, Task<IResult>>)GetTasksAsync);
        app.MapPost("/api/module-006/pipeline/{recordId:guid}/tasks", (Func<Guid, Module006TaskCreateRequest, HttpContext, Task<IResult>>)CreateTaskAsync);
        app.MapPut("/api/module-006/pipeline/{recordId:guid}/tasks/{taskId:guid}", (Func<Guid, Guid, Module006TaskUpdateRequest, HttpContext, Task<IResult>>)UpdateTaskAsync);
        app.MapPost("/api/module-006/pipeline/{recordId:guid}/tasks/{taskId:guid}/archive", (Func<Guid, Guid, Module006TaskArchiveRequest, HttpContext, Task<IResult>>)ArchiveTaskAsync);
        return app;
    }

    public sealed record Module006TaskCreateRequest(
        string? Title,
        string? Description,
        string? Status,
        string? AssignedTo,
        DateOnly? DueDate,
        string? Note);

    public sealed record Module006TaskUpdateRequest(
        string? Title,
        string? Description,
        string? Status,
        string? AssignedTo,
        DateOnly? DueDate,
        string? Note,
        int ExpectedRevision);

    public sealed record Module006TaskArchiveRequest(
        bool Archive,
        string? Reason,
        int ExpectedRevision);

    private sealed record TaskActor(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string DisplayName,
        string[] RoleCodes,
        bool IsViewAs)
    {
        public bool CanAccess => RoleCodes.Any(role => AuthorizedRoles.Contains(role));
        public bool CanEdit => !IsViewAs && CanAccess;
    }

    private static async Task<IResult> GetTasksAsync(HttpContext context)
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            var actor = await LoadActorAsync(connection, context);
            if (actor is null) return SessionRequired();
            if (!actor.CanAccess) return AccessDenied();
            if (!await RuntimeReadyAsync(connection)) return MigrationRequired();

            var tasks = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT task.module006_pipeline_task_id,
                       task.module006_pipeline_record_id,
                       task.task_title,
                       task.task_description,
                       task.task_status,
                       task.assigned_to,
                       task.due_date,
                       task.revision,
                       task.is_archived,
                       task.created_at,
                       task.updated_at,
                       COALESCE(created_by.display_name, created_by.email, ''),
                       COALESCE(updated_by.display_name, updated_by.email, '')
                FROM module006_pipeline_tasks task
                JOIN app_users created_by ON created_by.user_id = task.created_by_user_id
                JOIN app_users updated_by ON updated_by.user_id = task.updated_by_user_id
                ORDER BY task.is_archived, task.due_date NULLS LAST, task.updated_at DESC;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(context.RequestAborted))
            {
                while (await reader.ReadAsync(context.RequestAborted))
                {
                    tasks.Add(new
                    {
                        taskId = reader.GetGuid(0),
                        recordId = reader.GetGuid(1),
                        title = reader.GetString(2),
                        description = reader.GetString(3),
                        status = reader.GetString(4),
                        assignedTo = reader.GetString(5),
                        dueDate = ReadDate(reader, 6),
                        revision = reader.GetInt32(7),
                        isArchived = reader.GetBoolean(8),
                        createdAt = reader.GetFieldValue<DateTimeOffset>(9),
                        updatedAt = reader.GetFieldValue<DateTimeOffset>(10),
                        createdBy = reader.GetString(11),
                        updatedBy = reader.GetString(12)
                    });
                }
            }

            var events = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT event.module006_pipeline_task_event_id,
                       event.module006_pipeline_task_id,
                       event.event_type,
                       event.note_text,
                       event.task_status,
                       event.assigned_to,
                       event.due_date,
                       event.created_at,
                       COALESCE(actor.display_name, actor.email, '')
                FROM module006_pipeline_task_events event
                JOIN app_users actor ON actor.user_id = event.created_by_user_id
                ORDER BY event.created_at DESC;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(context.RequestAborted))
            {
                while (await reader.ReadAsync(context.RequestAborted))
                {
                    events.Add(new
                    {
                        eventId = reader.GetGuid(0),
                        taskId = reader.GetGuid(1),
                        eventType = reader.GetString(2),
                        note = reader.GetString(3),
                        status = reader.GetString(4),
                        assignedTo = reader.GetString(5),
                        dueDate = ReadDate(reader, 6),
                        createdAt = reader.GetFieldValue<DateTimeOffset>(7),
                        createdBy = reader.GetString(8)
                    });
                }
            }

            return Results.Ok(new
            {
                status = "module006_tasks_loaded",
                contractVersion = "module006-standalone-tasks-v1",
                authority = "module006",
                linkedToModule055C = false,
                actor = new { actor.DisplayName, actor.IsViewAs, actor.CanEdit, actor.RoleCodes },
                tasks,
                events
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "load tasks");
        }
    }

    private static async Task<IResult> CreateTaskAsync(
        Guid recordId,
        Module006TaskCreateRequest request,
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

            var title = Clean(request.Title);
            if (title.Length < 3) return Invalid("Enter a task title containing at least three characters.");
            var status = NormalizeStatus(request.Status);
            if (status is null) return Invalid("Select a valid task status.");

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            if (!await RecordExistsAsync(connection, transaction, recordId, context.RequestAborted))
                return Results.NotFound(new
                {
                    status = "module006_record_not_found",
                    message = "Save the Module 006 project record before creating a task."
                });

            var taskId = Guid.NewGuid();
            await using (var command = new NpgsqlCommand("""
                INSERT INTO module006_pipeline_tasks (
                    module006_pipeline_task_id,
                    module006_pipeline_record_id,
                    task_title,
                    task_description,
                    task_status,
                    assigned_to,
                    due_date,
                    revision,
                    is_archived,
                    created_by_user_id,
                    updated_by_user_id
                ) VALUES (
                    @task_id,
                    @record_id,
                    @title,
                    @description,
                    @status,
                    @assigned_to,
                    @due_date,
                    1,
                    FALSE,
                    @actor_id,
                    @actor_id
                );
                """, connection, transaction))
            {
                AddTaskParameters(command, taskId, recordId, title, request.Description, status,
                    request.AssignedTo, request.DueDate, actor.EffectiveUserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await InsertEventAsync(connection, transaction, taskId, "created", request.Note,
                status, request.AssignedTo, request.DueDate, actor.EffectiveUserId, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Created($"/api/module-006/pipeline/{recordId}/tasks/{taskId}", new
            {
                status = "module006_task_created",
                message = $"Task “{title}” was created in Module 006.",
                taskId,
                recordId,
                revision = 1,
                authority = "module006",
                linkedToModule055C = false
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "create task");
        }
    }

    private static async Task<IResult> UpdateTaskAsync(
        Guid recordId,
        Guid taskId,
        Module006TaskUpdateRequest request,
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

            var title = Clean(request.Title);
            if (title.Length < 3) return Invalid("Enter a task title containing at least three characters.");
            var status = NormalizeStatus(request.Status);
            if (status is null) return Invalid("Select a valid task status.");

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            int? currentRevision;
            await using (var command = new NpgsqlCommand("""
                SELECT revision
                FROM module006_pipeline_tasks
                WHERE module006_pipeline_task_id = @task_id
                  AND module006_pipeline_record_id = @record_id
                FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("task_id", taskId);
                command.Parameters.AddWithValue("record_id", recordId);
                currentRevision = await command.ExecuteScalarAsync(context.RequestAborted) as int?;
            }

            if (currentRevision is null)
                return Results.NotFound(new { status = "module006_task_not_found", message = "The Module 006 task no longer exists." });
            if (request.ExpectedRevision > 0 && request.ExpectedRevision != currentRevision.Value)
                return Results.Conflict(new
                {
                    status = "module006_task_revision_conflict",
                    message = "Someone else changed this task. Refresh before saving.",
                    currentRevision
                });

            await using (var command = new NpgsqlCommand("""
                UPDATE module006_pipeline_tasks
                SET task_title = @title,
                    task_description = @description,
                    task_status = @status,
                    assigned_to = @assigned_to,
                    due_date = @due_date,
                    revision = revision + 1,
                    updated_by_user_id = @actor_id,
                    updated_at = NOW()
                WHERE module006_pipeline_task_id = @task_id
                  AND module006_pipeline_record_id = @record_id;
                """, connection, transaction))
            {
                AddTaskParameters(command, taskId, recordId, title, request.Description, status,
                    request.AssignedTo, request.DueDate, actor.EffectiveUserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await InsertEventAsync(connection, transaction, taskId, "updated", request.Note,
                status, request.AssignedTo, request.DueDate, actor.EffectiveUserId, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                status = "module006_task_saved",
                message = $"Task “{title}” was updated.",
                taskId,
                recordId,
                revision = currentRevision.Value + 1,
                authority = "module006",
                linkedToModule055C = false
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "update task");
        }
    }

    private static async Task<IResult> ArchiveTaskAsync(
        Guid recordId,
        Guid taskId,
        Module006TaskArchiveRequest request,
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
            var reason = Clean(request.Reason);
            if (reason.Length < 3) return Invalid("Enter a reason containing at least three characters.");

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            int? currentRevision;
            string taskStatus;
            string assignedTo;
            DateOnly? dueDate;
            await using (var command = new NpgsqlCommand("""
                SELECT revision, task_status, assigned_to, due_date
                FROM module006_pipeline_tasks
                WHERE module006_pipeline_task_id = @task_id
                  AND module006_pipeline_record_id = @record_id
                FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("task_id", taskId);
                command.Parameters.AddWithValue("record_id", recordId);
                await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
                if (!await reader.ReadAsync(context.RequestAborted))
                    return Results.NotFound(new { status = "module006_task_not_found", message = "The Module 006 task no longer exists." });
                currentRevision = reader.GetInt32(0);
                taskStatus = reader.GetString(1);
                assignedTo = reader.GetString(2);
                dueDate = ReadDate(reader, 3);
            }

            if (request.ExpectedRevision > 0 && request.ExpectedRevision != currentRevision.Value)
                return Results.Conflict(new
                {
                    status = "module006_task_revision_conflict",
                    message = "Someone else changed this task. Refresh before changing its archive state.",
                    currentRevision
                });

            await using (var command = new NpgsqlCommand("""
                UPDATE module006_pipeline_tasks
                SET is_archived = @archive,
                    revision = revision + 1,
                    updated_by_user_id = @actor_id,
                    updated_at = NOW()
                WHERE module006_pipeline_task_id = @task_id
                  AND module006_pipeline_record_id = @record_id;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("archive", request.Archive);
                command.Parameters.AddWithValue("actor_id", actor.EffectiveUserId);
                command.Parameters.AddWithValue("task_id", taskId);
                command.Parameters.AddWithValue("record_id", recordId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await InsertEventAsync(connection, transaction, taskId,
                request.Archive ? "archived" : "restored", reason,
                taskStatus, assignedTo, dueDate, actor.EffectiveUserId, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                status = request.Archive ? "module006_task_archived" : "module006_task_restored",
                message = request.Archive ? "The Module 006 task was archived." : "The Module 006 task was restored.",
                taskId,
                recordId,
                revision = currentRevision.Value + 1,
                authority = "module006",
                linkedToModule055C = false
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, "change task archive state");
        }
    }

    private static void AddTaskParameters(
        NpgsqlCommand command,
        Guid taskId,
        Guid recordId,
        string title,
        string? description,
        string status,
        string? assignedTo,
        DateOnly? dueDate,
        Guid actorId)
    {
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("record_id", recordId);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("description", Clean(description));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("assigned_to", Clean(assignedTo));
        command.Parameters.AddWithValue("due_date", dueDate.HasValue ? (object)dueDate.Value : DBNull.Value);
        command.Parameters.AddWithValue("actor_id", actorId);
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        string eventType,
        string? note,
        string status,
        string? assignedTo,
        DateOnly? dueDate,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module006_pipeline_task_events (
                module006_pipeline_task_event_id,
                module006_pipeline_task_id,
                event_type,
                note_text,
                task_status,
                assigned_to,
                due_date,
                created_by_user_id
            ) VALUES (
                gen_random_uuid(),
                @task_id,
                @event_type,
                @note_text,
                @task_status,
                @assigned_to,
                @due_date,
                @actor_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("note_text", Clean(note));
        command.Parameters.AddWithValue("task_status", status);
        command.Parameters.AddWithValue("assigned_to", Clean(assignedTo));
        command.Parameters.AddWithValue("due_date", dueDate.HasValue ? (object)dueDate.Value : DBNull.Value);
        command.Parameters.AddWithValue("actor_id", actorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> RecordExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(
                SELECT 1 FROM module006_pipeline_records
                WHERE module006_pipeline_record_id = @record_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("record_id", recordId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string? NormalizeStatus(string? value)
    {
        var normalized = Clean(value).ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        if (string.IsNullOrWhiteSpace(normalized)) normalized = "not_started";
        return TaskStatuses.Contains(normalized) ? normalized : null;
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static DateOnly? ReadDate(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    private static async Task<bool> RuntimeReadyAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.module006_pipeline_records') IS NOT NULL
               AND to_regclass('public.module006_pipeline_tasks') IS NOT NULL
               AND to_regclass('public.module006_pipeline_task_events') IS NOT NULL
               AND EXISTS(
                    SELECT 1 FROM schema_migrations
                    WHERE migration_id='068_module006_standalone_pipeline_management'
               );
            """, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task<TaskActor?> LoadActorAsync(NpgsqlConnection connection, HttpContext context)
    {
        var actual = ReadGuid(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (actual is null) return null;
        var effective = ReadGuid(context, "ProjectPulseEffectiveUserId") ?? actual.Value;
        var isViewAs = context.Items.TryGetValue("ProjectPulseIsViewAs", out var flag) && flag is bool value && value;

        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(user_record.display_name, user_record.email, ''),
                   COALESCE(array_agg(DISTINCT upper(role.role_code)) FILTER (WHERE role.role_code IS NOT NULL), ARRAY[]::text[])
            FROM app_users user_record
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id=user_record.user_id AND assignment.is_active=TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE user_record.user_id=@user_id AND user_record.is_active=TRUE
            GROUP BY user_record.user_id;
            """, connection);
        command.Parameters.AddWithValue("user_id", effective);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted)) return null;
        var roles = reader.GetFieldValue<string[]>(1)
            .Select(ScopedRolePolicyModule.CanonicalRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new TaskActor(actual.Value, effective, reader.GetString(0), roles, isViewAs);
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

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var settings = new Dictionary<string, string?>
        {
            ["PTP_DB_HOST"] = Environment.GetEnvironmentVariable("PTP_DB_HOST"),
            ["PTP_DB_PORT"] = Environment.GetEnvironmentVariable("PTP_DB_PORT"),
            ["PTP_DB_NAME"] = Environment.GetEnvironmentVariable("PTP_DB_NAME"),
            ["PTP_DB_USER"] = Environment.GetEnvironmentVariable("PTP_DB_USER"),
            ["PTP_DB_PASSWORD"] = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD")
        };

        string connectionString;
        if (settings.Any(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            var missing = settings.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Pulse database configuration is incomplete: {string.Join(", ", missing)}.");
            connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = settings["PTP_DB_HOST"],
                Port = int.TryParse(settings["PTP_DB_PORT"], out var port) ? port : 5432,
                Database = settings["PTP_DB_NAME"],
                Username = settings["PTP_DB_USER"],
                Password = settings["PTP_DB_PASSWORD"],
                Pooling = true,
                MaxPoolSize = 20,
                IncludeErrorDetail = false
            }.ConnectionString;
        }
        else
        {
            connectionString = new[]
            {
                "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
                "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
                "PROJECTTIME_DATABASE_CONNECTION"
            }.Select(Environment.GetEnvironmentVariable)
             .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
             ?? throw new InvalidOperationException("Pulse database connection is not configured.");
        }

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid Pulse session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult AccessDenied() => Results.Json(new
    {
        status = "module006_project_manager_access_required",
        message = "Module 006 is restricted to Project Management and permanent Super Administrators."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsReadOnly() => Results.Json(new
    {
        status = "view_as_read_only",
        message = "Exit Administrator View-As before changing Module 006 tasks."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult MigrationRequired() => Results.Json(new
    {
        status = "module006_migration_required",
        message = "Module 006 standalone task management requires migration 068."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult Invalid(string message) => Results.Json(new
    {
        status = "module006_invalid_task",
        message
    }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult RuntimeFailure(Exception exception, string operation) => Results.Json(new
    {
        status = "module006_task_runtime_unavailable",
        message = $"Module 006 could not {operation}. Refresh and try again.",
        diagnostic = exception.GetType().Name
    }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

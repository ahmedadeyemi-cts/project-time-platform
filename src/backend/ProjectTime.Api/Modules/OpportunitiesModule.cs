using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class OpportunitiesModule
{
    private static readonly string[] AllowedRoleCodes =
    [
        "SALES",
        "ACCOUNT_EXECUTIVE",
        "ACCOUNT_EXECUTIVES",
        "PRESALES",
        "PRE_SALES",
        "ENGINEER",
        "ENGINEERING",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR"
    ];

    private static readonly string[] AllowedRoleTerms =
    [
        "sales",
        "account executive",
        "presales",
        "pre-sales",
        "pre sales",
        "engineer",
        "engineering"
    ];

    public static WebApplication MapOpportunityEndpoints(
        this WebApplication app)
    {
        app.MapGet("/api/opportunities/access", async (
            HttpContext context) =>
        {
            var actorId = SessionUserId(context);

            if (actorId is null)
            {
                return Results.Json(
                    new
                    {
                        status = "session_required",
                        message = "A ProjectPulse session is required."
                    },
                    statusCode: 401);
            }

            await using var connection =
                new NpgsqlConnection(ConnectionString());

            await connection.OpenAsync();

            var access = await ResolveAccessAsync(
                connection,
                actorId.Value);

            return Results.Ok(new
            {
                status = access.CanView
                    ? "opportunity_access_granted"
                    : "opportunity_access_denied",
                module = "063",
                access.UserId,
                access.DisplayName,
                access.Email,
                access.Roles,
                access.CanView,
                access.CanManage
            });
        });

        app.MapGet("/api/opportunities/options", async (
            HttpContext context) =>
        {
            var actorId = SessionUserId(context);

            if (actorId is null)
            {
                return Results.Json(
                    new { status = "session_required" },
                    statusCode: 401);
            }

            await using var connection =
                new NpgsqlConnection(ConnectionString());

            await connection.OpenAsync();

            var access = await ResolveAccessAsync(
                connection,
                actorId.Value);

            if (!access.CanView)
            {
                return Results.Json(
                    new
                    {
                        status = "access_denied",
                        message = "Module 063 is available to Account Executives, Sales, Presales, Engineers, and Administrators."
                    },
                    statusCode: 403);
            }

            var customers = new List<object>();

            await using (var command = new NpgsqlCommand("""
                SELECT client_id, client_name
                FROM clients
                WHERE is_active = TRUE
                ORDER BY client_name;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    customers.Add(new
                    {
                        clientId = reader.GetGuid(0),
                        customerName = reader.GetString(1)
                    });
                }
            }

            var users = await EligibleUsersAsync(connection);

            return Results.Ok(new
            {
                status = "opportunity_options_loaded",
                module = "063",
                currentUser = new
                {
                    userId = access.UserId,
                    displayName = access.DisplayName,
                    email = access.Email
                },
                customers,
                users,
                statuses = new[] { "active", "closed" },
                outcomes = new[] { "won", "lost", "cancelled", "other" },
                taskStatuses = new[] { "open", "completed", "cancelled" },
                assignedRoles = new[] { "Sales", "Presales", "Engineer" }
            });
        });

        app.MapGet("/api/opportunities", async (
            string? scope,
            string? search,
            HttpContext context) =>
        {
            var actorId = SessionUserId(context);

            if (actorId is null)
            {
                return Results.Json(
                    new { status = "session_required" },
                    statusCode: 401);
            }

            await using var connection =
                new NpgsqlConnection(ConnectionString());

            await connection.OpenAsync();

            var access = await ResolveAccessAsync(
                connection,
                actorId.Value);

            if (!access.CanView)
            {
                return Results.Json(
                    new { status = "access_denied" },
                    statusCode: 403);
            }

            var normalizedScope =
                NormalizeScope(scope);

            var rows = new List<object>();

            await using var command = new NpgsqlCommand("""
                SELECT
                    o.opportunity_id,
                    o.external_opportunity_id,
                    o.source_system,
                    o.client_id,
                    COALESCE(c.client_name, NULLIF(o.account_name, ''), 'Unassigned account'),
                    o.topic,
                    o.owner_user_id,
                    COALESCE(owner.display_name, owner.email, ''),
                    o.opportunity_status,
                    o.close_outcome,
                    o.estimated_revenue,
                    o.actual_revenue,
                    TO_CHAR(o.active_date, 'YYYY-MM-DD'),
                    CASE
                        WHEN o.closed_date IS NULL THEN NULL
                        ELSE TO_CHAR(o.closed_date, 'YYYY-MM-DD')
                    END,
                    o.notes,
                    o.created_by_user_id,
                    COALESCE(creator.display_name, creator.email, ''),
                    o.updated_by_user_id,
                    COALESCE(updater.display_name, updater.email, ''),
                    o.created_at,
                    o.updated_at,
                    COUNT(t.opportunity_task_id)
                        FILTER (WHERE t.task_status = 'open'),
                    COUNT(t.opportunity_task_id)
                        FILTER (WHERE t.task_status = 'completed'),
                    COUNT(t.opportunity_task_id)
                        FILTER (WHERE t.task_status = 'cancelled')
                FROM opportunities o
                LEFT JOIN clients c
                    ON c.client_id = o.client_id
                LEFT JOIN app_users owner
                    ON owner.user_id = o.owner_user_id
                JOIN app_users creator
                    ON creator.user_id = o.created_by_user_id
                JOIN app_users updater
                    ON updater.user_id = o.updated_by_user_id
                LEFT JOIN opportunity_tasks t
                    ON t.opportunity_id = o.opportunity_id
                WHERE (
                    @scope = 'all'
                    OR o.opportunity_status = @scope
                )
                  AND (
                    @search = ''
                    OR LOWER(
                        o.topic || ' '
                        || COALESCE(c.client_name, '') || ' '
                        || o.account_name || ' '
                        || o.external_opportunity_id || ' '
                        || COALESCE(owner.display_name, '') || ' '
                        || COALESCE(owner.email, '')
                    ) LIKE '%' || LOWER(@search) || '%'
                  )
                GROUP BY
                    o.opportunity_id,
                    c.client_name,
                    owner.display_name,
                    owner.email,
                    creator.display_name,
                    creator.email,
                    updater.display_name,
                    updater.email
                ORDER BY
                    CASE
                        WHEN o.opportunity_status = 'active' THEN 0
                        ELSE 1
                    END,
                    o.updated_at DESC,
                    o.topic;
                """, connection);

            command.Parameters.AddWithValue(
                "scope",
                normalizedScope);

            command.Parameters.AddWithValue(
                "search",
                search?.Trim() ?? "");

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows.Add(ReadOpportunitySummary(reader));
            }

            return Results.Ok(new
            {
                status = "opportunities_loaded",
                module = "063",
                scope = normalizedScope,
                count = rows.Count,
                opportunities = rows
            });
        });

        app.MapGet("/api/opportunities/{opportunityId:guid}", async (
            Guid opportunityId,
            HttpContext context) =>
        {
            var actorId = SessionUserId(context);

            if (actorId is null)
            {
                return Results.Json(
                    new { status = "session_required" },
                    statusCode: 401);
            }

            await using var connection =
                new NpgsqlConnection(ConnectionString());

            await connection.OpenAsync();

            var access = await ResolveAccessAsync(
                connection,
                actorId.Value);

            if (!access.CanView)
            {
                return Results.Json(
                    new { status = "access_denied" },
                    statusCode: 403);
            }

            var opportunity =
                await OpportunityDetailAsync(
                    connection,
                    opportunityId);

            if (opportunity is null)
            {
                return Results.NotFound(new
                {
                    status = "opportunity_not_found"
                });
            }

            var tasks = new List<object>();

            await using (var command = new NpgsqlCommand("""
                SELECT
                    t.opportunity_task_id,
                    t.task_title,
                    t.task_description,
                    t.assigned_role,
                    t.assigned_to_user_id,
                    COALESCE(assignee.display_name, assignee.email, ''),
                    t.due_date,
                    t.task_status,
                    t.created_by_user_id,
                    COALESCE(creator.display_name, creator.email, ''),
                    t.updated_by_user_id,
                    COALESCE(updater.display_name, updater.email, ''),
                    t.completed_by_user_id,
                    COALESCE(completer.display_name, completer.email, ''),
                    t.created_at,
                    t.updated_at,
                    t.completed_at
                FROM opportunity_tasks t
                JOIN app_users creator
                    ON creator.user_id = t.created_by_user_id
                JOIN app_users updater
                    ON updater.user_id = t.updated_by_user_id
                LEFT JOIN app_users assignee
                    ON assignee.user_id = t.assigned_to_user_id
                LEFT JOIN app_users completer
                    ON completer.user_id = t.completed_by_user_id
                WHERE t.opportunity_id = @opportunity_id
                ORDER BY
                    CASE
                        WHEN t.task_status = 'open' THEN 0
                        WHEN t.task_status = 'completed' THEN 1
                        ELSE 2
                    END,
                    t.due_date NULLS LAST,
                    t.updated_at DESC;
                """, connection))
            {
                command.Parameters.AddWithValue(
                    "opportunity_id",
                    opportunityId);

                await using var reader =
                    await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tasks.Add(new
                    {
                        opportunityTaskId = reader.GetGuid(0),
                        taskTitle = reader.GetString(1),
                        taskDescription = reader.GetString(2),
                        assignedRole = reader.GetString(3),
                        assignedToUserId = reader.IsDBNull(4)
                            ? (Guid?)null
                            : reader.GetGuid(4),
                        assignedToName = reader.GetString(5),
                        dueDate = reader.IsDBNull(6)
                            ? null
                            : reader.GetFieldValue<DateOnly>(6)
                                .ToString("yyyy-MM-dd"),
                        taskStatus = reader.GetString(7),
                        createdByUserId = reader.GetGuid(8),
                        createdByName = reader.GetString(9),
                        updatedByUserId = reader.GetGuid(10),
                        updatedByName = reader.GetString(11),
                        completedByUserId = reader.IsDBNull(12)
                            ? (Guid?)null
                            : reader.GetGuid(12),
                        completedByName = reader.GetString(13),
                        createdAt = reader.GetFieldValue<DateTime>(14),
                        updatedAt = reader.GetFieldValue<DateTime>(15),
                        completedAt = reader.IsDBNull(16)
                            ? (DateTime?)null
                            : reader.GetFieldValue<DateTime>(16)
                    });
                }
            }

            var events = new List<object>();

            await using (var command = new NpgsqlCommand("""
                SELECT
                    e.opportunity_event_id,
                    e.opportunity_task_id,
                    e.event_type,
                    e.event_details_json::text,
                    e.actor_user_id,
                    COALESCE(actor.display_name, actor.email, ''),
                    e.created_at
                FROM opportunity_events e
                JOIN app_users actor
                    ON actor.user_id = e.actor_user_id
                WHERE e.opportunity_id = @opportunity_id
                ORDER BY e.created_at DESC
                LIMIT 100;
                """, connection))
            {
                command.Parameters.AddWithValue(
                    "opportunity_id",
                    opportunityId);

                await using var reader =
                    await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    events.Add(new
                    {
                        opportunityEventId = reader.GetGuid(0),
                        opportunityTaskId = reader.IsDBNull(1)
                            ? (Guid?)null
                            : reader.GetGuid(1),
                        eventType = reader.GetString(2),
                        eventDetails = JsonDocument.Parse(
                            reader.GetString(3)).RootElement.Clone(),
                        actorUserId = reader.GetGuid(4),
                        actorName = reader.GetString(5),
                        createdAt = reader.GetFieldValue<DateTime>(6)
                    });
                }
            }

            return Results.Ok(new
            {
                status = "opportunity_loaded",
                module = "063",
                opportunity,
                tasks,
                events
            });
        });

        app.MapPost("/api/opportunities", async (
            CreateOpportunityRequest request,
            HttpContext context) =>
        {
            var actorId = SessionUserId(context);

            if (actorId is null)
            {
                return Results.Json(
                    new { status = "session_required" },
                    statusCode: 401);
            }

            if (string.IsNullOrWhiteSpace(request.Topic))
            {
                return Results.BadRequest(new
                {
                    status = "topic_required",
                    message = "Opportunity topic is required."
                });
            }

            await using var connection =
                new NpgsqlConnection(ConnectionString());

            await connection.OpenAsync();

            var access = await ResolveAccessAsync(
                connection,
                actorId.Value);

            if (!access.CanManage)
            {
                return Results.Json(
                    new { status = "access_denied" },
                    statusCode: 403);
            }

            var activeDate =
                ParseDate(request.ActiveDate)
                ?? DateOnly.FromDateTime(DateTime.UtcNow);

            var accountName =
                await ResolveAccountNameAsync(
                    connection,
                    request.ClientId,
                    request.AccountName);

            if (string.IsNullOrWhiteSpace(accountName))
            {
                return Results.BadRequest(new
                {
                    status = "account_required",
                    message = "Select a customer or enter an account name."
                });
            }

            await using var transaction =
                await connection.BeginTransactionAsync();

            var opportunityId = Guid.NewGuid();

            await using (var command = new NpgsqlCommand("""
                INSERT INTO opportunities (
                    opportunity_id,
                    external_opportunity_id,
                    source_system,
                    client_id,
                    account_name,
                    topic,
                    owner_user_id,
                    opportunity_status,
                    estimated_revenue,
                    actual_revenue,
                    active_date,
                    notes,
                    created_by_user_id,
                    updated_by_user_id
                )
                VALUES (
                    @opportunity_id,
                    @external_id,
                    @source_system,
                    @client_id,
                    @account_name,
                    @topic,
                    @owner_user_id,
                    'active',
                    @estimated_revenue,
                    @actual_revenue,
                    @active_date,
                    @notes,
                    @actor_id,
                    @actor_id
                );
                """, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "opportunity_id",
                    opportunityId);
                command.Parameters.AddWithValue(
                    "external_id",
                    request.ExternalOpportunityId?.Trim() ?? "");
                command.Parameters.AddWithValue(
                    "source_system",
                    string.IsNullOrWhiteSpace(request.SourceSystem)
                        ? "projectpulse"
                        : request.SourceSystem.Trim().ToLowerInvariant());
                command.Parameters.AddWithValue(
                    "client_id",
                    (object?)request.ClientId ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "account_name",
                    accountName);
                command.Parameters.AddWithValue(
                    "topic",
                    request.Topic.Trim());
                command.Parameters.AddWithValue(
                    "owner_user_id",
                    (object?)request.OwnerUserId ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "estimated_revenue",
                    (object?)request.EstimatedRevenue ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "actual_revenue",
                    (object?)request.ActualRevenue ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "active_date",
                    activeDate);
                command.Parameters.AddWithValue(
                    "notes",
                    request.Notes?.Trim() ?? "");
                command.Parameters.AddWithValue(
                    "actor_id",
                    actorId.Value);

                await command.ExecuteNonQueryAsync();
            }

            await InsertEventAsync(
                connection,
                transaction,
                opportunityId,
                null,
                "opportunity_created",
                actorId.Value,
                new
                {
                    request.Topic,
                    accountName,
                    activeDate = activeDate.ToString("yyyy-MM-dd")
                });

            await transaction.CommitAsync();

            return Results.Created(
                $"/api/opportunities/{opportunityId}",
                new
                {
                    status = "opportunity_created",
                    module = "063",
                    opportunityId
                });
        });

        app.MapPatch("/api/opportunities/{opportunityId:guid}", async (
            Guid opportunityId,
            UpdateOpportunityRequest request,
            HttpContext context) =>
        {
            var actorId = SessionUserId(context);

            if (actorId is null)
            {
                return Results.Json(
                    new { status = "session_required" },
                    statusCode: 401);
            }

            await using var connection =
                new NpgsqlConnection(ConnectionString());

            await connection.OpenAsync();

            var access = await ResolveAccessAsync(
                connection,
                actorId.Value);

            if (!access.CanManage)
            {
                return Results.Json(
                    new { status = "access_denied" },
                    statusCode: 403);
            }

            var status = NormalizeStatus(request.Status);
            var outcome = NormalizeOutcome(request.CloseOutcome);
            var activeDate = ParseDate(request.ActiveDate);
            var closedDate = ParseDate(request.ClosedDate);

            if (status == "closed"
                && closedDate is null)
            {
                closedDate =
                    DateOnly.FromDateTime(DateTime.UtcNow);
            }

            if (status == "closed"
                && string.IsNullOrWhiteSpace(outcome))
            {
                outcome = "other";
            }

            var accountName =
                await ResolveAccountNameAsync(
                    connection,
                    request.ClientId,
                    request.AccountName,
                    allowBlank: true);

            await using var transaction =
                await connection.BeginTransactionAsync();

            await using var command = new NpgsqlCommand("""
                UPDATE opportunities
                SET external_opportunity_id =
                        COALESCE(@external_id, external_opportunity_id),
                    source_system =
                        COALESCE(@source_system, source_system),
                    client_id =
                        COALESCE(@client_id, client_id),
                    account_name =
                        COALESCE(NULLIF(@account_name, ''), account_name),
                    topic =
                        COALESCE(NULLIF(@topic, ''), topic),
                    owner_user_id =
                        COALESCE(@owner_user_id, owner_user_id),
                    opportunity_status =
                        COALESCE(@status, opportunity_status),
                    close_outcome =
                        CASE
                            WHEN @status = 'active' THEN NULL
                            WHEN @status = 'closed'
                                THEN COALESCE(@close_outcome, close_outcome, 'other')
                            ELSE COALESCE(@close_outcome, close_outcome)
                        END,
                    estimated_revenue =
                        COALESCE(@estimated_revenue, estimated_revenue),
                    actual_revenue =
                        COALESCE(@actual_revenue, actual_revenue),
                    active_date =
                        COALESCE(@active_date, active_date),
                    closed_date =
                        CASE
                            WHEN @status = 'active' THEN NULL
                            WHEN @status = 'closed'
                                THEN COALESCE(@closed_date, closed_date, CURRENT_DATE)
                            ELSE COALESCE(@closed_date, closed_date)
                        END,
                    notes =
                        COALESCE(@notes, notes),
                    updated_by_user_id = @actor_id,
                    updated_at = NOW()
                WHERE opportunity_id = @opportunity_id;
                """, connection, transaction);

            command.Parameters.AddWithValue(
                "external_id",
                (object?)request.ExternalOpportunityId?.Trim()
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "source_system",
                (object?)request.SourceSystem?.Trim().ToLowerInvariant()
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "client_id",
                (object?)request.ClientId
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "account_name",
                accountName ?? "");
            command.Parameters.AddWithValue(
                "topic",
                request.Topic?.Trim() ?? "");
            command.Parameters.AddWithValue(
                "owner_user_id",
                (object?)request.OwnerUserId
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "status",
                (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "close_outcome",
                (object?)outcome ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "estimated_revenue",
                (object?)request.EstimatedRevenue
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "actual_revenue",
                (object?)request.ActualRevenue
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "active_date",
                (object?)activeDate
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "closed_date",
                (object?)closedDate
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "notes",
                (object?)request.Notes?.Trim()
                ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "actor_id",
                actorId.Value);
            command.Parameters.AddWithValue(
                "opportunity_id",
                opportunityId);

            var affected =
                await command.ExecuteNonQueryAsync();

            if (affected == 0)
            {
                await transaction.RollbackAsync();

                return Results.NotFound(new
                {
                    status = "opportunity_not_found"
                });
            }

            await InsertEventAsync(
                connection,
                transaction,
                opportunityId,
                null,
                status switch
                {
                    "closed" => "opportunity_closed",
                    "active" => "opportunity_reopened",
                    _ => "opportunity_updated"
                },
                actorId.Value,
                request);

            await transaction.CommitAsync();

            return Results.Ok(new
            {
                status = "opportunity_updated",
                module = "063",
                opportunityId
            });
        });

        app.MapPost("/api/opportunities/{opportunityId:guid}/tasks", async (
            Guid opportunityId,
            CreateOpportunityTaskRequest request,
            HttpContext context) =>
        {
            var actorId = SessionUserId(context);

            if (actorId is null)
            {
                return Results.Json(
                    new { status = "session_required" },
                    statusCode: 401);
            }

            if (string.IsNullOrWhiteSpace(request.TaskTitle))
            {
                return Results.BadRequest(new
                {
                    status = "task_title_required",
                    message = "Task title is required."
                });
            }

            await using var connection =
                new NpgsqlConnection(ConnectionString());

            await connection.OpenAsync();

            var access = await ResolveAccessAsync(
                connection,
                actorId.Value);

            if (!access.CanManage)
            {
                return Results.Json(
                    new { status = "access_denied" },
                    statusCode: 403);
            }

            await using var transaction =
                await connection.BeginTransactionAsync();

            var taskId = Guid.NewGuid();

            await using (var command = new NpgsqlCommand("""
                INSERT INTO opportunity_tasks (
                    opportunity_task_id,
                    opportunity_id,
                    task_title,
                    task_description,
                    assigned_role,
                    assigned_to_user_id,
                    due_date,
                    task_status,
                    created_by_user_id,
                    updated_by_user_id
                )
                SELECT
                    @task_id,
                    o.opportunity_id,
                    @task_title,
                    @task_description,
                    @assigned_role,
                    @assigned_to_user_id,
                    @due_date,
                    'open',
                    @actor_id,
                    @actor_id
                FROM opportunities o
                WHERE o.opportunity_id = @opportunity_id;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "task_id",
                    taskId);
                command.Parameters.AddWithValue(
                    "opportunity_id",
                    opportunityId);
                command.Parameters.AddWithValue(
                    "task_title",
                    request.TaskTitle.Trim());
                command.Parameters.AddWithValue(
                    "task_description",
                    request.TaskDescription?.Trim() ?? "");
                command.Parameters.AddWithValue(
                    "assigned_role",
                    request.AssignedRole?.Trim() ?? "");
                command.Parameters.AddWithValue(
                    "assigned_to_user_id",
                    (object?)request.AssignedToUserId
                    ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "due_date",
                    (object?)ParseDate(request.DueDate)
                    ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "actor_id",
                    actorId.Value);

                var affected =
                    await command.ExecuteNonQueryAsync();

                if (affected == 0)
                {
                    await transaction.RollbackAsync();

                    return Results.NotFound(new
                    {
                        status = "opportunity_not_found"
                    });
                }
            }

            await TouchOpportunityAsync(
                connection,
                transaction,
                opportunityId,
                actorId.Value);

            await InsertEventAsync(
                connection,
                transaction,
                opportunityId,
                taskId,
                "task_created",
                actorId.Value,
                new
                {
                    request.TaskTitle,
                    request.AssignedRole,
                    request.AssignedToUserId,
                    request.DueDate
                });

            await transaction.CommitAsync();

            return Results.Created(
                $"/api/opportunities/{opportunityId}",
                new
                {
                    status = "opportunity_task_created",
                    module = "063",
                    opportunityId,
                    opportunityTaskId = taskId
                });
        });

        app.MapPatch(
            "/api/opportunities/{opportunityId:guid}/tasks/{taskId:guid}",
            async (
                Guid opportunityId,
                Guid taskId,
                UpdateOpportunityTaskRequest request,
                HttpContext context) =>
            {
                var actorId = SessionUserId(context);

                if (actorId is null)
                {
                    return Results.Json(
                        new { status = "session_required" },
                        statusCode: 401);
                }

                await using var connection =
                    new NpgsqlConnection(ConnectionString());

                await connection.OpenAsync();

                var access = await ResolveAccessAsync(
                    connection,
                    actorId.Value);

                if (!access.CanManage)
                {
                    return Results.Json(
                        new { status = "access_denied" },
                        statusCode: 403);
                }

                var taskStatus =
                    NormalizeTaskStatus(request.TaskStatus);

                await using var transaction =
                    await connection.BeginTransactionAsync();

                await using var command = new NpgsqlCommand("""
                    UPDATE opportunity_tasks
                    SET task_title =
                            COALESCE(NULLIF(@task_title, ''), task_title),
                        task_description =
                            COALESCE(@task_description, task_description),
                        assigned_role =
                            COALESCE(@assigned_role, assigned_role),
                        assigned_to_user_id =
                            COALESCE(@assigned_to_user_id, assigned_to_user_id),
                        due_date =
                            COALESCE(@due_date, due_date),
                        task_status =
                            COALESCE(@task_status, task_status),
                        completed_by_user_id =
                            CASE
                                WHEN @task_status = 'completed' THEN @actor_id
                                WHEN @task_status = 'open' THEN NULL
                                ELSE completed_by_user_id
                            END,
                        completed_at =
                            CASE
                                WHEN @task_status = 'completed'
                                    THEN COALESCE(completed_at, NOW())
                                WHEN @task_status = 'open'
                                    THEN NULL
                                ELSE completed_at
                            END,
                        updated_by_user_id = @actor_id,
                        updated_at = NOW()
                    WHERE opportunity_task_id = @task_id
                      AND opportunity_id = @opportunity_id;
                    """, connection, transaction);

                command.Parameters.AddWithValue(
                    "task_title",
                    request.TaskTitle?.Trim() ?? "");
                command.Parameters.AddWithValue(
                    "task_description",
                    (object?)request.TaskDescription?.Trim()
                    ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "assigned_role",
                    (object?)request.AssignedRole?.Trim()
                    ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "assigned_to_user_id",
                    (object?)request.AssignedToUserId
                    ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "due_date",
                    (object?)ParseDate(request.DueDate)
                    ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "task_status",
                    (object?)taskStatus
                    ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "actor_id",
                    actorId.Value);
                command.Parameters.AddWithValue(
                    "task_id",
                    taskId);
                command.Parameters.AddWithValue(
                    "opportunity_id",
                    opportunityId);

                var affected =
                    await command.ExecuteNonQueryAsync();

                if (affected == 0)
                {
                    await transaction.RollbackAsync();

                    return Results.NotFound(new
                    {
                        status = "opportunity_task_not_found"
                    });
                }

                await TouchOpportunityAsync(
                    connection,
                    transaction,
                    opportunityId,
                    actorId.Value);

                await InsertEventAsync(
                    connection,
                    transaction,
                    opportunityId,
                    taskId,
                    taskStatus switch
                    {
                        "completed" => "task_completed",
                        "open" => "task_reopened",
                        "cancelled" => "task_cancelled",
                        _ => "task_updated"
                    },
                    actorId.Value,
                    request);

                await transaction.CommitAsync();

                return Results.Ok(new
                {
                    status = "opportunity_task_updated",
                    module = "063",
                    opportunityId,
                    opportunityTaskId = taskId
                });
            });

        return app;
    }

    private static object ReadOpportunitySummary(
        NpgsqlDataReader reader) =>
        new
        {
            opportunityId = reader.GetGuid(0),
            externalOpportunityId = reader.GetString(1),
            sourceSystem = reader.GetString(2),
            clientId = reader.IsDBNull(3)
                ? (Guid?)null
                : reader.GetGuid(3),
            accountName = reader.GetString(4),
            topic = reader.GetString(5),
            ownerUserId = reader.IsDBNull(6)
                ? (Guid?)null
                : reader.GetGuid(6),
            ownerName = reader.GetString(7),
            status = reader.GetString(8),
            closeOutcome = reader.IsDBNull(9)
                ? null
                : reader.GetString(9),
            estimatedRevenue = reader.IsDBNull(10)
                ? (decimal?)null
                : reader.GetDecimal(10),
            actualRevenue = reader.IsDBNull(11)
                ? (decimal?)null
                : reader.GetDecimal(11),
            activeDate = reader.GetString(12),
            closedDate = reader.IsDBNull(13)
                ? null
                : reader.GetString(13),
            notes = reader.GetString(14),
            createdByUserId = reader.GetGuid(15),
            createdByName = reader.GetString(16),
            updatedByUserId = reader.GetGuid(17),
            updatedByName = reader.GetString(18),
            createdAt = reader.GetFieldValue<DateTime>(19),
            updatedAt = reader.GetFieldValue<DateTime>(20),
            openTaskCount = reader.GetInt64(21),
            completedTaskCount = reader.GetInt64(22),
            cancelledTaskCount = reader.GetInt64(23)
        };

    private static async Task<object?> OpportunityDetailAsync(
        NpgsqlConnection connection,
        Guid opportunityId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                o.opportunity_id,
                o.external_opportunity_id,
                o.source_system,
                o.client_id,
                COALESCE(c.client_name, NULLIF(o.account_name, ''), 'Unassigned account'),
                o.topic,
                o.owner_user_id,
                COALESCE(owner.display_name, owner.email, ''),
                o.opportunity_status,
                o.close_outcome,
                o.estimated_revenue,
                o.actual_revenue,
                TO_CHAR(o.active_date, 'YYYY-MM-DD'),
                CASE
                    WHEN o.closed_date IS NULL THEN NULL
                    ELSE TO_CHAR(o.closed_date, 'YYYY-MM-DD')
                END,
                o.notes,
                o.created_by_user_id,
                COALESCE(creator.display_name, creator.email, ''),
                o.updated_by_user_id,
                COALESCE(updater.display_name, updater.email, ''),
                o.created_at,
                o.updated_at,
                COUNT(t.opportunity_task_id)
                    FILTER (WHERE t.task_status = 'open'),
                COUNT(t.opportunity_task_id)
                    FILTER (WHERE t.task_status = 'completed'),
                COUNT(t.opportunity_task_id)
                    FILTER (WHERE t.task_status = 'cancelled')
            FROM opportunities o
            LEFT JOIN clients c
                ON c.client_id = o.client_id
            LEFT JOIN app_users owner
                ON owner.user_id = o.owner_user_id
            JOIN app_users creator
                ON creator.user_id = o.created_by_user_id
            JOIN app_users updater
                ON updater.user_id = o.updated_by_user_id
            LEFT JOIN opportunity_tasks t
                ON t.opportunity_id = o.opportunity_id
            WHERE o.opportunity_id = @opportunity_id
            GROUP BY
                o.opportunity_id,
                c.client_name,
                owner.display_name,
                owner.email,
                creator.display_name,
                creator.email,
                updater.display_name,
                updater.email;
            """, connection);

        command.Parameters.AddWithValue(
            "opportunity_id",
            opportunityId);

        await using var reader =
            await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? ReadOpportunitySummary(reader)
            : null;
    }

    private static async Task<List<object>>
        EligibleUsersAsync(NpgsqlConnection connection)
    {
        var users = new List<object>();

        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT
                u.user_id,
                COALESCE(u.display_name, ''),
                COALESCE(u.email, ''),
                COALESCE(r.role_code, ''),
                COALESCE(
                    NULLIF(u.job_title, ''),
                    NULLIF(u.department_name, ''),
                    NULLIF(u.department, ''),
                    NULLIF(u.team_name, ''),
                    ''
                )
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE u.is_active = TRUE
              AND (
                    UPPER(COALESCE(r.role_code, '')) = ANY(@roles)
                 OR EXISTS (
                        SELECT 1
                        FROM UNNEST(@terms) AS t(term)
                        WHERE LOWER(
                            COALESCE(u.job_title, '') || ' '
                            || COALESCE(u.department_name, '') || ' '
                            || COALESCE(u.department, '') || ' '
                            || COALESCE(u.team_name, '')
                        )
                        LIKE '%' || LOWER(t.term) || '%'
                    )
              )
            ORDER BY 2, 3, 4;
            """, connection);

        command.Parameters.AddWithValue(
            "roles",
            AllowedRoleCodes);

        command.Parameters.AddWithValue(
            "terms",
            AllowedRoleTerms);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var email = reader.GetString(2).Trim();
            var displayName =
                ResolveDisplayName(
                    reader.GetString(1),
                    email);

            users.Add(new
            {
                userId = reader.GetGuid(0),
                displayName,
                email,
                roleCode = reader.GetString(3),
                roleOrTeam = reader.GetString(4)
            });
        }

        return users;
    }

    private static async Task<OpportunityAccess>
        ResolveAccessAsync(
            NpgsqlConnection connection,
            Guid userId)
    {
        var roles = new List<string>();
        var displayName = "";
        var email = "";

        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(u.display_name, ''),
                COALESCE(u.email, ''),
                COALESCE(r.role_code, ''),
                COALESCE(
                    NULLIF(to_jsonb(r)->>'role_name', ''),
                    NULLIF(to_jsonb(r)->>'display_name', ''),
                    NULLIF(to_jsonb(r)->>'name', ''),
                    ''
                ),
                COALESCE(u.job_title, ''),
                COALESCE(u.department_name, ''),
                COALESCE(u.department, ''),
                COALESCE(u.team_name, '')
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE u.user_id = @user_id
              AND u.is_active = TRUE;
            """, connection);

        command.Parameters.AddWithValue(
            "user_id",
            userId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            displayName = reader.GetString(0);
            email = reader.GetString(1);

            roles.AddRange(new[]
            {
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)
            }.Where(value =>
                !string.IsNullOrWhiteSpace(value)));
        }

        var normalizedRoles = roles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allowed =
            normalizedRoles.Any(IsAllowedRole);

        return new OpportunityAccess(
            userId,
            ResolveDisplayName(displayName, email),
            email,
            normalizedRoles,
            allowed,
            allowed);
    }

    private static bool IsAllowedRole(string value)
    {
        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');

        return normalized == "AE"
            || normalized.Contains("ACCOUNT_EXECUTIVE")
            || normalized.Contains("SALES")
            || normalized.Contains("PRESALES")
            || normalized.Contains("PRE_SALES")
            || normalized.Contains("ENGINEER")
            || normalized.Contains("ENGINEERING")
            || normalized.Contains("SYSTEM_ADMIN")
            || normalized.Contains("ADMINISTRATOR")
            || normalized.Contains("MANAGE_ALL");
    }

    private static async Task<string?>
        ResolveAccountNameAsync(
            NpgsqlConnection connection,
            Guid? clientId,
            string? fallback,
            bool allowBlank = false)
    {
        if (clientId is not null)
        {
            await using var command = new NpgsqlCommand("""
                SELECT client_name
                FROM clients
                WHERE client_id = @client_id
                  AND is_active = TRUE;
                """, connection);

            command.Parameters.AddWithValue(
                "client_id",
                clientId.Value);

            var value =
                await command.ExecuteScalarAsync();

            if (value is string name
                && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        var trimmed =
            fallback?.Trim() ?? "";

        return allowBlank || trimmed.Length > 0
            ? trimmed
            : null;
    }

    private static async Task TouchOpportunityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid opportunityId,
        Guid actorId)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE opportunities
            SET updated_by_user_id = @actor_id,
                updated_at = NOW()
            WHERE opportunity_id = @opportunity_id;
            """, connection, transaction);

        command.Parameters.AddWithValue(
            "actor_id",
            actorId);

        command.Parameters.AddWithValue(
            "opportunity_id",
            opportunityId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid opportunityId,
        Guid? taskId,
        string eventType,
        Guid actorId,
        object details)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO opportunity_events (
                opportunity_id,
                opportunity_task_id,
                event_type,
                event_details_json,
                actor_user_id
            )
            VALUES (
                @opportunity_id,
                @task_id,
                @event_type,
                CAST(@details AS jsonb),
                @actor_id
            );
            """, connection, transaction);

        command.Parameters.AddWithValue(
            "opportunity_id",
            opportunityId);

        command.Parameters.AddWithValue(
            "task_id",
            (object?)taskId ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "event_type",
            eventType);

        command.Parameters.AddWithValue(
            "details",
            JsonSerializer.Serialize(details));

        command.Parameters.AddWithValue(
            "actor_id",
            actorId);

        await command.ExecuteNonQueryAsync();
    }

    private static Guid? SessionUserId(
        HttpContext context)
    {
        foreach (var key in new[]
        {
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId",
            "ProjectPulseActualUserId"
        })
        {
            if (!context.Items.TryGetValue(
                    key,
                    out var value))
            {
                continue;
            }

            if (value is Guid guid)
            {
                return guid;
            }

            if (Guid.TryParse(
                    value?.ToString(),
                    out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string ConnectionString()
    {
        foreach (var name in new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        })
        {
            var value =
                Environment.GetEnvironmentVariable(name);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException(
            "ProjectPulse database connection is not configured.");
    }

    private static string ResolveDisplayName(
        string? displayName,
        string email)
    {
        var trimmed =
            displayName?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(trimmed)
            && !string.Equals(
                trimmed,
                email,
                StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var localPart =
            email.Split('@', 2)[0];

        var friendly =
            string.Join(
                " ",
                localPart
                    .Split(
                        new[] { '.', '_', '-', ' ' },
                        StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries)
                    .Select(word =>
                        char.ToUpperInvariant(word[0])
                        + word[1..].ToLowerInvariant()));

        return string.IsNullOrWhiteSpace(friendly)
            ? email
            : friendly;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static string NormalizeScope(string? value)
    {
        var normalized =
            value?.Trim().ToLowerInvariant();

        return normalized is "active" or "closed"
            ? normalized
            : "all";
    }

    private static string? NormalizeStatus(string? value)
    {
        var normalized =
            value?.Trim().ToLowerInvariant();

        return normalized is "active" or "closed"
            ? normalized
            : null;
    }

    private static string? NormalizeOutcome(string? value)
    {
        var normalized =
            value?.Trim().ToLowerInvariant();

        return normalized
            is "won"
            or "lost"
            or "cancelled"
            or "other"
            ? normalized
            : null;
    }

    private static string? NormalizeTaskStatus(string? value)
    {
        var normalized =
            value?.Trim().ToLowerInvariant();

        return normalized
            is "open"
            or "completed"
            or "cancelled"
            ? normalized
            : null;
    }

    private sealed record OpportunityAccess(
        Guid UserId,
        string DisplayName,
        string Email,
        string[] Roles,
        bool CanView,
        bool CanManage);

    private sealed record CreateOpportunityRequest(
        string? ExternalOpportunityId,
        string? SourceSystem,
        Guid? ClientId,
        string? AccountName,
        string Topic,
        Guid? OwnerUserId,
        decimal? EstimatedRevenue,
        decimal? ActualRevenue,
        string? ActiveDate,
        string? Notes);

    private sealed record UpdateOpportunityRequest(
        string? ExternalOpportunityId,
        string? SourceSystem,
        Guid? ClientId,
        string? AccountName,
        string? Topic,
        Guid? OwnerUserId,
        string? Status,
        string? CloseOutcome,
        decimal? EstimatedRevenue,
        decimal? ActualRevenue,
        string? ActiveDate,
        string? ClosedDate,
        string? Notes);

    private sealed record CreateOpportunityTaskRequest(
        string TaskTitle,
        string? TaskDescription,
        string? AssignedRole,
        Guid? AssignedToUserId,
        string? DueDate);

    private sealed record UpdateOpportunityTaskRequest(
        string? TaskTitle,
        string? TaskDescription,
        string? AssignedRole,
        Guid? AssignedToUserId,
        string? DueDate,
        string? TaskStatus);
}

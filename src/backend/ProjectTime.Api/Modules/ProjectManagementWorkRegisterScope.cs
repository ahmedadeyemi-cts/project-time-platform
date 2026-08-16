using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Server-side read scoping for Module 055C. Project Managers see only projects
/// they own. Project Management leads see their own projects plus projects owned
/// by Project Managers in their assigned teams. Project Management managers see
/// assigned-team projects as read-only. Project Team Coordinators and platform
/// administrators retain organization-wide access.
/// </summary>
internal static class ProjectManagementWorkRegisterScope
{
    private const string OverviewPath = "/api/work-register/overview";

    private static readonly HashSet<string> FullAccessRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR"
    };

    private static readonly HashSet<string> ProjectManagerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT"
    };

    private static readonly HashSet<string> ProjectManagementLeadRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    };

    private static readonly HashSet<string> ProjectManagementManagerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "MANAGER",
        "PROJECT_MANAGEMENT_MANAGER",
        "MANAGER_PROJECT_MANAGEMENT"
    };

    internal static async Task<bool> TryHandleReadAsync(
        HttpContext context,
        RequestDelegate next)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        if (path.Contains("/projects/documents//", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                status = "missing_document_id",
                module = "055C",
                message = "A valid project document ID is required before a document can be opened or downloaded."
            }, context.RequestAborted);
            return true;
        }

        if (path.Equals(OverviewPath, StringComparison.OrdinalIgnoreCase))
        {
            await WriteScopedOverviewAsync(context, next);
            return true;
        }

        if (TryReadProjectIdFromPath(path, out var projectId))
        {
            try
            {
                await using var connection = await OpenAsync(context.RequestAborted);
                var identity = await ReadIdentityAsync(connection, context, context.RequestAborted);
                if (identity is null)
                {
                    await WriteDeniedAsync(context, "A valid effective ProjectPulse identity is required.");
                    return true;
                }

                if (identity.HasFullAccess)
                {
                    await next(context);
                    return true;
                }

                var accessByProject = await ReadProjectAccessAsync(
                    connection,
                    identity,
                    projectId,
                    context.RequestAborted);
                if (!accessByProject.TryGetValue(projectId, out var access))
                {
                    await WriteDeniedAsync(context, "This project is outside your Project Management scope.");
                    return true;
                }

                context.Items["ProjectPulseWorkRegisterReadScope"] = access.Scope;
                context.Items["ProjectPulseWorkRegisterCanEdit"] = access.CanEdit;
                await next(context);
                return true;
            }
            catch (Exception exception)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ProjectManagementWorkRegisterScope")
                    .LogWarning(
                        "Work Register project read scope was unavailable ({ExceptionType}).",
                        exception.GetType().Name);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "work_register_read_scope_unavailable",
                    module = "055C",
                    correlationId = context.TraceIdentifier,
                    message = "Project visibility could not be verified. No project data was returned."
                }, context.RequestAborted);
                return true;
            }
        }

        return false;
    }

    private static async Task WriteScopedOverviewAsync(
        HttpContext context,
        RequestDelegate next)
    {
        try
        {
            await using var connection = await OpenAsync(context.RequestAborted);
            var identity = await ReadIdentityAsync(connection, context, context.RequestAborted);
            if (identity is null)
            {
                await WriteDeniedAsync(context, "A valid effective ProjectPulse identity is required.");
                return;
            }

            if (identity.HasFullAccess)
            {
                await next(context);
                return;
            }

            var projectAccess = await ReadProjectAccessAsync(
                connection,
                identity,
                projectId: null,
                context.RequestAborted);

            var originalBody = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next(context);
                buffer.Position = 0;

                if (context.Response.StatusCode != StatusCodes.Status200OK)
                {
                    context.Response.Body = originalBody;
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                JsonNode? parsed;
                try
                {
                    parsed = await JsonNode.ParseAsync(
                        buffer,
                        cancellationToken: context.RequestAborted);
                }
                catch (JsonException)
                {
                    context.Response.Body = originalBody;
                    buffer.Position = 0;
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                if (parsed is not JsonObject root)
                {
                    context.Response.Body = originalBody;
                    buffer.Position = 0;
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                var workItemsKey = FindPropertyName(root, "workItems");
                var sourceItems = workItemsKey is not null
                    ? root[workItemsKey] as JsonArray
                    : null;
                var filtered = new JsonArray();

                if (sourceItems is not null)
                {
                    foreach (var node in sourceItems)
                    {
                        if (node is not JsonObject item
                            || !TryReadProjectId(item, out var projectId)
                            || !projectAccess.TryGetValue(projectId, out var access))
                        {
                            continue;
                        }

                        var scopedItem = item.DeepClone().AsObject();
                        scopedItem["canEditProject"] = access.CanEdit;
                        scopedItem["accessScope"] = access.Scope;
                        scopedItem["accessScopeLabel"] = access.ScopeLabel;
                        filtered.Add(scopedItem);
                    }
                }

                root[workItemsKey ?? "workItems"] = filtered;
                root["summary"] = BuildSummary(filtered);
                root["access"] = new JsonObject
                {
                    ["scope"] = identity.CanViewTeamProjects
                        ? "project_management_team"
                        : identity.CanViewOwnProjects
                            ? "managed_projects"
                            : "none",
                    ["scopeLabel"] = identity.CanViewTeamProjects
                        ? "My projects and assigned Project Management team"
                        : identity.CanViewOwnProjects
                            ? "My managed projects"
                            : "No Project Management portfolio",
                    ["teamProjectsReadOnly"] = identity.CanViewTeamProjects,
                    ["projectTeamCoordinatorFullAccess"] = false,
                    ["effectiveUserId"] = identity.UserId.ToString()
                };

                context.Response.Body = originalBody;
                context.Response.ContentLength = null;
                context.Response.ContentType = "application/json; charset=utf-8";
                await JsonSerializer.SerializeAsync(
                    originalBody,
                    root,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    context.RequestAborted);
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectManagementWorkRegisterScope")
                .LogWarning(
                    "Work Register overview read scope was unavailable ({ExceptionType}).",
                    exception.GetType().Name);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "work_register_read_scope_unavailable",
                    module = "055C",
                    correlationId = context.TraceIdentifier,
                    message = "Project visibility could not be verified. No project data was returned."
                }, context.RequestAborted);
            }
        }
    }

    private static JsonObject BuildSummary(JsonArray items)
    {
        var objects = items.OfType<JsonObject>().ToArray();
        return new JsonObject
        {
            ["total"] = objects.Length,
            ["active"] = objects.Count(item =>
                string.Equals(ReadText(item, "lifecycle"), "active", StringComparison.OrdinalIgnoreCase)),
            ["closed"] = objects.Count(item =>
                string.Equals(ReadText(item, "lifecycle"), "closed", StringComparison.OrdinalIgnoreCase)),
            ["projects"] = objects.Count(item =>
                string.Equals(ReadText(item, "workType"), "Project", StringComparison.OrdinalIgnoreCase)),
            ["intakes"] = objects.Count(item =>
                string.Equals(ReadText(item, "sourceTable"), "project_intakes", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static async Task<ScopedIdentity?> ReadIdentityAsync(
        NpgsqlConnection connection,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = EffectiveUserId(context);
        if (userId is null || userId == Guid.Empty) return null;

        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(app_user.email, ''),
                COALESCE(array_agg(DISTINCT upper(role.role_code))
                    FILTER (WHERE role.role_code IS NOT NULL), ARRAY[]::text[])
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id = app_user.user_id
             AND assignment.is_active = TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id = assignment.app_role_id
             AND role.is_active = TRUE
            WHERE app_user.user_id = @user_id
              AND app_user.is_active = TRUE
            GROUP BY app_user.user_id, app_user.email;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var email = reader.GetString(0);
        var roles = reader.GetFieldValue<string[]>(1)
            .Select(role => role.Trim().ToUpperInvariant())
            .Where(role => role.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasFullAccess = roles.Overlaps(FullAccessRoles);
        var isProjectManager = roles.Overlaps(ProjectManagerRoles);
        var isLead = roles.Overlaps(ProjectManagementLeadRoles);
        var isManager = roles.Overlaps(ProjectManagementManagerRoles);

        return new ScopedIdentity(
            userId.Value,
            email,
            roles,
            hasFullAccess,
            CanViewOwnProjects: isProjectManager || isLead,
            CanViewTeamProjects: isLead || isManager,
            CanEditOwnProjects: isProjectManager || isLead);
    }

    private static async Task<Dictionary<Guid, ProjectReadAccess>> ReadProjectAccessAsync(
        NpgsqlConnection connection,
        ScopedIdentity identity,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (identity.HasFullAccess)
        {
            return new Dictionary<Guid, ProjectReadAccess>();
        }

        var assignmentTableExists = await TableExistsAsync(
            connection,
            "user_admin_manager_team_assignments",
            cancellationToken);
        var assignmentClause = assignmentTableExists
            ? """
              OR EXISTS (
                    SELECT 1
                    FROM user_admin_manager_team_assignments team_scope
                    WHERE team_scope.manager_user_id = @user_id
                      AND team_scope.is_active = TRUE
                      AND lower(team_scope.team_name) = lower(COALESCE(NULLIF(to_jsonb(project_manager)->>'team_name', ''), NULLIF(to_jsonb(project_manager)->>'department_name', ''), ''))
                )
              """
            : string.Empty;

        await using var command = new NpgsqlCommand($"""
            SELECT
                project.project_id,
                project.project_manager_user_id = @user_id AS is_owned_project
            FROM projects project
            LEFT JOIN app_users project_manager
              ON project_manager.user_id = project.project_manager_user_id
            WHERE (@project_id IS NULL OR project.project_id = @project_id)
              AND (
                    (@can_view_own = TRUE AND project.project_manager_user_id = @user_id)
                 OR (
                        @can_view_team = TRUE
                    AND (
                           lower(COALESCE(to_jsonb(project_manager)->>'manager_email', '')) = lower(@actor_email)
                           {assignmentClause}
                        )
                    )
              );
            """, connection);
        var projectIdParameter = command.Parameters.Add("project_id", NpgsqlDbType.Uuid);
        projectIdParameter.Value = projectId.HasValue
    ? (object)projectId.Value
    : DBNull.Value;
        command.Parameters.AddWithValue("user_id", identity.UserId);
        command.Parameters.AddWithValue("actor_email", identity.Email);
        command.Parameters.AddWithValue("can_view_own", identity.CanViewOwnProjects);
        command.Parameters.AddWithValue("can_view_team", identity.CanViewTeamProjects);

        var result = new Dictionary<Guid, ProjectReadAccess>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var owned = reader.GetBoolean(1);
            var canEdit = owned && identity.CanEditOwnProjects;
            result[id] = new ProjectReadAccess(
                canEdit,
                owned ? "assigned_project_manager" : "project_management_team_view",
                owned ? "Assigned Project Manager" : "Project Management team — view only");
        }

        return result;
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.' || @table_name) IS NOT NULL;",
            connection);
        command.Parameters.AddWithValue("table_name", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static bool TryReadProjectIdFromPath(string path, out Guid projectId)
    {
        projectId = Guid.Empty;
        var segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 4
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("work-register", StringComparison.OrdinalIgnoreCase)
            || !segments[2].Equals("projects", StringComparison.OrdinalIgnoreCase)
            || segments[3].Equals("documents", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Guid.TryParse(segments[3], out projectId) && projectId != Guid.Empty;
    }

    private static bool TryReadProjectId(JsonObject item, out Guid projectId)
    {
        foreach (var name in new[]
        {
            "projectId", "ProjectId", "workId", "WorkId", "sourceId", "SourceId", "id", "Id"
        })
        {
            if (!item.TryGetPropertyValue(name, out var value) || value is null) continue;
            if (Guid.TryParse(value.ToString().Trim('"'), out projectId) && projectId != Guid.Empty)
            {
                return true;
            }
        }

        projectId = Guid.Empty;
        return false;
    }

    private static string ReadText(JsonObject item, string name)
    {
        var key = FindPropertyName(item, name);
        return key is null ? string.Empty : item[key]?.ToString().Trim('"') ?? string.Empty;
    }

    private static string? FindPropertyName(JsonObject source, string expected)
    {
        foreach (var pair in source)
        {
            if (pair.Key.Equals(expected, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }

        return null;
    }

    private static Guid? EffectiveUserId(HttpContext context)
    {
        foreach (var key in new[]
        {
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId",
            "ProjectPulseActualUserId"
        })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid guid) return guid;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static async Task WriteDeniedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "access_denied",
            module = "055C",
            message
        }, context.RequestAborted);
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
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
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
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port)
                ? port
                : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private sealed record ScopedIdentity(
        Guid UserId,
        string Email,
        IReadOnlySet<string> Roles,
        bool HasFullAccess,
        bool CanViewOwnProjects,
        bool CanViewTeamProjects,
        bool CanEditOwnProjects);

    private sealed record ProjectReadAccess(
        bool CanEdit,
        string Scope,
        string ScopeLabel);
}
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class WorkRegisterAuthorization
{
    private static readonly string[] EditAllRoleCodes =
    [
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR"
    ];

    private static readonly string[] EditAssignedRoleCodes =
    [
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    ];

    private static readonly string[] CreateRoleCodes =
    [
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR"
    ];

    public static WebApplication UseWorkRegisterAuthorization(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith("/api/work-register/", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var isMutation = HttpMethods.IsPost(context.Request.Method)
                || HttpMethods.IsPut(context.Request.Method)
                || HttpMethods.IsPatch(context.Request.Method)
                || HttpMethods.IsDelete(context.Request.Method);
            var isCreateWorkflow = path.StartsWith(
                "/api/work-register/intake/packages",
                StringComparison.OrdinalIgnoreCase);

            if (!isMutation && !isCreateWorkflow)
            {
                await next();
                return;
            }

            bool allowed;
            try
            {
                await using var connection = await OpenAsync(context.RequestAborted);
                var access = await GetAccessAsync(connection, context, cancellationToken: context.RequestAborted);

                if (isCreateWorkflow)
                {
                    allowed = access.CanCreate;
                }
                else if (access.CanEditAll)
                {
                    allowed = true;
                }
                else if (access.CanEditAssigned)
                {
                    var projectId = await ResolveProjectIdAsync(context, context.RequestAborted);
                    allowed = projectId.HasValue
                        && await IsAssignedProjectManagerAsync(
                            connection,
                            access.ActualUserId,
                            projectId.Value,
                            context.RequestAborted);
                }
                else
                {
                    allowed = false;
                }
            }
            catch (Exception exception)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("WorkRegisterAuthorization")
                    .LogWarning("Work Register authorization was unavailable ({ExceptionType}).", exception.GetType().Name);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "work_register_authorization_unavailable",
                    message = "Work Register authorization is temporarily unavailable."
                }, context.RequestAborted);
                return;
            }

            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "access_denied",
                    module = isCreateWorkflow ? "055D" : "055C",
                    message = isCreateWorkflow
                        ? "Only a Project Team Coordinator, Administrator, or Super Administrator can create a Work Register record."
                        : "Only the assigned Project Manager can edit this project. Project Team Coordinators, Administrators, and Super Administrators can edit every project."
                }, context.RequestAborted);
                return;
            }

            await next();
        });

        return app;
    }

    public static async Task<WorkRegisterAccess> GetAccessAsync(
        NpgsqlConnection connection,
        HttpContext context,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var actualUserId = ActualUserId(context);
        if (actualUserId is null) return WorkRegisterAccess.Denied;

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT upper(role.role_code)
            FROM app_user_role_assignments assignment
            JOIN app_roles role ON role.app_role_id = assignment.app_role_id
            JOIN app_users app_user ON app_user.user_id = assignment.user_id
            WHERE assignment.user_id = @user_id
              AND assignment.is_active = TRUE
              AND role.is_active = TRUE
              AND app_user.is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", actualUserId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) roles.Add(reader.GetString(0));

        var isViewAs = context.Items.TryGetValue("ProjectPulseIsViewAs", out var viewAsValue)
            && viewAsValue is true;
        return new WorkRegisterAccess(
            CanEditAll: !isViewAs && EditAllRoleCodes.Any(roles.Contains),
            CanEditAssigned: !isViewAs && EditAssignedRoleCodes.Any(roles.Contains),
            CanCreate: !isViewAs && CreateRoleCodes.Any(roles.Contains),
            IsViewAs: isViewAs,
            ActualUserId: actualUserId.Value,
            RoleCodes: roles.OrderBy(value => value).ToArray());
    }

    public static async Task<bool> HasCreateAuthorityAsync(
        NpgsqlConnection connection,
        HttpContext context,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        (await GetAccessAsync(connection, context, transaction, cancellationToken)).CanCreate;

    private static async Task<Guid?> ResolveProjectIdAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        foreach (var key in new[] { "projectId", "project_id", "workId", "work_id" })
        {
            if (context.Request.RouteValues.TryGetValue(key, out var routeValue)
                && Guid.TryParse(routeValue?.ToString(), out var routeProjectId))
            {
                return routeProjectId;
            }
        }

        var pathSegments = context.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        for (var index = 0; index < pathSegments.Length - 1; index += 1)
        {
            if (string.Equals(pathSegments[index], "projects", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(pathSegments[index + 1], out var pathProjectId))
            {
                return pathProjectId;
            }
        }

        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            foreach (var key in new[] { "projectId", "project_id", "workId", "work_id" })
            {
                if (form.TryGetValue(key, out var formValue)
                    && Guid.TryParse(formValue.ToString(), out var formProjectId))
                {
                    return formProjectId;
                }
            }

            return null;
        }

        context.Request.EnableBuffering();
        if (context.Request.Body.CanSeek)
        {
            context.Request.Body.Position = 0;
        }

        try
        {
            using var document = await System.Text.Json.JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!new[] { "projectId", "project_id", "workId", "work_id" }
                        .Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Guid.TryParse(property.Value.ToString(), out var bodyProjectId))
                {
                    return bodyProjectId;
                }
            }

            return null;
        }
        finally
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }
        }
    }

    private static async Task<bool> IsAssignedProjectManagerAsync(
        NpgsqlConnection connection,
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM projects project
                WHERE project.project_id = @project_id
                  AND project.project_manager_user_id = @user_id
            );
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", userId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static Guid? ActualUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
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
}

public sealed record WorkRegisterAccess(
    bool CanEditAll,
    bool CanEditAssigned,
    bool CanCreate,
    bool IsViewAs,
    Guid ActualUserId,
    IReadOnlyList<string> RoleCodes)
{
    public bool CanEdit => CanEditAll || CanEditAssigned;

    public static WorkRegisterAccess Denied => new(false, false, false, false, Guid.Empty, []);
}

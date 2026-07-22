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
                var projectIdResolution = isCreateWorkflow
                    ? WorkRegisterProjectIdResolution.NotRequired()
                    : await ResolveProjectIdAsync(context, context.RequestAborted);

                if (projectIdResolution.Status is WorkRegisterProjectIdResolutionStatus.Invalid
                    or WorkRegisterProjectIdResolutionStatus.Conflicting)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        status = projectIdResolution.Status == WorkRegisterProjectIdResolutionStatus.Conflicting
                            ? "conflicting_project_ids"
                            : "invalid_project_id",
                        module = "055C",
                        message = projectIdResolution.Status == WorkRegisterProjectIdResolutionStatus.Conflicting
                            ? "The request contains conflicting Work Register project IDs. Submit one consistent project ID."
                            : "The request contains an invalid Work Register project ID."
                    }, context.RequestAborted);
                    return;
                }

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
                    allowed = projectIdResolution.Status == WorkRegisterProjectIdResolutionStatus.Found
                        && projectIdResolution.ProjectId.HasValue
                        && await IsAssignedProjectManagerAsync(
                            connection,
                            access.ActualUserId,
                            projectIdResolution.ProjectId.Value,
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

    internal static async Task<WorkRegisterProjectIdResolution> ResolveProjectIdAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        const string documentUploadPath = "/api/work-register/projects/documents/upload";
        var canonicalJsonPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/api/work-register/projects/documents/save",
            "/api/work-register/projects/documents/archive",
            "/api/work-register/projects/change-orders/save",
            "/api/work-register/tasks/assignments/roster/save",
            "/api/work-register/tasks/assignments/update"
        };
        var standardAliases = new[] { "projectId", "project_id", "workId", "work_id" };
        var projectUpdateAliases = new[]
        {
            "projectId",
            "project_id",
            "id",
            "workId",
            "work_id",
            "workRegisterProjectId",
            "selectedProjectId",
            "selectedWorkRegisterProjectId"
        };
        var recognizedAliases = projectUpdateAliases;
        var aliasJsonPaths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/api/work-register/projects/update"] = projectUpdateAliases,
            ["/api/work-register/projects/lifecycle"] = standardAliases
        };
        var normalizedPath = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        var candidates = new List<WorkRegisterProjectIdCandidate>();

        var pathSegments = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var isPurchaseOrderPath = pathSegments.Length == 5
            && string.Equals(pathSegments[0], "api", StringComparison.OrdinalIgnoreCase)
            && string.Equals(pathSegments[1], "work-register", StringComparison.OrdinalIgnoreCase)
            && string.Equals(pathSegments[2], "projects", StringComparison.OrdinalIgnoreCase)
            && string.Equals(pathSegments[4], "purchase-order", StringComparison.OrdinalIgnoreCase);

        if (isPurchaseOrderPath)
        {
            candidates.Add(new WorkRegisterProjectIdCandidate(
                "route:projectId",
                pathSegments[3],
                IsEndpointProjectId: true));

            var jsonCandidates = await ReadJsonProjectIdCandidatesAsync(
                context,
                actualNames: [],
                recognizedAliases,
                cancellationToken);
            if (jsonCandidates.InvalidJson) return WorkRegisterProjectIdResolution.Invalid();
            candidates.AddRange(jsonCandidates.Candidates);
        }
        else if (string.Equals(normalizedPath, documentUploadPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!context.Request.HasFormContentType)
            {
                return WorkRegisterProjectIdResolution.Missing();
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            foreach (var pair in form)
            {
                if (!recognizedAliases.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(new WorkRegisterProjectIdCandidate(
                    $"form:{pair.Key}",
                    pair.Value.ToString(),
                    IsEndpointProjectId: string.Equals(pair.Key, "projectId", StringComparison.OrdinalIgnoreCase)));
            }
        }
        else if (canonicalJsonPaths.Contains(normalizedPath)
                 || aliasJsonPaths.TryGetValue(normalizedPath, out _))
        {
            IReadOnlyList<string> actualNames = canonicalJsonPaths.Contains(normalizedPath)
                ? new[] { "projectId" }
                : aliasJsonPaths[normalizedPath];
            var jsonCandidates = await ReadJsonProjectIdCandidatesAsync(
                context,
                actualNames,
                recognizedAliases,
                cancellationToken);
            if (jsonCandidates.InvalidJson) return WorkRegisterProjectIdResolution.Invalid();
            candidates.AddRange(jsonCandidates.Candidates);
        }
        else
        {
            return WorkRegisterProjectIdResolution.Unsupported();
        }

        if (candidates.Count == 0)
        {
            return WorkRegisterProjectIdResolution.Missing();
        }

        var parsedCandidates = new List<(WorkRegisterProjectIdCandidate Candidate, Guid ProjectId)>();
        foreach (var candidate in candidates)
        {
            if (!Guid.TryParse(candidate.Value, out var parsedProjectId) || parsedProjectId == Guid.Empty)
            {
                return WorkRegisterProjectIdResolution.Invalid();
            }

            parsedCandidates.Add((candidate, parsedProjectId));
        }

        if (parsedCandidates.Select(candidate => candidate.ProjectId).Distinct().Skip(1).Any())
        {
            return WorkRegisterProjectIdResolution.Conflicting();
        }

        var endpointProjectIds = parsedCandidates
            .Where(candidate => candidate.Candidate.IsEndpointProjectId)
            .Select(candidate => candidate.ProjectId)
            .Distinct()
            .ToArray();

        return endpointProjectIds.Length switch
        {
            0 => WorkRegisterProjectIdResolution.Missing(),
            1 => WorkRegisterProjectIdResolution.Found(endpointProjectIds[0]),
            _ => WorkRegisterProjectIdResolution.Conflicting()
        };
    }

    private static async Task<(IReadOnlyList<WorkRegisterProjectIdCandidate> Candidates, bool InvalidJson)>
        ReadJsonProjectIdCandidatesAsync(
            HttpContext context,
            IReadOnlyList<string> actualNames,
            IReadOnlyList<string> aliases,
            CancellationToken cancellationToken)
    {
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
                return ([], true);
            }

            var candidates = new List<WorkRegisterProjectIdCandidate>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!aliases.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(new WorkRegisterProjectIdCandidate(
                    $"json:{property.Name}",
                    property.Value.ToString(),
                    IsEndpointProjectId: actualNames.Contains(property.Name, StringComparer.Ordinal)));
            }

            return (candidates, false);
        }
        catch (System.Text.Json.JsonException)
        {
            return ([], true);
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

internal enum WorkRegisterProjectIdResolutionStatus
{
    NotRequired,
    Found,
    Missing,
    Invalid,
    Conflicting,
    Unsupported
}

internal readonly record struct WorkRegisterProjectIdResolution(
    WorkRegisterProjectIdResolutionStatus Status,
    Guid? ProjectId)
{
    public static WorkRegisterProjectIdResolution NotRequired() =>
        new(WorkRegisterProjectIdResolutionStatus.NotRequired, null);

    public static WorkRegisterProjectIdResolution Found(Guid projectId) =>
        new(WorkRegisterProjectIdResolutionStatus.Found, projectId);

    public static WorkRegisterProjectIdResolution Missing() =>
        new(WorkRegisterProjectIdResolutionStatus.Missing, null);

    public static WorkRegisterProjectIdResolution Invalid() =>
        new(WorkRegisterProjectIdResolutionStatus.Invalid, null);

    public static WorkRegisterProjectIdResolution Conflicting() =>
        new(WorkRegisterProjectIdResolutionStatus.Conflicting, null);

    public static WorkRegisterProjectIdResolution Unsupported() =>
        new(WorkRegisterProjectIdResolutionStatus.Unsupported, null);
}

internal readonly record struct WorkRegisterProjectIdCandidate(
    string Source,
    string Value,
    bool IsEndpointProjectId);

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

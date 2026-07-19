using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 069 provides a role-scoped qualifications and certification matrix
/// over the existing ProjectPulse resource profile foundation. The package is
/// read-only and introduces no schema or data mutation.
/// </summary>
public static class QualificationsCertificationModule
{
    private const string ModuleNumber = "069";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline =
        "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";

    public static WebApplication MapQualificationsCertificationEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/qualifications/capabilities",
            (Func<HttpContext, Task<IResult>>)GetCapabilitiesAsync);

        app.MapGet(
            "/api/qualifications/matrix",
            (Func<string?, string?, string?, HttpContext, Task<IResult>>)GetMatrixAsync);

        return app;
    }

    private static async Task<IResult> GetCapabilitiesAsync(HttpContext context)
    {
        var opened = await OpenScopedConnectionAsync(context);
        if (opened.Failure is not null) return opened.Failure;

        await using var connection = opened.Connection!;
        var access = opened.Access!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Qualifications & Certification Matrix",
            status = "capabilities_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            access = AccessResponse(access, context),
            capabilities = new[]
            {
                new { code = "self_profile_visibility", state = "available", evidence = "Every active user can see their own qualification records." },
                new { code = "role_scoped_matrix", state = "available", evidence = "Authorized leaders receive organization or team-scoped rows." },
                new { code = "skill_and_certification_filters", state = "available", evidence = "Category, lifecycle, and free-text filters are server enforced." },
                new { code = "expiration_visibility", state = "available", evidence = "Effective-end dates produce current, expiring, and expired states." },
                new { code = "staffing_context", state = "available", evidence = "Function, competency, experience, team, and department are returned together." },
                new { code = "self_service_edit", state = "locked", evidence = "No mutation endpoint is authorized in this package." },
                new { code = "renewal_acknowledgement", state = "locked", evidence = "Requires approved persistence fields and audit workflow." },
                new { code = "expiration_notifications", state = "locked", evidence = "Depends on Module 067 activation and authorized notification persistence." }
            },
            databaseMutationEnabled = false,
            notificationEnabled = false
        });
    }

    private static async Task<IResult> GetMatrixAsync(
        string? search,
        string? category,
        string? status,
        HttpContext context)
    {
        var opened = await OpenScopedConnectionAsync(context);
        if (opened.Failure is not null) return opened.Failure;

        await using var connection = opened.Connection!;
        var access = opened.Access!;
        var normalizedStatus = NormalizeStatus(status);
        var normalizedCategory = category?.Trim() ?? string.Empty;
        var normalizedSearch = search?.Trim() ?? string.Empty;

        try
        {
            var rows = await LoadMatrixAsync(
                connection,
                access,
                normalizedSearch,
                normalizedCategory,
                normalizedStatus);

            var people = rows
                .GroupBy(row => row.UserId)
                .Select(group => new
                {
                    userId = group.Key,
                    displayName = group.First().DisplayName,
                    email = group.First().Email,
                    primaryFunction = group.First().PrimaryFunction,
                    teamName = group.First().TeamName,
                    departmentName = group.First().DepartmentName,
                    qualificationCount = group.Count(row => row.QualificationId is not null),
                    currentCount = group.Count(row => row.Lifecycle == "current"),
                    expiringCount = group.Count(row => row.Lifecycle == "expiring"),
                    expiredCount = group.Count(row => row.Lifecycle == "expired")
                })
                .OrderBy(row => row.displayName)
                .ToArray();

            var qualificationRows = rows
                .Where(row => row.QualificationId is not null)
                .ToArray();

            return Results.Ok(new
            {
                module = ModuleNumber,
                moduleName = "Qualifications & Certification Matrix",
                status = "matrix_loaded",
                contractVersion = ContractVersion,
                generatedAt = DateTimeOffset.UtcNow,
                access = AccessResponse(access, context),
                filters = new
                {
                    search = normalizedSearch,
                    category = normalizedCategory,
                    lifecycle = normalizedStatus,
                    expirationWindowDays = 90
                },
                summary = new
                {
                    peopleCount = people.Length,
                    qualificationCount = qualificationRows.Length,
                    currentCount = qualificationRows.Count(row => row.Lifecycle == "current"),
                    expiringCount = qualificationRows.Count(row => row.Lifecycle == "expiring"),
                    expiredCount = qualificationRows.Count(row => row.Lifecycle == "expired"),
                    unrecordedPeopleCount = people.Count(person => person.qualificationCount == 0),
                    categoryCount = qualificationRows
                        .Select(row => row.Category)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count()
                },
                categories = qualificationRows
                    .Select(row => row.Category)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToArray(),
                people,
                qualifications = qualificationRows,
                limitations = new[]
                {
                    "Existing effective_end_date is treated as the expiration or retirement date.",
                    "Evidence documents, issuer, credential identifiers, renewal target, and acknowledgement require separately authorized persistence.",
                    "This endpoint performs read-only queries and does not add, edit, acknowledge, renew, or delete qualification records.",
                    "Expiration email is disabled until shared mail and notification governance are active."
                }
            });
        }
        catch (Exception exception)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("QualificationsCertificationModule");
            logger.LogError(
                exception,
                "Module 069 failed to load its role-scoped qualification matrix.");

            return Results.Problem(
                title: "Qualifications matrix unavailable",
                detail: "The role-scoped qualifications and certification matrix could not be loaded.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<List<QualificationRow>> LoadMatrixAsync(
        NpgsqlConnection connection,
        QualificationAccess access,
        string search,
        string category,
        string lifecycle)
    {
        const string sql = """
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email,
                COALESCE(rp.primary_function, '') AS primary_function,
                COALESCE(u.team_name, '') AS team_name,
                COALESCE(u.department_name, u.department, '') AS department_name,
                q.resource_qualification_id,
                COALESCE(q.qualification_category, '') AS qualification_category,
                COALESCE(q.qualification_name, '') AS qualification_name,
                COALESCE(q.competency, '') AS competency,
                q.years_of_experience,
                q.effective_start_date,
                q.effective_end_date,
                CASE
                    WHEN q.resource_qualification_id IS NULL THEN 'unrecorded'
                    WHEN q.effective_end_date IS NOT NULL AND q.effective_end_date < CURRENT_DATE THEN 'expired'
                    WHEN q.effective_end_date IS NOT NULL AND q.effective_end_date <= CURRENT_DATE + 90 THEN 'expiring'
                    ELSE 'current'
                END AS lifecycle
            FROM app_users u
            LEFT JOIN resource_profiles rp
              ON rp.user_id = u.user_id
            LEFT JOIN resource_qualifications q
              ON q.user_id = u.user_id
            WHERE u.is_active = TRUE
              AND (
                  @broad_scope
                  OR u.user_id = @user_id
                  OR (
                      @team_scope
                      AND (
                          (@team_name <> '' AND COALESCE(u.team_name, '') = @team_name)
                          OR (@department_name <> '' AND COALESCE(u.department_name, u.department, '') = @department_name)
                      )
                  )
              )
              AND (
                  @search = ''
                  OR LOWER(
                      COALESCE(u.display_name, '') || ' ' || u.email || ' '
                      || COALESCE(rp.primary_function, '') || ' '
                      || COALESCE(q.qualification_category, '') || ' '
                      || COALESCE(q.qualification_name, '') || ' '
                      || COALESCE(q.competency, '')
                  ) LIKE '%' || LOWER(@search) || '%'
              )
              AND (
                  @category = ''
                  OR LOWER(COALESCE(q.qualification_category, '')) = LOWER(@category)
              )
              AND (
                  @lifecycle = 'all'
                  OR CASE
                      WHEN q.resource_qualification_id IS NULL THEN 'unrecorded'
                      WHEN q.effective_end_date IS NOT NULL AND q.effective_end_date < CURRENT_DATE THEN 'expired'
                      WHEN q.effective_end_date IS NOT NULL AND q.effective_end_date <= CURRENT_DATE + 90 THEN 'expiring'
                      ELSE 'current'
                  END = @lifecycle
              )
            ORDER BY display_name, qualification_category, qualification_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", access.UserId);
        command.Parameters.AddWithValue("broad_scope", access.BroadScope);
        command.Parameters.AddWithValue("team_scope", access.TeamScope);
        command.Parameters.AddWithValue("team_name", access.TeamName);
        command.Parameters.AddWithValue("department_name", access.DepartmentName);
        command.Parameters.AddWithValue("search", search);
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("lifecycle", lifecycle);

        var rows = new List<QualificationRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new QualificationRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                ReadDateOnlyOrNull(reader, 11),
                ReadDateOnlyOrNull(reader, 12),
                reader.GetString(13)));
        }

        return rows;
    }

    private static async Task<OpenOutcome> OpenScopedConnectionAsync(HttpContext context)
    {
        var userId = EffectiveSessionUserId(context);
        if (userId is null)
        {
            return new OpenOutcome(null, null, Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new OpenOutcome(null, null, Results.Json(new
            {
                module = ModuleNumber,
                status = "configuration_missing",
                message = "Qualifications authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            var access = await LoadAccessAsync(connection, userId.Value);
            if (!access.Active)
            {
                await connection.DisposeAsync();
                return new OpenOutcome(null, null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "access_denied",
                    message = "The active ProjectPulse user could not be resolved."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new OpenOutcome(connection, access, null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("QualificationsCertificationModule");
            logger.LogWarning(
                "Module 069 authorization dependency unavailable ({ExceptionType}).",
                exception.GetType().Name);
            return new OpenOutcome(null, null, Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Qualifications authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static async Task<QualificationAccess> LoadAccessAsync(
        NpgsqlConnection connection,
        Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email),
                u.email,
                COALESCE(u.team_name, ''),
                COALESCE(u.department_name, u.department, ''),
                COALESCE(string_agg(DISTINCT r.role_code, ','), ''),
                COALESCE(string_agg(DISTINCT p.permission_code, ','), '')
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id AND ura.is_active = TRUE
            LEFT JOIN app_roles r
              ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
            LEFT JOIN app_role_permissions rp
              ON rp.app_role_id = r.app_role_id
            LEFT JOIN app_permissions p
              ON p.app_permission_id = rp.app_permission_id
            WHERE u.user_id = @user_id AND u.is_active = TRUE
            GROUP BY u.user_id, u.display_name, u.email, u.team_name, u.department_name, u.department;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return QualificationAccess.Inactive(userId);

        var roles = SplitSet(reader.GetString(5));
        var permissions = SplitSet(reader.GetString(6));
        var broad = HasAny(roles,
            "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "ADMINISTRATOR",
            "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR", "EXECUTIVE", "EXECUTIVE_LEADERSHIP")
            || HasAny(permissions, "SYSTEM_ADMINISTRATION", "MANAGE_ALL");
        var team = broad || HasAny(roles,
            "MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD", "PROJECT_MANAGER", "PROJECT_MANAGEMENT")
            || HasAny(permissions,
                "VIEW_TEAM_UTILIZATION", "VIEW_RESOURCE_SCHEDULING", "MANAGE_RESOURCE_SCHEDULING");

        return new QualificationAccess(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            roles,
            permissions,
            broad,
            team,
            true);
    }

    private static object AccessResponse(QualificationAccess access, HttpContext context) => new
    {
        effectiveUserId = access.UserId,
        access.DisplayName,
        roles = access.Roles.OrderBy(value => value).ToArray(),
        scope = access.BroadScope ? "organization" : access.TeamScope ? "team" : "self",
        isViewAs = context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true,
        serverAuthorized = true
    };

    private static Guid? EffectiveSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static HashSet<string> SplitSet(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool HasAny(IReadOnlySet<string> values, params string[] candidates) =>
        candidates.Any(values.Contains);

    private static string NormalizeStatus(string? value)
    {
        var normalized = (value ?? "all").Trim().ToLowerInvariant();
        return normalized is "current" or "expiring" or "expired" or "unrecorded"
            ? normalized
            : "all";
    }

    private static DateOnly? ReadDateOnlyOrNull(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? string.Empty)
        };
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
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;

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

    private sealed record OpenOutcome(
        NpgsqlConnection? Connection,
        QualificationAccess? Access,
        IResult? Failure);

    private sealed record QualificationAccess(
        Guid UserId,
        string DisplayName,
        string Email,
        string TeamName,
        string DepartmentName,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        bool BroadScope,
        bool TeamScope,
        bool Active)
    {
        public static QualificationAccess Inactive(Guid userId) => new(
            userId, string.Empty, string.Empty, string.Empty, string.Empty,
            new HashSet<string>(), new HashSet<string>(), false, false, false);
    }

    private sealed record QualificationRow(
        Guid UserId,
        string DisplayName,
        string Email,
        string PrimaryFunction,
        string TeamName,
        string DepartmentName,
        Guid? QualificationId,
        string Category,
        string Name,
        string Competency,
        decimal? YearsOfExperience,
        DateOnly? EffectiveStartDate,
        DateOnly? EffectiveEndDate,
        string Lifecycle);
}

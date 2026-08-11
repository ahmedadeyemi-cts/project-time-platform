using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 069 self-service editing for an authenticated user's own qualification
/// and certification records. View-As is always read-only. Organization-wide
/// editing remains a separate administrator capability.
/// </summary>
public static class QualificationsCertificationSelfServiceModule
{
    private const string ModuleNumber = "069";
    private const int MaximumTextLength = 255;
    private static readonly HashSet<string> SelfServiceRoles = new(
        new[]
        {
            "ENGINEER",
            "ENGINEERING",
            "ENGINEERING_LEAD",
            "ENGINEERING_MANAGER",
            "ENGINEERING_TEAM_LEAD",
            "PROJECT_MANAGER",
            "PROJECT_MANAGEMENT",
            "PROJECT_MANAGEMENT_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD",
            "PM_TEAM_LEAD",
            "MANAGER",
            "PEOPLE_MANAGER",
            "SOLUTION_ARCHITECT",
            "ARCHITECT",
            "SA",
            "SAA",
            "PROJECT_TEAM_COORDINATOR"
        },
        StringComparer.OrdinalIgnoreCase);

    public static WebApplication MapQualificationsCertificationSelfServiceEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/qualifications/self-service",
            (Func<HttpContext, Task<IResult>>)GetSelfServiceAsync);
        app.MapPost(
            "/api/qualifications/self-service",
            (Func<HttpContext, Task<IResult>>)CreateAsync);
        app.MapPut(
            "/api/qualifications/self-service/{qualificationId:guid}",
            (Guid qualificationId, HttpContext context) => UpdateAsync(qualificationId, context));
        return app;
    }

    private static async Task<IResult> GetSelfServiceAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;

        await using var connection = access.Connection!;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                resource_qualification_id,
                qualification_category,
                qualification_name,
                COALESCE(competency, ''),
                years_of_experience,
                effective_start_date,
                effective_end_date,
                created_at,
                updated_at
            FROM resource_qualifications
            WHERE user_id = @user_id
            ORDER BY
                CASE WHEN effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE THEN 0 ELSE 1 END,
                qualification_category,
                qualification_name,
                effective_start_date DESC;
            """, connection);
        command.Parameters.AddWithValue("user_id", access.Context!.EffectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            rows.Add(new
            {
                qualificationId = reader.GetGuid(0),
                category = reader.GetString(1),
                name = reader.GetString(2),
                competency = reader.GetString(3),
                yearsOfExperience = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4),
                effectiveStartDate = reader.GetFieldValue<DateOnly>(5),
                effectiveEndDate = reader.IsDBNull(6) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(6),
                createdAt = reader.GetFieldValue<DateTimeOffset>(7),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(8)
            });
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "qualification_self_service_loaded",
            access = new
            {
                access.Context.EffectiveUserId,
                canEditOwn = access.Context.CanEditOwn,
                isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context),
                scope = "self",
                serverAuthorized = true
            },
            qualifications = rows,
            count = rows.Count,
            secretValuesReturned = false
        });
    }

    private static async Task<IResult> CreateAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        var mutationFailure = RequireMutation(context, access.Context!);
        if (mutationFailure is not null)
        {
            await access.Connection!.DisposeAsync();
            return mutationFailure;
        }

        var body = await ReadRequestAsync(context);
        if (body.Failure is not null)
        {
            await access.Connection!.DisposeAsync();
            return body.Failure;
        }

        await using var connection = access.Connection!;
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            var qualificationId = Guid.NewGuid();
            await using var command = new NpgsqlCommand("""
                INSERT INTO resource_qualifications (
                    resource_qualification_id,
                    user_id,
                    qualification_category,
                    qualification_name,
                    competency,
                    years_of_experience,
                    effective_start_date,
                    effective_end_date,
                    created_at,
                    updated_at
                )
                VALUES (
                    @qualification_id,
                    @user_id,
                    @category,
                    @name,
                    @competency,
                    @years,
                    @start_date,
                    @end_date,
                    NOW(),
                    NOW()
                );
                """, connection, transaction);
            Bind(command, qualificationId, access.Context!.ActualUserId, body.Value!);
            await command.ExecuteNonQueryAsync(context.RequestAborted);
            await WriteAuditBestEffortAsync(
                connection,
                transaction,
                access.Context.ActualUserId,
                qualificationId,
                "qualification_created",
                body.Value!,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Json(new
            {
                module = ModuleNumber,
                status = "qualification_created",
                qualificationId,
                userId = access.Context.ActualUserId,
                message = "Your qualification or certification was added.",
                secretValuesReturned = false
            }, statusCode: StatusCodes.Status201Created);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            LogFailure(context, exception, "create qualification");
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "qualification_create_unavailable",
                message = "The qualification or certification could not be added."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid qualificationId,
        HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        var mutationFailure = RequireMutation(context, access.Context!);
        if (mutationFailure is not null)
        {
            await access.Connection!.DisposeAsync();
            return mutationFailure;
        }

        var body = await ReadRequestAsync(context);
        if (body.Failure is not null)
        {
            await access.Connection!.DisposeAsync();
            return body.Failure;
        }

        await using var connection = access.Connection!;
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            int changed;
            await using (var command = new NpgsqlCommand("""
                UPDATE resource_qualifications
                SET qualification_category = @category,
                    qualification_name = @name,
                    competency = @competency,
                    years_of_experience = @years,
                    effective_start_date = @start_date,
                    effective_end_date = @end_date,
                    updated_at = NOW()
                WHERE resource_qualification_id = @qualification_id
                  AND user_id = @user_id;
                """, connection, transaction))
            {
                Bind(command, qualificationId, access.Context!.ActualUserId, body.Value!);
                changed = await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            if (changed == 0)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new
                {
                    module = ModuleNumber,
                    status = "qualification_not_found",
                    message = "That qualification is not part of your editable profile."
                });
            }

            await WriteAuditBestEffortAsync(
                connection,
                transaction,
                access.Context.ActualUserId,
                qualificationId,
                "qualification_updated",
                body.Value!,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "qualification_updated",
                qualificationId,
                userId = access.Context.ActualUserId,
                message = "Your qualification or certification was updated.",
                secretValuesReturned = false
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            LogFailure(context, exception, "update qualification");
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "qualification_update_unavailable",
                message = "The qualification or certification could not be updated."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static IResult? RequireMutation(HttpContext context, SelfServiceAccess access)
    {
        if (ProjectPulseActualSessionAuthority.IsViewAs(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "view_as_read_only",
                message = "Exit Administrator View-As before changing qualification records."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (access.ActualUserId != access.EffectiveUserId)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "own_session_required",
                message = "Qualifications can be changed only in the user's own authenticated session."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!access.CanEditOwn)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "qualification_self_service_permission_required",
                permission = "MANAGE_OWN_QUALIFICATIONS_069",
                message = "Your role is not configured for qualification self-service."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private static async Task<RequestOutcome> ReadRequestAsync(HttpContext context)
    {
        QualificationRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<QualificationRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            request = null;
        }

        if (request is null)
            return RequestOutcome.Fail(Invalid("A valid qualification request is required."));

        var category = request.Category?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var competency = request.Competency?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name))
            return RequestOutcome.Fail(Invalid("Category and qualification or certification name are required."));
        if (category.Length > MaximumTextLength
            || name.Length > MaximumTextLength
            || competency.Length > 100)
        {
            return RequestOutcome.Fail(Invalid("Qualification text exceeds the supported length."));
        }
        if (request.YearsOfExperience is < 0 or > 99.99m)
            return RequestOutcome.Fail(Invalid("Years of experience must be between 0 and 99.99."));

        var start = request.EffectiveStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.EffectiveEndDate is not null && request.EffectiveEndDate < start)
            return RequestOutcome.Fail(Invalid("The expiration or end date cannot precede the effective start date."));

        return new RequestOutcome(new NormalizedRequest(
            category,
            name,
            competency,
            request.YearsOfExperience,
            start,
            request.EffectiveEndDate), null);
    }

    private static async Task<AccessOutcome> ResolveAccessAsync(HttpContext context)
    {
        try
        {
            await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                context,
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            // Normal role authorization below remains authoritative.
        }

        var actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        var effectiveUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId");
        if (!actualUserId.HasValue || !effectiveUserId.HasValue)
        {
            return AccessOutcome.Fail(Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid Pulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return AccessOutcome.Fail(Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Qualification self-service authorization is unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT
                    COALESCE(array_agg(DISTINCT upper(role.role_code))
                        FILTER (WHERE role.role_code IS NOT NULL), ARRAY[]::text[]),
                    COALESCE(array_agg(DISTINCT upper(permission.permission_code))
                        FILTER (WHERE permission.permission_code IS NOT NULL), ARRAY[]::text[])
                FROM app_users app_user
                LEFT JOIN app_user_role_assignments assignment
                  ON assignment.user_id = app_user.user_id
                 AND assignment.is_active = TRUE
                LEFT JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                LEFT JOIN app_role_permissions relationship
                  ON relationship.app_role_id = role.app_role_id
                LEFT JOIN app_permissions permission
                  ON permission.app_permission_id = relationship.app_permission_id
                WHERE app_user.user_id = @user_id
                  AND app_user.is_active = TRUE
                GROUP BY app_user.user_id;
                """, connection);
            command.Parameters.AddWithValue("user_id", effectiveUserId.Value);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted))
            {
                await connection.DisposeAsync();
                return AccessOutcome.Fail(Results.Json(new
                {
                    module = ModuleNumber,
                    status = "active_user_required",
                    message = "The active Pulse user could not be resolved."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            var roles = reader.GetFieldValue<string[]>(0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var permissions = reader.GetFieldValue<string[]>(1)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var administrator = roles.Any(ProjectPulseActualSessionAuthority.IsAdministratorRoleCode)
                || context.Items.TryGetValue("ProjectPulsePermanentFullControl", out var permanent)
                   && permanent is true;
            var canEditOwn = administrator
                || permissions.Contains("MANAGE_OWN_QUALIFICATIONS_069")
                || permissions.Contains("MANAGE_ALL")
                || roles.Any(SelfServiceRoles.Contains);

            return new AccessOutcome(
                connection,
                new SelfServiceAccess(
                    actualUserId.Value,
                    effectiveUserId.Value,
                    roles,
                    permissions,
                    canEditOwn),
                null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            LogFailure(context, exception, "authorize qualification self-service");
            return AccessOutcome.Fail(Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Qualification self-service authorization is unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static void Bind(
        NpgsqlCommand command,
        Guid qualificationId,
        Guid userId,
        NormalizedRequest request)
    {
        command.Parameters.AddWithValue("qualification_id", qualificationId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("category", request.Category);
        command.Parameters.AddWithValue("name", request.Name);
        command.Parameters.AddWithValue("competency", request.Competency);
        command.Parameters.AddWithValue("years", (object?)request.YearsOfExperience ?? DBNull.Value);
        command.Parameters.AddWithValue("start_date", request.EffectiveStartDate);
        command.Parameters.AddWithValue("end_date", (object?)request.EffectiveEndDate ?? DBNull.Value);
    }

    private static async Task WriteAuditBestEffortAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        Guid qualificationId,
        string actionCode,
        NormalizedRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await SecurityDiagnosticsOperations.WriteAuditAsync(
                connection,
                transaction,
                ModuleNumber,
                "resource_qualification",
                qualificationId.ToString(),
                actionCode,
                actorUserId,
                new
                {
                    request.Category,
                    request.Name,
                    request.Competency,
                    request.YearsOfExperience,
                    request.EffectiveStartDate,
                    request.EffectiveEndDate,
                    selfService = true,
                    secretValuesReturned = false
                },
                cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Existing qualification persistence remains authoritative when the
            // optional shared module-audit table has not yet been installed.
        }
    }

    private static IResult Invalid(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_request",
        message
    });

    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("QualificationsCertificationSelfServiceModule")
            .LogWarning(
                "Module 069 could not {Operation} ({ExceptionType}); raw detail suppressed.",
                operation,
                exception.GetType().Name);
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
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private sealed record QualificationRequest(
        string? Category,
        string? Name,
        string? Competency,
        decimal? YearsOfExperience,
        DateOnly? EffectiveStartDate,
        DateOnly? EffectiveEndDate);

    private sealed record NormalizedRequest(
        string Category,
        string Name,
        string Competency,
        decimal? YearsOfExperience,
        DateOnly EffectiveStartDate,
        DateOnly? EffectiveEndDate);

    private sealed record SelfServiceAccess(
        Guid ActualUserId,
        Guid EffectiveUserId,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        bool CanEditOwn);

    private sealed record AccessOutcome(
        NpgsqlConnection? Connection,
        SelfServiceAccess? Context,
        IResult? Failure)
    {
        internal static AccessOutcome Fail(IResult failure) => new(null, null, failure);
    }

    private sealed record RequestOutcome(NormalizedRequest? Value, IResult? Failure)
    {
        internal static RequestOutcome Fail(IResult failure) => new(null, failure);
    }
}

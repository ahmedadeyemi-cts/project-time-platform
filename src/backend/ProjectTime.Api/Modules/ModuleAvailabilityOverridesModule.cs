using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Lightweight read contract for the existing Modules directory.
/// Only persisted overrides are returned; missing rows mean Enabled.
/// An actual Super Administrator receives the complete module catalog even when
/// the optional override table is not installed or a legacy ADMINISTRATOR role
/// assignment has not yet been canonicalized.
/// </summary>
public static class ModuleAvailabilityOverridesModule
{
    private const string MigrationFile = "042_module_availability_controls.sql";
    private const int RegisteredModuleCount = 64;

    public static WebApplication MapModuleAvailabilityOverrideEndpoints(this WebApplication app)
    {
        // Force the Minimal API Delegate overload. A direct method-group binding can
        // select RequestDelegate and discard the returned IResult, producing HTTP 200
        // with an empty body.
        app.MapGet(
            "/api/module-availability/overrides",
            (Func<HttpContext, Task<IResult>>)GetOverridesAsync);
        return app;
    }

    private static async Task<IResult> GetOverridesAsync(HttpContext context)
    {
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (actualUserId is null || effectiveUserId is null)
        {
            return Results.Json(new { status = "session_required" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return DependencyUnavailable();
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);

            var permanentFullControl = await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                context,
                connection,
                cancellationToken: context.RequestAborted);

            // The permanent-authority resolver may reconcile an older session user
            // identifier to the active app-user row with the trusted session email.
            actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId")
                ?? actualUserId;
            effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId")
                ?? effectiveUserId;

            var actualRoles = await ReadRolesAsync(connection, actualUserId.Value, context.RequestAborted);
            var effectiveRoles = actualUserId == effectiveUserId
                ? actualRoles
                : await ReadRolesAsync(connection, effectiveUserId.Value, context.RequestAborted);
            var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context);
            var actualAdministrator = permanentFullControl
                || actualRoles.Any(ProjectPulseActualSessionAuthority.IsAdministratorRoleCode);
            var effectiveAdministrator = isViewAs
                ? effectiveRoles.Any(ProjectPulseActualSessionAuthority.IsAdministratorRoleCode)
                : actualAdministrator;
            var canManage = actualAdministrator && !isViewAs;

            var states = new List<object>();
            var storageInstalled = true;
            try
            {
                await using var command = new NpgsqlCommand("""
                    SELECT module_number, is_enabled, revision_number, reason, updated_at
                    FROM projectpulse_module_availability
                    ORDER BY module_number;
                    """, connection);
                await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
                while (await reader.ReadAsync(context.RequestAborted))
                {
                    states.Add(new
                    {
                        moduleNumber = reader.GetString(0),
                        isEnabled = reader.GetBoolean(1),
                        revision = reader.GetInt32(2),
                        reason = reader.IsDBNull(3) ? null : reader.GetString(3),
                        updatedAt = reader.GetFieldValue<DateTimeOffset>(4)
                    });
                }
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // Missing override storage must not hide modules. The documented
                // default remains Enabled and the administrator can continue to
                // use every active module while migration readiness is reported.
                storageInstalled = false;
            }

            return Results.Ok(new
            {
                registeredModuleCount = RegisteredModuleCount,
                states,
                access = new
                {
                    actualRoles = actualRoles.OrderBy(value => value).ToArray(),
                    effectiveRoles = effectiveRoles.OrderBy(value => value).ToArray(),
                    isSuperAdministrator = effectiveAdministrator,
                    permanentFullControl = actualAdministrator && !isViewAs,
                    canManage,
                    isViewAs,
                    authoritySource = actualAdministrator
                        ? "actual_session_super_administrator"
                        : "effective_role_and_permission_policy"
                },
                policy = new
                {
                    defaultState = "ENABLED",
                    missingOverrideBehavior = "ENABLED",
                    disabledVisibility = "SUPER_ADMINISTRATOR_ONLY",
                    storageInstalled,
                    migrationRequired = !storageInstalled,
                    migration = MigrationFile
                }
            });
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ModuleAvailabilityOverridesModule")
                .LogWarning(
                    "Module availability overrides could not be loaded ({ExceptionType}).",
                    exception.GetType().Name);
            return DependencyUnavailable();
        }
    }

    private static async Task<HashSet<string>> ReadRolesAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT upper(COALESCE(r.role_code, ''))
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
             AND r.is_active = TRUE
            JOIN app_users u
              ON u.user_id = ura.user_id
             AND u.is_active = TRUE
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(role)) roles.Add(role);
        }
        return roles;
    }

    private static Guid? SessionUserId(HttpContext context, params string[] keys) =>
        ProjectPulseActualSessionAuthority.ReadUserId(context, keys);

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

    private static IResult DependencyUnavailable() =>
        Results.Json(new
        {
            status = "module_availability_unavailable",
            migration = MigrationFile,
            message = "Module availability storage or authorization is unavailable. Existing module source has not been disabled."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

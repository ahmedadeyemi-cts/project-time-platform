using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Lightweight read contract for the existing Modules directory.
/// Only persisted overrides are returned; missing rows mean Enabled.
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
            var actualRoles = await ReadRolesAsync(connectionString, actualUserId.Value);
            var effectiveRoles = actualUserId == effectiveUserId
                ? actualRoles
                : await ReadRolesAsync(connectionString, effectiveUserId.Value);
            var isViewAs = IsViewAs(context, actualUserId.Value, effectiveUserId.Value);
            var canManage = actualRoles.Contains("SUPER_ADMINISTRATOR") && !isViewAs;

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT module_number, is_enabled, revision_number, reason, updated_at
                FROM projectpulse_module_availability
                ORDER BY module_number;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();

            var states = new List<object>();
            while (await reader.ReadAsync())
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

            return Results.Ok(new
            {
                registeredModuleCount = RegisteredModuleCount,
                states,
                access = new
                {
                    actualRoles = actualRoles.OrderBy(value => value).ToArray(),
                    effectiveRoles = effectiveRoles.OrderBy(value => value).ToArray(),
                    isSuperAdministrator = effectiveRoles.Contains("SUPER_ADMINISTRATOR"),
                    canManage,
                    isViewAs
                },
                policy = new
                {
                    defaultState = "ENABLED",
                    missingOverrideBehavior = "ENABLED",
                    disabledVisibility = "SUPER_ADMINISTRATOR_ONLY",
                    migration = MigrationFile
                }
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return Results.Json(new
            {
                status = "module_availability_migration_pending",
                migration = MigrationFile,
                message = "Module availability storage is not installed."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ModuleAvailabilityOverridesModule")
                .LogWarning(exception, "Module availability overrides could not be loaded.");
            return DependencyUnavailable();
        }
    }

    private static async Task<HashSet<string>> ReadRolesAsync(string connectionString, Guid userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT upper(COALESCE(r.role_code, ''))
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
             AND r.is_active = TRUE
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) roles.Add(reader.GetString(0));
        return roles;
    }

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context, Guid actualUserId, Guid effectiveUserId)
    {
        if (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
            && value is bool isViewAs
            && isViewAs) return true;
        return actualUserId != effectiveUserId;
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

    private static IResult DependencyUnavailable() =>
        Results.Json(new
        {
            status = "module_availability_unavailable",
            migration = MigrationFile,
            message = "Module availability storage is unavailable."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

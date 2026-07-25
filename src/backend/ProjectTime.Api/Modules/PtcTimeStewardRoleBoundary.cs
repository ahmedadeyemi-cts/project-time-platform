using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static readonly string[] TimeStewardRoleCodes =
    {
        "PROJECT_TEAM_COORDINATOR",
        "SUPER_ADMINISTRATOR"
    };

    public static WebApplication UsePtcTimeStewardRoleBoundary(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var protectedRoute = path.StartsWith("/api/timesheet/ptc", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/runtime/timesheet/steward", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/scoped-time/", StringComparison.OrdinalIgnoreCase);
            if (!protectedRoute)
            {
                await next();
                return;
            }

            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var actor = await LoadActorAsync(context, connection);
            if (actor is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "session_required",
                    message = "A valid ProjectPulse session is required."
                });
                return;
            }

            var allowed = actor.RoleCodes.Any(roleCode =>
                TimeStewardRoleCodes.Contains(CanonicalRole(roleCode), StringComparer.OrdinalIgnoreCase));
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "time_steward_role_required",
                    allowedRoles = TimeStewardRoleCodes,
                    effectiveRoles = actor.RoleCodes,
                    isViewAs = actor.IsViewAs,
                    message = "Only Project Team Coordinator or Super Administrator may manage another user's time."
                });
                return;
            }

            var method = context.Request.Method.ToUpperInvariant();
            var isWrite = method is not ("GET" or "HEAD" or "OPTIONS");
            if (actor.IsViewAs && isWrite)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "view_as_read_only",
                    message = "Time-steward changes are disabled while using Administrator View-As."
                });
                return;
            }

            context.Items["ProjectPulsePtcTimeStewardBoundary"] = true;
            await next();
        });

        return app;
    }
}

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
        // Module 001B is registered with the same fail-closed time-steward role
        // boundary as the existing protected steward reads.
        app.MapModule001BTimeReallocationEndpoints();

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
                    message = "No Access. Module 001B is restricted to Project Team Coordinator and Super Administrator."
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

            // Reallocation no longer belongs to Module 001. Keep the legacy endpoints
            // unavailable even for authorized users so no caller can fall back to the
            // old Draft/unsubmit workflow.
            var legacyModule001Move =
                method == "POST"
                && path.EndsWith("/move", StringComparison.OrdinalIgnoreCase)
                && (path.StartsWith("/api/timesheet/ptc/entries/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/runtime/timesheet/steward/v2/entries/", StringComparison.OrdinalIgnoreCase));
            if (legacyModule001Move)
            {
                context.Response.StatusCode = StatusCodes.Status410Gone;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "module_001b_reallocation_required",
                    module = "001B",
                    message = "Time reallocation has moved to Module 001B. The legacy Module 001 move workflow is retired and cannot unsubmit or return time to Draft.",
                    replacement = "/api/runtime/timesheet/steward/001b/reallocation/entries/{timeEntryId}/move"
                });
                return;
            }

            context.Items["ProjectPulsePtcTimeStewardBoundary"] = true;
            await next();
        });

        return app;
    }
}

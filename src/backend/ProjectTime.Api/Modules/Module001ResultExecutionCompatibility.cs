namespace ProjectTime.Api.Modules;

/// <summary>
/// Executes Module 001 GET handlers through their returned IResult instead of
/// allowing Task&lt;IResult&gt; method groups to bind to RequestDelegate and emit an
/// empty HTTP 200 response. The live failures for eligible PTC users, timer
/// targets, active timer recovery, and timer history all shared that signature.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    private const string Module001ExplicitResultMarker = "explicit-iresult-v1";

    public static WebApplication UseModule001ResultExecutionCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                await next();
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            IResult? result = null;

            if (path.Equals("/api/timesheet/ptc/users", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/runtime/timesheet/steward/users", StringComparison.OrdinalIgnoreCase))
            {
                // Keep both historic read paths consistent with the role-filtered
                // runtime source. Only active Engineering/PM delivery roles are
                // returned; PTC and Super Administrator remain the calling roles.
                result = await RuntimePtcUsersAsync(context);
            }
            else if (TryModule001PtcWorkspacePath(path, out var targetUserId))
            {
                result = await RuntimePtcWorkspaceAsync(targetUserId, context);
            }
            else if (path.Equals("/api/timesheet/timers/targets", StringComparison.OrdinalIgnoreCase))
            {
                result = await Module001TimerTargetsAsync(context);
            }
            else if (path.Equals("/api/timesheet/timers/active", StringComparison.OrdinalIgnoreCase))
            {
                result = await Module001ActiveTimerAsync(context);
            }
            else if (path.Equals("/api/timesheet/timers/history", StringComparison.OrdinalIgnoreCase))
            {
                result = await Module001TimerHistoryAsync(context);
            }
            else if (path.Equals("/api/timesheet/work-queue", StringComparison.OrdinalIgnoreCase))
            {
                result = await Module001WorkQueueAsync(context);
            }
            else if (path.Equals("/api/timesheet/weekly-lines", StringComparison.OrdinalIgnoreCase))
            {
                result = await Module001WeeklyLinesAsync(context);
            }

            if (result is null)
            {
                await next();
                return;
            }

            context.Response.Headers["X-ProjectPulse-Module001-Result-Execution"] =
                Module001ExplicitResultMarker;
            await result.ExecuteAsync(context);
        });

        return app;
    }

    private static bool TryModule001PtcWorkspacePath(string path, out Guid targetUserId)
    {
        targetUserId = Guid.Empty;
        var prefixes = new[]
        {
            "/api/timesheet/ptc/users/",
            "/api/runtime/timesheet/steward/users/"
        };

        foreach (var prefix in prefixes)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var remainder = path[prefix.Length..];
            var suffix = prefix.Contains("runtime", StringComparison.OrdinalIgnoreCase)
                ? "/workspace"
                : "/entries";
            if (!remainder.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var rawId = remainder[..^suffix.Length];
            return Guid.TryParse(rawId, out targetUserId);
        }

        return false;
    }
}

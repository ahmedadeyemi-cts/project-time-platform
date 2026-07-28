using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Narrow bridge for modules that predate the dynamic RBAC middleware but need
/// to present the same effective authority in their UI and write handlers.
/// Legacy authorization remains the fallback when no published scoped decision
/// exists or Migration 040 is not available.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    internal static async Task<ScopedAuthorizationDecision?> EvaluateCurrentActorAsync(
        HttpContext context,
        string moduleCode,
        string actionCode,
        bool isWrite)
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(context.RequestAborted);

            if (!await ScopedPolicyTablesExistAsync(connection)) return null;

            var actor = await LoadActorAsync(context, connection);
            if (actor is null) return null;

            return await ScopedAuthorizationEvaluator.EvaluateAsync(
                connection,
                actor,
                moduleCode,
                actionCode,
                null,
                null,
                null,
                isWrite);
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ScopedRolePolicyAuthorizationBridge")
                .LogWarning(
                    "Dynamic RBAC bridge unavailable for Module {ModuleCode} action {ActionCode} ({ExceptionType}).",
                    moduleCode,
                    actionCode,
                    exception.GetType().Name);
            return null;
        }
    }
}

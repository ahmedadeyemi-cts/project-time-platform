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

            // The actual authenticated Super Administrator invariant is checked
            // independently of effective-policy rows. It cannot transfer through
            // View-As and cannot be reduced by a module-specific denial.
            if (await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                    context,
                    connection,
                    cancellationToken: context.RequestAborted))
            {
                return new ScopedAuthorizationDecision(
                    true,
                    false,
                    false,
                    false,
                    (moduleCode ?? string.Empty).Trim().ToUpperInvariant(),
                    (actionCode ?? string.Empty).Trim().ToUpperInvariant(),
                    "ORGANIZATION",
                    null,
                    false,
                    true,
                    true,
                    "Super Administrator has permanent organization-wide Full Control in their own session.");
            }

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

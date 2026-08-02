using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Normalizes the actual authenticated administrator before endpoint-local
/// authorization runs and preserves the explicit Billing exclusion from
/// Module 008. The compatibility layer never transfers actual-session
/// administrator authority into View-As.
/// </summary>
public static class PermanentRoleAuthorityCompatibility
{
    private static readonly HashSet<string> BillingRoleCodes = new(
        new[] { "BILLING", "ACCOUNTING_BILLING", "FINANCE" },
        StringComparer.OrdinalIgnoreCase);

    public static WebApplication UsePermanentRoleAuthorityCompatibility(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            try
            {
                // This request-local reconciliation is intentionally performed
                // before Module 026, Module 065, Microsoft Integration, native
                // administration, and scoped-RBAC endpoint authorization.
                await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                    context,
                    cancellationToken: context.RequestAborted);
            }
            catch (Exception exception)
            {
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("PermanentRoleAuthorityCompatibility")
                    .LogWarning(
                        "Permanent administrator authority could not be evaluated ({ExceptionType}); endpoint authorization remains fail-closed.",
                        exception.GetType().Name);
            }

            if (IsAuditHistoryPath(path)
                && await IsPureBillingEffectiveActorAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    module = "008",
                    status = "billing_audit_history_not_authorized",
                    message = "Billing does not have access to Audit History. Use the authorized billing, reconciliation, and financial-operations workspaces instead.",
                    viewAsReadOnly = ProjectPulseActualSessionAuthority.IsViewAs(context)
                });
                return;
            }

            await next();
        });

        return app;
    }

    private static bool IsAuditHistoryPath(PathString path) =>
        path.StartsWithSegments("/api/admin/audit-history")
        || path.StartsWithSegments("/api/audit-history")
        || path.StartsWithSegments("/api/modules/008");

    private static async Task<bool> IsPureBillingEffectiveActorAsync(HttpContext context)
    {
        // An actual administrator in their own session always retains Module 008.
        if (!ProjectPulseActualSessionAuthority.IsViewAs(context)
            && context.Items.TryGetValue("ProjectPulsePermanentFullControl", out var permanent)
            && permanent is true)
        {
            return false;
        }

        var effectiveUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId");
        if (!effectiveUserId.HasValue) return false;

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT upper(COALESCE(role.role_code, ''))
                FROM app_user_role_assignments assignment
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                JOIN app_users app_user
                  ON app_user.user_id = assignment.user_id
                 AND app_user.is_active = TRUE
                WHERE assignment.user_id = @user_id
                  AND assignment.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", effectiveUserId.Value);

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                var roleCode = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(roleCode)) roles.Add(roleCode);
            }

            return roles.Count > 0
                && roles.Any(BillingRoleCodes.Contains)
                && roles.All(BillingRoleCodes.Contains);
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("PermanentRoleAuthorityCompatibility")
                .LogWarning(
                    "Billing Audit History boundary could not be evaluated ({ExceptionType}); existing endpoint authorization remains authoritative.",
                    exception.GetType().Name);
            return false;
        }
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
}

using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Read-only administrator audit for role, permission, module, and View-As
/// invariants. It reports drift but never changes authorization or business data.
/// </summary>
public static class RoleAccessAuditModule
{
    private static readonly string[] AdministratorRoleCodes =
        ["SUPER_ADMINISTRATOR", "ADMINISTRATOR"];

    private static readonly string[] ProjectManagementRoleCodes =
    [
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    ];

    private static readonly string[] BillingRoleCodes =
        ["BILLING", "ACCOUNTING_BILLING", "FINANCE"];

    private static readonly string[] RequiredProjectManagementPermissions =
    [
        "VIEW_TIME_ENTRY",
        "EDIT_OWN_TIME",
        "SUBMIT_OWN_TIME",
        "VIEW_APPROVAL_INBOX",
        "APPROVE_TIME",
        "REJECT_TIME",
        "PROJECT_TIME_APPROVAL",
        "VIEW_HOLIDAYS",
        "VIEW_EXPENSES",
        "MANAGE_EXPENSES",
        "VIEW_QUALIFICATIONS_069",
        "MANAGE_OWN_QUALIFICATIONS_069",
        "VIEW_PROJECT_WORKSPACE",
        "VIEW_REPORTS"
    ];

    public static WebApplication MapRoleAccessAuditEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/admin/role-access-audit",
            (Func<HttpContext, Task<IResult>>)GetAuditAsync);
        return app;
    }

    private static async Task<IResult> GetAuditAsync(HttpContext context)
    {
        if (ProjectPulseActualSessionAuthority.IsViewAs(context))
        {
            return Results.Json(new
            {
                status = "actual_session_required",
                message = "Exit Administrator View-As before running the role access audit."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Role access audit storage is unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            if (!await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                    context,
                    connection,
                    cancellationToken: context.RequestAborted))
            {
                return Results.Json(new
                {
                    status = "super_administrator_required",
                    message = "Only an actual Super Administrator session may run the role access audit."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var activeRoles = await LoadActiveRolesAsync(connection, context.RequestAborted);
            var permissionCount = await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM app_permissions;",
                context.RequestAborted);
            var activeUserCount = await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM app_users WHERE is_active = TRUE;",
                context.RequestAborted);
            var activeModuleCount = await ActiveModuleCountAsync(connection, context.RequestAborted);
            var administratorMissingPermissions = await AdministratorMissingPermissionsAsync(
                connection,
                context.RequestAborted);
            var legacyAssignmentsWithoutCanonical = await LegacyAssignmentsWithoutCanonicalAsync(
                connection,
                context.RequestAborted);
            var projectManagementMissingPermissions = await ProjectManagementMissingPermissionsAsync(
                connection,
                context.RequestAborted);
            var billingAuditPermissions = await BillingAuditPermissionsAsync(
                connection,
                context.RequestAborted);
            var billingDynamicModule008Grants = await BillingDynamicModule008GrantsAsync(
                connection,
                context.RequestAborted);
            var administratorDynamicDenials = await AdministratorDynamicDenialsAsync(
                connection,
                context.RequestAborted);

            var findings = new List<object>();
            AddFinding(
                findings,
                "SUPER_ADMIN_PERMISSION_COVERAGE",
                administratorMissingPermissions.Count == 0,
                administratorMissingPermissions.Count == 0
                    ? "Every registered permission is assigned to both administrator role codes."
                    : $"{administratorMissingPermissions.Count} administrator permission relationship(s) are missing.",
                administratorMissingPermissions);
            AddFinding(
                findings,
                "LEGACY_ADMIN_ASSIGNMENT_RECONCILIATION",
                legacyAssignmentsWithoutCanonical.Count == 0,
                legacyAssignmentsWithoutCanonical.Count == 0
                    ? "Every active legacy Administrator assignment has a canonical Super Administrator assignment."
                    : $"{legacyAssignmentsWithoutCanonical.Count} active legacy assignment(s) still need reconciliation.",
                legacyAssignmentsWithoutCanonical);
            AddFinding(
                findings,
                "PROJECT_MANAGEMENT_REQUIRED_PERMISSIONS",
                projectManagementMissingPermissions.Count == 0,
                projectManagementMissingPermissions.Count == 0
                    ? "Project Management time, project approval, holiday, expense, and self-qualification permissions are complete."
                    : $"{projectManagementMissingPermissions.Count} Project Management permission relationship(s) are missing.",
                projectManagementMissingPermissions);
            AddFinding(
                findings,
                "BILLING_AUDIT_HISTORY_EXCLUSION",
                billingAuditPermissions.Count == 0 && billingDynamicModule008Grants.Count == 0,
                billingAuditPermissions.Count == 0 && billingDynamicModule008Grants.Count == 0
                    ? "Billing role aliases have no Module 008 Audit History permission or dynamic grant."
                    : "Billing still has one or more stale Module 008 permissions or dynamic grants.",
                new
                {
                    legacyPermissions = billingAuditPermissions,
                    dynamicGrants = billingDynamicModule008Grants
                });
            AddFinding(
                findings,
                "SUPER_ADMIN_DYNAMIC_DENY_INVARIANT",
                administratorDynamicDenials.Count == 0,
                administratorDynamicDenials.Count == 0
                    ? "No published policy attempts to deny Super Administrator module access."
                    : "Published policy contains administrator denials; runtime permanent Full Control still overrides them, but the policy should be cleaned.",
                administratorDynamicDenials,
                administratorDynamicDenials.Count == 0 ? "pass" : "warning");

            var failed = findings.Count(item =>
                string.Equals(
                    Convert.ToString(item.GetType().GetProperty("severity")?.GetValue(item)),
                    "error",
                    StringComparison.OrdinalIgnoreCase));
            var warnings = findings.Count(item =>
                string.Equals(
                    Convert.ToString(item.GetType().GetProperty("severity")?.GetValue(item)),
                    "warning",
                    StringComparison.OrdinalIgnoreCase));

            return Results.Ok(new
            {
                status = failed == 0 ? "role_access_audit_complete" : "role_access_drift_detected",
                generatedAt = DateTimeOffset.UtcNow,
                contractVersion = "projectpulse-role-access-audit-v1-2026-08-01",
                actualSession = new
                {
                    permanentFullControl = true,
                    authoritySource = "actual_session_super_administrator",
                    viewAsTransfersAuthority = false
                },
                inventory = new
                {
                    activeUserCount,
                    activeRoleCount = activeRoles.Count,
                    permissionCount,
                    activeModuleCount
                },
                roles = activeRoles,
                summary = new
                {
                    findingCount = findings.Count,
                    failed,
                    warnings,
                    passed = findings.Count - failed - warnings
                },
                findings,
                secretValuesReturned = false,
                writeOperationsPerformed = false
            });
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("RoleAccessAuditModule")
                .LogWarning(
                    "Role access audit failed ({ExceptionType}); raw detail suppressed.",
                    exception.GetType().Name);
            return Results.Json(new
            {
                status = "role_access_audit_unavailable",
                message = "Role and module access could not be audited."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static void AddFinding(
        ICollection<object> findings,
        string code,
        bool passed,
        string message,
        object evidence,
        string? severity = null)
    {
        findings.Add(new
        {
            code,
            passed,
            severity = severity ?? (passed ? "pass" : "error"),
            message,
            evidence
        });
    }

    private static async Task<List<object>> LoadActiveRolesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                upper(role.role_code),
                role.role_name,
                COUNT(DISTINCT assignment.user_id) FILTER (
                    WHERE assignment.is_active = TRUE
                ) AS active_user_count,
                COUNT(DISTINCT relationship.app_permission_id) AS permission_count
            FROM app_roles role
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.app_role_id = role.app_role_id
            LEFT JOIN app_role_permissions relationship
              ON relationship.app_role_id = role.app_role_id
            WHERE role.is_active = TRUE
            GROUP BY role.app_role_id, role.role_code, role.role_name
            ORDER BY role.display_order, role.role_code;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.GetString(1),
                activeUserCount = reader.GetInt64(2),
                permissionCount = reader.GetInt64(3)
            });
        }
        return rows;
    }

    private static async Task<int> ScalarAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<int> ActiveModuleCountAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(connection, "scoped_role_policy_modules", cancellationToken))
        {
            return await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM scoped_role_policy_modules WHERE is_active = TRUE;",
                cancellationToken);
        }

        return await ScalarAsync(
            connection,
            "SELECT COUNT(DISTINCT module_code) FROM app_feature_catalog WHERE is_active = TRUE;",
            cancellationToken);
    }

    private static async Task<List<object>> AdministratorMissingPermissionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT upper(role.role_code), permission.permission_code
            FROM app_roles role
            CROSS JOIN app_permissions permission
            LEFT JOIN app_role_permissions relationship
              ON relationship.app_role_id = role.app_role_id
             AND relationship.app_permission_id = permission.app_permission_id
            WHERE role.is_active = TRUE
              AND upper(role.role_code) = ANY(@role_codes)
              AND relationship.app_role_permission_id IS NULL
            ORDER BY role.role_code, permission.permission_code;
            """, connection);
        command.Parameters.AddWithValue("role_codes", AdministratorRoleCodes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new { roleCode = reader.GetString(0), permissionCode = reader.GetString(1) });
        }
        return rows;
    }

    private static async Task<List<object>> LegacyAssignmentsWithoutCanonicalAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT legacy.user_id, app_user.email
            FROM app_user_role_assignments legacy
            JOIN app_roles legacy_role
              ON legacy_role.app_role_id = legacy.app_role_id
             AND legacy_role.is_active = TRUE
            JOIN app_users app_user
              ON app_user.user_id = legacy.user_id
             AND app_user.is_active = TRUE
            WHERE legacy.is_active = TRUE
              AND upper(legacy_role.role_code) = 'ADMINISTRATOR'
              AND NOT EXISTS (
                  SELECT 1
                  FROM app_user_role_assignments canonical
                  JOIN app_roles canonical_role
                    ON canonical_role.app_role_id = canonical.app_role_id
                   AND canonical_role.is_active = TRUE
                  WHERE canonical.user_id = legacy.user_id
                    AND canonical.is_active = TRUE
                    AND upper(canonical_role.role_code) = 'SUPER_ADMINISTRATOR'
              )
            ORDER BY app_user.email;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new { userId = reader.GetGuid(0), email = reader.GetString(1) });
        }
        return rows;
    }

    private static async Task<List<object>> ProjectManagementMissingPermissionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT upper(role.role_code), required.permission_code
            FROM app_roles role
            CROSS JOIN unnest(@required_permissions::text[]) AS required(permission_code)
            LEFT JOIN app_permissions permission
              ON upper(permission.permission_code) = upper(required.permission_code)
            LEFT JOIN app_role_permissions relationship
              ON relationship.app_role_id = role.app_role_id
             AND relationship.app_permission_id = permission.app_permission_id
            WHERE role.is_active = TRUE
              AND upper(role.role_code) = ANY(@role_codes)
              AND (
                    permission.app_permission_id IS NULL
                 OR relationship.app_role_permission_id IS NULL
              )
            ORDER BY role.role_code, required.permission_code;
            """, connection);
        command.Parameters.AddWithValue("required_permissions", RequiredProjectManagementPermissions);
        command.Parameters.AddWithValue("role_codes", ProjectManagementRoleCodes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new { roleCode = reader.GetString(0), permissionCode = reader.GetString(1) });
        }
        return rows;
    }

    private static async Task<List<object>> BillingAuditPermissionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT upper(role.role_code), permission.permission_code, permission.module_code
            FROM app_roles role
            JOIN app_role_permissions relationship
              ON relationship.app_role_id = role.app_role_id
            JOIN app_permissions permission
              ON permission.app_permission_id = relationship.app_permission_id
            WHERE role.is_active = TRUE
              AND upper(role.role_code) = ANY(@role_codes)
              AND (
                    upper(COALESCE(permission.module_code, '')) = '008'
                 OR upper(permission.permission_code) IN (
                        'VIEW_AUDIT_TRAIL',
                        'VIEW_AUDIT_HISTORY',
                        'VIEW_AUDIT_HISTORY_008'
                    )
              )
            ORDER BY role.role_code, permission.permission_code;
            """, connection);
        command.Parameters.AddWithValue("role_codes", BillingRoleCodes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                roleCode = reader.GetString(0),
                permissionCode = reader.GetString(1),
                moduleCode = reader.GetString(2)
            });
        }
        return rows;
    }

    private static async Task<List<object>> BillingDynamicModule008GrantsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        if (!await TableExistsAsync(connection, "scoped_role_policy_effective_grants", cancellationToken))
            return rows;

        await using var command = new NpgsqlCommand("""
            SELECT role_code, action_code, scope_code, grant_effect, version_number
            FROM scoped_role_policy_effective_grants
            WHERE upper(role_code) = ANY(@role_codes)
              AND upper(module_code) = '008'
              AND upper(grant_effect) = 'GRANT'
            ORDER BY role_code, action_code;
            """, connection);
        command.Parameters.AddWithValue("role_codes", BillingRoleCodes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                roleCode = reader.GetString(0),
                actionCode = reader.GetString(1),
                scopeCode = reader.GetString(2),
                grantEffect = reader.GetString(3),
                versionNumber = reader.GetInt32(4)
            });
        }
        return rows;
    }

    private static async Task<List<object>> AdministratorDynamicDenialsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        if (!await TableExistsAsync(connection, "scoped_role_policy_effective_grants", cancellationToken))
            return rows;

        await using var command = new NpgsqlCommand("""
            SELECT role_code, module_code, action_code, scope_code, version_number
            FROM scoped_role_policy_effective_grants
            WHERE upper(role_code) = ANY(@role_codes)
              AND upper(grant_effect) = 'DENY'
            ORDER BY role_code, module_code, action_code;
            """, connection);
        command.Parameters.AddWithValue("role_codes", AdministratorRoleCodes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                roleCode = reader.GetString(0),
                moduleCode = reader.GetString(1),
                actionCode = reader.GetString(2),
                scopeCode = reader.GetString(3),
                versionNumber = reader.GetInt32(4)
            });
        }
        return rows;
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.' || @table_name) IS NOT NULL;",
            connection);
        command.Parameters.AddWithValue("table_name", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
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

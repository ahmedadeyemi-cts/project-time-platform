using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static readonly string[] CombinedRuntimeOperatorRoles =
    {
        "PROJECT_TEAM_COORDINATOR",
        "SUPER_ADMINISTRATOR"
    };

    public static WebApplication MapCombinedModuleRuntimeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/runtime/v2/readiness", CombinedRuntimeReadinessAsync);
        app.MapGet("/api/runtime/v2/role-policy/summary", CombinedRolePolicySummaryAsync);
        app.MapGet("/api/runtime/v2/role-policy/catalog", CatalogAsync);
        app.MapGet("/api/runtime/v2/role-policy/versions", VersionsAsync);
        app.MapGet("/api/runtime/v2/role-policy/matrix", CombinedRolePolicyMatrixAsync);
        app.MapGet("/api/runtime/v2/role-policy/roles/{roleCode}", RoleDetailAsync);
        app.MapGet("/api/runtime/v2/timesheet/steward/users", CombinedTimeStewardUsersAsync);
        app.MapGet("/api/runtime/v2/timesheet/steward/users/{targetUserId:guid}/workspace", RuntimePtcWorkspaceAsync);
        return app;
    }

    private static async Task<IResult> CombinedRuntimeReadinessAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                WITH canonical_roles(role_code) AS (
                    VALUES
                      ('ENGINEERING'),('PROJECT_MANAGEMENT'),('ENGINEERING_LEAD'),('PROJECT_MANAGEMENT_LEAD'),
                      ('MANAGER'),('SALES'),('INSIDE_SALES'),('SOLUTION_ARCHITECT'),('EXECUTIVE'),
                      ('PROJECT_TEAM_COORDINATOR'),('ACCOUNTING'),('SUPER_ADMINISTRATOR')
                ), eligible_aliases(role_code) AS (
                    VALUES
                      ('ENGINEERING'),('ENGINEER'),
                      ('ENGINEERING_LEAD'),('ENGINEERING_TEAM_LEAD'),
                      ('PROJECT_MANAGEMENT'),('PROJECT_MANAGER'),
                      ('PROJECT_MANAGEMENT_LEAD'),('PROJECT_MANAGEMENT_TEAM_LEAD'),('PM_TEAM_LEAD')
                )
                SELECT
                    (SELECT COUNT(*) FROM app_roles r
                     JOIN canonical_roles c ON c.role_code=UPPER(r.role_code)
                     WHERE r.is_active=TRUE),
                    (SELECT COUNT(*) FROM scoped_role_policy_modules WHERE is_active=TRUE),
                    (SELECT COUNT(*) FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED'),
                    (SELECT COUNT(*) FROM scoped_role_policy_effective_grants),
                    (SELECT COUNT(DISTINCT u.user_id)
                     FROM app_users u
                     JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
                     JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                     JOIN eligible_aliases e ON e.role_code=UPPER(r.role_code)
                     WHERE u.is_active=TRUE),
                    (SELECT COUNT(DISTINCT u.user_id)
                     FROM app_users u
                     JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
                     JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                     WHERE u.is_active=TRUE
                       AND UPPER(r.role_code) IN ('PROJECT_TEAM_COORDINATOR','SUPER_ADMINISTRATOR','ADMINISTRATOR')),
                    (SELECT COUNT(*) FROM schema_migrations
                     WHERE migration_id IN (
                       '040_scoped_role_policy_versions',
                       '043_ptc_time_steward_permissions')),
                    (SELECT COUNT(*) FROM schema_migrations
                     WHERE migration_id IN (
                       '044_project_expense_upload_certify_connection',
                       '044a_project_expense_self_certify_permission')),
                    (SELECT COUNT(*) FROM pg_tables
                     WHERE schemaname='public'
                       AND tablename IN (
                         'project_expense_uploads',
                         'project_expense_lines',
                         'project_expense_events',
                         'project_expense_mail_outbox',
                         'certify_connection_profiles',
                         'certify_expense_import_runs')),
                    (SELECT COUNT(*) FROM non_project_time_categories WHERE is_active=TRUE);
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            var roleCount = Convert.ToInt32(reader.GetInt64(0));
            var moduleCount = Convert.ToInt32(reader.GetInt64(1));
            var publishedPolicyCount = Convert.ToInt32(reader.GetInt64(2));
            var effectiveGrantCount = Convert.ToInt32(reader.GetInt64(3));
            var eligibleUserCount = Convert.ToInt32(reader.GetInt64(4));
            var operatorCount = Convert.ToInt32(reader.GetInt64(5));
            var foundationalMigrationCount = Convert.ToInt32(reader.GetInt64(6));
            var expenseMigrationCount = Convert.ToInt32(reader.GetInt64(7));
            var expenseTableCount = Convert.ToInt32(reader.GetInt64(8));
            var nonProjectCategoryCount = Convert.ToInt32(reader.GetInt64(9));
            var ready = roleCount == 12
                && moduleCount == 70
                && publishedPolicyCount == 1
                && effectiveGrantCount > 0
                && eligibleUserCount > 0
                && operatorCount > 0
                && foundationalMigrationCount == 2
                && expenseMigrationCount == 2
                && expenseTableCount == 6
                && nonProjectCategoryCount > 0;

            return Results.Json(new
            {
                status = ready ? "combined_module_runtime_ready" : "combined_module_runtime_incomplete",
                contractVersion = "combined-modules-001-005-012-037-038-v2",
                roleCount,
                moduleCount,
                publishedPolicyCount,
                effectiveGrantCount,
                eligibleUserCount,
                operatorCount,
                foundationalMigrationCount,
                expenseMigrationCount,
                expenseTableCount,
                nonProjectCategoryCount,
                modules = new[] { "001", "005", "012", "037", "038" }
            }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception)
        {
            return CombinedRuntimeUnavailable(exception);
        }
    }

    private static async Task<IResult> CombinedRolePolicySummaryAsync(HttpContext context)
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var readiness = await RequirePolicyTablesAsync(connection);
            if (readiness is not null) return readiness;
            var actor = await LoadActorAsync(context, connection);
            if (actor is null) return SessionRequired();

            var roles = await LoadRolesAsync(connection);
            var modules = await LoadModulesAsync(connection);
            var version = await LoadPublishedVersionAsync(connection);
            var grantCount = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM scoped_role_policy_effective_grants;");
            if (roles.Count != 12 || modules.Count != 70 || version is null || grantCount == 0)
            {
                return Results.Json(new
                {
                    status = "role_policy_contract_incomplete",
                    message = "The authoritative role-policy data did not satisfy the required 12-role and 70-module contract.",
                    roleCount = roles.Count,
                    moduleCount = modules.Count,
                    publishedPolicyFound = version is not null,
                    grantCount
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                module = "012",
                status = "authoritative_role_policy_summary_loaded",
                apiContractVersion = "combined-runtime-v2-2026-07-26",
                canViewRoleDirectory = true,
                canWritePolicy = actor.IsSuperAdministrator && !actor.IsViewAs,
                ownSessionRequired = true,
                isViewAs = actor.IsViewAs,
                actor = new
                {
                    actor.ActualUserId,
                    actor.EffectiveUserId,
                    actor.Email,
                    actor.RoleCodes
                },
                policyVersion = version,
                roles,
                modules,
                summary = new
                {
                    roleCount = roles.Count,
                    moduleCount = modules.Count,
                    grantCount,
                    activeSuperAdministrators = await CountActiveSuperAdministratorsAsync(connection),
                    authoritativeDatabase = true,
                    emptyCollectionsAllowed = false
                }
            });
        }
        catch (Exception exception)
        {
            return CombinedRuntimeUnavailable(exception);
        }
    }

    private static async Task<IResult> CombinedRolePolicyMatrixAsync(HttpContext context)
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var readiness = await RequirePolicyTablesAsync(connection);
            if (readiness is not null) return readiness;
            if (await LoadActorAsync(context, connection) is null) return SessionRequired();

            var roles = await LoadRolesAsync(connection);
            var modules = await LoadModulesAsync(connection);
            var grants = await LoadGrantsAsync(connection, null, null);
            var version = await LoadPublishedVersionAsync(connection);
            if (roles.Count != 12 || modules.Count != 70 || version is null || grants.Count == 0)
            {
                return Results.Json(new
                {
                    status = "permission_matrix_contract_incomplete",
                    message = "The authoritative permission matrix did not satisfy the required 12-role and 70-module contract.",
                    roleCount = roles.Count,
                    moduleCount = modules.Count,
                    grantCount = grants.Count,
                    publishedPolicyFound = version is not null
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var configuredPairs = grants
                .Select(item => $"{item.RoleCode}|{item.ModuleCode}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var legacyFallback = new List<object>();
            foreach (var role in roles)
            foreach (var module in modules)
            {
                if (configuredPairs.Contains($"{role.RoleCode}|{module.ModuleCode}")) continue;
                legacyFallback.Add(new
                {
                    roleCode = role.RoleCode,
                    moduleCode = module.ModuleCode,
                    moduleName = module.ModuleName,
                    actionCode = "LEGACY_FALLBACK",
                    scopeCode = "CUSTOM_RULE",
                    granted = false,
                    explicitDeny = false,
                    inherited = true,
                    delegatedAuthority = false,
                    reasonRequired = false,
                    auditRequired = true,
                    explanation = "No scoped workbook decision exists. Existing Pulse authorization remains in effect."
                });
            }

            return Results.Ok(new
            {
                module = "037",
                status = "authoritative_permission_matrix_loaded",
                apiContractVersion = "combined-runtime-v2-2026-07-26",
                readOnly = true,
                writeEndpoints = Array.Empty<string>(),
                policyVersion = version,
                roles,
                modules,
                grants,
                legacyFallback
            });
        }
        catch (Exception exception)
        {
            return CombinedRuntimeUnavailable(exception);
        }
    }

    private static async Task<IResult> CombinedTimeStewardUsersAsync(HttpContext context)
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var actor = await LoadActorAsync(context, connection);
            if (actor is null) return SessionRequired();
            if (actor.IsViewAs || !actor.RoleCodes.Any(role => CombinedRuntimeOperatorRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
            {
                return Results.Json(new
                {
                    status = "time_steward_role_required",
                    message = "Use your own Project Team Coordinator or Super Administrator session to manage another user's time."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var readiness = await RequirePtcTimeStewardTablesAsync(connection);
            if (readiness is not null) return readiness;
            var weekStart = PtcRequestedWeek(context);
            var weekEnd = weekStart.AddDays(6);
            var search = context.Request.Query["search"].FirstOrDefault()?.Trim() ?? string.Empty;
            var users = new List<object>();
            await using var command = new NpgsqlCommand("""
                WITH eligible_users AS (
                    SELECT
                        u.user_id,
                        u.email,
                        COALESCE(NULLIF(u.display_name,''),u.email) AS display_name,
                        ARRAY_AGG(DISTINCT UPPER(r.role_code)) AS raw_role_codes
                    FROM app_users u
                    JOIN app_user_role_assignments ura
                      ON ura.user_id=u.user_id AND ura.is_active=TRUE
                    JOIN app_roles r
                      ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                    WHERE u.is_active=TRUE
                      AND UPPER(r.role_code)=ANY(@role_codes)
                      AND (
                        @search=''
                        OR u.email ILIKE '%' || @search || '%'
                        OR COALESCE(u.display_name,'') ILIKE '%' || @search || '%'
                      )
                    GROUP BY u.user_id,u.email,u.display_name
                )
                SELECT
                    eu.user_id,
                    eu.email,
                    eu.display_name,
                    eu.raw_role_codes,
                    t.timesheet_id,
                    COALESCE(t.status,'not_started'),
                    COALESCE(SUM(te.hours),0),
                    COUNT(te.time_entry_id),
                    MAX(COALESCE(te.updated_at,t.updated_at))
                FROM eligible_users eu
                LEFT JOIN timesheets t
                  ON t.user_id=eu.user_id AND t.week_start_date=@week_start
                LEFT JOIN time_entries te
                  ON te.timesheet_id=t.timesheet_id
                 AND te.work_date BETWEEN @week_start AND @week_end
                GROUP BY eu.user_id,eu.email,eu.display_name,eu.raw_role_codes,
                         t.timesheet_id,t.status
                ORDER BY eu.display_name,eu.email
                LIMIT 500;
                """, connection);
            command.Parameters.AddWithValue("role_codes", PtcManagedRoleAliases);
            command.Parameters.AddWithValue("search", search);
            command.Parameters.AddWithValue("week_start", weekStart);
            command.Parameters.AddWithValue("week_end", weekEnd);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var roleCodes = CanonicalPtcManagedRoles(reader.GetFieldValue<string[]>(3));
                users.Add(new
                {
                    userId = reader.GetGuid(0),
                    email = reader.GetString(1),
                    displayName = reader.GetString(2),
                    roleCodes,
                    roleNames = roleCodes.Select(RoleDisplayName).ToArray(),
                    timesheetId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                    status = reader.GetString(5),
                    totalHours = reader.GetDecimal(6),
                    entryCount = Convert.ToInt32(reader.GetInt64(7)),
                    lastUpdatedAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8)
                });
            }

            if (users.Count == 0)
            {
                return Results.Json(new
                {
                    status = "eligible_time_steward_users_missing",
                    message = "The authoritative database returned no eligible Engineering or Project Management users.",
                    eligibleRoleCodes = PtcManagedRoleAliases
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                apiContractVersion = "combined-runtime-v2-2026-07-26",
                weekStart,
                weekEnd,
                eligibleRoleCodes = new[]
                {
                    "ENGINEERING", "ENGINEERING_LEAD",
                    "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD"
                },
                count = users.Count,
                canSubmitOnBehalf = false,
                operatorRoles = actor.RoleCodes,
                users
            });
        }
        catch (Exception exception)
        {
            return CombinedRuntimeUnavailable(exception);
        }
    }

    private static IResult CombinedRuntimeUnavailable(Exception exception) => Results.Json(new
    {
        status = "combined_module_runtime_unavailable",
        errorType = exception.GetType().Name,
        message = "The combined Module 001/005/012/037/038 runtime could not load authoritative database data."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

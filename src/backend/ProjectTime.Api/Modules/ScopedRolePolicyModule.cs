using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static readonly string[] CanonicalRoleOrder =
    {
        "ENGINEERING",
        "PROJECT_MANAGEMENT",
        "ENGINEERING_LEAD",
        "PROJECT_MANAGEMENT_LEAD",
        "MANAGER",
        "SALES",
        "INSIDE_SALES",
        "SOLUTION_ARCHITECT",
        "EXECUTIVE",
        "PROJECT_TEAM_COORDINATOR",
        "ACCOUNTING",
        "SUPER_ADMINISTRATOR"
    };

    private static readonly Dictionary<string, string> RoleAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ENGINEER"] = "ENGINEERING",
            ["ENGINEERING"] = "ENGINEERING",
            ["ENGINEERING_TEAM_LEAD"] = "ENGINEERING_LEAD",
            ["ENGINEERING_LEAD"] = "ENGINEERING_LEAD",
            ["PROJECT_MANAGER"] = "PROJECT_MANAGEMENT",
            ["PROJECT_MANAGEMENT"] = "PROJECT_MANAGEMENT",
            ["PROJECT_MANAGEMENT_TEAM_LEAD"] = "PROJECT_MANAGEMENT_LEAD",
            ["PM_TEAM_LEAD"] = "PROJECT_MANAGEMENT_LEAD",
            ["PROJECT_MANAGEMENT_LEAD"] = "PROJECT_MANAGEMENT_LEAD",
            ["ADMINISTRATOR"] = "SUPER_ADMINISTRATOR",
            ["SUPER_ADMINISTRATOR"] = "SUPER_ADMINISTRATOR"
        };

    public static WebApplication MapScopedRolePolicyEndpoints(this WebApplication app)
    {
        app.UseScopedRolePolicyAuthorization();

        app.MapGet("/api/role-policy/summary", SummaryAsync);
        app.MapGet("/api/role-policy/catalog", CatalogAsync);
        app.MapGet("/api/role-policy/roles/{roleCode}", RoleDetailAsync);
        app.MapGet("/api/role-policy/matrix", MatrixAsync);
        app.MapGet("/api/role-policy/versions", VersionsAsync);
        app.MapGet("/api/role-policy/explain", ExplainAsync);
        app.MapPost("/api/role-policy/validate", ValidateDraftAsync);
        app.MapPost("/api/role-policy/publish", PublishAsync);
        app.MapPost("/api/role-policy/versions/{policyVersionId:guid}/restore", RestoreAsync);
        app.MapGet("/api/scoped-authorization/evaluate", EvaluateEndpointAsync);
        app.MapGet("/api/scoped-approval/stages", ApprovalStagesAsync);
        app.MapPost("/api/scoped-approval/delegated", DelegatedApprovalAsync);
        app.MapPost("/api/scoped-approval/ptc-final", PtcFinalApprovalAsync);
        app.MapPost("/api/scoped-time/reopen", ReopenTimeAsync);
        app.MapPost("/api/scoped-time/correction", CorrectTimeAsync);
        app.MapPost("/api/scoped-time/reassign", ReassignTimeAsync);

        return app;
    }

    public static WebApplication UseScopedRolePolicyAuthorization(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var route = ScopedRolePolicyRules.RouteContract(
                context.Request.Path.Value ?? string.Empty,
                context.Request.Method);

            if (route is null)
            {
                await next();
                return;
            }

            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();

            if (!await ScopedPolicyTablesExistAsync(connection))
            {
                await next();
                return;
            }

            var actor = await LoadActorAsync(context, connection);
            if (actor is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "session_required",
                    message = "A valid ProjectPulse session is required for scoped authorization."
                });
                return;
            }

            var actionCode = route.ActionCode;
            if (route.ModuleCode == "002" && route.IsWrite)
            {
                actionCode = ScopedRolePolicyRules.ResolveApprovalAction(
                    actor.RoleCodes,
                    context.Request.Path.Value ?? string.Empty);
            }

            var decision = await ScopedAuthorizationEvaluator.EvaluateAsync(
                connection,
                actor,
                route.ModuleCode,
                actionCode,
                null,
                null,
                null,
                route.IsWrite);

            if (!decision.Allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "scoped_access_denied",
                    moduleCode = route.ModuleCode,
                    actionCode,
                    decision.ScopeCode,
                    decision.ExplicitDeny,
                    decision.IsViewAs,
                    message = decision.Explanation
                });
                return;
            }

            context.Items["ProjectPulseScopedPolicyDecision"] = decision;
            await next();
        });

        return app;
    }

    private static async Task<IResult> SummaryAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;

        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return SessionRequired();

        var version = await LoadPublishedVersionAsync(connection);
        var roles = await LoadRolesAsync(connection);
        var modules = await LoadModulesAsync(connection);
        var activeSuperAdministrators = await CountActiveSuperAdministratorsAsync(connection);
        var grantCount = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM scoped_role_policy_effective_grants;");
        var explicitDenyCount = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE grant_effect = 'DENY';");
        var delegatedCount = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE delegated_authority = TRUE;");

        return Results.Ok(new
        {
            module = "012",
            status = "scoped_role_policy_summary_loaded",
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
                explicitDenyCount,
                delegatedCount,
                activeSuperAdministrators,
                notSetBehavior = "legacy_fallback",
                nonBypassableSafetyControlsRemainSeparate = true
            }
        });
    }

    private static async Task<IResult> CatalogAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        if (await LoadActorAsync(context, connection) is null) return SessionRequired();

        var actions = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT action_code, action_description, is_non_bypassable
            FROM scoped_role_policy_actions
            WHERE is_active = TRUE
            ORDER BY action_code;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actions.Add(new
                {
                    actionCode = reader.GetString(0),
                    actionDescription = reader.GetString(1),
                    isNonBypassable = reader.GetBoolean(2)
                });
            }
        }

        var scopes = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT scope_code, scope_description
            FROM scoped_role_policy_scopes
            WHERE is_active = TRUE
            ORDER BY scope_code;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                scopes.Add(new
                {
                    scopeCode = reader.GetString(0),
                    scopeDescription = reader.GetString(1)
                });
            }
        }

        return Results.Ok(new
        {
            actions,
            scopes,
            effects = new[] { "GRANT", "DENY" },
            policyStatuses = new[] { "DRAFT", "PUBLISHED", "RETIRED" }
        });
    }

    private static async Task<IResult> RoleDetailAsync(
        string roleCode,
        string? moduleCode,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        if (await LoadActorAsync(context, connection) is null) return SessionRequired();

        var canonicalRoleCode = CanonicalRole(roleCode);
        var role = (await LoadRolesAsync(connection))
            .FirstOrDefault(item => string.Equals(
                item.RoleCode,
                canonicalRoleCode,
                StringComparison.OrdinalIgnoreCase));

        if (role is null)
        {
            return Results.NotFound(new
            {
                status = "role_not_found",
                message = $"Role {canonicalRoleCode} was not found."
            });
        }

        var users = await LoadAssignedUsersAsync(connection, canonicalRoleCode);
        var grants = await LoadGrantsAsync(connection, canonicalRoleCode, moduleCode);
        var version = await LoadPublishedVersionAsync(connection);

        return Results.Ok(new
        {
            role,
            assignedUsers = users,
            grants,
            policyVersion = version,
            moduleCode = string.IsNullOrWhiteSpace(moduleCode) ? null : moduleCode.Trim()
        });
    }

    private static async Task<IResult> MatrixAsync(HttpContext context)
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
                conditions = new { legacyAuthorizationPreserved = true },
                explanation = "No scoped workbook decision exists. Existing ProjectPulse authorization remains in effect."
            });
        }

        return Results.Ok(new
        {
            module = "037",
            status = "effective_scoped_matrix_loaded",
            readOnly = true,
            writeEndpoints = Array.Empty<string>(),
            policyVersion = version,
            roles,
            modules,
            grants,
            legacyFallback
        });
    }

    private static async Task<IResult> VersionsAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        if (await LoadActorAsync(context, connection) is null) return SessionRequired();

        var versions = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                v.policy_version_id,
                v.version_number,
                v.policy_name,
                v.policy_status,
                v.source_name,
                v.source_sha256,
                v.policy_notes,
                v.restored_from_policy_version_id,
                v.created_at,
                v.published_at,
                v.retired_at,
                COALESCE(creator.display_name, creator.email, 'System'),
                COALESCE(publisher.display_name, publisher.email, 'System'),
                (SELECT COUNT(*) FROM scoped_role_policy_grants g
                 WHERE g.policy_version_id = v.policy_version_id),
                (SELECT COUNT(*) FROM scoped_role_policy_audit_events a
                 WHERE a.policy_version_id = v.policy_version_id)
            FROM scoped_role_policy_versions v
            LEFT JOIN app_users creator ON creator.user_id = v.created_by_user_id
            LEFT JOIN app_users publisher ON publisher.user_id = v.published_by_user_id
            ORDER BY v.version_number DESC;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(new
            {
                policyVersionId = reader.GetGuid(0),
                versionNumber = reader.GetInt32(1),
                policyName = reader.GetString(2),
                policyStatus = reader.GetString(3),
                sourceName = reader.GetString(4),
                sourceSha256 = reader.GetString(5),
                policyNotes = reader.GetString(6),
                restoredFromPolicyVersionId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
                createdAt = reader.GetFieldValue<DateTimeOffset>(8),
                publishedAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                retiredAt = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                createdBy = reader.GetString(11),
                publishedBy = reader.GetString(12),
                grantCount = reader.GetInt64(13),
                auditEventCount = reader.GetInt64(14)
            });
        }

        return Results.Ok(new { versions });
    }

    private static async Task<IResult> ExplainAsync(
        string roleCode,
        string moduleCode,
        string actionCode,
        string? scopeCode,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        if (await LoadActorAsync(context, connection) is null) return SessionRequired();

        var canonicalRole = CanonicalRole(roleCode);
        var rows = await LoadGrantsAsync(connection, canonicalRole, moduleCode);
        var matches = rows.Where(item =>
            string.Equals(item.ActionCode, actionCode, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(scopeCode)
                || string.Equals(item.ScopeCode, scopeCode, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matches.Length == 0)
        {
            return Results.Ok(new
            {
                roleCode = canonicalRole,
                moduleCode,
                actionCode,
                scopeCode = scopeCode ?? "CUSTOM_RULE",
                granted = false,
                explicitDeny = false,
                inherited = true,
                explanation = "No scoped decision exists for this action. Existing legacy authorization is preserved."
            });
        }

        var explicitDeny = matches.Any(item => item.GrantEffect == "DENY");
        var grant = matches.FirstOrDefault(item => item.GrantEffect == "GRANT");
        var explanation = explicitDeny
            ? $"{canonicalRole} is explicitly denied {actionCode} in Module {moduleCode}."
            : $"{canonicalRole} is granted {actionCode} in Module {moduleCode} within {grant?.ScopeCode}.";

        return Results.Ok(new
        {
            roleCode = canonicalRole,
            moduleCode,
            actionCode,
            scopeCode = grant?.ScopeCode ?? matches[0].ScopeCode,
            granted = !explicitDeny && grant is not null,
            explicitDeny,
            inherited = false,
            delegatedAuthority = grant?.DelegatedAuthority ?? false,
            reasonRequired = grant?.ReasonRequired ?? false,
            auditRequired = grant?.AuditRequired ?? true,
            policyVersion = grant?.VersionNumber ?? matches[0].VersionNumber,
            lastModifiedBy = grant?.LastModifiedBy ?? matches[0].LastModifiedBy,
            lastModifiedAt = grant?.LastModifiedAt ?? matches[0].LastModifiedAt,
            explanation
        });
    }
}

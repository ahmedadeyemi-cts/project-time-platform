using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Authoritative, database-backed RBAC administration contract for Modules 012 and 037.
///
/// This module intentionally avoids fixed role/module counts. Active modules are read from
/// scoped_role_policy_modules, so Module 012 can register or retire modules without a new
/// application release or permission-matrix rewrite. New modules are published fail-closed:
/// every non-Super-Administrator role receives an explicit MODULE_ACCESS denial until an
/// administrator deliberately configures that role/module pair.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    private const string DynamicRbacContractVersion = "projectpulse-rbac-v1-2026-07-28";
    private static readonly Regex DynamicModuleCodePattern =
        new("^[0-9]{3}[A-Z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ProtectedGovernanceModules =
        new(StringComparer.OrdinalIgnoreCase) { "008", "009", "012", "037" };

    public static WebApplication MapDynamicRbacAdministrationEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/rbac/v1/bootstrap",
            (Func<HttpContext, Task<IResult>>)DynamicRbacBootstrapAsync);
        app.MapGet(
            "/api/rbac/v1/matrix",
            (Func<HttpContext, Task<IResult>>)DynamicRbacMatrixAsync);
        app.MapGet(
            "/api/rbac/v1/roles/{roleCode}",
            (Func<string, string?, HttpContext, Task<IResult>>)DynamicRbacRoleDetailAsync);
        app.MapGet(
            "/api/rbac/v1/users",
            (Func<HttpContext, Task<IResult>>)DynamicRbacUsersAsync);
        app.MapGet(
            "/api/rbac/v1/modules",
            (Func<HttpContext, Task<IResult>>)DynamicRbacModulesAsync);

        app.MapPost(
            "/api/rbac/v1/policies/validate",
            (Func<PolicyPublishRequest, HttpContext, Task<IResult>>)ValidateDraftAsync);
        app.MapPost(
            "/api/rbac/v1/policies/publish",
            (Func<PolicyPublishRequest, HttpContext, Task<IResult>>)PublishAsync);
        app.MapPost(
            "/api/rbac/v1/policies/versions/{policyVersionId:guid}/restore",
            (Func<Guid, PolicyRestoreRequest, HttpContext, Task<IResult>>)RestoreAsync);

        app.MapPost(
            "/api/rbac/v1/role-memberships/assign",
            (Func<RoleMembershipRequest, HttpContext, Task<IResult>>)AssignRoleMembershipAsync);
        app.MapPost(
            "/api/rbac/v1/role-memberships/remove",
            (Func<RoleMembershipRequest, HttpContext, Task<IResult>>)RemoveRoleMembershipAsync);

        app.MapPost(
            "/api/rbac/v1/modules/register",
            (Func<ModuleCatalogRequest, HttpContext, Task<IResult>>)RegisterDynamicModuleAsync);
        app.MapPost(
            "/api/rbac/v1/modules/{moduleCode}/retire",
            (Func<string, ModuleLifecycleRequest, HttpContext, Task<IResult>>)RetireDynamicModuleAsync);
        app.MapPost(
            "/api/rbac/v1/modules/{moduleCode}/restore",
            (Func<string, ModuleLifecycleRequest, HttpContext, Task<IResult>>)RestoreDynamicModuleAsync);

        return app;
    }

    private static async Task<IResult> DynamicRbacBootstrapAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;

        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return SessionRequired();

        var roles = await LoadRolesAsync(connection);
        var modules = await LoadModulesAsync(connection);
        var version = await LoadPublishedVersionAsync(connection);
        var catalog = await LoadDynamicCatalogAsync(connection);
        var activeSuperAdministrators = await CountActiveSuperAdministratorsAsync(connection);
        var grantCount = await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM scoped_role_policy_effective_grants;");
        var configuredPairCount = await ScalarIntAsync(
            connection,
            "SELECT COUNT(DISTINCT role_code || '|' || module_code) FROM scoped_role_policy_effective_grants;");

        if (roles.Count == 0 || modules.Count == 0 || version is null)
        {
            return Results.Json(new
            {
                status = "dynamic_rbac_foundation_unavailable",
                message = "The active role directory, module catalog, and published policy are required.",
                roleCount = roles.Count,
                moduleCount = modules.Count,
                publishedPolicyFound = version is not null
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!roles.Any(role => string.Equals(
                role.RoleCode,
                "SUPER_ADMINISTRATOR",
                StringComparison.OrdinalIgnoreCase))
            || activeSuperAdministrators < 1)
        {
            return Results.Json(new
            {
                status = "super_administrator_invariant_unavailable",
                message = "At least one active Super Administrator role assignment is required before RBAC administration can continue.",
                activeSuperAdministrators
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            contractVersion = DynamicRbacContractVersion,
            status = "dynamic_rbac_bootstrap_loaded",
            module = "012",
            moduleCatalogMode = "database_dynamic",
            fixedModuleCountRequired = false,
            canWritePolicy = actor.IsSuperAdministrator && !actor.IsViewAs,
            canManageRoleMemberships = actor.IsSuperAdministrator && !actor.IsViewAs,
            canManageModuleCatalog = actor.IsSuperAdministrator && !actor.IsViewAs,
            ownSessionRequired = true,
            isViewAs = actor.IsViewAs,
            actor = new
            {
                actor.ActualUserId,
                actor.EffectiveUserId,
                actor.Email,
                actor.RoleCodes
            },
            superAdministratorInvariant = new
            {
                permanentFullControl = true,
                organizationWide = true,
                reducible = false,
                activeAssignments = activeSuperAdministrators
            },
            policyVersion = version,
            roles,
            modules,
            actions = catalog.Actions,
            scopes = catalog.Scopes,
            effects = new[] { "GRANT", "DENY" },
            summary = new
            {
                roleCount = roles.Count,
                moduleCount = modules.Count,
                grantCount,
                configuredPairCount,
                possiblePairCount = roles.Count * modules.Count,
                unconfiguredPairCount = Math.Max(0, roles.Count * modules.Count - configuredPairCount),
                newModuleDefault = "NO_ACCESS_FOR_NON_SUPER_ADMINISTRATORS",
                retiredModulesExcluded = true
            }
        });
    }

    private static async Task<IResult> DynamicRbacMatrixAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        if (await LoadActorAsync(context, connection) is null) return SessionRequired();

        var roles = await LoadRolesAsync(connection);
        var modules = await LoadModulesAsync(connection);
        var grants = await LoadGrantsAsync(connection, null, null);
        var version = await LoadPublishedVersionAsync(connection);
        var catalog = await LoadDynamicCatalogAsync(connection);

        if (roles.Count == 0 || modules.Count == 0 || version is null)
        {
            return Results.Json(new
            {
                status = "dynamic_rbac_matrix_unavailable",
                message = "The active role directory, module catalog, and published policy are required.",
                roleCount = roles.Count,
                moduleCount = modules.Count,
                publishedPolicyFound = version is not null
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var configuredPairs = grants
            .Select(item => $"{item.RoleCode}|{item.ModuleCode}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unconfigured = new List<object>();
        foreach (var role in roles)
        foreach (var module in modules)
        {
            if (string.Equals(role.RoleCode, "SUPER_ADMINISTRATOR", StringComparison.OrdinalIgnoreCase))
                continue;
            if (configuredPairs.Contains($"{role.RoleCode}|{module.ModuleCode}"))
                continue;

            unconfigured.Add(new
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
                explanation = "No explicit RBAC decision exists. Existing endpoint authorization remains in effect until Module 012 publishes a decision."
            });
        }

        return Results.Ok(new
        {
            contractVersion = DynamicRbacContractVersion,
            status = "dynamic_rbac_matrix_loaded",
            module = "037",
            fixedModuleCountRequired = false,
            readOnly = true,
            writeEndpoints = Array.Empty<string>(),
            policyVersion = version,
            roles,
            modules,
            actions = catalog.Actions,
            scopes = catalog.Scopes,
            grants,
            legacyFallback = unconfigured,
            summary = new
            {
                roleCount = roles.Count,
                moduleCount = modules.Count,
                configuredPairCount = configuredPairs.Count,
                unconfiguredPairCount = unconfigured.Count,
                superAdministratorFullControl = true
            }
        });
    }

    private static async Task<IResult> DynamicRbacRoleDetailAsync(
        string roleCode,
        string? moduleCode,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
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

        return Results.Ok(new
        {
            contractVersion = DynamicRbacContractVersion,
            role,
            assignedUsers = await LoadAssignedUsersAsync(connection, canonicalRoleCode),
            grants = await LoadGrantsAsync(connection, canonicalRoleCode, moduleCode),
            policyVersion = await LoadPublishedVersionAsync(connection),
            moduleCode = string.IsNullOrWhiteSpace(moduleCode) ? null : moduleCode.Trim(),
            superAdministratorInvariant = string.Equals(
                canonicalRoleCode,
                "SUPER_ADMINISTRATOR",
                StringComparison.OrdinalIgnoreCase)
        });
    }

    private static async Task<IResult> DynamicRbacUsersAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;

        var search = context.Request.Query["search"].FirstOrDefault()?.Trim() ?? string.Empty;
        var users = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                u.user_id,
                u.email,
                COALESCE(NULLIF(u.display_name, ''), u.email),
                u.is_active,
                COALESCE(
                    ARRAY_AGG(DISTINCT UPPER(r.role_code))
                        FILTER (WHERE ura.is_active = TRUE AND r.is_active = TRUE),
                    ARRAY[]::text[]
                )
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id
            LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE u.is_active = TRUE
              AND (
                @search = ''
                OR u.email ILIKE '%' || @search || '%'
                OR COALESCE(u.display_name, '') ILIKE '%' || @search || '%'
              )
            GROUP BY u.user_id, u.email, u.display_name, u.is_active
            ORDER BY COALESCE(NULLIF(u.display_name, ''), u.email), u.email
            LIMIT 1000;
            """, connection);
        command.Parameters.AddWithValue("search", search);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            users.Add(new
            {
                userId = reader.GetGuid(0),
                email = reader.GetString(1),
                displayName = reader.GetString(2),
                isActive = reader.GetBoolean(3),
                roleCodes = reader.GetFieldValue<string[]>(4)
                    .Select(CanonicalRole)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToArray()
            });
        }

        return Results.Ok(new
        {
            contractVersion = DynamicRbacContractVersion,
            users,
            count = users.Count,
            secretValuesReturned = false
        });
    }

    private static async Task<IResult> DynamicRbacModulesAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        if (await LoadActorAsync(context, connection) is null) return SessionRequired();

        var includeInactive = string.Equals(
            context.Request.Query["includeInactive"].FirstOrDefault(),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var modules = new List<object>();
        await using var command = new NpgsqlCommand($"""
            SELECT module_code, module_name, route_scope, current_state,
                   permission_notes, source_url, is_active, created_at
            FROM scoped_role_policy_modules
            {(includeInactive ? string.Empty : "WHERE is_active = TRUE")}
            ORDER BY
                CASE WHEN module_code ~ '^[0-9]+' THEN
                    substring(module_code from '^[0-9]+')::integer
                ELSE 10000 END,
                module_code;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            modules.Add(new
            {
                moduleCode = reader.GetString(0),
                moduleName = reader.GetString(1),
                routeScope = reader.GetString(2),
                currentState = reader.GetString(3),
                permissionNotes = reader.GetString(4),
                sourceUrl = reader.GetString(5),
                isActive = reader.GetBoolean(6),
                createdAt = reader.GetFieldValue<DateTimeOffset>(7),
                protectedGovernanceModule = ProtectedGovernanceModules.Contains(reader.GetString(0))
            });
        }

        return Results.Ok(new
        {
            contractVersion = DynamicRbacContractVersion,
            modules,
            activeCount = modules.Count(item =>
                Convert.ToBoolean(item.GetType().GetProperty("isActive")?.GetValue(item) ?? false)),
            fixedModuleCountRequired = false
        });
    }

    private static async Task<IResult> AssignRoleMembershipAsync(
        RoleMembershipRequest request,
        HttpContext context)
    {
        var reason = CleanRequiredReason(request.Reason);
        if (reason is null) return DynamicRbacReasonRequired("assign a role");

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;
        var actor = actorResult.Actor!;

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await AdvisoryLockAsync(connection, transaction);
            var role = await ResolveActiveRoleAsync(connection, transaction, request.RoleCode);
            if (role is null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new
                {
                    status = "role_not_found",
                    message = "Select an active Pulse role."
                });
            }
            if (!await DynamicRbacActiveUserExistsAsync(connection, transaction, request.UserId))
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new
                {
                    status = "user_not_found",
                    message = "Select an active Pulse user."
                });
            }

            await using (var command = new NpgsqlCommand("""
                INSERT INTO app_user_role_assignments (
                    user_id, app_role_id, assigned_by_user_id,
                    assignment_reason, is_active, created_at, updated_at
                )
                VALUES (
                    @user_id, @role_id, @actor_user_id,
                    @reason, TRUE, NOW(), NOW()
                )
                ON CONFLICT (user_id, app_role_id) DO UPDATE
                SET assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                    assignment_reason = EXCLUDED.assignment_reason,
                    is_active = TRUE,
                    updated_at = NOW();
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("user_id", request.UserId);
                command.Parameters.AddWithValue("role_id", role.Value.RoleId);
                command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                command.Parameters.AddWithValue("reason", reason);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var version = await LoadPublishedVersionAsync(connection, transaction);
            if (version is null) throw new InvalidOperationException("No published RBAC policy exists.");
            await InsertAuditAsync(
                connection,
                transaction,
                version.PolicyVersionId,
                "ROLE_MEMBERSHIP_ASSIGNED",
                actor,
                reason,
                JsonSerializer.SerializeToElement(new { request.UserId, roleCode = role.Value.RoleCode, assigned = false }),
                JsonSerializer.SerializeToElement(new { request.UserId, roleCode = role.Value.RoleCode, assigned = true }));

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "role_membership_assigned",
                request.UserId,
                roleCode = role.Value.RoleCode,
                reason,
                effectiveOnNextAuthorizedRequest = true,
                activeSuperAdministrators = await CountActiveSuperAdministratorsAsync(connection)
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<IResult> RemoveRoleMembershipAsync(
        RoleMembershipRequest request,
        HttpContext context)
    {
        var reason = CleanRequiredReason(request.Reason);
        if (reason is null) return DynamicRbacReasonRequired("remove a role");

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;
        var actor = actorResult.Actor!;

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await AdvisoryLockAsync(connection, transaction);
            var role = await ResolveActiveRoleAsync(connection, transaction, request.RoleCode);
            if (role is null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new { status = "role_not_found", message = "Select an active Pulse role." });
            }

            var superAdministrator = string.Equals(
                role.Value.RoleCode,
                "SUPER_ADMINISTRATOR",
                StringComparison.OrdinalIgnoreCase);
            if (superAdministrator && request.UserId == actor.ActualUserId)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    status = "super_administrator_self_lockout_blocked",
                    message = "A Super Administrator cannot remove their own Super Administrator assignment. Another Super Administrator must make that change."
                });
            }
            if (superAdministrator
                && await CountActiveSuperAdministratorsAsync(connection, transaction) <= 1)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    status = "final_super_administrator_removal_blocked",
                    message = "The final active Super Administrator assignment cannot be removed."
                });
            }

            int affected;
            await using (var command = new NpgsqlCommand("""
                UPDATE app_user_role_assignments
                SET is_active = FALSE,
                    assigned_by_user_id = @actor_user_id,
                    assignment_reason = @reason,
                    updated_at = NOW()
                WHERE user_id = @user_id
                  AND app_role_id = @role_id
                  AND is_active = TRUE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("user_id", request.UserId);
                command.Parameters.AddWithValue("role_id", role.Value.RoleId);
                command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                command.Parameters.AddWithValue("reason", reason);
                affected = await command.ExecuteNonQueryAsync(context.RequestAborted);
            }
            if (affected == 0)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new
                {
                    status = "role_membership_not_found",
                    message = "The selected user does not currently have that role."
                });
            }

            var version = await LoadPublishedVersionAsync(connection, transaction);
            if (version is null) throw new InvalidOperationException("No published RBAC policy exists.");
            await InsertAuditAsync(
                connection,
                transaction,
                version.PolicyVersionId,
                "ROLE_MEMBERSHIP_REMOVED",
                actor,
                reason,
                JsonSerializer.SerializeToElement(new { request.UserId, roleCode = role.Value.RoleCode, assigned = true }),
                JsonSerializer.SerializeToElement(new { request.UserId, roleCode = role.Value.RoleCode, assigned = false }));

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "role_membership_removed",
                request.UserId,
                roleCode = role.Value.RoleCode,
                reason,
                effectiveOnNextAuthorizedRequest = true,
                activeSuperAdministrators = await CountActiveSuperAdministratorsAsync(connection)
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<IResult> RegisterDynamicModuleAsync(
        ModuleCatalogRequest request,
        HttpContext context)
    {
        var moduleCode = NormalizeModuleCode(request.ModuleCode);
        var moduleName = (request.ModuleName ?? string.Empty).Trim();
        var routeScope = (request.RouteScope ?? string.Empty).Trim();
        var currentState = string.IsNullOrWhiteSpace(request.CurrentState)
            ? "Active"
            : request.CurrentState.Trim();
        var reason = CleanRequiredReason(request.Reason);
        if (moduleCode is null || moduleName.Length is < 2 or > 200 || routeScope.Length is < 1 or > 200)
        {
            return Results.BadRequest(new
            {
                status = "invalid_module_catalog_request",
                message = "Enter a valid module code, name, and route or scope."
            });
        }
        if (reason is null) return DynamicRbacReasonRequired("register or restore a module");

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;
        var actor = actorResult.Actor!;

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await AdvisoryLockAsync(connection, transaction);
            var previous = await ReadModuleCatalogRowAsync(connection, transaction, moduleCode);
            await using (var command = new NpgsqlCommand("""
                INSERT INTO scoped_role_policy_modules (
                    module_code, module_name, route_scope, current_state,
                    permission_notes, source_url, is_active, created_at
                )
                VALUES (
                    @module_code, @module_name, @route_scope, @current_state,
                    @permission_notes, 'Module 012 dynamic RBAC catalog', TRUE, NOW()
                )
                ON CONFLICT (module_code) DO UPDATE
                SET module_name = EXCLUDED.module_name,
                    route_scope = EXCLUDED.route_scope,
                    current_state = EXCLUDED.current_state,
                    permission_notes = EXCLUDED.permission_notes,
                    source_url = EXCLUDED.source_url,
                    is_active = TRUE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("module_code", moduleCode);
                command.Parameters.AddWithValue("module_name", moduleName);
                command.Parameters.AddWithValue("route_scope", routeScope);
                command.Parameters.AddWithValue("current_state", currentState);
                command.Parameters.AddWithValue("permission_notes", request.PermissionNotes?.Trim() ?? string.Empty);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var published = await PublishModuleCatalogPolicyVersionAsync(
                connection,
                transaction,
                actor,
                moduleCode,
                reason,
                previous is null ? "MODULE_REGISTERED" : "MODULE_RESTORED_OR_UPDATED");
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                status = previous is null ? "module_registered" : "module_restored_or_updated",
                moduleCode,
                moduleName,
                routeScope,
                defaultAccess = "NO_ACCESS_FOR_NON_SUPER_ADMINISTRATORS",
                superAdministratorAccess = "FULL_CONTROL",
                publishedPolicyVersion = published.VersionNumber,
                defaultDenyCount = published.DefaultDenyCount
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<IResult> RetireDynamicModuleAsync(
        string moduleCode,
        ModuleLifecycleRequest request,
        HttpContext context)
    {
        var normalized = NormalizeModuleCode(moduleCode);
        var reason = CleanRequiredReason(request.Reason);
        if (normalized is null) return Results.BadRequest(new { status = "invalid_module_code" });
        if (ProtectedGovernanceModules.Contains(normalized))
        {
            return Results.Conflict(new
            {
                status = "protected_governance_module",
                message = "Modules 008, 009, 012, and 037 cannot be retired through Module 012."
            });
        }
        if (reason is null) return DynamicRbacReasonRequired("retire a module");

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;
        var actor = actorResult.Actor!;

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await AdvisoryLockAsync(connection, transaction);
            var previous = await ReadModuleCatalogRowAsync(connection, transaction, normalized);
            if (previous is null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new { status = "module_not_found" });
            }

            await using (var command = new NpgsqlCommand("""
                UPDATE scoped_role_policy_modules
                SET is_active = FALSE,
                    current_state = 'Retired',
                    permission_notes = @reason
                WHERE module_code = @module_code;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("module_code", normalized);
                command.Parameters.AddWithValue("reason", reason);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var version = await LoadPublishedVersionAsync(connection, transaction);
            if (version is null) throw new InvalidOperationException("No published RBAC policy exists.");
            await InsertAuditAsync(
                connection,
                transaction,
                version.PolicyVersionId,
                "MODULE_RETIRED",
                actor,
                reason,
                JsonSerializer.SerializeToElement(previous),
                JsonSerializer.SerializeToElement(new { moduleCode = normalized, isActive = false, currentState = "Retired" }));
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                status = "module_retired",
                moduleCode = normalized,
                removedFromActivePermissionCatalog = true,
                historicalPolicyAndAuditPreserved = true
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<IResult> RestoreDynamicModuleAsync(
        string moduleCode,
        ModuleLifecycleRequest request,
        HttpContext context)
    {
        var normalized = NormalizeModuleCode(moduleCode);
        var reason = CleanRequiredReason(request.Reason);
        if (normalized is null) return Results.BadRequest(new { status = "invalid_module_code" });
        if (reason is null) return DynamicRbacReasonRequired("restore a module");

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;
        var actor = actorResult.Actor!;

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await AdvisoryLockAsync(connection, transaction);
            var previous = await ReadModuleCatalogRowAsync(connection, transaction, normalized);
            if (previous is null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new
                {
                    status = "module_not_found",
                    message = "Register the module with its name and route before restoring it."
                });
            }

            await using (var command = new NpgsqlCommand("""
                UPDATE scoped_role_policy_modules
                SET is_active = TRUE,
                    current_state = CASE WHEN current_state = 'Retired' THEN 'Active' ELSE current_state END,
                    permission_notes = @reason
                WHERE module_code = @module_code;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("module_code", normalized);
                command.Parameters.AddWithValue("reason", reason);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var published = await PublishModuleCatalogPolicyVersionAsync(
                connection,
                transaction,
                actor,
                normalized,
                reason,
                "MODULE_RESTORED");
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                status = "module_restored",
                moduleCode = normalized,
                defaultAccess = "NO_ACCESS_FOR_NON_SUPER_ADMINISTRATORS",
                superAdministratorAccess = "FULL_CONTROL",
                publishedPolicyVersion = published.VersionNumber,
                defaultDenyCount = published.DefaultDenyCount
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<(int VersionNumber, int DefaultDenyCount)> PublishModuleCatalogPolicyVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActorContext actor,
        string moduleCode,
        string reason,
        string eventCode)
    {
        var current = await LoadPublishedVersionAsync(connection, transaction)
            ?? throw new InvalidOperationException("No published RBAC policy exists.");
        var nextVersion = await NextVersionNumberAsync(connection, transaction);
        var newPolicyId = Guid.NewGuid();
        await InsertVersionAsync(
            connection,
            transaction,
            newPolicyId,
            nextVersion,
            $"Dynamic RBAC module catalog v{nextVersion}",
            "DRAFT",
            "Module 012 dynamic RBAC catalog",
            "runtime-dynamic-module-catalog",
            reason,
            actor.ActualUserId,
            null);

        await using (var clone = new NpgsqlCommand("""
            INSERT INTO scoped_role_policy_grants (
                policy_version_id, role_code, module_code, action_code,
                scope_code, grant_effect, conditions, delegated_authority,
                reason_required, audit_required, source_designation,
                source_notes, is_active
            )
            SELECT
                @new_policy_id, role_code, module_code, action_code,
                scope_code, grant_effect, conditions, delegated_authority,
                reason_required, audit_required, source_designation,
                source_notes, is_active
            FROM scoped_role_policy_grants
            WHERE policy_version_id = @current_policy_id;
            """, connection, transaction))
        {
            clone.Parameters.AddWithValue("new_policy_id", newPolicyId);
            clone.Parameters.AddWithValue("current_policy_id", current.PolicyVersionId);
            await clone.ExecuteNonQueryAsync();
        }

        var defaultDenyCount = 0;
        var roleCodes = (await LoadCodeSetAsync(
                connection,
                transaction,
                "SELECT role_code FROM app_roles WHERE is_active = TRUE;"))
            .Select(CanonicalRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(role => !string.Equals(role, "SUPER_ADMINISTRATOR", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var roleCode in roleCodes)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO scoped_role_policy_grants (
                    policy_version_id, role_code, module_code, action_code,
                    scope_code, grant_effect, conditions, delegated_authority,
                    reason_required, audit_required, source_designation,
                    source_notes, is_active
                )
                SELECT
                    @policy_version_id, @role_code, @module_code, 'MODULE_ACCESS',
                    'ORGANIZATION', 'DENY', @conditions::jsonb, FALSE,
                    FALSE, TRUE, 'No Access', @reason, TRUE
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM scoped_role_policy_grants
                    WHERE policy_version_id = @policy_version_id
                      AND role_code = @role_code
                      AND module_code = @module_code
                );
                """, connection, transaction);
            insert.Parameters.AddWithValue("policy_version_id", newPolicyId);
            insert.Parameters.AddWithValue("role_code", roleCode);
            insert.Parameters.AddWithValue("module_code", moduleCode);
            insert.Parameters.AddWithValue("reason", reason);
            insert.Parameters.AddWithValue(
                "conditions",
                JsonSerializer.Serialize(new
                {
                    source = "Module 012 dynamic RBAC catalog",
                    permissionLevel = "No Access",
                    defaultPolicy = "NO_ACCESS_UNTIL_CONFIGURED",
                    newModuleFailClosed = true
                }));
            defaultDenyCount += await insert.ExecuteNonQueryAsync();
        }

        var validation = await ValidatePolicyVersionAsync(connection, transaction, newPolicyId);
        if (!validation.Valid)
            throw new InvalidOperationException(string.Join(" ", validation.Errors));

        await using (var retire = new NpgsqlCommand("""
            UPDATE scoped_role_policy_versions
            SET policy_status = 'RETIRED', retired_at = NOW()
            WHERE policy_version_id = @current_policy_id;
            """, connection, transaction))
        {
            retire.Parameters.AddWithValue("current_policy_id", current.PolicyVersionId);
            await retire.ExecuteNonQueryAsync();
        }
        await using (var publish = new NpgsqlCommand("""
            UPDATE scoped_role_policy_versions
            SET policy_status = 'PUBLISHED',
                published_by_user_id = @actor_user_id,
                published_at = NOW()
            WHERE policy_version_id = @new_policy_id;
            """, connection, transaction))
        {
            publish.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
            publish.Parameters.AddWithValue("new_policy_id", newPolicyId);
            await publish.ExecuteNonQueryAsync();
        }

        await InsertAuditAsync(
            connection,
            transaction,
            newPolicyId,
            eventCode,
            actor,
            reason,
            JsonSerializer.SerializeToElement(new
            {
                current.PolicyVersionId,
                current.VersionNumber,
                moduleCode
            }),
            JsonSerializer.SerializeToElement(new
            {
                policyVersionId = newPolicyId,
                versionNumber = nextVersion,
                moduleCode,
                defaultDenyCount,
                superAdministratorFullControl = true
            }));
        return (nextVersion, defaultDenyCount);
    }

    private static async Task<(Guid RoleId, string RoleCode)?> ResolveActiveRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? requestedRole)
    {
        var canonical = CanonicalRole(requestedRole);
        if (!CanonicalRoleOrder.Contains(canonical, StringComparer.OrdinalIgnoreCase)) return null;
        await using var command = new NpgsqlCommand("""
            SELECT app_role_id, role_code
            FROM app_roles
            WHERE is_active = TRUE
              AND UPPER(role_code) = ANY(@role_codes)
            ORDER BY CASE WHEN UPPER(role_code) = @canonical THEN 0 ELSE 1 END
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("role_codes", AliasesFor(canonical));
        command.Parameters.AddWithValue("canonical", canonical);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (reader.GetGuid(0), canonical)
            : null;
    }

    private static async Task<bool> DynamicRbacActiveUserExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM app_users WHERE user_id = @user_id AND is_active = TRUE);",
            connection,
            transaction);
        command.Parameters.AddWithValue("user_id", userId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<ModuleCatalogSnapshot?> ReadModuleCatalogRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string moduleCode)
    {
        await using var command = new NpgsqlCommand("""
            SELECT module_code, module_name, route_scope, current_state,
                   permission_notes, source_url, is_active
            FROM scoped_role_policy_modules
            WHERE module_code = @module_code;
            """, connection, transaction);
        command.Parameters.AddWithValue("module_code", moduleCode);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new ModuleCatalogSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6))
            : null;
    }

    private static async Task<(List<object> Actions, List<object> Scopes)> LoadDynamicCatalogAsync(
        NpgsqlConnection connection)
    {
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
        return (actions, scopes);
    }

    private static string? NormalizeModuleCode(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return DynamicModuleCodePattern.IsMatch(normalized) ? normalized : null;
    }

    private static string? CleanRequiredReason(string? value)
    {
        var reason = (value ?? string.Empty).Trim();
        return reason.Length is >= 5 and <= 1000 ? reason : null;
    }

    private static IResult DynamicRbacReasonRequired(string operation) => Results.BadRequest(new
    {
        status = "reason_required",
        message = $"Enter a reason of 5 to 1,000 characters to {operation}."
    });

    public sealed record RoleMembershipRequest(Guid UserId, string RoleCode, string? Reason);
    public sealed record ModuleCatalogRequest(
        string ModuleCode,
        string ModuleName,
        string RouteScope,
        string? CurrentState,
        string? PermissionNotes,
        string? Reason);
    public sealed record ModuleLifecycleRequest(string? Reason);
    private sealed record ModuleCatalogSnapshot(
        string ModuleCode,
        string ModuleName,
        string RouteScope,
        string CurrentState,
        string PermissionNotes,
        string SourceUrl,
        bool IsActive);
}

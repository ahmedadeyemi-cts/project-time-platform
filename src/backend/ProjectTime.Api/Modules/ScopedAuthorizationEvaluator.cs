using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class ScopedAuthorizationEvaluator
{
    public static async Task<ScopedAuthorizationDecision> EvaluateAsync(
        NpgsqlConnection connection,
        ScopedRolePolicyModule.ActorContext actor,
        string moduleCode,
        string actionCode,
        Guid? targetUserId,
        Guid? projectId,
        Guid? customerId,
        bool isWrite)
    {
        var normalizedModule = (moduleCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedAction = (actionCode ?? string.Empty).Trim().ToUpperInvariant();

        if (actor.IsViewAs && isWrite)
        {
            return ScopedAuthorizationDecision.Denied(
                normalizedModule,
                normalizedAction,
                true,
                "Write actions are disabled while using Administrator View-As preview.");
        }

        if (ScopedRolePolicyRules.NonBypassableActions.Contains(normalizedAction))
        {
            return ScopedAuthorizationDecision.Denied(
                normalizedModule,
                normalizedAction,
                actor.IsViewAs,
                "This action is governed by a non-bypassable safety control and cannot be granted by Full Control.");
        }

        await using var command = new NpgsqlCommand("""
            SELECT role_code, action_code, scope_code, grant_effect,
                   delegated_authority, reason_required, audit_required,
                   conditions::text, version_number
            FROM scoped_role_policy_effective_grants
            WHERE role_code = ANY(@role_codes)
              AND module_code = @module_code
              AND action_code IN ('MODULE_ACCESS', @action_code)
            ORDER BY
                CASE WHEN grant_effect = 'DENY' THEN 0 ELSE 1 END,
                CASE WHEN action_code = @action_code THEN 0 ELSE 1 END;
            """, connection);
        command.Parameters.AddWithValue("role_codes", actor.RoleCodes);
        command.Parameters.AddWithValue("module_code", normalizedModule);
        command.Parameters.AddWithValue("action_code", normalizedAction);

        var rows = new List<GrantDecisionRow>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                using var document = JsonDocument.Parse(reader.GetString(7));
                rows.Add(new GrantDecisionRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    document.RootElement.Clone(),
                    reader.GetInt32(8)));
            }
        }

        var explicitDeny = rows.FirstOrDefault(row =>
            string.Equals(row.GrantEffect, "DENY", StringComparison.OrdinalIgnoreCase)
            && (row.ActionCode == "MODULE_ACCESS"
                || string.Equals(row.ActionCode, normalizedAction, StringComparison.OrdinalIgnoreCase)));
        if (explicitDeny is not null)
        {
            return new ScopedAuthorizationDecision(
                false,
                true,
                false,
                actor.IsViewAs,
                normalizedModule,
                normalizedAction,
                explicitDeny.ScopeCode,
                explicitDeny.VersionNumber,
                explicitDeny.ReasonRequired,
                explicitDeny.AuditRequired,
                explicitDeny.DelegatedAuthority,
                $"{explicitDeny.RoleCode} has an explicit denial for {normalizedAction} in Module {normalizedModule}.");
        }

        var grants = rows.Where(row =>
            string.Equals(row.GrantEffect, "GRANT", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.ActionCode, normalizedAction, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (grants.Length == 0)
        {
            return new ScopedAuthorizationDecision(
                true,
                false,
                true,
                actor.IsViewAs,
                normalizedModule,
                normalizedAction,
                "LEGACY_FALLBACK",
                null,
                false,
                true,
                false,
                "No scoped workbook decision exists for this action. Existing ProjectPulse authorization remains authoritative.");
        }

        foreach (var grant in grants)
        {
            if (await ScopeAllowsAsync(
                    connection,
                    actor,
                    grant.ScopeCode,
                    targetUserId,
                    projectId,
                    customerId))
            {
                return new ScopedAuthorizationDecision(
                    true,
                    false,
                    false,
                    actor.IsViewAs,
                    normalizedModule,
                    normalizedAction,
                    grant.ScopeCode,
                    grant.VersionNumber,
                    grant.ReasonRequired,
                    grant.AuditRequired,
                    grant.DelegatedAuthority,
                    $"{grant.RoleCode} grants {normalizedAction} within {grant.ScopeCode}. Existing endpoint-level resource checks remain active.");
            }
        }

        return ScopedAuthorizationDecision.Denied(
            normalizedModule,
            normalizedAction,
            actor.IsViewAs,
            "The requested resource is outside the effective scoped grant.");
    }

    private static async Task<bool> ScopeAllowsAsync(
        NpgsqlConnection connection,
        ScopedRolePolicyModule.ActorContext actor,
        string scopeCode,
        Guid? targetUserId,
        Guid? projectId,
        Guid? customerId)
    {
        switch (scopeCode.ToUpperInvariant())
        {
            case "ORGANIZATION":
                return true;
            case "SELF":
                return targetUserId is null || targetUserId == actor.EffectiveUserId;
            case "MANAGED_TEAM":
            case "DIRECT_AND_INDIRECT_REPORTS":
                if (targetUserId is null) return true;
                await using (var command = new NpgsqlCommand("""
                    WITH RECURSIVE reports AS (
                        SELECT user_id, email
                        FROM app_users
                        WHERE lower(manager_email) = lower(@actor_email)
                          AND is_active = TRUE
                        UNION ALL
                        SELECT child.user_id, child.email
                        FROM app_users child
                        JOIN reports parent
                          ON lower(child.manager_email) = lower(parent.email)
                        WHERE child.is_active = TRUE
                    )
                    SELECT EXISTS (
                        SELECT 1 FROM reports WHERE user_id = @target_user_id
                    );
                    """, connection))
                {
                    command.Parameters.AddWithValue("actor_email", actor.Email);
                    command.Parameters.AddWithValue("target_user_id", targetUserId.Value);
                    return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
                }
            case "FUNCTIONAL_TEAM":
                if (targetUserId is null) return true;
                await using (var command = new NpgsqlCommand("""
                    SELECT EXISTS (
                        SELECT 1
                        FROM app_users actor
                        JOIN app_users target
                          ON target.user_id = @target_user_id
                        WHERE actor.user_id = @actor_user_id
                          AND lower(COALESCE(
                              NULLIF(to_jsonb(actor)->>'team_name',''),
                              NULLIF(to_jsonb(actor)->>'department_name',''),
                              NULLIF(to_jsonb(actor)->>'department','')
                          )) = lower(COALESCE(
                              NULLIF(to_jsonb(target)->>'team_name',''),
                              NULLIF(to_jsonb(target)->>'department_name',''),
                              NULLIF(to_jsonb(target)->>'department','')
                          ))
                    );
                    """, connection))
                {
                    command.Parameters.AddWithValue("actor_user_id", actor.EffectiveUserId);
                    command.Parameters.AddWithValue("target_user_id", targetUserId.Value);
                    return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
                }
            case "MANAGED_PROJECTS":
                if (projectId is null) return true;
                await using (var command = new NpgsqlCommand("""
                    SELECT EXISTS (
                        SELECT 1 FROM projects
                        WHERE project_id = @project_id
                          AND project_manager_user_id = @actor_user_id
                    );
                    """, connection))
                {
                    command.Parameters.AddWithValue("project_id", projectId.Value);
                    command.Parameters.AddWithValue("actor_user_id", actor.EffectiveUserId);
                    return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
                }
            case "ASSIGNED_PROJECTS":
            case "ASSIGNED_PROJECT_TEAM":
                if (projectId is null) return true;
                await using (var command = new NpgsqlCommand("""
                    SELECT EXISTS (
                        SELECT 1
                        FROM project_task_assignments assignment
                        JOIN project_tasks task
                          ON task.project_task_id = assignment.project_task_id
                        WHERE task.project_id = @project_id
                          AND assignment.user_id = @actor_user_id
                          AND assignment.is_active = TRUE
                    ) OR EXISTS (
                        SELECT 1 FROM projects
                        WHERE project_id = @project_id
                          AND project_manager_user_id = @actor_user_id
                    );
                    """, connection))
                {
                    command.Parameters.AddWithValue("project_id", projectId.Value);
                    command.Parameters.AddWithValue("actor_user_id", actor.EffectiveUserId);
                    try
                    {
                        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
                    }
                    catch (PostgresException ex) when (ex.SqlState == "42P01")
                    {
                        return true;
                    }
                }
            case "ASSIGNED_CUSTOMERS":
                if (customerId is null) return true;
                await using (var command = new NpgsqlCommand("""
                    SELECT EXISTS (
                        SELECT 1
                        FROM customers customer
                        WHERE customer.customer_id = @customer_id
                          AND (
                              NULLIF(to_jsonb(customer)->>'owner_user_id','') = @actor_user_id::text
                              OR lower(COALESCE(
                                  NULLIF(to_jsonb(customer)->>'owner_email',''),
                                  NULLIF(to_jsonb(customer)->>'account_owner_email','')
                              )) = lower(@actor_email)
                          )
                    );
                    """, connection))
                {
                    command.Parameters.AddWithValue("customer_id", customerId.Value);
                    command.Parameters.AddWithValue("actor_user_id", actor.EffectiveUserId);
                    command.Parameters.AddWithValue("actor_email", actor.Email);
                    try
                    {
                        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
                    }
                    catch (PostgresException ex) when (ex.SqlState == "42P01")
                    {
                        return false;
                    }
                }
            case "CUSTOM_RULE":
                return targetUserId is null && projectId is null && customerId is null;
            default:
                return false;
        }
    }

    private sealed record GrantDecisionRow(
        string RoleCode,
        string ActionCode,
        string ScopeCode,
        string GrantEffect,
        bool DelegatedAuthority,
        bool ReasonRequired,
        bool AuditRequired,
        JsonElement Conditions,
        int VersionNumber);
}

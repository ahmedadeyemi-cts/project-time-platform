using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<List<RoleSummaryRow>> LoadRolesAsync(NpgsqlConnection connection)
    {
        var roleRows = new Dictionary<string, RoleSummaryRow>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT
                r.role_code,
                r.role_name,
                COALESCE(r.role_description, ''),
                r.is_active,
                COUNT(DISTINCT ura.user_id) FILTER (
                    WHERE ura.is_active = TRUE AND u.is_active = TRUE
                ) AS active_user_count
            FROM app_roles r
            LEFT JOIN app_user_role_assignments ura
              ON ura.app_role_id = r.app_role_id
            LEFT JOIN app_users u ON u.user_id = ura.user_id
            GROUP BY r.app_role_id, r.role_code, r.role_name,
                     r.role_description, r.is_active;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var rawCode = reader.GetString(0);
            var canonical = CanonicalRole(rawCode);
            if (!CanonicalRoleOrder.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                continue;
            var count = Convert.ToInt32(reader.GetInt64(4));
            if (!roleRows.TryGetValue(canonical, out var current))
            {
                roleRows[canonical] = new RoleSummaryRow(
                    canonical,
                    RoleDisplayName(canonical),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    count);
            }
            else
            {
                roleRows[canonical] = current with
                {
                    ActiveUserCount = current.ActiveUserCount + count,
                    IsActive = current.IsActive || reader.GetBoolean(3)
                };
            }
        }

        foreach (var roleCode in CanonicalRoleOrder)
        {
            if (!roleRows.ContainsKey(roleCode))
            {
                roleRows[roleCode] = new RoleSummaryRow(
                    roleCode,
                    RoleDisplayName(roleCode),
                    string.Empty,
                    false,
                    0);
            }
        }

        return CanonicalRoleOrder.Select(code => roleRows[code]).ToList();
    }

    private static async Task<List<ModuleSummaryRow>> LoadModulesAsync(NpgsqlConnection connection)
    {
        var modules = new List<ModuleSummaryRow>();
        await using var command = new NpgsqlCommand("""
            SELECT module_code, module_name, route_scope, current_state,
                   permission_notes, source_url
            FROM scoped_role_policy_modules
            WHERE is_active = TRUE
            ORDER BY
                CASE WHEN module_code ~ '^[0-9]+' THEN
                    substring(module_code from '^[0-9]+')::integer
                ELSE 10000 END,
                module_code;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(new ModuleSummaryRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }
        return modules;
    }

    private static async Task<List<AssignedUserRow>> LoadAssignedUsersAsync(
        NpgsqlConnection connection,
        string roleCode)
    {
        var users = new List<AssignedUserRow>();
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT u.user_id, u.email,
                   COALESCE(u.display_name, u.email),
                   u.is_active
            FROM app_users u
            JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id AND ura.is_active = TRUE
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
            WHERE upper(r.role_code) = ANY(@role_codes)
            ORDER BY COALESCE(u.display_name, u.email), u.email;
            """, connection);
        command.Parameters.AddWithValue("role_codes", AliasesFor(roleCode));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new AssignedUserRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }
        return users;
    }

    private static async Task<List<PolicyGrantRow>> LoadGrantsAsync(
        NpgsqlConnection connection,
        string? roleCode,
        string? moduleCode)
    {
        var grants = new List<PolicyGrantRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                g.role_code,
                g.module_code,
                g.module_name,
                g.route_scope,
                g.action_code,
                g.scope_code,
                g.grant_effect,
                g.conditions::text,
                g.delegated_authority,
                g.reason_required,
                g.audit_required,
                g.source_designation,
                g.source_notes,
                g.version_number,
                COALESCE(u.display_name, u.email, 'System'),
                g.published_at
            FROM scoped_role_policy_effective_grants g
            LEFT JOIN scoped_role_policy_versions v
              ON v.policy_version_id = g.policy_version_id
            LEFT JOIN app_users u ON u.user_id = v.published_by_user_id
            WHERE (@role_code = '' OR g.role_code = @role_code)
              AND (@module_code = '' OR g.module_code = @module_code)
            ORDER BY g.module_code, g.action_code, g.scope_code, g.role_code;
            """, connection);
        command.Parameters.AddWithValue("role_code", roleCode?.Trim().ToUpperInvariant() ?? string.Empty);
        command.Parameters.AddWithValue("module_code", moduleCode?.Trim() ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            using var document = JsonDocument.Parse(reader.GetString(7));
            grants.Add(new PolicyGrantRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                document.RootElement.Clone(),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetInt32(13),
                reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15)));
        }
        return grants;
    }
}

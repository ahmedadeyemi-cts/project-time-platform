#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.6"', api)
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')

endpoints = r'''
app.MapGet("/api/security/me", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    return Results.Ok(await BuildSecurityContextAsync(connection, userId));
});

app.MapGet("/api/security/role-matrix", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            r.role_code,
            r.role_name,
            r.role_description,
            COALESCE(array_agg(p.permission_code ORDER BY p.module_code, p.permission_code) FILTER (WHERE p.permission_code IS NOT NULL), ARRAY[]::text[]) AS permissions
        FROM app_roles r
        LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
        LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
        WHERE r.is_active = TRUE
        GROUP BY r.role_code, r.role_name, r.role_description, r.display_order
        ORDER BY r.display_order;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(new
        {
            roleCode = reader.GetString(0),
            roleName = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            permissions = reader.GetFieldValue<string[]>(3)
        });
    }

    return Results.Ok(new { count = roles.Count, roles });
});

'''

if 'app.MapGet("/api/security/me"' not in api:
    api = api.replace('\napp.Run();', '\n' + endpoints + 'app.Run();', 1)

helpers = r'''
static async Task<object> BuildSecurityContextAsync(NpgsqlConnection connection, Guid userId)
{
    string? email = null;
    string? displayName = null;

    await using (var userCommand = new NpgsqlCommand("SELECT email, display_name FROM app_users WHERE user_id = @user_id;", connection))
    {
        userCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await userCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            email = reader.GetString(0);
            displayName = reader.GetString(1);
        }
    }

    var roles = new List<object>();
    await using (var roleCommand = new NpgsqlCommand("""
        SELECT r.role_code, r.role_name, r.role_description
        FROM app_user_role_assignments ura
        INNER JOIN app_roles r ON r.app_role_id = ura.app_role_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY r.display_order;
        """, connection))
    {
        roleCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await roleCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }
    }

    var permissions = new List<string>();
    await using (var permissionCommand = new NpgsqlCommand("""
        SELECT DISTINCT p.permission_code
        FROM app_user_role_assignments ura
        INNER JOIN app_roles r ON r.app_role_id = ura.app_role_id
        INNER JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
        INNER JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY p.permission_code;
        """, connection))
    {
        permissionCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await permissionCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync()) permissions.Add(reader.GetString(0));
    }

    var features = new List<object>();
    await using (var featureCommand = new NpgsqlCommand("""
        SELECT feature_code, feature_name, module_code, route_anchor, required_permission_code, feature_description
        FROM app_feature_catalog
        WHERE is_active = TRUE
          AND (required_permission_code IS NULL OR required_permission_code = ANY(@permissions))
        ORDER BY display_order;
        """, connection))
    {
        featureCommand.Parameters.AddWithValue("permissions", permissions.ToArray());
        await using var reader = await featureCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            features.Add(new
            {
                featureCode = reader.GetString(0),
                featureName = reader.GetString(1),
                moduleCode = reader.GetString(2),
                routeAnchor = reader.IsDBNull(3) ? null : reader.GetString(3),
                requiredPermissionCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                description = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    return new
    {
        userId,
        email,
        displayName,
        roles,
        permissions,
        features,
        can = new
        {
            viewTimeEntry = permissions.Contains("VIEW_TIME_ENTRY"),
            editOwnTime = permissions.Contains("EDIT_OWN_TIME"),
            approveTime = permissions.Contains("APPROVE_TIME"),
            rejectTime = permissions.Contains("REJECT_TIME"),
            manageHolidays = permissions.Contains("MANAGE_HOLIDAYS"),
            viewHolidays = permissions.Contains("VIEW_HOLIDAYS"),
            viewProjectIntake = permissions.Contains("VIEW_PROJECT_INTAKE"),
            viewResourceScheduling = permissions.Contains("VIEW_RESOURCE_SCHEDULING"),
            viewExpenses = permissions.Contains("VIEW_EXPENSES"),
            viewExecutiveReporting = permissions.Contains("VIEW_EXECUTIVE_REPORTING"),
            viewAuditTrail = permissions.Contains("VIEW_AUDIT_TRAIL"),
            exportTimePdf = permissions.Contains("EXPORT_TIME_PDF"),
            exportTimeExcel = permissions.Contains("EXPORT_TIME_EXCEL"),
            systemAdministration = permissions.Contains("SYSTEM_ADMINISTRATION"),
            manageAll = permissions.Contains("MANAGE_ALL")
        }
    };
}

'''

if 'static async Task<object> BuildSecurityContextAsync' not in api:
    api = api.replace('static IResult? ValidateConfig(DatabaseConfig config)', helpers + 'static IResult? ValidateConfig(DatabaseConfig config)', 1)

api_file.write_text(api)
PY

echo "==> Role security API patch applied"
echo "==> Expected API version after redeploy: 0.5.6"

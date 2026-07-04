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

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.7"', api)
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')

endpoints = r'''
app.MapGet("/api/admin/roles", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT role_code, role_name, role_description, display_order
        FROM app_roles
        WHERE is_active = TRUE
        ORDER BY display_order, role_name;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(new
        {
            roleCode = reader.GetString(0),
            roleName = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            displayOrder = reader.GetInt32(3)
        });
    }

    return Results.Ok(new { count = roles.Count, roles });
});

app.MapGet("/api/admin/users", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var users = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            u.user_id,
            u.email,
            u.display_name,
            COALESCE(u.job_title, '') AS job_title,
            COALESCE(u.department, '') AS department,
            u.is_active,
            COALESCE(array_agg(r.role_code ORDER BY r.display_order) FILTER (WHERE r.role_code IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_codes,
            COALESCE(array_agg(r.role_name ORDER BY r.display_order) FILTER (WHERE r.role_name IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_names
        FROM app_users u
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        GROUP BY u.user_id, u.email, u.display_name, u.job_title, u.department, u.is_active
        ORDER BY u.display_name, u.email;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(new
        {
            userId = reader.GetGuid(0),
            email = reader.GetString(1),
            displayName = reader.GetString(2),
            jobTitle = reader.GetString(3),
            department = reader.GetString(4),
            isActive = reader.GetBoolean(5),
            roleCodes = reader.GetFieldValue<string[]>(6),
            roleNames = reader.GetFieldValue<string[]>(7)
        });
    }

    return Results.Ok(new { count = users.Count, users });
});

app.MapPost("/api/admin/users/roles", async (UserRoleAssignmentRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Email is required." });
    }

    var roleCodes = request.RoleCodes?.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim().ToUpperInvariant()).Distinct().ToArray() ?? Array.Empty<string>();
    if (roleCodes.Length == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "At least one role code is required." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var adminUserId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        Guid targetUserId;

        await using (var userCommand = new NpgsqlCommand("SELECT user_id FROM app_users WHERE lower(email) = lower(@email);", connection, transaction))
        {
            userCommand.Parameters.AddWithValue("email", request.Email.Trim());
            var result = await userCommand.ExecuteScalarAsync();
            if (result is null)
            {
                return Results.NotFound(new { status = "not_found", message = $"No user found for {request.Email}." });
            }
            targetUserId = (Guid)result;
        }

        await using (var deactivateCommand = new NpgsqlCommand("""
            UPDATE app_user_role_assignments
            SET is_active = FALSE,
                updated_at = NOW()
            WHERE user_id = @user_id;
            """, connection, transaction))
        {
            deactivateCommand.Parameters.AddWithValue("user_id", targetUserId);
            await deactivateCommand.ExecuteNonQueryAsync();
        }

        foreach (var roleCode in roleCodes)
        {
            await using var assignCommand = new NpgsqlCommand("""
                INSERT INTO app_user_role_assignments (user_id, app_role_id, assigned_by_user_id, assignment_reason, is_active)
                SELECT @user_id, app_role_id, @assigned_by_user_id, @assignment_reason, TRUE
                FROM app_roles
                WHERE role_code = @role_code
                  AND is_active = TRUE
                ON CONFLICT (user_id, app_role_id) DO UPDATE
                SET is_active = TRUE,
                    assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                    assignment_reason = EXCLUDED.assignment_reason,
                    updated_at = NOW();
                """, connection, transaction);
            assignCommand.Parameters.AddWithValue("user_id", targetUserId);
            assignCommand.Parameters.AddWithValue("assigned_by_user_id", adminUserId);
            assignCommand.Parameters.AddWithValue("assignment_reason", string.IsNullOrWhiteSpace(request.Reason) ? "Role updated from Project Health Dashboard role administration" : request.Reason.Trim());
            assignCommand.Parameters.AddWithValue("role_code", roleCode);
            await assignCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return Results.Ok(new { status = "roles_updated", email = request.Email.Trim(), roleCodes });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(title: "Failed to update user roles", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});

'''

if 'app.MapGet("/api/admin/roles"' not in api:
    api = api.replace('\napp.Run();', '\n' + endpoints + 'app.Run();', 1)

if 'internal sealed record UserRoleAssignmentRequest' not in api:
    api = api.replace('internal sealed record TimesheetSaveRequest', 'internal sealed record UserRoleAssignmentRequest(string Email, List<string>? RoleCodes, string? Reason);\n\ninternal sealed record TimesheetSaveRequest', 1)

api_file.write_text(api)
PY

echo "==> Role administration API patch applied"
echo "==> Expected API version after redeploy: 0.5.7"

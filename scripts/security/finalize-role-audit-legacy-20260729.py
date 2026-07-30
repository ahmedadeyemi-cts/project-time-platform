#!/usr/bin/env python3
from pathlib import Path

root = Path(__file__).resolve().parents[2]
path = root / "src/backend/ProjectTime.Api/Program.cs"
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    text = text.replace(old, new, 1)


if "SECURITY_20260729_TRANSACTIONAL_ROLE_AUDIT_HELPERS" not in text:
    replace_once(
        '''async Task InsertProjectPulseAuditEventAsync(
    NpgsqlConnection connection,''',
        '''async Task<Dictionary<Guid, string[]>> LoadProjectPulseActiveRoleCodesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    IEnumerable<Guid> userIds)
{
    var normalizedUserIds = userIds.Distinct().ToArray();
    var rolesByUser = normalizedUserIds.ToDictionary(userId => userId, _ => new List<string>());

    if (normalizedUserIds.Length == 0)
    {
        return new Dictionary<Guid, string[]>();
    }

    await using var command = new NpgsqlCommand("""
        SELECT ura.user_id, r.role_code
        FROM app_user_role_assignments ura
        JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE ura.user_id = ANY(@user_ids)
          AND ura.is_active = TRUE
        ORDER BY ura.user_id, r.display_order, r.role_code;
        """, connection, transaction);
    command.Parameters.AddWithValue("user_ids", normalizedUserIds);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rolesByUser[reader.GetGuid(0)].Add(reader.GetString(1));
    }

    return rolesByUser.ToDictionary(
        pair => pair.Key,
        pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(roleCode => roleCode, StringComparer.OrdinalIgnoreCase)
            .ToArray());
}

async Task InsertProjectPulseRoleAuditAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid actorUserId,
    Guid targetUserId,
    string action,
    string reason,
    IEnumerable<string> oldRoleCodes,
    IEnumerable<string> newRoleCodes,
    HttpContext httpContext)
{
    var oldValue = JsonSerializer.Serialize(new
    {
        roleCodes = oldRoleCodes.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(roleCode => roleCode, StringComparer.OrdinalIgnoreCase).ToArray()
    });
    var newValue = JsonSerializer.Serialize(new
    {
        roleCodes = newRoleCodes.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(roleCode => roleCode, StringComparer.OrdinalIgnoreCase).ToArray(),
        reason,
        route = httpContext.Request.Path.Value ?? string.Empty
    });

    await using var command = new NpgsqlCommand("""
        INSERT INTO audit_logs (
            actor_user_id, action, entity_type, entity_id,
            old_value, new_value, ip_address, user_agent
        )
        VALUES (
            @actor_user_id, @action, 'app_user_roles', @entity_id,
            CAST(@old_value AS jsonb), CAST(@new_value AS jsonb),
            NULLIF(@ip_address, '')::inet, @user_agent
        );
        """, connection, transaction);
    command.Parameters.AddWithValue("actor_user_id", actorUserId);
    command.Parameters.AddWithValue("action", action);
    command.Parameters.AddWithValue("entity_id", targetUserId);
    command.Parameters.AddWithValue("old_value", oldValue);
    command.Parameters.AddWithValue("new_value", newValue);
    command.Parameters.AddWithValue("ip_address", httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
    command.Parameters.AddWithValue("user_agent", httpContext.Request.Headers.UserAgent.ToString());
    await command.ExecuteNonQueryAsync();
}

// SECURITY_20260729_TRANSACTIONAL_ROLE_AUDIT_HELPERS
async Task InsertProjectPulseAuditEventAsync(
    NpgsqlConnection connection,''',
        "role audit helpers",
    )

if "user_roles_updated_legacy" not in text:
    replace_once(
        '''            if (result is null)
            {
                return Results.NotFound(new { status = "not_found", message = $"No user found for {request.Email}." });
            }
            targetUserId = (Guid)result;
        }

        await using (var deactivateCommand = new NpgsqlCommand("""''',
        '''            if (result is null)
            {
                await transaction.RollbackAsync();
                return Results.NotFound(new { status = "not_found", message = $"No user found for {request.Email}." });
            }
            targetUserId = (Guid)result;
        }

        var previousRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { targetUserId });
        var previousRoleCodes = previousRoleCodesByUser[targetUserId];
        var actorRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { adminUserId });
        var actorIsSuperAdministrator = actorRoleCodesByUser[adminUserId]
            .Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase);

        if (previousRoleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase)
            && !actorIsSuperAdministrator)
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "super_administrator_target_protected",
                message = "Only a Super Administrator can change roles for an existing Super Administrator."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        await using (var deactivateCommand = new NpgsqlCommand("""''',
        "legacy role pre-mutation audit state",
    )

    replace_once(
        '''        await transaction.CommitAsync();
        return Results.Ok(new { status = "roles_updated", email = request.Email.Trim(), roleCodes });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(title: "Failed to update user roles", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});



const int ProjectPulseSessionMinutes''',
        '''        var currentRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { targetUserId });
        await InsertProjectPulseRoleAuditAsync(
            connection,
            transaction,
            adminUserId,
            targetUserId,
            "user_roles_updated_legacy",
            string.IsNullOrWhiteSpace(request.Reason)
                ? "Role updated from Project Pulse role administration"
                : request.Reason.Trim(),
            previousRoleCodes,
            currentRoleCodesByUser[targetUserId],
            httpContext);

        await transaction.CommitAsync();
        return Results.Ok(new { status = "roles_updated", email = request.Email.Trim(), roleCodes });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(title: "Failed to update user roles", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});



const int ProjectPulseSessionMinutes''',
        "legacy role audit insert",
    )

for marker in (
    "SECURITY_20260729_TRANSACTIONAL_ROLE_AUDIT_HELPERS",
    "LoadProjectPulseActiveRoleCodesAsync",
    "InsertProjectPulseRoleAuditAsync",
    "user_roles_updated_legacy",
    "CAST(@old_value AS jsonb)",
):
    if marker not in text:
        raise RuntimeError(f"missing marker: {marker}")

path.write_text(text, encoding="utf-8")
print("SECURITY_LEGACY_ROLE_AUDIT_FINALIZER=PASSED")

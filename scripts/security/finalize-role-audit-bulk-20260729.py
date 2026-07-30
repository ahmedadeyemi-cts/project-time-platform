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


if "user_roles_bulk_updated" not in text:
    replace_once(
        """    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "User Administration is restricted to administrators and project/team coordinators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);

    if (sessionUserId is not null && userIds.Contains(sessionUserId.Value))""",
        """    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "admin_required",
            message = "Bulk role updates are restricted to Administrators and Super Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (userIds.Contains(sessionUserId.Value))""",
        "bulk Administrator boundary",
    )

    replace_once(
        """    try
    {
        await using (var profileCommand = new NpgsqlCommand("""""",
        """    try
    {
        var previousRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, userIds);
        var actorRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { sessionUserId.Value });
        var actorIsSuperAdministrator = actorRoleCodesByUser[sessionUserId.Value]
            .Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase);

        if (!actorIsSuperAdministrator
            && previousRoleCodesByUser.Values.Any(roleCodes =>
                roleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase)))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "super_administrator_target_protected",
                message = "Only a Super Administrator can bulk-modify an existing Super Administrator."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!actorIsSuperAdministrator
            && cleanRoleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "super_administrator_required",
                message = "Only a Super Administrator can grant the Super Administrator role."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        await using (var profileCommand = new NpgsqlCommand("""""",
        "bulk old-state and Super Administrator protection",
    )

    replace_once(
        """        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "bulk_user_update_completed",""",
        """        if (roleMode != "none")
        {
            var currentRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
                connection, transaction, userIds);

            foreach (var userId in userIds)
            {
                await InsertProjectPulseRoleAuditAsync(
                    connection,
                    transaction,
                    sessionUserId.Value,
                    userId,
                    "user_roles_bulk_updated",
                    string.IsNullOrWhiteSpace(request.Reason)
                        ? "Bulk update from User Administration"
                        : request.Reason.Trim(),
                    previousRoleCodesByUser[userId],
                    currentRoleCodesByUser[userId],
                    httpContext);
            }
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "bulk_user_update_completed",""",
        "bulk transactional role audit",
    )

for marker in (
    "Bulk role updates are restricted to Administrators and Super Administrators.",
    "Only a Super Administrator can bulk-modify an existing Super Administrator.",
    "user_roles_bulk_updated",
):
    if marker not in text:
        raise RuntimeError(f"missing marker: {marker}")

path.write_text(text, encoding="utf-8")
print("SECURITY_BULK_ROLE_AUDIT_FINALIZER=PASSED")

#!/usr/bin/env python3
from pathlib import Path

root = Path(__file__).resolve().parents[2]
path = root / "src/backend/ProjectTime.Api/Program.cs"
text = path.read_text(encoding="utf-8")


def replace_in_section(start: str, end: str, old: str, new: str, label: str) -> None:
    global text
    start_index = text.index(start)
    end_index = text.index(end, start_index)
    section = text[start_index:end_index]
    count = section.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one section match, found {count}")
    section = section.replace(old, new, 1)
    text = text[:start_index] + section + text[end_index:]


single_start = 'app.MapPost("/api/admin/user-admin/users/roles"'
single_end = 'app.MapPost("/api/admin/user-admin/local-password"'
local_start = 'app.MapPost("/api/admin/user-admin/users/local"'
local_end = 'app.MapPost("/api/admin/user-admin/users/deactivate"'

if '''"user_roles_updated",
            string.IsNullOrWhiteSpace(request.Reason)''' not in text:
    replace_in_section(
        single_start,
        single_end,
        '''        if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        var cleanRoleCodes = (request.RoleCodes ?? new List<string>())''',
        '''        if (!await RequestUserIsAdministratorAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "admin_required",
                message = "Role assignment is restricted to Administrators and Super Administrators."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var cleanRoleCodes = (request.RoleCodes ?? new List<string>())''',
        "single role Administrator boundary",
    )

    replace_in_section(
        single_start,
        single_end,
        '''            .Distinct()
            .ToList();

        if (sessionUserId == request.UserId && !(cleanRoleCodes.Contains("SUPER_ADMINISTRATOR") || cleanRoleCodes.Contains("ADMINISTRATOR")))''',
        '''            .Distinct()
            .ToList();

        var previousRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { request.UserId });
        var previousRoleCodes = previousRoleCodesByUser[request.UserId];
        var actorRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { sessionUserId.Value });
        var actorIsSuperAdministrator = actorRoleCodesByUser[sessionUserId.Value]
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

        if (cleanRoleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase)
            && !actorIsSuperAdministrator)
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "super_administrator_required",
                message = "Only a Super Administrator can grant the Super Administrator role."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (sessionUserId == request.UserId && !(cleanRoleCodes.Contains("SUPER_ADMINISTRATOR") || cleanRoleCodes.Contains("ADMINISTRATOR")))''',
        "single role old-state and Super Administrator protection",
    )

    replace_in_section(
        single_start,
        single_end,
        '''        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "user_roles_updated",
            roleCodes = cleanRoleCodes,''',
        '''        var currentRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { request.UserId });
        await InsertProjectPulseRoleAuditAsync(
            connection,
            transaction,
            sessionUserId.Value,
            request.UserId,
            "user_roles_updated",
            string.IsNullOrWhiteSpace(request.Reason) ? "Updated from User Administration" : request.Reason.Trim(),
            previousRoleCodes,
            currentRoleCodesByUser[request.UserId],
            httpContext);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "user_roles_updated",
            roleCodes = cleanRoleCodes,''',
        "single role transactional audit",
    )

if "local_user_created_with_roles" not in text:
    replace_in_section(
        local_start,
        local_end,
        '''        if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)''',
        '''        if (!await RequestUserIsAdministratorAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "admin_required",
                message = "Local user creation is restricted to Administrators and Super Administrators."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)''',
        "local user Administrator boundary",
    )

    replace_in_section(
        local_start,
        local_end,
        '''        if (cleanRoleCodes.Count == 0)
        {
            cleanRoleCodes.Add("ENGINEERING");
        }

        await using (var userCommand = new NpgsqlCommand("""''',
        '''        if (cleanRoleCodes.Count == 0)
        {
            cleanRoleCodes.Add("ENGINEERING");
        }

        var actorRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { sessionUserId.Value });
        var actorIsSuperAdministrator = actorRoleCodesByUser[sessionUserId.Value]
            .Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase);

        if (cleanRoleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase)
            && !actorIsSuperAdministrator)
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "super_administrator_required",
                message = "Only a Super Administrator can create another Super Administrator."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        await using (var userCommand = new NpgsqlCommand("""''',
        "local user Super Administrator parity",
    )

    replace_in_section(
        local_start,
        local_end,
        '''        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "local_user_created",
            userId,
            email,''',
        '''        var createdRoleCodesByUser = await LoadProjectPulseActiveRoleCodesAsync(
            connection, transaction, new[] { userId });
        await InsertProjectPulseRoleAuditAsync(
            connection,
            transaction,
            sessionUserId.Value,
            userId,
            "local_user_created_with_roles",
            "Created from User Administration local user workflow.",
            Array.Empty<string>(),
            createdRoleCodesByUser[userId],
            httpContext);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "local_user_created",
            userId,
            email,''',
        "local user transactional role audit",
    )

for marker in (
    "Role assignment is restricted to Administrators and Super Administrators.",
    "local_user_created_with_roles",
    "Only a Super Administrator can create another Super Administrator.",
):
    if marker not in text:
        raise RuntimeError(f"missing marker: {marker}")

path.write_text(text, encoding="utf-8")
print("SECURITY_USER_ADMIN_ROLE_AUDIT_FINALIZER=PASSED")

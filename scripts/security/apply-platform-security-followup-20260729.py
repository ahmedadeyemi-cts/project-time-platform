#!/usr/bin/env python3
"""Apply the follow-up security boundary corrections identified during review.

This patch is intentionally scoped to the already-added SecurityHardeningModule.
It closes fail-open request inspection, protects existing Super Administrators from
lower-privileged role mutation, and enforces parent/document object scope.
"""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SECURITY_MODULE = ROOT / "src/backend/ProjectTime.Api/Modules/SecurityHardeningModule.cs"


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one exact match, found {count}")
    return text.replace(old, new, 1)


def replace_regex_once(text: str, pattern: str, replacement: str, label: str) -> str:
    updated, count = re.subn(pattern, replacement, text, count=1, flags=re.DOTALL)
    if count != 1:
        raise RuntimeError(f"{label}: expected one regex match, found {count}")
    return updated


text = SECURITY_MODULE.read_text(encoding="utf-8")

if "SECURITY_20260729_FOLLOWUP_COMPLETE" in text:
    print("SECURITY_FOLLOWUP_ALREADY_APPLIED=YES")
    raise SystemExit(0)

text = replace_once(
    text,
    '''    private static readonly Regex IntakeDocumentDownloadPath = new(
        @"^/api/project-intake/documents/(?<id>[0-9a-fA-F-]{36})/download$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
''',
    '''    private static readonly Regex IntakeDocumentDownloadPath = new(
        @"^/api/project-intake/documents/(?<id>[0-9a-fA-F-]{36})/download$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IntakeRequestMutationPath = new(
        @"^/api/project-intake/(?:requests/)?(?<id>[0-9a-fA-F-]{36})/(?:documents|supporting-documents/upload|post-intake|project-link)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
''',
    "intake mutation route matcher",
)

text = replace_regex_once(
    text,
    r'''        JsonDocument\? inspectedPayload = null;\n\n        if \(IsUnsafeMethod\(method\).*?\n        NpgsqlConnection\? authorizationConnection = null;''',
    '''        var sensitivePayloadRequired =
            IsRoleMutationPath(path, method)
            || IsBreakGlassPasswordPath(path, method);

        JsonDocument? inspectedPayload = null;

        if (sensitivePayloadRequired && !IsJsonRequest(context.Request))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "json_body_required",
                "This security-sensitive action requires an application/json request body.");
            return;
        }

        if (IsUnsafeMethod(method) && IsJsonRequest(context.Request))
        {
            if (context.Request.ContentLength is long contentLength
                && contentLength > MaximumInspectedJsonBytes)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "request_body_too_large",
                    "The request body exceeds the security inspection limit.");
                return;
            }

            var inspection = await InspectJsonBodyAsync(context);

            if (inspection.TooLarge)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "request_body_too_large",
                    "The request body exceeds the security inspection limit.");
                return;
            }

            if (inspection.Malformed)
            {
                if (sensitivePayloadRequired)
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "invalid_json_body",
                        "A valid JSON request body is required for this security-sensitive action.");
                    return;
                }
            }
            else
            {
                inspectedPayload = inspection.Document;

                if (inspectedPayload is not null
                    && !await ValidateJsonSafetyAsync(context, path, inspectedPayload.RootElement))
                {
                    inspectedPayload.Dispose();
                    return;
                }
            }
        }

        if (sensitivePayloadRequired && inspectedPayload is null)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "security_payload_unavailable",
                "The request body could not be inspected and the action was denied.");
            return;
        }

        NpgsqlConnection? authorizationConnection = null;''',
    "fail-closed JSON inspection",
)

text = replace_regex_once(
    text,
    r'''    private static async Task<JsonDocument\?> TryReadJsonBodyAsync\(HttpContext context\).*?\n    private static async Task<bool> ValidateJsonSafetyAsync\(''',
    '''    private static async Task<JsonBodyInspection> InspectJsonBodyAsync(HttpContext context)
    {
        try
        {
            context.Request.EnableBuffering(
                bufferThreshold: 64 * 1024,
                bufferLimit: MaximumInspectedJsonBytes);
            context.Request.Body.Position = 0;
            var document = await JsonDocument.ParseAsync(context.Request.Body);
            context.Request.Body.Position = 0;
            return new JsonBodyInspection(document, TooLarge: false, Malformed: false);
        }
        catch (IOException)
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            return new JsonBodyInspection(null, TooLarge: true, Malformed: false);
        }
        catch (JsonException)
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            return new JsonBodyInspection(null, TooLarge: false, Malformed: true);
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        value = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static async Task<string> TryReadJsonStringAsync(HttpContext context, string propertyName)
    {
        var inspection = await InspectJsonBodyAsync(context);
        var document = inspection.Document;

        if (document is null)
        {
            return string.Empty;
        }

        using (document)
        {
            return TryGetPropertyIgnoreCase(document.RootElement, propertyName, out var property)
                   && property.ValueKind == JsonValueKind.String
                ? property.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
    }

    private static async Task<bool> ValidateJsonSafetyAsync(''',
    "bounded JSON reader and case-insensitive properties",
)

text = replace_once(
    text,
    '''            if (path.Contains("/assign", StringComparison.OrdinalIgnoreCase))
            {
                return SecurityPolicy.ProjectAssignment;
            }
''',
    '''            if (path.Contains("/assign", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/project-intake/resource-assignment-promotions", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/project-link", StringComparison.OrdinalIgnoreCase))
            {
                return SecurityPolicy.ProjectAssignment;
            }
''',
    "assignment policy routes",
)

text = replace_regex_once(
    text,
    r'''    private static async Task<bool> ValidateRoleMutationAsync\(.*?\n    private static HashSet<string> ReadRoleCodes\(JsonElement payload\)''',
    '''    private static async Task<bool> ValidateRoleMutationAsync(
        HttpContext context,
        NpgsqlConnection connection,
        Guid actorUserId,
        AccessContext access,
        JsonElement payload)
    {
        var roleCodes = ReadRoleCodes(payload);

        if (roleCodes.Count > 0)
        {
            var knownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using (var command = new NpgsqlCommand("""
                SELECT role_code
                FROM app_roles
                WHERE is_active = TRUE
                  AND role_code = ANY(@role_codes);
                """, connection))
            {
                command.Parameters.AddWithValue("role_codes", roleCodes.ToArray());

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    knownCodes.Add(reader.GetString(0));
                }
            }

            var unknownCodes = roleCodes.Where(code => !knownCodes.Contains(code)).ToArray();

            if (unknownCodes.Length > 0)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "unknown_role_code",
                    $"Unknown or inactive role code(s): {string.Join(", ", unknownCodes)}.");
                return false;
            }
        }

        var targetUserIds = ReadTargetUserIds(payload);
        var targetEmail = ReadString(payload, "email");

        if (!access.IsSuperAdministrator
            && await TargetsExistingSuperAdministratorAsync(
                connection,
                targetUserIds,
                targetEmail))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "super_administrator_target_protected",
                "Only a Super Administrator can change roles for an existing Super Administrator.");
            return false;
        }

        if (roleCodes.Contains("SUPER_ADMINISTRATOR") && !access.IsSuperAdministrator)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "super_administrator_assignment_forbidden",
                "Only a Super Administrator can grant or preserve the Super Administrator role.");
            return false;
        }

        var targetUserId = ReadGuid(payload, "userId");
        if (targetUserId == actorUserId
            && roleCodes.Contains("SUPER_ADMINISTRATOR")
            && !access.IsSuperAdministrator)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "self_elevation_forbidden",
                "Self-elevation to Super Administrator is forbidden.");
            return false;
        }

        return true;
    }

    private static async Task<bool> TargetsExistingSuperAdministratorAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<Guid> targetUserIds,
        string targetEmail)
    {
        if (targetUserIds.Count == 0 && string.IsNullOrWhiteSpace(targetEmail))
        {
            return false;
        }

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_users u
                JOIN app_user_role_assignments ura
                  ON ura.user_id = u.user_id
                 AND ura.is_active = TRUE
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                WHERE r.role_code = 'SUPER_ADMINISTRATOR'
                  AND (
                        (cardinality(@target_user_ids) > 0 AND u.user_id = ANY(@target_user_ids))
                     OR (NULLIF(@target_email, '') IS NOT NULL AND lower(u.email) = lower(@target_email))
                  )
            );
            """, connection);

        command.Parameters.AddWithValue(
            "target_user_ids",
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid,
            targetUserIds.ToArray());
        command.Parameters.AddWithValue("target_email", targetEmail ?? string.Empty);

        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static HashSet<string> ReadRoleCodes(JsonElement payload)''',
    "super administrator target protection",
)

text = replace_regex_once(
    text,
    r'''    private static HashSet<string> ReadRoleCodes\(JsonElement payload\).*?\n    private static async Task<bool> ValidateBreakGlassPasswordMutationAsync\(''',
    '''    private static HashSet<string> ReadRoleCodes(JsonElement payload)
    {
        var roleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetPropertyIgnoreCase(payload, "roleCodes", out var roleElement)
            || roleElement.ValueKind != JsonValueKind.Array)
        {
            return roleCodes;
        }

        foreach (var item in roleElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var code = item.GetString()?.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(code))
            {
                roleCodes.Add(code);
            }
        }

        return roleCodes;
    }

    private static HashSet<Guid> ReadTargetUserIds(JsonElement payload)
    {
        var userIds = new HashSet<Guid>();
        var singleUserId = ReadGuid(payload, "userId");

        if (singleUserId is not null)
        {
            userIds.Add(singleUserId.Value);
        }

        if (TryGetPropertyIgnoreCase(payload, "userIds", out var userIdsElement)
            && userIdsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in userIdsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String
                    && Guid.TryParse(item.GetString(), out var parsed))
                {
                    userIds.Add(parsed);
                }
            }
        }

        return userIds;
    }

    private static Guid? ReadGuid(JsonElement payload, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(payload, propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ReadString(JsonElement payload, string propertyName)
    {
        return TryGetPropertyIgnoreCase(payload, propertyName, out var element)
               && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static async Task<bool> ValidateBreakGlassPasswordMutationAsync(''',
    "case-insensitive role payload parsing",
)

text = replace_regex_once(
    text,
    r'''    private static async Task<bool> ValidateObjectScopeAsync\(.*?\n    private static async Task<bool> CanAccessProjectAsync\(''',
    '''    private static async Task<bool> ValidateObjectScopeAsync(
        HttpContext context,
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        string path)
    {
        var detailsMatch = WorkRegisterDetailsPath.Match(path);

        if (detailsMatch.Success
            && Guid.TryParse(detailsMatch.Groups["id"].Value, out var projectId)
            && !await CanAccessProjectAsync(connection, access, actorUserId, projectId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "project_access_denied",
                "You do not have access to this project.");
            return false;
        }

        var documentMatch = WorkRegisterDocumentDownloadPath.Match(path);

        if (documentMatch.Success
            && Guid.TryParse(documentMatch.Groups["id"].Value, out var documentId)
            && !await CanAccessWorkRegisterDocumentAsync(
                connection,
                access,
                actorUserId,
                documentId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "document_access_denied",
                "The requested project document was not found or is outside your document visibility scope.");
            return false;
        }

        var intakeMutationMatch = IntakeRequestMutationPath.Match(path);

        if (intakeMutationMatch.Success
            && Guid.TryParse(intakeMutationMatch.Groups["id"].Value, out var intakeRequestId)
            && !await CanAccessIntakeRequestAsync(
                connection,
                access,
                actorUserId,
                intakeRequestId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "intake_access_denied",
                "The requested Project Intake record was not found or is outside your role scope.");
            return false;
        }

        var intakeMatch = IntakeDocumentDownloadPath.Match(path);

        if (intakeMatch.Success
            && Guid.TryParse(intakeMatch.Groups["id"].Value, out var intakeDocumentId)
            && !await CanAccessIntakeDocumentAsync(
                connection,
                access,
                actorUserId,
                intakeDocumentId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "document_access_denied",
                "The requested intake document was not found or is outside your role scope.");
            return false;
        }

        return true;
    }

    private static async Task<bool> CanAccessProjectAsync(''',
    "object-scope dispatch",
)

text = replace_regex_once(
    text,
    r'''    private static async Task<bool> CanAccessProjectAsync\(.*?\n    private static async Task<bool> CanAccessIntakeDocumentAsync\(''',
    '''    private static async Task<bool> CanAccessProjectAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid projectId)
    {
        if (access.HasOrganizationProjectScope)
        {
            return true;
        }

        return await IsProjectManagerAsync(connection, actorUserId, projectId)
               || await IsAssignedToProjectAsync(connection, actorUserId, projectId);
    }

    private static async Task<bool> CanAccessWorkRegisterDocumentAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid documentId)
    {
        Guid projectId;
        string visibility;

        await using (var command = new NpgsqlCommand("""
            SELECT
                project_id,
                lower(COALESCE(NULLIF(visibility, ''), 'project_team'))
            FROM work_register_documents
            WHERE work_register_document_id = @document_id
              AND status = 'active'
            LIMIT 1;
            """, connection))
        {
            command.Parameters.AddWithValue("document_id", documentId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return false;
            }

            projectId = reader.GetGuid(0);
            visibility = reader.GetString(1);
        }

        if (access.CanManageAllWorkRegisterDocuments)
        {
            return true;
        }

        var isProjectManager = await IsProjectManagerAsync(connection, actorUserId, projectId);

        if (isProjectManager
            && visibility is "project_team" or "pm_ptc_admin" or "engineering_team")
        {
            return true;
        }

        var isAssigned = await IsAssignedToProjectAsync(connection, actorUserId, projectId);

        return isAssigned
               && visibility is "project_team" or "engineering_team";
    }

    private static async Task<bool> IsProjectManagerAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        Guid projectId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM projects
                WHERE project_id = @project_id
                  AND project_manager_user_id = @user_id
            );
            """, connection);

        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", actorUserId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> IsAssignedToProjectAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        Guid projectId)
    {
        if (await TableExistsAsync(connection, "project_assignments"))
        {
            await using var projectAssignmentCommand = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM project_assignments
                    WHERE project_id = @project_id
                      AND user_id = @user_id
                );
                """, connection);

            projectAssignmentCommand.Parameters.AddWithValue("project_id", projectId);
            projectAssignmentCommand.Parameters.AddWithValue("user_id", actorUserId);

            if (Convert.ToBoolean(await projectAssignmentCommand.ExecuteScalarAsync() ?? false))
            {
                return true;
            }
        }

        if (await TableExistsAsync(connection, "work_register_task_assignment_history"))
        {
            await using var assignmentCommand = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM work_register_task_assignment_history
                    WHERE project_id = @project_id
                      AND assigned_user_id = @user_id
                      AND assignment_status = 'active'
                      AND effective_end_date IS NULL
                );
                """, connection);

            assignmentCommand.Parameters.AddWithValue("project_id", projectId);
            assignmentCommand.Parameters.AddWithValue("user_id", actorUserId);

            if (Convert.ToBoolean(await assignmentCommand.ExecuteScalarAsync() ?? false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CanAccessIntakeDocumentAsync(''',
    "document visibility enforcement",
)

text = replace_regex_once(
    text,
    r'''    private static async Task<bool> CanAccessIntakeDocumentAsync\(.*?\n    private static async Task<bool> TableExistsAsync\(''',
    '''    private static async Task<bool> CanAccessIntakeRequestAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid requestId)
    {
        if (access.HasOrganizationIntakeScope)
        {
            return true;
        }

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM project_intake_requests r
                WHERE r.project_intake_request_id = @request_id
                  AND (
                        r.requested_by_user_id = @user_id
                     OR r.assigned_pm_user_id = @user_id
                     OR r.account_executive_user_id = @user_id
                     OR r.solution_architect_user_id = @user_id
                  )
            );
            """, connection);

        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("user_id", actorUserId);

        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> CanAccessIntakeDocumentAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid documentId)
    {
        if (access.HasOrganizationIntakeScope)
        {
            return true;
        }

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM project_intake_documents d
                JOIN project_intake_requests r
                  ON r.project_intake_request_id = d.project_intake_request_id
                WHERE d.project_intake_document_id = @document_id
                  AND COALESCE(d.is_active, TRUE) = TRUE
                  AND (
                        r.requested_by_user_id = @user_id
                     OR r.assigned_pm_user_id = @user_id
                     OR r.account_executive_user_id = @user_id
                     OR r.solution_architect_user_id = @user_id
                     OR (
                            COALESCE(d.engineering_visible, FALSE) = TRUE
                        AND d.project_id IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM project_assignments pa
                            WHERE pa.project_id = d.project_id
                              AND pa.user_id = @user_id
                        )
                     )
                  )
            );
            """, connection);

        command.Parameters.AddWithValue("document_id", documentId);
        command.Parameters.AddWithValue("user_id", actorUserId);

        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> TableExistsAsync(''',
    "intake parent and document scope",
)

text = replace_once(
    text,
    '''        public bool HasOrganizationProjectScope =>
            IsAdministrator''',
    '''        public bool CanManageAllWorkRegisterDocuments =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]
            {
                "MANAGE_WORK_REGISTER",
                "MANAGE_PROJECT_DOCUMENTS",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool HasOrganizationProjectScope =>
            IsAdministrator''',
    "work register document management capability",
)

text = replace_once(
    text,
    '''    private enum SecurityPolicy
    {''',
    '''    private sealed record JsonBodyInspection(
        JsonDocument? Document,
        bool TooLarge,
        bool Malformed);

    private enum SecurityPolicy
    {''',
    "JSON inspection result type",
)

text = text.replace(
    '''                "security_configuration_unavailable",
                ex.Message);''',
    '''                "security_configuration_unavailable",
                "Security authorization could not be evaluated. The action was denied.");''',
    1,
)

text = text.replace(
    '''    }
}
''',
    '''    }

    // SECURITY_20260729_FOLLOWUP_COMPLETE
}
''',
    1,
)

required = (
    "SECURITY_20260729_FOLLOWUP_COMPLETE",
    "sensitivePayloadRequired",
    "StatusCodes.Status413PayloadTooLarge",
    "TryGetPropertyIgnoreCase",
    "TargetsExistingSuperAdministratorAsync",
    "super_administrator_target_protected",
    "IntakeRequestMutationPath",
    "CanAccessIntakeRequestAsync",
    "CanAccessWorkRegisterDocumentAsync",
    "CanManageAllWorkRegisterDocuments",
    "resource-assignment-promotions",
)

for marker in required:
    if marker not in text:
        raise RuntimeError(f"Required follow-up marker is missing: {marker}")

for forbidden in (
    "private static async Task<JsonDocument?> TryReadJsonBodyAsync",
    "if (roleCodes.Count == 0)\n        {\n            return true;",
    "The requested document was not found."
):
    if forbidden in text:
        raise RuntimeError(f"A fail-open or superseded pattern remains: {forbidden}")

SECURITY_MODULE.write_text(text, encoding="utf-8")
print("SECURITY_FOLLOWUP_SOURCE_APPLY=PASSED")

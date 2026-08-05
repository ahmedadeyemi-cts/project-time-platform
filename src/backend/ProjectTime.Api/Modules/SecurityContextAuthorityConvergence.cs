using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Converges the legacy /api/security/me payload with the platform's permanent
/// actual-session administrator authority. The existing endpoint remains the
/// source for effective-user roles, features, and permissions. This middleware
/// adds non-transferable actual-session authority evidence after the endpoint
/// completes so Module 011, Module 065, and every route consumer make the same
/// decision as the Modules directory and server authorization helpers.
///
/// View-As always remains read-only and never receives permanent authority.
/// </summary>
internal static class SecurityContextAuthorityConvergence
{
    private static readonly string[] PermanentPermissionCodes =
    [
        "MANAGE_ALL",
        "SYSTEM_ADMINISTRATION"
    ];

    internal static IApplicationBuilder UseSecurityContextAuthorityConvergence(
        this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method)
                || !context.Request.Path.Equals(
                    "/api/security/me",
                    StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context);
            var permanentFullControl = false;
            try
            {
                permanentFullControl = await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                        context,
                        cancellationToken: context.RequestAborted);
            }
            catch
            {
                // Preserve the existing security endpoint if the compatibility
                // lookup is temporarily unavailable. The returned role evidence
                // is evaluated again after the endpoint completes.
            }

            var originalBody = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await next();
                buffer.Position = 0;
                context.Response.Body = originalBody;

                if (context.Response.StatusCode < 200
                    || context.Response.StatusCode >= 300
                    || !IsJsonResponse(context.Response.ContentType))
                {
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                JsonNode? parsed;
                try
                {
                    parsed = await JsonNode.ParseAsync(
                        buffer,
                        cancellationToken: context.RequestAborted);
                }
                catch (JsonException)
                {
                    buffer.Position = 0;
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                if (parsed is not JsonObject payload)
                {
                    buffer.Position = 0;
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                isViewAs = isViewAs || ReadBoolean(payload["isViewAs"]);
                permanentFullControl = !isViewAs && (
                    permanentFullControl
                    || ReadBoolean(payload["permanentFullControl"])
                    || ContainsAdministratorRole(payload["roles"]));

                payload["isViewAs"] = isViewAs;
                payload["permanentFullControl"] = permanentFullControl;
                payload["authoritySource"] = permanentFullControl
                    ? "actual_session_super_administrator"
                    : isViewAs
                        ? "view_as_read_only"
                        : ReadText(payload["authoritySource"])
                            ?? "published_role_permissions";

                if (permanentFullControl)
                {
                    var permissions = payload["permissions"] as JsonArray ?? new JsonArray();
                    foreach (var permissionCode in PermanentPermissionCodes)
                        AddUniqueString(permissions, permissionCode);
                    payload["permissions"] = permissions;

                    var can = payload["can"] as JsonObject ?? new JsonObject();
                    can["systemAdministration"] = true;
                    can["manageAll"] = true;
                    payload["can"] = can;
                }

                var json = payload.ToJsonString(new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                context.Response.ContentLength = null;
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(json, context.RequestAborted);
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        });
    }

    private static bool IsJsonResponse(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType)
        || contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAdministratorRole(JsonNode? rolesNode)
    {
        if (rolesNode is not JsonArray roles) return false;
        foreach (var roleNode in roles)
        {
            var roleCode = roleNode?["roleCode"]?.GetValue<string?>()
                ?? roleNode?["roleName"]?.GetValue<string?>();
            if (ProjectPulseActualSessionAuthority.IsAdministratorRoleCode(roleCode))
                return true;
        }
        return false;
    }

    private static void AddUniqueString(JsonArray values, string value)
    {
        if (values.Any(item => string.Equals(
                ReadText(item),
                value,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        values.Add(value);
    }

    private static bool ReadBoolean(JsonNode? node)
    {
        try { return node?.GetValue<bool>() == true; }
        catch { return false; }
    }

    private static string? ReadText(JsonNode? node)
    {
        try
        {
            var value = node?.GetValue<string?>()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }
}

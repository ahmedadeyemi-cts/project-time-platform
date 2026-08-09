using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseNotificationTemplateRenderer
{
    private static readonly HashSet<string> ProhibitedTokenFragments = new(
        new[]
        {
            "secret",
            "password",
            "credential",
            "authorization",
            "accesstoken",
            "refreshtoken",
            "apikey",
            "privatekey",
            "connectionstring"
        },
        StringComparer.OrdinalIgnoreCase);

    internal static async Task<EnterpriseNotificationTemplate> RenderAsync(
        NpgsqlConnection connection,
        EnterpriseNotificationPolicyRow policy,
        EnterpriseNotificationEventRow notificationEvent,
        CancellationToken cancellationToken)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["policyCode"] = policy.PolicyCode,
            ["policyName"] = policy.PolicyName,
            ["eventCode"] = policy.EventCode,
            ["sourceModule"] = notificationEvent.SourceModule,
            ["status"] = EnterpriseNotificationRecipientResolver.PayloadString(notificationEvent.Payload, "status"),
            ["occurredAt"] = notificationEvent.OccurredAt.ToString("O"),
            ["eventId"] = notificationEvent.EventId.ToString(),
            ["correlationId"] = EnterpriseNotificationRecipientResolver.PayloadString(notificationEvent.Payload, "correlationId")
        };

        AddPayloadTokens(tokens, notificationEvent.Payload);
        await AddUserTokensAsync(
            connection,
            tokens,
            notificationEvent.SubjectUserId,
            cancellationToken);
        await AddProjectTokensAsync(
            connection,
            tokens,
            notificationEvent.ProjectId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(tokens.GetValueOrDefault("deepLink")))
            tokens["deepLink"] = DefaultDeepLink(policy.SourceModule);
        tokens["projectPulseUrl"] = ResolvePublicUrl(tokens.GetValueOrDefault("deepLink"));

        var subject = Replace(policy.SubjectTemplate, tokens);
        var textBody = Replace(policy.TextTemplate, tokens);
        if (string.IsNullOrWhiteSpace(subject))
            subject = $"Pulse: {policy.PolicyName}";
        if (string.IsNullOrWhiteSpace(textBody))
            textBody = $"A Pulse {policy.PolicyName} event occurred at {notificationEvent.OccurredAt:O}.";

        subject = Truncate(subject.Replace('\r', ' ').Replace('\n', ' ').Trim(), 240);
        textBody = Truncate(textBody.Trim(), 12000);
        var htmlBody = BuildHtml(policy, subject, textBody, tokens.GetValueOrDefault("projectPulseUrl"));
        return new(subject, textBody, htmlBody, tokens);
    }

    internal static bool PayloadContainsProhibitedMaterial(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return false;
        return ContainsProhibited(payload, string.Empty);
    }

    private static bool ContainsProhibited(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var normalized = NormalizeTokenName(property.Name);
                    if (ProhibitedTokenFragments.Any(fragment =>
                            normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                        return true;
                    if (ContainsProhibited(property.Value, $"{path}.{property.Name}")) return true;
                }
                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(item => ContainsProhibited(item, path));
            default:
                return false;
        }
    }

    private static void AddPayloadTokens(
        Dictionary<string, string> tokens,
        JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return;
        foreach (var property in payload.EnumerateObject())
        {
            var tokenName = property.Name.Trim();
            var normalized = NormalizeTokenName(tokenName);
            if (ProhibitedTokenFragments.Any(fragment =>
                    normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                continue;

            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(value)) tokens[tokenName] = Truncate(value.Trim(), 2000);
        }
    }

    private static async Task AddUserTokensAsync(
        NpgsqlConnection connection,
        Dictionary<string, string> tokens,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return;
        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(NULLIF(display_name, ''), email),
                lower(email),
                COALESCE(manager_email, '')
            FROM app_users
            WHERE user_id = @user_id
              AND is_active = TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return;
        var displayName = reader.GetString(0);
        tokens.TryAdd("displayName", displayName);
        tokens.TryAdd("engineerName", displayName);
        tokens.TryAdd("assigneeName", displayName);
        tokens.TryAdd("recipientName", displayName);
        tokens.TryAdd("userEmail", reader.GetString(1));
        tokens.TryAdd("managerEmail", reader.GetString(2));
    }

    private static async Task AddProjectTokensAsync(
        NpgsqlConnection connection,
        Dictionary<string, string> tokens,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (!projectId.HasValue) return;
        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(project_code, ''),
                COALESCE(project_name, ''),
                COALESCE(status, '')
            FROM projects
            WHERE project_id = @project_id;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return;
        tokens.TryAdd("projectCode", reader.GetString(0));
        tokens.TryAdd("projectName", reader.GetString(1));
        tokens.TryAdd("projectStatus", reader.GetString(2));
    }

    private static string Replace(
        string template,
        IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        return TokenPattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            return tokens.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : "Not available";
        });
    }

    private static string BuildHtml(
        EnterpriseNotificationPolicyRow policy,
        string subject,
        string textBody,
        string? projectPulseUrl)
    {
        var paragraphs = textBody
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph =>
                $"<p style=\"margin:0 0 14px;line-height:1.55;color:#253247\">{WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>")}</p>")
            .ToArray();
        var action = string.IsNullOrWhiteSpace(projectPulseUrl)
            ? string.Empty
            : $"""
              <p style="margin:24px 0 4px">
                <a href="{WebUtility.HtmlEncode(projectPulseUrl)}" style="display:inline-block;background:#005a9c;color:#fff;text-decoration:none;padding:11px 18px;border-radius:6px;font-weight:700">Open Pulse</a>
              </p>
              """;
        return $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;background:#f3f6f9;font-family:Arial,Helvetica,sans-serif">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f6f9;padding:24px 0">
                <tr><td align="center">
                  <table role="presentation" width="640" cellspacing="0" cellpadding="0" style="width:100%;max-width:640px;background:#fff;border:1px solid #dbe3eb;border-radius:10px;overflow:hidden">
                    <tr><td style="background:#0b2d4d;padding:20px 28px;color:#fff">
                      <div style="font-size:12px;letter-spacing:.12em;text-transform:uppercase;opacity:.84">US Signal · Pulse</div>
                      <div style="font-size:22px;font-weight:700;margin-top:7px">{WebUtility.HtmlEncode(subject)}</div>
                    </td></tr>
                    <tr><td style="padding:28px">
                      {string.Join(string.Empty, paragraphs)}
                      {action}
                    </td></tr>
                    <tr><td style="padding:16px 28px;background:#eef3f7;color:#526273;font-size:12px;line-height:1.45">
                      Policy {WebUtility.HtmlEncode(policy.PolicyCode)} · Source Module {WebUtility.HtmlEncode(policy.SourceModule)} · Delivery governed by Module 065.<br>
                      This message contains no stored mail credential or client-secret value.
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string DefaultDeepLink(string sourceModule) => sourceModule switch
    {
        "001" => "#timesheet",
        "002" => "#manager-approval",
        "003" => "#utilization",
        "005" => "#project-allocation-info",
        "019" => "#project-workspace",
        "022" => "#cost-alerts",
        "026" => "#crm-integration",
        "030" => "#reporting",
        "032" => "#notification-delivery-monitor",
        "039" => "#billing-readiness",
        "040" => "#project-closeout",
        "041" => "#closeout-email",
        "042" => "#invoice-billing-center",
        "065" => "#entra-secret-administration",
        "069" => "#qualifications-certifications",
        "071" => "#oncall-scheduling",
        "076" => "#module-076",
        _ => "#dashboard"
    };

    private static string ResolvePublicUrl(string? deepLink)
    {
        var configured = Environment.GetEnvironmentVariable("PROJECTPULSE_PUBLIC_URL")?.Trim()
            ?? Environment.GetEnvironmentVariable("PROJECTPULSE_PUBLIC_ORIGIN")?.Trim()
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configured)) return string.Empty;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps) return string.Empty;

        var relative = deepLink?.Trim() ?? string.Empty;
        if (relative.StartsWith('#')) return origin.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/" + relative;
        if (relative.StartsWith('/')) return origin.GetLeftPart(UriPartial.Authority).TrimEnd('/') + relative;
        return origin.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeTokenName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        return builder.ToString();
    }

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    [GeneratedRegex("\\{\\{\\s*([A-Za-z0-9_.-]+)\\s*\\}\\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}

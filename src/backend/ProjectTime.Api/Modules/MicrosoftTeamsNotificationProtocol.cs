using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>Bounded Graph protocol shared by the real sender and executable tests.
/// No app installation, tenant policy mutation, arbitrary URL, or automatic resend.</summary>
internal static class MicrosoftTeamsNotificationProtocol
{
    internal sealed record Evidence(string Code, string Message, string? GraphErrorCode = null, string? RequestId = null);
    internal sealed record Outcome(string Status, Evidence Diagnostic, Guid? CatalogAppId = null, string? InstalledVersion = null);
    private sealed record Installation(string Id, Guid CatalogId, string Version);
    private sealed class ProtocolFailure(Evidence evidence) : Exception("Teams protocol prerequisite failed")
    { internal Evidence Evidence { get; } = evidence; }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private static readonly HashSet<string> SafeGraphCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BadRequest", "InvalidRequest", "Forbidden", "NotFound", "ResourceNotFound", "ErrorItemNotFound",
        "Authorization_RequestDenied", "AccessDenied", "InvalidAuthenticationToken", "TooManyRequests",
        "InternalServerError", "ServiceUnavailable", "Conflict", "Request_BadRequest", "Request_ResourceNotFound"
    };

    internal static async Task<Outcome> ExecuteAsync(HttpClient client, Guid tenantId, Guid clientId, string secret,
        Guid manifestAppId, string recipient, bool send, Func<CancellationToken, Task<bool>> authorizeSend,
        CancellationToken cancellationToken)
    {
        var sendAttempted = false;
        try
        {
            if (tenantId == Guid.Empty || clientId == Guid.Empty || manifestAppId == Guid.Empty || string.IsNullOrWhiteSpace(secret))
                throw Failure("teams_services_configuration_incomplete", "Save the matching Module 065 services connection and credential first.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var ct = timeout.Token;
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{tenantId:D}/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["client_id"] = clientId.ToString("D"), ["client_secret"] = secret,
                  ["scope"] = "https://graph.microsoft.com/.default", ["grant_type"] = "client_credentials" })
            };
            using var tokenResponse = await client.SendAsync(tokenRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (tokenResponse.StatusCode != HttpStatusCode.OK)
                return Failed(await ErrorAsync(tokenResponse, "token", ct));
            using var tokenJson = await ReadJsonAsync(tokenResponse.Content, ct);
            var token = Text(tokenJson.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(token) || token.Length > 16384)
                throw Failure("teams_token_missing", "Microsoft did not return a usable application token.");

            // A mail alias is not necessarily a UPN. Resolve the exact requested identity;
            // do not search the entire directory or fall back to another tenant/user.
            using var userRequest = GraphGet($"users/{Uri.EscapeDataString(recipient.Trim())}?$select=id,userPrincipalName,userType,accountEnabled", token);
            using var userResponse = await client.SendAsync(userRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (userResponse.StatusCode != HttpStatusCode.OK) return Failed(await ErrorAsync(userResponse, "recipient", ct));
            using var userJson = await ReadJsonAsync(userResponse.Content, ct);
            var user = userJson.RootElement;
            if (!Guid.TryParse(Text(user, "id"), out var userId) || userId == Guid.Empty
                || Text(user, "userType") != "Member" || !user.TryGetProperty("accountEnabled", out var active)
                || active.ValueKind != JsonValueKind.True)
                throw Failure("teams_recipient_not_active_member", "Use an active member account in the configured tenant. Guest and disabled accounts are not included in this pilot.");

            // Read only this package in this user's personal scope. RSC Read.User is
            // included in the updated package; no tenant-wide app-management grant.
            var filter = Uri.EscapeDataString($"teamsApp/externalId eq '{manifestAppId:D}'");
            using var installRequest = GraphGet($"users/{userId:D}/teamwork/installedApps?$expand=teamsApp,teamsAppDefinition&$filter={filter}", token);
            using var installResponse = await client.SendAsync(installRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (installResponse.StatusCode != HttpStatusCode.OK) return Failed(await ErrorAsync(installResponse, "installation", ct));
            using var installedJson = await ReadJsonAsync(installResponse.Content, ct);
            var installation = ResolveInstallation(installedJson.RootElement, manifestAppId, clientId);
            if (!send) return new("installation_verified", new("teams_installation_verified",
                "The recipient and installed app match the saved services identity. No notification was sent; receipt and click-through are not yet verified."), installation.CatalogId, installation.Version);

            // Re-check configuration revision, credentials, caller authority and recipient
            // boundary immediately before the only externally visible mutation.
            if (!await authorizeSend(ct))
                throw Failure("teams_authority_changed", "Configuration, permission, or recipient policy changed. No notification was sent; refresh and review before retrying.");
            using var message = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{userId:D}/teamwork/sendActivityNotification");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var correlation = Guid.NewGuid().ToString("D");
            message.Headers.Add("client-request-id", correlation);
            message.Headers.Add("return-client-request-id", "true");
            // Graph resolves this verified installation to its Teams app. Never use a
            // direct Pulse URL or confuse manifest ID, catalog ID and installation ID.
            message.Content = JsonContent.Create(new
            {
                topic = new { source = "entityUrl", value = $"https://graph.microsoft.com/v1.0/users/{userId:D}/teamwork/installedApps/{Uri.EscapeDataString(installation.Id)}" },
                activityType = "systemDefault", teamsAppId = installation.CatalogId.ToString("D"),
                previewText = new { content = "A Pulse notification is available. Open Pulse to review it." },
                templateParameters = new[] { new { name = "systemDefaultText", value = "A Pulse notification is available." } }
            });
            sendAttempted = true;
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NoContent)
                return new("sent", new("teams_graph_http_204", "Microsoft accepted the notification. Confirm it in Teams Activity and open its destination.", RequestId: RequestId(response) ?? correlation), installation.CatalogId, installation.Version);
            var diagnostic = await ErrorAsync(response, "graph", ct);
            return new((int)response.StatusCode >= 500 || response.IsSuccessStatusCode ? "outcome_unknown" : "failed",
                diagnostic, installation.CatalogId, installation.Version);
        }
        catch (ProtocolFailure error) { return new(sendAttempted ? "outcome_unknown" : "failed", error.Evidence); }
        catch (Exception)
        {
            return new(sendAttempted ? "outcome_unknown" : "failed",
                new(sendAttempted ? "teams_delivery_outcome_unknown" : "teams_preflight_failed",
                    sendAttempted ? "The send outcome is unknown. Do not automatically resend; check Teams and delivery history."
                    : "The connection or prerequisite response could not be verified. No notification was sent. Retry the installation check after reviewing configuration."));
        }
    }

    private static Installation ResolveInstallation(JsonElement root, Guid manifestId, Guid clientId)
    {
        if (!root.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array || root.TryGetProperty("@odata.nextLink", out _))
            throw Failure("teams_installation_response_incomplete", "The filtered installation response is incomplete; no notification was sent.");
        if (values.GetArrayLength() == 0)
            throw Failure("teams_app_not_installed", "PulseApp is not installed in this user's personal Teams scope. Upload the updated package, allow this member in Teams admin center, and install it for the same tenant account.");
        if (values.GetArrayLength() != 1)
            throw Failure("teams_installation_ambiguous", "Multiple installation records matched this package. Resolve the duplicate in Teams before retrying.");
        var item = values[0];
        if (!item.TryGetProperty("teamsApp", out var app) || !item.TryGetProperty("teamsAppDefinition", out var definition)
            || !Guid.TryParse(Text(app, "externalId"), out var externalId) || externalId != manifestId
            || !Guid.TryParse(Text(app, "id"), out var catalogId) || catalogId == Guid.Empty
            || !Guid.TryParse(Text(definition, "teamsAppId"), out var definitionCatalog) || definitionCatalog != catalogId)
            throw Failure("teams_installation_identity_invalid", "The installed package could not be matched to its catalog identity. Update the same app package; do not substitute another app ID.");
        if (!Guid.TryParse(Text(definition, "azureADAppId"), out var associated) || associated != clientId)
            throw Failure("teams_app_identity_mismatch", "The installed manifest's webApplicationInfo.id does not match Pulse's saved services client ID. Update the installed package to use that services application.");
        var publishingState = Text(definition, "publishingState");
        if (publishingState.Length > 0 && publishingState != "published")
            throw Failure("teams_app_not_published", "The app definition is not published. Ask the Teams administrator to complete approval and availability for the pilot user.");
        var installationId = Text(item, "id");
        if (installationId.Length is < 1 or > 1024 || installationId.Any(char.IsControl))
            throw Failure("teams_installation_identity_invalid", "The installation identifier is invalid; no notification was sent.");
        var version = Text(definition, "version");
        return new(installationId, catalogId, Version.TryParse(version, out _) && version.Length <= 32 ? version : "unknown");
    }

    private static HttpRequestMessage GraphGet(string suffix, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/" + suffix);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
    private static Outcome Failed(Evidence evidence) => new("failed", evidence);
    private static ProtocolFailure Failure(string code, string message) => new(new(code, message));
    internal static string Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    internal static async Task<JsonDocument> ReadJsonAsync(HttpContent content, CancellationToken ct)
    {
        const int maximum = 32768;
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("Response exceeds bound");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximum + 1 - (int)memory.Length)), ct)) > 0)
        {
            memory.Write(buffer, 0, count);
            if (memory.Length > maximum) throw new InvalidDataException("Response exceeds bound");
        }
        return JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
    }
    private static string? RequestId(HttpResponseMessage response)
        => response.Headers.TryGetValues("request-id", out var values) && Guid.TryParse(values.FirstOrDefault(), out var id) ? id.ToString("D") : null;
    internal static async Task<Evidence> ErrorAsync(HttpResponseMessage response, string stage, CancellationToken ct)
    {
        var http = (int)response.StatusCode;
        string? code = null;
        var requestId = RequestId(response);
        var classification = "";
        try
        {
            using var json = await ReadJsonAsync(response.Content, ct);
            if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var rawCode = Text(error, "code");
                code = SafeGraphCodes.Contains(rawCode) ? rawCode : "unclassified";
                // Untrusted provider prose is classified, never retained or returned.
                var message = Text(error, "message");
                if (message.Contains("not installed", StringComparison.OrdinalIgnoreCase)) classification = "installation";
                else if (message.Contains("webUrl", StringComparison.OrdinalIgnoreCase)) classification = "destination";
                if (requestId is null && error.TryGetProperty("innerError", out var inner)
                    && Guid.TryParse(Text(inner, "request-id"), out var id)) requestId = id.ToString("D");
            }
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or OperationCanceledException or HttpRequestException) { }
        var messageSafe = stage switch
        {
            "token" => "Microsoft rejected the saved services application's token request. Verify its tenant, client ID and stored credential; do not change the SSO connection.",
            "recipient" when http == 404 => "The recipient was not found by that sign-in address in the configured tenant. Use the verified Entra user principal name, not a mail alias.",
            "recipient" => "Recipient lookup failed. Verify the saved services application's directory-read permission and the active tenant member.",
            "installation" when http == 403 => "Installation cannot be checked. Install/update the corrected PulseApp package and consent to its TeamsAppInstallation.Read.User permission, or ask the Teams administrator to approve the scoped read.",
            "installation" => "The app installation query failed. Verify personal installation, tenant and Teams app availability; HTTP status alone does not prove the app is blocked.",
            _ when classification == "installation" => "Microsoft reports that the notification app is not installed for this recipient. Update the package and its personal installation.",
            _ when classification == "destination" => "Microsoft rejected the notification topic or destination. Retain this request ID for investigation.",
            _ when http == 403 => "Microsoft denied notification delivery. Verify TeamsActivity.Send.User consent or TeamsActivity.Send application consent for the saved services application.",
            _ when http == 429 => "Microsoft rate-limited the request. No automatic resend was performed.",
            _ => "Microsoft did not confirm notification acceptance. Review this error code and request ID; do not infer the cause from HTTP status alone."
        };
        return new($"teams_{stage}_http_{http}", messageSafe, code, requestId);
    }
    internal static string Store(Evidence evidence) => JsonSerializer.Serialize(evidence, JsonOptions);
    internal static Evidence ReadStored(string value)
    {
        if (value.Length <= 4096 && value.StartsWith('{'))
        {
            try { return JsonSerializer.Deserialize<Evidence>(value, JsonOptions) ?? new("teams_diagnostic_unavailable", "Diagnostic is unavailable."); }
            catch (JsonException) { }
        }
        return new(value.Length <= 128 ? value : "teams_diagnostic_unavailable", "Legacy result: detailed Microsoft evidence was not recorded. Use Check installation before sending another test.");
    }
}

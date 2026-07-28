using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 065 non-delivery readiness test for the configured Microsoft mail
/// transport. Test and Production profiles can be evaluated independently.
/// Credentials are read only from the established environment-specific stores,
/// never accepted from the browser, never returned, and no email is sent.
/// </summary>
public static class MicrosoftMailTransportTestModule
{
    private const string ModuleNumber = "065";
    private const string TestPath = "/api/microsoft-integration/mail-runtime/test";
    private const string ConfigurationMarker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(8);

    public static WebApplication MapMicrosoftMailTransportTestEndpoints(this WebApplication app)
    {
        app.MapPost(TestPath, (Func<HttpContext, Task<IResult>>)TestAsync);
        return app;
    }

    private static async Task<IResult> TestAsync(HttpContext context)
    {
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (AdminExperienceCommon.IsViewAs(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "view_as_read_only",
                message = "Exit Administrator View-As before testing Microsoft mail transport."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        MailTestRequest? request = null;
        try
        {
            if (context.Request.ContentLength is > 0)
            {
                request = await context.Request.ReadFromJsonAsync<MailTestRequest>(
                    cancellationToken: context.RequestAborted);
            }
        }
        catch
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_mail_test_request",
                message = "Choose Test or Production before running the sender and transport readiness test."
            });
        }

        var runtimeEnvironment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        var environmentMode = MicrosoftEnvironmentRuntimeResolver.Normalize(request?.EnvironmentMode);
        if (string.IsNullOrWhiteSpace(environmentMode)) environmentMode = runtimeEnvironment;
        if (string.IsNullOrWhiteSpace(environmentMode))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "microsoft_environment_unresolved",
                correlationId = context.TraceIdentifier,
                message = "ProjectPulse could not determine the Test or Production Microsoft environment."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var profile = await ReadStoredProfileAsync(
            access.Context!.ConnectionString,
            environmentMode,
            context.RequestAborted);
        if (profile is null)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "mail_profile_not_configured",
                environmentMode,
                correlationId = context.TraceIdentifier,
                message = $"Complete and save the {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} sender and transport settings in Module 065 before testing."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var servicesSecret = ResolveServicesSecret(environmentMode, profile.TenantKey);
        var smtpCredential = ResolveSmtpCredential(environmentMode);
        var graph = profile.Provider == "microsoft_graph"
            ? await TestGraphAsync(
                profile.TenantId,
                profile.ClientId,
                servicesSecret,
                profile.Sender,
                context.RequestAborted)
            : GraphTest.NotSelected();
        var smtp = profile.Provider == "smtp_relay"
            ? await TestSmtpAsync(
                profile.SmtpHost,
                profile.SmtpPort,
                smtpCredential,
                context.RequestAborted)
            : SmtpTest.NotSelected();

        var metadataReady = !string.IsNullOrWhiteSpace(profile.Provider)
            && (profile.Provider == "locked" || IsEmail(profile.Sender));
        var providerReady = profile.Provider switch
        {
            "microsoft_graph" => graph.AuthenticationReady
                && graph.MailSendRoleDeclared
                && graph.SenderResolved,
            "smtp_relay" => smtp.NetworkReachable && smtp.CredentialAvailable,
            "locked" => true,
            _ => false
        };
        var ready = metadataReady && providerReady;
        var selectedEnvironmentIsRuntime = environmentMode.Equals(
            runtimeEnvironment,
            StringComparison.OrdinalIgnoreCase);
        var liveDeliveryEnabled = selectedEnvironmentIsRuntime
            && profile.Boundary == "production_governed"
            && profile.Provider != "locked"
            && ready;
        var activeDeliveryProvider = liveDeliveryEnabled
            ? profile.Provider
            : selectedEnvironmentIsRuntime ? "outbox_only" : "profile_not_active_here";
        var deliveryMode = liveDeliveryEnabled ? "live_governed" : "outbox_only";

        var evidence = new
        {
            environmentMode,
            runtimeEnvironment,
            configuredProvider = profile.Provider,
            activeDeliveryProvider,
            boundary = profile.Boundary,
            senderMailbox = profile.Sender,
            metadataReady,
            providerReady,
            ready,
            liveDeliveryEnabled,
            deliveryMode,
            graph = new
            {
                graph.Status,
                graph.AuthenticationReady,
                graph.MailSendRoleDeclared,
                graph.DirectoryRolesDeclared,
                graph.SenderResolved,
                graph.HttpStatus,
                graph.Message
            },
            smtp = new
            {
                smtp.Status,
                smtp.NetworkReachable,
                smtp.CredentialAvailable,
                smtp.HostAccepted,
                smtp.Port,
                smtp.Message
            },
            liveMessageSent = false,
            secretValuesReturned = false
        };

        try
        {
            await using var connection = new NpgsqlConnection(access.Context.ConnectionString);
            await connection.OpenAsync(context.RequestAborted);
            await AdminExperienceCommon.WriteAuditAsync(
                connection,
                null,
                "integration",
                ready ? "success" : "warning",
                "MICROSOFT_MAIL_TRANSPORT_TESTED",
                access.Context.UserId,
                access.Context.Email,
                "microsoft_mail_transport",
                environmentMode,
                profile.Sender,
                ModuleNumber,
                "projectpulse_system_audit_events",
                context.TraceIdentifier,
                ready
                    ? $"Module 065 {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} sender and configured transport readiness test passed."
                    : $"Module 065 {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} sender and configured transport readiness test requires attention.",
                evidence,
                AdminExperienceCommon.ClientIp(context),
                context.TraceIdentifier,
                context.RequestAborted);
        }
        catch
        {
            // Readiness results remain available when optional audit evidence is unavailable.
        }

        var boundaryMessage = profile.Boundary switch
        {
            "production_governed" when liveDeliveryEnabled =>
                "Governed live delivery is eligible in the running environment.",
            "production_governed" when !selectedEnvironmentIsRuntime =>
                "This profile is not active in the currently running environment.",
            "production_governed" =>
                "Live delivery remains disabled until the configured provider is ready.",
            _ =>
                "The recipient boundary intentionally prevents live delivery; the configured provider was still tested."
        };

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = ready
                ? "mail_transport_test_passed"
                : "mail_transport_test_attention_required",
            testedAt = DateTimeOffset.UtcNow,
            environmentMode,
            runtimeEnvironment,
            selectedEnvironmentIsRuntime,
            configuredProvider = profile.Provider,
            provider = profile.Provider,
            activeDeliveryProvider,
            recipientBoundary = profile.Boundary,
            senderMailbox = profile.Sender,
            metadataReady,
            providerReady,
            runtimeReady = ready,
            configuredTransportReady = ready,
            liveDeliveryEnabled,
            deliveryMode,
            graph = new
            {
                graph.Status,
                graph.AuthenticationReady,
                graph.MailSendRoleDeclared,
                graph.DirectoryRolesDeclared,
                graph.SenderResolved,
                graph.HttpStatus,
                graph.Message
            },
            smtp = new
            {
                smtp.Status,
                smtp.NetworkReachable,
                smtp.CredentialAvailable,
                smtp.HostAccepted,
                smtp.Port,
                smtp.Message
            },
            liveMessageSent = false,
            outboxMessageCreated = false,
            secretValuesReturned = false,
            auditEvidenceRequested = true,
            correlationId = context.TraceIdentifier,
            message = ready
                ? $"The configured {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} sender and {ProviderLabel(profile.Provider)} transport passed the non-delivery readiness test. {boundaryMessage} No email was sent."
                : $"The configured {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} {ProviderLabel(profile.Provider)} transport requires attention. {boundaryMessage} No email was sent."
        });
    }

    private static async Task<MailProfile?> ReadStoredProfileAsync(
        string connectionString,
        string environmentMode,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT document_json::text
            FROM projectpulse_native_admin_documents
            WHERE module_number='065' AND document_key='configuration'
            LIMIT 1;
            """, connection);
        var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(raw)) return null;

        using var document = JsonDocument.Parse(raw);
        if (!TryProperty(document.RootElement, "configuration", out var configuration)) return null;
        var notes = JsonString(configuration, "notes");
        if (!notes.StartsWith(ConfigurationMarker, StringComparison.Ordinal)) return null;

        using var stored = JsonDocument.Parse(notes[ConfigurationMarker.Length..]);
        var root = stored.RootElement;
        if (!TryProperty(root, "tenants", out var tenants)
            || tenants.ValueKind != JsonValueKind.Array) return null;

        foreach (var tenant in tenants.EnumerateArray())
        {
            var mode = MicrosoftEnvironmentRuntimeResolver.Normalize(
                JsonString(tenant, "environmentMode"));
            if (!mode.Equals(environmentMode, StringComparison.OrdinalIgnoreCase)) continue;

            if (!TryProperty(tenant, "services", out var services)) services = default;
            if (!TryProperty(tenant, "mail", out var mail)
                && !TryProperty(root, "mail", out mail)) mail = default;

            return new MailProfile(
                First(JsonString(tenant, "key"), JsonString(tenant, "tenantKey")),
                environmentMode,
                NormalizeProvider(JsonString(mail, "providerTarget")),
                NormalizeBoundary(JsonString(mail, "recipientBoundary")),
                JsonString(tenant, "tenantId"),
                JsonString(services, "clientId"),
                JsonString(mail, "senderAddress"),
                First(JsonString(mail, "smtpHost"), "smtp.office365.com"),
                JsonInt(mail, "smtpPort") ?? 587);
        }

        return null;
    }

    private static async Task<GraphTest> TestGraphAsync(
        string tenantId,
        string clientId,
        string clientSecret,
        string sender,
        CancellationToken requestCancellation)
    {
        if (!Guid.TryParse(tenantId, out var tenantGuid)
            || !Guid.TryParse(clientId, out _)
            || string.IsNullOrWhiteSpace(clientSecret)
            || !IsEmail(sender))
        {
            return new(
                "configuration_incomplete",
                false,
                false,
                false,
                false,
                0,
                "Graph mail requires a tenant GUID, services client GUID, environment-specific services secret, and sender mailbox.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
        timeout.CancelAfter(NetworkTimeout);
        using var client = new HttpClient { Timeout = NetworkTimeout };

        try
        {
            var tokenUrl = $"https://login.microsoftonline.com/{tenantGuid:D}/oauth2/v2.0/token";
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials"
                })
            };
            using var tokenResponse = await client.SendAsync(tokenRequest, timeout.Token);
            var tokenRaw = await tokenResponse.Content.ReadAsStringAsync(timeout.Token);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return new(
                    "token_request_failed",
                    false,
                    false,
                    false,
                    false,
                    (int)tokenResponse.StatusCode,
                    $"Microsoft identity rejected the services credential with HTTP {(int)tokenResponse.StatusCode}.");
            }

            using var tokenDocument = JsonDocument.Parse(tokenRaw);
            var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement)
                ? tokenElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(accessToken))
                return new("token_missing", false, false, false, false, 200, "Microsoft identity did not return an application access token.");

            var roles = ReadTokenRoles(accessToken);
            var mailSend = roles.Contains("Mail.Send");
            var directoryRoles = roles.Contains("Directory.Read.All")
                && roles.Contains("User.Read.All");

            using var senderRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(sender)}?$select=id,mail,userPrincipalName,accountEnabled");
            senderRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var senderResponse = await client.SendAsync(senderRequest, timeout.Token);
            var senderResolved = senderResponse.IsSuccessStatusCode;

            return new(
                senderResolved && mailSend ? "ready" : "attention_required",
                true,
                mailSend,
                directoryRoles,
                senderResolved,
                (int)senderResponse.StatusCode,
                senderResolved
                    ? mailSend
                        ? "Microsoft Graph authenticated, the sender mailbox resolved, and Mail.Send is present in the application token."
                        : "Microsoft Graph authenticated and the sender mailbox resolved, but Mail.Send is not present in the application token."
                    : $"Microsoft Graph authenticated, but the sender mailbox lookup returned HTTP {(int)senderResponse.StatusCode}.");
        }
        catch (OperationCanceledException)
        {
            return new("timeout", false, false, false, false, 0, "The Microsoft Graph readiness test timed out.");
        }
        catch
        {
            return new("network_failure", false, false, false, false, 0, "The Microsoft Graph readiness test could not complete.");
        }
    }

    private static async Task<SmtpTest> TestSmtpAsync(
        string host,
        int port,
        SmtpCredential credential,
        CancellationToken requestCancellation)
    {
        var accepted = host.Equals("smtp.office365.com", StringComparison.OrdinalIgnoreCase);
        if (!accepted || port is <= 0 or > 65535)
        {
            return new(
                "host_not_allowed",
                false,
                credential.Available,
                accepted,
                port,
                "The non-delivery SMTP test permits only smtp.office365.com with a valid port.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
        timeout.CancelAfter(NetworkTimeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            return new(
                credential.Available ? "ready" : "credential_required",
                client.Connected,
                credential.Available,
                true,
                port,
                credential.Available
                    ? "The Microsoft 365 SMTP endpoint is reachable and the environment-specific credential pair is present. No authentication or email send was attempted."
                    : "The Microsoft 365 SMTP endpoint is reachable, but the environment-specific credential pair is missing.");
        }
        catch (OperationCanceledException)
        {
            return new("timeout", false, credential.Available, true, port, "The Microsoft 365 SMTP connectivity test timed out.");
        }
        catch (SocketException)
        {
            return new("network_failure", false, credential.Available, true, port, "The Microsoft 365 SMTP endpoint could not be reached.");
        }
    }

    private static IReadOnlySet<string> ReadTokenRoles(string accessToken)
    {
        try
        {
            var segments = accessToken.Split('.');
            if (segments.Length < 2) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (!document.RootElement.TryGetProperty("roles", out var roles))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    var value = role.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
                }
            }
            else if (roles.ValueKind == JsonValueKind.String)
            {
                var value = roles.GetString();
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            }
            return result;
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ResolveServicesSecret(string environmentMode, string tenantKey)
    {
        var token = new string((tenantKey ?? string.Empty).ToUpperInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')
            .ToArray());
        var activeMode = MicrosoftEnvironmentRuntimeResolver.Normalize(
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
        var modeName = environmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET"
            : "PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET";
        return First(
            Environment.GetEnvironmentVariable($"PROJECTPULSE_MICROSOFT_TENANT_{token}_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable(modeName),
            activeMode == environmentMode
                ? Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET")
                : string.Empty,
            activeMode == environmentMode
                ? Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET")
                : string.Empty);
    }

    private static SmtpCredential ResolveSmtpCredential(string environmentMode)
    {
        var activeMode = MicrosoftEnvironmentRuntimeResolver.Normalize(
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
        var prefix = environmentMode == "production"
            ? "PROJECTPULSE_PRODUCTION_SMTP_"
            : "PROJECTPULSE_TEST_SMTP_";
        var username = First(
            Environment.GetEnvironmentVariable(prefix + "USERNAME"),
            activeMode == environmentMode
                ? Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME")
                : string.Empty,
            activeMode == environmentMode
                ? Environment.GetEnvironmentVariable("SMTP_USERNAME")
                : string.Empty);
        var password = First(
            Environment.GetEnvironmentVariable(prefix + "PASSWORD"),
            activeMode == environmentMode
                ? Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD")
                : string.Empty,
            activeMode == environmentMode
                ? Environment.GetEnvironmentVariable("SMTP_PASSWORD")
                : string.Empty);
        return new(!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password));
    }

    private static string NormalizeProvider(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "microsoft_graph" => "microsoft_graph",
            "exchange_online_smtp" or "smtp_relay" or "smtp" => "smtp_relay",
            "locked" or "outbox_only" or "" => "locked",
            _ => normalized
        };
    }

    private static string NormalizeBoundary(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "production_governed" or "test_only" or "locked"
            ? normalized
            : "test_only";
    }

    private static string ProviderLabel(string provider) => provider switch
    {
        "microsoft_graph" => "Microsoft Graph",
        "smtp_relay" => "Microsoft 365 SMTP relay",
        _ => "Locked"
    };

    private static bool IsEmail(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains('@')
        && value.IndexOf('@') > 0
        && value.LastIndexOf('.') > value.IndexOf('@') + 1;

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string JsonString(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int? JsonInt(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record MailTestRequest(string? EnvironmentMode);
    private sealed record MailProfile(
        string TenantKey,
        string EnvironmentMode,
        string Provider,
        string Boundary,
        string TenantId,
        string ClientId,
        string Sender,
        string SmtpHost,
        int SmtpPort);

    private sealed record GraphTest(
        string Status,
        bool AuthenticationReady,
        bool MailSendRoleDeclared,
        bool DirectoryRolesDeclared,
        bool SenderResolved,
        int HttpStatus,
        string Message)
    {
        internal static GraphTest NotSelected() =>
            new("not_selected", false, false, false, false, 0, "Microsoft Graph is not the configured mail provider for this environment.");
    }

    private sealed record SmtpTest(
        string Status,
        bool NetworkReachable,
        bool CredentialAvailable,
        bool HostAccepted,
        int Port,
        string Message)
    {
        internal static SmtpTest NotSelected() =>
            new("not_selected", false, false, false, 0, "Microsoft 365 SMTP relay is not the configured mail provider for this environment.");
    }

    private sealed record SmtpCredential(bool Available);
}

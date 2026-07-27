using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 065 non-delivery readiness test for the configured Microsoft mail
/// transport. It never accepts or returns credentials, never sends email, and
/// records sanitized evidence in Module 008 when the immutable audit ledger is
/// available.
/// </summary>
public static class MicrosoftMailTransportTestModule
{
    private const string ModuleNumber = "065";
    private const string TestPath = "/api/microsoft-integration/mail-runtime/test";
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

        var environmentMode = NormalizeEnvironment(First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_SSO_MODE"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")));
        var provider = NormalizeProvider(First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_PROVIDER"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_EMAIL_PROVIDER")));
        var boundary = NormalizeBoundary(Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_RECIPIENT_BOUNDARY"));
        var sender = First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_M365_SENDER_MAILBOX"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_FROM"));
        var tenantId = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_TENANT_ID") ?? string.Empty;
        var clientId = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_ID") ?? string.Empty;
        var clientSecret = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET") ?? string.Empty;
        var smtpHost = First(Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_HOST"), "smtp.office365.com");
        var smtpPort = int.TryParse(Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PORT"), out var parsedPort)
            ? parsedPort
            : 587;
        var smtpCredential = ResolveSmtpCredential(environmentMode);

        var graph = provider == "microsoft_graph"
            ? await TestGraphAsync(tenantId, clientId, clientSecret, sender, context.RequestAborted)
            : GraphTest.NotSelected();
        var smtp = provider is "smtp_relay" or "exchange_online_smtp" or "smtp"
            ? await TestSmtpAsync(smtpHost, smtpPort, smtpCredential, context.RequestAborted)
            : SmtpTest.NotSelected();

        var metadataReady = !string.IsNullOrWhiteSpace(environmentMode)
            && !string.IsNullOrWhiteSpace(provider)
            && !string.IsNullOrWhiteSpace(sender)
            && IsEmail(sender);
        var providerReady = provider switch
        {
            "microsoft_graph" => graph.AuthenticationReady && graph.MailSendRoleDeclared && graph.SenderResolved,
            "smtp_relay" or "exchange_online_smtp" or "smtp" => smtp.NetworkReachable && smtp.CredentialAvailable,
            "locked" or "outbox_only" => true,
            _ => false
        };
        var ready = metadataReady && providerReady;
        var deliveryMode = boundary == "production_governed" && provider is not ("locked" or "outbox_only")
            ? "live_governed"
            : "outbox_only";

        var evidence = new
        {
            environmentMode,
            provider,
            boundary,
            senderMailbox = sender,
            metadataReady,
            providerReady,
            ready,
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
            secretValuesRead = false,
            secretValuesReturned = false
        };

        try
        {
            await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
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
                sender,
                ModuleNumber,
                "projectpulse_system_audit_events",
                context.TraceIdentifier,
                ready
                    ? "Module 065 Microsoft sender and transport readiness test passed."
                    : "Module 065 Microsoft sender and transport readiness test requires attention.",
                evidence,
                AdminExperienceCommon.ClientIp(context),
                context.TraceIdentifier,
                context.RequestAborted);
        }
        catch
        {
            // Readiness results remain available when optional audit evidence is unavailable.
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = ready ? "mail_transport_test_passed" : "mail_transport_test_attention_required",
            testedAt = DateTimeOffset.UtcNow,
            environmentMode,
            provider,
            recipientBoundary = boundary,
            senderMailbox = sender,
            metadataReady,
            providerReady,
            runtimeReady = ready,
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
            message = ready
                ? "The configured sender and transport passed the non-delivery readiness test. No email was sent."
                : "The non-delivery readiness test found configuration or connectivity items that require attention. No email was sent."
        });
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
            {
                return new("token_missing", false, false, false, false, 200, "Microsoft identity did not return an application access token.");
            }

            var roles = ReadTokenRoles(accessToken);
            var mailSend = roles.Contains("Mail.Send", StringComparer.OrdinalIgnoreCase);
            var directoryRoles = roles.Contains("Directory.Read.All", StringComparer.OrdinalIgnoreCase)
                && roles.Contains("User.Read.All", StringComparer.OrdinalIgnoreCase);

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
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(accessToken);
            return token.Claims
                .Where(claim => claim.Type == "roles")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static SmtpCredential ResolveSmtpCredential(string environmentMode)
    {
        var prefix = environmentMode == "production"
            ? "PROJECTPULSE_PRODUCTION_SMTP_"
            : "PROJECTPULSE_TEST_SMTP_";
        var username = First(
            Environment.GetEnvironmentVariable(prefix + "USERNAME"),
            environmentMode == "production" ? string.Empty : Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME"));
        var password = First(
            Environment.GetEnvironmentVariable(prefix + "PASSWORD"),
            environmentMode == "production" ? string.Empty : Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD"));
        return new(!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password));
    }

    private static string NormalizeEnvironment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "test" or "testing" or "development" or "dev") return "test";
        if (normalized is "production" or "prod") return "production";
        return string.Empty;
    }

    private static string NormalizeProvider(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "microsoft_graph" => "microsoft_graph",
            "exchange_online_smtp" or "smtp_relay" or "smtp" => "smtp_relay",
            "locked" or "outbox_only" or "" => normalized is "" ? "locked" : normalized,
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

    private static bool IsEmail(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains('@')
        && value.IndexOf('@') > 0
        && value.LastIndexOf('.') > value.IndexOf('@') + 1;

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

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
            new("not_selected", false, false, false, false, 0, "Microsoft Graph is not the selected mail provider.");
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
            new("not_selected", false, false, false, 0, "Microsoft 365 SMTP relay is not the selected mail provider.");
    }

    private sealed record SmtpCredential(bool Available);
}

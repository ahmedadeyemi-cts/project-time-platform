using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Governed Group 4 delivery adapter. Module 065 remains the only owner of the
/// active Microsoft mail profile, environment-specific credentials, sender,
/// transport, and recipient boundary. Group 4 never reads the retired Module 067
/// configuration document and never accepts credentials from a notification request.
/// </summary>
internal static class Module065ProjectNotificationDelivery
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(15);

    internal static async Task<Module065MailReadiness> GetReadinessAsync(
        HttpContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var runtimeEnvironment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (string.IsNullOrWhiteSpace(runtimeEnvironment))
        {
            return Module065MailReadiness.Locked(
                "ProjectPulse could not determine whether the Test or Production Module 065 mail profile is active.");
        }

        try
        {
            await MicrosoftMailRuntimeConfigurationModule.ApplyStoredEnvironmentAsync(runtimeEnvironment);
        }
        catch
        {
            // The explicit readiness contract below remains authoritative and
            // fail-closed even when optional runtime hydration cannot complete.
        }

        cancellationToken.ThrowIfCancellationRequested();

        var configuredEnvironment = MicrosoftEnvironmentRuntimeResolver.Normalize(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_CONFIGURED_ENVIRONMENT"));
        var configuredProvider = NormalizeProvider(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_CONFIGURED_PROVIDER"));
        var activeProvider = NormalizeActiveProvider(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_PROVIDER"));
        var boundary = NormalizeBoundary(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_RECIPIENT_BOUNDARY"));
        var sender = First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_M365_SENDER_MAILBOX"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_FROM"));

        var tenantId = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_TENANT_ID") ?? string.Empty;
        var clientId = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_ID") ?? string.Empty;
        var servicesSecret = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET") ?? string.Empty;
        var smtpCredential = ResolveSmtpCredential(runtimeEnvironment);

        var graphCredentialAvailable = Guid.TryParse(tenantId, out _)
            && Guid.TryParse(clientId, out _)
            && !string.IsNullOrWhiteSpace(servicesSecret)
            && IsEmail(sender);
        var smtpCredentialAvailable = !string.IsNullOrWhiteSpace(smtpCredential.Username)
            && !string.IsNullOrWhiteSpace(smtpCredential.Password)
            && IsEmail(sender)
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_HOST"));

        var configuredTransportReady = configuredProvider switch
        {
            "microsoft_graph" => graphCredentialAvailable,
            "smtp_relay" => smtpCredentialAvailable,
            "locked" => true,
            _ => false
        };
        var profileMatchesRuntime = configuredEnvironment == runtimeEnvironment;
        var liveDeliveryEnabled = profileMatchesRuntime
            && boundary == "production_governed"
            && configuredProvider != "locked"
            && configuredTransportReady
            && activeProvider != "locked";
        var deliveryMode = liveDeliveryEnabled ? "live_governed" : "outbox_only";

        var message = !profileMatchesRuntime
            ? $"The stored Module 065 {Display(configuredEnvironment)} profile is not active in the current {Display(runtimeEnvironment)} runtime."
            : boundary == "test_only"
                ? "Module 065 is configured with a Test-only recipient boundary. Dispatches remain recorded and suppressed; no live email is sent."
                : boundary == "locked"
                    ? "Module 065 delivery is locked. Dispatches remain recorded and cannot leave ProjectPulse."
                    : !configuredTransportReady
                        ? "The Module 065 transport or sender is incomplete. Dispatches remain queued or failed with sanitized diagnostics."
                        : "Module 065 is eligible for governed delivery in the running environment.";

        return new(
            runtimeEnvironment,
            configuredEnvironment,
            configuredProvider,
            activeProvider,
            boundary,
            sender,
            configuredTransportReady,
            liveDeliveryEnabled,
            graphCredentialAvailable,
            smtpCredentialAvailable,
            deliveryMode,
            message);
    }

    /// <summary>
    /// Sends one explicitly confirmed Test-environment message without changing
    /// the global recipient boundary. This method is intentionally internal and
    /// may be called only after the Module 065 endpoint has verified the exact
    /// confirmation phrase and a self/allowlisted recipient. General notification
    /// delivery continues to honor test_only as outbox-only.
    /// </summary>
    internal static async Task<Module065MailDeliveryResult> DeliverGovernedTestAsync(
        string subject,
        string textBody,
        string htmlBody,
        ProjectNotificationUser recipient,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var readiness = await GetReadinessAsync(context, cancellationToken);
        if (!readiness.RuntimeEnvironment.Equals("test", StringComparison.OrdinalIgnoreCase)
            || !readiness.ConfiguredEnvironment.Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                "suppressed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "MODULE_065_TEST_ENVIRONMENT_REQUIRED",
                "A governed delivery test may run only from the active Test Module 065 profile.");
        }

        if (!IsEmail(recipient.Email))
        {
            return new(
                false,
                "suppressed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "MODULE_065_TEST_RECIPIENT_INVALID",
                "The governed Test dispatch requires one valid email recipient.");
        }

        if (readiness.RecipientBoundary == "locked")
        {
            return new(
                false,
                "suppressed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "MODULE_065_TEST_BOUNDARY_LOCKED",
                "The Module 065 recipient boundary is locked. No Test message was sent.");
        }

        if (!readiness.RuntimeReady)
        {
            return new(
                false,
                "failed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "MODULE_065_TEST_TRANSPORT_NOT_READY",
                readiness.Message);
        }

        try
        {
            var recipients = new[] { recipient with { RecipientType = "to" } };
            return readiness.ConfiguredProvider switch
            {
                "microsoft_graph" => await DeliverGraphAsync(
                    readiness,
                    subject,
                    textBody,
                    htmlBody,
                    recipients,
                    cancellationToken),
                "smtp_relay" => await DeliverSmtpAsync(
                    readiness,
                    subject,
                    textBody,
                    htmlBody,
                    recipients,
                    cancellationToken),
                _ => new(
                    false,
                    "suppressed",
                    readiness.ConfiguredProvider,
                    readiness.RecipientBoundary,
                    string.Empty,
                    "MODULE_065_TEST_PROVIDER_LOCKED",
                    "Select Microsoft Graph or Microsoft 365 SMTP relay before sending a governed Test message.")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(
                false,
                "failed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "MODULE_065_TEST_DELIVERY_TIMEOUT",
                "The governed Module 065 Test delivery timed out.");
        }
        catch (Exception exception)
        {
            return new(
                false,
                "failed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                Diagnostic(exception),
                FriendlyDeliveryFailure(exception));
        }
    }

    internal static async Task<Module065MailDeliveryResult> DeliverAsync(
        string subject,
        string textBody,
        string htmlBody,
        IReadOnlyCollection<ProjectNotificationUser> recipients,
        HttpContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var readiness = await GetReadinessAsync(context, cancellationToken);
        var validRecipients = recipients
            .Where(recipient => IsEmail(recipient.Email))
            .GroupBy(recipient => $"{recipient.RecipientType}:{recipient.Email}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (validRecipients.Length == 0)
        {
            return new(
                false,
                "suppressed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "NO_VALID_RECIPIENTS",
                "The dispatch did not contain an email-ready recipient.");
        }

        if (!readiness.LiveDeliveryEnabled)
        {
            return new(
                false,
                readiness.RecipientBoundary == "locked" ? "suppressed" : "queued",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                readiness.RuntimeReady
                    ? "RECIPIENT_BOUNDARY_PREVENTED_DELIVERY"
                    : "MODULE_065_TRANSPORT_NOT_READY",
                readiness.Message);
        }

        try
        {
            return readiness.ConfiguredProvider switch
            {
                "microsoft_graph" => await DeliverGraphAsync(
                    readiness,
                    subject,
                    textBody,
                    htmlBody,
                    validRecipients,
                    cancellationToken),
                "smtp_relay" => await DeliverSmtpAsync(
                    readiness,
                    subject,
                    textBody,
                    htmlBody,
                    validRecipients,
                    cancellationToken),
                _ => new(
                    false,
                    "suppressed",
                    readiness.ConfiguredProvider,
                    readiness.RecipientBoundary,
                    string.Empty,
                    "MODULE_065_PROVIDER_LOCKED",
                    "Module 065 does not have an active governed delivery provider.")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(
                false,
                "failed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "MODULE_065_DELIVERY_TIMEOUT",
                "The configured Module 065 delivery operation timed out.");
        }
        catch (Exception exception)
        {
            return new(
                false,
                "failed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                Diagnostic(exception),
                FriendlyDeliveryFailure(exception));
        }
    }

    private static async Task<Module065MailDeliveryResult> DeliverGraphAsync(
        Module065MailReadiness readiness,
        string subject,
        string textBody,
        string htmlBody,
        ProjectNotificationUser[] recipients,
        CancellationToken cancellationToken)
    {
        var tenantId = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_TENANT_ID") ?? string.Empty;
        var clientId = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_ID") ?? string.Empty;
        var clientSecret = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET") ?? string.Empty;
        if (!Guid.TryParse(tenantId, out var tenantGuid)
            || !Guid.TryParse(clientId, out _)
            || string.IsNullOrWhiteSpace(clientSecret)
            || !IsEmail(readiness.SenderMailbox))
        {
            return new(false, "failed", "microsoft_graph", readiness.RecipientBoundary, string.Empty,
                "MODULE_065_GRAPH_CONFIGURATION_INCOMPLETE",
                "The Module 065 Microsoft Graph sender or application credential is incomplete.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(NetworkTimeout);
        using var client = new HttpClient { Timeout = NetworkTimeout };
        using var tokenResponse = await client.PostAsync(
            $"https://login.microsoftonline.com/{tenantGuid:D}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            }),
            timeout.Token);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            return new(false, "failed", "microsoft_graph", readiness.RecipientBoundary, string.Empty,
                $"MODULE_065_GRAPH_TOKEN_HTTP_{(int)tokenResponse.StatusCode}",
                "Microsoft identity did not authorize the Module 065 mail application.");
        }

        using var tokenDocument = JsonDocument.Parse(
            await tokenResponse.Content.ReadAsStringAsync(timeout.Token));
        var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement)
            ? tokenElement.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new(false, "failed", "microsoft_graph", readiness.RecipientBoundary, string.Empty,
                "MODULE_065_GRAPH_TOKEN_MISSING",
                "Microsoft identity did not return an application access token.");
        }

        var to = recipients.Where(item => item.RecipientType == "to")
            .Select(GraphRecipient).ToArray();
        var cc = recipients.Where(item => item.RecipientType == "cc")
            .Select(GraphRecipient).ToArray();
        var bcc = recipients.Where(item => item.RecipientType == "bcc")
            .Select(GraphRecipient).ToArray();
        if (to.Length == 0)
        {
            to = recipients.Take(1).Select(GraphRecipient).ToArray();
            var primary = to[0].emailAddress.address;
            cc = recipients.Skip(1)
                .Where(item => !item.Email.Equals(primary, StringComparison.OrdinalIgnoreCase))
                .Select(GraphRecipient).ToArray();
        }

        var bodyContent = !string.IsNullOrWhiteSpace(htmlBody) ? htmlBody : WebUtility.HtmlEncode(textBody)
            .Replace("\r\n", "<br />", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal);
        var payload = JsonSerializer.Serialize(new
        {
            message = new
            {
                subject = Limit(subject, 500),
                body = new { contentType = "HTML", content = bodyContent },
                toRecipients = to,
                ccRecipients = cc,
                bccRecipients = bcc
            },
            saveToSentItems = true
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(readiness.SenderMailbox)}/sendMail")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return new(false, "failed", "microsoft_graph", readiness.RecipientBoundary, string.Empty,
                $"MODULE_065_GRAPH_SEND_HTTP_{(int)response.StatusCode}",
                "Microsoft Graph did not accept the governed ProjectPulse notification.");
        }

        return new(
            true,
            "sent",
            "microsoft_graph",
            readiness.RecipientBoundary,
            response.Headers.TryGetValues("request-id", out var values)
                ? values.FirstOrDefault() ?? string.Empty
                : string.Empty,
            string.Empty,
            "Module 065 delivered the notification through Microsoft Graph.");
    }

    private static async Task<Module065MailDeliveryResult> DeliverSmtpAsync(
        Module065MailReadiness readiness,
        string subject,
        string textBody,
        string htmlBody,
        ProjectNotificationUser[] recipients,
        CancellationToken cancellationToken)
    {
        var host = First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_HOST"),
            "smtp.office365.com");
        var port = int.TryParse(Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PORT"), out var parsed)
            ? parsed
            : 587;
        var credential = ResolveSmtpCredential(readiness.RuntimeEnvironment);
        if (!IsEmail(readiness.SenderMailbox)
            || string.IsNullOrWhiteSpace(credential.Username)
            || string.IsNullOrWhiteSpace(credential.Password))
        {
            return new(false, "failed", "smtp_relay", readiness.RecipientBoundary, string.Empty,
                "MODULE_065_SMTP_CONFIGURATION_INCOMPLETE",
                "The Module 065 SMTP sender or environment-specific credential is incomplete.");
        }

#pragma warning disable SYSLIB0014
        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(credential.Username, credential.Password),
            Timeout = (int)NetworkTimeout.TotalMilliseconds
        };
#pragma warning restore SYSLIB0014
        using var message = new MailMessage
        {
            From = new MailAddress(readiness.SenderMailbox),
            Subject = Limit(subject, 500),
            Body = !string.IsNullOrWhiteSpace(htmlBody) ? htmlBody : textBody,
            IsBodyHtml = !string.IsNullOrWhiteSpace(htmlBody)
        };

        foreach (var recipient in recipients)
        {
            var address = new MailAddress(recipient.Email, recipient.DisplayName);
            switch (recipient.RecipientType)
            {
                case "cc": message.CC.Add(address); break;
                case "bcc": message.Bcc.Add(address); break;
                default: message.To.Add(address); break;
            }
        }
        if (message.To.Count == 0 && message.CC.Count > 0)
        {
            var first = message.CC[0];
            message.CC.Remove(first);
            message.To.Add(first);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(message, cancellationToken);
        return new(
            true,
            "sent",
            "smtp_relay",
            readiness.RecipientBoundary,
            string.Empty,
            string.Empty,
            "Module 065 delivered the notification through Microsoft 365 SMTP relay.");
    }

    private static dynamic GraphRecipient(ProjectNotificationUser recipient) => new
    {
        emailAddress = new
        {
            address = recipient.Email,
            name = string.IsNullOrWhiteSpace(recipient.DisplayName)
                ? recipient.Email
                : recipient.DisplayName
        }
    };

    private static SmtpCredential ResolveSmtpCredential(string environmentMode)
    {
        var prefix = environmentMode == "production"
            ? "PROJECTPULSE_PRODUCTION_SMTP_"
            : "PROJECTPULSE_TEST_SMTP_";
        return new(
            First(
                Environment.GetEnvironmentVariable(prefix + "USERNAME"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME"),
                Environment.GetEnvironmentVariable("SMTP_USERNAME")),
            First(
                Environment.GetEnvironmentVariable(prefix + "PASSWORD"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD"),
                Environment.GetEnvironmentVariable("SMTP_PASSWORD")));
    }

    private static string NormalizeProvider(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "microsoft_graph" => "microsoft_graph",
            "smtp_relay" or "exchange_online_smtp" or "smtp" => "smtp_relay",
            _ => "locked"
        };

    private static string NormalizeActiveProvider(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "microsoft_graph" => "microsoft_graph",
            "smtp_relay" or "exchange_online_smtp" or "smtp" => "smtp_relay",
            _ => "locked"
        };

    private static string NormalizeBoundary(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "production_governed" => "production_governed",
            "test_only" => "test_only",
            _ => "locked"
        };

    private static bool IsEmail(string value)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(value)
                && new MailAddress(value).Address.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string FriendlyDeliveryFailure(Exception exception) => exception switch
    {
        SmtpException => "The configured Module 065 SMTP transport did not accept the governed notification.",
        HttpRequestException => "The configured Module 065 Microsoft transport could not be reached.",
        _ => "The governed Module 065 notification delivery failed. Technical details remain in diagnostic evidence."
    };

    private static string Diagnostic(Exception exception) => exception switch
    {
        SmtpException smtp => $"SMTP_{smtp.StatusCode}",
        HttpRequestException => "MICROSOFT_TRANSPORT_UNAVAILABLE",
        _ => exception.GetType().Name.ToUpperInvariant()
    };

    private static string Display(string value) => value == "production"
        ? "Production"
        : value == "test" ? "Test" : "unresolved";

    private static string Limit(string value, int max) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record SmtpCredential(string Username, string Password);
}

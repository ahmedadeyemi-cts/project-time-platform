using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 065 attachment-delivery adapter for scheduled Analytics Center reports.
/// It consumes Module 065 readiness and the same environment-specific Entra/SMTP
/// profile as all other governed mail. No Analytics credential or provider setting
/// exists outside Module 065.
/// </summary>
internal static class Module065AnalyticsAttachmentDelivery
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(30);

    internal static async Task<Module065MailDeliveryResult> DeliverAsync(
        string subject,
        string textBody,
        string htmlBody,
        ProjectNotificationUser recipient,
        IReadOnlyCollection<Module065MailAttachment> attachments,
        HttpContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);
        if (!IsEmail(recipient.Email))
        {
            return new(
                false,
                "suppressed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "NO_VALID_RECIPIENTS",
                "The scheduled Analytics report did not contain an email-ready recipient.");
        }
        if (attachments.Count == 0 || attachments.Any(attachment => attachment.Content.Length == 0))
        {
            return new(
                false,
                "failed",
                readiness.ConfiguredProvider,
                readiness.RecipientBoundary,
                string.Empty,
                "ANALYTICS_ATTACHMENT_MISSING",
                "The branded Analytics attachment could not be created.");
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
                    recipient,
                    attachments,
                    cancellationToken),
                "smtp_relay" => await DeliverSmtpAsync(
                    readiness,
                    subject,
                    textBody,
                    htmlBody,
                    recipient,
                    attachments,
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
                "The Module 065 Analytics delivery operation timed out.");
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
                Friendly(exception));
        }
    }

    private static async Task<Module065MailDeliveryResult> DeliverGraphAsync(
        Module065MailReadiness readiness,
        string subject,
        string textBody,
        string htmlBody,
        ProjectNotificationUser recipient,
        IReadOnlyCollection<Module065MailAttachment> attachments,
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

        var bodyContent = !string.IsNullOrWhiteSpace(htmlBody)
            ? htmlBody
            : WebUtility.HtmlEncode(textBody)
                .Replace("\r\n", "<br />", StringComparison.Ordinal)
                .Replace("\n", "<br />", StringComparison.Ordinal);
        var graphAttachments = attachments.Select(attachment =>
            (object)new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = Limit(attachment.FileName, 240),
                ["contentType"] = Limit(attachment.ContentType, 160),
                ["contentBytes"] = Convert.ToBase64String(attachment.Content)
            }).ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            message = new
            {
                subject = Limit(subject, 500),
                body = new { contentType = "HTML", content = bodyContent },
                toRecipients = new[]
                {
                    new
                    {
                        emailAddress = new
                        {
                            address = recipient.Email,
                            name = string.IsNullOrWhiteSpace(recipient.DisplayName)
                                ? recipient.Email
                                : recipient.DisplayName
                        }
                    }
                },
                attachments = graphAttachments
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
                "Microsoft Graph did not accept the governed Analytics report email.");
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
            "Module 065 delivered the US Signal branded Analytics report through Microsoft Graph.");
    }

    private static async Task<Module065MailDeliveryResult> DeliverSmtpAsync(
        Module065MailReadiness readiness,
        string subject,
        string textBody,
        string htmlBody,
        ProjectNotificationUser recipient,
        IReadOnlyCollection<Module065MailAttachment> attachments,
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
        message.To.Add(new MailAddress(recipient.Email, recipient.DisplayName));
        var ownedStreams = new List<MemoryStream>();
        try
        {
            foreach (var attachment in attachments)
            {
                var stream = new MemoryStream(attachment.Content, writable: false);
                ownedStreams.Add(stream);
                message.Attachments.Add(new Attachment(
                    stream,
                    attachment.FileName,
                    attachment.ContentType));
            }
            cancellationToken.ThrowIfCancellationRequested();
            await smtp.SendMailAsync(message, cancellationToken);
        }
        finally
        {
            foreach (var stream in ownedStreams) stream.Dispose();
        }
        return new(
            true,
            "sent",
            "smtp_relay",
            readiness.RecipientBoundary,
            string.Empty,
            string.Empty,
            "Module 065 delivered the US Signal branded Analytics report through Microsoft 365 SMTP relay.");
    }

    private static (string Username, string Password) ResolveSmtpCredential(string environmentMode)
    {
        var prefix = environmentMode == "production"
            ? "PROJECTPULSE_PRODUCTION_SMTP_"
            : "PROJECTPULSE_TEST_SMTP_";
        return (
            First(
                Environment.GetEnvironmentVariable(prefix + "USERNAME"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME"),
                Environment.GetEnvironmentVariable("SMTP_USERNAME")),
            First(
                Environment.GetEnvironmentVariable(prefix + "PASSWORD"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD"),
                Environment.GetEnvironmentVariable("SMTP_PASSWORD")));
    }

    private static bool IsEmail(string? value)
    {
        try { _ = new MailAddress(value ?? string.Empty); return true; }
        catch { return false; }
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private static string Limit(string? value, int maximum)
    {
        var clean = (value ?? string.Empty).Replace('\0', ' ').Trim();
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        SmtpException smtp => $"MODULE_065_SMTP_{smtp.StatusCode}",
        HttpRequestException http when http.StatusCode.HasValue =>
            $"MODULE_065_HTTP_{(int)http.StatusCode.Value}",
        TimeoutException => "MODULE_065_DELIVERY_TIMEOUT",
        _ => $"MODULE_065_{exception.GetType().Name.ToUpperInvariant()}"
    };

    private static string Friendly(Exception exception) => exception switch
    {
        SmtpException => "Microsoft 365 SMTP did not accept the scheduled Analytics report.",
        HttpRequestException => "Microsoft Graph did not accept the scheduled Analytics report.",
        _ => "Module 065 could not deliver the scheduled Analytics report. Review its Entra Secret Administration and SMTP readiness."
    };
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private static object GlobalMailState()
    {
        var provider = (Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_PROVIDER")
            ?? Environment.GetEnvironmentVariable("PROJECTPULSE_EMAIL_PROVIDER")
            ?? string.Empty).Trim().ToLowerInvariant();
        var sender = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_SENDER_MAILBOX")
            ?? Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_FROM")
            ?? Environment.GetEnvironmentVariable("SMTP_FROM");
        return new
        {
            source = "Module 067 Global Mail Configuration",
            provider = string.IsNullOrWhiteSpace(provider) ? "not_configured" : provider,
            senderConfigured = !string.IsNullOrWhiteSpace(sender),
            moduleSpecificCredentials = false
        };
    }

    private static async Task QueueExpenseNotificationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid uploadId,
        ExpenseProject project,
        ExpenseActor actor,
        Guid ownerId,
        ParsedExpenseFile parsed)
    {
        var owner = await LoadUserAsync(connection, transaction, ownerId);
        if (owner is null || string.IsNullOrWhiteSpace(owner.Email)) return;
        ExpenseOwner? projectManager = null;
        if (project.ProjectManagerUserId is not null)
            projectManager = await LoadUserAsync(connection, transaction, project.ProjectManagerUserId.Value);

        var categories = parsed.Lines.GroupBy(line => line.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { category = group.Key, amount = group.Sum(line => line.Amount) })
            .OrderByDescending(row => row.amount).ToArray();
        var period = parsed.PeriodStart is null && parsed.PeriodEnd is null
            ? "Not specified"
            : $"{parsed.PeriodStart?.ToString("yyyy-MM-dd") ?? "open"} through {parsed.PeriodEnd?.ToString("yyyy-MM-dd") ?? "open"}";
        var treatment = BillingTreatment(project.ContractType);
        var treatmentText = treatment == "pass_through_invoice"
            ? "Time and Materials — reimbursable expenses are available as customer invoice pass-through costs."
            : treatment == "included_fixed_price"
                ? "Fixed Price — expenses are tracked as included project cost and are not added as a separate customer charge."
                : "Internal / non-billable tracking.";

        var subject = $"Project expenses uploaded — {project.ProjectCode} {project.ProjectName}";
        var text = new StringBuilder()
            .AppendLine($"Project expense upload summary for {owner.DisplayName}")
            .AppendLine($"Customer: {project.CustomerName}")
            .AppendLine($"Project: {project.ProjectCode} — {project.ProjectName}")
            .AppendLine($"Period: {period}")
            .AppendLine($"Uploaded by: {actor.DisplayName} ({actor.Email})")
            .AppendLine($"Uploaded at: {DateTimeOffset.UtcNow:u}")
            .AppendLine($"Source: {parsed.FormatCode}")
            .AppendLine($"Expense lines: {parsed.Lines.Count}")
            .AppendLine($"Total: {parsed.TotalAmount:C}")
            .AppendLine($"Reimbursable: {parsed.ReimbursableAmount:C}")
            .AppendLine($"Billing treatment: {treatmentText}")
            .AppendLine("Category totals:");
        foreach (var category in categories) text.AppendLine($"- {category.category}: {category.amount:C}");

        var htmlRows = string.Join(string.Empty, categories.Select(category => $"<tr><td>{WebUtility.HtmlEncode(category.category)}</td><td style=\"text-align:right\">{category.amount:C}</td></tr>"));
        var html = $"""
            <h2>Project expense upload summary</h2>
            <p><strong>Expense owner:</strong> {WebUtility.HtmlEncode(owner.DisplayName)} ({WebUtility.HtmlEncode(owner.Email)})</p>
            <p><strong>Customer:</strong> {WebUtility.HtmlEncode(project.CustomerName)}<br/>
            <strong>Project:</strong> {WebUtility.HtmlEncode(project.ProjectCode)} — {WebUtility.HtmlEncode(project.ProjectName)}<br/>
            <strong>Period:</strong> {WebUtility.HtmlEncode(period)}<br/>
            <strong>Uploaded by:</strong> {WebUtility.HtmlEncode(actor.DisplayName)} ({WebUtility.HtmlEncode(actor.Email)})<br/>
            <strong>Uploaded at:</strong> {DateTimeOffset.UtcNow:u}<br/>
            <strong>Source:</strong> {WebUtility.HtmlEncode(parsed.FormatCode)}</p>
            <table cellpadding="6" cellspacing="0" border="1"><thead><tr><th>Category</th><th>Amount</th></tr></thead><tbody>{htmlRows}</tbody></table>
            <p><strong>Total:</strong> {parsed.TotalAmount:C}<br/><strong>Reimbursable:</strong> {parsed.ReimbursableAmount:C}</p>
            <p><strong>Billing treatment:</strong> {WebUtility.HtmlEncode(treatmentText)}</p>
            """;

        var to = new[] { owner.Email };
        var cc = projectManager is not null && !string.IsNullOrWhiteSpace(projectManager.Email)
            && !projectManager.Email.Equals(owner.Email, StringComparison.OrdinalIgnoreCase)
            ? new[] { projectManager.Email }
            : Array.Empty<string>();

        await using var command = new NpgsqlCommand("""
            INSERT INTO project_expense_mail_outbox (
                project_expense_mail_outbox_id, project_expense_upload_id,
                to_addresses, cc_addresses, subject, text_body, html_body,
                delivery_status
            ) VALUES (gen_random_uuid(), @upload_id, @to, @cc, @subject, @text, @html, 'queued');
            UPDATE project_expense_uploads
            SET notification_status='queued', notification_detail='Expense summary queued through Module 067 global mail configuration.'
            WHERE project_expense_upload_id=@upload_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("upload_id", uploadId);
        command.Parameters.AddWithValue("to", to);
        command.Parameters.AddWithValue("cc", cc);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("text", text.ToString());
        command.Parameters.AddWithValue("html", html);
        await command.ExecuteNonQueryAsync();
        await InsertExpenseEventAsync(connection, transaction, uploadId, project.ProjectId, "NOTIFICATION_QUEUED", actor.ActualUserId, ownerId, string.Empty, new { to, cc, providerSource = "global_mail_configuration" });
    }

    private static async Task<IResult> RetryNotificationAsync(Guid uploadId, HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs) return ViewAsReadOnly();
        var result = await DeliverExpenseNotificationAsync(connection, uploadId, actor.ActualUserId);
        return Results.Ok(new { status = "project_expense_notification_processed", uploadId, notification = result });
    }

    private static async Task<object> DeliverExpenseNotificationAsync(NpgsqlConnection connection, Guid uploadId, Guid actorId)
    {
        MailOutboxRow? mail = null;
        await using (var command = new NpgsqlCommand("""
            SELECT project_expense_mail_outbox_id, to_addresses, cc_addresses, subject, text_body, html_body
            FROM project_expense_mail_outbox
            WHERE project_expense_upload_id=@upload_id AND delivery_status IN ('queued','configuration_pending','failed')
            ORDER BY created_at DESC LIMIT 1;
            """, connection))
        {
            command.Parameters.AddWithValue("upload_id", uploadId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                mail = new MailOutboxRow(reader.GetGuid(0), reader.GetFieldValue<string[]>(1), reader.GetFieldValue<string[]>(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
        }
        if (mail is null) return new { status = "not_queued", message = "No pending expense summary notification was found." };

        MailDelivery delivery;
        try { delivery = await SendUsingGlobalMailAsync(mail, CancellationToken.None); }
        catch (Exception exception) { delivery = new MailDelivery(false, "failed", "global_mail_configuration", string.Empty, exception.Message); }

        await using var transaction = await connection.BeginTransactionAsync();
        await using (var update = new NpgsqlCommand("""
            UPDATE project_expense_mail_outbox
            SET delivery_status=@status, delivery_attempts=delivery_attempts+1,
                provider_message_id=@message_id, last_error=@detail,
                sent_at=CASE WHEN @sent THEN NOW() ELSE sent_at END, updated_at=NOW()
            WHERE project_expense_mail_outbox_id=@outbox_id;
            UPDATE project_expense_uploads
            SET notification_status=@status, notification_detail=@detail
            WHERE project_expense_upload_id=@upload_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("status", delivery.Status);
            update.Parameters.AddWithValue("message_id", delivery.ProviderMessageId);
            update.Parameters.AddWithValue("detail", delivery.Message);
            update.Parameters.AddWithValue("sent", delivery.Success);
            update.Parameters.AddWithValue("outbox_id", mail.OutboxId);
            update.Parameters.AddWithValue("upload_id", uploadId);
            await update.ExecuteNonQueryAsync();
        }
        await using (var eventCommand = new NpgsqlCommand("""
            INSERT INTO project_expense_events (
                project_expense_event_id, project_expense_upload_id, project_id,
                event_code, actor_user_id, target_user_id, reason, event_metadata
            )
            SELECT gen_random_uuid(), upload.project_expense_upload_id, upload.project_id,
                   @event_code, @actor_id, upload.expense_owner_user_id, @detail,
                   jsonb_build_object('provider', @provider, 'providerMessageId', @message_id)
            FROM project_expense_uploads upload WHERE upload.project_expense_upload_id=@upload_id;
            """, connection, transaction))
        {
            eventCommand.Parameters.AddWithValue("event_code", delivery.Success ? "NOTIFICATION_SENT" : "NOTIFICATION_FAILED");
            eventCommand.Parameters.AddWithValue("actor_id", actorId);
            eventCommand.Parameters.AddWithValue("detail", delivery.Message);
            eventCommand.Parameters.AddWithValue("provider", delivery.Provider);
            eventCommand.Parameters.AddWithValue("message_id", delivery.ProviderMessageId);
            eventCommand.Parameters.AddWithValue("upload_id", uploadId);
            await eventCommand.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return new { status = delivery.Status, delivery.Provider, delivery.Message, sent = delivery.Success };
    }

    private static async Task<MailDelivery> SendUsingGlobalMailAsync(MailOutboxRow mail, CancellationToken cancellationToken)
    {
        var provider = (Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_PROVIDER")
            ?? Environment.GetEnvironmentVariable("PROJECTPULSE_EMAIL_PROVIDER")
            ?? string.Empty).Trim().ToLowerInvariant();
        if (provider is "microsoft_graph") return await SendGraphAsync(mail, cancellationToken);
        if (provider is "brevo_api") return await SendBrevoAsync(mail, cancellationToken);
        if (provider is "exchange_online_smtp" or "smtp") return SendSmtp(mail);
        return new MailDelivery(false, "configuration_pending", "global_mail_configuration", string.Empty, "Module 067 global mail provider is not configured for delivery.");
    }

    private static async Task<MailDelivery> SendGraphAsync(MailOutboxRow mail, CancellationToken cancellationToken)
    {
        var tenant = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_TENANT_ID") ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_ID") ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET") ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        var sender = Environment.GetEnvironmentVariable("PROJECTPULSE_M365_SENDER_MAILBOX");
        if (new[] { tenant, clientId, secret, sender }.Any(string.IsNullOrWhiteSpace))
            return new MailDelivery(false, "configuration_pending", "microsoft_graph", string.Empty, "Module 067 Microsoft Graph settings are incomplete.");

        using var client = new HttpClient();
        using var tokenResponse = await client.PostAsync($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId!, ["client_secret"] = secret!, ["scope"] = "https://graph.microsoft.com/.default", ["grant_type"] = "client_credentials"
        }), cancellationToken);
        var tokenText = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode) throw new InvalidOperationException($"Microsoft Graph token request returned HTTP {(int)tokenResponse.StatusCode}.");
        using var tokenDocument = JsonDocument.Parse(tokenText);
        var token = tokenDocument.RootElement.GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var payload = new
        {
            message = new
            {
                subject = mail.Subject,
                body = new { contentType = "HTML", content = mail.HtmlBody },
                toRecipients = mail.To.Select(address => new { emailAddress = new { address } }),
                ccRecipients = mail.Cc.Select(address => new { emailAddress = new { address } })
            },
            saveToSentItems = true
        };
        using var response = await client.PostAsJsonAsync($"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(sender!)}/sendMail", payload, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Microsoft Graph sendMail returned HTTP {(int)response.StatusCode}.");
        var requestId = response.Headers.TryGetValues("request-id", out var values) ? values.FirstOrDefault() ?? string.Empty : string.Empty;
        return new MailDelivery(true, "sent", "microsoft_graph", requestId, "Expense summary sent through Module 067 Microsoft Graph configuration.");
    }

    private static async Task<MailDelivery> SendBrevoAsync(MailOutboxRow mail, CancellationToken cancellationToken)
    {
        var key = Environment.GetEnvironmentVariable("PROJECTPULSE_BREVO_API_KEY") ?? Environment.GetEnvironmentVariable("BREVO_API_KEY");
        var sender = Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_FROM") ?? Environment.GetEnvironmentVariable("SMTP_FROM");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(sender))
            return new MailDelivery(false, "configuration_pending", "brevo_api", string.Empty, "Module 067 Brevo settings are incomplete.");
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("api-key", key);
        var payload = new
        {
            sender = new { email = sender, name = "Pulse" },
            to = mail.To.Select(address => new { email = address }),
            cc = mail.Cc.Select(address => new { email = address }),
            subject = mail.Subject,
            htmlContent = mail.HtmlBody,
            textContent = mail.TextBody
        };
        using var response = await client.PostAsJsonAsync("https://api.brevo.com/v3/smtp/email", payload, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Brevo delivery returned HTTP {(int)response.StatusCode}.");
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        return new MailDelivery(true, "sent", "brevo_api", responseText, "Expense summary sent through Module 067 Brevo configuration.");
    }

    private static MailDelivery SendSmtp(MailOutboxRow mail)
    {
        var host = Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_HOST") ?? Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.office365.com";
        var from = Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_FROM") ?? Environment.GetEnvironmentVariable("SMTP_FROM");
        var user = Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME") ?? Environment.GetEnvironmentVariable("SMTP_USERNAME");
        var password = Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD") ?? Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            return new MailDelivery(false, "configuration_pending", "exchange_online_smtp", string.Empty, "Module 067 SMTP settings are incomplete.");
        using var message = new MailMessage { From = new MailAddress(from), Subject = mail.Subject, Body = mail.HtmlBody, IsBodyHtml = true };
        foreach (var address in mail.To) message.To.Add(address);
        foreach (var address in mail.Cc) message.CC.Add(address);
        using var smtp = new SmtpClient(host, int.TryParse(Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PORT"), out var port) ? port : 587)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(user, password)
        };
        smtp.Send(message);
        return new MailDelivery(true, "sent", "exchange_online_smtp", string.Empty, "Expense summary sent through Module 067 SMTP configuration.");
    }

    private static async Task<ExpenseOwner?> LoadUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId)
    {
        await using var command = new NpgsqlCommand("SELECT user_id, COALESCE(display_name,email,''), COALESCE(email,'') FROM app_users WHERE user_id=@user_id;", connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new ExpenseOwner(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), Array.Empty<string>()) : null;
    }
}

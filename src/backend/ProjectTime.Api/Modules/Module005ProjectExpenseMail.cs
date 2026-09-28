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

    private static Task<object> DeliverExpenseNotificationAsync(NpgsqlConnection connection, Guid uploadId, Guid actorId)
        => EnterpriseNotificationOrchestrationService.QueueExpenseUploadAsync(connection, uploadId, actorId, CancellationToken.None);

    private static async Task<ExpenseOwner?> LoadUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId)
    {
        await using var command = new NpgsqlCommand("SELECT user_id, COALESCE(display_name,email,''), COALESCE(email,'') FROM app_users WHERE user_id=@user_id;", connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new ExpenseOwner(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), Array.Empty<string>()) : null;
    }
}

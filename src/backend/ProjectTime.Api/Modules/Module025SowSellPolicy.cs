using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

public sealed record Module025SowReleaseRequest(int ExpectedRevision);
public sealed record Module025SowSellSubmitRequest(int ExpectedRevision, Guid VersionId);

internal sealed record Module025SellRecipient(Guid UserId, string DisplayName, string Email, string Role, string RecipientType);
internal sealed record Module025SellReadiness(bool Ready, string DiagnosticCode, string Message);
internal sealed record Module025SellPackage(
    Guid SubmissionId, Guid EngagementId, Guid VersionId, int VersionNumber,
    string EngagementNumber, string CustomerName, string RuntimeEnvironment,
    string? ExistingSellRecordId, byte[] SowContent, byte[] GsdContent,
    string SowSha256, string GsdSha256, IReadOnlyList<Module025SellRecipient> Recipients)
{
    public string RecordKey => $"module025:{RuntimeEnvironment}:{EngagementId:N}";
    public string IdempotencyKey => $"{RecordKey}:{VersionId:N}";
    public string SowFileName => $"{EngagementNumber}-v{VersionNumber}-SOW.docx";
    public string GsdFileName => $"{EngagementNumber}-v{VersionNumber}-GSD.xlsx";
}
internal sealed record Module025SellReceipt(
    Guid SubmissionId, Guid VersionId, string SellRecordId,
    string SowDocumentId, string GsdDocumentId, string SowSha256, string GsdSha256,
    string ProviderReceiptId);
internal enum Module025SellOutcomeKind { RejectedBeforeWrite, Accepted, NeedsReconciliation }
internal sealed record Module025SellPublishOutcome(
    Module025SellOutcomeKind Kind, Module025SellReceipt? Receipt, string DiagnosticCode);

/// <summary>
/// An adapter must use Module 026 credentials/configuration, bind the destination
/// to runtimeEnvironment, upsert by RecordKey, append both documents, and read
/// them back before returning Accepted. It must reconcile IdempotencyKey after
/// an ambiguous response. Browser assertions and guessed vendor URLs are not
/// acceptable receipts. The default adapter below intentionally cannot publish.
/// </summary>
internal interface IModule025SellPublisher
{
    Task<Module025SellReadiness> GetReadinessAsync(string runtimeEnvironment, CancellationToken cancellationToken);
    Task<Module025SellPublishOutcome> PublishAsync(Module025SellPackage package, CancellationToken cancellationToken);
}

internal sealed class Module025ZendeskSellPublisher : IModule025SellPublisher
{
    internal const string Blocker = "SELL_DOCUMENT_WRITE_ADAPTER_REQUIRED";
    public Task<Module025SellReadiness> GetReadinessAsync(string runtimeEnvironment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new Module025SellReadiness(false, Blocker,
            "The existing SELL connection supports reads. An approved SELL record-and-document write adapter is required before automatic submission can be enabled. No SELL record or email has been created."));
    }
    public Task<Module025SellPublishOutcome> PublishAsync(Module025SellPackage package, CancellationToken cancellationToken) =>
        Task.FromResult(new Module025SellPublishOutcome(Module025SellOutcomeKind.RejectedBeforeWrite, null, Blocker));
}

internal static class Module025SowSellPolicy
{
    internal const string DestinationKey = "zendesk_sell";
    internal const int MaximumArtifactBytes = 16 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The editor owns reviewed phase content. Do not reintroduce stale AI-only
    // summary arrays when the SA has edited those same requirements in a phase.
    internal static Module025EngagementRow ReviewedContent(Module025EngagementRow engagement)
    {
        var sections = engagement.SowSections.ValueKind == JsonValueKind.Object
            ? engagement.SowSections.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var phases = engagement.Phases.OrderBy(p => p.SortOrder).ToArray();
        sections["deliverables"] = JsonSerializer.SerializeToElement(phases.SelectMany(p => p.Deliverables).Distinct().ToArray());
        sections["customerResponsibilities"] = JsonSerializer.SerializeToElement(phases.SelectMany(p => p.CustomerResponsibilities).Distinct().ToArray());
        sections["assumptions"] = JsonSerializer.SerializeToElement(phases.SelectMany(p => p.Assumptions).Distinct().ToArray());
        sections["dependencies"] = JsonSerializer.SerializeToElement(phases.SelectMany(p => p.Dependencies).Distinct().ToArray());
        return engagement with { SowSections = JsonSerializer.SerializeToElement(sections), Phases = phases };
    }

    internal static string Fingerprint(Module025EngagementRow source)
    {
        var e = ReviewedContent(source);
        // Working-copy revision/status/timestamps do not make a new document.
        // A changed field or phase does. Reverting after another release creates
        // a new version because only the latest fingerprint is deduplicated.
        var content = JsonSerializer.SerializeToElement(new
        {
            e.EngagementId, e.EngagementNumber, e.OwnerUserId, e.OwnerDisplayName,
            e.OwnerDepartmentName, e.OwnerTeamName, e.CustomerId, e.CustomerName,
            e.CustomerEntryMode, e.CommercialModel, e.CustomerProgram, e.GsdTemplateKey,
            e.AccountExecutiveUserId, e.AccountExecutiveName, e.ResaleUserId, e.ResaleName,
            e.ServiceOverview, e.SowSections, e.AiMetadata,
            phases = e.Phases.Select(p => new
            {
                p.PhaseCode, p.SortOrder, p.SuggestedHours, p.FinalHours, p.Objective,
                p.DetailedActivities, p.TechnicalTasks, p.Deliverables, p.CustomerResponsibilities,
                p.UsSignalResponsibilities, p.Prerequisites, p.Dependencies, p.Assumptions,
                p.OpenQuestions, p.AcceptanceCriteria, p.ValidationSteps, p.Risks,
                p.LoeRationale, p.SourceCitationIds, p.AiGenerated
            })
        }, Json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, content);
        return Hash(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var child in value.EnumerateArray()) WriteCanonical(writer, child);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
        }
        else value.WriteTo(writer);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static bool ValidEmail(string? value) => !string.IsNullOrWhiteSpace(value)
        && MailAddress.TryCreate(value.Trim(), out var address)
        && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static bool ValidReceipt(Module025SellPackage package, Module025SellReceipt? receipt) =>
        receipt is not null
        && receipt.SubmissionId == package.SubmissionId && receipt.VersionId == package.VersionId
        && UsableId(receipt.SellRecordId) && UsableId(receipt.SowDocumentId)
        && UsableId(receipt.GsdDocumentId) && UsableId(receipt.ProviderReceiptId)
        && receipt.SowDocumentId != receipt.GsdDocumentId
        && string.Equals(receipt.SowSha256, package.SowSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(receipt.GsdSha256, package.GsdSha256, StringComparison.OrdinalIgnoreCase)
        && Hash(package.SowContent) == package.SowSha256 && Hash(package.GsdContent) == package.GsdSha256
        && (string.IsNullOrEmpty(package.ExistingSellRecordId) || receipt.SellRecordId == package.ExistingSellRecordId);

    private static bool UsableId(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 200 && !value.Any(char.IsControl) && value == value.Trim();

    internal static (string Subject, string Body) Notification(Module025SellPackage package, Module025SellReceipt receipt)
    {
        if (!ValidReceipt(package, receipt)) throw new InvalidOperationException("A verified two-document SELL receipt is required.");
        var sa = package.Recipients.Single(p => p.Role == "solution_architect");
        var inside = package.Recipients.Single(p => p.Role == "inside_sales");
        var ae = package.Recipients.Single(p => p.Role == "account_executive");
        var updated = !string.IsNullOrWhiteSpace(package.ExistingSellRecordId);
        var verb = updated ? "updated in" : "created in";
        var subject = $"SOW and GSD {verb} SELL | {package.EngagementNumber} v{package.VersionNumber} | {package.CustomerName}";
        subject = new string(subject.Where(c => !char.IsControl(c)).ToArray());
        if (subject.Length > 500) subject = subject[..500];
        var upload = updated ? "has uploaded an updated SOW and GSD to the existing record in SELL" : "has uploaded a SOW and GSD in SELL";
        var body = $"{sa.DisplayName} {upload} for customer \"{package.CustomerName}\".\n\n"
            + $"{inside.DisplayName}, please can you assist in processing this quote?\n\n"
            + $"SOW record: {package.EngagementNumber}\nVersion: {package.VersionNumber}\nSELL record: {receipt.SellRecordId}\n"
            + $"Solution Architect: {sa.DisplayName}\nAccount Executive: {ae.DisplayName}\nInside Sales Representative: {inside.DisplayName}\n\n"
            + "Both the SOW and GSD for this version have been verified in SELL. Earlier versions remain retained in the SOW Register.";
        return (subject, body);
    }

    internal static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        var leading = text.TrimStart();
        if (leading.Length > 0 && "=+-@".Contains(leading[0]) || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

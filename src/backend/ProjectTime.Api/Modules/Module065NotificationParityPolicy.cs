using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

// Pure rules shared by production fan-out and synthetic regression tests.
internal static class Module065NotificationParityPolicy
{
    internal const int CallsPerFiveMinutes = 20; // Leave headroom under the Flow bot connector limit.
    internal const int MaximumAttempts = 5;
    internal static bool MaySend(string dispatchBoundary, string servicesBoundary, string environment,
        string configuredEnvironment, bool enabled, bool isViewAs) =>
        enabled && !isViewAs && environment is "test" or "production"
        && environment == configuredEnvironment && dispatchBoundary == "production_governed"
        && servicesBoundary == "production_governed";

    // A quiet-hours pause is not a revoked delivery boundary. No transport is invoked
    // while deferred; a later attempt still reloads the real provider configuration.
    internal static string MailBoundary(string source, string transport, bool current, bool deferred)
    {
        if (!current || source is not ("production_governed" or "test_only")) return "locked";
        if (deferred) return source;
        if (transport == "locked") return "locked";
        return source == "production_governed" && transport == "production_governed" ? "production_governed" : "test_only";
    }

    internal static string[] Recipients(IEnumerable<string> addresses) => addresses
        .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 320
            && System.Net.Mail.MailAddress.TryCreate(value.Trim(), out var address)
            && address.Address == value.Trim())
        .Select(value => value.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal).ToArray();

    internal static async Task<T> IndependentChannelsAsync<T>(Func<Task> queueTeams, Func<Task<T>> sendEmail)
    {
        try { await queueTeams(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Teams queue requires attention: {0}", error.GetType().Name); }
        return await sendEmail();
    }

    internal static Guid EventId(string source, Guid id) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"pulse:teams:email-mirror:{source}:{id:D}"))[..16]);

    internal static string RecipientKey(Guid dispatchId, string email) =>
        $"{dispatchId:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()))).ToLowerInvariant()}";

    // Plain-text email content is inserted into the existing HTML Compose safely.
    // Bound *encoded* content so the composed message stays well below Teams' ~28KB limit.
    internal static string HtmlText(string? value, int maximumBytes = 12000)
    {
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in (value ?? "").EnumerateRunes())
        {
            if (rune.Value == '\r') continue;
            var text = rune.Value == '\n' ? "<br />" : WebUtility.HtmlEncode(rune.ToString());
            var size = Encoding.UTF8.GetByteCount(text);
            if (bytes + size > maximumBytes) { result.Append("<br />[Message shortened. Open Pulse for the full details.]"); break; }
            result.Append(text); bytes += size;
        }
        return result.ToString();
    }

    internal static string TerminalStatus(string status, string code, int attempt) =>
        status == "sent" ? "accepted"
        : code == "teams_workflow_rate_limited" && attempt < MaximumAttempts ? "retry_wait"
        : status == "outcome_unknown" ? "outcome_unknown" : "failed";

    internal static string Text(JsonElement metadata, string name) =>
        metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(name, out var item)
            && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";

    internal static string Link(string? baseUrl, string? fragment, string environment)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            return environment == "test" ? "https://phd-west-test.onenecklab.com/#dashboard" : "";
        // Only an in-app fragment may be selected by an event. Never accept another origin or token URL.
        var safe = !string.IsNullOrWhiteSpace(fragment) && fragment.StartsWith('#')
            && fragment.Length <= 800 && fragment.All(c => char.IsAsciiLetterOrDigit(c) || "#-_.~/?=&:%".Contains(c)) ? fragment : "#dashboard";
        return new Uri(uri, "/").AbsoluteUri + safe;
    }
}

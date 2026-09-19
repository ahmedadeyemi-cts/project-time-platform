using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>ConnectWise SELL (CPQ) API-key authentication; never a Zendesk bearer token.</summary>
internal static class ConnectWiseSellApi
{
    internal const string ProviderKey = "connectwise_sell";
    internal const string DisplayName = "ConnectWise SELL";
    internal const string BaseUrl = "https://sellapi.quosalsell.com";
    internal const string HealthUrl = BaseUrl + "/api/quotes?page=1&pageSize=1";
    internal const string LookupUrl = BaseUrl + "/api/quotes/{recordId}";
    // Quote items are a separate resource. Do not invent a labor-rate mapping.
    internal const string ImportMapping = """{"projectNamePath":"name","quoteNumberPath":"quoteNumber","customerNamePath":"accountName","contractedAmountPath":"quoteTotal"}""";
    internal const string CustomerSyncMessage = "ConnectWise SELL customer synchronization requires a quote-customer adapter. The Module 026 connection test verifies API access only; customer sync is not yet available. Existing customers and local contacts are preserved.";

    internal static bool TryPackCredential(string? accessKey, string? publicKey, string? privateKey, out string secret)
    {
        secret = string.Empty;
        if (!ValidPart(accessKey, true) || !ValidPart(publicKey, true) || !ValidPart(privateKey, false)) return false;
        secret = JsonSerializer.Serialize(new Credential(accessKey!.Trim(), publicKey!.Trim(), privateKey!.Trim()));
        return true;
    }

    private static bool ValidPart(string? value, bool usernamePart) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 2048
        && !value.Any(char.IsControl)
        && (!usernamePart || !value.Any(character => character is ':' or '+' || char.IsWhiteSpace(character)));

    internal static bool TryAuthorize(HttpRequestMessage request, string? secret)
    {
        // Pin the credential destination independently of editable provider configuration.
        if (!IsApiUri(request.RequestUri) || string.IsNullOrWhiteSpace(secret)) return false;
        try
        {
            var credential = JsonSerializer.Deserialize<Credential>(secret);
            if (credential is null || !TryPackCredential(credential.AccessKey, credential.PublicKey, credential.PrivateKey, out _)) return false;
            var bytes = Encoding.UTF8.GetBytes($"{credential.AccessKey}+{credential.PublicKey}:{credential.PrivateKey}");
            try
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new ByteArrayContent([]);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; version=1.0");
            return true;
        }
        catch (JsonException) { return false; }
    }

    internal static bool IsApiUri(Uri? uri) => uri is not null && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("sellapi.quosalsell.com", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (uri.AbsolutePath == "/api/quotes" || uri.AbsolutePath.StartsWith("/api/quotes/", StringComparison.Ordinal));

    internal static bool IsQuoteList(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(id.GetString()));
        }
        catch (JsonException) { return false; }
    }

    private sealed record Credential(string AccessKey, string PublicKey, string PrivateKey);
}

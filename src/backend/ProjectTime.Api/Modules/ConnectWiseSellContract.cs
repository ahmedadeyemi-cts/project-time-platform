using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>ConnectWise SELL/CPQ REST authentication; never reuse legacy CRM tokens.</summary>
internal static class ConnectWiseSellContract
{
    internal const string DisplayName = "ConnectWise SELL";
    internal const string ProviderKey = "connectwise_sell";
    internal const string LegacyProviderKey = "zendesk_sell";
    internal const string BaseUrl = "https://sellapi.quosalsell.com";
    internal const string HealthUrl = BaseUrl + "/api/quotes?page=1&pageSize=1&includeFields=id";
    internal const string LookupUrl = BaseUrl + "/api/quotes/{recordId}";
    internal const string CustomerSyncBlocker = "connectwise_customer_adapter_required";
    internal const string CustomerSyncMessage = "ConnectWise SELL customer synchronization requires a verified customer identity mapping. The previous contacts adapter is disabled. Local customers and contacts remain available.";

    // Restrict the built-in connector before loading any credential. Generic custom
    // connectors continue using the existing Module 026 public-address policy.
    internal static bool IsEndpoint(Uri? uri) => uri is not null && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443
        && uri.IdnHost.Equals("sellapi.quosalsell.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment)
        && (uri.AbsolutePath == "/api/quotes" || uri.AbsolutePath.StartsWith("/api/quotes/", StringComparison.Ordinal));

    internal static bool IsConfiguration(string? auth, string? baseUrl, string? healthUrl,
        string? header, string? prefix, string? lookup, string? authorizationUrl, string? tokenUrl)
        => auth == "api_key" && baseUrl?.TrimEnd('/') == BaseUrl
        && healthUrl == HealthUrl && header == "Authorization" && prefix == "Basic"
        && (string.IsNullOrEmpty(lookup) || lookup == LookupUrl)
        && string.IsNullOrEmpty(authorizationUrl) && string.IsNullOrEmpty(tokenUrl);

    internal static bool TryCreateCredential(string? accessKey, string? publicKey, string? privateKey, out string envelope)
    {
        envelope = string.Empty;
        accessKey = accessKey?.Trim(); publicKey = publicKey?.Trim(); privateKey = privateKey?.Trim();
        if (!ValidPart(accessKey) || !ValidPart(publicKey) || !ValidPart(privateKey)
            || accessKey!.Contains('+') || publicKey!.Contains('+')) return false;
        envelope = JsonSerializer.Serialize(new { accessKey, publicKey, privateKey });
        return true;
    }

    private static bool ValidPart(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 1024 && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == ':');

    internal static bool TryAuthorize(HttpRequestMessage request, string envelope)
    {
        if (!IsEndpoint(request.RequestUri)) return false;
        try
        {
            using var document = JsonDocument.Parse(envelope);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            string? Read(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var access = Read("accessKey"); var pub = Read("publicKey"); var priv = Read("privateKey");
            if (!TryCreateCredential(access, pub, priv, out _)) return false;
            var bytes = Encoding.UTF8.GetBytes($"{access!.Trim()}+{pub!.Trim()}:{priv!.Trim()}");
            try { request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes)); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            request.Headers.Accept.ParseAdd("application/json");
            request.Content = new ByteArrayContent([]);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; version=1.0");
            return true;
        }
        catch (JsonException) { return false; }
    }

    internal static bool IsQuoteList(string? body)
    {
        if (body is null) return false;
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
}

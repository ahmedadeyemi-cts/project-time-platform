using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed record ProjectPulseAiAvailableModel(
    string Id,
    string DisplayName,
    int? InputTokenLimit,
    int? OutputTokenLimit,
    string Family,
    string CostGuidance,
    string PricingUrl);

public sealed record ProjectPulseAiModelCatalogResult(
    string Provider,
    string Status,
    DateTimeOffset? CheckedAt,
    IReadOnlyList<ProjectPulseAiAvailableModel> Models,
    string? Diagnostic,
    string ActiveModel,
    string Message);

/// <summary>
/// Reads Google's model inventory with Module 064's saved credential. Inventory
/// availability is separate from generation readiness: discovery never changes
/// the selected model, resets its circuit, or sends a generation request.
/// </summary>
public sealed class ProjectPulseAiModelCatalog(
    IHttpClientFactory clients,
    ProjectPulseAiConfiguration configuration,
    TimeProvider? clock = null)
{
    // The named client must have automatic redirects disabled. This prevents the
    // API key header from being forwarded to another location by HttpClient.
    public const string ClientName = "projectpulse-ai-model-catalog";
    public const string GeminiPricingUrl = "https://ai.google.dev/gemini-api/docs/pricing";
    private const int MaximumPageBytes = 1_048_576;
    private const int MaximumPages = 20;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly Regex ModelId = new("\\Agemini-[a-zA-Z0-9][a-zA-Z0-9._-]{0,119}\\z", RegexOptions.CultureInvariant);
    private static readonly string[] UnsupportedModelKinds = ["embedding", "live", "image", "tts", "audio", "robotics", "computer-use", "deep-research"];
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public async Task<ProjectPulseAiModelCatalogResult> GetAsync(string provider, bool refresh, CancellationToken cancellationToken)
    {
        if (!string.Equals(provider, ProjectPulseAiProviders.Gemini, StringComparison.OrdinalIgnoreCase))
            return new(provider, "unsupported", null, [], "model_discovery_not_supported", "",
                "Automatic discovery is available for Gemini. Other providers use their approved model list.");

        var current = configuration.Provider(ProjectPulseAiProviders.Gemini);
        if (!current.Configured)
            return new(current.Code, "not_configured", null, [], "provider_not_configured", current.Model,
                "Save a Gemini API key in Module 064 to discover available models.");
        if (!IsSupportedEndpoint(current.Endpoint))
            return new(current.Code, "unavailable", null, [], "gemini_model_catalog_endpoint_unsupported", current.Model,
                "Model discovery requires the Google Gemini API endpoint.");

        // Use the complete credential hash rather than the short, displayed key
        // fingerprint. Rotating a credential must invalidate its inventory cache.
        var credentialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(current.ApiKey!)));
        var cacheKey = current.Endpoint.TrimEnd('/') + ":" + credentialHash;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!CredentialStillCurrent(current)) return CredentialChanged();
            var now = _clock.GetUtcNow();
            if (_cache.TryGetValue(cacheKey, out var entry)
                && now < (refresh ? entry.RefreshAfter : entry.ExpiresAt))
                return WithCurrentSelection(entry.Result);

            var result = await DiscoverAsync(current, cancellationToken);
            if (!CredentialStillCurrent(current)) return CredentialChanged();
            now = _clock.GetUtcNow();
            var refreshAfter = now + (result.RetryAfter ?? MinimumRefreshInterval);
            var expiresAt = result.Result.Status == "available" ? now + CacheLifetime : refreshAfter;
            // Only a few credential rotations can be retained in memory.
            if (_cache.Count >= 8 && !_cache.ContainsKey(cacheKey))
                _cache.Remove(_cache.MinBy(pair => pair.Value.ExpiresAt).Key);
            _cache[cacheKey] = new(result.Result, refreshAfter, expiresAt);
            return WithCurrentSelection(result.Result);
        }
        finally
        {
            _gate.Release();
        }

        bool CredentialStillCurrent(ProjectPulseAiProviderConfiguration captured)
        {
            var latest = configuration.Provider(ProjectPulseAiProviders.Gemini);
            return latest.ApiKey == captured.ApiKey && latest.Endpoint == captured.Endpoint;
        }
        ProjectPulseAiModelCatalogResult CredentialChanged() => new(current.Code, "unavailable", _clock.GetUtcNow(), [],
            "gemini_model_catalog_credential_changed", configuration.Provider(ProjectPulseAiProviders.Gemini).Model,
            "The saved Gemini credential changed during discovery. Refresh the model list to use the new credential.");
    }

    public async Task<bool> IsAvailableAsync(string provider, string model, CancellationToken cancellationToken)
    {
        if (!IsGeminiTextModelId(model) || !ProjectPulseAiConfiguration.GeminiModelPermitted(model)) return false;
        var result = await GetAsync(provider, false, cancellationToken);
        return result.Status == "available" && result.Models.Any(item => string.Equals(item.Id, model, StringComparison.Ordinal));
    }

    public static bool IsGeminiTextModelId(string? model) => model is not null
        && ModelId.IsMatch(model)
        && !UnsupportedModelKinds.Any(kind => model.Contains(kind, StringComparison.OrdinalIgnoreCase));

    private ProjectPulseAiModelCatalogResult WithCurrentSelection(ProjectPulseAiModelCatalogResult result) => result with
    {
        ActiveModel = configuration.Provider(ProjectPulseAiProviders.Gemini).Model,
        Models = result.Models.Where(model => ProjectPulseAiConfiguration.GeminiModelPermitted(model.Id)).ToArray()
    };

    private async Task<DiscoveryResult> DiscoverAsync(ProjectPulseAiProviderConfiguration provider, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var checkedAt = _clock.GetUtcNow();
        var models = new Dictionary<string, ProjectPulseAiAvailableModel>(StringComparer.Ordinal);
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        try
        {
            using var client = clients.CreateClient(ClientName);
            for (var page = 0; page < MaximumPages; page++)
            {
                var url = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200";
                if (pageToken is not null) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-goog-api-key", provider.ApiKey);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if ((int)response.StatusCode is >= 300 and < 400
                    || response.RequestMessage?.RequestUri is { } actual && actual != request.RequestUri)
                    return Failure("gemini_model_catalog_redirect_rejected", "Google returned an unexpected redirect. Model discovery did not complete.");
                if (!response.IsSuccessStatusCode)
                {
                    TimeSpan? retry = response.StatusCode == HttpStatusCode.TooManyRequests ? RetryDelay(response) : null;
                    var message = response.StatusCode switch
                    {
                        HttpStatusCode.TooManyRequests => "Google rate-limited model discovery (HTTP 429). Check the API project's quota and billing, then retry after the cooldown. This does not indicate which models have generation quota.",
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Google rejected access to model discovery. Check the saved API key and API project permissions.",
                        _ => "Google's model list is temporarily unavailable. The saved model has not changed."
                    };
                    return Failure($"gemini_model_catalog_http_{(int)response.StatusCode}", message, retry);
                }
                if (response.Content.Headers.ContentLength > MaximumPageBytes)
                    return Failure("gemini_model_catalog_response_too_large", "Google's model list exceeded the supported response size.");
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(chunk, deadline.Token)) > 0)
                {
                    if (buffer.Length + read > MaximumPageBytes)
                        return Failure("gemini_model_catalog_response_too_large", "Google's model list exceeded the supported response size.");
                    buffer.Write(chunk, 0, read);
                }
                using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("models", out var items) || items.ValueKind != JsonValueKind.Array)
                    return Failure("gemini_model_catalog_invalid_response", "Google returned an invalid model list. The saved model has not changed.");
                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var name = String(item, "name");
                    if (name is null || !name.StartsWith("models/", StringComparison.Ordinal)) continue;
                    var id = name[7..];
                    if (!IsGeminiTextModelId(id) || !ProjectPulseAiConfiguration.GeminiModelPermitted(id)
                        || !item.TryGetProperty("supportedGenerationMethods", out var methods)
                        || methods.ValueKind != JsonValueKind.Array
                        || !methods.EnumerateArray().Any(method => method.ValueKind == JsonValueKind.String && method.GetString() == "generateContent")) continue;
                    var family = Family(id);
                    var displayName = String(item, "displayName");
                    models[id] = new(id,
                        displayName is { Length: > 0 and <= 128 } && !displayName.Any(char.IsControl) ? displayName : id,
                        PositiveInteger(item, "inputTokenLimit"), PositiveInteger(item, "outputTokenLimit"), family,
                        family + " family. Google does not include prices in model discovery; compare current pricing before selecting.", GeminiPricingUrl);
                }
                if (root.TryGetProperty("nextPageToken", out var next) && next.ValueKind != JsonValueKind.String)
                    return Failure("gemini_model_catalog_invalid_response", "Google returned an invalid pagination token.");
                pageToken = String(root, "nextPageToken");
                if (string.IsNullOrEmpty(pageToken))
                {
                    return new(new(provider.Code, "available", checkedAt,
                        models.Values.OrderBy(item => FamilyOrder(item.Family)).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                        null, provider.Model,
                        models.Count == 0
                            ? "No compatible Gemini text-generation models are available within this deployment's model policy. The saved model has not changed."
                            : "Models were discovered using the saved Gemini key. Discovery does not verify generation quota or pricing. Select a model and use Save and test to activate it."), null);
                }
                if (pageToken.Length > 4096 || !seenPageTokens.Add(pageToken))
                    return Failure("gemini_model_catalog_invalid_pagination", "Google returned an invalid or repeated pagination token.");
            }
            return Failure("gemini_model_catalog_page_limit", "Google's model list exceeded the supported page limit. No partial inventory was adopted.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("gemini_model_catalog_timeout", "Google's model discovery timed out. The saved model has not changed.");
        }
        catch (HttpRequestException)
        {
            return Failure("gemini_model_catalog_unavailable", "Google's model discovery could not be reached. The saved model has not changed.");
        }
        catch (JsonException)
        {
            return Failure("gemini_model_catalog_invalid_response", "Google returned an invalid model list. The saved model has not changed.");
        }
        catch (IOException)
        {
            return Failure("gemini_model_catalog_unavailable", "Google's model discovery response was interrupted. The saved model has not changed.");
        }

        DiscoveryResult Failure(string code, string message, TimeSpan? retryAfter = null) =>
            new(new(provider.Code, "unavailable", checkedAt, [], code, provider.Model, message), retryAfter);
    }

    private TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        var seconds = retry?.Delta?.TotalSeconds ?? (retry?.Date - _clock.GetUtcNow())?.TotalSeconds ?? 60;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 30, 3600));
    }

    private static bool IsSupportedEndpoint(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "generativelanguage.googleapis.com" && uri.Port == 443
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath.TrimEnd('/') == "/v1beta/openai";

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? PositiveInteger(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) && number > 0 ? number : null;

    private static string Family(string id) => id.Contains("flash-lite", StringComparison.OrdinalIgnoreCase) ? "Flash Lite"
        : id.Contains("flash", StringComparison.OrdinalIgnoreCase) ? "Flash"
        : id.Contains("pro", StringComparison.OrdinalIgnoreCase) ? "Pro" : "Gemini";

    private static int FamilyOrder(string family) => family switch { "Flash Lite" => 0, "Flash" => 1, "Pro" => 2, _ => 3 };
    private sealed record CacheEntry(ProjectPulseAiModelCatalogResult Result, DateTimeOffset RefreshAfter, DateTimeOffset ExpiresAt);
    private sealed record DiscoveryResult(ProjectPulseAiModelCatalogResult Result, TimeSpan? RetryAfter);
}

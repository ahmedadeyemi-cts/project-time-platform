using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

// Only known protocol categories and parsed deadlines leave this adapter. Google
// error messages/metadata can contain project IDs, prompts or credential details.
internal static class ProjectPulseGeminiErrors
{
    internal static ProjectPulseAiProviderResult Parse(HttpResponseMessage response, string body, DateTimeOffset now)
    {
        var status = (int)response.StatusCode;
        var code = "gemini_http_" + status;
        var retry = RetryHeader(response, now);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            string? quotaCategory = null;
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("details", out var details)
                    && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var type = Text(detail, "@type");
                        if (type == "type.googleapis.com/google.rpc.RetryInfo"
                            && Delay(Text(detail, "retryDelay")) is { } delay)
                            retry = Later(retry, now.Add(delay));
                        if (type == "type.googleapis.com/google.rpc.QuotaFailure"
                            && detail.TryGetProperty("violations", out var violations) && violations.ValueKind == JsonValueKind.Array)
                            foreach (var violation in violations.EnumerateArray())
                            {
                                var quota = (Text(violation, "quotaId") ?? "") + " " + (Text(violation, "quotaMetric") ?? "");
                                if (quota.Contains("PerDay", StringComparison.OrdinalIgnoreCase)
                                    || quota.Contains("per_day", StringComparison.OrdinalIgnoreCase))
                                    quotaCategory = "daily_quota";
                                else if (quotaCategory != "daily_quota" && (quota.Contains("PerMinute", StringComparison.OrdinalIgnoreCase)
                                    || quota.Contains("per_minute", StringComparison.OrdinalIgnoreCase)))
                                    quotaCategory = "rate_limit";
                            }
                        if (type == "type.googleapis.com/google.rpc.ErrorInfo" && quotaCategory is null
                            && Text(detail, "reason") is "QUOTA_EXCEEDED" or "RATE_LIMIT_EXCEEDED")
                            quotaCategory = Text(detail, "reason") == "QUOTA_EXCEEDED" ? "quota_exhausted" : "rate_limit";
                    }
                }
            }
            catch (JsonException) { }
            if (quotaCategory is not null) code += "_" + quotaCategory;
            // A missing RetryInfo never means immediately retrying all nine
            // readiness checks. The next probe waits at least one minute.
            retry = Later(retry, now.AddSeconds(60));
        }
        var limits = retry is null ? null : new ProjectPulseAiRateLimits(null, null, retry.Value.ToString("O"), null);
        return new(ProjectPulseAiProviders.Gemini, ProjectPulseAiOutcomes.Unavailable, null, code,
            Message(code) ?? "Gemini could not complete this request.", RequestId(response), null, status, limits)
            { RetryAfterUtc = retry };
    }

    internal static string? Message(string? code) => NormalizeCode(code) switch
    {
        "gemini_http_429" => "Google rejected the request because a rate or quota limit was reached. Check the selected model's limits and billing in Google AI Studio; the response did not identify which limit.",
        "gemini_http_429_daily_quota" => "Google reported a daily quota limit for this model. Check the project's quota and billing in Google AI Studio before retrying.",
        "gemini_http_429_rate_limit" => "Google reported a request or token rate limit. Pulse will wait until the retry time before checking this provider again.",
        "gemini_http_429_quota_exhausted" => "Google reported exhausted quota. Check the project's model quota and billing in Google AI Studio.",
        "gemini_http_401" or "gemini_http_403" => "Google did not authorize this request. Check the saved API key and its Gemini API permissions.",
        "gemini_http_404" => "The selected Gemini model or endpoint was not found. Refresh available models and select a supported model.",
        "gemini_timeout" => "Gemini did not respond within this request's time limit.",
        "gemini_network_error" => "Pulse could not connect to Gemini.",
        "gemini_output_truncated" => "Gemini reached the output limit before completing this response.",
        "gemini_invalid_response" => "Gemini returned an unexpected response format.",
        "gemini_response_too_large" => "Gemini returned a response larger than the allowed limit.",
        _ => null
    };

    private static string? NormalizeCode(string? code) => code?.StartsWith("module025_external_", StringComparison.Ordinal) == true
        ? code["module025_external_".Length..] : code;

    internal static string? RequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "x-request-id", "x-goog-request-id" })
        {
            if (!response.Headers.TryGetValues(name, out var values)) continue;
            var value = values.FirstOrDefault();
            if (value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
                return value;
        }
        return null;
    }

    private static DateTimeOffset? RetryHeader(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Date is { } date && date > now) return date;
        return header?.Delta is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromDays(365)
            ? now.Add(delay) : null;
    }

    private static TimeSpan? Delay(string? value)
    {
        if (value is not { Length: > 1 and <= 32 } || !value.EndsWith('s')) return null;
        return double.TryParse(value[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds > 0 && seconds <= 365 * 86400
            ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static DateTimeOffset Later(DateTimeOffset? left, DateTimeOffset right) => left is { } current && current > right ? current : right;
    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}

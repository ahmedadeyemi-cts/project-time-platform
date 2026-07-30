using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Executes only pre-registered, same-origin, read-only Pulse tools. The service
/// never accepts an arbitrary URL from the user or model. Session and View-As
/// headers are forwarded so the owning endpoint remains the authorization
/// authority for every source.
/// </summary>
public sealed class PulseAiSystemToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PulseAiSystemToolExecutor> _logger;

    public PulseAiSystemToolExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<PulseAiSystemToolExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PulseAiSystemToolResult>> ExecuteAsync(
        HttpContext context,
        IReadOnlyList<PulseAiSystemToolDefinition> definitions,
        PulseAiSystemIntelligenceOptions options,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PulseAiSystemToolResult>();
        foreach (var definition in definitions.Take(options.MaximumTools))
        {
            results.Add(await ExecuteOneAsync(
                context,
                definition,
                options,
                cancellationToken));
        }
        return results;
    }

    public async Task<PulseAiSystemToolResult> ExecuteOneAsync(
        HttpContext context,
        PulseAiSystemToolDefinition definition,
        PulseAiSystemIntelligenceOptions options,
        CancellationToken cancellationToken = default)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        if (!definition.SafeReadOnly || !HttpMethods.IsGet(definition.Method))
        {
            return Result(
                definition,
                "skipped",
                0,
                stopwatch,
                0,
                "tool_not_read_only",
                string.Empty,
                ["The tool is not registered as a safe read-only GET operation."],
                observedAt);
        }
        if (!ValidRelativeApiPath(definition.Path))
        {
            return Result(
                definition,
                "skipped",
                0,
                stopwatch,
                0,
                "tool_path_rejected",
                string.Empty,
                ["The tool path is not an approved same-origin API path."],
                observedAt);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ToolTimeoutSeconds));
            var target = BuildTarget(context, definition.Path);
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            ForwardSessionHeaders(context, request);
            request.Headers.TryAddWithoutValidation("X-Pulse-AI-System-Tool", definition.Code);
            request.Headers.TryAddWithoutValidation("X-Pulse-AI-Privacy-Boundary", PulseAiSystemIntelligencePolicy.PrivacyBoundary);
            request.Headers.TryAddWithoutValidation("X-Pulse-AI-Correlation-Id", CorrelationId(context));

            var client = _httpClientFactory.CreateClient("PulseAiSystemTools");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var bytes = await ReadBoundedAsync(
                response.Content,
                options.MaximumToolResponseCharacters,
                timeout.Token);
            stopwatch.Stop();
            var body = Encoding.UTF8.GetString(bytes);
            var summary = SummarizeResponse(body, response.StatusCode, definition);
            var status = response.IsSuccessStatusCode
                ? "succeeded"
                : response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                    ? "forbidden"
                    : response.StatusCode is System.Net.HttpStatusCode.Forbidden
                        ? "forbidden"
                        : response.StatusCode is System.Net.HttpStatusCode.NotFound
                            ? "not_found"
                            : "failed";
            var diagnostic = response.IsSuccessStatusCode
                ? string.Empty
                : $"HTTP_{(int)response.StatusCode}";

            return Result(
                definition,
                status,
                (int)response.StatusCode,
                stopwatch,
                bytes.Length,
                diagnostic,
                body,
                summary,
                observedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Result(
                definition,
                "failed",
                0,
                stopwatch,
                0,
                "tool_timeout",
                string.Empty,
                ["The owning Pulse API did not respond within the configured diagnostic timeout."],
                observedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                exception,
                "Pulse AI system tool failed without logging a response body. Tool={Tool} Diagnostic={Diagnostic}",
                definition.Code,
                Diagnostic(exception));
            return Result(
                definition,
                "failed",
                0,
                stopwatch,
                0,
                Diagnostic(exception),
                string.Empty,
                ["The owning Pulse API could not be reached through the governed same-origin tool boundary."],
                observedAt);
        }
    }

    public async Task<object> RetestAsync(
        HttpContext context,
        PulseAiSystemApiDescriptor api,
        string? confirmation,
        PulseAiSystemIntelligenceOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                confirmation?.Trim(),
                PulseAiSystemIntelligencePolicy.RetestConfirmation,
                StringComparison.Ordinal))
        {
            return new
            {
                status = "confirmation_required",
                requiredConfirmation = PulseAiSystemIntelligencePolicy.RetestConfirmation,
                api.ApiId,
                api.Method,
                api.RoutePattern,
                retestPerformed = false
            };
        }
        if (!api.SafeRetestSupported)
        {
            return new
            {
                status = "safe_retest_not_supported",
                api.ApiId,
                api.Method,
                api.RoutePattern,
                reason = api.SafeRetestReason,
                retestPerformed = false
            };
        }

        var definition = new PulseAiSystemToolDefinition(
            Code: $"safe_retest_{api.ApiId}",
            Name: $"Safe retest: {api.Method} {api.RoutePattern}",
            ModuleCode: api.ModuleCode,
            ModuleName: api.ModuleName,
            Method: api.Method,
            Path: api.RoutePattern,
            Purpose: "Explicitly confirmed same-origin status and latency verification.",
            Intents: ["troubleshooting"],
            Priority: 1,
            RequiresApiInventoryPermission: true,
            RequiresTroubleshootingPermission: true,
            AdministrativeEvidence: true,
            SafeReadOnly: true);
        var result = await ExecuteOneAsync(context, definition, options, cancellationToken);
        return new
        {
            status = "safe_api_retest_completed",
            api.ApiId,
            api.Method,
            api.RoutePattern,
            api.ModuleCode,
            api.ModuleName,
            result = result.Status,
            result.StatusCode,
            result.DurationMs,
            result.DiagnosticCode,
            result.ObservedAt,
            retestPerformed = true,
            responseBodyReturned = false,
            stateChanged = false,
            secretValuesReturned = false
        };
    }

    private static PulseAiSystemToolResult Result(
        PulseAiSystemToolDefinition definition,
        string status,
        int statusCode,
        Stopwatch stopwatch,
        int responseBytes,
        string diagnosticCode,
        string responseJson,
        IReadOnlyList<string> evidence,
        DateTimeOffset observedAt) =>
        new(
            ToolCode: definition.Code,
            ToolName: definition.Name,
            ModuleCode: definition.ModuleCode,
            ModuleName: definition.ModuleName,
            Method: definition.Method,
            Path: definition.Path,
            Status: status,
            StatusCode: statusCode,
            DurationMs: Math.Round((decimal)stopwatch.Elapsed.TotalMilliseconds, 3),
            ResponseBytes: responseBytes,
            DiagnosticCode: diagnosticCode,
            ResponseJson: responseJson,
            EvidenceSummary: evidence,
            ObservedAt: observedAt);

    private static Uri BuildTarget(HttpContext context, string relativePath)
    {
        var queryIndex = relativePath.IndexOf('?');
        var path = queryIndex >= 0 ? relativePath[..queryIndex] : relativePath;
        var query = queryIndex >= 0 ? relativePath[(queryIndex + 1)..] : string.Empty;
        var builder = new UriBuilder(
            context.Request.Scheme,
            context.Request.Host.Host,
            context.Request.Host.Port ?? -1,
            path)
        {
            Query = query
        };
        return builder.Uri;
    }

    private static bool ValidRelativeApiPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Uri.TryCreate(path, UriKind.Absolute, out _)) return false;
        var cleanPath = path.Split('?', 2)[0];
        if (!cleanPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && !cleanPath.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !cleanPath.Contains("..", StringComparison.Ordinal)
            && !cleanPath.Contains('\\');
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var maximumBytes = Math.Clamp(maximumCharacters * 4, 8_000, 1_000_000);
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream(Math.Min(maximumBytes, 64_000));
        var buffer = new byte[16 * 1024];
        while (memory.Length < maximumBytes)
        {
            var remaining = maximumBytes - (int)memory.Length;
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (read == 0) break;
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return memory.ToArray();
    }

    private static IReadOnlyList<string> SummarizeResponse(
        string body,
        System.Net.HttpStatusCode statusCode,
        PulseAiSystemToolDefinition definition)
    {
        var summary = new List<string>
        {
            $"{definition.Name} returned HTTP {(int)statusCode} ({statusCode})."
        };
        if (string.IsNullOrWhiteSpace(body))
        {
            summary.Add("The endpoint returned no JSON response body.");
            return summary;
        }

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 128 });
            var root = document.RootElement;
            AddString(root, "status", "Status", summary);
            AddString(root, "module", "Module", summary);
            AddString(root, "moduleName", "Module name", summary);
            AddString(root, "message", "Message", summary);
            AddString(root, "diagnosticCode", "Diagnostic", summary);
            AddString(root, "contractVersion", "Contract", summary);
            AddString(root, "generatedAt", "Generated", summary);
            AddString(root, "observedAt", "Observed", summary);
            AddObject(root, "summary", "Summary", summary);
            AddObject(root, "metrics", "Metrics", summary);
            AddArrayCount(root, "apis", "API records", summary);
            AddArrayCount(root, "events", "Evidence events", summary);
            AddArrayCount(root, "workers", "Workers", summary);
            AddArrayCount(root, "checks", "Diagnostic checks", summary);
            AddArrayCount(root, "activeIssues", "Active issues", summary);
            AddArrayCount(root, "alerts", "Alerts", summary);
            AddArrayCount(root, "blockers", "Blockers", summary);
            AddArrayCount(root, "releases", "Releases", summary);
            AddArrayCount(root, "environments", "Environments", summary);
            AddArrayCount(root, "nodes", "Architecture nodes", summary);
            AddArrayCount(root, "connections", "Architecture connections", summary);
        }
        catch (JsonException)
        {
            summary.Add("The endpoint response was not valid JSON; raw content was retained only inside the bounded private tool context.");
        }

        return summary.Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
    }

    private static void AddString(
        JsonElement root,
        string propertyName,
        string label,
        List<string> output)
    {
        if (!root.TryGetProperty(propertyName, out var value)) return;
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? value.GetRawText()
                : string.Empty;
        text = Limit(text, 1_000);
        if (text.Length > 0) output.Add($"{label}: {text}");
    }

    private static void AddObject(
        JsonElement root,
        string propertyName,
        string label,
        List<string> output)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return;
        }
        output.Add($"{label}: {Limit(value.GetRawText(), 2_000)}");
    }

    private static void AddArrayCount(
        JsonElement root,
        string propertyName,
        string label,
        List<string> output)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        output.Add($"{label}: {value.GetArrayLength()}");
    }

    private static void ForwardSessionHeaders(HttpContext source, HttpRequestMessage target)
    {
        foreach (var header in new[]
                 {
                     "Authorization",
                     "Cookie",
                     "X-ProjectPulse-Session",
                     "X-Project-Pulse-Session",
                     "X-Session-Token",
                     "X-ProjectPulse-View-As-User"
                 })
        {
            if (!source.Request.Headers.TryGetValue(header, out var values)) continue;
            target.Headers.TryAddWithoutValidation(header, values.ToArray());
        }
        target.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string CorrelationId(HttpContext context)
    {
        foreach (var name in new[]
                 {
                     "X-Correlation-ID",
                     "X-Request-ID",
                     "X-ProjectPulse-Correlation-Id"
                 })
        {
            var value = context.Request.Headers[name].ToString().Trim();
            if (value.Length > 0) return Limit(value, 160);
        }
        return $"pulse-ai-system-{Guid.NewGuid():N}";
    }

    private static string Limit(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        HttpRequestException => "tool_transport_failure",
        JsonException => "tool_json_failure",
        TimeoutException => "tool_timeout",
        OperationCanceledException => "tool_cancelled",
        IOException => "tool_io_failure",
        _ => "tool_execution_failure"
    };
}

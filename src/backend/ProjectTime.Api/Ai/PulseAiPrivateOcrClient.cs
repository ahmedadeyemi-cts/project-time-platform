using System.Net.Http.Headers;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateOcrClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PulseAiPrivateOcrClient> _logger;

    public PulseAiPrivateOcrClient(
        IHttpClientFactory httpClientFactory,
        ILogger<PulseAiPrivateOcrClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PulseAiPrivateOcrResult> ExtractAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions pipelineOptions,
        PulseAiPrivateRuntimeOptions runtimeOptions,
        CancellationToken cancellationToken = default)
    {
        var completedAt = DateTimeOffset.UtcNow;
        if (!runtimeOptions.OcrConfigured)
        {
            return Failure("private_ocr_not_configured", "ocr_not_configured", completedAt);
        }

        var endpointResolution = await PulseAiPrivateEndpointPolicy.VerifyResolvedPrivateEndpointAsync(
                runtimeOptions.OcrEndpoint,
                runtimeOptions.PrivateHostAllowlist,
                requireHttps: true,
                allowLoopback: false,
                cancellationToken: cancellationToken);
        var endpoint = endpointResolution.Endpoint;
        if (!endpointResolution.Approved || endpoint is null)
        {
            return Failure("private_ocr_endpoint_rejected", endpointResolution.Reason, completedAt);
        }

        if (!File.Exists(source.StoragePath))
        {
            return Failure("private_ocr_source_unavailable", "file_unavailable", completedAt);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(runtimeOptions.OcrBearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    runtimeOptions.OcrBearerToken);
            }
            request.Headers.Add("X-Pulse-AI-Privacy-Boundary", PulseAiPrivateRuntimePolicy.PrivacyBoundary);
            request.Headers.Add("X-Pulse-AI-Document-Id", source.DocumentId.ToString("D"));

            await using var stream = new FileStream(
                source.StoragePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                string.IsNullOrWhiteSpace(source.ContentType)
                    ? "application/octet-stream"
                    : source.ContentType);
            content.Add(fileContent, "file", source.OriginalFileName);
            content.Add(new StringContent(runtimeOptions.OcrModel), "model");
            content.Add(new StringContent(source.DocumentId.ToString("D")), "documentId");
            content.Add(new StringContent(source.DocumentCategory), "documentCategory");
            request.Content = content;

            var client = _httpClientFactory.CreateClient("PulseAiPrivateOcr");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    "private_ocr_failed",
                    $"ocr_http_{(int)response.StatusCode}",
                    DateTimeOffset.UtcNow);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(
                responseStream,
                new JsonDocumentOptions { MaxDepth = 64 },
                cancellationToken);
            var sections = ParseSections(json.RootElement, pipelineOptions);
            var pageCount = sections
                .Where(section => section.PageNumber is not null)
                .Select(section => section.PageNumber!.Value)
                .DefaultIfEmpty(0)
                .Max();
            var characters = sections.Sum(section => section.CharacterCount);
            if (sections.Count == 0)
            {
                return Failure(
                    "private_ocr_empty",
                    "ocr_no_text_returned",
                    DateTimeOffset.UtcNow);
            }

            return new PulseAiPrivateOcrResult(
                Status: "private_ocr_completed",
                Provider: endpoint.Host,
                Model: runtimeOptions.OcrModel,
                Sections: sections,
                PageCount: pageCount,
                CharacterCount: characters,
                Warnings: [],
                DiagnosticCode: string.Empty,
                CompletedAt: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI private OCR failed without logging source content. DocumentId={DocumentId} Diagnostic={Diagnostic}",
                source.DocumentId,
                Diagnostic(exception));
            return Failure("private_ocr_failed", Diagnostic(exception), DateTimeOffset.UtcNow);
        }
    }

    private static IReadOnlyList<PulseAiExtractedSection> ParseSections(
        JsonElement root,
        PulseAiDocumentPipelineOptions options)
    {
        var sections = new List<PulseAiExtractedSection>();
        if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var page in pages.EnumerateArray())
            {
                if (sections.Count >= options.MaximumSections) break;
                var pageNumber = page.TryGetProperty("pageNumber", out var number)
                    && number.TryGetInt32(out var parsed)
                    ? parsed
                    : index + 1;
                var text = page.TryGetProperty("text", out var textProperty)
                    ? Normalize(textProperty.GetString(), options.MaximumCharacters - sections.Sum(item => item.CharacterCount))
                    : string.Empty;
                if (text.Length == 0)
                {
                    index++;
                    continue;
                }
                sections.Add(new PulseAiExtractedSection(
                    SectionIndex: sections.Count,
                    Anchor: $"page:{pageNumber}",
                    Title: $"OCR page {pageNumber}",
                    Text: text,
                    PageNumber: pageNumber,
                    SheetName: null,
                    CharacterCount: text.Length,
                    TextSha256: Sha256(text)));
                index++;
                if (sections.Sum(item => item.CharacterCount) >= options.MaximumCharacters) break;
            }
        }
        else if (root.TryGetProperty("text", out var textProperty))
        {
            var text = Normalize(textProperty.GetString(), options.MaximumCharacters);
            if (text.Length > 0)
            {
                sections.Add(new PulseAiExtractedSection(
                    SectionIndex: 0,
                    Anchor: "ocr:document",
                    Title: "OCR document text",
                    Text: text,
                    PageNumber: null,
                    SheetName: null,
                    CharacterCount: text.Length,
                    TextSha256: Sha256(text)));
            }
        }

        return sections;
    }

    private static string Normalize(string? value, int remaining)
    {
        if (remaining <= 0) return string.Empty;
        var clean = (value ?? string.Empty)
            .Replace("\0", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
        return clean.Length <= remaining ? clean : clean[..remaining];
    }

    private static string Sha256(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static PulseAiPrivateOcrResult Failure(
        string status,
        string diagnostic,
        DateTimeOffset completedAt) =>
        new(
            Status: status,
            Provider: string.Empty,
            Model: string.Empty,
            Sections: [],
            PageCount: 0,
            CharacterCount: 0,
            Warnings: [],
            DiagnosticCode: diagnostic,
            CompletedAt: completedAt);

    private static string Diagnostic(Exception exception) => exception switch
    {
        HttpRequestException => "ocr_transport_failure",
        JsonException => "ocr_response_invalid",
        IOException => "ocr_io_failure",
        UnauthorizedAccessException => "ocr_storage_access_denied",
        TimeoutException => "ocr_timeout",
        OperationCanceledException => "ocr_cancelled",
        _ => "ocr_failure"
    };
}

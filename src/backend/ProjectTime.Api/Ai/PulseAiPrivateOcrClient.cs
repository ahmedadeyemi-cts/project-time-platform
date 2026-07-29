using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

public interface IPulseAiPrivateOcrClient
{
    Task<PulseAiPrivateOcrResult> ExtractAsync(
        string filePath,
        string originalFileName,
        string sourceSha256,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class PulseAiPrivateOcrClient : IPulseAiPrivateOcrClient
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
        string filePath,
        string originalFileName,
        string sourceSha256,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        if (!options.OcrConfigured)
        {
            return Failure(
                "not_configured",
                "private_ocr_not_configured",
                "The approved private OCR service is not configured.",
                correlationId,
                generatedAt);
        }

        if (!File.Exists(filePath))
        {
            return Failure(
                "failed",
                "source_file_missing",
                "The document file was not available for private OCR.",
                correlationId,
                generatedAt);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.OcrTimeoutSeconds));

        try
        {
            var client = _httpClientFactory.CreateClient("PulseAiPrivateServices");
            using var request = new HttpRequestMessage(HttpMethod.Post, options.OcrEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("X-Pulse-AI-Correlation-Id", correlationId);
            request.Headers.Add("X-Pulse-AI-Source-SHA256", sourceSha256);
            if (!string.IsNullOrWhiteSpace(options.OcrApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.OcrApiKey);
            }

            await using var file = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(file);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(ContentType(originalFileName));
            form.Add(fileContent, "file", Path.GetFileName(originalFileName));
            form.Add(new StringContent(sourceSha256), "sourceSha256");
            form.Add(new StringContent(PulseAiPrivateDocumentRuntimePolicy.ContractVersion), "contractVersion");
            request.Content = form;

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var requestId = Header(response, "x-request-id")
                ?? Header(response, "request-id")
                ?? correlationId;
            var body = await ReadBoundedBodyAsync(response, 4 * 1024 * 1024, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    "failed",
                    $"private_ocr_http_{(int)response.StatusCode}",
                    "The approved private OCR service did not accept the document.",
                    requestId,
                    generatedAt);
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var pages = new List<PulseAiPrivateOcrPage>();
            if (root.TryGetProperty("pages", out var pageArray)
                && pageArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var page in pageArray.EnumerateArray())
                {
                    if (!page.TryGetProperty("text", out var textElement)) continue;
                    var text = textElement.GetString()?.Trim() ?? string.Empty;
                    if (text.Length == 0) continue;
                    var pageNumber = page.TryGetProperty("pageNumber", out var pageNumberElement)
                        && pageNumberElement.TryGetInt32(out var parsedPage)
                            ? Math.Max(parsedPage, 1)
                            : pages.Count + 1;
                    var confidence = page.TryGetProperty("confidence", out var confidenceElement)
                        && confidenceElement.TryGetDecimal(out var parsedConfidence)
                            ? Math.Clamp(parsedConfidence, 0m, 1m)
                            : 0m;
                    pages.Add(new PulseAiPrivateOcrPage(
                        pageNumber,
                        text,
                        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
                        confidence));
                }
            }

            var engine = root.TryGetProperty("engine", out var engineElement)
                ? engineElement.GetString() ?? "private_ocr"
                : "private_ocr";
            var version = root.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString() ?? string.Empty
                : string.Empty;

            if (pages.Count == 0)
            {
                return Failure(
                    "failed",
                    "private_ocr_empty_result",
                    "The private OCR service returned no usable page text.",
                    requestId,
                    generatedAt);
            }

            return new PulseAiPrivateOcrResult(
                "success",
                engine,
                version,
                pages,
                string.Empty,
                string.Empty,
                requestId,
                generatedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                "failed",
                "private_ocr_timeout",
                "The private OCR request timed out.",
                correlationId,
                generatedAt);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private OCR failed without exposing document content. CorrelationId={CorrelationId}",
                correlationId);
            return Failure(
                "failed",
                "private_ocr_failure",
                "The approved private OCR service was unavailable or returned an invalid response.",
                correlationId,
                generatedAt);
        }
    }

    private static PulseAiPrivateOcrResult Failure(
        string status,
        string code,
        string message,
        string requestId,
        DateTimeOffset generatedAt) =>
        new(status, "private_ocr", string.Empty, [], code, message, requestId, generatedAt);

    private static string ContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<string> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (memory.Length <= maximumBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (memory.Length > maximumBytes)
        {
            throw new InvalidDataException("The private OCR response exceeded the configured bound.");
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }
}

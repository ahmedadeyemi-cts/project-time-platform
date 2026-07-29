using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

public interface IPulseAiPrivateIndexClient
{
    Task<PulseAiPrivateIndexBatchResult> UpsertAsync(
        IReadOnlyList<PulseAiPrivateIndexDocument> documents,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default);

    Task<PulseAiPrivateIndexBatchResult> DeleteVersionAsync(
        Guid versionId,
        IReadOnlyList<string> chunkIds,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class PulseAiPrivateIndexClient : IPulseAiPrivateIndexClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PulseAiPrivateIndexClient> _logger;

    public PulseAiPrivateIndexClient(
        IHttpClientFactory httpClientFactory,
        ILogger<PulseAiPrivateIndexClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<PulseAiPrivateIndexBatchResult> UpsertAsync(
        IReadOnlyList<PulseAiPrivateIndexDocument> documents,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            operation = "upsert",
            contractVersion = PulseAiPrivateDocumentRuntimePolicy.ContractVersion,
            indexName = options.IndexName,
            correlationId,
            documents = documents.Select(document => new
            {
                id = document.ChunkId,
                text = document.Text,
                vector = document.Vector,
                security = new
                {
                    documentId = document.DocumentId,
                    versionId = document.VersionId,
                    projectId = document.ProjectId,
                    projectCode = document.ProjectCode,
                    customerScope = document.CustomerScope,
                    documentCategory = document.DocumentCategory,
                    documentVersion = document.DocumentVersion,
                    classification = document.Classification,
                    engineeringVisible = document.EngineeringVisible,
                    aiTimesheetContextEnabled = document.AiTimesheetContextEnabled,
                    accessScope = document.AccessScope,
                    citationAnchor = document.CitationAnchor,
                    pageNumber = document.PageNumber,
                    sheetName = document.SheetName,
                    sourceSha256 = document.SourceSha256,
                    textSha256 = document.TextSha256,
                    metadata = document.SecurityMetadata
                }
            }).ToArray()
        };

        return SendAsync(
            "/documents/upsert",
            payload,
            documents.Select(document => document.ChunkId).ToArray(),
            "indexed",
            correlationId,
            options,
            cancellationToken);
    }

    public Task<PulseAiPrivateIndexBatchResult> DeleteVersionAsync(
        Guid versionId,
        IReadOnlyList<string> chunkIds,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            operation = "delete",
            contractVersion = PulseAiPrivateDocumentRuntimePolicy.ContractVersion,
            indexName = options.IndexName,
            correlationId,
            versionId,
            ids = chunkIds
        };

        return SendAsync(
            "/documents/delete",
            payload,
            chunkIds,
            "deleted",
            correlationId,
            options,
            cancellationToken);
    }

    private async Task<PulseAiPrivateIndexBatchResult> SendAsync(
        string path,
        object payload,
        IReadOnlyList<string> expectedChunkIds,
        string expectedStatus,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        if (!options.IndexConfigured)
        {
            return Failure(
                expectedChunkIds,
                "private_index_not_configured",
                "The approved permission-scoped private index is not configured.",
                correlationId,
                options,
                generatedAt);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.IndexTimeoutSeconds));

        try
        {
            var client = _httpClientFactory.CreateClient("PulseAiPrivateServices");
            var endpoint = options.IndexEndpoint!.TrimEnd('/') + path;
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("X-Pulse-AI-Correlation-Id", correlationId);
            if (!string.IsNullOrWhiteSpace(options.IndexApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.IndexApiKey);
            }
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var requestId = Header(response, "x-request-id")
                ?? Header(response, "request-id")
                ?? correlationId;
            var body = await ReadBoundedBodyAsync(response, 8 * 1024 * 1024, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    expectedChunkIds,
                    $"private_index_http_{(int)response.StatusCode}",
                    "The approved private index did not accept the operation.",
                    requestId,
                    options,
                    generatedAt);
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var indexVersion = root.TryGetProperty("indexVersion", out var versionElement)
                ? versionElement.GetString() ?? string.Empty
                : string.Empty;
            var receipts = new Dictionary<string, PulseAiPrivateIndexWriteReceipt>(StringComparer.Ordinal);
            if (root.TryGetProperty("receipts", out var receiptArray)
                && receiptArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var receipt in receiptArray.EnumerateArray())
                {
                    var chunkId = receipt.TryGetProperty("id", out var idElement)
                        ? idElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (chunkId.Length == 0) continue;
                    var status = receipt.TryGetProperty("status", out var statusElement)
                        ? statusElement.GetString() ?? string.Empty
                        : string.Empty;
                    var externalKey = receipt.TryGetProperty("externalKey", out var keyElement)
                        ? keyElement.GetString() ?? chunkId
                        : chunkId;
                    var code = receipt.TryGetProperty("code", out var codeElement)
                        ? codeElement.GetString() ?? string.Empty
                        : string.Empty;
                    var message = receipt.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString() ?? string.Empty
                        : string.Empty;
                    receipts[chunkId] = new PulseAiPrivateIndexWriteReceipt(
                        chunkId,
                        status,
                        externalKey,
                        indexVersion,
                        code,
                        message);
                }
            }

            foreach (var chunkId in expectedChunkIds)
            {
                if (!receipts.ContainsKey(chunkId))
                {
                    receipts[chunkId] = new PulseAiPrivateIndexWriteReceipt(
                        chunkId,
                        "failed",
                        string.Empty,
                        indexVersion,
                        "private_index_receipt_missing",
                        "The private index did not return a receipt for this chunk.");
                }
            }

            var ordered = expectedChunkIds.Select(chunkId => receipts[chunkId]).ToArray();
            var success = ordered.All(receipt =>
                receipt.Status.Equals(expectedStatus, StringComparison.OrdinalIgnoreCase));
            return new PulseAiPrivateIndexBatchResult(
                success ? "success" : "partial",
                "private_hybrid_index",
                options.IndexName!,
                ordered,
                success ? string.Empty : "private_index_partial_result",
                success ? string.Empty : "One or more private index operations did not complete.",
                requestId,
                generatedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                expectedChunkIds,
                "private_index_timeout",
                "The private index operation timed out.",
                correlationId,
                options,
                generatedAt);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private index operation failed without logging private text or vectors. CorrelationId={CorrelationId}",
                correlationId);
            return Failure(
                expectedChunkIds,
                "private_index_failure",
                "The approved private index was unavailable or returned an invalid response.",
                correlationId,
                options,
                generatedAt);
        }
    }

    private static PulseAiPrivateIndexBatchResult Failure(
        IReadOnlyList<string> chunkIds,
        string code,
        string message,
        string requestId,
        PulseAiPrivateDocumentRuntimeOptions options,
        DateTimeOffset generatedAt) =>
        new(
            "failed",
            "private_hybrid_index",
            options.IndexName ?? string.Empty,
            chunkIds.Select(chunkId => new PulseAiPrivateIndexWriteReceipt(
                chunkId,
                "failed",
                string.Empty,
                string.Empty,
                code,
                message)).ToArray(),
            code,
            message,
            requestId,
            generatedAt);

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<string> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (memory.Length <= maximumBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (memory.Length > maximumBytes)
        {
            throw new InvalidDataException("The private index response exceeded the configured bound.");
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }
}

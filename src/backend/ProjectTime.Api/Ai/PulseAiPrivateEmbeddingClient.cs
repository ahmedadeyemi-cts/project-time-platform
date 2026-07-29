using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

public interface IPulseAiPrivateEmbeddingClient
{
    Task<PulseAiPrivateEmbeddingBatchResult> EmbedAsync(
        IReadOnlyList<PulseAiDocumentChunk> chunks,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class PulseAiPrivateEmbeddingClient : IPulseAiPrivateEmbeddingClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PulseAiPrivateEmbeddingClient> _logger;

    public PulseAiPrivateEmbeddingClient(
        IHttpClientFactory httpClientFactory,
        ILogger<PulseAiPrivateEmbeddingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PulseAiPrivateEmbeddingBatchResult> EmbedAsync(
        IReadOnlyList<PulseAiDocumentChunk> chunks,
        string correlationId,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        if (!options.EmbeddingConfigured)
        {
            return Failure(
                options.EmbeddingModel ?? string.Empty,
                "private_embedding_not_configured",
                "The approved private embedding endpoint and model are required.",
                correlationId,
                generatedAt);
        }

        if (chunks.Count == 0)
        {
            return Failure(
                options.EmbeddingModel!,
                "private_embedding_empty_input",
                "No private chunks were available for embedding.",
                correlationId,
                generatedAt);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.EmbeddingTimeoutSeconds));

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                model = options.EmbeddingModel,
                input = chunks.Select(chunk => chunk.Text).ToArray(),
                encoding_format = "float",
                metadata = new
                {
                    contractVersion = PulseAiPrivateDocumentRuntimePolicy.ContractVersion,
                    correlationId,
                    dataBoundary = PulseAiPrivateDocumentRuntimePolicy.PrivacyBoundary
                }
            });

            var client = _httpClientFactory.CreateClient("PulseAiPrivateServices");
            using var request = new HttpRequestMessage(HttpMethod.Post, options.EmbeddingEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("X-Pulse-AI-Correlation-Id", correlationId);
            if (!string.IsNullOrWhiteSpace(options.EmbeddingApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.EmbeddingApiKey);
            }
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var requestId = Header(response, "x-request-id")
                ?? Header(response, "request-id")
                ?? correlationId;
            var body = await ReadBoundedBodyAsync(response, 16 * 1024 * 1024, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    options.EmbeddingModel!,
                    $"private_embedding_http_{(int)response.StatusCode}",
                    "The approved private embedding endpoint did not accept the request.",
                    requestId,
                    generatedAt);
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return Failure(
                    options.EmbeddingModel!,
                    "private_embedding_invalid_response",
                    "The private embedding endpoint returned no data array.",
                    requestId,
                    generatedAt);
            }

            var vectors = new Dictionary<int, IReadOnlyList<float>>();
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("index", out var indexElement)
                    || !indexElement.TryGetInt32(out var index)
                    || !item.TryGetProperty("embedding", out var embedding)
                    || embedding.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var values = new List<float>();
                foreach (var value in embedding.EnumerateArray())
                {
                    if (value.TryGetSingle(out var single)) values.Add(single);
                    else if (value.TryGetDouble(out var number)) values.Add((float)number);
                }
                if (values.Count > 0) vectors[index] = values;
            }

            if (vectors.Count != chunks.Count)
            {
                return Failure(
                    options.EmbeddingModel!,
                    "private_embedding_count_mismatch",
                    "The private embedding result count did not match the chunk count.",
                    requestId,
                    generatedAt);
            }

            var dimension = vectors.Values.First().Count;
            if (dimension <= 0 || vectors.Values.Any(vector => vector.Count != dimension))
            {
                return Failure(
                    options.EmbeddingModel!,
                    "private_embedding_dimension_mismatch",
                    "The private embedding vectors had inconsistent dimensions.",
                    requestId,
                    generatedAt);
            }

            var items = chunks.Select((chunk, index) =>
                new PulseAiPrivateEmbeddingItem(chunk.ChunkId, vectors[index])).ToArray();

            return new PulseAiPrivateEmbeddingBatchResult(
                "success",
                options.EmbeddingModel!,
                dimension,
                items,
                string.Empty,
                string.Empty,
                requestId,
                generatedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                options.EmbeddingModel!,
                "private_embedding_timeout",
                "The private embedding request timed out.",
                correlationId,
                generatedAt);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private embedding failed without logging chunk text. CorrelationId={CorrelationId}",
                correlationId);
            return Failure(
                options.EmbeddingModel!,
                "private_embedding_failure",
                "The approved private embedding endpoint was unavailable or returned an invalid response.",
                correlationId,
                generatedAt);
        }
    }

    private static PulseAiPrivateEmbeddingBatchResult Failure(
        string model,
        string code,
        string message,
        string requestId,
        DateTimeOffset generatedAt) =>
        new("failed", model, 0, [], code, message, requestId, generatedAt);

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
            throw new InvalidDataException("The private embedding response exceeded the configured bound.");
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }
}

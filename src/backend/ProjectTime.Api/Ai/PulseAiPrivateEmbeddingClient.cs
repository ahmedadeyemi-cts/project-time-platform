using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateEmbeddingClient
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

    public async Task<PulseAiPrivateEmbeddingResult> GenerateAsync(
        IReadOnlyList<string> inputs,
        PulseAiPrivateRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        var completedAt = DateTimeOffset.UtcNow;
        if (inputs.Count == 0)
        {
            return Failure("private_embeddings_empty_input", "embedding_input_empty", completedAt);
        }
        if (!options.EmbeddingConfigured)
        {
            return Failure("private_embeddings_not_configured", "embedding_not_configured", completedAt);
        }
        if (!PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
                options.EmbeddingEndpoint,
                options.PrivateHostAllowlist,
                out var endpoint,
                out var endpointReason)
            || endpoint is null)
        {
            return Failure("private_embedding_endpoint_rejected", endpointReason, completedAt);
        }

        try
        {
            var vectors = new List<double[]>(inputs.Count);
            var client = _httpClientFactory.CreateClient("PulseAiPrivateEmbedding");
            for (var start = 0; start < inputs.Count; start += options.EmbeddingBatchSize)
            {
                var batch = inputs
                    .Skip(start)
                    .Take(options.EmbeddingBatchSize)
                    .Select(value => value.Length <= 24_000 ? value : value[..24_000])
                    .ToArray();
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(new
                    {
                        model = options.EmbeddingModel,
                        input = batch,
                        encoding_format = "float"
                    })
                };
                if (!string.IsNullOrWhiteSpace(options.EmbeddingBearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        options.EmbeddingBearerToken);
                }
                request.Headers.Add("X-Pulse-AI-Privacy-Boundary", PulseAiPrivateRuntimePolicy.PrivacyBoundary);
                request.Headers.Add("X-Pulse-AI-Feature", "private_document_embedding");

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Failure(
                        "private_embeddings_failed",
                        $"embedding_http_{(int)response.StatusCode}",
                        DateTimeOffset.UtcNow);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions { MaxDepth = 32 },
                    cancellationToken);
                var batchVectors = ParseVectors(json.RootElement, batch.Length);
                if (batchVectors.Count != batch.Length)
                {
                    return Failure(
                        "private_embeddings_invalid_response",
                        "embedding_count_mismatch",
                        DateTimeOffset.UtcNow);
                }
                vectors.AddRange(batchVectors);
            }

            var dimension = vectors[0].Length;
            if (dimension == 0 || vectors.Any(vector => vector.Length != dimension))
            {
                return Failure(
                    "private_embeddings_invalid_response",
                    "embedding_dimension_mismatch",
                    DateTimeOffset.UtcNow);
            }

            return new PulseAiPrivateEmbeddingResult(
                Status: "private_embeddings_completed",
                Provider: endpoint.Host,
                Model: options.EmbeddingModel,
                Dimension: dimension,
                Vectors: vectors,
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
                "Pulse AI private embedding request failed without logging input text. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return Failure(
                "private_embeddings_failed",
                Diagnostic(exception),
                DateTimeOffset.UtcNow);
        }
    }

    private static IReadOnlyList<double[]> ParseVectors(
        JsonElement root,
        int expectedCount)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var indexed = new SortedDictionary<int, double[]>();
        var fallbackIndex = 0;
        foreach (var item in data.EnumerateArray())
        {
            var index = item.TryGetProperty("index", out var indexProperty)
                && indexProperty.TryGetInt32(out var parsedIndex)
                ? parsedIndex
                : fallbackIndex;
            fallbackIndex++;
            if (!item.TryGetProperty("embedding", out var embedding)
                || embedding.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var values = embedding
                .EnumerateArray()
                .Select(value => value.GetDouble())
                .ToArray();
            if (values.Length == 0 || values.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
            {
                return [];
            }
            indexed[index] = values;
        }

        if (indexed.Count != expectedCount) return [];
        return indexed.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
    }

    private static PulseAiPrivateEmbeddingResult Failure(
        string status,
        string diagnosticCode,
        DateTimeOffset completedAt) =>
        new(
            Status: status,
            Provider: string.Empty,
            Model: string.Empty,
            Dimension: 0,
            Vectors: [],
            DiagnosticCode: diagnosticCode,
            CompletedAt: completedAt);

    private static string Diagnostic(Exception exception) => exception switch
    {
        HttpRequestException => "embedding_transport_failure",
        JsonException => "embedding_response_invalid",
        TimeoutException => "embedding_timeout",
        OperationCanceledException => "embedding_cancelled",
        _ => "embedding_failure"
    };
}

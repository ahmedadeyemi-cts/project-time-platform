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
        var endpointResolution = await PulseAiPrivateEndpointPolicy.VerifyResolvedPrivateEndpointAsync(
                options.EmbeddingEndpoint,
                options.PrivateHostAllowlist,
                requireHttps: true,
                allowLoopback: false,
                cancellationToken: cancellationToken);
        var endpoint = endpointResolution.Endpoint;
        if (!endpointResolution.Approved || endpoint is null)
        {
            return Failure("private_embedding_endpoint_rejected", endpointResolution.Reason, completedAt);
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
                "Celar AI private embedding request failed without logging input text. Diagnostic={Diagnostic}",
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
        if (expectedCount <= 0) return [];

        return root.ValueKind switch
        {
            JsonValueKind.Object => ParseObjectEnvelope(root, expectedCount),
            JsonValueKind.Array => ParseArrayEnvelope(root, expectedCount),
            _ => []
        };
    }

    private static IReadOnlyList<double[]> ParseObjectEnvelope(
        JsonElement root,
        int expectedCount)
    {
        if (root.TryGetProperty("data", out var data))
        {
            return ParseEmbeddingItems(data, expectedCount);
        }

        if (root.TryGetProperty("embeddings", out var embeddings))
        {
            return ParseArrayEnvelope(embeddings, expectedCount);
        }

        if (root.TryGetProperty("embedding", out var embedding)
            && expectedCount == 1
            && TryReadVector(embedding, out var vector))
        {
            return [vector];
        }

        return [];
    }

    private static IReadOnlyList<double[]> ParseArrayEnvelope(
        JsonElement array,
        int expectedCount)
    {
        if (array.ValueKind != JsonValueKind.Array) return [];
        var items = array.EnumerateArray().ToArray();
        if (items.Length == 0) return [];

        if (items.All(item => item.ValueKind == JsonValueKind.Number))
        {
            return expectedCount == 1 && TryReadVector(array, out var vector)
                ? [vector]
                : [];
        }

        if (items.All(item => item.ValueKind == JsonValueKind.Array))
        {
            if (items.Length != expectedCount) return [];
            var vectors = new List<double[]>(items.Length);
            foreach (var item in items)
            {
                if (!TryReadVector(item, out var vector)) return [];
                vectors.Add(vector);
            }
            return HasConsistentDimension(vectors) ? vectors : [];
        }

        if (items.All(item => item.ValueKind == JsonValueKind.Object))
        {
            return ParseEmbeddingItems(array, expectedCount);
        }

        return [];
    }

    private static IReadOnlyList<double[]> ParseEmbeddingItems(
        JsonElement items,
        int expectedCount)
    {
        if (items.ValueKind != JsonValueKind.Array) return [];
        var indexed = new SortedDictionary<int, double[]>();
        var fallbackIndex = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("embedding", out var embedding)
                || !TryReadVector(embedding, out var vector))
            {
                return [];
            }

            var index = item.TryGetProperty("index", out var indexProperty)
                && indexProperty.ValueKind == JsonValueKind.Number
                && indexProperty.TryGetInt32(out var parsedIndex)
                ? parsedIndex
                : fallbackIndex;
            fallbackIndex++;

            if (index < 0 || index >= expectedCount || !indexed.TryAdd(index, vector))
            {
                return [];
            }
        }

        if (indexed.Count != expectedCount) return [];
        for (var index = 0; index < expectedCount; index++)
        {
            if (!indexed.ContainsKey(index)) return [];
        }

        var ordered = indexed.Values.ToArray();
        return HasConsistentDimension(ordered) ? ordered : [];
    }

    private static bool TryReadVector(
        JsonElement element,
        out double[] vector)
    {
        vector = [];
        if (element.ValueKind != JsonValueKind.Array) return false;

        var values = new List<double>();
        foreach (var value in element.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out var parsed)
                || double.IsNaN(parsed)
                || double.IsInfinity(parsed))
            {
                return false;
            }
            values.Add(parsed);
        }

        if (values.Count == 0) return false;
        vector = values.ToArray();
        return true;
    }

    private static bool HasConsistentDimension(IReadOnlyList<double[]> vectors)
    {
        if (vectors.Count == 0 || vectors[0].Length == 0) return false;
        var dimension = vectors[0].Length;
        return vectors.All(vector => vector.Length == dimension);
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

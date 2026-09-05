using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectTime.Api.Modules;

namespace ProjectTime.Api.Ai;

public static class ProjectPulseAiServiceCollectionExtensions
{
    public static IServiceCollection AddProjectPulseAi(this IServiceCollection services)
    {
        // Validate revision-scoped candidate configuration before any AI
        // background worker or configuration loader is allowed to start.
        services.AddHostedService<ProjectPulseAiReleaseRuntimeGuard>();
        services.AddHttpContextAccessor();
        services.AddHttpClient("DeepSeekDgx", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(15)
            });
        services.AddHttpClient("ProjectPulseAi")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                UseCookies = false,
                UseProxy = false
            });
        services.AddHttpClient("PulseAiPrivateOcr", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
        services.AddHttpClient("PulseAiPrivateEmbedding", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
        services.AddHttpClient("PulseAiPrivateInference", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
        services.AddTransient<PulseAiPrivateSowInferenceBudgetHandler>();
        // SOW/GSD generation deliberately runs behind a durable queue. Its
        // logical quality contract remains comprehensive, while the dedicated
        // handler bounds the non-streaming response accepted by the protected
        // gateway before the strict Module 025 parser sees it.
        services.AddHttpClient("PulseAiPrivateSowInference", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(12);
        })
        .AddHttpMessageHandler<PulseAiPrivateSowInferenceBudgetHandler>()
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
        services.AddHttpClient("PulseAiPrivateMalwareScan", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
        services.AddHttpClient("PulseAiExternalRuntimeReadiness", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
        services.AddHttpClient("PulseAiPrivateTraining", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
        services.AddHostedService<PulseAiExternalHttpsRuntimeGuard>();
        // System Intelligence forwards current-session headers only to a configured,
        // allowlisted same-origin target. Redirects and shared cookie storage stay disabled.
        services.AddHttpClient("PulseAiSystemTools", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(75);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });
        services.AddSingleton<ProjectPulseAiConfiguration>();
        services.AddSingleton<ProjectPulseAiSecretStore>();
        services.AddSingleton<ProjectPulseAiEncryptionRotationService>();
        services.AddHostedService<ProjectPulseAiSecretLoader>();
        services.AddHostedService<ProjectPulseAiConfigurationSynchronizer>();
        services.AddSingleton<ProjectPulseAiHealthRegistry>();
        services.AddSingleton<ProjectPulseClaudeProvider>();
        services.AddSingleton<ProjectPulseDeepSeekProvider>();
        services.AddSingleton<IProjectPulseAiProvider>(provider => provider.GetRequiredService<ProjectPulseDeepSeekProvider>());
        services.AddSingleton<ProjectPulseOpenAiProvider>();
        services.AddSingleton<IProjectPulseAiProvider>(provider => provider.GetRequiredService<ProjectPulseClaudeProvider>());
        services.AddSingleton<IProjectPulseAiProvider>(provider => provider.GetRequiredService<ProjectPulseOpenAiProvider>());
        services.AddSingleton<ProjectPulseAiRouter>();
        services.AddSingleton<ProjectPulseAiHealthCoordinator>();

        // Module 064 capability routing and private Celar AI runtime profile.
        services.AddSingleton<CelarAiCapabilityRoutingStore>();
        services.AddSingleton<CelarAiPrivateGenerationTarget>();
        services.AddSingleton<CelarAiConsumerAssuranceRegistry>();
        services.AddSingleton<CelarAiKnowledgeFabricService>();
        services.AddSingleton<CelarAiCapabilityRouter>();
        services.AddHostedService<CelarAiCapabilityRoutingLoader>();

        services.AddSingleton<PulseAiDocumentGroundingService>();
        services.AddSingleton<PulseAiQuestionPlanner>();
        services.AddSingleton<PulseAiEscalationSanitizer>();
        services.AddSingleton<PulseAiPrivateDocumentExtractionService>();
        services.AddSingleton<PulseAiPrivateDocumentPipelineService>();
        services.AddSingleton<PulseAiPrivateRuntimeSourceResolver>();
        services.AddSingleton<PulseAiPrivateMalwareScanner>();
        services.AddSingleton<PulseAiPrivateOcrClient>();
        services.AddSingleton<PulseAiPrivateEmbeddingClient>();
        services.AddSingleton<PulseAiPrivateDocumentRuntimeRepository>();
        services.AddSingleton<PulseAiPrivateDocumentRuntimeService>();
        services.AddHostedService<PulseAiPrivateDocumentRuntimeWorker>();
        services.AddSingleton<CelarAiConversationAttachmentRepository>();
        services.AddSingleton<CelarAiConversationAttachmentService>();
        services.AddHostedService<CelarAiConversationAttachmentRetentionWorker>();
        services.AddSingleton<PulseAiPrivateRagRepository>();
        services.AddSingleton<PulseAiPrivateRetrievalAuthorizationService>();
        services.AddSingleton<PulseAiPrivateRetrievalService>();
        services.AddSingleton<PulseAiPrivateModelClient>();
        services.AddSingleton<PulseAiPrivateRagService>();
        services.AddSingleton<PulseAiSystemApiCatalogService>();
        services.AddSingleton<PulseAiSystemToolExecutor>();
        services.AddSingleton<PulseAiSystemIntelligenceRepository>();
        services.AddSingleton<PulseAiSystemIntelligenceService>();
        services.AddSingleton<CelarAiInternalDataService>();
        services.AddSingleton<CelarAiPeopleAndGuidanceService>();
        services.AddSingleton<CelarAiExternalReasoningService>();
        services.AddSingleton<CelarAiEnterprisePlatformService>();
        services.AddHostedService<ProjectFlowHiveAiPlannerWorker>();
        services.AddSingleton<IProjectFlowHivePlanRepository, PostgresProjectFlowHivePlanRepository>();
        services.AddSingleton<ProjectPulseAiTimesheetContextResolver>();
        services.AddSingleton<ProjectPulseAiTimeEntrySuggestionService>();
        services.AddHostedService<ProjectPulseAiHealthMonitor>();
        return services;
    }

    /// <summary>
    /// Creates the transport used by every private Celar AI, OCR, embedding, and
    /// training request. The request adapters still authorize the configured
    /// hostname and perform a preflight DNS check. ConnectCallback closes the
    /// remaining DNS-rebinding window by resolving again at the moment the TCP
    /// socket is opened and connecting only to that validated private address.
    ///
    /// The callback returns a raw network stream. SocketsHttpHandler therefore
    /// retains responsibility for TLS and validates the certificate against the
    /// original request hostname; no certificate-validation override is used.
    /// </summary>
    internal static SocketsHttpHandler PrivateHttpHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        UseCookies = false,
        UseProxy = false,
        ConnectCallback = ConnectToPinnedPrivateEndpointAsync
    };

    internal static async ValueTask<Stream> ConnectToPinnedPrivateEndpointAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                context.InitialRequestMessage.RequestUri?.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Private AI transports require HTTPS.");
        }

        var host = context.DnsEndPoint.Host?.Trim() ?? string.Empty;
        if (host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("The private AI endpoint hostname is not connectable.");
        }

        IPAddress[] addresses;
        if (PulseAiExternalHttpsRuntimePolicy.TryGetPinnedAddress(
                context.InitialRequestMessage.RequestUri,
                out var pinnedAddress,
                out _))
        {
            // The endpoint adapter already revalidated live DNS. Connect only to
            // the approved public IPv4 address while SocketsHttpHandler validates
            // TLS against the original celarai.onenecklab.com request hostname.
            addresses = [pinnedAddress];
        }
        else
        {
            try
            {
                addresses = IPAddress.TryParse(host, out var literal)
                    ? [literal]
                    : await Dns.GetHostAddressesAsync(host, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException)
            {
                throw new HttpRequestException("The private AI endpoint could not be resolved.", exception);
            }

            // Reject the entire answer set when even one address is unsafe. Selecting
            // only a private member from a mixed answer would make DNS rebinding and
            // split-horizon configuration mistakes difficult to detect.
            if (addresses.Length == 0
                || addresses.Any(address => !PulseAiPrivateEndpointPolicy.IsConnectablePrivateAddress(address)))
            {
                throw new HttpRequestException(
                    "The private AI endpoint did not resolve exclusively to private, non-loopback addresses.");
            }
        }

        Exception? lastFailure = null;
        foreach (var address in addresses.Distinct())
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                lastFailure = exception;
                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            "The private AI endpoint could not be reached through its validated private addresses.",
            lastFailure);
    }
}

/// <summary>
/// Keeps Module 025 private SOW inference inside the private boundary while
/// using the non-streaming OpenAI-compatible contract accepted by the protected
/// gateway. The handler applies a compact output budget and owns the bounded
/// body read before the strict Module 025 parser sees the completion.
/// </summary>
internal sealed class PulseAiPrivateSowInferenceBudgetHandler : DelegatingHandler
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private const int PrimaryMaximumOutputTokens = 12_000;
    private const int RecoveryMaximumOutputTokens = 10_000;
    private const int MaximumBufferedResponseBytes = 1_000_000;
    private const int MaximumStreamedResponseBytes = 2_000_000;
    private const int MaximumSseLineBytes = 256_000;
    private const int MaximumStreamedContentCharacters = 96_000;
    private static readonly TimeSpan PrimaryAttemptBudget = TimeSpan.FromSeconds(420);
    private static readonly TimeSpan RecoveryAttemptBudget = TimeSpan.FromSeconds(260);
    private static readonly TimeSpan OverallInferenceBudget = TimeSpan.FromSeconds(690);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var originalBody = await request.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(originalBody))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        using var overallCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallCancellation.CancelAfter(OverallInferenceBudget);

        try
        {
            using var primaryRequest = CloneWithBudget(
                request,
                originalBody,
                PrimaryMaximumOutputTokens,
                recoveryAttempt: false);
            try
            {
                using var primaryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    overallCancellation.Token);
                primaryCancellation.CancelAfter(PrimaryAttemptBudget);
                var primaryResponse = await SendBoundedAttemptAsync(
                    primaryRequest,
                    primaryCancellation.Token);
                if (!IsTransientGatewayFailure(primaryResponse.StatusCode)
                    && !await IsOutputLimitedCompletionAsync(
                        primaryResponse,
                        primaryCancellation.Token)
                    && !await IsInvalidJsonObjectCompletionAsync(
                        primaryResponse,
                        primaryCancellation.Token))
                {
                    return primaryResponse;
                }
                primaryResponse.Dispose();
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested
                && !overallCancellation.IsCancellationRequested)
            {
                // Retry once with a smaller complete JSON contract. The same
                // authorized evidence and strict adoption boundary are retained.
            }

            using var recoveryRequest = CloneWithBudget(
                request,
                originalBody,
                RecoveryMaximumOutputTokens,
                recoveryAttempt: true);
            using var recoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                overallCancellation.Token);
            recoveryCancellation.CancelAfter(RecoveryAttemptBudget);
            return await SendBoundedAttemptAsync(
                recoveryRequest,
                recoveryCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The bounded private SOW inference stream timed out.");
        }
    }

    private async Task<HttpResponseMessage> SendBoundedAttemptAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            return IsEventStream(response)
                ? await BufferStreamingCompletionAsync(response, cancellationToken)
                : await BufferRegularCompletionAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task<HttpResponseMessage> BufferRegularCompletionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            if (response.Content.Headers.ContentLength is > MaximumBufferedResponseBytes)
            {
                throw new InvalidOperationException("Private SOW response exceeded the bounded transport limit.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var body = await ReadBoundedUtf8Async(
                stream,
                MaximumBufferedResponseBytes,
                "Private SOW response exceeded the bounded transport limit.",
                cancellationToken);
            return BufferedResponse(
                response,
                body,
                response.Content.Headers.ContentType?.MediaType ?? "application/json");
        }
    }

    private static async Task<HttpResponseMessage> BufferStreamingCompletionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            if (response.Content.Headers.ContentLength is > MaximumStreamedResponseBytes)
            {
                throw new InvalidOperationException("Private SOW event stream exceeded the bounded transport limit.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var readBuffer = new byte[8_192];
            var lineBuffer = new ArrayBufferWriter<byte>(8_192);
            var content = new StringBuilder();
            var finishReason = string.Empty;
            JsonObject? terminalError = null;
            var totalStreamBytes = 0;
            var stopReading = false;
            var sawTerminalEvent = false;
            var firstSseLine = true;

            bool ProcessLine(string line)
            {
                if (line.EndsWith('\r'))
                {
                    line = line[..^1];
                }
                if (firstSseLine)
                {
                    firstSseLine = false;
                    if (line.Length > 0 && line[0] == '\uFEFF')
                    {
                        line = line[1..];
                    }
                }
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;

                var data = line[5..].Trim();
                if (data.Length == 0) return false;
                if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                {
                    sawTerminalEvent = true;
                    return true;
                }

                var envelope = JsonNode.Parse(data) as JsonObject
                    ?? throw new InvalidOperationException("Private SOW stream event must be a JSON object.");
                if (envelope["error"] is JsonObject error)
                {
                    terminalError = error.DeepClone().AsObject();
                    sawTerminalEvent = true;
                    return true;
                }

                if (envelope["choices"] is not JsonArray choices
                    || choices.Count == 0
                    || choices[0] is not JsonObject choice)
                {
                    return false;
                }

                var eventFinishReason = JsonString(choice["finish_reason"]);
                if (eventFinishReason.Length > 0)
                {
                    finishReason = eventFinishReason;
                }

                var deltaContent = choice["delta"] is JsonObject delta
                    ? JsonString(delta["content"])
                    : string.Empty;
                if (deltaContent.Length == 0 && choice["message"] is JsonObject message)
                {
                    deltaContent = JsonString(message["content"]);
                }
                if (deltaContent.Length == 0) return false;

                if (deltaContent.Length > MaximumStreamedContentCharacters - content.Length)
                {
                    finishReason = "length";
                    sawTerminalEvent = true;
                    return true;
                }

                content.Append(deltaContent);
                return false;
            }

            while (!stopReading)
            {
                var read = await stream.ReadAsync(readBuffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    if (lineBuffer.WrittenCount > 0)
                    {
                        stopReading = ProcessLine(StrictUtf8.GetString(lineBuffer.WrittenSpan));
                        lineBuffer.Clear();
                    }
                    break;
                }

                if (totalStreamBytes > MaximumStreamedResponseBytes - read)
                {
                    throw new InvalidOperationException("Private SOW event stream exceeded the bounded transport limit.");
                }
                totalStreamBytes += read;

                for (var index = 0; index < read; index++)
                {
                    var value = readBuffer[index];
                    if (value == (byte)'\n')
                    {
                        stopReading = ProcessLine(StrictUtf8.GetString(lineBuffer.WrittenSpan));
                        lineBuffer.Clear();
                        if (stopReading) break;
                        continue;
                    }

                    if (lineBuffer.WrittenCount >= MaximumSseLineBytes)
                    {
                        throw new InvalidOperationException("Private SOW event exceeded the bounded per-event transport limit.");
                    }

                    var destination = lineBuffer.GetSpan(1);
                    destination[0] = value;
                    lineBuffer.Advance(1);
                }
            }

            if (!sawTerminalEvent && finishReason.Length == 0 && terminalError is null)
            {
                throw new InvalidOperationException("Private SOW event stream ended without a terminal event.");
            }

            JsonObject bufferedEnvelope;
            if (terminalError is not null)
            {
                bufferedEnvelope = new JsonObject
                {
                    ["error"] = terminalError
                };
            }
            else
            {
                bufferedEnvelope = new JsonObject
                {
                    ["choices"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["message"] = new JsonObject
                            {
                                ["content"] = content.ToString()
                            },
                            ["finish_reason"] = finishReason.Length == 0
                                ? null
                                : JsonValue.Create(finishReason)
                        }
                    }
                };
            }

            return BufferedResponse(
                response,
                bufferedEnvelope.ToJsonString(),
                "application/json");
        }
    }

    private static async Task<string> ReadBoundedUtf8Async(
        Stream stream,
        int maximumBytes,
        string limitMessage,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var chunk = new byte[8_192];
        var totalBytes = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (totalBytes > maximumBytes - read)
            {
                throw new InvalidOperationException(limitMessage);
            }

            totalBytes += read;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return StrictUtf8.GetString(
            buffer.GetBuffer(),
            0,
            checked((int)buffer.Length));
    }

    private static HttpResponseMessage BufferedResponse(
        HttpResponseMessage source,
        string body,
        string mediaType)
    {
        var buffered = new HttpResponseMessage(source.StatusCode)
        {
            Version = source.Version,
            ReasonPhrase = source.ReasonPhrase,
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };
        foreach (var header in source.Headers)
        {
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return buffered;
    }

    private static string JsonString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text ?? string.Empty
            : string.Empty;

    private static bool IsEventStream(HttpResponseMessage response) =>
        string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/event-stream",
            StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsOutputLimitedCompletionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) return false;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (PulseAiPrivateModelResponsePolicy.IsSafetyRefusal(root)) return false;
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("finish_reason", out var finishReason)
                || finishReason.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return finishReason.GetString() is "length" or "max_tokens";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<bool> IsInvalidJsonObjectCompletionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) return false;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var envelope = JsonDocument.Parse(body);
            var root = envelope.RootElement;
            if (PulseAiPrivateModelResponsePolicy.IsSafetyRefusal(root)) return false;
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var contentNode)
                || contentNode.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var content = contentNode.GetString()?.Trim() ?? string.Empty;
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = content.IndexOf('\n');
                var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLineEnd >= 0 && lastFence > firstLineEnd)
                {
                    content = content[(firstLineEnd + 1)..lastFence].Trim();
                }
            }
            if (content.Length == 0) return true;

            using var draft = JsonDocument.Parse(
                content,
                new JsonDocumentOptions { MaxDepth = 128 });
            return draft.RootElement.ValueKind != JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static HttpRequestMessage CloneWithBudget(
        HttpRequestMessage source,
        string originalBody,
        int maximumOutputTokens,
        bool recoveryAttempt)
    {
        var payload = JsonNode.Parse(originalBody) as JsonObject
            ?? throw new InvalidOperationException("Private SOW inference payload must be a JSON object.");
        var requestedMaximum = maximumOutputTokens;
        if (payload["max_tokens"] is JsonValue tokenValue
            && tokenValue.TryGetValue<int>(out var parsedMaximum))
        {
            requestedMaximum = parsedMaximum;
        }
        payload["max_tokens"] = Math.Min(requestedMaximum, maximumOutputTokens);
        // The protected Oracle gateway rejects streamed chat completions. Keep
        // its supported JSON-object contract, but bound the generation tightly
        // enough to finish inside the governed request window.
        payload["stream"] = false;

        if (payload["messages"] is JsonArray messages)
        {
            for (var index = messages.Count - 1; index >= 0; index--)
            {
                if (messages[index] is not JsonObject message
                    || !string.Equals(
                        message["role"]?.GetValue<string>(),
                        "user",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var content = message["content"]?.GetValue<string>() ?? string.Empty;
                var boundedInstruction = recoveryAttempt
                    ? "RECOVERY RESPONSE BUDGET: Return substantive technology-specific SOW/GSD work packages covering every service in the Service Overview, with at least two under each of Plan, Design, Implement, Validate, and Release. Do not cap the scope at ten packages. Preserve every required field and citationIds:[1]. Use at least three ordered implementation-grade detailedSteps per task and populate all required task lists. Include recommended engineering hours based on the activity, complexity, and explicit assumptions; never use a fixed phase allocation. Keep wording concise without omitting required work. Close the JSON object. Do not invent unsupported customer facts."
                    : "BOUNDED SOW RESPONSE: Return substantive technology-specific SOW/GSD work packages covering every service in the Service Overview, with at least two under each of Plan, Design, Implement, Validate, and Release. Do not cap the scope at ten packages. Preserve every required field and citationIds:[1]. Use at least three ordered implementation-grade detailedSteps per task and populate all required task lists. Include recommended engineering hours based on the activity, complexity, and explicit assumptions; never use a fixed phase allocation. Avoid repetition, preserve technical detail and deliverables, and do not invent unsupported customer facts.";
                message["content"] = $"{content}\n\n{boundedInstruction}";
                break;
            }
        }

        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        clone.Headers.TryAddWithoutValidation("Accept", "application/json");
        return clone;
    }

    private static bool IsTransientGatewayFailure(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}

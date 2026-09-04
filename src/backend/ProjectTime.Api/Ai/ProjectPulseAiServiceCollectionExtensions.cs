using System.Net;
using System.Net.Sockets;
using System.Text;
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
        // logical quality contract remains comprehensive, but a bounded private-
        // transport budget prevents a single oversized inference from reaching
        // the upstream gateway timeout. A transient private-model attempt may be
        // retried once without changing the evidence, citations, or fail-closed
        // SOW validation boundary.
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
/// preventing one oversized generation from consuming the entire upstream
/// gateway window. The second attempt is allowed only for transient transport
/// outcomes and reuses the same evidence/citations with a tighter JSON budget.
/// </summary>
internal sealed class PulseAiPrivateSowInferenceBudgetHandler : DelegatingHandler
{
    private const int PrimaryMaximumOutputTokens = 7_500;
    private const int RecoveryMaximumOutputTokens = 6_000;
    private static readonly TimeSpan PrimaryAttemptBudget = TimeSpan.FromSeconds(405);
    private static readonly TimeSpan RecoveryAttemptBudget = TimeSpan.FromSeconds(300);

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

        using var primaryRequest = CloneWithBudget(
            request,
            originalBody,
            PrimaryMaximumOutputTokens,
            recoveryAttempt: false);
        try
        {
            using var primaryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            primaryCancellation.CancelAfter(PrimaryAttemptBudget);
            var primaryResponse = await base.SendAsync(primaryRequest, primaryCancellation.Token);
            if (!IsTransientGatewayFailure(primaryResponse.StatusCode))
            {
                return primaryResponse;
            }
            primaryResponse.Dispose();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The first private attempt exceeded its bounded inference window.
            // Retry once with a tighter response budget before the named client's
            // overall twelve-minute deadline expires.
        }

        using var recoveryRequest = CloneWithBudget(
            request,
            originalBody,
            RecoveryMaximumOutputTokens,
            recoveryAttempt: true);
        using var recoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        recoveryCancellation.CancelAfter(RecoveryAttemptBudget);
        try
        {
            return await base.SendAsync(recoveryRequest, recoveryCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The bounded private SOW inference recovery attempt timed out.");
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
                    ? "RECOVERY RESPONSE BUDGET: Return the complete governed SOW/GSD JSON within this tighter private-model budget. Preserve all five phases, at least two substantive work packages per phase and at least ten total, all required execution/detail fields, authoritative citation IDs, milestone constraints, assumptions/open questions, acceptance criteria, validation procedures, responsibilities, risks, and engineering hours. Use concise implementation-grade wording; do not omit required fields and do not invent unsupported customer facts."
                    : "BOUNDED SOW RESPONSE: Return the complete governed SOW/GSD JSON within the available private-model budget. Preserve all five phases, at least two substantive work packages per phase and at least ten total, all required execution/detail fields, authoritative citation IDs, milestone constraints, assumptions/open questions, acceptance criteria, validation procedures, responsibilities, risks, and engineering hours. Prefer concise implementation-grade wording and detailed steps over narrative repetition; add extra work packages only when materially distinct work requires them.";
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
        return clone;
    }

    private static bool IsTransientGatewayFailure(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}

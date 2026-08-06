using System.Net;
using System.Net.Sockets;
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
        services.AddHttpClient("PulseAiPrivateTraining", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PrivateHttpHandler());
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
        services.AddSingleton<CelarAiPeopleAndGuidanceService>();
        services.AddSingleton<CelarAiExternalReasoningService>();
        services.AddSingleton<CelarAiEnterprisePlatformService>();
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

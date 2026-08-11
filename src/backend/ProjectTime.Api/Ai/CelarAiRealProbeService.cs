using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Distinct, bounded, read-only probes for every seeded Module 078/076 policy.
/// The service never accepts a model-generated target, command, SQL statement,
/// or request body. All external targets are exact allowlisted HTTPS endpoints.
/// </summary>
public sealed class CelarAiRealProbeService
{
    private const int MaximumResponseBytes = 64 * 1024;
    private const int ExpectedEmbeddingDimensions = 768;
    private static readonly byte[] OcrProbePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAaQAAABkCAIAAABGsdNiAAAEw0lEQVR4nO3bPyxzaxzA8VO50aRLRaOjgcEktLU0LdW/kQiDxWazSJioxd5YxGJAQ2wGi0iECkkTRVikSbWJRUIkJgwkPei5w8ltztVztbnNfV/u7/uZHsfvPE+P4RutsGiapgDA/13D734BAPArEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAiEDsAIhA7ACIQOwAi1Bu71dVVt9sdCAQGBwdvb2/1izabrf8vCwsLiqI0NTVV3ptMJq1W68PDg/GuQCDgdrvT6XR5bH193ePxeL1ej8ezsbFR9dzKHfTrwWDQ7/dfXFyYTpqeUh5zuVw7OzumjwbgZ9DqkEqlgsHg6+urpmm7u7uhUEi/brfbP01WXtE0bXh4eHp6em1t7dNMNpvt7OzU13t7ez6f7/HxUdO0x8dHn893cHBQ9VzjDp+u9/T0VE6anmIcu7y8bG1t/acHAfD91RW7WCx2enpa/nJ8fFxVVa222L28vITD4UKhMDIy8mmmVCo1Nzfr63A4fHJyUr4rk8lEIpGq5xp3+HS6w+GonDQ95dNYW1ub6YMA+BH+qOe3wlwu53K5yl+urKzUfu/+/v7AwEBHR8fNzY2qqo2NjeVvpVKpUCikr/P5vPEIt9t9dXVlsVi+Pte4g9Hh4WF3d3fl5MnJSeUpxrGjo6PFxcXanw7Ad1NX7D4+Pkyvq6ra39+vrxOJhNfrrZzZ3t6+vLzc2tq6v79Pp9PRaFS/6+3trVAo5HI50501TbNYLF+fW7mDfl3TNLvdnkwmKyc9Hk/lKeWxYrF4cXERCoWGhoZqeTQA31E9vxb29fWdnZ3p61KpNDY2pq+rvo19f3/3er36em9vb2pqyjgzPz+fSCT0dSQSyWQy5RuPj49jsVjVc407fP169EnTU7S/f7Snr3kbC/xQdf01dmJiYm5urlgsKoqyubmpL2qRyWS6urr0dW9vbyqVMn43Go2en5/r65mZmXg8/vz8rCjK09PT7OxsPB6veq5xh6/pk6anGMccDkd7e3uNTwfgG6rrbezo6Oj19bXH42lpaXE6nUtLS/p143s9r9ebSCRUVfX7/foVn8/3/v5e/kzNZrM5nc58Pl/etqOjI5vNlkqlhoaGWCx2d3cXDAatVquqqpOTk+FwWFEU03NNd/j6EfTJSCRieor+IPomy8vLpo/2b394AH4pi6Zpv/s1AMB/jv+gACACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgArEDIAKxAyACsQMgwp+C9UZv8Tt2mgAAAABJRU5ErkJggg==");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CelarAiCapabilityRoutingStore _routing;
    private readonly ILogger<CelarAiRealProbeService> _logger;

    public CelarAiRealProbeService(
        IHttpClientFactory httpClientFactory,
        CelarAiCapabilityRoutingStore routing,
        ILogger<CelarAiRealProbeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _routing = routing;
        _logger = logger;
    }

    public Task<CelarAiProbeEvidence> RunAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken) => policy.PolicyCode switch
    {
        "pulse_database" => ProbeDatabaseAsync(cancellationToken),
        "private_inference" => ProbeInferenceAsync(cancellationToken),
        "private_embeddings" => ProbeEmbeddingAsync(cancellationToken),
        "private_ocr" => ProbeOcrAsync(cancellationToken),
        "private_malware_scan" => ProbeMalwareAsync(cancellationToken),
        "all_ai_targets" => ProbeReadinessAsync(policy, cancellationToken),
        "tls_certificate" => ProbeTlsAsync(policy, cancellationToken),
        "clamav_signatures" => ProbeClamAvSignaturesAsync(policy, cancellationToken),
        "module064" => ProbeModule064Async(cancellationToken),
        "github_api" => ProbeGitHubRepositoryAsync(cancellationToken),
        "github_actions" => ProbeGitHubActionsAsync(cancellationToken),
        "module067" => ProbeNotificationOutboxAsync(cancellationToken),
        _ => ProbeConfiguredPulseEndpointAsync(policy, cancellationToken)
    };

    private async Task<CelarAiProbeEvidence> ProbeDatabaseAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var connection = await CelarAiOperationsDatabase.OpenAsync(cancellationToken);
            await using var command = new Npgsql.NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return Probe("pulse_database", "pulse_database", "Pulse database", "healthy", null,
                Elapsed(started), string.Empty, "Connection and bounded SELECT 1 succeeded.",
                "pulse_database", DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log("Pulse database", exception);
            return Probe("pulse_database", "pulse_database", "Pulse database", "failed", null,
                Elapsed(started), "database_unavailable", "The bounded database probe did not complete.",
                "pulse_database", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeInferenceAsync(CancellationToken cancellationToken)
    {
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!TryRuntimeEndpoint(snapshot, snapshot.InferenceEndpoint, out var endpoint, out var failure))
            return RuntimeConfigurationProbe("private_inference", "Private inference", failure);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = AuthorizedJson(
                HttpMethod.Post,
                endpoint,
                "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN",
                new
                {
                    model = PulseAiExternalHttpsRuntimePolicy.GenerationModel,
                    messages = new[] { new { role = "user", content = "Respond with exactly: CELAR PROBE OK" } },
                    temperature = 0,
                    max_tokens = 12,
                    stream = false
                });
            using var response = await SendAsync("PulseAiPrivateInference", request, cancellationToken);
            var body = await ReadLimitedAsync(response, cancellationToken);
            var exact = body.Contains("CELAR PROBE OK", StringComparison.OrdinalIgnoreCase);
            var healthy = response.IsSuccessStatusCode && exact;
            return Probe("private_inference_completion", "private_inference", "Private inference",
                healthy ? "healthy" : "failed", (int)response.StatusCode, Elapsed(started),
                healthy ? string.Empty : response.IsSuccessStatusCode ? "inference_probe_content_mismatch" : "inference_http_failure",
                healthy ? "Authenticated private generation returned the exact bounded probe response."
                    : "The authenticated private generation probe did not return the approved response.",
                endpoint.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log("private inference", exception);
            return Probe("private_inference_completion", "private_inference", "Private inference", "failed",
                null, Elapsed(started), "inference_unavailable",
                "The authenticated private generation probe did not complete.", endpoint.Host, DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeEmbeddingAsync(CancellationToken cancellationToken)
    {
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!TryRuntimeEndpoint(snapshot, snapshot.EmbeddingEndpoint, out var endpoint, out var failure))
            return RuntimeConfigurationProbe("private_embeddings", "Private embeddings", failure);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = AuthorizedJson(
                HttpMethod.Post,
                endpoint,
                "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN",
                new
                {
                    model = PulseAiExternalHttpsRuntimePolicy.EmbeddingModel,
                    input = "Celar AI bounded embedding readiness probe"
                });
            using var response = await SendAsync("PulseAiPrivateEmbedding", request, cancellationToken);
            var body = await ReadLimitedAsync(response, cancellationToken);
            var dimensions = EmbeddingDimensions(body);
            var healthy = response.IsSuccessStatusCode && dimensions == ExpectedEmbeddingDimensions;
            return Probe("private_embedding_dimension", "private_embeddings", "Private embeddings",
                healthy ? "healthy" : "failed", (int)response.StatusCode, Elapsed(started),
                healthy ? string.Empty : dimensions.HasValue ? "embedding_dimension_mismatch" : "embedding_response_invalid",
                healthy
                    ? $"Authenticated private embedding returned {dimensions} dimensions."
                    : $"The private embedding probe returned {dimensions?.ToString() ?? "no"} dimensions; {ExpectedEmbeddingDimensions} are required.",
                endpoint.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log("private embedding", exception);
            return Probe("private_embedding_dimension", "private_embeddings", "Private embeddings", "failed",
                null, Elapsed(started), "embedding_unavailable",
                "The authenticated private embedding probe did not complete.", endpoint.Host, DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeOcrAsync(CancellationToken cancellationToken)
    {
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!TryRuntimeEndpoint(snapshot, snapshot.OcrEndpoint, out var endpoint, out var failure))
            return RuntimeConfigurationProbe("private_ocr", "Private OCR", failure);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(OcrProbePng);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "celar-ocr-readiness.png");
            form.Add(new StringContent("validation"), "documentCategory");
            using var request = AuthorizedRequest(
                HttpMethod.Post,
                endpoint,
                "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN");
            request.Content = form;
            using var response = await SendAsync("PulseAiPrivateOcr", request, cancellationToken);
            var body = await ReadLimitedAsync(response, cancellationToken);
            var healthy = response.IsSuccessStatusCode
                && body.Contains("CELAR OCR PROBE", StringComparison.OrdinalIgnoreCase);
            return Probe("private_ocr_text", "private_ocr", "Private OCR",
                healthy ? "healthy" : "failed", (int)response.StatusCode, Elapsed(started),
                healthy ? string.Empty : response.IsSuccessStatusCode ? "ocr_probe_text_missing" : "ocr_http_failure",
                healthy ? "Authenticated private OCR extracted the expected probe text."
                    : "The private OCR probe did not return the expected bounded text.",
                endpoint.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log("private OCR", exception);
            return Probe("private_ocr_text", "private_ocr", "Private OCR", "failed",
                null, Elapsed(started), "ocr_unavailable",
                "The authenticated private OCR probe did not complete.", endpoint.Host, DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeMalwareAsync(CancellationToken cancellationToken)
    {
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!TryRuntimeEndpoint(snapshot, snapshot.MalwareScanEndpoint, out var endpoint, out var failure))
            return RuntimeConfigurationProbe("private_malware_scan", "Private malware scanning", failure);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("CELAR MALWARE SCAN READINESS PROBE\n"));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(file, "file", "celar-malware-readiness.txt");
            using var request = AuthorizedRequest(
                HttpMethod.Post,
                endpoint,
                PulseAiExternalHttpsRuntimePolicy.MalwareScanBearerTokenVariable);
            request.Content = form;
            using var response = await SendAsync("PulseAiPrivateMalwareScan", request, cancellationToken);
            var body = await ReadLimitedAsync(response, cancellationToken);
            var healthy = response.IsSuccessStatusCode
                && body.Contains("\"clean\":true", StringComparison.OrdinalIgnoreCase);
            return Probe("private_malware_clean_file", "private_malware_scan", "Private malware scanning",
                healthy ? "healthy" : "failed", (int)response.StatusCode, Elapsed(started),
                healthy ? string.Empty : response.IsSuccessStatusCode ? "clean_scan_attestation_missing" : "malware_scan_http_failure",
                healthy ? "Authenticated ClamAV scan returned a clean-file attestation."
                    : "The private malware scan did not return the required clean-file attestation.",
                endpoint.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log("private malware scan", exception);
            return Probe("private_malware_clean_file", "private_malware_scan", "Private malware scanning", "failed",
                null, Elapsed(started), "malware_scanner_unavailable",
                "The authenticated private malware probe did not complete.", endpoint.Host, DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeReadinessAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken)
    {
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!TryRuntimeEndpoint(snapshot, snapshot.ReadinessEndpoint, out var endpoint, out var failure))
            return RuntimeConfigurationProbe(policy.ComponentCode, policy.DisplayName, failure);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = AuthorizedRequest(
                HttpMethod.Get,
                endpoint,
                "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN");
            using var response = await SendAsync("PulseAiExternalRuntimeReadiness", request, cancellationToken);
            var body = await ReadLimitedAsync(response, cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var ready = response.IsSuccessStatusCode
                && String(root, "status").Equals("ready", StringComparison.OrdinalIgnoreCase)
                && Boolean(root, "ollamaReady")
                && Boolean(root, "generationModelReady")
                && Boolean(root, "embeddingModelReady")
                && Boolean(root, "tesseractReady")
                && Boolean(root, "clamavReady");
            var cloudAttested = root.TryGetProperty("ollamaCloudDisabled", out var cloud)
                && cloud.ValueKind == JsonValueKind.True;
            var healthy = ready && cloudAttested;
            return Probe("oracle_runtime_readiness", policy.ComponentCode, policy.DisplayName,
                healthy ? "healthy" : ready ? "degraded" : "failed",
                (int)response.StatusCode, Elapsed(started),
                healthy ? string.Empty : ready ? "ollama_cloud_disable_attestation_missing" : "runtime_component_not_ready",
                healthy
                    ? "All private runtime components are ready and Ollama cloud access is attested disabled."
                    : ready
                        ? "Private components are ready, but the runtime has not attested that Ollama cloud access is disabled."
                        : "One or more private runtime components are not ready.",
                endpoint.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log("private runtime readiness", exception);
            return Probe("oracle_runtime_readiness", policy.ComponentCode, policy.DisplayName, "failed", null,
                Elapsed(started), "runtime_health_unavailable",
                "The authenticated private-runtime readiness request did not complete.", endpoint.Host, DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeClamAvSignaturesAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken)
    {
        var readiness = await ProbeReadinessAsync(policy, cancellationToken);
        var configuredVersion = CelarAiOperationsPolicy.Clean(
            Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION"),
            160);
        if (!readiness.Healthy) return readiness with { ProbeCode = "clamav_signature_freshness" };
        if (configuredVersion.Length == 0)
        {
            return readiness with
            {
                ProbeCode = "clamav_signature_freshness",
                Status = "degraded",
                FailureCode = "clamav_signature_version_unavailable",
                Detail = "ClamAV is ready, but the reviewed signature version is not projected into Pulse."
            };
        }
        return readiness with
        {
            ProbeCode = "clamav_signature_freshness",
            ComponentCode = "clamav_signatures",
            DisplayName = "ClamAV signature freshness",
            Detail = "ClamAV is ready and a reviewed signature version is projected into Pulse."
        };
    }

    private async Task<CelarAiProbeEvidence> ProbeTlsAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken)
    {
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!snapshot.Active || !snapshot.Host.Equals(PulseAiExternalHttpsRuntimePolicy.ApprovedHost, StringComparison.Ordinal))
            return RuntimeConfigurationProbe(policy.ComponentCode, policy.DisplayName, "external_https_runtime_configuration_invalid");
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(snapshot.Host, 443, cancellationToken);
            await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = snapshot.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.Online
            }, cancellationToken);
            var certificate = ssl.RemoteCertificate is null
                ? null
                : new X509Certificate2(ssl.RemoteCertificate);
            var remaining = certificate is null
                ? TimeSpan.Zero
                : certificate.NotAfter.ToUniversalTime() - DateTime.UtcNow;
            var healthy = certificate is not null && remaining >= TimeSpan.FromDays(21);
            return Probe("tls_certificate_expiry", "tls_certificate", "Celar AI TLS certificate",
                healthy ? "healthy" : "failed", null, Elapsed(started),
                healthy ? string.Empty : "tls_certificate_expiring_or_unavailable",
                certificate is null
                    ? "No validated server certificate was available."
                    : $"The validated TLS certificate has {Math.Max(0, Math.Floor(remaining.TotalDays))} day(s) remaining.",
                snapshot.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log("TLS certificate", exception);
            return Probe("tls_certificate_expiry", "tls_certificate", "Celar AI TLS certificate", "failed",
                null, Elapsed(started), "tls_validation_unavailable",
                "The standard TLS validation probe did not complete.", snapshot.Host, DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeModule064Async(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var route = await _routing.LoadRouteAsync(CelarAiCapabilityCatalog.HelpAssistant, cancellationToken);
            var targets = route.Targets.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            var healthy = targets.Length > 0;
            return Probe("module064_help_route", "module064", "Module 064 Ask Celar AI route",
                healthy ? "healthy" : "failed", null, Elapsed(started),
                healthy ? string.Empty : "module064_route_empty",
                healthy ? $"The governed Ask Celar AI route contains {targets.Length} target(s)."
                    : "No governed Ask Celar AI target is configured.",
                "module064_capability_routing_store", DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            Log("Module 064 route", exception);
            return Probe("module064_help_route", "module064", "Module 064 Ask Celar AI route", "failed",
                null, Elapsed(started), "module064_unavailable",
                "The governed provider route could not be loaded.",
                "module064_capability_routing_store", DateTimeOffset.UtcNow);
        }
    }

    private Task<CelarAiProbeEvidence> ProbeGitHubRepositoryAsync(CancellationToken cancellationToken) =>
        ProbeGitHubAsync(
            "github_repository",
            "github_api",
            "GitHub repository access",
            $"https://api.github.com/repos/{CelarAiOperationsPolicy.DefaultRepository}",
            cancellationToken);

    private Task<CelarAiProbeEvidence> ProbeGitHubActionsAsync(CancellationToken cancellationToken) =>
        ProbeGitHubAsync(
            "github_actions_runs",
            "github_actions",
            "GitHub Actions availability",
            $"https://api.github.com/repos/{CelarAiOperationsPolicy.DefaultRepository}/actions/runs?per_page=1",
            cancellationToken);

    private async Task<CelarAiProbeEvidence> ProbeGitHubAsync(
        string probeCode,
        string componentCode,
        string displayName,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.ParseAdd("Pulse-Celar-AI-Operations/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            var token = Environment.GetEnvironmentVariable("PROJECTPULSE_GITHUB_MONITOR_TOKEN")?.Trim();
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync("ProjectPulseAi", request, cancellationToken);
            var healthy = response.IsSuccessStatusCode;
            return Probe(probeCode, componentCode, displayName,
                healthy ? "healthy" : "failed", (int)response.StatusCode, Elapsed(started),
                healthy ? string.Empty : $"github_http_{(int)response.StatusCode}",
                healthy ? "The exact allowlisted GitHub API request succeeded."
                    : "The exact allowlisted GitHub API request failed.",
                "api.github.com", DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log(displayName, exception);
            return Probe(probeCode, componentCode, displayName, "failed", null,
                Elapsed(started), "github_unavailable",
                "The exact allowlisted GitHub API request did not complete.",
                "api.github.com", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeNotificationOutboxAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var connection = await CelarAiOperationsDatabase.OpenReadyAsync(cancellationToken);
            await using var command = new Npgsql.NpgsqlCommand("""
                SELECT COUNT(*),MIN(created_at)
                FROM module076_notification_outbox
                WHERE status='pending';
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var count = reader.GetInt64(0);
            var oldest = reader.IsDBNull(1) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(1);
            var delayed = oldest.HasValue && DateTimeOffset.UtcNow - oldest.Value > TimeSpan.FromMinutes(15);
            return Probe("module067_outbox", "module067", "Module 067 defect notification handoff",
                delayed ? "failed" : count > 0 ? "degraded" : "healthy", null, Elapsed(started),
                delayed ? "notification_outbox_delayed" : string.Empty,
                count == 0 ? "No pending Module 076 notification events."
                    : $"{count} notification event(s) are waiting for Module 067 delivery.",
                "module076_notification_outbox", DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            Log("Module 067 notification handoff", exception);
            return Probe("module067_outbox", "module067", "Module 067 defect notification handoff", "failed",
                null, Elapsed(started), "notification_outbox_unavailable",
                "The notification outbox could not be inspected.",
                "module076_notification_outbox", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeConfiguredPulseEndpointAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var variable = $"PROJECTPULSE_CELAR_AI_{policy.PolicyCode.ToUpperInvariant()}_HEALTH_ENDPOINT";
        var configured = Environment.GetEnvironmentVariable(variable)?.Trim();
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName, "unknown", null,
                Elapsed(started), "probe_endpoint_not_configured", $"{variable} is not configured.",
                "deployment_managed_probe_configuration", DateTimeOffset.UtcNow);
        }
        var publicOrigin = Environment.GetEnvironmentVariable("PROJECTPULSE_PUBLIC_ORIGIN")?.Trim();
        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin)
            || !endpoint.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
        {
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName, "failed", null,
                Elapsed(started), "probe_endpoint_host_not_approved",
                "The health endpoint is not on the deployment-managed Pulse public origin.",
                "deployment_managed_probe_configuration", DateTimeOffset.UtcNow);
        }
        try
        {
            using var response = await _httpClientFactory.CreateClient("ProjectPulseAi")
                .GetAsync(endpoint, cancellationToken);
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName,
                response.IsSuccessStatusCode ? "healthy" : "failed", (int)response.StatusCode,
                Elapsed(started), response.IsSuccessStatusCode ? string.Empty : $"pulse_http_{(int)response.StatusCode}",
                response.IsSuccessStatusCode ? "The configured Pulse health request succeeded."
                    : "The configured Pulse health request failed.",
                endpoint.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                           || !cancellationToken.IsCancellationRequested)
        {
            Log(policy.DisplayName, exception);
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName, "failed", null,
                Elapsed(started), "pulse_health_unavailable",
                "The configured Pulse health request did not complete.",
                endpoint.Host, DateTimeOffset.UtcNow);
        }
    }

    private static bool TryRuntimeEndpoint(
        PulseAiExternalHttpsRuntimePolicy.Snapshot snapshot,
        Uri? endpoint,
        out Uri approved,
        out string failure)
    {
        approved = endpoint ?? new Uri("https://invalid.invalid/");
        if (!snapshot.Active || endpoint is null)
        {
            failure = "external_https_runtime_configuration_invalid";
            return false;
        }
        approved = endpoint;
        failure = string.Empty;
        return true;
    }

    private static CelarAiProbeEvidence RuntimeConfigurationProbe(
        string componentCode,
        string displayName,
        string failure) =>
        Probe(componentCode, componentCode, displayName, "unknown", null, 0,
            failure, "The protected Test external-runtime policy is not active and valid.",
            "external_runtime_policy", DateTimeOffset.UtcNow);

    private static HttpRequestMessage AuthorizedJson(
        HttpMethod method,
        Uri endpoint,
        string tokenVariable,
        object body)
    {
        var request = AuthorizedRequest(method, endpoint, tokenVariable);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        Uri endpoint,
        string tokenVariable)
    {
        var token = Environment.GetEnvironmentVariable(tokenVariable)?.Trim() ?? string.Empty;
        if (token.Length < 32)
            throw new InvalidOperationException("The protected private-runtime token is unavailable.");
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("X-Pulse-AI-Privacy-Boundary", "private_pulse_runtime_only");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString("N"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string clientName,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var endpointCheck = await PulseAiExternalHttpsRuntimePolicy.VerifyEndpointAsync(
            request.RequestUri?.ToString(), cancellationToken);
        if (request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) != true
            && !endpointCheck.Approved)
        {
            throw new InvalidOperationException($"The private endpoint failed exact-host and IP-pin validation: {endpointCheck.Reason}.");
        }
        return await _httpClientFactory.CreateClient(clientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<string> ReadLimitedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new InvalidOperationException("The probe response exceeded the approved size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8_192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (memory.Length + read > MaximumResponseBytes)
                throw new InvalidOperationException("The probe response exceeded the approved size limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static int? EmbeddingDimensions(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.GetArrayLength() > 0
                && data[0].TryGetProperty("embedding", out var embedding)
                && embedding.ValueKind == JsonValueKind.Array)
                return embedding.GetArrayLength();
            if (root.TryGetProperty("embeddings", out var embeddings)
                && embeddings.ValueKind == JsonValueKind.Array
                && embeddings.GetArrayLength() > 0
                && embeddings[0].ValueKind == JsonValueKind.Array)
                return embeddings[0].GetArrayLength();
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static bool Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static CelarAiProbeEvidence Probe(
        string probeCode,
        string componentCode,
        string displayName,
        string status,
        int? httpStatus,
        int? latencyMs,
        string failureCode,
        string detail,
        string source,
        DateTimeOffset observedAt) => new(
            CelarAiOperationsPolicy.Clean(probeCode, 120),
            CelarAiOperationsPolicy.Clean(componentCode, 100),
            CelarAiOperationsPolicy.Clean(displayName, 240),
            CelarAiOperationsPolicy.Clean(status, 24),
            httpStatus,
            latencyMs,
            CelarAiOperationsPolicy.Clean(failureCode, 120),
            CelarAiOperationsPolicy.SanitizeOperationalDetail(detail),
            CelarAiOperationsPolicy.Clean(source, 500),
            observedAt);

    private static int Elapsed(long started) =>
        Math.Max(0, (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(started).TotalMilliseconds));

    private void Log(string operation, Exception exception) =>
        _logger.LogWarning(
            "Celar AI bounded probe {Operation} failed ({ExceptionType}).",
            operation,
            exception.GetType().Name);
}

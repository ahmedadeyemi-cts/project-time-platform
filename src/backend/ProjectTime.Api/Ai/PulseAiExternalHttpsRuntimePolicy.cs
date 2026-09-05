using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Authorizes one temporary, deployment-managed HTTPS runtime for protected Test.
/// This is deliberately narrower than the normal private-DNS policy: the exact
/// DNS host, validated public DNS addresses, endpoint paths, models, bearer token,
/// and approval reference must agree before any request can leave Pulse.
/// Production can never enable this mode.
/// </summary>
public static class PulseAiExternalHttpsRuntimePolicy
{
    public const string EnabledVariable = "PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ENABLED";
    public const string HostVariable = "PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_HOST";
    public const string AddressModeVariable = "PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ADDRESS_MODE";
    public const string ExpectedIpVariable = "PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_EXPECTED_IP";
    public const string ApprovalReferenceVariable = "PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_APPROVAL_REFERENCE";
    public const string ReadinessEndpointVariable = "PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_READINESS_ENDPOINT";
    public const string MalwareScanEndpointVariable = "PROJECTPULSE_PRIVATE_MALWARE_SCAN_ENDPOINT";
    public const string MalwareScanBearerTokenVariable = "PROJECTPULSE_PRIVATE_MALWARE_SCAN_BEARER_TOKEN";
    public const string MalwareScanBearerTokenSecretReferenceVariable = "PROJECTPULSE_PRIVATE_MALWARE_SCAN_BEARER_TOKEN_SECRET_REFERENCE";

    public const string ApprovedHost = "celarai.onenecklab.com";
    public const string InferencePath = "/v1/chat/completions";
    public const string EmbeddingPath = "/v1/embeddings";
    public const string OcrPath = "/v1/extract";
    public const string MalwareScanPath = "/v1/scan";
    public const string ReadinessPath = "/health";
    public const string GenerationModel = "gemma3:4b";
    public const string EmbeddingModel = "embeddinggemma";
    public const string OcrModel = "tesseract-5-eng";
    public const string MalwareScannerMode = "celar_https_gateway";

    internal const int MaximumHealthResponseBytes = 64 * 1024;
    private static readonly Regex ApprovalReference = new(
        @"^ORACLE-TEST-[A-Za-z0-9][A-Za-z0-9._-]{7,119}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] EndpointVariables =
    [
        "PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT",
        "PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT",
        "PROJECTPULSE_PRIVATE_OCR_ENDPOINT",
        MalwareScanEndpointVariable,
        ReadinessEndpointVariable
    ];

    private static readonly string[] TokenVariables =
    [
        "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN",
        "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN",
        "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN",
        MalwareScanBearerTokenVariable
    ];

    private static readonly string[] TokenReferenceVariables =
    [
        "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN_SECRET_REFERENCE",
        MalwareScanBearerTokenSecretReferenceVariable
    ];

    public sealed record Snapshot(
        bool Enabled,
        bool ConfigurationPresent,
        string Environment,
        string Host,
        IPAddress? ExpectedAddress,
        string ApprovalReference,
        Uri? InferenceEndpoint,
        Uri? EmbeddingEndpoint,
        Uri? OcrEndpoint,
        Uri? MalwareScanEndpoint,
        Uri? ReadinessEndpoint,
        IReadOnlyList<string> Errors)
    {
        public bool DnsManaged { get; init; }
        public bool Valid => Errors.Count == 0;
        public bool Active => Enabled && Valid;
    }

    // The Oracle gateway rejects max_tokens above 8192 before inference.
    public static int CompletionBudget(Uri endpoint, int requestedTokens)
    {
        var runtime = Evaluate();
        return runtime.Active && endpoint == runtime.InferenceEndpoint
            ? Math.Min(requestedTokens, 8192)
            : requestedTokens;
    }

    public static bool IsEnabled => Boolean(EnabledVariable);

    public static Snapshot Evaluate()
    {
        var enabled = Boolean(EnabledVariable);
        var environment = Clean(Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT"));
        var host = Clean(Environment.GetEnvironmentVariable(HostVariable)).TrimEnd('.').ToLowerInvariant();
        var addressMode = Clean(Environment.GetEnvironmentVariable(AddressModeVariable));
        var dnsManaged = string.Equals(addressMode, "dns", StringComparison.OrdinalIgnoreCase);
        var expectedIpText = Clean(Environment.GetEnvironmentVariable(ExpectedIpVariable));
        var approvalReference = Clean(Environment.GetEnvironmentVariable(ApprovalReferenceVariable));
        var errors = new List<string>();

        var externalConfigurationPresent =
            !string.IsNullOrWhiteSpace(addressMode)
            || !string.IsNullOrWhiteSpace(host)
            || !string.IsNullOrWhiteSpace(expectedIpText)
            || !string.IsNullOrWhiteSpace(approvalReference)
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ReadinessEndpointVariable))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MalwareScanEndpointVariable))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MalwareScanBearerTokenVariable))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MalwareScanBearerTokenSecretReferenceVariable))
            || string.Equals(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE")?.Trim(),
                MalwareScannerMode,
                StringComparison.OrdinalIgnoreCase);

        var endpoints = EndpointVariables.ToDictionary(
            name => name,
            name => ParseEndpoint(Environment.GetEnvironmentVariable(name)),
            StringComparer.Ordinal);

        IPAddress? expectedAddress = null;
        if (!enabled)
        {
            if (externalConfigurationPresent)
                errors.Add("External HTTPS runtime configuration is present while the Test-only enable flag is false.");
            return BuildSnapshot(false, externalConfigurationPresent, environment, host, null, approvalReference, endpoints, errors);
        }

        if (!string.Equals(environment, "test", StringComparison.OrdinalIgnoreCase))
            errors.Add("The external HTTPS runtime is authorized only when PROJECTPULSE_ENVIRONMENT is exactly test.");

        if (!string.Equals(host, ApprovedHost, StringComparison.Ordinal))
            errors.Add($"The external HTTPS runtime host must be exactly {ApprovedHost}.");

        if (addressMode.Length > 0 && !dnsManaged && !string.Equals(addressMode, "pinned", StringComparison.OrdinalIgnoreCase))
            errors.Add("The external HTTPS address mode must be dns or pinned.");

        if (!dnsManaged && (!IPAddress.TryParse(expectedIpText, out expectedAddress)
            || !IsApprovedPublicIpv4(expectedAddress)))
        {
            errors.Add("The external HTTPS runtime expected address must be one explicit public IPv4 address.");
            expectedAddress = null;
        }

        if (!ApprovalReference.IsMatch(approvalReference))
            errors.Add("The external HTTPS runtime approval reference must match ORACLE-TEST-<approved-reference>.");

        ValidateEndpoint(endpoints["PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT"], host, InferencePath, "inference", errors);
        ValidateEndpoint(endpoints["PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT"], host, EmbeddingPath, "embedding", errors);
        ValidateEndpoint(endpoints["PROJECTPULSE_PRIVATE_OCR_ENDPOINT"], host, OcrPath, "OCR", errors);
        ValidateEndpoint(endpoints[MalwareScanEndpointVariable], host, MalwareScanPath, "malware scan", errors);
        ValidateEndpoint(endpoints[ReadinessEndpointVariable], host, ReadinessPath, "readiness", errors);

        var allowlist = Split(Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST"));
        if (allowlist.Length != 1
            || !string.Equals(allowlist[0].TrimEnd('.'), host, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The external HTTPS runtime requires one exact host allowlist entry and no suffix or fallback entries.");
        }

        RequireExact("PROJECTPULSE_PRIVATE_INFERENCE_MODEL", GenerationModel, errors);
        RequireExact("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL", EmbeddingModel, errors);
        RequireExact("PROJECTPULSE_PRIVATE_OCR_MODEL", OcrModel, errors);
        RequireExact("PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE", "bearer", errors);
        RequireExact("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE", MalwareScannerMode, errors);
        RequireExact("PROJECTPULSE_CELAR_AI_TRAINING_ENABLED", "false", errors);

        foreach (var name in new[]
                 {
                     "PROJECTPULSE_CELAR_AI_TRAINING_ENDPOINT",
                     "PROJECTPULSE_CELAR_AI_TRAINING_HOST_ALLOWLIST",
                     "PROJECTPULSE_CELAR_AI_TRAINING_BEARER_TOKEN"
                 })
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                errors.Add($"{name} must be empty while the temporary external HTTPS runtime is enabled.");
        }

        var tokens = TokenVariables
            .Select(name => Environment.GetEnvironmentVariable(name)?.Trim() ?? string.Empty)
            .ToArray();
        if (tokens.Any(string.IsNullOrWhiteSpace))
            errors.Add("Inference, embedding, OCR, and malware scanning must all receive the protected runtime bearer token.");
        else if (tokens.Any(value => value.Length < 32))
            errors.Add("The protected external HTTPS runtime bearer token must contain at least 32 characters.");
        else if (!AllEqualConstantTime(tokens))
            errors.Add("All temporary external HTTPS runtime clients must use one protected gateway bearer token.");

        var tokenReferences = TokenReferenceVariables
            .Select(name => Environment.GetEnvironmentVariable(name)?.Trim() ?? string.Empty)
            .ToArray();
        if (tokenReferences.Any(string.IsNullOrWhiteSpace))
            errors.Add("Every temporary external HTTPS runtime token must include a protected secret reference.");
        else if (tokenReferences.Distinct(StringComparer.Ordinal).Count() != 1)
            errors.Add("All temporary external HTTPS runtime clients must reference the same protected token version.");

        if (!Boolean("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED"))
            errors.Add("The external HTTPS malware scanner requires reviewed live scan attestation.");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION")))
            errors.Add("The external HTTPS malware scanner requires recorded ClamAV signature evidence.");
        if (!string.Equals(
                Clean(Environment.GetEnvironmentVariable(
                    "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE")),
                approvalReference,
                StringComparison.Ordinal))
            errors.Add("The malware-scan approval reference must match the external HTTPS runtime approval reference.");

        return BuildSnapshot(true, externalConfigurationPresent, environment, host, expectedAddress, approvalReference, endpoints, errors) with { DnsManaged = dnsManaged };
    }

    public static Snapshot RequireValid()
    {
        var snapshot = Evaluate();
        if (snapshot.Errors.Count > 0)
            throw new InvalidOperationException(
                $"Celar AI external HTTPS runtime configuration is invalid: {string.Join(" ", snapshot.Errors)}");
        return snapshot;
    }

    public static async Task<PulseAiPrivateEndpointPolicy.ResolutionResult> VerifyEndpointAsync(
        string? value,
        CancellationToken cancellationToken = default)
    {
        var snapshot = Evaluate();
        if (!snapshot.Enabled)
            return new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "external_https_runtime_disabled", 0);
        if (!snapshot.Valid || (!snapshot.DnsManaged && snapshot.ExpectedAddress is null))
            return new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "external_https_runtime_configuration_invalid", 0);
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint)
            || !IsConfiguredEndpoint(snapshot, endpoint))
            return new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "external_https_endpoint_not_approved", 0);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(snapshot.Host, cancellationToken);
            if (addresses.Length == 0)
                return new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "external_https_dns_no_addresses", 0);
            var normalized = addresses
                .Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
                .Distinct()
                .ToArray();
            if (!AddressesApproved(snapshot, normalized))
            {
                return new PulseAiPrivateEndpointPolicy.ResolutionResult(
                    false,
                    null,
                    snapshot.DnsManaged ? "external_https_dns_unsafe_address" : "external_https_dns_pin_mismatch",
                    normalized.Length);
            }
            return new PulseAiPrivateEndpointPolicy.ResolutionResult(
                true,
                endpoint,
                snapshot.DnsManaged ? "test_external_https_hostname_verified" : "test_external_https_dns_and_ip_pin_verified",
                normalized.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException)
        {
            return new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "external_https_dns_resolution_failed", 0);
        }
    }

    internal static bool AddressesApproved(Snapshot snapshot, IReadOnlyList<IPAddress> addresses) =>
        addresses.Count > 0 && addresses.All(address => IsApprovedPublicIpv4(address)
            && (snapshot.DnsManaged || address.Equals(snapshot.ExpectedAddress)));

    public static async Task<IPAddress[]> ResolveConnectAddressesAsync(Uri? requestUri, CancellationToken token)
    {
        var snapshot = Evaluate();
        if (!snapshot.Active || requestUri is null || !IsConfiguredEndpoint(snapshot, requestUri))
            throw new HttpRequestException("The external HTTPS endpoint is not approved.");
        var addresses = (await Dns.GetHostAddressesAsync(snapshot.Host, token))
            .Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
            .Distinct().ToArray();
        if (!AddressesApproved(snapshot, addresses))
            throw new HttpRequestException("The external HTTPS hostname did not resolve exclusively to approved public addresses.");
        // The transport connects to this validated answer set directly. TLS still
        // authenticates the configured hostname; no second DNS lookup can rebind it.
        return addresses;
    }

    public static bool TryGetPinnedAddress(
        Uri? requestUri,
        out IPAddress address,
        out string reason)
    {
        address = IPAddress.None;
        var snapshot = Evaluate();
        if (!snapshot.Enabled)
        {
            reason = "external_https_runtime_disabled";
            return false;
        }
        if (!snapshot.Valid || snapshot.ExpectedAddress is null)
        {
            reason = "external_https_runtime_configuration_invalid";
            return false;
        }
        if (requestUri is null || !IsConfiguredEndpoint(snapshot, requestUri))
        {
            reason = "external_https_endpoint_not_approved";
            return false;
        }
        address = snapshot.ExpectedAddress;
        reason = "test_external_https_connect_ip_pinned";
        return true;
    }

    private static Snapshot BuildSnapshot(
        bool enabled,
        bool configurationPresent,
        string environment,
        string host,
        IPAddress? expectedAddress,
        string approvalReference,
        IReadOnlyDictionary<string, Uri?> endpoints,
        IReadOnlyList<string> errors) =>
        new(
            enabled,
            configurationPresent,
            environment,
            host,
            expectedAddress,
            approvalReference,
            endpoints["PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT"],
            endpoints["PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT"],
            endpoints["PROJECTPULSE_PRIVATE_OCR_ENDPOINT"],
            endpoints[MalwareScanEndpointVariable],
            endpoints[ReadinessEndpointVariable],
            errors.Distinct(StringComparer.Ordinal).ToArray());

    private static Uri? ParseEndpoint(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint) ? endpoint : null;

    private static void ValidateEndpoint(
        Uri? endpoint,
        string host,
        string expectedPath,
        string label,
        ICollection<string> errors)
    {
        if (endpoint is null
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !endpoint.IsDefaultPort
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !string.Equals(endpoint.DnsSafeHost.TrimEnd('.'), host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(endpoint.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            errors.Add($"The external HTTPS {label} endpoint must be exactly https://{host}{expectedPath}.");
        }
    }

    private static bool IsConfiguredEndpoint(Snapshot snapshot, Uri endpoint)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps
            || !endpoint.IsDefaultPort
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !string.Equals(
                endpoint.DnsSafeHost.TrimEnd('.'),
                snapshot.Host,
                StringComparison.OrdinalIgnoreCase))
            return false;

        return new[]
            {
                snapshot.InferenceEndpoint,
                snapshot.EmbeddingEndpoint,
                snapshot.OcrEndpoint,
                snapshot.MalwareScanEndpoint,
                snapshot.ReadinessEndpoint
            }
            .Where(candidate => candidate is not null)
            .Any(candidate => Uri.Compare(
                    candidate!,
                    endpoint,
                    UriComponents.SchemeAndServer | UriComponents.Path,
                    UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) == 0);
    }

    private static bool IsApprovedPublicIpv4(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.None)
            || PulseAiPrivateEndpointPolicy.IsConnectablePrivateAddress(address))
            return false;

        var bytes = address.GetAddressBytes();
        if (bytes[0] is 0 or >= 224) return false;
        if (bytes[0] == 169 && bytes[1] == 254) return false;
        if (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return false;
        if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) return false;
        if (bytes[0] == 198 && bytes[1] is 18 or 19) return false;
        if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return false;
        if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return false;
        return true;
    }

    private static void RequireExact(string name, string expected, ICollection<string> errors)
    {
        var actual = Clean(Environment.GetEnvironmentVariable(name));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            errors.Add($"{name} must be exactly {expected} for the approved external HTTPS runtime.");
    }

    private static bool AllEqualConstantTime(IReadOnlyList<string> values)
    {
        if (values.Count == 0) return false;
        var expected = System.Text.Encoding.UTF8.GetBytes(values[0]);
        foreach (var value in values.Skip(1))
        {
            var candidate = System.Text.Encoding.UTF8.GetBytes(value);
            if (candidate.Length != expected.Length
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, candidate))
                return false;
        }
        return true;
    }

    private static string[] Split(string? value) =>
        (value ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool Boolean(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name)?.Trim(), out var value) && value;

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}

public sealed class PulseAiExternalHttpsRuntimeGuard(
    IHttpClientFactory httpClientFactory,
    ILogger<PulseAiExternalHttpsRuntimeGuard> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = PulseAiExternalHttpsRuntimePolicy.RequireValid();
        if (!snapshot.Enabled) return;

        var endpointResolution = await PulseAiExternalHttpsRuntimePolicy.VerifyEndpointAsync(
            snapshot.ReadinessEndpoint?.AbsoluteUri,
            cancellationToken);
        if (!endpointResolution.Approved || endpointResolution.Endpoint is null)
            throw new InvalidOperationException(
                $"The Celar AI external HTTPS readiness endpoint failed DNS/IP pin validation ({endpointResolution.Reason}).");

        var token = Environment.GetEnvironmentVariable(
                "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN")
            ?.Trim() ?? string.Empty;
        using var request = new HttpRequestMessage(HttpMethod.Get, endpointResolution.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Pulse-AI-Privacy-Boundary", PulseAiPrivateRuntimePolicy.PrivacyBoundary);
        request.Headers.Add("X-Pulse-AI-Feature", "external_https_runtime_startup_readiness");

        var client = httpClientFactory.CreateClient("PulseAiExternalRuntimeReadiness");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"The Celar AI external HTTPS readiness endpoint returned HTTP {(int)response.StatusCode}.");

        using var json = await ReadBoundedJsonAsync(response.Content, cancellationToken);
        var root = json.RootElement;
        var ready = StringEquals(root, "status", "ready")
            && Boolean(root, "ollamaReady")
            && Boolean(root, "generationModelReady")
            && Boolean(root, "embeddingModelReady")
            && Boolean(root, "tesseractReady")
            && Boolean(root, "clamavReady")
            && StringEquals(root, "generationModel", PulseAiExternalHttpsRuntimePolicy.GenerationModel)
            && StringEquals(root, "embeddingModel", PulseAiExternalHttpsRuntimePolicy.EmbeddingModel)
            && StringEquals(root, "ocrModel", PulseAiExternalHttpsRuntimePolicy.OcrModel)
            && root.TryGetProperty("rawDocumentContentLogged", out var logged)
            && logged.ValueKind == JsonValueKind.False;
        if (!ready)
            throw new InvalidOperationException(
                "The Celar AI external HTTPS runtime failed the authenticated startup readiness contract.");

        logger.LogInformation(
            "Celar AI protected Test external HTTPS runtime passed authenticated readiness. Host={Host} Address={Address}",
            snapshot.Host,
            snapshot.ExpectedAddress);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<JsonDocument> ReadBoundedJsonAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > PulseAiExternalHttpsRuntimePolicy.MaximumHealthResponseBytes)
                throw new JsonException("External HTTPS runtime readiness exceeded the bounded response limit.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(
            buffer,
            new JsonDocumentOptions { MaxDepth = 32 },
            cancellationToken);
    }

    private static bool Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool StringEquals(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);
}

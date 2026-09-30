using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>Independent deployment-selected documents; no model or account configuration changes.</summary>
public sealed class PulseLayaServiceOptions
{
    public const string Prefix = "PROJECTPULSE_LAYA_SERVICE_";
    public const string Provider = "pulse_container_laya";
    public const string ModelRevision = LayaDecisionContract.Revision;
    public const string TestAppName = "ca-phd-test-laya-westus3";
    public static PulseLayaServiceOptions Disabled { get; } = new();
    public string Mode { get; init; } = "legacy";
    public string EnvironmentName { get; init; } = "";
    public string EnvironmentDomain { get; init; } = "";
    public string Origin { get; init; } = "";
    public string ApprovalReference { get; init; } = "";
    public string TokenSecretReference { get; init; } = "";
    [JsonIgnore] public string BearerToken { get; init; } = "";
    public string ConfigurationSha256 { get; init; } = "";
    public bool Requested => Mode != "legacy";
    public bool Valid => Requested && ErrorCode.Length == 0;
    public string ExpectedHost => TestAppName + ".internal." + EnvironmentDomain;
    public override string ToString() => "Pulse Laya-service configuration; credentials omitted";
    public string ErrorCode
    {
        get
        {
            if (!Requested) return "";
            if (Mode != "pulse_container") return "laya_service_mode_invalid";
            if (EnvironmentName != "test") return "laya_service_test_only";
            if (!Regex.IsMatch(EnvironmentDomain, @"^[a-z0-9][a-z0-9-]{1,62}\.[a-z0-9-]+\.azurecontainerapps\.io$"))
                return "laya_service_environment_domain_invalid";
            if (Origin != "https://" + ExpectedHost || !Uri.TryCreate(Origin, UriKind.Absolute, out _))
                return "laya_service_origin_invalid";
            if (!Regex.IsMatch(ApprovalReference, @"^PR-[1-9][0-9]{0,8}-[0-9a-f]{12,40}$"))
                return "laya_service_approval_missing";
            if (!Regex.IsMatch(TokenSecretReference, @"^[a-z][a-z0-9-]{2,62}$")
                || !Regex.IsMatch(BearerToken, @"^[A-Za-z0-9_-]{32,4096}$"))
                return "laya_service_credential_invalid";
            if (!Regex.IsMatch(ConfigurationSha256, @"^[0-9a-f]{64}$")
                || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ConfigurationSha256),
                    Encoding.ASCII.GetBytes(ComputeConfigurationSha256())))
                return "laya_service_configuration_changed";
            return "";
        }
    }
    // Binds destination and credential version; not a replacement for protected release admission.
    public string ComputeConfigurationSha256() => Hash(string.Join('\n',
        "pulse-laya-v1", Mode, EnvironmentName, EnvironmentDomain, Origin,
        ApprovalReference, TokenSecretReference, Hash(BearerToken)));
    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static PulseLayaServiceOptions FromEnvironment() => Read(Environment.GetEnvironmentVariable);
    public static PulseLayaServiceOptions Read(Func<string,string?> read)
    {
        string Value(string key) => read(Prefix + key)?.Trim() ?? "";
        var mode = Value("MODE");
        return new()
        {
            Mode = mode.Length == 0 ? "legacy" : mode,
            EnvironmentName = read("PROJECTPULSE_ENVIRONMENT")?.Trim() ?? "",
            EnvironmentDomain = Value("ENVIRONMENT_DOMAIN"), Origin = Value("ORIGIN"),
            ApprovalReference = Value("APPROVAL_REFERENCE"),
            TokenSecretReference = Value("TOKEN_SECRET_REFERENCE"), BearerToken = Value("TOKEN"),
            ConfigurationSha256 = Value("CONFIGURATION_SHA256")
        };
    }
    public Uri Endpoint(string path)
    {
        if (!Valid) throw new PulseLayaServiceException(ErrorCode.Length > 0 ? ErrorCode : "laya_service_disabled");
        if (path is not ("/v1/decisions/health" or "/v1/decisions/document-type"))
            throw new PulseLayaServiceException("laya_service_path_rejected");
        return new Uri(Origin + path);
    }
    public static bool AddressesApproved(IReadOnlyCollection<IPAddress> addresses) =>
        addresses.Count > 0 && addresses.All(PulseAiPrivateEndpointPolicy.IsConnectablePrivateAddress);
    public async Task<PulseAiPrivateEndpointPolicy.ResolutionResult> ResolveAsync(string path, CancellationToken token)
    {
        if (!Valid) return new(false, null, ErrorCode, 0);
        var endpoint = Endpoint(path);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(endpoint.DnsSafeHost, token);
            var approved = AddressesApproved(addresses);
            return new(approved, approved ? endpoint : null,
                approved ? "laya_service_private_dns_verified" : "laya_service_dns_rejected", addresses.Length);
        }
        catch (SocketException) { return new(false, null, "laya_service_dns_unavailable", 0); }
    }
}

public sealed class PulseLayaServiceException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

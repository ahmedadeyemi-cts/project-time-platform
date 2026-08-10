using System.Net;
using ProjectTime.Api.Ai;

const string RuntimeToken = "test-runtime-token-value-1234567890-abcdef";
const string TokenReference = "secretref://github-environment-test/celar-ai-oracle-runtime-token@1111111111111111111111111111111111111111";
const string Approval = "ORACLE-TEST-CI-20260809";

var touched = new[]
{
    "PROJECTPULSE_ENVIRONMENT",
    PulseAiExternalHttpsRuntimePolicy.EnabledVariable,
    PulseAiExternalHttpsRuntimePolicy.HostVariable,
    PulseAiExternalHttpsRuntimePolicy.ExpectedIpVariable,
    PulseAiExternalHttpsRuntimePolicy.ApprovalReferenceVariable,
    PulseAiExternalHttpsRuntimePolicy.ReadinessEndpointVariable,
    PulseAiExternalHttpsRuntimePolicy.MalwareScanEndpointVariable,
    PulseAiExternalHttpsRuntimePolicy.MalwareScanBearerTokenVariable,
    PulseAiExternalHttpsRuntimePolicy.MalwareScanBearerTokenSecretReferenceVariable,
    "PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT",
    "PROJECTPULSE_PRIVATE_INFERENCE_MODEL",
    "PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE",
    "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN",
    "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE",
    "PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT",
    "PROJECTPULSE_PRIVATE_EMBEDDING_MODEL",
    "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN",
    "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN_SECRET_REFERENCE",
    "PROJECTPULSE_PRIVATE_OCR_ENDPOINT",
    "PROJECTPULSE_PRIVATE_OCR_MODEL",
    "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN",
    "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN_SECRET_REFERENCE",
    "PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST",
    "PROJECTPULSE_CELAR_AI_TRAINING_ENABLED",
    "PROJECTPULSE_CELAR_AI_TRAINING_ENDPOINT",
    "PROJECTPULSE_CELAR_AI_TRAINING_HOST_ALLOWLIST",
    "PROJECTPULSE_CELAR_AI_TRAINING_BEARER_TOKEN",
    "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE",
    "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED",
    "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION",
    "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE"
};
var before = touched.ToDictionary(
    name => name,
    Environment.GetEnvironmentVariable,
    StringComparer.Ordinal);

try
{
    foreach (var name in touched) Environment.SetEnvironmentVariable(name, null);

    var disabled = PulseAiExternalHttpsRuntimePolicy.Evaluate();
    Require(!disabled.Enabled && disabled.Valid, "disabled mode is inert and valid");

    Set(PulseAiExternalHttpsRuntimePolicy.HostVariable, PulseAiExternalHttpsRuntimePolicy.ApprovedHost);
    var dangling = PulseAiExternalHttpsRuntimePolicy.Evaluate();
    Require(!dangling.Valid, "dangling external configuration is rejected while disabled");

    foreach (var name in touched) Environment.SetEnvironmentVariable(name, null);
    ConfigureValidTestRuntime();
    var active = PulseAiExternalHttpsRuntimePolicy.Evaluate();
    Require(active.Active, "exact protected Test configuration is accepted");
    Require(active.ExpectedAddress?.Equals(IPAddress.Parse("129.213.82.144")) == true,
        "configured public IPv4 pin is retained");

    var inference = new Uri("https://celarai.onenecklab.com/v1/chat/completions");
    Require(PulseAiExternalHttpsRuntimePolicy.TryGetPinnedAddress(
            inference, out var address, out var reason)
            && address.Equals(IPAddress.Parse("129.213.82.144"))
            && reason == "test_external_https_connect_ip_pinned",
        "approved endpoint connects only to the exact pinned address");
    Require(!PulseAiExternalHttpsRuntimePolicy.TryGetPinnedAddress(
            new Uri("https://celarai.onenecklab.com/v1/not-approved"), out _, out _),
        "unapproved path is rejected");
    Require(!PulseAiExternalHttpsRuntimePolicy.TryGetPinnedAddress(
            new Uri("https://example.com/v1/chat/completions"), out _, out _),
        "unapproved host is rejected");

    Set("PROJECTPULSE_ENVIRONMENT", "production");
    Require(!PulseAiExternalHttpsRuntimePolicy.Evaluate().Valid,
        "Production cannot enable the temporary external runtime");
    Set("PROJECTPULSE_ENVIRONMENT", "test");

    Set("PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN", "different-protected-token-value-1234567890");
    Require(!PulseAiExternalHttpsRuntimePolicy.Evaluate().Valid,
        "mismatched service bearer tokens are rejected");
    Set("PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN", RuntimeToken);

    Set(PulseAiExternalHttpsRuntimePolicy.ExpectedIpVariable, "10.0.0.10");
    Require(!PulseAiExternalHttpsRuntimePolicy.Evaluate().Valid,
        "private expected addresses are rejected by the public Test exception");
    Set(PulseAiExternalHttpsRuntimePolicy.ExpectedIpVariable, "129.213.82.144");

    var options = PulseAiPrivateRuntimeOptions.FromEnvironment();
    Require(options.HttpsMalwareScanConfigured && options.MalwareScannerConfigured,
        "runtime options activate authenticated HTTPS malware scanning");
    Require(options.MalwareScanEndpoint == "https://celarai.onenecklab.com/v1/scan",
        "runtime options preserve the exact scan endpoint");

    Console.WriteLine("CELAR_AI_ORACLE_EXTERNAL_HTTPS_RUNTIME_BEHAVIOR=PASS");
}
finally
{
    foreach (var pair in before)
        Environment.SetEnvironmentVariable(pair.Key, pair.Value);
}

return;

static void ConfigureValidTestRuntime()
{
    Set("PROJECTPULSE_ENVIRONMENT", "test");
    Set(PulseAiExternalHttpsRuntimePolicy.EnabledVariable, "true");
    Set(PulseAiExternalHttpsRuntimePolicy.HostVariable, PulseAiExternalHttpsRuntimePolicy.ApprovedHost);
    Set(PulseAiExternalHttpsRuntimePolicy.ExpectedIpVariable, "129.213.82.144");
    Set(PulseAiExternalHttpsRuntimePolicy.ApprovalReferenceVariable, Approval);
    Set(PulseAiExternalHttpsRuntimePolicy.ReadinessEndpointVariable,
        "https://celarai.onenecklab.com/health");

    Set("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT",
        "https://celarai.onenecklab.com/v1/chat/completions");
    Set("PROJECTPULSE_PRIVATE_INFERENCE_MODEL", PulseAiExternalHttpsRuntimePolicy.GenerationModel);
    Set("PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE", "bearer");
    Set("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN", RuntimeToken);
    Set("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE", TokenReference);

    Set("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT",
        "https://celarai.onenecklab.com/v1/embeddings");
    Set("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL", PulseAiExternalHttpsRuntimePolicy.EmbeddingModel);
    Set("PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN", RuntimeToken);
    Set("PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN_SECRET_REFERENCE", TokenReference);

    Set("PROJECTPULSE_PRIVATE_OCR_ENDPOINT",
        "https://celarai.onenecklab.com/v1/extract");
    Set("PROJECTPULSE_PRIVATE_OCR_MODEL", PulseAiExternalHttpsRuntimePolicy.OcrModel);
    Set("PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN", RuntimeToken);
    Set("PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN_SECRET_REFERENCE", TokenReference);

    Set(PulseAiExternalHttpsRuntimePolicy.MalwareScanEndpointVariable,
        "https://celarai.onenecklab.com/v1/scan");
    Set(PulseAiExternalHttpsRuntimePolicy.MalwareScanBearerTokenVariable, RuntimeToken);
    Set(PulseAiExternalHttpsRuntimePolicy.MalwareScanBearerTokenSecretReferenceVariable, TokenReference);
    Set("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST", PulseAiExternalHttpsRuntimePolicy.ApprovedHost);

    Set("PROJECTPULSE_CELAR_AI_TRAINING_ENABLED", "false");
    Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE",
        PulseAiExternalHttpsRuntimePolicy.MalwareScannerMode);
    Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED", "true");
    Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION",
        "clamav-1.5.3-daily-28087-20260809");
    Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE", Approval);
}

static void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);

static void Require(bool condition, string evidence)
{
    if (!condition)
        throw new InvalidOperationException($"Oracle external-runtime assertion failed: {evidence}.");
}

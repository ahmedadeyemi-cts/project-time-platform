using System.Diagnostics;
using System.Text.Json;
using ProjectTime.Api.Ai;
using Microsoft.Extensions.Logging.Abstractions;

internal static class Module025ProviderQualification
{
    // Explicit CLI only. CI never calls this path. One synthetic Plan phase,
    // one chosen provider, no private source, DB write, publication or fallback.
    internal static async Task RunAsync(string target, bool useModule064Store = false)
    {
        if (target is not ("claude" or "openai")) throw new ArgumentException("Choose claude or openai.");
        if (useModule064Store && Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT") != "test")
        { Block(target, "module064_qualification_requires_test_environment"); return; }
        var configuration = new ProjectPulseAiConfiguration();
        if (useModule064Store)
        {
            // Same read-only hydration used by the API. No secret is exported,
            // no table is created and no stored configuration is changed.
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var store = new ProjectPulseAiSecretStore(NullLogger<ProjectPulseAiSecretStore>.Instance);
                if (!store.Available) { Block(target, "module064_store_unavailable"); return; }
                var secrets = await store.LoadAsync(timeout.Token);
                var secret = secrets.SingleOrDefault(item => item.ProviderCode == target);
                if (secret is null) { Block(target, "module064_provider_secret_unavailable"); return; }
                configuration.ApplyStoredSecret(target, secret.ApiKey, secret.Version, secret.RotatedAt);
                var models = await store.LoadModelsAsync(timeout.Token);
                if (models.TryGetValue(target, out var model)) configuration.ApplyStoredModel(target, model);
                var enabled = await store.LoadEnabledAsync(timeout.Token);
                if (enabled.TryGetValue(target, out var active)) configuration.ApplyStoredEnabled(target, active);
            }
            catch (Exception)
            { Block(target, "module064_configuration_read_failed"); return; }
        }
        var providerConfiguration = configuration.Provider(target);
        if (!providerConfiguration.ApprovedModels.Contains(providerConfiguration.Model, StringComparer.OrdinalIgnoreCase))
        { Block(target, "provider_model_not_approved"); return; }
        var phase = new Module025PhaseExecution("Plan", 0, (_, _) => Task.CompletedTask);
        var evidence = new CelarAiAuthoritativeScopeEvidence(Guid.NewGuid(), 1, "SYNTHETIC-QUALIFICATION", "Synthetic Qualification",
            "Upgrade Cisco Unified Communications Manager from 14.0 to 15.0. Include readiness, compatibility, licensing, backups and rollback planning.",
            DateTimeOffset.UtcNow, phase);
        var sanitizer = new PulseAiEscalationSanitizer();
        var adapter = Module025ExternalSowAdapter.TryCreate(evidence)!;
        var request = adapter.Prepare(sanitizer, out var diagnostic);
        if (!providerConfiguration.Enabled || !providerConfiguration.Configured || request is null)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { passed = false, called = false, provider = target,
                diagnostic = request is null ? diagnostic : "provider_not_enabled_or_configured" }));
            Environment.ExitCode = 1;
            return;
        }
        using var factory = new QualificationHttpFactory();
        IProjectPulseAiProvider provider = target == "claude"
            ? new ProjectPulseClaudeProvider(factory, configuration) : new ProjectPulseOpenAiProvider(factory, configuration);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(Module025GenerationEngine.ProviderTimeoutSeconds));
        var elapsed = Stopwatch.StartNew();
        ProjectPulseAiProviderResult result;
        try { result = await provider.GenerateAsync(request!, deadline.Token); }
        catch (OperationCanceledException)
        { result = new(target, "unavailable", null, "provider_deadline_exceeded", null, null, null, null); }
        result = adapter.ValidateResult(result, "synthetic-qualification", sanitizer);
        var passed = result.IsSuccess && adapter.AcceptedAnswer is not null;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passed, called = true, provider = target, requestedModel = providerConfiguration.Model,
            configurationSource = useModule064Store ? "module064_store" : "environment",
            elapsedMilliseconds = elapsed.ElapsedMilliseconds, result.Usage, result.SowDiagnostics,
            diagnostic = passed ? "module025_plan_phase_qualified" : result.Code ?? diagnostic,
            phase = "Plan", workPackages = adapter.AcceptedAnswer?.FlowHivePlan?.Tasks.Count ?? 0,
            plan = passed ? adapter.AcceptedAnswer?.FlowHivePlan : null,
            fullLifecyclePassed = false, productionMutation = false
        }));
        Environment.ExitCode = passed ? 0 : 1;
    }
    private static void Block(string target, string diagnostic)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { passed = false, called = false, provider = target, diagnostic }));
        Environment.ExitCode = 1;
    }
    private sealed class QualificationHttpFactory : IHttpClientFactory, IDisposable
    {
        private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false, UseCookies = false,
            UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        public HttpClient CreateClient(string name) => new(_handler, false) { Timeout = Timeout.InfiniteTimeSpan };
        public void Dispose() => _handler.Dispose();
    }
}

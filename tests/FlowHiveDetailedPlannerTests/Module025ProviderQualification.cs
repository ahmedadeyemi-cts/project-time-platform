using System.Diagnostics;
using System.Text.Json;
using ProjectTime.Api.Ai;

internal static class Module025ProviderQualification
{
    // Explicit CLI only. CI never calls this path. One synthetic Plan phase,
    // one chosen provider, no private source, DB write, publication or fallback.
    internal static async Task RunAsync(string target)
    {
        if (target is not ("claude" or "openai")) throw new ArgumentException("Choose claude or openai.");
        var configuration = new ProjectPulseAiConfiguration();
        var providerConfiguration = configuration.Provider(target);
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
        var passed = result.IsSuccess && result.Content is not null
            && adapter.Validate(result.Content, target, "synthetic-qualification", sanitizer, out diagnostic);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passed, called = true, provider = target, requestedModel = providerConfiguration.Model,
            elapsedMilliseconds = elapsed.ElapsedMilliseconds, result.Usage,
            diagnostic = passed ? "module025_plan_phase_qualified" : result.Code ?? diagnostic,
            phase = "Plan", workPackages = adapter.AcceptedAnswer?.FlowHivePlan?.Tasks.Count ?? 0,
            plan = passed ? adapter.AcceptedAnswer?.FlowHivePlan : null,
            fullLifecyclePassed = false, productionMutation = false
        }));
        Environment.ExitCode = passed ? 0 : 1;
    }
    private sealed class QualificationHttpFactory : IHttpClientFactory, IDisposable
    {
        private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false, UseCookies = false,
            UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        public HttpClient CreateClient(string name) => new(_handler, false) { Timeout = Timeout.InfiniteTimeSpan };
        public void Dispose() => _handler.Dispose();
    }
}

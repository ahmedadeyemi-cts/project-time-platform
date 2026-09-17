using System.Text.Json;

namespace ProjectTime.Api.Ai;

// Module 025 owns its generation state. Partial phases are private checkpoints,
// never a published SOW, a confirmed version, or a generic fallback artifact.
internal static class Module025GenerationEngine
{
    internal const string ContractVersion = "module025-detailed-phases-v2";
    internal const int DeadlineSeconds = 1200;
    internal const int ProviderTimeoutSeconds = 120;
    internal const int AttemptsPerPhase = 2;
    internal const int MaximumOutputTokens = 6144;
    internal static readonly string[] Phases = ["Plan", "Design", "Implement", "Validate", "Release"];

    internal static async Task<CelarAiComposeResult> RunAsync(
        CelarAiAuthoritativeScopeEvidence evidence,
        IReadOnlyDictionary<string, CelarAiComposeResult> checkpoints,
        IReadOnlyDictionary<string, int> attempts,
        Func<CelarAiAuthoritativeScopeEvidence, CancellationToken, Task<CelarAiComposeResult>> generate,
        Func<Module025GenerationProgress, CancellationToken, Task> persist,
        CancellationToken cancellationToken)
    {
        var results = new List<CelarAiComposeResult>();
        foreach (var phase in Phases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (checkpoints.TryGetValue(phase, out var checkpoint))
            {
                // Revalidate stored data against today's contract and exact saved source.
                PulseAiPrivateRagService.ValidateModule025Phase(checkpoint.FlowHivePlan, phase, evidence);
                results.Add(checkpoint);
                await persist(new("phase_resumed", phase, checkpoint.SelectedTarget), cancellationToken);
                continue;
            }
            var execution = new Module025PhaseExecution(phase, attempts.GetValueOrDefault(phase), persist);
            await persist(new("phase_started", phase), cancellationToken);
            var result = await generate(evidence with { PhaseExecution = execution }, cancellationToken);
            if (result.SowDraft is null || result.FlowHivePlan is null)
            {
                await persist(new("phase_failed", phase, result.SelectedTarget,
                    DiagnosticCode: "module025_phase_provider_contract_failed",
                    TargetDecisions: result.TargetDecisions), cancellationToken);
                return result with { SowDraft = null, FlowHivePlan = null };
            }
            PulseAiPrivateRagService.ValidateModule025Phase(result.FlowHivePlan, phase, evidence);
            // Await durable commit before spending inference on the next phase.
            await persist(new("phase_completed", phase, result.SelectedTarget,
                Result: result, TargetDecisions: result.TargetDecisions), cancellationToken);
            results.Add(result);
        }
        var plan = PulseAiPrivateRagService.AssembleModule025PhasePlans(results.Select(result => result.FlowHivePlan!).ToArray());
        PulseAiPrivateRagService.ValidateModule025Phase(plan, null, evidence);
        if (JsonSerializer.Serialize(plan).Length > 96_000)
            throw new JsonException("module025_assembled_plan_limit_exceeded");
        return results[^1] with
        {
            FlowHivePlan = plan,
            SowDraft = CelarAiEnterprisePlatformService.BuildSowDraftFromPlan(plan, evidence.EngagementNumber, evidence.CustomerName),
            AttemptedTargets = results.SelectMany(result => result.AttemptedTargets ?? []).Distinct().ToArray(),
            SkippedTargets = results.SelectMany(result => result.SkippedTargets ?? []).Distinct().ToArray(),
            TargetDecisions = results.SelectMany(result => result.TargetDecisions ?? []).Distinct().ToArray()
        };
    }
}

internal sealed record Module025GenerationProgress(
    string Stage, string Phase, string Provider = "", int Attempt = 0,
    string DiagnosticCode = "", string Model = "", int InputCharacters = 0,
    int OutputCharacters = 0, long ElapsedMilliseconds = 0,
    CelarAiComposeResult? Result = null,
    IReadOnlyList<ProjectPulseAiTargetDecision>? TargetDecisions = null,
    long? InputTokens = null, long? OutputTokens = null, string RequestedModel = "",
    long? ReasoningTokens = null, Module025ProviderDiagnostics? SowDiagnostics = null);

internal sealed class Module025PhaseExecution(
    string phase, int attempts,
    Func<Module025GenerationProgress, CancellationToken, Task> persist)
{
    internal string Phase { get; } = phase;
    private int _attempts = attempts;
    private string _provider = string.Empty;
    private System.Diagnostics.Stopwatch? _elapsed;

    internal async Task<bool> BeforeAttemptAsync(string provider, CancellationToken token)
    {
        if (_attempts >= Module025GenerationEngine.AttemptsPerPhase)
            return false;
        _provider = provider;
        // Reserve the attempt durably before the provider call. A process restart
        // cannot silently reset the call budget or duplicate a completed phase.
        await persist(new("provider_started", Phase, provider, ++_attempts), token);
        _elapsed = System.Diagnostics.Stopwatch.StartNew();
        return true;
    }

    internal Task ObserveProviderAsync(ProjectPulseAiProviderResult result, string requestedModel, CancellationToken token) =>
        persist(new("provider_finished", Phase, _provider, _attempts,
            result.Code ?? (result.IsSuccess ? "phase_contract_completed" : "provider_failed"),
            Model: requestedModel, ElapsedMilliseconds: _elapsed?.ElapsedMilliseconds ?? 0,
            OutputCharacters: result.SowDiagnostics?.OutputTextCharacters ?? result.Content?.Length ?? 0,
            InputTokens: result.Usage?.InputTokens, OutputTokens: result.Usage?.OutputTokens, RequestedModel: requestedModel,
            ReasoningTokens: result.Usage?.ReasoningTokens, SowDiagnostics: result.SowDiagnostics), token);

    internal Task ObserveAsync(PulseAiPrivateModelResult result, CancellationToken token) =>
        persist(new("provider_completed", Phase, _provider, _attempts,
            result.DiagnosticCode, result.Model, result.InputCharacters,
            result.OutputCharacters, _elapsed?.ElapsedMilliseconds ?? 0), token);
}

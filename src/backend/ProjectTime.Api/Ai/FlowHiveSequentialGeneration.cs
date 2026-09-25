using System.Text.Json;
using ProjectTime.Api.Modules;

namespace ProjectTime.Api.Ai;

// Server-owned checkpoints. Evidence text and partial plans never enter the public progress response.
internal sealed record FlowHivePhaseState(string Phase, string Status = "pending", int Attempts = 0,
    DateTimeOffset? StartedAt = null, DateTimeOffset? CompletedAt = null,
    PulseAiPrivateFlowHivePlan? Plan = null, string DiagnosticCode = "");

internal sealed record FlowHiveSequentialState(string SourceFingerprint, PulseAiPrivateRetrievalResult? Evidence,
    IReadOnlyList<FlowHivePhaseState> Phases)
{
    internal static FlowHiveSequentialState Empty(string fingerprint) =>
        new(fingerprint, null, FlowHiveSequentialExecution.Phases.Select(p => new FlowHivePhaseState(p)).ToArray());

    internal object[] Progress(bool terminal, DateTimeOffset? completedAt) => Phases.Select((p, index) => (object)new
    {
        number = index + 1, total = 5, name = p.Phase,
        status = terminal && p.Status == "processing" ? "stopped" : terminal && p.Status == "retrying" ? "needs_attention" : p.Status,
        p.StartedAt, completedAt = p.CompletedAt ?? (terminal && p.StartedAt.HasValue ? completedAt : null),
        attemptCount = p.Attempts, taskCount = p.Plan?.Tasks.Count ?? 0, p.DiagnosticCode
    }).ToArray();
}

internal sealed class FlowHiveSequentialExecution(
    ProjectPlanningDocumentResolution documents, FlowHiveSequentialState? saved,
    Func<FlowHiveSequentialState, CancellationToken, Task> persist)
{
    internal static readonly string[] Phases = ["Plan", "Design", "Implement", "Validate", "Release"];
    internal const int MaximumAttempts = 4;
    internal const int MinimumTasksPerPhase = 3;
    internal const int MaximumTasksPerPhase = 8;
    internal const int MaximumOutputTokens = 3072;
    internal const string PhaseSchema = "flowhive_detailed_phase";
    internal const int GatewayPhaseTimeoutSeconds = 150;
    internal static readonly TimeSpan PhaseBudget = TimeSpan.FromSeconds(210);
    internal FlowHiveSequentialState State { get; private set; } = saved ??
        FlowHiveSequentialState.Empty(ProjectFlowHiveExecutionPolicy.VersionFingerprint(documents));

    internal async Task SaveAsync(FlowHiveSequentialState next, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (next.SourceFingerprint != ProjectFlowHiveExecutionPolicy.VersionFingerprint(documents))
            throw new InvalidOperationException("flowhive_phase_source_changed");
        await persist(next, token);
        State = next;
    }

    internal PulseAiPrivateRetrievalResult PinEvidence(PulseAiPrivateRetrievalResult evidence)
    {
        if (State.SourceFingerprint != ProjectFlowHiveExecutionPolicy.VersionFingerprint(documents))
            throw new InvalidOperationException("flowhive_phase_source_changed");
        var current = evidence.Chunks.Where(chunk => documents.SelectedDocuments.Any(document =>
            document.DocumentId == chunk.DocumentId && document.ActiveVersionId == chunk.DocumentVersionId
            && document.ActiveSourceSha256.Length == 64
            && string.Equals(document.ActiveSourceSha256, chunk.SourceSha256, StringComparison.OrdinalIgnoreCase)
            && document.ActiveDocumentVersion == chunk.DocumentVersion && document.EngineeringVisible)).ToArray();
        if (!current.Any(chunk => chunk.DocumentId == documents.StatementOfWork?.DocumentId))
            throw new InvalidOperationException("flowhive_current_sow_evidence_missing");

        // Preserve more of the contractual scope before secondary evidence. The
        // five phase queries reuse this one immutable evidence snapshot and only
        // change which passages are prioritized for the current planning question.
        var scope = current.Where(c => c.DocumentId == documents.StatementOfWork?.DocumentId)
            .OrderByDescending(c => ScopeSection(c.SectionTitle + " " + c.CitationAnchor)).ToArray();
        var ordered = scope.Take(5).Concat(current.GroupBy(c => c.DocumentId).Select(g => g.First()))
            .Concat(current).DistinctBy(c => c.ChunkId);
        var remaining = 18000;
        var chunks = new List<PulseAiPrivateRetrievedChunk>();
        foreach (var chunk in ordered)
        {
            if (remaining <= 0) break;
            var size = Math.Min(chunk.Text.Length, Math.Min(2600, remaining));
            chunks.Add(chunk with { Text = chunk.Text[..size] });
            remaining -= size;
        }
        return evidence with { Chunks = chunks.Select((c, i) => c with { RankOrder = i + 1 }).ToArray() };
    }

    internal PulseAiPrivateRetrievalResult EvidenceForPhase(PulseAiPrivateRetrievalResult pinned, string phase)
    {
        var terms = PhaseEvidenceTerms(phase);
        var sowId = documents.StatementOfWork?.DocumentId;
        var scope = pinned.Chunks
            .Where(chunk => chunk.DocumentId == sowId && ScopeSection(chunk.SectionTitle + " " + chunk.CitationAnchor))
            .Take(5);
        var phaseMatches = pinned.Chunks
            .Where(chunk => EvidenceMatches(chunk, terms))
            .OrderByDescending(chunk => chunk.CombinedScore);
        var eachDocument = pinned.Chunks
            .GroupBy(chunk => chunk.DocumentId)
            .Select(group => group.OrderBy(chunk => chunk.RankOrder).First());

        var ordered = scope.Concat(phaseMatches).Concat(eachDocument).Concat(pinned.Chunks)
            .DistinctBy(chunk => chunk.ChunkId);
        var remaining = 14000;
        var selected = new List<PulseAiPrivateRetrievedChunk>();
        foreach (var chunk in ordered)
        {
            if (remaining <= 0) break;
            var size = Math.Min(chunk.Text.Length, Math.Min(2400, remaining));
            selected.Add(chunk with { Text = chunk.Text[..size] });
            remaining -= size;
        }

        if (!selected.Any(chunk => chunk.DocumentId == sowId))
            throw new InvalidOperationException("flowhive_phase_sow_evidence_missing");

        // RankOrder is the pinned citation identifier. Never renumber it for a
        // phase-specific query or a returned citation could point at another chunk.
        return pinned with { Chunks = selected.OrderBy(chunk => chunk.RankOrder).ToArray() };
    }

    private static bool EvidenceMatches(PulseAiPrivateRetrievedChunk chunk, IReadOnlyList<string> terms)
    {
        var searchable = string.Join(" ", chunk.SectionTitle, chunk.CitationAnchor, chunk.SheetName ?? string.Empty, chunk.Text);
        return terms.Any(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> PhaseEvidenceTerms(string phase) => phase switch
    {
        "Plan" => ["scope", "service", "deliverable", "prerequisite", "responsib", "assum", "depend", "inventory", "access", "license", "stakeholder", "readiness"],
        "Design" => ["design", "architecture", "target", "integration", "compatib", "capacity", "security", "network", "interface", "rollback", "test", "acceptance"],
        "Implement" => ["implement", "install", "configur", "upgrade", "migrat", "change", "backup", "rollback", "cutover", "deploy", "version", "integration"],
        "Validate" => ["validat", "test", "acceptance", "functional", "integration", "security", "resilien", "performance", "defect", "evidence", "success criteria"],
        "Release" => ["release", "handoff", "closeout", "document", "training", "knowledge", "support", "monitor", "operational", "acceptance", "sign-off", "transition"],
        _ => throw new ArgumentException("Unknown FlowHive phase.", nameof(phase))
    };

    private static bool ScopeSection(string text) => text.Contains("scope", StringComparison.OrdinalIgnoreCase)
        || text.Contains("service overview", StringComparison.OrdinalIgnoreCase)
        || text.Contains("statement of work", StringComparison.OrdinalIgnoreCase);

    internal static string PhasePurpose(string phase) => phase switch
    {
        "Plan" => "Determine what must be planned to deliver the SOW scope: confirm inventory and current/target versions, scope boundaries, access, dependencies, licensing, stakeholders, risks, readiness and the delivery approach. Do not perform implementation in this phase.",
        "Design" => "Determine the design needed to deliver that same scope: target configuration and architecture, supported transition path, integrations, compatibility, capacity, security, change sequence, rollback design, and the test and acceptance approach. Use Plan findings as proposed inputs, not verified customer facts.",
        "Implement" => "Specify the technical execution required by the approved design: backups, prerequisites, configuration or upgrades, integration changes, migration sequence, change-window controls and executable rollback preparation. Preserve the source and target versions in the SOW.",
        "Validate" => "Specify how the implemented scope will be tested: functional, integration, security, resilience and performance checks applicable to the documented solution, defect correction, retesting and measurable customer acceptance evidence.",
        "Release" => "Specify controlled handover and closeout: approved production release, operational readiness, monitoring, as-built documentation, support knowledge transfer, acceptance sign-off and project closure. Do not claim that any work has already been completed.",
        _ => throw new ArgumentException("Unknown FlowHive phase.", nameof(phase))
    };
}

public sealed partial class PulseAiPrivateRagService
{
    private static PulseAiPrivateFlowHivePlan ParseFlowHivePhase(string content,
        PulseAiPrivateRetrievalResult evidence, IReadOnlyList<string> phases)
    {
        using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        if (!TryModelJsonProperty(json.RootElement, "tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            throw new JsonException("flowhive_tasks_required");
        var citations = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        var available = evidence.Chunks.Select(c => c.RankOrder).ToHashSet();
        foreach (var task in tasks.EnumerateArray())
        {
            var wbs = ModelJsonString(task, "wbs");
            if (!TryModelJsonProperty(task, "citationIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
                throw new JsonException("flowhive_task_citations_required");
            var selected = ids.EnumerateArray().Select(id => id.TryGetInt32(out var n) ? n : -1).Distinct().ToArray();
            if (selected.Length == 0 || selected.Any(id => !available.Contains(id))
                || ModelJsonDecimal(task, "estimatedHours") is null or <= 0m
                || ModelJsonDecimal(task, "estimatedDurationDays") is null or <= 0m
                || !citations.TryAdd(wbs, selected))
                throw new JsonException("flowhive_task_evidence_or_estimate_invalid");
        }

        // FlowHive asks the model for the project-specific identity, outcome,
        // effort, citations and technical steps. The server deterministically
        // completes repetitive review fields from that task instead of forcing a
        // 4B model to reproduce a large schema for every work package.
        var validated = ParseModule025PlanContent(
            content,
            evidence,
            phases,
            allowCompactTaskFields: true);

        foreach (var phase in phases)
        {
            var phaseTasks = validated.Tasks.Where(task => string.Equals(task.Phase, phase, StringComparison.Ordinal)).ToArray();
            if (phaseTasks.Length < FlowHiveSequentialExecution.MinimumTasksPerPhase
                || phaseTasks.Length > FlowHiveSequentialExecution.MaximumTasksPerPhase)
                throw new JsonException("flowhive_phase_task_depth_invalid");
            if (phaseTasks.Select(task => task.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != phaseTasks.Length)
                throw new JsonException("flowhive_phase_task_names_not_distinct");
            if (phaseTasks.SelectMany(task => task.DetailedSteps ?? Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() < phaseTasks.Length * 2)
                throw new JsonException("flowhive_phase_steps_not_distinct");
        }

        return validated with
        {
            Tasks = validated.Tasks.Select(t => t with { CitationIds = citations[t.Wbs] }).ToArray(),
            CitationIds = citations.Values.SelectMany(ids => ids).Distinct().OrderBy(id => id).ToArray(),
            Milestones = []
        };
    }

    internal static async Task<PulseAiPrivateModelResult> GenerateFlowHiveSequentialAsync(
        PulseAiPrivateModelRequest request, PulseAiPrivateRetrievalResult evidence,
        FlowHiveSequentialExecution execution,
        Func<PulseAiPrivateModelRequest, CancellationToken, Task<PulseAiPrivateModelResult>> generate,
        CancellationToken token)
    {
        var pinned = execution.PinEvidence(execution.State.Evidence ?? evidence);
        if (execution.State.Evidence is null)
            await execution.SaveAsync(execution.State with { Evidence = pinned }, token);
        else if (pinned.Chunks.Count != execution.State.Evidence.Chunks.Count)
            throw new InvalidOperationException("flowhive_checkpoint_evidence_invalid");

        PulseAiPrivateModelResult? last = null;
        var inputCharacters = 0;
        foreach (var phase in FlowHiveSequentialExecution.Phases)
        {
            token.ThrowIfCancellationRequested();
            var index = Array.IndexOf(FlowHiveSequentialExecution.Phases, phase);
            var state = execution.State.Phases.Single(p => p.Phase == phase);
            var phaseEvidence = execution.EvidenceForPhase(pinned, phase);
            if (state.Status == "completed" && state.Plan is not null)
            {
                _ = ParseFlowHivePhase(JsonSerializer.Serialize(state.Plan), pinned, [phase]);
                continue;
            }

            // One wall-clock budget governs the entire phase, including a schema
            // repair. The previous implementation restarted a fresh 330-second
            // timer for every repair and allowed one failed phase to occupy the UI
            // for almost ten minutes.
            using var phaseDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            phaseDeadline.CancelAfter(FlowHiveSequentialExecution.PhaseBudget);
            var feedback = string.Empty;
            var providerAttempts = 0;
            while (state.Attempts < FlowHiveSequentialExecution.MaximumAttempts
                && providerAttempts++ < 2
                && !phaseDeadline.IsCancellationRequested)
            {
                state = state with
                {
                    Status = "processing",
                    Attempts = state.Attempts + 1,
                    StartedAt = state.StartedAt ?? DateTimeOffset.UtcNow,
                    CompletedAt = null,
                    DiagnosticCode = ""
                };
                await SavePhaseAsync(state, token);

                var prior = JsonSerializer.Serialize(execution.State.Phases
                    .Take(index)
                    .Where(p => p.Status == "completed" && p.Plan is not null)
                    .TakeLast(1)
                    .SelectMany(p => p.Plan!.Tasks)
                    .Select(t => new { t.Wbs, t.Name, Outputs = (t.Outputs ?? []).Take(2) }));

                var phaseRequest = request with
                {
                    OutputSchemaName = FlowHiveSequentialExecution.PhaseSchema,
                    MaximumOutputTokens = FlowHiveSequentialExecution.MaximumOutputTokens,
                    Sources = phaseEvidence.Chunks,
                    SystemInstruction = BuildFlowHivePhaseInstruction(request.SystemInstruction, phase, index, prior, feedback),
                    UserInstruction = request.UserInstruction
                        + "\nGenerate ONLY this stage of the project plan from the supplied current SOW/GSD evidence.\n"
                        + $"Stage {index + 1} of 5: {phase}. {FlowHiveSequentialExecution.PhasePurpose(phase)} "
                        + $"Return {FlowHiveSequentialExecution.MinimumTasksPerPhase} to 6 distinct {phase} work packages using WBS {index + 1}.1 onward. "
                        + "Every task must be directly traceable to at least one supplied citation. Missing customer facts belong in the description as a review condition; do not invent them. "
                        + feedback
                };

                try
                {
                    last = await generate(phaseRequest, phaseDeadline.Token).WaitAsync(phaseDeadline.Token);
                    inputCharacters += last.InputCharacters;
                    if (!last.Succeeded)
                    {
                        state = state with { DiagnosticCode = last.DiagnosticCode };
                        // Module 064 owns private-provider failover. Do not replay
                        // an unavailable route simply to consume the phase budget.
                        break;
                    }

                    var plan = ParseFlowHivePhase(last.Content, phaseEvidence, [phase]);
                    if (plan.Tasks.Any(t => !string.Equals(t.Phase, phase, StringComparison.Ordinal)
                        || !t.Wbs.StartsWith($"{index + 1}.", StringComparison.Ordinal))
                        || plan.Tasks.Select(t => t.Wbs).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Tasks.Count)
                        throw new JsonException("flowhive_phase_wbs_invalid");

                    state = state with
                    {
                        Status = "completed",
                        CompletedAt = DateTimeOffset.UtcNow,
                        Plan = plan,
                        DiagnosticCode = ""
                    };
                    await SavePhaseAsync(state, token);
                    break;
                }
                catch (OperationCanceledException) when (phaseDeadline.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    state = state with { DiagnosticCode = "flowhive_phase_deadline_exceeded" };
                    break;
                }
                catch (JsonException)
                {
                    state = state with { DiagnosticCode = "flowhive_phase_contract_invalid" };
                    feedback = "The prior response failed the compact FlowHive contract. Return three to six distinct, SOW-specific tasks with unique WBS values, positive estimates, at least two concrete steps, and valid supplied citation IDs. Return JSON only.";
                }
            }

            if (state.Status != "completed")
            {
                if (phaseDeadline.IsCancellationRequested && string.IsNullOrEmpty(state.DiagnosticCode))
                    state = state with { DiagnosticCode = "flowhive_phase_deadline_exceeded" };
                state = state with
                {
                    Status = "retrying",
                    CompletedAt = null,
                    DiagnosticCode = string.IsNullOrEmpty(state.DiagnosticCode)
                        ? "flowhive_phase_attempts_exhausted"
                        : state.DiagnosticCode
                };
                await SavePhaseAsync(state, token);
                return new(
                    "private_model_failed",
                    last?.Provider ?? "celar_ai",
                    last?.Model ?? "",
                    "",
                    inputCharacters,
                    0,
                    state.DiagnosticCode,
                    DateTimeOffset.UtcNow);
            }
        }

        var combined = AssembleModule025PhasePlans(execution.State.Phases.Select(p => p.Plan!).ToArray());
        combined = combined with
        {
            CitationIds = combined.Tasks.SelectMany(t => t.CitationIds).Distinct().OrderBy(id => id).ToArray(),
            Milestones = []
        };
        var content = JsonSerializer.Serialize(combined);
        if (content.Length > 512000) throw new JsonException("flowhive_assembled_plan_limit_exceeded");
        _ = ParseFlowHivePhase(content, pinned, FlowHiveSequentialExecution.Phases);
        return last is null
            ? new("private_model_completed", "celar_ai", "checkpoint", content, 0, content.Length, "", DateTimeOffset.UtcNow)
            : last with
            {
                Content = content,
                InputCharacters = inputCharacters,
                OutputCharacters = content.Length,
                CompletedAt = DateTimeOffset.UtcNow
            };

        Task SavePhaseAsync(FlowHivePhaseState phase, CancellationToken ct) => execution.SaveAsync(execution.State with
        {
            Phases = execution.State.Phases.Select(p => p.Phase == phase.Phase ? phase : p).ToArray()
        }, ct);
    }

    private static string BuildFlowHivePhaseInstruction(
        string baseInstruction,
        string phase,
        int phaseIndex,
        string prior,
        string feedback)
    {
        return baseInstruction
            + "\nYou are building one phase of a real professional-services WBS from authorized private project evidence."
            + "\nThe current Work Register SOW Scope of Services / Service Overview is the delivery authority. GSD and authorized supporting documents may refine design, prerequisites, implementation constraints, validation, and handoff."
            + "\n" + FlowHiveSequentialExecution.PhasePurpose(phase)
            + $"\nReturn ONLY {phase} work. Produce 3 to 6 substantive, non-overlapping tasks using WBS {phaseIndex + 1}.1 onward. Do not create a phase-summary row or milestone."
            + "\nKeep the model output compact. The server will derive repetitive review fields from each accepted task. Return one JSON object with a top-level tasks array. Every task must include exactly these planning fields: "
            + "{\"wbs\":\"1.1\",\"phase\":\"Plan\",\"name\":\"specific activity\",\"description\":\"source-specific outcome of at least 80 characters\",\"estimatedHours\":8,\"estimatedDurationDays\":1,\"requiredRoles\":[\"role\"],\"predecessors\":[],\"citationIds\":[1],\"detailedSteps\":[\"concrete step 1\",\"concrete step 2\"]}."
            + "\nUse actual supplied citation IDs, not the example citation. Preserve cited products, versions, quantities, interfaces and requirements in the task name, description, or steps when present. Never invent missing facts."
            + "\nTask names and steps must describe what the delivery team will actually do for this project's scope, not generic phrases such as review scope, implement solution, validate system, or complete handoff."
            + "\nEffort and duration are review-only estimates. Cross-phase predecessor references may use the immediately preceding validated WBS when appropriate."
            + "\nEarlier validated phase tasks (planning proposals, never customer facts): " + prior
            + (string.IsNullOrWhiteSpace(feedback) ? "" : "\nRepair guidance: " + feedback);
    }
}

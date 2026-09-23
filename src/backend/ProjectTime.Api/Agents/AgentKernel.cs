using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Agents;

// Preparatory library: deliberately not registered with HTTP endpoints, hosted
// workers or workspaces until production authority/storage adapters are qualified.
// Phase one can READ and PROPOSE only. There is no mutation/publication executor.
public sealed class AgentKernel
{
    private readonly Func<AgentOptions> options;
    private readonly IAgentAuthority authority;
    private readonly IAgentRunStore store;
    private readonly IAgentDecisionSource model;
    private readonly IAgentHandoffResolver recipients;
    private readonly IReadOnlyDictionary<string, IAgentReadTool> tools;
    private readonly TimeProvider clock;

    public AgentKernel(Func<AgentOptions> options, IAgentAuthority authority, IAgentRunStore store,
        IAgentDecisionSource model, IAgentHandoffResolver recipients, IEnumerable<IAgentReadTool> tools,
        TimeProvider? clock = null)
    {
        this.options = options; this.authority = authority; this.store = store;
        this.model = model; this.recipients = recipients; this.clock = clock ?? TimeProvider.System;
        this.tools = tools.ToDictionary(t => t.Code, StringComparer.Ordinal);
        if (this.tools.Values.Any(t => !t.ReadOnly)) throw new AgentBoundaryException("agent_write_tool_rejected");
    }

    public async Task<AgentRun> StartAsync(AgentActor actor, AgentResource resource, string capability,
        string goal, CancellationToken token = default)
    {
        var limits = Guard(actor, token);
        _ = AgentCapabilityCatalog.Get(capability);
        if (resource.Id == Guid.Empty || string.IsNullOrWhiteSpace(resource.Kind) || resource.Kind.Length > 40
            || string.IsNullOrWhiteSpace(resource.ScopeKey) || resource.ScopeKey.Length > 128
            || string.IsNullOrWhiteSpace(resource.SourceVersion) || resource.SourceVersion.Length > 128
            || string.IsNullOrWhiteSpace(goal) || goal.Length > 4000)
            throw new AgentBoundaryException("agent_input_invalid");
        var now = clock.GetUtcNow();
        var run = new AgentRun(Guid.NewGuid(), actor, resource, capability, goal.Trim(), now, now.AddSeconds(limits.RunSeconds));
        var access = await CheckAsync(run, "start", token);
        run = run with { PolicyRevision = access.PolicyRevision };
        await store.CreateAsync(run, token);
        return run;
    }

    public async Task<AgentRun> AdvanceAsync(Guid id, AgentActor actor, int expectedRevision,
        CancellationToken token = default)
    {
        var limits = Guard(actor, token);
        var run = await OwnedAsync(id, actor, token);
        if (run.Revision != expectedRevision || run.Status != AgentRunStatus.Ready)
            throw new AgentBoundaryException("agent_run_changed_or_not_ready");
        if (clock.GetUtcNow() >= run.DeadlineAt || run.ModelCalls >= limits.MaximumModelCalls)
            return await ReplaceAsync(run, run with { Status = AgentRunStatus.Blocked, Diagnostic = "agent_budget_exhausted" }, token);
        var access = await CheckAsync(run, "model", token);
        var stepDeadline = clock.GetUtcNow().AddSeconds(limits.StepSeconds);
        if (stepDeadline > run.DeadlineAt) stepDeadline = run.DeadlineAt;
        // Durable reservation BEFORE inference. Concurrent requests cannot both
        // enter; a crash remains Running until explicit interruption recovery.
        run = await ReplaceAsync(run, run with { Status = AgentRunStatus.Running,
            ModelCalls = run.ModelCalls + 1, StepDeadlineAt = stepDeadline,
            PolicyRevision = access.PolicyRevision }, token);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(stepDeadline - clock.GetUtcNow());
        AgentRun next;
        try
        {
            var capability = AgentCapabilityCatalog.Get(run.Capability);
            var reply = await model.DecideAsync(run, capability, budget.Token).WaitAsync(budget.Token);
            AgentDecisionParser.Validate(reply.Decision);
            await CheckAsync(run, "after_model", budget.Token);
            next = run with { Provider = reply.Provider, Routing = reply.Routing,
                Note = reply.Decision.Note, Diagnostic = "" };
            switch (reply.Decision.Kind)
            {
                case AgentDecisionKind.Read:
                    var code = reply.Decision.Tool;
                    if (!capability.ReadTools.Contains(code) || !tools.TryGetValue(code, out var tool) || !tool.ReadOnly)
                        throw new AgentBoundaryException("agent_tool_not_registered");
                    if (run.ToolCalls >= limits.MaximumToolCalls || run.Evidence.Any(e => e.Tool == code))
                        throw new AgentBoundaryException("agent_tool_budget_or_repeat");
                    await CheckAsync(run, "read:" + code, budget.Token);
                    var evidence = await tool.ReadAsync(actor, run.Resource, budget.Token).WaitAsync(budget.Token);
                    await CheckAsync(run, "after_tool:" + code, budget.Token);
                    if (!evidence.Verified || evidence.SourceVersion != run.Resource.SourceVersion
                        || string.IsNullOrWhiteSpace(evidence.Reference) || evidence.Reference.Length > 500
                        || string.IsNullOrWhiteSpace(evidence.Text) || evidence.Text.Length > 4000)
                        throw new AgentBoundaryException("agent_evidence_not_verified");
                    next = next with { Status = AgentRunStatus.Ready, ToolCalls = run.ToolCalls + 1,
                        Evidence = run.Evidence.Add(new(code, evidence.SourceVersion, evidence.Reference, evidence.Text)) };
                    break;
                case AgentDecisionKind.Ask:
                    next = next with { Status = AgentRunStatus.AwaitingInput };
                    break;
                case AgentDecisionKind.Handoff:
                    var handoff = reply.Decision.Handoff;
                    if (!capability.Handoffs.Contains(handoff) || run.Evidence.Length == 0)
                        throw new AgentBoundaryException("agent_handoff_not_allowed");
                    await CheckAsync(run, "propose:" + handoff, budget.Token);
                    var recipient = await recipients.ResolveAsync(actor, run.Resource, run.Capability, handoff, budget.Token)
                        .WaitAsync(budget.Token);
                    if (recipient is null || !recipient.CanReceive || recipient.UserId == Guid.Empty
                        || recipient.UserId == actor.ActualUserId || recipient.SourceVersion != run.Resource.SourceVersion
                        || recipient.Role != AgentCapabilityCatalog.RecipientRole(handoff))
                        throw new AgentBoundaryException("agent_handoff_recipient_unverified");
                    // Hash binds this proposal to owner, exact resource/version,
                    // destination and content. It is NOT an approval token.
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    { run.Id, run.Owner, run.Resource, handoff, recipient.UserId, recipient.Role, reply.Decision.Note })))).ToLowerInvariant();
                    next = next with { Status = AgentRunStatus.AwaitingReview,
                        Handoff = new(handoff, recipient.UserId, recipient.Role, run.Resource.SourceVersion, reply.Decision.Note, hash) };
                    break;
                case AgentDecisionKind.Finish:
                    if (run.Evidence.Length == 0) throw new AgentBoundaryException("agent_completion_evidence_missing");
                    next = next with { Status = AgentRunStatus.ProposalComplete };
                    break;
                default: throw new AgentBoundaryException("agent_decision_invalid");
            }
            // Recheck the kill switch, permission and source after an expensive
            // operation. Generated text and a successful tool do not grant access.
            await CheckAsync(run, "save_proposal", budget.Token);
        }
        catch (OperationCanceledException)
        {
            next = run with { Status = token.IsCancellationRequested ? AgentRunStatus.Cancelled : AgentRunStatus.Blocked,
                Diagnostic = token.IsCancellationRequested ? "agent_cancelled" : "agent_step_timeout", Note = "", Handoff = null };
        }
        catch (AgentBoundaryException exception)
        {
            next = run with { Status = AgentRunStatus.Blocked, Diagnostic = exception.Code,
                Note = "", Handoff = null, Evidence = [] };
        }
        catch (Exception)
        {
            // Do not expose provider bodies, database errors, credentials or
            // retrieved text as a diagnostic. No automatic repeat of this step.
            next = run with { Status = AgentRunStatus.Blocked, Diagnostic = "agent_step_unavailable", Note = "", Handoff = null };
        }
        using var saveBudget = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return await ReplaceAsync(run, next with { StepDeadlineAt = null }, saveBudget.Token);
    }

    public async Task<AgentRun> SupplyInputAsync(Guid id, AgentActor actor, int revision, string answer,
        CancellationToken token = default)
    {
        Guard(actor, token);
        var run = await OwnedAsync(id, actor, token);
        if (run.Revision != revision || run.Status != AgentRunStatus.AwaitingInput)
            throw new AgentBoundaryException("agent_run_changed_or_not_waiting");
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 1000 || run.Goal.Length + answer.Length + 24 > 4000)
            throw new AgentBoundaryException("agent_input_invalid");
        await CheckAsync(run, "supply_input", token);
        if (clock.GetUtcNow() >= run.DeadlineAt) throw new AgentBoundaryException("agent_budget_exhausted");
        // Explicit owner input is still untrusted task data, not permission or
        // a change to the authoritative resource. Budgets never reset on resume.
        return await ReplaceAsync(run, run with { Status = AgentRunStatus.Ready,
            Goal = run.Goal + "\nOwner clarification:\n" + answer.Trim(), Note = "" }, token);
    }

    public async Task<AgentRun> RecoverInterruptedAsync(Guid id, AgentActor actor, int revision,
        CancellationToken token = default)
    {
        Guard(actor, token);
        var run = await OwnedAsync(id, actor, token);
        if (run.Revision != revision || run.Status != AgentRunStatus.Running
            || run.StepDeadlineAt is null || run.StepDeadlineAt > clock.GetUtcNow())
            throw new AgentBoundaryException("agent_recovery_not_eligible");
        await CheckAsync(run, "recover", token);
        return await ReplaceAsync(run, run with { Status = AgentRunStatus.Blocked, StepDeadlineAt = null,
            Diagnostic = "agent_interrupted_outcome_unknown", Note = "", Handoff = null }, token);
    }

    private AgentOptions Guard(AgentActor actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var value = options(); value.Validate();
        if (!value.Enabled || value.Environment != "test") throw new AgentBoundaryException("agent_pilot_disabled");
        if (actor.ActualUserId == Guid.Empty || actor.EffectiveUserId == Guid.Empty
            || actor.IsViewAs || actor.ActualUserId != actor.EffectiveUserId)
            throw new AgentBoundaryException("agent_own_session_required");
        return value;
    }

    private async Task<AgentAccess> CheckAsync(AgentRun run, string operation, CancellationToken token)
    {
        Guard(run.Owner, token);
        var decision = await authority.CheckAsync(run.Owner, run.Resource, run.Capability, operation, token).WaitAsync(token);
        if (!decision.Allowed || string.IsNullOrWhiteSpace(decision.PolicyRevision))
            throw new AgentBoundaryException("agent_action_denied");
        if (decision.CurrentSourceVersion != run.Resource.SourceVersion)
            throw new AgentBoundaryException("agent_source_changed");
        return decision;
    }

    private async Task<AgentRun> OwnedAsync(Guid id, AgentActor actor, CancellationToken token)
    {
        var run = await store.ReadOwnedAsync(id, actor, token);
        if (run is null || run.Owner != actor) throw new AgentBoundaryException("agent_run_not_found");
        return run;
    }

    private async Task<AgentRun> ReplaceAsync(AgentRun before, AgentRun after, CancellationToken token)
    {
        after = after with { Revision = checked(before.Revision + 1) };
        if (!await store.TryReplaceAsync(before, after, token)) throw new AgentBoundaryException("agent_revision_conflict");
        return after;
    }
}

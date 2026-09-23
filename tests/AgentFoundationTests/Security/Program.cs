using ProjectTime.Api.Agents;

// Additional executable regressions for field/tool permission revocation. All
// adapters are synthetic, with no network, database or business writes.
var checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException("ASSERTION_FAILED " + label);
    checks++;
}

async Task ExpectDenied(Func<Task> action, string code)
{
    try { await action(); }
    catch (AgentBoundaryException exception) when (exception.Code == code) { checks++; return; }
    throw new InvalidOperationException("Expected " + code);
}

var actorId = Guid.NewGuid();
var actor = new AgentActor(actorId, actorId);
var resource = new AgentResource("project", Guid.NewGuid(), "test-team", "v1");

foreach (var revokeDuringModel in new[] { false, true })
{
    var authority = new Authority();
    var store = new Store();
    var model = new Model();
    var tool = new ReadTool();
    var kernel = new AgentKernel(() => new(true, "test"), authority, store, model, new NoRecipient(), [tool]);
    var run = await kernel.StartAsync(actor, resource, "engineering_readiness", "Review assignment");
    run = await kernel.AdvanceAsync(run.Id, actor, run.Revision);
    Check(run.Evidence.Length == 1 && run.ModelCalls == 1, "verified evidence checkpoint");
    Check(authority.CheckedRead >= 2, "per-tool permission checked beyond broad capability grant");
    // The user keeps start/model/save permission but loses this individual
    // record/field/tool grant. Cached context must not preserve the old access.
    model.Next = new(AgentDecisionKind.Finish, Note: "Proposed assignment summary");
    if (revokeDuringModel)
    {
        model.BeforeReply = () => authority.AllowRead = false;
        var result = await kernel.AdvanceAsync(run.Id, actor, run.Revision);
        Check(result.Status == AgentRunStatus.Blocked && result.Diagnostic == "agent_action_denied", "revoked tool grant blocks generated proposal");
        Check(result.Evidence.Length == 0 && result.Note.Length == 0 && result.Handoff is null, "revoked text cannot escape in returned checkpoint");
    }
    else
    {
        authority.AllowRead = false;
        await ExpectDenied(async () => await kernel.AdvanceAsync(run.Id, actor, run.Revision), "agent_action_denied");
        Check(model.Calls == 1, "cached evidence reauthorized before second inference");
    }
    Check(tool.Calls == 1, "revocation never refetches or repeats original tool");
}

{
    var authority = new Authority(); var store = new Store(); var model = new Model(); var tool = new ReadTool();
    var kernel = new AgentKernel(() => new(true, "test"), authority, store, model, new NoRecipient(), [tool]);
    var run = await kernel.StartAsync(actor, resource, "engineering_readiness", "Review assignment");
    run = await kernel.AdvanceAsync(run.Id, actor, run.Revision);
    model.Next = new(AgentDecisionKind.Ask, Note: "Clarify the dependency");
    run = await kernel.AdvanceAsync(run.Id, actor, run.Revision);
    Check(run.Status == AgentRunStatus.AwaitingInput, "clarification can retain valid context");
    authority.AllowRead = false;
    await ExpectDenied(async () => await kernel.SupplyInputAsync(run.Id, actor, run.Revision, "Access is pending"), "agent_action_denied");
    Check(model.Calls == 2, "clarification cannot revive revoked cached source");
}

{
    var authority = new Authority(); var store = new Store(); var model = new Model(); var tool = new ReadTool();
    var kernel = new AgentKernel(() => new(true, "test", MaximumModelCalls: 1), authority, store, model, new NoRecipient(), [tool]);
    var run = await kernel.StartAsync(actor, resource, "engineering_readiness", "Review assignment");
    run = await kernel.AdvanceAsync(run.Id, actor, run.Revision);
    authority.AllowRead = false;
    await ExpectDenied(async () => await kernel.AdvanceAsync(run.Id, actor, run.Revision), "agent_action_denied");
    Check(model.Calls == 1, "budget-exhausted route does not return unauthorized cached evidence");
}
Console.WriteLine($"CELAR_AGENT_CACHED_EVIDENCE_SECURITY=PASS assertions={checks}");

sealed class Authority : IAgentAuthority
{
    internal bool AllowRead = true;
    internal int CheckedRead;
    public Task<AgentAccess> CheckAsync(AgentActor actor, AgentResource resource, string capability, string operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (operation.StartsWith("read:", StringComparison.Ordinal)) CheckedRead++;
        return Task.FromResult(new AgentAccess(!operation.StartsWith("read:", StringComparison.Ordinal) || AllowRead, resource.SourceVersion, "synthetic-policy"));
    }
}
sealed class Store : IAgentRunStore
{
    private AgentRun? row;
    public Task CreateAsync(AgentRun run, CancellationToken token) { token.ThrowIfCancellationRequested(); row = run; return Task.CompletedTask; }
    public Task<AgentRun?> ReadOwnedAsync(Guid id, AgentActor actor, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(row?.Id == id && row.Owner == actor ? row : null); }
    public Task<bool> TryReplaceAsync(AgentRun expected, AgentRun replacement, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (row != expected || replacement.Revision != expected.Revision + 1) return Task.FromResult(false);
        row = replacement; return Task.FromResult(true);
    }
}
sealed class Model : IAgentDecisionSource
{
    internal int Calls;
    internal Action? BeforeReply;
    internal AgentDecision Next = new(AgentDecisionKind.Read, Tool: "assignment_read");
    public Task<AgentModelReply> DecideAsync(AgentRun run, AgentCapability capability, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Calls++; BeforeReply?.Invoke();
        return Task.FromResult(new AgentModelReply(Next, "synthetic", []));
    }
}
sealed class ReadTool : IAgentReadTool
{
    public string Code => "assignment_read";
    public bool ReadOnly => true;
    internal int Calls;
    public Task<AgentToolResult> ReadAsync(AgentActor actor, AgentResource resource, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new AgentToolResult(true, resource.SourceVersion, "test:assignment", "Restricted synthetic field")); }
}
sealed class NoRecipient : IAgentHandoffResolver
{
    public Task<AgentRecipient?> ResolveAsync(AgentActor sender, AgentResource resource, string capability, string handoffKind, CancellationToken token) => throw new InvalidOperationException("Not called");
}

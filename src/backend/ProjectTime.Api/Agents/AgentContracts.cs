using System.Collections.Immutable;

namespace ProjectTime.Api.Agents;

// Server-side contracts, never HTTP request DTOs. A future host must resolve
// identities, record scope and source versions through the owning modules.
public sealed record AgentActor(Guid ActualUserId, Guid EffectiveUserId, bool IsViewAs = false);
public sealed record AgentResource(string Kind, Guid Id, string ScopeKey, string SourceVersion);
public enum AgentRunStatus { Ready, Running, AwaitingInput, AwaitingReview, ProposalComplete, Blocked, Cancelled }
public enum AgentDecisionKind { Read, Ask, Handoff, Finish }
public sealed record AgentDecision(AgentDecisionKind Kind, string Tool = "", string Handoff = "", string Note = "");
public sealed record AgentEvidence(string Tool, string SourceVersion, string Reference, string Text);
public sealed record AgentProviderDecision(string Target, string Outcome, string Reason);
public sealed record AgentModelReply(AgentDecision Decision, string Provider, ImmutableArray<AgentProviderDecision> Routing);
public sealed record AgentToolResult(bool Verified, string SourceVersion, string Reference, string Text, string Diagnostic = "");
public sealed record AgentAccess(bool Allowed, string CurrentSourceVersion, string PolicyRevision);
public sealed record AgentRecipient(Guid UserId, string Role, string SourceVersion, bool CanReceive);
public sealed record AgentHandoffProposal(string Kind, Guid RecipientId, string RecipientRole, string SourceVersion,
    string Note, string ProposalHash)
{
    public bool Applied => false;
    public bool NotificationSent => false;
}

public sealed record AgentRun(Guid Id, AgentActor Owner, AgentResource Resource, string Capability, string Goal,
    DateTimeOffset CreatedAt, DateTimeOffset DeadlineAt)
{
    public int Revision { get; init; } = 1;
    public AgentRunStatus Status { get; init; } = AgentRunStatus.Ready;
    public int ModelCalls { get; init; }
    public int ToolCalls { get; init; }
    public DateTimeOffset? StepDeadlineAt { get; init; }
    public string Diagnostic { get; init; } = "";
    public string Note { get; init; } = "";
    public string Provider { get; init; } = "";
    public string PolicyRevision { get; init; } = "";
    public ImmutableArray<AgentEvidence> Evidence { get; init; } = [];
    public ImmutableArray<AgentProviderDecision> Routing { get; init; } = [];
    public AgentHandoffProposal? Handoff { get; init; }
}

public sealed record AgentOptions(bool Enabled = false, string Environment = "", int MaximumModelCalls = 6,
    int MaximumToolCalls = 4, int StepSeconds = 90, int RunSeconds = 600)
{
    public void Validate()
    {
        if (MaximumModelCalls is < 1 or > 12 || MaximumToolCalls is < 1 or > 8
            || StepSeconds is < 1 or > 120 || RunSeconds < StepSeconds || RunSeconds > 1200)
            throw new AgentBoundaryException("agent_limits_invalid");
    }
}

public sealed class AgentBoundaryException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

public interface IAgentAuthority
{
    // Re-evaluate actual/effective user, current role policy, record/team/field
    // scope, operation, and source version. No authority comes from the model.
    Task<AgentAccess> CheckAsync(AgentActor actor, AgentResource resource, string capability,
        string operation, CancellationToken token);
}
public sealed class DenyAllAgentAuthority : IAgentAuthority
{
    public Task<AgentAccess> CheckAsync(AgentActor actor, AgentResource resource, string capability,
        string operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new AgentAccess(false, "", "unbound"));
    }
}
public interface IAgentRunStore
{
    Task CreateAsync(AgentRun run, CancellationToken token);
    // Filter by BOTH actual and effective owner before returning any source or text.
    Task<AgentRun?> ReadOwnedAsync(Guid id, AgentActor actor, CancellationToken token);
    // Atomic compare-and-swap: expected revision and immutable owner/resource/
    // capability must match; replacement revision must be expected + 1. Persist
    // state and its audit event together. A stale writer must return false.
    Task<bool> TryReplaceAsync(AgentRun expected, AgentRun replacement, CancellationToken token);
}
public interface IAgentDecisionSource
{
    Task<AgentModelReply> DecideAsync(AgentRun run, AgentCapability capability, CancellationToken token);
}
public interface IAgentReadTool
{
    string Code { get; }
    bool ReadOnly { get; }
    // No arbitrary arguments, URLs, SQL, user IDs, or project IDs from the model.
    Task<AgentToolResult> ReadAsync(AgentActor actor, AgentResource resource, CancellationToken token);
}
public interface IAgentHandoffResolver
{
    // Resolve the currently assigned recipient from authoritative records and
    // check that recipient's own permission. Never forward the sender's session.
    Task<AgentRecipient?> ResolveAsync(AgentActor sender, AgentResource resource,
        string capability, string handoffKind, CancellationToken token);
}

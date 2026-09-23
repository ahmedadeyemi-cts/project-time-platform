using System.Collections.Immutable;
using ProjectTime.Api.Agents;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("ASSERTION_FAILED: " + label);
        checks++;
    }
    private static async Task Denied(Func<Task> action, string code)
    {
        try { await action(); }
        catch (AgentBoundaryException ex) when (ex.Code == code) { checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + code);
    }
    private static async Task Main()
    {
        Check(!new AgentOptions().Enabled, "off by default");
        foreach (var env in new[] { "", "production", "Production", "TEST" })
        {
            var s = new Scenario(); s.Options = s.Options with { Environment = env };
            await Denied(async () => await s.Start(), "agent_pilot_disabled");
            Check(s.Model.Calls == 0 && s.Store.Count == 0, "disabled environments have no effects");
        }
        {
            var s = new Scenario(); s.Options = s.Options with { Enabled = false };
            await Denied(async () => await s.Start(), "agent_pilot_disabled");
            s.Options = s.Options with { Enabled = true, MaximumModelCalls = 0 };
            await Denied(async () => await s.Start(), "agent_limits_invalid");
        }
        {
            var s = new Scenario();
            foreach (var actor in new[] { s.Actor with { IsViewAs = true }, s.Actor with { EffectiveUserId = Guid.NewGuid() }, new AgentActor(Guid.Empty, Guid.Empty) })
                await Denied(async () => await s.Kernel.StartAsync(actor, s.Resource, "engineering_readiness", "Inspect my assignment"), "agent_own_session_required");
            Check(s.Model.Calls == 0, "View-As cannot run agents or transfer administrator power");
        }
        {
            var s = new Scenario(); s.Authority.Allow = false;
            await Denied(async () => await s.Start(), "agent_action_denied");
            var result = await new DenyAllAgentAuthority().CheckAsync(s.Actor, s.Resource, "project_delivery", "start", default);
            Check(!result.Allowed, "unbound production authority always denies");
        }
        // Audience strings are metadata. Even a plausible role/capability name
        // cannot replace a current owning-module resource decision.
        foreach (var cap in AgentCapabilityCatalog.All)
        {
            var s = new Scenario(); s.Authority.Allow = false;
            await Denied(async () => await s.Kernel.StartAsync(s.Actor, s.Resource, cap.Code, "Test"), "agent_action_denied");
        }
        Check(AgentCapabilityCatalog.All.Select(c => c.Code).Distinct().Count() == AgentCapabilityCatalog.All.Length, "unique capability identifiers");
        Check(AgentCapabilityCatalog.Get("coordination").ReadTools.SequenceEqual(new[] { "coordination_work_read" }), "Project Coordinator cannot inherit PTC tools");
        Check(AgentCapabilityCatalog.Get("time_exception").ReadTools.SequenceEqual(new[] { "time_exception_read" }), "PTC has its own read-proposal boundary");
        Check(AgentCapabilityCatalog.Get("scope_review").Audience.Length == 2, "both SA teams use shared capability, not shared access");
        foreach (var cap in AgentCapabilityCatalog.All)
        {
            Check(cap.Audience.Length > 0 && cap.ReadTools.Length > 0, "each role bundle has explicit metadata and reads");
            foreach (var h in cap.Handoffs) Check(AgentCapabilityCatalog.RecipientRole(h).Length > 0, "handoff has a source-controlled destination role");
        }
        foreach (var json in new[]
        {
            "{}", "[]", "{\"kind\":\"deploy\"}",
            "{\"kind\":\"read\",\"kind\":\"finish\",\"tool\":\"assignment_read\"}",
            "{\"kind\":\"read\",\"tool\":\"assignment_read\",\"url\":\"https://example.invalid\"}",
            "{\"kind\":\"handoff\",\"handoff\":\"engineering_to_pm\",\"recipientId\":\"invented\",\"note\":\"x\"}",
            "{\"kind\":\"read\",\"tool\":\"assignment_read\",\"handoff\":\"engineering_to_pm\"}",
            "{\"kind\":\"ask\",\"note\":null}",
            "{\"kind\":\"finish\",\"note\":\" \"}", new string('x', 8193)
        })
            await Denied(() => { AgentDecisionParser.Parse(json); return Task.CompletedTask; }, "agent_decision_invalid");
        Check(AgentDecisionParser.Parse("{\"kind\":\"read\",\"tool\":\"assignment_read\"}").Kind == AgentDecisionKind.Read, "strict JSON read decision");
        Check(AgentDecisionParser.Parse("{\"kind\":\"ask\",\"note\":\"Which task?\"}").Kind == AgentDecisionKind.Ask, "clarification decision");
        {
            var s = new Scenario(); s.Authority.SourceVersion = "changed";
            await Denied(async () => await s.Start(), "agent_source_changed");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            var stranger = new AgentActor(Guid.NewGuid(), Guid.NewGuid()); stranger = stranger with { EffectiveUserId = stranger.ActualUserId };
            await Denied(async () => await s.Kernel.AdvanceAsync(run.Id, stranger, run.Revision), "agent_run_not_found");
            Check(s.Model.Calls == 0, "cross-user run cannot start inference");
            await Denied(async () => await s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision + 1), "agent_run_changed_or_not_ready");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            var next = await s.Advance(run, new(AgentDecisionKind.Read, Tool: "assignment_read"));
            Check(next.Status == AgentRunStatus.Ready && next.Evidence.Length == 1 && next.ToolCalls == 1, "verified read checkpoint");
            Check(next.ModelCalls == 1 && next.Revision == 3, "inference reserved before result checkpoint");
            Check(s.Authority.Operations.Contains("read:assignment_read") && s.Authority.Operations.Contains("after_tool:assignment_read"), "tool reauthorized before and after execution");
            var finish = await s.Advance(next, new(AgentDecisionKind.Finish, Note: "Proposed next step based on assignment evidence"));
            Check(finish.Status == AgentRunStatus.ProposalComplete && finish.Handoff is null, "finish is only a proposal, not completed business work");
        }
        foreach (var tool in new[] { "approve_time", "run_sql", "authorized_financial_read", "https://example.invalid" })
        {
            var s = new Scenario(); var run = await s.Start();
            var next = await s.Advance(run, new(AgentDecisionKind.Read, Tool: tool));
            Check(next.Status == AgentRunStatus.Blocked && next.Diagnostic == "agent_tool_not_registered", "tool injection or out-of-capability action rejected");
            Check(s.Tools.Sum(t => t.Calls) == 0, "rejected tools never execute");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            run = await s.Advance(run, new(AgentDecisionKind.Read, Tool: "assignment_read"));
            run = await s.Advance(run, new(AgentDecisionKind.Read, Tool: "assignment_read"));
            Check(run.Diagnostic == "agent_tool_budget_or_repeat" && s.Tools.Single(t => t.Code == "assignment_read").Calls == 1, "repeat loop stopped");
        }
        {
            var s = new Scenario(); s.Options = s.Options with { MaximumModelCalls = 1 };
            var run = await s.Start(); run = await s.Advance(run, new(AgentDecisionKind.Read, Tool: "assignment_read"));
            run = await s.Advance(run, new(AgentDecisionKind.Finish, Note: "Proposed"));
            Check(run.Diagnostic == "agent_budget_exhausted" && s.Model.Calls == 1, "goal-wide model-call ceiling");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            run = await s.Advance(run, new(AgentDecisionKind.Ask, Note: "Which readiness question should be prioritized?"));
            Check(run.Status == AgentRunStatus.AwaitingInput, "explicit clarification pause");
            var resumed = await s.Kernel.SupplyInputAsync(run.Id, s.Actor, run.Revision, "Check access dependencies");
            Check(resumed.ModelCalls == 1 && resumed.DeadlineAt == run.DeadlineAt && resumed.Status == AgentRunStatus.Ready, "resume preserves budget and original source");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            s.Model.Behavior = (_, _) => { s.Authority.SourceVersion = "2"; return Task.FromResult(Reply(new(AgentDecisionKind.Read, Tool: "assignment_read"))); };
            var next = await s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision);
            Check(next.Diagnostic == "agent_source_changed" && s.Tools.Sum(t => t.Calls) == 0 && next.Evidence.Length == 0, "source change during reasoning stops tools and output");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            s.Model.Behavior = (_, _) => { s.Authority.Allow = false; return Task.FromResult(Reply(new(AgentDecisionKind.Read, Tool: "assignment_read"))); };
            var next = await s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision);
            Check(next.Diagnostic == "agent_action_denied" && next.Evidence.Length == 0, "permission revocation mid-step stops execution");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            s.Model.Behavior = (_, _) => { s.Options = s.Options with { Enabled = false }; return Task.FromResult(Reply(new(AgentDecisionKind.Read, Tool: "assignment_read"))); };
            var next = await s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision);
            Check(next.Diagnostic == "agent_pilot_disabled" && s.Tools.Sum(t => t.Calls) == 0, "kill switch checked after inference");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            run = await s.Advance(run, new(AgentDecisionKind.Finish, Note: "All work is done"));
            Check(run.Diagnostic == "agent_completion_evidence_missing", "model cannot claim completion without evidence");
        }
        foreach (var mode in new[] { "missing", "wrong_role", "stale", "denied", "self" })
        {
            var s = new Scenario(); var run = await s.Start();
            run = await s.Advance(run, new(AgentDecisionKind.Read, Tool: "assignment_read"));
            s.Recipients.Mode = mode;
            run = await s.Advance(run, new(AgentDecisionKind.Handoff, Handoff: "engineering_to_pm", Note: "Review this documented blocker"));
            Check(run.Diagnostic == "agent_handoff_recipient_unverified" && run.Handoff is null, "recipient revalidation: " + mode);
        }
        {
            var s = new Scenario(); var run = await s.Start();
            run = await s.Advance(run, new(AgentDecisionKind.Read, Tool: "assignment_read"));
            run = await s.Advance(run, new(AgentDecisionKind.Handoff, Handoff: "engineering_to_pm", Note: "Review access dependency"));
            Check(run.Status == AgentRunStatus.AwaitingReview && run.Handoff is not null, "Engineering to PM draft handoff");
            Check(!run.Handoff!.Applied && !run.Handoff.NotificationSent && run.Handoff.ProposalHash.Length == 64, "proposal not approval, dispatch or notification");
            Check(run.Handoff.RecipientId != s.Actor.ActualUserId && run.Owner == s.Actor, "handoff never transfers initiator identity");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            s.Model.Behavior = async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return Reply(new(AgentDecisionKind.Ask, Note: "Clarify")); };
            var first = s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision);
            await entered.Task;
            await Denied(async () => await s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision), "agent_run_changed_or_not_ready");
            release.SetResult(); await first;
            Check(s.Model.Calls == 1, "concurrent advance does not double-charge inference");
        }
        {
            var s = new Scenario(); var run = await s.Start();
            var interrupted = run with { Revision = 2, Status = AgentRunStatus.Running, ModelCalls = 1, StepDeadlineAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
            Check(await s.Store.TryReplaceAsync(run, interrupted, default), "interruption fixture");
            var recovered = await s.Kernel.RecoverInterruptedAsync(run.Id, s.Actor, 2);
            Check(recovered.Diagnostic == "agent_interrupted_outcome_unknown" && recovered.ModelCalls == 1 && s.Model.Calls == 0, "crash recovery does not replay inference or reset budgets");
        }
        {
            var s = new Scenario(); var run = await s.Start(); using var stop = new CancellationTokenSource();
            s.Model.Behavior = async (_, token) => { stop.Cancel(); await Task.Delay(1000, token); return Reply(new(AgentDecisionKind.Ask, Note: "never")); };
            var next = await s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision, stop.Token);
            Check(next.Status == AgentRunStatus.Cancelled, "cancellation checkpoint survives caller cancellation");
        }
        {
            var s = new Scenario(); var run = await s.Start(); s.Options = s.Options with { StepSeconds = 1 };
            s.Model.Behavior = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new Exception(); };
            var next = await s.Kernel.AdvanceAsync(run.Id, s.Actor, run.Revision);
            Check(next.Diagnostic == "agent_step_timeout" && s.Model.Calls == 1, "finite step deadline, no retry storm");
        }
        Console.WriteLine($"CELAR_AGENT_FOUNDATION_TESTS=PASS assertions={checks}");
        Console.WriteLine("LIVE_PROVIDER_CALLS=0 LIVE_AUTHORITY_ADAPTER=NOT_REGISTERED LIVE_STORAGE_ADAPTER=NOT_REGISTERED");
    }

    private static AgentModelReply Reply(AgentDecision d) => new(d, "synthetic", []);
    private sealed class Scenario
    {
        internal AgentActor Actor = new(Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("10000000-0000-0000-0000-000000000001"));
        internal AgentResource Resource = new("project", Guid.Parse("20000000-0000-0000-0000-000000000001"), "synthetic-team", "1");
        internal AgentOptions Options = new(true, "test");
        internal MemoryStore Store = new(); internal Authority Authority = new(); internal Model Model = new();
        internal Recipients Recipients = new(); internal Tool[] Tools;
        internal AgentKernel Kernel;
        internal Scenario()
        {
            Tools = AgentCapabilityCatalog.All.SelectMany(c => c.ReadTools).Distinct().Select(code => new Tool(code)).ToArray();
            Kernel = new(() => Options, Authority, Store, Model, Recipients, Tools);
        }
        internal Task<AgentRun> Start() => Kernel.StartAsync(Actor, Resource, "engineering_readiness", "Assess this assigned work");
        internal Task<AgentRun> Advance(AgentRun run, AgentDecision d)
        { Model.Behavior = (_, _) => Task.FromResult(Reply(d)); return Kernel.AdvanceAsync(run.Id, Actor, run.Revision); }
    }
    // TEST DOUBLE ONLY. There is intentionally no in-memory production fallback.
    private sealed class MemoryStore : IAgentRunStore
    {
        private readonly Dictionary<Guid, AgentRun> rows = []; private readonly object gate = new();
        internal int Count { get { lock (gate) return rows.Count; } }
        public Task CreateAsync(AgentRun run, CancellationToken token)
        { token.ThrowIfCancellationRequested(); lock (gate) rows.Add(run.Id, run); return Task.CompletedTask; }
        public Task<AgentRun?> ReadOwnedAsync(Guid id, AgentActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); lock (gate) return Task.FromResult(rows.TryGetValue(id, out var r) && r.Owner == actor ? r : null); }
        public Task<bool> TryReplaceAsync(AgentRun expected, AgentRun next, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); lock (gate)
            {
                if (!rows.TryGetValue(expected.Id, out var current) || current != expected
                    || next.Id != expected.Id || next.Revision != expected.Revision + 1
                    || next.Owner != expected.Owner || next.Resource != expected.Resource
                    || next.Capability != expected.Capability || next.DeadlineAt != expected.DeadlineAt) return Task.FromResult(false);
                rows[next.Id] = next; return Task.FromResult(true);
            }
        }
    }
    private sealed class Authority : IAgentAuthority
    {
        internal bool Allow = true; internal string SourceVersion = "1"; internal List<string> Operations = [];
        public Task<AgentAccess> CheckAsync(AgentActor actor, AgentResource resource, string capability, string operation, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Operations.Add(operation); return Task.FromResult(new AgentAccess(Allow, SourceVersion, "synthetic-policy-1")); }
    }
    private sealed class Model : IAgentDecisionSource
    {
        internal int Calls;
        internal Func<AgentRun, CancellationToken, Task<AgentModelReply>> Behavior = (_, _) => Task.FromResult(Reply(new(AgentDecisionKind.Ask, Note: "Clarify")));
        public Task<AgentModelReply> DecideAsync(AgentRun run, AgentCapability capability, CancellationToken token)
        { Interlocked.Increment(ref Calls); return Behavior(run, token); }
    }
    private sealed class Tool(string code) : IAgentReadTool
    {
        public string Code => code; public bool ReadOnly => true; internal int Calls;
        public Task<AgentToolResult> ReadAsync(AgentActor actor, AgentResource resource, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new AgentToolResult(true, resource.SourceVersion, "synthetic:assignment", "Synthetic assigned-task evidence.")); }
    }
    private sealed class Recipients : IAgentHandoffResolver
    {
        internal string Mode = "valid";
        public Task<AgentRecipient?> ResolveAsync(AgentActor sender, AgentResource resource, string capability, string handoffKind, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<AgentRecipient?>(Mode == "missing" ? null : new AgentRecipient(
                Mode == "self" ? sender.ActualUserId : Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Mode == "wrong_role" ? "administrator" : AgentCapabilityCatalog.RecipientRole(handoffKind),
                Mode == "stale" ? "2" : resource.SourceVersion, Mode != "denied"));
        }
    }
}

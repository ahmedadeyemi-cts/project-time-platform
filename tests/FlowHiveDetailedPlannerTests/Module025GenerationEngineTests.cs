using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

internal static class Module025GenerationEngineTests
{
    internal static async Task RunAsync(PulseAiPrivateFlowHivePlan fixture)
    {
        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("ASSERTION_FAILED " + label);
            Console.WriteLine("ASSERTION_PASSED " + label);
        }
        var evidence = new CelarAiAuthoritativeScopeEvidence(Guid.NewGuid(), 1, "SOW-TEST-025",
            "Test customer", "Upgrade Cisco Unified Communications Manager from version 14.0 to version 15.0.", DateTimeOffset.UtcNow);
        CelarAiComposeResult Result(string phase)
        {
            var plan = fixture with { Tasks = fixture.Tasks.Where(task => task.Phase == phase).ToArray() };
            return new("completed", "sow_draft", "private", null, evidence.EngagementNumber,
                evidence.CustomerName, null, plan,
                CelarAiEnterprisePlatformService.BuildSowDraftFromPlan(plan, evidence.EngagementNumber, evidence.CustomerName),
                [], null, [], [], [], [], 1m, 1m, "Synthetic test evidence", null,
                DateTimeOffset.UtcNow, "module025-test", "deepseek", ["deepseek"], [], []);
        }
        var saved = new Dictionary<string, CelarAiComposeResult>();
        var events = new List<Module025GenerationProgress>();
        Task Persist(Module025GenerationProgress progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            events.Add(progress);
            if (progress.Stage == "phase_completed") saved[progress.Phase] = progress.Result!;
            return Task.CompletedTask;
        }
        var calls = new List<string>();
        Task<CelarAiComposeResult> Generate(CelarAiAuthoritativeScopeEvidence source, CancellationToken token)
        {
            var phase = source.PhaseExecution!.Phase;
            calls.Add(phase);
            // Each preceding phase must have committed before the next provider request.
            Check(Module025GenerationEngine.Phases.TakeWhile(value => value != phase).All(saved.ContainsKey),
                "module025_checkpoint_commits_before_" + phase);
            return Task.FromResult(Result(phase));
        }
        var failed = await Module025GenerationEngine.RunAsync(evidence, saved, new Dictionary<string, int>(),
            (source, token) => source.PhaseExecution!.Phase == "Implement"
                ? Task.FromResult(Result("Implement") with { SowDraft = null, FlowHivePlan = null })
                : Generate(source, token), Persist, CancellationToken.None);
        Check(failed.SowDraft is null && saved.Keys.SequenceEqual(new[] { "Plan", "Design" }),
            "module025_failed_phase_keeps_only_validated_checkpoints_and_no_partial_sow");
        calls.Clear();
        // Simulate a new process by round-tripping checkpoints through JSON.
        var restored = JsonSerializer.Deserialize<Dictionary<string, CelarAiComposeResult>>(JsonSerializer.Serialize(saved))!;
        var completed = await Module025GenerationEngine.RunAsync(evidence, restored, new Dictionary<string, int>(),
            Generate, Persist, CancellationToken.None);
        Check(calls.SequenceEqual(new[] { "Implement", "Validate", "Release" }), "module025_restart_only_generates_missing_phases");
        Check(completed.SowDraft!.WorkPackages.Count == fixture.Tasks.Count && completed.FlowHivePlan!.Tasks.Count == fixture.Tasks.Count,
            "module025_final_assembly_preserves_every_detailed_work_package");
        Check(completed.FlowHivePlan!.Tasks.All(task => task.Description.Length >= 80 && task.DetailedSteps!.Count >= 2
            && task.AcceptanceCriteria!.Count > 0 && task.ValidationSteps!.Count > 0
            && task.CustomerResponsibilities!.Count > 0 && task.UsSignalResponsibilities!.Count > 0),
            "module025_resume_preserves_full_detail_contract");
        var execution = new Module025PhaseExecution("Plan", 0, Persist);
        Check(await execution.BeforeAttemptAsync("deepseek", CancellationToken.None), "module025_first_attempt_reserved");
        Check(await execution.BeforeAttemptAsync("celar_ai", CancellationToken.None), "module025_second_attempt_reserved");
        Check(!await execution.BeforeAttemptAsync("deepseek", CancellationToken.None), "module025_attempt_budget_blocks_third_request");
        var restarted = new Module025PhaseExecution("Plan", events.Where(item => item.Stage == "provider_started").Max(item => item.Attempt), Persist);
        Check(!await restarted.BeforeAttemptAsync("deepseek", CancellationToken.None), "module025_restart_cannot_reset_attempt_budget");

        using var cancelled = new CancellationTokenSource();
        var cancellationCalls = 0;
        try
        {
            await Module025GenerationEngine.RunAsync(evidence, new Dictionary<string, CelarAiComposeResult>(), new Dictionary<string, int>(),
                (source, token) => { cancellationCalls++; cancelled.Cancel(); return Task.FromResult(Result(source.PhaseExecution!.Phase)); },
                Persist, cancelled.Token);
            throw new InvalidOperationException("Cancellation was ignored.");
        }
        catch (OperationCanceledException) { Check(cancellationCalls == 1, "module025_cancellation_stops_following_phases"); }
        try
        {
            var malformed = Result("Plan") with { FlowHivePlan = fixture with { Tasks = [fixture.Tasks[0] with { DetailedSteps = [] }] } };
            await Module025GenerationEngine.RunAsync(evidence, new Dictionary<string, CelarAiComposeResult> { ["Plan"] = malformed },
                new Dictionary<string, int>(), (_, _) => throw new InvalidOperationException("Provider must not be called."), Persist, CancellationToken.None);
            throw new InvalidOperationException("Malformed checkpoint was accepted.");
        }
        catch (JsonException) { Check(true, "module025_rejects_invalid_checkpoint_before_spending"); }
        var connectionString = Environment.GetEnvironmentVariable("MODULE025_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("MODULE025_JOURNAL_DATABASE_TESTS=NOT_RUN (requires disposable PostgreSQL; CI supplies it)");
            return;
        }
        // Capture the actual private-client -> DeepSeek HTTP payload while using
        // the real PostgreSQL queue lock. No inference endpoint is contacted.
        var priorConnection = Environment.GetEnvironmentVariable("PROJECTPULSE_DB_CONNECTION");
        try
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_DB_CONNECTION", connectionString);
            var configuration = new ProjectPulseAiConfiguration();
            configuration.ApplyStoredSecret(ProjectPulseAiProviders.DeepSeek, "synthetic-test-only", "test", DateTimeOffset.UtcNow);
            configuration.ApplyStoredEnabled(ProjectPulseAiProviders.DeepSeek, true);
            var transport = new CaptureCompletionTransport(JsonSerializer.Serialize(Result("Plan").FlowHivePlan));
            var client = new PulseAiPrivateModelClient(transport,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PulseAiPrivateModelClient>.Instance,
                new ProjectPulseDeepSeekProvider(transport, configuration));
            var request = new PulseAiPrivateModelRequest(CelarAiCapabilityCatalog.SowGsdPlanning,
                "sow_draft", "comprehensive", "Return the detailed Plan phase.", evidence.ServiceOverview,
                [], "module025_detailed_phase", Module025GenerationEngine.MaximumOutputTokens, 0.1m, "module025-budget-test");
            var response = await ProjectPulseDeepSeekProvider.RunPrivateTargetAsync(CelarAiCapabilityTargets.DeepSeek,
                token => client.GenerateAsync(request, PulseAiPrivateRagOptions.FromEnvironment() with { Enabled = true }, token),
                CancellationToken.None);
            Check(response.Succeeded && transport.RequestCount == 1 && transport.MaximumTokens == 6_144,
                "module025_private_client_deepseek_http_request_obeys_total_6144_token_ceiling");
        }
        finally { Environment.SetEnvironmentVariable("PROJECTPULSE_DB_CONNECTION", priorConnection); }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var migration in new[] { "001_initial_schema.sql", "099_module025_sow_gsd_workspace.sql", "106_module025_sow_sell_register.sql" })
        {
            await using var command = new NpgsqlCommand(await File.ReadAllTextAsync("database/migrations/" + migration), connection);
            await command.ExecuteNonQueryAsync();
        }
        var actor = Guid.NewGuid();
        await using (var setup = new NpgsqlCommand("""
            INSERT INTO app_users(user_id,email,display_name) VALUES (@actor,'module025-engine@example.invalid','Engine Test');
            INSERT INTO module025_sow_gsd_engagements(engagement_id,owner_user_id,owner_display_name,customer_name,customer_entry_mode)
            VALUES (@engagement,@actor,'Engine Test','Synthetic customer','manual');
            """, connection))
        {
            setup.Parameters.AddWithValue("actor", actor);
            setup.Parameters.AddWithValue("engagement", evidence.EngagementId);
            await setup.ExecuteNonQueryAsync();
        }
        var generationId = Guid.NewGuid();
        var hash = PulseAiPrivateRagService.CreateModule025AuthoritativeScopeSource(evidence)!.SourceSha256;
        Module025GenerationJournal Journal(Guid generation, int revision = 1, string? sourceHash = null) =>
            new(connectionString, evidence.EngagementId, actor, revision, generation, sourceHash ?? hash);
        await Journal(generationId).PersistAsync(new("provider_started", "Plan", "deepseek", 1), CancellationToken.None);
        await Journal(generationId).PersistAsync(new("phase_completed", "Plan", "deepseek", Result: Result("Plan")), CancellationToken.None);
        var reloaded = await Journal(generationId).LoadAsync(CancellationToken.None);
        Check(reloaded.Checkpoints["Plan"].FlowHivePlan!.Tasks.Count == 2 && reloaded.Attempts["Plan"] == 1,
            "module025_postgres_checkpoint_and_attempt_survive_new_journal");
        var retry = await Journal(Guid.NewGuid()).LoadAsync(CancellationToken.None);
        Check(retry.Checkpoints.ContainsKey("Plan") && retry.Attempts.Count == 0,
            "module025_explicit_retry_reuses_validated_phase_with_new_attempt_budget");
        Check((await Journal(generationId, sourceHash: new string('f', 64)).LoadAsync(CancellationToken.None)).Checkpoints.Count == 0
            && (await Journal(generationId, revision: 2).LoadAsync(CancellationToken.None)).Checkpoints.Count == 0,
            "module025_changed_source_or_revision_cannot_reuse_checkpoint");
        await using (var noPublication = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM module025_sow_gsd_generation_snapshots)
                 + (SELECT count(*) FROM module025_sow_gsd_versions)
                 + (SELECT count(*) FROM module025_sow_gsd_phases);
            """, connection))
            Check(Convert.ToInt64(await noPublication.ExecuteScalarAsync()) == 0, "module025_checkpoint_does_not_publish_draft_snapshot_or_version");
        foreach (var change in new[] { "revision=2", "revision=1,status='archived'", "status='draft',is_active=FALSE" })
        {
            await using var update = new NpgsqlCommand("UPDATE module025_sow_gsd_engagements SET " + change, connection);
            await update.ExecuteNonQueryAsync();
            try
            {
                await Journal(generationId).PersistAsync(new("provider_started", "Design", "deepseek", 1), CancellationToken.None);
                throw new InvalidOperationException("Changed source guard did not reject progress.");
            }
            catch (Module025GenerationSourceChangedException) { Check(true, "module025_postgres_rejects_progress_after_" + change); }
        }
        Console.WriteLine("MODULE025_JOURNAL_DATABASE_TESTS=PASS");
    }
    private sealed class CaptureCompletionTransport(string plan) : HttpMessageHandler, IHttpClientFactory
    {
        internal int MaximumTokens { get; private set; }
        internal int RequestCount { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            MaximumTokens = payload.RootElement.GetProperty("max_tokens").GetInt32();
            RequestCount++;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { finish_reason = "stop", message = new { content = plan } } }
                }))
            };
        }
    }

}

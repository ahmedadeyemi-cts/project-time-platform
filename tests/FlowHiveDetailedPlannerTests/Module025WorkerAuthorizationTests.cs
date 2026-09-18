using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

internal static class Module025WorkerAuthorizationTests
{
    internal static async Task RunAsync()
    {
        static void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("ASSERTION_FAILED " + name);
            Console.WriteLine("ASSERTION_PASSED " + name);
        }
        var enabled = Module025ProtectedTestUatAccess.EnabledVariable;
        var run = Module025ProtectedTestUatAccess.RunIdVariable;
        var source = Module025ProtectedTestUatAccess.SourceCommitVariable;
        var expiry = Module025ProtectedTestUatAccess.ExpiresAtVariable;
        var release = "PROJECTPULSE_SOURCE_COMMIT";
        var previous = new[] { enabled, run, source, expiry, release }
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);
        var user = Guid.NewGuid();
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MANAGER" };
        var current = new PulseAiPrivateRagAccess(user, true, roles, new HashSet<string>());
        var queued = new Module025AccessContext(user, user, "Test manager", Module025ProtectedTestUatAccess.TargetEmail,
            "", "", roles, false, false, true, true, true, new HashSet<Guid> { user });
        var sha = new string('a', 40);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        void Configure()
        {
            Environment.SetEnvironmentVariable(enabled, "true");
            Environment.SetEnvironmentVariable(run, "12345-1");
            Environment.SetEnvironmentVariable(source, sha);
            Environment.SetEnvironmentVariable(release, sha);
            Environment.SetEnvironmentVariable(expiry, expiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        try
        {
            Configure();
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("phd-west-test.onenecklab.com");
            context.Request.Headers.Origin = "https://phd-west-test.onenecklab.com";
            context.Request.Headers[Module025ProtectedTestUatAccess.RunIdHeader] = "12345-1";
            Check(Module025ProtectedTestUatAccess.Authorizes(context, user, user,
                Module025ProtectedTestUatAccess.TargetEmail, roles), "module025_original_request_fixture_authorizes");
            var grant = Module025ProtectedTestUatAccess.CurrentWorkerGrant()!;
            // Round-trip the same nested object serializer used by the durable queue.
            var evidence = JsonSerializer.SerializeToElement(new {
                queued.IsProtectedTestUatRoleFixture, protectedTestUatGrant = grant });
            var restored = evidence.GetProperty("protectedTestUatGrant")
                .Deserialize<Module025ProtectedTestUatAccess.WorkerGrant>();
            Check(Module025SowGsdModule.HasGenerationAuthority(queued, current, restored),
                "module025_matching_worker_retains_authorized_fixture_after_queue_roundtrip");
            foreach (var change in new (string Key, string? Value)[] {
                (enabled, "false"), (run, "12345-2"), (run, "malformed"),
                (source, new string('b', 40)), (release, new string('b', 40)),
                (expiry, "0"), (expiry, "invalid"), (expiry, (expiresAt + 1).ToString()),
                (expiry, DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString()), (enabled, null) })
            {
                Configure();
                Environment.SetEnvironmentVariable(change.Key, change.Value);
                Check(!Module025SowGsdModule.HasGenerationAuthority(queued, current, restored),
                    "module025_fixture_rejects_changed_" + change.Key + "_" + (change.Value ?? "missing"));
            }
            Configure();
            Check(!Module025SowGsdModule.HasGenerationAuthority(queued, current, null), "module025_fixture_rejects_unbound_legacy_job");
            Check(!Module025SowGsdModule.HasGenerationAuthority(queued, current with { IsActive = false }, restored), "module025_fixture_rejects_deactivated_user");
            Check(!Module025SowGsdModule.HasGenerationAuthority(queued, current with { RoleCodes = new HashSet<string>() }, restored), "module025_fixture_rejects_revoked_manager_role");
            Check(!Module025SowGsdModule.HasGenerationAuthority(queued, current with { UserId = Guid.NewGuid() }, restored), "module025_fixture_rejects_wrong_loaded_identity");
            Check(!Module025SowGsdModule.HasGenerationAuthority(queued with { ActualUserId = Guid.NewGuid() }, current, restored), "module025_fixture_rejects_impersonation");
            Check(!Module025SowGsdModule.HasGenerationAuthority(queued with { IsViewAs = true }, current, restored), "module025_fixture_rejects_view_as");
            Check(!Module025SowGsdModule.HasGenerationAuthority(queued with { IsProtectedTestUatRoleFixture = false }, current, restored), "module025_ordinary_manager_cannot_inherit_fixture");
            context.Request.Headers[Module025ProtectedTestUatAccess.RunIdHeader] = "different-run";
            Check(!Module025ProtectedTestUatAccess.Authorizes(context, user, user, Module025ProtectedTestUatAccess.TargetEmail, roles), "module025_request_still_requires_exact_run_header");
            Environment.SetEnvironmentVariable(enabled, "false");
            foreach (var role in new[] { "SOLUTION_ARCHITECT", "SA", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR" })
                Check(Module025SowGsdModule.HasGenerationAuthority(queued with { IsProtectedTestUatRoleFixture = false },
                    current with { RoleCodes = new HashSet<string> { role } }, null), "module025_normal_authority_preserved_" + role);

            var connectionString = Environment.GetEnvironmentVariable("MODULE025_TEST_DATABASE");
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            await using var db = new NpgsqlConnection(connectionString);
            await db.OpenAsync();
            await using (var create = new NpgsqlCommand("""
                CREATE TEMP TABLE module025_sow_gsd_events (
                    event_id bigint GENERATED ALWAYS AS IDENTITY, engagement_id uuid,
                    actor_user_id uuid, engagement_revision integer, event_type text, evidence_json jsonb);
                """, db)) await create.ExecuteNonQueryAsync();
            async Task Seed(bool fixture, Module025ProtectedTestUatAccess.WorkerGrant? value)
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO module025_sow_gsd_events(engagement_id,actor_user_id,engagement_revision,event_type,evidence_json)
                    VALUES(@id,@id,1,'ai_generation_queued',@evidence::jsonb)
                    """, db);
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                insert.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(new {
                    generationId = Guid.NewGuid(), IsProtectedTestUatRoleFixture = fixture,
                    protectedTestUatGrant = value }));
                await insert.ExecuteNonQueryAsync();
            }
            // More foreign jobs than the query LIMIT must not starve regular jobs.
            for (var i = 0; i < 13; i++) await Seed(true, grant with { RunId = "previous-1" });
            await Seed(true, null);
            await Seed(false, null);
            await Seed(true, grant);
            async Task<int> CandidateCount()
            {
                await using var query = new NpgsqlCommand(Module025SowGsdModule.GenerationCandidateSql, db);
                query.Parameters.AddWithValue("uat_grant", JsonSerializer.Serialize(Module025ProtectedTestUatAccess.CurrentWorkerGrant()));
                await using var reader = await query.ExecuteReaderAsync();
                var count = 0;
                while (await reader.ReadAsync()) count++;
                return count;
            }
            Check(await CandidateCount() == 1, "module025_old_disabled_revision_cannot_claim_fixture_or_starve_normal_jobs");
            Configure();
            Check(await CandidateCount() == 2, "module025_exact_enabled_revision_can_claim_fixture_and_normal_jobs");
            Environment.SetEnvironmentVariable(run, "12345-2");
            Check(await CandidateCount() == 1, "module025_next_test_run_cannot_claim_previous_run_fixture");
            Configure();
            Environment.SetEnvironmentVariable(release, new string('b', 40));
            Check(await CandidateCount() == 1, "module025_other_release_cannot_claim_fixture");
        }
        finally
        {
            foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }
}

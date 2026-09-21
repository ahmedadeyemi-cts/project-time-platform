using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;
using System.Security.Cryptography;

internal static class DatabaseChecks
{
    internal static async Task RunAsync(Action<bool, string> check, Module025EngagementRow row)
    {
        var supplied = Environment.GetEnvironmentVariable("MODULE025_SCOPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("The scope suite requires a disposable loopback PostgreSQL connection.");
        var admin = new NpgsqlConnectionStringBuilder(supplied) { Database = "postgres", Pooling = false, IncludeErrorDetail = false };
        if (admin.Host is not ("127.0.0.1" or "localhost" or "::1")) throw new InvalidOperationException("Only loopback test databases are permitted.");
        var name = "scope_test_" + Guid.NewGuid().ToString("N");
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", control).ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name }.ConnectionString;
        var env = ProjectPulseAiDatabaseConnection.DirectAliases.Concat(new[] { "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD",
            "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID", "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING" }).Distinct().ToDictionary(x => x, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var key in env.Keys) Environment.SetEnvironmentVariable(key, null);
            Environment.SetEnvironmentVariable("PROJECTPULSE_CONNECTION_STRING", connectionString);
            Environment.SetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            await using var sql = new NpgsqlConnection(connectionString);
            await sql.OpenAsync();
            async Task Run(string query) => _ = await new NpgsqlCommand(query, sql).ExecuteNonQueryAsync();
            async Task Apply(string path) => await Run(await File.ReadAllTextAsync(path));
            async Task<long> Count(string query) => Convert.ToInt64(await new NpgsqlCommand(query, sql).ExecuteScalarAsync());
            await Apply("database/migrations/061_celar_ai_capability_routing.sql");
            // Minimal predecessor record fixture; real additive migrations execute unchanged.
            await Run("""
                CREATE TABLE module025_sow_gsd_engagements (engagement_id uuid PRIMARY KEY, service_overview text NOT NULL);
                INSERT INTO schema_migrations(migration_id, description) VALUES
                  ('099_module025_sow_gsd_workspace','Disposable workspace predecessor'),
                  ('071_ai_runtime_production_hardening','Disposable route predecessor');
                """);
            await Apply("database/migrations/123_module064_external_generation_approval.sql");
            await using (var insert = new NpgsqlCommand("INSERT INTO module025_sow_gsd_engagements VALUES (@id,'Existing saved text')", sql))
            { insert.Parameters.AddWithValue("id", row.EngagementId); await insert.ExecuteNonQueryAsync(); }
            await Run("UPDATE ai_capability_routes SET sanitized_external_generation_approved=TRUE WHERE feature_code='sow_gsd_planning'");
            check(!await Module025ServiceScopePolicy.SchemaReadyAsync(sql, default), "pre_migration124_schema_is_not_advertised");
            await Apply("database/migrations/124_module025_service_scope.sql");
            await Apply("database/migrations/124_module025_service_scope.sql");
            await Apply("scripts/release-test/verify-module025-service-scope.sql");
            check(await Module025ServiceScopePolicy.SchemaReadyAsync(sql, default), "migration124_replay_and_schema_verification_pass");
            check(await Count("SELECT count(*) FROM module025_sow_gsd_engagements WHERE service_scope IS NULL AND service_overview='Existing saved text' AND generated_service_overview='' AND NOT service_overview_manually_edited") == 1,
                "migration_never_copies_overview_to_source_or_changes_existing_content");
            check(await Count("SELECT count(*) FROM ai_capability_routes WHERE service_scope_full_text_approved") == 0, "migration_does_not_grant_full_text_consent");
            using var store = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
            var before = await store.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning);
            check(before.SanitizedExternalGenerationApproved && !before.ServiceScopeFullTextApproved, "old_approval_remains_distinct_after_migration");
            string[] order = ["gemini", "claude", "openai", "deepseek_v4", "celar_ai", "local_template"];
            var actor = Guid.NewGuid();
            var saved = await store.SaveRouteAsync(before.FeatureCode, order, before.Revision, actor, serviceScopeFullTextApproved: true);
            var loaded = await store.LoadRouteAsync(before.FeatureCode);
            check(saved.ServiceScopeFullTextApproved && loaded.ServiceScopeFullTextApproved && loaded.ServiceScopeApprovalSchemaReady && loaded.Targets.SequenceEqual(order),
                "full_text_approval_and_exact_order_survive_reload");
            check(await Count("SELECT count(*) FROM ai_capability_route_audit WHERE NOT previous_scope_full_text_approved AND new_scope_full_text_approved") == 1,
                "consent_change_is_audited_once");
            try { await store.SaveRouteAsync(before.FeatureCode, order, before.Revision, actor, serviceScopeFullTextApproved: false); throw new Exception("Stale revision accepted"); }
            catch (CelarAiConfigurationConflictException) { check(true, "stale_approval_save_rejected"); }
            var revoked = await store.SaveRouteAsync(before.FeatureCode, order, loaded.Revision, actor, serviceScopeFullTextApproved: false);
            check(!revoked.ServiceScopeFullTextApproved && revoked.Targets.SequenceEqual(order), "revocation_does_not_reorder_providers");
            check(await Count("SELECT count(*) FROM ai_capability_route_audit WHERE previous_scope_full_text_approved AND NOT new_scope_full_text_approved") == 1,
                "revocation_audit_retains_previous_value");
            await using (var tx = await sql.BeginTransactionAsync())
            {
                await Module025ServiceScopeWorkspace.SaveInputAsync(sql, tx, row.EngagementId, row.ServiceScope!, true, default);
                await Module025ServiceScopeWorkspace.SaveOverviewAsync(sql, tx, row, "Latest AI overview proposal", default);
                await tx.CommitAsync();
            }
            await using (var verify = new NpgsqlCommand("SELECT service_scope, service_overview, generated_service_overview FROM module025_sow_gsd_engagements WHERE engagement_id=@id", sql))
            {
                verify.Parameters.AddWithValue("id", row.EngagementId); await using var read = await verify.ExecuteReaderAsync(); await read.ReadAsync();
                check(read.GetString(0) == row.ServiceScope && read.GetString(1) == row.ServiceOverview && read.GetString(2) == "Latest AI overview proposal",
                    "original_source_reviewed_overview_and_new_proposal_persist_separately");
            }
            await using (var tx = await sql.BeginTransactionAsync())
            {
                await Module025ServiceScopeWorkspace.SaveInputAsync(sql, tx, row.EngagementId, "Transaction must roll back this input", false, default);
                await tx.RollbackAsync();
            }
            check(await Count("SELECT count(*) FROM module025_sow_gsd_engagements WHERE service_scope='Transaction must roll back this input'") == 0,
                "failed_transaction_does_not_replace_saved_scope");
        }
        finally
        {
            foreach (var pair in env) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            NpgsqlConnection.ClearAllPools();
            await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", control).ExecuteNonQueryAsync();
        }
    }
}

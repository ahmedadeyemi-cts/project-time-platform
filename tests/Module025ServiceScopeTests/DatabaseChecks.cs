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
            // Fixture only the unrelated older document-worker and workspace
            // records. Run the real routing/hardening/approval SQL unchanged;
            // a fabricated 071 ledger row cannot satisfy its physical schema.
            await Run("""
                CREATE TABLE module025_sow_gsd_engagements (engagement_id uuid PRIMARY KEY, service_overview text NOT NULL);
                CREATE TABLE pulse_ai_document_processing_jobs (
                    pulse_ai_document_processing_job_id uuid PRIMARY KEY, lease_owner text NULL
                );
                INSERT INTO schema_migrations(migration_id, description) VALUES
                  ('099_module025_sow_gsd_workspace','Disposable workspace predecessor'),
                  ('052_pulse_ai_private_document_runtime','Disposable document-worker predecessor'),
                  ('053_pulse_ai_private_rag_orchestration','Disposable document-worker predecessor');
                """);
            using (var incompleteStore = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance))
            {
                var rejected = false;
                try { await incompleteStore.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning); }
                catch (InvalidOperationException exception) when (exception.Message == "module064_route_store_unavailable")
                { rejected = true; }
                check(rejected, "missing_real_migration071_still_blocks_route_reads");
            }
            await Apply("database/migrations/071_ai_runtime_production_hardening.sql");
            check(await Count("SELECT count(*) FROM schema_migrations WHERE migration_id='071_ai_runtime_production_hardening'") == 1,
                "real_hardening_migration_records_its_own_receipt");
            using (var hardenedStore = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance))
            {
                var legacyRoute = await hardenedStore.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning);
                check(legacyRoute.Persisted && !legacyRoute.ExternalGenerationApprovalSchemaReady,
                    "real_migration071_restores_reads_without_granting_external_approval");
            }
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

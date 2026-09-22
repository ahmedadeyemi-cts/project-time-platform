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
            // Fixture only unrelated identity/document-worker prerequisites.
            // Run the real 061/071/099/123/124 migrations: migration 099 never
            // registers a ledger row, which the former fixture incorrectly hid.
            await Run("""
                CREATE TABLE app_users (user_id uuid PRIMARY KEY);
                CREATE TABLE clients (client_id uuid PRIMARY KEY);
                CREATE TABLE pulse_ai_document_processing_jobs (
                    pulse_ai_document_processing_job_id uuid PRIMARY KEY, lease_owner text NULL
                );
                INSERT INTO schema_migrations(migration_id, description) VALUES
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
            async Task Reject124(string expectedMessage, string label)
            {
                var rejected = false;
                try { await Apply("database/migrations/124_module025_service_scope.sql"); }
                catch (PostgresException exception) when (exception.SqlState == "P0001"
                    && exception.MessageText.Contains(expectedMessage, StringComparison.Ordinal))
                { rejected = true; }
                finally { await Run("ROLLBACK"); }
                check(rejected, label);
                check(await Count("SELECT count(*) FROM schema_migrations WHERE migration_id='124_module025_service_scope'") == 0,
                    label + "_no_false_receipt");
                check(await Count("SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND column_name IN ('service_scope','service_scope_full_text_approved')") == 0,
                    label + "_no_partial_columns");
            }
            await Reject124("migration 123 approval receipt", "missing_123_receipt_fails_closed");
            await Apply("database/migrations/123_module064_external_generation_approval.sql");
            await Reject124("migration 099 workspace schema", "missing_workspace_fails_closed");
            await Run("INSERT INTO schema_migrations(migration_id,description) VALUES ('099_module025_sow_gsd_workspace','Intentionally invalid test-only receipt')");
            await Reject124("migration 099 workspace schema", "ledger_alone_cannot_replace_physical_workspace");
            await Run("DELETE FROM schema_migrations WHERE migration_id='099_module025_sow_gsd_workspace'");
            await Apply("database/migrations/099_module025_sow_gsd_workspace.sql");
            check(await Count("SELECT count(*) FROM schema_migrations WHERE migration_id='099_module025_sow_gsd_workspace'") == 0,
                "real_099_uses_physical_schema_not_a_fabricated_ledger_receipt");
            foreach (var (change, restore, expected, label) in new[]
            {
                ("ALTER TABLE module025_sow_gsd_phases RENAME TO fixture_missing_phases", "ALTER TABLE fixture_missing_phases RENAME TO module025_sow_gsd_phases",
                    "migration 099 workspace schema", "missing_phases_rejected"),
                ("ALTER TABLE module025_sow_gsd_engagements DISABLE TRIGGER trg_module025_protect_sow_gsd_identity", "ALTER TABLE module025_sow_gsd_engagements ENABLE TRIGGER trg_module025_protect_sow_gsd_identity",
                    "migration 099 workspace schema", "disabled_identity_protection_rejected"),
                ("ALTER TABLE module025_sow_gsd_phases RENAME COLUMN final_hours TO fixture_final_hours", "ALTER TABLE module025_sow_gsd_phases RENAME COLUMN fixture_final_hours TO final_hours",
                    "complete migration 099 and 123 columns", "missing_reviewed_hours_rejected"),
                ("ALTER TABLE ai_capability_route_audit RENAME COLUMN new_external_generation_approved TO fixture_approval", "ALTER TABLE ai_capability_route_audit RENAME COLUMN fixture_approval TO new_external_generation_approved",
                    "complete migration 099 and 123 columns", "missing_approval_audit_column_rejected")
            })
            {
                await Run(change);
                try { await Reject124(expected, label); }
                finally { await Run(restore); }
            }
            await using (var insert = new NpgsqlCommand("INSERT INTO app_users(user_id) VALUES (@owner); INSERT INTO module025_sow_gsd_engagements(engagement_id,owner_user_id,service_overview) VALUES (@id,@owner,'Existing saved text')", sql))
            {
                insert.Parameters.AddWithValue("id", row.EngagementId);
                insert.Parameters.AddWithValue("owner", row.OwnerUserId);
                await insert.ExecuteNonQueryAsync();
            }
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

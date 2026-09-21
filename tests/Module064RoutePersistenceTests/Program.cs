using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectTime.Api.Ai;
using System.Security.Cryptography;

var supplied = Environment.GetEnvironmentVariable("MODULE064_TEST_DATABASE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("A disposable loopback PostgreSQL connection is required.");
var admin = new NpgsqlConnectionStringBuilder(supplied) { Database = "postgres", Pooling = false, IncludeErrorDetail = false };
if (admin.Host is not ("127.0.0.1" or "localhost" or "::1"))
    throw new InvalidOperationException("Route persistence tests may use only disposable loopback PostgreSQL.");
var database = "module064_route_test_" + Guid.NewGuid().ToString("N");
await using var control = new NpgsqlConnection(admin.ConnectionString);
await control.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE {database}", control).ExecuteNonQueryAsync();
var connectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
var previous = ProjectPulseAiDatabaseConnection.DirectAliases.Concat(new[] {
    "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD",
    "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID", "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING"
}).ToDictionary(name => name, Environment.GetEnvironmentVariable);
var checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception(label);
    checks++;
    Console.WriteLine("PASS " + label);
}
async Task Reject<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); } catch (T) { Check(true, label); return; }
    throw new Exception("Expected rejection: " + label);
}
var root = new DirectoryInfo(Directory.GetCurrentDirectory());
while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "database", "migrations"))) root = root.Parent;
if (root is null) throw new InvalidOperationException("Run from the repository.");
try
{
    foreach (var name in previous.Keys) Environment.SetEnvironmentVariable(name, null);
    Environment.SetEnvironmentVariable("PROJECTPULSE_CONNECTION_STRING", connectionString);
    Environment.SetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    await using var sql = new NpgsqlConnection(connectionString);
    await sql.OpenAsync();
    async Task Execute(string command) => _ = await new NpgsqlCommand(command, sql).ExecuteNonQueryAsync();
    async Task Apply(string path) => await Execute(await File.ReadAllTextAsync(Path.Combine(root.FullName, path)));
    async Task<long> Count(string query) => Convert.ToInt64(await new NpgsqlCommand(query, sql).ExecuteScalarAsync());
    const string migration = "database/migrations/123_module064_external_generation_approval.sql";
    const string rollback = "database/rollback/123_module064_external_generation_approval_rollback.sql";
    await Apply("database/migrations/061_celar_ai_capability_routing.sql");
    await Reject<PostgresException>(() => Apply(migration), "migration 123 rejects missing production-hardening prerequisite");
    await Execute("ROLLBACK");
    Check(await Count("SELECT COUNT(*) FROM information_schema.columns WHERE table_name='ai_capability_routes' AND column_name='sanitized_external_generation_approved'") == 0,
        "failed prerequisite leaves schema untouched");

    // Isolated fixture for the older document-worker schema required by 071.
    // The real 061, 071 and new 123 SQL execute unchanged below.
    await Execute("""
        CREATE TABLE pulse_ai_document_processing_jobs (
            pulse_ai_document_processing_job_id uuid PRIMARY KEY, lease_owner text NULL
        );
        INSERT INTO schema_migrations(migration_id, description) VALUES
            ('052_pulse_ai_private_document_runtime', 'Disposable predecessor fixture'),
            ('053_pulse_ai_private_rag_orchestration', 'Disposable predecessor fixture');
        """);
    await Apply("database/migrations/071_ai_runtime_production_hardening.sql");
    using (var oldSchemaStore = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance))
    {
        var oldSchemaRoute = await oldSchemaStore.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning);
        Check(oldSchemaRoute.Persisted && !oldSchemaRoute.SanitizedExternalGenerationApproved
            && !oldSchemaRoute.ExternalGenerationApprovalSchemaReady, "old schema preserves persisted route with approval unavailable");
        Check(oldSchemaRoute.Targets.SequenceEqual(new[] { "celar_ai", "claude", "openai", "local_template" }),
            "reading the old schema does not silently insert a newer provider");
        // Supply a valid current write shape so this checks the missing-schema
        // gate, independently of legacy read compatibility and input validation.
        await Reject<CelarAiRouteSchemaUnavailableException>(() => oldSchemaStore.SaveRouteAsync(oldSchemaRoute.FeatureCode,
            CelarAiCapabilityTargets.DefaultOrder, oldSchemaRoute.Revision, Guid.NewGuid(), sanitizedExternalGenerationApproved: true),
            "route mutation fails closed until migration 123 is verified");
    }
    await Apply(migration);
    await Apply(migration);
    await Apply("scripts/release-test/verify-module064-external-generation-approval.sql");
    using var store = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
    var routes = await store.LoadRoutesAsync();
    Check(routes.Select(route => route.FeatureCode).ToHashSet(StringComparer.OrdinalIgnoreCase)
        .SetEquals(CelarAiCapabilityCatalog.Definitions.Keys), "all registered capability routes remain available after migration");
    Check(routes.All(route => !route.SanitizedExternalGenerationApproved)
        && await Count("SELECT COUNT(*) FROM ai_capability_routes WHERE sanitized_external_generation_approved") == 0,
        "apply and reapply never opt in existing routes");
    var actor = Guid.NewGuid();
    var route = await store.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning);
    var order = new[] { "gemini", "claude", "openai", "deepseek_v4", "celar_ai", "local_template" };
    await Reject<ArgumentException>(() => store.SaveRouteAsync(route.FeatureCode, order, null, actor,
        sanitizedExternalGenerationApproved: true), "approval requires revision concurrency token");
    await Reject<ArgumentException>(() => store.SaveRouteAsync(CelarAiCapabilityCatalog.ProjectFlowHivePlan, order, 1, actor,
        sanitizedExternalGenerationApproved: true), "structured approval cannot authorize unsupported consumers");
    var approved = await store.SaveRouteAsync(route.FeatureCode, order, route.Revision, actor,
        sanitizedExternalGenerationApproved: true);
    Check(approved.SanitizedExternalGenerationApproved && approved.Targets.SequenceEqual(order), "approved route saves exact manager-selected order");
    using var secondStore = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
    var loaded = await secondStore.LoadRouteAsync(route.FeatureCode);
    Check(loaded.Persisted && loaded.Revision == approved.Revision && loaded.SanitizedExternalGenerationApproved
        && loaded.Targets.SequenceEqual(order), "independent replica reloads persisted approval and order");
    await Reject<CelarAiConfigurationConflictException>(() => store.SaveRouteAsync(route.FeatureCode, order, route.Revision,
        actor, sanitizedExternalGenerationApproved: false), "stale revision cannot revoke or overwrite approval");
    Check(await Count("SELECT COUNT(*) FROM ai_capability_route_audit") == 1, "rejected writes create no route audit entries");
    Check(await Count($"SELECT COUNT(*) FROM ai_capability_route_audit WHERE actor_user_id='{actor}' AND NOT previous_external_generation_approved AND new_external_generation_approved") == 1,
        "approval audit records actor and false-to-true transition");
    var reordered = new[] { "openai", "gemini", "claude", "deepseek_v4", "celar_ai", "local_template" };
    var saved = await store.SaveRouteAsync(route.FeatureCode, reordered, loaded.Revision, actor);
    Check(saved.SanitizedExternalGenerationApproved && saved.Targets.SequenceEqual(reordered), "order-only edit preserves explicit approval");
    Check(await Count("SELECT COUNT(*) FROM ai_capability_route_audit WHERE previous_external_generation_approved AND new_external_generation_approved") == 1,
        "order-only audit preserves both approval states");
    await Apply(migration);
    Check((await store.LoadRouteAsync(route.FeatureCode)).SanitizedExternalGenerationApproved, "migration replay preserves an explicitly approved route");
    await Apply(rollback);
    var rolledBackApproval = await store.LoadRouteAsync(route.FeatureCode);
    Check(rolledBackApproval.SanitizedExternalGenerationApproved && !rolledBackApproval.ExternalGenerationApprovalSchemaReady,
        "rollback preserves history but disables structured external approval");
    await Reject<CelarAiRouteSchemaUnavailableException>(() => store.ResetRouteAsync(route.FeatureCode, saved.Revision, actor),
        "rollback prevents route mutation until schema is reverified");
    await Apply(migration);
    var reset = await store.ResetRouteAsync(route.FeatureCode, saved.Revision, actor);
    Check(!reset.SanitizedExternalGenerationApproved && reset.Targets.SequenceEqual(CelarAiCapabilityTargets.DefaultOrder), "reset revokes approval and restores default order");
    Check(await Count("SELECT COUNT(*) FROM ai_capability_route_audit WHERE previous_external_generation_approved AND NOT new_external_generation_approved") == 1,
        "reset audit records true-to-false transition");
    Check((await store.LoadRoutesAsync()).Where(item => item.FeatureCode != route.FeatureCode)
        .All(item => !item.SanitizedExternalGenerationApproved), "SOW changes never authorize another capability");
    await Apply(rollback);
    Check(await Count("SELECT COUNT(*) FROM schema_migrations WHERE migration_id='123_module064_external_generation_approval'") == 0,
        "rollback removes only its migration ledger entry");
    Check(await Count("SELECT COUNT(*) FROM ai_capability_route_audit") == 3,
        "rollback retains immutable transition evidence");
    await Apply(migration);
    var reapplied = await store.LoadRouteAsync(route.FeatureCode);
    Check(!reapplied.SanitizedExternalGenerationApproved && reapplied.Revision == reset.Revision,
        "rollback and reapply preserve latest route revision and revoked approval");

    await Apply("database/migrations/112_optional_ai_providers.sql");
    using var secretStore = new ProjectPulseAiSecretStore(NullLogger<ProjectPulseAiSecretStore>.Instance);
    using var replicaStore = new ProjectPulseAiSecretStore(NullLogger<ProjectPulseAiSecretStore>.Instance);
    var firstKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    var originalSecret = await secretStore.SaveAsync("gemini", firstKey, actor, default);
    var replicaConfiguration = new ProjectPulseAiConfiguration();
    replicaConfiguration.ApplyStoredSecret("gemini", originalSecret.ApiKey, originalSecret.Version, originalSecret.RotatedAt);
    replicaConfiguration.ApplyStoredEnabled("gemini", false);
    var originalProvider = replicaConfiguration.Provider("gemini");
    const string firstModel = "gemini-fixture-flash";
    const string secondModel = "gemini-fixture-pro";
    Check(await secretStore.TrySaveVerifiedModelAsync("gemini", firstModel, originalProvider, actor, default),
        "verified model commits against the exact saved credential");
    Check((await secretStore.LoadModelsAsync())["gemini"] == firstModel
        && !(await secretStore.LoadEnabledAsync())["gemini"], "first model insert preserves disabled provider state");
    Check(await Count("SELECT COUNT(*) FROM ai_provider_settings_audit WHERE provider_code='gemini'") == 1,
        "successful verified model write creates one audit record");
    Check(!await replicaStore.TrySaveVerifiedModelAsync("gemini", secondModel, originalProvider, actor, default),
        "stale replica cannot overwrite a concurrently selected model");
    Check(await Count("SELECT COUNT(*) FROM ai_provider_settings_audit WHERE provider_code='gemini'") == 1,
        "rejected stale model write does not add an audit record");
    replicaConfiguration.ApplyStoredModel("gemini", firstModel);
    var beforeDisableChange = replicaConfiguration.Provider("gemini");
    await secretStore.SaveEnabledAsync("gemini", true, firstModel, actor, default);
    Check(!await replicaStore.TrySaveVerifiedModelAsync("gemini", secondModel, beforeDisableChange, actor, default),
        "verified model save detects another replica's enabled-state change");
    replicaConfiguration.ApplyStoredEnabled("gemini", true);
    var beforeRotation = replicaConfiguration.Provider("gemini");
    var rotatedSecret = await secretStore.SaveAsync("gemini", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), actor, default);
    Check(!await replicaStore.TrySaveVerifiedModelAsync("gemini", secondModel, beforeRotation, actor, default),
        "successful probe with a stale replica credential cannot activate under a rotated database key");
    await using (var collision = new NpgsqlCommand("UPDATE ai_provider_secrets SET version=@version WHERE provider_code='gemini'", sql))
    {
        collision.Parameters.AddWithValue("version", beforeRotation.Secret.Version!);
        await collision.ExecuteNonQueryAsync();
    }
    Check(!await replicaStore.TrySaveVerifiedModelAsync("gemini", secondModel, beforeRotation, actor, default),
        "same-version credential collision still compares the full decrypted key");
    Check(await Count("SELECT COUNT(*) FROM ai_provider_settings_audit WHERE provider_code='gemini' AND action='model_changed'") == 1,
        "credential conflicts cannot write model activation audit entries");
    var currentSecret = (await secretStore.LoadAsync()).Single(item => item.ProviderCode == "gemini");
    replicaConfiguration.ApplyStoredSecret("gemini", currentSecret.ApiKey, currentSecret.Version, currentSecret.RotatedAt);
    var pendingProbeSnapshot = replicaConfiguration.Provider("gemini");

    // Hold the same database lock used by credential rotation, modify its
    // version, then start activation from another connection while uncommitted.
    // The candidate must wait and recheck committed state after the lock opens.
    await using (var rotationConnection = new NpgsqlConnection(connectionString))
    {
        await rotationConnection.OpenAsync();
        await using var rotationTransaction = await rotationConnection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(hashtextextended('module064_provider:gemini', 0));
            UPDATE ai_provider_secrets SET version = version || '-concurrent' WHERE provider_code='gemini';
            """, rotationConnection, rotationTransaction))
            await command.ExecuteNonQueryAsync();
        using var raceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var activation = replicaStore.TrySaveVerifiedModelAsync("gemini", secondModel, pendingProbeSnapshot, actor, raceTimeout.Token);
        var observedWait = false;
        var waitDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < waitDeadline)
        {
            if (await Count("SELECT COUNT(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE a.datname=current_database() AND l.locktype='advisory' AND NOT l.granted") > 0)
            {
                observedWait = true;
                break;
            }
            await Task.Delay(10, raceTimeout.Token);
        }
        Check(observedWait && !activation.IsCompleted, "model activation waits for an in-flight credential rotation transaction");
        await rotationTransaction.CommitAsync();
        Check(!await activation, "activation rechecks the newly committed credential after waiting for the provider lock");
    }
    Check((await secretStore.LoadModelsAsync())["gemini"] == firstModel
        && await Count("SELECT COUNT(*) FROM ai_provider_settings_audit WHERE provider_code='gemini' AND action='model_changed'") == 1,
        "concurrent rotation preserves the prior model and adds no activation audit entry");
    var afterConcurrentRotation = (await secretStore.LoadAsync()).Single(item => item.ProviderCode == "gemini");
    replicaConfiguration.ApplyStoredSecret("gemini", afterConcurrentRotation.ApiKey, afterConcurrentRotation.Version, afterConcurrentRotation.RotatedAt);
    Check(await replicaStore.TrySaveVerifiedModelAsync("gemini", secondModel, replicaConfiguration.Provider("gemini"), actor, default),
        "a newly tested snapshot can activate after the rotation completes");
    Check((await secretStore.LoadModelsAsync())["gemini"] == secondModel
        && await Count("SELECT COUNT(*) FROM ai_provider_settings_audit WHERE provider_code='gemini' AND action='model_changed'") == 2,
        "successful retry writes the model and exactly one additional audit record");
    var environmentOnly = replicaConfiguration.Provider("gemini") with
        { Secret = replicaConfiguration.Provider("gemini").Secret with { Source = "environment", Version = null } };
    Check(!await secretStore.TrySaveVerifiedModelAsync("gemini", firstModel, environmentOnly, actor, default),
        "environment-only credentials cannot bypass shared credential validation");
    // A historical saved sequence is authoritative as stored; a read may not
    // silently prepend a newer provider or substitute a default route.
    await Execute("UPDATE ai_capability_routes SET route_targets='[\"claude\",\"celar_ai\",\"openai\",\"local_template\"]'::jsonb WHERE feature_code='sow_gsd_planning'");
    Check((await store.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning)).Targets.SequenceEqual(
        new[] { "claude", "celar_ai", "openai", "local_template" }), "legacy persisted order is not silently expanded");
    await Execute("UPDATE ai_capability_routes SET route_targets='[\"unknown\",\"local_template\"]'::jsonb WHERE feature_code='sow_gsd_planning'");
    await Reject<InvalidOperationException>(() => store.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning),
        "invalid persisted order fails closed instead of running defaults");
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        await Reject<OperationCanceledException>(() => store.LoadRouteAsync(CelarAiCapabilityCatalog.SowGsdPlanning, cancellation.Token),
            "cancelled route read cannot continue with defaults");
    }
    var activeConnection = Environment.GetEnvironmentVariable("PROJECTPULSE_CONNECTION_STRING");
    try
    {
        Environment.SetEnvironmentVariable("PROJECTPULSE_CONNECTION_STRING",
            new NpgsqlConnectionStringBuilder(connectionString) { Port = 1, Timeout = 1, Pooling = false }.ConnectionString);
        using var unavailableStore = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
        await Reject<InvalidOperationException>(() => unavailableStore.LoadRouteAsync(CelarAiCapabilityCatalog.ProjectFlowHivePlan),
            "database outage cannot select an alternate provider order");
    }
    finally { Environment.SetEnvironmentVariable("PROJECTPULSE_CONNECTION_STRING", activeConnection); }
    Console.WriteLine($"MODULE064_ROUTE_PERSISTENCE=PASS checks={checks}");
}
finally
{
    foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    NpgsqlConnection.ClearAllPools();
    await new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", control).ExecuteNonQueryAsync();
}

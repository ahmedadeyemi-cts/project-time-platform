using Npgsql;

// Loopback PostgreSQL checks for migration 125 and canonical CRUD. No-ops
// cleanly when MODULE025_CANONICAL_TEST_DATABASE is absent so `dotnet run`
// passes locally without a database.
internal static class DatabaseChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var supplied = Environment.GetEnvironmentVariable("MODULE025_CANONICAL_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(supplied))
        {
            Console.WriteLine("SKIP database_checks (MODULE025_CANONICAL_TEST_DATABASE not set)");
            return;
        }
        var admin = new NpgsqlConnectionStringBuilder(supplied) { Database = "postgres", Pooling = false, IncludeErrorDetail = false };
        if (admin.Host is not ("127.0.0.1" or "localhost" or "::1")) throw new InvalidOperationException("Only loopback test databases are permitted.");
        var name = "canonical_test_" + Guid.NewGuid().ToString("N");
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", control).ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name }.ConnectionString;
        try
        {
            await using var sql = new NpgsqlConnection(connectionString);
            await sql.OpenAsync();
            async Task Run(string query) => _ = await new NpgsqlCommand(query, sql).ExecuteNonQueryAsync();
            async Task<long> Count(string query) => Convert.ToInt64(await new NpgsqlCommand(query, sql).ExecuteScalarAsync());
            async Task Apply(string path) => await Run(await File.ReadAllTextAsync(path));

            // Migration 125 requires app_users and a schema_migrations ledger.
            await Run("""
                CREATE TABLE app_users (user_id uuid PRIMARY KEY);
                CREATE TABLE schema_migrations (migration_id text PRIMARY KEY, description text, applied_at timestamptz NOT NULL DEFAULT now());
                INSERT INTO app_users(user_id) VALUES (gen_random_uuid());
                """);
            await Apply("database/migrations/125_module025_canonical_references.sql");
            await Apply("database/migrations/125_module025_canonical_references.sql"); // idempotent replay

            check(await Count("SELECT count(*) FROM schema_migrations WHERE migration_id='125_module025_canonical_references'") == 1,
                "migration125_records_a_single_receipt");
            check(await Count("SELECT count(*) FROM information_schema.columns WHERE table_name='module025_canonical_references' AND column_name IN ('label','source_text','source_sha256','active','created_by','updated_by')") == 6,
                "canonical_table_has_expected_columns");
            check(await Count("SELECT count(*) FROM information_schema.columns WHERE table_name='module025_canonical_references' AND (column_name ILIKE '%customer%' OR column_name ILIKE '%client%' OR column_name ILIKE '%engagement%')") == 0,
                "canonical_table_stores_no_customer_columns");

            var actor = await new NpgsqlCommand("SELECT user_id FROM app_users LIMIT 1", sql).ExecuteScalarAsync();
            var referenceId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("INSERT INTO module025_canonical_references(id,label,source_text,created_by,updated_by) VALUES (@id,'Standard Upgrade Template','Plan, design, implement, validate, release.',@actor,@actor)", sql))
            {
                insert.Parameters.AddWithValue("id", referenceId);
                insert.Parameters.AddWithValue("actor", actor!);
                await insert.ExecuteNonQueryAsync();
            }
            check(await Count("SELECT count(*) FROM module025_canonical_references WHERE active=TRUE") == 1, "new_reference_is_active_and_selectable");

            // Author selection is limited to active references.
            await Run($"UPDATE module025_canonical_references SET active=FALSE WHERE id='{referenceId}'");
            check(await Count("SELECT count(*) FROM module025_canonical_references WHERE active=TRUE") == 0, "deactivated_reference_is_not_author_selectable");
            check(await Count("SELECT count(*) FROM module025_canonical_references") == 1, "deactivation_is_soft_delete");

            // Length cap enforced by the CHECK constraint.
            var capRejected = false;
            try
            {
                await using var over = new NpgsqlCommand("INSERT INTO module025_canonical_references(id,label,source_text) VALUES (@id,'Too long',@text)", sql);
                over.Parameters.AddWithValue("id", Guid.NewGuid());
                over.Parameters.AddWithValue("text", new string('a', 30_001));
                await over.ExecuteNonQueryAsync();
            }
            catch (PostgresException exception) when (exception.SqlState == "23514") { capRejected = true; }
            check(capRejected, "source_text_length_cap_enforced_by_check_constraint");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", control).ExecuteNonQueryAsync();
        }
    }
}

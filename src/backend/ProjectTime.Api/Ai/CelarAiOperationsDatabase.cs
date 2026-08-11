using Npgsql;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Shared least-privilege Pulse database boundary for Celar AI operational
/// services. Connection material is read only from the existing runtime secret
/// projection and is never returned, logged, or persisted in an evidence row.
/// </summary>
public static class CelarAiOperationsDatabase
{
    public static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = ConnectionString();
        if (connectionString.Length == 0)
            throw new InvalidOperationException("The Pulse database connection is unavailable.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public static async Task<NpgsqlConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT
                to_regclass('public.module076_defects') IS NOT NULL
                AND to_regclass('public.module076_monitor_policies') IS NOT NULL
                AND to_regclass('public.module076_notification_outbox') IS NOT NULL
                AND EXISTS(
                    SELECT 1 FROM schema_migrations
                    WHERE migration_id='084_module_076_celar_ai_defect_operations'
                );
            """, connection);
        if (await command.ExecuteScalarAsync(cancellationToken) is true)
            return connection;
        await connection.DisposeAsync();
        throw new InvalidOperationException("Migration 084 is required before Celar AI operations can run.");
    }

    public static string ConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
            return string.Empty;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Require,
            Timeout = 15,
            CommandTimeout = 30,
            ApplicationName = "Pulse-Celar-AI-Operations"
        }.ConnectionString;
    }
}

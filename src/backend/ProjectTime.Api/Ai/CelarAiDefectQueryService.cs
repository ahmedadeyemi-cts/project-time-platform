using Npgsql;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Read-only, permission-scoped Module 076 defect queries for Ask Celar AI and
/// the Module 076 register. Ordinary users see only defects they reported or
/// are assigned; authorized managers may see all records. The service contains
/// no mutation SQL and does not widen View-As authority.
/// </summary>
public sealed class CelarAiDefectQueryService
{
    public async Task<IReadOnlyList<CelarAiDefectRecord>> ListAsync(
        Guid actualUserId,
        bool canViewAll,
        string? status,
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DefectSelect + """
            WHERE (
                    @all=TRUE
                    OR d.actual_reporter_user_id=@user
                    OR d.assignee_user_id=@user
                  )
              AND (@status='' OR lower(d.status)=lower(@status))
              AND (
                    @search=''
                    OR d.defect_number ILIKE '%' || @search || '%'
                    OR d.title ILIKE '%' || @search || '%'
                    OR d.description ILIKE '%' || @search || '%'
                    OR d.affected_module ILIKE '%' || @search || '%'
                    OR d.affected_system ILIKE '%' || @search || '%'
                  )
            ORDER BY
                CASE d.priority WHEN 'Critical' THEN 1 WHEN 'High' THEN 2 WHEN 'Medium' THEN 3 ELSE 4 END,
                d.date_added DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("all", canViewAll);
        command.Parameters.AddWithValue("user", actualUserId);
        command.Parameters.AddWithValue("status", CelarAiOperationsPolicy.Clean(status, 24));
        command.Parameters.AddWithValue("search", CelarAiOperationsPolicy.Clean(search, 240));
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CelarAiDefectRecord>> FindMatchesAsync(
        Guid actualUserId,
        bool canViewAll,
        string? environment,
        string? affectedModule,
        string? componentCode,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DefectSelect + """
            WHERE d.status IN ('Open','In Progress','Blocked','Reopened')
              AND (
                    @all=TRUE
                    OR d.actual_reporter_user_id=@user
                    OR d.assignee_user_id=@user
                  )
              AND (@environment='' OR lower(d.environment)=lower(@environment))
              AND (@module='' OR lower(d.affected_module)=lower(@module))
              AND (
                    @component=''
                    OR lower(d.affected_system)=lower(@component)
                    OR d.metadata->>'componentCode'=@component
                  )
              AND (@failure='' OR d.metadata->>'failureCode'=@failure)
            ORDER BY
                CASE d.priority WHEN 'Critical' THEN 1 WHEN 'High' THEN 2 WHEN 'Medium' THEN 3 ELSE 4 END,
                d.date_added DESC
            LIMIT 25;
            """, connection);
        command.Parameters.AddWithValue("all", canViewAll);
        command.Parameters.AddWithValue("user", actualUserId);
        command.Parameters.AddWithValue("environment", CelarAiOperationsPolicy.Clean(environment, 32));
        command.Parameters.AddWithValue("module", CelarAiOperationsPolicy.Clean(affectedModule, 20));
        command.Parameters.AddWithValue("component", CelarAiOperationsPolicy.Clean(componentCode, 100));
        command.Parameters.AddWithValue("failure", CelarAiOperationsPolicy.Clean(failureCode, 120));
        return await ReadAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<CelarAiDefectRecord>> ReadAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<CelarAiDefectRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CelarAiDefectRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                new CelarAiDefectIdentity(
                    reader.IsDBNull(12) ? null : reader.GetGuid(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    "snapshot"),
                new CelarAiDefectIdentity(
                    reader.IsDBNull(15) ? null : reader.GetGuid(15),
                    reader.GetString(16),
                    reader.GetString(17),
                    "snapshot"),
                reader.GetBoolean(18),
                reader.GetString(19),
                reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
                reader.GetInt32(21),
                reader.GetInt32(22),
                reader.GetFieldValue<DateTimeOffset>(23),
                reader.IsDBNull(24) ? null : reader.GetFieldValue<DateTimeOffset>(24),
                reader.IsDBNull(25) ? null : reader.GetInt64(25),
                reader.GetInt32(26)));
        }
        return rows;
    }

    private static async Task<NpgsqlConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connectionString = ConnectionString();
        if (connectionString.Length == 0)
            throw new InvalidOperationException("The Pulse database connection is unavailable.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT
                to_regclass('public.module076_defects') IS NOT NULL
                AND EXISTS(
                    SELECT 1 FROM schema_migrations
                    WHERE migration_id='084_module_076_celar_ai_defect_operations'
                );
            """, connection);
        if (await command.ExecuteScalarAsync(cancellationToken) is true) return connection;
        await connection.DisposeAsync();
        throw new InvalidOperationException("Migration 084 is required before Module 076 defect queries can run.");
    }

    private static string ConnectionString()
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
            || string.IsNullOrWhiteSpace(password)) return string.Empty;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Require,
            Timeout = 15,
            CommandTimeout = 30
        }.ConnectionString;
    }

    private const string DefectSelect = """
        SELECT d.defect_id,d.defect_number,d.title,d.description,d.category,d.priority,d.status,
               d.source_channel,d.environment,d.affected_system,d.affected_module,d.affected_route,
               d.actual_reporter_user_id,d.reporter_display_name,d.reporter_email,
               d.assignee_user_id,d.assignee_display_name,d.assignee_email,
               d.machine_created,d.correlation_id,d.release_sha,
               d.occurrence_count,d.flapping_count,d.date_added,d.date_resolved,
               d.resolution_seconds,d.revision_number
        FROM module076_defects d
        """;
}

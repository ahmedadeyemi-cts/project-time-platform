using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateRetrievalAuthorizationService
{
    private readonly ILogger<PulseAiPrivateRetrievalAuthorizationService> _logger;

    public PulseAiPrivateRetrievalAuthorizationService(
        ILogger<PulseAiPrivateRetrievalAuthorizationService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<PulseAiPrivateRetrievedChunk>> ReauthorizeAsync(
        PulseAiPrivateRagAccess access,
        IReadOnlyList<PulseAiPrivateRetrievedChunk> chunks,
        bool requireTimesheetFlag,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive || chunks.Count == 0) return [];
        if (MissingDatabaseConfiguration().Count > 0) return [];
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT ch.chunk_id
                FROM pulse_ai_document_chunks ch
                JOIN project_intake_documents d
                  ON d.project_intake_document_id = ch.project_intake_document_id
                JOIN pulse_ai_document_versions v
                  ON v.pulse_ai_document_version_id = ch.pulse_ai_document_version_id
                JOIN projects p ON p.project_id = ch.project_id
                WHERE ch.chunk_id = ANY(@chunk_ids)
                  AND ch.is_active = TRUE
                  AND ch.index_status IN ('lexical_ready','embedding_ready','ready')
                  AND d.is_active = TRUE
                  AND COALESCE(d.engineering_visible, FALSE) = TRUE
                  AND COALESCE(d.pulse_ai_processing_status, '') = 'ready'
                  AND d.pulse_ai_active_version_id = ch.pulse_ai_document_version_id
                  AND v.authority_status IN ('approved','canonical')
                  AND (@require_timesheet = FALSE OR ch.ai_timesheet_context_enabled = TRUE)
                  AND (
                    @is_broad = TRUE
                    OR p.project_manager_user_id = @user_id
                    OR EXISTS (
                        SELECT 1 FROM project_assignments pa
                        WHERE pa.project_id = p.project_id
                          AND pa.user_id = @user_id
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM engineering_resource_requests err
                        WHERE err.project_id = p.project_id
                          AND (
                            err.fulfilled_by_user_id = @user_id
                            OR err.assigned_pm_user_id = @user_id
                            OR EXISTS (
                                SELECT 1
                                FROM engineering_resource_request_assignments erra
                                WHERE erra.engineering_resource_request_id = err.engineering_resource_request_id
                                  AND erra.user_id = @user_id
                            )
                          )
                    )
                  );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("chunk_ids", chunks.Select(chunk => chunk.ChunkId).ToArray());
            command.Parameters.AddWithValue("require_timesheet", requireTimesheetFlag);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("user_id", access.UserId);
            var authorized = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                authorized.Add(reader.GetString(0));
            }
            return chunks
                .Where(chunk => authorized.Contains(chunk.ChunkId))
                .Select((chunk, index) => chunk with { RankOrder = index + 1 })
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI prompt-assembly reauthorization failed closed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return [];
        }
    }

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        var required = new[] { "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD" };
        return required
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();
    }

    private static string ConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("PTP_DB_HOST"),
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = Environment.GetEnvironmentVariable("PTP_DB_NAME"),
            Username = Environment.GetEnvironmentVariable("PTP_DB_USER"),
            Password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD"),
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 5,
            Timeout = 8,
            CommandTimeout = 20
        };
        return builder.ConnectionString;
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        _ => "private_retrieval_reauthorization_failure"
    };
}

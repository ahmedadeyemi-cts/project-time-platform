using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiSystemOperationsRepository
{
    private readonly ILogger<PulseAiSystemOperationsRepository> _logger;

    public PulseAiSystemOperationsRepository(ILogger<PulseAiSystemOperationsRepository> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT to_regclass('public.pulse_ai_system_operations_investigations') IS NOT NULL
                   AND to_regclass('public.pulse_ai_system_operations_evidence') IS NOT NULL
                   AND to_regclass('public.pulse_ai_future_enhancement_plans') IS NOT NULL;
                """, connection);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            Log(exception, "check system operations schema");
            return false;
        }
    }

    public async Task<Guid> CreateInvestigationAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        string sanitizedQuestion,
        PulseAiSystemOperationsClassification classification,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return Guid.Empty;
        var investigationId = Guid.NewGuid();
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                INSERT INTO pulse_ai_system_operations_investigations (
                    pulse_ai_system_operations_investigation_id,
                    actual_user_id,
                    effective_user_id,
                    intent_code,
                    investigation_status,
                    sanitized_question,
                    question_sha256,
                    classification_json,
                    correlation_id,
                    release_sha,
                    requested_at
                ) VALUES (
                    @id,
                    @actual_user_id,
                    @effective_user_id,
                    @intent_code,
                    'requested',
                    @sanitized_question,
                    @question_sha256,
                    CAST(@classification_json AS jsonb),
                    @correlation_id,
                    @release_sha,
                    NOW()
                );
                """, connection);
            command.Parameters.AddWithValue("id", investigationId);
            command.Parameters.AddWithValue("actual_user_id", actualUserId);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            command.Parameters.AddWithValue("intent_code", classification.Intent);
            command.Parameters.AddWithValue("sanitized_question", Limit(sanitizedQuestion, 6_000));
            command.Parameters.AddWithValue("question_sha256", Sha256(sanitizedQuestion));
            command.Parameters.AddWithValue("classification_json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(classification));
            command.Parameters.AddWithValue("correlation_id", Limit(correlationId, 160));
            command.Parameters.AddWithValue("release_sha", ReleaseSha());
            await command.ExecuteNonQueryAsync(cancellationToken);
            return investigationId;
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return Guid.Empty;
        }
        catch (Exception exception)
        {
            Log(exception, "create system operations investigation");
            return Guid.Empty;
        }
    }

    public async Task CompleteInvestigationAsync(
        PulseAiSystemOperationsAnswer answer,
        PulseAiSystemOperationsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (answer.InvestigationId == Guid.Empty) return;
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var update = new NpgsqlCommand("""
                UPDATE pulse_ai_system_operations_investigations
                SET investigation_status = @status,
                    direct_conclusion = @direct_conclusion,
                    answer_json = CAST(@answer_json AS jsonb),
                    api_count = @api_count,
                    evidence_count = @evidence_count,
                    finding_count = @finding_count,
                    dependency_count = @dependency_count,
                    correlation_id = @correlation_id,
                    release_sha = @release_sha,
                    data_as_of = @data_as_of,
                    diagnostic_code = @diagnostic_code,
                    completed_at = NOW(),
                    updated_at = NOW()
                WHERE pulse_ai_system_operations_investigation_id = @id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("id", answer.InvestigationId);
                update.Parameters.AddWithValue("status", Limit(answer.Status, 40));
                update.Parameters.AddWithValue("direct_conclusion", Limit(answer.Answer.DirectConclusion, 12_000));
                update.Parameters.AddWithValue("answer_json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(answer.ToPublicResponse()));
                update.Parameters.AddWithValue("api_count", answer.MatchingApiCount);
                update.Parameters.AddWithValue("evidence_count", answer.OperationalCitations.Count);
                update.Parameters.AddWithValue("finding_count", snapshot.PersistentFindings.Count);
                update.Parameters.AddWithValue("dependency_count", snapshot.Dependencies.Count);
                update.Parameters.AddWithValue("correlation_id", Limit(answer.CorrelationId, 160));
                update.Parameters.AddWithValue("release_sha", Limit(answer.ReleaseSha, 160));
                update.Parameters.AddWithValue("data_as_of", answer.DataAsOf);
                update.Parameters.AddWithValue("diagnostic_code", Limit(answer.DiagnosticCode, 120));
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var citation in answer.OperationalCitations.Take(500))
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO pulse_ai_system_operations_evidence (
                        pulse_ai_system_operations_evidence_id,
                        pulse_ai_system_operations_investigation_id,
                        rank_order,
                        evidence_type,
                        source_module,
                        source_name,
                        api_id,
                        method,
                        path,
                        evidence_status,
                        status_code,
                        response_time_ms,
                        error_code,
                        correlation_id,
                        observed_at,
                        release_sha,
                        evidence_json
                    ) VALUES (
                        @evidence_id,
                        @investigation_id,
                        @rank_order,
                        @evidence_type,
                        @source_module,
                        @source_name,
                        @api_id,
                        @method,
                        @path,
                        @evidence_status,
                        @status_code,
                        @response_time_ms,
                        @error_code,
                        @correlation_id,
                        @observed_at,
                        @release_sha,
                        CAST(@evidence_json AS jsonb)
                    ) ON CONFLICT (pulse_ai_system_operations_investigation_id, rank_order)
                      DO NOTHING;
                    """, connection, transaction);
                insert.Parameters.AddWithValue("evidence_id", Guid.NewGuid());
                insert.Parameters.AddWithValue("investigation_id", answer.InvestigationId);
                insert.Parameters.AddWithValue("rank_order", citation.CitationId);
                insert.Parameters.AddWithValue("evidence_type", Limit(citation.EvidenceType, 60));
                insert.Parameters.AddWithValue("source_module", Limit(citation.SourceModule, 20));
                insert.Parameters.AddWithValue("source_name", Limit(citation.SourceName, 300));
                insert.Parameters.AddWithValue("api_id", Limit(citation.ApiId, 300));
                insert.Parameters.AddWithValue("method", Limit(citation.Method, 16));
                insert.Parameters.AddWithValue("path", Limit(citation.Path, 500));
                insert.Parameters.AddWithValue("evidence_status", Limit(citation.Status, 40));
                insert.Parameters.AddWithValue("status_code", (object?)citation.StatusCode ?? DBNull.Value);
                insert.Parameters.AddWithValue("response_time_ms", (object?)citation.ResponseTimeMs ?? DBNull.Value);
                insert.Parameters.AddWithValue("error_code", Limit(citation.ErrorCode, 160));
                insert.Parameters.AddWithValue("correlation_id", Limit(citation.CorrelationId, 160));
                insert.Parameters.AddWithValue("observed_at", (object?)citation.ObservedAt ?? DBNull.Value);
                insert.Parameters.AddWithValue("release_sha", Limit(citation.ReleaseSha, 160));
                insert.Parameters.AddWithValue("evidence_json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(citation));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Log(exception, "complete system operations investigation");
        }
    }

    public async Task<IReadOnlyList<PulseAiSystemOperationsHistoryItem>> ListHistoryAsync(
        Guid actualUserId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return [];
        var rows = new List<PulseAiSystemOperationsHistoryItem>();
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT pulse_ai_system_operations_investigation_id,
                       intent_code,
                       investigation_status,
                       sanitized_question,
                       direct_conclusion,
                       api_count,
                       evidence_count,
                       correlation_id,
                       release_sha,
                       requested_at,
                       completed_at
                FROM pulse_ai_system_operations_investigations
                WHERE actual_user_id = @actual_user_id
                ORDER BY requested_at DESC
                LIMIT @limit;
                """, connection);
            command.Parameters.AddWithValue("actual_user_id", actualUserId);
            command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 200));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new PulseAiSystemOperationsHistoryItem(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)));
            }
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return [];
        }
        catch (Exception exception)
        {
            Log(exception, "list system operations history");
        }
        return rows;
    }

    public async Task<object?> GetInvestigationAsync(
        Guid investigationId,
        Guid actualUserId,
        CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            object? investigation = null;
            await using (var command = new NpgsqlCommand("""
                SELECT intent_code, investigation_status, sanitized_question,
                       direct_conclusion, answer_json, api_count, evidence_count,
                       finding_count, dependency_count, correlation_id, release_sha,
                       data_as_of, diagnostic_code, requested_at, completed_at
                FROM pulse_ai_system_operations_investigations
                WHERE pulse_ai_system_operations_investigation_id = @id
                  AND actual_user_id = @actual_user_id;
                """, connection))
            {
                command.Parameters.AddWithValue("id", investigationId);
                command.Parameters.AddWithValue("actual_user_id", actualUserId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return null;
                investigation = new
                {
                    investigationId,
                    intentCode = reader.GetString(0),
                    status = reader.GetString(1),
                    sanitizedQuestion = reader.GetString(2),
                    directConclusion = reader.GetString(3),
                    answer = JsonSerializer.Deserialize<object>(reader.GetString(4)),
                    apiCount = reader.GetInt32(5),
                    evidenceCount = reader.GetInt32(6),
                    findingCount = reader.GetInt32(7),
                    dependencyCount = reader.GetInt32(8),
                    correlationId = reader.GetString(9),
                    releaseSha = reader.GetString(10),
                    dataAsOf = reader.GetFieldValue<DateTimeOffset>(11),
                    diagnosticCode = reader.GetString(12),
                    requestedAt = reader.GetFieldValue<DateTimeOffset>(13),
                    completedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14)
                };
            }

            var evidence = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT rank_order, evidence_type, source_module, source_name,
                       api_id, method, path, evidence_status, status_code,
                       response_time_ms, error_code, correlation_id, observed_at,
                       release_sha
                FROM pulse_ai_system_operations_evidence
                WHERE pulse_ai_system_operations_investigation_id = @id
                ORDER BY rank_order;
                """, connection))
            {
                command.Parameters.AddWithValue("id", investigationId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    evidence.Add(new
                    {
                        rankOrder = reader.GetInt32(0),
                        evidenceType = reader.GetString(1),
                        sourceModule = reader.GetString(2),
                        sourceName = reader.GetString(3),
                        apiId = reader.GetString(4),
                        method = reader.GetString(5),
                        path = reader.GetString(6),
                        status = reader.GetString(7),
                        statusCode = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        responseTimeMs = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                        errorCode = reader.GetString(10),
                        correlationId = reader.GetString(11),
                        observedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                        releaseSha = reader.GetString(13)
                    });
                }
            }

            return new { investigation, evidence, rawLogsReturned = false, secretValuesReturned = false };
        }
        catch (Exception exception)
        {
            Log(exception, "get system operations investigation");
            return null;
        }
    }

    public async Task<Guid> SaveFutureEnhancementPlanAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        string title,
        string request,
        object plan,
        IReadOnlyList<string> affectedModules,
        CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return Guid.Empty;
        var id = Guid.NewGuid();
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                INSERT INTO pulse_ai_future_enhancement_plans (
                    pulse_ai_future_enhancement_plan_id,
                    actual_user_id,
                    effective_user_id,
                    plan_status,
                    title,
                    sanitized_request,
                    request_sha256,
                    affected_modules_json,
                    plan_json,
                    created_at,
                    updated_at
                ) VALUES (
                    @id,
                    @actual_user_id,
                    @effective_user_id,
                    'draft',
                    @title,
                    @request,
                    @request_sha256,
                    CAST(@affected_modules_json AS jsonb),
                    CAST(@plan_json AS jsonb),
                    NOW(),
                    NOW()
                );
                """, connection);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("actual_user_id", actualUserId);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            command.Parameters.AddWithValue("title", Limit(title, 300));
            command.Parameters.AddWithValue("request", Limit(request, 6_000));
            command.Parameters.AddWithValue("request_sha256", Sha256(request));
            command.Parameters.AddWithValue("affected_modules_json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(affectedModules));
            command.Parameters.AddWithValue("plan_json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(plan));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return Guid.Empty;
        }
        catch (Exception exception)
        {
            Log(exception, "save future enhancement plan");
            return Guid.Empty;
        }
    }

    private void Log(Exception exception, string operation) =>
        _logger.LogWarning(
            exception,
            "Pulse AI system operations could not {Operation}. Diagnostic={Diagnostic}",
            operation,
            Diagnostic(exception));

    private static string? BuildConnectionString()
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
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private static string ReleaseSha() =>
        Environment.GetEnvironmentVariable("SOURCE_VERSION")?.Trim()
        ?? Environment.GetEnvironmentVariable("PROJECTPULSE_RELEASE_SHA")?.Trim()
        ?? "not_recorded";

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Limit(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "database_timeout",
        OperationCanceledException => "operation_cancelled",
        _ => "repository_failure"
    };
}

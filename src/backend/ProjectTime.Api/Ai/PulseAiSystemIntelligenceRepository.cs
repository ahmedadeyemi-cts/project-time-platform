using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiSystemIntelligenceRepository
{
    private readonly ILogger<PulseAiSystemIntelligenceRepository> _logger;

    public PulseAiSystemIntelligenceRepository(ILogger<PulseAiSystemIntelligenceRepository> logger)
    {
        _logger = logger;
    }

    public bool DatabaseConfigured => MissingDatabaseConfiguration().Count == 0;

    public async Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return false;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = @migration_id)
                    AND to_regclass('public.pulse_ai_conversations') IS NOT NULL
                    AND to_regclass('public.pulse_ai_conversation_messages') IS NOT NULL
                    AND to_regclass('public.pulse_ai_system_inquiry_runs') IS NOT NULL
                    AND to_regclass('public.pulse_ai_system_tool_events') IS NOT NULL;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("migration_id", PulseAiSystemIntelligencePolicy.MigrationId);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI system intelligence schema readiness failed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return false;
        }
    }

    public async Task<object> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        var schemaReady = await IsSchemaReadyAsync(cancellationToken);
        var conversationCount = 0L;
        var inquiryCount = 0L;
        var toolEventCount = 0L;
        if (schemaReady)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString());
                await connection.OpenAsync(cancellationToken);
                const string sql = """
                    SELECT
                        (SELECT COUNT(*) FROM pulse_ai_conversations),
                        (SELECT COUNT(*) FROM pulse_ai_system_inquiry_runs),
                        (SELECT COUNT(*) FROM pulse_ai_system_tool_events);
                    """;
                await using var command = new NpgsqlCommand(sql, connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    conversationCount = reader.GetInt64(0);
                    inquiryCount = reader.GetInt64(1);
                    toolEventCount = reader.GetInt64(2);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Pulse AI system intelligence readiness counts failed. Diagnostic={Diagnostic}",
                    Diagnostic(exception));
            }
        }

        return new
        {
            status = schemaReady ? "system_intelligence_schema_ready" : "system_intelligence_schema_unavailable",
            contractVersion = PulseAiSystemIntelligencePolicy.ContractVersion,
            migrationId = PulseAiSystemIntelligencePolicy.MigrationId,
            databaseConfigured = DatabaseConfigured,
            schemaReady,
            conversationCount,
            inquiryCount,
            toolEventCount,
            durableConversations = schemaReady,
            immutableToolEvidence = schemaReady,
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<PulseAiConversationSummary?> CreateConversationAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiConversationCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active) return null;
        if (!await IsSchemaReadyAsync(cancellationToken)) return null;
        var conversationId = Guid.NewGuid();
        var mode = NormalizeMode(request.Mode);
        var title = Clean(request.Title, 240);
        if (title.Length == 0) title = "New Pulse AI conversation";
        var scopeJson = Serialize(request.Scope ?? new { });

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                INSERT INTO pulse_ai_conversations (
                    pulse_ai_conversation_id,actual_user_id,effective_user_id,
                    conversation_mode,title,scope_json,retention_until
                ) VALUES (
                    @conversation_id,@actual_user_id,@effective_user_id,
                    @conversation_mode,@title,@scope_json,@retention_until
                )
                RETURNING pulse_ai_conversation_id,effective_user_id,conversation_mode,
                          title,status,message_count,last_message_at,created_at,updated_at;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("conversation_id", conversationId);
            command.Parameters.AddWithValue("actual_user_id", actualUserId);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            command.Parameters.AddWithValue("conversation_mode", mode);
            command.Parameters.AddWithValue("title", title);
            command.Parameters.Add("scope_json", NpgsqlDbType.Jsonb).Value = scopeJson;
            command.Parameters.AddWithValue("retention_until", DateTimeOffset.UtcNow.AddDays(90));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? ReadConversationSummary(reader)
                : null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI conversation creation failed. EffectiveUserId={EffectiveUserId} Diagnostic={Diagnostic}",
                effectiveUserId,
                Diagnostic(exception));
            return null;
        }
    }

    public async Task<IReadOnlyList<PulseAiConversationSummary>> ListConversationsAsync(
        Guid effectiveUserId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return [];
        limit = Math.Clamp(limit, 1, 200);
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT pulse_ai_conversation_id,effective_user_id,conversation_mode,
                       title,status,message_count,last_message_at,created_at,updated_at
                FROM pulse_ai_conversations
                WHERE effective_user_id = @effective_user_id
                  AND status = 'active'
                ORDER BY COALESCE(last_message_at,created_at) DESC
                LIMIT @limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            command.Parameters.AddWithValue("limit", limit);
            var rows = new List<PulseAiConversationSummary>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadConversationSummary(reader));
            return rows;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI conversation listing failed. EffectiveUserId={EffectiveUserId} Diagnostic={Diagnostic}",
                effectiveUserId,
                Diagnostic(exception));
            return [];
        }
    }

    public async Task<PulseAiConversationDetail?> GetConversationAsync(
        Guid conversationId,
        Guid effectiveUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string conversationSql = """
                SELECT pulse_ai_conversation_id,effective_user_id,conversation_mode,
                       title,status,message_count,last_message_at,created_at,updated_at
                FROM pulse_ai_conversations
                WHERE pulse_ai_conversation_id = @conversation_id
                  AND effective_user_id = @effective_user_id
                  AND status = 'active';
                """;
            await using var command = new NpgsqlCommand(conversationSql, connection);
            command.Parameters.AddWithValue("conversation_id", conversationId);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            PulseAiConversationSummary? conversation;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) return null;
                conversation = ReadConversationSummary(reader);
            }

            const string messageSql = """
                SELECT pulse_ai_conversation_message_id,pulse_ai_conversation_id,
                       sequence_number,role,message_status,message_text,
                       structured_response_json,system_inquiry_run_id,
                       private_answer_run_id,correlation_id,model_provider,model_name,
                       tool_codes_json,source_states_json,data_as_of,created_at
                FROM pulse_ai_conversation_messages
                WHERE pulse_ai_conversation_id = @conversation_id
                ORDER BY sequence_number;
                """;
            await using var messageCommand = new NpgsqlCommand(messageSql, connection);
            messageCommand.Parameters.AddWithValue("conversation_id", conversationId);
            var messages = new List<PulseAiConversationMessage>();
            await using var messageReader = await messageCommand.ExecuteReaderAsync(cancellationToken);
            while (await messageReader.ReadAsync(cancellationToken))
            {
                messages.Add(new PulseAiConversationMessage(
                    MessageId: messageReader.GetGuid(0),
                    ConversationId: messageReader.GetGuid(1),
                    SequenceNumber: messageReader.GetInt32(2),
                    Role: messageReader.GetString(3),
                    Status: messageReader.GetString(4),
                    Text: messageReader.GetString(5),
                    StructuredResponse: ParseJson(messageReader.GetString(6)),
                    InquiryRunId: messageReader.IsDBNull(7) ? (Guid?)null : messageReader.GetGuid(7),
                    PrivateAnswerRunId: messageReader.IsDBNull(8) ? (Guid?)null : messageReader.GetGuid(8),
                    CorrelationId: messageReader.GetString(9),
                    ModelProvider: messageReader.GetString(10),
                    ModelName: messageReader.GetString(11),
                    ToolCodes: ParseStringArray(messageReader.GetString(12)),
                    SourceStates: ParseJson(messageReader.GetString(13)),
                    DataAsOf: messageReader.IsDBNull(14) ? (DateTimeOffset?)null : messageReader.GetFieldValue<DateTimeOffset>(14),
                    CreatedAt: messageReader.GetFieldValue<DateTimeOffset>(15)));
            }
            return new PulseAiConversationDetail(conversation, messages);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI conversation read failed. ConversationId={ConversationId} Diagnostic={Diagnostic}",
                conversationId,
                Diagnostic(exception));
            return null;
        }
    }

    public async Task<PulseAiConversationSummary?> EnsureConversationAsync(
        Guid? requestedConversationId,
        Guid actualUserId,
        Guid effectiveUserId,
        string mode,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active) return null;
        if (requestedConversationId is Guid requested && requested != Guid.Empty)
        {
            var existing = await GetConversationAsync(requested, effectiveUserId, cancellationToken);
            if (existing is not null) return existing.Conversation;
        }
        return await CreateConversationAsync(
            actualUserId,
            effectiveUserId,
            new PulseAiConversationCreateRequest(null, mode, null),
            cancellationToken);
    }

    public async Task<(Guid MessageId, int SequenceNumber)> AppendMessageAsync(
        Guid conversationId,
        Guid effectiveUserId,
        string role,
        string status,
        string messageText,
        object? structuredResponse,
        Guid? inquiryRunId,
        Guid? privateAnswerRunId,
        string correlationId,
        string modelProvider,
        string modelName,
        IReadOnlyList<string> toolCodes,
        object? sourceStates,
        DateTimeOffset? dataAsOf,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active) return (Guid.Empty, 0);
        if (!await IsSchemaReadyAsync(cancellationToken)) return (Guid.Empty, 0);
        var messageId = Guid.NewGuid();
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            const string lockSql = """
                SELECT 1
                FROM pulse_ai_conversations
                WHERE pulse_ai_conversation_id = @conversation_id
                  AND effective_user_id = @effective_user_id
                  AND status = 'active'
                FOR UPDATE;
                """;
            await using var lockCommand = new NpgsqlCommand(lockSql, connection, transaction);
            lockCommand.Parameters.AddWithValue("conversation_id", conversationId);
            lockCommand.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (Guid.Empty, 0);
            }

            const string nextSql = """
                SELECT COALESCE(MAX(sequence_number),0) + 1
                FROM pulse_ai_conversation_messages
                WHERE pulse_ai_conversation_id = @conversation_id;
                """;
            await using var nextCommand = new NpgsqlCommand(nextSql, connection, transaction);
            nextCommand.Parameters.AddWithValue("conversation_id", conversationId);
            var sequence = Convert.ToInt32(await nextCommand.ExecuteScalarAsync(cancellationToken) ?? 1);

            const string insertSql = """
                INSERT INTO pulse_ai_conversation_messages (
                    pulse_ai_conversation_message_id,pulse_ai_conversation_id,
                    sequence_number,role,message_status,message_text,
                    structured_response_json,system_inquiry_run_id,
                    private_answer_run_id,correlation_id,model_provider,model_name,
                    tool_codes_json,source_states_json,data_as_of
                ) VALUES (
                    @message_id,@conversation_id,@sequence_number,@role,@message_status,
                    @message_text,@structured_response_json,@system_inquiry_run_id,
                    @private_answer_run_id,@correlation_id,@model_provider,@model_name,
                    @tool_codes_json,@source_states_json,@data_as_of
                );
                """;
            await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
            insert.Parameters.AddWithValue("message_id", messageId);
            insert.Parameters.AddWithValue("conversation_id", conversationId);
            insert.Parameters.AddWithValue("sequence_number", sequence);
            insert.Parameters.AddWithValue("role", NormalizeRole(role));
            insert.Parameters.AddWithValue("message_status", NormalizeMessageStatus(status));
            insert.Parameters.AddWithValue("message_text", messageText ?? string.Empty);
            insert.Parameters.Add("structured_response_json", NpgsqlDbType.Jsonb).Value = Serialize(structuredResponse ?? new { });
            insert.Parameters.AddWithValue("system_inquiry_run_id", inquiryRunId is null ? DBNull.Value : inquiryRunId.Value);
            insert.Parameters.AddWithValue("private_answer_run_id", privateAnswerRunId is null ? DBNull.Value : privateAnswerRunId.Value);
            insert.Parameters.AddWithValue("correlation_id", Clean(correlationId, 160));
            insert.Parameters.AddWithValue("model_provider", Clean(modelProvider, 240));
            insert.Parameters.AddWithValue("model_name", Clean(modelName, 240));
            insert.Parameters.Add("tool_codes_json", NpgsqlDbType.Jsonb).Value = Serialize(toolCodes);
            insert.Parameters.Add("source_states_json", NpgsqlDbType.Jsonb).Value = Serialize(sourceStates ?? new { });
            insert.Parameters.AddWithValue("data_as_of", dataAsOf is null ? DBNull.Value : dataAsOf.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (messageId, sequence);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI conversation message persistence failed. ConversationId={ConversationId} Role={Role} Diagnostic={Diagnostic}",
                conversationId,
                role,
                Diagnostic(exception));
            return (Guid.Empty, 0);
        }
    }

    public async Task<Guid> CreateInquiryRunAsync(
        Guid conversationId,
        Guid userMessageId,
        Guid actualUserId,
        Guid effectiveUserId,
        string intentCode,
        string detailLevel,
        string questionSha256,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active) return Guid.Empty;
        if (!await IsSchemaReadyAsync(cancellationToken)) return Guid.Empty;
        var runId = Guid.NewGuid();
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                INSERT INTO pulse_ai_system_inquiry_runs (
                    pulse_ai_system_inquiry_run_id,pulse_ai_conversation_id,
                    user_message_id,actual_user_id,effective_user_id,intent_code,
                    detail_level,question_sha256,correlation_id
                ) VALUES (
                    @run_id,@conversation_id,@user_message_id,@actual_user_id,
                    @effective_user_id,@intent_code,@detail_level,@question_sha256,
                    @correlation_id
                );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("run_id", runId);
            command.Parameters.AddWithValue("conversation_id", conversationId);
            command.Parameters.AddWithValue("user_message_id", userMessageId);
            command.Parameters.AddWithValue("actual_user_id", actualUserId);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            command.Parameters.AddWithValue("intent_code", Clean(intentCode, 80));
            command.Parameters.AddWithValue("detail_level", Clean(detailLevel, 50));
            command.Parameters.AddWithValue("question_sha256", Clean(questionSha256, 64));
            command.Parameters.AddWithValue("correlation_id", Clean(correlationId, 160));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return runId;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI inquiry-run persistence failed. ConversationId={ConversationId} Diagnostic={Diagnostic}",
                conversationId,
                Diagnostic(exception));
            return Guid.Empty;
        }
    }

    public async Task SaveToolEventAsync(
        Guid inquiryRunId,
        PulseAiSystemToolResult result,
        bool persistResponseBody,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active) return;
        if (inquiryRunId == Guid.Empty || !await IsSchemaReadyAsync(cancellationToken)) return;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                INSERT INTO pulse_ai_system_tool_events (
                    pulse_ai_system_inquiry_run_id,tool_code,module_code,method,path,
                    event_status,status_code,duration_ms,response_bytes,diagnostic_code,
                    evidence_json,observed_at
                ) VALUES (
                    @run_id,@tool_code,@module_code,@method,@path,@event_status,
                    @status_code,@duration_ms,@response_bytes,@diagnostic_code,
                    @evidence_json,@observed_at
                );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("run_id", inquiryRunId);
            command.Parameters.AddWithValue("tool_code", result.ToolCode);
            command.Parameters.AddWithValue("module_code", result.ModuleCode);
            command.Parameters.AddWithValue("method", result.Method);
            command.Parameters.AddWithValue("path", result.Path);
            command.Parameters.AddWithValue("event_status", NormalizeToolStatus(result.Status));
            command.Parameters.AddWithValue("status_code", result.StatusCode);
            command.Parameters.AddWithValue("duration_ms", result.DurationMs);
            command.Parameters.AddWithValue("response_bytes", result.ResponseBytes);
            command.Parameters.AddWithValue("diagnostic_code", result.DiagnosticCode);
            command.Parameters.Add("evidence_json", NpgsqlDbType.Jsonb).Value = Serialize(new
            {
                result.EvidenceSummary,
                responseSha256 = Sha256(result.ResponseJson),
                responseBodyPersisted = persistResponseBody,
                responseBody = persistResponseBody ? result.ResponseJson : string.Empty
            });
            command.Parameters.AddWithValue("observed_at", result.ObservedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI tool-event persistence failed. Tool={Tool} Diagnostic={Diagnostic}",
                result.ToolCode,
                Diagnostic(exception));
        }
    }

    public async Task CompleteInquiryRunAsync(
        Guid inquiryRunId,
        Guid assistantMessageId,
        string status,
        IReadOnlyList<PulseAiSystemToolDefinition> selectedTools,
        IReadOnlyList<PulseAiSystemToolResult> toolResults,
        int registeredApiCount,
        decimal confidence,
        string diagnosticCode,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active) return;
        if (inquiryRunId == Guid.Empty || !await IsSchemaReadyAsync(cancellationToken)) return;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                UPDATE pulse_ai_system_inquiry_runs
                SET assistant_message_id = @assistant_message_id,
                    selected_tools_json = @selected_tools_json,
                    tool_summary_json = @tool_summary_json,
                    answer_status = @answer_status,
                    registered_api_count = @registered_api_count,
                    successful_tool_count = @successful_tool_count,
                    failed_tool_count = @failed_tool_count,
                    confidence = @confidence,
                    diagnostic_code = @diagnostic_code,
                    completed_at = NOW(),
                    updated_at = NOW()
                WHERE pulse_ai_system_inquiry_run_id = @run_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("run_id", inquiryRunId);
            command.Parameters.AddWithValue("assistant_message_id", assistantMessageId == Guid.Empty ? DBNull.Value : assistantMessageId);
            command.Parameters.Add("selected_tools_json", NpgsqlDbType.Jsonb).Value = Serialize(selectedTools.Select(tool => new
            {
                tool.Code,
                tool.Name,
                tool.ModuleCode,
                tool.Method,
                tool.Path
            }));
            command.Parameters.Add("tool_summary_json", NpgsqlDbType.Jsonb).Value = Serialize(new
            {
                total = toolResults.Count,
                succeeded = toolResults.Count(result => result.Succeeded),
                failed = toolResults.Count(result => !result.Succeeded),
                forbidden = toolResults.Count(result => result.Forbidden),
                diagnostics = toolResults.Where(result => result.DiagnosticCode.Length > 0)
                    .GroupBy(result => result.DiagnosticCode)
                    .ToDictionary(group => group.Key, group => group.Count())
            });
            command.Parameters.AddWithValue("answer_status", NormalizeInquiryStatus(status));
            command.Parameters.AddWithValue("registered_api_count", Math.Max(0, registeredApiCount));
            command.Parameters.AddWithValue("successful_tool_count", toolResults.Count(result => result.Succeeded));
            command.Parameters.AddWithValue("failed_tool_count", toolResults.Count(result => !result.Succeeded));
            command.Parameters.AddWithValue("confidence", Math.Clamp(confidence, 0m, 1m));
            command.Parameters.AddWithValue("diagnostic_code", Clean(diagnosticCode, 160));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI inquiry-run completion failed. InquiryRunId={InquiryRunId} Diagnostic={Diagnostic}",
                inquiryRunId,
                Diagnostic(exception));
        }
    }

    private static PulseAiConversationSummary ReadConversationSummary(NpgsqlDataReader reader) =>
        new(
            ConversationId: reader.GetGuid(0),
            EffectiveUserId: reader.GetGuid(1),
            Mode: reader.GetString(2),
            Title: reader.GetString(3),
            Status: reader.GetString(4),
            MessageCount: reader.GetInt32(5),
            LastMessageAt: reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(7),
            UpdatedAt: reader.GetFieldValue<DateTimeOffset>(8));

    private static string NormalizeMode(string? mode) => Clean(mode, 50).ToLowerInvariant() switch
    {
        "api_inventory" => "api_inventory",
        "troubleshooting" => "troubleshooting",
        "future_enhancement" => "future_enhancement",
        "project_intelligence" => "project_intelligence",
        "general" => "general",
        _ => "system_help"
    };

    private static string NormalizeRole(string role) => role.ToLowerInvariant() switch
    {
        "assistant" => "assistant",
        "system" => "system",
        _ => "user"
    };

    private static string NormalizeMessageStatus(string status) => status.ToLowerInvariant() switch
    {
        "queued" => "queued",
        "partial" => "partial",
        "failed" => "failed",
        "blocked" => "blocked",
        _ => "completed"
    };

    private static string NormalizeInquiryStatus(string status) => status.ToLowerInvariant() switch
    {
        "partial" => "partial",
        "failed" => "failed",
        "blocked" => "blocked",
        "running" => "running",
        _ => "completed"
    };

    private static string NormalizeToolStatus(string status) => status.ToLowerInvariant() switch
    {
        "succeeded" => "succeeded",
        "partial" => "partial",
        "forbidden" => "forbidden",
        "not_found" => "not_found",
        "skipped" => "skipped",
        _ => "failed"
    };

    private static object ParseJson(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(value) ?? new { };
        }
        catch
        {
            return new { };
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

    private static string Sha256(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        var required = new[] { "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD" };
        return required.Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))).ToArray();
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
            MaxPoolSize = 12,
            Timeout = 8,
            CommandTimeout = 30
        };
        return builder.ConnectionString;
    }

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        _ => "system_intelligence_repository_failure"
    };
}

using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class FinancialOperationsRepository
{
    private const string MigrationId = "051_financial_operations_reporting_recovery";

    internal static async Task<NpgsqlConnection> OpenAsync(
        CancellationToken cancellationToken)
    {
        var connectionString = ProjectFinancialTruthModule.FinancialOperationsConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Pulse database configuration is unavailable.");

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    internal static async Task<bool> MigrationReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM schema_migrations
                WHERE migration_id = @migration_id
            );
            """, connection);
        command.Parameters.AddWithValue("migration_id", MigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task<Guid> SaveReportRunAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        FinancialReportResult result,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO financial_report_runs (
                financial_report_run_id,
                report_code,
                report_name,
                actual_user_id,
                effective_user_id,
                filters_json,
                result_status,
                row_count,
                source_states_json,
                result_json,
                diagnostic_code,
                diagnostic_message,
                started_at,
                completed_at,
                created_at
            )
            VALUES (
                @run_id,
                @report_code,
                @report_name,
                @actual_user_id,
                @effective_user_id,
                @filters::jsonb,
                @result_status,
                @row_count,
                @sources::jsonb,
                @results::jsonb,
                '',
                @message,
                @started_at,
                NOW(),
                NOW()
            );
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("report_code", result.ReportCode);
        command.Parameters.AddWithValue("report_name", result.ReportName);
        command.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("filters", JsonSerializer.Serialize(result.Filters));
        command.Parameters.AddWithValue("result_status", result.ResultStatus);
        command.Parameters.AddWithValue("row_count", result.RowCount);
        command.Parameters.AddWithValue("sources", JsonSerializer.Serialize(result.Sources));
        command.Parameters.AddWithValue("results", JsonSerializer.Serialize(result.Rows));
        command.Parameters.AddWithValue("message", Limit(result.Message, 4000));
        command.Parameters.AddWithValue("started_at", result.GeneratedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return runId;
    }

    internal static async Task<List<FinancialReportRunRow>> LoadReportHistoryAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<FinancialReportRunRow>();
        await using var command = new NpgsqlCommand("""
            SELECT financial_report_run_id,
                   report_code,
                   report_name,
                   result_status,
                   row_count,
                   actual_user_id,
                   effective_user_id,
                   filters_json::text,
                   source_states_json::text,
                   result_json::text,
                   diagnostic_code,
                   diagnostic_message,
                   started_at,
                   completed_at,
                   last_exported_at,
                   created_at
            FROM financial_report_runs
            WHERE @broad OR effective_user_id = @effective_user_id
            ORDER BY created_at DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 200));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(ReadReportRun(reader));
        return rows;
    }

    internal static async Task<FinancialReportRunRow?> LoadReportRunAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT financial_report_run_id,
                   report_code,
                   report_name,
                   result_status,
                   row_count,
                   actual_user_id,
                   effective_user_id,
                   filters_json::text,
                   source_states_json::text,
                   result_json::text,
                   diagnostic_code,
                   diagnostic_message,
                   started_at,
                   completed_at,
                   last_exported_at,
                   created_at
            FROM financial_report_runs
            WHERE financial_report_run_id = @run_id
              AND (@broad OR effective_user_id = @effective_user_id)
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReportRun(reader) : null;
    }

    internal static async Task MarkReportExportedAsync(
        NpgsqlConnection connection,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE financial_report_runs
            SET last_exported_at = NOW()
            WHERE financial_report_run_id = @run_id;
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task UpsertWorkItemsAsync(
        NpgsqlConnection connection,
        IEnumerable<FinancialOperationsDerivedItem> items,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in items)
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO financial_operations_work_items (
                        financial_operations_work_item_id,
                        deduplication_key,
                        project_id,
                        module_code,
                        item_type,
                        source_key,
                        priority,
                        work_status,
                        title,
                        detail,
                        owner_user_id,
                        retry_endpoint,
                        first_detected_at,
                        last_detected_at,
                        metadata_json,
                        created_at,
                        updated_at
                    )
                    VALUES (
                        gen_random_uuid(),
                        @deduplication_key,
                        @project_id,
                        @module_code,
                        @item_type,
                        @source_key,
                        @priority,
                        'open',
                        @title,
                        @detail,
                        @owner_user_id,
                        @retry_endpoint,
                        NOW(),
                        NOW(),
                        @metadata::jsonb,
                        NOW(),
                        NOW()
                    )
                    ON CONFLICT (deduplication_key)
                    DO UPDATE SET
                        project_id = EXCLUDED.project_id,
                        module_code = EXCLUDED.module_code,
                        item_type = EXCLUDED.item_type,
                        source_key = EXCLUDED.source_key,
                        priority = EXCLUDED.priority,
                        work_status = CASE
                            WHEN financial_operations_work_items.work_status IN ('resolved', 'dismissed')
                                THEN 'open'
                            ELSE financial_operations_work_items.work_status
                        END,
                        title = EXCLUDED.title,
                        detail = EXCLUDED.detail,
                        owner_user_id = EXCLUDED.owner_user_id,
                        retry_endpoint = EXCLUDED.retry_endpoint,
                        last_detected_at = NOW(),
                        resolved_by_user_id = NULL,
                        resolved_at = NULL,
                        resolution_note = CASE
                            WHEN financial_operations_work_items.work_status IN ('resolved', 'dismissed')
                                THEN ''
                            ELSE financial_operations_work_items.resolution_note
                        END,
                        metadata_json = EXCLUDED.metadata_json,
                        updated_at = NOW();
                    """, connection, transaction);
                command.Parameters.AddWithValue("deduplication_key", Limit(item.DeduplicationKey, 300));
                AddNullable(command, "project_id", NpgsqlDbType.Uuid, item.ProjectId);
                command.Parameters.AddWithValue("module_code", Limit(item.ModuleCode, 20));
                command.Parameters.AddWithValue("item_type", Limit(item.ItemType, 100));
                command.Parameters.AddWithValue("source_key", Limit(item.SourceKey, 120));
                command.Parameters.AddWithValue("priority", NormalizePriority(item.Priority));
                command.Parameters.AddWithValue("title", Limit(item.Title, 320));
                command.Parameters.AddWithValue("detail", Limit(item.Detail, 10000));
                AddNullable(command, "owner_user_id", NpgsqlDbType.Uuid, item.OwnerUserId);
                command.Parameters.AddWithValue("retry_endpoint", Limit(item.RetryEndpoint, 1000));
                command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(item.Metadata));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<List<FinancialOperationsWorkItem>> LoadWorkItemsAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        Guid[] visibleProjectIds,
        string status,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<FinancialOperationsWorkItem>();
        await using var command = new NpgsqlCommand("""
            SELECT item.financial_operations_work_item_id,
                   item.deduplication_key,
                   item.project_id,
                   item.module_code,
                   item.item_type,
                   item.source_key,
                   item.priority,
                   item.work_status,
                   item.title,
                   item.detail,
                   item.owner_user_id,
                   COALESCE(owner.display_name, owner.email, ''),
                   item.retry_endpoint,
                   item.first_detected_at,
                   item.last_detected_at,
                   item.acknowledged_at,
                   item.resolved_at,
                   item.resolution_note,
                   item.metadata_json::text
            FROM financial_operations_work_items item
            LEFT JOIN app_users owner ON owner.user_id = item.owner_user_id
            WHERE (@status = '' OR item.work_status = @status)
              AND (
                    @broad
                    OR item.project_id = ANY(@project_ids)
                    OR (item.project_id IS NULL AND item.owner_user_id = @effective_user_id)
              )
            ORDER BY
                CASE item.priority
                    WHEN 'critical' THEN 0
                    WHEN 'high' THEN 1
                    WHEN 'medium' THEN 2
                    ELSE 3
                END,
                item.last_detected_at DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("status", NormalizeStatusFilter(status));
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.Add(new NpgsqlParameter(
            "project_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = visibleProjectIds
        });
        command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FinancialOperationsWorkItem(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.GetString(11),
                reader.GetString(12),
                DateTimeOffsetValue(reader, 13),
                DateTimeOffsetValue(reader, 14),
                reader.IsDBNull(15) ? null : DateTimeOffsetValue(reader, 15),
                reader.IsDBNull(16) ? null : DateTimeOffsetValue(reader, 16),
                reader.GetString(17),
                ParseJson(reader.GetString(18))));
        }
        return rows;
    }

    internal static async Task<bool> UpdateWorkItemAsync(
        NpgsqlConnection connection,
        Guid workItemId,
        string status,
        string note,
        FinancialOperationsActor actor,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = status.Equals("acknowledged", StringComparison.OrdinalIgnoreCase)
            ? "acknowledged"
            : status.Equals("dismissed", StringComparison.OrdinalIgnoreCase)
                ? "dismissed"
                : "resolved";
        await using var command = new NpgsqlCommand("""
            UPDATE financial_operations_work_items
            SET work_status = @status,
                acknowledged_by_user_id = CASE
                    WHEN @status = 'acknowledged' THEN @actor_user_id
                    ELSE acknowledged_by_user_id
                END,
                acknowledged_at = CASE
                    WHEN @status = 'acknowledged' THEN NOW()
                    ELSE acknowledged_at
                END,
                resolved_by_user_id = CASE
                    WHEN @status IN ('resolved', 'dismissed') THEN @actor_user_id
                    ELSE NULL
                END,
                resolved_at = CASE
                    WHEN @status IN ('resolved', 'dismissed') THEN NOW()
                    ELSE NULL
                END,
                resolution_note = @note,
                updated_at = NOW()
            WHERE financial_operations_work_item_id = @work_item_id;
            """, connection);
        command.Parameters.AddWithValue("status", normalizedStatus);
        command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("note", Limit(note, 5000));
        command.Parameters.AddWithValue("work_item_id", workItemId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal static async Task RecordActionAsync(
        NpgsqlConnection connection,
        Guid? workItemId,
        Guid? projectId,
        string sourceKey,
        string actionCode,
        string actionStatus,
        FinancialOperationsActor actor,
        string diagnosticCode,
        string diagnosticMessage,
        string correlationId,
        object metadata,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO financial_operations_actions (
                financial_operations_action_id,
                financial_operations_work_item_id,
                project_id,
                source_key,
                action_code,
                action_status,
                actor_user_id,
                diagnostic_code,
                diagnostic_message,
                correlation_id,
                metadata_json,
                created_at
            )
            VALUES (
                gen_random_uuid(),
                @work_item_id,
                @project_id,
                @source_key,
                @action_code,
                @action_status,
                @actor_user_id,
                @diagnostic_code,
                @diagnostic_message,
                @correlation_id,
                @metadata::jsonb,
                NOW()
            );
            """, connection);
        AddNullable(command, "work_item_id", NpgsqlDbType.Uuid, workItemId);
        AddNullable(command, "project_id", NpgsqlDbType.Uuid, projectId);
        command.Parameters.AddWithValue("source_key", Limit(sourceKey, 120));
        command.Parameters.AddWithValue("action_code", Limit(actionCode, 120));
        command.Parameters.AddWithValue("action_status", NormalizeActionStatus(actionStatus));
        command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("diagnostic_code", Limit(diagnosticCode, 120));
        command.Parameters.AddWithValue("diagnostic_message", Limit(diagnosticMessage, 10000));
        command.Parameters.AddWithValue("correlation_id", Limit(correlationId, 160));
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<List<FinancialOperationsAction>> LoadActionsAsync(
        NpgsqlConnection connection,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<FinancialOperationsAction>();
        await using var command = new NpgsqlCommand("""
            SELECT financial_operations_action_id,
                   financial_operations_work_item_id,
                   project_id,
                   source_key,
                   action_code,
                   action_status,
                   actor_user_id,
                   diagnostic_code,
                   diagnostic_message,
                   correlation_id,
                   metadata_json::text,
                   created_at
            FROM financial_operations_actions
            ORDER BY created_at DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 200));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FinancialOperationsAction(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                ParseJson(reader.GetString(10)),
                DateTimeOffsetValue(reader, 11)));
        }
        return rows;
    }

    private static FinancialReportRunRow ReadReportRun(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetGuid(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6),
        ParseJson(reader.GetString(7)),
        ParseJson(reader.GetString(8)),
        ParseJson(reader.GetString(9)),
        reader.GetString(10),
        reader.GetString(11),
        DateTimeOffsetValue(reader, 12),
        reader.IsDBNull(13) ? null : DateTimeOffsetValue(reader, 13),
        reader.IsDBNull(14) ? null : DateTimeOffsetValue(reader, 14),
        DateTimeOffsetValue(reader, 15));

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    private static DateTimeOffset DateTimeOffsetValue(
        NpgsqlDataReader reader,
        int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(
                DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? "")
        };
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value) => command.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    private static string NormalizePriority(string value) =>
        value.ToLowerInvariant() is "low" or "high" or "critical"
            ? value.ToLowerInvariant()
            : "medium";

    private static string NormalizeStatusFilter(string value) =>
        value.ToLowerInvariant() is "open" or "acknowledged" or "resolved" or "dismissed"
            ? value.ToLowerInvariant()
            : "";

    private static string NormalizeActionStatus(string value) =>
        value.ToLowerInvariant() is "requested" or "succeeded" or "partial" or "failed" or "suppressed"
            ? value.ToLowerInvariant()
            : "failed";

    private static string Limit(string value, int maximum)
    {
        var clean = (value ?? string.Empty).Replace('\0', ' ').Trim();
        return clean.Length <= maximum ? clean : clean[..maximum];
    }
}

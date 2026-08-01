using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseReportingRepository
{
    internal const string MigrationId = "054_enterprise_reporting_center";

    internal static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = ProjectFinancialTruthModule.FinancialOperationsConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ProjectPulse database configuration is unavailable.");
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
                SELECT 1 FROM schema_migrations WHERE migration_id = @migration_id
            );
            """, connection);
        command.Parameters.AddWithValue("migration_id", MigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task<Guid> SaveRunAsync(
        NpgsqlConnection connection,
        EnterpriseReportingContext context,
        EnterpriseReportResult result,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO enterprise_report_runs (
                enterprise_report_run_id,
                report_code,
                report_name,
                actual_user_id,
                effective_user_id,
                scope_snapshot_json,
                filters_json,
                columns_json,
                result_status,
                row_count,
                source_states_json,
                result_json,
                started_at,
                completed_at,
                created_at
            ) VALUES (
                @run_id, @report_code, @report_name,
                @actual_user_id, @effective_user_id,
                @scope::jsonb, @filters::jsonb, @columns::jsonb,
                @result_status, @row_count,
                @sources::jsonb, @results::jsonb,
                @started_at, NOW(), NOW()
            );
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("report_code", result.ReportCode);
        command.Parameters.AddWithValue("report_name", result.ReportName);
        command.Parameters.AddWithValue("actual_user_id", context.Actor.ActualUserId);
        command.Parameters.AddWithValue("effective_user_id", context.Actor.EffectiveUserId);
        command.Parameters.AddWithValue("scope", JsonSerializer.Serialize(result.ScopeEvidence));
        command.Parameters.AddWithValue("filters", JsonSerializer.Serialize(result.EffectiveFilters));
        command.Parameters.AddWithValue("columns", JsonSerializer.Serialize(result.Columns));
        command.Parameters.AddWithValue("result_status", result.ResultStatus);
        command.Parameters.AddWithValue("row_count", result.RowCount);
        command.Parameters.AddWithValue("sources", JsonSerializer.Serialize(result.Sources));
        command.Parameters.AddWithValue("results", JsonSerializer.Serialize(result.Rows));
        command.Parameters.AddWithValue("started_at", result.GeneratedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return runId;
    }

    internal static async Task<EnterpriseReportRunRecord?> LoadRunAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT enterprise_report_run_id, report_code, report_name,
                   result_status, row_count, actual_user_id, effective_user_id,
                   scope_snapshot_json::text, filters_json::text,
                   columns_json::text, source_states_json::text,
                   result_json::text, started_at, completed_at, created_at
            FROM enterprise_report_runs
            WHERE enterprise_report_run_id = @run_id
              AND (@broad OR effective_user_id = @effective_user_id)
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    internal static async Task<EnterpriseReportRunRecord[]> LoadHistoryAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<EnterpriseReportRunRecord>();
        await using var command = new NpgsqlCommand("""
            SELECT enterprise_report_run_id, report_code, report_name,
                   result_status, row_count, actual_user_id, effective_user_id,
                   scope_snapshot_json::text, filters_json::text,
                   columns_json::text, source_states_json::text,
                   result_json::text, started_at, completed_at, created_at
            FROM enterprise_report_runs
            WHERE @broad OR effective_user_id = @effective_user_id
            ORDER BY created_at DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 250));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadRun(reader));
        return rows.ToArray();
    }

    internal static async Task RecordExportAsync(
        NpgsqlConnection connection,
        Guid runId,
        FinancialOperationsActor actor,
        string format,
        int rowCount,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await using var command = new NpgsqlCommand("""
            INSERT INTO enterprise_report_exports (
                enterprise_report_export_id,
                enterprise_report_run_id,
                actor_user_id,
                export_format,
                row_count,
                content_sha256,
                created_at
            ) VALUES (
                gen_random_uuid(), @run_id, @actor_user_id,
                @format, @row_count, @checksum, NOW()
            );
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("format", format);
        command.Parameters.AddWithValue("row_count", rowCount);
        command.Parameters.AddWithValue("checksum", checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<EnterpriseSavedViewRecord[]> LoadSavedViewsAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        CancellationToken cancellationToken)
    {
        var rows = new List<EnterpriseSavedViewRecord>();
        await using var command = new NpgsqlCommand("""
            SELECT enterprise_report_saved_view_id, view_name, report_code,
                   owner_user_id, filters_json::text, is_default, version,
                   created_at, updated_at
            FROM enterprise_report_saved_views
            WHERE owner_user_id = @owner_user_id
            ORDER BY is_default DESC, view_name;
            """, connection);
        command.Parameters.AddWithValue("owner_user_id", actor.EffectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new EnterpriseSavedViewRecord(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetGuid(3), ParseJson(reader.GetString(4)), reader.GetBoolean(5),
                reader.GetInt32(6), DateTimeOffsetValue(reader, 7), DateTimeOffsetValue(reader, 8)));
        }
        return rows.ToArray();
    }

    internal static async Task<Guid> SaveViewAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        EnterpriseSavedViewRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.SavedViewId ?? Guid.NewGuid();
        var name = Clean(request.Name, 160);
        var reportCode = Clean(request.ReportCode, 120);
        if (name.Length == 0 || reportCode.Length == 0)
            throw new ArgumentException("View name and report code are required.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (request.IsDefault == true)
        {
            await using var clear = new NpgsqlCommand("""
                UPDATE enterprise_report_saved_views
                SET is_default = FALSE, updated_at = NOW(), version = version + 1
                WHERE owner_user_id = @owner_user_id;
                """, connection, transaction);
            clear.Parameters.AddWithValue("owner_user_id", actor.EffectiveUserId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand("""
            INSERT INTO enterprise_report_saved_views (
                enterprise_report_saved_view_id, view_name, report_code,
                owner_user_id, filters_json, is_default, version,
                created_at, updated_at
            ) VALUES (
                @id, @name, @report_code, @owner_user_id,
                @filters::jsonb, @is_default, 1, NOW(), NOW()
            )
            ON CONFLICT (enterprise_report_saved_view_id)
            DO UPDATE SET
                view_name = EXCLUDED.view_name,
                report_code = EXCLUDED.report_code,
                filters_json = EXCLUDED.filters_json,
                is_default = EXCLUDED.is_default,
                version = enterprise_report_saved_views.version + 1,
                updated_at = NOW()
            WHERE enterprise_report_saved_views.owner_user_id = @owner_user_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("report_code", reportCode);
        command.Parameters.AddWithValue("owner_user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("filters", JsonSerializer.Serialize(request.Filters ?? new EnterpriseReportRequest(
            reportCode, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, 500, false)));
        command.Parameters.AddWithValue("is_default", request.IsDefault == true);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 0)
            throw new UnauthorizedAccessException("The saved report view is outside the current user's scope.");
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    internal static async Task<bool> DeleteSavedViewAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        Guid savedViewId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            DELETE FROM enterprise_report_saved_views
            WHERE enterprise_report_saved_view_id = @id
              AND owner_user_id = @owner_user_id;
            """, connection);
        command.Parameters.AddWithValue("id", savedViewId);
        command.Parameters.AddWithValue("owner_user_id", actor.EffectiveUserId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static EnterpriseReportRunRecord ReadRun(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetGuid(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6),
        ParseJson(reader.GetString(7)), ParseJson(reader.GetString(8)),
        ParseJson(reader.GetString(9)), ParseJson(reader.GetString(10)),
        ParseJson(reader.GetString(11)), DateTimeOffsetValue(reader, 12),
        DateTimeOffsetValue(reader, 13), DateTimeOffsetValue(reader, 14));

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    private static DateTimeOffset DateTimeOffsetValue(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static string Clean(string? value, int maximum)
    {
        var clean = (value ?? string.Empty).Replace('\0', ' ').Trim();
        return clean.Length <= maximum ? clean : clean[..maximum];
    }
}

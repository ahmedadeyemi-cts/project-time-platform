using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class FinancialOperationsSourceLoader
{
    internal static async Task<FinancialSupplementalData> LoadAsync(
        FinancialOperationsTruthSnapshot truth,
        CancellationToken cancellationToken)
    {
        var projectIds = truth.Projects.Select(project => project.ProjectId).ToArray();
        var connectionString = ProjectFinancialTruthModule.FinancialOperationsConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var unavailable = new[]
            {
                Unavailable("approved_time_entries", "Approved project time", true, "DATABASE_CONFIGURATION_UNAVAILABLE"),
                Unavailable("billing_readiness_reviews", "Billing-readiness reviews", false, "DATABASE_CONFIGURATION_UNAVAILABLE"),
                Unavailable("project_closeout_records", "Project closeout records", false, "DATABASE_CONFIGURATION_UNAVAILABLE"),
                Unavailable("project_notification_dispatches", "Group 4 notification delivery", false, "DATABASE_CONFIGURATION_UNAVAILABLE")
            };
            return new(
                new Dictionary<Guid, FinancialApprovedTime>(),
                new Dictionary<Guid, FinancialBillingReadiness>(),
                new Dictionary<Guid, FinancialCloseoutState>(),
                Array.Empty<FinancialNotificationState>(),
                unavailable);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            var code = Diagnostic(exception);
            var unavailable = new[]
            {
                Unavailable("approved_time_entries", "Approved project time", true, code),
                Unavailable("billing_readiness_reviews", "Billing-readiness reviews", false, code),
                Unavailable("project_closeout_records", "Project closeout records", false, code),
                Unavailable("project_notification_dispatches", "Group 4 notification delivery", false, code)
            };
            return new(
                new Dictionary<Guid, FinancialApprovedTime>(),
                new Dictionary<Guid, FinancialBillingReadiness>(),
                new Dictionary<Guid, FinancialCloseoutState>(),
                Array.Empty<FinancialNotificationState>(),
                unavailable);
        }

        var approved = await TryLoadAsync(
            "approved_time_entries",
            "Approved project time",
            true,
            () => LoadApprovedTimeAsync(connection, projectIds, cancellationToken));
        var billing = await TryLoadAsync(
            "billing_readiness_reviews",
            "Billing-readiness reviews",
            false,
            () => LoadBillingReadinessAsync(connection, projectIds, cancellationToken));
        var closeout = await TryLoadAsync(
            "project_closeout_records",
            "Project closeout records",
            false,
            () => LoadCloseoutAsync(connection, projectIds, cancellationToken));
        var notifications = await TryLoadAsync(
            "project_notification_dispatches",
            "Group 4 notification delivery",
            false,
            () => LoadNotificationsAsync(connection, projectIds, cancellationToken));

        return new(
            approved.Value.ToDictionary(item => item.ProjectId),
            billing.Value.ToDictionary(item => item.ProjectId),
            closeout.Value.ToDictionary(item => item.ProjectId),
            notifications.Value.ToArray(),
            new[] { approved.State, billing.State, closeout.State, notifications.State });
    }

    private static async Task<LoadResult<T>> TryLoadAsync<T>(
        string key,
        string name,
        bool required,
        Func<Task<List<T>>> loader)
    {
        try
        {
            var value = await loader();
            return new(
                value,
                new FinancialOperationsSourceState(
                    key,
                    name,
                    "healthy",
                    required,
                    "The source loaded successfully.",
                    "",
                    value.Count,
                    DateTimeOffset.UtcNow,
                    $"/api/financial-operations/sources/{Uri.EscapeDataString(key)}/retry"));
        }
        catch (Exception exception)
        {
            return new(
                new List<T>(),
                Unavailable(key, name, required, Diagnostic(exception)));
        }
    }

    private static FinancialOperationsSourceState Unavailable(
        string key,
        string name,
        bool required,
        string diagnosticCode) => new(
            key,
            name,
            "unavailable",
            required,
            "This source is unavailable. Other healthy financial content remains visible and can be retried independently.",
            diagnosticCode,
            0,
            DateTimeOffset.UtcNow,
            $"/api/financial-operations/sources/{Uri.EscapeDataString(key)}/retry");

    private static async Task<List<FinancialApprovedTime>> LoadApprovedTimeAsync(
        NpgsqlConnection connection,
        Guid[] projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<FinancialApprovedTime>();
        if (projectIds.Length == 0) return rows;

        await using var command = new NpgsqlCommand("""
            SELECT project_id,
                   COALESCE(SUM(hours), 0)::numeric,
                   COUNT(*)::integer
            FROM time_entries
            WHERE project_id = ANY(@project_ids)
              AND lower(COALESCE(status, '')) IN (
                  'pm_approved',
                  'manager_approved',
                  'project_approved',
                  'project_validated',
                  'accounting_ready',
                  'reconciled',
                  'locked'
              )
            GROUP BY project_id;
            """, connection);
        AddProjectIds(command, projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FinancialApprovedTime(
                reader.GetGuid(0),
                reader.GetDecimal(1),
                reader.GetInt32(2),
                null,
                null));
        }
        return rows;
    }

    private static async Task<List<FinancialBillingReadiness>> LoadBillingReadinessAsync(
        NpgsqlConnection connection,
        Guid[] projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<FinancialBillingReadiness>();
        if (projectIds.Length == 0) return rows;

        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT ON (project_id)
                   work_billing_readiness_review_id,
                   project_id,
                   billing_period_start,
                   billing_period_end,
                   COALESCE(package_type, ''),
                   COALESCE(review_status, 'draft'),
                   COALESCE(evidence_source_type, ''),
                   COALESCE(evidence_description, ''),
                   evidence_amount,
                   reviewed_by_user_id,
                   updated_at
            FROM work_billing_readiness_reviews
            WHERE project_id = ANY(@project_ids)
            ORDER BY project_id, updated_at DESC;
            """, connection);
        AddProjectIds(command, projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FinancialBillingReadiness(
                reader.GetGuid(0),
                reader.GetGuid(1),
                DateOnlyValue(reader, 2),
                DateOnlyValue(reader, 3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                DateTimeOffsetValue(reader, 10)));
        }
        return rows;
    }

    private static async Task<List<FinancialCloseoutState>> LoadCloseoutAsync(
        NpgsqlConnection connection,
        Guid[] projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<FinancialCloseoutState>();
        if (projectIds.Length == 0) return rows;

        await using var command = new NpgsqlCommand("""
            SELECT project_id,
                   COALESCE(closeout_status, 'not_started'),
                   COALESCE(prior_project_status, ''),
                   COALESCE(billing_disposition, ''),
                   COALESCE(reason, ''),
                   requested_by_user_id,
                   closed_by_user_id,
                   requested_at,
                   closed_at,
                   updated_at
            FROM work_closeout_records
            WHERE project_id = ANY(@project_ids);
            """, connection);
        AddProjectIds(command, projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FinancialCloseoutState(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : DateTimeOffsetValue(reader, 7),
                reader.IsDBNull(8) ? null : DateTimeOffsetValue(reader, 8),
                DateTimeOffsetValue(reader, 9)));
        }
        return rows;
    }

    private static async Task<List<FinancialNotificationState>> LoadNotificationsAsync(
        NpgsqlConnection connection,
        Guid[] projectIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<FinancialNotificationState>();
        if (projectIds.Length == 0) return rows;

        await using var command = new NpgsqlCommand("""
            SELECT dispatch.project_notification_dispatch_id,
                   dispatch.project_id,
                   dispatch.notification_type,
                   dispatch.alert_severity,
                   dispatch.source_module,
                   dispatch.source_status,
                   dispatch.delivery_boundary,
                   dispatch.delivery_status,
                   COUNT(recipient.project_notification_dispatch_recipient_id)::integer,
                   dispatch.last_error_code,
                   dispatch.last_error_message,
                   dispatch.created_at,
                   dispatch.sent_at
            FROM project_notification_dispatches dispatch
            LEFT JOIN project_notification_dispatch_recipients recipient
              ON recipient.project_notification_dispatch_id = dispatch.project_notification_dispatch_id
            WHERE dispatch.project_id = ANY(@project_ids)
            GROUP BY dispatch.project_notification_dispatch_id
            ORDER BY dispatch.created_at DESC
            LIMIT 500;
            """, connection);
        AddProjectIds(command, projectIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FinancialNotificationState(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetString(9),
                reader.GetString(10),
                DateTimeOffsetValue(reader, 11),
                reader.IsDBNull(12) ? null : DateTimeOffsetValue(reader, 12)));
        }
        return rows;
    }

    private static void AddProjectIds(NpgsqlCommand command, Guid[] projectIds) =>
        command.Parameters.Add(new NpgsqlParameter(
            "project_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = projectIds
        });

    private static DateOnly DateOnlyValue(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? "")
        };
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

    internal static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres when postgres.SqlState == "42P01" =>
            "SOURCE_TABLE_NOT_AVAILABLE",
        PostgresException postgres => $"POSTGRES_{postgres.SqlState}",
        TimeoutException => "SOURCE_TIMEOUT",
        OperationCanceledException => "SOURCE_CANCELLED",
        _ => exception.GetType().Name
    };

    private sealed record LoadResult<T>(
        List<T> Value,
        FinancialOperationsSourceState State);
}

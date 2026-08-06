using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 008 unified Audit and History read model. It discovers existing
/// audit/history/event/log/outbox/sync sources, normalizes sanitized records,
/// and writes best-effort immutable API lifecycle evidence when migration 048
/// is available.
/// </summary>
public static class AdminAuditHistoryModule
{
    private const string ModuleNumber = "008";
    private const string CentralAuditTable = "projectpulse_system_audit_events";
    private const int MaximumResultLimit = 1000;
    private const int MaximumSourceTables = 60;
    private const int MaximumDetailDepth = 4;
    private const int MaximumDetailFields = 80;
    private const int MaximumStringLength = 2000;

    private static readonly string[] ExplicitSourceTables =
    [
        "audit_logs",
        "auth_login_attempts",
        "auth_login_events",
        "auth_sessions",
        "auth_password_reset_requests",
        "azure_sync_runs",
        "notification_log",
        "notification_outbox",
        "email_notification_outbox",
        "projectpulse_module_availability_audit",
        "scoped_role_policy_audit_events",
        "scoped_approval_stage_events",
        "scoped_time_correction_events",
        "projectpulse_native_admin_document_history",
        "microsoft_integration_audit_events",
        "timesheet_day_statuses",
        "scoped_time_management_events",
        "scoped_time_correction_events",
        "time_workflow_exports",
        "module001_timer_audit_events",
        "ai_capability_route_audit",
        "ai_provider_probe_evidence",
        "pulse_ai_answer_runs",
        "pulse_ai_system_inquiry_runs",
        "pulse_ai_system_tool_events",
        "pulse_ai_retrieval_events",
        "pulse_ai_document_processing_events",
        "project_intake_change_history",
        "work_register_change_history",
        "project_notification_dispatches",
        "project_notification_delivery_attempts",
        "enterprise_notification_event_history",
        "enterprise_notification_run_history",
        "system_email_provider_test_events"
    ];

    private static readonly string[] TimestampCandidates =
    [
        "event_time",
        "occurred_at",
        "logged_at",
        "attempted_at",
        "started_at",
        "completed_at",
        "sent_at",
        "failed_at",
        "decided_at",
        "assigned_at",
        "saved_at",
        "published_at",
        "applied_at",
        "updated_at",
        "created_at"
    ];

    private static readonly string[] EventTypeCandidates =
    [
        "event_type",
        "event_code",
        "action_code",
        "action",
        "operation",
        "activity_type",
        "notification_type",
        "change_type",
        "request_type",
        "approval_stage"
    ];

    private static readonly string[] StatusCandidates =
    [
        "status",
        "outcome_code",
        "outcome",
        "result",
        "delivery_status",
        "notification_status",
        "state",
        "success"
    ];

    private static readonly string[] ActorCandidates =
    [
        "actor_email",
        "performed_by_email",
        "changed_by_email",
        "created_by_email",
        "updated_by_email",
        "reviewer_email",
        "approver_email",
        "user_email",
        "email",
        "actor_user_id",
        "performed_by_user_id",
        "changed_by_user_id",
        "created_by_user_id",
        "updated_by_user_id",
        "user_id"
    ];

    private static readonly string[] TargetCandidates =
    [
        "target_label",
        "target_email",
        "target_id",
        "entity_id",
        "record_id",
        "user_id",
        "project_id",
        "timesheet_id",
        "time_entry_id",
        "module_code",
        "tenant_key",
        "recipient"
    ];

    private static readonly string[] SummaryCandidates =
    [
        "summary",
        "message",
        "reason",
        "details",
        "description",
        "notes",
        "decision_comment",
        "notification_detail",
        "error_message"
    ];

    private static readonly HashSet<string> SensitiveKeys = new(
        new[]
        {
            "secret",
            "secret_value",
            "client_secret",
            "password",
            "password_hash",
            "temporary_password",
            "token",
            "access_token",
            "refresh_token",
            "id_token",
            "authorization",
            "api_key",
            "credential",
            "connection_string",
            "private_key",
            "ciphertext",
            "nonce",
            "authentication_tag",
            "source_file_bytes"
        },
        StringComparer.OrdinalIgnoreCase);

    public static WebApplication MapAdminAuditHistoryEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/admin/audit-history/events",
            (Func<HttpContext, Task<IResult>>)GetEventsAsync);

        var environment = app.Environment.EnvironmentName;
        var loggerFactory = app.Services.GetService<ILoggerFactory>();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(() => RecordLifecycleEventAsync(
                "API_STARTED",
                "success",
                "ProjectPulse API process started.",
                environment,
                loggerFactory));
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                RecordLifecycleEventAsync(
                        "API_STOPPING",
                        "warning",
                        "ProjectPulse API process is stopping.",
                        environment,
                        loggerFactory)
                    .Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Process shutdown must never be blocked by optional audit evidence.
            }
        });

        return app;
    }

    private static async Task<IResult> GetEventsAsync(HttpContext context)
    {
        var access = await AdminExperienceCommon.AuthorizeAsync(
            context,
            allowAuditViewer: true);
        if (access.Failure is not null) return access.Failure;

        var days = BoundedInt(context.Request.Query["days"].FirstOrDefault(), 14, 1, 3650);
        var limit = BoundedInt(context.Request.Query["limit"].FirstOrDefault(), 300, 25, MaximumResultLimit);
        var requestedCategory = NormalizeFilter(context.Request.Query["category"].FirstOrDefault());
        var requestedStatus = NormalizeFilter(context.Request.Query["status"].FirstOrDefault());
        var requestedSource = NormalizeFilter(context.Request.Query["source"].FirstOrDefault());
        var search = (context.Request.Query["search"].FirstOrDefault() ?? string.Empty).Trim();
        var from = DateTimeOffset.UtcNow.AddDays(-days);

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);

        var discoveredTables = await DiscoverAuditTablesAsync(
            connection,
            context.RequestAborted);
        var perSourceLimit = Math.Clamp(
            (int)Math.Ceiling((double)limit / Math.Max(discoveredTables.Count, 1)) + 20,
            20,
            250);

        var events = new List<AuditEventRecord>();
        var sourceStates = new List<object>();

        foreach (var tableName in discoveredTables.Take(MaximumSourceTables))
        {
            try
            {
                var sourceEvents = await ReadSourceEventsAsync(
                    connection,
                    tableName,
                    from,
                    perSourceLimit,
                    context.RequestAborted);
                events.AddRange(sourceEvents);
                sourceStates.Add(new
                {
                    source = tableName,
                    label = Humanize(tableName),
                    status = "available",
                    eventCount = sourceEvents.Count
                });
            }
            catch
            {
                sourceStates.Add(new
                {
                    source = tableName,
                    label = Humanize(tableName),
                    status = "temporarily_unavailable",
                    eventCount = 0
                });
            }
        }

        var filtered = events
            .Where(item => item.EventTime >= from)
            .Where(item => requestedCategory is "all" or "" ||
                item.Category.Equals(requestedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(item => requestedStatus is "all" or "" ||
                item.Status.Equals(requestedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(item => requestedSource is "all" or "" ||
                item.SourceTable.Equals(requestedSource, StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesSearch(item, search))
            .OrderByDescending(item => item.EventTime)
            .ThenBy(item => item.EventId, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        var centralAuditAvailable = discoveredTables.Contains(
            CentralAuditTable,
            StringComparer.OrdinalIgnoreCase);
        var categories = filtered
            .Select(item => item.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = filtered
            .Select(item => item.SourceTable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Audit and History",
            status = "audit_history_loaded",
            observedAt = DateTimeOffset.UtcNow,
            lookbackDays = days,
            requestedLimit = limit,
            returnedCount = filtered.Count,
            discoveredSourceCount = discoveredTables.Count,
            centralAudit = new
            {
                available = centralAuditAvailable,
                immutable = centralAuditAvailable,
                migration = "048_admin_audit_and_manager_team_scope"
            },
            summary = new
            {
                total = filtered.Count,
                success = filtered.Count(item => item.Status == "success"),
                failure = filtered.Count(item => item.Status == "failure"),
                warning = filtered.Count(item => item.Status == "warning"),
                pending = filtered.Count(item => item.Status == "pending"),
                info = filtered.Count(item => item.Status == "info"),
                immutable = filtered.Count(item => item.Immutable)
            },
            categories,
            sources,
            sourceStates,
            events = filtered.Select(item => new
            {
                item.EventId,
                item.EventTime,
                item.Category,
                item.Status,
                item.EventType,
                item.Actor,
                item.Target,
                item.Source,
                item.SourceTable,
                item.SourceRecordId,
                item.Summary,
                item.IpAddress,
                item.CorrelationId,
                item.Immutable,
                item.Details
            })
        });
    }

    private static async Task<List<string>> DiscoverAuditTablesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
              AND (
                    lower(table_name) ~ '(audit|history|event|log|outbox|sync_run|revision)'
                    OR lower(table_name) = ANY(@explicit_tables)
                  )
              AND lower(table_name) NOT LIKE '%secret%'
              AND lower(table_name) NOT LIKE '%credential%'
              AND lower(table_name) NOT IN (
                    'schema_migrations',
                    'time_entries',
                    'timesheets',
                    'app_users',
                    'app_roles',
                    'app_permissions'
                  )
            ORDER BY CASE WHEN table_name = @central_table THEN 0 ELSE 1 END,
                     table_name;
            """, connection);
        command.Parameters.AddWithValue("explicit_tables", ExplicitSourceTables);
        command.Parameters.AddWithValue("central_table", CentralAuditTable);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            if (SafeIdentifier(table)) tables.Add(table);
        }

        return tables;
    }

    private static async Task<List<AuditEventRecord>> ReadSourceEventsAsync(
        NpgsqlConnection connection,
        string tableName,
        DateTimeOffset from,
        int limit,
        CancellationToken cancellationToken)
    {
        var columns = await ReadColumnsAsync(connection, tableName, cancellationToken);
        if (columns.Count == 0) return [];

        var timestampColumn = TimestampCandidates.FirstOrDefault(columns.ContainsKey);
        var timestampType = timestampColumn is null ? string.Empty : columns[timestampColumn];
        var canFilterTimestamp = timestampType.Contains("timestamp", StringComparison.OrdinalIgnoreCase)
            || timestampType.Equals("date", StringComparison.OrdinalIgnoreCase);

        var quotedTable = QuoteIdentifier(tableName);
        var timestampExpression = timestampColumn is null
            ? string.Empty
            : QuoteIdentifier(timestampColumn);
        var whereClause = timestampColumn is not null && canFilterTimestamp
            ? timestampType.Equals("date", StringComparison.OrdinalIgnoreCase)
                ? $"WHERE {timestampExpression} >= @from_date"
                : $"WHERE {timestampExpression} >= @from_time"
            : string.Empty;
        var orderClause = timestampColumn is not null
            ? $"ORDER BY {timestampExpression} DESC NULLS LAST"
            : string.Empty;

        await using var command = new NpgsqlCommand($"""
            SELECT to_jsonb(source)::text
            FROM {quotedTable} source
            {whereClause}
            {orderClause}
            LIMIT @limit;
            """, connection);
        if (timestampColumn is not null && canFilterTimestamp)
        {
            if (timestampType.Equals("date", StringComparison.OrdinalIgnoreCase))
            {
                command.Parameters.AddWithValue("from_date", DateOnly.FromDateTime(from.UtcDateTime));
            }
            else
            {
                command.Parameters.AddWithValue("from_time", from);
            }
        }
        command.Parameters.AddWithValue("limit", limit);

        var events = new List<AuditEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rowIndex = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            rowIndex += 1;
            var raw = reader.IsDBNull(0) ? "{}" : reader.GetString(0);
            using var document = JsonDocument.Parse(raw);
            events.Add(NormalizeEvent(tableName, document.RootElement, raw, rowIndex));
        }

        return events;
    }

    private static async Task<Dictionary<string, string>> ReadColumnsAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table_name
            ORDER BY ordinal_position;
            """, connection);
        command.Parameters.AddWithValue("table_name", tableName);

        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }

    private static AuditEventRecord NormalizeEvent(
        string tableName,
        JsonElement row,
        string raw,
        int rowIndex)
    {
        var eventTime = ParseTimestamp(row) ?? DateTimeOffset.MinValue;
        var eventType = FirstValue(row, EventTypeCandidates);
        if (string.IsNullOrWhiteSpace(eventType)) eventType = Humanize(tableName);
        var rawStatus = FirstValue(row, StatusCandidates);
        var status = NormalizeStatus(rawStatus, row);
        var actor = FirstValue(row, ActorCandidates);
        var target = FirstValue(row, TargetCandidates);
        var summary = FirstValue(row, SummaryCandidates);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = $"{Humanize(eventType)} recorded by {Humanize(tableName)}.";
        }

        var sourceRecordId = FirstValue(row,
        [
            "projectpulse_system_audit_event_id",
            "audit_log_id",
            "event_id",
            "history_id",
            "revision_id",
            "request_id",
            "operation_id",
            "notification_id",
            "sync_run_id",
            "id"
        ]);
        var hashInput = $"{tableName}|{sourceRecordId}|{eventTime:O}|{raw}|{rowIndex}";
        var eventId = string.IsNullOrWhiteSpace(sourceRecordId)
            ? $"{tableName}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..24].ToLowerInvariant()}"
            : $"{tableName}:{sourceRecordId}";

        var category = ClassifyCategory(tableName, eventType);
        var immutable = tableName.Equals(CentralAuditTable, StringComparison.OrdinalIgnoreCase)
            || tableName.Contains("audit", StringComparison.OrdinalIgnoreCase)
            || tableName.Contains("history", StringComparison.OrdinalIgnoreCase);

        return new(
            eventId,
            eventTime,
            category,
            status,
            Humanize(eventType),
            string.IsNullOrWhiteSpace(actor) ? "System / not recorded" : actor,
            string.IsNullOrWhiteSpace(target) ? "Not specified" : target,
            Humanize(tableName),
            tableName,
            sourceRecordId,
            Truncate(summary, MaximumStringLength),
            FirstValue(row, ["ip_address", "client_ip", "remote_ip", "source_ip"]),
            FirstValue(row, ["correlation_id", "trace_id", "trace_identifier", "request_id"]),
            immutable,
            SanitizeObject(row, 0));
    }

    private static DateTimeOffset? ParseTimestamp(JsonElement row)
    {
        foreach (var key in TimestampCandidates)
        {
            if (!TryGetProperty(row, key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string FirstValue(JsonElement row, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetProperty(row, key, out var value)) continue;
            var text = ScalarText(value);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return string.Empty;
    }

    private static bool TryGetProperty(
        JsonElement row,
        string propertyName,
        out JsonElement value)
    {
        if (row.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in row.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ScalarText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static string NormalizeStatus(string rawStatus, JsonElement row)
    {
        var normalized = (rawStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "true" or "ok" or "completed" or "complete" or "success" or "successful" or "sent" or "approved" or "active" or "healthy")
        {
            return "success";
        }

        if (normalized.Contains("fail", StringComparison.Ordinal)
            || normalized.Contains("error", StringComparison.Ordinal)
            || normalized.Contains("denied", StringComparison.Ordinal)
            || normalized.Contains("expired", StringComparison.Ordinal)
            || normalized.Contains("rejected", StringComparison.Ordinal))
        {
            return "failure";
        }

        if (normalized.Contains("warn", StringComparison.Ordinal)
            || normalized.Contains("degrad", StringComparison.Ordinal)
            || normalized.Contains("stop", StringComparison.Ordinal)
            || normalized.Contains("rollback", StringComparison.Ordinal)
            || normalized.Contains("declined", StringComparison.Ordinal))
        {
            return "warning";
        }

        if (normalized.Contains("pending", StringComparison.Ordinal)
            || normalized.Contains("queued", StringComparison.Ordinal)
            || normalized.Contains("await", StringComparison.Ordinal)
            || normalized.Contains("prepared", StringComparison.Ordinal)
            || normalized.Contains("draft", StringComparison.Ordinal))
        {
            return "pending";
        }

        if (TryGetProperty(row, "success", out var successValue))
        {
            if (successValue.ValueKind == JsonValueKind.True) return "success";
            if (successValue.ValueKind == JsonValueKind.False) return "failure";
        }

        return "info";
    }

    private static string ClassifyCategory(string tableName, string eventType)
    {
        var text = $"{tableName} {eventType}".ToLowerInvariant();
        if (text.Contains("password")) return "password_reset";
        if (text.Contains("auth") || text.Contains("login") || text.Contains("session")) return "authentication";
        if (text.Contains("pulse_ai") || text.Contains("celar") || text.Contains("ai_provider") || text.Contains("ai_capability")) return "ai_usage";
        if (text.Contains("service") || text.Contains("restart")) return "service_control";
        if (text.Contains("deploy") || text.Contains("release") || text.Contains("container") || text.Contains("runtime")) return "platform";
        if (text.Contains("azure") || text.Contains("entra") || text.Contains("microsoft") || text.Contains("sync") || text.Contains("integration")) return "integration";
        if (text.Contains("mail") || text.Contains("notification") || text.Contains("outbox")) return "notification";
        if (text.Contains("role") || text.Contains("permission") || text.Contains("policy")) return "authorization";
        if (text.Contains("user") || text.Contains("team") || text.Contains("department")) return "user_administration";
        if (text.Contains("timesheet") || text.Contains("time_") || text.Contains("approval")) return "workflow";
        if (text.Contains("expense") || text.Contains("invoice") || text.Contains("billing")) return "financial";
        if (text.Contains("security") || text.Contains("incident") || text.Contains("threat")) return "security";
        return "system";
    }

    private static object? SanitizeValue(JsonElement value, int depth)
    {
        if (depth >= MaximumDetailDepth) return "[nested value omitted]";

        return value.ValueKind switch
        {
            JsonValueKind.Object => SanitizeObject(value, depth + 1),
            JsonValueKind.Array => value.EnumerateArray()
                .Take(30)
                .Select(item => SanitizeValue(item, depth + 1))
                .ToArray(),
            JsonValueKind.String => Truncate(value.GetString() ?? string.Empty, MaximumStringLength),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => Truncate(value.ToString(), MaximumStringLength)
        };
    }

    private static Dictionary<string, object?> SanitizeObject(JsonElement value, int depth)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (value.ValueKind != JsonValueKind.Object) return result;

        foreach (var property in value.EnumerateObject().Take(MaximumDetailFields))
        {
            result[property.Name] = IsSensitive(property.Name)
                ? "[redacted]"
                : SanitizeValue(property.Value, depth);
        }

        return result;
    }

    private static bool IsSensitive(string key)
    {
        if (SensitiveKeys.Contains(key)) return true;
        var normalized = key.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("connection_string", StringComparison.Ordinal)
            || normalized.Contains("private_key", StringComparison.Ordinal)
            || normalized.Contains("api_key", StringComparison.Ordinal);
    }

    private static bool MatchesSearch(AuditEventRecord item, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var text = string.Join(' ',
            item.EventType,
            item.Actor,
            item.Target,
            item.Source,
            item.SourceTable,
            item.SourceRecordId,
            item.Summary,
            item.CorrelationId,
            JsonSerializer.Serialize(item.Details));
        return text.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RecordLifecycleEventAsync(
        string eventType,
        string status,
        string summary,
        string environment,
        ILoggerFactory? loggerFactory)
    {
        var connectionString = AdminExperienceCommon.ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var revision = FirstEnvironment(
                "CONTAINER_APP_REVISION",
                "K_REVISION",
                "PROJECTPULSE_API_REVISION");
            var replica = FirstEnvironment(
                "CONTAINER_APP_REPLICA_NAME",
                "HOSTNAME");
            await AdminExperienceCommon.WriteAuditAsync(
                connection,
                null,
                "platform",
                status,
                eventType,
                null,
                "system",
                "api_runtime",
                revision,
                string.IsNullOrWhiteSpace(revision) ? "ProjectPulse API" : revision,
                ModuleNumber,
                CentralAuditTable,
                revision,
                summary,
                new
                {
                    environment,
                    revision,
                    replica,
                    sourceCommit = FirstEnvironment("PROJECTPULSE_SOURCE_COMMIT", "SOURCE_COMMIT"),
                    processId = Environment.ProcessId
                },
                string.Empty,
                $"lifecycle-{Environment.ProcessId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        }
        catch (Exception exception)
        {
            loggerFactory?
                .CreateLogger("AdminAuditHistoryModule")
                .LogDebug(
                    "Module 008 lifecycle audit evidence was unavailable ({ExceptionType}).",
                    exception.GetType().Name);
        }
    }

    private static string FirstEnvironment(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return string.Empty;
    }

    private static int BoundedInt(string? raw, int fallback, int minimum, int maximum) =>
        int.TryParse(raw, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static string NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "all"
            : value.Trim().ToLowerInvariant();

    private static bool SafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static string QuoteIdentifier(string value)
    {
        if (!SafeIdentifier(value)) throw new InvalidOperationException("Unsafe database identifier.");
        return $"\"{value}\"";
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "System event";
        var normalized = value
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();
        return string.Join(' ', normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum
            ? value
            : value[..maximum] + "…";

    private sealed record AuditEventRecord(
        string EventId,
        DateTimeOffset EventTime,
        string Category,
        string Status,
        string EventType,
        string Actor,
        string Target,
        string Source,
        string SourceTable,
        string SourceRecordId,
        string Summary,
        string IpAddress,
        string CorrelationId,
        bool Immutable,
        IReadOnlyDictionary<string, object?> Details);
}

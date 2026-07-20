using System.Globalization;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class Module071072NativePersistence
{
    internal sealed record Outcome(JsonNode? Payload, IResult? Failure);

    internal static async Task<Outcome> ReadOnCallScheduleAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("071", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT schedule_json::text
                FROM projectpulse_oncall_schedule_versions
                WHERE is_current = TRUE
                ORDER BY saved_at DESC
                LIMIT 1;
                """, connection);

            var raw = await command.ExecuteScalarAsync() as string;
            var schedule = string.IsNullOrWhiteSpace(raw)
                ? DefaultSchedule()
                : JsonNode.Parse(raw) ?? DefaultSchedule();
            return new(schedule, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "071", "load native on-call schedule");
            return Unavailable("071", "The native on-call schedule is unavailable. Migration 031 may be pending.");
        }
    }

    internal static async Task<Outcome> ReadOnCallRosterAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("071", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    r.department_code,
                    r.user_id,
                    COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                    u.email,
                    COALESCE(r.routing_phone, '') AS routing_phone,
                    COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), 'Unassigned') AS team_name,
                    COALESCE(NULLIF(u.department_name, ''), NULLIF(u.department, ''), NULLIF(u.team_name, ''), 'Unassigned') AS department_name
                FROM projectpulse_oncall_roster_members r
                JOIN app_users u
                  ON u.user_id = r.user_id
                 AND u.is_active = TRUE
                WHERE r.is_active = TRUE
                ORDER BY r.department_code, r.sort_order, display_name;
                """, connection);

            var roster = new JsonObject();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var department = reader.GetString(0);
                if (roster[department] is not JsonArray people)
                {
                    people = new JsonArray();
                    roster[department] = people;
                }

                people.Add(new JsonObject
                {
                    ["userId"] = reader.GetGuid(1).ToString(),
                    ["name"] = reader.GetString(2),
                    ["email"] = reader.GetString(3),
                    ["phone"] = reader.GetString(4),
                    ["teamName"] = reader.GetString(5),
                    ["departmentName"] = reader.GetString(6)
                });
            }

            return new(roster, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "071", "load native on-call roster");
            return Unavailable("071", "The native on-call roster is unavailable. Migration 031 may be pending.");
        }
    }

    internal static async Task<Outcome> ReadOnCallHistoryAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("071", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    schedule_version_id,
                    revision_number,
                    entries_count,
                    saved_by,
                    saved_at,
                    change_reason,
                    schedule_json::text,
                    is_current
                FROM projectpulse_oncall_schedule_versions
                ORDER BY saved_at DESC
                LIMIT 100;
                """, connection);

            var history = new JsonArray();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                history.Add(new JsonObject
                {
                    ["id"] = reader.GetGuid(0).ToString(),
                    ["revision"] = reader.GetInt64(1),
                    ["entriesCount"] = reader.GetInt32(2),
                    ["savedBy"] = reader.IsDBNull(3) ? null : reader.GetGuid(3).ToString(),
                    ["savedAt"] = reader.GetFieldValue<DateTimeOffset>(4).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["reason"] = reader.GetString(5),
                    ["schedule"] = JsonNode.Parse(reader.GetString(6)),
                    ["isCurrent"] = reader.GetBoolean(7)
                });
            }

            return new(history, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "071", "load native on-call history");
            return Unavailable("071", "The native on-call history is unavailable. Migration 031 may be pending.");
        }
    }

    internal static async Task<Outcome> SaveOnCallScheduleAsync(JsonNode payload, Guid actorUserId, HttpContext context)
    {
        var schedule = payload["schedule"] ?? payload;
        if (schedule is not JsonObject)
        {
            return new(null, Results.BadRequest(new { module = "071", status = "invalid_schedule" }));
        }

        var entriesCount = schedule["entries"] is JsonArray entries ? entries.Count : 0;
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("071", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var clearCurrent = new NpgsqlCommand("""
                UPDATE projectpulse_oncall_schedule_versions
                SET is_current = FALSE
                WHERE is_current = TRUE;
                """, connection, transaction))
            {
                await clearCurrent.ExecuteNonQueryAsync();
            }

            var versionId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO projectpulse_oncall_schedule_versions
                (
                    schedule_version_id,
                    schedule_json,
                    entries_count,
                    is_current,
                    saved_by,
                    change_reason
                )
                VALUES
                (
                    @version_id,
                    CAST(@schedule_json AS jsonb),
                    @entries_count,
                    TRUE,
                    @saved_by,
                    'schedule_saved'
                );
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("version_id", versionId);
                insert.Parameters.AddWithValue("schedule_json", schedule.ToJsonString());
                insert.Parameters.AddWithValue("entries_count", entriesCount);
                insert.Parameters.AddWithValue("saved_by", actorUserId);
                await insert.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(
                connection,
                transaction,
                "071",
                "schedule",
                versionId.ToString(),
                "save",
                actorUserId,
                new JsonObject { ["entriesCount"] = entriesCount });

            await transaction.CommitAsync();
            return new(new JsonObject
            {
                ["scheduleVersionId"] = versionId.ToString(),
                ["entriesCount"] = entriesCount,
                ["persistence"] = "projectpulse_postgresql"
            }, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "071", "save native on-call schedule");
            return Unavailable("071", "The native on-call schedule could not be saved.");
        }
    }

    internal static async Task<Outcome> SaveOnCallRosterAsync(JsonNode payload, Guid actorUserId, HttpContext context)
    {
        var roster = payload["roster"] as JsonObject ?? payload as JsonObject;
        if (roster is null)
        {
            return new(null, Results.BadRequest(new { module = "071", status = "invalid_roster" }));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("071", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var clear = new NpgsqlCommand("DELETE FROM projectpulse_oncall_roster_members;", connection, transaction))
            {
                await clear.ExecuteNonQueryAsync();
            }

            var memberCount = 0;
            foreach (var (departmentName, value) in roster)
            {
                var department = NormalizeDepartment(departmentName);
                if (string.IsNullOrWhiteSpace(department) || value is not JsonArray people) continue;

                for (var index = 0; index < people.Count; index++)
                {
                    if (people[index] is not JsonObject person) continue;
                    if (!Guid.TryParse(NodeText(person["userId"]), out var userId)) continue;

                    var phone = NodeText(person["phone"])?.Trim() ?? string.Empty;
                    if (phone.Length > 50) phone = phone[..50];

                    await using var insert = new NpgsqlCommand("""
                        INSERT INTO projectpulse_oncall_roster_members
                        (
                            department_code,
                            user_id,
                            routing_phone,
                            sort_order,
                            is_active,
                            updated_by,
                            updated_at
                        )
                        VALUES
                        (
                            @department,
                            @user_id,
                            @phone,
                            @sort_order,
                            TRUE,
                            @updated_by,
                            now()
                        );
                        """, connection, transaction);
                    insert.Parameters.AddWithValue("department", department);
                    insert.Parameters.AddWithValue("user_id", userId);
                    insert.Parameters.AddWithValue("phone", phone);
                    insert.Parameters.AddWithValue("sort_order", index);
                    insert.Parameters.AddWithValue("updated_by", actorUserId);
                    await insert.ExecuteNonQueryAsync();
                    memberCount++;
                }
            }

            await InsertAuditAsync(
                connection,
                transaction,
                "071",
                "roster",
                "current",
                "replace",
                actorUserId,
                new JsonObject
                {
                    ["memberCount"] = memberCount,
                    ["departmentCount"] = roster.Count
                });

            await transaction.CommitAsync();
            return new(new JsonObject
            {
                ["memberCount"] = memberCount,
                ["departmentCount"] = roster.Count,
                ["persistence"] = "projectpulse_postgresql"
            }, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "071", "save native on-call roster");
            return Unavailable("071", "The native on-call roster could not be saved.");
        }
    }

    internal static async Task<Outcome> RestoreOnCallScheduleAsync(string snapshotId, Guid actorUserId, HttpContext context)
    {
        if (!Guid.TryParse(snapshotId, out var sourceVersionId))
        {
            return new(null, Results.BadRequest(new { module = "071", status = "invalid_snapshot_id" }));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("071", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            string scheduleJson;
            int entriesCount;
            await using (var select = new NpgsqlCommand("""
                SELECT schedule_json::text, entries_count
                FROM projectpulse_oncall_schedule_versions
                WHERE schedule_version_id = @version_id;
                """, connection, transaction))
            {
                select.Parameters.AddWithValue("version_id", sourceVersionId);
                await using var reader = await select.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return new(null, Results.NotFound(new { module = "071", status = "snapshot_not_found" }));
                }
                scheduleJson = reader.GetString(0);
                entriesCount = reader.GetInt32(1);
            }

            await using (var clearCurrent = new NpgsqlCommand("""
                UPDATE projectpulse_oncall_schedule_versions
                SET is_current = FALSE
                WHERE is_current = TRUE;
                """, connection, transaction))
            {
                await clearCurrent.ExecuteNonQueryAsync();
            }

            var restoredVersionId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO projectpulse_oncall_schedule_versions
                (
                    schedule_version_id,
                    schedule_json,
                    entries_count,
                    is_current,
                    saved_by,
                    restored_from_schedule_version_id,
                    change_reason
                )
                VALUES
                (
                    @version_id,
                    CAST(@schedule_json AS jsonb),
                    @entries_count,
                    TRUE,
                    @saved_by,
                    @restored_from,
                    'history_restored'
                );
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("version_id", restoredVersionId);
                insert.Parameters.AddWithValue("schedule_json", scheduleJson);
                insert.Parameters.AddWithValue("entries_count", entriesCount);
                insert.Parameters.AddWithValue("saved_by", actorUserId);
                insert.Parameters.AddWithValue("restored_from", sourceVersionId);
                await insert.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(
                connection,
                transaction,
                "071",
                "schedule",
                restoredVersionId.ToString(),
                "restore",
                actorUserId,
                new JsonObject { ["restoredFrom"] = sourceVersionId.ToString() });

            await transaction.CommitAsync();
            return new(new JsonObject
            {
                ["scheduleVersionId"] = restoredVersionId.ToString(),
                ["restoredFrom"] = sourceVersionId.ToString(),
                ["entriesCount"] = entriesCount
            }, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "071", "restore native on-call history");
            return Unavailable("071", "The native on-call snapshot could not be restored.");
        }
    }

    internal static async Task<Outcome> ReadOneAssistRoutesAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("072", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT route_id, customer_name, routing_pin
                FROM projectpulse_oneassist_routes
                WHERE is_active = TRUE
                ORDER BY sort_order, lower(customer_name), routing_pin;
                """, connection);

            var routes = new JsonArray();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                routes.Add(new JsonObject
                {
                    ["id"] = reader.GetString(0),
                    ["name"] = reader.GetString(1),
                    ["pin"] = reader.GetString(2)
                });
            }
            return new(routes, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "072", "load native OneAssist routes");
            return Unavailable("072", "The native OneAssist directory is unavailable. Migration 031 may be pending.");
        }
    }

    internal static async Task<Outcome> SaveOneAssistRoutesAsync(JsonNode payload, Guid actorUserId, HttpContext context)
    {
        var routes = payload["customers"] as JsonArray
            ?? payload["routes"] as JsonArray
            ?? payload as JsonArray;
        if (routes is null)
        {
            return new(null, Results.BadRequest(new { module = "072", status = "invalid_routes" }));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unavailable("072", "ProjectPulse database configuration is unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var archive = new NpgsqlCommand("""
                UPDATE projectpulse_oneassist_routes
                SET is_active = FALSE, updated_by = @actor, updated_at = now()
                WHERE is_active = TRUE;
                """, connection, transaction))
            {
                archive.Parameters.AddWithValue("actor", actorUserId);
                await archive.ExecuteNonQueryAsync();
            }

            var activeCount = 0;
            for (var index = 0; index < routes.Count; index++)
            {
                if (routes[index] is not JsonObject route) continue;
                var routeId = NodeText(route["id"])?.Trim();
                var customerName = NodeText(route["name"])?.Trim();
                var pin = NodeText(route["pin"])?.Trim();
                if (string.IsNullOrWhiteSpace(routeId)) routeId = Guid.NewGuid().ToString();
                if (routeId.Length > 100
                    || string.IsNullOrWhiteSpace(customerName)
                    || customerName.Length > 200
                    || pin is not { Length: 5 }
                    || !pin.All(char.IsAsciiDigit))
                {
                    return new(null, Results.BadRequest(new { module = "072", status = "invalid_route", row = index + 1 }));
                }

                await using var upsert = new NpgsqlCommand("""
                    INSERT INTO projectpulse_oneassist_routes
                    (
                        route_id,
                        customer_name,
                        routing_pin,
                        sort_order,
                        is_active,
                        created_by,
                        created_at,
                        updated_by,
                        updated_at
                    )
                    VALUES
                    (
                        @route_id,
                        @customer_name,
                        @routing_pin,
                        @sort_order,
                        TRUE,
                        @actor,
                        now(),
                        @actor,
                        now()
                    )
                    ON CONFLICT (route_id)
                    DO UPDATE SET
                        customer_name = EXCLUDED.customer_name,
                        routing_pin = EXCLUDED.routing_pin,
                        sort_order = EXCLUDED.sort_order,
                        is_active = TRUE,
                        updated_by = EXCLUDED.updated_by,
                        updated_at = now();
                    """, connection, transaction);
                upsert.Parameters.AddWithValue("route_id", routeId);
                upsert.Parameters.AddWithValue("customer_name", customerName);
                upsert.Parameters.AddWithValue("routing_pin", pin);
                upsert.Parameters.AddWithValue("sort_order", index);
                upsert.Parameters.AddWithValue("actor", actorUserId);
                await upsert.ExecuteNonQueryAsync();
                activeCount++;
            }

            var revisionId = Guid.NewGuid();
            await using (var revision = new NpgsqlCommand("""
                INSERT INTO projectpulse_oneassist_route_revisions
                (revision_id, routes_json, route_count, saved_by)
                VALUES (@revision_id, CAST(@routes_json AS jsonb), @route_count, @saved_by);
                """, connection, transaction))
            {
                revision.Parameters.AddWithValue("revision_id", revisionId);
                revision.Parameters.AddWithValue("routes_json", routes.ToJsonString());
                revision.Parameters.AddWithValue("route_count", activeCount);
                revision.Parameters.AddWithValue("saved_by", actorUserId);
                await revision.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(
                connection,
                transaction,
                "072",
                "routing_directory",
                revisionId.ToString(),
                "replace",
                actorUserId,
                new JsonObject { ["routeCount"] = activeCount });

            await transaction.CommitAsync();
            return new(new JsonObject
            {
                ["revisionId"] = revisionId.ToString(),
                ["routeCount"] = activeCount,
                ["persistence"] = "projectpulse_postgresql"
            }, null);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            LogFailure(context, exception, "072", "save duplicate OneAssist route");
            return new(null, Results.BadRequest(new
            {
                module = "072",
                status = "duplicate_routing_pin",
                message = "Every active OneAssist routing PIN must remain unique."
            }));
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "072", "save native OneAssist routes");
            return Unavailable("072", "The native OneAssist directory could not be saved.");
        }
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string moduleNumber,
        string entityType,
        string entityId,
        string actionCode,
        Guid actorUserId,
        JsonObject evidence)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO projectpulse_module_audit_events
            (
                event_id,
                module_number,
                entity_type,
                entity_id,
                action_code,
                actor_user_id,
                evidence_json
            )
            VALUES
            (
                @event_id,
                @module_number,
                @entity_type,
                @entity_id,
                @action_code,
                @actor_user_id,
                CAST(@evidence_json AS jsonb)
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", Guid.NewGuid());
        command.Parameters.AddWithValue("module_number", moduleNumber);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("action_code", actionCode);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("evidence_json", evidence.ToJsonString());
        await command.ExecuteNonQueryAsync();
    }

    private static JsonObject DefaultSchedule() => new()
    {
        ["version"] = 1,
        ["tz"] = "America/Chicago",
        ["updatedAt"] = null,
        ["entries"] = new JsonArray()
    };

    private static string NormalizeDepartment(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

    private static string? NodeText(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        return node?.ToString();
    }

    private static Outcome Unavailable(string module, string message) =>
        new(null, Results.Json(new
        {
            module,
            status = "native_persistence_unavailable",
            message
        }, statusCode: StatusCodes.Status503ServiceUnavailable));

    private static void LogFailure(HttpContext context, Exception exception, string module, string operation)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Module071072NativePersistence")
            .LogWarning(exception, "Module {Module} could not {Operation}.", module, operation);
    }

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
            MaxPoolSize = 5
        }.ConnectionString;
    }
}

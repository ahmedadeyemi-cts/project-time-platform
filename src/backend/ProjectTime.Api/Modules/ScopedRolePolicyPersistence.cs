using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<ActorContext?> LoadActorAsync(
        HttpContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null)
    {
        var actualUserId = ReadGuid(
            context.Items["ProjectPulseActualUserId"]
            ?? context.Items["ProjectPulseSessionUserId"]);
        var effectiveUserId = ReadGuid(
            context.Items["ProjectPulseEffectiveUserId"]
            ?? context.Items["ProjectPulseSessionUserId"]);
        if (actualUserId is null || effectiveUserId is null) return null;

        var roleCodes = await LoadCanonicalRoleCodesAsync(
            connection,
            transaction,
            effectiveUserId.Value);
        var email = Convert.ToString(context.Items["ProjectPulseSessionEmail"])
            ?? string.Empty;
        var isViewAs = actualUserId.Value != effectiveUserId.Value
            || context.Request.Headers.ContainsKey("X-ProjectPulse-View-As-User");

        return new ActorContext(
            actualUserId.Value,
            effectiveUserId.Value,
            email,
            roleCodes,
            isViewAs,
            roleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<string[]> LoadCanonicalRoleCodesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT r.role_code
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(CanonicalRole(reader.GetString(0)));
        }
        return roles.ToArray();
    }

    private static async Task<(ActorContext? Actor, IResult? Error)>
        RequireOwnSessionSuperAdministratorAsync(
            HttpContext context,
            NpgsqlConnection connection)
    {
        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return (null, SessionRequired());
        if (actor.IsViewAs)
        {
            return (null, Results.Json(new
            {
                status = "view_as_read_only",
                message = "Module 012 policy writes are disabled while using View-As."
            }, statusCode: StatusCodes.Status403Forbidden));
        }
        if (!actor.IsSuperAdministrator)
        {
            return (null, Results.Json(new
            {
                status = "super_administrator_required",
                message = "Only an authenticated Super Administrator in their own session may change scoped role policy."
            }, statusCode: StatusCodes.Status403Forbidden));
        }
        return (actor, null);
    }

    private static async Task<int> CountActiveSuperAdministratorsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(DISTINCT u.user_id)
            FROM app_users u
            JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id AND ura.is_active = TRUE
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
            WHERE u.is_active = TRUE
              AND upper(r.role_code) IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR');
            """, connection, transaction);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<PolicyVersionRow?> LoadPublishedVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            SELECT policy_version_id, version_number, policy_name,
                   policy_status, source_name, source_sha256,
                   policy_notes, restored_from_policy_version_id,
                   created_at, published_at, retired_at
            FROM scoped_role_policy_versions
            WHERE policy_status = 'PUBLISHED'
            ORDER BY version_number DESC
            LIMIT 1;
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadVersion(reader) : null;
    }

    private static async Task<PolicyVersionRow?> LoadVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid policyVersionId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT policy_version_id, version_number, policy_name,
                   policy_status, source_name, source_sha256,
                   policy_notes, restored_from_policy_version_id,
                   created_at, published_at, retired_at
            FROM scoped_role_policy_versions
            WHERE policy_version_id = @policy_version_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_version_id", policyVersionId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadVersion(reader) : null;
    }

    private static PolicyVersionRow ReadVersion(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));

    private static async Task InsertVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid policyVersionId,
        int versionNumber,
        string policyName,
        string status,
        string sourceName,
        string sourceSha256,
        string notes,
        Guid actorUserId,
        Guid? restoredFrom)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO scoped_role_policy_versions (
                policy_version_id, version_number, policy_name,
                policy_status, source_name, source_sha256, policy_notes,
                created_by_user_id, restored_from_policy_version_id
            )
            VALUES (
                @policy_version_id, @version_number, @policy_name,
                @policy_status, @source_name, @source_sha256, @policy_notes,
                @actor_user_id, @restored_from_policy_version_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_version_id", policyVersionId);
        command.Parameters.AddWithValue("version_number", versionNumber);
        command.Parameters.AddWithValue("policy_name", policyName);
        command.Parameters.AddWithValue("policy_status", status);
        command.Parameters.AddWithValue("source_name", sourceName);
        command.Parameters.AddWithValue("source_sha256", sourceSha256);
        command.Parameters.AddWithValue("policy_notes", notes);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue(
            "restored_from_policy_version_id",
            (object?)restoredFrom ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid policyVersionId,
        string eventCode,
        ActorContext actor,
        string reason,
        JsonElement previousState,
        JsonElement newState)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO scoped_role_policy_audit_events (
                policy_version_id, event_code, actor_user_id,
                actor_email, reason, previous_state, new_state,
                event_metadata
            )
            VALUES (
                @policy_version_id, @event_code, @actor_user_id,
                @actor_email, @reason, @previous_state::jsonb,
                @new_state::jsonb, @event_metadata::jsonb
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_version_id", policyVersionId);
        command.Parameters.AddWithValue("event_code", eventCode);
        command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("actor_email", actor.Email);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("previous_state", previousState.GetRawText());
        command.Parameters.AddWithValue("new_state", newState.GetRawText());
        command.Parameters.AddWithValue(
            "event_metadata",
            JsonSerializer.Serialize(new
            {
                actor.ActualUserId,
                actor.EffectiveUserId,
                actor.RoleCodes,
                actor.IsViewAs,
                immutableAudit = true
            }));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(JsonElement Payload, string Status)?>
        LoadTimeCorrectionTargetAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid timesheetId,
            DateOnly workDate,
            Guid? timeEntryId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(te.time_entry_id, '00000000-0000-0000-0000-000000000000'::uuid),
                tds.user_id,
                tds.status,
                te.project_id,
                to_jsonb(te)->>'project_task_id',
                te.hours,
                COALESCE(te.description, '')
            FROM timesheet_day_statuses tds
            LEFT JOIN time_entries te
              ON te.timesheet_id = tds.timesheet_id
             AND te.work_date = tds.work_date
             AND (@time_entry_id IS NULL OR te.time_entry_id = @time_entry_id)
            WHERE tds.timesheet_id = @timesheet_id
              AND tds.work_date = @work_date
            ORDER BY te.created_at NULLS LAST
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("time_entry_id", (object?)timeEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var payload = JsonSerializer.SerializeToElement(new
        {
            timeEntryId = reader.GetGuid(0),
            userId = reader.GetGuid(1),
            status = reader.GetString(2),
            projectId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            taskId = reader.IsDBNull(4) ? null : reader.GetString(4),
            hours = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            description = reader.GetString(6)
        });
        return (payload, reader.GetString(2));
    }
}

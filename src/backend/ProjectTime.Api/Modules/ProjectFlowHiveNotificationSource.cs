using System.Text.Json;
using Npgsql;
using static ProjectTime.Api.Modules.ProjectFlowHiveNotificationPolicy;

namespace ProjectTime.Api.Modules;

internal static class ProjectFlowHiveNotificationSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string AssignmentPolicy = "FLOWHIVE_TASK_ASSIGNED";
    internal const string DuePolicy = "FLOWHIVE_TASK_DUE";
    private sealed record Snapshot(Guid ProjectId, string Revision, string Code, Guid? Pm,
        Settings Settings, ProjectFlowHiveNotificationPolicy.Task[] Tasks, Dictionary<Guid, State> State);

    internal static async System.Threading.Tasks.Task<bool> ReadyAsync(NpgsqlConnection connection, CancellationToken token)
    {
        // Module 103 can exist before the optional Module 065 orchestration schema.
        // Check relation names without parsing a query against missing tables.
        await using var tables = new NpgsqlCommand("""
            SELECT to_regclass('public.project_flowhive_notification_state') IS NOT NULL
                AND to_regclass('public.project_flowhive_plan_versions') IS NOT NULL
                AND to_regclass('public.project_flowhive_plan_reviews') IS NOT NULL
                AND to_regclass('public.enterprise_notification_policies') IS NOT NULL;
            """, connection);
        if (await tables.ExecuteScalarAsync(token) is not true) return false;
        await using var policies = new NpgsqlCommand("""
            SELECT COUNT(*)=2 FROM enterprise_notification_policies
            WHERE policy_code IN ('FLOWHIVE_TASK_ASSIGNED','FLOWHIVE_TASK_DUE');
            """, connection);
        return await policies.ExecuteScalarAsync(token) is true;
    }

    internal static async System.Threading.Tasks.Task<EnterpriseNotificationSourceObservation> ScanAsync(
        NpgsqlConnection connection, string correlationId, CancellationToken token)
    {
        const string source = "flowhive_approved_wbs";
        if (!await ReadyAsync(connection, token))
            return EnterpriseNotificationSourceObservation.Unavailable(source, "066", "MIGRATION_112_REQUIRED",
                "FlowHive task notification migration 112 is required.");
        var projects = new List<Guid>();
        await using (var query = new NpgsqlCommand("SELECT DISTINCT project_id FROM project_flowhive_plans WHERE baseline_version_number IS NOT NULL AND plan_status <> 'archived';", connection))
        await using (var reader = await query.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) projects.Add(reader.GetGuid(0));
        var created = 0;
        var failed = 0;
        foreach (var project in projects)
        {
            token.ThrowIfCancellationRequested();
            // Session lock spans InsertEventAsync's individual transactions and the final checkpoint.
            // Crash before checkpoint replays identical keys; replicas cannot consume the same transition.
            await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@key, 66));", connection);
            acquire.Parameters.AddWithValue("key", project.ToString());
            if (await acquire.ExecuteScalarAsync(token) is not true) continue;
            try
            {
                var snapshot = await LoadAsync(connection, project, token);
                if (snapshot is null) continue;
                var now = DateTimeOffset.UtcNow;
                var evaluation = Evaluate(project, snapshot.Revision, snapshot.Tasks, snapshot.Pm,
                    snapshot.Settings, snapshot.State, now);
                foreach (var item in evaluation.Events)
                {
                    var result = await EnterpriseNotificationRepository.InsertEventAsync(connection,
                        item.Kind == "assigned" ? AssignmentPolicy : DuePolicy, "066", item.Key, item.Key,
                        "flowhive_task", item.TaskId, project, item.Recipient, now, now,
                        JsonSerializer.SerializeToElement(new
                        {
                            contract = "flowhive-task-events-v1", projectId = project,
                            taskId = item.TaskId, taskWbs = item.Wbs, taskName = item.Name,
                            dueDate = item.Due.ToString("yyyy-MM-dd"), kind = item.Kind,
                            transition = item.Token, snapshot.Code,
                            projectCode = snapshot.Code, timezone = snapshot.Settings.Timezone,
                            deliveryBoundary = snapshot.Settings.Boundary,
                            notificationLabel = item.Kind.Replace('_', ' '),
                            deepLink = $"#project-flowhive?projectId={project:D}"
                        }, Json), "authoritative_scanner", null, correlationId, token);
                    if (result.Created) created++;
                }
                await using var save = new NpgsqlCommand("""
                    INSERT INTO project_flowhive_notification_state(project_id,state)
                    VALUES(@project,@state::jsonb) ON CONFLICT(project_id)
                    DO UPDATE SET state=EXCLUDED.state,updated_at=NOW();
                    """, connection);
                save.Parameters.AddWithValue("project", project);
                save.Parameters.AddWithValue("state", JsonSerializer.Serialize(evaluation.State, Json));
                await save.ExecuteNonQueryAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { failed++; } // Report source failure without leaking document or mailbox data.
            finally
            {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@key, 66));", connection);
                release.Parameters.AddWithValue("key", project.ToString());
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        var observation = failed == 0
            ? EnterpriseNotificationSourceObservation.Healthy(source, "066", projects.Count, created,
                "Approved WBS assignments and local-date reminders evaluated through Module 065.")
            : EnterpriseNotificationSourceObservation.Failed(source, "066", "FLOWHIVE_NOTIFICATION_SOURCE_FAILED",
                $"{failed} project source(s) could not be evaluated; subsequent scans retry without duplicate events.");
        await EnterpriseNotificationRepository.UpsertCheckpointAsync(connection, observation, DateTimeOffset.UtcNow, token);
        return observation;
    }

    // The same check runs for manual retries: disabling, completing, deleting, rescheduling,
    // reassigning or archiving work invalidates a previously queued event.
    internal static async System.Threading.Tasks.Task<(bool Current, bool Defer, string Boundary)> ValidateAsync(
        NpgsqlConnection connection, EnterpriseNotificationEventRow item, CancellationToken token)
    {
        if (item.ProjectId is not { } project || item.EntityId is not { } taskId || item.SubjectUserId is not { } user
            || item.IngestionSource != "authoritative_scanner" || !await ReadyAsync(connection, token))
            return (false, false, "locked");
        var snapshot = await LoadAsync(connection, project, token);
        if (snapshot is null || !snapshot.Settings.Enabled || snapshot.Settings.Boundary == "locked") return (false, false, "locked");
        var task = snapshot.Tasks.SingleOrDefault(task => task.Id == taskId);
        if (task is null) return (false, false, "locked");
        var observed = Evaluate(project, snapshot.Revision, snapshot.Tasks, snapshot.Pm,
            snapshot.Settings with { QuietStart = null, QuietEnd = null }, snapshot.State, DateTimeOffset.UtcNow);
        if (!observed.State.TryGetValue(taskId, out var state)) return (false, false, "locked");
        var payload = item.Payload;
        string Value(string name) => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var kind = Value("kind");
        var isAssignee = snapshot.Settings.IncludeTeam && task.Assignees.Contains(user);
        var expected = kind == "assigned" ? state.Assignments.GetValueOrDefault(user)
            : state.DueToken + ":" + state.Assignments.GetValueOrDefault(user, "pm");
        var current = Value("transition") == expected && Value("dueDate") == task.Due.ToString("yyyy-MM-dd")
            && (kind == "assigned" ? isAssignee && item.PolicyCode == AssignmentPolicy
                : item.PolicyCode == DuePolicy && kind == DueKind(task.Due, LocalDate(DateTimeOffset.UtcNow, snapshot.Settings), snapshot.Settings)
                    && (isAssignee || snapshot.Settings.IncludePm && snapshot.Pm == user));
        // Never promote a test event when either the project or global policy becomes live later.
        var boundary = Value("deliveryBoundary") == "production_governed" && snapshot.Settings.Boundary == "production_governed"
            ? "production_governed" : "test_only";
        return (current, current && IsQuiet(DateTimeOffset.UtcNow, snapshot.Settings), boundary);
    }

    internal static async System.Threading.Tasks.Task<object[]> RecentAsync(NpgsqlConnection connection, Guid project, CancellationToken token)
    {
        var rows = new List<object>();
        await using var query = new NpgsqlCommand("""
            SELECT e.enterprise_notification_event_id,e.payload->>'taskWbs',e.payload->>'taskName',
                   e.payload->>'kind',e.payload->>'dueDate',e.event_status,e.created_at,
                   e.last_error_code,e.attempt_count,COALESCE(u.display_name,'Team member')
            FROM enterprise_notification_events e LEFT JOIN app_users u ON u.user_id=e.subject_user_id
            WHERE e.project_id=@project AND e.policy_code IN ('FLOWHIVE_TASK_ASSIGNED','FLOWHIVE_TASK_DUE')
            ORDER BY e.created_at DESC LIMIT 50;
            """, connection);
        query.Parameters.AddWithValue("project", project);
        await using var reader = await query.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new
        {
            eventId=reader.GetGuid(0),taskWbs=reader.IsDBNull(1) ? "" : reader.GetString(1),
            taskName=reader.IsDBNull(2) ? "" : reader.GetString(2),kind=reader.IsDBNull(3) ? "" : reader.GetString(3),
            dueDate=reader.IsDBNull(4) ? "" : reader.GetString(4),status=reader.GetString(5),
            createdAt=reader.GetFieldValue<DateTimeOffset>(6),diagnosticCode=reader.GetString(7),
            attempts=reader.GetInt32(8),recipient=reader.GetString(9)
        });
        return rows.ToArray();
    }

    private static async System.Threading.Tasks.Task<Snapshot?> LoadAsync(NpgsqlConnection connection, Guid project, CancellationToken token)
    {
        const string sql = """
            SELECT plan.plan_id,plan.baseline_version_number,p.project_code,p.project_manager_user_id,
                   v.plan_payload::text,v.schedule_payload::text,to_jsonb(pref)::text,
                   COALESCE(s.state,'{}'::jsonb)::text
            FROM projects p
            JOIN LATERAL (
                SELECT f.* FROM project_flowhive_plans f
                WHERE f.project_id=p.project_id AND f.baseline_version_number IS NOT NULL AND f.plan_status <> 'archived'
                ORDER BY f.baselined_at DESC,f.plan_id LIMIT 1
            ) plan ON TRUE
            JOIN project_flowhive_plan_versions v ON v.plan_id=plan.plan_id AND v.version_number=plan.baseline_version_number
            JOIN project_flowhive_plan_reviews r ON r.plan_id=plan.plan_id AND r.version_number=plan.baseline_version_number AND r.decision='approved_for_baseline'
            LEFT JOIN project_flowhive_task_reminder_preferences pref ON pref.project_id=p.project_id
            LEFT JOIN project_flowhive_notification_state s ON s.project_id=p.project_id
            WHERE p.project_id=@project AND lower(p.status) NOT IN ('closed','completed','cancelled','canceled','archived');
            """;
        Snapshot? snapshot;
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("project", project);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            var settings = Default;
            if (!reader.IsDBNull(6))
            {
                using var doc = JsonDocument.Parse(reader.GetString(6));
                var pref = doc.RootElement;
                TimeSpan? Time(string key) => pref.GetProperty(key).ValueKind == JsonValueKind.Null ? null : TimeSpan.Parse(pref.GetProperty(key).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                settings = new(pref.GetProperty("enabled").GetBoolean(),
                    pref.GetProperty("lead_days").EnumerateArray().Select(v => v.GetInt16()).ToArray(),
                    pref.GetProperty("include_project_manager").GetBoolean(), pref.GetProperty("include_assigned_team_members").GetBoolean(),
                    pref.GetProperty("include_overdue").GetBoolean(),pref.GetProperty("timezone_name").GetString()!,
                    pref.GetProperty("delivery_boundary").GetString()!,Time("quiet_hours_start"),Time("quiet_hours_end"));
            }
            snapshot = new(project, $"{reader.GetGuid(0):N}-{reader.GetInt32(1)}", reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3), settings,
                Tasks(JsonSerializer.Deserialize<ProjectFlowHivePlanRequest>(reader.GetString(4), Json)!,
                    JsonSerializer.Deserialize<ProjectFlowHiveScheduleResult>(reader.GetString(5), Json)!),
                JsonSerializer.Deserialize<Dictionary<Guid,State>>(reader.GetString(7), Json)!);
        }
        // Canonical, active internal identities only. Never derive recipients from display names or emails in WBS JSON.
        var ids = snapshot.Tasks.SelectMany(t => t.Assignees).Append(snapshot.Pm ?? Guid.Empty).Distinct().ToArray();
        var active = new HashSet<Guid>();
        await using (var command = new NpgsqlCommand("SELECT user_id FROM app_users WHERE user_id=ANY(@ids) AND is_active=TRUE;", connection))
        {
            command.Parameters.AddWithValue("ids", ids);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) active.Add(reader.GetGuid(0));
        }
        return snapshot with { Pm = snapshot.Pm.HasValue && active.Contains(snapshot.Pm.Value) ? snapshot.Pm : null,
            Tasks = snapshot.Tasks.Select(t => t with { Assignees=t.Assignees.Where(active.Contains).ToArray() }).ToArray() };
    }
}

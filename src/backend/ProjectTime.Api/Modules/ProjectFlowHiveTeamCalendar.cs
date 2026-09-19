using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static partial class ProjectFlowHivePsaModule
{
    // Dates are explicit UTC day boundaries; only free/busy intervals leave this endpoint.
    // Callers cannot supply a mailbox or resource identity outside the selected project.
    private static async Task<IResult> GetTeamCalendarAsync(
        Guid projectId, DateOnly start, DateOnly end, HttpContext context, CancellationToken cancellationToken)
    {
        if (end <= start || end.DayNumber - start.DayNumber > 42)
            return Results.BadRequest(new { status = "invalid_calendar_range", message = "Select 1 to 42 days. The end date is exclusive." });
        var access = await OpenProjectAsync(projectId, context, null, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var members = new List<FlowHiveCalendarMember>();
        await using (var command = new NpgsqlCommand("""
            WITH members AS (
                SELECT project_manager_user_id AS user_id, 'Project Manager' AS role FROM projects WHERE project_id=@project
                UNION ALL SELECT account_executive_user_id, 'Account Executive' FROM projects WHERE project_id=@project
                UNION ALL SELECT solution_architect_user_id, 'Solution Architect' FROM projects WHERE project_id=@project
                UNION ALL SELECT user_id, 'Project team' FROM project_assignments WHERE project_id=@project
                    AND (effective_start_date IS NULL OR effective_start_date <= CURRENT_DATE)
                    AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE)
                UNION ALL SELECT user_id, 'Planning collaborator' FROM project_planning_collaborators
                    WHERE project_id=@project AND module_code='066' AND is_active
                      AND effective_start_date <= CURRENT_DATE
                      AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE)
                UNION ALL
                SELECT u.user_id, 'WBS assignee'
                FROM project_flowhive_working_copies wc
                CROSS JOIN LATERAL jsonb_array_elements(
                    CASE WHEN jsonb_typeof(wc.working_payload->'assignments')='array'
                         THEN wc.working_payload->'assignments' ELSE '[]'::jsonb END) assignment
                JOIN app_users u ON u.user_id::text=assignment->>'resourceUserId'
                WHERE wc.project_id=@project
            )
            SELECT u.user_id, COALESCE(NULLIF(u.display_name,''),u.email,'Team member'),
                   COALESCE(u.email,''), string_agg(DISTINCT m.role, ', ' ORDER BY m.role)
            FROM members m JOIN app_users u ON u.user_id=m.user_id
            WHERE u.is_active=TRUE
            GROUP BY u.user_id, u.display_name, u.email ORDER BY 2;
            """, connection))
        {
            command.Parameters.AddWithValue("project", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                members.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        var rows = members.ToDictionary(m => m.UserId, m => new FlowHiveCalendarRow(
            m.UserId, m.DisplayName, m.Role, "unknown", [], null));
        var queryable = members.Where(m => !string.IsNullOrWhiteSpace(m.Email)).ToArray();
        if (queryable.Length > 0)
        {
            try
            {
                var token = await CalendarCapacityModule.GraphToken(cancellationToken);
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                foreach (var batch in queryable.Chunk(20))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var response = await client.PostAsJsonAsync(
                            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(batch[0].Email)}/calendar/getSchedule",
                            new
                            {
                                schedules = batch.Select(m => m.Email).Distinct(StringComparer.OrdinalIgnoreCase),
                                startTime = new { dateTime = start.ToString("yyyy-MM-dd") + "T00:00:00", timeZone = "UTC" },
                                endTime = new { dateTime = end.ToString("yyyy-MM-dd") + "T00:00:00", timeZone = "UTC" },
                                availabilityViewInterval = 30
                            }, cancellationToken);
                        if (!response.IsSuccessStatusCode) continue;
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                        if (!document.RootElement.TryGetProperty("value", out var values)) continue;
                        foreach (var value in values.EnumerateArray())
                        {
                            var email = value.TryGetProperty("scheduleId", out var id) ? id.GetString() : null;
                            if (value.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) continue;
                            // An omitted schedule is unknown, not an empty (free) calendar.
                            if (!value.TryGetProperty("scheduleItems", out var items) || items.ValueKind != JsonValueKind.Array) continue;
                            var intervals = new List<FlowHiveBusyInterval>();
                            foreach (var item in items.EnumerateArray())
                            {
                                var status = item.TryGetProperty("status", out var state) ? state.GetString() ?? "unknown" : "unknown";
                                if (!TryCalendarInstant(item, "start", out var from) || !TryCalendarInstant(item, "end", out var to))
                                    throw new JsonException("Calendar interval was incomplete.");
                                intervals.Add(new(from, to, status));
                            }
                            foreach (var member in batch.Where(m => m.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                                rows[member.UserId] = new(member.UserId, member.DisplayName, member.Role, "available", intervals, DateTimeOffset.UtcNow);
                        }
                    }
                    catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException
                                                     && !cancellationToken.IsCancellationRequested)
                    {
                        // A failed batch leaves its members explicitly unknown. Never return Graph payloads or event subjects.
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException
                                             && !cancellationToken.IsCancellationRequested)
            {
                // Missing consent/credentials must not fabricate free time or hide the project team.
            }
        }
        return Results.Ok(new
        {
            projectId, start, end, timeZone = "UTC", endExclusive = true,
            status = rows.Values.All(r => r.Status == "available") ? "loaded" : "partial",
            members = members.Select(m => rows[m.UserId]),
            message = "Calendar availability only. Project allocations and task effort remain separate from free/busy time."
        });
    }

    private static bool TryCalendarInstant(JsonElement item, string property, out DateTimeOffset instant)
    {
        instant = default;
        return item.TryGetProperty(property, out var value)
            && value.TryGetProperty("dateTime", out var date)
            && DateTimeOffset.TryParse(date.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out instant);
    }

    private sealed record FlowHiveCalendarMember(Guid UserId, string DisplayName, string Email, string Role);
    private sealed record FlowHiveBusyInterval(DateTimeOffset Start, DateTimeOffset End, string Status);
    private sealed record FlowHiveCalendarRow(Guid UserId, string DisplayName, string Role, string Status,
        IReadOnlyList<FlowHiveBusyInterval> Intervals, DateTimeOffset? RetrievedAt);
}

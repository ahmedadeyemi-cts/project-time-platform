using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class CalendarCapacityModule
{
    public static WebApplication MapCalendarCapacityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/calendar/configuration", () => Results.Ok(new
        {
            module = "057",
            status = "calendar_configuration_loaded",
            environmentMode = Env("PROJECTPULSE_ENTRA_MODE", "development"),
            testDomain = Env("PROJECTPULSE_ENTRA_TEST_DOMAIN", "onenecklab.com"),
            productionDomain = Env("PROJECTPULSE_ENTRA_PRODUCTION_DOMAIN", "ussignal.com"),
            allowedDomains = AllowedDomains(),
            graphConfigured = Has("PROJECTPULSE_ENTRA_TENANT_ID") && Has("PROJECTPULSE_ENTRA_CLIENT_ID") && Has("PROJECTPULSE_ENTRA_CLIENT_SECRET"),
            privacyDefault = "availability_only",
            supportedViews = new[] { "day", "workweek", "week", "month", "agenda", "timeline", "custom" },
            futureMonthNavigation = true
        }));

        app.MapGet("/api/calendar/resources", async (HttpContext context) =>
        {
            if (SessionUserId(context) is null)
                return Results.Json(new { status = "session_required", message = "A ProjectPulse session is required." }, statusCode: 401);

            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var resources = new List<ResourceRow>();

            await using var command = new NpgsqlCommand("""
                SELECT
                    u.user_id,
                    COALESCE(u.display_name, u.email),
                    u.email,
                    NULLIF(to_jsonb(u)->>'entra_object_id', ''),
                    COALESCE(NULLIF(to_jsonb(u)->>'team_name', ''), NULLIF(to_jsonb(u)->>'department_name', ''), NULLIF(to_jsonb(u)->>'department', ''), 'Unassigned'),
                    COALESCE(NULLIF(to_jsonb(u)->>'department_name', ''), NULLIF(to_jsonb(u)->>'department', ''), NULLIF(to_jsonb(u)->>'team_name', ''), 'Unassigned')
                FROM app_users u
                WHERE u.is_active = TRUE
                  AND COALESCE(u.login_enabled, TRUE) = TRUE
                  AND u.email IS NOT NULL
                  AND u.email <> ''
                  AND lower(u.email) NOT LIKE '%.local'
                  AND lower(u.email) NOT LIKE '%.cloud'
                  AND EXISTS (
                      SELECT 1
                      FROM app_user_role_assignments ura
                      JOIN app_roles r
                        ON r.role_id = ura.role_id
                      WHERE ura.user_id = u.user_id
                        AND COALESCE(
                              NULLIF(to_jsonb(ura)->>'is_active', '')::boolean,
                              TRUE
                            ) = TRUE
                        AND lower(
                              COALESCE(
                                NULLIF(to_jsonb(r)->>'name', ''),
                                NULLIF(to_jsonb(r)->>'role_name', ''),
                                NULLIF(to_jsonb(r)->>'code', ''),
                                ''
                              )
                            ) IN (
                              'engineer',
                              'engineering',
                              'engineering team lead'
                            )
                  )
                ORDER BY COALESCE(u.display_name, u.email);
                """, connection);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                resources.Add(new ResourceRow(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4), reader.GetString(5)));
            }

            return Results.Ok(new
            {
                status = "calendar_resources_loaded",
                count = resources.Count,
                resources,
                teams = resources.GroupBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new { teamName = g.Key, resourceCount = g.Count(), resourceIds = g.Select(x => x.UserId).ToArray() })
                    .OrderBy(x => x.teamName, StringComparer.OrdinalIgnoreCase),
                departments = resources.GroupBy(r => r.DepartmentName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new { departmentName = g.Key, resourceCount = g.Count(), resourceIds = g.Select(x => x.UserId).ToArray() })
                    .OrderBy(x => x.departmentName, StringComparer.OrdinalIgnoreCase)
            });
        });

        app.MapPost("/api/calendar/schedule", async (ScheduleRequest request, HttpContext context) =>
        {
            var actor = SessionUserId(context);
            if (actor is null)
                return Results.Json(new { status = "session_required", message = "A ProjectPulse session is required." }, statusCode: 401);

            if (request.End <= request.Start || request.End - request.Start > TimeSpan.FromDays(186))
                return Results.BadRequest(new { status = "invalid_range", message = "Choose a valid range of 186 days or fewer." });

            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var resources = await ResolveResources(connection, request, actor.Value);
            if (resources.Count == 0)
                return Results.BadRequest(new { status = "no_resources", message = "Select an engineer, team, or department." });

            try
            {
                var token = await GraphToken();
                var target = resources[0].Email;
                var endpoint = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(target)}/calendar/getSchedule";
                var body = new
                {
                    schedules = resources.Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    startTime = new { dateTime = request.Start.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = TimeZone(request.TimeZone) },
                    endTime = new { dateTime = request.End.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = TimeZone(request.TimeZone) },
                    availabilityViewInterval = Math.Clamp(request.IntervalMinutes ?? 30, 5, 1440)
                };

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(endpoint, content);
                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return Results.Json(new { status = "graph_calendar_query_failed", graphHttpStatus = (int)response.StatusCode, message = "Microsoft Graph calendar availability could not be loaded. Confirm application calendar permissions and admin consent.", graphResponse = SafeGraphError(raw) }, statusCode: 502);

                using var document = JsonDocument.Parse(raw);
                var interval = Math.Clamp(request.IntervalMinutes ?? 30, 5, 1440);
                var schedules = new List<object>();
                if (document.RootElement.TryGetProperty("value", out var values))
                {
                    foreach (var value in values.EnumerateArray())
                    {
                        var scheduleId = Str(value, "scheduleId") ?? "";
                        var resource = resources.FirstOrDefault(r => r.Email.Equals(scheduleId, StringComparison.OrdinalIgnoreCase));
                        var availability = Str(value, "availabilityView") ?? "";
                        var occupied = availability.Count(c => c != '0');
                        var scheduledHours = Math.Round(occupied * interval / 60m, 2);
                        var workingHours = WorkingHours(request.Start, request.End);
                        var items = new List<object>();
                        if (value.TryGetProperty("scheduleItems", out var scheduleItems))
                        {
                            foreach (var item in scheduleItems.EnumerateArray())
                            {
                                items.Add(new
                                {
                                    status = Str(item, "status") ?? "busy",
                                    start = Nested(item, "start", "dateTime") ?? "",
                                    end = Nested(item, "end", "dateTime") ?? "",
                                    subject = "Busy",
                                    isPrivate = true
                                });
                            }
                        }
                        schedules.Add(new
                        {
                            userId = resource?.UserId,
                            displayName = resource?.DisplayName ?? scheduleId,
                            email = scheduleId,
                            teamName = resource?.TeamName ?? "Unassigned",
                            departmentName = resource?.DepartmentName ?? "Unassigned",
                            workingHours,
                            scheduledHours,
                            availableHours = Math.Max(0m, workingHours - scheduledHours),
                            capacityPercent = workingHours <= 0 ? 0 : Math.Round(Math.Min(200m, scheduledHours / workingHours * 100m), 1),
                            availabilityView = availability,
                            scheduleItems = items
                        });
                    }
                }

                return Results.Ok(new
                {
                    status = "calendar_schedule_loaded",
                    privacyMode = "availability_only",
                    request.Start,
                    request.End,
                    timeZone = TimeZone(request.TimeZone),
                    view = request.View ?? "month",
                    resourceCount = resources.Count,
                    schedules
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "calendar_integration_error", message = ex.Message }, statusCode: 502);
            }
        });

        return app;
    }

    private static async Task<List<ResourceRow>> ResolveResources(NpgsqlConnection connection, ScheduleRequest request, Guid actor)
    {
        var rows = new List<ResourceRow>();
        await using var command = new NpgsqlCommand("""
            SELECT u.user_id, COALESCE(u.display_name,u.email), u.email,
                   NULLIF(to_jsonb(u)->>'entra_object_id',''),
                   COALESCE(NULLIF(to_jsonb(u)->>'team_name',''),NULLIF(to_jsonb(u)->>'department_name',''),NULLIF(to_jsonb(u)->>'department',''),'Unassigned'),
                   COALESCE(NULLIF(to_jsonb(u)->>'department_name',''),NULLIF(to_jsonb(u)->>'department',''),NULLIF(to_jsonb(u)->>'team_name',''),'Unassigned')
            FROM app_users u
            WHERE u.is_active=TRUE AND COALESCE(u.login_enabled,TRUE)=TRUE
              AND u.email IS NOT NULL AND u.email<>'' AND lower(u.email) NOT LIKE '%.local'
                  AND lower(u.email) NOT LIKE '%.cloud'
              AND EXISTS (
                  SELECT 1
                  FROM app_user_role_assignments ura
                  JOIN app_roles r
                    ON r.role_id = ura.role_id
                  WHERE ura.user_id = u.user_id
                    AND COALESCE(
                          NULLIF(to_jsonb(ura)->>'is_active', '')::boolean,
                          TRUE
                        ) = TRUE
                    AND lower(
                          COALESCE(
                            NULLIF(to_jsonb(r)->>'name', ''),
                            NULLIF(to_jsonb(r)->>'role_name', ''),
                            NULLIF(to_jsonb(r)->>'code', ''),
                            ''
                          )
                        ) IN (
                          'engineer',
                          'engineering',
                          'engineering team lead'
                        )
              )
              AND (
                    (cardinality(@ids)>0 AND u.user_id=ANY(@ids))
                 OR (cardinality(@ids)=0 AND NULLIF(@team,'') IS NOT NULL AND lower(COALESCE(NULLIF(to_jsonb(u)->>'team_name',''),NULLIF(to_jsonb(u)->>'department_name',''),NULLIF(to_jsonb(u)->>'department',''),'Unassigned'))=lower(@team))
                 OR (cardinality(@ids)=0 AND NULLIF(@department,'') IS NOT NULL AND lower(COALESCE(NULLIF(to_jsonb(u)->>'department_name',''),NULLIF(to_jsonb(u)->>'department',''),NULLIF(to_jsonb(u)->>'team_name',''),'Unassigned'))=lower(@department))
                 OR (cardinality(@ids)=0 AND NULLIF(@team,'') IS NULL AND NULLIF(@department,'') IS NULL AND u.user_id=@actor)
              )
            ORDER BY COALESCE(u.display_name,u.email);
            """, connection);
        command.Parameters.AddWithValue("ids", request.ResourceIds?.Distinct().ToArray() ?? Array.Empty<Guid>());
        command.Parameters.AddWithValue("team", request.TeamName?.Trim() ?? "");
        command.Parameters.AddWithValue("department", request.DepartmentName?.Trim() ?? "");
        command.Parameters.AddWithValue("actor", actor);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new ResourceRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return rows;
    }

    private static Guid? SessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId", "ProjectPulseActualUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid guid) return guid;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string ConnectionString()
    {
        foreach (var name in new[] { "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse", "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING", "PROJECTTIME_DATABASE_CONNECTION" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        throw new InvalidOperationException("ProjectPulse database connection is not configured.");
    }

    private static async Task<string> GraphToken()
    {
        using var client = new HttpClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["client_id"] = Required("PROJECTPULSE_ENTRA_CLIENT_ID"),
            ["client_secret"] = Required("PROJECTPULSE_ENTRA_CLIENT_SECRET"),
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials"
        });
        var tenant = Required("PROJECTPULSE_ENTRA_TENANT_ID");
        var response = await client.PostAsync($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", content);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Graph token request failed with HTTP {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Graph token missing.");
    }

    private static decimal WorkingHours(DateTimeOffset start, DateTimeOffset end)
    {
        var date = DateOnly.FromDateTime(start.Date);
        var last = DateOnly.FromDateTime(end.Date);
        var days = 0;
        while (date < last) { if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) days++; date = date.AddDays(1); }
        return days * 8m;
    }

    private static string SafeGraphError(string raw)
    {
        try { using var d = JsonDocument.Parse(raw); var e = d.RootElement.GetProperty("error"); return $"{Str(e,"code")}: {Str(e,"message")}"; }
        catch { return "Microsoft Graph rejected the request."; }
    }

    private static string? Str(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? Nested(JsonElement item, string property, string child) => item.TryGetProperty(property, out var nested) ? Str(nested, child) : null;
    private static string TimeZone(string? value) => string.IsNullOrWhiteSpace(value) ? "UTC" : value.Trim();
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is not configured.");
    private static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;
    private static bool Has(string name) => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
    private static string[] AllowedDomains()
    {
        var set = (Environment.GetEnvironmentVariable("PROJECTPULSE_SSO_ALLOWED_DOMAINS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().TrimStart('@').ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        set.Add(Env("PROJECTPULSE_ENTRA_TEST_DOMAIN", "onenecklab.com").TrimStart('@'));
        set.Add(Env("PROJECTPULSE_ENTRA_PRODUCTION_DOMAIN", "ussignal.com").TrimStart('@'));
        return set.OrderBy(x => x).ToArray();
    }

    private sealed record ResourceRow(Guid UserId, string DisplayName, string Email, string? EntraObjectId, string TeamName, string DepartmentName);
    private sealed record ScheduleRequest(DateTimeOffset Start, DateTimeOffset End, string? TimeZone, string? View, int? IntervalMinutes, Guid[]? ResourceIds, string? TeamName, string? DepartmentName);
}

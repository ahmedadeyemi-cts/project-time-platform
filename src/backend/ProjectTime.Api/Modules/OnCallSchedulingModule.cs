using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 071 migrates the established US Signal on-call schedule experience
/// behind ProjectPulse identity and role enforcement. The first source package
/// uses the existing Cloudflare service as its compatibility store; switching
/// persistence providers is a separately authorized change.
/// </summary>
public static class OnCallSchedulingModule
{
    private const string ModuleNumber = "071";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline =
        "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";
    private const string DefaultTimeZone = "America/Chicago";
    private const string ManagePermission = "MANAGE_ONCALL_SCHEDULE";
    private static readonly HttpClient UpstreamClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static WebApplication MapOnCallSchedulingEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/oncall-scheduling/capabilities",
            (Func<HttpContext, Task<IResult>>)GetCapabilitiesAsync);
        app.MapGet(
            "/api/oncall-scheduling/schedule",
            (Func<HttpContext, Task<IResult>>)GetScheduleAsync);
        app.MapGet(
            "/api/oncall-scheduling/roster",
            (Func<HttpContext, Task<IResult>>)GetRosterAsync);
        app.MapGet(
            "/api/oncall-scheduling/identity-options",
            (Func<HttpContext, Task<IResult>>)GetIdentityOptionsAsync);
        app.MapGet(
            "/api/oncall-scheduling/history",
            (Func<HttpContext, Task<IResult>>)GetHistoryAsync);
        app.MapPut(
            "/api/oncall-scheduling/schedule",
            (Func<HttpContext, Task<IResult>>)SaveScheduleAsync);
        app.MapPut(
            "/api/oncall-scheduling/roster",
            (Func<HttpContext, Task<IResult>>)SaveRosterAsync);
        app.MapPost(
            "/api/oncall-scheduling/autogenerate",
            (Func<HttpContext, Task<IResult>>)AutoGenerateAsync);
        app.MapPost(
            "/api/oncall-scheduling/history/restore",
            (Func<HttpContext, Task<IResult>>)RestoreHistoryAsync);
        app.MapGet(
            "/api/public/v1/oncall/current",
            (Func<string?, HttpContext, Task<IResult>>)GetPublicCurrentAsync);
        app.MapGet(
            "/api/public/v1/oncall/schedule",
            (Func<HttpContext, Task<IResult>>)GetPublicScheduleAsync);

        return app;
    }

    private static async Task<IResult> GetCapabilitiesAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "On-Call Scheduling",
            status = "capabilities_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            access = AccessResponse(access.Context!, context),
            authorization = new
            {
                view = "all authenticated ProjectPulse users",
                manage = new[]
                {
                    "SUPER_ADMINISTRATOR",
                    "ADMINISTRATOR",
                    "MANAGER",
                    "ENGINEERING_TEAM_LEAD"
                },
                permission = ManagePermission,
                platformAdministratorAccess = true,
                serverEnforced = true,
                viewAsTransfersAuthority = false
            },
            schedule = new
            {
                timeZone = DefaultTimeZone,
                starts = "Friday 16:00 America/Chicago",
                ends = "following Friday 07:00 America/Chicago",
                editableAtAnyTime = true,
                identitySource = "Module 062 stable app_users.user_id"
            },
            notifications = new
            {
                provider = "Module 067 Global SMTP",
                monday = "upcoming Friday assignment email",
                tuesday = "missing acknowledgement escalation",
                friday = "assignment start email",
                sendTime = "08:00 America/Chicago",
                smsEnabled = false,
                brevoEnabled = false,
                activation = "deferred until shared Global SMTP and scheduler registration are authorized"
            },
            publicApi = new[]
            {
                "/api/public/v1/oncall/current",
                "/api/public/v1/oncall/current?department=collaboration",
                "/api/public/v1/oncall/schedule"
            },
            persistence = PersistenceStatus()
        });
    }

    private static async Task<IResult> GetScheduleAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        var upstream = await ReadUpstreamAsync("/api/oncall", includeAdminHeaders: false, context);
        if (upstream.Failure is not null) return upstream.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "schedule_loaded",
            access = AccessResponse(access.Context!, context),
            canManage = access.Context!.CanManage,
            schedule = NormalizeSchedule(upstream.Payload),
            source = "configured Cloudflare compatibility store",
            sourceMutationPerformed = false
        });
    }

    private static async Task<IResult> GetRosterAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        var upstream = await ReadUpstreamAsync("/api/admin/roster", includeAdminHeaders: true, context);
        if (upstream.Failure is not null) return upstream.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "roster_loaded",
            access = AccessResponse(access.Context!, context),
            canManage = access.Context!.CanManage,
            roster = upstream.Payload ?? new JsonObject(),
            identityAuthority = "Module 062 / app_users.user_id"
        });
    }

    private static async Task<IResult> GetIdentityOptionsAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: true);
        if (access.Failure is not null) return access.Failure;

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return AuthorizationUnavailable();

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT
                    u.user_id,
                    COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                    u.email,
                    COALESCE(NULLIF(u.job_title, ''), 'Engineer') AS job_title,
                    COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), 'Unassigned') AS team_name,
                    COALESCE(NULLIF(u.department_name, ''), NULLIF(u.department, ''), NULLIF(u.team_name, ''), 'Unassigned') AS department_name
                FROM app_users u
                JOIN app_user_role_assignments ura
                  ON ura.user_id = u.user_id
                 AND ura.is_active = TRUE
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                WHERE u.is_active = TRUE
                  AND COALESCE(u.login_enabled, TRUE) = TRUE
                  AND upper(COALESCE(r.role_code, '')) IN (
                      'ENGINEER',
                      'ENGINEERING',
                      'ENGINEERING_MANAGER',
                      'ENGINEERING_TEAM_LEAD',
                      'MANAGER'
                  )
                ORDER BY display_name, u.email;
                """, connection);

            var identities = new List<object>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                identities.Add(new
                {
                    userId = reader.GetGuid(0),
                    displayName = reader.GetString(1),
                    email = reader.GetString(2),
                    jobTitle = reader.GetString(3),
                    teamName = reader.GetString(4),
                    departmentName = reader.GetString(5)
                });
            }

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "identity_options_loaded",
                identityAuthority = "Module 062 / app_users.user_id",
                count = identities.Count,
                identities
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "load on-call identity choices");
            return DependencyUnavailable("Identity choices are temporarily unavailable.");
        }
    }

    private static async Task<IResult> GetHistoryAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        var upstream = await ReadUpstreamAsync("/api/admin/oncall/history", includeAdminHeaders: true, context);
        if (upstream.Failure is not null) return upstream.Failure;
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "history_loaded",
            canRestore = access.Context!.CanManage,
            history = upstream.Payload ?? new JsonArray()
        });
    }

    private static async Task<IResult> SaveScheduleAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: true);
        if (access.Failure is not null) return access.Failure;

        var payload = await ReadJsonBodyAsync(context);
        if (payload.Failure is not null) return payload.Failure;
        var schedule = payload.Payload?["schedule"] ?? payload.Payload;
        var validation = ValidateSchedule(schedule);
        if (validation is not null) return validation;

        var identityValidation = await ValidateAssignedIdentitiesAsync(schedule!, context);
        if (identityValidation is not null) return identityValidation;

        var upstreamBody = new JsonObject { ["schedule"] = schedule!.DeepClone() };
        var upstream = await WriteUpstreamAsync(
            HttpMethod.Post,
            "/api/admin/oncall/save",
            upstreamBody,
            context);
        if (upstream.Failure is not null) return upstream.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "schedule_saved",
            savedAt = DateTimeOffset.UtcNow,
            savedBy = access.Context!.ActualUserId,
            upstream = upstream.Payload,
            auditRequired = true
        });
    }

    private static async Task<IResult> SaveRosterAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: true);
        if (access.Failure is not null) return access.Failure;

        var payload = await ReadJsonBodyAsync(context);
        if (payload.Failure is not null) return payload.Failure;
        var roster = payload.Payload?["roster"] ?? payload.Payload;
        if (roster is not JsonObject)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_roster",
                message = "Roster must be a department object containing identity-backed people."
            });
        }

        var identityValidation = await ValidateAssignedIdentitiesAsync(roster, context);
        if (identityValidation is not null) return identityValidation;

        var upstreamBody = new JsonObject { ["roster"] = roster.DeepClone() };
        var upstream = await WriteUpstreamAsync(
            HttpMethod.Post,
            "/api/admin/roster/save",
            upstreamBody,
            context);
        if (upstream.Failure is not null) return upstream.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "roster_saved",
            savedAt = DateTimeOffset.UtcNow,
            savedBy = access.Context!.ActualUserId,
            upstream = upstream.Payload
        });
    }

    private static async Task<IResult> AutoGenerateAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: true);
        if (access.Failure is not null) return access.Failure;

        var payload = await ReadJsonBodyAsync(context);
        if (payload.Failure is not null) return payload.Failure;
        var startText = payload.Payload?["startDate"]?.GetValue<string>();
        var endText = payload.Payload?["endDate"]?.GetValue<string>();
        var seedIndex = payload.Payload?["seedIndex"]?.GetValue<int?>() ?? 0;
        if (!DateOnly.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate)
            || !DateOnly.TryParseExact(endText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate)
            || endDate < startDate)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_generation_range",
                message = "startDate and endDate must use YYYY-MM-DD and endDate must not precede startDate."
            });
        }

        var rosterResult = await ReadUpstreamAsync("/api/admin/roster", includeAdminHeaders: true, context);
        if (rosterResult.Failure is not null) return rosterResult.Failure;
        var roster = rosterResult.Payload as JsonObject ?? new JsonObject();
        var entries = BuildRotationEntries(roster, startDate, endDate, seedIndex);
        var schedule = new JsonObject
        {
            ["version"] = 1,
            ["tz"] = DefaultTimeZone,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["updatedBy"] = access.Context!.ActualUserId.ToString(),
            ["entries"] = entries
        };

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "schedule_generation_previewed",
            persistencePerformed = false,
            entriesGenerated = entries.Count,
            schedule
        });
    }

    private static async Task<IResult> RestoreHistoryAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: true);
        if (access.Failure is not null) return access.Failure;
        var payload = await ReadJsonBodyAsync(context);
        if (payload.Failure is not null) return payload.Failure;
        var id = payload.Payload?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { module = ModuleNumber, status = "snapshot_id_required" });
        }

        var upstream = await WriteUpstreamAsync(
            HttpMethod.Post,
            "/api/admin/oncall/history/restore",
            new JsonObject { ["id"] = id },
            context);
        if (upstream.Failure is not null) return upstream.Failure;
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "schedule_history_restored",
            restoredBy = access.Context!.ActualUserId,
            snapshotId = id,
            upstream = upstream.Payload
        });
    }

    private static async Task<IResult> GetPublicScheduleAsync(HttpContext context)
    {
        SetPublicHeaders(context);
        var upstream = await ReadUpstreamAsync("/api/oncall", includeAdminHeaders: false, context);
        if (upstream.Failure is not null) return upstream.Failure;
        return Results.Ok(new
        {
            module = ModuleNumber,
            service = "US Signal On-Call Routing",
            status = "schedule_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            schedule = NormalizeSchedule(upstream.Payload)
        });
    }

    private static async Task<IResult> GetPublicCurrentAsync(string? department, HttpContext context)
    {
        SetPublicHeaders(context);
        var upstream = await ReadUpstreamAsync("/api/oncall", includeAdminHeaders: false, context);
        if (upstream.Failure is not null) return upstream.Failure;
        var schedule = NormalizeSchedule(upstream.Payload);
        var current = FindCurrentEntry(schedule);
        if (current is null)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                service = "US Signal On-Call Routing",
                status = "no_current_assignment",
                department = NormalizeDepartment(department)
            }, statusCode: StatusCodes.Status404NotFound);
        }

        var normalizedDepartment = NormalizeDepartment(department);
        if (!string.IsNullOrWhiteSpace(normalizedDepartment))
        {
            var person = current["departments"]?[normalizedDepartment];
            if (person is null)
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    service = "US Signal On-Call Routing",
                    status = "department_not_assigned",
                    department = normalizedDepartment
                }, statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(new
            {
                module = ModuleNumber,
                service = "US Signal On-Call Routing",
                status = "current_assignment_loaded",
                department = normalizedDepartment,
                window = new { startISO = current["startISO"], endISO = current["endISO"], timeZone = DefaultTimeZone },
                onCall = person
            });
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            service = "US Signal On-Call Routing",
            status = "current_assignments_loaded",
            window = new { startISO = current["startISO"], endISO = current["endISO"], timeZone = DefaultTimeZone },
            departments = current["departments"]
        });
    }

    private static JsonObject NormalizeSchedule(JsonNode? payload)
    {
        var source = payload?["schedule"] ?? payload;
        var entries = source?["entries"] as JsonArray ?? new JsonArray();
        return new JsonObject
        {
            ["version"] = source?["version"]?.DeepClone() ?? 1,
            ["tz"] = source?["tz"]?.DeepClone() ?? DefaultTimeZone,
            ["updatedAt"] = source?["updatedAt"]?.DeepClone(),
            ["entries"] = entries.DeepClone()
        };
    }

    private static JsonObject? FindCurrentEntry(JsonObject schedule)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var node in schedule["entries"]?.AsArray() ?? new JsonArray())
        {
            if (node is not JsonObject entry) continue;
            if (!TryChicagoInstant(entry["startISO"]?.GetValue<string>(), out var start)
                || !TryChicagoInstant(entry["endISO"]?.GetValue<string>(), out var end)) continue;
            if (now >= start && now < end) return entry;
        }
        return null;
    }

    private static IResult? ValidateSchedule(JsonNode? schedule)
    {
        if (schedule is not JsonObject document || document["entries"] is not JsonArray entries)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_schedule",
                message = "Schedule must contain an entries array."
            });
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in entries)
        {
            if (node is not JsonObject entry)
            {
                return Results.BadRequest(new { module = ModuleNumber, status = "invalid_schedule_entry" });
            }
            var id = entry["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                return Results.BadRequest(new
                {
                    module = ModuleNumber,
                    status = "invalid_schedule_entry_id",
                    message = "Every entry requires a unique identifier."
                });
            }
            if (!TryChicagoInstant(entry["startISO"]?.GetValue<string>(), out var start)
                || !TryChicagoInstant(entry["endISO"]?.GetValue<string>(), out var end)
                || end <= start)
            {
                return Results.BadRequest(new
                {
                    module = ModuleNumber,
                    status = "invalid_schedule_window",
                    message = "Each entry requires a valid America/Chicago start and end, with end after start."
                });
            }
            if (entry["departments"] is not JsonObject)
            {
                return Results.BadRequest(new
                {
                    module = ModuleNumber,
                    status = "invalid_departments",
                    message = "Each schedule entry requires a departments object."
                });
            }
        }
        return null;
    }

    private static async Task<IResult?> ValidateAssignedIdentitiesAsync(JsonNode document, HttpContext context)
    {
        var userIds = new HashSet<Guid>();
        foreach (var node in EnumerateObjects(document))
        {
            if (!node.TryGetPropertyValue("userId", out var userIdNode)) continue;
            var value = userIdNode?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!Guid.TryParse(value, out var userId))
            {
                return Results.BadRequest(new
                {
                    module = ModuleNumber,
                    status = "invalid_identity_id",
                    message = "On-call userId values must be stable ProjectPulse identity GUIDs."
                });
            }
            userIds.Add(userId);
        }

        if (userIds.Count == 0) return null; // Existing legacy schedule rows remain readable and editable.
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return AuthorizationUnavailable();
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT user_id
                FROM app_users
                WHERE is_active = TRUE
                  AND user_id = ANY(@user_ids);
                """, connection);
            command.Parameters.AddWithValue("user_ids", userIds.ToArray());
            var active = new HashSet<Guid>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) active.Add(reader.GetGuid(0));
            if (!active.SetEquals(userIds))
            {
                return Results.BadRequest(new
                {
                    module = ModuleNumber,
                    status = "inactive_or_unknown_identity",
                    message = "Every selected on-call identity must remain active in ProjectPulse."
                });
            }
            return null;
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "validate on-call identities");
            return DependencyUnavailable("On-call identities could not be validated.");
        }
    }

    private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? node)
    {
        if (node is JsonObject current)
        {
            yield return current;
            foreach (var child in current.Select(pair => pair.Value))
            {
                foreach (var descendant in EnumerateObjects(child)) yield return descendant;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                foreach (var descendant in EnumerateObjects(child)) yield return descendant;
            }
        }
    }

    private static JsonArray BuildRotationEntries(JsonObject roster, DateOnly startDate, DateOnly endDate, int seedIndex)
    {
        var entries = new JsonArray();
        var rotation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cursor = NextFriday(startDate);
        while (cursor <= endDate)
        {
            var departments = new JsonObject();
            foreach (var (department, value) in roster)
            {
                if (value is not JsonArray people || people.Count == 0) continue;
                rotation.TryGetValue(department, out var used);
                var selectedIndex = Math.Abs(seedIndex + used) % people.Count;
                departments[department] = people[selectedIndex]?.DeepClone();
                rotation[department] = used + 1;
            }
            var start = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T16:00:00";
            var end = cursor.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T07:00:00";
            entries.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["startISO"] = start,
                ["endISO"] = end,
                ["departments"] = departments
            });
            cursor = cursor.AddDays(7);
        }
        return entries;
    }

    private static DateOnly NextFriday(DateOnly value)
    {
        var delta = ((int)DayOfWeek.Friday - (int)value.DayOfWeek + 7) % 7;
        return value.AddDays(delta);
    }

    private static bool TryChicagoInstant(string? localIso, out DateTimeOffset instant)
    {
        instant = default;
        if (!DateTime.TryParseExact(
                localIso,
                new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFF" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local)) return false;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone);
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) return false;
            instant = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<AccessOutcome> ResolveAccessAsync(HttpContext context, bool requireManage)
    {
        var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (effectiveUserId is null || actualUserId is null)
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return new(null, AuthorizationUnavailable());
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT upper(COALESCE(r.role_code, ''))
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", actualUserId.Value);
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) roles.Add(reader.GetString(0));
            var canManage =
                roles.Contains("SUPER_ADMINISTRATOR")
                || roles.Contains("ADMINISTRATOR")
                || roles.Contains("MANAGER")
                || roles.Contains("ENGINEERING_TEAM_LEAD");
            if (requireManage && !canManage)
            {
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "oncall_manage_permission_required",
                    permission = ManagePermission,
                    message = "Only Super Administrators, Administrators, Managers, and Engineering Team Leads can manage the on-call schedule."
                }, statusCode: StatusCodes.Status403Forbidden));
            }
            return new(new AccessContext(actualUserId.Value, effectiveUserId.Value, roles, canManage), null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "authorize on-call access");
            return new(null, AuthorizationUnavailable());
        }
    }

    private static object AccessResponse(AccessContext access, HttpContext context) => new
    {
        actualUserId = access.ActualUserId,
        effectiveUserId = access.EffectiveUserId,
        roles = access.Roles.OrderBy(value => value),
        canView = true,
        canManage = access.CanManage,
        managePermission = ManagePermission,
        isViewAs = IsViewAs(context),
        authoritySource = "actual ProjectPulse session"
    };

    private static async Task<UpstreamOutcome> ReadUpstreamAsync(
        string path,
        bool includeAdminHeaders,
        HttpContext context) =>
        await SendUpstreamAsync(HttpMethod.Get, path, null, includeAdminHeaders, context);

    private static async Task<UpstreamOutcome> WriteUpstreamAsync(
        HttpMethod method,
        string path,
        JsonNode payload,
        HttpContext context) =>
        await SendUpstreamAsync(method, path, payload, includeAdminHeaders: true, context);

    private static async Task<UpstreamOutcome> SendUpstreamAsync(
        HttpMethod method,
        string path,
        JsonNode? payload,
        bool includeAdminHeaders,
        HttpContext context)
    {
        var baseUri = UpstreamBaseUri();
        if (baseUri is null)
        {
            return new(null, DependencyUnavailable(
                "The governed Cloudflare on-call compatibility source is not configured."));
        }
        if (includeAdminHeaders)
        {
            var clientId = Environment.GetEnvironmentVariable("PROJECTPULSE_ONCALL_ACCESS_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("PROJECTPULSE_ONCALL_ACCESS_CLIENT_SECRET");
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return new(null, DependencyUnavailable(
                    "The governed on-call service credential is not configured."));
            }
        }

        try
        {
            using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (includeAdminHeaders)
            {
                request.Headers.TryAddWithoutValidation(
                    "CF-Access-Client-Id",
                    Environment.GetEnvironmentVariable("PROJECTPULSE_ONCALL_ACCESS_CLIENT_ID"));
                request.Headers.TryAddWithoutValidation(
                    "CF-Access-Client-Secret",
                    Environment.GetEnvironmentVariable("PROJECTPULSE_ONCALL_ACCESS_CLIENT_SECRET"));
            }
            if (payload is not null)
            {
                request.Content = new StringContent(
                    payload.ToJsonString(),
                    Encoding.UTF8,
                    "application/json");
            }
            using var response = await UpstreamClient.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            JsonNode? body = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try { body = JsonNode.Parse(raw); }
                catch { body = null; }
            }
            if (!response.IsSuccessStatusCode)
            {
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("OnCallSchedulingModule")
                    .LogWarning(
                        "Module 071 upstream request {Method} {Path} returned HTTP {StatusCode}.",
                        method.Method,
                        path,
                        (int)response.StatusCode);
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "oncall_source_unavailable",
                    message = "The on-call compatibility source did not accept the request."
                }, statusCode: StatusCodes.Status502BadGateway));
            }
            return new(body, null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "contact the on-call compatibility source");
            return new(null, DependencyUnavailable("The on-call compatibility source is unavailable."));
        }
    }

    private static Uri? UpstreamBaseUri()
    {
        var raw = Environment.GetEnvironmentVariable("PROJECTPULSE_ONCALL_UPSTREAM_BASE_URL");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    private static async Task<JsonOutcome> ReadJsonBodyAsync(HttpContext context)
    {
        try
        {
            var payload = await JsonNode.ParseAsync(context.Request.Body);
            return payload is null
                ? new(null, Results.BadRequest(new { module = ModuleNumber, status = "json_body_required" }))
                : new(payload, null);
        }
        catch (JsonException)
        {
            return new(null, Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_json",
                message = "A valid JSON request body is required."
            }));
        }
    }

    private static object PersistenceStatus() => new
    {
        mode = "cloudflare_compatibility_adapter",
        configured = UpstreamBaseUri() is not null,
        databaseSchemaIntroduced = false,
        cloudflareMutationPerformedBySourcePackage = false,
        activation = "authorized integration step only"
    };

    private static void SetPublicHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "public, max-age=60";
        context.Response.Headers.AccessControlAllowOrigin = "*";
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    }

    private static string NormalizeDepartment(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static IResult AuthorizationUnavailable() => Results.Json(new
    {
        module = ModuleNumber,
        status = "authorization_dependency_unavailable",
        message = "On-call authorization is temporarily unavailable."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult DependencyUnavailable(string message) => Results.Json(new
    {
        module = ModuleNumber,
        status = "dependency_unavailable",
        message
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("OnCallSchedulingModule")
            .LogWarning(exception, "Module 071 could not {Operation}.", operation);
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

    private sealed record AccessOutcome(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        IReadOnlySet<string> Roles,
        bool CanManage);
    private sealed record UpstreamOutcome(JsonNode? Payload, IResult? Failure);
    private sealed record JsonOutcome(JsonNode? Payload, IResult? Failure);
}

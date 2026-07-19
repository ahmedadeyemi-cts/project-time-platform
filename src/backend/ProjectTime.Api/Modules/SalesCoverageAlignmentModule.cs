using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 073 projects current AE/SA signals and validates effective-dated
/// alignment drafts. Persistence is intentionally locked pending an approved
/// database design.
/// </summary>
public static class SalesCoverageAlignmentModule
{
    private const string ModuleNumber = "073";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline = "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";
    private static readonly HashSet<string> ManageRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SOLUTION_ARCHITECT", "PROJECT_TEAM_COORDINATOR"
    };

    public static WebApplication MapSalesCoverageAlignmentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sales-coverage/capabilities", (Func<HttpContext, Task<IResult>>)GetCapabilitiesAsync);
        app.MapGet("/api/sales-coverage/source-signals", (Func<HttpContext, Task<IResult>>)GetSourceSignalsAsync);
        app.MapGet("/api/sales-coverage/identity-options", (Func<HttpContext, Task<IResult>>)GetIdentityOptionsAsync);
        app.MapPost("/api/sales-coverage/validate", (Func<HttpContext, Task<IResult>>)ValidateDraftAsync);
        return app;
    }

    private static async Task<IResult> GetCapabilitiesAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, false);
        if (access.Failure is not null) return access.Failure;
        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Sales Coverage Alignment",
            status = "capabilities_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            access = AccessResponse(access.Context!, context),
            fields = new[]
            {
                "accountExecutiveUserId", "primaryResaleOperationsUserId",
                "backupResaleOperationsUserId", "solutionArchitectUserId",
                "territory", "team", "effectiveStartDate", "effectiveEndDate"
            },
            persistence = new
            {
                mode = "validated_unsaved_draft",
                enabled = false,
                databaseAuthorizationRequired = true
            }
        });
    }

    private static async Task<IResult> GetSourceSignalsAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, false);
        if (access.Failure is not null) return access.Failure;
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable();
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var projects = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT p.project_id,
                       p.project_code,
                       p.project_name,
                       COALESCE(c.client_name, 'No customer'),
                       p.account_executive_user_id,
                       COALESCE(ae.display_name, ae.email, ''),
                       p.solution_architect_user_id,
                       COALESCE(sa.display_name, sa.email, ''),
                       p.start_date,
                       p.end_date,
                       p.status
                FROM projects p
                LEFT JOIN clients c ON c.client_id = p.client_id
                LEFT JOIN app_users ae ON ae.user_id = p.account_executive_user_id
                LEFT JOIN app_users sa ON sa.user_id = p.solution_architect_user_id
                WHERE p.account_executive_user_id IS NOT NULL
                   OR p.solution_architect_user_id IS NOT NULL
                ORDER BY p.updated_at DESC
                LIMIT 250;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    projects.Add(new
                    {
                        sourceType = "project",
                        sourceId = reader.GetGuid(0),
                        sourceCode = reader.GetString(1),
                        sourceName = reader.GetString(2),
                        customerName = reader.GetString(3),
                        accountExecutiveUserId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                        accountExecutiveName = reader.GetString(5),
                        solutionArchitectUserId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                        solutionArchitectName = reader.GetString(7),
                        effectiveStartDate = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        effectiveEndDate = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        status = reader.GetString(10)
                    });
                }
            }

            var intake = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT pir.project_intake_request_id,
                       pir.request_number,
                       pir.request_title,
                       pir.client_name,
                       pir.account_executive_user_id,
                       COALESCE(ae.display_name, ae.email, ''),
                       pir.solution_architect_user_id,
                       COALESCE(sa.display_name, sa.email, ''),
                       pir.target_start_date,
                       pir.target_completion_date,
                       pir.intake_status
                FROM project_intake_requests pir
                LEFT JOIN app_users ae ON ae.user_id = pir.account_executive_user_id
                LEFT JOIN app_users sa ON sa.user_id = pir.solution_architect_user_id
                WHERE pir.account_executive_user_id IS NOT NULL
                   OR pir.solution_architect_user_id IS NOT NULL
                ORDER BY pir.updated_at DESC
                LIMIT 250;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    intake.Add(new
                    {
                        sourceType = "intake",
                        sourceId = reader.GetGuid(0),
                        sourceCode = reader.GetString(1),
                        sourceName = reader.GetString(2),
                        customerName = reader.GetString(3),
                        accountExecutiveUserId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                        accountExecutiveName = reader.GetString(5),
                        solutionArchitectUserId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                        solutionArchitectName = reader.GetString(7),
                        effectiveStartDate = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        effectiveEndDate = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        status = reader.GetString(10)
                    });
                }
            }
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "source_signals_loaded",
                projects,
                intake,
                persistencePerformed = false
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "load sales coverage source signals");
            return DependencyUnavailable();
        }
    }

    private static async Task<IResult> GetIdentityOptionsAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, true);
        if (access.Failure is not null) return access.Failure;
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable();
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT u.user_id,
                       COALESCE(NULLIF(u.display_name, ''), u.email),
                       u.email,
                       COALESCE(NULLIF(u.job_title, ''), ''),
                       COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), ''),
                       array_agg(DISTINCT upper(r.role_code))
                FROM app_users u
                JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
                JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                WHERE u.is_active = TRUE AND COALESCE(u.login_enabled, TRUE) = TRUE
                GROUP BY u.user_id, u.display_name, u.email, u.job_title, u.team_name, u.department_name, u.department
                ORDER BY COALESCE(NULLIF(u.display_name, ''), u.email);
                """, connection);
            var identities = new List<object>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var roles = reader.GetFieldValue<string[]>(5);
                var profile = string.Join(' ', roles.Append(reader.GetString(3))).ToUpperInvariant();
                var categories = new List<string>();
                if (profile.Contains("ACCOUNT_EXECUTIVE") || profile.Contains("SALES")) categories.Add("account_executive");
                if (profile.Contains("RESALE") || profile.Contains("SALES_OPERATIONS")) categories.Add("resale_operations");
                if (profile.Contains("SOLUTION_ARCHITECT") || profile.Contains("ARCHITECT")) categories.Add("solution_architect");
                if (categories.Count == 0) continue;
                identities.Add(new
                {
                    userId = reader.GetGuid(0), displayName = reader.GetString(1), email = reader.GetString(2),
                    jobTitle = reader.GetString(3), team = reader.GetString(4), roles, categories
                });
            }
            return Results.Ok(new { module = ModuleNumber, status = "identity_options_loaded", identities });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "load sales coverage identities");
            return DependencyUnavailable();
        }
    }

    private static async Task<IResult> ValidateDraftAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, true);
        if (access.Failure is not null) return access.Failure;
        JsonNode? payload;
        try { payload = await JsonNode.ParseAsync(context.Request.Body); }
        catch (JsonException) { return Results.BadRequest(new { module = ModuleNumber, status = "invalid_json" }); }
        var rows = payload?["alignments"] as JsonArray ?? payload as JsonArray;
        if (rows is null) return Results.BadRequest(new { module = ModuleNumber, status = "alignments_required" });
        if (rows.Count > 1000) return Results.BadRequest(new { module = ModuleNumber, status = "alignment_limit_exceeded", maximum = 1000 });

        var normalized = new JsonArray();
        var errors = new List<object>();
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index] is not JsonObject row) { errors.Add(new { row = index + 1, code = "object_required" }); continue; }
            var ae = GuidText(row, "accountExecutiveUserId");
            var primary = GuidText(row, "primaryResaleOperationsUserId");
            var backup = GuidText(row, "backupResaleOperationsUserId", optional: true);
            var sa = GuidText(row, "solutionArchitectUserId");
            var territory = Text(row, "territory");
            var team = Text(row, "team");
            var start = DateText(row, "effectiveStartDate");
            var end = DateText(row, "effectiveEndDate", optional: true);
            if (ae is null || primary is null || sa is null || string.IsNullOrWhiteSpace(territory) || string.IsNullOrWhiteSpace(team) || start is null)
            {
                errors.Add(new { row = index + 1, code = "required_alignment_field_missing" }); continue;
            }
            if (backup is not null && string.Equals(primary, backup, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new { row = index + 1, code = "primary_backup_must_differ" }); continue;
            }
            if (end is not null && string.CompareOrdinal(end, start) < 0)
            {
                errors.Add(new { row = index + 1, code = "effective_end_precedes_start" }); continue;
            }
            normalized.Add(new JsonObject
            {
                ["id"] = Text(row, "id") ?? Guid.NewGuid().ToString(),
                ["accountExecutiveUserId"] = ae,
                ["primaryResaleOperationsUserId"] = primary,
                ["backupResaleOperationsUserId"] = backup,
                ["solutionArchitectUserId"] = sa,
                ["territory"] = territory,
                ["team"] = team,
                ["effectiveStartDate"] = start,
                ["effectiveEndDate"] = end,
                ["notes"] = Text(row, "notes") ?? string.Empty
            });
        }
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = errors.Count == 0 ? "draft_valid" : "draft_has_errors",
            valid = errors.Count == 0,
            validCount = normalized.Count,
            errorCount = errors.Count,
            alignments = normalized,
            errors,
            persistencePerformed = false
        });
    }

    private static string? Text(JsonObject row, string key) => row[key]?.GetValue<string>()?.Trim();
    private static string? GuidText(JsonObject row, string key, bool optional = false)
    {
        var value = Text(row, key);
        if (optional && string.IsNullOrWhiteSpace(value)) return null;
        return Guid.TryParse(value, out var id) ? id.ToString() : null;
    }
    private static string? DateText(JsonObject row, string key, bool optional = false)
    {
        var value = Text(row, key);
        if (optional && string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    private static async Task<AccessOutcome> ResolveAccessAsync(HttpContext context, bool requireManage)
    {
        var actual = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (actual is null || effective is null) return new(null, Results.Json(new { module = ModuleNumber, status = "session_required" }, statusCode: 401));
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return new(null, DependencyUnavailable());
        try
        {
            await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT upper(r.role_code)
                FROM app_user_role_assignments ura
                JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                WHERE ura.user_id = @user_id AND ura.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", actual.Value);
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) roles.Add(reader.GetString(0));
            var canManage = roles.Overlaps(ManageRoles);
            if (requireManage && !canManage) return new(null, Results.Json(new { module = ModuleNumber, status = "sales_coverage_manage_permission_required", message = "Administrators, Solution Architects, or Project Team Coordinators are required." }, statusCode: 403));
            return new(new(actual.Value, effective.Value, roles, canManage), null);
        }
        catch (Exception exception) { LogFailure(context, exception, "authorize sales coverage"); return new(null, DependencyUnavailable()); }
    }

    private static object AccessResponse(AccessContext access, HttpContext context) => new
    {
        actualUserId = access.ActualUserId, effectiveUserId = access.EffectiveUserId,
        roles = access.Roles.OrderBy(value => value), canView = true, canManage = access.CanManage,
        manageRoles = ManageRoles.OrderBy(value => value), isViewAs = IsViewAs(context), authoritySource = "actual ProjectPulse session"
    };
    private static Guid? SessionUserId(HttpContext context, params string[] keys) { foreach (var key in keys) { if (!context.Items.TryGetValue(key, out var value)) continue; if (value is Guid id) return id; if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed; } return null; }
    private static bool IsViewAs(HttpContext context) => context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is bool flag && flag;
    private static void LogFailure(HttpContext context, Exception exception, string operation) => context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SalesCoverageAlignmentModule").LogWarning(exception, "Module 073 could not {Operation}.", operation);
    private static IResult DependencyUnavailable() => Results.Json(new { module = ModuleNumber, status = "dependency_unavailable", message = "Sales coverage dependencies are temporarily unavailable." }, statusCode: 503);
    private static string? BuildConnectionString()
    {
        foreach (var name in new[] { "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse", "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING", "PROJECTTIME_DATABASE_CONNECTION" }) { var value = Environment.GetEnvironmentVariable(name); if (!string.IsNullOrWhiteSpace(value)) return value; }
        var host=Environment.GetEnvironmentVariable("PTP_DB_HOST"); var database=Environment.GetEnvironmentVariable("PTP_DB_NAME"); var username=Environment.GetEnvironmentVariable("PTP_DB_USER"); var password=Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (new[]{host,database,username,password}.Any(string.IsNullOrWhiteSpace)) return null;
        return new NpgsqlConnectionStringBuilder { Host=host, Port=int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"),out var port)?port:5432, Database=database, Username=username, Password=password, IncludeErrorDetail=false, Pooling=true, MaxPoolSize=5 }.ConnectionString;
    }
    private sealed record AccessOutcome(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(Guid ActualUserId, Guid EffectiveUserId, IReadOnlySet<string> Roles, bool CanManage);
}

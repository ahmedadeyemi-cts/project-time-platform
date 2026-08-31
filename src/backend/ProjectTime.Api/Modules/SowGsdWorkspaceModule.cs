using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Npgsql;
using ProjectTime.Api;

namespace ProjectTime.Api.Modules;

internal static class SowGsdWorkspaceModule
{
    private static readonly string[] RequiredPhases = ["Plan", "Design", "Implement", "Validate", "Release"];

    internal static IEndpointRouteBuilder MapSowGsdWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sow-gsd-workspace").RequireAuthorization("MfaVerified");

        group.MapGet("/options", GetOptionsAsync);
        group.MapGet("/records", ListRecordsAsync);
        group.MapGet("/records/{id:guid}", GetRecordAsync);
        group.MapPost("/records", CreateRecordAsync);
        group.MapPut("/records/{id:guid}", UpdateRecordAsync);
        group.MapPost("/records/{id:guid}/confirm", ConfirmRecordAsync);
        group.MapPost("/records/{id:guid}/archive", ArchiveRecordAsync);
        group.MapPost("/records/{id:guid}/restore", RestoreRecordAsync);
        group.MapGet("/records/{id:guid}/download/sow", DownloadSowAsync);
        group.MapGet("/records/{id:guid}/download/gsd", DownloadGsdAsync);

        return endpoints;
    }

    private static async Task<IResult> GetOptionsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();

        var people = await LoadPeopleAsync(connection, cancellationToken);
        var visible = people
            .Where(person => scope.VisibleUserIds.Contains(person.UserId))
            .OrderBy(person => person.Department)
            .ThenBy(person => person.FullName)
            .ToArray();

        return Results.Ok(new
        {
            currentUserId = scope.ActorUserId,
            canViewTeam = scope.VisibleUserIds.Count > 1,
            solutionArchitects = visible,
            people
        });
    }

    private static async Task<IResult> ListRecordsAsync(
        HttpContext context,
        string? state,
        Guid? solutionArchitectId,
        string? search,
        CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();

        if (solutionArchitectId.HasValue && !scope.VisibleUserIds.Contains(solutionArchitectId.Value))
            return Results.Forbid();

        var archived = string.Equals(state, "archived", StringComparison.OrdinalIgnoreCase);
        var sql = new StringBuilder("""
            SELECT id, record_number, solution_architect_user_id, solution_architect_name,
                   customer_id, customer_name, customer_is_manual, opportunity_id, project_name,
                   contract_type, gsd_template,
                   account_executive_user_id, account_executive_name,
                   resale_user_id, resale_name,
                   service_overview, scope_json::text, document_json::text, status,
                   created_at_utc, updated_at_utc, confirmed_at_utc, archived_at_utc
            FROM sow_gsd_records
            WHERE solution_architect_user_id = ANY(@visible_user_ids)
              AND status {0}
            """.Replace("{0}", archived ? "= 'Archived'" : "<> 'Archived'"));

        if (solutionArchitectId.HasValue)
            sql.AppendLine(" AND solution_architect_user_id = @solution_architect_id");
        if (!string.IsNullOrWhiteSpace(search))
            sql.AppendLine(" AND (record_number ILIKE @search OR customer_name ILIKE @search OR project_name ILIKE @search)");
        sql.AppendLine(" ORDER BY updated_at_utc DESC LIMIT 500");

        await using var command = new NpgsqlCommand(sql.ToString(), connection);
        command.Parameters.AddWithValue("visible_user_ids", scope.VisibleUserIds.ToArray());
        if (solutionArchitectId.HasValue)
            command.Parameters.AddWithValue("solution_architect_id", solutionArchitectId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            command.Parameters.AddWithValue("search", $"%{search.Trim()}%");

        var records = new List<SowGsdRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(ReadRecord(reader));

        return Results.Ok(records);
    }

    private static async Task<IResult> GetRecordAsync(HttpContext context, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();

        var record = await LoadRecordAsync(connection, id, cancellationToken);
        if (record is null)
            return Results.NotFound();
        if (!scope.VisibleUserIds.Contains(record.SolutionArchitectUserId))
            return Results.Forbid();

        return Results.Ok(record);
    }

    private static async Task<IResult> CreateRecordAsync(
        HttpContext context,
        SowGsdRecordRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();

        var solutionArchitectId = request.SolutionArchitectUserId ?? scope.ActorUserId;
        if (!scope.VisibleUserIds.Contains(solutionArchitectId))
            return Results.Forbid();

        var solutionArchitectName = await ResolvePersonNameAsync(connection, solutionArchitectId, cancellationToken)
            ?? request.SolutionArchitectName?.Trim()
            ?? string.Empty;
        var accountExecutiveName = await ResolveOptionalPersonNameAsync(connection, request.AccountExecutiveUserId, request.AccountExecutiveName, cancellationToken);
        var resaleName = await ResolveOptionalPersonNameAsync(connection, request.ResaleUserId, request.ResaleName, cancellationToken);

        var id = Guid.NewGuid();
        var recordNumber = $"SOWGSD-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        var contractType = NormalizeContractType(request.ContractType);
        var gsdTemplate = NormalizeTemplate(request.GsdTemplate);
        var scopeJson = NormalizeJson(request.Scope, "{\"phases\":[]}");
        var documentJson = NormalizeJson(request.Document, "{}");

        await using var command = new NpgsqlCommand("""
            INSERT INTO sow_gsd_records (
                id, record_number, solution_architect_user_id, solution_architect_name,
                customer_id, customer_name, customer_is_manual, opportunity_id, project_name,
                contract_type, gsd_template,
                account_executive_user_id, account_executive_name,
                resale_user_id, resale_name,
                service_overview, scope_json, document_json, status,
                created_by_user_id, updated_by_user_id)
            VALUES (
                @id, @record_number, @solution_architect_user_id, @solution_architect_name,
                @customer_id, @customer_name, @customer_is_manual, @opportunity_id, @project_name,
                @contract_type, @gsd_template,
                @account_executive_user_id, @account_executive_name,
                @resale_user_id, @resale_name,
                @service_overview, @scope_json::jsonb, @document_json::jsonb, 'Draft',
                @actor_user_id, @actor_user_id)
            """, connection);
        AddWriteParameters(command, id, recordNumber, solutionArchitectId, solutionArchitectName, request,
            accountExecutiveName, resaleName, contractType, gsdTemplate, scopeJson, documentJson, scope.ActorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        var created = await LoadRecordAsync(connection, id, cancellationToken);
        return Results.Created($"/api/sow-gsd-workspace/records/{id}", created);
    }

    private static async Task<IResult> UpdateRecordAsync(
        HttpContext context,
        Guid id,
        SowGsdRecordRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();

        var existing = await LoadRecordAsync(connection, id, cancellationToken);
        if (existing is null)
            return Results.NotFound();
        if (!scope.VisibleUserIds.Contains(existing.SolutionArchitectUserId))
            return Results.Forbid();
        if (existing.Status == "Archived")
            return Results.Conflict(new { error = "Archived SOW/GSD records must be restored before editing." });

        var solutionArchitectId = request.SolutionArchitectUserId ?? existing.SolutionArchitectUserId;
        if (!scope.VisibleUserIds.Contains(solutionArchitectId))
            return Results.Forbid();

        var solutionArchitectName = await ResolvePersonNameAsync(connection, solutionArchitectId, cancellationToken)
            ?? request.SolutionArchitectName?.Trim()
            ?? existing.SolutionArchitectName;
        var accountExecutiveName = await ResolveOptionalPersonNameAsync(connection, request.AccountExecutiveUserId, request.AccountExecutiveName, cancellationToken);
        var resaleName = await ResolveOptionalPersonNameAsync(connection, request.ResaleUserId, request.ResaleName, cancellationToken);
        var contractType = NormalizeContractType(request.ContractType);
        var gsdTemplate = NormalizeTemplate(request.GsdTemplate);
        var scopeJson = NormalizeJson(request.Scope, existing.ScopeJson);
        var documentJson = NormalizeJson(request.Document, existing.DocumentJson);

        await using var command = new NpgsqlCommand("""
            UPDATE sow_gsd_records
            SET solution_architect_user_id = @solution_architect_user_id,
                solution_architect_name = @solution_architect_name,
                customer_id = @customer_id,
                customer_name = @customer_name,
                customer_is_manual = @customer_is_manual,
                opportunity_id = @opportunity_id,
                project_name = @project_name,
                contract_type = @contract_type,
                gsd_template = @gsd_template,
                account_executive_user_id = @account_executive_user_id,
                account_executive_name = @account_executive_name,
                resale_user_id = @resale_user_id,
                resale_name = @resale_name,
                service_overview = @service_overview,
                scope_json = @scope_json::jsonb,
                document_json = @document_json::jsonb,
                status = CASE WHEN status = 'Confirmed' THEN 'Draft' ELSE status END,
                confirmed_at_utc = CASE WHEN status = 'Confirmed' THEN NULL ELSE confirmed_at_utc END,
                confirmed_by_user_id = CASE WHEN status = 'Confirmed' THEN NULL ELSE confirmed_by_user_id END,
                updated_at_utc = now(),
                updated_by_user_id = @actor_user_id
            WHERE id = @id
            """, connection);
        AddWriteParameters(command, id, existing.RecordNumber, solutionArchitectId, solutionArchitectName, request,
            accountExecutiveName, resaleName, contractType, gsdTemplate, scopeJson, documentJson, scope.ActorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return Results.Ok(await LoadRecordAsync(connection, id, cancellationToken));
    }

    private static async Task<IResult> ConfirmRecordAsync(HttpContext context, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();

        var record = await LoadRecordAsync(connection, id, cancellationToken);
        if (record is null)
            return Results.NotFound();
        if (!scope.VisibleUserIds.Contains(record.SolutionArchitectUserId))
            return Results.Forbid();
        if (record.Status == "Archived")
            return Results.Conflict(new { error = "Restore this record before confirming it." });

        var validation = ValidateForConfirmation(record);
        if (validation.Count > 0)
            return Results.BadRequest(new { error = "The SOW/GSD is not ready to confirm.", missing = validation });

        await using var command = new NpgsqlCommand("""
            UPDATE sow_gsd_records
            SET status = 'Confirmed',
                confirmed_at_utc = now(), confirmed_by_user_id = @actor,
                updated_at_utc = now(), updated_by_user_id = @actor
            WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("actor", scope.ActorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Results.Ok(await LoadRecordAsync(connection, id, cancellationToken));
    }

    private static Task<IResult> ArchiveRecordAsync(HttpContext context, Guid id, CancellationToken cancellationToken) =>
        SetArchiveStateAsync(context, id, archive: true, cancellationToken: cancellationToken);

    private static Task<IResult> RestoreRecordAsync(HttpContext context, Guid id, CancellationToken cancellationToken) =>
        SetArchiveStateAsync(context, id, archive: false, cancellationToken: cancellationToken);

    private static async Task<IResult> SetArchiveStateAsync(
        HttpContext context,
        Guid id,
        bool archive,
        CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();

        var record = await LoadRecordAsync(connection, id, cancellationToken);
        if (record is null)
            return Results.NotFound();
        if (!scope.VisibleUserIds.Contains(record.SolutionArchitectUserId))
            return Results.Forbid();

        var sql = archive
            ? """
              UPDATE sow_gsd_records
              SET status = 'Archived', archived_at_utc = now(), archived_by_user_id = @actor,
                  updated_at_utc = now(), updated_by_user_id = @actor
              WHERE id = @id
              """
            : """
              UPDATE sow_gsd_records
              SET status = CASE WHEN confirmed_at_utc IS NULL THEN 'Draft' ELSE 'Confirmed' END,
                  archived_at_utc = NULL, archived_by_user_id = NULL,
                  updated_at_utc = now(), updated_by_user_id = @actor
              WHERE id = @id
              """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("actor", scope.ActorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Results.Ok(await LoadRecordAsync(connection, id, cancellationToken));
    }

    private static async Task<IResult> DownloadSowAsync(HttpContext context, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();
        var record = await LoadRecordAsync(connection, id, cancellationToken);
        if (record is null)
            return Results.NotFound();
        if (!scope.VisibleUserIds.Contains(record.SolutionArchitectUserId))
            return Results.Forbid();
        if (record.Status != "Confirmed")
            return Results.Conflict(new { error = "Confirm the SOW/GSD before downloading final documents." });

        var bytes = BuildSowDocx(record);
        var fileName = $"{SafeFileName(record.CustomerName)}-{SafeFileName(record.ProjectName)}-{record.RecordNumber}-SOW.docx";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
    }

    private static async Task<IResult> DownloadGsdAsync(HttpContext context, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var scope = await ResolveScopeAsync(connection, context, cancellationToken);
        if (scope is null)
            return Results.Unauthorized();
        var record = await LoadRecordAsync(connection, id, cancellationToken);
        if (record is null)
            return Results.NotFound();
        if (!scope.VisibleUserIds.Contains(record.SolutionArchitectUserId))
            return Results.Forbid();
        if (record.Status != "Confirmed")
            return Results.Conflict(new { error = "Confirm the SOW/GSD before downloading final documents." });

        var bytes = BuildGsdWorkbook(record);
        var fileName = $"{SafeFileName(record.CustomerName)}-{SafeFileName(record.ProjectName)}-{record.RecordNumber}-GSD.xlsx";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static async Task<UserScope?> ResolveScopeAsync(
        NpgsqlConnection connection,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        var effective = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId") ?? actual;
        if (!effective.HasValue)
            return null;

        var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context)
            || (actual.HasValue && actual.Value != effective.Value);
        if (isViewAs)
            return new UserScope(effective.Value, new HashSet<Guid> { effective.Value });

        await using var command = new NpgsqlCommand("""
            WITH RECURSIVE reporting_tree(user_id) AS (
                SELECT @actor::uuid
                UNION
                SELECT rr.employee_user_id
                FROM reporting_relationships rr
                JOIN reporting_tree manager_scope
                  ON rr.primary_manager_user_id = manager_scope.user_id
                  OR EXISTS (
                      SELECT 1
                      FROM reporting_relationship_secondary_managers sm
                      WHERE sm.reporting_relationship_id = rr.id
                        AND sm.manager_user_id = manager_scope.user_id)
                WHERE (rr.start_date IS NULL OR rr.start_date <= CURRENT_DATE)
                  AND (rr.end_date IS NULL OR rr.end_date >= CURRENT_DATE)
            )
            SELECT DISTINCT user_id FROM reporting_tree
            """, connection);
        command.Parameters.AddWithValue("actor", effective.Value);

        var visible = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            visible.Add(reader.GetGuid(0));
        visible.Add(effective.Value);
        return new UserScope(effective.Value, visible);
    }

    private static async Task<PersonOption[]> LoadPeopleAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT u.user_id, COALESCE(NULLIF(trim(u.full_name), ''), u.email) AS full_name,
                   u.email, COALESCE(u.department, '') AS department,
                   COALESCE(NULLIF(trim(u.job_title), ''), NULLIF(trim(u.role_title), ''), '') AS job_title
            FROM app_users u
            WHERE u.is_active = TRUE
            ORDER BY lower(COALESCE(NULLIF(trim(u.full_name), ''), u.email))
            LIMIT 1500
            """, connection);
        var people = new List<PersonOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            people.Add(new PersonOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }
        return people.ToArray();
    }

    private static async Task<string?> ResolvePersonNameAsync(NpgsqlConnection connection, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(NULLIF(trim(full_name), ''), email)
            FROM app_users
            WHERE user_id = @user_id AND is_active = TRUE
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value?.ToString();
    }

    private static async Task<string> ResolveOptionalPersonNameAsync(
        NpgsqlConnection connection,
        Guid? userId,
        string? suppliedName,
        CancellationToken cancellationToken)
    {
        if (userId.HasValue)
            return await ResolvePersonNameAsync(connection, userId.Value, cancellationToken)
                ?? suppliedName?.Trim()
                ?? string.Empty;
        return suppliedName?.Trim() ?? string.Empty;
    }

    private static async Task<SowGsdRecord?> LoadRecordAsync(NpgsqlConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, record_number, solution_architect_user_id, solution_architect_name,
                   customer_id, customer_name, customer_is_manual, opportunity_id, project_name,
                   contract_type, gsd_template,
                   account_executive_user_id, account_executive_name,
                   resale_user_id, resale_name,
                   service_overview, scope_json::text, document_json::text, status,
                   created_at_utc, updated_at_utc, confirmed_at_utc, archived_at_utc
            FROM sow_gsd_records
            WHERE id = @id
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static SowGsdRecord ReadRecord(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        reader.GetBoolean(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetGuid(11),
        reader.GetString(12),
        reader.IsDBNull(13) ? null : reader.GetGuid(13),
        reader.GetString(14),
        reader.GetString(15),
        reader.GetString(16),
        reader.GetString(17),
        reader.GetString(18),
        reader.GetDateTime(19),
        reader.GetDateTime(20),
        reader.IsDBNull(21) ? null : reader.GetDateTime(21),
        reader.IsDBNull(22) ? null : reader.GetDateTime(22));

    private static void AddWriteParameters(
        NpgsqlCommand command,
        Guid id,
        string recordNumber,
        Guid solutionArchitectId,
        string solutionArchitectName,
        SowGsdRecordRequest request,
        string accountExecutiveName,
        string resaleName,
        string contractType,
        string gsdTemplate,
        string scopeJson,
        string documentJson,
        Guid actorUserId)
    {
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("record_number", recordNumber);
        command.Parameters.AddWithValue("solution_architect_user_id", solutionArchitectId);
        command.Parameters.AddWithValue("solution_architect_name", solutionArchitectName);
        command.Parameters.AddWithValue("customer_id", (object?)request.CustomerId?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("customer_name", request.CustomerName?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("customer_is_manual", request.CustomerIsManual);
        command.Parameters.AddWithValue("opportunity_id", (object?)request.OpportunityId?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("project_name", request.ProjectName?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("contract_type", contractType);
        command.Parameters.AddWithValue("gsd_template", gsdTemplate);
        command.Parameters.AddWithValue("account_executive_user_id", (object?)request.AccountExecutiveUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("account_executive_name", accountExecutiveName);
        command.Parameters.AddWithValue("resale_user_id", (object?)request.ResaleUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("resale_name", resaleName);
        command.Parameters.AddWithValue("service_overview", request.ServiceOverview?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("scope_json", scopeJson);
        command.Parameters.AddWithValue("document_json", documentJson);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
    }

    private static List<string> ValidateForConfirmation(SowGsdRecord record)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(record.CustomerName)) missing.Add("Customer");
        if (string.IsNullOrWhiteSpace(record.ProjectName)) missing.Add("Project / SOW name");
        if (string.IsNullOrWhiteSpace(record.SolutionArchitectName)) missing.Add("Solution Architect");
        if (string.IsNullOrWhiteSpace(record.AccountExecutiveName)) missing.Add("Account Executive");
        if (string.IsNullOrWhiteSpace(record.ResaleName)) missing.Add("Resale person");
        if (string.IsNullOrWhiteSpace(record.ServiceOverview)) missing.Add("Services Overview");

        var phases = ReadPhases(record.ScopeJson);
        foreach (var phaseName in RequiredPhases)
        {
            if (!phases.Any(phase => string.Equals(phase.Name, phaseName, StringComparison.OrdinalIgnoreCase)))
                missing.Add($"{phaseName} scope");
        }
        if (phases.Sum(phase => phase.Hours) <= 0)
            missing.Add("Level of effort hours");
        return missing;
    }

    private static string NormalizeContractType(string? value) =>
        string.Equals(value?.Trim(), "Fixed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "FP", StringComparison.OrdinalIgnoreCase)
            ? "Fixed"
            : "T&M";

    private static string NormalizeTemplate(string? value) =>
        string.Equals(value?.Trim(), "ToyotaHyundai", StringComparison.OrdinalIgnoreCase)
            ? "ToyotaHyundai"
            : "Standard";

    private static string NormalizeJson(JsonElement value, string fallback)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return fallback;
        return value.GetRawText();
    }

    private static List<PhaseWork> ReadPhases(string scopeJson)
    {
        var result = new List<PhaseWork>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(scopeJson) ? "{}" : scopeJson);
            if (!doc.RootElement.TryGetProperty("phases", out var phases) || phases.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var phase in phases.EnumerateArray())
            {
                var name = GetString(phase, "name") ?? GetString(phase, "key") ?? string.Empty;
                name = NormalizePhaseName(name);
                var description = GetString(phase, "description") ?? string.Empty;
                var suggested = GetDecimal(phase, "suggestedHours");
                var hours = GetDecimal(phase, "hours");
                var activities = new List<string>();
                if (phase.TryGetProperty("activities", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var text = item.ValueKind == JsonValueKind.String
                            ? item.GetString()
                            : GetString(item, "text") ?? GetString(item, "title") ?? GetString(item, "description");
                        if (!string.IsNullOrWhiteSpace(text)) activities.Add(text.Trim());
                    }
                }
                result.Add(new PhaseWork(name, description, suggested, hours, activities));
            }
        }
        catch (JsonException)
        {
            // Preserve record accessibility even if an old draft contains malformed JSON.
        }
        return result;
    }

    private static string NormalizePhaseName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Contains("discover") || normalized == "plan") return "Plan";
        if (normalized.Contains("design")) return "Design";
        if (normalized.Contains("implement")) return "Implement";
        if (normalized.Contains("validat")) return "Validate";
        if (normalized.Contains("release") || normalized.Contains("handoff") || normalized.Contains("closeout")) return "Release";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Trim());
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal GetDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)) return number;
        return 0;
    }

    private static byte[] BuildGsdWorkbook(SowGsdRecord record)
    {
        var isToyotaHyundai = record.GsdTemplate == "ToyotaHyundai";
        var templateFile = isToyotaHyundai ? "Toyota-Hyundai-GSD.xlsx" : "Standard-GSD.xlsx";
        var templatePath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "templates", "sow-gsd", templateFile);
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"GSD template was not found: {templateFile}", templatePath);

        using var workbook = new XLWorkbook(templatePath);
        var summary = workbook.Worksheet("Summary");
        summary.Cell("C11").Value = record.CustomerName;
        summary.Cell("C12").Value = record.ProjectName;
        summary.Cell("C13").Value = isToyotaHyundai && record.ContractType == "Fixed" ? "FP" : record.ContractType;
        summary.Cell("C14").Value = record.AccountExecutiveName;
        summary.Cell("C15").Value = record.SolutionArchitectName;

        if (isToyotaHyundai)
        {
            summary.Cell("B16").Value = $"Resale: {record.ResaleName}\nSOW/GSD ID: {record.RecordNumber}";
            summary.Cell("B16").Style.Alignment.WrapText = true;
        }
        else
        {
            summary.Cell("B16").Value = "Resale";
            summary.Cell("C16").Value = record.ResaleName;
            summary.Cell("B22").Value = $"SOW/GSD ID: {record.RecordNumber}";
            summary.Cell("B24").Value = record.ServiceOverview;
            summary.Cell("B24").Style.Alignment.WrapText = true;
        }

        var phases = ReadPhases(record.ScopeJson);
        var sheetMap = isToyotaHyundai
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
              {
                  ["Plan"] = "Plan - I", ["Design"] = "Design - II", ["Implement"] = "Implement - III",
                  ["Validate"] = "Validate - IV", ["Release"] = "Release - V"
              }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
              {
                  ["Plan"] = "Plan", ["Design"] = "Design", ["Implement"] = "Implement",
                  ["Validate"] = "Validate", ["Release"] = "Release"
              };

        foreach (var phaseName in RequiredPhases)
        {
            if (!sheetMap.TryGetValue(phaseName, out var sheetName) || !workbook.TryGetWorksheet(sheetName, out var sheet))
                continue;
            var phase = phases.FirstOrDefault(item => string.Equals(item.Name, phaseName, StringComparison.OrdinalIgnoreCase))
                ?? new PhaseWork(phaseName, string.Empty, 0, 0, []);
            PopulatePhaseSheet(sheet, phase, isToyotaHyundai);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void PopulatePhaseSheet(IXLWorksheet sheet, PhaseWork phase, bool isToyotaHyundai)
    {
        const int firstTaskRow = 4;
        const int lastTaskRow = 99;
        for (var row = firstTaskRow; row <= lastTaskRow; row++)
        {
            sheet.Cell(row, 1).Clear(XLClearOptions.Contents);
            sheet.Cell(row, 2).Clear(XLClearOptions.Contents);
            sheet.Cell(row, 3).Clear(XLClearOptions.Contents);
            sheet.Cell(row, 5).Clear(XLClearOptions.Contents);
            sheet.Cell(row, 6).Clear(XLClearOptions.Contents);
            sheet.Cell(row, isToyotaHyundai ? 9 : 7).Clear(XLClearOptions.Contents);
        }

        var activities = phase.Activities.Where(item => !string.IsNullOrWhiteSpace(item)).Take(lastTaskRow - firstTaskRow + 1).ToList();
        if (activities.Count == 0)
            activities.Add(string.IsNullOrWhiteSpace(phase.Description) ? $"{phase.Name} activities" : phase.Description);
        var allocations = AllocateHours(phase.Hours, activities.Count);

        for (var index = 0; index < activities.Count; index++)
        {
            var row = firstTaskRow + index;
            sheet.Cell(row, 1).Value = activities[index];
            sheet.Cell(row, 2).Value = allocations[index];
            sheet.Cell(row, 3).Value = 0;
            sheet.Cell(row, 5).Value = "Consulting Eng";
            sheet.Cell(row, 6).Value = "No";
            sheet.Cell(row, isToyotaHyundai ? 9 : 7).Value = index == 0 ? phase.Description : string.Empty;
        }
    }

    private static decimal[] AllocateHours(decimal total, int count)
    {
        if (count <= 0) return [];
        if (count == 1) return [Math.Max(0, total)];
        total = Math.Max(0, total);
        var baseHours = Math.Floor((total / count) * 4m) / 4m;
        var values = Enumerable.Repeat(baseHours, count).ToArray();
        values[^1] = total - baseHours * (count - 1);
        return values;
    }

    private static byte[] BuildSowDocx(SowGsdRecord record)
    {
        var phases = ReadPhases(record.ScopeJson);
        var document = ParseDocumentSections(record.DocumentJson);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipText(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """);
            WriteZipText(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            WriteZipText(archive, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            WriteZipText(archive, "word/styles.xml", BuildStylesXml());
            WriteZipText(archive, "word/document.xml", BuildDocumentXml(record, phases, document));
        }
        return output.ToArray();
    }

    private static string BuildStylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:sz w:val="22"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="36"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="28"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>
        </w:styles>
        """;

    private static string BuildDocumentXml(SowGsdRecord record, IReadOnlyList<PhaseWork> phases, DocumentSections sections)
    {
        var body = new StringBuilder();
        body.Append(Paragraph("STATEMENT OF WORK", "Title"));
        body.Append(Paragraph(record.ProjectName, "Heading1"));
        body.Append(Paragraph($"SOW/GSD ID: {record.RecordNumber}"));
        body.Append(Paragraph($"Customer: {record.CustomerName}"));
        body.Append(Paragraph($"Contract Type: {record.ContractType}"));
        body.Append(Paragraph($"Solution Architect: {record.SolutionArchitectName}"));
        body.Append(Paragraph($"Account Executive: {record.AccountExecutiveName}"));
        body.Append(Paragraph($"Resale: {record.ResaleName}"));

        body.Append(Paragraph("2.1 Services Overview", "Heading1"));
        body.Append(Paragraph(record.ServiceOverview));
        body.Append(Paragraph("2.2 Services Description", "Heading1"));
        foreach (var phaseName in RequiredPhases)
        {
            var phase = phases.FirstOrDefault(item => string.Equals(item.Name, phaseName, StringComparison.OrdinalIgnoreCase));
            body.Append(Paragraph(phaseName, "Heading2"));
            if (phase is null) continue;
            if (!string.IsNullOrWhiteSpace(phase.Description)) body.Append(Paragraph(phase.Description));
            foreach (var activity in phase.Activities) body.Append(Bullet(activity));
        }

        body.Append(Paragraph("2.3 Deliverables", "Heading1"));
        AppendLines(body, sections.Deliverables);
        body.Append(Paragraph("2.4 Detailed Exclusions", "Heading1"));
        AppendLines(body, sections.Exclusions);
        body.Append(Paragraph("2.5 Client Involvement", "Heading1"));
        AppendLines(body, sections.ClientInvolvement);
        if (sections.Assumptions.Count > 0)
        {
            body.Append(Paragraph("Assumptions", "Heading1"));
            AppendLines(body, sections.Assumptions);
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>{body}<w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1080" w:right="1080" w:bottom="1080" w:left="1080"/></w:sectPr></w:body>
            </w:document>
            """;
    }

    private static void AppendLines(StringBuilder body, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            body.Append(Paragraph("None specified."));
            return;
        }
        foreach (var line in lines) body.Append(Bullet(line));
    }

    private static string Paragraph(string value, string? style = null)
    {
        var styleXml = string.IsNullOrWhiteSpace(style) ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>";
        return $"<w:p>{styleXml}<w:r><w:t xml:space=\"preserve\">{EscapeXml(value)}</w:t></w:r></w:p>";
    }

    private static string Bullet(string value) =>
        $"<w:p><w:pPr><w:ind w:left=\"360\"/></w:pPr><w:r><w:t xml:space=\"preserve\">• {EscapeXml(value)}</w:t></w:r></w:p>";

    private static string EscapeXml(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static void WriteZipText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.TrimStart());
    }

    private static DocumentSections ParseDocumentSections(string documentJson)
    {
        var deliverables = new List<string>();
        var exclusions = new List<string>();
        var client = new List<string>();
        var assumptions = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(documentJson) ? "{}" : documentJson);
            ReadLines(doc.RootElement, "deliverablesText", deliverables);
            ReadLines(doc.RootElement, "exclusionsText", exclusions);
            ReadLines(doc.RootElement, "clientInvolvementText", client);
            ReadLines(doc.RootElement, "assumptionsText", assumptions);
        }
        catch (JsonException)
        {
            // Older drafts remain downloadable after confirmation if their section JSON was empty.
        }
        return new DocumentSections(deliverables, exclusions, client, assumptions);
    }

    private static void ReadLines(JsonElement root, string property, List<string> target)
    {
        if (!root.TryGetProperty(property, out var value)) return;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) target.Add(item.GetString()!.Trim());
            return;
        }
        if (value.ValueKind != JsonValueKind.String) return;
        foreach (var line in (value.GetString() ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var clean = line.Trim().TrimStart('•', '-', '*').Trim();
            if (!string.IsNullOrWhiteSpace(clean)) target.Add(clean);
        }
    }

    private static string SafeFileName(string? value)
    {
        var input = string.IsNullOrWhiteSpace(value) ? "Document" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
            builder.Append(invalid.Contains(ch) ? '-' : ch);
        return builder.ToString().Replace(' ', '-');
    }

    private sealed record UserScope(Guid ActorUserId, HashSet<Guid> VisibleUserIds);
    private sealed record PersonOption(Guid UserId, string FullName, string Email, string Department, string JobTitle);
    private sealed record PhaseWork(string Name, string Description, decimal SuggestedHours, decimal Hours, List<string> Activities);
    private sealed record DocumentSections(List<string> Deliverables, List<string> Exclusions, List<string> ClientInvolvement, List<string> Assumptions);

    internal sealed record SowGsdRecordRequest(
        Guid? SolutionArchitectUserId,
        string? SolutionArchitectName,
        string? CustomerId,
        string? CustomerName,
        bool CustomerIsManual,
        string? OpportunityId,
        string? ProjectName,
        string? ContractType,
        string? GsdTemplate,
        Guid? AccountExecutiveUserId,
        string? AccountExecutiveName,
        Guid? ResaleUserId,
        string? ResaleName,
        string? ServiceOverview,
        JsonElement Scope,
        JsonElement Document);

    internal sealed record SowGsdRecord(
        Guid Id,
        string RecordNumber,
        Guid SolutionArchitectUserId,
        string SolutionArchitectName,
        string? CustomerId,
        string CustomerName,
        bool CustomerIsManual,
        string? OpportunityId,
        string ProjectName,
        string ContractType,
        string GsdTemplate,
        Guid? AccountExecutiveUserId,
        string AccountExecutiveName,
        Guid? ResaleUserId,
        string ResaleName,
        string ServiceOverview,
        string ScopeJson,
        string DocumentJson,
        string Status,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? ConfirmedAtUtc,
        DateTime? ArchivedAtUtc);
}

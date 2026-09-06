using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed partial class CelarAiInternalDataService
{
    // People are constrained to the same authoritative scope used for existing
    // deterministic workload answers. No salary, credentials or private profile fields.
    private static readonly string EnterprisePeopleSql = ScopeCte + """
        , visible_people AS (
            SELECT user_id FROM authorized_people
            UNION SELECT user_id FROM team_members WHERE @can_view_team_scope
            UNION SELECT user_id FROM app_users WHERE @is_broad_scope AND is_active
        ), records AS (
            SELECT DISTINCT person.user_id, person.display_name, person.email,
                person.team_name, person.department_name,
                relationship.manager_user_id, manager.display_name AS manager_name,
                relationship.team_lead_user_id, lead.display_name AS team_lead_name,
                relationship.effective_start_date, relationship.effective_end_date
            FROM app_users person
            JOIN visible_people allowed ON allowed.user_id=person.user_id
            LEFT JOIN reporting_relationships relationship ON relationship.employee_user_id=person.user_id
                AND relationship.effective_start_date<=CURRENT_DATE
                AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
            LEFT JOIN app_users manager ON manager.user_id=relationship.manager_user_id
            LEFT JOIN app_users lead ON lead.user_id=relationship.team_lead_user_id
            WHERE person.is_active
        )
        SELECT jsonb_build_object('status','loaded', 'scope','authorized active people and current reporting relationships',
            'totalPeople',(SELECT count(DISTINCT user_id) FROM records),
            'totalRelationshipRows',(SELECT count(*) FROM records),
            'hasMore',(SELECT count(*)>200 FROM records),
            'records',COALESCE((SELECT jsonb_agg(row_to_json(page)) FROM
                (SELECT * FROM records ORDER BY display_name,user_id,manager_user_id LIMIT 200) page),'[]'::jsonb))::text;
        """;

    private const string EnterpriseOwnTimeSql = """
        WITH records AS (
            SELECT entry.work_date,entry.status,entry.billable,
                SUM(entry.hours)::numeric AS hours, COUNT(*)::bigint AS entry_count
            FROM time_entries entry
            WHERE entry.user_id=@effective_user_id AND entry.work_date BETWEEN @period_start AND @period_end
            GROUP BY entry.work_date,entry.status,entry.billable
        )
        SELECT jsonb_build_object('status','loaded','scope','effective user only; all recorded statuses retained separately',
            'startDate',@period_start::date,'endDate',@period_end::date,
            'totalRecordedHours',COALESCE((SELECT SUM(hours) FROM records),0),
            'entryCount',COALESCE((SELECT SUM(entry_count) FROM records),0),
            'records',COALESCE((SELECT jsonb_agg(row_to_json(records) ORDER BY work_date,status,billable) FROM records),'[]'::jsonb))::text;
        """;

    public async Task<PulseAiSystemToolResult> ReadEnterpriseEvidenceAsync(
        Guid effectiveUserId, PulseAiSystemAccess access, PulseAiSystemToolDefinition definition,
        string question, string? timeZone, int maximumCharacters, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observed = DateTimeOffset.UtcNow;
        PulseAiSystemToolResult Result(string status, int httpStatus, string code, string body, string summary) =>
            new(definition.Code,definition.Name,definition.ModuleCode,definition.ModuleName,"INTERNAL",definition.Path,
                status,httpStatus,(decimal)stopwatch.Elapsed.TotalMilliseconds,body.Length,code,body,[definition.Purpose,summary],observed);
        if (!access.IsActive || access.UserId != effectiveUserId)
            return Result("forbidden",403,"effective_scope_required","","An active matching effective-user scope is required.");
        var time = definition.Code == "enterprise_own_time";
        if (!time && definition.Code != "enterprise_people")
            return Result("failed",400,"adapter_not_registered","","Only registered read adapters may execute.");
        if (time && !access.IsSuperAdministrator && !access.PermissionCodes.Contains("TIME_VIEW"))
            return Result("forbidden",403,"time_view_required","","The effective user lacks TIME_VIEW.");
        var period = time ? CelarAiEnterprisePeriod.Parse(question, timeZone) : null;
        if (time && period is null)
            return Result("incomplete",400,"explicit_period_required","","Specify an ISO date range of at most 366 days, or this/last week, month, quarter or year.");
        try
        {
            await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY",connection,transaction))
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
            await using var command = new NpgsqlCommand(time ? EnterpriseOwnTimeSql : EnterprisePeopleSql,connection,transaction) { CommandTimeout = 12 };
            AddScopeParameters(command,effectiveUserId,access);
            if (period is not null)
            {
                command.Parameters.AddWithValue("period_start",period.Start);
                command.Parameters.AddWithValue("period_end",period.End);
            }
            var body = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "";
            if (body.Length>maximumCharacters)
                return Result("incomplete",200,"tool_response_incomplete","","The source exceeds the answer budget; no partial body was promoted.");
            var diagnostic = CelarAiEnterpriseEvidencePolicy.ValidateResponse(body);
            if (diagnostic.Length>0) return Result("incomplete",200,diagnostic,"","The source is incomplete; narrow the requested scope.");
            using var evidence = JsonDocument.Parse(body);
            var summary = period is null
                ? $"Current authorized directory: {evidence.RootElement.GetProperty("totalPeople").GetInt64()} active people; current reporting relationships included."
                : $"Your recorded hours for {period.Start:yyyy-MM-dd} through {period.End:yyyy-MM-dd}: {evidence.RootElement.GetProperty("totalRecordedHours").GetDecimal():0.##} hours across {evidence.RootElement.GetProperty("entryCount").GetInt64()} entries. This includes all recorded statuses, not only approved time; status and billability are separated in the evidence.";
            return Result("succeeded",200,"",body,summary);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            // Never promote SQL text, connection details or DB error bodies.
            return Result("failed",503,"enterprise_source_unavailable","","The authoritative source is unavailable; values remain unknown.");
        }
    }
}

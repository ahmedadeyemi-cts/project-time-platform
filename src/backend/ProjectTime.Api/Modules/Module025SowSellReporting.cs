using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class Module025SowGsdModule
{
    // Aggregation precedes pagination. Downloads are intentionally absent from
    // production-output metrics. Date filters apply to each metric's own event.
    private const string SowReportMeasureSql = """
        WITH measured AS (
            SELECT e.*,
                (SELECT count(DISTINCT COALESCE(NULLIF(ev.evidence_json->>'generationId',''),ev.event_id::text))
                 FROM module025_sow_gsd_events ev WHERE ev.engagement_id=e.engagement_id
                   AND ev.event_type='ai_generation_completed'
                   AND (@from::timestamptz IS NULL OR ev.created_at>=@from)
                   AND (@until::timestamptz IS NULL OR ev.created_at<@until)) AS generated_count,
                (SELECT count(DISTINCT COALESCE(NULLIF(ev.evidence_json->>'generationId',''),ev.event_id::text))
                 FROM module025_sow_gsd_events ev WHERE ev.engagement_id=e.engagement_id
                   AND ev.event_type='ai_generation_failed'
                   AND (@from::timestamptz IS NULL OR ev.created_at>=@from)
                   AND (@until::timestamptz IS NULL OR ev.created_at<@until)) AS failed_count,
                (SELECT count(*) FROM module025_sow_gsd_versions v WHERE v.engagement_id=e.engagement_id
                   AND (@from::timestamptz IS NULL OR v.created_at>=@from)
                   AND (@until::timestamptz IS NULL OR v.created_at<@until)) AS version_count,
                (SELECT count(*) FROM module025_sow_sell_submissions s WHERE s.engagement_id=e.engagement_id
                   AND s.runtime_environment=@environment
                   AND (@from::timestamptz IS NULL OR s.created_at>=@from)
                   AND (@until::timestamptz IS NULL OR s.created_at<@until)) AS requested_count,
                (SELECT count(*) FROM module025_sow_sell_receipts r
                 JOIN module025_sow_sell_submissions s USING(submission_id) WHERE s.engagement_id=e.engagement_id
                   AND s.runtime_environment=@environment
                   AND (@from::timestamptz IS NULL OR r.verified_at>=@from)
                   AND (@until::timestamptz IS NULL OR r.verified_at<@until)) AS sent_count,
                (SELECT count(*) FROM module025_sow_sell_submissions s
                 JOIN module025_sow_sell_dispatch d USING(submission_id) WHERE s.engagement_id=e.engagement_id
                   AND s.runtime_environment=@environment AND d.sell_status='blocked'
                   AND (@from::timestamptz IS NULL OR s.created_at>=@from)
                   AND (@until::timestamptz IS NULL OR s.created_at<@until)) AS blocked_count
            FROM module025_sow_gsd_engagements e
            WHERE (@all OR e.owner_user_id=ANY(@owners))
              AND (@search='' OR e.customer_name ILIKE @pattern OR e.engagement_number ILIKE @pattern OR e.service_overview ILIKE @pattern)
        ), matched AS (
            SELECT * FROM measured
            WHERE ((@from::timestamptz IS NULL OR created_at>=@from) AND (@until::timestamptz IS NULL OR created_at<@until))
               OR generated_count>0 OR failed_count>0 OR version_count>0 OR requested_count>0 OR sent_count>0
        )
        """;

    private static async Task<IResult> SowRegisterAsync(Guid? ownerUserId, string? search, string? fromDate, string? toDate,
        int? page, string? format, HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        if (page is < 1 or > 100000 || format is not (null or "json" or "csv")) return Results.BadRequest(new { message = "Invalid report page or format." });
        if (!SowReportDate(fromDate, false, out var from) || !SowReportDate(toDate, true, out var until)
            || (from.HasValue && until.HasValue && from.Value >= until.Value))
            return Results.BadRequest(new { message = "Use a valid YYYY-MM-DD date range. Dates are UTC; the end date is inclusive." });
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        if (ownerUserId.HasValue && !access.CanViewOwned(ownerUserId.Value)) return Forbidden("module025_owner_scope");
        var all = access.IsAdministrator && !access.IsViewAs && !ownerUserId.HasValue;
        var owners = ownerUserId.HasValue ? new[] { ownerUserId.Value }
            : access.VisibleSolutionArchitectIds.Append(access.EffectiveUserId).Distinct().ToArray();
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(context) ?? string.Empty;
        if (environment is not ("test" or "production")) return StateConflict("environment_required", "Resolve the governed runtime environment before reporting SELL submissions.");
        var searchText = Clean(search, MaximumSearchLength);
        var pageNumber = page ?? 1;
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var command = new NpgsqlCommand(SowReportMeasureSql + sql, connection, transaction);
            command.Parameters.AddWithValue("all", all);
            command.Parameters.AddWithValue("owners", owners);
            command.Parameters.AddWithValue("search", searchText);
            command.Parameters.AddWithValue("pattern", $"%{searchText}%");
            command.Parameters.AddWithValue("environment", environment);
            command.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = (object?)from ?? DBNull.Value;
            command.Parameters.Add("until", NpgsqlDbType.TimestampTz).Value = (object?)until ?? DBNull.Value;
            return command;
        }
        var statistics = new List<JsonElement>();
        await using (var summary = Query("""
            SELECT jsonb_build_object('ownerUserId',owner_user_id,'ownerDisplayName',max(owner_display_name),
                'recordsCreated',count(*) FILTER (WHERE (@from::timestamptz IS NULL OR created_at>=@from) AND (@until::timestamptz IS NULL OR created_at<@until)),
                'uniqueSowsGenerated',count(*) FILTER (WHERE generated_count>0),
                'successfulGenerationRuns',sum(generated_count),'failedGenerationRuns',sum(failed_count),
                'releasedVersions',sum(version_count),'uniqueSowsSent',count(*) FILTER (WHERE sent_count>0),
                'successfulVersionSubmissions',sum(sent_count),'submissionRequests',sum(requested_count),
                'blockedSubmissions',sum(blocked_count),'matchingRecords',count(*))::text
            FROM matched GROUP BY owner_user_id ORDER BY max(owner_display_name),owner_user_id;
            """))
        await using (var reader = await summary.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) statistics.Add(ParseJson(reader.GetString(0), JsonValueKind.Object));

        if (format == "csv")
        {
            await transaction.CommitAsync(cancellationToken);
            var fields = new[] { "ownerUserId", "ownerDisplayName", "recordsCreated", "uniqueSowsGenerated", "successfulGenerationRuns", "failedGenerationRuns", "releasedVersions", "uniqueSowsSent", "successfulVersionSubmissions", "submissionRequests", "blockedSubmissions" };
            var csv = new StringBuilder("\uFEFF");
            csv.AppendLine(string.Join(',', new[] { "Runtime environment", "From UTC date", "Through UTC date" }.Concat(fields).Select(Module025SowSellPolicy.Csv)));
            foreach (var row in statistics)
            {
                var values = new[] { environment, fromDate ?? "All time", toDate ?? "All time" }
                    .Concat(fields.Select(field => row.TryGetProperty(field, out var value) ? value.ToString() : string.Empty));
                csv.AppendLine(string.Join(',', values.Select(Module025SowSellPolicy.Csv)));
            }
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "sow-sa-production-report.csv");
        }
        var records = new List<JsonElement>();
        await using (var rows = Query("""
            SELECT jsonb_build_object('engagementId',m.engagement_id,'engagementNumber',m.engagement_number,
                'ownerUserId',m.owner_user_id,'ownerDisplayName',m.owner_display_name,'customerName',m.customer_name,
                'status',m.status,'revision',m.revision,'createdAt',m.created_at,'updatedAt',m.updated_at,
                'generationRunsInWindow',m.generated_count,'successfulSubmissionsInWindow',m.sent_count,
                'latestVersionNumber',(SELECT max(v.version_number) FROM module025_sow_gsd_versions v WHERE v.engagement_id=m.engagement_id),
                'lastSellStatus',(SELECT d.sell_status FROM module025_sow_sell_dispatch d
                    JOIN module025_sow_sell_submissions s USING(submission_id)
                    WHERE s.engagement_id=m.engagement_id AND s.runtime_environment=@environment ORDER BY s.created_at DESC LIMIT 1),
                'lastSellVerifiedAt',(SELECT max(r.verified_at) FROM module025_sow_sell_receipts r
                    JOIN module025_sow_sell_submissions s USING(submission_id)
                    WHERE s.engagement_id=m.engagement_id AND s.runtime_environment=@environment))::text
            FROM matched m ORDER BY m.updated_at DESC,m.engagement_id LIMIT 101 OFFSET @offset;
            """))
        {
            rows.Parameters.AddWithValue("offset", (pageNumber - 1) * 100);
            await using var reader = await rows.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) records.Add(ParseJson(reader.GetString(0), JsonValueKind.Object));
        }
        await transaction.CommitAsync(cancellationToken);
        context.Response.Headers.CacheControl = "private, no-store";
        return Results.Ok(new
        {
            status = "module025_sow_register", runtimeEnvironment = environment,
            fromDate, toDate, dateBoundary = "UTC, inclusive date range; metrics use their own event timestamps",
            statistics, records = records.Take(100), page = pageNumber, hasMore = records.Count > 100,
            totalRecords = statistics.Sum(row => row.GetProperty("matchingRecords").GetInt64()),
            countDefinition = "Unique generated and sent SOWs are distinct engagement IDs. Generation runs, released versions, requests and verified submissions are separate. Downloads never increase SOW output counts."
        });
    }

    private static bool SowReportDate(string? text, bool exclusiveEnd, out DateTimeOffset? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        if (exclusiveEnd)
        {
            if (date == DateOnly.MaxValue) return false;
            date = date.AddDays(1);
        }
        result = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return true;
    }
}

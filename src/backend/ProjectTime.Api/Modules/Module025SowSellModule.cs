using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace ProjectTime.Api.Modules;

// The guarded build bridge makes the existing workspace class partial. All
// endpoints reuse its actual-session, owner-scope and View-As authorization.
public static partial class Module025SowGsdModule
{
    private const string SowSellMigration = "106_module025_sow_sell_register";

    public static IServiceCollection AddModule025SowSell(this IServiceCollection services)
    {
        services.TryAddSingleton<IModule025SellPublisher, Module025ZendeskSellPublisher>();
        services.AddHostedService<Module025SowSellWorker>();
        return services;
    }

    public static WebApplication MapModule025SowSellEndpoints(this WebApplication app)
    {
        app.MapGet("/api/module025/sow-register", (Func<Guid?, string?, string?, string?, int?, string?, HttpContext, CancellationToken, Task<IResult>>)SowRegisterAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/versions", (Func<Guid, int?, HttpContext, CancellationToken, Task<IResult>>)SowVersionsAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/history", (Func<Guid, long?, HttpContext, CancellationToken, Task<IResult>>)SowHistoryAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/versions", (Func<Guid, Module025SowReleaseRequest, HttpContext, CancellationToken, Task<IResult>>)ReleaseSowVersionAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/versions/{versionId:guid}/{artifact}", (Func<Guid, Guid, string, HttpContext, CancellationToken, Task<IResult>>)DownloadSowVersionAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/sell", (Func<Guid, Module025SowSellSubmitRequest, HttpContext, CancellationToken, Task<IResult>>)SubmitSowToSellAsync);
        return app;
    }

    private static async Task<bool> SowSellSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.module025_sow_gsd_versions') IS NOT NULL
               AND to_regclass('public.module025_sow_sell_dispatch') IS NOT NULL
               AND to_regclass('public.module025_sow_sell_receipts') IS NOT NULL
               AND to_regclass('public.module025_sow_gsd_generation_snapshots') IS NOT NULL;
            """, connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static IResult SowSellMigrationRequired() => Results.Json(new
    {
        status = "module025_sow_register_migration_required", migration = SowSellMigration,
        message = "The SOW Register requires migration 106. No untracked document or SELL submission was created."
    }, statusCode: StatusCodes.Status409Conflict);

    private sealed record ReleasedSowVersion(Guid VersionId, int VersionNumber, int SourceRevision,
        string ContentSha256, string SowSha256, string GsdSha256);

    private static async Task<ReleasedSowVersion?> LatestSowVersionAsync(NpgsqlConnection connection, Guid engagementId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT version_id,version_number,source_revision,content_sha256,sow_sha256,gsd_sha256
            FROM module025_sow_gsd_versions WHERE engagement_id=@id ORDER BY version_number DESC LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("id", engagementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

    // Called inside the workspace's confirm transaction, after its optimistic
    // UPDATE has locked the root. Both final byte streams and the audit event
    // commit with confirmation, or none of them do.
    private static async Task<(ReleasedSowVersion Version, bool Created)> CaptureConfirmedSowVersionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid engagementId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken)
            ?? throw new InvalidOperationException("The confirmed SOW disappeared.");
        if (engagement.Status != "confirmed" || !engagement.IsActive)
            throw new InvalidOperationException("Only a confirmed, active SOW may be released.");
        var fingerprint = Module025SowSellPolicy.Fingerprint(engagement);
        var latest = await LatestSowVersionAsync(connection, engagementId, cancellationToken);
        if (latest is not null && latest.ContentSha256 == fingerprint) return (latest, false);
        var number = (latest?.VersionNumber ?? 0) + 1;
        var document = Module025SowSellPolicy.ReviewedContent(engagement) with { Revision = number };
        var sow = Module025SowGsdDocumentExporter.CreateSowDocx(BuildDocumentModel(document));
        var gsd = Module025SowGsdDocumentExporter.CreateGsdXlsx(BuildDocumentModel(document));
        if (sow.Length is 0 or > Module025SowSellPolicy.MaximumArtifactBytes || gsd.Length is 0 or > Module025SowSellPolicy.MaximumArtifactBytes)
            throw new InvalidOperationException("The released SOW/GSD exceeds the retained-artifact boundary.");
        var version = new ReleasedSowVersion(Guid.NewGuid(), number, engagement.Revision, fingerprint,
            Module025SowSellPolicy.Hash(sow), Module025SowSellPolicy.Hash(gsd));
        await using (var command = new NpgsqlCommand("""
            INSERT INTO module025_sow_gsd_versions(version_id,engagement_id,version_number,source_revision,
                content_sha256,source_json,sow_content,gsd_content,actor_user_id)
            VALUES(@version,@id,@number,@revision,@hash,@source::jsonb,@sow,@gsd,@actor);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("version", version.VersionId);
            command.Parameters.AddWithValue("id", engagementId);
            command.Parameters.AddWithValue("number", number);
            command.Parameters.AddWithValue("revision", engagement.Revision);
            command.Parameters.AddWithValue("hash", fingerprint);
            command.Parameters.AddWithValue("source", JsonSerializer.Serialize(engagement, Module025SowSellPolicy.Json));
            command.Parameters.AddWithValue("sow", sow);
            command.Parameters.AddWithValue("gsd", gsd);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertEventAsync(connection, transaction, engagementId, actorUserId, engagement.Revision,
            "documents_released", "Reviewed SOW and GSD retained as an immutable version.", version, cancellationToken);
        return (version, true);
    }

    private static async Task<IResult> ReleaseSowVersionAsync(Guid engagementId, Module025SowReleaseRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var state = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await LockSowForReleaseAsync(connection, transaction, engagementId, cancellationToken);
        if (current is null) return Results.NotFound();
        if (current.Revision != request.ExpectedRevision) return RevisionConflict(current.Revision);
        if (current.Status != "confirmed" || !current.IsActive) return StateConflict("confirmation_required", "Confirm the reviewed SOW/GSD before releasing a document version.");
        var released = await CaptureConfirmedSowVersionAsync(connection, transaction, engagementId, state.Access!.ActualUserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_version_released", version = released.Version, stateChanged = released.Created });
    }

    private static async Task<Module025EngagementRow?> LockSowForReleaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid engagementId, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand("SELECT engagement_id FROM module025_sow_gsd_engagements WHERE engagement_id=@id FOR UPDATE;", connection, transaction))
        {
            command.Parameters.AddWithValue("id", engagementId);
            if (await command.ExecuteScalarAsync(cancellationToken) is null) return null;
        }
        return await LoadEngagementAsync(connection, engagementId, cancellationToken);
    }

    private static Task<IResult> DownloadSowAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken) =>
        DownloadRetainedSowAsync(engagementId, null, "sow.docx", context, cancellationToken);
    private static Task<IResult> DownloadGsdAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken) =>
        DownloadRetainedSowAsync(engagementId, null, "gsd.xlsx", context, cancellationToken);
    private static Task<IResult> DownloadSowVersionAsync(Guid engagementId, Guid versionId, string artifact, HttpContext context, CancellationToken cancellationToken) =>
        DownloadRetainedSowAsync(engagementId, versionId, artifact, context, cancellationToken);

    private static async Task<IResult> DownloadRetainedSowAsync(Guid engagementId, Guid? versionId, string artifact, HttpContext context, CancellationToken cancellationToken)
    {
        if (artifact is not ("sow.docx" or "gsd.xlsx")) return Results.NotFound();
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();
        if (versionId is null && state.Engagement!.Status != "confirmed")
            return StateConflict("confirmation_required", "Confirm the latest edits, or download an earlier retained version from the SOW Register.");
        var sow = artifact == "sow.docx";
        // These two identifiers are selected solely from the literal whitelist.
        var bytesColumn = sow ? "sow_content" : "gsd_content";
        var hashColumn = sow ? "sow_sha256" : "gsd_sha256";
        byte[] bytes;
        Guid selectedVersion;
        int number;
        string hash;
        await using (var command = new NpgsqlCommand($"""
            SELECT version_id,version_number,{bytesColumn},{hashColumn}
            FROM module025_sow_gsd_versions
            WHERE engagement_id=@id AND (@version::uuid IS NULL OR version_id=@version)
            ORDER BY version_number DESC LIMIT 1;
            """, connection))
        {
            command.Parameters.AddWithValue("id", engagementId);
            AddNullableGuid(command, "version", versionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return StateConflict("version_release_required", "This older confirmed record has no retained version yet. Its owner can select Retain confirmed version in the SOW Register.");
            selectedVersion = reader.GetGuid(0);
            number = reader.GetInt32(1);
            bytes = reader.GetFieldValue<byte[]>(2);
            hash = reader.GetString(3);
        }
        if (Module025SowSellPolicy.Hash(bytes) != hash)
            return Results.Json(new { status = "artifact_integrity_failure", message = "The retained document failed its integrity check." }, statusCode: 503);
        if (!state.Access!.IsViewAs)
        {
            await using var issuance = new NpgsqlCommand("""
                INSERT INTO module025_sow_gsd_artifact_issuance(version_id,artifact_kind,actor_user_id)
                VALUES(@version,@kind,@actor) ON CONFLICT (version_id,artifact_kind) DO NOTHING;
                """, connection);
            issuance.Parameters.AddWithValue("version", selectedVersion);
            issuance.Parameters.AddWithValue("kind", sow ? "sow" : "gsd");
            issuance.Parameters.AddWithValue("actor", state.Access.ActualUserId);
            await issuance.ExecuteNonQueryAsync(cancellationToken);
        }
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-SOW-Version"] = number.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-Content-SHA256"] = hash;
        var name = $"{SafeFileName(state.Engagement!.EngagementNumber)}-v{number}-{(sow ? "SOW.docx" : "GSD.xlsx")}";
        return Results.File(bytes, sow ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    private static async Task<IResult> SowVersionsAsync(Guid engagementId, int? page, HttpContext context, CancellationToken cancellationToken)
    {
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();
        if (page is < 1 or > 100000) return Results.BadRequest(new { message = "Invalid version page." });
        var pageNumber = page ?? 1;
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
                'versionId',v.version_id,'versionNumber',v.version_number,'sourceRevision',v.source_revision,
                'createdAt',v.created_at,'sowSha256',v.sow_sha256,'gsdSha256',v.gsd_sha256,
                'firstSowServedAt',(SELECT first_served_at FROM module025_sow_gsd_artifact_issuance a WHERE a.version_id=v.version_id AND artifact_kind='sow'),
                'firstGsdServedAt',(SELECT first_served_at FROM module025_sow_gsd_artifact_issuance a WHERE a.version_id=v.version_id AND artifact_kind='gsd'),
                'submissions',COALESCE((SELECT jsonb_agg(jsonb_build_object(
                    'submissionId',s.submission_id,'environment',s.runtime_environment,'requestedAt',s.created_at,
                    'sellStatus',d.sell_status,'mailStatus',d.mail_status,'diagnosticCode',d.diagnostic_code,
                    'sellRecordId',r.sell_record_id,'verifiedAt',r.verified_at))
                    FROM module025_sow_sell_submissions s
                    JOIN module025_sow_sell_dispatch d USING(submission_id)
                    LEFT JOIN module025_sow_sell_receipts r USING(submission_id)
                    WHERE s.version_id=v.version_id),'[]'::jsonb))::text
            FROM module025_sow_gsd_versions v WHERE v.engagement_id=@id
            ORDER BY version_number DESC LIMIT 51 OFFSET @offset;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", engagementId);
        command.Parameters.AddWithValue("offset", (pageNumber - 1) * 50);
        var versions = new List<JsonElement>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) versions.Add(ParseJson(reader.GetString(0), JsonValueKind.Object));
        var latest = await LatestSowVersionAsync(connection, engagementId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(context) ?? string.Empty;
        var readiness = await context.RequestServices.GetRequiredService<IModule025SellPublisher>().GetReadinessAsync(environment, cancellationToken);
        var current = state.Engagement!;
        return Results.Ok(new
        {
            engagementId, current.EngagementNumber, current.CustomerName, current.OwnerDisplayName,
            current.Revision, current.Status, current.IsActive,
            canWrite = state.Access!.CanWriteOwned(current.OwnerUserId),
            latestVersionId = latest?.VersionId,
            currentContentReleased = latest is not null && latest.ContentSha256 == Module025SowSellPolicy.Fingerprint(current),
            versions = versions.Take(50), page = pageNumber, hasMore = versions.Count > 50,
            runtimeEnvironment = environment, sellReadiness = readiness,
            legacyGenerationNotice = "Generation events before migration 106 remain reportable; missing historical document bytes are never reconstructed as original evidence."
        });
    }

    private static async Task<IResult> SowHistoryAsync(Guid engagementId, long? beforeEventId, HttpContext context, CancellationToken cancellationToken)
    {
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object('eventId',e.event_id,'eventType',e.event_type,'actorUserId',e.actor_user_id,
                'revision',e.engagement_revision,'summary',e.summary,'evidence',e.evidence_json,'createdAt',e.created_at,
                'generationSnapshotSha256',g.source_sha256)::text, e.event_id
            FROM module025_sow_gsd_events e LEFT JOIN module025_sow_gsd_generation_snapshots g USING(event_id)
            WHERE e.engagement_id=@id AND e.event_id < @before ORDER BY e.event_id DESC LIMIT 101;
            """, connection);
        command.Parameters.AddWithValue("id", engagementId);
        command.Parameters.AddWithValue("before", beforeEventId ?? long.MaxValue);
        var events = new List<JsonElement>();
        long? next = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ParseJson(reader.GetString(0), JsonValueKind.Object));
            if (events.Count == 100) next = reader.GetInt64(1);
        }
        return Results.Ok(new { engagementId, events = events.Take(100), nextBeforeEventId = events.Count > 100 ? next : null });
    }

    private static async Task<IResult> SubmitSowToSellAsync(Guid engagementId, Module025SowSellSubmitRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var state = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(context) ?? string.Empty;
        if (environment is not ("test" or "production")) return StateConflict("environment_required", "The governed runtime environment could not be resolved. No SELL write was attempted.");
        var readiness = await context.RequestServices.GetRequiredService<IModule025SellPublisher>().GetReadinessAsync(environment, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await LockSowForReleaseAsync(connection, transaction, engagementId, cancellationToken);
        if (current is null) return Results.NotFound();
        if (current.Revision != request.ExpectedRevision) return RevisionConflict(current.Revision);
        if (current.Status != "confirmed" || !current.IsActive) return StateConflict("confirmation_required", "Confirm the latest SA edits before pushing to SELL.");
        var version = await LatestSowVersionAsync(connection, engagementId, cancellationToken);
        if (version is null || version.VersionId != request.VersionId || version.ContentSha256 != Module025SowSellPolicy.Fingerprint(current))
            return StateConflict("current_version_required", "Release and select the latest confirmed document version before pushing to SELL.");

        // Freeze server-resolved recipients, never a browser-supplied mail list.
        var recipients = await ResolveSowSellRecipientsAsync(connection, current, cancellationToken);
        if (recipients.Count != 3) return StateConflict("sell_recipients_required", "The assigned Solution Architect, Account Executive and Inside Sales Representative must all be active users with valid email addresses.");
        var proposedId = Guid.NewGuid();
        Guid submissionId;
        bool created;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO module025_sow_sell_submissions(submission_id,engagement_id,version_id,destination_key,
                runtime_environment,actor_user_id,recipients_json)
            VALUES(@submission,@id,@version,'zendesk_sell',@environment,@actor,@recipients::jsonb)
            ON CONFLICT (version_id,destination_key,runtime_environment) DO NOTHING RETURNING submission_id;
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("submission", proposedId);
            insert.Parameters.AddWithValue("id", engagementId);
            insert.Parameters.AddWithValue("version", version.VersionId);
            insert.Parameters.AddWithValue("environment", environment);
            insert.Parameters.AddWithValue("actor", state.Access!.ActualUserId);
            insert.Parameters.AddWithValue("recipients", JsonSerializer.Serialize(recipients, Module025SowSellPolicy.Json));
            created = await insert.ExecuteScalarAsync(cancellationToken) is Guid;
        }
        await using (var find = new NpgsqlCommand("SELECT submission_id FROM module025_sow_sell_submissions WHERE version_id=@version AND destination_key='zendesk_sell' AND runtime_environment=@environment;", connection, transaction))
        {
            find.Parameters.AddWithValue("version", version.VersionId);
            find.Parameters.AddWithValue("environment", environment);
            submissionId = (Guid)(await find.ExecuteScalarAsync(cancellationToken))!;
        }
        if (created)
        {
            await using var dispatch = new NpgsqlCommand("""
                INSERT INTO module025_sow_sell_dispatch(submission_id,engagement_id,sell_status,diagnostic_code)
                VALUES(@submission,@id,@status,@diagnostic);
                """, connection, transaction);
            dispatch.Parameters.AddWithValue("submission", submissionId);
            dispatch.Parameters.AddWithValue("id", engagementId);
            dispatch.Parameters.AddWithValue("status", readiness.Ready ? "queued" : "blocked");
            dispatch.Parameters.AddWithValue("diagnostic", readiness.Ready ? string.Empty : Clean(readiness.DiagnosticCode,160));
            await dispatch.ExecuteNonQueryAsync(cancellationToken);
            await InsertEventAsync(connection, transaction, engagementId, state.Access!.ActualUserId, current.Revision,
                "sell_submission_requested", "The retained SOW/GSD version was registered for SELL processing; this is not a success receipt.",
                new { submissionId, versionId = version.VersionId, versionNumber = version.VersionNumber, runtimeEnvironment = environment, blocked = !readiness.Ready }, cancellationToken);
        }
        else if (readiness.Ready)
        {
            // Only a known no-write configuration block can be released here.
            // Publishing, failed and uncertain requests are never blindly retried.
            await using var release = new NpgsqlCommand("UPDATE module025_sow_sell_dispatch SET sell_status='queued',diagnostic_code='',updated_at=now() WHERE submission_id=@id AND sell_status='blocked';", connection, transaction);
            release.Parameters.AddWithValue("id", submissionId);
            if (await release.ExecuteNonQueryAsync(cancellationToken) > 0)
                await InsertEventAsync(connection, transaction, engagementId, state.Access!.ActualUserId, current.Revision,
                    "sell_submission_unblocked", "An explicitly retried, previously blocked submission is now queued.", new { submissionId }, cancellationToken);
        }
        string sellStatus;
        string mailStatus;
        await using (var status = new NpgsqlCommand("SELECT sell_status,mail_status FROM module025_sow_sell_dispatch WHERE submission_id=@id;", connection, transaction))
        {
            status.Parameters.AddWithValue("id", submissionId);
            await using var reader = await status.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            sellStatus = reader.GetString(0);
            mailStatus = reader.GetString(1);
        }
        await transaction.CommitAsync(cancellationToken);
        var message = sellStatus == "published" ? "This exact version already has a verified SELL receipt. No duplicate submission was created."
            : sellStatus == "blocked" ? readiness.Message
            : sellStatus == "queued" ? "The version is queued. SELL success and notification status will appear in the immutable history after verification."
            : "This submission already exists. Its recorded status is authoritative; no duplicate external write was requested.";
        return Results.Json(new { status = "module025_sell_submission", submissionId, sellStatus, mailStatus, message, stateChanged = created },
            statusCode: sellStatus == "blocked" ? 409 : sellStatus == "queued" ? 202 : 200);
    }

    private static async Task<IReadOnlyList<Module025SellRecipient>> ResolveSowSellRecipientsAsync(NpgsqlConnection connection, Module025EngagementRow engagement, CancellationToken cancellationToken)
    {
        if (!engagement.AccountExecutiveUserId.HasValue || !engagement.ResaleUserId.HasValue) return [];
        var ae = await ResolvePersonAsync(connection, engagement.AccountExecutiveUserId, AccountExecutiveRoles, cancellationToken);
        var inside = await ResolvePersonAsync(connection, engagement.ResaleUserId, InsideSalesRepresentativeRoles, cancellationToken);
        if (!ae.UserId.HasValue || !inside.UserId.HasValue) return [];
        var roles = new[]
        {
            (engagement.OwnerUserId, "solution_architect", "cc"),
            (engagement.AccountExecutiveUserId.Value, "account_executive", "cc"),
            (engagement.ResaleUserId.Value, "inside_sales", "to")
        };
        var result = new List<Module025SellRecipient>();
        foreach (var (id, role, recipientType) in roles)
        {
            await using var command = new NpgsqlCommand("SELECT COALESCE(NULLIF(display_name,''),email,''),COALESCE(email,'') FROM app_users WHERE user_id=@id AND is_active=TRUE;", connection);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || !Module025SowSellPolicy.ValidEmail(reader.GetString(1))) return [];
            result.Add(new(id, reader.GetString(0), reader.GetString(1).Trim(), role, recipientType));
        }
        return result;
    }
}

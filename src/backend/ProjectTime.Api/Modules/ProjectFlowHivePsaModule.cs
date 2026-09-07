using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public sealed record ProjectFlowHiveMeetingUpdateRequest(
    string? Title,
    DateTimeOffset? MeetingAt,
    bool? CustomerVisible,
    bool RetryTranscription = false);

public sealed record ProjectFlowHiveReminderPreferenceRequest(
    bool Enabled,
    short[]? LeadDays,
    bool IncludeProjectManager = true,
    bool IncludeAssignedTeamMembers = true,
    bool IncludeOverdue = true,
    string? TimezoneName = "America/Chicago",
    string? DeliveryBoundary = "test_only");

public sealed record ProjectFlowHivePsaArtifactRequest(ProjectFlowHivePlanRequest Plan);

internal static class ProjectFlowHivePsaModule
{
    internal const string MigrationId = "103_module_066_flowhive_enterprise_psa_revamp";
    private const long MaximumMeetingBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ArtifactKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "timeline-risk", "raid", "decision-matrix", "gantt", "monthly-calendar", "work-breakdown"
    };

    internal static IEndpointRouteBuilder MapProjectFlowHivePsaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/project-flowhive/projects/{projectId:guid}/psa",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetWorkspaceAsync);
        endpoints.MapPost(
            "/api/project-flowhive/projects/{projectId:guid}/meetings",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)UploadMeetingAsync);
        endpoints.MapPut(
            "/api/project-flowhive/projects/{projectId:guid}/meetings/{meetingId:guid}",
            (Func<Guid, Guid, ProjectFlowHiveMeetingUpdateRequest, HttpContext, CancellationToken, Task<IResult>>)UpdateMeetingAsync);
        endpoints.MapGet(
            "/api/project-flowhive/projects/{projectId:guid}/meetings/{meetingId:guid}/download",
            (Func<Guid, Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadMeetingAsync);
        endpoints.MapGet(
            "/api/project-flowhive/share/{token}/meetings/{meetingId:guid}/download",
            (Func<string, Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadSharedMeetingAsync)
            .AllowAnonymous();
        endpoints.MapPut(
            "/api/project-flowhive/projects/{projectId:guid}/task-reminders",
            (Func<Guid, ProjectFlowHiveReminderPreferenceRequest, HttpContext, CancellationToken, Task<IResult>>)SaveReminderPreferencesAsync);
        endpoints.MapPost(
            "/api/project-flowhive/projects/{projectId:guid}/artifacts/{kind}/{format}",
            (Func<Guid, string, string, ProjectFlowHivePsaArtifactRequest, HttpContext, CancellationToken, Task<IResult>>)BuildArtifactAsync);
        return endpoints;
    }

    private static async Task<IResult> GetWorkspaceAsync(
        Guid projectId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var access = await OpenProjectAsync(projectId, context, write: false, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        try
        {
            if (!await MigrationReadyAsync(connection, cancellationToken)) return MigrationRequired();
            var meetings = await LoadMeetingsAsync(connection, projectId, cancellationToken);
            var raidHistory = await LoadRaidHistoryAsync(connection, projectId, cancellationToken);
            var reminders = await LoadReminderPreferencesAsync(connection, projectId, cancellationToken);
            var decisions = await LoadRaidRowsAsync(connection, projectId, "decision", cancellationToken);
            return Results.Ok(new
            {
                module = "066",
                status = "flowhive_enterprise_psa_loaded",
                projectId,
                access = new
                {
                    canView = true,
                    canManage = access.CanManage,
                    isViewAs = access.IsViewAs,
                    access.Scope
                },
                meetings,
                decisions,
                raidHistory,
                reminderPreferences = reminders,
                capabilities = new
                {
                    kanban = true,
                    gantt = true,
                    monthlyCalendar = true,
                    decisionMatrix = true,
                    immutableRaidHistory = true,
                    meetingRecordings = true,
                    customerMeetingDownloads = true,
                    taskDueReminders = true,
                    transcription = new
                    {
                        configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLOWHIVE_TRANSCRIPTION_ENDPOINT")),
                        mode = "governed_private_worker",
                        actionItemExtraction = true
                    },
                    brandedArtifacts = ArtifactKinds.OrderBy(value => value).ToArray()
                }
            });
        }
        catch (Exception exception)
        {
            return Failure(context, exception, "load the enterprise PSA workspace");
        }
    }

    private static async Task<IResult> UploadMeetingAsync(
        Guid projectId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var access = await OpenProjectAsync(projectId, context, write: true, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        if (!await MigrationReadyAsync(connection, cancellationToken)) return MigrationRequired();
        if (!context.Request.HasFormContentType)
            return Results.BadRequest(new { status = "meeting_file_required", message = "Upload an MP4 meeting recording using multipart/form-data." });

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length <= 0)
            return Results.BadRequest(new { status = "meeting_file_required", message = "Select an MP4 meeting recording to upload." });
        if (file.Length > MaximumMeetingBytes)
            return Results.Json(new { status = "meeting_file_too_large", message = "Meeting recordings are limited to 2 GB per file." }, statusCode: StatusCodes.Status413PayloadTooLarge);
        if (!Path.GetExtension(file.FileName).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { status = "meeting_mp4_required", message = "Project FlowHive currently accepts MP4 meeting recordings only." });

        var title = Clean(form["title"].FirstOrDefault(), 240);
        if (title.Length < 3) title = Path.GetFileNameWithoutExtension(file.FileName);
        var meetingAt = DateTimeOffset.TryParse(form["meetingAt"].FirstOrDefault(), out var parsedMeetingAt)
            ? parsedMeetingAt.ToUniversalTime()
            : DateTimeOffset.UtcNow;
        var customerVisible = bool.TryParse(form["customerVisible"].FirstOrDefault(), out var parsedVisible) && parsedVisible;
        var meetingId = Guid.NewGuid();
        var root = ProjectPulseUploadStorage.ResolveRoot();
        var relativeDirectory = Path.Combine("flowhive-meetings", projectId.ToString("N"), meetingId.ToString("N"));
        var directory = SafePath(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var safeName = SafeFileName(file.FileName);
        var destination = SafePath(root, Path.Combine(relativeDirectory, safeName));

        string sha;
        try
        {
            await using var source = file.OpenReadStream();
            var prefix = new byte[12];
            var prefixRead = 0;
            while (prefixRead < prefix.Length)
            {
                var read = await source.ReadAsync(prefix.AsMemory(prefixRead, prefix.Length - prefixRead), cancellationToken);
                if (read == 0) break;
                prefixRead += read;
            }
            if (prefixRead < 12 || Encoding.ASCII.GetString(prefix, 4, 4) != "ftyp")
                return Results.BadRequest(new { status = "meeting_mp4_signature_invalid", message = "The uploaded file does not have a valid MP4 signature." });

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
            await output.WriteAsync(prefix.AsMemory(0, prefixRead), cancellationToken);
            hash.AppendData(prefix, 0, prefixRead);
            var buffer = new byte[1024 * 1024];
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                hash.AppendData(buffer, 0, count);
            }
            sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            TryDelete(destination);
            throw;
        }

        var transcriptionConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLOWHIVE_TRANSCRIPTION_ENDPOINT"));
        var transcriptStatus = transcriptionConfigured ? "queued" : "unavailable";
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string insert = """
                INSERT INTO project_flowhive_meetings(
                    meeting_id, project_id, title, meeting_at, original_file_name,
                    storage_relative_path, content_type, size_bytes, sha256, customer_visible,
                    transcript_status, transcription_diagnostic, uploaded_by_user_id, updated_by_user_id)
                VALUES(
                    @meeting_id, @project_id, @title, @meeting_at, @original_file_name,
                    @storage_relative_path, 'video/mp4', @size_bytes, @sha256, @customer_visible,
                    @transcript_status, @diagnostic, @actor, @actor);
                INSERT INTO project_flowhive_meeting_events(
                    meeting_id, project_id, event_code, actor_user_id, detail_json)
                VALUES(@meeting_id, @project_id, 'uploaded', @actor, @detail::jsonb);
                """;
            await using var command = new NpgsqlCommand(insert, connection, transaction);
            command.Parameters.AddWithValue("meeting_id", meetingId);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("title", title);
            command.Parameters.AddWithValue("meeting_at", meetingAt);
            command.Parameters.AddWithValue("original_file_name", Path.GetFileName(file.FileName));
            command.Parameters.AddWithValue("storage_relative_path", relativeDirectory.Replace('\\', '/') + "/" + safeName);
            command.Parameters.AddWithValue("size_bytes", file.Length);
            command.Parameters.AddWithValue("sha256", sha);
            command.Parameters.AddWithValue("customer_visible", customerVisible);
            command.Parameters.AddWithValue("transcript_status", transcriptStatus);
            command.Parameters.AddWithValue("diagnostic", transcriptionConfigured ? "automatic_transcription_queued" : "governed_transcription_endpoint_not_configured");
            command.Parameters.AddWithValue("actor", access.ActualUserId!.Value);
            command.Parameters.AddWithValue("detail", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new { fileName = Path.GetFileName(file.FileName), file.Length, sha256 = sha, customerVisible, transcriptStatus }, Json));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            TryDelete(destination);
            throw;
        }

        return Results.Ok(new
        {
            status = "flowhive_meeting_uploaded",
            meetingId,
            title,
            meetingAt,
            fileName = Path.GetFileName(file.FileName),
            sizeBytes = file.Length,
            sha256 = sha,
            customerVisible,
            transcriptStatus,
            message = transcriptionConfigured
                ? "Meeting recording saved. Governed automatic transcription and action-item extraction are queued."
                : "Meeting recording saved. Automatic transcription is unavailable until the governed private transcription endpoint is configured."
        });
    }

    private static async Task<IResult> UpdateMeetingAsync(
        Guid projectId,
        Guid meetingId,
        ProjectFlowHiveMeetingUpdateRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var access = await OpenProjectAsync(projectId, context, write: true, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        if (!await MigrationReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLOWHIVE_TRANSCRIPTION_ENDPOINT"));
        const string sql = """
            WITH prior AS (
                SELECT customer_visible, transcript_status
                FROM project_flowhive_meetings
                WHERE meeting_id = @meeting_id AND project_id = @project_id
            ), changed AS (
                UPDATE project_flowhive_meetings
                SET title = COALESCE(NULLIF(@title, ''), title),
                    meeting_at = COALESCE(@meeting_at, meeting_at),
                    customer_visible = COALESCE(@customer_visible, customer_visible),
                    transcript_status = CASE WHEN @retry_transcription THEN @retry_status ELSE transcript_status END,
                    transcription_diagnostic = CASE WHEN @retry_transcription THEN @retry_diagnostic ELSE transcription_diagnostic END,
                    updated_by_user_id = @actor
                WHERE meeting_id = @meeting_id AND project_id = @project_id
                RETURNING meeting_id, project_id, title, meeting_at, original_file_name, size_bytes, sha256,
                          customer_visible, transcript_status, transcript_language, action_items, transcription_diagnostic, updated_at
            )
            INSERT INTO project_flowhive_meeting_events(meeting_id, project_id, event_code, actor_user_id, detail_json)
            SELECT changed.meeting_id, changed.project_id,
                   CASE WHEN prior.customer_visible IS DISTINCT FROM changed.customer_visible THEN 'customer_visibility_changed' ELSE 'metadata_updated' END,
                   @actor,
                   jsonb_build_object('customerVisible', changed.customer_visible, 'transcriptStatus', changed.transcript_status)
            FROM changed CROSS JOIN prior;

            SELECT meeting_id, project_id, title, meeting_at, original_file_name, size_bytes, sha256,
                   customer_visible, transcript_status, transcript_language, action_items, transcription_diagnostic, created_at, updated_at
            FROM project_flowhive_meetings
            WHERE meeting_id = @meeting_id AND project_id = @project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("meeting_id", meetingId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("title", Clean(request.Title, 240));
        command.Parameters.AddWithValue("meeting_at", NpgsqlDbType.TimestampTz, request.MeetingAt is null ? DBNull.Value : request.MeetingAt.Value.ToUniversalTime());
        command.Parameters.AddWithValue("customer_visible", NpgsqlDbType.Boolean, request.CustomerVisible is null ? DBNull.Value : request.CustomerVisible.Value);
        command.Parameters.AddWithValue("retry_transcription", request.RetryTranscription);
        command.Parameters.AddWithValue("retry_status", configured ? "queued" : "unavailable");
        command.Parameters.AddWithValue("retry_diagnostic", configured ? "automatic_transcription_requeued" : "governed_transcription_endpoint_not_configured");
        command.Parameters.AddWithValue("actor", access.ActualUserId!.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { status = "meeting_not_found" });
        return Results.Ok(ReadMeeting(reader));
    }

    private static async Task<IResult> DownloadMeetingAsync(
        Guid projectId,
        Guid meetingId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var access = await OpenProjectAsync(projectId, context, write: false, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        if (!await MigrationReadyAsync(connection, cancellationToken)) return MigrationRequired();
        return await MeetingFileResultAsync(connection, projectId, meetingId, access.EffectiveUserId, false, cancellationToken);
    }

    private static async Task<IResult> DownloadSharedMeetingAsync(
        string token,
        Guid meetingId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 24)
            return Results.NotFound(new { status = "customer_share_not_found" });
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await MigrationReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();
        const string sql = """
            SELECT s.share_id, s.project_id
            FROM project_flowhive_customer_shares s
            JOIN project_flowhive_project_controls c ON c.project_id = s.project_id AND c.customer_sharing_enabled = TRUE
            WHERE s.token_sha256 = @token_sha256
              AND s.revoked_at IS NULL
              AND s.expires_at > NOW()
              AND (s.allowed_artifacts && ARRAY['meetings','meeting_recordings']::TEXT[])
            LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("token_sha256", tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { status = "customer_share_not_found" });
        var shareId = reader.GetGuid(0);
        var projectId = reader.GetGuid(1);
        await reader.DisposeAsync();
        var result = await MeetingFileResultAsync(connection, projectId, meetingId, null, true, cancellationToken, shareId);
        if (result is not IStatusCodeHttpResult { StatusCode: >= 400 })
        {
            await using var update = new NpgsqlCommand("UPDATE project_flowhive_customer_shares SET last_accessed_at = NOW(), access_count = access_count + 1 WHERE share_id = @share_id;", connection);
            update.Parameters.AddWithValue("share_id", shareId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        return result;
    }

    private static async Task<IResult> SaveReminderPreferencesAsync(
        Guid projectId,
        ProjectFlowHiveReminderPreferenceRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var access = await OpenProjectAsync(projectId, context, write: true, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        if (!await MigrationReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var leadDays = (request.LeadDays ?? [2, 1]).Distinct().Where(value => value is >= 0 and <= 60).OrderByDescending(value => value).Take(8).ToArray();
        if (leadDays.Length == 0) leadDays = [1];
        var timezone = Clean(request.TimezoneName, 100);
        if (timezone.Length == 0) timezone = "America/Chicago";
        var boundary = request.DeliveryBoundary is "production_governed" or "locked" ? request.DeliveryBoundary : "test_only";
        const string sql = """
            INSERT INTO project_flowhive_task_reminder_preferences(
                project_id, enabled, lead_days, include_project_manager, include_assigned_team_members,
                include_overdue, timezone_name, delivery_boundary, updated_by_user_id)
            VALUES(@project_id, @enabled, @lead_days, @include_pm, @include_team, @include_overdue, @timezone, @boundary, @actor)
            ON CONFLICT(project_id) DO UPDATE
            SET enabled = EXCLUDED.enabled,
                lead_days = EXCLUDED.lead_days,
                include_project_manager = EXCLUDED.include_project_manager,
                include_assigned_team_members = EXCLUDED.include_assigned_team_members,
                include_overdue = EXCLUDED.include_overdue,
                timezone_name = EXCLUDED.timezone_name,
                delivery_boundary = EXCLUDED.delivery_boundary,
                updated_by_user_id = EXCLUDED.updated_by_user_id
            RETURNING enabled, lead_days, include_project_manager, include_assigned_team_members,
                      include_overdue, timezone_name, quiet_hours_start, quiet_hours_end, delivery_boundary, updated_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("enabled", request.Enabled);
        command.Parameters.AddWithValue("lead_days", leadDays);
        command.Parameters.AddWithValue("include_pm", request.IncludeProjectManager);
        command.Parameters.AddWithValue("include_team", request.IncludeAssignedTeamMembers);
        command.Parameters.AddWithValue("include_overdue", request.IncludeOverdue);
        command.Parameters.AddWithValue("timezone", timezone);
        command.Parameters.AddWithValue("boundary", boundary);
        command.Parameters.AddWithValue("actor", access.ActualUserId!.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Results.Ok(ReadReminderPreferences(reader));
    }

    private static async Task<IResult> BuildArtifactAsync(
        Guid projectId,
        string kind,
        string format,
        ProjectFlowHivePsaArtifactRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var access = await OpenProjectAsync(projectId, context, write: false, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        if (!ArtifactKinds.Contains(kind)) return Results.BadRequest(new { status = "unsupported_flowhive_artifact", supported = ArtifactKinds.OrderBy(value => value) });
        if (!format.Equals("pdf", StringComparison.OrdinalIgnoreCase) && !format.Equals("excel", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { status = "unsupported_flowhive_artifact_format", supported = new[] { "pdf", "excel" } });
        if (request.Plan.ProjectId != projectId) return Results.BadRequest(new { status = "project_plan_mismatch" });
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(request.Plan);
        if (!schedule.Valid) return Results.BadRequest(schedule);
        var raid = await LoadRaidRowsAsync(connection, projectId, null, cancellationToken);
        var artifact = BuildArtifactTable(kind, request.Plan, schedule, raid);
        var bytes = format.Equals("pdf", StringComparison.OrdinalIgnoreCase)
            ? ProjectFlowHivePsaArtifactRenderer.BuildPdf(artifact)
            : ProjectFlowHivePsaArtifactRenderer.BuildExcel(artifact);
        var extension = format.Equals("pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "xlsx";
        var contentType = extension == "pdf" ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return Results.File(bytes, contentType, $"{SafeFileName(request.Plan.ProjectCode)}-{kind}.{extension}");
    }

    private static ProjectFlowHivePsaArtifactTable BuildArtifactTable(
        string kind,
        ProjectFlowHivePlanRequest plan,
        ProjectFlowHiveScheduleResult schedule,
        IReadOnlyList<RaidRow> raid)
    {
        var scheduleByWbs = schedule.Tasks.ToDictionary(row => row.WbsNumber, StringComparer.OrdinalIgnoreCase);
        var assignmentByWbs = (plan.Assignments ?? []).GroupBy(row => row.TaskWbs ?? string.Empty).ToDictionary(group => group.Key, group => string.Join(", ", group.Select(row => row.ResourceDisplayName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()), StringComparer.OrdinalIgnoreCase);
        var notes = new[] { "Generated from the current authorized FlowHive working plan and project controls.", "Customer delivery still requires an exact reviewed baseline and governed share action." };
        if (kind.Equals("raid", StringComparison.OrdinalIgnoreCase) || kind.Equals("decision-matrix", StringComparison.OrdinalIgnoreCase))
        {
            var source = kind.Equals("decision-matrix", StringComparison.OrdinalIgnoreCase) ? raid.Where(row => row.ItemType == "decision") : raid;
            return new(kind, kind == "raid" ? "RAID Log" : "Decision Matrix", plan.ProjectCode, plan.ProjectName, plan.CustomerName,
                kind == "raid"
                    ? new[] { "Type", "Title", "Status", "Priority", "Probability", "Impact", "Owner", "Due Date", "Mitigation" }
                    : new[] { "Decision", "Status", "Priority", "Owner", "Due Date", "Decision / Mitigation", "Source" },
                source.Select(row => (IReadOnlyList<string>)(kind == "raid"
                    ? new[] { row.ItemType, row.Title, row.Status, row.Priority, row.Probability, row.Impact, row.Owner, row.DueDate, row.Mitigation }
                    : new[] { row.Title, row.Status, row.Priority, row.Owner, row.DueDate, row.Mitigation, row.SourceReference })).ToArray(), notes);
        }

        var tasks = (plan.Tasks ?? []).Where(task => !task.IsSummary).ToArray();
        if (kind.Equals("work-breakdown", StringComparison.OrdinalIgnoreCase))
        {
            return new(kind, "Project Work Breakdown", plan.ProjectCode, plan.ProjectName, plan.CustomerName,
                new[] { "WBS", "Phase", "Task", "Start", "End", "Duration", "Hours", "Progress", "Status", "Assigned Identity" },
                tasks.Select(task =>
                {
                    scheduleByWbs.TryGetValue(task.WbsNumber ?? string.Empty, out var scheduled);
                    assignmentByWbs.TryGetValue(task.WbsNumber ?? string.Empty, out var assigned);
                    return (IReadOnlyList<string>)new[] { task.WbsNumber ?? "", task.Phase ?? "", task.Name ?? "", scheduled?.StartDate.ToString("yyyy-MM-dd") ?? "", scheduled?.EndDate.ToString("yyyy-MM-dd") ?? "", (scheduled?.DurationWorkingDays ?? task.DurationWorkingDays).ToString(), task.RemainingEffortHours.ToString("0.##"), task.PercentComplete.ToString("0.##") + "%", task.Status ?? "", assigned ?? "" };
                }).ToArray(), notes);
        }

        if (kind.Equals("monthly-calendar", StringComparison.OrdinalIgnoreCase))
        {
            return new(kind, "Monthly Project Calendar", plan.ProjectCode, plan.ProjectName, plan.CustomerName,
                new[] { "Start Date", "End Date", "WBS", "Phase", "Task", "Assigned Identity", "Status" },
                tasks.OrderBy(task => scheduleByWbs.TryGetValue(task.WbsNumber ?? "", out var scheduled) ? scheduled.StartDate : DateOnly.MaxValue).Select(task =>
                {
                    scheduleByWbs.TryGetValue(task.WbsNumber ?? "", out var scheduled);
                    assignmentByWbs.TryGetValue(task.WbsNumber ?? "", out var assigned);
                    return (IReadOnlyList<string>)new[] { scheduled?.StartDate.ToString("yyyy-MM-dd") ?? "", scheduled?.EndDate.ToString("yyyy-MM-dd") ?? "", task.WbsNumber ?? "", task.Phase ?? "", task.Name ?? "", assigned ?? "", task.Status ?? "" };
                }).ToArray(), notes);
        }

        var title = kind.Equals("gantt", StringComparison.OrdinalIgnoreCase) ? "Gantt Chart" : "Timeline and Risk";
        var columns = kind.Equals("gantt", StringComparison.OrdinalIgnoreCase)
            ? new[] { "WBS", "Task", "Start", "End", "Duration", "Start Offset", "Critical", "Float", "Predecessor" }
            : new[] { "WBS", "Phase", "Task", "Start", "End", "Duration", "Critical", "Float", "Risk / Open Questions" };
        return new(kind, title, plan.ProjectCode, plan.ProjectName, plan.CustomerName, columns,
            tasks.Select(task =>
            {
                scheduleByWbs.TryGetValue(task.WbsNumber ?? "", out var scheduled);
                var predecessor = (plan.Dependencies ?? []).FirstOrDefault(dep => dep.SuccessorWbs == task.WbsNumber)?.PredecessorWbs ?? "";
                var riskText = string.Join("; ", (task.Risks ?? []).Concat(task.OpenQuestions ?? []).Take(4));
                return (IReadOnlyList<string>)(kind.Equals("gantt", StringComparison.OrdinalIgnoreCase)
                    ? new[] { task.WbsNumber ?? "", task.Name ?? "", scheduled?.StartDate.ToString("yyyy-MM-dd") ?? "", scheduled?.EndDate.ToString("yyyy-MM-dd") ?? "", (scheduled?.DurationWorkingDays ?? task.DurationWorkingDays).ToString(), (scheduled?.EarliestStartIndex ?? 0).ToString(), scheduled?.IsCritical == true ? "Yes" : "No", (scheduled?.TotalFloatWorkingDays ?? 0).ToString(), predecessor }
                    : new[] { task.WbsNumber ?? "", task.Phase ?? "", task.Name ?? "", scheduled?.StartDate.ToString("yyyy-MM-dd") ?? "", scheduled?.EndDate.ToString("yyyy-MM-dd") ?? "", (scheduled?.DurationWorkingDays ?? task.DurationWorkingDays).ToString(), scheduled?.IsCritical == true ? "Yes" : "No", (scheduled?.TotalFloatWorkingDays ?? 0).ToString(), riskText });
            }).ToArray(), notes);
    }

    private static async Task<IResult> MeetingFileResultAsync(
        NpgsqlConnection connection,
        Guid projectId,
        Guid meetingId,
        Guid? actorUserId,
        bool customer,
        CancellationToken cancellationToken,
        Guid? shareId = null)
    {
        const string sql = """
            SELECT original_file_name, storage_relative_path, content_type, customer_visible
            FROM project_flowhive_meetings
            WHERE meeting_id = @meeting_id AND project_id = @project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("meeting_id", meetingId);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { status = "meeting_not_found" });
        var fileName = reader.GetString(0);
        var relative = reader.GetString(1);
        var contentType = reader.GetString(2);
        var visible = reader.GetBoolean(3);
        await reader.DisposeAsync();
        if (customer && !visible) return Results.NotFound(new { status = "meeting_not_found" });
        var root = ProjectPulseUploadStorage.ResolveRoot();
        var path = SafePath(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return Results.Json(new { status = "meeting_recording_storage_unavailable", message = "The meeting record exists but its durable recording is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        await using var audit = new NpgsqlCommand("INSERT INTO project_flowhive_meeting_events(meeting_id, project_id, event_code, actor_user_id, detail_json) VALUES(@meeting_id,@project_id,@event_code,@actor,@detail::jsonb);", connection);
        audit.Parameters.AddWithValue("meeting_id", meetingId);
        audit.Parameters.AddWithValue("project_id", projectId);
        audit.Parameters.AddWithValue("event_code", customer ? "customer_downloaded" : "internal_downloaded");
        audit.Parameters.AddWithValue("actor", NpgsqlDbType.Uuid, actorUserId is null ? DBNull.Value : actorUserId.Value);
        audit.Parameters.AddWithValue("detail", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new { shareId }, Json));
        await audit.ExecuteNonQueryAsync(cancellationToken);
        return Results.File(path, contentType, fileName, enableRangeProcessing: true);
    }

    private static async Task<List<object>> LoadMeetingsAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT meeting_id, project_id, title, meeting_at, original_file_name, size_bytes, sha256,
                   customer_visible, transcript_status, transcript_language, action_items,
                   transcription_diagnostic, created_at, updated_at
            FROM project_flowhive_meetings
            WHERE project_id = @project_id
            ORDER BY meeting_at DESC, created_at DESC
            LIMIT 200;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<object>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadMeeting(reader));
        return rows;
    }

    private static object ReadMeeting(NpgsqlDataReader reader)
    {
        return new
        {
            meetingId = reader.GetGuid(0),
            projectId = reader.GetGuid(1),
            title = reader.GetString(2),
            meetingAt = reader.GetFieldValue<DateTimeOffset>(3),
            originalFileName = reader.GetString(4),
            sizeBytes = reader.GetInt64(5),
            sha256 = reader.GetString(6),
            customerVisible = reader.GetBoolean(7),
            transcriptStatus = reader.GetString(8),
            transcriptLanguage = reader.GetString(9),
            actionItems = ReadJson(reader, 10),
            transcriptionDiagnostic = reader.GetString(11),
            createdAt = reader.GetFieldValue<DateTimeOffset>(12),
            updatedAt = reader.GetFieldValue<DateTimeOffset>(13)
        };
    }

    private static JsonElement ReadJson(NpgsqlDataReader reader, int ordinal)
    {
        using var document = JsonDocument.Parse(reader.GetString(ordinal));
        return document.RootElement.Clone();
    }

    private static async Task<List<object>> LoadRaidHistoryAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT raid_event_id, raid_item_id, action_code, actor_user_id, prior_json, new_json, occurred_at
            FROM project_flowhive_raid_events
            WHERE project_id = @project_id
            ORDER BY occurred_at DESC
            LIMIT 500;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                raidEventId = reader.GetGuid(0), raidItemId = reader.GetGuid(1), actionCode = reader.GetString(2),
                actorUserId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
                prior = reader.IsDBNull(4) ? (JsonElement?)null : ReadJson(reader, 4),
                current = reader.IsDBNull(5) ? (JsonElement?)null : ReadJson(reader, 5),
                occurredAt = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }
        return rows;
    }

    private static async Task<object> LoadReminderPreferencesAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT enabled, lead_days, include_project_manager, include_assigned_team_members, include_overdue, timezone_name, quiet_hours_start, quiet_hours_end, delivery_boundary, updated_at FROM project_flowhive_task_reminder_preferences WHERE project_id = @project_id;", connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new { enabled = true, leadDays = new[] { 2, 1 }, includeProjectManager = true, includeAssignedTeamMembers = true, includeOverdue = true, timezoneName = "America/Chicago", deliveryBoundary = "test_only", persisted = false };
        return ReadReminderPreferences(reader);
    }

    private static object ReadReminderPreferences(NpgsqlDataReader reader) => new
    {
        enabled = reader.GetBoolean(0),
        leadDays = reader.GetFieldValue<short[]>(1),
        includeProjectManager = reader.GetBoolean(2),
        includeAssignedTeamMembers = reader.GetBoolean(3),
        includeOverdue = reader.GetBoolean(4),
        timezoneName = reader.GetString(5),
        quietHoursStart = reader.IsDBNull(6) ? null : reader.GetTimeSpan(6).ToString(),
        quietHoursEnd = reader.IsDBNull(7) ? null : reader.GetTimeSpan(7).ToString(),
        deliveryBoundary = reader.GetString(8),
        updatedAt = reader.GetFieldValue<DateTimeOffset>(9),
        persisted = true
    };

    private sealed record RaidRow(string ItemType, string Title, string Status, string Priority, string Probability, string Impact, string Owner, string DueDate, string Mitigation, string SourceReference);

    private static async Task<List<RaidRow>> LoadRaidRowsAsync(NpgsqlConnection connection, Guid projectId, string? itemType, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.item_type, r.title, r.status, r.priority,
                   COALESCE(r.probability::text,''), COALESCE(r.impact::text,''),
                   COALESCE(NULLIF(u.display_name,''), u.email, ''), COALESCE(r.due_date::text,''),
                   r.mitigation, r.source_reference
            FROM project_flowhive_raid_items r
            LEFT JOIN app_users u ON u.user_id = r.owner_user_id
            WHERE r.project_id = @project_id
              AND (@item_type = '' OR r.item_type = @item_type)
            ORDER BY CASE r.priority WHEN 'critical' THEN 1 WHEN 'high' THEN 2 WHEN 'medium' THEN 3 ELSE 4 END,
                     r.due_date NULLS LAST, r.created_at DESC;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("item_type", itemType ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<RaidRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9)));
        return rows;
    }

    private sealed record ProjectAccess(
        NpgsqlConnection? Connection,
        IResult? Failure,
        Guid? ActualUserId,
        Guid EffectiveUserId,
        bool IsViewAs,
        bool CanManage,
        string Scope);

    private static async Task<ProjectAccess> OpenProjectAsync(Guid projectId, HttpContext context, bool write, CancellationToken cancellationToken)
    {
        var effective = EffectiveUserId(context);
        if (effective is null) return new(null, Results.Json(new { status = "session_required" }, statusCode: 401), null, Guid.Empty, false, false, "none");
        var actual = ActualUserId(context) ?? effective;
        var isViewAs = actual != effective || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var viewAsValue) && viewAsValue is bool active && active);
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0) return new(null, Results.Json(new { status = "configuration_missing", missing = config.Missing }, statusCode: 503), actual, effective.Value, isViewAs, false, "none");
        var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT
                p.project_manager_user_id = @user_id AS is_owner,
                p.account_executive_user_id = @user_id OR p.solution_architect_user_id = @user_id AS is_associated,
                EXISTS(SELECT 1 FROM project_assignments a WHERE a.project_id = p.project_id AND a.user_id = @user_id) AS is_assigned,
                EXISTS(SELECT 1 FROM project_planning_collaborators c WHERE c.project_id = p.project_id AND c.user_id = @user_id AND c.module_code = '066' AND c.is_active = TRUE) AS is_collaborator,
                EXISTS(
                    SELECT 1 FROM app_user_role_assignments ura
                    JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                    WHERE ura.user_id = @user_id AND ura.is_active = TRUE
                      AND r.role_code IN ('SUPER_ADMINISTRATOR','SYSTEM_ADMINISTRATOR','ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','EXECUTIVE','EXECUTIVE_LEADERSHIP')
                ) AS broad_scope
            FROM projects p
            WHERE p.project_id = @project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", effective.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await connection.DisposeAsync();
            return new(null, Results.NotFound(new { status = "project_not_found" }), actual, effective.Value, isViewAs, false, "none");
        }
        var isOwner = reader.GetBoolean(0);
        var canRead = isOwner || reader.GetBoolean(1) || reader.GetBoolean(2) || reader.GetBoolean(3) || reader.GetBoolean(4);
        await reader.DisposeAsync();
        if (!canRead)
        {
            await connection.DisposeAsync();
            return new(null, Results.Json(new { status = "forbidden", message = "The project is outside the effective user's FlowHive scope." }, statusCode: 403), actual, effective.Value, isViewAs, false, "none");
        }
        var canManage = isOwner && !isViewAs && actual == effective;
        if (write && !canManage)
        {
            await connection.DisposeAsync();
            return new(null, Results.Json(new { status = isViewAs ? "view_as_write_blocked" : "project_manager_ownership_required", message = "Only the assigned Project Manager can change the FlowHive PSA workspace. Exit View-As before writing." }, statusCode: 403), actual, effective.Value, isViewAs, false, "read_only");
        }
        return new(connection, null, actual, effective.Value, isViewAs, canManage, canManage ? "project_manager_full_control" : "authorized_read");
    }

    private static async Task<bool> MigrationReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = @migration_id) AND to_regclass('public.project_flowhive_meetings') IS NOT NULL;", connection);
        command.Parameters.AddWithValue("migration_id", MigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static IResult MigrationRequired() => Results.Json(new { status = "migration_103_required", requiredMigration = MigrationId, message = "Apply the FlowHive enterprise PSA migration before using meetings, immutable RAID history, or task reminders." }, statusCode: 503);
    private static Guid? EffectiveUserId(HttpContext context) => context.Items.TryGetValue("ProjectPulseEffectiveUserId", out var value) && value is Guid id ? id : context.Items.TryGetValue("ProjectPulseSessionUserId", out value) && value is Guid session ? session : null;
    private static Guid? ActualUserId(HttpContext context) => context.Items.TryGetValue("ProjectPulseActualUserId", out var value) && value is Guid id ? id : context.Items.TryGetValue("ProjectPulseSessionUserId", out value) && value is Guid session ? session : null;
    private static string Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string SafeFileName(string value)
    {
        var extension = Path.GetExtension(value).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(value);
        var safe = new string(stem.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray()).Trim('-');
        if (safe.Length == 0) safe = "meeting-recording";
        return safe[..Math.Min(safe.Length, 120)] + extension;
    }
    private static string SafePath(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!path.StartsWith(normalizedRoot, StringComparison.Ordinal)) throw new InvalidOperationException("FlowHive meeting storage path escaped the canonical upload root.");
        return path;
    }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static IResult Failure(HttpContext context, Exception exception, string action)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ProjectFlowHivePsaModule").LogError(exception, "Project FlowHive could not {Action}.", action);
        return Results.Json(new { status = "flowhive_psa_dependency_unavailable", message = "The FlowHive PSA workspace is temporarily unavailable. No partial change was accepted." }, statusCode: 503);
    }
}

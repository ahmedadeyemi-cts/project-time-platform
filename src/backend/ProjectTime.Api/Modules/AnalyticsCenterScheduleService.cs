using System.Net.Mail;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal static class AnalyticsCenterScheduleService
{
    internal static async Task<IResult> GetSchedulesAsync(HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(seed.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var schedules = await AnalyticsCenterScheduleRepository.LoadSchedulesAsync(
                connection,
                seed.Seed.Actor,
                includeDisabled: true,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                moduleName = "Analytics Center",
                status = schedules.Length == 0 ? "analytics_schedules_empty" : "analytics_schedules_loaded",
                schedules,
                access = AnalyticsCenterExperienceScope.Access(seed.Seed.Actor),
                capabilities = AnalyticsCenterExperienceScope.Capabilities(seed.Seed.Actor)
            });
        }
        catch (Exception exception)
        {
            return Failure(context, exception, "Analytics schedules are temporarily unavailable.");
        }
    }

    internal static async Task<IResult> GetRecipientOptionsAsync(HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        var actor = seed.Seed!.Actor;
        if (!AnalyticsCenterExperienceScope.CanManageSchedules(actor))
            return AnalyticsCenterExperienceScope.AccessDenied("Manage Analytics Schedules authority is required.");
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var search = context.Request.Query["search"].ToString();
            var options = await AnalyticsCenterScheduleRepository.LoadRecipientOptionsAsync(
                connection,
                actor,
                AnalyticsCenterExperienceScope.CanDeliverMultiple(actor),
                search,
                500,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = "analytics_recipient_options_loaded",
                recipients = options,
                multipleRecipientsAllowed = AnalyticsCenterExperienceScope.CanDeliverMultiple(actor),
                selfOnly = !AnalyticsCenterExperienceScope.CanDeliverMultiple(actor),
                rule = AnalyticsCenterExperienceScope.CanDeliverMultiple(actor)
                    ? "Authorized schedule managers can select multiple active Pulse users or governed @ussignal.com recipients. Each active Pulse user receives a report generated under that recipient's own role scope."
                    : "This role can schedule delivery only to the signed-in user's active Pulse email address."
            });
        }
        catch (Exception exception)
        {
            return Failure(context, exception, "Analytics recipient options are temporarily unavailable.");
        }
    }

    internal static async Task<IResult> SaveScheduleAsync(
        AnalyticsScheduleUpsertRequest request,
        HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        var actor = seed.Seed!.Actor;
        if (!AnalyticsCenterExperienceScope.CanManageSchedules(actor))
            return AnalyticsCenterExperienceScope.AccessDenied("Manage Analytics Schedules authority is required.");
        if (actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("saving a recurring Analytics schedule");

        var definition = EnterpriseReportingCatalog.Find(seed.Seed.Reporting, request.ReportCode);
        if (definition is null)
            return AnalyticsCenterExperienceScope.ReportNotFound(seed.Seed.Reporting);
        var criteria = AnalyticsCenterExperienceScope.NormalizeSelections(
            seed.Seed,
            request.Criteria ?? AnalyticsExperienceRequest.Empty(definition.Code)) with
        {
            ReportCode = definition.Code
        };
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var recipients = await NormalizeRecipientsAsync(
                connection,
                actor,
                request.Recipients,
                context.RequestAborted);
            if (recipients.Length == 0)
            {
                return Results.BadRequest(new
                {
                    module = "030",
                    status = "analytics_schedule_recipient_required",
                    message = "Select at least one active US Signal recipient."
                });
            }
            var cadence = AnalyticsCenterScheduleRepository.NormalizeCadence(request.Cadence);
            var localTime = request.LocalTime ?? new TimeOnly(8, 0);
            var timezone = NormalizeTimezone(request.TimezoneName);
            var nextRun = request.Enabled == false
                ? null
                : AnalyticsCenterScheduler.CalculateNextRun(
                    cadence,
                    request.DayOfWeek,
                    request.DayOfMonth,
                    request.MonthOfYear,
                    localTime,
                    timezone,
                    DateTimeOffset.UtcNow);
            var deliveryBoundary = AnalyticsCenterScheduleRepository.NormalizeDeliveryBoundary(
                request.DeliveryBoundary);
            if (deliveryBoundary == "production_governed"
                && recipients.Length > 1
                && !AnalyticsCenterExperienceScope.CanDeliverMultiple(actor))
            {
                return AnalyticsCenterExperienceScope.AccessDenied(
                    "Multiple-recipient production delivery requires Deliver Analytics Schedules authority.");
            }
            var normalizedRequest = request with
            {
                ReportCode = definition.Code,
                Criteria = criteria,
                Cadence = cadence,
                LocalTime = localTime,
                TimezoneName = timezone,
                ExportFormat = AnalyticsCenterScheduleRepository.NormalizeExportFormat(request.ExportFormat),
                DeliveryBoundary = deliveryBoundary,
                Recipients = recipients
            };
            var id = await AnalyticsCenterScheduleRepository.SaveScheduleAsync(
                connection,
                actor,
                normalizedRequest,
                criteria,
                recipients,
                nextRun,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = request.ScheduleId.HasValue
                    ? "analytics_schedule_updated"
                    : "analytics_schedule_created",
                scheduleId = id,
                nextRunAt = nextRun,
                recipientCount = recipients.Length,
                individualizedRecipientScope = true,
                format = normalizedRequest.ExportFormat,
                deliveryAuthority = "Module 065",
                message = "The recurring Analytics report schedule was saved."
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new
            {
                module = "030",
                status = "analytics_schedule_invalid",
                message = exception.Message
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Json(new
            {
                module = "030",
                status = "analytics_schedule_scope_required",
                message = exception.Message
            }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception exception)
        {
            return Failure(context, exception, "The Analytics schedule could not be saved.");
        }
    }

    internal static async Task<IResult> DeleteScheduleAsync(Guid scheduleId, HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        var actor = seed.Seed!.Actor;
        if (!AnalyticsCenterExperienceScope.CanManageSchedules(actor))
            return AnalyticsCenterExperienceScope.AccessDenied("Manage Analytics Schedules authority is required.");
        if (actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("deleting a recurring Analytics schedule");
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var deleted = await AnalyticsCenterScheduleRepository.DeleteScheduleAsync(
                connection,
                scheduleId,
                actor,
                context.RequestAborted);
            return deleted
                ? Results.Ok(new
                {
                    module = "030",
                    status = "analytics_schedule_deleted",
                    scheduleId,
                    message = "The schedule was deleted. Immutable prior run and delivery evidence remains available."
                })
                : Results.NotFound(new
                {
                    module = "030",
                    status = "analytics_schedule_not_found_or_outside_scope"
                });
        }
        catch (Exception exception)
        {
            return Failure(context, exception, "The Analytics schedule could not be deleted.");
        }
    }

    internal static async Task<IResult> RunScheduleNowAsync(Guid scheduleId, HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        var actor = seed.Seed!.Actor;
        if (!AnalyticsCenterExperienceScope.CanManageSchedules(actor))
            return AnalyticsCenterExperienceScope.AccessDenied("Manage Analytics Schedules authority is required.");
        if (actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("running a recurring Analytics schedule");
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var schedule = await AnalyticsCenterScheduleRepository.LoadScheduleAsync(
                connection,
                scheduleId,
                actor,
                context.RequestAborted);
            if (schedule is null)
                return Results.NotFound(new
                {
                    module = "030",
                    status = "analytics_schedule_not_found_or_outside_scope"
                });
            var summary = await ExecuteAsync(
                schedule,
                context.RequestServices,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = "analytics_schedule_run_completed",
                summary
            });
        }
        catch (Exception exception)
        {
            return Failure(context, exception, "The Analytics schedule could not be run.");
        }
    }

    internal static async Task<IResult> GetRunHistoryAsync(HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(seed.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var limit = int.TryParse(context.Request.Query["limit"], out var requested) ? requested : 100;
            var runs = await AnalyticsCenterScheduleRepository.LoadScheduleRunsAsync(
                connection,
                seed.Seed.Actor,
                limit,
                context.RequestAborted);
            var delivery = await AnalyticsCenterScheduleRepository.LoadDeliveryEvidenceAsync(
                connection,
                seed.Seed.Actor,
                limit * 5,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = runs.Length == 0 ? "analytics_schedule_history_empty" : "analytics_schedule_history_loaded",
                runs,
                delivery,
                immutable = true
            });
        }
        catch (Exception exception)
        {
            return Failure(context, exception, "Analytics schedule history is temporarily unavailable.");
        }
    }

    internal static async Task<IResult> GetReadinessAsync(HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(seed.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        var mail = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            context.RequestAborted);
        var migrationReady = false;
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            migrationReady = await AnalyticsCenterScheduleRepository.MigrationReadyAsync(
                connection,
                context.RequestAborted);
        }
        catch
        {
            migrationReady = false;
        }
        return Results.Ok(new
        {
            module = "030",
            status = migrationReady ? "analytics_schedule_readiness_loaded" : "analytics_schedule_migration_required",
            migration = new
            {
                id = AnalyticsCenterScheduleRepository.MigrationId,
                ready = migrationReady
            },
            module065 = mail,
            scheduler = new
            {
                enabled = true,
                multiReplicaLock = "PostgreSQL advisory lock",
                individualizedRecipientScope = true,
                formats = new[] { "pdf", "xlsx" },
                cadences = new[] { "daily", "weekdays", "weekly", "monthly", "quarterly", "yearly" }
            },
            access = AnalyticsCenterExperienceScope.Access(seed.Seed.Actor),
            capabilities = AnalyticsCenterExperienceScope.Capabilities(seed.Seed.Actor)
        });
    }

    internal static async Task<IResult> RunDueAsync(HttpContext context)
    {
        var seed = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!seed.Seed!.Actor.Broad
            && !seed.Seed.Actor.HasPermission("DELIVER_ANALYTICS_SCHEDULES", "MANAGE_ALL"))
            return AnalyticsCenterExperienceScope.AccessDenied("Deliver Analytics Schedules authority is required.");
        if (seed.Seed.Actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("running due Analytics schedules");
        var summary = await AnalyticsCenterScheduler.RunOnceAsync(
            context.RequestServices,
            context.RequestAborted);
        return Results.Ok(new
        {
            module = "030",
            status = "analytics_due_schedules_processed",
            summary
        });
    }

    internal static async Task<AnalyticsScheduleExecutionSummary> ExecuteAsync(
        AnalyticsSchedule schedule,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempts = new List<DeliveryBuffer>();
        foreach (var recipient in schedule.Recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? reportRunId = null;
            AnalyticsBrandedExport? export = null;
            Module065MailDeliveryResult delivery;
            try
            {
                var scopeUserId = recipient.UserId ?? schedule.OwnerEffectiveUserId;
                var report = await AnalyticsCenterEnterpriseExperienceModule.BuildScheduledReportAsync(
                    services,
                    scopeUserId,
                    schedule.ReportCode,
                    schedule.Criteria,
                    cancellationToken);
                reportRunId = report.ReportRunId;
                export = AnalyticsBrandedExportBuilder.Build(
                    await LoadRunAsync(report.ReportRunId, report.Actor, cancellationToken),
                    schedule.ExportFormat);

                if (schedule.DeliveryBoundary != "production_governed")
                {
                    delivery = new(
                        false,
                        schedule.DeliveryBoundary == "locked" ? "suppressed" : "queued",
                        "module_065",
                        schedule.DeliveryBoundary,
                        string.Empty,
                        schedule.DeliveryBoundary == "locked"
                            ? "ANALYTICS_SCHEDULE_LOCKED"
                            : "ANALYTICS_SCHEDULE_TEST_ONLY",
                        schedule.DeliveryBoundary == "locked"
                            ? "The Analytics schedule is locked and cannot send email."
                            : "The Analytics schedule is Test-only. The individualized branded report was generated and delivery evidence was queued without sending email.");
                }
                else
                {
                    var mailRecipient = new ProjectNotificationUser(
                        recipient.UserId,
                        recipient.DisplayName,
                        recipient.Email,
                        "analytics_schedule_recipient",
                        recipient.UserId.HasValue ? "active_projectpulse_user" : "governed_internal_email",
                        recipient.RecipientType);
                    var subject = string.IsNullOrWhiteSpace(schedule.EmailSubject)
                        ? $"US Signal Analytics: {report.Definition.Name}"
                        : schedule.EmailSubject;
                    var text = string.IsNullOrWhiteSpace(schedule.EmailMessage)
                        ? $"Your scheduled US Signal Analytics Center report, {report.Definition.Name}, is attached. The report was generated under your current Pulse access scope."
                        : schedule.EmailMessage;
                    var html = $"<p>{Web(text)}</p><p><strong>Report:</strong> {Web(report.Definition.Name)}<br/><strong>Generated:</strong> {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC<br/><strong>Rows:</strong> {report.Result.RowCount}</p><p>This individualized report was generated under the recipient's current Pulse authorization scope.</p>";
                    delivery = await Module065AnalyticsAttachmentDelivery.DeliverAsync(
                        subject,
                        text,
                        html,
                        mailRecipient,
                        [new Module065MailAttachment(export.FileName, export.ContentType, export.Content)],
                        null,
                        cancellationToken);
                }
            }
            catch (Exception exception)
            {
                delivery = new(
                    false,
                    "failed",
                    "module_065",
                    schedule.DeliveryBoundary,
                    string.Empty,
                    EnterpriseReportingSourceLoader.Diagnostic(exception),
                    "The recipient-specific Analytics report or delivery operation failed. Other recipients continue independently.");
            }
            attempts.Add(new DeliveryBuffer(recipient, reportRunId, export, delivery));
        }

        var completedAt = DateTimeOffset.UtcNow;
        var sent = attempts.Count(attempt => attempt.Delivery.Status == "sent");
        var queued = attempts.Count(attempt => attempt.Delivery.Status is "queued" or "suppressed");
        var failed = attempts.Count - sent - queued;
        var status = failed == 0 && sent == attempts.Count ? "complete"
            : failed == attempts.Count ? "failed"
            : sent == 0 && queued == attempts.Count ? "queued"
            : "partial";
        var diagnostic = failed > 0 ? "ANALYTICS_SCHEDULE_RECIPIENT_FAILURE" : string.Empty;
        var message = $"Recipients: {attempts.Count}; sent: {sent}; queued/suppressed: {queued}; failed: {failed}.";

        await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(cancellationToken);
        var scheduleRunId = await AnalyticsCenterScheduleRepository.InsertScheduleRunAsync(
            connection,
            schedule,
            startedAt,
            completedAt,
            status,
            attempts.Count,
            sent,
            queued,
            failed,
            diagnostic,
            message,
            cancellationToken);
        foreach (var attempt in attempts)
        {
            await AnalyticsCenterScheduleRepository.InsertDeliveryEvidenceAsync(
                connection,
                scheduleRunId,
                attempt.ReportRunId,
                attempt.Recipient,
                schedule.ExportFormat,
                attempt.Export?.Sha256 ?? string.Empty,
                attempt.Delivery,
                cancellationToken);
        }
        var next = schedule.Enabled
            ? AnalyticsCenterScheduler.CalculateNextRun(
                schedule.Cadence,
                schedule.DayOfWeek,
                schedule.DayOfMonth,
                schedule.MonthOfYear,
                schedule.LocalTime,
                schedule.TimezoneName,
                completedAt)
            : null;
        await AnalyticsCenterScheduleRepository.UpdateScheduleAfterRunAsync(
            connection,
            schedule.ScheduleId,
            startedAt,
            completedAt,
            next,
            status,
            cancellationToken);
        return new(
            scheduleRunId,
            schedule.ScheduleId,
            status,
            attempts.Count,
            sent,
            queued,
            failed,
            next,
            message);
    }

    private static async Task<EnterpriseReportRunRecord> LoadRunAsync(
        Guid runId,
        FinancialOperationsActor actor,
        CancellationToken cancellationToken)
    {
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        var run = await EnterpriseReportingRepository.LoadRunAsync(
            connection,
            actor,
            runId,
            cancellationToken);
        return run ?? throw new InvalidOperationException("The scheduled report run could not be reloaded for export.");
    }

    private static async Task<AnalyticsScheduleRecipientRequest[]> NormalizeRecipientsAsync(
        Npgsql.NpgsqlConnection connection,
        FinancialOperationsActor actor,
        AnalyticsScheduleRecipientRequest[]? requested,
        CancellationToken cancellationToken)
    {
        var canMultiple = AnalyticsCenterExperienceScope.CanDeliverMultiple(actor);
        var directory = await AnalyticsCenterScheduleRepository.LoadRecipientOptionsAsync(
            connection,
            actor,
            canMultiple,
            string.Empty,
            500,
            cancellationToken);
        var byId = directory.Where(option => option.UserId.HasValue)
            .ToDictionary(option => option.UserId!.Value);
        var byEmail = directory.ToDictionary(option => option.Email, StringComparer.OrdinalIgnoreCase);
        var candidates = requested is { Length: > 0 }
            ? requested
            : directory.Where(option => option.UserId == actor.EffectiveUserId)
                .Select(option => new AnalyticsScheduleRecipientRequest(
                    option.UserId,
                    option.DisplayName,
                    option.Email,
                    "to"))
                .ToArray();
        var result = new List<AnalyticsScheduleRecipientRequest>();
        foreach (var candidate in candidates.Take(50))
        {
            AnalyticsRecipientOption? resolved = null;
            if (candidate.UserId.HasValue)
                byId.TryGetValue(candidate.UserId.Value, out resolved);
            if (resolved is null && !string.IsNullOrWhiteSpace(candidate.Email))
                byEmail.TryGetValue(candidate.Email.Trim(), out resolved);
            if (resolved is not null)
            {
                result.Add(new(
                    resolved.UserId,
                    resolved.DisplayName,
                    resolved.Email,
                    NormalizeRecipientType(candidate.RecipientType)));
                continue;
            }
            var manualEmail = (candidate.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (!canMultiple || !IsEmail(manualEmail) || !manualEmail.EndsWith("@ussignal.com", StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(new(
                null,
                string.IsNullOrWhiteSpace(candidate.DisplayName) ? manualEmail : candidate.DisplayName.Trim(),
                manualEmail,
                NormalizeRecipientType(candidate.RecipientType)));
        }
        if (!canMultiple)
            result = result.Where(recipient => recipient.UserId == actor.EffectiveUserId).Take(1).ToList();
        return result
            .Where(recipient => IsEmail(recipient.Email))
            .GroupBy(recipient => $"{recipient.RecipientType}:{recipient.Email}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string NormalizeTimezone(string? value)
    {
        var timezone = string.IsNullOrWhiteSpace(value) ? "America/New_York" : value.Trim();
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timezone); return timezone; }
        catch { return "UTC"; }
    }

    private static string NormalizeRecipientType(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cc" => "cc",
            "bcc" => "bcc",
            _ => "to"
        };

    private static bool IsEmail(string? value)
    {
        try { _ = new MailAddress(value ?? string.Empty); return true; }
        catch { return false; }
    }

    private static string Web(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    internal static IResult MigrationRequired() => Results.Json(new
    {
        module = "030",
        status = "migration_060_required",
        migration = AnalyticsCenterScheduleRepository.MigrationId,
        message = "Analytics reports remain available. Recurring schedules, favorites, recent activity, and PDF export evidence require migration 060."
    }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Failure(HttpContext context, Exception exception, string message)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AnalyticsCenterScheduleService")
            .LogWarning(
                "Analytics schedule operation failed ({ExceptionType}) correlation {CorrelationId}.",
                exception.GetType().Name,
                context.TraceIdentifier);
        return Results.Json(new
        {
            module = "030",
            status = "analytics_schedule_source_unavailable",
            message,
            correlationId = context.TraceIdentifier,
            diagnosticCode = EnterpriseReportingSourceLoader.Diagnostic(exception),
            rawExceptionReturned = false
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record DeliveryBuffer(
        AnalyticsScheduleRecipient Recipient,
        Guid? ReportRunId,
        AnalyticsBrandedExport? Export,
        Module065MailDeliveryResult Delivery);
}

internal sealed record AnalyticsScheduleExecutionSummary(
    Guid ScheduleRunId,
    Guid ScheduleId,
    string Status,
    int RecipientCount,
    int SentCount,
    int QueuedCount,
    int FailedCount,
    DateTimeOffset? NextRunAt,
    string Message);

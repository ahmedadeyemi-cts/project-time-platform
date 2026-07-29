using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class ProjectNotificationProcessingService
{
    internal static async Task<EvaluationResult> EvaluateAndQueueAsync(
        NpgsqlConnection connection,
        ProjectNotificationActor actor,
        Guid? projectId,
        bool releaseEligible,
        string evaluationReason,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        ProjectNotificationSnapshotResult snapshot;
        try
        {
            snapshot = await ProjectNotificationFinancialSnapshotLoader.LoadAsync(
                connection,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return EvaluationResult.Fail(ProjectNotificationRepository.SourceFailure(
                "022",
                "project_financial_snapshot",
                exception,
                "Project cost rules could not be evaluated because the authoritative project source is unavailable."));
        }

        List<ProjectCostRoutingRule> rules;
        try
        {
            rules = await ProjectNotificationRepository.LoadRulesAsync(
                connection,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return EvaluationResult.Fail(ProjectNotificationRepository.SourceFailure(
                "022",
                "project_cost_alert_routing_rules",
                exception,
                "Project cost rules could not be loaded. Apply migration 050 before evaluating notifications."));
        }

        var activeRules = rules.Where(rule => rule.Enabled).ToArray();
        var projects = snapshot.Projects
            .Where(project => !projectId.HasValue || project.ProjectId == projectId.Value)
            .Where(project => ProjectNotificationEvaluator.CanAccessProject(actor, project))
            .ToArray();

        if (projectId.HasValue && projects.Length == 0)
        {
            return EvaluationResult.Fail(Results.NotFound(new
            {
                module = "022",
                status = "project_not_found_or_outside_scope",
                message = "The project was not found in the current notification scope."
            }));
        }

        var module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);
        var dispatchIds = new List<Guid>();
        var triggeredRuleCount = 0;
        var delivered = 0;
        var failures = 0;

        foreach (var project in projects)
        {
            foreach (var rule in activeRules.Where(rule =>
                         !rule.MetricCode.Equals(
                             "failed_project_data_refresh",
                             StringComparison.OrdinalIgnoreCase)))
            {
                var evaluation = ProjectNotificationEvaluator.EvaluateRule(rule, project);
                if (!evaluation.Triggered) continue;

                triggeredRuleCount++;
                var recipients = await ProjectNotificationEvaluator.DeriveRecipientsAsync(
                    connection,
                    project,
                    rule,
                    cancellationToken);
                var effectiveBoundary = ProjectNotificationEvaluator.MoreRestrictiveBoundary(
                    rule.DeliveryBoundary,
                    module065.RecipientBoundary);
                var eventKey = $"cost:{rule.RuleCode}:{project.ProjectId:D}:{DateTimeOffset.UtcNow:yyyyMMdd}";
                var subject = ProjectNotificationEvaluator.BuildCostAlertSubject(rule, project);
                var textBody = ProjectNotificationEvaluator.BuildCostAlertBody(
                    rule,
                    project,
                    evaluation);

                Guid dispatchId;
                try
                {
                    dispatchId = await ProjectNotificationRepository.UpsertDispatchAsync(
                        connection,
                        rule.RuleId,
                        null,
                        project,
                        eventKey,
                        rule.MetricCode,
                        rule.AlertSeverity,
                        "022",
                        project.BudgetStatus,
                        subject,
                        textBody,
                        ProjectNotificationEvaluator.Html(textBody),
                        effectiveBoundary,
                        recipients.Length == 0 ? "suppressed" : "held",
                        recipients,
                        new
                        {
                            rule.RuleCode,
                            evaluation.ObservedValue,
                            evaluation.ComparisonValue,
                            evaluation.ObservedUnit,
                            evaluation.Reason,
                            project.BudgetStatus,
                            project.MissingFinancialInformation,
                            evaluationReason,
                            sourceStates = snapshot.Sources
                        },
                        cancellationToken);
                    dispatchIds.Add(dispatchId);
                }
                catch
                {
                    failures++;
                    continue;
                }

                if (releaseEligible && actor.CanDeliver && recipients.Length > 0)
                {
                    var delivery = await DeliverDispatchAsync(
                        connection,
                        dispatchId,
                        actor.ActualUserId,
                        evaluationReason,
                        context,
                        cancellationToken);
                    if (delivery.Sent) delivered++;
                    else if (delivery.Status == "failed") failures++;
                }
            }
        }

        var unavailableSources = snapshot.Sources
            .Where(source => source.Status == "unavailable")
            .ToArray();
        var sourceFailureRule = activeRules.FirstOrDefault(rule =>
            rule.MetricCode.Equals(
                "failed_project_data_refresh",
                StringComparison.OrdinalIgnoreCase));

        if (sourceFailureRule is not null && unavailableSources.Length > 0)
        {
            var recipients = await ProjectNotificationEvaluator.LoadGlobalRecipientsAsync(
                connection,
                sourceFailureRule,
                cancellationToken);
            var effectiveBoundary = ProjectNotificationEvaluator.MoreRestrictiveBoundary(
                sourceFailureRule.DeliveryBoundary,
                module065.RecipientBoundary);
            var textBody = "Project financial refresh requires attention.\n\n"
                + string.Join("\n", unavailableSources.Select(source =>
                    $"- {source.Name}: {source.Message} ({source.DiagnosticCode})"));
            try
            {
                var dispatchId = await ProjectNotificationRepository.UpsertDispatchAsync(
                    connection,
                    sourceFailureRule.RuleId,
                    null,
                    null,
                    $"source:{sourceFailureRule.RuleCode}:{DateTimeOffset.UtcNow:yyyyMMddHH}",
                    sourceFailureRule.MetricCode,
                    sourceFailureRule.AlertSeverity,
                    "022",
                    "financial_data_source_unavailable",
                    "ProjectPulse project financial refresh requires attention",
                    textBody,
                    ProjectNotificationEvaluator.Html(textBody),
                    effectiveBoundary,
                    recipients.Length == 0 ? "suppressed" : "held",
                    recipients,
                    new
                    {
                        unavailableSources,
                        evaluationReason
                    },
                    cancellationToken);
                dispatchIds.Add(dispatchId);
                triggeredRuleCount++;
            }
            catch
            {
                failures++;
            }
        }

        return EvaluationResult.Success(
            projects.Length,
            activeRules.Length,
            triggeredRuleCount,
            dispatchIds.ToArray(),
            delivered,
            failures,
            releaseEligible,
            module065,
            snapshot.Sources,
            DateTimeOffset.UtcNow);
    }

    internal static async Task<NotificationDeliveryOutcome> DeliverDispatchAsync(
        NpgsqlConnection connection,
        Guid dispatchId,
        Guid? releasedByUserId,
        string reason,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        var dispatch = await ProjectNotificationRepository.LoadDispatchAsync(
            connection,
            dispatchId,
            cancellationToken);
        if (dispatch is null)
        {
            return NotificationDeliveryOutcome.NotFound();
        }

        if (dispatch.DeliveryStatus == "sent")
        {
            return new(
                true,
                "notification_already_sent",
                dispatch.ProviderSource,
                dispatch.DeliveryBoundary,
                dispatch.ProviderMessageId,
                string.Empty,
                "The notification was already delivered. Duplicate delivery was prevented.",
                dispatch.DispatchId,
                dispatch.AttemptCount);
        }

        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);
        var effectiveBoundary = ProjectNotificationEvaluator.MoreRestrictiveBoundary(
            dispatch.DeliveryBoundary,
            readiness.RecipientBoundary);
        var delivery = effectiveBoundary == "production_governed"
            ? await Module065ProjectNotificationDelivery.DeliverAsync(
                dispatch.Subject,
                dispatch.TextBody,
                dispatch.HtmlBody,
                dispatch.Recipients,
                context,
                cancellationToken)
            : new Module065MailDeliveryResult(
                false,
                effectiveBoundary == "locked" ? "suppressed" : "queued",
                readiness.ConfiguredProvider,
                effectiveBoundary,
                string.Empty,
                "RECIPIENT_BOUNDARY_PREVENTED_DELIVERY",
                effectiveBoundary == "test_only"
                    ? "The dispatch remains in Test-only mode. Module 065 did not send live email."
                    : "The dispatch is locked. Module 065 did not send live email.");

        try
        {
            await ProjectNotificationRepository.RecordDeliveryAsync(
                connection,
                dispatch,
                delivery,
                releasedByUserId,
                reason,
                context?.TraceIdentifier ?? "scheduler",
                cancellationToken);
        }
        catch (Exception exception)
        {
            return new(
                false,
                "failed",
                delivery.Provider,
                delivery.RecipientBoundary,
                delivery.ProviderMessageId,
                ProjectNotificationRepository.Diagnostic(exception),
                "The delivery result could not be recorded. Retry after the database source is restored.",
                dispatch.DispatchId,
                dispatch.AttemptCount);
        }

        return new(
            delivery.Sent,
            delivery.Status,
            delivery.Provider,
            delivery.RecipientBoundary,
            delivery.ProviderMessageId,
            delivery.DiagnosticCode,
            delivery.Message,
            dispatch.DispatchId,
            dispatch.AttemptCount + 1);
    }

    internal static async Task<ScheduleRunSummary> RunDueSchedulesAsync(
        NpgsqlConnection connection,
        Guid? actorUserId,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var schedules = (await ProjectNotificationRepository.LoadSchedulesAsync(
                connection,
                cancellationToken))
            .Where(schedule => schedule.Enabled
                && (!schedule.NextRunAt.HasValue || schedule.NextRunAt.Value <= now))
            .OrderBy(schedule => schedule.NextRunAt)
            .ToArray();
        var evaluatedProjects = 0;
        var queued = 0;
        var delivered = 0;
        var failures = 0;

        foreach (var schedule in schedules)
        {
            try
            {
                await ProjectNotificationRepository.UpdateScheduleRunStateAsync(
                    connection,
                    schedule.ScheduleId,
                    true,
                    "running",
                    null,
                    cancellationToken);

                if (schedule.ScheduleType == "cost_alert_evaluation")
                {
                    var actor = actorUserId.HasValue
                        ? await ProjectNotificationRepository.LoadActorAsync(
                            connection,
                            actorUserId.Value,
                            actorUserId.Value,
                            false,
                            cancellationToken)
                        : SchedulerActor();
                    var result = await EvaluateAndQueueAsync(
                        connection,
                        actor,
                        null,
                        schedule.DeliveryBoundary == "production_governed",
                        $"Scheduled evaluation {schedule.ScheduleCode}.",
                        context,
                        cancellationToken);
                    evaluatedProjects += result.EvaluatedProjectCount;
                    queued += result.DispatchIds.Length;
                    delivered += result.DeliveredCount;
                    failures += result.FailureCount;
                }
                else
                {
                    var reminder = await QueueScheduledRemindersAsync(
                        connection,
                        schedule,
                        cancellationToken);
                    queued += reminder.QueuedCount;
                    failures += reminder.FailureCount;
                }

                var nextRun = ProjectNotificationScheduler.CalculateNextRun(
                    schedule.ScheduleType,
                    schedule.DayOfWeek,
                    schedule.LocalTime,
                    schedule.TimezoneName,
                    schedule.DaysBeforeMonthEnd,
                    now.AddMinutes(1));
                await ProjectNotificationRepository.UpdateScheduleRunStateAsync(
                    connection,
                    schedule.ScheduleId,
                    false,
                    "completed",
                    nextRun,
                    cancellationToken);
            }
            catch
            {
                failures++;
                await ProjectNotificationRepository.UpdateScheduleRunStateAsync(
                    connection,
                    schedule.ScheduleId,
                    false,
                    "failed",
                    ProjectNotificationScheduler.CalculateNextRun(
                        schedule.ScheduleType,
                        schedule.DayOfWeek,
                        schedule.LocalTime,
                        schedule.TimezoneName,
                        schedule.DaysBeforeMonthEnd,
                        now.AddMinutes(5)),
                    cancellationToken);
            }
        }

        var dueDispatches = await ProjectNotificationRepository.LoadDueDispatchIdsAsync(
            connection,
            cancellationToken);
        foreach (var dispatchId in dueDispatches)
        {
            var outcome = await DeliverDispatchAsync(
                connection,
                dispatchId,
                actorUserId,
                "Scheduled Group 4 delivery attempt.",
                context,
                cancellationToken);
            if (outcome.Sent) delivered++;
            else if (outcome.Status == "failed") failures++;
        }

        return new(
            schedules.Length,
            evaluatedProjects,
            queued,
            delivered,
            failures,
            dueDispatches.Count,
            DateTimeOffset.UtcNow);
    }

    private static async Task<ScheduledReminderResult> QueueScheduledRemindersAsync(
        NpgsqlConnection connection,
        ProjectNotificationSchedule schedule,
        CancellationToken cancellationToken)
    {
        ProjectNotificationSnapshotResult snapshot;
        try
        {
            snapshot = await ProjectNotificationFinancialSnapshotLoader.LoadAsync(
                connection,
                cancellationToken);
        }
        catch
        {
            return new(0, 1);
        }

        var module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            null,
            cancellationToken);
        var queued = 0;
        var failures = 0;

        foreach (var project in snapshot.Projects)
        {
            var shouldQueue = schedule.ScheduleType switch
            {
                "weekly_reminder" => project.BudgetStatus is
                    "approaching_budget" or "over_budget" or "missing_financial_information",
                "monday_reminder" => project.BudgetStatus is
                    "over_budget" or "missing_financial_information",
                "month_end_reminder" => project.ProjectStatus is not
                    ("completed" or "closed" or "cancelled"),
                "escalation" => project.BudgetStatus == "over_budget",
                _ => false
            };
            if (!shouldQueue) continue;

            var recipients = ProjectNotificationEvaluator.DeriveRecipients(
                project,
                [
                    "project_manager",
                    "solution_architect",
                    "account_executive",
                    "project_team_coordinator"
                ],
                null);
            if (recipients.Length == 0)
            {
                failures++;
                continue;
            }

            var body = ProjectNotificationEvaluator.BuildScheduledReminderBody(
                schedule,
                project);
            try
            {
                await ProjectNotificationRepository.UpsertDispatchAsync(
                    connection,
                    null,
                    schedule.ScheduleId,
                    project,
                    $"schedule:{schedule.ScheduleCode}:{project.ProjectId:D}:{DateTimeOffset.UtcNow:yyyyMMdd}",
                    schedule.ScheduleType,
                    project.BudgetStatus == "over_budget" ? "critical" : "warning",
                    "023",
                    project.BudgetStatus,
                    $"{schedule.ScheduleName}: {project.ProjectCode} — {project.ProjectName}",
                    body,
                    ProjectNotificationEvaluator.Html(body),
                    ProjectNotificationEvaluator.MoreRestrictiveBoundary(
                        schedule.DeliveryBoundary,
                        module065.RecipientBoundary),
                    schedule.DeliveryBoundary == "production_governed" ? "queued" : "held",
                    recipients,
                    new
                    {
                        schedule.ScheduleCode,
                        project.BudgetStatus,
                        project.MissingFinancialInformation,
                        sourceStates = snapshot.Sources
                    },
                    cancellationToken);
                queued++;
            }
            catch
            {
                failures++;
            }
        }

        return new(queued, failures);
    }

    private static ProjectNotificationActor SchedulerActor() => new(
        Guid.Empty,
        Guid.Empty,
        "projectpulse-scheduler@system.local",
        "ProjectPulse Notification Scheduler",
        new HashSet<string>(["SUPER_ADMINISTRATOR"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["MANAGE_ALL"], StringComparer.OrdinalIgnoreCase),
        false);

    internal sealed record EvaluationResult(
        IResult? Failure,
        int EvaluatedProjectCount,
        int ActiveRuleCount,
        int TriggeredRuleCount,
        Guid[] DispatchIds,
        int DeliveredCount,
        int FailureCount,
        bool ReleaseRequested,
        Module065MailReadiness? Module065,
        ProjectNotificationSourceState[] Sources,
        DateTimeOffset EvaluatedAt)
    {
        internal static EvaluationResult Success(
            int evaluatedProjectCount,
            int activeRuleCount,
            int triggeredRuleCount,
            Guid[] dispatchIds,
            int deliveredCount,
            int failureCount,
            bool releaseRequested,
            Module065MailReadiness module065,
            ProjectNotificationSourceState[] sources,
            DateTimeOffset evaluatedAt) => new(
                null,
                evaluatedProjectCount,
                activeRuleCount,
                triggeredRuleCount,
                dispatchIds,
                deliveredCount,
                failureCount,
                releaseRequested,
                module065,
                sources,
                evaluatedAt);

        internal static EvaluationResult Fail(IResult failure) => new(
            failure,
            0,
            0,
            0,
            [],
            0,
            1,
            false,
            null,
            [],
            DateTimeOffset.UtcNow);
    }

    internal sealed record NotificationDeliveryOutcome(
        bool Sent,
        string Status,
        string Provider,
        string RecipientBoundary,
        string ProviderMessageId,
        string DiagnosticCode,
        string Message,
        Guid? DispatchId,
        int AttemptNumber)
    {
        internal static NotificationDeliveryOutcome NotFound() => new(
            false,
            "notification_dispatch_not_found",
            "module_065",
            "locked",
            string.Empty,
            "DISPATCH_NOT_FOUND",
            "The notification dispatch was not found.",
            null,
            0);
    }

    internal sealed record ScheduleRunSummary(
        int DueScheduleCount,
        int EvaluatedProjectCount,
        int DispatchesQueued,
        int Delivered,
        int Failures,
        int DueDispatchCount,
        DateTimeOffset CompletedAt);

    private sealed record ScheduledReminderResult(int QueuedCount, int FailureCount);
}

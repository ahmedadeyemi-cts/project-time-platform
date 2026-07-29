namespace ProjectTime.Api.Modules;

internal static class ProjectNotificationAutomationService
{
    private const string ContractVersion = "2026-07-28.1";

    internal static async Task<IResult> GetRoutingRulesAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var rules = await ProjectNotificationRepository.LoadRulesAsync(
                connection,
                context.RequestAborted);
            var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
                context,
                context.RequestAborted);

            return Results.Ok(new
            {
                module = "022",
                moduleName = "Cost Alert Routing Rules",
                status = "project_cost_routing_rules_loaded",
                contractVersion = ContractVersion,
                generatedAt = DateTimeOffset.UtcNow,
                access = AccessContract(access.Actor!),
                count = rules.Count,
                rules,
                supportedMetrics = ProjectNotificationEvaluator.AllowedMetricCodes
                    .OrderBy(value => value),
                supportedRecipientRoles = ProjectNotificationEvaluator.AllowedRecipientRoles
                    .OrderBy(value => value),
                recipientDerivation = RecipientDerivationContract(),
                module065 = readiness,
                security = SecurityContract()
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "022",
                "project_cost_alert_routing_rules",
                exception,
                "Cost alert routing rules could not be loaded. Apply migration 050 and retry."
            );
        }
    }

    internal static async Task<IResult> UpdateRoutingRuleAsync(
        Guid ruleId,
        ProjectCostRoutingRuleUpdateRequest request,
        HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanManageRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;

        try
        {
            var prior = (await ProjectNotificationRepository.LoadRulesAsync(
                    connection,
                    context.RequestAborted))
                .FirstOrDefault(rule => rule.RuleId == ruleId);
            if (prior is null)
            {
                return Results.NotFound(new
                {
                    module = "022",
                    status = "routing_rule_not_found",
                    message = "The selected routing rule was not found."
                });
            }

            var metric = ProjectNotificationEvaluator.NormalizeMetric(
                request.MetricCode,
                prior.MetricCode);
            var comparison = ProjectNotificationEvaluator.NormalizeComparison(
                request.ComparisonOperator,
                prior.ComparisonOperator);
            var unit = ProjectNotificationEvaluator.NormalizeUnit(
                request.ThresholdUnit,
                prior.ThresholdUnit);
            var severity = ProjectNotificationEvaluator.NormalizeSeverity(
                request.AlertSeverity,
                prior.AlertSeverity);
            var boundary = ProjectNotificationEvaluator.NormalizeBoundary(
                request.DeliveryBoundary,
                prior.DeliveryBoundary);
            var recipientRoles = ProjectNotificationEvaluator.NormalizeRecipientRoles(
                request.RecipientRoles,
                prior.RecipientRoles);
            var threshold = request.ThresholdValue ?? prior.ThresholdValue;
            var escalationAfter = request.EscalationAfterMinutes
                ?? prior.EscalationAfterMinutes;
            var validation = ProjectNotificationEvaluator.ValidateRule(
                metric,
                comparison,
                threshold,
                unit,
                recipientRoles,
                escalationAfter);
            if (validation is not null)
            {
                return Results.BadRequest(new
                {
                    module = "022",
                    status = "invalid_routing_rule",
                    message = validation
                });
            }

            var replacement = prior with
            {
                RuleName = ProjectNotificationEvaluator.Clean(
                    request.RuleName,
                    220,
                    prior.RuleName),
                MetricCode = metric,
                ComparisonOperator = comparison,
                ThresholdValue = threshold,
                ThresholdUnit = unit,
                AlertSeverity = severity,
                RecipientRoles = recipientRoles,
                OptionalEscalationManagerUserId = request.OptionalEscalationManagerUserId,
                EscalationAfterMinutes = escalationAfter,
                DeliveryBoundary = boundary,
                Enabled = request.Enabled ?? prior.Enabled,
                Description = ProjectNotificationEvaluator.Clean(
                    request.Description,
                    2000,
                    prior.Description),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await ProjectNotificationRepository.UpdateRuleAsync(
                connection,
                actor,
                prior,
                replacement,
                ProjectNotificationEvaluator.Clean(
                    request.ChangeReason,
                    1000,
                    "Updated from Module 022 Cost Alert Routing Rules."),
                context.TraceIdentifier,
                context.RequestAborted);

            return Results.Ok(new
            {
                module = "022",
                status = "routing_rule_updated",
                rule = replacement,
                message = "The project cost routing rule was updated and recorded in immutable configuration history."
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "022",
                "routing_rule_write",
                exception,
                "The routing rule could not be saved. No partial configuration was committed."
            );
        }
    }

    internal static async Task<IResult> GetSchedulesAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var schedules = await ProjectNotificationRepository.LoadSchedulesAsync(
                connection,
                context.RequestAborted);
            var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
                context,
                context.RequestAborted);

            return Results.Ok(new
            {
                module = "023",
                moduleName = "Notification Scheduling",
                status = "project_notification_schedules_loaded",
                contractVersion = ContractVersion,
                generatedAt = DateTimeOffset.UtcNow,
                access = AccessContract(access.Actor!),
                count = schedules.Count,
                schedules,
                allowedTimezones = new[]
                {
                    "America/Chicago",
                    "America/New_York",
                    "America/Denver",
                    "America/Los_Angeles",
                    "UTC"
                },
                deliveryBoundaries = new[]
                {
                    "test_only",
                    "production_governed",
                    "locked"
                },
                module065 = readiness,
                security = SecurityContract()
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "023",
                "project_notification_schedules",
                exception,
                "Notification schedules could not be loaded. Apply migration 050 and retry."
            );
        }
    }

    internal static async Task<IResult> UpdateScheduleAsync(
        Guid scheduleId,
        ProjectNotificationScheduleUpdateRequest request,
        HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanManageSchedules);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;

        try
        {
            var prior = (await ProjectNotificationRepository.LoadSchedulesAsync(
                    connection,
                    context.RequestAborted))
                .FirstOrDefault(schedule => schedule.ScheduleId == scheduleId);
            if (prior is null)
            {
                return Results.NotFound(new
                {
                    module = "023",
                    status = "notification_schedule_not_found",
                    message = "The selected notification schedule was not found."
                });
            }

            var scheduleType = ProjectNotificationEvaluator.NormalizeScheduleType(
                request.ScheduleType,
                prior.ScheduleType);
            var timezone = ProjectNotificationEvaluator.NormalizeTimezone(
                request.TimezoneName,
                prior.TimezoneName);
            var localTime = TimeOnly.TryParse(request.LocalTime, out var parsedLocalTime)
                ? parsedLocalTime
                : prior.LocalTime;
            var quietStart = ProjectNotificationEvaluator.ParseTimeOrExisting(
                request.QuietHoursStart,
                prior.QuietHoursStart);
            var quietEnd = ProjectNotificationEvaluator.ParseTimeOrExisting(
                request.QuietHoursEnd,
                prior.QuietHoursEnd);
            var dayOfWeek = request.DayOfWeek ?? prior.DayOfWeek;
            var daysBeforeMonthEnd = request.DaysBeforeMonthEnd
                ?? prior.DaysBeforeMonthEnd;
            var escalationAfter = request.EscalationAfterMinutes
                ?? prior.EscalationAfterMinutes;
            var validation = ProjectNotificationEvaluator.ValidateSchedule(
                scheduleType,
                dayOfWeek,
                daysBeforeMonthEnd,
                escalationAfter,
                quietStart,
                quietEnd);
            if (validation is not null)
            {
                return Results.BadRequest(new
                {
                    module = "023",
                    status = "invalid_notification_schedule",
                    message = validation
                });
            }

            var replacement = prior with
            {
                ScheduleName = ProjectNotificationEvaluator.Clean(
                    request.ScheduleName,
                    220,
                    prior.ScheduleName),
                ScheduleType = scheduleType,
                DayOfWeek = dayOfWeek,
                LocalTime = localTime,
                TimezoneName = timezone,
                DaysBeforeMonthEnd = daysBeforeMonthEnd,
                EscalationAfterMinutes = escalationAfter,
                QuietHoursStart = quietStart,
                QuietHoursEnd = quietEnd,
                Enabled = request.Enabled ?? prior.Enabled,
                DeliveryBoundary = ProjectNotificationEvaluator.NormalizeBoundary(
                    request.DeliveryBoundary,
                    prior.DeliveryBoundary),
                NextRunAt = ProjectNotificationScheduler.CalculateNextRun(
                    scheduleType,
                    dayOfWeek,
                    localTime,
                    timezone,
                    daysBeforeMonthEnd,
                    DateTimeOffset.UtcNow),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await ProjectNotificationRepository.UpdateScheduleAsync(
                connection,
                actor,
                prior,
                replacement,
                ProjectNotificationEvaluator.Clean(
                    request.ChangeReason,
                    1000,
                    "Updated from Module 023 Notification Scheduling."),
                context.TraceIdentifier,
                context.RequestAborted);

            return Results.Ok(new
            {
                module = "023",
                status = "notification_schedule_updated",
                schedule = replacement,
                message = "The notification schedule was updated and recorded in immutable configuration history."
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "023",
                "notification_schedule_write",
                exception,
                "The notification schedule could not be saved. No partial configuration was committed."
            );
        }
    }

    internal static async Task<IResult> GetModule065ReadinessAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            context.RequestAborted);
        return Results.Ok(new
        {
            module = "065",
            status = readiness.RuntimeReady
                ? "module_065_mail_runtime_ready"
                : "module_065_mail_runtime_attention_required",
            readiness,
            configurationOwner = "Module 065 Microsoft Integration Connection",
            retiredModule067Read = false,
            credentialsAcceptedByGroup4 = false,
            credentialsReturned = false,
            message = readiness.Message
        });
    }

    internal static async Task<IResult> EvaluateAsync(
        ProjectNotificationEvaluationRequest request,
        HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanManageRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        var evaluation = await ProjectNotificationProcessingService.EvaluateAndQueueAsync(
            connection,
            access.Actor!,
            request.ProjectId,
            request.ReleaseEligible,
            ProjectNotificationEvaluator.Clean(
                request.EvaluationReason,
                1000,
                "Manual project cost-alert evaluation."),
            context,
            context.RequestAborted);
        if (evaluation.Failure is not null) return evaluation.Failure;

        return Results.Ok(new
        {
            module = "022",
            status = "project_cost_routing_evaluated",
            evaluatedAt = evaluation.EvaluatedAt,
            evaluatedProjectCount = evaluation.EvaluatedProjectCount,
            activeRuleCount = evaluation.ActiveRuleCount,
            triggeredRuleCount = evaluation.TriggeredRuleCount,
            dispatchesQueued = evaluation.DispatchIds.Length,
            dispatchIds = evaluation.DispatchIds,
            delivered = evaluation.DeliveredCount,
            failures = evaluation.FailureCount,
            releaseRequested = evaluation.ReleaseRequested,
            module065 = evaluation.Module065,
            sources = evaluation.Sources,
            message = "Project cost routing rules were evaluated against authoritative project financial data."
        });
    }

    internal static async Task<IResult> GetDispatchesAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var limit = Math.Clamp(
                int.TryParse(context.Request.Query["limit"], out var requested)
                    ? requested
                    : 100,
                1,
                250);
            var status = ProjectNotificationEvaluator.Clean(
                context.Request.Query["status"],
                40,
                string.Empty);
            var dispatches = await ProjectNotificationRepository.LoadDispatchesAsync(
                connection,
                access.Actor!,
                status,
                limit,
                context.RequestAborted);
            var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
                context,
                context.RequestAborted);

            return Results.Ok(new
            {
                module = "032",
                moduleName = "Notification Delivery Monitor",
                status = "project_notification_dispatches_loaded",
                contractVersion = ContractVersion,
                generatedAt = DateTimeOffset.UtcNow,
                access = AccessContract(access.Actor!),
                count = dispatches.Count,
                dispatches,
                module065 = readiness,
                security = SecurityContract()
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "032",
                "project_notification_dispatches",
                exception,
                "Notification dispatches could not be loaded. Apply migration 050 and retry."
            );
        }
    }

    internal static async Task<IResult> GetDeliveryMonitorAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var dispatches = await ProjectNotificationRepository.LoadDispatchesAsync(
                connection,
                access.Actor!,
                string.Empty,
                100,
                context.RequestAborted);
            var attempts = await ProjectNotificationRepository.LoadRecentAttemptsAsync(
                connection,
                100,
                context.RequestAborted);
            var schedules = await ProjectNotificationRepository.LoadSchedulesAsync(
                connection,
                context.RequestAborted);
            var rules = await ProjectNotificationRepository.LoadRulesAsync(
                connection,
                context.RequestAborted);
            var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
                context,
                context.RequestAborted);
            var snapshot = await LoadSnapshotSafelyAsync(
                connection,
                context.RequestAborted);

            return Results.Ok(new
            {
                module = "032",
                moduleName = "Notification Delivery Monitor",
                status = "notification_delivery_monitor_loaded",
                contractVersion = ContractVersion,
                generatedAt = DateTimeOffset.UtcNow,
                access = AccessContract(access.Actor!),
                summary = new
                {
                    dispatchCount = dispatches.Count,
                    queued = dispatches.Count(item => item.DeliveryStatus == "queued"),
                    held = dispatches.Count(item => item.DeliveryStatus is "held" or "preview_ready"),
                    sent = dispatches.Count(item => item.DeliveryStatus == "sent"),
                    failed = dispatches.Count(item => item.DeliveryStatus == "failed"),
                    suppressed = dispatches.Count(item => item.DeliveryStatus == "suppressed"),
                    deliveryAttemptCount = attempts.Count,
                    activeRules = rules.Count(item => item.Enabled),
                    activeSchedules = schedules.Count(item => item.Enabled),
                    sourceFailures = snapshot.Sources.Count(item => item.Status == "unavailable")
                },
                module065 = readiness,
                sources = snapshot.Sources,
                schedules = schedules.OrderBy(item => item.NextRunAt),
                dispatches,
                deliveryAttempts = attempts,
                productivityPurpose = "A single operational inbox for notification dispatches, automatically derived recipients, provider readiness, failures, retries, and audit evidence.",
                security = SecurityContract()
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "032",
                "notification_delivery_monitor",
                exception,
                "The Notification Delivery Monitor could not be loaded. Apply migration 050 and retry."
            );
        }
    }

    internal static async Task<IResult> ReleaseDispatchAsync(
        Guid dispatchId,
        ProjectNotificationReleaseRequest request,
        HttpContext context) => await ReleaseOrRetryAsync(
            dispatchId,
            request,
            context,
            false);

    internal static async Task<IResult> RetryDispatchAsync(
        Guid dispatchId,
        ProjectNotificationReleaseRequest request,
        HttpContext context) => await ReleaseOrRetryAsync(
            dispatchId,
            request,
            context,
            true);

    private static async Task<IResult> ReleaseOrRetryAsync(
        Guid dispatchId,
        ProjectNotificationReleaseRequest request,
        HttpContext context,
        bool retry)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanDeliver);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        var delivery = await ProjectNotificationProcessingService.DeliverDispatchAsync(
            connection,
            dispatchId,
            access.Actor!.ActualUserId,
            ProjectNotificationEvaluator.Clean(
                request.Reason,
                1000,
                retry
                    ? "Manual retry from Module 032 Notification Delivery Monitor."
                    : "Manual release from Module 032 Notification Delivery Monitor."),
            context,
            context.RequestAborted);

        var response = new
        {
            module = "032",
            status = delivery.Status,
            sent = delivery.Sent,
            provider = delivery.Provider,
            recipientBoundary = delivery.RecipientBoundary,
            providerMessageId = delivery.ProviderMessageId,
            diagnosticCode = delivery.DiagnosticCode,
            message = delivery.Message,
            dispatchId = delivery.DispatchId,
            attemptNumber = delivery.AttemptNumber
        };
        return Results.Json(
            response,
            statusCode: delivery.Status == "notification_dispatch_not_found"
                ? StatusCodes.Status404NotFound
                : delivery.Sent
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status202Accepted);
    }

    internal static async Task<IResult> RunDueAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanManageSchedules);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var summary = await ProjectNotificationProcessingService.RunDueSchedulesAsync(
                connection,
                access.Actor!.ActualUserId,
                context,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "023",
                status = "due_notification_schedules_processed",
                summary,
                message = "Due schedules were evaluated. Module 065 remained the only delivery authority."
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "023",
                "due_notification_schedule_processing",
                exception,
                "Due notification schedules could not be processed. Retry after the source is restored."
            );
        }
    }

    internal static async Task<IResult> QueueCloseoutAsync(
        ProjectCloseoutNotificationRequest request,
        HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            ProjectNotificationEvaluator.CanQueueCloseout);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;

        var snapshot = await LoadSnapshotSafelyAsync(
            connection,
            context.RequestAborted);
        var project = request.ProjectId.HasValue
            ? snapshot.Projects.FirstOrDefault(item => item.ProjectId == request.ProjectId.Value)
            : snapshot.Projects.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(request.ProjectCode)
                && item.ProjectCode.Equals(
                    request.ProjectCode.Trim(),
                    StringComparison.OrdinalIgnoreCase));

        if (project is null)
        {
            return Results.NotFound(new
            {
                module = "041",
                status = "project_not_found_or_outside_scope",
                message = "The project could not be resolved from authoritative ProjectPulse data."
            });
        }

        if (!ProjectNotificationEvaluator.CanAccessProject(actor, project))
        {
            return Results.Json(new
            {
                module = "041",
                status = "closeout_project_access_denied",
                message = "The selected project is outside the current user's closeout-notification scope."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var subject = ProjectNotificationEvaluator.Clean(
            request.Subject,
            500,
            $"Project closeout: {project.ProjectCode} — {project.ProjectName}");
        var textBody = ProjectNotificationEvaluator.Clean(
            request.Body,
            20000,
            ProjectNotificationEvaluator.BuildCloseoutBody(project));
        var recipients = ProjectNotificationEvaluator.DeriveRecipients(
            project,
            [
                "project_manager",
                "assigned_engineers",
                "solution_architect",
                "account_executive",
                "project_team_coordinator"
            ],
            null);

        if (recipients.Length == 0)
        {
            return Results.BadRequest(new
            {
                module = "041",
                status = "no_authoritative_closeout_recipients",
                message = "No email-ready project recipients could be derived from the authoritative project team."
            });
        }

        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            context.RequestAborted);
        Guid dispatchId;
        try
        {
            dispatchId = await ProjectNotificationRepository.UpsertDispatchAsync(
                connection,
                null,
                null,
                project,
                $"closeout:{project.ProjectId:D}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                "project_closeout",
                "informational",
                "041",
                "closeout_requested",
                subject,
                textBody,
                ProjectNotificationEvaluator.Html(textBody),
                readiness.RecipientBoundary,
                "queued",
                recipients,
                new
                {
                    trigger = "module_041_closeout",
                    actualUserId = actor.ActualUserId,
                    effectiveUserId = actor.EffectiveUserId,
                    actorEmail = actor.Email,
                    serverDerivedRecipients = true,
                    clientRecipientListIgnored = true,
                    sourceStates = snapshot.Sources
                },
                context.RequestAborted);
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "041",
                "closeout_notification_dispatch",
                exception,
                "The closeout notification could not be queued."
            );
        }

        var delivery = actor.CanDeliver
            ? await ProjectNotificationProcessingService.DeliverDispatchAsync(
                connection,
                dispatchId,
                actor.ActualUserId,
                "Module 041 closeout notification requested.",
                context,
                context.RequestAborted)
            : new ProjectNotificationProcessingService.NotificationDeliveryOutcome(
                false,
                "closeout_notification_queued",
                "module_065",
                readiness.RecipientBoundary,
                string.Empty,
                string.Empty,
                "The closeout notification was queued for governed Module 065 delivery.",
                dispatchId,
                0);

        var toCount = recipients.Count(recipient => recipient.RecipientType == "to");
        var ccCount = recipients.Count(recipient => recipient.RecipientType == "cc");
        return Results.Json(new
        {
            module = "041",
            status = delivery.Status,
            sent = delivery.Sent,
            message = delivery.Message,
            dispatchId,
            recipientCount = toCount,
            ccRecipientCount = ccCount,
            provider = delivery.Provider,
            recipientBoundary = delivery.RecipientBoundary,
            auditPath = $"/api/project-notifications/dispatches?dispatchId={dispatchId:D}",
            outboxPath = $"/api/project-notifications/dispatches?dispatchId={dispatchId:D}",
            serverDerivedRecipients = true,
            clientRecipientListIgnored = true
        }, statusCode: delivery.Sent
            ? StatusCodes.Status200OK
            : StatusCodes.Status202Accepted);
    }

    private static async Task<ProjectNotificationSnapshotResult> LoadSnapshotSafelyAsync(
        Npgsql.NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProjectNotificationFinancialSnapshotLoader.LoadAsync(
                connection,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return new(
                [],
                [
                    ProjectNotificationSourceState.Unavailable(
                        "projects",
                        "Authoritative projects and accountable owners",
                        true,
                        ProjectNotificationRepository.Diagnostic(exception),
                        "The project financial source is unavailable; notification evaluation was not fabricated.")
                ],
                DateTimeOffset.UtcNow);
        }
    }

    private static object AccessContract(ProjectNotificationActor actor) => new
    {
        actualUserId = actor.ActualUserId,
        effectiveUserId = actor.EffectiveUserId,
        actor.Email,
        actor.DisplayName,
        roles = actor.Roles.OrderBy(value => value),
        permissions = actor.Permissions.OrderBy(value => value),
        actor.IsViewAs,
        canView = actor.CanViewRouting,
        canManageRouting = actor.CanManageRouting,
        canManageSchedules = actor.CanManageSchedules,
        canDeliver = actor.CanDeliver,
        viewAsTransfersMutationAuthority = false
    };

    private static object RecipientDerivationContract() => new
    {
        authoritativeProjectFields = new[]
        {
            "projects.project_manager_user_id",
            "project_assignments.user_id",
            "projects.solution_architect_user_id",
            "projects.account_executive_user_id",
            "projects.project_coordinator_user_id"
        },
        recipientRoles = new[]
        {
            "Project Manager",
            "Assigned Engineer(s)",
            "Solution Architect",
            "Account Executive",
            "Project Team Coordinator",
            "Optional Escalation Manager"
        },
        clientSuppliedRecipientListAccepted = false,
        invalidOrDuplicateEmailsSuppressed = true,
        serverDerived = true
    };

    private static object SecurityContract() => new
    {
        actualSessionRequired = true,
        viewAsReadOnly = true,
        module065OwnsCredentials = true,
        retiredModule067ConfigurationRead = false,
        secretValuesAccepted = false,
        secretValuesReturned = false,
        providerErrorsSanitized = true,
        liveDeliveryRequiresProductionGovernedBoundary = true
    };
}

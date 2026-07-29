using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Group 4 authority for Modules 022, 023, 032, and notification portions of
/// Modules 018 and 041. Project recipients are derived from server-side project
/// ownership and assignments. All external delivery is delegated to Module 065.
/// </summary>
public static class ProjectNotificationAutomationModule
{
    private const string ContractVersion = "2026-07-28.1";
    private static readonly string[] BroadRoles =
    [
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "PROJECT_TEAM_COORDINATOR",
        "ACCOUNTING", "ACCOUNTING_BILLING", "BILLING", "FINANCE", "EXECUTIVE"
    ];
    private static readonly HashSet<string> AllowedMetricCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "hours_used_percent", "labor_budget_used_percent", "expenses_used_percent",
        "forecasted_total_cost", "approaching_budget", "over_budget",
        "missing_financial_information", "failed_project_data_refresh"
    };
    private static readonly HashSet<string> AllowedRecipientRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "project_manager", "assigned_engineers", "solution_architect",
        "account_executive", "project_team_coordinator", "escalation_manager"
    };

    public static IEndpointRouteBuilder MapProjectNotificationAutomationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/project-notifications/routing-rules",
            (Func<HttpContext, Task<IResult>>)GetRoutingRulesAsync);
        endpoints.MapPut(
            "/api/project-notifications/routing-rules/{ruleId:guid}",
            (Func<Guid, ProjectCostRoutingRuleUpdateRequest, HttpContext, Task<IResult>>)UpdateRoutingRuleAsync);
        endpoints.MapGet(
            "/api/project-notifications/schedules",
            (Func<HttpContext, Task<IResult>>)GetSchedulesAsync);
        endpoints.MapPut(
            "/api/project-notifications/schedules/{scheduleId:guid}",
            (Func<Guid, ProjectNotificationScheduleUpdateRequest, HttpContext, Task<IResult>>)UpdateScheduleAsync);
        endpoints.MapGet(
            "/api/project-notifications/module-065-readiness",
            (Func<HttpContext, Task<IResult>>)GetModule065ReadinessAsync);
        endpoints.MapPost(
            "/api/project-notifications/evaluate",
            (Func<ProjectNotificationEvaluationRequest, HttpContext, Task<IResult>>)EvaluateAsync);
        endpoints.MapGet(
            "/api/project-notifications/dispatches",
            (Func<HttpContext, Task<IResult>>)GetDispatchesAsync);
        endpoints.MapGet(
            "/api/project-notifications/delivery-monitor",
            (Func<HttpContext, Task<IResult>>)GetDeliveryMonitorAsync);
        endpoints.MapPost(
            "/api/project-notifications/dispatches/{dispatchId:guid}/release",
            (Func<Guid, ProjectNotificationReleaseRequest, HttpContext, Task<IResult>>)ReleaseDispatchAsync);
        endpoints.MapPost(
            "/api/project-notifications/dispatches/{dispatchId:guid}/retry",
            (Func<Guid, ProjectNotificationReleaseRequest, HttpContext, Task<IResult>>)RetryDispatchAsync);
        endpoints.MapPost(
            "/api/project-notifications/run-due",
            (Func<HttpContext, Task<IResult>>)RunDueAsync);
        endpoints.MapPost(
            "/api/project-notifications/closeout/queue",
            (Func<ProjectCloseoutNotificationRequest, HttpContext, Task<IResult>>)QueueCloseoutAsync);

        if (endpoints is WebApplication application)
            ProjectNotificationScheduler.Start(application);

        return endpoints;
    }

    /// <summary>
    /// Preserve the historical Module 041 route while replacing its client-provided
    /// recipient list and legacy SMTP/sendmail logic with the Group 4 contract.
    /// </summary>
    public static WebApplication UseProjectNotificationCloseoutCompatibility(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method)
                || !context.Request.Path.Equals(
                    "/api/project-closeout/email/send",
                    StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            ProjectCloseoutNotificationRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<ProjectCloseoutNotificationRequest>(
                    cancellationToken: context.RequestAborted);
            }
            catch
            {
                await Results.BadRequest(new
                {
                    module = "041",
                    status = "invalid_closeout_notification_request",
                    message = "A valid project closeout notification request is required."
                }).ExecuteAsync(context);
                return;
            }

            var result = await QueueCloseoutCoreAsync(request ?? new(null, null, null, null, null, null, null, null), context);
            await result.ExecuteAsync(context);
        });
        return app;
    }

    private static async Task<IResult> GetRoutingRulesAsync(HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var rules = await LoadRulesAsync(connection, context.RequestAborted);

        return Results.Ok(new
        {
            module = "022",
            moduleName = "Cost Alert Routing Rules",
            status = "project_cost_routing_rules_loaded",
            contractVersion = ContractVersion,
            access = AccessContract(access.Actor!),
            count = rules.Count,
            rules,
            supportedMetrics = AllowedMetricCodes.OrderBy(value => value),
            supportedRecipientRoles = AllowedRecipientRoles.OrderBy(value => value),
            recipientDerivation = RecipientDerivationContract(),
            module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
                context,
                context.RequestAborted),
            security = SecurityContract()
        });
    }

    private static async Task<IResult> UpdateRoutingRuleAsync(
        Guid ruleId,
        ProjectCostRoutingRuleUpdateRequest request,
        HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanManageRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;
        var existing = (await LoadRulesAsync(connection, context.RequestAborted))
            .FirstOrDefault(rule => rule.RuleId == ruleId);
        if (existing is null)
            return Results.NotFound(new { module = "022", status = "routing_rule_not_found" });

        var metric = NormalizeMetric(request.MetricCode, existing.MetricCode);
        var comparison = NormalizeComparison(request.ComparisonOperator, existing.ComparisonOperator);
        var unit = NormalizeUnit(request.ThresholdUnit, existing.ThresholdUnit);
        var severity = NormalizeSeverity(request.AlertSeverity, existing.AlertSeverity);
        var boundary = NormalizeBoundary(request.DeliveryBoundary, existing.DeliveryBoundary);
        var recipients = NormalizeRecipientRoles(request.RecipientRoles, existing.RecipientRoles);
        var validation = ValidateRule(
            metric,
            comparison,
            request.ThresholdValue ?? existing.ThresholdValue,
            unit,
            recipients,
            request.EscalationAfterMinutes ?? existing.EscalationAfterMinutes);
        if (validation is not null) return Results.BadRequest(new
        {
            module = "022",
            status = "invalid_routing_rule",
            message = validation
        });

        var replacement = existing with
        {
            RuleName = Clean(request.RuleName, 220, existing.RuleName),
            MetricCode = metric,
            ComparisonOperator = comparison,
            ThresholdValue = request.ThresholdValue ?? existing.ThresholdValue,
            ThresholdUnit = unit,
            AlertSeverity = severity,
            RecipientRoles = recipients,
            OptionalEscalationManagerUserId = request.OptionalEscalationManagerUserId,
            EscalationAfterMinutes = request.EscalationAfterMinutes ?? existing.EscalationAfterMinutes,
            DeliveryBoundary = boundary,
            Enabled = request.Enabled ?? existing.Enabled,
            Description = Clean(request.Description, 2000, existing.Description),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await using var command = new NpgsqlCommand("""
                UPDATE project_cost_alert_routing_rules
                SET rule_name=@rule_name,
                    metric_code=@metric_code,
                    comparison_operator=@comparison_operator,
                    threshold_value=@threshold_value,
                    threshold_unit=@threshold_unit,
                    alert_severity=@alert_severity,
                    recipient_roles=@recipient_roles,
                    optional_escalation_manager_user_id=@escalation_manager,
                    escalation_after_minutes=@escalation_after,
                    delivery_boundary=@delivery_boundary,
                    enabled=@enabled,
                    description=@description,
                    updated_by_user_id=@updated_by,
                    updated_at=NOW()
                WHERE project_cost_alert_routing_rule_id=@rule_id;
                """, connection, transaction);
            command.Parameters.AddWithValue("rule_name", replacement.RuleName);
            command.Parameters.AddWithValue("metric_code", replacement.MetricCode);
            command.Parameters.AddWithValue("comparison_operator", replacement.ComparisonOperator);
            command.Parameters.Add(new NpgsqlParameter("threshold_value", NpgsqlDbType.Numeric)
            {
                Value = replacement.ThresholdValue.HasValue
                    ? replacement.ThresholdValue.Value
                    : DBNull.Value
            });
            command.Parameters.AddWithValue("threshold_unit", replacement.ThresholdUnit);
            command.Parameters.AddWithValue("alert_severity", replacement.AlertSeverity);
            command.Parameters.Add(new NpgsqlParameter("recipient_roles", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = replacement.RecipientRoles
            });
            command.Parameters.Add(new NpgsqlParameter("escalation_manager", NpgsqlDbType.Uuid)
            {
                Value = replacement.OptionalEscalationManagerUserId.HasValue
                    ? replacement.OptionalEscalationManagerUserId.Value
                    : DBNull.Value
            });
            command.Parameters.Add(new NpgsqlParameter("escalation_after", NpgsqlDbType.Integer)
            {
                Value = replacement.EscalationAfterMinutes.HasValue
                    ? replacement.EscalationAfterMinutes.Value
                    : DBNull.Value
            });
            command.Parameters.AddWithValue("delivery_boundary", replacement.DeliveryBoundary);
            command.Parameters.AddWithValue("enabled", replacement.Enabled);
            command.Parameters.AddWithValue("description", replacement.Description);
            command.Parameters.AddWithValue("updated_by", actor.ActualUserId);
            command.Parameters.AddWithValue("rule_id", ruleId);
            await command.ExecuteNonQueryAsync(context.RequestAborted);

            await WriteAuditAsync(
                connection,
                transaction,
                "routing_rule",
                ruleId,
                "ROUTING_RULE_UPDATED",
                actor.ActualUserId,
                Clean(request.ChangeReason, 1000, "Updated from Module 022 Cost Alert Routing Rules."),
                existing,
                replacement,
                context.TraceIdentifier,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return SourceFailure("022", "routing_rule_write", exception,
                "The routing rule could not be saved. No partial configuration was committed.");
        }

        return Results.Ok(new
        {
            module = "022",
            status = "routing_rule_updated",
            rule = replacement,
            message = "The project cost routing rule was updated and recorded in immutable configuration history."
        });
    }

    private static async Task<IResult> GetSchedulesAsync(HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var schedules = await LoadSchedulesAsync(connection, context.RequestAborted);
        return Results.Ok(new
        {
            module = "023",
            moduleName = "Notification Scheduling",
            status = "project_notification_schedules_loaded",
            contractVersion = ContractVersion,
            access = AccessContract(access.Actor!),
            count = schedules.Count,
            schedules,
            allowedTimezones = new[]
            {
                "America/Chicago", "America/New_York", "America/Denver",
                "America/Los_Angeles", "UTC"
            },
            deliveryBoundary = new[] { "test_only", "production_governed", "locked" },
            module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
                context,
                context.RequestAborted),
            security = SecurityContract()
        });
    }

    private static async Task<IResult> UpdateScheduleAsync(
        Guid scheduleId,
        ProjectNotificationScheduleUpdateRequest request,
        HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanManageSchedules);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;
        var existing = (await LoadSchedulesAsync(connection, context.RequestAborted))
            .FirstOrDefault(schedule => schedule.ScheduleId == scheduleId);
        if (existing is null)
            return Results.NotFound(new { module = "023", status = "notification_schedule_not_found" });

        var scheduleType = NormalizeScheduleType(request.ScheduleType, existing.ScheduleType);
        var timezone = NormalizeTimezone(request.TimezoneName, existing.TimezoneName);
        if (!TimeOnly.TryParse(request.LocalTime, out var localTime)) localTime = existing.LocalTime;
        TimeOnly? quietStart = ParseTimeOrExisting(request.QuietHoursStart, existing.QuietHoursStart);
        TimeOnly? quietEnd = ParseTimeOrExisting(request.QuietHoursEnd, existing.QuietHoursEnd);
        var dayOfWeek = request.DayOfWeek ?? existing.DayOfWeek;
        var daysBeforeMonthEnd = request.DaysBeforeMonthEnd ?? existing.DaysBeforeMonthEnd;
        var escalationAfter = request.EscalationAfterMinutes ?? existing.EscalationAfterMinutes;
        var scheduleValidation = ValidateSchedule(
            scheduleType,
            dayOfWeek,
            daysBeforeMonthEnd,
            escalationAfter,
            quietStart,
            quietEnd);
        if (scheduleValidation is not null) return Results.BadRequest(new
        {
            module = "023",
            status = "invalid_notification_schedule",
            message = scheduleValidation
        });

        var replacement = existing with
        {
            ScheduleName = Clean(request.ScheduleName, 220, existing.ScheduleName),
            ScheduleType = scheduleType,
            DayOfWeek = dayOfWeek,
            LocalTime = localTime,
            TimezoneName = timezone,
            DaysBeforeMonthEnd = daysBeforeMonthEnd,
            EscalationAfterMinutes = escalationAfter,
            QuietHoursStart = quietStart,
            QuietHoursEnd = quietEnd,
            Enabled = request.Enabled ?? existing.Enabled,
            DeliveryBoundary = NormalizeBoundary(request.DeliveryBoundary, existing.DeliveryBoundary),
            NextRunAt = ProjectNotificationScheduler.CalculateNextRun(
                scheduleType,
                dayOfWeek,
                localTime,
                timezone,
                daysBeforeMonthEnd,
                DateTimeOffset.UtcNow),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await using var command = new NpgsqlCommand("""
                UPDATE project_notification_schedules
                SET schedule_name=@schedule_name,
                    schedule_type=@schedule_type,
                    day_of_week=@day_of_week,
                    local_time=@local_time,
                    timezone_name=@timezone_name,
                    days_before_month_end=@days_before_month_end,
                    escalation_after_minutes=@escalation_after,
                    quiet_hours_start=@quiet_start,
                    quiet_hours_end=@quiet_end,
                    enabled=@enabled,
                    delivery_boundary=@delivery_boundary,
                    next_run_at=@next_run_at,
                    updated_by_user_id=@updated_by,
                    updated_at=NOW()
                WHERE project_notification_schedule_id=@schedule_id;
                """, connection, transaction);
            command.Parameters.AddWithValue("schedule_name", replacement.ScheduleName);
            command.Parameters.AddWithValue("schedule_type", replacement.ScheduleType);
            AddNullable(command, "day_of_week", NpgsqlDbType.Smallint,
                replacement.DayOfWeek.HasValue ? (short)replacement.DayOfWeek.Value : null);
            command.Parameters.AddWithValue("local_time", replacement.LocalTime);
            command.Parameters.AddWithValue("timezone_name", replacement.TimezoneName);
            AddNullable(command, "days_before_month_end", NpgsqlDbType.Integer, replacement.DaysBeforeMonthEnd);
            AddNullable(command, "escalation_after", NpgsqlDbType.Integer, replacement.EscalationAfterMinutes);
            AddNullable(command, "quiet_start", NpgsqlDbType.Time, replacement.QuietHoursStart);
            AddNullable(command, "quiet_end", NpgsqlDbType.Time, replacement.QuietHoursEnd);
            command.Parameters.AddWithValue("enabled", replacement.Enabled);
            command.Parameters.AddWithValue("delivery_boundary", replacement.DeliveryBoundary);
            AddNullable(command, "next_run_at", NpgsqlDbType.TimestampTz, replacement.NextRunAt);
            command.Parameters.AddWithValue("updated_by", actor.ActualUserId);
            command.Parameters.AddWithValue("schedule_id", scheduleId);
            await command.ExecuteNonQueryAsync(context.RequestAborted);

            await WriteAuditAsync(
                connection,
                transaction,
                "schedule",
                scheduleId,
                "NOTIFICATION_SCHEDULE_UPDATED",
                actor.ActualUserId,
                Clean(request.ChangeReason, 1000, "Updated from Module 023 Notification Scheduling."),
                existing,
                replacement,
                context.TraceIdentifier,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return SourceFailure("023", "notification_schedule_write", exception,
                "The notification schedule could not be saved. No partial configuration was committed.");
        }

        return Results.Ok(new
        {
            module = "023",
            status = "notification_schedule_updated",
            schedule = replacement,
            message = "The notification schedule was updated and recorded in immutable configuration history."
        });
    }

    private static async Task<IResult> GetModule065ReadinessAsync(HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanViewRouting);
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

    private static async Task<IResult> EvaluateAsync(
        ProjectNotificationEvaluationRequest request,
        HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanManageRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var evaluation = await EvaluateAndQueueAsync(
            connection,
            access.Actor!,
            request.ProjectId,
            request.ReleaseEligible,
            Clean(request.EvaluationReason, 1000, "Manual cost-alert evaluation."),
            context,
            context.RequestAborted);
        return evaluation.Failure ?? Results.Ok(evaluation.Payload!);
    }

    private static async Task<IResult> GetDispatchesAsync(HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var limit = Math.Clamp(
            int.TryParse(context.Request.Query["limit"], out var requested) ? requested : 100,
            1,
            250);
        var status = Clean(context.Request.Query["status"], 40, "");
        var dispatches = await LoadDispatchesAsync(
            connection,
            access.Actor!,
            status,
            limit,
            context.RequestAborted);
        return Results.Ok(new
        {
            module = "032",
            moduleName = "Notification Delivery Monitor",
            status = "project_notification_dispatches_loaded",
            contractVersion = ContractVersion,
            access = AccessContract(access.Actor!),
            count = dispatches.Count,
            dispatches,
            module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
                context,
                context.RequestAborted),
            security = SecurityContract()
        });
    }

    private static async Task<IResult> GetDeliveryMonitorAsync(HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanViewRouting);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var dispatches = await LoadDispatchesAsync(
            connection,
            access.Actor!,
            "",
            100,
            context.RequestAborted);
        var schedules = await LoadSchedulesAsync(connection, context.RequestAborted);
        var rules = await LoadRulesAsync(connection, context.RequestAborted);
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            context.RequestAborted);
        var sourceSnapshot = await LoadSnapshotSafelyAsync(connection, context.RequestAborted);

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
                activeRules = rules.Count(item => item.Enabled),
                activeSchedules = schedules.Count(item => item.Enabled),
                sourceFailures = sourceSnapshot.Sources.Count(item => item.Status == "unavailable")
            },
            module065 = readiness,
            sources = sourceSnapshot.Sources,
            schedules = schedules.OrderBy(item => item.NextRunAt),
            dispatches,
            productivityPurpose = "A single operational inbox for notification dispatches, automatically derived recipients, provider readiness, failures, retries, and audit evidence.",
            security = SecurityContract()
        });
    }

    private static async Task<IResult> ReleaseDispatchAsync(
        Guid dispatchId,
        ProjectNotificationReleaseRequest request,
        HttpContext context) => await ReleaseOrRetryAsync(
            dispatchId,
            request,
            context,
            retry: false);

    private static async Task<IResult> RetryDispatchAsync(
        Guid dispatchId,
        ProjectNotificationReleaseRequest request,
        HttpContext context) => await ReleaseOrRetryAsync(
            dispatchId,
            request,
            context,
            retry: true);

    private static async Task<IResult> ReleaseOrRetryAsync(
        Guid dispatchId,
        ProjectNotificationReleaseRequest request,
        HttpContext context,
        bool retry)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanDeliver);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var result = await DeliverDispatchAsync(
            connection,
            dispatchId,
            access.Actor!.ActualUserId,
            Clean(request.Reason, 1000, retry
                ? "Manual retry from Module 032."
                : "Manual release from Module 032."),
            context,
            context.RequestAborted);
        return result.Failure ?? Results.Json(
            result.Payload!,
            statusCode: result.Sent ? StatusCodes.Status200OK : StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> RunDueAsync(HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => actor.CanManageSchedules);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var summary = await RunDueSchedulesAsync(
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

    private static async Task<IResult> QueueCloseoutAsync(
        ProjectCloseoutNotificationRequest request,
        HttpContext context) => await QueueCloseoutCoreAsync(request, context);

    private static async Task<IResult> QueueCloseoutCoreAsync(
        ProjectCloseoutNotificationRequest request,
        HttpContext context)
    {
        var access = await OpenAuthorizedAsync(context, actor => CanQueueCloseout(actor));
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;

        var snapshot = await LoadSnapshotSafelyAsync(connection, context.RequestAborted);
        var project = request.ProjectId.HasValue
            ? snapshot.Projects.FirstOrDefault(item => item.ProjectId == request.ProjectId.Value)
            : snapshot.Projects.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(request.ProjectCode)
                && item.ProjectCode.Equals(request.ProjectCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            return Results.NotFound(new
            {
                module = "041",
                status = "project_not_found_or_outside_scope",
                message = "The project could not be resolved from authoritative ProjectPulse data."
            });
        }
        if (!CanAccessProject(actor, project))
        {
            return Results.Json(new
            {
                module = "041",
                status = "closeout_project_access_denied",
                message = "The selected project is outside the current user's closeout-notification scope."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var subject = Clean(
            request.Subject,
            500,
            $"Project closeout: {project.ProjectCode} — {project.ProjectName}");
        var body = Clean(
            request.Body,
            20000,
            BuildCloseoutBody(project));
        var recipients = DeriveRecipients(
            project,
            ["project_manager", "assigned_engineers", "solution_architect", "account_executive", "project_team_coordinator"],
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

        var boundary = "test_only";
        var module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            context.RequestAborted);
        boundary = module065.RecipientBoundary;
        var eventKey = $"closeout:{project.ProjectId:D}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var dispatchId = await UpsertDispatchAsync(
            connection,
            null,
            null,
            project,
            eventKey,
            "project_closeout",
            "informational",
            "041",
            "closeout_requested",
            subject,
            body,
            Html(body),
            boundary,
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

        var delivery = actor.CanDeliver
            ? await DeliverDispatchAsync(
                connection,
                dispatchId,
                actor.ActualUserId,
                "Module 041 closeout notification requested.",
                context,
                context.RequestAborted)
            : DeliveryOutcome.Pending(new
            {
                module = "041",
                status = "closeout_notification_queued",
                sent = false,
                message = "The closeout notification was queued for governed Module 065 delivery."
            });

        if (delivery.Failure is not null) return delivery.Failure;
        var payload = delivery.Payload!;
        return Results.Json(new
        {
            module = "041",
            status = payload.status,
            sent = delivery.Sent,
            message = payload.message,
            dispatchId,
            recipientCount = recipients.Count(item => item.RecipientType == "to"),
            ccRecipientCount = recipients.Count(item => item.RecipientType == "cc"),
            provider = payload.provider,
            recipientBoundary = payload.recipientBoundary,
            auditPath = $"/api/project-notifications/dispatches?dispatchId={dispatchId:D}",
            outboxPath = $"/api/project-notifications/dispatches?dispatchId={dispatchId:D}",
            serverDerivedRecipients = true,
            clientRecipientListIgnored = true
        }, statusCode: delivery.Sent ? StatusCodes.Status200OK : StatusCodes.Status202Accepted);
    }

    internal static async Task<ScheduleRunSummary> RunDueSchedulesAsync(
        NpgsqlConnection connection,
        Guid? actorUserId,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var schedules = (await LoadSchedulesAsync(connection, cancellationToken))
            .Where(schedule => schedule.Enabled
                && (!schedule.NextRunAt.HasValue || schedule.NextRunAt.Value <= now))
            .OrderBy(schedule => schedule.NextRunAt)
            .ToArray();
        var evaluated = 0;
        var queued = 0;
        var delivered = 0;
        var failures = 0;

        foreach (var schedule in schedules)
        {
            try
            {
                await UpdateScheduleRunStateAsync(
                    connection,
                    schedule.ScheduleId,
                    started: true,
                    "running",
                    null,
                    cancellationToken);

                if (schedule.ScheduleType == "cost_alert_evaluation")
                {
                    var systemActor = actorUserId.HasValue
                        ? await LoadActorAsync(
                            connection,
                            actorUserId.Value,
                            actorUserId.Value,
                            false,
                            cancellationToken)
                        : ProjectNotificationActorForScheduler();
                    var outcome = await EvaluateAndQueueAsync(
                        connection,
                        systemActor,
                        null,
                        schedule.DeliveryBoundary == "production_governed",
                        $"Scheduled evaluation {schedule.ScheduleCode}.",
                        context,
                        cancellationToken);
                    evaluated += outcome.EvaluatedProjects;
                    queued += outcome.DispatchesQueued;
                    delivered += outcome.Delivered;
                    failures += outcome.Failures;
                }
                else
                {
                    var reminder = await QueueScheduledReminderAsync(
                        connection,
                        schedule,
                        cancellationToken);
                    queued += reminder.Queued;
                    failures += reminder.Failures;
                }

                var nextRun = ProjectNotificationScheduler.CalculateNextRun(
                    schedule.ScheduleType,
                    schedule.DayOfWeek,
                    schedule.LocalTime,
                    schedule.TimezoneName,
                    schedule.DaysBeforeMonthEnd,
                    now.AddMinutes(1));
                await UpdateScheduleRunStateAsync(
                    connection,
                    schedule.ScheduleId,
                    started: false,
                    "completed",
                    nextRun,
                    cancellationToken);
            }
            catch
            {
                failures++;
                await UpdateScheduleRunStateAsync(
                    connection,
                    schedule.ScheduleId,
                    started: false,
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

        var dueDispatches = await LoadDueDispatchIdsAsync(connection, cancellationToken);
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
            else if (outcome.Failure is not null) failures++;
        }

        return new(
            schedules.Length,
            evaluated,
            queued,
            delivered,
            failures,
            dueDispatches.Count,
            DateTimeOffset.UtcNow);
    }

    private static async Task<EvaluationOutcome> EvaluateAndQueueAsync(
        NpgsqlConnection connection,
        ProjectNotificationActor actor,
        Guid? projectId,
        bool releaseEligible,
        string reason,
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
            return EvaluationOutcome.Fail(SourceFailure(
                "022",
                "project_financial_snapshot",
                exception,
                "Project cost rules could not be evaluated because the authoritative project source is unavailable."));
        }

        var rules = (await LoadRulesAsync(connection, cancellationToken))
            .Where(rule => rule.Enabled)
            .ToArray();
        var projects = snapshot.Projects
            .Where(project => !projectId.HasValue || project.ProjectId == projectId.Value)
            .Where(project => CanAccessProject(actor, project))
            .ToArray();
        if (projectId.HasValue && projects.Length == 0)
        {
            return EvaluationOutcome.Fail(Results.NotFound(new
            {
                module = "022",
                status = "project_not_found_or_outside_scope"
            }));
        }

        var module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);
        var dispatches = new List<Guid>();
        var triggered = 0;
        var delivered = 0;
        var failures = 0;

        foreach (var project in projects)
        {
            foreach (var rule in rules.Where(rule => rule.MetricCode != "failed_project_data_refresh"))
            {
                var evaluation = EvaluateRule(rule, project);
                if (!evaluation.Triggered) continue;
                triggered++;
                var eventKey = $"cost:{rule.RuleCode}:{project.ProjectId:D}:{DateTimeOffset.UtcNow:yyyyMMdd}";
                var recipients = await DeriveRecipientsWithEscalationAsync(
                    connection,
                    project,
                    rule,
                    cancellationToken);
                var subject = BuildCostAlertSubject(rule, project);
                var body = BuildCostAlertBody(rule, project, evaluation);
                var status = recipients.Length == 0 ? "suppressed" : "held";
                var dispatchId = await UpsertDispatchAsync(
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
                    body,
                    Html(body),
                    MoreRestrictiveBoundary(rule.DeliveryBoundary, module065.RecipientBoundary),
                    status,
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
                        evaluationReason = reason,
                        sourceStates = snapshot.Sources
                    },
                    cancellationToken);
                dispatches.Add(dispatchId);

                if (releaseEligible && actor.CanDeliver && recipients.Length > 0)
                {
                    var delivery = await DeliverDispatchAsync(
                        connection,
                        dispatchId,
                        actor.ActualUserId,
                        reason,
                        context,
                        cancellationToken);
                    if (delivery.Sent) delivered++;
                    else if (delivery.Failure is not null) failures++;
                }
            }
        }

        var unavailable = snapshot.Sources.Where(source => source.Status == "unavailable").ToArray();
        var refreshRule = rules.FirstOrDefault(rule => rule.MetricCode == "failed_project_data_refresh");
        if (refreshRule is not null && unavailable.Length > 0)
        {
            var recipients = await LoadGlobalRecipientsAsync(
                connection,
                refreshRule,
                cancellationToken);
            var eventKey = $"source:{refreshRule.RuleCode}:{DateTimeOffset.UtcNow:yyyyMMddHH}";
            var body = "Project financial refresh requires attention.\n\n"
                + string.Join("\n", unavailable.Select(source =>
                    $"- {source.Name}: {source.Message} ({source.DiagnosticCode})"));
            var dispatchId = await UpsertDispatchAsync(
                connection,
                refreshRule.RuleId,
                null,
                null,
                eventKey,
                refreshRule.MetricCode,
                refreshRule.AlertSeverity,
                "022",
                "financial_data_source_unavailable",
                "ProjectPulse project financial refresh requires attention",
                body,
                Html(body),
                MoreRestrictiveBoundary(refreshRule.DeliveryBoundary, module065.RecipientBoundary),
                recipients.Length == 0 ? "suppressed" : "held",
                recipients,
                new { unavailableSources = unavailable, evaluationReason = reason },
                cancellationToken);
            dispatches.Add(dispatchId);
            triggered++;
        }

        return EvaluationOutcome.Success(
            new
            {
                module = "022",
                status = "project_cost_routing_evaluated",
                evaluatedAt = DateTimeOffset.UtcNow,
                evaluatedProjectCount = projects.Length,
                activeRuleCount = rules.Length,
                triggeredRuleCount = triggered,
                dispatchesQueued = dispatches.Count,
                dispatchIds = dispatches,
                delivered,
                failures,
                releaseRequested = releaseEligible,
                module065,
                sources = snapshot.Sources,
                message = "Project cost routing rules were evaluated against authoritative project financial data."
            },
            projects.Length,
            dispatches.Count,
            delivered,
            failures);
    }

    private static ProjectNotificationMetricEvaluation EvaluateRule(
        ProjectCostRoutingRule rule,
        ProjectNotificationFinancialSnapshot project)
    {
        decimal? observed = null;
        decimal? comparison = rule.ThresholdValue;
        var unit = rule.ThresholdUnit;
        var triggered = false;
        var reason = string.Empty;

        switch (rule.MetricCode)
        {
            case "hours_used_percent":
                observed = project.PlannedHours > 0
                    ? Math.Round(project.UsedHours / project.PlannedHours * 100m, 2)
                    : null;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? $"Used hours are {observed.Value:0.##}% of planned hours."
                    : "Planned hours are not recorded.";
                break;
            case "labor_budget_used_percent":
                observed = project.LaborBudget is > 0 && project.LaborCost.HasValue
                    ? Math.Round(project.LaborCost.Value / project.LaborBudget.Value * 100m, 2)
                    : null;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? $"Calculated labor cost is {observed.Value:0.##}% of the known labor budget."
                    : "Labor budget or governed rate evidence is missing.";
                break;
            case "expenses_used_percent":
                observed = project.ExpenseBudget is > 0 && project.UploadedExpenses.HasValue
                    ? Math.Round(project.UploadedExpenses.Value / project.ExpenseBudget.Value * 100m, 2)
                    : null;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? $"Current Module 005 expenses are {observed.Value:0.##}% of the known expense budget."
                    : "Expense budget or current expense evidence is missing.";
                break;
            case "forecasted_total_cost":
                var totalBudget = SumKnown(project.LaborBudget, project.ExpenseBudget);
                observed = rule.ThresholdUnit == "percent" && totalBudget is > 0
                    && project.ForecastedFinalCost.HasValue
                    ? Math.Round(project.ForecastedFinalCost.Value / totalBudget.Value * 100m, 2)
                    : project.ForecastedFinalCost;
                triggered = Compare(observed, comparison, rule.ComparisonOperator);
                reason = observed.HasValue
                    ? rule.ThresholdUnit == "percent"
                        ? $"Forecasted final cost is {observed.Value:0.##}% of the known project budget."
                        : $"Forecasted final cost is {observed.Value:C}."
                    : "Forecasted final cost is incomplete.";
                break;
            case "approaching_budget":
                triggered = project.BudgetStatus == "approaching_budget";
                reason = triggered
                    ? "The authoritative project financial status is approaching budget."
                    : "The project is not currently approaching budget.";
                break;
            case "over_budget":
                triggered = project.BudgetStatus == "over_budget";
                reason = triggered
                    ? "The authoritative project financial status is over budget."
                    : "The project is not currently over budget.";
                break;
            case "missing_financial_information":
                observed = project.MissingFinancialInformation.Length;
                comparison = 0m;
                triggered = project.MissingFinancialInformation.Length > 0;
                unit = "count";
                reason = triggered
                    ? $"Missing project financial information: {string.Join(", ", project.MissingFinancialInformation)}."
                    : "Required project financial information is recorded.";
                break;
        }

        return new(triggered, observed, comparison, unit, reason);
    }

    private static bool Compare(decimal? observed, decimal? threshold, string comparison)
    {
        if (!observed.HasValue || !threshold.HasValue) return false;
        return comparison switch
        {
            "gt" => observed.Value > threshold.Value,
            "gte" => observed.Value >= threshold.Value,
            "lt" => observed.Value < threshold.Value,
            "lte" => observed.Value <= threshold.Value,
            "eq" => observed.Value == threshold.Value,
            _ => false
        };
    }

    private static async Task<ProjectNotificationUser[]> DeriveRecipientsWithEscalationAsync(
        NpgsqlConnection connection,
        ProjectNotificationFinancialSnapshot project,
        ProjectCostRoutingRule rule,
        CancellationToken cancellationToken)
    {
        ProjectNotificationUser? escalation = null;
        if (rule.OptionalEscalationManagerUserId.HasValue)
        {
            escalation = await LoadUserAsync(
                connection,
                rule.OptionalEscalationManagerUserId.Value,
                "escalation_manager",
                "routing_rule.optional_escalation_manager_user_id",
                cancellationToken);
        }
        return DeriveRecipients(project, rule.RecipientRoles, escalation);
    }

    private static ProjectNotificationUser[] DeriveRecipients(
        ProjectNotificationFinancialSnapshot project,
        IEnumerable<string> roles,
        ProjectNotificationUser? escalation)
    {
        var requested = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recipients = new List<ProjectNotificationUser>();
        if (requested.Contains("project_manager") && project.ProjectManager is not null)
            recipients.Add(project.ProjectManager with { RecipientType = "to" });
        if (requested.Contains("assigned_engineers"))
            recipients.AddRange(project.Engineers.Select(engineer => new ProjectNotificationUser(
                engineer.UserId,
                engineer.DisplayName,
                engineer.Email,
                "assigned_engineer",
                "project_assignments.user_id",
                "to")));
        if (requested.Contains("solution_architect") && project.SolutionArchitect is not null)
            recipients.Add(project.SolutionArchitect with { RecipientType = "cc" });
        if (requested.Contains("account_executive") && project.AccountExecutive is not null)
            recipients.Add(project.AccountExecutive with { RecipientType = "cc" });
        if (requested.Contains("project_team_coordinator") && project.ProjectTeamCoordinator is not null)
            recipients.Add(project.ProjectTeamCoordinator with { RecipientType = "cc" });
        if (requested.Contains("escalation_manager") && escalation is not null)
            recipients.Add(escalation with { RecipientType = "cc" });

        return recipients
            .Where(recipient => IsEmail(recipient.Email))
            .GroupBy(recipient => recipient.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.RecipientType == "to" ? 0 : 1).First())
            .ToArray();
    }

    private static async Task<ProjectNotificationUser[]> LoadGlobalRecipientsAsync(
        NpgsqlConnection connection,
        ProjectCostRoutingRule rule,
        CancellationToken cancellationToken)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rule.RecipientRoles.Contains("project_team_coordinator", StringComparer.OrdinalIgnoreCase))
            roles.Add("PROJECT_TEAM_COORDINATOR");
        if (roles.Count == 0) roles.Add("PROJECT_TEAM_COORDINATOR");
        var recipients = new List<ProjectNotificationUser>();
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT
                app_user.user_id,
                COALESCE(app_user.display_name, app_user.email, ''),
                COALESCE(app_user.email, ''),
                upper(role.role_code)
            FROM app_user_role_assignments assignment
            JOIN app_roles role
              ON role.app_role_id=assignment.app_role_id
             AND role.is_active=TRUE
            JOIN app_users app_user
              ON app_user.user_id=assignment.user_id
             AND app_user.is_active=TRUE
            WHERE assignment.is_active=TRUE
              AND upper(role.role_code)=ANY(@role_codes)
            ORDER BY 2;
            """, connection);
        command.Parameters.AddWithValue("role_codes", roles.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recipients.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                "project_team_coordinator",
                $"app_user_role_assignments:{reader.GetString(3)}",
                "to"));
        }
        if (rule.OptionalEscalationManagerUserId.HasValue)
        {
            var escalation = await LoadUserAsync(
                connection,
                rule.OptionalEscalationManagerUserId.Value,
                "escalation_manager",
                "routing_rule.optional_escalation_manager_user_id",
                cancellationToken);
            if (escalation is not null) recipients.Add(escalation with { RecipientType = "cc" });
        }
        return recipients.Where(item => IsEmail(item.Email))
            .GroupBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
    }

    private static async Task<ProjectNotificationUser?> LoadUserAsync(
        NpgsqlConnection connection,
        Guid userId,
        string role,
        string source,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT user_id, COALESCE(display_name,email,''), COALESCE(email,'')
            FROM app_users
            WHERE user_id=@user_id AND is_active=TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), role, source, "cc")
            : null;
    }

    private static async Task<Guid> UpsertDispatchAsync(
        NpgsqlConnection connection,
        Guid? ruleId,
        Guid? scheduleId,
        ProjectNotificationFinancialSnapshot? project,
        string eventKey,
        string notificationType,
        string severity,
        string sourceModule,
        string sourceStatus,
        string subject,
        string textBody,
        string htmlBody,
        string deliveryBoundary,
        string deliveryStatus,
        ProjectNotificationUser[] recipients,
        object metadata,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            Guid dispatchId;
            await using (var command = new NpgsqlCommand("""
                INSERT INTO project_notification_dispatches (
                    project_id, routing_rule_id, schedule_id, event_key,
                    notification_type, alert_severity, source_module, source_status,
                    subject, text_body, html_body, delivery_boundary,
                    provider_source, delivery_status, scheduled_for, metadata_json
                )
                VALUES (
                    @project_id, @rule_id, @schedule_id, @event_key,
                    @notification_type, @severity, @source_module, @source_status,
                    @subject, @text_body, @html_body, @delivery_boundary,
                    'module_065', @delivery_status, NOW(), @metadata::jsonb
                )
                ON CONFLICT (event_key) DO UPDATE
                SET alert_severity=EXCLUDED.alert_severity,
                    source_status=EXCLUDED.source_status,
                    subject=EXCLUDED.subject,
                    text_body=EXCLUDED.text_body,
                    html_body=EXCLUDED.html_body,
                    delivery_boundary=EXCLUDED.delivery_boundary,
                    delivery_status=CASE
                        WHEN project_notification_dispatches.delivery_status='sent'
                            THEN project_notification_dispatches.delivery_status
                        ELSE EXCLUDED.delivery_status
                    END,
                    metadata_json=EXCLUDED.metadata_json,
                    updated_at=NOW()
                RETURNING project_notification_dispatch_id;
                """, connection, transaction))
            {
                AddNullable(command, "project_id", NpgsqlDbType.Uuid, project?.ProjectId);
                AddNullable(command, "rule_id", NpgsqlDbType.Uuid, ruleId);
                AddNullable(command, "schedule_id", NpgsqlDbType.Uuid, scheduleId);
                command.Parameters.AddWithValue("event_key", Limit(eventKey, 260));
                command.Parameters.AddWithValue("notification_type", Limit(notificationType, 120));
                command.Parameters.AddWithValue("severity", NormalizeSeverity(severity, "warning"));
                command.Parameters.AddWithValue("source_module", Limit(sourceModule, 20));
                command.Parameters.AddWithValue("source_status", Limit(sourceStatus, 80));
                command.Parameters.AddWithValue("subject", Limit(subject, 500));
                command.Parameters.AddWithValue("text_body", Limit(textBody, 30000));
                command.Parameters.AddWithValue("html_body", Limit(htmlBody, 60000));
                command.Parameters.AddWithValue("delivery_boundary", NormalizeBoundary(deliveryBoundary, "locked"));
                command.Parameters.AddWithValue("delivery_status", deliveryStatus);
                command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
                dispatchId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Dispatch ID was not returned."));
            }

            await using (var clear = new NpgsqlCommand("""
                DELETE FROM project_notification_dispatch_recipients
                WHERE project_notification_dispatch_id=@dispatch_id
                  AND NOT EXISTS (
                    SELECT 1 FROM project_notification_dispatches dispatch
                    WHERE dispatch.project_notification_dispatch_id=@dispatch_id
                      AND dispatch.delivery_status='sent'
                  );
                """, connection, transaction))
            {
                clear.Parameters.AddWithValue("dispatch_id", dispatchId);
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var recipient in recipients)
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO project_notification_dispatch_recipients (
                        project_notification_dispatch_id, recipient_role,
                        recipient_user_id, recipient_name, recipient_email,
                        recipient_type, derivation_source, delivery_status
                    )
                    VALUES (
                        @dispatch_id, @role, @user_id, @name, @email,
                        @type, @source, 'pending'
                    )
                    ON CONFLICT (project_notification_dispatch_id, lower(recipient_email), recipient_type)
                    DO UPDATE SET
                        recipient_role=EXCLUDED.recipient_role,
                        recipient_user_id=EXCLUDED.recipient_user_id,
                        recipient_name=EXCLUDED.recipient_name,
                        derivation_source=EXCLUDED.derivation_source;
                    """, connection, transaction);
                command.Parameters.AddWithValue("dispatch_id", dispatchId);
                command.Parameters.AddWithValue("role", Limit(recipient.Role, 100));
                AddNullable(command, "user_id", NpgsqlDbType.Uuid, recipient.UserId);
                command.Parameters.AddWithValue("name", Limit(recipient.DisplayName, 320));
                command.Parameters.AddWithValue("email", recipient.Email.Trim().ToLowerInvariant());
                command.Parameters.AddWithValue("type", RecipientType(recipient.RecipientType));
                command.Parameters.AddWithValue("source", Limit(recipient.DerivationSource, 120));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return dispatchId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<DeliveryOutcome> DeliverDispatchAsync(
        NpgsqlConnection connection,
        Guid dispatchId,
        Guid? releasedBy,
        string reason,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        var dispatch = await LoadDispatchAsync(connection, dispatchId, cancellationToken);
        if (dispatch is null)
            return DeliveryOutcome.Fail(Results.NotFound(new
            {
                module = "032",
                status = "notification_dispatch_not_found"
            }));
        if (dispatch.DeliveryStatus == "sent")
            return DeliveryOutcome.Pending(new
            {
                module = "032",
                status = "notification_already_sent",
                sent = true,
                provider = dispatch.ProviderSource,
                recipientBoundary = dispatch.DeliveryBoundary,
                message = "The notification was already delivered. Duplicate delivery was prevented."
            }, sent: true);

        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);
        var effectiveBoundary = MoreRestrictiveBoundary(
            dispatch.DeliveryBoundary,
            readiness.RecipientBoundary);
        var result = effectiveBoundary == "production_governed"
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

        var attemptNumber = dispatch.AttemptCount + 1;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var attempt = new NpgsqlCommand("""
                INSERT INTO project_notification_delivery_attempts (
                    project_notification_dispatch_id, attempt_number,
                    provider_source, configured_provider, recipient_boundary,
                    attempt_status, provider_message_id,
                    diagnostic_code, diagnostic_message, attempted_at
                )
                VALUES (
                    @dispatch_id, @attempt_number, 'module_065', @provider,
                    @boundary, @status, @message_id, @diagnostic_code,
                    @diagnostic_message, NOW()
                );
                """, connection, transaction))
            {
                attempt.Parameters.AddWithValue("dispatch_id", dispatchId);
                attempt.Parameters.AddWithValue("attempt_number", attemptNumber);
                attempt.Parameters.AddWithValue("provider", result.Provider);
                attempt.Parameters.AddWithValue("boundary", result.RecipientBoundary);
                attempt.Parameters.AddWithValue("status", result.Status);
                attempt.Parameters.AddWithValue("message_id", result.ProviderMessageId ?? string.Empty);
                attempt.Parameters.AddWithValue("diagnostic_code", result.DiagnosticCode ?? string.Empty);
                attempt.Parameters.AddWithValue("diagnostic_message", Limit(result.Message, 2000));
                await attempt.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var update = new NpgsqlCommand("""
                UPDATE project_notification_dispatches
                SET delivery_status=@status,
                    delivery_boundary=@boundary,
                    released_at=COALESCE(released_at,NOW()),
                    released_by_user_id=COALESCE(@released_by,released_by_user_id),
                    sent_at=CASE WHEN @sent THEN NOW() ELSE sent_at END,
                    provider_message_id=@message_id,
                    last_error_code=CASE WHEN @sent THEN '' ELSE @diagnostic_code END,
                    last_error_message=CASE WHEN @sent THEN '' ELSE @diagnostic_message END,
                    updated_at=NOW()
                WHERE project_notification_dispatch_id=@dispatch_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("status", result.Status);
                update.Parameters.AddWithValue("boundary", result.RecipientBoundary);
                AddNullable(update, "released_by", NpgsqlDbType.Uuid, releasedBy);
                update.Parameters.AddWithValue("sent", result.Sent);
                update.Parameters.AddWithValue("message_id", result.ProviderMessageId ?? string.Empty);
                update.Parameters.AddWithValue("diagnostic_code", result.DiagnosticCode ?? string.Empty);
                update.Parameters.AddWithValue("diagnostic_message", Limit(result.Message, 2000));
                update.Parameters.AddWithValue("dispatch_id", dispatchId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var recipients = new NpgsqlCommand("""
                UPDATE project_notification_dispatch_recipients
                SET delivery_status=@recipient_status
                WHERE project_notification_dispatch_id=@dispatch_id;
                """, connection, transaction))
            {
                recipients.Parameters.AddWithValue("recipient_status", result.Sent ? "sent" : result.Status == "failed" ? "failed" : "suppressed");
                recipients.Parameters.AddWithValue("dispatch_id", dispatchId);
                await recipients.ExecuteNonQueryAsync(cancellationToken);
            }

            await WriteAuditAsync(
                connection,
                transaction,
                "dispatch",
                dispatchId,
                result.Sent ? "NOTIFICATION_DELIVERED" : "NOTIFICATION_DELIVERY_RECORDED",
                releasedBy,
                reason,
                new { dispatch.DeliveryStatus, dispatch.AttemptCount },
                new { result.Status, result.Provider, result.RecipientBoundary, result.DiagnosticCode },
                context?.TraceIdentifier ?? "scheduler",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return DeliveryOutcome.Fail(SourceFailure(
                "032",
                "notification_delivery_evidence",
                exception,
                "The delivery result could not be recorded. Retry after the database source is restored."));
        }

        return DeliveryOutcome.Pending(new
        {
            module = "032",
            status = result.Status,
            sent = result.Sent,
            provider = result.Provider,
            recipientBoundary = result.RecipientBoundary,
            providerMessageId = result.ProviderMessageId,
            diagnosticCode = result.DiagnosticCode,
            message = result.Message,
            dispatchId,
            attemptNumber
        }, result.Sent);
    }

    private static async Task<ProjectNotificationDispatchRow?> LoadDispatchAsync(
        NpgsqlConnection connection,
        Guid dispatchId,
        CancellationToken cancellationToken)
    {
        var rows = await LoadDispatchRowsAsync(
            connection,
            "dispatch.project_notification_dispatch_id=@dispatch_id",
            command => command.Parameters.AddWithValue("dispatch_id", dispatchId),
            1,
            cancellationToken);
        return rows.FirstOrDefault();
    }

    private static async Task<List<ProjectNotificationDispatchRow>> LoadDispatchesAsync(
        NpgsqlConnection connection,
        ProjectNotificationActor actor,
        string status,
        int limit,
        CancellationToken cancellationToken)
    {
        var broad = IsBroad(actor);
        return await LoadDispatchRowsAsync(
            connection,
            """
            (@status='' OR dispatch.delivery_status=@status)
            AND (
                @broad
                OR dispatch.project_id IS NULL
                OR EXISTS (
                    SELECT 1 FROM projects project
                    WHERE project.project_id=dispatch.project_id
                      AND project.project_manager_user_id=@effective_user_id
                )
                OR EXISTS (
                    SELECT 1 FROM project_assignments assignment
                    WHERE assignment.project_id=dispatch.project_id
                      AND assignment.user_id=@effective_user_id
                )
                OR EXISTS (
                    SELECT 1 FROM project_notification_dispatch_recipients recipient
                    WHERE recipient.project_notification_dispatch_id=dispatch.project_notification_dispatch_id
                      AND (
                        recipient.recipient_user_id=@effective_user_id
                        OR lower(recipient.recipient_email)=lower(@email)
                      )
                )
            )
            """,
            command =>
            {
                command.Parameters.AddWithValue("status", status.Trim().ToLowerInvariant());
                command.Parameters.AddWithValue("broad", broad);
                command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
                command.Parameters.AddWithValue("email", actor.Email);
            },
            limit,
            cancellationToken);
    }

    private static async Task<List<ProjectNotificationDispatchRow>> LoadDispatchRowsAsync(
        NpgsqlConnection connection,
        string where,
        Action<NpgsqlCommand> parameters,
        int limit,
        CancellationToken cancellationToken)
    {
        var basics = new List<DispatchBasic>();
        await using (var command = new NpgsqlCommand($"""
            SELECT
                dispatch.project_notification_dispatch_id,
                dispatch.project_id,
                dispatch.routing_rule_id,
                dispatch.schedule_id,
                dispatch.event_key,
                dispatch.notification_type,
                dispatch.alert_severity,
                dispatch.source_module,
                dispatch.source_status,
                dispatch.subject,
                dispatch.text_body,
                dispatch.html_body,
                dispatch.delivery_boundary,
                dispatch.provider_source,
                dispatch.delivery_status,
                dispatch.scheduled_for,
                dispatch.released_at,
                dispatch.released_by_user_id,
                dispatch.sent_at,
                COALESCE(dispatch.provider_message_id,''),
                COALESCE(dispatch.last_error_code,''),
                COALESCE(dispatch.last_error_message,''),
                dispatch.metadata_json::text,
                dispatch.created_at,
                dispatch.updated_at,
                (SELECT COUNT(*)::integer
                 FROM project_notification_delivery_attempts attempt
                 WHERE attempt.project_notification_dispatch_id=dispatch.project_notification_dispatch_id)
            FROM project_notification_dispatches dispatch
            WHERE {where}
            ORDER BY dispatch.created_at DESC
            LIMIT @limit;
            """, connection))
        {
            parameters(command);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                basics.Add(new(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.GetString(7), reader.GetString(8), reader.GetString(9),
                    reader.GetString(10), reader.GetString(11), reader.GetString(12),
                    reader.GetString(13), reader.GetString(14),
                    reader.IsDBNull(15) ? null : ReadDateTimeOffset(reader, 15),
                    reader.IsDBNull(16) ? null : ReadDateTimeOffset(reader, 16),
                    reader.IsDBNull(17) ? null : reader.GetGuid(17),
                    reader.IsDBNull(18) ? null : ReadDateTimeOffset(reader, 18),
                    reader.GetString(19), reader.GetString(20), reader.GetString(21),
                    JsonDocument.Parse(reader.GetString(22)).RootElement.Clone(),
                    ReadDateTimeOffset(reader, 23), ReadDateTimeOffset(reader, 24),
                    reader.GetInt32(25)));
            }
        }

        var rows = new List<ProjectNotificationDispatchRow>();
        foreach (var basic in basics)
        {
            var recipients = new List<ProjectNotificationUser>();
            await using var command = new NpgsqlCommand("""
                SELECT recipient_user_id, recipient_name, recipient_email,
                       recipient_role, derivation_source, recipient_type
                FROM project_notification_dispatch_recipients
                WHERE project_notification_dispatch_id=@dispatch_id
                ORDER BY CASE recipient_type WHEN 'to' THEN 0 WHEN 'cc' THEN 1 ELSE 2 END,
                         recipient_name, recipient_email;
                """, connection);
            command.Parameters.AddWithValue("dispatch_id", basic.DispatchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                recipients.Add(new(
                    reader.IsDBNull(0) ? null : reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }
            rows.Add(new(
                basic.DispatchId, basic.ProjectId, basic.RoutingRuleId, basic.ScheduleId,
                basic.EventKey, basic.NotificationType, basic.AlertSeverity,
                basic.SourceModule, basic.SourceStatus, basic.Subject, basic.TextBody,
                basic.HtmlBody, basic.DeliveryBoundary, basic.ProviderSource,
                basic.DeliveryStatus, basic.ScheduledFor, basic.ReleasedAt,
                basic.ReleasedByUserId, basic.SentAt, basic.ProviderMessageId,
                basic.LastErrorCode, basic.LastErrorMessage, basic.Metadata,
                basic.CreatedAt, basic.UpdatedAt, recipients.ToArray(), basic.AttemptCount));
        }
        return rows;
    }

    private static async Task<List<Guid>> LoadDueDispatchIdsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<Guid>();
        await using var command = new NpgsqlCommand("""
            SELECT project_notification_dispatch_id
            FROM project_notification_dispatches
            WHERE delivery_status='queued'
              AND delivery_boundary='production_governed'
              AND COALESCE(scheduled_for,NOW()) <= NOW()
            ORDER BY created_at
            LIMIT 50;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(reader.GetGuid(0));
        return rows;
    }

    private static async Task<ScheduledReminderOutcome> QueueScheduledReminderAsync(
        NpgsqlConnection connection,
        ProjectNotificationSchedule schedule,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadSnapshotSafelyAsync(connection, cancellationToken);
        var module065 = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            null,
            cancellationToken);
        var queued = 0;
        var failures = 0;
        foreach (var project in snapshot.Projects)
        {
            var shouldQueue = schedule.ScheduleType switch
            {
                "weekly_reminder" => project.BudgetStatus is "approaching_budget" or "over_budget" or "missing_financial_information",
                "monday_reminder" => project.BudgetStatus is "over_budget" or "missing_financial_information",
                "month_end_reminder" => project.ProjectStatus is not ("completed" or "closed"),
                "escalation" => project.BudgetStatus == "over_budget",
                _ => false
            };
            if (!shouldQueue) continue;
            var recipients = DeriveRecipients(
                project,
                ["project_manager", "solution_architect", "account_executive", "project_team_coordinator"],
                null);
            if (recipients.Length == 0) { failures++; continue; }
            var eventKey = $"schedule:{schedule.ScheduleCode}:{project.ProjectId:D}:{DateTimeOffset.UtcNow:yyyyMMdd}";
            var subject = $"{schedule.ScheduleName}: {project.ProjectCode} — {project.ProjectName}";
            var body = $"Project: {project.ProjectCode} — {project.ProjectName}\n"
                + $"Customer: {project.CustomerName}\n"
                + $"Financial status: {project.BudgetStatus.Replace('_', ' ')}\n"
                + $"Planned hours: {project.PlannedHours:0.##}\n"
                + $"Used hours: {project.UsedHours:0.##}\n"
                + $"Remaining hours: {project.RemainingHours:0.##}\n"
                + $"Forecasted final cost: {Money(project.ForecastedFinalCost)}\n"
                + $"Current variance: {Money(project.CurrentVariance)}\n\n"
                + "Review the authoritative ProjectPulse financial workspace and resolve any missing information before the next governed reminder.";
            await UpsertDispatchAsync(
                connection,
                null,
                schedule.ScheduleId,
                project,
                eventKey,
                schedule.ScheduleType,
                project.BudgetStatus == "over_budget" ? "critical" : "warning",
                "023",
                project.BudgetStatus,
                subject,
                body,
                Html(body),
                MoreRestrictiveBoundary(schedule.DeliveryBoundary, module065.RecipientBoundary),
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
        return new(queued, failures);
    }

    private static async Task UpdateScheduleRunStateAsync(
        NpgsqlConnection connection,
        Guid scheduleId,
        bool started,
        string status,
        DateTimeOffset? nextRunAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(started
            ? """
              UPDATE project_notification_schedules
              SET last_started_at=NOW(), last_status=@status, updated_at=NOW()
              WHERE project_notification_schedule_id=@schedule_id;
              """
            : """
              UPDATE project_notification_schedules
              SET last_completed_at=NOW(), last_status=@status,
                  next_run_at=@next_run_at, updated_at=NOW()
              WHERE project_notification_schedule_id=@schedule_id;
              """, connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("schedule_id", scheduleId);
        if (!started) AddNullable(command, "next_run_at", NpgsqlDbType.TimestampTz, nextRunAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<ProjectCostRoutingRule>> LoadRulesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectCostRoutingRule>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project_cost_alert_routing_rule_id, rule_code, rule_name,
                metric_code, comparison_operator, threshold_value,
                threshold_unit, alert_severity, recipient_roles,
                optional_escalation_manager_user_id, escalation_after_minutes,
                delivery_boundary, enabled, description, created_at, updated_at
            FROM project_cost_alert_routing_rules
            ORDER BY CASE alert_severity WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'warning' THEN 2 ELSE 3 END,
                     rule_name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.GetString(6), reader.GetString(7), reader.GetFieldValue<string[]>(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.GetString(11), reader.GetBoolean(12), reader.GetString(13),
                ReadDateTimeOffset(reader, 14), ReadDateTimeOffset(reader, 15)));
        }
        return rows;
    }

    private static async Task<List<ProjectNotificationSchedule>> LoadSchedulesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectNotificationSchedule>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project_notification_schedule_id, schedule_code, schedule_name,
                schedule_type, day_of_week, local_time, timezone_name,
                days_before_month_end, escalation_after_minutes,
                quiet_hours_start, quiet_hours_end, enabled, delivery_boundary,
                last_started_at, last_completed_at, last_status, next_run_at,
                created_at, updated_at
            FROM project_notification_schedules
            ORDER BY enabled DESC, schedule_name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt16(4),
                ReadTimeOnly(reader, 5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : ReadTimeOnly(reader, 9),
                reader.IsDBNull(10) ? null : ReadTimeOnly(reader, 10),
                reader.GetBoolean(11), reader.GetString(12),
                reader.IsDBNull(13) ? null : ReadDateTimeOffset(reader, 13),
                reader.IsDBNull(14) ? null : ReadDateTimeOffset(reader, 14),
                reader.GetString(15),
                reader.IsDBNull(16) ? null : ReadDateTimeOffset(reader, 16),
                ReadDateTimeOffset(reader, 17), ReadDateTimeOffset(reader, 18)));
        }
        return rows;
    }

    private static async Task<ProjectNotificationSnapshotResult> LoadSnapshotSafelyAsync(
        NpgsqlConnection connection,
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
                [ProjectNotificationSourceState.Unavailable(
                    "projects",
                    "Authoritative projects and accountable owners",
                    true,
                    Diagnostic(exception),
                    "The project financial source is unavailable; notification evaluation was not fabricated.")],
                DateTimeOffset.UtcNow);
        }
    }

    private static async Task<AuthorizedConnection> OpenAuthorizedAsync(
        HttpContext context,
        Func<ProjectNotificationActor, bool> allowed)
    {
        var actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        var effectiveUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId") ?? actualUserId;
        if (!actualUserId.HasValue || !effectiveUserId.HasValue)
            return AuthorizedConnection.Fail(Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));

        var connectionString = ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return AuthorizedConnection.Fail(Results.Json(new
            {
                status = "notification_configuration_unavailable",
                source = "projectpulse_database",
                message = "Project notification configuration is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(context.RequestAborted);
            var actor = await LoadActorAsync(
                connection,
                actualUserId.Value,
                effectiveUserId.Value,
                ProjectPulseActualSessionAuthority.IsViewAs(context),
                context.RequestAborted);
            if (!allowed(actor))
            {
                await connection.DisposeAsync();
                return AuthorizedConnection.Fail(Results.Json(new
                {
                    status = actor.IsViewAs
                        ? "view_as_read_only"
                        : "project_notification_access_denied",
                    message = actor.IsViewAs
                        ? "Exit Administrator View-As before changing project notification configuration or delivery."
                        : "The current role does not have access to this project notification operation."
                }, statusCode: StatusCodes.Status403Forbidden));
            }
            return new(connection, actor, null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            return AuthorizedConnection.Fail(SourceFailure(
                "GROUP_4",
                "project_notification_database",
                exception,
                "Project notification data could not be loaded. Retry after the database source is restored."));
        }
    }

    private static async Task<ProjectNotificationActor> LoadActorAsync(
        NpgsqlConnection connection,
        Guid actualUserId,
        Guid effectiveUserId,
        bool isViewAs,
        CancellationToken cancellationToken)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string email = string.Empty;
        string displayName = string.Empty;
        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(app_user.email,''),
                COALESCE(app_user.display_name,app_user.email,''),
                COALESCE(role.role_code,''),
                COALESCE(permission.permission_code,'')
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id=app_user.user_id
             AND assignment.is_active=TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id=assignment.app_role_id
             AND role.is_active=TRUE
            LEFT JOIN app_role_permissions role_permission
              ON role_permission.app_role_id=role.app_role_id
            LEFT JOIN app_permissions permission
              ON permission.app_permission_id=role_permission.app_permission_id
            WHERE app_user.user_id=@user_id
              AND app_user.is_active=TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", effectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            email = reader.GetString(0);
            displayName = reader.GetString(1);
            if (!string.IsNullOrWhiteSpace(reader.GetString(2))) roles.Add(reader.GetString(2));
            if (!string.IsNullOrWhiteSpace(reader.GetString(3))) permissions.Add(reader.GetString(3));
        }
        return new(actualUserId, effectiveUserId, email, displayName, roles, permissions, isViewAs);
    }

    private static bool CanQueueCloseout(ProjectNotificationActor actor) => !actor.IsViewAs && (
        actor.CanDeliver
        || actor.Permissions.Contains("VIEW_CLOSEOUT_NOTIFICATION_ROUTING")
        || actor.Roles.Contains("PROJECT_MANAGER")
        || actor.Roles.Contains("PROJECT_MANAGEMENT")
        || actor.Roles.Contains("PROJECT_MANAGEMENT_LEAD")
        || actor.Roles.Contains("PM_TEAM_LEAD"));

    private static bool CanAccessProject(
        ProjectNotificationActor actor,
        ProjectNotificationFinancialSnapshot project) => IsBroad(actor)
        || project.ProjectManager?.UserId == actor.EffectiveUserId
        || project.ProjectTeamCoordinator?.UserId == actor.EffectiveUserId
        || project.SolutionArchitect?.UserId == actor.EffectiveUserId
        || project.AccountExecutive?.UserId == actor.EffectiveUserId
        || project.Engineers.Any(engineer => engineer.UserId == actor.EffectiveUserId);

    private static bool IsBroad(ProjectNotificationActor actor) => actor.Roles.Any(role =>
            BroadRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        || actor.Permissions.Contains("SYSTEM_ADMINISTRATION")
        || actor.Permissions.Contains("MANAGE_ALL")
        || actor.Permissions.Contains("VIEW_NOTIFICATION_DELIVERY_MONITOR");

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
            "Project Manager", "Assigned Engineer(s)", "Solution Architect",
            "Account Executive", "Project Team Coordinator", "Optional Escalation Manager"
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

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string entityType,
        Guid entityId,
        string actionCode,
        Guid? actorUserId,
        string reason,
        object? prior,
        object? next,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO project_notification_configuration_audit (
                entity_type, entity_id, action_code, actor_user_id,
                change_reason, prior_json, new_json, correlation_id
            )
            VALUES (
                @entity_type, @entity_id, @action_code, @actor_user_id,
                @change_reason, @prior_json::jsonb, @new_json::jsonb, @correlation_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("action_code", Limit(actionCode, 100));
        AddNullable(command, "actor_user_id", NpgsqlDbType.Uuid, actorUserId);
        command.Parameters.AddWithValue("change_reason", Limit(reason, 2000));
        command.Parameters.AddWithValue("prior_json", prior is null
            ? "null"
            : JsonSerializer.Serialize(prior));
        command.Parameters.AddWithValue("new_json", next is null
            ? "null"
            : JsonSerializer.Serialize(next));
        command.Parameters.AddWithValue("correlation_id", Limit(correlationId, 160));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IResult SourceFailure(
        string module,
        string source,
        Exception exception,
        string message) => Results.Json(new
        {
            module,
            status = "source_unavailable",
            source,
            diagnosticCode = Diagnostic(exception),
            correlationId = Guid.NewGuid().ToString("N"),
            message
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"POSTGRES_{postgres.SqlState}",
        NpgsqlException => "POSTGRES_CONNECTION_UNAVAILABLE",
        _ => exception.GetType().Name.ToUpperInvariant()
    };

    private static string ValidateRule(
        string metric,
        string comparison,
        decimal? threshold,
        string unit,
        string[] recipients,
        int? escalationAfter)
    {
        if (!AllowedMetricCodes.Contains(metric)) return "Choose a supported project cost metric.";
        if (metric is "approaching_budget" or "over_budget" or "missing_financial_information" or "failed_project_data_refresh")
        {
            if (comparison is not ("state" or "event"))
                return "State and event rules must use a state or event comparison.";
        }
        else if (comparison is not ("gt" or "gte" or "lt" or "lte" or "eq"))
            return "Numeric rules must use a numeric comparison.";
        if (metric is "hours_used_percent" or "labor_budget_used_percent" or "expenses_used_percent" or "forecasted_total_cost"
            && !threshold.HasValue)
            return "A numeric threshold is required for this rule.";
        if (threshold is < 0) return "Threshold values cannot be negative.";
        if (unit == "percent" && threshold is > 10000) return "Percentage thresholds must be reasonable.";
        if (recipients.Length == 0) return "Select at least one automatically derived recipient role.";
        if (recipients.Any(role => !AllowedRecipientRoles.Contains(role)))
            return "One or more recipient roles are not supported.";
        if (escalationAfter is < 0 or > 43200)
            return "Escalation timing must be between zero and 43,200 minutes.";
        return string.Empty;
    }

    private static string ValidateSchedule(
        string scheduleType,
        int? dayOfWeek,
        int? daysBeforeMonthEnd,
        int? escalationAfter,
        TimeOnly? quietStart,
        TimeOnly? quietEnd)
    {
        if (scheduleType is not ("cost_alert_evaluation" or "weekly_reminder" or "monday_reminder" or "month_end_reminder" or "escalation"))
            return "Choose a supported notification schedule type.";
        if (scheduleType is "weekly_reminder" or "monday_reminder" or "cost_alert_evaluation"
            && (!dayOfWeek.HasValue || dayOfWeek is < 0 or > 6))
            return "A valid day of week is required for this schedule.";
        if (scheduleType == "month_end_reminder"
            && (!daysBeforeMonthEnd.HasValue || daysBeforeMonthEnd is < 0 or > 31))
            return "Month-end schedules require zero to 31 days before month end.";
        if (escalationAfter is < 0 or > 43200)
            return "Escalation timing must be between zero and 43,200 minutes.";
        if (quietStart.HasValue != quietEnd.HasValue)
            return "Quiet hours require both a start and an end time.";
        return string.Empty;
    }

    private static string NormalizeMetric(string? value, string fallback)
    {
        var normalized = (value ?? fallback).Trim().ToLowerInvariant();
        return AllowedMetricCodes.Contains(normalized) ? normalized : fallback;
    }

    private static string NormalizeComparison(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "gt" or "gte" or "lt" or "lte" or "eq" or "state" or "event" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    private static string NormalizeUnit(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "percent" or "currency" or "state" or "event" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    private static string NormalizeSeverity(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "informational" or "warning" or "high" or "critical" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    private static string NormalizeBoundary(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "test_only" or "production_governed" or "locked" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    private static string NormalizeScheduleType(string? value, string fallback) =>
        (value ?? fallback).Trim().ToLowerInvariant() switch
        {
            "cost_alert_evaluation" or "weekly_reminder" or "monday_reminder"
                or "month_end_reminder" or "escalation" =>
                (value ?? fallback).Trim().ToLowerInvariant(),
            _ => fallback
        };

    private static string NormalizeTimezone(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(candidate); return candidate; }
        catch { return fallback; }
    }

    private static string[] NormalizeRecipientRoles(string[]? values, string[] fallback)
    {
        var normalized = (values ?? fallback)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(AllowedRecipientRoles.Contains)
            .ToArray();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string MoreRestrictiveBoundary(string rule, string module065)
    {
        var ranking = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["locked"] = 0,
            ["test_only"] = 1,
            ["production_governed"] = 2
        };
        var normalizedRule = NormalizeBoundary(rule, "locked");
        var normalizedModule = NormalizeBoundary(module065, "locked");
        return ranking[normalizedRule] <= ranking[normalizedModule]
            ? normalizedRule
            : normalizedModule;
    }

    private static string BuildCostAlertSubject(
        ProjectCostRoutingRule rule,
        ProjectNotificationFinancialSnapshot project) =>
        $"Project cost {SeverityLabel(rule.AlertSeverity)}: {project.ProjectCode} — {project.ProjectName}";

    private static string BuildCostAlertBody(
        ProjectCostRoutingRule rule,
        ProjectNotificationFinancialSnapshot project,
        ProjectNotificationMetricEvaluation evaluation) =>
        $"Project cost routing rule triggered\n\n"
        + $"Rule: {rule.RuleName}\n"
        + $"Customer: {project.CustomerName}\n"
        + $"Project: {project.ProjectCode} — {project.ProjectName}\n"
        + $"Project Manager: {project.ProjectManager?.DisplayName ?? "Not assigned"}\n"
        + $"Financial status: {project.BudgetStatus.Replace('_', ' ')}\n"
        + $"Reason: {evaluation.Reason}\n\n"
        + $"Planned hours: {project.PlannedHours:0.##}\n"
        + $"Used hours: {project.UsedHours:0.##}\n"
        + $"Remaining hours: {project.RemainingHours:0.##}\n"
        + $"Labor budget: {Money(project.LaborBudget)}\n"
        + $"Expense budget: {Money(project.ExpenseBudget)}\n"
        + $"Calculated labor cost: {Money(project.LaborCost)}\n"
        + $"Uploaded expenses: {Money(project.UploadedExpenses)}\n"
        + $"Forecasted final cost: {Money(project.ForecastedFinalCost)}\n"
        + $"Current variance: {Money(project.CurrentVariance)}\n\n"
        + "Open ProjectPulse to review the authoritative calculation and source evidence.";

    private static string BuildCloseoutBody(ProjectNotificationFinancialSnapshot project) =>
        $"Project {project.ProjectCode} — {project.ProjectName} for {project.CustomerName} is ready for closeout communication.\n\n"
        + $"Project Manager: {project.ProjectManager?.DisplayName ?? "Not assigned"}\n"
        + $"Project status: {project.ProjectStatus}\n"
        + $"Planned hours: {project.PlannedHours:0.##}\n"
        + $"Used hours: {project.UsedHours:0.##}\n"
        + $"Remaining hours: {project.RemainingHours:0.##}\n"
        + $"Financial status: {project.BudgetStatus.Replace('_', ' ')}\n\n"
        + "Project Manager: schedule the customer lessons-learned session and complete the governed closeout checklist.\n"
        + "This notification does not finalize accounting, send an invoice, or replace customer acceptance evidence.";

    private static string Html(string text) => "<div style=\"font-family:Arial,sans-serif;line-height:1.5\">"
        + System.Net.WebUtility.HtmlEncode(text)
            .Replace("\r\n", "<br />", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal)
        + "</div>";

    private static string SeverityLabel(string severity) => severity switch
    {
        "critical" => "critical alert",
        "high" => "high-priority alert",
        "warning" => "warning",
        _ => "notice"
    };

    private static string Money(decimal? value) => value.HasValue
        ? value.Value.ToString("C2")
        : "Not recorded";

    private static decimal? SumKnown(params decimal?[] values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static bool IsEmail(string value)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(value)
                && new System.Net.Mail.MailAddress(value).Address.Equals(
                    value.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string RecipientType(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cc" => "cc",
            "bcc" => "bcc",
            _ => "to"
        };

    private static string Clean(string? value, int max, string fallback)
    {
        var cleaned = (value ?? string.Empty).Replace('\0', ' ').Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = fallback;
        return Limit(cleaned, max);
    }

    private static string Limit(string value, int max) => value.Length <= max
        ? value
        : value[..max];

    private static TimeOnly? ParseTimeOrExisting(string? value, TimeOnly? existing) =>
        string.IsNullOrWhiteSpace(value)
            ? existing
            : TimeOnly.TryParse(value, out var parsed) ? parsed : existing;

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value) => command.Parameters.Add(new NpgsqlParameter(name, type)
        {
            Value = value ?? DBNull.Value
        });

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static TimeOnly ReadTimeOnly(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            TimeOnly time => time,
            TimeSpan span => TimeOnly.FromTimeSpan(span),
            DateTime dateTime => TimeOnly.FromDateTime(dateTime),
            _ => TimeOnly.Parse(value.ToString() ?? string.Empty)
        };
    }

    internal static string ConnectionString()
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
            || string.IsNullOrWhiteSpace(password)) return string.Empty;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port)
                ? port
                : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10,
            Timeout = 5,
            CommandTimeout = 20
        }.ConnectionString;
    }

    private static ProjectNotificationActor ProjectNotificationActorForScheduler() => new(
        Guid.Empty,
        Guid.Empty,
        "projectpulse-scheduler@system.local",
        "ProjectPulse Notification Scheduler",
        new HashSet<string>(["SUPER_ADMINISTRATOR"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["MANAGE_ALL"], StringComparer.OrdinalIgnoreCase),
        false);

    private sealed record AuthorizedConnection(
        NpgsqlConnection? Connection,
        ProjectNotificationActor? Actor,
        IResult? Failure)
    {
        internal static AuthorizedConnection Fail(IResult result) => new(null, null, result);
    }

    private sealed record DispatchBasic(
        Guid DispatchId, Guid? ProjectId, Guid? RoutingRuleId, Guid? ScheduleId,
        string EventKey, string NotificationType, string AlertSeverity,
        string SourceModule, string SourceStatus, string Subject, string TextBody,
        string HtmlBody, string DeliveryBoundary, string ProviderSource,
        string DeliveryStatus, DateTimeOffset? ScheduledFor, DateTimeOffset? ReleasedAt,
        Guid? ReleasedByUserId, DateTimeOffset? SentAt, string ProviderMessageId,
        string LastErrorCode, string LastErrorMessage, JsonElement Metadata,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int AttemptCount);

    private sealed record EvaluationOutcome(
        object? Payload,
        IResult? Failure,
        int EvaluatedProjects,
        int DispatchesQueued,
        int Delivered,
        int Failures)
    {
        internal static EvaluationOutcome Success(
            object payload,
            int evaluated,
            int queued,
            int delivered,
            int failures) => new(payload, null, evaluated, queued, delivered, failures);
        internal static EvaluationOutcome Fail(IResult failure) => new(null, failure, 0, 0, 0, 1);
    }

    private sealed record DeliveryOutcome(object? Payload, IResult? Failure, bool Sent)
    {
        internal static DeliveryOutcome Pending(object payload, bool sent = false) => new(payload, null, sent);
        internal static DeliveryOutcome Fail(IResult failure) => new(null, failure, false);
    }

    internal sealed record ScheduleRunSummary(
        int DueScheduleCount,
        int EvaluatedProjectCount,
        int DispatchesQueued,
        int Delivered,
        int Failures,
        int DueDispatchCount,
        DateTimeOffset CompletedAt);

    private sealed record ScheduledReminderOutcome(int Queued, int Failures);
}

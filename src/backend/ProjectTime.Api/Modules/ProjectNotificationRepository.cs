using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class ProjectNotificationRepository
{
    internal static readonly TimeSpan DeliveryClaimReconciliationDelay = TimeSpan.FromMinutes(10);

    private static readonly string[] BroadRoles =
    [
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "PROJECT_TEAM_COORDINATOR",
        "ACCOUNTING", "ACCOUNTING_BILLING", "BILLING", "FINANCE", "EXECUTIVE"
    ];

    internal static async Task<AuthorizedConnection> OpenAuthorizedAsync(
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
        {
            return AuthorizedConnection.Fail(Results.Json(new
            {
                status = "session_required",
                message = "A valid Pulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return AuthorizedConnection.Fail(Results.Json(new
            {
                status = "notification_configuration_unavailable",
                source = "projectpulse_database",
                message = "Project notification configuration is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

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

    internal static async Task<ProjectNotificationActor> LoadActorAsync(
        NpgsqlConnection connection,
        Guid actualUserId,
        Guid effectiveUserId,
        bool isViewAs,
        CancellationToken cancellationToken)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var email = string.Empty;
        var displayName = string.Empty;

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
            var role = reader.GetString(2);
            var permission = reader.GetString(3);
            if (!string.IsNullOrWhiteSpace(role)) roles.Add(role);
            if (!string.IsNullOrWhiteSpace(permission)) permissions.Add(permission);
        }

        return new(
            actualUserId,
            effectiveUserId,
            email,
            displayName,
            roles,
            permissions,
            isViewAs);
    }

    internal static bool IsBroad(ProjectNotificationActor actor) =>
        actor.Roles.Any(role => BroadRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        || actor.Permissions.Contains("SYSTEM_ADMINISTRATION")
        || actor.Permissions.Contains("MANAGE_ALL")
        || actor.Permissions.Contains("VIEW_NOTIFICATION_DELIVERY_MONITOR");

    internal static async Task<List<ProjectCostRoutingRule>> LoadRulesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectCostRoutingRule>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project_cost_alert_routing_rule_id,
                rule_code,
                rule_name,
                metric_code,
                comparison_operator,
                threshold_value,
                threshold_unit,
                alert_severity,
                recipient_roles,
                optional_escalation_manager_user_id,
                escalation_after_minutes,
                delivery_boundary,
                enabled,
                description,
                created_at,
                updated_at
            FROM project_cost_alert_routing_rules
            ORDER BY
                CASE alert_severity
                    WHEN 'critical' THEN 0
                    WHEN 'high' THEN 1
                    WHEN 'warning' THEN 2
                    ELSE 3
                END,
                rule_name;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetFieldValue<string[]>(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.GetString(11),
                reader.GetBoolean(12),
                reader.GetString(13),
                ReadDateTimeOffset(reader, 14),
                ReadDateTimeOffset(reader, 15)));
        }

        return rows;
    }

    internal static async Task UpdateRuleAsync(
        NpgsqlConnection connection,
        ProjectNotificationActor actor,
        ProjectCostRoutingRule prior,
        ProjectCostRoutingRule replacement,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
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
            AddNullable(command, "threshold_value", NpgsqlDbType.Numeric, replacement.ThresholdValue);
            command.Parameters.AddWithValue("threshold_unit", replacement.ThresholdUnit);
            command.Parameters.AddWithValue("alert_severity", replacement.AlertSeverity);
            command.Parameters.Add(new NpgsqlParameter(
                "recipient_roles",
                NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = replacement.RecipientRoles
            });
            AddNullable(
                command,
                "escalation_manager",
                NpgsqlDbType.Uuid,
                replacement.OptionalEscalationManagerUserId);
            AddNullable(
                command,
                "escalation_after",
                NpgsqlDbType.Integer,
                replacement.EscalationAfterMinutes);
            command.Parameters.AddWithValue("delivery_boundary", replacement.DeliveryBoundary);
            command.Parameters.AddWithValue("enabled", replacement.Enabled);
            command.Parameters.AddWithValue("description", replacement.Description);
            command.Parameters.AddWithValue("updated_by", actor.ActualUserId);
            command.Parameters.AddWithValue("rule_id", replacement.RuleId);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await WriteAuditAsync(
                connection,
                transaction,
                "routing_rule",
                replacement.RuleId,
                "ROUTING_RULE_UPDATED",
                actor.ActualUserId,
                reason,
                prior,
                replacement,
                correlationId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<List<ProjectNotificationSchedule>> LoadSchedulesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectNotificationSchedule>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project_notification_schedule_id,
                schedule_code,
                schedule_name,
                schedule_type,
                day_of_week,
                local_time,
                timezone_name,
                days_before_month_end,
                escalation_after_minutes,
                quiet_hours_start,
                quiet_hours_end,
                enabled,
                delivery_boundary,
                last_started_at,
                last_completed_at,
                last_status,
                next_run_at,
                created_at,
                updated_at
            FROM project_notification_schedules
            ORDER BY enabled DESC, schedule_name;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt16(4),
                ReadTimeOnly(reader, 5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : ReadTimeOnly(reader, 9),
                reader.IsDBNull(10) ? null : ReadTimeOnly(reader, 10),
                reader.GetBoolean(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : ReadDateTimeOffset(reader, 13),
                reader.IsDBNull(14) ? null : ReadDateTimeOffset(reader, 14),
                reader.GetString(15),
                reader.IsDBNull(16) ? null : ReadDateTimeOffset(reader, 16),
                ReadDateTimeOffset(reader, 17),
                ReadDateTimeOffset(reader, 18)));
        }

        return rows;
    }

    internal static async Task UpdateScheduleAsync(
        NpgsqlConnection connection,
        ProjectNotificationActor actor,
        ProjectNotificationSchedule prior,
        ProjectNotificationSchedule replacement,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
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
            AddNullable(
                command,
                "day_of_week",
                NpgsqlDbType.Smallint,
                replacement.DayOfWeek.HasValue ? (short)replacement.DayOfWeek.Value : null);
            command.Parameters.AddWithValue("local_time", replacement.LocalTime);
            command.Parameters.AddWithValue("timezone_name", replacement.TimezoneName);
            AddNullable(
                command,
                "days_before_month_end",
                NpgsqlDbType.Integer,
                replacement.DaysBeforeMonthEnd);
            AddNullable(
                command,
                "escalation_after",
                NpgsqlDbType.Integer,
                replacement.EscalationAfterMinutes);
            AddNullable(command, "quiet_start", NpgsqlDbType.Time, replacement.QuietHoursStart);
            AddNullable(command, "quiet_end", NpgsqlDbType.Time, replacement.QuietHoursEnd);
            command.Parameters.AddWithValue("enabled", replacement.Enabled);
            command.Parameters.AddWithValue("delivery_boundary", replacement.DeliveryBoundary);
            AddNullable(command, "next_run_at", NpgsqlDbType.TimestampTz, replacement.NextRunAt);
            command.Parameters.AddWithValue("updated_by", actor.ActualUserId);
            command.Parameters.AddWithValue("schedule_id", replacement.ScheduleId);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await WriteAuditAsync(
                connection,
                transaction,
                "schedule",
                replacement.ScheduleId,
                "NOTIFICATION_SCHEDULE_UPDATED",
                actor.ActualUserId,
                reason,
                prior,
                replacement,
                correlationId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<Guid> UpsertDispatchAsync(
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
        IReadOnlyCollection<ProjectNotificationUser> recipients,
        object metadata,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            Guid dispatchId;
            await using (var command = new NpgsqlCommand("""
                INSERT INTO project_notification_dispatches (
                    project_id,
                    routing_rule_id,
                    schedule_id,
                    event_key,
                    notification_type,
                    alert_severity,
                    source_module,
                    source_status,
                    subject,
                    text_body,
                    html_body,
                    delivery_boundary,
                    provider_source,
                    delivery_status,
                    scheduled_for,
                    metadata_json
                )
                VALUES (
                    @project_id,
                    @rule_id,
                    @schedule_id,
                    @event_key,
                    @notification_type,
                    @severity,
                    @source_module,
                    @source_status,
                    @subject,
                    @text_body,
                    @html_body,
                    @delivery_boundary,
                    'module_065',
                    @delivery_status,
                    NOW(),
                    @metadata::jsonb
                )
                ON CONFLICT (event_key) DO UPDATE
                SET alert_severity=EXCLUDED.alert_severity,
                    source_status=EXCLUDED.source_status,
                    subject=EXCLUDED.subject,
                    text_body=EXCLUDED.text_body,
                    html_body=EXCLUDED.html_body,
                    delivery_boundary=EXCLUDED.delivery_boundary,
                    delivery_status=CASE
                        WHEN project_notification_dispatches.delivery_status IN ('sent','sending')
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
                command.Parameters.AddWithValue("severity", NormalizeSeverity(severity));
                command.Parameters.AddWithValue("source_module", Limit(sourceModule, 20));
                command.Parameters.AddWithValue("source_status", Limit(sourceStatus, 80));
                command.Parameters.AddWithValue("subject", Limit(subject, 500));
                command.Parameters.AddWithValue("text_body", Limit(textBody, 30000));
                command.Parameters.AddWithValue("html_body", Limit(htmlBody, 60000));
                command.Parameters.AddWithValue("delivery_boundary", NormalizeBoundary(deliveryBoundary));
                command.Parameters.AddWithValue("delivery_status", NormalizeDispatchStatus(deliveryStatus));
                command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
                dispatchId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Dispatch ID was not returned."));
            }

            await using (var clear = new NpgsqlCommand("""
                DELETE FROM project_notification_dispatch_recipients
                WHERE project_notification_dispatch_id=@dispatch_id
                  AND NOT EXISTS (
                    SELECT 1
                    FROM project_notification_dispatches dispatch
                    WHERE dispatch.project_notification_dispatch_id=@dispatch_id
                      AND dispatch.delivery_status IN ('sent','sending')
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
                        project_notification_dispatch_id,
                        recipient_role,
                        recipient_user_id,
                        recipient_name,
                        recipient_email,
                        recipient_type,
                        derivation_source,
                        delivery_status
                    )
                    VALUES (
                        @dispatch_id,
                        @role,
                        @user_id,
                        @name,
                        @email,
                        @type,
                        @source,
                        'pending'
                    )
                    ON CONFLICT (
                        project_notification_dispatch_id,
                        lower(recipient_email),
                        recipient_type
                    )
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

    internal static async Task<ProjectNotificationDispatchRow?> LoadDispatchAsync(
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

    internal static async Task<List<ProjectNotificationDispatchRow>> LoadDispatchesAsync(
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
                    SELECT 1
                    FROM projects project
                    WHERE project.project_id=dispatch.project_id
                      AND project.project_manager_user_id=@effective_user_id
                )
                OR EXISTS (
                    SELECT 1
                    FROM project_assignments assignment
                    WHERE assignment.project_id=dispatch.project_id
                      AND assignment.user_id=@effective_user_id
                )
                OR EXISTS (
                    SELECT 1
                    FROM project_notification_dispatch_recipients recipient
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

    internal static async Task<List<DeliveryAttemptView>> LoadRecentAttemptsAsync(
        NpgsqlConnection connection,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<DeliveryAttemptView>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project_notification_delivery_attempt_id,
                project_notification_dispatch_id,
                attempt_number,
                provider_source,
                configured_provider,
                recipient_boundary,
                attempt_status,
                COALESCE(provider_message_id,''),
                COALESCE(diagnostic_code,''),
                COALESCE(diagnostic_message,''),
                attempted_at
            FROM project_notification_delivery_attempts
            ORDER BY attempted_at DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                ReadDateTimeOffset(reader, 10)));
        }
        return rows;
    }

    internal static async Task<List<Guid>> LoadDueDispatchIdsAsync(
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

    internal static async Task<bool> TryClaimDispatchDeliveryAsync(
        NpgsqlConnection connection,
        Guid dispatchId,
        Guid? releasedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                UPDATE project_notification_dispatches
                SET delivery_status='sending',
                    released_at=COALESCE(released_at,NOW()),
                    released_by_user_id=COALESCE(@released_by,released_by_user_id),
                    last_error_code='',
                    last_error_message='',
                    updated_at=NOW()
                WHERE project_notification_dispatch_id=@dispatch_id
                  AND delivery_status IN ('preview_ready','held','queued','failed','suppressed')
                RETURNING delivery_status;
                """, connection, transaction);
            AddNullable(command, "released_by", NpgsqlDbType.Uuid, releasedBy);
            command.Parameters.AddWithValue("dispatch_id", dispatchId);
            var claimed = await command.ExecuteScalarAsync(cancellationToken) is string;
            if (claimed)
            {
                await WriteAuditAsync(
                    connection,
                    transaction,
                    "dispatch",
                    dispatchId,
                    "NOTIFICATION_DELIVERY_CLAIMED",
                    releasedBy,
                    reason,
                    new { deliveryStatus = "eligible" },
                    new { deliveryStatus = "sending", retryAllowed = false },
                    correlationId,
                    cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task MarkDispatchDeliveryOutcomeUnknownAsync(
        NpgsqlConnection connection,
        Guid dispatchId,
        Guid? releasedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                UPDATE project_notification_dispatches
                SET last_error_code='DELIVERY_OUTCOME_UNKNOWN',
                    last_error_message='The provider call finished, but its result could not be finalized. Review provider evidence before reconciling this dispatch.'
                WHERE project_notification_dispatch_id=@dispatch_id
                  AND delivery_status='sending';
                """, connection, transaction);
            command.Parameters.AddWithValue("dispatch_id", dispatchId);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
            if (changed)
            {
                await WriteAuditAsync(
                    connection,
                    transaction,
                    "dispatch",
                    dispatchId,
                    "NOTIFICATION_DELIVERY_OUTCOME_UNKNOWN",
                    releasedBy,
                    reason,
                    new { deliveryStatus = "sending" },
                    new
                    {
                        deliveryStatus = "sending",
                        diagnosticCode = "DELIVERY_OUTCOME_UNKNOWN",
                        automaticRetryAllowed = false
                    },
                    correlationId,
                    cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<DispatchDeliveryReconciliationResult> ReconcileDispatchDeliveryAsync(
        NpgsqlConnection connection,
        Guid dispatchId,
        Guid reconciledBy,
        bool confirmedSent,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            string? currentStatus = null;
            var updatedAt = DateTimeOffset.MinValue;
            var providerSource = "module_065";
            var deliveryBoundary = "locked";
            var providerMessageId = string.Empty;
            await using (var read = new NpgsqlCommand("""
                SELECT delivery_status,
                       updated_at,
                       provider_source,
                       delivery_boundary,
                       COALESCE(provider_message_id,'')
                FROM project_notification_dispatches
                WHERE project_notification_dispatch_id=@dispatch_id
                FOR UPDATE;
                """, connection, transaction))
            {
                read.Parameters.AddWithValue("dispatch_id", dispatchId);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    currentStatus = reader.GetString(0);
                    updatedAt = ReadDateTimeOffset(reader, 1);
                    providerSource = reader.GetString(2);
                    deliveryBoundary = reader.GetString(3);
                    providerMessageId = reader.GetString(4);
                }
            }

            if (currentStatus is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DispatchDeliveryReconciliationResult.NotFound(dispatchId);
            }
            if (!string.Equals(currentStatus, "sending", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DispatchDeliveryReconciliationResult.NotSending(dispatchId, currentStatus);
            }

            var availableAt = updatedAt.Add(DeliveryClaimReconciliationDelay);
            if (DateTimeOffset.UtcNow < availableAt)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DispatchDeliveryReconciliationResult.Waiting(dispatchId, availableAt);
            }

            var nextStatus = confirmedSent ? "sent" : "failed";
            var diagnosticCode = confirmedSent
                ? "DELIVERY_CONFIRMED_SENT"
                : "DELIVERY_CONFIRMED_NOT_SENT";
            var diagnosticMessage = confirmedSent
                ? "An authorized operator confirmed delivery from provider evidence after the application result became indeterminate."
                : "An authorized operator confirmed that the provider did not send the message. A separate explicit retry is now permitted.";

            await using (var attempt = new NpgsqlCommand("""
                INSERT INTO project_notification_delivery_attempts (
                    project_notification_dispatch_id,
                    attempt_number,
                    provider_source,
                    configured_provider,
                    recipient_boundary,
                    attempt_status,
                    provider_message_id,
                    diagnostic_code,
                    diagnostic_message,
                    attempted_at
                )
                SELECT @dispatch_id,
                       COALESCE(MAX(existing.attempt_number),0) + 1,
                       @provider_source,
                       'manual_reconciliation',
                       @boundary,
                       @status,
                       @message_id,
                       @diagnostic_code,
                       @diagnostic_message,
                       NOW()
                FROM project_notification_delivery_attempts existing
                WHERE existing.project_notification_dispatch_id=@dispatch_id;
                """, connection, transaction))
            {
                attempt.Parameters.AddWithValue("dispatch_id", dispatchId);
                attempt.Parameters.AddWithValue("provider_source", providerSource);
                attempt.Parameters.AddWithValue("boundary", deliveryBoundary);
                attempt.Parameters.AddWithValue("status", nextStatus);
                attempt.Parameters.AddWithValue("message_id", providerMessageId);
                attempt.Parameters.AddWithValue("diagnostic_code", diagnosticCode);
                attempt.Parameters.AddWithValue("diagnostic_message", diagnosticMessage);
                await attempt.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var update = new NpgsqlCommand("""
                UPDATE project_notification_dispatches
                SET delivery_status=@status,
                    sent_at=CASE WHEN @confirmed_sent THEN COALESCE(sent_at,NOW()) ELSE sent_at END,
                    last_error_code=CASE WHEN @confirmed_sent THEN '' ELSE @diagnostic_code END,
                    last_error_message=CASE WHEN @confirmed_sent THEN '' ELSE @diagnostic_message END,
                    updated_at=NOW()
                WHERE project_notification_dispatch_id=@dispatch_id
                  AND delivery_status='sending';
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("status", nextStatus);
                update.Parameters.AddWithValue("confirmed_sent", confirmedSent);
                update.Parameters.AddWithValue("diagnostic_code", diagnosticCode);
                update.Parameters.AddWithValue("diagnostic_message", diagnosticMessage);
                update.Parameters.AddWithValue("dispatch_id", dispatchId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var recipients = new NpgsqlCommand("""
                UPDATE project_notification_dispatch_recipients
                SET delivery_status=@status
                WHERE project_notification_dispatch_id=@dispatch_id;
                """, connection, transaction))
            {
                recipients.Parameters.AddWithValue("status", confirmedSent ? "sent" : "failed");
                recipients.Parameters.AddWithValue("dispatch_id", dispatchId);
                await recipients.ExecuteNonQueryAsync(cancellationToken);
            }

            await WriteAuditAsync(
                connection,
                transaction,
                "dispatch",
                dispatchId,
                confirmedSent
                    ? "NOTIFICATION_DELIVERY_RECONCILED_SENT"
                    : "NOTIFICATION_DELIVERY_RECONCILED_NOT_SENT",
                reconciledBy,
                reason,
                new { deliveryStatus = currentStatus, outcome = "unknown" },
                new
                {
                    deliveryStatus = nextStatus,
                    outcome = confirmedSent ? "confirmed_sent" : "confirmed_not_sent",
                    automaticRetryAllowed = false,
                    explicitRetryAllowed = !confirmedSent
                },
                correlationId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return DispatchDeliveryReconciliationResult.Reconciled(
                dispatchId,
                nextStatus,
                confirmedSent);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task RecordDeliveryAsync(
        NpgsqlConnection connection,
        ProjectNotificationDispatchRow dispatch,
        Module065MailDeliveryResult result,
        Guid? releasedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var attemptNumber = dispatch.AttemptCount + 1;
            await using (var attempt = new NpgsqlCommand("""
                INSERT INTO project_notification_delivery_attempts (
                    project_notification_dispatch_id,
                    attempt_number,
                    provider_source,
                    configured_provider,
                    recipient_boundary,
                    attempt_status,
                    provider_message_id,
                    diagnostic_code,
                    diagnostic_message,
                    attempted_at
                )
                VALUES (
                    @dispatch_id,
                    @attempt_number,
                    'module_065',
                    @provider,
                    @boundary,
                    @status,
                    @message_id,
                    @diagnostic_code,
                    @diagnostic_message,
                    NOW()
                );
                """, connection, transaction))
            {
                attempt.Parameters.AddWithValue("dispatch_id", dispatch.DispatchId);
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
                update.Parameters.AddWithValue("dispatch_id", dispatch.DispatchId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var recipients = new NpgsqlCommand("""
                UPDATE project_notification_dispatch_recipients
                SET delivery_status=@recipient_status
                WHERE project_notification_dispatch_id=@dispatch_id;
                """, connection, transaction))
            {
                recipients.Parameters.AddWithValue(
                    "recipient_status",
                    result.Sent
                        ? "sent"
                        : result.Status == "failed" ? "failed" : "suppressed");
                recipients.Parameters.AddWithValue("dispatch_id", dispatch.DispatchId);
                await recipients.ExecuteNonQueryAsync(cancellationToken);
            }

            await WriteAuditAsync(
                connection,
                transaction,
                "dispatch",
                dispatch.DispatchId,
                result.Sent
                    ? "NOTIFICATION_DELIVERED"
                    : "NOTIFICATION_DELIVERY_RECORDED",
                releasedBy,
                reason,
                new { dispatch.DeliveryStatus, dispatch.AttemptCount },
                new
                {
                    result.Status,
                    result.Provider,
                    result.RecipientBoundary,
                    result.DiagnosticCode
                },
                correlationId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task UpdateScheduleRunStateAsync(
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
              SET last_completed_at=NOW(),
                  last_status=@status,
                  next_run_at=@next_run_at,
                  updated_at=NOW()
              WHERE project_notification_schedule_id=@schedule_id;
              """, connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("schedule_id", scheduleId);
        if (!started)
            AddNullable(command, "next_run_at", NpgsqlDbType.TimestampTz, nextRunAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<ProjectNotificationUser?> LoadUserAsync(
        NpgsqlConnection connection,
        Guid userId,
        string role,
        string source,
        string recipientType,
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
            ? new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                role,
                source,
                recipientType)
            : null;
    }

    internal static async Task<ProjectNotificationUser[]> LoadUsersInRolesAsync(
        NpgsqlConnection connection,
        string[] roleCodes,
        string recipientRole,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectNotificationUser>();
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT
                app_user.user_id,
                COALESCE(app_user.display_name,app_user.email,''),
                COALESCE(app_user.email,''),
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
        command.Parameters.AddWithValue("role_codes", roleCodes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                recipientRole,
                $"app_user_role_assignments:{reader.GetString(3)}",
                "to"));
        }
        return rows.ToArray();
    }

    internal static async Task<bool> TryAcquireSchedulerLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_try_advisory_lock(hashtext('projectpulse_group4_notification_scheduler'));
            """, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    internal static async Task ReleaseSchedulerLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_advisory_unlock(hashtext('projectpulse_group4_notification_scheduler'));
            """, connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    internal static async Task<bool> MigrationReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.project_notification_schedules') IS NOT NULL
               AND to_regclass('public.project_notification_dispatches') IS NOT NULL
               AND to_regclass('public.project_cost_alert_routing_rules') IS NOT NULL;
            """, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
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
            || string.IsNullOrWhiteSpace(password))
            return string.Empty;

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

    internal static IResult SourceFailure(
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

    internal static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"POSTGRES_{postgres.SqlState}",
        NpgsqlException => "POSTGRES_CONNECTION_UNAVAILABLE",
        _ => exception.GetType().Name.ToUpperInvariant()
    };

    private static async Task<List<ProjectNotificationDispatchRow>> LoadDispatchRowsAsync(
        NpgsqlConnection connection,
        string where,
        Action<NpgsqlCommand> addParameters,
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
                (
                    SELECT COUNT(*)::integer
                    FROM project_notification_delivery_attempts attempt
                    WHERE attempt.project_notification_dispatch_id=dispatch.project_notification_dispatch_id
                ) AS attempt_count
            FROM project_notification_dispatches dispatch
            WHERE {where}
            ORDER BY dispatch.created_at DESC
            LIMIT @limit;
            """, connection))
        {
            addParameters(command);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                basics.Add(new(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : ReadDateTimeOffset(reader, 15),
                    reader.IsDBNull(16) ? null : ReadDateTimeOffset(reader, 16),
                    reader.IsDBNull(17) ? null : reader.GetGuid(17),
                    reader.IsDBNull(18) ? null : ReadDateTimeOffset(reader, 18),
                    reader.GetString(19),
                    reader.GetString(20),
                    reader.GetString(21),
                    JsonDocument.Parse(reader.GetString(22)).RootElement.Clone(),
                    ReadDateTimeOffset(reader, 23),
                    ReadDateTimeOffset(reader, 24),
                    reader.GetInt32(25)));
            }
        }

        var rows = new List<ProjectNotificationDispatchRow>();
        foreach (var basic in basics)
        {
            var recipients = new List<ProjectNotificationUser>();
            await using var command = new NpgsqlCommand("""
                SELECT
                    recipient_user_id,
                    recipient_name,
                    recipient_email,
                    recipient_role,
                    derivation_source,
                    recipient_type
                FROM project_notification_dispatch_recipients
                WHERE project_notification_dispatch_id=@dispatch_id
                ORDER BY
                    CASE recipient_type WHEN 'to' THEN 0 WHEN 'cc' THEN 1 ELSE 2 END,
                    recipient_name,
                    recipient_email;
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
                basic.DispatchId,
                basic.ProjectId,
                basic.RoutingRuleId,
                basic.ScheduleId,
                basic.EventKey,
                basic.NotificationType,
                basic.AlertSeverity,
                basic.SourceModule,
                basic.SourceStatus,
                basic.Subject,
                basic.TextBody,
                basic.HtmlBody,
                basic.DeliveryBoundary,
                basic.ProviderSource,
                basic.DeliveryStatus,
                basic.ScheduledFor,
                basic.ReleasedAt,
                basic.ReleasedByUserId,
                basic.SentAt,
                basic.ProviderMessageId,
                basic.LastErrorCode,
                basic.LastErrorMessage,
                basic.Metadata,
                basic.CreatedAt,
                basic.UpdatedAt,
                recipients.ToArray(),
                basic.AttemptCount));
        }

        return rows;
    }

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
                entity_type,
                entity_id,
                action_code,
                actor_user_id,
                change_reason,
                prior_json,
                new_json,
                correlation_id
            )
            VALUES (
                @entity_type,
                @entity_id,
                @action_code,
                @actor_user_id,
                @change_reason,
                @prior_json::jsonb,
                @new_json::jsonb,
                @correlation_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("action_code", Limit(actionCode, 100));
        AddNullable(command, "actor_user_id", NpgsqlDbType.Uuid, actorUserId);
        command.Parameters.AddWithValue("change_reason", Limit(reason, 2000));
        command.Parameters.AddWithValue(
            "prior_json",
            prior is null ? "null" : JsonSerializer.Serialize(prior));
        command.Parameters.AddWithValue(
            "new_json",
            next is null ? "null" : JsonSerializer.Serialize(next));
        command.Parameters.AddWithValue("correlation_id", Limit(correlationId, 160));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value) => command.Parameters.Add(new NpgsqlParameter(name, type)
        {
            Value = value ?? DBNull.Value
        });

    private static string NormalizeSeverity(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "informational" or "warning" or "high" or "critical" =>
                value.Trim().ToLowerInvariant(),
            _ => "warning"
        };

    private static string NormalizeBoundary(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "test_only" or "production_governed" or "locked" =>
                value.Trim().ToLowerInvariant(),
            _ => "locked"
        };

    private static string NormalizeDispatchStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "preview_ready" or "held" or "queued" or "sending"
                or "sent" or "failed" or "suppressed" =>
                value.Trim().ToLowerInvariant(),
            _ => "preview_ready"
        };

    private static string RecipientType(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cc" => "cc",
            "bcc" => "bcc",
            _ => "to"
        };

    private static string Limit(string value, int max)
    {
        var clean = value ?? string.Empty;
        return clean.Length <= max ? clean : clean[..max];
    }

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

    internal sealed record AuthorizedConnection(
        NpgsqlConnection? Connection,
        ProjectNotificationActor? Actor,
        IResult? Failure)
    {
        internal static AuthorizedConnection Fail(IResult result) => new(null, null, result);
    }

    internal sealed record DeliveryAttemptView(
        Guid AttemptId,
        Guid DispatchId,
        int AttemptNumber,
        string ProviderSource,
        string ConfiguredProvider,
        string RecipientBoundary,
        string AttemptStatus,
        string ProviderMessageId,
        string DiagnosticCode,
        string DiagnosticMessage,
        DateTimeOffset AttemptedAt);

    internal sealed record DispatchDeliveryReconciliationResult(
        Guid DispatchId,
        bool Success,
        bool Found,
        string Status,
        string Message,
        DateTimeOffset? AvailableAt,
        bool ConfirmedSent)
    {
        internal static DispatchDeliveryReconciliationResult NotFound(Guid dispatchId) =>
            new(dispatchId, false, false, "notification_dispatch_not_found", "The notification dispatch was not found.", null, false);

        internal static DispatchDeliveryReconciliationResult NotSending(Guid dispatchId, string status) =>
            new(dispatchId, false, true, "notification_reconciliation_not_required", $"The dispatch is {status} and does not require outcome reconciliation.", null, false);

        internal static DispatchDeliveryReconciliationResult Waiting(Guid dispatchId, DateTimeOffset availableAt) =>
            new(dispatchId, false, true, "notification_reconciliation_waiting", "The delivery claim is still within its execution window. Wait for normal finalization before reconciling it.", availableAt, false);

        internal static DispatchDeliveryReconciliationResult Reconciled(Guid dispatchId, string status, bool confirmedSent) =>
            new(dispatchId, true, true, status, confirmedSent
                ? "Provider evidence was reconciled as sent. No resend is permitted."
                : "Provider evidence was reconciled as not sent. An authorized operator may now issue a separate retry.", null, confirmedSent);
    }

    private sealed record DispatchBasic(
        Guid DispatchId,
        Guid? ProjectId,
        Guid? RoutingRuleId,
        Guid? ScheduleId,
        string EventKey,
        string NotificationType,
        string AlertSeverity,
        string SourceModule,
        string SourceStatus,
        string Subject,
        string TextBody,
        string HtmlBody,
        string DeliveryBoundary,
        string ProviderSource,
        string DeliveryStatus,
        DateTimeOffset? ScheduledFor,
        DateTimeOffset? ReleasedAt,
        Guid? ReleasedByUserId,
        DateTimeOffset? SentAt,
        string ProviderMessageId,
        string LastErrorCode,
        string LastErrorMessage,
        JsonElement Metadata,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        int AttemptCount);
}

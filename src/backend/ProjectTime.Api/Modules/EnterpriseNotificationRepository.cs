using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseNotificationRepository
{
    internal const string MigrationId = "064_module_065_enterprise_notification_orchestration";

    internal static async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    internal static async Task<bool> IsReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.enterprise_notification_policies') IS NOT NULL
               AND to_regclass('public.enterprise_notification_events') IS NOT NULL
               AND EXISTS (
                    SELECT 1
                    FROM schema_migrations
                    WHERE migration_id = @migration_id
               );
            """, connection);
        command.Parameters.AddWithValue("migration_id", MigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    internal static async Task<EnterpriseNotificationPolicyRow[]> LoadPoliciesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<EnterpriseNotificationPolicyRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                enterprise_notification_policy_id,
                policy_code,
                policy_name,
                category,
                source_module,
                event_code,
                trigger_mode,
                recipient_strategy,
                trigger_configuration::text,
                recipient_configuration::text,
                severity,
                delivery_boundary,
                acknowledgement_required,
                acknowledgement_escalation_minutes,
                subject_template,
                text_template,
                owner_module,
                producer_contract,
                source_state,
                enabled,
                created_at,
                updated_at
            FROM enterprise_notification_policies
            ORDER BY category, source_module, policy_name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(ReadPolicy(reader));
        return rows.ToArray();
    }

    internal static async Task<EnterpriseNotificationPolicyRow?> LoadPolicyAsync(
        NpgsqlConnection connection,
        string policyCode,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                enterprise_notification_policy_id,
                policy_code,
                policy_name,
                category,
                source_module,
                event_code,
                trigger_mode,
                recipient_strategy,
                trigger_configuration::text,
                recipient_configuration::text,
                severity,
                delivery_boundary,
                acknowledgement_required,
                acknowledgement_escalation_minutes,
                subject_template,
                text_template,
                owner_module,
                producer_contract,
                source_state,
                enabled,
                created_at,
                updated_at
            FROM enterprise_notification_policies
            WHERE policy_code = @policy_code;
            """, connection);
        command.Parameters.AddWithValue("policy_code", NormalizeCode(policyCode));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicy(reader) : null;
    }

    internal static async Task<EnterpriseNotificationPolicyRow?> UpdatePolicyAsync(
        NpgsqlConnection connection,
        EnterpriseNotificationPolicyRow existing,
        EnterpriseNotificationPolicyUpdateRequest request,
        ProjectNotificationActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var enabled = request.Enabled ?? existing.Enabled;
        var boundary = NormalizeBoundary(request.DeliveryBoundary, existing.DeliveryBoundary);
        var severity = NormalizeSeverity(request.Severity, existing.Severity);
        var recipientStrategy = Clean(request.RecipientStrategy, 160, existing.RecipientStrategy);
        var triggerConfiguration = request.TriggerConfiguration.HasValue
            ? request.TriggerConfiguration.Value.GetRawText()
            : existing.TriggerConfiguration.GetRawText();
        var recipientConfiguration = request.RecipientConfiguration.HasValue
            ? request.RecipientConfiguration.Value.GetRawText()
            : existing.RecipientConfiguration.GetRawText();
        var subjectTemplate = Clean(request.SubjectTemplate, 1000, existing.SubjectTemplate);
        var textTemplate = Clean(request.TextTemplate, 12000, existing.TextTemplate);
        var acknowledgementRequired = request.AcknowledgementRequired ?? existing.AcknowledgementRequired;
        var escalationMinutes = acknowledgementRequired
            ? request.AcknowledgementEscalationMinutes ?? existing.AcknowledgementEscalationMinutes
            : null;
        if (escalationMinutes is < 1 or > 43200)
            throw new ArgumentOutOfRangeException(
                nameof(request.AcknowledgementEscalationMinutes),
                "Acknowledgement escalation must be between one minute and thirty days.");

        var reason = Clean(request.ChangeReason, 2000);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A change reason is required.", nameof(request.ChangeReason));

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var update = new NpgsqlCommand("""
                UPDATE enterprise_notification_policies
                SET enabled = @enabled,
                    delivery_boundary = @delivery_boundary,
                    severity = @severity,
                    recipient_strategy = @recipient_strategy,
                    trigger_configuration = @trigger_configuration::jsonb,
                    recipient_configuration = @recipient_configuration::jsonb,
                    subject_template = @subject_template,
                    text_template = @text_template,
                    acknowledgement_required = @ack_required,
                    acknowledgement_escalation_minutes = @ack_minutes,
                    updated_by_user_id = @actor_user_id,
                    updated_at = NOW()
                WHERE enterprise_notification_policy_id = @policy_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("enabled", enabled);
                update.Parameters.AddWithValue("delivery_boundary", boundary);
                update.Parameters.AddWithValue("severity", severity);
                update.Parameters.AddWithValue("recipient_strategy", recipientStrategy);
                update.Parameters.AddWithValue("trigger_configuration", triggerConfiguration);
                update.Parameters.AddWithValue("recipient_configuration", recipientConfiguration);
                update.Parameters.AddWithValue("subject_template", subjectTemplate);
                update.Parameters.AddWithValue("text_template", textTemplate);
                update.Parameters.AddWithValue("ack_required", acknowledgementRequired);
                update.Parameters.AddWithValue("ack_minutes", (object?)escalationMinutes ?? DBNull.Value);
                update.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                update.Parameters.AddWithValue("policy_id", existing.PolicyId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var audit = new NpgsqlCommand("""
                INSERT INTO enterprise_notification_policy_audit (
                    enterprise_notification_policy_id,
                    action_code,
                    actor_user_id,
                    change_reason,
                    prior_state,
                    new_state,
                    correlation_id
                )
                VALUES (
                    @policy_id,
                    'POLICY_UPDATED',
                    @actor_user_id,
                    @reason,
                    @prior_state::jsonb,
                    @new_state::jsonb,
                    @correlation_id
                );
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("policy_id", existing.PolicyId);
                audit.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                audit.Parameters.AddWithValue("reason", reason);
                audit.Parameters.AddWithValue("prior_state", JsonSerializer.Serialize(existing));
                audit.Parameters.AddWithValue("new_state", JsonSerializer.Serialize(new
                {
                    enabled,
                    deliveryBoundary = boundary,
                    severity,
                    recipientStrategy,
                    triggerConfiguration = JsonDocument.Parse(triggerConfiguration).RootElement,
                    recipientConfiguration = JsonDocument.Parse(recipientConfiguration).RootElement,
                    subjectTemplate,
                    textTemplate,
                    acknowledgementRequired,
                    acknowledgementEscalationMinutes = escalationMinutes
                }));
                audit.Parameters.AddWithValue("correlation_id", Clean(correlationId, 180));
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return await LoadPolicyAsync(connection, existing.PolicyCode, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<EnterpriseNotificationEventInsertResult> InsertEventAsync(
        NpgsqlConnection connection,
        string policyCode,
        string sourceModule,
        string sourceEventId,
        string idempotencyKey,
        string entityType,
        Guid? entityId,
        Guid? projectId,
        Guid? subjectUserId,
        DateTimeOffset occurredAt,
        DateTimeOffset availableAt,
        JsonElement payload,
        string ingestionSource,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedPolicy = NormalizeCode(policyCode);
        var normalizedSourceEvent = Clean(sourceEventId, 320);
        var normalizedIdempotency = Clean(idempotencyKey, 420);
        if (string.IsNullOrWhiteSpace(normalizedPolicy)
            || string.IsNullOrWhiteSpace(normalizedSourceEvent)
            || string.IsNullOrWhiteSpace(normalizedIdempotency))
        {
            throw new ArgumentException("Policy code, source event ID, and idempotency key are required.");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            Guid? eventId;
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO enterprise_notification_events (
                    enterprise_notification_event_id,
                    policy_code,
                    source_module,
                    source_event_id,
                    idempotency_key,
                    entity_type,
                    entity_id,
                    project_id,
                    subject_user_id,
                    occurred_at,
                    available_at,
                    payload,
                    ingestion_source,
                    event_status,
                    created_at,
                    updated_at
                )
                SELECT
                    gen_random_uuid(),
                    policy.policy_code,
                    @source_module,
                    @source_event_id,
                    @idempotency_key,
                    @entity_type,
                    @entity_id,
                    @project_id,
                    @subject_user_id,
                    @occurred_at,
                    @available_at,
                    @payload::jsonb,
                    @ingestion_source,
                    CASE WHEN policy.enabled THEN 'pending' ELSE 'suppressed' END,
                    NOW(),
                    NOW()
                FROM enterprise_notification_policies policy
                WHERE policy.policy_code = @policy_code
                ON CONFLICT (idempotency_key) DO NOTHING
                RETURNING enterprise_notification_event_id;
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("policy_code", normalizedPolicy);
                insert.Parameters.AddWithValue("source_module", Clean(sourceModule, 20));
                insert.Parameters.AddWithValue("source_event_id", normalizedSourceEvent);
                insert.Parameters.AddWithValue("idempotency_key", normalizedIdempotency);
                insert.Parameters.AddWithValue("entity_type", Clean(entityType, 120));
                AddNullableGuid(insert, "entity_id", entityId);
                AddNullableGuid(insert, "project_id", projectId);
                AddNullableGuid(insert, "subject_user_id", subjectUserId);
                insert.Parameters.AddWithValue("occurred_at", occurredAt.ToUniversalTime());
                insert.Parameters.AddWithValue("available_at", availableAt.ToUniversalTime());
                insert.Parameters.AddWithValue("payload", payload.ValueKind == JsonValueKind.Undefined ? "{}" : payload.GetRawText());
                insert.Parameters.AddWithValue("ingestion_source", NormalizeIngestionSource(ingestionSource));
                eventId = await insert.ExecuteScalarAsync(cancellationToken) as Guid?;
            }

            var created = eventId.HasValue;
            if (!created)
            {
                await using var existing = new NpgsqlCommand("""
                    SELECT enterprise_notification_event_id
                    FROM enterprise_notification_events
                    WHERE idempotency_key = @idempotency_key;
                    """, connection, transaction);
                existing.Parameters.AddWithValue("idempotency_key", normalizedIdempotency);
                var raw = await existing.ExecuteScalarAsync(cancellationToken);
                eventId = raw is Guid id ? id : null;
            }

            if (!eventId.HasValue)
                throw new InvalidOperationException(
                    "The enterprise notification policy was not found or the event could not be recorded.");

            if (created)
            {
                await InsertHistoryAsync(
                    connection,
                    transaction,
                    eventId.Value,
                    "EVENT_ACCEPTED",
                    "pending",
                    actorUserId,
                    string.Empty,
                    new
                    {
                        ingestionSource = NormalizeIngestionSource(ingestionSource),
                        sourceModule = Clean(sourceModule, 20),
                        sourceEventId = normalizedSourceEvent,
                        payloadContainsSecret = false
                    },
                    correlationId,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new(
                eventId.Value,
                created,
                created ? "event_accepted" : "duplicate_suppressed",
                created
                    ? "The enterprise notification event was recorded."
                    : "An event with the same idempotency key already exists.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<EnterpriseNotificationEventRow[]> ClaimDueEventsAsync(
        NpgsqlConnection connection,
        int maximum,
        CancellationToken cancellationToken)
    {
        var rows = new List<EnterpriseNotificationEventRow>();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                WITH selected AS (
                    SELECT event.enterprise_notification_event_id
                    FROM enterprise_notification_events event
                    JOIN enterprise_notification_policies policy
                      ON policy.policy_code = event.policy_code
                    WHERE event.event_status IN ('pending', 'failed')
                      AND event.available_at <= NOW()
                      AND event.attempt_count < 8
                      AND policy.enabled = TRUE
                    ORDER BY event.available_at, event.created_at
                    LIMIT @maximum
                    FOR UPDATE OF event SKIP LOCKED
                )
                UPDATE enterprise_notification_events event
                SET event_status = 'processing',
                    attempt_count = event.attempt_count + 1,
                    updated_at = NOW()
                FROM selected
                WHERE event.enterprise_notification_event_id = selected.enterprise_notification_event_id
                RETURNING
                    event.enterprise_notification_event_id,
                    event.policy_code,
                    event.source_module,
                    event.source_event_id,
                    event.idempotency_key,
                    event.entity_type,
                    event.entity_id,
                    event.project_id,
                    event.subject_user_id,
                    event.occurred_at,
                    event.available_at,
                    event.payload::text,
                    event.ingestion_source,
                    event.event_status,
                    event.dispatch_id,
                    event.attempt_count,
                    event.last_error_code,
                    event.last_error_message,
                    event.processed_at,
                    event.created_at,
                    event.updated_at;
                """, connection, transaction);
            command.Parameters.AddWithValue("maximum", Math.Clamp(maximum, 1, 250));
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadEvent(reader));
            }
            await transaction.CommitAsync(cancellationToken);
            return rows.ToArray();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task CompleteEventAsync(
        NpgsqlConnection connection,
        EnterpriseNotificationEventRow notificationEvent,
        string status,
        Guid? dispatchId,
        Guid? actorUserId,
        string diagnosticCode,
        string message,
        object metadata,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = status is "dispatched" or "suppressed" or "failed"
            ? status
            : "failed";
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var update = new NpgsqlCommand("""
                UPDATE enterprise_notification_events
                SET event_status = @status,
                    dispatch_id = @dispatch_id,
                    last_error_code = @diagnostic_code,
                    last_error_message = @message,
                    processed_at = CASE WHEN @status IN ('dispatched','suppressed') THEN NOW() ELSE processed_at END,
                    available_at = CASE
                        WHEN @status = 'failed' THEN NOW() + make_interval(mins => LEAST(1440, GREATEST(5, attempt_count * attempt_count * 5)))
                        ELSE available_at
                    END,
                    updated_at = NOW()
                WHERE enterprise_notification_event_id = @event_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("status", normalizedStatus);
                AddNullableGuid(update, "dispatch_id", dispatchId);
                update.Parameters.AddWithValue("diagnostic_code", Clean(diagnosticCode, 160));
                update.Parameters.AddWithValue("message", Clean(message, 4000));
                update.Parameters.AddWithValue("event_id", notificationEvent.EventId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertHistoryAsync(
                connection,
                transaction,
                notificationEvent.EventId,
                normalizedStatus switch
                {
                    "dispatched" => "EVENT_DISPATCHED",
                    "suppressed" => "EVENT_SUPPRESSED",
                    _ => "EVENT_FAILED"
                },
                normalizedStatus,
                actorUserId,
                diagnosticCode,
                metadata,
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

    internal static async Task<EnterpriseNotificationEventRow[]> LoadRecentEventsAsync(
        NpgsqlConnection connection,
        int maximum,
        CancellationToken cancellationToken)
    {
        var rows = new List<EnterpriseNotificationEventRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                enterprise_notification_event_id,
                policy_code,
                source_module,
                source_event_id,
                idempotency_key,
                entity_type,
                entity_id,
                project_id,
                subject_user_id,
                occurred_at,
                available_at,
                payload::text,
                ingestion_source,
                event_status,
                dispatch_id,
                attempt_count,
                last_error_code,
                last_error_message,
                processed_at,
                created_at,
                updated_at
            FROM enterprise_notification_events
            ORDER BY created_at DESC
            LIMIT @maximum;
            """, connection);
        command.Parameters.AddWithValue("maximum", Math.Clamp(maximum, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadEvent(reader));
        return rows.ToArray();
    }

    internal static async Task<EnterpriseNotificationInventoryRow[]> LoadInventoryAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<EnterpriseNotificationInventoryRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                inventory.policy_code,
                inventory.policy_name,
                inventory.category,
                inventory.source_module,
                inventory.event_code,
                inventory.trigger_mode,
                inventory.recipient_strategy,
                inventory.severity,
                inventory.delivery_boundary,
                inventory.acknowledgement_required,
                inventory.owner_module,
                inventory.producer_contract,
                inventory.source_state,
                inventory.enabled,
                inventory.delivery_authority,
                inventory.direct_smtp_authorized,
                inventory.direct_brevo_authorized,
                CASE
                    WHEN inventory.source_state = 'native_worker' THEN 'native_worker'
                    WHEN inventory.source_state = 'scanner' THEN 'authoritative_scanner'
                    WHEN inventory.source_state = 'signed_event' THEN 'signed_event_contract'
                    ELSE 'contract_ready'
                END AS runtime_coverage,
                CASE
                    WHEN inventory.source_state = 'native_worker' THEN 'An existing governed worker already uses Module 065.'
                    WHEN inventory.source_state = 'scanner' THEN 'The enterprise worker reads authoritative Pulse state.'
                    WHEN inventory.source_state = 'signed_event' THEN 'The producer posts a signed, idempotent event; Module 065 owns delivery.'
                    ELSE 'The policy is registered and remains fail-closed until its producer is connected.'
                END AS runtime_message
            FROM enterprise_notification_inventory inventory
            ORDER BY inventory.category, inventory.source_module, inventory.policy_name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetBoolean(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetBoolean(13),
                reader.GetString(14),
                reader.GetBoolean(15),
                reader.GetBoolean(16),
                reader.GetString(17),
                reader.GetString(18)));
        }
        return rows.ToArray();
    }

    internal static async Task<Guid> StartRunAsync(
        NpgsqlConnection connection,
        string runType,
        Guid? startedBy,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO enterprise_notification_run_history (
                enterprise_notification_run_history_id,
                run_type,
                started_by_user_id,
                run_status,
                correlation_id
            )
            VALUES (
                gen_random_uuid(),
                @run_type,
                @started_by,
                'running',
                @correlation_id
            )
            RETURNING enterprise_notification_run_history_id;
            """, connection);
        command.Parameters.AddWithValue("run_type", runType);
        AddNullableGuid(command, "started_by", startedBy);
        command.Parameters.AddWithValue("correlation_id", Clean(correlationId, 180));
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Unable to create enterprise notification run evidence."));
    }

    internal static async Task CompleteRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        EnterpriseNotificationRunSummary summary,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE enterprise_notification_run_history
            SET completed_at = @completed_at,
                run_status = @run_status,
                observed_count = @observed_count,
                created_count = @created_count,
                dispatched_count = @dispatched_count,
                queued_count = @queued_count,
                suppressed_count = @suppressed_count,
                failed_count = @failed_count,
                source_states = @source_states::jsonb,
                diagnostic_code = @diagnostic_code
            WHERE enterprise_notification_run_history_id = @run_id;
            """, connection);
        command.Parameters.AddWithValue("completed_at", summary.CompletedAt);
        command.Parameters.AddWithValue("run_status", summary.Status);
        command.Parameters.AddWithValue("observed_count", summary.ObservedCount);
        command.Parameters.AddWithValue("created_count", summary.CreatedCount);
        command.Parameters.AddWithValue("dispatched_count", summary.DispatchedCount);
        command.Parameters.AddWithValue("queued_count", summary.QueuedCount);
        command.Parameters.AddWithValue("suppressed_count", summary.SuppressedCount);
        command.Parameters.AddWithValue("failed_count", summary.FailedCount);
        command.Parameters.AddWithValue("source_states", JsonSerializer.Serialize(summary.Sources));
        command.Parameters.AddWithValue("diagnostic_code", summary.FailedCount > 0 ? "PARTIAL_DELIVERY_FAILURE" : string.Empty);
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task UpsertCheckpointAsync(
        NpgsqlConnection connection,
        EnterpriseNotificationSourceObservation observation,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO enterprise_notification_source_checkpoints (
                source_code,
                source_module,
                last_scan_started_at,
                last_scan_completed_at,
                last_successful_at,
                last_status,
                last_diagnostic_code,
                last_message,
                records_observed,
                events_created,
                updated_at
            )
            VALUES (
                @source_code,
                @source_module,
                @started_at,
                @completed_at,
                CASE WHEN @status = 'healthy' THEN @completed_at ELSE NULL END,
                @status,
                @diagnostic_code,
                @message,
                @records_observed,
                @events_created,
                NOW()
            )
            ON CONFLICT (source_code) DO UPDATE
            SET source_module = EXCLUDED.source_module,
                last_scan_started_at = EXCLUDED.last_scan_started_at,
                last_scan_completed_at = EXCLUDED.last_scan_completed_at,
                last_successful_at = CASE
                    WHEN EXCLUDED.last_status = 'healthy' THEN EXCLUDED.last_scan_completed_at
                    ELSE enterprise_notification_source_checkpoints.last_successful_at
                END,
                last_status = EXCLUDED.last_status,
                last_diagnostic_code = EXCLUDED.last_diagnostic_code,
                last_message = EXCLUDED.last_message,
                records_observed = EXCLUDED.records_observed,
                events_created = EXCLUDED.events_created,
                updated_at = NOW();
            """, connection);
        command.Parameters.AddWithValue("source_code", Clean(observation.SourceCode, 160));
        command.Parameters.AddWithValue("source_module", Clean(observation.SourceModule, 20));
        command.Parameters.AddWithValue("started_at", startedAt);
        command.Parameters.AddWithValue("completed_at", observation.ObservedAt);
        command.Parameters.AddWithValue("status", observation.Status);
        command.Parameters.AddWithValue("diagnostic_code", Clean(observation.DiagnosticCode, 160));
        command.Parameters.AddWithValue("message", Clean(observation.Message, 4000));
        command.Parameters.AddWithValue("records_observed", observation.RecordsObserved);
        command.Parameters.AddWithValue("events_created", observation.EventsCreated);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<object[]> LoadCheckpointsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                source_code,
                source_module,
                last_scan_started_at,
                last_scan_completed_at,
                last_successful_at,
                last_status,
                last_diagnostic_code,
                last_message,
                records_observed,
                events_created,
                updated_at
            FROM enterprise_notification_source_checkpoints
            ORDER BY source_code;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                sourceCode = reader.GetString(0),
                sourceModule = reader.GetString(1),
                lastScanStartedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2),
                lastScanCompletedAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3),
                lastSuccessfulAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4),
                status = reader.GetString(5),
                diagnosticCode = reader.GetString(6),
                message = reader.GetString(7),
                recordsObserved = reader.GetInt32(8),
                eventsCreated = reader.GetInt32(9),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(10)
            });
        }
        return rows.ToArray();
    }

    internal static async Task<bool> AcknowledgeAsync(
        NpgsqlConnection connection,
        Guid eventId,
        ProjectNotificationActor actor,
        string statement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var inserted = 0;
            await using (var command = new NpgsqlCommand("""
                INSERT INTO enterprise_notification_acknowledgements (
                    enterprise_notification_acknowledgement_id,
                    enterprise_notification_event_id,
                    user_id,
                    acknowledged_by_actual_user_id,
                    acknowledgement_statement,
                    acknowledged_at
                )
                SELECT
                    gen_random_uuid(),
                    event.enterprise_notification_event_id,
                    @effective_user_id,
                    @actual_user_id,
                    @statement,
                    NOW()
                FROM enterprise_notification_events event
                JOIN enterprise_notification_policies policy
                  ON policy.policy_code = event.policy_code
                WHERE event.enterprise_notification_event_id = @event_id
                  AND policy.acknowledgement_required = TRUE
                ON CONFLICT (enterprise_notification_event_id, user_id) DO NOTHING;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
                command.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
                command.Parameters.AddWithValue("statement", Clean(statement, 2000));
                command.Parameters.AddWithValue("event_id", eventId);
                inserted = await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (inserted > 0)
            {
                await InsertHistoryAsync(
                    connection,
                    transaction,
                    eventId,
                    "EVENT_ACKNOWLEDGED",
                    "dispatched",
                    actor.ActualUserId,
                    string.Empty,
                    new
                    {
                        acknowledgedUserId = actor.EffectiveUserId,
                        actor.IsViewAs,
                        acknowledgementContainsSecret = false
                    },
                    correlationId,
                    cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return inserted > 0;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<int> CountPendingAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM enterprise_notification_events
            WHERE event_status IN ('pending','failed')
              AND available_at <= NOW()
              AND attempt_count < 8;
            """, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    internal static async Task InsertHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        string historyCode,
        string eventStatus,
        Guid? actorUserId,
        string diagnosticCode,
        object metadata,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO enterprise_notification_event_history (
                enterprise_notification_event_history_id,
                enterprise_notification_event_id,
                history_code,
                event_status,
                actor_user_id,
                diagnostic_code,
                history_metadata,
                correlation_id,
                created_at
            )
            VALUES (
                gen_random_uuid(),
                @event_id,
                @history_code,
                @event_status,
                @actor_user_id,
                @diagnostic_code,
                @metadata::jsonb,
                @correlation_id,
                NOW()
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("history_code", Clean(historyCode, 120));
        command.Parameters.AddWithValue("event_status", Clean(eventStatus, 40));
        AddNullableGuid(command, "actor_user_id", actorUserId);
        command.Parameters.AddWithValue("diagnostic_code", Clean(diagnosticCode, 160));
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        command.Parameters.AddWithValue("correlation_id", Clean(correlationId, 180));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres when postgres.SqlState == PostgresErrorCodes.UndefinedTable => "MIGRATION_064_REQUIRED",
        PostgresException postgres when postgres.SqlState == PostgresErrorCodes.UndefinedColumn => "SOURCE_COLUMN_UNAVAILABLE",
        PostgresException postgres => $"POSTGRES_{postgres.SqlState}",
        TimeoutException => "DATABASE_TIMEOUT",
        OperationCanceledException => "OPERATION_CANCELLED",
        _ => exception.GetType().Name
    };

    private static EnterpriseNotificationPolicyRow ReadPolicy(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        ParseJson(reader.GetString(8)),
        ParseJson(reader.GetString(9)),
        reader.GetString(10),
        reader.GetString(11),
        reader.GetBoolean(12),
        reader.IsDBNull(13) ? null : reader.GetInt32(13),
        reader.GetString(14),
        reader.GetString(15),
        reader.GetString(16),
        reader.GetString(17),
        reader.GetString(18),
        reader.GetBoolean(19),
        reader.GetFieldValue<DateTimeOffset>(20),
        reader.GetFieldValue<DateTimeOffset>(21));

    private static EnterpriseNotificationEventRow ReadEvent(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6),
        reader.IsDBNull(7) ? null : reader.GetGuid(7),
        reader.IsDBNull(8) ? null : reader.GetGuid(8),
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.GetFieldValue<DateTimeOffset>(10),
        ParseJson(reader.GetString(11)),
        reader.GetString(12),
        reader.GetString(13),
        reader.IsDBNull(14) ? null : reader.GetGuid(14),
        reader.GetInt32(15),
        reader.GetString(16),
        reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
        reader.GetFieldValue<DateTimeOffset>(19),
        reader.GetFieldValue<DateTimeOffset>(20));

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });

    private static string NormalizeCode(string? value) =>
        Clean(value, 160).ToUpperInvariant().Replace('-', '_').Replace(' ', '_');

    private static string NormalizeBoundary(string? value, string fallback)
    {
        var normalized = Clean(value, 40).ToLowerInvariant();
        return normalized is "test_only" or "production_governed" or "locked"
            ? normalized
            : fallback;
    }

    private static string NormalizeSeverity(string? value, string fallback)
    {
        var normalized = Clean(value, 24).ToLowerInvariant();
        return normalized is "informational" or "warning" or "high" or "critical"
            ? normalized
            : fallback;
    }

    private static string NormalizeIngestionSource(string? value)
    {
        var normalized = Clean(value, 40).ToLowerInvariant();
        return normalized is "authoritative_scanner" or "signed_api" or "native_bridge" or "manual_preview"
            ? normalized
            : "signed_api";
    }

    internal static string Clean(string? value, int maximum, string fallback = "")
    {
        var clean = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clean)) clean = fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string ConnectionString()
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
        {
            throw new InvalidOperationException("Pulse database connection is not configured.");
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 20,
            Timeout = 15,
            CommandTimeout = 30
        }.ConnectionString;
    }
}

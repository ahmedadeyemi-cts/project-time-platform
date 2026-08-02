using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseNotificationOrchestrationService
{
    private const int MaximumEventsPerRun = 100;

    internal static async Task<EnterpriseNotificationRunSummary> RunAsync(
        HttpContext? context,
        Guid? startedByUserId,
        string runType,
        bool scanAuthoritativeSources,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var correlationId = context?.TraceIdentifier
            ?? $"enterprise-notification-{Guid.NewGuid():N}";
        await using var connection = await EnterpriseNotificationRepository.OpenConnectionAsync(cancellationToken);
        if (!await EnterpriseNotificationRepository.IsReadyAsync(connection, cancellationToken))
        {
            return new(
                Guid.Empty,
                "failed",
                0,
                0,
                0,
                0,
                0,
                1,
                [EnterpriseNotificationSourceObservation.Unavailable(
                    "enterprise_notification_orchestration",
                    "065",
                    "MIGRATION_064_REQUIRED",
                    "Apply migration 064 before running enterprise notification orchestration.")],
                Array.Empty<EnterpriseNotificationDispatchSummary>(),
                startedAt,
                DateTimeOffset.UtcNow,
                "Enterprise notification orchestration is not initialized.");
        }

        var runId = await EnterpriseNotificationRepository.StartRunAsync(
            connection,
            NormalizeRunType(runType),
            startedByUserId,
            correlationId,
            cancellationToken);
        var sourceObservations = scanAuthoritativeSources
            ? await EnterpriseNotificationSourceScanner.ScanAsync(
                connection,
                correlationId,
                cancellationToken)
            : Array.Empty<EnterpriseNotificationSourceObservation>();
        var created = sourceObservations.Sum(source => source.EventsCreated);
        var observed = sourceObservations.Sum(source => source.RecordsObserved);
        var claimed = await EnterpriseNotificationRepository.ClaimDueEventsAsync(
            connection,
            Math.Clamp(maximumEvents, 1, MaximumEventsPerRun),
            cancellationToken);
        var dispatches = new List<EnterpriseNotificationDispatchSummary>();
        foreach (var notificationEvent in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dispatches.Add(await ProcessEventAsync(
                connection,
                notificationEvent,
                context,
                startedByUserId,
                correlationId,
                cancellationToken));
        }

        var dispatchedCount = dispatches.Count(item => item.Status is "sent" or "dispatched");
        var queuedCount = dispatches.Count(item => item.Status == "queued");
        var suppressedCount = dispatches.Count(item => item.Status == "suppressed");
        var failedCount = dispatches.Count(item => item.Status == "failed");
        var unavailableSourceCount = sourceObservations.Count(source => source.Status != "healthy");
        var status = failedCount > 0
            ? "partial"
            : unavailableSourceCount > 0
                ? "partial"
                : "completed";
        var completedAt = DateTimeOffset.UtcNow;
        var summary = new EnterpriseNotificationRunSummary(
            runId,
            status,
            observed,
            created,
            dispatchedCount,
            queuedCount,
            suppressedCount,
            failedCount,
            sourceObservations,
            dispatches.ToArray(),
            startedAt,
            completedAt,
            $"Observed {observed} source record(s), created {created} idempotent event(s), and processed {dispatches.Count} due event(s). No direct SMTP or Brevo path was used.");
        await EnterpriseNotificationRepository.CompleteRunAsync(
            connection,
            runId,
            summary,
            cancellationToken);
        return summary;
    }

    internal static async Task<EnterpriseNotificationDispatchSummary> ProcessEventAsync(
        NpgsqlConnection connection,
        EnterpriseNotificationEventRow notificationEvent,
        HttpContext? context,
        Guid? releasedByUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var policy = await EnterpriseNotificationRepository.LoadPolicyAsync(
            connection,
            notificationEvent.PolicyCode,
            cancellationToken);
        if (policy is null)
        {
            const string message = "The enterprise notification policy no longer exists.";
            await EnterpriseNotificationRepository.CompleteEventAsync(
                connection,
                notificationEvent,
                "failed",
                null,
                releasedByUserId,
                "POLICY_NOT_FOUND",
                message,
                new { notificationEvent.PolicyCode },
                correlationId,
                cancellationToken);
            return new(
                notificationEvent.EventId,
                null,
                notificationEvent.PolicyCode,
                "failed",
                "module_065",
                "locked",
                0,
                "POLICY_NOT_FOUND",
                message);
        }

        if (EnterpriseNotificationTemplateRenderer.PayloadContainsProhibitedMaterial(notificationEvent.Payload))
        {
            const string message = "The notification payload contained a prohibited credential-like field and was suppressed before recipient resolution.";
            await EnterpriseNotificationRepository.CompleteEventAsync(
                connection,
                notificationEvent,
                "suppressed",
                null,
                releasedByUserId,
                "PROHIBITED_PAYLOAD_FIELD",
                message,
                new { payloadStored = true, payloadReturned = false, credentialValueLogged = false },
                correlationId,
                cancellationToken);
            return new(
                notificationEvent.EventId,
                null,
                policy.PolicyCode,
                "suppressed",
                "module_065",
                "locked",
                0,
                "PROHIBITED_PAYLOAD_FIELD",
                message);
        }

        if (!policy.Enabled)
        {
            const string message = "The notification policy is disabled.";
            await EnterpriseNotificationRepository.CompleteEventAsync(
                connection,
                notificationEvent,
                "suppressed",
                null,
                releasedByUserId,
                "POLICY_DISABLED",
                message,
                new { policy.Enabled },
                correlationId,
                cancellationToken);
            return new(
                notificationEvent.EventId,
                null,
                policy.PolicyCode,
                "suppressed",
                "module_065",
                policy.DeliveryBoundary,
                0,
                "POLICY_DISABLED",
                message);
        }

        var recipientResolution = await EnterpriseNotificationRecipientResolver.ResolveAsync(
            connection,
            policy,
            notificationEvent,
            cancellationToken);
        var template = await EnterpriseNotificationTemplateRenderer.RenderAsync(
            connection,
            policy,
            notificationEvent,
            cancellationToken);
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);
        var boundary = EffectiveBoundary(policy.DeliveryBoundary, readiness.RecipientBoundary);
        var project = await LoadMinimalProjectSnapshotAsync(
            connection,
            notificationEvent.ProjectId,
            cancellationToken);
        var initialStatus = recipientResolution.Recipients.Length == 0
            ? "suppressed"
            : boundary == "locked"
                ? "held"
                : "queued";
        var eventKey = DispatchEventKey(notificationEvent);
        var dispatchId = await ProjectNotificationRepository.UpsertDispatchAsync(
            connection,
            null,
            null,
            project,
            eventKey,
            policy.PolicyCode.ToLowerInvariant(),
            policy.Severity,
            notificationEvent.SourceModule,
            notificationEvent.EventStatus,
            template.Subject,
            template.TextBody,
            template.HtmlBody,
            boundary,
            initialStatus,
            recipientResolution.Recipients,
            new
            {
                enterpriseNotificationEventId = notificationEvent.EventId,
                policy.PolicyCode,
                policy.EventCode,
                policy.TriggerMode,
                policy.ProducerContract,
                policy.SourceState,
                recipientResolution.Evidence,
                recipientResolution.DiagnosticCode,
                payloadContainsCredential = false,
                deliveryAuthority = "module_065",
                directSmtpAuthorized = false,
                directBrevoAuthorized = false
            },
            cancellationToken);

        var delivery = DeliveryDecision(
            policy,
            readiness,
            boundary,
            recipientResolution,
            template,
            notificationEvent,
            context,
            cancellationToken);
        var deliveryResult = await delivery;
        var dispatch = await ProjectNotificationRepository.LoadDispatchAsync(
            connection,
            dispatchId,
            cancellationToken);
        if (dispatch is not null)
        {
            await ProjectNotificationRepository.RecordDeliveryAsync(
                connection,
                dispatch,
                deliveryResult,
                releasedByUserId,
                $"Enterprise policy {policy.PolicyCode} processed through Module 065.",
                correlationId,
                cancellationToken);
        }

        var eventStatus = deliveryResult.Status == "failed"
            ? "failed"
            : deliveryResult.Status == "suppressed"
                ? "suppressed"
                : "dispatched";
        await EnterpriseNotificationRepository.CompleteEventAsync(
            connection,
            notificationEvent,
            eventStatus,
            dispatchId,
            releasedByUserId,
            deliveryResult.DiagnosticCode,
            deliveryResult.Message,
            new
            {
                dispatchId,
                deliveryResult.Sent,
                deliveryResult.Status,
                deliveryResult.Provider,
                deliveryResult.RecipientBoundary,
                recipientCount = recipientResolution.Recipients.Length,
                deliveryAuthority = "module_065",
                credentialValueLogged = false
            },
            correlationId,
            cancellationToken);

        var summaryStatus = deliveryResult.Sent
            ? "sent"
            : deliveryResult.Status;
        return new(
            notificationEvent.EventId,
            dispatchId,
            policy.PolicyCode,
            summaryStatus,
            deliveryResult.Provider,
            deliveryResult.RecipientBoundary,
            recipientResolution.Recipients.Length,
            deliveryResult.DiagnosticCode,
            deliveryResult.Message);
    }

    internal static async Task<object> QueueExpenseUploadAsync(
        NpgsqlConnection connection,
        Guid uploadId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (!await EnterpriseNotificationRepository.IsReadyAsync(connection, cancellationToken))
        {
            await UpdateExpenseCompatibilityAsync(
                connection,
                uploadId,
                "configuration_pending",
                "Migration 064 is required before Module 065 enterprise delivery can process the expense notification.",
                cancellationToken);
            return new
            {
                status = "configuration_pending",
                provider = "module_065",
                message = "Enterprise notification orchestration is not initialized."
            };
        }

        ExpenseBridgeSource? source = null;
        await using (var command = new NpgsqlCommand("""
            SELECT
                upload.project_expense_upload_id,
                upload.project_id,
                upload.expense_owner_user_id,
                upload.uploaded_by_user_id,
                upload.project_code,
                upload.project_name,
                upload.line_count,
                upload.total_amount,
                upload.reimbursable_amount,
                upload.currency,
                upload.uploaded_at,
                COALESCE(owner.display_name, owner.email, 'Expense owner')
            FROM project_expense_uploads upload
            JOIN app_users owner ON owner.user_id = upload.expense_owner_user_id
            WHERE upload.project_expense_upload_id = @upload_id;
            """, connection))
        {
            command.Parameters.AddWithValue("upload_id", uploadId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                source = new(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.GetString(9),
                    reader.GetFieldValue<DateTimeOffset>(10),
                    reader.GetString(11));
            }
        }

        if (source is null)
            return new { status = "not_queued", provider = "module_065", message = "The expense upload was not found." };

        var correlationId = $"expense-upload-{uploadId:N}";
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["uploadId"] = source.UploadId,
            ["projectId"] = source.ProjectId,
            ["expenseOwnerUserId"] = source.OwnerUserId,
            ["uploadedByUserId"] = source.UploadedByUserId,
            ["recipientName"] = source.OwnerName,
            ["projectCode"] = source.ProjectCode,
            ["projectName"] = source.ProjectName,
            ["lineCount"] = source.LineCount,
            ["totalAmount"] = $"{source.TotalAmount:0.00} {source.Currency}",
            ["reimbursableAmount"] = $"{source.ReimbursableAmount:0.00} {source.Currency}",
            ["deepLink"] = "#project-allocation-info",
            ["correlationId"] = correlationId
        });
        var ownerEvent = await EnterpriseNotificationRepository.InsertEventAsync(
            connection,
            "EXPENSE_UPLOAD_CONFIRMATION",
            "005",
            $"expense-upload:{uploadId:N}:owner-confirmation",
            $"enterprise:expense:upload-confirmation:{uploadId:N}",
            "project_expense_upload",
            uploadId,
            source.ProjectId,
            source.OwnerUserId,
            source.UploadedAt,
            DateTimeOffset.UtcNow,
            payload,
            "native_bridge",
            actorUserId,
            correlationId,
            cancellationToken);
        var pmEvent = await EnterpriseNotificationRepository.InsertEventAsync(
            connection,
            "EXPENSE_PM_REVIEW_REQUEST",
            "005",
            $"expense-upload:{uploadId:N}:pm-review",
            $"enterprise:expense:pm-review:{uploadId:N}",
            "project_expense_upload",
            uploadId,
            source.ProjectId,
            source.OwnerUserId,
            source.UploadedAt,
            DateTimeOffset.UtcNow,
            payload,
            "native_bridge",
            actorUserId,
            correlationId,
            cancellationToken);

        await UpdateExpenseCompatibilityAsync(
            connection,
            uploadId,
            "queued",
            "Expense notifications were accepted by Module 065 enterprise orchestration. Module 032 contains delivery evidence.",
            cancellationToken);
        var run = await RunAsync(
            null,
            actorUserId,
            "signed_event",
            false,
            50,
            cancellationToken);
        return new
        {
            status = run.FailedCount > 0 ? "queued" : "processed",
            provider = "module_065",
            ownerEventId = ownerEvent.EventId,
            projectManagementEventId = pmEvent.EventId,
            ownerEvent.Created,
            projectManagementEventCreated = pmEvent.Created,
            run.RunId,
            run.DispatchedCount,
            run.QueuedCount,
            run.SuppressedCount,
            run.FailedCount,
            message = "Expense notifications are governed by Module 065; the legacy direct Graph, Brevo, and SMTP paths are not used."
        };
    }

    private static async Task<Module065MailDeliveryResult> DeliveryDecision(
        EnterpriseNotificationPolicyRow policy,
        Module065MailReadiness readiness,
        string effectiveBoundary,
        EnterpriseNotificationRecipientResolution recipientResolution,
        EnterpriseNotificationTemplate template,
        EnterpriseNotificationEventRow notificationEvent,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        if (recipientResolution.Recipients.Length == 0)
        {
            return new(
                false,
                "suppressed",
                readiness.ConfiguredProvider,
                effectiveBoundary,
                string.Empty,
                recipientResolution.DiagnosticCode,
                recipientResolution.Message);
        }

        if (policy.DeliveryBoundary == "locked" || effectiveBoundary == "locked")
        {
            return new(
                false,
                "suppressed",
                readiness.ConfiguredProvider,
                "locked",
                string.Empty,
                "POLICY_DELIVERY_LOCKED",
                "The enterprise notification policy or Module 065 delivery boundary is locked. The dispatch remains recorded inside ProjectPulse.");
        }

        if (policy.DeliveryBoundary == "test_only")
        {
            return new(
                false,
                "queued",
                readiness.ConfiguredProvider,
                "test_only",
                string.Empty,
                "POLICY_TEST_ONLY",
                "The enterprise notification policy is Test-only. The dispatch is recorded in Module 032 and cannot leave ProjectPulse.");
        }

        return await Module065ProjectNotificationDelivery.DeliverAsync(
            template.Subject,
            template.TextBody,
            template.HtmlBody,
            recipientResolution.Recipients,
            context,
            cancellationToken);
    }

    private static async Task<ProjectNotificationFinancialSnapshot?> LoadMinimalProjectSnapshotAsync(
        NpgsqlConnection connection,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (!projectId.HasValue) return null;
        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(to_jsonb(project)->>'customer_name', ''),
                COALESCE(to_jsonb(project)->>'project_code', ''),
                COALESCE(to_jsonb(project)->>'project_name', ''),
                COALESCE(to_jsonb(project)->>'status', ''),
                COALESCE(to_jsonb(project)->>'contract_type', '')
            FROM projects project
            WHERE project.project_id = @project_id;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            projectId.Value,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            null,
            null,
            null,
            null,
            Array.Empty<ProjectNotificationEngineer>(),
            null,
            null,
            null,
            0,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            "not_evaluated",
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
    }

    private static async Task UpdateExpenseCompatibilityAsync(
        NpgsqlConnection connection,
        Guid uploadId,
        string status,
        string detail,
        CancellationToken cancellationToken)
    {
        var legacyStatus = status is "sent" or "queued" or "configuration_pending" or "failed"
            ? status
            : "queued";
        await using var command = new NpgsqlCommand("""
            UPDATE project_expense_uploads
            SET notification_status = @status,
                notification_detail = @detail
            WHERE project_expense_upload_id = @upload_id;

            UPDATE project_expense_mail_outbox
            SET provider_source = 'module_065_enterprise_orchestration',
                delivery_status = CASE
                    WHEN delivery_status = 'sent' THEN delivery_status
                    ELSE @status
                END,
                last_error = @detail,
                updated_at = NOW()
            WHERE project_expense_upload_id = @upload_id;
            """, connection);
        command.Parameters.AddWithValue("status", legacyStatus);
        command.Parameters.AddWithValue("detail", EnterpriseNotificationRepository.Clean(detail, 2000));
        command.Parameters.AddWithValue("upload_id", uploadId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EffectiveBoundary(string policyBoundary, string moduleBoundary)
    {
        if (policyBoundary == "locked" || moduleBoundary == "locked") return "locked";
        if (policyBoundary != "production_governed" || moduleBoundary != "production_governed")
            return "test_only";
        return "production_governed";
    }

    private static string DispatchEventKey(EnterpriseNotificationEventRow notificationEvent)
    {
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(notificationEvent.IdempotencyKey)))
            .ToLowerInvariant();
        return $"enterprise:{notificationEvent.PolicyCode.ToLowerInvariant()}:{digest}";
    }

    private static string NormalizeRunType(string value) => value switch
    {
        "manual_run" => "manual_run",
        "signed_event" => "signed_event",
        "preview" => "preview",
        _ => "scheduled_worker"
    };

    private sealed record ExpenseBridgeSource(
        Guid UploadId,
        Guid ProjectId,
        Guid OwnerUserId,
        Guid UploadedByUserId,
        string ProjectCode,
        string ProjectName,
        int LineCount,
        decimal TotalAmount,
        decimal ReimbursableAmount,
        string Currency,
        DateTimeOffset UploadedAt,
        string OwnerName);
}
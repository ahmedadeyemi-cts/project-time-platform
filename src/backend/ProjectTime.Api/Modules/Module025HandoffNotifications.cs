using System.Net.Mail;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class Module025SowGsdModule
{
    private const string HandoffNotificationMigrationId = "120_module025_handoff_notifications";
    private sealed record Module025HandoffNotificationResult(string Status, Guid? EventId, string Message);

    private static (string Policy, string Label, string AuditType) HandoffNotificationKind(string kind) => kind switch
    {
        "ownership_transferred" => ("MODULE025_HANDOFF", "SOW/GSD transferred", "ownership_transferred"),
        "coverage_started" => ("MODULE025_COVERAGE_STARTED", "Temporary SOW/GSD coverage started", "ownership_transferred"),
        "coverage_returned" => ("MODULE025_COVERAGE_RETURNED", "SOW/GSD coverage returned", "ownership_transferred"),
        "coverage_acknowledged" => ("MODULE025_HANDOFF_ACKNOWLEDGED", "SOW/GSD handoff acknowledged", "handoff_acknowledged"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static async Task<bool> HandoffNotificationsReadyAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration);", connection, transaction);
        command.Parameters.AddWithValue("migration", HandoffNotificationMigrationId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    // Intentionally takes the caller's transaction. A configured queue failure
    // rolls back the ownership operation; network delivery never runs here.
    private static async Task<Module025HandoffNotificationResult> QueueHandoffNotificationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid engagementId, int nextRevision,
        string notificationKind, Guid newOwnerUserId, CancellationToken cancellationToken)
    {
        var kind = HandoffNotificationKind(notificationKind);
        if (!await HandoffNotificationsReadyAsync(connection, transaction, cancellationToken))
            return new("unconfigured", null, "Handoff recorded. Notifications require the Module 065 handoff policy migration.");

        const string sql = """
            WITH source AS (
              SELECT audit.event_id,audit.actor_user_id,audit.created_at,engagement.engagement_number,
                engagement.owner_display_name
              FROM module025_sow_gsd_events audit
              JOIN module025_sow_gsd_engagements engagement USING(engagement_id)
              WHERE audit.engagement_id=@id AND audit.engagement_revision=@revision
                AND audit.event_type=@audit_type AND engagement.owner_user_id=@owner
              ORDER BY audit.event_id DESC LIMIT 1
            ), inserted AS (
              INSERT INTO enterprise_notification_events(
                policy_code,source_module,source_event_id,idempotency_key,entity_type,entity_id,
                subject_user_id,occurred_at,available_at,payload,ingestion_source,event_status)
              SELECT policy.policy_code,'025',source.event_id::text,
                'module025:handoff:'||source.event_id::text||':'||@kind,
                'module025_sow_gsd',@id,@owner,source.created_at,NOW(),
                jsonb_build_object('kind',@kind,'notificationLabel',@label,
                  'sourceAuditEventId',source.event_id::text,'engagementNumber',source.engagement_number,
                  'ownerDisplayName',source.owner_display_name,'deepLink','#sow-generator'),
                'native_bridge',CASE WHEN policy.enabled THEN 'pending' ELSE 'suppressed' END
              FROM source JOIN enterprise_notification_policies policy ON policy.policy_code=@policy
              ON CONFLICT(idempotency_key) DO NOTHING
              RETURNING enterprise_notification_event_id,event_status
            ), history AS (
              INSERT INTO enterprise_notification_event_history(
                enterprise_notification_event_id,history_code,event_status,actor_user_id,history_metadata)
              SELECT inserted.enterprise_notification_event_id,'EVENT_ACCEPTED',inserted.event_status,
                source.actor_user_id,jsonb_build_object('sourceModule','025','sourceAuditEventId',source.event_id,
                  'deliveryAuthority','module_065','providerInvoked',false)
              FROM inserted CROSS JOIN source
            )
            SELECT enterprise_notification_event_id,event_status FROM inserted
            UNION ALL
            SELECT event.enterprise_notification_event_id,event.event_status
              FROM enterprise_notification_events event CROSS JOIN source
              WHERE event.idempotency_key='module025:handoff:'||source.event_id::text||':'||@kind
                AND NOT EXISTS(SELECT 1 FROM inserted);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", engagementId);
        command.Parameters.AddWithValue("revision", nextRevision);
        command.Parameters.AddWithValue("audit_type", kind.AuditType);
        command.Parameters.AddWithValue("owner", newOwnerUserId);
        command.Parameters.AddWithValue("kind", notificationKind);
        command.Parameters.AddWithValue("label", kind.Label);
        command.Parameters.AddWithValue("policy", kind.Policy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The handoff notification could not be recorded against its authoritative source event.");
        var eventId = reader.GetGuid(0);
        var suppressed = reader.GetString(1) == "suppressed";
        return new(suppressed ? "suppressed" : "queued", eventId, suppressed
            ? "Handoff recorded. Its notification policy is disabled in Module 065."
            : "Handoff notification queued through Module 065; delivery has not yet been confirmed.");
    }

    private static async Task<IResult> HandoffNotificationStatusAsync(Guid engagementId, HttpContext context,
        CancellationToken cancellationToken)
    {
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        var configured = await HandoffNotificationsReadyAsync(connection, null, cancellationToken);
        var items = new List<object>();
        if (configured)
        {
            await using var command = new NpgsqlCommand("""
                SELECT event.enterprise_notification_event_id,COALESCE(event.payload->>'kind',''),
                  event.event_status,COALESCE(dispatch.delivery_status,''),event.last_error_code,
                  event.occurred_at,event.processed_at,COALESCE(dispatch.delivery_boundary,policy.delivery_boundary)
                FROM enterprise_notification_events event
                JOIN enterprise_notification_policies policy USING(policy_code)
                LEFT JOIN project_notification_dispatches dispatch
                  ON dispatch.project_notification_dispatch_id=event.dispatch_id
                WHERE event.entity_id=@id AND event.entity_type='module025_sow_gsd'
                  AND event.source_module='025' AND policy.producer_contract='module025-handoff-v1'
                ORDER BY event.occurred_at DESC,event.created_at DESC LIMIT 20;
                """, connection);
            command.Parameters.AddWithValue("id", engagementId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var dispatchStatus = reader.GetString(3);
                items.Add(new { eventId=reader.GetGuid(0),kind=reader.GetString(1),
                    status=HandoffDeliveryStatus(reader.GetString(2),dispatchStatus),dispatchStatus,
                    diagnosticCode=reader.GetString(4),occurredAt=reader.GetFieldValue<DateTimeOffset>(5),
                    processedAt=reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
                    deliveryBoundary=reader.GetString(7) });
            }
        }
        return Results.Ok(new { engagementId,configured,status=!configured ? "unconfigured" : items.Count==0 ? "empty" : "ready",
            deliveryAuthority="module_065",message=configured ? "Delivery follows Module 065 policy and configured transports."
                : "Handoff notifications are awaiting migration 120.",items });
    }

    internal static string HandoffDeliveryStatus(string eventStatus, string dispatchStatus) =>
        !string.IsNullOrWhiteSpace(dispatchStatus) ? dispatchStatus
        : eventStatus == "pending" ? "queued" : eventStatus == "dispatched" ? "recorded" : eventStatus;

    internal sealed record HandoffDispatchPreflight(bool Current, string Boundary, string DiagnosticCode);

    // Covers Module 032 manual release/retry as well as the automatic worker.
    // Stored recipients are evidence, not continuing authorization to email them.
    internal static async Task<HandoffDispatchPreflight> ValidateHandoffDispatchAsync(
        NpgsqlConnection connection, ProjectNotificationDispatchRow dispatch, CancellationToken cancellationToken)
    {
        if (dispatch.SourceModule != "025" || dispatch.NotificationType is not
            ("module025_handoff" or "module025_coverage_started" or "module025_coverage_returned" or "module025_handoff_acknowledged"))
            return new(true,dispatch.DeliveryBoundary,"");
        try
        {
            if (dispatch.Metadata.ValueKind != JsonValueKind.Object
                || !dispatch.Metadata.TryGetProperty("enterpriseNotificationEventId",out var idValue)
                || idValue.ValueKind != JsonValueKind.String || !Guid.TryParse(idValue.GetString(),out var id))
                return new(false,"locked","MODULE025_HANDOFF_SOURCE_INVALID");
            var notification=await EnterpriseNotificationRepository.LoadEventAsync(connection,id,cancellationToken);
            if (notification is null || notification.PolicyCode.ToLowerInvariant()!=dispatch.NotificationType)
                return new(false,"locked","MODULE025_HANDOFF_SOURCE_INVALID");
            var policy=await EnterpriseNotificationRepository.LoadPolicyAsync(connection,notification.PolicyCode,cancellationToken);
            if (policy is null || policy.ProducerContract!="module025-handoff-v1" || !policy.Enabled)
                return new(false,"locked","MODULE025_HANDOFF_POLICY_DISABLED");
            var resolution=await ResolveHandoffNotificationRecipientsAsync(connection,notification,cancellationToken);
            static string Identity(ProjectNotificationUser user) =>
                $"{user.UserId:D}|{user.Email.Trim().ToLowerInvariant()}|{user.RecipientType}";
            var expected=resolution.Recipients.Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var stored=dispatch.Recipients.Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return expected.Count>0 && expected.SetEquals(stored)
                ? new(true,policy.DeliveryBoundary,"")
                : new(false,policy.DeliveryBoundary,"MODULE025_HANDOFF_RECIPIENTS_CHANGED");
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { throw; }
        catch(Exception)
        {
            return new(false,"locked","MODULE025_HANDOFF_SOURCE_UNAVAILABLE");
        }
    }

    // Only a native audited producer can use this strategy. Ignore payload email
    // addresses and user-ID overrides; resolve current ownership and reporting
    // relationships afresh so an old event cannot notify a stale owner/team.
    internal static async Task<EnterpriseNotificationRecipientResolution> ResolveHandoffNotificationRecipientsAsync(
        NpgsqlConnection connection, EnterpriseNotificationEventRow notificationEvent, CancellationToken cancellationToken)
    {
        EnterpriseNotificationRecipientResolution Suppress(string code) => new([],"suppressed",code,
            "The current SOW/GSD handoff recipients could not be verified.",[]);
        if (notificationEvent.IngestionSource != "native_bridge" || notificationEvent.SourceModule != "025"
            || notificationEvent.EntityType != "module025_sow_gsd" || !notificationEvent.EntityId.HasValue
            || !notificationEvent.SubjectUserId.HasValue
            || !long.TryParse(EnterpriseNotificationRecipientResolver.PayloadString(notificationEvent.Payload,"sourceAuditEventId"),out var auditId))
            return Suppress("MODULE025_HANDOFF_SOURCE_INVALID");
        const string sql = """
            WITH owner AS (
              SELECT person.user_id,COALESCE(NULLIF(person.display_name,''),person.email) display_name,person.email
              FROM module025_sow_gsd_engagements engagement
              JOIN app_users person ON person.user_id=engagement.owner_user_id AND person.is_active=TRUE
              JOIN module025_sow_gsd_events audit ON audit.engagement_id=engagement.engagement_id
              WHERE engagement.engagement_id=@id AND engagement.owner_user_id=@owner AND engagement.is_active=TRUE
                AND EXISTS(SELECT 1 FROM app_user_role_assignments assignment
                  JOIN app_roles role ON role.app_role_id=assignment.app_role_id
                  WHERE assignment.user_id=person.user_id AND assignment.is_active=TRUE AND role.is_active=TRUE
                    AND upper(role.role_code)=ANY(@sa_roles))
                AND audit.event_id=@audit_id AND audit.event_id::text=@source_event_id
                AND ((audit.event_type='ownership_transferred' AND audit.evidence_json->>'newOwnerUserId'=@owner::text)
                  OR (audit.event_type='handoff_acknowledged' AND audit.actor_user_id=@owner))
            )
            SELECT user_id,display_name,email,'SOLUTION_ARCHITECT' role,'to' recipient_type FROM owner
            UNION ALL
            SELECT DISTINCT manager.user_id,COALESCE(NULLIF(manager.display_name,''),manager.email),manager.email,'MANAGER','cc'
              FROM owner JOIN reporting_relationships relationship ON relationship.employee_user_id=owner.user_id
              JOIN app_users manager ON manager.user_id=relationship.manager_user_id AND manager.is_active=TRUE
              WHERE relationship.effective_start_date<=CURRENT_DATE
                AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                AND EXISTS(SELECT 1 FROM app_user_role_assignments assignment
                  JOIN app_roles role ON role.app_role_id=assignment.app_role_id
                  WHERE assignment.user_id=manager.user_id AND assignment.is_active=TRUE AND role.is_active=TRUE
                    AND upper(role.role_code)=ANY(@view_roles));
            """;
        await using var command = new NpgsqlCommand(sql,connection);
        command.Parameters.AddWithValue("id",notificationEvent.EntityId.Value);
        command.Parameters.AddWithValue("owner",notificationEvent.SubjectUserId.Value);
        command.Parameters.AddWithValue("audit_id",auditId);
        command.Parameters.AddWithValue("source_event_id",notificationEvent.SourceEventId);
        command.Parameters.AddWithValue("sa_roles",SolutionArchitectRoles.ToArray());
        command.Parameters.AddWithValue("view_roles",ViewRoles);
        var recipients = new List<ProjectNotificationUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var email=reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
            if (!MailAddress.TryCreate(email,out var address) || !string.Equals(address.Address,email,StringComparison.OrdinalIgnoreCase)) continue;
            recipients.Add(new(reader.GetGuid(0),reader.GetString(1),email.ToLowerInvariant(),reader.GetString(3),
                "module025.current_owner.active_reporting_relationship",reader.GetString(4)));
        }
        var unique=recipients.GroupBy(person=>person.Email,StringComparer.OrdinalIgnoreCase)
            .Select(group=>group.FirstOrDefault(person=>person.RecipientType=="to") ?? group.First()).ToArray();
        return unique.Length==0 ? Suppress("MODULE025_HANDOFF_STALE_OR_NO_RECIPIENTS")
            : new(unique,"resolved","","Current owner and active reporting managers resolved server-side.",
                ["module025.current_owner","reporting_relationships.active_manager"]);
    }
}

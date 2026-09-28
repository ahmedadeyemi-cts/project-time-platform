using System.Text.Json;
using Npgsql;
using static ProjectTime.Api.Modules.Module065NotificationParityPolicy;

namespace ProjectTime.Api.Modules;

internal static class Module065NotificationParityAuthorization
{
    internal sealed record Decision(bool Allowed, bool Defer, string Boundary, string Code);
    internal static async Task<Decision> ValidateAsync(NpgsqlConnection connection,
        ProjectNotificationDispatchRow snapshot, string email, string environment, CancellationToken token)
    {
        var recipient = snapshot.Recipients.FirstOrDefault(r => r.Email.Trim().Equals(email, StringComparison.OrdinalIgnoreCase));
        if (recipient is null) return Denied("TEAMS_RECIPIENT_NOT_IN_EMAIL_SNAPSHOT");
        await using (var user = new NpgsqlCommand("""
            SELECT count(*)=1 FROM app_users WHERE lower(email)=@email AND is_active=TRUE
              AND login_enabled=TRUE AND (@id IS NULL OR user_id=@id);
            """, connection))
        {
            user.Parameters.AddWithValue("email", email);
            user.Parameters.Add(new("id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)recipient.UserId ?? DBNull.Value });
            if (await user.ExecuteScalarAsync(token) is not true) return Denied("TEAMS_TENANT_USER_UNAVAILABLE");
        }
        var native = Text(snapshot.Metadata, "teamsNativeSource");
        if (native.Length > 0) return await ValidateNativeAsync(connection, snapshot, email, environment, token);
        var current = await ProjectNotificationRepository.LoadDispatchAsync(connection, snapshot.DispatchId, token);
        if (current is null || current.Subject != snapshot.Subject || current.TextBody != snapshot.TextBody
            || !current.Recipients.Any(r => r.UserId == recipient.UserId && r.Email.Trim().Equals(email, StringComparison.OrdinalIgnoreCase)))
            return Denied("TEAMS_EMAIL_SOURCE_CHANGED");
        var currentSource = await Module065NotificationSourceGuard.ValidateAsync(connection, current, token);
        if (!currentSource.Current || currentSource.Defer)
            return new(false, currentSource.Defer, currentSource.Boundary, currentSource.Code);
        var boundary = ProjectNotificationEvaluator.MoreRestrictiveBoundary(snapshot.DeliveryBoundary, current.DeliveryBoundary);
        var handoff = await Module025SowGsdModule.ValidateHandoffDispatchAsync(connection, current, token);
        if (!handoff.Current) return Denied(handoff.DiagnosticCode);
        boundary = ProjectNotificationEvaluator.MoreRestrictiveBoundary(boundary, handoff.Boundary);
        if (Guid.TryParse(Text(current.Metadata, "enterpriseNotificationEventId"), out var eventId))
        {
            var notification = await EnterpriseNotificationRepository.LoadEventAsync(connection, eventId, token);
            if (notification is null) return Denied("TEAMS_SOURCE_EVENT_MISSING");
            var policy = await EnterpriseNotificationRepository.LoadPolicyAsync(connection, notification.PolicyCode, token);
            if (policy is null || !policy.Enabled) return Denied("TEAMS_SOURCE_POLICY_DISABLED");
            boundary = ProjectNotificationEvaluator.MoreRestrictiveBoundary(boundary, policy.DeliveryBoundary);
            var recipients = await EnterpriseNotificationRecipientResolver.ResolveAsync(connection, policy, notification, token);
            if (!recipients.Recipients.Any(r => r.UserId == recipient.UserId && r.Email.Trim().Equals(email, StringComparison.OrdinalIgnoreCase)))
                return Denied("TEAMS_RECIPIENT_AUTHORIZATION_CHANGED");
            if (notification.PolicyCode is ProjectFlowHiveNotificationSource.AssignmentPolicy or ProjectFlowHiveNotificationSource.DuePolicy)
            {
                var source = await ProjectFlowHiveNotificationSource.ValidateAsync(connection, notification, token);
                if (!source.Current) return Denied("FLOWHIVE_TASK_EVENT_STALE");
                boundary = ProjectNotificationEvaluator.MoreRestrictiveBoundary(boundary, source.Boundary);
                if (source.Defer) return new(false, true, boundary, "FLOWHIVE_QUIET_HOURS");
            }
            if (!await EnterpriseReminderNotificationSource.IsCurrentAsync(connection, notification, token))
                return Denied("REMINDER_SOURCE_NO_LONGER_CURRENT");
            if (notification.PolicyCode is "TIME_MANAGER_APPROVAL_REQUEST" or "TIME_PM_APPROVAL_REQUEST" or "TIME_PTC_FINAL_APPROVAL_REQUEST" or "TIME_APPROVAL_OVERDUE_3_DAYS")
            {
                var expected = EnterpriseNotificationRecipientResolver.PayloadString(notification.Payload, "status");
                var date = EnterpriseNotificationRecipientResolver.PayloadDate(notification.Payload, "workDate");
                await using var stage = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM timesheet_day_statuses WHERE timesheet_id=@id AND work_date=@date AND status=@status);", connection);
                stage.Parameters.AddWithValue("id", notification.EntityId ?? Guid.Empty);
                stage.Parameters.Add(new("date", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)date ?? DBNull.Value });
                stage.Parameters.AddWithValue("status", expected);
                if (await stage.ExecuteScalarAsync(token) is not true) return Denied("TIME_APPROVAL_STAGE_CHANGED");
            }
        }
        if (current.NotificationType == "entra_client_secret_expiration")
        {
            var profileText=Text(current.Metadata,"ProfileId");
            if(!Guid.TryParse(profileText,out var profileId)) return Denied("ENTRA_EXPIRATION_PROFILE_INVALID");
            await using var query = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM entra_secret_expiration_state state
                  JOIN entra_secret_expiration_recipients recipient ON recipient.profile_id=state.active_profile_id
                  WHERE state.active_profile_id=@profile AND lower(recipient.email)=@email
                    AND NOT EXISTS(SELECT 1 FROM entra_secret_expiration_acknowledgements ack
                      WHERE ack.profile_id=recipient.profile_id AND ack.user_id=recipient.user_id));
                """, connection);
            query.Parameters.AddWithValue("profile", profileId); query.Parameters.AddWithValue("email", email);
            if (await query.ExecuteScalarAsync(token) is not true) return Denied("ENTRA_EXPIRATION_ACKNOWLEDGED_OR_ROTATED");
        }
        return new(boundary == "production_governed", false, boundary, boundary == "production_governed" ? "" : "TEAMS_SOURCE_BOUNDARY_BLOCKED");
    }

    private static async Task<Decision> ValidateNativeAsync(NpgsqlConnection connection,
        ProjectNotificationDispatchRow snapshot, string email, string environment, CancellationToken token)
    {
        if (!Guid.TryParse(Text(snapshot.Metadata, "sourceId"), out var sourceId)) return Denied("TEAMS_NATIVE_SOURCE_INVALID");
        var native = Text(snapshot.Metadata, "teamsNativeSource");
        var details = snapshot.Metadata.GetProperty("details");
        if (native == "sow_sell")
        {
            var current = await Module025SowGsdModule.ValidateSowSellTeamsNotificationAsync(connection, sourceId, email, environment, token);
            return current ? new(true, false, snapshot.DeliveryBoundary, "") : Denied("SOW_SELL_RECIPIENT_OR_SOURCE_CHANGED");
        }
        if (native == "analytics_schedule" && Guid.TryParse(Text(details, "scheduleId"), out var scheduleId))
        {
            await using var query = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM analytics_report_schedules schedule
                  JOIN analytics_report_schedule_recipients recipient USING(analytics_report_schedule_id)
                  WHERE schedule.analytics_report_schedule_id=@id AND schedule.enabled=TRUE
                    AND schedule.delivery_boundary='production_governed' AND lower(recipient.recipient_email)=@email);
                """, connection);
            query.Parameters.AddWithValue("id", scheduleId); query.Parameters.AddWithValue("email", email);
            return await query.ExecuteScalarAsync(token) is true ? new(true, false, snapshot.DeliveryBoundary, "") : Denied("ANALYTICS_SCHEDULE_OR_RECIPIENT_CHANGED");
        }
        if (native == "legacy_time_compliance" && Guid.TryParse(Text(details,"runId"),out var runId)
            && Guid.TryParse(Text(details,"userId"),out var userId) && DateOnly.TryParse(Text(details,"weekStart"),out var week))
        {
            var current=await TimeComplianceModule.ValidateTeamsNotificationAsync(connection,runId,userId,week,Text(details,"scenario"),email,token);
            return current ? new(true,false,snapshot.DeliveryBoundary,"") : Denied("TIME_COMPLIANCE_REVIEW_OR_SOURCE_CHANGED");
        }
        if (native == "work_register_ptc")
        {
            await using var query = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM work_register_temp_cloud_user_notifications WHERE work_register_temp_cloud_user_notification_id=@id)
                  AND EXISTS(SELECT 1 FROM app_users u WHERE lower(u.email)=@email AND u.is_active=TRUE AND COALESCE(u.login_enabled,TRUE)=TRUE
                    AND lower(u.email) LIKE '%@ussignal.com' AND (
                      lower(COALESCE(u.job_title,'')) LIKE '%project team coordinator%'
                      OR lower(COALESCE(u.department,'')) LIKE '%project management%'
                      OR lower(COALESCE(u.department_name,'')) LIKE '%project management%'
                      OR lower(COALESCE(u.team_name,'')) LIKE '%project management%'
                      OR lower(COALESCE(u.team_name,'')) LIKE '%pmo%'
                      OR EXISTS(SELECT 1 FROM app_user_role_assignments a JOIN app_roles role ON role.app_role_id=a.app_role_id AND role.is_active=TRUE
                        WHERE a.user_id=u.user_id AND a.is_active=TRUE AND (
                          lower(role.role_name) LIKE '%project team coordinator%' OR lower(role.role_name) LIKE '%project management%' OR lower(role.role_name) LIKE '%pmo%'
                          OR replace(lower(role.role_code),'_',' ') LIKE '%project team coordinator%'
                          OR replace(lower(role.role_code),'_',' ') LIKE '%project management%' OR replace(lower(role.role_code),'_',' ') LIKE '%pmo%'))));
                """, connection);
            query.Parameters.AddWithValue("id", sourceId); query.Parameters.AddWithValue("email", email);
            return await query.ExecuteScalarAsync(token) is true ? new(true, false, snapshot.DeliveryBoundary, "") : Denied("WORK_REGISTER_PTC_SCOPE_CHANGED");
        }
        return Denied("TEAMS_NATIVE_SOURCE_UNSUPPORTED");
    }
    private static Decision Denied(string code) => new(false, false, "locked", code);
}

using Npgsql;
using static ProjectTime.Api.Modules.Module065NotificationParityPolicy;

namespace ProjectTime.Api.Modules;

// Revalidates queued enterprise events for both channels and Module 032 releases/retries.
internal static class Module065NotificationSourceGuard
{
    internal sealed record State(bool Current,bool Defer,string Boundary,string Code);
    internal static async Task<State> ValidateAsync(NpgsqlConnection connection,ProjectNotificationDispatchRow dispatch,CancellationToken token)
    {
        if(!Guid.TryParse(Text(dispatch.Metadata,"enterpriseNotificationEventId"),out var id)) return new(true,false,dispatch.DeliveryBoundary,"");
        var notification=await EnterpriseNotificationRepository.LoadEventAsync(connection,id,token);
        if(notification is null) return new(false,false,"locked","NOTIFICATION_SOURCE_EVENT_MISSING");
        var policy=await EnterpriseNotificationRepository.LoadPolicyAsync(connection,notification.PolicyCode,token);
        if(policy is null || !policy.Enabled) return new(false,false,"locked","NOTIFICATION_SOURCE_POLICY_DISABLED");
        var boundary=ProjectNotificationEvaluator.MoreRestrictiveBoundary(dispatch.DeliveryBoundary,policy.DeliveryBoundary);
        if(!await EnterpriseReminderNotificationSource.IsCurrentAsync(connection,notification,token))
            return new(false,false,boundary,"REMINDER_SOURCE_NO_LONGER_CURRENT");
        if(notification.PolicyCode==ProjectFlowHiveAiPlannerOrchestrationModule.AutomationNotification
            && !await ProjectFlowHiveAiPlannerOrchestrationModule.AutomaticNotificationCurrentAsync(connection,notification,token))
            return new(false,false,boundary,"FLOWHIVE_FIRST_DRAFT_STALE");
        if(notification.PolicyCode is ProjectFlowHiveNotificationSource.AssignmentPolicy or ProjectFlowHiveNotificationSource.DuePolicy)
        {
            var source=await ProjectFlowHiveNotificationSource.ValidateAsync(connection,notification,token);
            boundary=ProjectNotificationEvaluator.MoreRestrictiveBoundary(boundary,source.Boundary);
            if(!source.Current) return new(false,false,boundary,"FLOWHIVE_TASK_EVENT_STALE");
            if(source.Defer) return new(true,true,boundary,"FLOWHIVE_QUIET_HOURS");
        }
        var resolution=await EnterpriseNotificationRecipientResolver.ResolveAsync(connection,policy,notification,token);
        static string Identity(ProjectNotificationUser recipient) => $"{recipient.UserId:D}|{recipient.Email.Trim().ToLowerInvariant()}|{recipient.RecipientType}";
        if(!resolution.Recipients.Select(Identity).ToHashSet(StringComparer.Ordinal).SetEquals(dispatch.Recipients.Select(Identity)))
            return new(false,false,boundary,"NOTIFICATION_SOURCE_RECIPIENTS_CHANGED");
        if(notification.PolicyCode is "TIME_MANAGER_APPROVAL_REQUEST" or "TIME_PM_APPROVAL_REQUEST" or "TIME_PTC_FINAL_APPROVAL_REQUEST" or "TIME_APPROVAL_OVERDUE_3_DAYS")
        {
            var date=EnterpriseNotificationRecipientResolver.PayloadDate(notification.Payload,"workDate");
            await using var query=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM timesheet_day_statuses WHERE timesheet_id=@id AND work_date=@date AND status=@status);",connection);
            query.Parameters.AddWithValue("id",notification.EntityId ?? Guid.Empty);
            query.Parameters.Add(new("date",NpgsqlTypes.NpgsqlDbType.Date){Value=(object?)date ?? DBNull.Value});
            query.Parameters.AddWithValue("status",EnterpriseNotificationRecipientResolver.PayloadString(notification.Payload,"status"));
            if(await query.ExecuteScalarAsync(token) is not true) return new(false,false,boundary,"TIME_APPROVAL_STAGE_CHANGED");
        }
        return new(true,false,boundary,"");
    }
}

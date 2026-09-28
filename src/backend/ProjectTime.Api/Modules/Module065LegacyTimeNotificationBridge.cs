using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class Module065LegacyTimeNotificationBridge
{
    internal static async Task QueueAsync(NpgsqlConnection connection,Guid runId,Guid? userId,DateOnly? weekStart,
        string scenario,string email,string name,string[] cc,string subject,string body,HttpContext context)
    {
        if(!userId.HasValue || !weekStart.HasValue) return;
        var readiness=await Module065ProjectNotificationDelivery.GetReadinessAsync(context,context.RequestAborted);
        var recipients=new[] { new ProjectNotificationUser(userId,name,email,"ENGINEER","existing_time_compliance_preview","to") }
            .Concat(cc.Select(address=>new ProjectNotificationUser(null,address,address,"","existing_time_compliance_preview","cc"))).ToArray();
        var sourceId=Module065NotificationParityPolicy.EventId("legacy-time-recipient:"+email.Trim().ToLowerInvariant(),runId);
        await Module065NotificationFanout.QueueNativeAsync(connection,sourceId,"legacy_time_compliance","023",
            "time_compliance_"+scenario,subject,body.Replace("Notification preview only. No email was sent.","Time-compliance notification from Pulse.",StringComparison.Ordinal),
            recipients,readiness.RecipientBoundary,new { runId,userId,weekStart=weekStart.Value.ToString("yyyy-MM-dd"),scenario,deepLink="#time-entry" },context,context.RequestAborted);
    }
}

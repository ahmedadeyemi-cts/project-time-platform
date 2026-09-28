using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class Module065WorkRegisterNotificationBridge
{
    internal static async Task DeliverAsync(NpgsqlConnection connection,Guid notificationId,string[] recipients,string subject,string body)
    {
        var users = Module065NotificationParityPolicy.Recipients(recipients)
            .Select(email=>new ProjectNotificationUser(null,email,email,"PROJECT_TEAM_COORDINATOR","work_register_trusted_ptc_snapshot")).ToArray();
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(null,CancellationToken.None);
        await Module065NotificationFanout.QueueNativeAsync(connection,notificationId,"work_register_ptc","Work Register",
            "work_register_identity_action_required",subject,body,users,readiness.RecipientBoundary,
            new { notificationId },null,CancellationToken.None);
        var delivery = await Module065ProjectNotificationDelivery.DeliverAsync(subject,body,
            "<p>"+System.Net.WebUtility.HtmlEncode(body).Replace("\n","<br />",StringComparison.Ordinal)+"</p>",users);
        await using var result = new NpgsqlCommand("""
            UPDATE work_register_temp_cloud_user_notifications SET notification_status=@status,
              notification_error=@diagnostic,sent_at=CASE WHEN @sent THEN now() ELSE sent_at END
            WHERE work_register_temp_cloud_user_notification_id=@id;
            """,connection);
        result.Parameters.AddWithValue("id",notificationId);result.Parameters.AddWithValue("sent",delivery.Sent);
        result.Parameters.AddWithValue("status",delivery.Sent ? "sent" : delivery.Status);
        result.Parameters.AddWithValue("diagnostic",delivery.DiagnosticCode);
        await result.ExecuteNonQueryAsync();
    }
}

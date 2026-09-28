using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

// Mirrors native email senders without changing their existing email/SELL/report ledgers.
// Stable source IDs, not message text or wall-clock timestamps, identify notifications.
internal static class Module065NotificationFanout
{
    internal static Task QueueNativeAsync(NpgsqlConnection connection, Guid sourceId, string sourceKind,
        string module, string type, string subject, string text, ProjectNotificationUser[] recipients,
        string boundary, object metadata, HttpContext? context, CancellationToken token)
    {
        var details = JsonSerializer.SerializeToElement(metadata);
        var scope = JsonSerializer.SerializeToElement(new { teamsNativeSource = sourceKind, sourceId, details,
            deepLink = Module065NotificationParityPolicy.Text(details, "deepLink") });
        var now = DateTimeOffset.UtcNow;
        var dispatch = new ProjectNotificationDispatchRow(
            Module065NotificationParityPolicy.EventId(sourceKind, sourceId), null, null, null,
            $"native:{sourceKind}:{sourceId:N}", type, "informational", module, "notification_created",
            subject, text, "", boundary, "module_065", "queued", now, null, null, null,
            "", "", "", scope, now, now, recipients, 0);
        return MicrosoftTeamsNotificationModule.TryDeliverDispatchAsync(connection, dispatch, context, token);
    }
}

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ProjectTime.Api.Modules;

internal static class WorkLifecycleBillingNotificationBridge
{
    internal static async Task<bool> QueueAsync(
        string policyCode,
        string eventIdentity,
        Guid projectId,
        Guid actorUserId,
        string status,
        string deepLink,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var queued = false;
        try
        {
            await using var connection = await EnterpriseNotificationRepository.OpenConnectionAsync(cancellationToken);
            if (!await EnterpriseNotificationRepository.IsReadyAsync(connection, cancellationToken))
                return false;

            var occurredAt = DateTimeOffset.UtcNow;
            var correlationId = string.IsNullOrWhiteSpace(context.TraceIdentifier)
                ? $"work-lifecycle-{Guid.NewGuid():N}"
                : context.TraceIdentifier;
            var normalizedIdentity = string.IsNullOrWhiteSpace(eventIdentity)
                ? Guid.NewGuid().ToString("N")
                : eventIdentity.Trim();

            var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["status"] = status,
                ["deepLink"] = deepLink,
                ["correlationId"] = correlationId
            });

            await EnterpriseNotificationRepository.InsertEventAsync(
                connection,
                policyCode,
                "040",
                $"work-lifecycle:{policyCode.ToLowerInvariant()}:{projectId:N}:{normalizedIdentity}",
                $"enterprise:work-lifecycle:{policyCode.ToLowerInvariant()}:{projectId:N}:{normalizedIdentity}",
                "project",
                projectId,
                projectId,
                null,
                occurredAt,
                occurredAt,
                payload,
                "module_040_native",
                actorUserId,
                correlationId,
                cancellationToken);
            queued = true;

            try
            {
                await EnterpriseNotificationOrchestrationService.RunAsync(
                    context,
                    actorUserId,
                    "event",
                    false,
                    25,
                    cancellationToken);
            }
            catch
            {
                // The durable event remains queued for the next Module 065 worker pass.
            }

            return true;
        }
        catch
        {
            // Project lifecycle decisions remain durable even when notification
            // infrastructure is temporarily unavailable.
            return queued;
        }
    }
}

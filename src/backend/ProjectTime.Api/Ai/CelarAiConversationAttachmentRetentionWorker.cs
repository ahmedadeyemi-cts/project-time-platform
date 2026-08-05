namespace ProjectTime.Api.Ai;

/// <summary>
/// Enforces the attachment retention boundary without reading file contents.
/// Retrieval is already denied at expiry/revocation; this worker completes the
/// physical deletion and records retryable purge evidence.
/// </summary>
public sealed class CelarAiConversationAttachmentRetentionWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);
    private readonly CelarAiConversationAttachmentService _attachments;
    private readonly ILogger<CelarAiConversationAttachmentRetentionWorker> _logger;

    public CelarAiConversationAttachmentRetentionWorker(
        CelarAiConversationAttachmentService attachments,
        ILogger<CelarAiConversationAttachmentRetentionWorker> logger)
    {
        _attachments = attachments;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var purged = await _attachments.PurgeExpiredAsync(stoppingToken);
                if (purged > 0)
                {
                    _logger.LogInformation(
                        "Celar AI attachment retention completed physical cleanup. PurgedCount={PurgedCount}",
                        purged);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Celar AI attachment retention encountered a bounded failure without logging attachment paths or content.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

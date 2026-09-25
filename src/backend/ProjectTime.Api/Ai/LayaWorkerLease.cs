namespace ProjectTime.Api.Ai;

/// <summary>
/// Couples a worker operation to its lease without leaving an infinite heartbeat
/// running after that operation finishes. Lease loss cancels in-flight work;
/// host cancellation is propagated rather than written as a model failure.
/// </summary>
public static class LayaWorkerLease
{
    public static async Task<bool> RunAsync(
        Func<CancellationToken, Task> operation,
        Func<CancellationToken, Task<bool>> renewLease,
        TimeSpan heartbeatInterval,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(renewLease);
        if (heartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));
        stoppingToken.ThrowIfCancellationRequested();

        using var processingStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var leaseLost = 0;
        var heartbeat = HeartbeatAsync();
        try
        {
            await operation(processingStop.Token);
            stoppingToken.ThrowIfCancellationRequested();
            return Volatile.Read(ref leaseLost) == 0;
        }
        catch (OperationCanceledException) when (
            !stoppingToken.IsCancellationRequested && Volatile.Read(ref leaseLost) != 0)
        {
            return false;
        }
        finally
        {
            // These are intentionally separate tokens. Completing the operation
            // stops renewal; losing renewal stops the operation.
            heartbeatStop.Cancel();
            try { await heartbeat; }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
        }

        async Task HeartbeatAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(heartbeatInterval);
                while (await timer.WaitForNextTickAsync(heartbeatStop.Token))
                {
                    if (!await renewLease(heartbeatStop.Token))
                    {
                        Interlocked.Exchange(ref leaseLost, 1);
                        processingStop.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
            catch
            {
                // An unavailable lease store is not evidence that we still own
                // the job. Stop model/source work and surface the renewal error.
                Interlocked.Exchange(ref leaseLost, 1);
                processingStop.Cancel();
                throw;
            }
        }
    }
}

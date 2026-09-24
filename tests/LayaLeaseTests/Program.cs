using ProjectTime.Api.Ai;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
    passed++;
}

// A minute-long heartbeat must not add a minute to a completed classification.
var renewals = 0;
for (var index = 0; index < 2; index++)
{
    var completed = await LayaWorkerLease.RunAsync(
        _ => Task.CompletedTask,
        _ => { Interlocked.Increment(ref renewals); return Task.FromResult(true); },
        TimeSpan.FromMinutes(1), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    Check(completed, "completed job releases heartbeat and permits next job " + index);
}
Check(renewals == 0, "completed work does not wait for a lease-renewal tick");

// The same cancellation token reaches a simulated inference operation.
var attemptedSave = false;
var cancelledInference = false;
var held = await LayaWorkerLease.RunAsync(
    async ct =>
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        catch (OperationCanceledException) { cancelledInference = true; throw; }
        attemptedSave = true;
    },
    _ => Task.FromResult(false),
    TimeSpan.FromMilliseconds(20), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
Check(!held && cancelledInference && !attemptedSave, "lease loss cancels inference before publication");

using (var stop = new CancellationTokenSource())
{
    var task = LayaWorkerLease.RunAsync(
        async ct => { stop.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); },
        _ => Task.FromResult(true), TimeSpan.FromMinutes(1), stop.Token);
    try { await task.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("host cancellation swallowed"); }
    catch (OperationCanceledException) { Check(true, "host shutdown propagates as cancellation"); }
}

var failedRenewalStoppedWork = false;
try
{
    await LayaWorkerLease.RunAsync(
        async ct =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { failedRenewalStoppedWork = true; throw; }
        },
        _ => Task.FromException<bool>(new IOException("synthetic lease-store failure")),
        TimeSpan.FromMilliseconds(20), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    throw new Exception("renewal error swallowed");
}
catch (IOException)
{
    Check(failedRenewalStoppedWork, "renewal failure cancels in-flight work and remains observable");
}

try
{
    await LayaWorkerLease.RunAsync(
        _ => Task.FromException(new InvalidOperationException("synthetic operation failure")),
        _ => Task.FromResult(true), TimeSpan.FromMinutes(1), CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(2));
    throw new Exception("operation failure swallowed");
}
catch (InvalidOperationException e) when (e.Message == "synthetic operation failure")
{
    Check(true, "operation failure stops heartbeat without hanging or masking the error");
}

using (var stop = new CancellationTokenSource())
{
    stop.Cancel();
    var executed = false;
    try
    {
        await LayaWorkerLease.RunAsync(
            _ => { executed = true; return Task.CompletedTask; },
            _ => Task.FromResult(true), TimeSpan.FromSeconds(1), stop.Token);
        throw new Exception("pre-cancelled operation executed");
    }
    catch (OperationCanceledException) { Check(!executed, "cancelled host does not start a new job"); }
}
Console.WriteLine($"LAYA_LEASE_ASSERTIONS_PASSED={passed}");

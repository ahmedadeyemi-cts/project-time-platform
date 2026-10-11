using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectTime.Api.Modules;

// Exercise the production middleware through real HTTP on an isolated loopback
// server. No application startup, database, credentials, or UAT endpoint is used.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var app = builder.Build();
app.UsePlatformOperationsTelemetry();
app.Run(context => { context.Response.StatusCode = 404; return Task.CompletedTask; });
await app.StartAsync();
try
{
    var server = app.Services.GetRequiredService<IServer>();
    var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
    var methods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "PROBE_A", "PROBE_B" };
    async Task SendAsync(int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            using var request = new HttpRequestMessage(new HttpMethod(methods[i % methods.Length]), "/api/public/probe-" + i.ToString("D8"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.NotFound) throw new Exception("Unexpected unmatched HTTP result");
        }
    }
    var flags = BindingFlags.Static | BindingFlags.NonPublic;
    var observations = typeof(PlatformOperationsModule).GetField("ApiObservations", flags)!.GetValue(null)!;
    var evidence = typeof(PlatformOperationsModule).GetField("Evidence", flags)!.GetValue(null)!;
    int Count(object value) => (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
    (int Keys, int Evidence, long Heap, long WorkingSet) Sample()
    {
        var keys = ((IEnumerable)observations.GetType().GetProperty("Keys")!.GetValue(observations)!).Cast<string>().ToArray();
        if (keys.Length != 8 || keys.Any(key => !key.Contains("/api/{unmatched}", StringComparison.Ordinal) || key.Contains("probe-", StringComparison.Ordinal)))
            throw new Exception("Unmatched paths or arbitrary verbs created observation keys");
        if (Count(evidence) > 2000) throw new Exception("Operational evidence exceeded its retention bound");
        var heap = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return (keys.Length, Count(evidence), heap, process.WorkingSet64);
    }
    await SendAsync(0, 5000); // Warm framework, networking, and all method buckets.
    await SendAsync(5000, 10000);
    var first = Sample();
    await SendAsync(15000, 40000);
    var second = Sample();
    var heapGrowth = second.Heap - first.Heap;
    var workingSetGrowth = second.WorkingSet - first.WorkingSet;
    if (second.Keys != first.Keys || second.Evidence != first.Evidence || heapGrowth > 4 * 1024 * 1024 || workingSetGrowth > 32 * 1024 * 1024)
        throw new Exception("Unmatched HTTP request volume grew telemetry retention or exceeded memory budget");
    Console.WriteLine($"SECURITY_UNMATCHED_TELEMETRY_HTTP=PASS requests=55000 firstKeys={first.Keys} finalKeys={second.Keys} evidence={second.Evidence} heapGrowthBytes={heapGrowth} workingSetGrowthBytes={workingSetGrowth}");
}
finally { await app.StopAsync(); }

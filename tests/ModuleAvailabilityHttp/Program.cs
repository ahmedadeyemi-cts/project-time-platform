using System.Net;
using System.Text.Json;
using ProjectTime.Api.Modules;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var app = builder.Build();
app.MapModuleAvailabilityEndpoints();
await app.StartAsync();
try {
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    foreach (var path in new[] { "/api/module-availability", "/api/module-availability/audit" }) {
        using var response = await client.GetAsync(path);
        if (response.StatusCode != HttpStatusCode.Unauthorized) throw new Exception($"{path}: expected 401 JSON, got {response.StatusCode}");
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new Exception("Missing JSON content type");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!body.RootElement.TryGetProperty("status", out _)) throw new Exception("Handler response was discarded");
    }
    Console.WriteLine("2 real HTTP availability endpoints preserve authorization status and JSON results.");
} finally { await app.StopAsync(); }

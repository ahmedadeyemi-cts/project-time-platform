using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Private-ingress candidate validation. Credentials arrive only through terminal stdin.
// Never writes application data besides the bounded login/logout session lifecycle.
Console.WriteLine("PULSE_CANARY_READY");
string? token = null;
string failure = "input_contract";
var checks = new List<string>();
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
{
    BaseAddress = new Uri("http://127.0.0.1:5080"),
    Timeout = TimeSpan.FromSeconds(20),
    MaxResponseContentBufferSize = 4 * 1024 * 1024
};
client.DefaultRequestHeaders.Add("Origin", "https://phd-west-test.onenecklab.com");
client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
string source = "";
object? result = null;

async Task<JsonElement> Request(string path, string module = "001", object? payload = null, bool authenticated = false, bool denyAnonymous = false)
{
    using var request = new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post, path);
    request.Headers.Add("X-ProjectPulse-Module-Number", module);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    if (authenticated)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-ProjectPulse-Session", token);
    }
    if (payload is not null) request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    using var response = await client.SendAsync(request);
    if (denyAnonymous)
    {
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) throw new InvalidOperationException();
        return default;
    }
    if (response.StatusCode != HttpStatusCode.OK || !(response.Content.Headers.ContentType?.MediaType?.Contains("json") ?? false)) throw new InvalidOperationException();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.Clone();
}

try
{
    using var input = JsonDocument.Parse(Console.ReadLine() ?? "");
    var password = input.RootElement.GetProperty("password").GetString() ?? "";
    source = input.RootElement.GetProperty("sourceCommit").GetString() ?? "";
    if (password.Length < 12 || !Regex.IsMatch(source, "^[a-f0-9]{40}$")) throw new InvalidOperationException();
    foreach (var (path, status) in new[] { ("/health", "healthy"), ("/health/live", "alive"), ("/health/ready", "ready") })
    {
        failure = "health_contract";
        if ((await Request(path)).GetProperty("status").GetString() != status) throw new InvalidOperationException();
        checks.Add(path);
    }
    failure = "source_identity";
    var identity = await Request("/health/source");
    if (identity.GetProperty("component").GetString() != "ProjectTime.Api" || identity.GetProperty("sourceCommit").GetString() != source) throw new InvalidOperationException();
    checks.Add("/health/source");
    failure = "anonymous_access";
    await Request("/api/security/context", denyAnonymous: true);
    checks.Add("anonymous_session_denied");
    failure = "local_login";
    var login = await Request("/api/auth/local/login", payload: new { username = "jason.mosier@ussignal.local", password });
    token = login.GetProperty("sessionToken").GetString();
    if (login.GetProperty("provider").GetString() != "LOCAL" || login.GetProperty("mustChangePassword").GetBoolean() || string.IsNullOrEmpty(token)) throw new InvalidOperationException();
    failure = "authenticated_identity";
    var context = await Request("/api/security/context", authenticated: true);
    if (string.IsNullOrEmpty(context.GetProperty("userId").GetString())) throw new InvalidOperationException();
    checks.Add("/api/security/context");
    foreach (var (module, path) in new[] {
        ("001", "/api/assignments/available-tasks?weekStart=2026-08-16"),
        ("001", "/api/timesheet/work-queue?weekStart=2026-08-16"),
        ("001A", "/api/engineer-task-closeout/overview"),
        ("019", "/api/project-workspace/overview") })
    {
        failure = "authenticated_core_read";
        var value = await Request(path, module, authenticated: true);
        if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) throw new InvalidOperationException();
        checks.Add(path);
    }
    result = new { result = "PASS", checks, sourceCommit = source, celarSowAcceptance = "PENDING_NOT_EXECUTED" };
}
catch { result = new { result = "FAILED", failureReason = failure, checks, celarSowAcceptance = "PENDING_NOT_EXECUTED" }; }
finally
{
    if (token is not null)
    {
        try { await Request("/api/auth/session/logout", payload: new { }, authenticated: true); }
        catch { result = new { result = "FAILED", failureReason = "session_revocation", checks, celarSowAcceptance = "PENDING_NOT_EXECUTED" }; }
    }
}
Console.WriteLine("PULSE_CANARY_RESULT=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result))));

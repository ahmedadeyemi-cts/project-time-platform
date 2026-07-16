namespace ProjectTime.Api.Modules;

public static class SessionIntelligenceModule
{
    public static WebApplication MapSessionIntelligenceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/security/session-intelligence", (HttpContext context) =>
        {
            var sessionUser = context.Items["ProjectPulseSessionUserId"]?.ToString();
            if (string.IsNullOrWhiteSpace(sessionUser))
                return Results.Json(new { status = "session_required" }, statusCode: 401);

            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var clientIp = !string.IsNullOrWhiteSpace(forwarded)
                ? forwarded.Split(',')[0].Trim()
                : context.Connection.RemoteIpAddress?.ToString() ?? "Not available";

            return Results.Ok(new
            {
                status = "session_intelligence_loaded",
                network = new
                {
                    publicIp = clientIp,
                    forwardedForPresent = !string.IsNullOrWhiteSpace(forwarded),
                    protocol = context.Request.Protocol,
                    scheme = context.Request.Scheme,
                    host = context.Request.Host.Value
                },
                request = new
                {
                    userAgent = context.Request.Headers.UserAgent.ToString(),
                    acceptLanguage = context.Request.Headers.AcceptLanguage.ToString(),
                    traceIdentifier = context.TraceIdentifier
                },
                runtime = new
                {
                    environment = Environment.GetEnvironmentVariable("PROJECTPULSE_CICD_ENVIRONMENT") ?? "test",
                    apiRevision = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION") ?? "Not configured",
                    apiReplica = Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME") ?? "Not configured",
                    sourceCommit = Environment.GetEnvironmentVariable("PROJECTPULSE_CICD_SOURCE_COMMIT") ?? "Not configured"
                },
                privacy = new
                {
                    browserFingerprinting = false,
                    secretsReturned = false,
                    tokenValuesReturned = false
                }
            });
        });
        return app;
    }
}

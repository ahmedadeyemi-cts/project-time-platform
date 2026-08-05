namespace ProjectTime.Api.Ai;

/// <summary>
/// A release candidate is a zero-traffic verification appliance, not an
/// application revision. Its HTTP surface is closed by default so an accidental
/// candidate FQDN or writable credential cannot mutate shared application data.
/// </summary>
public static class ProjectPulseAiCandidateRequestFence
{
    public const string VerificationPath = "/api/ai-configuration/release-candidate/verify";

    public static IApplicationBuilder UseProjectPulseAiCandidateRequestFence(
        this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (!release.IsCandidate)
        {
            await next();
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var health = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && string.Equals(path, "/health", StringComparison.Ordinal);
        var verify = HttpMethods.IsPost(context.Request.Method)
            && string.Equals(path, VerificationPath, StringComparison.Ordinal);
        if (health || verify)
        {
            await next();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status423Locked;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new
        {
            status = "release_candidate_request_locked",
            message = "This zero-traffic release candidate exposes only health and its combined verification operation.",
            releasePhase = release.PhaseCode,
            sourceCommit = release.SourceCommit,
            stateChanged = false
        });
    });
}

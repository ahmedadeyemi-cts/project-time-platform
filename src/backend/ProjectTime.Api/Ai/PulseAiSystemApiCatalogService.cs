using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Reads the endpoint metadata of the running ASP.NET application. This is the
/// authoritative source for questions such as "which APIs are registered in
/// this revision?" It does not infer routes from documentation or source files.
/// </summary>
public sealed class PulseAiSystemApiCatalogService
{
    private readonly IEnumerable<EndpointDataSource> _endpointDataSources;

    public PulseAiSystemApiCatalogService(IEnumerable<EndpointDataSource> endpointDataSources)
    {
        _endpointDataSources = endpointDataSources;
    }

    public IReadOnlyList<PulseAiSystemApiDescriptor> List(
        string? search = null,
        string? moduleCode = null,
        string? method = null,
        bool? safeRetest = null,
        int limit = 500)
    {
        var normalizedSearch = Clean(search, 500).ToLowerInvariant();
        var normalizedModule = Clean(moduleCode, 20).ToUpperInvariant();
        var normalizedMethod = Clean(method, 12).ToUpperInvariant();
        limit = Math.Clamp(limit, 1, 2_500);

        var results = BuildInventory()
            .Where(item => normalizedSearch.Length == 0
                || item.SearchText.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedModule.Length == 0
                || string.Equals(item.ModuleCode, normalizedModule, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedMethod.Length == 0
                || string.Equals(item.Method, normalizedMethod, StringComparison.OrdinalIgnoreCase))
            .Where(item => safeRetest is null || item.SafeRetestSupported == safeRetest)
            .OrderBy(item => item.ModuleCode)
            .ThenBy(item => item.RoutePattern)
            .ThenBy(item => item.Method)
            .Take(limit)
            .ToArray();

        return results;
    }

    public PulseAiSystemApiDescriptor? Find(string apiId) =>
        BuildInventory().FirstOrDefault(item => string.Equals(
            item.ApiId,
            Clean(apiId, 100),
            StringComparison.OrdinalIgnoreCase));

    public object Summary(IReadOnlyList<PulseAiSystemApiDescriptor> apis) => new
    {
        total = apis.Count,
        modules = apis.Select(item => item.ModuleCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        get = apis.Count(item => HttpMethods.IsGet(item.Method)),
        post = apis.Count(item => HttpMethods.IsPost(item.Method)),
        put = apis.Count(item => HttpMethods.IsPut(item.Method)),
        patch = apis.Count(item => HttpMethods.IsPatch(item.Method)),
        delete = apis.Count(item => HttpMethods.IsDelete(item.Method)),
        other = apis.Count(item => !new[]
        {
            HttpMethods.Get,
            HttpMethods.Post,
            HttpMethods.Put,
            HttpMethods.Patch,
            HttpMethods.Delete
        }.Contains(item.Method, StringComparer.OrdinalIgnoreCase)),
        parameterized = apis.Count(item => item.Parameterized),
        safeRetestSupported = apis.Count(item => item.SafeRetestSupported),
        anonymous = apis.Count(item => item.AllowsAnonymous),
        sessionProtected = apis.Count(item => item.RequiresApplicationSession),
        releaseSha = ReleaseSha(),
        catalogVersion = PulseAiSystemIntelligencePolicy.ApiCatalogVersion,
        generatedAt = DateTimeOffset.UtcNow
    };

    private IReadOnlyList<PulseAiSystemApiDescriptor> BuildInventory()
    {
        var releaseSha = ReleaseSha();
        var inventory = new Dictionary<string, PulseAiSystemApiDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in _endpointDataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var route = endpoint.RoutePattern.RawText?.Trim();
            if (string.IsNullOrWhiteSpace(route)) continue;
            if (!route.StartsWith('/')) route = $"/{route}";

            var methodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            var methods = methodMetadata?.HttpMethods?.Count > 0
                ? methodMetadata.HttpMethods
                : ["ANY"];
            var displayName = Clean(endpoint.DisplayName, 500);
            var endpointName = Clean(
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                300);
            var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var explicitAuthorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            var isPublic = IsPublicPath(route);
            var owner = PulseAiSystemKnowledgeCatalog.InferModule(route);

            foreach (var methodValue in methods)
            {
                var httpMethod = string.IsNullOrWhiteSpace(methodValue)
                    ? "ANY"
                    : methodValue.Trim().ToUpperInvariant();
                var retest = SafeRetest(route, httpMethod);
                var apiId = ApiId(httpMethod, route);
                inventory[apiId] = new PulseAiSystemApiDescriptor(
                    ApiId: apiId,
                    Method: httpMethod,
                    RoutePattern: route,
                    DisplayName: displayName,
                    EndpointName: endpointName,
                    Order: endpoint.Order,
                    ModuleCode: owner.ModuleCode,
                    ModuleName: owner.ModuleName,
                    Purpose: PulseAiSystemKnowledgeCatalog.PurposeFor(route, displayName),
                    Parameterized: route.Contains('{', StringComparison.Ordinal),
                    RequiresApplicationSession: !allowsAnonymous && !isPublic,
                    AllowsAnonymous: allowsAnonymous || isPublic,
                    SafeRetestSupported: retest.Supported,
                    SafeRetestReason: retest.Reason,
                    RegistrationStatus: explicitAuthorization
                        ? "registered_with_explicit_authorization_metadata"
                        : !allowsAnonymous && !isPublic
                            ? "registered_under_application_session_boundary"
                            : "registered_public_or_anonymous",
                    ReleaseSha: releaseSha);
            }
        }

        return inventory.Values.ToArray();
    }

    private static (bool Supported, string Reason) SafeRetest(string route, string method)
    {
        if (!HttpMethods.IsGet(method))
            return (false, "Only GET endpoints are eligible for a safe read-only retest.");
        if (route.Contains('{', StringComparison.Ordinal))
            return (false, "The route requires one or more path parameters.");
        if (route.Contains("/auth/", StringComparison.OrdinalIgnoreCase)
            || route.Contains("callback", StringComparison.OrdinalIgnoreCase)
            || route.Contains("logout", StringComparison.OrdinalIgnoreCase)
            || route.Contains("token", StringComparison.OrdinalIgnoreCase)
            || route.Contains("secret", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Authentication, callback, token, or secret routes are never retested by Pulse AI.");
        }
        if (route.Contains("download", StringComparison.OrdinalIgnoreCase)
            || route.Contains("export", StringComparison.OrdinalIgnoreCase)
            || route.Contains("stream", StringComparison.OrdinalIgnoreCase)
            || route.Contains("attachment", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Download, export, stream, and attachment routes are excluded.");
        }
        if (route.StartsWith("/api/pulse-ai/v1/system/apis/", StringComparison.OrdinalIgnoreCase)
            || route.StartsWith("/api/platform-operations/apis/", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "The route could recurse into an API diagnostic operation.");
        }
        if (route.Contains("refresh", StringComparison.OrdinalIgnoreCase)
            || route.Contains("retest", StringComparison.OrdinalIgnoreCase)
            || route.Contains("probe", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Refresh, retest, and probe routes require an explicit owning-module action contract.");
        }

        return (
            true,
            "A same-origin GET can verify status and latency. Pulse AI does not return the response body from a safe retest.");
    }

    private static bool IsPublicPath(string route) =>
        route.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/api/public/", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/api/bootstrap", StringComparison.OrdinalIgnoreCase);

    private static string ApiId(string method, string route)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{method}|{route}"));
        return Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }

    private static string ReleaseSha()
    {
        foreach (var name in new[]
                 {
                     "PROJECTPULSE_RELEASE_COMMIT",
                     "PROJECTPULSE_RELEASE_SHA",
                     "GITHUB_SHA",
                     "WEBSITE_COMMIT_ID",
                     "CONTAINER_APP_REVISION"
                 })
        {
            var value = Environment.GetEnvironmentVariable(name)?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "not_recorded";
    }

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}

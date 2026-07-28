namespace ProjectTime.Api.Modules;

/// <summary>
/// Executes the read-only Module 012/037 IResult handlers explicitly.
///
/// The original MapGet method-group registrations accept handlers whose concrete
/// return type is Task&lt;IResult&gt;. That method group can bind to ASP.NET's raw
/// RequestDelegate overload because Task&lt;IResult&gt; derives from Task. The raw
/// delegate is awaited, but its IResult value is not executed, which produces the
/// observed HTTP 200 response with Content-Length: 0.
///
/// This compatibility middleware is intentionally limited to authenticated GET
/// reads for the existing role-policy routes. It calls the existing handlers and
/// explicitly executes their IResult, preserving all database, session, View-As,
/// and policy-read checks while restoring the JSON response body.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    private const string ResultExecutionMarker = "explicit-iresult-v1";
    private const string ModuleHeader = "X-ProjectPulse-Module-Number";

    private static readonly string[] ReadRoutePrefixes =
    [
        "/api/runtime/v2/role-policy/",
        "/api/runtime/role-policy/",
        "/api/role-policy/"
    ];

    public static WebApplication UseScopedRolePolicyResultExecutionCompatibility(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method)
                || !TryResolveReadOperation(
                    context.Request.Path.Value,
                    out var operation,
                    out var requestedModule))
            {
                await next();
                return;
            }

            context.Items["ProjectPulseRequestedModuleNumber"] = requestedModule;
            context.Items["ProjectPulseModuleAvailabilityReadContinuity"] = true;
            context.Request.Headers.Remove(ModuleHeader);

            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-ProjectPulse-Module-Availability"] =
                "authorized-read-continuity";
            context.Response.Headers["X-ProjectPulse-Requested-Module"] =
                requestedModule;
            context.Response.Headers["X-ProjectPulse-Role-Policy-Execution"] =
                ResultExecutionMarker;

            IResult result = operation switch
            {
                "summary" => await SummaryAsync(context),
                "catalog" => await CatalogAsync(context),
                "versions" => await VersionsAsync(context),
                "matrix" => await MatrixAsync(context),
                _ => throw new InvalidOperationException(
                    $"Unsupported role-policy read operation: {operation}")
            };

            await result.ExecuteAsync(context);
        });

        return app;
    }

    private static bool TryResolveReadOperation(
        string? requestPath,
        out string operation,
        out string requestedModule)
    {
        var normalized = (requestPath ?? string.Empty).Trim().TrimEnd('/');
        foreach (var prefix in ReadRoutePrefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = normalized[prefix.Length..].Trim('/').ToLowerInvariant();
            if (candidate is not ("summary" or "catalog" or "versions" or "matrix"))
                break;

            operation = candidate;
            requestedModule = candidate == "matrix" ? "037" : "012";
            return true;
        }

        operation = string.Empty;
        requestedModule = string.Empty;
        return false;
    }
}

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    public static WebApplication MapCombinedModulePublicReadinessEndpoint(this WebApplication app)
    {
        app.MapGet("/health/combined-modules", CombinedRuntimeReadinessAsync);
        app.MapGet("/api/public/combined-modules/readiness", CombinedRuntimeReadinessAsync);
        return app;
    }
}

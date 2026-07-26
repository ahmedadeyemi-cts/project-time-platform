namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    public static WebApplication MapCombinedModulePublicReadinessEndpoint(this WebApplication app)
    {
        app.MapGet("/health/combined-modules", CombinedRuntimeReadinessAsync);
        return app;
    }
}

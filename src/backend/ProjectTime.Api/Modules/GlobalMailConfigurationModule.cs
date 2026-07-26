namespace ProjectTime.Api.Modules;

/// <summary>
/// Compatibility registration point retained for the former Module 067.
/// Module 065 owns Microsoft Integration; Module 010 owns Entra directory-user import.
/// Program.cs continues to call this existing method once, so no shared startup edit is required.
/// </summary>
public static class GlobalMailConfigurationModule
{
    public static WebApplication MapGlobalMailConfigurationEndpoints(this WebApplication app)
    {
        app.UseMicrosoftIntegrationSecurityCompatibility();
        app.UseMicrosoftSsoRuntimeCompatibility();
        MicrosoftIntegrationModule.MapEndpoints(app);
        app.MapMicrosoftSsoConnectionProfileEndpoints();
        app.MapMicrosoftSsoRuntimeProfileEndpoints();
        AzureDirectoryImportModule.MapEndpoints(app);
        return app;
    }
}

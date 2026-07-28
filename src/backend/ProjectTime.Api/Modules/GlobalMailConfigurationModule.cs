namespace ProjectTime.Api.Modules;

/// <summary>
/// Compatibility registration point retained for the former Module 067.
/// Module 065 owns Microsoft Integration; Module 010 owns Entra directory-user import and sync.
/// Program.cs continues to call this existing method once, so no broad startup edit is required.
/// Trusted public-origin resolution is centralized in
/// <see cref="ProjectPulsePublicOriginCompatibility"/> so Microsoft, Entra preview,
/// and CRM/ERP mutations share one fail-closed origin boundary.
/// </summary>
public static class GlobalMailConfigurationModule
{
    private const string MicrosoftSsoRuntimePrefix = "/api/microsoft-integration/sso-";

    public static WebApplication MapGlobalMailConfigurationEndpoints(this WebApplication app)
    {
        app.UseProjectPulsePublicOriginCompatibility();
        app.UseMicrosoftEnvironmentRuntimeCompatibility();
        app.UseMicrosoftSsoInteractiveStartActivation();
        app.UseMicrosoftIntegrationSecurityCompatibility();
        app.UseMicrosoftPublicSsoOriginCompatibility();
        app.UseScopedRolePolicyResultExecutionCompatibility();
        app.UseModuleAvailabilityReadContinuityCompatibility();
        app.UseMicrosoftSsoRuntimeCompatibility();
        app.UseMicrosoftSmtpCredentialProjectionCompatibility();
        MicrosoftIntegrationModule.MapEndpoints(app);
        app.MapMicrosoftSsoConnectionProfileEndpoints();
        app.MapMicrosoftSsoRuntimeProfileEndpoints();
        app.MapMicrosoftServicesRuntimeProfileEndpoints();
        app.MapMicrosoftMailRuntimeConfigurationEndpoints();
        app.MapMicrosoftMailTransportTestEndpoints();
        AzureDirectoryImportModule.MapEndpoints(app);
        app.MapMicrosoftDirectorySyncEndpoints();

        app.MapDynamicRbacAdministrationEndpoints();
        app.MapCustomerDirectorySellSyncEndpoints();
        return app;
    }

    private static WebApplication UseMicrosoftPublicSsoOriginCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith(MicrosoftSsoRuntimePrefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (!TryResolvePublicOrigin(context, out var publicOrigin, out var source))
            {
                await Results.BadRequest(new
                {
                    module = "065",
                    status = "trusted_public_origin_unavailable",
                    message = "ProjectPulse could not determine the trusted HTTPS public origin for this Microsoft SSO operation. Verify the public URL and reverse-proxy forwarding configuration."
                }).ExecuteAsync(context);
                return;
            }

            context.Request.Scheme = publicOrigin.Scheme;
            context.Request.Host = HostString.FromUriComponent(publicOrigin.Authority);
            context.Items[ProjectPulsePublicOriginCompatibility.PublicOriginItem] = publicOrigin;
            context.Items[ProjectPulsePublicOriginCompatibility.PublicOriginSourceItem] = source;
            context.Items["ProjectPulsePublicOrigin"] = publicOrigin.GetLeftPart(UriPartial.Authority);
            await next();
        });
        return app;
    }

    private static bool TryResolvePublicOrigin(
        HttpContext context,
        out Uri publicOrigin,
        out string source)
    {
        if (context.Items.TryGetValue(ProjectPulsePublicOriginCompatibility.PublicOriginItem, out var existing)
            && existing is Uri existingUri)
        {
            publicOrigin = existingUri;
            source = context.Items.TryGetValue(ProjectPulsePublicOriginCompatibility.PublicOriginSourceItem, out var existingSource)
                ? existingSource?.ToString() ?? "trusted_origin_middleware"
                : "trusted_origin_middleware";
            return true;
        }

        if (ProjectPulsePublicOriginCompatibility.TryResolveProxyOrConfiguredOrigin(
                context,
                out publicOrigin,
                out source))
        {
            return true;
        }

        return ProjectPulsePublicOriginCompatibility.TryBrowserOrigin(
            context,
            out publicOrigin,
            out source);
    }
}

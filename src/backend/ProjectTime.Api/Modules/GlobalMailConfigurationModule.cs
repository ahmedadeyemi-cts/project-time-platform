namespace ProjectTime.Api.Modules;

/// <summary>
/// Compatibility registration point retained for the former Module 067.
/// Module 065 owns Microsoft Integration; Module 010 owns Entra directory-user import.
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
        // Normalize only trusted ProjectPulse public origins before any
        // Microsoft, Entra-preview, or Module 026 same-origin decisions run.
        app.UseProjectPulsePublicOriginCompatibility();
        // Derive Test versus Production from the trusted public host before the
        // existing Microsoft runtime modules inspect ASPNETCORE_ENVIRONMENT.
        app.UseMicrosoftEnvironmentRuntimeCompatibility();
        app.UseMicrosoftIntegrationSecurityCompatibility();
        app.UseMicrosoftPublicSsoOriginCompatibility();
        // Preserve the legacy role-policy route families while Modules 012 and 037
        // move to the explicit, database-dynamic /api/rbac/v1 contract.
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

        // Modules 012 and 037 use this clean v1 RBAC contract. It has no fixed
        // module count, supports audited role membership and module lifecycle,
        // and defaults newly registered modules to No Access for every ordinary
        // role while preserving permanent Super Administrator Full Control.
        app.MapDynamicRbacAdministrationEndpoints();

        // Additive registration only: Module 021 consumes the authoritative
        // Module 026 SELL connection without changing Microsoft Integration.
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

        // Browser Origin and Referer remain a final compatibility candidate,
        // but the shared resolver still enforces HTTPS and approved ProjectPulse
        // environment hosts. An invalid forwarded value is never accepted and
        // no longer prevents a valid browser origin from being evaluated.
        return ProjectPulsePublicOriginCompatibility.TryBrowserOrigin(
            context,
            out publicOrigin,
            out source);
    }
}

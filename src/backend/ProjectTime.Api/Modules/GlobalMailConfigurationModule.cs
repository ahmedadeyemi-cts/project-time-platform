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
    private const string InteractiveSsoPrefix = "/api/auth/sso/";

    public static WebApplication MapGlobalMailConfigurationEndpoints(this WebApplication app)
    {
        app.UseProjectPulsePublicOriginCompatibility();

        // Resolve the trusted public HTTPS origin before environment selection or
        // interactive SSO activation. Azure's internal Container Apps host must
        // never become the expected Entra callback URI.
        app.UseMicrosoftPublicSsoOriginCompatibility();
        app.UseMicrosoftEnvironmentRuntimeCompatibility();
        app.UseMicrosoftSsoInteractiveStartActivation();
        app.UseMicrosoftIntegrationSecurityCompatibility();
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
            var isSsoOperation = path.StartsWith(MicrosoftSsoRuntimePrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(InteractiveSsoPrefix, StringComparison.OrdinalIgnoreCase);
            if (!isSsoOperation)
            {
                await next();
                return;
            }

            if (!TryResolvePublicOrigin(context, out var publicOrigin, out var source))
            {
                var isInteractiveCallback = path.Equals(
                    MicrosoftSsoStateOriginRecovery.CallbackPath,
                    StringComparison.OrdinalIgnoreCase);
                var recovered = isInteractiveCallback
                    ? await MicrosoftSsoStateOriginRecovery.TryRecoverAsync(
                        context,
                        context.Request.Query["state"].ToString(),
                        context.RequestAborted)
                    : MicrosoftSsoStateOriginRecovery.StateOriginResult.Fail("state_origin_recovery_not_applicable");

                if (!recovered.Recovered || recovered.PublicOrigin is null)
                {
                    await Results.BadRequest(new
                    {
                        module = "065",
                        status = "trusted_public_origin_unavailable",
                        correlationId = context.TraceIdentifier,
                        originRecoveryCode = recovered.FailureCode,
                        message = "ProjectPulse could not determine the trusted HTTPS public origin for this Microsoft SSO operation. Verify the public URL and reverse-proxy forwarding configuration."
                    }).ExecuteAsync(context);
                    return;
                }

                publicOrigin = recovered.PublicOrigin;
                source = recovered.Source;
                context.Items["ProjectPulseSsoStateRedirectUri"] = recovered.RedirectUri;
            }

            context.Request.Scheme = publicOrigin.Scheme;
            context.Request.Host = HostString.FromUriComponent(publicOrigin.Authority);
            context.Items[ProjectPulsePublicOriginCompatibility.PublicOriginItem] = publicOrigin;
            context.Items[ProjectPulsePublicOriginCompatibility.PublicOriginSourceItem] = source;
            context.Items["ProjectPulsePublicOrigin"] = publicOrigin.GetLeftPart(UriPartial.Authority);
            context.Items["ProjectPulsePublicSsoOriginResolved"] = true;
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
            && existing is Uri existingUri
            && ProjectPulsePublicOriginCompatibility.TrustedHost(existingUri.Host, context))
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

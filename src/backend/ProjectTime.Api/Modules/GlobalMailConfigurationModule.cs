namespace ProjectTime.Api.Modules;

/// <summary>
/// Compatibility registration point retained for the former Module 067.
/// Module 065 owns Microsoft Integration; Module 010 owns Entra directory-user import.
/// Program.cs continues to call this existing method once, so no shared startup edit is required.
/// </summary>
public static class GlobalMailConfigurationModule
{
    private const string MicrosoftSsoRuntimePrefix = "/api/microsoft-integration/sso-";

    public static WebApplication MapGlobalMailConfigurationEndpoints(this WebApplication app)
    {
        app.UseMicrosoftIntegrationSecurityCompatibility();
        app.UseMicrosoftPublicSsoOriginCompatibility();
        app.UseMicrosoftSsoRuntimeCompatibility();
        app.UseMicrosoftSmtpCredentialProjectionCompatibility();
        MicrosoftIntegrationModule.MapEndpoints(app);
        app.MapMicrosoftSsoConnectionProfileEndpoints();
        app.MapMicrosoftSsoRuntimeProfileEndpoints();
        app.MapMicrosoftServicesRuntimeProfileEndpoints();
        app.MapMicrosoftMailRuntimeConfigurationEndpoints();
        AzureDirectoryImportModule.MapEndpoints(app);
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

            var forwardedHost = FirstForwardedValue(context.Request.Headers["X-Forwarded-Host"].ToString());
            var forwardedProto = FirstForwardedValue(context.Request.Headers["X-Forwarded-Proto"].ToString());
            if (string.IsNullOrWhiteSpace(forwardedHost) && string.IsNullOrWhiteSpace(forwardedProto))
            {
                await next();
                return;
            }

            var scheme = string.IsNullOrWhiteSpace(forwardedProto)
                ? context.Request.Scheme
                : forwardedProto.ToLowerInvariant();
            var authority = string.IsNullOrWhiteSpace(forwardedHost)
                ? context.Request.Host.Value
                : forwardedHost;

            if ((scheme != "https" && scheme != "http")
                || !Uri.TryCreate($"{scheme}://{authority}", UriKind.Absolute, out var publicOrigin)
                || !string.IsNullOrWhiteSpace(publicOrigin.UserInfo)
                || publicOrigin.AbsolutePath != "/"
                || !string.IsNullOrWhiteSpace(publicOrigin.Query)
                || !string.IsNullOrWhiteSpace(publicOrigin.Fragment))
            {
                await Results.BadRequest(new
                {
                    module = "065",
                    status = "invalid_forwarded_public_origin",
                    message = "The forwarded ProjectPulse public origin is invalid."
                }).ExecuteAsync(context);
                return;
            }

            context.Request.Scheme = publicOrigin.Scheme;
            context.Request.Host = HostString.FromUriComponent(publicOrigin.Authority);
            await next();
        });
        return app;
    }

    private static string FirstForwardedValue(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
}

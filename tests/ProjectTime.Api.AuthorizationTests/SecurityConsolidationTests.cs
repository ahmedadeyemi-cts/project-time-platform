using Microsoft.AspNetCore.Http;
using ProjectTime.Api.Modules;

internal static class SecurityConsolidationTests
{
    internal static async Task RunAsync()
    {
        int assertions = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
        var context = new DefaultHttpContext();
        context.Items["ProjectPulsePermanentFullControl"] = true;
        context.Items["ProjectPulseActualEmail"] = "administrator@example.invalid";
        Check(!ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, ["ADMINISTRATOR"]), "Administrator is not a super-administrator alias");
        Check(!ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, ["ENGINEERING"]), "Cached authority does not override current roles");
        Check(!await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(context), "Email and cached authority cannot establish a session identity");
        Check(ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, ["SUPER_ADMINISTRATOR"]), "Actual super-administrator role preserved");
        context.Request.Headers["X-ProjectPulse-View-As-User"] = Guid.NewGuid().ToString();
        Check(!ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, ["SUPER_ADMINISTRATOR"]), "View-As cannot inherit permanent authority");
        Check(!await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(context), "View-As denied before any database access");

        string[] names = ["PROJECTPULSE_MICROSOFT_ENVIRONMENT", "PROJECTPULSE_ENVIRONMENT", "PROJECTPULSE_PUBLIC_URL", "PROJECTPULSE_PUBLIC_BASE_URL", "PROJECTPULSE_WEB_URL", "PUBLIC_URL", "PROJECTPULSE_SSO_MODE", "PROJECTPULSE_ENTRA_MODE", "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT"];
        var saved = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT", "production");
            var hostile = new DefaultHttpContext();
            hostile.Request.Host = new HostString("phd-west-test.onenecklab.com");
            hostile.Request.Headers["X-Forwarded-Host"] = "phd-west-test.onenecklab.com";
            hostile.Items["ProjectPulseMicrosoftEnvironment"] = "test";
            Check(MicrosoftEnvironmentRuntimeResolver.Resolve(hostile, "localhost") == "production", "Request host and request items cannot select a deployment identity");
            Check(Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_ENVIRONMENT") is null, "Resolver never mutates process configuration");
            Environment.SetEnvironmentVariable("PROJECTPULSE_MICROSOFT_ENVIRONMENT", "test");
            Check(MicrosoftEnvironmentRuntimeResolver.Resolve() == "test", "Explicit Microsoft deployment configuration takes priority");
            Environment.SetEnvironmentVariable("PROJECTPULSE_MICROSOFT_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("PROJECTPULSE_PUBLIC_URL", "https://phd-west-test.onenecklab.com");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Check(MicrosoftEnvironmentRuntimeResolver.Resolve() == "test", "Configured Test origin remains usable under ASP.NET Production");
            hostile.Request.Headers["X-Forwarded-Host"] = "other.ussignal.com";
            Check(ProjectPulsePublicOriginCompatibility.TryResolveProxyOrConfiguredOrigin(hostile, out var origin, out _)
                && origin.AbsoluteUri == "https://phd-west-test.onenecklab.com/", "Configured SSO origin wins over forwarded headers");
            Check(!ProjectPulsePublicOriginCompatibility.TryOrigin("https://other.onenecklab.com", hostile, out _), "Unconfigured environment host rejected");
            Check(!ProjectPulsePublicOriginCompatibility.TryOrigin("http://phd-west-test.onenecklab.com", hostile, out _), "Plaintext public origin rejected");
            Environment.SetEnvironmentVariable("PROJECTPULSE_PUBLIC_URL", null);
            Check(!ProjectPulsePublicOriginCompatibility.TryResolveProxyOrConfiguredOrigin(hostile, out _, out _), "Request host alone cannot establish public origin");
        }
        finally { foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value); }
        Console.WriteLine($"SECURITY_AUTHORITY_REGRESSIONS=PASS assertions={assertions}");
    }
}

using System.Security.Cryptography;

namespace ProjectTime.Api.Modules;

internal static class IntegrationSecretKeys
{
    internal static byte[]? Read(string variable)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured)) return null;
        try
        {
            var key = Convert.FromBase64String(configured);
            if (key.Length == 32) return key;
            CryptographicOperations.ZeroMemory(key);
        }
        catch (FormatException) { }
        return null;
    }

    internal static byte[]? Microsoft() => Read("PROJECTPULSE_MICROSOFT_INTEGRATION_SECRET_KEY");
}

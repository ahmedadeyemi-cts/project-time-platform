using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Projects the selected Test or Production SMTP credential pair into the
/// legacy runtime variables consumed by existing senders. Credential values
/// remain environment-backed and are never accepted from or returned to the
/// browser. Missing or non-SMTP selections clear stale runtime credentials.
/// </summary>
public static class MicrosoftSmtpCredentialProjectionCompatibility
{
    private const string RuntimePath = "/api/microsoft-integration/mail-runtime";
    private const string ConfigurationMarker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";

    // Capture the process-start legacy fallback before any projection mutates
    // the generic variables. Later environment switches never read projected
    // values as credential sources.
    private static readonly string OriginalActiveEnvironment = NormalizeEnvironment(
        Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
    private static readonly string OriginalLegacyUsername = First(
        Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME"),
        Environment.GetEnvironmentVariable("SMTP_USERNAME"));
    private static readonly string OriginalLegacyPassword = First(
        Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD"),
        Environment.GetEnvironmentVariable("SMTP_PASSWORD"));

    public static WebApplication UseMicrosoftSmtpCredentialProjectionCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPut(context.Request.Method)
                || !context.Request.Path.Equals(RuntimePath, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Authorization and request validation occur in the endpoint first.
            // Capture only its small sanitized JSON response; never buffer or
            // parse the unauthenticated request body.
            var originalResponseBody = context.Response.Body;
            await using var responseBuffer = new MemoryStream();
            context.Response.Body = responseBuffer;
            try
            {
                await next();

                responseBuffer.Position = 0;
                if (context.Response.StatusCode is >= 200 and < 300)
                {
                    var selection = await ReadValidatedSelectionFromResponseAsync(
                        responseBuffer,
                        context.RequestAborted);
                    if (selection is not null)
                    {
                        ApplySelectedCredential(
                            selection.EnvironmentMode,
                            selection.ProviderTarget);
                    }
                }

                responseBuffer.Position = 0;
                await responseBuffer.CopyToAsync(
                    originalResponseBody,
                    context.RequestAborted);
            }
            finally
            {
                context.Response.Body = originalResponseBody;
            }
        });

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(HydrateSelectedCredentialAsync));
        return app;
    }

    private static async Task<RuntimeSelection?> ReadValidatedSelectionFromResponseAsync(
        Stream responseBody,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                responseBody,
                cancellationToken: cancellationToken);
            var environmentMode = NormalizeEnvironment(
                JsonString(document.RootElement, "environmentMode"));
            var providerTarget = JsonString(
                document.RootElement,
                "providerTarget").Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(environmentMode)
                ? null
                : new(environmentMode, providerTarget);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplySelectedCredential(string environmentMode, string providerTarget)
    {
        if (providerTarget != "smtp_relay"
            || string.IsNullOrWhiteSpace(environmentMode))
        {
            ClearLegacyCredential();
            return;
        }

        var credential = ResolveCredential(environmentMode);
        Environment.SetEnvironmentVariable(
            "PROJECTPULSE_SMTP_USERNAME",
            string.IsNullOrWhiteSpace(credential.Username) ? null : credential.Username);
        Environment.SetEnvironmentVariable(
            "PROJECTPULSE_SMTP_PASSWORD",
            string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password);
    }

    private static SmtpCredential ResolveCredential(string environmentMode)
    {
        var prefix = environmentMode == "production"
            ? "PROJECTPULSE_PRODUCTION_SMTP_"
            : "PROJECTPULSE_TEST_SMTP_";

        return new(
            First(
                Environment.GetEnvironmentVariable(prefix + "USERNAME"),
                OriginalActiveEnvironment == environmentMode
                    ? OriginalLegacyUsername
                    : string.Empty),
            First(
                Environment.GetEnvironmentVariable(prefix + "PASSWORD"),
                OriginalActiveEnvironment == environmentMode
                    ? OriginalLegacyPassword
                    : string.Empty));
    }

    private static void ClearLegacyCredential()
    {
        Environment.SetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME", null);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD", null);
    }

    private static async Task HydrateSelectedCredentialAsync()
    {
        foreach (var delay in new[] { 700, 1800, 3500 })
        {
            try
            {
                await Task.Delay(delay);
                var selection = await ReadStoredSelectionAsync();
                if (selection is null) continue;
                ApplySelectedCredential(selection.EnvironmentMode, selection.ProviderTarget);
                return;
            }
            catch
            {
                // Existing environment configuration remains untouched until a
                // complete stored Module 065 selection can be resolved.
            }
        }
    }

    private static async Task<RuntimeSelection?> ReadStoredSelectionAsync()
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT document_json::text
            FROM projectpulse_native_admin_documents
            WHERE module_number='065'
              AND document_key='configuration'
            LIMIT 1;
            """, connection);
        var raw = Convert.ToString(await command.ExecuteScalarAsync());
        if (string.IsNullOrWhiteSpace(raw)) return null;

        using var document = JsonDocument.Parse(raw);
        if (!TryProperty(document.RootElement, "configuration", out var configuration)) return null;
        var notes = JsonString(configuration, "notes");
        if (!notes.StartsWith(ConfigurationMarker, StringComparison.Ordinal)) return null;

        using var stored = JsonDocument.Parse(notes[ConfigurationMarker.Length..]);
        var root = stored.RootElement;
        var environmentMode = NormalizeEnvironment(JsonString(root, "activeEnvironmentMode"));
        if (!TryProperty(root, "mail", out var mail)) return null;
        var providerTarget = JsonString(mail, "providerTarget").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(environmentMode)
            ? null
            : new(environmentMode, providerTarget);
    }

    private static string BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var databasePassword = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(databasePassword)) return string.Empty;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = databasePassword,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private static string NormalizeEnvironment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "test" or "development" or "dev" or "onenecklab" => "test",
            "production" or "prod" or "ussignal" => "production",
            _ => string.Empty
        };
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string JsonString(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value)
            || value.ValueKind != JsonValueKind.String) return string.Empty;
        return value.GetString()?.Trim() ?? string.Empty;
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record RuntimeSelection(string EnvironmentMode, string ProviderTarget);
    private sealed record SmtpCredential(string Username, string Password);
}

using System.Text;
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

    public static WebApplication UseMicrosoftSmtpCredentialProjectionCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            RuntimeSelection? selection = null;
            if (HttpMethods.IsPut(context.Request.Method)
                && context.Request.Path.Equals(RuntimePath, StringComparison.OrdinalIgnoreCase))
            {
                selection = await ReadSelectionAsync(context);
            }

            await next();

            if (selection is not null
                && context.Response.StatusCode is >= 200 and < 300)
            {
                ApplySelectedCredential(selection.EnvironmentMode, selection.ProviderTarget);
            }
        });

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(HydrateSelectedCredentialAsync));
        return app;
    }

    private static async Task<RuntimeSelection?> ReadSelectionAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        try
        {
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var raw = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            using var document = JsonDocument.Parse(raw);
            return new(
                NormalizeEnvironment(JsonString(document.RootElement, "environmentMode")),
                JsonString(document.RootElement, "providerTarget").Trim().ToLowerInvariant());
        }
        catch
        {
            context.Request.Body.Position = 0;
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
        var activeMode = NormalizeEnvironment(Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
        var prefix = environmentMode == "production"
            ? "PROJECTPULSE_PRODUCTION_SMTP_"
            : "PROJECTPULSE_TEST_SMTP_";

        return new(
            First(
                Environment.GetEnvironmentVariable(prefix + "USERNAME"),
                activeMode == environmentMode
                    ? Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME")
                    : string.Empty,
                activeMode == environmentMode
                    ? Environment.GetEnvironmentVariable("SMTP_USERNAME")
                    : string.Empty),
            First(
                Environment.GetEnvironmentVariable(prefix + "PASSWORD"),
                activeMode == environmentMode
                    ? Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD")
                    : string.Empty,
                activeMode == environmentMode
                    ? Environment.GetEnvironmentVariable("SMTP_PASSWORD")
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
        return new(environmentMode, providerTarget);
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
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return string.Empty;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
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

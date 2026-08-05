using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Canonical AI database configuration boundary. Every AI repository and the
/// release-candidate verifier resolve the same aliases and PTP_DB_* contract.
/// Multiple declarations are accepted only when they identify the exact same
/// effective connection; ambiguity fails closed instead of silently selecting
/// the first credential in process-environment order.
/// </summary>
public static class ProjectPulseAiDatabaseConnection
{
    public static readonly string[] DirectAliases =
    [
        "ConnectionStrings__DefaultConnection",
        "ConnectionStrings__ProjectPulse",
        "ConnectionStrings__ProjectTime",
        "PROJECTPULSE_CONNECTION_STRING",
        "PROJECTTIME_DATABASE_CONNECTION",
        "PROJECTPULSE_DB_CONNECTION",
        "PROJECTTIME_DB_CONNECTION"
    ];

    public static string? Resolve() => ResolveEvidence().ConnectionString;

    public static ProjectPulseAiDatabaseConnectionEvidence ResolveEvidence()
    {
        var candidates = new List<(string Source, NpgsqlConnectionStringBuilder Builder)>();
        foreach (var name in DirectAliases)
        {
            var value = Environment.GetEnvironmentVariable(name)?.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;
            try
            {
                candidates.Add((name, Harden(new NpgsqlConnectionStringBuilder(value))));
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                throw new InvalidOperationException($"{name} is not a valid PostgreSQL connection string.", exception);
            }
        }

        var componentNames = new[]
        {
            "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD"
        };
        var components = componentNames.ToDictionary(
            name => name,
            name => Environment.GetEnvironmentVariable(name)?.Trim() ?? string.Empty,
            StringComparer.Ordinal);
        var anyComponents = components.Values.Any(value => value.Length > 0);
        if (anyComponents)
        {
            var missing = new[] { "PTP_DB_HOST", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD" }
                .Where(name => components[name].Length == 0)
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    $"The PTP_DB_* database contract is incomplete; missing {string.Join(", ", missing)}.");
            if (components["PTP_DB_PORT"].Length > 0
                && (!int.TryParse(components["PTP_DB_PORT"], out var parsedPort)
                    || parsedPort is < 1 or > 65535))
            {
                throw new InvalidOperationException("PTP_DB_PORT must be an integer from 1 through 65535.");
            }

            candidates.Add(("PTP_DB_*", Harden(new NpgsqlConnectionStringBuilder
            {
                Host = components["PTP_DB_HOST"],
                Port = int.TryParse(components["PTP_DB_PORT"], out var port) ? port : 5432,
                Database = components["PTP_DB_NAME"],
                Username = components["PTP_DB_USER"],
                Password = components["PTP_DB_PASSWORD"]
            })));
        }

        if (candidates.Count == 0)
            return ProjectPulseAiDatabaseConnectionEvidence.Unconfigured;

        var selected = candidates[0];
        var selectedSecretFingerprint = SecretFingerprint(selected.Builder);
        var conflicts = candidates.Skip(1)
            .Where(candidate => !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(selectedSecretFingerprint),
                Convert.FromHexString(SecretFingerprint(candidate.Builder))))
            .Select(candidate => candidate.Source)
            .ToArray();
        if (conflicts.Length > 0)
        {
            throw new InvalidOperationException(
                $"Conflicting AI database declarations were rejected ({selected.Source} versus {string.Join(", ", conflicts)}). Keep exactly one source or make every declaration identical.");
        }

        var nonSecretIdentity = string.Join('|', new[]
        {
            selected.Builder.Host.Trim().ToLowerInvariant(),
            selected.Builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            selected.Builder.Database.Trim().ToLowerInvariant(),
            selected.Builder.Username.Trim().ToLowerInvariant(),
            selected.Builder.SslMode.ToString().ToLowerInvariant()
        });
        return new ProjectPulseAiDatabaseConnectionEvidence(
            selected.Builder.ConnectionString,
            selected.Source,
            candidates.Select(candidate => candidate.Source).ToArray(),
            Fingerprint(nonSecretIdentity),
            Fingerprint(selected.Builder.Username.Trim().ToLowerInvariant()));
    }

    public static string FingerprintRole(string? role) =>
        Fingerprint((role ?? string.Empty).Trim().ToLowerInvariant());

    private static NpgsqlConnectionStringBuilder Harden(NpgsqlConnectionStringBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException(
                "AI database configuration requires a host, database, and username.");
        }
        builder.IncludeErrorDetail = false;
        builder.Pooling = true;
        builder.MinPoolSize = 0;
        builder.MaxPoolSize = Math.Clamp(builder.MaxPoolSize, 1, 20);
        return builder;
    }

    // This digest is used only for equality. It is never returned or logged.
    private static string SecretFingerprint(NpgsqlConnectionStringBuilder builder) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ConnectionString)))
            .ToLowerInvariant();

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..16];
}

public sealed record ProjectPulseAiDatabaseConnectionEvidence(
    string? ConnectionString,
    string Source,
    IReadOnlyList<string> EquivalentSources,
    string DatabaseFingerprint,
    string ConfiguredRoleFingerprint)
{
    public bool Configured => !string.IsNullOrWhiteSpace(ConnectionString);
    public static ProjectPulseAiDatabaseConnectionEvidence Unconfigured { get; } =
        new(null, "unconfigured", [], string.Empty, string.Empty);
}

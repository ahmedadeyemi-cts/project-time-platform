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
        var directCandidates = new List<(string Source, NpgsqlConnectionStringBuilder Builder)>();
        foreach (var name in DirectAliases)
        {
            var value = Environment.GetEnvironmentVariable(name)?.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;
            try
            {
                directCandidates.Add((name, Harden(new NpgsqlConnectionStringBuilder(value))));
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
        (string Source, NpgsqlConnectionStringBuilder Builder)? componentCandidate = null;
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

            componentCandidate = ("PTP_DB_*", Harden(new NpgsqlConnectionStringBuilder
            {
                Host = components["PTP_DB_HOST"],
                Port = int.TryParse(components["PTP_DB_PORT"], out var port) ? port : 5432,
                Database = components["PTP_DB_NAME"],
                Username = components["PTP_DB_USER"],
                Password = components["PTP_DB_PASSWORD"]
            }));
        }

        if (directCandidates.Count == 0 && componentCandidate is null)
            return ProjectPulseAiDatabaseConnectionEvidence.Unconfigured;

        // Full connection-string aliases must remain byte-for-byte equivalent
        // after Npgsql normalization. This preserves fail-closed handling for
        // conflicting TLS, certificate, timeout, search-path, or application
        // behavior declared by two equally expressive sources.
        var selected = directCandidates.FirstOrDefault();
        var selectedSecretFingerprint = selected.Builder is null
            ? string.Empty
            : FullConnectionFingerprint(selected.Builder);
        var conflicts = directCandidates.Skip(1)
            .Where(candidate => !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(selectedSecretFingerprint),
                Convert.FromHexString(FullConnectionFingerprint(candidate.Builder))))
            .Select(candidate => candidate.Source)
            .ToList();

        // The protected Container Apps deployment intentionally carries both
        // legacy full aliases and the PTP_DB_* component contract. Components
        // cannot express every Npgsql transport option, so compare only the
        // credential and destination identity they do express. When they
        // agree, keep the full connection string so its TLS/options are not
        // weakened. A host, port, database, username, or password mismatch is
        // still rejected.
        if (componentCandidate is { } component && selected.Builder is not null
            && !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(CoreCredentialFingerprint(selected.Builder)),
                Convert.FromHexString(CoreCredentialFingerprint(component.Builder))))
        {
            conflicts.Add(component.Source);
        }
        if (conflicts.Count > 0)
        {
            throw new InvalidOperationException(
                $"Conflicting AI database declarations were rejected ({selected.Source} versus {string.Join(", ", conflicts)}). Keep exactly one source or make every declaration identical.");
        }

        if (selected.Builder is null)
            selected = componentCandidate!.Value;

        var equivalentSources = directCandidates
            .Select(candidate => candidate.Source)
            .Concat(componentCandidate is null
                ? Array.Empty<string>()
                : [componentCandidate.Value.Source])
            .ToArray();

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
            equivalentSources,
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
    private static string FullConnectionFingerprint(NpgsqlConnectionStringBuilder builder) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ConnectionString)))
            .ToLowerInvariant();

    private static string CoreCredentialFingerprint(NpgsqlConnectionStringBuilder builder)
    {
        var identity = new NpgsqlConnectionStringBuilder
        {
            Host = builder.Host.Trim().ToLowerInvariant(),
            Port = builder.Port,
            Database = builder.Database.Trim(),
            Username = builder.Username.Trim(),
            Password = builder.Password
        }.ConnectionString;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
    }

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

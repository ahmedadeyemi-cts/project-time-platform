using Npgsql;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Resolves the AI configuration stores against the same database contract used
/// by the API. Direct connection strings remain supported; Container Apps can
/// use the standard PTP_DB_* secret references without a second DB credential.
/// </summary>
internal static class ProjectPulseAiDatabaseConnection
{
    public static string? Resolve()
    {
        var direct = new[]
            {
                "ConnectionStrings__DefaultConnection",
                "ConnectionStrings__ProjectPulse",
                "ConnectionStrings__ProjectTime",
                "PROJECTPULSE_CONNECTION_STRING",
                "PROJECTTIME_DATABASE_CONNECTION",
                "PROJECTPULSE_DB_CONNECTION",
                "PROJECTTIME_DB_CONNECTION"
            }
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (new[] { host, database, username, password }.Any(string.IsNullOrWhiteSpace))
            return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port)
                ? port
                : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 5
        }.ConnectionString;
    }
}

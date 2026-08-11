using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class AnalyticsCenterDirectoryLoader
{
    internal static async Task<AnalyticsDirectorySnapshot> LoadAsync(
        FinancialOperationsTruthSnapshot truth,
        CancellationToken cancellationToken)
    {
        var connectionString = ProjectFinancialTruthModule.FinancialOperationsConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return AnalyticsDirectorySnapshot.Fallback(
                truth.Projects,
                "DIRECTORY_CONFIGURATION_UNAVAILABLE",
                "Customer and project choices are available from the authorized portfolio. Team-directory choices are temporarily unavailable.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var customers = await LoadCustomersAsync(connection, truth, cancellationToken);
            var teams = await LoadTeamsAsync(connection, truth, cancellationToken);
            return new AnalyticsDirectorySnapshot(
                customers,
                teams,
                new EnterpriseReportSourceState(
                    "analytics_directory",
                    "Customer, project, people, and team directory",
                    "healthy",
                    false,
                    customers.Length + teams.Length,
                    "Filter choices were loaded from the role-scoped Pulse customer and team directories.",
                    string.Empty,
                    DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            return AnalyticsDirectorySnapshot.Fallback(
                truth.Projects,
                EnterpriseReportingSourceLoader.Diagnostic(exception),
                "Customer and project choices are available from the authorized portfolio. The broader directory could not be loaded, so team choices may be incomplete.");
        }
    }

    private static async Task<AnalyticsCustomerOption[]> LoadCustomersAsync(
        NpgsqlConnection connection,
        FinancialOperationsTruthSnapshot truth,
        CancellationToken cancellationToken)
    {
        var visibleClientIds = truth.Projects
            .Where(project => project.ClientId.HasValue)
            .Select(project => project.ClientId!.Value)
            .Distinct()
            .ToArray();

        await using var command = new NpgsqlCommand("""
            SELECT client.client_id,
                   COALESCE(NULLIF(client.client_name, ''), client.client_id::text) AS client_name,
                   COALESCE(to_jsonb(client)->>'client_code', '') AS client_code
            FROM clients client
            WHERE COALESCE(NULLIF(to_jsonb(client)->>'is_active', '')::boolean, TRUE) = TRUE
              AND (@broad OR client.client_id = ANY(@client_ids))
            ORDER BY client_name, client_code;
            """, connection);
        command.Parameters.AddWithValue("broad", truth.Actor.Broad);
        command.Parameters.Add(new NpgsqlParameter(
            "client_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = visibleClientIds
        });

        var rows = new List<AnalyticsCustomerOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AnalyticsCustomerOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2)));
        }
        return rows.ToArray();
    }

    private static async Task<AnalyticsTeamOption[]> LoadTeamsAsync(
        NpgsqlConnection connection,
        FinancialOperationsTruthSnapshot truth,
        CancellationToken cancellationToken)
    {
        var visibleUserIds = VisibleUserIds(truth);
        await using var command = new NpgsqlCommand("""
            SELECT team.team_id,
                   COALESCE(
                       NULLIF(to_jsonb(team)->>'team_name', ''),
                       NULLIF(to_jsonb(team)->>'name', ''),
                       team.team_id::text
                   ) AS team_name,
                   array_agg(DISTINCT membership.user_id ORDER BY membership.user_id) AS member_user_ids
            FROM teams team
            JOIN team_memberships membership
              ON membership.team_id = team.team_id
            JOIN app_users app_user
              ON app_user.user_id = membership.user_id
             AND app_user.is_active = TRUE
            WHERE COALESCE(NULLIF(to_jsonb(team)->>'is_active', '')::boolean, TRUE) = TRUE
              AND COALESCE(
                    NULLIF(to_jsonb(membership)->>'effective_start_date', '')::date,
                    CURRENT_DATE
                  ) <= CURRENT_DATE
              AND (
                    NULLIF(to_jsonb(membership)->>'effective_end_date', '') IS NULL
                    OR NULLIF(to_jsonb(membership)->>'effective_end_date', '')::date >= CURRENT_DATE
                  )
              AND (@broad OR membership.user_id = ANY(@visible_user_ids))
            GROUP BY team.team_id, team_name
            ORDER BY team_name;
            """, connection);
        command.Parameters.AddWithValue("broad", truth.Actor.Broad);
        command.Parameters.Add(new NpgsqlParameter(
            "visible_user_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = visibleUserIds
        });

        var rows = new List<AnalyticsTeamOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AnalyticsTeamOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<Guid[]>(2)));
        }
        return rows.ToArray();
    }

    private static Guid[] VisibleUserIds(FinancialOperationsTruthSnapshot truth) => truth.Projects
        .SelectMany(project => project.Engineers.Select(engineer => engineer.UserId)
            .Concat(project.ProjectManagerUserId.HasValue
                ? [project.ProjectManagerUserId.Value]
                : Array.Empty<Guid>())
            .Concat(project.ProjectTeamCoordinator is null
                ? Array.Empty<Guid>()
                : [project.ProjectTeamCoordinator.UserId])
            .Concat(project.SolutionArchitect is null
                ? Array.Empty<Guid>()
                : [project.SolutionArchitect.UserId])
            .Concat(project.AccountExecutive is null
                ? Array.Empty<Guid>()
                : [project.AccountExecutive.UserId]))
        .Append(truth.Actor.EffectiveUserId)
        .Distinct()
        .ToArray();
}

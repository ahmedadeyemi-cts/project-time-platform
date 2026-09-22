using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

internal static class Module025ServiceScopeWorkspace
{
    internal static bool SourceChanged(Module025EngagementRow current, string? scope, string overview) =>
        scope is not null ? current.ServiceScope is null || scope != current.ServiceScope
            : current.ServiceScope is null && overview != current.ServiceOverview;

    // Never overwrite an author's reviewed overview while refreshing its AI proposal.
    internal static string WorkingOverview(Module025EngagementRow current, string proposal) =>
        current.ServiceOverviewManuallyEdited ? current.ServiceOverview : proposal;

    internal static async Task SaveInputAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid engagementId, string scope, bool overviewEdited, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE module025_sow_gsd_engagements SET service_scope=@scope,
                service_overview_manually_edited=@edited WHERE engagement_id=@id;
            """, connection, transaction);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("edited", overviewEdited);
        command.Parameters.AddWithValue("id", engagementId);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task SaveOverviewAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Module025EngagementRow current, string proposal, CancellationToken token)
    {
        if (current.ServiceScope is null) return; // Preserve legacy record semantics.
        await using var command = new NpgsqlCommand("""
            UPDATE module025_sow_gsd_engagements SET service_overview=@overview,
                generated_service_overview=@proposal WHERE engagement_id=@id;
            """, connection, transaction);
        command.Parameters.AddWithValue("overview", WorkingOverview(current, proposal));
        command.Parameters.AddWithValue("proposal", proposal);
        command.Parameters.AddWithValue("id", current.EngagementId);
        await command.ExecuteNonQueryAsync(token);
    }
}

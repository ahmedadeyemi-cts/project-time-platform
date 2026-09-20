using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>Archive membership follows the canonical project lifecycle. Plan history never moves or disappears.</summary>
internal static class ProjectFlowHiveLifecycle
{
    internal static bool IsArchived(string? status) => status?.Trim().ToLowerInvariant() is
        "closed" or "completed" or "cancelled" or "canceled" or "archived";

    // Hold the project row through a plan write so closure and a late AI save serialize.
    internal static async Task<bool> LockActiveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT status FROM projects WHERE project_id=@project FOR SHARE;", connection, transaction);
        command.Parameters.AddWithValue("project", projectId);
        return await command.ExecuteScalarAsync(token) is string status && !IsArchived(status);
    }

    internal static IResult Archived() => Results.Conflict(new
    {
        status = "flowhive_project_archived", stateChanged = false,
        message = "This project is closed and its plan is archived. You can view its plan and history. Reopen the project in Work Register before making changes."
    });
}

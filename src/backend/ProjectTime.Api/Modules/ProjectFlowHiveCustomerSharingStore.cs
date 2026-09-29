using Npgsql;

namespace ProjectTime.Api.Modules;

// Called only after the existing project-scoped CustomerShare authorization succeeds.
internal static class ProjectFlowHiveCustomerSharingStore
{
    internal static async Task<bool> EnableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, Guid actorId, CancellationToken cancellationToken)
    {
        // Existing commercial values are never supplied or overwritten. New rows use the
        // existing migration086 defaults. The false->true transition is atomic and idempotent.
        await using var command = new NpgsqlCommand("""
            INSERT INTO project_flowhive_project_controls(project_id,customer_sharing_enabled,updated_by_user_id)
            VALUES(@project,TRUE,@actor)
            ON CONFLICT(project_id) DO UPDATE
              SET customer_sharing_enabled=TRUE,updated_by_user_id=EXCLUDED.updated_by_user_id,updated_at=NOW()
              WHERE project_flowhive_project_controls.customer_sharing_enabled=FALSE
            RETURNING project_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("actor", actorId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid;
    }
}

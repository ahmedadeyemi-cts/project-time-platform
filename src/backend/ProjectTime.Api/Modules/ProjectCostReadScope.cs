using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class ProjectCostReadScope
{
    internal static async Task<Guid[]> LoadAsync(HttpContext context, NpgsqlConnection connection)
    {
        var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
        if (access is null) return [];
        var sql = $$"""
            WITH {{EnterpriseGovernanceAccessResolver.TeamMembersCte}}
            SELECT p.project_id FROM projects p
            WHERE @broad OR p.project_manager_user_id=@user_id
                OR (@team_scope AND (
                    p.project_manager_user_id IN (SELECT user_id FROM scoped_team_members)
                    OR EXISTS (SELECT 1 FROM project_assignments a
                        WHERE a.project_id=p.project_id
                          AND a.user_id IN (SELECT user_id FROM scoped_team_members)
                          AND a.effective_start_date<=CURRENT_DATE
                          AND (a.effective_end_date IS NULL OR a.effective_end_date>=CURRENT_DATE)
                          AND COALESCE(to_jsonb(a)->>'module001a_closeout_status','active')='active')
                ))
                OR (@sales AND (p.account_executive_user_id=@user_id OR p.solution_architect_user_id=@user_id));
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access);
        command.Parameters.AddWithValue("broad", access.IsBroadScope || access.Roles.Overlaps(new[] { "EXECUTIVE", "EXECUTIVE_LEADERSHIP" }));
        command.Parameters.AddWithValue("sales", access.Roles.Overlaps(new[] { "SOLUTION_ARCHITECT", "SA", "SAA", "ACCOUNT_EXECUTIVE", "ACCOUNT_EXECUTIVES", "SALES" }));
        var projects = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted)) projects.Add(reader.GetGuid(0));
        return projects.ToArray();
    }
}

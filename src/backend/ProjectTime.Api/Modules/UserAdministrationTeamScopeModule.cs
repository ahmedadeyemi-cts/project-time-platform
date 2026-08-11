using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 009 manager-to-multiple-team administration. A team has at most one
/// active manager assignment, while a manager may own multiple teams. Saving
/// scope reconciles each active team member's manager_email so existing manager
/// authorization continues to use authoritative Pulse user data.
/// </summary>
public static class UserAdministrationTeamScopeModule
{
    private const string ModuleNumber = "009";
    private const string AssignmentTable = "user_admin_manager_team_assignments";
    private const string MigrationId = "048_admin_audit_and_manager_team_scope";
    private const int MaximumTeamsPerManager = 50;

    public static WebApplication MapUserAdministrationTeamScopeEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/admin/user-admin/manager-team-assignments",
            (Func<HttpContext, Task<IResult>>)GetAssignmentsAsync);
        app.MapPut(
            "/api/admin/user-admin/manager-team-assignments/{managerUserId:guid}",
            (Guid managerUserId, HttpContext context) =>
                SaveAssignmentsAsync(managerUserId, context));

        return app;
    }

    private static async Task<IResult> GetAssignmentsAsync(HttpContext context)
    {
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);

        var migrationReady = await AdminExperienceCommon.TableExistsAsync(
            connection,
            AssignmentTable,
            cancellationToken: context.RequestAborted);
        var managers = await ReadManagersAsync(connection, context.RequestAborted);
        var teams = await ReadTeamsAsync(connection, context.RequestAborted);
        var assignments = migrationReady
            ? await ReadAssignmentsAsync(connection, context.RequestAborted)
            : [];

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "manager_team_assignments_loaded",
            migration = new
            {
                id = MigrationId,
                ready = migrationReady
            },
            policy = new
            {
                multipleTeamsPerManager = true,
                oneActiveManagerPerTeam = true,
                memberManagerEmailReconciledOnSave = true,
                managerAuthoritySource = "app_users.manager_email"
            },
            managers,
            teams = teams.Select(team => new
            {
                team.TeamName,
                team.MemberCount,
                team.ActiveMemberCount,
                activeManager = assignments
                    .Where(assignment => assignment.IsActive)
                    .Where(assignment => assignment.TeamName.Equals(
                        team.TeamName,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(assignment => new
                    {
                        assignment.ManagerUserId,
                        assignment.ManagerEmail,
                        assignment.ManagerDisplayName
                    })
                    .FirstOrDefault()
            }),
            assignments = assignments.Select(assignment => new
            {
                assignment.AssignmentId,
                assignment.ManagerUserId,
                assignment.ManagerEmail,
                assignment.ManagerDisplayName,
                assignment.TeamName,
                assignment.IsActive,
                assignment.AssignmentReason,
                assignment.UpdatedAt
            })
        });
    }

    private static async Task<IResult> SaveAssignmentsAsync(
        Guid managerUserId,
        HttpContext context)
    {
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (AdminExperienceCommon.IsViewAs(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "view_as_read_only",
                message = "Exit Administrator View-As before changing manager team scope."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        TeamScopeRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<TeamScopeRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid manager team-scope request is required.");
        }

        var requestedTeams = (request?.TeamNames ?? [])
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requestedTeams.Count > MaximumTeamsPerManager)
        {
            return InvalidRequest($"A manager may be assigned to no more than {MaximumTeamsPerManager} teams.");
        }

        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 4)
        {
            return InvalidRequest("A brief reason is required for manager team-scope changes.");
        }

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        if (!await AdminExperienceCommon.TableExistsAsync(
                connection,
                AssignmentTable,
                cancellationToken: context.RequestAborted))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "migration_required",
                migration = MigrationId,
                message = "Manager multi-team scope is not installed yet. Apply the approved migration before saving assignments."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var manager = await ReadManagerAsync(
            connection,
            managerUserId,
            context.RequestAborted);
        if (manager is null)
        {
            return Results.NotFound(new
            {
                module = ModuleNumber,
                status = "manager_not_found",
                message = "The selected active Pulse manager was not found."
            });
        }

        if (!manager.RoleCodes.Any(IsManagerRole))
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "manager_role_required",
                message = "Assign a Manager, Project Manager, Management, or Lead role before assigning team scope."
            });
        }

        var availableTeams = await ReadTeamsAsync(connection, context.RequestAborted);
        var canonicalTeams = availableTeams.ToDictionary(
            team => team.TeamName,
            team => team.TeamName,
            StringComparer.OrdinalIgnoreCase);
        var unknownTeams = requestedTeams
            .Where(team => !canonicalTeams.ContainsKey(team))
            .ToArray();
        if (unknownTeams.Length > 0)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "unknown_team",
                unknownTeams,
                message = "Select only teams currently available in User Administration."
            });
        }

        requestedTeams = requestedTeams
            .Select(team => canonicalTeams[team])
            .OrderBy(team => team, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var transaction = await connection.BeginTransactionAsync(
            context.RequestAborted);
        try
        {
            var existingTeams = await ReadManagerTeamsForUpdateAsync(
                connection,
                transaction,
                managerUserId,
                context.RequestAborted);
            var removedTeams = existingTeams
                .Where(team => !requestedTeams.Contains(team, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var reassignedTeams = new List<string>();
            var membersUpdated = 0;
            var membersCleared = 0;

            foreach (var teamName in requestedTeams)
            {
                var priorManagers = await ReadActiveTeamManagersForUpdateAsync(
                    connection,
                    transaction,
                    teamName,
                    context.RequestAborted);
                if (priorManagers.Any(prior => prior != managerUserId))
                {
                    reassignedTeams.Add(teamName);
                }

                await using (var deactivateOthers = new NpgsqlCommand("""
                    UPDATE user_admin_manager_team_assignments
                    SET is_active = FALSE,
                        updated_at = NOW(),
                        assignment_reason = @reason
                    WHERE lower(team_name) = lower(@team_name)
                      AND manager_user_id <> @manager_user_id
                      AND is_active = TRUE;
                    """, connection, transaction))
                {
                    deactivateOthers.Parameters.AddWithValue("team_name", teamName);
                    deactivateOthers.Parameters.AddWithValue("manager_user_id", managerUserId);
                    deactivateOthers.Parameters.AddWithValue("reason", $"Reassigned: {reason}");
                    await deactivateOthers.ExecuteNonQueryAsync(context.RequestAborted);
                }

                await using (var upsert = new NpgsqlCommand("""
                    INSERT INTO user_admin_manager_team_assignments (
                        manager_user_id,
                        manager_email,
                        team_name,
                        is_active,
                        assigned_by_user_id,
                        assignment_reason,
                        created_at,
                        updated_at
                    )
                    VALUES (
                        @manager_user_id,
                        @manager_email,
                        @team_name,
                        TRUE,
                        @assigned_by_user_id,
                        @reason,
                        NOW(),
                        NOW()
                    )
                    ON CONFLICT (manager_user_id, team_name)
                    DO UPDATE SET
                        manager_email = EXCLUDED.manager_email,
                        is_active = TRUE,
                        assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                        assignment_reason = EXCLUDED.assignment_reason,
                        updated_at = NOW();
                    """, connection, transaction))
                {
                    upsert.Parameters.AddWithValue("manager_user_id", managerUserId);
                    upsert.Parameters.AddWithValue("manager_email", manager.Email);
                    upsert.Parameters.AddWithValue("team_name", teamName);
                    upsert.Parameters.AddWithValue("assigned_by_user_id", access.Context.UserId);
                    upsert.Parameters.AddWithValue("reason", reason);
                    await upsert.ExecuteNonQueryAsync(context.RequestAborted);
                }

                await using var reconcileMembers = new NpgsqlCommand("""
                    UPDATE app_users
                    SET manager_email = @manager_email,
                        updated_at = NOW()
                    WHERE user_id <> @manager_user_id
                      AND COALESCE(is_active, TRUE) = TRUE
                      AND lower(COALESCE(team_name, '')) = lower(@team_name)
                      AND lower(COALESCE(manager_email, '')) IS DISTINCT FROM lower(@manager_email);
                    """, connection, transaction);
                reconcileMembers.Parameters.AddWithValue("manager_email", manager.Email);
                reconcileMembers.Parameters.AddWithValue("manager_user_id", managerUserId);
                reconcileMembers.Parameters.AddWithValue("team_name", teamName);
                membersUpdated += await reconcileMembers.ExecuteNonQueryAsync(
                    context.RequestAborted);
            }

            foreach (var teamName in removedTeams)
            {
                await using (var deactivate = new NpgsqlCommand("""
                    UPDATE user_admin_manager_team_assignments
                    SET is_active = FALSE,
                        assignment_reason = @reason,
                        updated_at = NOW()
                    WHERE manager_user_id = @manager_user_id
                      AND lower(team_name) = lower(@team_name)
                      AND is_active = TRUE;
                    """, connection, transaction))
                {
                    deactivate.Parameters.AddWithValue("manager_user_id", managerUserId);
                    deactivate.Parameters.AddWithValue("team_name", teamName);
                    deactivate.Parameters.AddWithValue("reason", $"Removed: {reason}");
                    await deactivate.ExecuteNonQueryAsync(context.RequestAborted);
                }

                var replacement = await ReadReplacementManagerEmailAsync(
                    connection,
                    transaction,
                    teamName,
                    managerUserId,
                    context.RequestAborted);
                await using var clearMembers = new NpgsqlCommand("""
                    UPDATE app_users
                    SET manager_email = @replacement_email,
                        updated_at = NOW()
                    WHERE lower(COALESCE(team_name, '')) = lower(@team_name)
                      AND lower(COALESCE(manager_email, '')) = lower(@manager_email);
                    """, connection, transaction);
                clearMembers.Parameters.AddWithValue("replacement_email", replacement);
                clearMembers.Parameters.AddWithValue("team_name", teamName);
                clearMembers.Parameters.AddWithValue("manager_email", manager.Email);
                membersCleared += await clearMembers.ExecuteNonQueryAsync(
                    context.RequestAborted);
            }

            await AdminExperienceCommon.WriteAuditAsync(
                connection,
                transaction,
                "user_administration",
                "success",
                "MANAGER_TEAM_SCOPE_UPDATED",
                access.Context.UserId,
                access.Context.Email,
                "manager_user",
                managerUserId.ToString(),
                manager.DisplayName,
                ModuleNumber,
                AssignmentTable,
                managerUserId.ToString(),
                $"Manager team scope updated for {manager.DisplayName}.",
                new
                {
                    managerUserId,
                    manager.Email,
                    selectedTeams = requestedTeams,
                    removedTeams,
                    reassignedTeams,
                    membersUpdated,
                    membersCleared,
                    reason
                },
                AdminExperienceCommon.ClientIp(context),
                context.TraceIdentifier,
                context.RequestAborted);

            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "manager_team_scope_saved",
                manager = new
                {
                    manager.UserId,
                    manager.Email,
                    manager.DisplayName,
                    manager.RoleCodes
                },
                selectedTeams = requestedTeams,
                removedTeams,
                reassignedTeams,
                membersUpdated,
                membersCleared,
                transactionCommitted = true,
                message = $"Saved {requestedTeams.Count} team assignment(s) and reconciled manager email for {membersUpdated + membersCleared} team member record(s)."
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "manager_team_scope_save_failed",
                transactionCommitted = false,
                message = "Manager team scope could not be saved. No partial changes were committed."
            }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<List<ManagerOption>> ReadManagersAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                u.user_id,
                COALESCE(u.email, ''),
                COALESCE(u.display_name, u.email, 'Pulse manager'),
                COALESCE(
                    array_agg(DISTINCT r.role_code)
                        FILTER (WHERE r.role_code IS NOT NULL),
                    ARRAY[]::varchar[]
                )
            FROM app_users u
            JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id
             AND ura.is_active = TRUE
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
             AND r.is_active = TRUE
            WHERE COALESCE(u.is_active, TRUE) = TRUE
            GROUP BY u.user_id, u.email, u.display_name
            ORDER BY COALESCE(u.display_name, u.email);
            """, connection);

        var result = new List<ManagerOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var roles = reader.GetFieldValue<string[]>(3);
            if (!roles.Any(IsManagerRole)) continue;
            result.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                roles.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        return result;
    }

    private static async Task<ManagerOption?> ReadManagerAsync(
        NpgsqlConnection connection,
        Guid managerUserId,
        CancellationToken cancellationToken)
    {
        var managers = await ReadManagersAsync(connection, cancellationToken);
        return managers.FirstOrDefault(manager => manager.UserId == managerUserId);
    }

    private static async Task<List<TeamOption>> ReadTeamsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var teamsTableExists = await AdminExperienceCommon.TableExistsAsync(
            connection,
            "teams",
            cancellationToken: cancellationToken);
        var union = teamsTableExists
            ? """
              UNION
              SELECT NULLIF(trim(COALESCE(to_jsonb(t)->>'team_name', to_jsonb(t)->>'name', '')), '')
              FROM teams t
              """
            : string.Empty;

        await using var command = new NpgsqlCommand($"""
            WITH team_names AS (
                SELECT NULLIF(trim(COALESCE(to_jsonb(u)->>'team_name', '')), '') AS team_name
                FROM app_users u
                {union}
            )
            SELECT
                names.team_name,
                COUNT(users.user_id)::int AS member_count,
                COUNT(users.user_id) FILTER (WHERE COALESCE(users.is_active, TRUE) = TRUE)::int AS active_member_count
            FROM (
                SELECT DISTINCT team_name
                FROM team_names
                WHERE team_name IS NOT NULL
            ) names
            LEFT JOIN app_users users
              ON lower(COALESCE(users.team_name, '')) = lower(names.team_name)
            GROUP BY names.team_name
            ORDER BY names.team_name;
            """, connection);

        var result = new List<TeamOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2)));
        }

        return result;
    }

    private static async Task<List<ManagerTeamAssignment>> ReadAssignmentsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                assignment.user_admin_manager_team_assignment_id,
                assignment.manager_user_id,
                assignment.manager_email,
                COALESCE(manager.display_name, manager.email, assignment.manager_email),
                assignment.team_name,
                assignment.is_active,
                assignment.assignment_reason,
                assignment.updated_at
            FROM user_admin_manager_team_assignments assignment
            LEFT JOIN app_users manager
              ON manager.user_id = assignment.manager_user_id
            ORDER BY assignment.is_active DESC,
                     COALESCE(manager.display_name, manager.email),
                     assignment.team_name;
            """, connection);

        var result = new List<ManagerTeamAssignment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return result;
    }

    private static async Task<List<string>> ReadManagerTeamsForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid managerUserId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT team_name
            FROM user_admin_manager_team_assignments
            WHERE manager_user_id = @manager_user_id
              AND is_active = TRUE
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("manager_user_id", managerUserId);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<List<Guid>> ReadActiveTeamManagersForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string teamName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT manager_user_id
            FROM user_admin_manager_team_assignments
            WHERE lower(team_name) = lower(@team_name)
              AND is_active = TRUE
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("team_name", teamName);

        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetGuid(0));
        return result;
    }

    private static async Task<string> ReadReplacementManagerEmailAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string teamName,
        Guid excludedManagerUserId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT manager_email
            FROM user_admin_manager_team_assignments
            WHERE lower(team_name) = lower(@team_name)
              AND manager_user_id <> @excluded_manager_user_id
              AND is_active = TRUE
            ORDER BY updated_at DESC
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("team_name", teamName);
        command.Parameters.AddWithValue("excluded_manager_user_id", excludedManagerUserId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    private static bool IsManagerRole(string roleCode)
    {
        var normalized = (roleCode ?? string.Empty).Trim().ToUpperInvariant();
        return normalized == "MANAGER"
            || normalized.Contains("MANAGER", StringComparison.Ordinal)
            || normalized.Contains("MANAGEMENT", StringComparison.Ordinal)
            || normalized.EndsWith("_LEAD", StringComparison.Ordinal)
            || normalized == "ENGINEERING_LEAD";
    }

    private static IResult InvalidRequest(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_request",
        message
    });

    private sealed record TeamScopeRequest(
        List<string?>? TeamNames,
        string? Reason);

    private sealed record ManagerOption(
        Guid UserId,
        string Email,
        string DisplayName,
        IReadOnlyList<string> RoleCodes);

    private sealed record TeamOption(
        string TeamName,
        int MemberCount,
        int ActiveMemberCount);

    private sealed record ManagerTeamAssignment(
        Guid AssignmentId,
        Guid ManagerUserId,
        string ManagerEmail,
        string ManagerDisplayName,
        string TeamName,
        bool IsActive,
        string AssignmentReason,
        DateTimeOffset UpdatedAt);
}

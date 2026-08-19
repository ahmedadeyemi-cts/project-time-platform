using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Project-scoped planning collaboration administration shared by FlowHive and
/// Project Forge. Module catalog ownership is intentionally not an access input.
/// </summary>
public static class ProjectPlanningCollaborationModule
{
    public sealed record CollaboratorPutRequest(
        string? CollaborationRole,
        string? AccessLevel,
        string? Reason,
        DateOnly? EffectiveStartDate,
        DateOnly? EffectiveEndDate,
        int? ExpectedRevision);

    private static readonly HashSet<string> CollaborationRoles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "project_manager_lead",
            "engineering_lead",
            "engineer",
            "technical_reviewer",
            "planner_editor",
            "stakeholder"
        };

    private static readonly HashSet<string> AccessLevels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "view",
            "review",
            "edit",
            "administer"
        };

    public static WebApplication MapProjectPlanningCollaborationEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/project-planning/projects",
            (Func<HttpContext, CancellationToken, Task<IResult>>)ListProjectsAsync);
        app.MapGet(
            "/api/project-planning/projects/{projectId:guid}/access",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetAccessAsync);
        app.MapGet(
            "/api/project-planning/projects/{projectId:guid}/collaborators",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ListCollaboratorsAsync);
        app.MapPut(
            "/api/project-planning/projects/{projectId:guid}/collaborators/{userId:guid}",
            (Func<Guid, Guid, CollaboratorPutRequest, HttpContext, CancellationToken, Task<IResult>>)PutCollaboratorAsync);
        app.MapDelete(
            "/api/project-planning/projects/{projectId:guid}/collaborators/{userId:guid}",
            (Func<Guid, Guid, int?, HttpContext, CancellationToken, Task<IResult>>)DeactivateCollaboratorAsync);
        return app;
    }

    private static async Task<IResult> ListProjectsAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (ProjectPlanningAccessResolver.ReadUserId(
                context,
                "ProjectPulseEffectiveUserId",
                "ProjectPulseSessionUserId",
                "ProjectPulseActualUserId") is null)
        {
            return ProjectPlanningAccessResolver.SessionRequired();
        }

        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var projects = await ProjectPlanningAccessResolver.ListProjectsAsync(
                connection,
                context,
                cancellationToken);
            return Results.Ok(new
            {
                status = "project_planning_projects_loaded",
                policy = ProjectPlanningAccessResolver.PolicyVersion,
                ownershipDoesNotGrantAccess = true,
                count = projects.Count,
                projects,
                stateChanged = false
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable
                or PostgresErrorCodes.UndefinedColumn
                or PostgresErrorCodes.UndefinedFunction)
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> GetAccessAsync(
        Guid projectId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                cancellationToken);
            return access.CanView
                ? Results.Ok(new
                {
                    status = "project_planning_access_loaded",
                    access,
                    ownershipDoesNotGrantAccess = true,
                    stateChanged = false
                })
                : ProjectPlanningAccessResolver.ProjectForbidden("project_planning_view");
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable
                or PostgresErrorCodes.UndefinedColumn
                or PostgresErrorCodes.UndefinedFunction)
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> ListCollaboratorsAsync(
        Guid projectId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                cancellationToken);
            if (!access.CanAdministerPlanner)
            {
                return ProjectPlanningAccessResolver.ProjectForbidden(
                    "project_planning_collaborator_administration");
            }

            var collaborators = await ProjectPlanningAccessResolver.ListCollaboratorsAsync(
                connection,
                projectId,
                cancellationToken);
            return Results.Ok(new
            {
                status = "project_planning_collaborators_loaded",
                access,
                count = collaborators.Count,
                collaborators,
                stateChanged = false
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable
                or PostgresErrorCodes.UndefinedColumn
                or PostgresErrorCodes.UndefinedFunction)
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> PutCollaboratorAsync(
        Guid projectId,
        Guid userId,
        CollaboratorPutRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                cancellationToken);
            if (access.IsViewAs)
            {
                return ProjectPlanningAccessResolver.ViewAsWriteBlocked();
            }
            if (!access.CanAdministerPlanner)
            {
                return ProjectPlanningAccessResolver.ProjectForbidden(
                    "project_planning_collaborator_administration");
            }

            var role = Normalize(request.CollaborationRole);
            var level = Normalize(request.AccessLevel);
            var reason = Clean(request.Reason, 500);
            var startDate = request.EffectiveStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var endDate = request.EffectiveEndDate;

            if (!CollaborationRoles.Contains(role))
            {
                return Results.BadRequest(new
                {
                    status = "invalid_collaboration_role",
                    allowed = CollaborationRoles.OrderBy(value => value).ToArray()
                });
            }
            if (!AccessLevels.Contains(level))
            {
                return Results.BadRequest(new
                {
                    status = "invalid_access_level",
                    allowed = AccessLevels.OrderBy(value => value).ToArray()
                });
            }
            if (endDate.HasValue && endDate.Value < startDate)
            {
                return Results.BadRequest(new
                {
                    status = "invalid_effective_dates",
                    message = "The collaboration end date cannot precede its start date."
                });
            }
            if (level == "administer" && role != "project_manager_lead")
            {
                return Results.BadRequest(new
                {
                    status = "administer_role_required",
                    message = "Only a Project Manager Lead collaboration may administer project planning."
                });
            }
            if (level is "review" or "edit"
                && role is not (
                    "engineering_lead"
                    or "engineer"
                    or "technical_reviewer"
                    or "planner_editor"
                    or "project_manager_lead"))
            {
                return Results.BadRequest(new
                {
                    status = "planner_editor_role_required",
                    message = "Review and edit access require an engineering, reviewer, planner-editor, or PM Lead collaboration role."
                });
            }

            var targetRoleError = await ValidateTargetRoleAsync(
                connection,
                userId,
                role,
                level,
                cancellationToken);
            if (targetRoleError is not null) return targetRoleError;

            var actorUserId = access.ActualUserId;
            const string sql = """
                INSERT INTO project_planning_collaborators(
                    project_id,
                    user_id,
                    collaboration_role,
                    access_level,
                    reason,
                    is_active,
                    effective_start_date,
                    effective_end_date,
                    created_by_user_id,
                    updated_by_user_id)
                VALUES(
                    @project_id,
                    @user_id,
                    @collaboration_role,
                    @access_level,
                    @reason,
                    TRUE,
                    @effective_start_date,
                    @effective_end_date,
                    @actor_user_id,
                    @actor_user_id)
                ON CONFLICT(project_id, user_id) DO UPDATE
                SET collaboration_role = EXCLUDED.collaboration_role,
                    access_level = EXCLUDED.access_level,
                    reason = EXCLUDED.reason,
                    is_active = TRUE,
                    effective_start_date = EXCLUDED.effective_start_date,
                    effective_end_date = EXCLUDED.effective_end_date,
                    updated_by_user_id = EXCLUDED.updated_by_user_id
                WHERE @expected_revision IS NULL
                   OR project_planning_collaborators.revision_number = @expected_revision
                RETURNING
                    project_planning_collaborator_id,
                    revision_number,
                    updated_at;
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("collaboration_role", role);
            command.Parameters.AddWithValue("access_level", level);
            command.Parameters.AddWithValue("reason", reason);
            command.Parameters.AddWithValue("effective_start_date", startDate);
            command.Parameters.Add("effective_end_date", NpgsqlDbType.Date).Value =
                endDate.HasValue ? endDate.Value : DBNull.Value;
            command.Parameters.AddWithValue("actor_user_id", actorUserId);
            command.Parameters.Add("expected_revision", NpgsqlDbType.Integer).Value =
                request.ExpectedRevision.HasValue
                    ? request.ExpectedRevision.Value
                    : DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.Conflict(new
                {
                    status = "project_planning_collaborator_revision_conflict",
                    message = "The collaborator assignment changed after it was loaded. Reload before saving again.",
                    stateChanged = false
                });
            }

            return Results.Ok(new
            {
                status = "project_planning_collaborator_saved",
                projectPlanningCollaboratorId = reader.GetGuid(0),
                projectId,
                userId,
                collaborationRole = role,
                accessLevel = level,
                revision = reader.GetInt32(1),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(2),
                policy = ProjectPlanningAccessResolver.PolicyVersion,
                stateChanged = true
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable
                or PostgresErrorCodes.UndefinedColumn
                or PostgresErrorCodes.UndefinedFunction)
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> DeactivateCollaboratorAsync(
        Guid projectId,
        Guid userId,
        int? expectedRevision,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                cancellationToken);
            if (access.IsViewAs)
            {
                return ProjectPlanningAccessResolver.ViewAsWriteBlocked();
            }
            if (!access.CanAdministerPlanner)
            {
                return ProjectPlanningAccessResolver.ProjectForbidden(
                    "project_planning_collaborator_administration");
            }

            const string sql = """
                UPDATE project_planning_collaborators
                SET is_active = FALSE,
                    effective_end_date = COALESCE(effective_end_date, CURRENT_DATE),
                    updated_by_user_id = @actor_user_id
                WHERE project_id = @project_id
                  AND user_id = @user_id
                  AND is_active = TRUE
                  AND (@expected_revision IS NULL OR revision_number = @expected_revision)
                RETURNING revision_number, updated_at;
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            command.Parameters.Add("expected_revision", NpgsqlDbType.Integer).Value =
                expectedRevision.HasValue ? expectedRevision.Value : DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.NotFound(new
                {
                    status = "project_planning_collaborator_not_found",
                    message = "No active collaborator assignment matched the selected project, user, and revision.",
                    stateChanged = false
                });
            }

            return Results.Ok(new
            {
                status = "project_planning_collaborator_deactivated",
                projectId,
                userId,
                revision = reader.GetInt32(0),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(1),
                stateChanged = true
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable
                or PostgresErrorCodes.UndefinedColumn
                or PostgresErrorCodes.UndefinedFunction)
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult?> ValidateTargetRoleAsync(
        NpgsqlConnection connection,
        Guid userId,
        string collaborationRole,
        string accessLevel,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                COALESCE(app_user.is_active, TRUE),
                ARRAY_REMOVE(ARRAY_AGG(DISTINCT trim(both '_' from regexp_replace(
                    upper(btrim(COALESCE(role.role_code, ''))),
                    '[^A-Z0-9]+',
                    '_',
                    'g'))), NULL)
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id = app_user.user_id
             AND assignment.is_active = TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id = assignment.app_role_id
             AND role.is_active = TRUE
            WHERE app_user.user_id = @user_id
            GROUP BY app_user.user_id, app_user.is_active;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0))
        {
            return Results.BadRequest(new
            {
                status = "inactive_collaborator",
                message = "The selected collaborator must be an active Pulse user."
            });
        }

        var roles = reader.IsDBNull(1)
            ? Array.Empty<string>()
            : reader.GetFieldValue<string[]>(1);
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engineering = roleSet.Overlaps(new[]
        {
            "ENGINEER",
            "ENGINEERING",
            "ENGINEERING_LEAD",
            "ENGINEERING_TEAM_LEAD",
            "SYSTEMS_ENGINEER",
            "NETWORK_ENGINEER",
            "ENTERPRISE_NETWORK_ENGINEER"
        });
        var pmLead = roleSet.Overlaps(new[]
        {
            "PROJECT_MANAGER_LEAD",
            "PROJECT_MANAGEMENT_LEAD",
            "PM_LEAD"
        });

        if (accessLevel is "review" or "edit"
            && collaborationRole != "project_manager_lead"
            && !engineering)
        {
            return Results.BadRequest(new
            {
                status = "engineering_collaborator_role_required",
                message = "Planner review or edit access requires an active Engineer or Engineering Lead role."
            });
        }
        if (accessLevel == "administer" && !pmLead)
        {
            return Results.BadRequest(new
            {
                status = "project_manager_lead_role_required",
                message = "Planner administration requires an active Project Manager Lead role."
            });
        }

        return null;
    }

    private static async Task<(NpgsqlConnection? Connection, IResult? Error)>
        OpenConnectionAsync(
            HttpContext context,
            CancellationToken cancellationToken)
    {
        if (ProjectPlanningAccessResolver.ReadUserId(
                context,
                "ProjectPulseActualUserId",
                "ProjectPulseSessionUserId") is null)
        {
            return (null, ProjectPlanningAccessResolver.SessionRequired());
        }

        var connectionString = ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return (null, Results.Json(new
            {
                status = "project_planning_dependency_unavailable",
                message = "Project-planning persistence is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return (connection, null);
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectPlanningCollaborationModule")
                .LogWarning(
                    "Project-planning collaboration persistence could not be opened ({ExceptionType}).",
                    exception.GetType().Name);
            return (null, Results.Json(new
            {
                status = "project_planning_dependency_unavailable",
                message = "Project-planning persistence is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static IResult MigrationRequired() =>
        Results.Json(new
        {
            status = "project_planning_migration_required",
            migration = "094_project_planning_collaboration_access.sql",
            message = "Apply the project-planning collaboration migration before using this capability."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string ConnectionString()
    {
        foreach (var key in new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return string.Empty;
    }

    private static string Normalize(string? value) =>
        string.Join(
                '_',
                (value ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant()
                    .Split(
                        new[] { ' ', '-', '_' },
                        StringSplitOptions.RemoveEmptyEntries))
            .Trim('_');

    private static string Clean(string? value, int maximumLength)
    {
        var clean = string.Join(
            ' ',
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength];
    }
}

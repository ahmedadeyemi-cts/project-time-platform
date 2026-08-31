using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Project-scoped planning collaboration administration shared by FlowHive and
/// Project Forge. Functional governance remains with the assigned Project
/// Manager, an authorized PM Lead, or an Administrator. Module catalog owner
/// metadata is descriptive only and never grants project access.
/// </summary>
public static class ProjectPlanningCollaborationModule
{
    public sealed record CollaboratorPutRequest(
        string? ModuleCode,
        string? CollaborationLevel,
        string? AccessLevel,
        string? CollaborationRole,
        string? Notes,
        string? Reason,
        DateOnly? EffectiveStartDate,
        DateOnly? EffectiveEndDate,
        DateTimeOffset? ExpectedUpdatedAt,
        int? ExpectedRevision);

    private static readonly HashSet<string> AllowedModules =
        new(StringComparer.OrdinalIgnoreCase) { "033", "066" };

    private static readonly HashSet<string> AllowedLevels =
        new(StringComparer.OrdinalIgnoreCase) { "viewer", "reviewer", "editor" };

    public static WebApplication MapProjectPlanningCollaborationEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/project-planning/projects",
            (Func<string?, HttpContext, CancellationToken, Task<IResult>>)ListProjectsAsync);
        app.MapGet(
            "/api/project-planning/projects/{projectId:guid}/access",
            (Func<Guid, string?, HttpContext, CancellationToken, Task<IResult>>)GetAccessAsync);
        app.MapGet(
            "/api/project-planning/projects/{projectId:guid}/collaborators",
            (Func<Guid, string?, HttpContext, CancellationToken, Task<IResult>>)ListCollaboratorsAsync);
        app.MapPut(
            "/api/project-planning/projects/{projectId:guid}/collaborators/{userId:guid}",
            (Func<Guid, Guid, CollaboratorPutRequest, HttpContext, CancellationToken, Task<IResult>>)PutCollaboratorAsync);
        app.MapDelete(
            "/api/project-planning/projects/{projectId:guid}/collaborators/{userId:guid}",
            (Func<Guid, Guid, string?, DateTimeOffset?, HttpContext, CancellationToken, Task<IResult>>)DeactivateCollaboratorAsync);
        return app;
    }

    private static async Task<IResult> ListProjectsAsync(
        string? module,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var moduleCode = NormalizeModule(module);
        if (moduleCode is null) return InvalidModule();

        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var candidates = new List<ProjectCandidate>();
            const string sql = """
                SELECT
                    project.project_id,
                    project.project_code,
                    project.project_name,
                    COALESCE(client.client_name,''),
                    COALESCE(project.status,''),
                    project.start_date,
                    project.end_date,
                    project.project_manager_user_id,
                    COALESCE(NULLIF(project_manager.display_name,''),project_manager.email,'Unassigned')
                FROM projects project
                LEFT JOIN clients client ON client.client_id=project.client_id
                LEFT JOIN app_users project_manager ON project_manager.user_id=project.project_manager_user_id
                ORDER BY project.created_at DESC
                LIMIT 250;
                """;
            await using (var command = new NpgsqlCommand(sql, connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    candidates.Add(new ProjectCandidate(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        ReadDateOrNull(reader, 5),
                        ReadDateOrNull(reader, 6),
                        reader.IsDBNull(7) ? null : reader.GetGuid(7),
                        reader.GetString(8)));
                }
            }

            var visible = new List<object>();
            foreach (var project in candidates)
            {
                var access = await ProjectPlanningAccessResolver.ResolveAsync(
                    connection,
                    context,
                    project.ProjectId,
                    moduleCode,
                    cancellationToken);
                if (!access.CanView) continue;
                visible.Add(new
                {
                    project.ProjectId,
                    project.ProjectCode,
                    project.ProjectName,
                    project.CustomerName,
                    project.Status,
                    project.StartDate,
                    project.EndDate,
                    project.ProjectManagerUserId,
                    project.ProjectManagerName,
                    access = access.ToResponse()
                });
            }

            return Results.Ok(new
            {
                status = "project_planning_projects_loaded",
                policy = ProjectPlanningAccessResolver.Contract,
                moduleCode,
                ownershipDoesNotGrantAccess = true,
                count = visible.Count,
                projects = visible,
                stateChanged = false
            });
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> GetAccessAsync(
        Guid projectId,
        string? module,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var moduleCode = NormalizeModule(module);
        if (moduleCode is null) return InvalidModule();

        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                moduleCode,
                cancellationToken);
            return access.CanView
                ? Results.Ok(new
                {
                    status = "project_planning_access_loaded",
                    policy = ProjectPlanningAccessResolver.Contract,
                    access = access.ToResponse(),
                    ownershipDoesNotGrantAccess = true,
                    stateChanged = false
                })
                : ProjectForbidden("project_planning_view");
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> ListCollaboratorsAsync(
        Guid projectId,
        string? module,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var moduleCode = NormalizeModule(module);
        if (moduleCode is null) return InvalidModule();

        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                moduleCode,
                cancellationToken);
            if (!access.CanAdministerPlanner)
                return ProjectForbidden("project_planning_collaborator_administration");

            const string sql = """
                SELECT
                    collaborator.project_planning_collaborator_id,
                    collaborator.project_id,
                    collaborator.user_id,
                    collaborator.module_code,
                    collaborator.collaboration_level,
                    collaborator.effective_start_date,
                    collaborator.effective_end_date,
                    collaborator.is_active,
                    collaborator.notes,
                    collaborator.assigned_by_user_id,
                    collaborator.created_at,
                    collaborator.updated_at,
                    COALESCE(NULLIF(target.display_name,''),target.email,''),
                    target.email,
                    COALESCE(NULLIF(actor.display_name,''),actor.email,'')
                FROM project_planning_collaborators collaborator
                JOIN app_users target ON target.user_id=collaborator.user_id
                LEFT JOIN app_users actor ON actor.user_id=collaborator.assigned_by_user_id
                WHERE collaborator.project_id=@project_id
                  AND collaborator.module_code=@module_code
                ORDER BY collaborator.is_active DESC,
                         collaborator.collaboration_level DESC,
                         target.display_name,
                         target.email;
                """;
            var collaborators = new List<object>();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("module_code", moduleCode);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                collaborators.Add(new
                {
                    projectPlanningCollaboratorId = reader.GetGuid(0),
                    projectId = reader.GetGuid(1),
                    userId = reader.GetGuid(2),
                    moduleCode = reader.GetString(3),
                    collaborationLevel = reader.GetString(4),
                    effectiveStartDate = ReadDate(reader, 5),
                    effectiveEndDate = ReadDateOrNull(reader, 6),
                    isActive = reader.GetBoolean(7),
                    notes = reader.GetString(8),
                    assignedByUserId = reader.GetGuid(9),
                    createdAt = reader.GetFieldValue<DateTimeOffset>(10),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(11),
                    displayName = reader.GetString(12),
                    email = reader.GetString(13),
                    assignedByDisplayName = reader.GetString(14)
                });
            }

            return Results.Ok(new
            {
                status = "project_planning_collaborators_loaded",
                policy = ProjectPlanningAccessResolver.Contract,
                moduleCode,
                access = access.ToResponse(),
                count = collaborators.Count,
                collaborators,
                stateChanged = false
            });
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
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
        var moduleCode = NormalizeModule(request.ModuleCode);
        if (moduleCode is null) return InvalidModule();
        var level = NormalizeLevel(request.CollaborationLevel ?? request.AccessLevel);
        if (level is null)
        {
            return Results.BadRequest(new
            {
                status = "invalid_collaboration_level",
                allowed = AllowedLevels.OrderBy(value => value).ToArray(),
                message = "Use viewer, reviewer, or editor. PM governance is role-derived and cannot be transferred through a collaborator record."
            });
        }

        var startDate = request.EffectiveStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = request.EffectiveEndDate;
        if (endDate.HasValue && endDate.Value < startDate)
        {
            return Results.BadRequest(new
            {
                status = "invalid_effective_dates",
                message = "The collaboration end date cannot precede its start date."
            });
        }

        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                moduleCode,
                cancellationToken);
            if (access.IsViewAs) return ViewAsWriteBlocked();
            if (!access.CanAdministerPlanner)
                return ProjectForbidden("project_planning_collaborator_administration");

            var targetError = await ValidateTargetAsync(
                connection,
                userId,
                level,
                cancellationToken);
            if (targetError is not null) return targetError;

            var actor = access.ActualUserId ?? access.EffectiveUserId;
            if (!actor.HasValue) return SessionRequired();
            var notes = Clean(request.Notes ?? request.Reason, 4000);

            const string sql = """
                INSERT INTO project_planning_collaborators(
                    project_id,
                    user_id,
                    module_code,
                    collaboration_level,
                    assigned_by_user_id,
                    effective_start_date,
                    effective_end_date,
                    is_active,
                    notes)
                VALUES(
                    @project_id,
                    @user_id,
                    @module_code,
                    @collaboration_level,
                    @actor_user_id,
                    @effective_start_date,
                    @effective_end_date,
                    TRUE,
                    @notes)
                ON CONFLICT(project_id,user_id,module_code) DO UPDATE
                SET collaboration_level=EXCLUDED.collaboration_level,
                    assigned_by_user_id=EXCLUDED.assigned_by_user_id,
                    effective_start_date=EXCLUDED.effective_start_date,
                    effective_end_date=EXCLUDED.effective_end_date,
                    is_active=TRUE,
                    notes=EXCLUDED.notes
                WHERE @expected_updated_at::timestamptz IS NULL
                   OR project_planning_collaborators.updated_at=@expected_updated_at
                RETURNING project_planning_collaborator_id,created_at,updated_at;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("module_code", moduleCode);
            command.Parameters.AddWithValue("collaboration_level", level);
            command.Parameters.AddWithValue("actor_user_id", actor.Value);
            command.Parameters.AddWithValue("effective_start_date", startDate);
            command.Parameters.Add("effective_end_date", NpgsqlDbType.Date).Value =
                endDate.HasValue ? endDate.Value : DBNull.Value;
            command.Parameters.AddWithValue("notes", notes);
            command.Parameters.Add("expected_updated_at", NpgsqlDbType.TimestampTz).Value =
                request.ExpectedUpdatedAt.HasValue
                    ? request.ExpectedUpdatedAt.Value
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
                policy = ProjectPlanningAccessResolver.Contract,
                projectPlanningCollaboratorId = reader.GetGuid(0),
                projectId,
                userId,
                moduleCode,
                collaborationLevel = level,
                effectiveStartDate = startDate,
                effectiveEndDate = endDate,
                notes,
                createdAt = reader.GetFieldValue<DateTimeOffset>(1),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(2),
                stateChanged = true
            });
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> DeactivateCollaboratorAsync(
        Guid projectId,
        Guid userId,
        string? module,
        DateTimeOffset? expectedUpdatedAt,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var moduleCode = NormalizeModule(module);
        if (moduleCode is null) return InvalidModule();

        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;

        try
        {
            var access = await ProjectPlanningAccessResolver.ResolveAsync(
                connection,
                context,
                projectId,
                moduleCode,
                cancellationToken);
            if (access.IsViewAs) return ViewAsWriteBlocked();
            if (!access.CanAdministerPlanner)
                return ProjectForbidden("project_planning_collaborator_administration");

            var actor = access.ActualUserId ?? access.EffectiveUserId;
            if (!actor.HasValue) return SessionRequired();

            const string sql = """
                UPDATE project_planning_collaborators
                SET is_active=FALSE,
                    effective_end_date=COALESCE(effective_end_date,CURRENT_DATE),
                    assigned_by_user_id=@actor_user_id
                WHERE project_id=@project_id
                  AND user_id=@user_id
                  AND module_code=@module_code
                  AND is_active=TRUE
                  AND (@expected_updated_at::timestamptz IS NULL OR updated_at=@expected_updated_at)
                RETURNING project_planning_collaborator_id,updated_at;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("module_code", moduleCode);
            command.Parameters.AddWithValue("actor_user_id", actor.Value);
            command.Parameters.Add("expected_updated_at", NpgsqlDbType.TimestampTz).Value =
                expectedUpdatedAt.HasValue ? expectedUpdatedAt.Value : DBNull.Value;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.NotFound(new
                {
                    status = "project_planning_collaborator_not_found",
                    message = "No active collaborator assignment matched the selected project, user, module, and version.",
                    stateChanged = false
                });
            }

            return Results.Ok(new
            {
                status = "project_planning_collaborator_deactivated",
                policy = ProjectPlanningAccessResolver.Contract,
                projectPlanningCollaboratorId = reader.GetGuid(0),
                projectId,
                userId,
                moduleCode,
                updatedAt = reader.GetFieldValue<DateTimeOffset>(1),
                stateChanged = true
            });
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult?> ValidateTargetAsync(
        NpgsqlConnection connection,
        Guid userId,
        string level,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                app_user.is_active,
                COALESCE(string_agg(DISTINCT upper(role.role_code),','),'')
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id=app_user.user_id
             AND assignment.is_active=TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id=assignment.app_role_id
             AND role.is_active=TRUE
            WHERE app_user.user_id=@user_id
            GROUP BY app_user.user_id,app_user.is_active;
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

        if (level is not ("reviewer" or "editor")) return null;
        var roles = reader.GetString(1)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var technicalRole = roles.Overlaps(new[]
        {
            "ENGINEER",
            "ENGINEERING",
            "ENGINEERING_LEAD",
            "ENGINEERING_TEAM_LEAD",
            "SYSTEMS_ENGINEER",
            "NETWORK_ENGINEER",
            "ENTERPRISE_NETWORK_ENGINEER",
            "PROJECT_MANAGER",
            "PROJECT_MANAGEMENT",
            "PROJECT_MANAGEMENT_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD",
            "PM_TEAM_LEAD"
        });
        return technicalRole
            ? null
            : Results.BadRequest(new
            {
                status = "technical_collaborator_role_required",
                message = "Review or edit collaboration requires an active Engineering, Project Manager, or PM Lead role."
            });
    }

    private static async Task<(NpgsqlConnection? Connection, IResult? Error)> OpenConnectionAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        if (!actual.HasValue) return (null, SessionRequired());

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
            context.RequestServices.GetRequiredService<ILoggerFactory>()
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

    private static bool IsMigrationError(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.UndefinedTable
            or PostgresErrorCodes.UndefinedColumn
            or PostgresErrorCodes.UndefinedFunction;

    private static IResult MigrationRequired() => Results.Json(new
    {
        status = "project_planning_migration_required",
        migration = "095_project_planning_collaboration_access.sql",
        message = "Apply Migration 095 before using project-planning collaboration.",
        stateChanged = false
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid ProjectPulse session is required.",
        stateChanged = false
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult ProjectForbidden(string capability) => Results.Json(new
    {
        status = "project_planning_forbidden",
        capability,
        message = "The selected project or planning action is outside the current server-authorized scope.",
        ownershipDoesNotGrantAccess = true,
        stateChanged = false
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsWriteBlocked() => Results.Json(new
    {
        status = "view_as_write_blocked",
        message = "Exit View-As before changing project-planning collaborators.",
        stateChanged = false
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult InvalidModule() => Results.BadRequest(new
    {
        status = "invalid_project_planning_module",
        allowed = AllowedModules.OrderBy(value => value).ToArray(),
        message = "Use module 033 for Project Forge or 066 for Project FlowHive."
    });

    private static string? NormalizeModule(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "066" : value.Trim();
        return AllowedModules.Contains(normalized) ? normalized : null;
    }

    private static string? NormalizeLevel(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "view" or "viewer" or "stakeholder" => "viewer",
            "review" or "reviewer" or "technical_reviewer" => "reviewer",
            "edit" or "editor" or "planner_editor" => "editor",
            _ => null
        };

    private static string Clean(string? value, int maximumLength)
    {
        var clean = string.Join(
            ' ',
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

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

        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        return config.Missing.Count == 0 ? config.ConnectionString : string.Empty;
    }

    private static DateOnly ReadDate(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static DateOnly? ReadDateOrNull(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);

    private sealed record ProjectCandidate(
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string CustomerName,
        string Status,
        DateOnly? StartDate,
        DateOnly? EndDate,
        Guid? ProjectManagerUserId,
        string ProjectManagerName);
}

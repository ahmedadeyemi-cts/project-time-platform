using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Shared project-scoped authorization for Module 033 Project Forge and
/// Module 066 Project FlowHive. Module Management ownership is intentionally
/// excluded: it is descriptive developer accountability metadata and never an
/// access grant.
/// </summary>
internal static class ProjectPlanningAccessResolver
{
    internal const string Contract = "PROJECT_PLANNING_COLLABORATION_V1";

    private static readonly string[] AdministratorRoles =
    [
        "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "ADMINISTRATOR"
    ];

    private static readonly string[] ProjectManagerRoles =
    [
        "PROJECT_MANAGER", "PROJECT_MANAGEMENT"
    ];

    private static readonly string[] ProjectManagementLeadRoles =
    [
        "PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD"
    ];

    private static readonly string[] EngineeringRoles =
    [
        "ENGINEER", "ENGINEERING", "SYSTEMS_ENGINEER", "NETWORK_ENGINEER",
        "ENTERPRISE_NETWORK_ENGINEER"
    ];

    private static readonly string[] EngineeringLeadRoles =
    [
        "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD"
    ];

    private static readonly string[] AccountExecutiveRoles =
    [
        "ACCOUNT_EXECUTIVE", "SALES_ACCOUNT_EXECUTIVE"
    ];

    private static readonly string[] SolutionArchitectRoles =
    [
        "SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT"
    ];

    private static readonly string[] ProjectCoordinatorRoles =
    [
        "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR"
    ];

    private static readonly string[] ExecutiveRoles =
    [
        "EXECUTIVE", "EXECUTIVE_LEADERSHIP"
    ];

    private static readonly string[] PeopleManagerRoles =
    [
        "MANAGER"
    ];

    internal static async Task<ProjectPlanningAccess> ResolveAsync(
        NpgsqlConnection connection,
        HttpContext context,
        Guid projectId,
        string moduleCode,
        CancellationToken cancellationToken)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        var effective = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId") ?? actual;

        if (!actual.HasValue || !effective.HasValue)
            return ProjectPlanningAccess.SessionRequired(projectId, moduleCode);

        var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context)
            || actual.Value != effective.Value;
        var permanentFullControl = context.Items.TryGetValue(
            "ProjectPulsePermanentFullControl",
            out var permanent) && permanent is true;

        return await ResolveCoreAsync(
            connection,
            actual.Value,
            effective.Value,
            projectId,
            moduleCode,
            isViewAs,
            permanentFullControl,
            cancellationToken);
    }

    internal static Task<ProjectPlanningAccess> ResolveForActorAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        Guid projectId,
        string moduleCode,
        CancellationToken cancellationToken) =>
        ResolveCoreAsync(
            connection,
            actorUserId,
            actorUserId,
            projectId,
            moduleCode,
            isViewAs: false,
            permanentFullControl: false,
            cancellationToken);

    private static async Task<ProjectPlanningAccess> ResolveCoreAsync(
        NpgsqlConnection connection,
        Guid actualUserId,
        Guid effectiveUserId,
        Guid projectId,
        string moduleCode,
        bool isViewAs,
        bool permanentFullControl,
        CancellationToken cancellationToken)
    {
        var normalizedModule = moduleCode.Trim().ToUpperInvariant();
        if (normalizedModule is not ("033" or "066"))
            return ProjectPlanningAccess.Denied(
                actualUserId,
                effectiveUserId,
                projectId,
                normalizedModule,
                isViewAs,
                "unsupported_module");

        var identity = await LoadIdentityAsync(
            connection,
            effectiveUserId,
            cancellationToken);
        if (identity is null)
            return ProjectPlanningAccess.Denied(
                actualUserId,
                effectiveUserId,
                projectId,
                normalizedModule,
                isViewAs,
                "inactive_identity");

        var association = await LoadAssociationAsync(
            connection,
            effectiveUserId,
            identity.TeamName,
            identity.DepartmentName,
            projectId,
            normalizedModule,
            cancellationToken);
        if (association is null)
            return ProjectPlanningAccess.Denied(
                actualUserId,
                effectiveUserId,
                projectId,
                normalizedModule,
                isViewAs,
                "project_not_found");

        var administrator = permanentFullControl || HasAny(identity.Roles, AdministratorRoles);
        var projectManagerRole = HasAny(identity.Roles, ProjectManagerRoles);
        var projectManagementLeadRole = HasAny(identity.Roles, ProjectManagementLeadRoles);
        var engineeringRole = HasAny(identity.Roles, EngineeringRoles);
        var engineeringLeadRole = HasAny(identity.Roles, EngineeringLeadRoles);
        var accountExecutiveRole = HasAny(identity.Roles, AccountExecutiveRoles);
        var solutionArchitectRole = HasAny(identity.Roles, SolutionArchitectRoles);
        var projectCoordinatorRole = HasAny(identity.Roles, ProjectCoordinatorRoles);
        var executiveRole = HasAny(identity.Roles, ExecutiveRoles);
        var peopleManagerRole = HasAny(identity.Roles, PeopleManagerRoles);

        var projectManagerOwner = projectManagerRole
            && association.ProjectManagerUserId == effectiveUserId;
        var projectManagementLeadScope = projectManagementLeadRole
            && association.ProjectManagementLeadScope;
        var engineeringLeadScope = engineeringLeadRole
            && association.EngineeringLeadScope;
        var directProjectAssignment = association.DirectProjectAssignment
            && (engineeringRole || engineeringLeadRole);
        var accountExecutive = accountExecutiveRole
            && association.AccountExecutiveUserId == effectiveUserId;
        var solutionArchitect = solutionArchitectRole
            && association.SolutionArchitectUserId == effectiveUserId;
        var businessBroadRead = normalizedModule == "066"
            && (projectCoordinatorRole || executiveRole);
        var peopleManagerScope = normalizedModule == "066"
            && peopleManagerRole
            && association.EngineeringLeadScope;

        var explicitLevel = association.ExplicitCollaborationLevel;
        var explicitViewer = explicitLevel is "viewer" or "reviewer" or "editor";
        var explicitReviewer = explicitLevel is "reviewer" or "editor";
        var explicitEditor = explicitLevel == "editor";

        var associated = administrator
            || projectManagerOwner
            || projectManagementLeadScope
            || directProjectAssignment
            || engineeringLeadScope
            || explicitViewer
            || accountExecutive
            || solutionArchitect
            || businessBroadRead
            || peopleManagerScope;

        var viewPermission = normalizedModule == "033"
            ? HasAnyPermission(
                identity.Permissions,
                "VIEW_PROJECT_FORGE_033",
                "VIEW_ASSOCIATED_PROJECT_FORGE_033")
            : HasAnyPermission(
                identity.Permissions,
                "VIEW_PROJECT_FLOWHIVE_066",
                "VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066");
        var reviewPermission = normalizedModule == "033"
            ? identity.Permissions.Contains("REVIEW_PROJECT_FORGE_PLAN_033")
            : identity.Permissions.Contains("REVIEW_FLOWHIVE_PLANNER_066");
        var editPermission = normalizedModule == "033"
            ? identity.Permissions.Contains("EDIT_PROJECT_FORGE_REVIEW_PLAN_033")
            : identity.Permissions.Contains("EDIT_FLOWHIVE_PLANNER_066");
        var administerPermission = normalizedModule == "033"
            ? identity.Permissions.Contains("MANAGE_PROJECT_FORGE_033")
            : HasAnyPermission(
                identity.Permissions,
                "MANAGE_PROJECT_FLOWHIVE_066",
                "MANAGE_FLOWHIVE_PM_WORKSPACE_066");

        var canView = associated && (administrator || businessBroadRead || viewPermission);
        var ownSession = !isViewAs && actualUserId == effectiveUserId;
        var technicalReviewAssociation = directProjectAssignment
            || engineeringLeadScope
            || explicitReviewer;
        var technicalEditAssociation = directProjectAssignment
            || engineeringLeadScope
            || explicitEditor;

        var canReviewPlanner = ownSession
            && canView
            && (administrator
                || projectManagerOwner
                || projectManagementLeadScope
                || (technicalReviewAssociation && reviewPermission));
        var canEditPlanner = ownSession
            && canView
            && (administrator
                || projectManagerOwner
                || projectManagementLeadScope
                || (technicalEditAssociation && editPermission));
        var canAdministerPlanner = ownSession
            && (administrator || projectManagerOwner || projectManagementLeadScope)
            && (administrator || administerPermission);
        var canAdoptBaseline = canAdministerPlanner;
        var canManageFinancials = ownSession
            && canAdministerPlanner
            && (administrator
                || identity.Permissions.Contains("VIEW_FLOWHIVE_FINANCIALS_066"));
        var canCreateCustomerShare = normalizedModule == "066"
            && ownSession
            && canAdministerPlanner
            && (administrator
                || identity.Permissions.Contains("CREATE_FLOWHIVE_CUSTOMER_SHARE_066"));

        var scopeReason = administrator ? "administrator_support"
            : projectCoordinatorRole && businessBroadRead ? "project_team_coordinator_business_scope"
            : executiveRole && businessBroadRead ? "executive_read_scope"
            : peopleManagerScope ? "manager_team_scope"
            : projectManagerOwner ? "assigned_project_manager"
            : projectManagementLeadScope ? "assigned_pm_team_scope"
            : directProjectAssignment ? "active_project_assignment"
            : engineeringLeadScope ? "assigned_engineering_team_scope"
            : explicitEditor ? "explicit_planning_editor"
            : explicitReviewer ? "explicit_planning_reviewer"
            : explicitViewer ? "explicit_planning_viewer"
            : accountExecutive ? "associated_account_executive"
            : solutionArchitect ? "associated_solution_architect"
            : "not_associated";

        var capabilityLabel = canAdministerPlanner ? "Project Owner — Full Control"
            : canEditPlanner ? "Engineering Collaborator — Planner Edit"
            : canReviewPlanner ? "Technical Reviewer — Review and Comment"
            : canView ? "Project Stakeholder — Read Only"
            : "No Project Access";

        return new ProjectPlanningAccess(
            Contract,
            actualUserId,
            effectiveUserId,
            projectId,
            normalizedModule,
            isViewAs,
            true,
            administrator,
            projectManagerOwner,
            projectManagementLeadScope,
            directProjectAssignment,
            engineeringLeadScope,
            explicitLevel,
            accountExecutive,
            solutionArchitect,
            canView,
            canReviewPlanner,
            canEditPlanner,
            canAdministerPlanner,
            canAdoptBaseline,
            canManageFinancials,
            canCreateCustomerShare,
            scopeReason,
            capabilityLabel);
    }

    private static async Task<PlanningIdentity?> LoadIdentityAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                app_user.user_id,
                COALESCE(NULLIF(app_user.team_name,''),''),
                COALESCE(NULLIF(app_user.department_name,''),NULLIF(app_user.department,''),''),
                COALESCE(string_agg(DISTINCT upper(role.role_code),',' ORDER BY upper(role.role_code)),''),
                COALESCE(string_agg(DISTINCT permission.permission_code,',' ORDER BY permission.permission_code),'')
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id=app_user.user_id
             AND assignment.is_active=TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id=assignment.app_role_id
             AND role.is_active=TRUE
            LEFT JOIN app_role_permissions role_permission
              ON role_permission.app_role_id=role.app_role_id
            LEFT JOIN app_permissions permission
              ON permission.app_permission_id=role_permission.app_permission_id
            WHERE app_user.user_id=@user_id
              AND app_user.is_active=TRUE
            GROUP BY app_user.user_id,app_user.team_name,app_user.department_name,app_user.department;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new PlanningIdentity(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            Split(reader.GetString(3)),
            Split(reader.GetString(4)));
    }

    private static async Task<PlanningAssociation?> LoadAssociationAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        string teamName,
        string departmentName,
        Guid projectId,
        string moduleCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                project.project_manager_user_id,
                project.account_executive_user_id,
                project.solution_architect_user_id,
                EXISTS(
                    SELECT 1
                    FROM project_assignments assignment
                    WHERE assignment.project_id=project.project_id
                      AND assignment.user_id=@user_id
                      AND assignment.effective_start_date<=CURRENT_DATE
                      AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date>=CURRENT_DATE)
                ) AS direct_assignment,
                COALESCE((
                    SELECT collaborator.collaboration_level
                    FROM project_planning_collaborators collaborator
                    WHERE collaborator.project_id=project.project_id
                      AND collaborator.user_id=@user_id
                      AND collaborator.module_code=@module_code
                      AND collaborator.is_active=TRUE
                      AND collaborator.effective_start_date<=CURRENT_DATE
                      AND (collaborator.effective_end_date IS NULL OR collaborator.effective_end_date>=CURRENT_DATE)
                    ORDER BY CASE collaborator.collaboration_level
                        WHEN 'editor' THEN 0 WHEN 'reviewer' THEN 1 ELSE 2 END
                    LIMIT 1
                ),'') AS explicit_level,
                EXISTS(
                    SELECT 1
                    FROM app_users project_manager
                    WHERE project_manager.user_id=project.project_manager_user_id
                      AND project_manager.is_active=TRUE
                      AND (
                        EXISTS(
                            SELECT 1
                            FROM reporting_relationships relationship
                            WHERE relationship.employee_user_id=project_manager.user_id
                              AND (relationship.manager_user_id=@user_id OR relationship.team_lead_user_id=@user_id)
                              AND relationship.effective_start_date<=CURRENT_DATE
                              AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                        )
                        OR EXISTS(
                            SELECT 1
                            FROM projectpulse_team_scope_assignments scope
                            WHERE scope.scoped_user_id=@user_id
                              AND scope.is_active=TRUE
                              AND scope.scope_type='project_management_team_lead'
                              AND (
                                scope.manager_user_id=project_manager.user_id
                                OR (scope.team_name IS NOT NULL AND lower(COALESCE(project_manager.team_name,''))=lower(scope.team_name))
                                OR (scope.department_name IS NOT NULL AND lower(COALESCE(project_manager.department_name,project_manager.department,''))=lower(scope.department_name))
                              )
                        )
                      )
                ) AS pm_lead_scope,
                EXISTS(
                    SELECT 1
                    FROM project_assignments team_assignment
                    JOIN app_users team_member
                      ON team_member.user_id=team_assignment.user_id
                     AND team_member.is_active=TRUE
                    WHERE team_assignment.project_id=project.project_id
                      AND team_assignment.effective_start_date<=CURRENT_DATE
                      AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date>=CURRENT_DATE)
                      AND (
                        EXISTS(
                            SELECT 1
                            FROM reporting_relationships relationship
                            WHERE relationship.employee_user_id=team_member.user_id
                              AND (relationship.manager_user_id=@user_id OR relationship.team_lead_user_id=@user_id)
                              AND relationship.effective_start_date<=CURRENT_DATE
                              AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                        )
                        OR EXISTS(
                            SELECT 1
                            FROM projectpulse_team_scope_assignments scope
                            WHERE scope.scoped_user_id=@user_id
                              AND scope.is_active=TRUE
                              AND scope.scope_type='engineering_team_lead'
                              AND (
                                scope.manager_user_id=team_member.user_id
                                OR (scope.team_name IS NOT NULL AND lower(COALESCE(team_member.team_name,''))=lower(scope.team_name))
                                OR (scope.department_name IS NOT NULL AND lower(COALESCE(team_member.department_name,team_member.department,''))=lower(scope.department_name))
                              )
                        )
                        OR (@team_name<>'' AND lower(COALESCE(team_member.team_name,''))=lower(@team_name))
                        OR (@department_name<>'' AND lower(COALESCE(team_member.department_name,team_member.department,''))=lower(@department_name))
                      )
                ) AS engineering_lead_scope
            FROM projects project
            WHERE project.project_id=@project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", effectiveUserId);
        command.Parameters.AddWithValue("module_code", moduleCode);
        command.Parameters.AddWithValue("team_name", teamName);
        command.Parameters.AddWithValue("department_name", departmentName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new PlanningAssociation(
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetBoolean(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6));
    }

    private static IReadOnlySet<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool HasAny(IReadOnlySet<string> values, IEnumerable<string> candidates) =>
        candidates.Any(values.Contains);

    private static bool HasAnyPermission(IReadOnlySet<string> permissions, params string[] candidates) =>
        candidates.Any(permissions.Contains);

    private sealed record PlanningIdentity(
        Guid UserId,
        string TeamName,
        string DepartmentName,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions);

    private sealed record PlanningAssociation(
        Guid? ProjectManagerUserId,
        Guid? AccountExecutiveUserId,
        Guid? SolutionArchitectUserId,
        bool DirectProjectAssignment,
        string ExplicitCollaborationLevel,
        bool ProjectManagementLeadScope,
        bool EngineeringLeadScope);
}

internal sealed record ProjectPlanningAccess(
    string Contract,
    Guid? ActualUserId,
    Guid? EffectiveUserId,
    Guid ProjectId,
    string ModuleCode,
    bool IsViewAs,
    bool IsActiveIdentity,
    bool IsAdministrator,
    bool IsProjectManagerOwner,
    bool IsProjectManagementLeadScope,
    bool IsDirectProjectAssignee,
    bool IsEngineeringLeadScope,
    string ExplicitCollaborationLevel,
    bool IsAccountExecutive,
    bool IsSolutionArchitect,
    bool CanView,
    bool CanReviewPlanner,
    bool CanEditPlanner,
    bool CanAdministerPlanner,
    bool CanAdoptBaseline,
    bool CanManageFinancials,
    bool CanCreateCustomerShare,
    string ScopeReason,
    string CapabilityLabel)
{
    internal static ProjectPlanningAccess SessionRequired(Guid projectId, string moduleCode) =>
        new(
            ProjectPlanningAccessResolver.Contract,
            null,
            null,
            projectId,
            moduleCode,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            string.Empty,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            "session_required",
            "No Project Access");

    internal static ProjectPlanningAccess Denied(
        Guid actual,
        Guid effective,
        Guid projectId,
        string moduleCode,
        bool isViewAs,
        string reason) =>
        new(
            ProjectPlanningAccessResolver.Contract,
            actual,
            effective,
            projectId,
            moduleCode,
            isViewAs,
            reason is not "inactive_identity",
            false,
            false,
            false,
            false,
            false,
            string.Empty,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            reason,
            "No Project Access");

    internal object ToResponse() => new
    {
        contract = Contract,
        actualUserId = ActualUserId,
        effectiveUserId = EffectiveUserId,
        projectId = ProjectId,
        moduleCode = ModuleCode,
        isViewAs = IsViewAs,
        isAdministrator = IsAdministrator,
        isProjectManagerOwner = IsProjectManagerOwner,
        isProjectManagementLeadScope = IsProjectManagementLeadScope,
        isDirectProjectAssignee = IsDirectProjectAssignee,
        isEngineeringLeadScope = IsEngineeringLeadScope,
        explicitCollaborationLevel = ExplicitCollaborationLevel,
        isAccountExecutive = IsAccountExecutive,
        isSolutionArchitect = IsSolutionArchitect,
        canView = CanView,
        canReviewPlanner = CanReviewPlanner,
        canEditPlanner = CanEditPlanner,
        canAdministerPlanner = CanAdministerPlanner,
        canAdoptBaseline = CanAdoptBaseline,
        canManageFinancials = CanManageFinancials,
        canCreateCustomerShare = CanCreateCustomerShare,
        scopeReason = ScopeReason,
        capabilityLabel = CapabilityLabel,
        serverAuthorized = true
    };
}

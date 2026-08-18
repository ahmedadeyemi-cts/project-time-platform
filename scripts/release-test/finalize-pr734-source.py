from pathlib import Path
import re


def read(path: str) -> str:
    return Path(path).read_text()


def write(path: str, source: str) -> None:
    Path(path).write_text(source)


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    return source.replace(old, new, 1)


def replace_exact_count(source: str, old: str, new: str, expected: int, label: str) -> str:
    count = source.count(old)
    if count != expected:
        raise SystemExit(f"{label}: expected {expected} matches, found {count}")
    return source.replace(old, new)


def replace_regex(source: str, pattern: str, replacement: str, label: str, count: int = 1) -> str:
    updated, matches = re.subn(pattern, replacement, source, count=count, flags=re.S)
    if matches != count:
        raise SystemExit(f"{label}: expected {count} regex matches, found {matches}")
    return updated


def replace_in_method(source: str, method_name: str, old: str, new: str) -> str:
    marker = f"    private static async Task<IResult> {method_name}("
    start = source.find(marker)
    if start < 0:
        raise SystemExit(f"{method_name}: method marker not found")
    end = source.find("\n    private static ", start + len(marker))
    if end < 0:
        raise SystemExit(f"{method_name}: method end not found")
    segment = source[start:end]
    count = segment.count(old)
    if count != 1:
        raise SystemExit(f"{method_name}: expected one authorization call, found {count}")
    segment = segment.replace(old, new, 1)
    return source[:start] + segment + source[end:]


# ---------------------------------------------------------------------------
# Module 066 enterprise workspace: separate planner editing from PM governance.
# ---------------------------------------------------------------------------
flowhive_enterprise_path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs"
flowhive_enterprise = read(flowhive_enterprise_path)

flowhive_enterprise = replace_once(
    flowhive_enterprise,
    "internal static class ProjectFlowHiveEnterpriseModule\n{\n    private const string MigrationId = \"086_module_066_flowhive_enterprise_pm\";",
    "internal static class ProjectFlowHiveEnterpriseModule\n{\n    private enum FlowHiveAccessRequirement\n    {\n        View,\n        EditPlanner,\n        AdministerPlanner,\n        CustomerShare\n    }\n\n    private const string MigrationId = \"086_module_066_flowhive_enterprise_pm\";",
    "FlowHive access requirement enum"
)

flowhive_requirements = {
    "GetEnterpriseWorkspaceAsync": ("requireManage: false", "FlowHiveAccessRequirement.View"),
    "SaveWorkingCopyAsync": ("requireManage: true", "FlowHiveAccessRequirement.EditPlanner"),
    "SaveControlsAsync": ("requireManage: true", "FlowHiveAccessRequirement.AdministerPlanner"),
    "CreateRaidAsync": ("requireManage: true", "FlowHiveAccessRequirement.EditPlanner"),
    "UpdateRaidAsync": ("requireManage: true", "FlowHiveAccessRequirement.EditPlanner"),
    "DeleteRaidAsync": ("requireManage: true", "FlowHiveAccessRequirement.EditPlanner"),
    "CreateStatusReportAsync": ("requireManage: true", "FlowHiveAccessRequirement.AdministerPlanner"),
    "CreateCustomerShareAsync": ("requireManage: true", "FlowHiveAccessRequirement.CustomerShare"),
    "RevokeCustomerShareAsync": ("requireManage: true", "FlowHiveAccessRequirement.CustomerShare"),
    "PrepareSowEvidenceAsync": ("requireManage: true", "FlowHiveAccessRequirement.AdministerPlanner"),
}
for method_name, (old_argument, new_argument) in flowhive_requirements.items():
    flowhive_enterprise = replace_in_method(
        flowhive_enterprise,
        method_name,
        f"OpenAuthorizedAsync(projectId, context, {old_argument}, cancellationToken);",
        f"OpenAuthorizedAsync(projectId, context, {new_argument}, cancellationToken);"
    )

flowhive_enterprise = replace_once(
    flowhive_enterprise,
    "        var controls = await LoadControlsAsync(connection, projectId, cancellationToken);",
    "        object controls = access.CanViewFinancials\n            ? await LoadControlsAsync(connection, projectId, cancellationToken)\n            : RedactedControls(projectId);",
    "FlowHive financial-control redaction"
)

flowhive_enterprise = replace_once(
    flowhive_enterprise,
    """                access.IsProjectManagerOwner,
                access.IsAdministrator,
                access.CanView,
                access.CanManage,
                access.CanShare,
                access.CanViewFinancials,
                managementRule = "A Project Manager may mutate only projects for which they are the assigned Project Manager. Administrator support authority is non-transferable and unavailable in View-As."
""",
    """                access.IsProjectManagerOwner,
                access.IsAdministrator,
                access.IsAccountExecutive,
                access.IsSolutionArchitect,
                access.CanView,
                access.CanReviewPlanner,
                access.CanEditPlanner,
                access.CanAdministerPlanner,
                access.CanAdoptBaseline,
                access.CanManage,
                access.CanShare,
                access.CanViewFinancials,
                access.ScopeReason,
                access.CapabilityLabel,
                accessContract = ProjectPlanningAccessResolver.Contract,
                managementRule = "Project Managers and PM Leads retain governance. Associated Engineering collaborators may edit planning content only; associated Account Executives and Solution Architects are read-only. View-As cannot write."
""",
    "FlowHive access response"
)

new_flowhive_access_block = r'''    private static async Task<OpenOutcome> OpenAuthorizedAsync(
        Guid projectId,
        HttpContext context,
        FlowHiveAccessRequirement requirement,
        CancellationToken cancellationToken)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId") ?? actual;
        if (!actual.HasValue || !effective.HasValue)
            return OpenOutcome.Fail(Results.Json(new { status = "session_required", message = "A valid ProjectPulse session is required." }, statusCode: 401));

        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
            return OpenOutcome.Fail(Results.Json(new { status = "configuration_missing", message = "Project FlowHive database configuration is incomplete." }, statusCode: 503));
        var connection = new NpgsqlConnection(config.ConnectionString);
        try { await connection.OpenAsync(cancellationToken); }
        catch
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Results.Json(new { status = "persistence_dependency_unavailable", message = "Project FlowHive persistence is temporarily unavailable." }, statusCode: 503));
        }

        if (!await EnterpriseSchemaReadyAsync(connection, cancellationToken))
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Results.Json(new
            {
                status = "migration_086_required",
                message = "Project FlowHive enterprise persistence requires Migration 086.",
                stateChanged = false
            }, statusCode: 503));
        }

        var planningAccess = await ProjectPlanningAccessResolver.ResolveAsync(
            connection,
            context,
            projectId,
            "066",
            cancellationToken);
        if (!planningAccess.CanView)
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Forbidden("The project is outside the current FlowHive scope."));
        }

        var allowed = requirement switch
        {
            FlowHiveAccessRequirement.View => planningAccess.CanView,
            FlowHiveAccessRequirement.EditPlanner => planningAccess.CanEditPlanner,
            FlowHiveAccessRequirement.AdministerPlanner => planningAccess.CanAdministerPlanner,
            FlowHiveAccessRequirement.CustomerShare => planningAccess.CanCreateCustomerShare,
            _ => false
        };
        if (!allowed)
        {
            await connection.DisposeAsync();
            var message = requirement switch
            {
                FlowHiveAccessRequirement.EditPlanner => "This project is read-only for the current identity. Planner editing requires an associated Engineering collaborator or PM governance role.",
                FlowHiveAccessRequirement.CustomerShare => "Only the assigned Project Manager, authorized PM Lead, or Administrator may create or revoke customer shares.",
                _ => "Only the assigned Project Manager, authorized PM Lead, or Administrator may perform this project-governance action."
            };
            return OpenOutcome.Fail(Forbidden(message));
        }

        var access = await LoadAccessAsync(connection, projectId, planningAccess, cancellationToken);
        if (access is null)
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Forbidden("The project is outside the current FlowHive scope."));
        }
        return new OpenOutcome(connection, access, null);
    }

    private static async Task<bool> EnterpriseSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration)
               AND to_regclass('public.project_flowhive_working_copies') IS NOT NULL
               AND to_regclass('public.project_flowhive_project_controls') IS NOT NULL
               AND to_regclass('public.project_flowhive_raid_items') IS NOT NULL
               AND to_regclass('public.project_flowhive_status_reports') IS NOT NULL
               AND to_regclass('public.project_flowhive_customer_shares') IS NOT NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migration", MigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<ProjectFlowHiveEnterpriseAccess?> LoadAccessAsync(
        NpgsqlConnection connection,
        Guid projectId,
        ProjectPlanningAccess planningAccess,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = planningAccess.EffectiveUserId ?? Guid.Empty;
        const string sql = """
            SELECT project.project_id,project.project_code,project.project_name,
                   COALESCE(client.client_name,''),project.project_manager_user_id,
                   COALESCE(NULLIF(manager.display_name,''),manager.email,'Unassigned'),
                   COALESCE(NULLIF(actor.display_name,''),actor.email,'')
            FROM projects project
            LEFT JOIN clients client ON client.client_id=project.client_id
            LEFT JOIN app_users manager ON manager.user_id=project.project_manager_user_id
            JOIN app_users actor ON actor.user_id=@effective AND actor.is_active=TRUE
            WHERE project.project_id=@project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("effective", effectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var managerId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
        return new ProjectFlowHiveEnterpriseAccess(
            planningAccess.ActualUserId ?? Guid.Empty,
            effectiveUserId,
            reader.GetString(6),
            planningAccess.IsViewAs,
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            managerId,
            reader.GetString(5),
            planningAccess.IsProjectManagerOwner,
            planningAccess.IsAdministrator,
            planningAccess.IsAccountExecutive,
            planningAccess.IsSolutionArchitect,
            planningAccess.CanView,
            planningAccess.CanReviewPlanner,
            planningAccess.CanEditPlanner,
            planningAccess.CanAdministerPlanner,
            planningAccess.CanAdoptBaseline,
            planningAccess.CanAdministerPlanner,
            planningAccess.CanCreateCustomerShare,
            planningAccess.CanManageFinancials,
            planningAccess.ScopeReason,
            planningAccess.CapabilityLabel);
    }

    private static async Task<object?> LoadWorkingCopyAsync'''

flowhive_enterprise = replace_regex(
    flowhive_enterprise,
    r"    private static async Task<OpenOutcome> OpenAuthorizedAsync\([\s\S]*?\n    private static async Task<object\?> LoadWorkingCopyAsync",
    new_flowhive_access_block,
    "FlowHive shared access resolver integration"
)

flowhive_enterprise = replace_once(
    flowhive_enterprise,
    "    private static async Task<object> LoadControlsAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)",
    """    private static object RedactedControls(Guid projectId) => new
    {
        projectId,
        contractType = "restricted",
        currencyCode = "USD",
        approvedBudget = (decimal?)null,
        expenseBudget = (decimal?)null,
        contingencyBudget = (decimal?)null,
        forecastAtCompletion = (decimal?)null,
        percentCompleteMethod = "restricted",
        statusReportCadence = "restricted",
        customerSharingEnabled = false,
        financialNotes = string.Empty,
        restricted = true,
        updatedAt = (DateTimeOffset?)null
    };

    private static async Task<object> LoadControlsAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)""",
    "FlowHive redacted controls helper"
)

flowhive_enterprise = replace_once(
    flowhive_enterprise,
    """internal sealed record ProjectFlowHiveEnterpriseAccess(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string DisplayName,
    bool IsViewAs,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    Guid? ProjectManagerUserId,
    string ProjectManagerName,
    bool IsProjectManagerOwner,
    bool IsAdministrator,
    bool CanView,
    bool CanManage,
    bool CanShare,
    bool CanViewFinancials);""",
    """internal sealed record ProjectFlowHiveEnterpriseAccess(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string DisplayName,
    bool IsViewAs,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    Guid? ProjectManagerUserId,
    string ProjectManagerName,
    bool IsProjectManagerOwner,
    bool IsAdministrator,
    bool IsAccountExecutive,
    bool IsSolutionArchitect,
    bool CanView,
    bool CanReviewPlanner,
    bool CanEditPlanner,
    bool CanAdministerPlanner,
    bool CanAdoptBaseline,
    bool CanManage,
    bool CanShare,
    bool CanViewFinancials,
    string ScopeReason,
    string CapabilityLabel);""",
    "FlowHive access record"
)
write(flowhive_enterprise_path, flowhive_enterprise)


# ---------------------------------------------------------------------------
# Module 066 portfolio and repository project association.
# ---------------------------------------------------------------------------
flowhive_module_path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs"
flowhive_module = read(flowhive_module_path)

association_for_p = """OR p.project_manager_user_id = @user_id
                OR p.account_executive_user_id = @user_id
                OR p.solution_architect_user_id = @user_id
                OR EXISTS (
                    SELECT 1
                    FROM project_planning_collaborators collaborator
                    WHERE collaborator.project_id = p.project_id
                      AND collaborator.user_id = @user_id
                      AND collaborator.module_code = '066'
                      AND collaborator.is_active = TRUE
                      AND collaborator.effective_start_date <= CURRENT_DATE
                      AND (collaborator.effective_end_date IS NULL OR collaborator.effective_end_date >= CURRENT_DATE)
                )
                OR EXISTS ("""
flowhive_module = replace_exact_count(
    flowhive_module,
    "OR p.project_manager_user_id = @user_id\n                OR EXISTS (",
    association_for_p,
    2,
    "FlowHive project association scopes"
)

flowhive_module = replace_once(
    flowhive_module,
    """                OR project.project_manager_user_id = @user_id
                OR (
                    @can_view_team_scope = TRUE""",
    """                OR project.project_manager_user_id = @user_id
                OR project.account_executive_user_id = @user_id
                OR project.solution_architect_user_id = @user_id
                OR EXISTS (
                    SELECT 1
                    FROM project_planning_collaborators collaborator
                    WHERE collaborator.project_id = project.project_id
                      AND collaborator.user_id = @user_id
                      AND collaborator.module_code = '066'
                      AND collaborator.is_active = TRUE
                      AND collaborator.effective_start_date <= CURRENT_DATE
                      AND (collaborator.effective_end_date IS NULL OR collaborator.effective_end_date >= CURRENT_DATE)
                )
                OR (
                    @can_view_team_scope = TRUE""",
    "FlowHive assignment stakeholder scope"
)

flowhive_module = replace_once(
    flowhive_module,
    """    public bool IsEngineeringLead => HasRole(
        "ENGINEERING_LEAD",
        "ENGINEERING_TEAM_LEAD");

    public bool IsExecutive => HasRole(""",
    """    public bool IsEngineeringLead => HasRole(
        "ENGINEERING_LEAD",
        "ENGINEERING_TEAM_LEAD");

    public bool IsAccountExecutive => HasRole(
        "ACCOUNT_EXECUTIVE",
        "SALES_ACCOUNT_EXECUTIVE");

    public bool IsSolutionArchitect => HasRole(
        "SOLUTION_ARCHITECT",
        "SOLUTIONS_ARCHITECT");

    public bool IsExecutive => HasRole(""",
    "FlowHive stakeholder role classification"
)
flowhive_module = replace_once(
    flowhive_module,
    """        || IsPeopleManager
        || IsEngineeringLead;

    public string ScopeLabel""",
    """        || IsPeopleManager
        || IsEngineeringLead
        || IsAccountExecutive
        || IsSolutionArchitect;

    public string ScopeLabel""",
    "FlowHive stakeholder task visibility"
)
flowhive_module = replace_once(
    flowhive_module,
    """            if (IsEngineeringLead) return "engineering_team_scope";
            if (IsProjectManager) return "managed_projects_scope";""",
    """            if (IsEngineeringLead) return "engineering_team_scope";
            if (IsAccountExecutive) return "associated_account_executive_projects";
            if (IsSolutionArchitect) return "associated_solution_architect_projects";
            if (IsProjectManager) return "managed_projects_scope";""",
    "FlowHive stakeholder scope labels"
)
write(flowhive_module_path, flowhive_module)

repository_path = "src/backend/ProjectTime.Api/Modules/PostgresProjectFlowHivePlanRepository.cs"
repository = read(repository_path)
repository = replace_once(
    repository,
    """                  actor.broad_scope
                  OR project.project_manager_user_id=@actor
                  OR EXISTS(SELECT 1 FROM project_assignments assignment
                            WHERE assignment.project_id=plan.project_id AND assignment.user_id=@actor)
              )""",
    """                  actor.broad_scope
                  OR project.project_manager_user_id=@actor
                  OR project.account_executive_user_id=@actor
                  OR project.solution_architect_user_id=@actor
                  OR EXISTS(SELECT 1 FROM project_assignments assignment
                            WHERE assignment.project_id=plan.project_id AND assignment.user_id=@actor
                              AND assignment.effective_start_date<=CURRENT_DATE
                              AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date>=CURRENT_DATE))
                  OR EXISTS(SELECT 1 FROM project_planning_collaborators collaborator
                            WHERE collaborator.project_id=plan.project_id AND collaborator.user_id=@actor
                              AND collaborator.module_code='066' AND collaborator.is_active=TRUE
                              AND collaborator.effective_start_date<=CURRENT_DATE
                              AND (collaborator.effective_end_date IS NULL OR collaborator.effective_end_date>=CURRENT_DATE))
                  OR EXISTS(
                      SELECT 1 FROM project_assignments team_assignment
                      JOIN app_users team_member ON team_member.user_id=team_assignment.user_id AND team_member.is_active=TRUE
                      WHERE team_assignment.project_id=plan.project_id
                        AND team_assignment.effective_start_date<=CURRENT_DATE
                        AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date>=CURRENT_DATE)
                        AND EXISTS(SELECT 1 FROM app_user_role_assignments lead_assignment
                                   JOIN app_roles lead_role ON lead_role.app_role_id=lead_assignment.app_role_id AND lead_role.is_active=TRUE
                                   WHERE lead_assignment.user_id=@actor AND lead_assignment.is_active=TRUE
                                     AND lead_role.role_code IN ('ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD'))
                        AND (
                          EXISTS(SELECT 1 FROM reporting_relationships relationship
                                 WHERE relationship.employee_user_id=team_member.user_id
                                   AND (relationship.manager_user_id=@actor OR relationship.team_lead_user_id=@actor)
                                   AND relationship.effective_start_date<=CURRENT_DATE
                                   AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE))
                          OR EXISTS(SELECT 1 FROM projectpulse_team_scope_assignments scope
                                    WHERE scope.scoped_user_id=@actor AND scope.is_active=TRUE
                                      AND scope.scope_type='engineering_team_lead'
                                      AND ((scope.team_name IS NOT NULL AND lower(COALESCE(team_member.team_name,''))=lower(scope.team_name))
                                        OR (scope.department_name IS NOT NULL AND lower(COALESCE(team_member.department_name,team_member.department,''))=lower(scope.department_name))
                                        OR scope.manager_user_id=team_member.user_id))
                        )
                  )
              )""",
    "FlowHive persisted plan association"
)

new_repository_access_methods = r'''    private static async Task<bool> CanViewPlanAsync(
        NpgsqlConnection connection, Guid actor, Guid planId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT project_id FROM project_flowhive_plans WHERE plan_id=@plan_id;",
            connection);
        command.Parameters.AddWithValue("plan_id", planId);
        var project = await command.ExecuteScalarAsync(cancellationToken);
        if (project is not Guid projectId) return false;
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(
            connection, actor, projectId, "066", cancellationToken);
        return access.CanView;
    }

    private static async Task<bool> CanManageProjectAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actor, Guid projectId,
        CancellationToken cancellationToken)
    {
        _ = transaction;
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(
            connection, actor, projectId, "066", cancellationToken);
        return access.CanEditPlanner;
    }

    private static async Task<bool> CanBaselineProjectAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actor, Guid projectId,
        CancellationToken cancellationToken)
    {
        _ = transaction;
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(
            connection, actor, projectId, "066", cancellationToken);
        return access.CanAdoptBaseline;
    }

    private static async Task<bool> HasProjectPermissionAsync('''
repository = replace_regex(
    repository,
    r"    private static async Task<bool> CanViewPlanAsync\([\s\S]*?\n    private static async Task<bool> HasProjectPermissionAsync\(",
    new_repository_access_methods,
    "FlowHive repository shared access methods"
)
write(repository_path, repository)


# ---------------------------------------------------------------------------
# Module 033 Project Forge project association and review-plan editing.
# ---------------------------------------------------------------------------
forge_path = "src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs"
forge = read(forge_path)
for method_name in ("CreatePlanAsync", "UpdatePlanAsync"):
    forge = replace_in_method(
        forge,
        method_name,
        "if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);",
        "if (!access.CanEditReviewPlan || access.IsViewAs) return WriteForbidden(access);"
    )

new_scope_cte = r'''    private const string ScopeCte = """
        WITH authorized_lead_pms AS (
            SELECT DISTINCT pm.user_id
            FROM app_users pm
            WHERE pm.is_active=TRUE
              AND (
                EXISTS (
                    SELECT 1 FROM reporting_relationships rr
                    WHERE rr.employee_user_id=pm.user_id
                      AND (rr.manager_user_id=@effective_user_id OR rr.team_lead_user_id=@effective_user_id)
                      AND rr.effective_start_date<=CURRENT_DATE
                      AND (rr.effective_end_date IS NULL OR rr.effective_end_date>=CURRENT_DATE)
                )
                OR EXISTS (
                    SELECT 1 FROM projectpulse_team_scope_assignments scope
                    WHERE scope.scoped_user_id=@effective_user_id AND scope.is_active=TRUE
                      AND scope.scope_type='project_management_team_lead'
                      AND ((scope.team_name IS NOT NULL AND LOWER(COALESCE(pm.team_name,''))=LOWER(scope.team_name))
                        OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(pm.department_name,pm.department,''))=LOWER(scope.department_name))
                        OR scope.manager_user_id=pm.user_id)
                )
              )
        ), authorized_engineering_members AS (
            SELECT DISTINCT member.user_id
            FROM app_users member
            WHERE member.is_active=TRUE
              AND (
                EXISTS(
                    SELECT 1 FROM reporting_relationships rr
                    WHERE rr.employee_user_id=member.user_id
                      AND (rr.manager_user_id=@effective_user_id OR rr.team_lead_user_id=@effective_user_id)
                      AND rr.effective_start_date<=CURRENT_DATE
                      AND (rr.effective_end_date IS NULL OR rr.effective_end_date>=CURRENT_DATE)
                )
                OR EXISTS(
                    SELECT 1 FROM projectpulse_team_scope_assignments scope
                    WHERE scope.scoped_user_id=@effective_user_id AND scope.is_active=TRUE
                      AND scope.scope_type='engineering_team_lead'
                      AND ((scope.team_name IS NOT NULL AND LOWER(COALESCE(member.team_name,''))=LOWER(scope.team_name))
                        OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(member.department_name,member.department,''))=LOWER(scope.department_name))
                        OR scope.manager_user_id=member.user_id)
                )
              )
        ), scoped_projects AS (
            SELECT p.project_id
            FROM projects p
            WHERE (
                @is_admin
                OR (@is_pm_lead AND p.project_manager_user_id IN (SELECT user_id FROM authorized_lead_pms))
                OR (@is_pm AND p.project_manager_user_id=@effective_user_id)
                OR (@is_engineer AND EXISTS (
                    SELECT 1 FROM project_assignments self_assignment
                    WHERE self_assignment.project_id=p.project_id AND self_assignment.user_id=@effective_user_id
                      AND self_assignment.effective_start_date<=CURRENT_DATE
                      AND (self_assignment.effective_end_date IS NULL OR self_assignment.effective_end_date>=CURRENT_DATE)
                ))
                OR (@is_engineering_lead AND EXISTS(
                    SELECT 1 FROM project_assignments team_assignment
                    WHERE team_assignment.project_id=p.project_id
                      AND team_assignment.user_id IN (SELECT user_id FROM authorized_engineering_members)
                      AND team_assignment.effective_start_date<=CURRENT_DATE
                      AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date>=CURRENT_DATE)
                ))
                OR (@is_account_executive AND p.account_executive_user_id=@effective_user_id)
                OR (@is_solution_architect AND p.solution_architect_user_id=@effective_user_id)
                OR EXISTS(
                    SELECT 1 FROM project_planning_collaborators collaborator
                    WHERE collaborator.project_id=p.project_id
                      AND collaborator.user_id=@effective_user_id
                      AND collaborator.module_code='033'
                      AND collaborator.is_active=TRUE
                      AND collaborator.effective_start_date<=CURRENT_DATE
                      AND (collaborator.effective_end_date IS NULL OR collaborator.effective_end_date>=CURRENT_DATE)
                )
            )
            AND (@manager_filter IS NULL OR p.project_manager_user_id=@manager_filter)
            AND (@project_filter IS NULL OR p.project_id=@project_filter)
        )
        """;

    private static readonly string ProjectsSql'''
forge = replace_regex(
    forge,
    r"    private const string ScopeCte = \"\"\"[\s\S]*?\n    private static readonly string ProjectsSql",
    new_scope_cte,
    "Project Forge associated project scope"
)

new_can_access_project = r'''    private static async Task<bool> CanAccessProjectAsync(
        NpgsqlConnection connection,
        ProjectForgeAccess access,
        Guid projectId,
        Guid? managerFilter,
        CancellationToken cancellationToken)
    {
        if (managerFilter.HasValue)
        {
            await using var managerCommand = new NpgsqlCommand(
                "SELECT project_manager_user_id FROM projects WHERE project_id=@project_id;",
                connection);
            managerCommand.Parameters.AddWithValue("project_id", projectId);
            var manager = await managerCommand.ExecuteScalarAsync(cancellationToken);
            if (manager is not Guid managerUserId || managerUserId != managerFilter.Value)
                return false;
        }

        var planningAccess = await ProjectPlanningAccessResolver.ResolveForActorAsync(
            connection,
            access.EffectiveUserId,
            projectId,
            "033",
            cancellationToken);
        return planningAccess.CanView;
    }

    private static async Task<bool> IsEligibleEngineerReviewerAsync'''
forge = replace_regex(
    forge,
    r"    private static async Task<bool> CanAccessProjectAsync\([\s\S]*?\n    private static async Task<bool> IsEligibleEngineerReviewerAsync",
    new_can_access_project,
    "Project Forge shared access check"
)

forge = replace_once(
    forge,
    """        command.Parameters.AddWithValue("is_engineer", access.IsEngineer);
        command.Parameters.AddWithValue("can_view_all_tasks", access.CanViewAllScopedTasks);""",
    """        command.Parameters.AddWithValue("is_engineer", access.IsEngineer);
        command.Parameters.AddWithValue("is_engineering_lead", access.IsEngineeringLead);
        command.Parameters.AddWithValue("is_account_executive", access.IsAccountExecutive);
        command.Parameters.AddWithValue("is_solution_architect", access.IsSolutionArchitect);
        command.Parameters.AddWithValue("can_view_all_tasks", access.CanViewAllScopedTasks);""",
    "Project Forge scope parameters"
)
forge = replace_once(
    forge,
    """        command.Parameters.AddWithValue("can_write_estimate", !access.IsViewAs && (access.CanManage || access.CanEditAssignedEstimate));""",
    """        command.Parameters.AddWithValue("can_write_estimate", !access.IsViewAs && (access.CanManage || access.CanEditReviewPlan || access.CanEditAssignedEstimate));""",
    "Project Forge review-plan estimate parameter"
)

new_forge_access_record = r'''    private sealed record ProjectForgeAccess(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string DisplayName,
        string Email,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        bool IsViewAs,
        bool IsActive)
    {
        public static ProjectForgeAccess Inactive(Guid actual, Guid effective) => new(actual, effective, string.Empty, string.Empty, new HashSet<string>(), new HashSet<string>(), false, false);
        private bool HasRole(params string[] codes) => codes.Any(Roles.Contains);
        private bool HasPermission(string code) => Permissions.Contains(code);
        public bool IsAdministrator => HasRole("SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR");
        public bool IsProjectManagementLead => HasRole("PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD");
        public bool IsProjectManager => !IsAdministrator && !IsProjectManagementLead && HasRole("PROJECT_MANAGER", "PROJECT_MANAGEMENT");
        public bool IsEngineeringLead => !IsAdministrator && !IsProjectManagementLead && !IsProjectManager
            && HasRole("ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD");
        public bool IsAccountExecutive => !IsAdministrator && HasRole("ACCOUNT_EXECUTIVE", "SALES_ACCOUNT_EXECUTIVE");
        public bool IsSolutionArchitect => !IsAdministrator && HasRole("SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT");
        public bool IsEngineer => !IsAdministrator && !IsProjectManagementLead && !IsProjectManager
            && (IsEngineeringLead
                || HasRole("ENGINEER", "ENGINEERING", "SYSTEMS_ENGINEER", "NETWORK_ENGINEER", "ENTERPRISE_NETWORK_ENGINEER")
                || HasPermission("EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033"));
        public bool CanView => IsActive && (IsAdministrator || IsProjectManagementLead || IsProjectManager || IsEngineer
            || IsAccountExecutive || IsSolutionArchitect
            || HasPermission("VIEW_PROJECT_FORGE_033") || HasPermission("VIEW_ASSOCIATED_PROJECT_FORGE_033"));
        public bool CanManage => IsAdministrator || IsProjectManagementLead || IsProjectManager || HasPermission("MANAGE_PROJECT_FORGE_033");
        public bool CanReviewPlan => CanView && (CanManage || HasPermission("REVIEW_PROJECT_FORGE_PLAN_033"));
        public bool CanEditReviewPlan => CanView && (CanManage || HasPermission("EDIT_PROJECT_FORGE_REVIEW_PLAN_033"));
        public bool CanAdoptPlan => CanManage;
        public bool CanUseAi => CanManage && (IsAdministrator || IsProjectManagementLead || IsProjectManager || HasPermission("USE_PROJECT_FORGE_AI_033"));
        public bool CanEditAssignedEstimate => IsEngineer || HasPermission("EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033");
        public bool CanUpdateAssignedTaskStatus => HasPermission("UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033");
        public bool CanViewFinancials => CanManage && !IsViewAs;
        public bool CanViewAiCitations => CanManage && !IsViewAs;
        public bool CanSelectProjectManager => IsAdministrator || IsProjectManagementLead;
        public bool CanViewAllScopedTasks => IsAdministrator || IsProjectManagementLead || IsProjectManager
            || IsEngineeringLead || IsAccountExecutive || IsSolutionArchitect
            || HasPermission("VIEW_ASSOCIATED_PROJECT_FORGE_033");
        public object ToResponse(Guid? selectedManager) => new
        {
            actualUserId = ActualUserId, effectiveUserId = EffectiveUserId, DisplayName, Email,
            roles = Roles.OrderBy(value => value), isViewAs = IsViewAs,
            scope = IsAdministrator ? "all_projects"
                : IsProjectManagementLead ? "managed_pm_team_projects"
                : IsProjectManager ? "own_managed_projects"
                : IsEngineeringLead ? "assigned_engineering_team_projects"
                : IsAccountExecutive ? "associated_account_executive_projects"
                : IsSolutionArchitect ? "associated_solution_architect_projects"
                : "assigned_projects_and_tasks",
            capabilityLabel = CanManage ? "Project Owner — Full Control"
                : CanEditReviewPlan ? "Engineering Collaborator — Planner Edit"
                : CanReviewPlan ? "Technical Reviewer — Review and Comment"
                : "Project Stakeholder — Read Only",
            accessContract = ProjectPlanningAccessResolver.Contract,
            canSelectProjectManager = CanSelectProjectManager, selectedProjectManagerUserId = selectedManager,
            canManage = CanManage && !IsViewAs,
            canAdministerPlanner = CanManage && !IsViewAs,
            canReviewPlan = CanReviewPlan && !IsViewAs,
            canEditReviewPlan = CanEditReviewPlan && !IsViewAs,
            canAdoptPlan = CanAdoptPlan && !IsViewAs,
            canUseAi = CanUseAi && !IsViewAs,
            canEditAssignedEstimate = CanEditAssignedEstimate && !IsViewAs,
            canUpdateAssignedTaskStatus = CanUpdateAssignedTaskStatus && !IsViewAs,
            canViewFinancials = CanViewFinancials,
            serverAuthorized = true
        };
    }

    private sealed record AdoptionTask'''
forge = replace_regex(
    forge,
    r"    private sealed record ProjectForgeAccess\([\s\S]*?\n    private sealed record AdoptionTask",
    new_forge_access_record,
    "Project Forge capability record"
)
write(forge_path, forge)

interactive_path = "src/backend/ProjectTime.Api/Modules/ProjectForgeInteractiveModule.cs"
interactive = read(interactive_path)
interactive = replace_once(
    interactive,
    """    private static bool CanManageTask(ProjectForgeAccess access, InteractiveTaskState state, bool workflowOnly)
        => !access.IsViewAs && (access.CanManage
            || (workflowOnly && state.RecordSource == "canonical" && state.IsAssignedToEffectiveUser && access.CanUpdateAssignedTaskStatus));""",
    """    private static bool CanManageTask(ProjectForgeAccess access, InteractiveTaskState state, bool workflowOnly)
        => !access.IsViewAs && (access.CanManage
            || (state.RecordSource == "review_plan" && access.CanEditReviewPlan)
            || (workflowOnly && state.RecordSource == "canonical" && state.IsAssignedToEffectiveUser && access.CanUpdateAssignedTaskStatus));""",
    "Project Forge review-plan task editing"
)
write(interactive_path, interactive)


# ---------------------------------------------------------------------------
# Frontend capability-driven controls.
# ---------------------------------------------------------------------------
flowhive_ui_path = "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx"
flowhive_ui = read(flowhive_ui_path)
flowhive_ui = replace_once(
    flowhive_ui,
    """  const selectedProject = projects.find((project) => project.projectId === selectedProjectId) || null;
  const scheduleByWbs = useMemo(() => new Map(""",
    """  const selectedProject = projects.find((project) => project.projectId === selectedProjectId) || null;
  const canEditPlanner = Boolean(enterprise?.access?.canEditPlanner && !enterprise?.access?.isViewAs);
  const canAdministerPlanner = Boolean(enterprise?.access?.canAdministerPlanner && !enterprise?.access?.isViewAs);
  const canAdoptBaseline = Boolean(enterprise?.access?.canAdoptBaseline && !enterprise?.access?.isViewAs);
  const capabilityLabel = enterprise?.access?.capabilityLabel || 'Project scope resolving';
  const scheduleByWbs = useMemo(() => new Map(""",
    "FlowHive frontend capabilities"
)
flowhive_ui = replace_once(
    flowhive_ui,
    "setNotice(`Loaded PM working-copy revision ${result.workingCopy.workingRevision}.`);",
    "setNotice(`Loaded project planning working-copy revision ${result.workingCopy.workingRevision}.`);",
    "FlowHive working-copy terminology"
)
flowhive_ui = replace_once(
    flowhive_ui,
    "setNotice(`PM working-copy revision ${result.workingRevision} saved. The canonical project and immutable plan history were not changed.`);",
    "setNotice(`Project planning working-copy revision ${result.workingRevision} saved. The canonical project and immutable plan history were not changed.`);",
    "FlowHive save terminology"
)
flowhive_ui = replace_once(
    flowhive_ui,
    """          <div><span>View-As</span><strong>{portfolio.access.isViewAs ? 'Read-only preview' : 'Not active'}</strong></div>
          <div><span>Persistence</span><strong>{capabilityResponse?.databaseMutationEnabled ? 'Ready' : 'Unavailable'}</strong></div>""",
    """          <div><span>View-As</span><strong>{portfolio.access.isViewAs ? 'Read-only preview' : 'Not active'}</strong></div>
          <div><span>Planning capability</span><strong>{capabilityLabel}</strong></div>
          <div><span>Persistence</span><strong>{capabilityResponse?.databaseMutationEnabled ? 'Ready' : 'Unavailable'}</strong></div>""",
    "FlowHive capability label"
)
flowhive_ui = replace_once(
    flowhive_ui,
    """            <button type="button" onClick={createLocalDraft} disabled={!selectedProject}>Create/reset draft</button><button type="button" onClick={() => loadEnterpriseWorkspace(selectedProjectId, true)} disabled={!enterprise?.workingCopy}>Load working copy</button>
            <button type="button" className="primary flowhive-ai-planner-button" onClick={previewAiRequest} disabled={!draftPlan || busy}>{busy === 'ai-planner' ? 'Building from SOW…' : 'AI Planner'}</button>
            <button type="button" onClick={validatePlan} disabled={!draftPlan || busy}>Validate</button>
            <button type="button" onClick={calculateSchedule} disabled={!draftPlan || busy}>{busy === 'schedule' ? 'Calculating…' : 'Calculate schedule'}</button>
            <button type="button" onClick={saveDraft} disabled={!draftPlan || busy || portfolio?.access?.isViewAs}>{busy === 'save' ? 'Saving…' : 'Save immutable version'}</button>
            <button type="button" onClick={establishBaseline} disabled={!draftPlan?.planId || busy || portfolio?.access?.isViewAs || baselineNote.trim().length < 10}>{busy === 'baseline' ? 'Approving…' : 'Establish reviewed baseline'}</button>
          </div>
          <FlowHiveSaveBar dirty={dirty} workingCopy={enterprise?.workingCopy} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onSaveWorkingCopy={saveWorkingCopy} onSaveVersion={saveDraft} />""",
    """            <button type="button" onClick={createLocalDraft} disabled={!selectedProject || !canEditPlanner}>Create/reset draft</button><button type="button" onClick={() => loadEnterpriseWorkspace(selectedProjectId, true)} disabled={!enterprise?.workingCopy}>Load working copy</button>
            <button type="button" className="primary flowhive-ai-planner-button" onClick={previewAiRequest} disabled={!draftPlan || busy || !canAdministerPlanner}>{busy === 'ai-planner' ? 'Building from SOW…' : 'AI Planner'}</button>
            <button type="button" onClick={validatePlan} disabled={!draftPlan || busy}>Validate</button>
            <button type="button" onClick={calculateSchedule} disabled={!draftPlan || busy}>Calculate schedule</button>
            <button type="button" onClick={saveDraft} disabled={!draftPlan || busy || !canEditPlanner}>{busy === 'save' ? 'Saving…' : 'Save immutable version'}</button>
            <button type="button" onClick={establishBaseline} disabled={!draftPlan?.planId || busy || !canAdoptBaseline || baselineNote.trim().length < 10}>{busy === 'baseline' ? 'Approving…' : 'Establish reviewed baseline'}</button>
          </div>
          <FlowHiveSaveBar dirty={dirty} workingCopy={enterprise?.workingCopy} canManage={canEditPlanner} busy={busy} onSaveWorkingCopy={saveWorkingCopy} onSaveVersion={saveDraft} />""",
    "FlowHive planner capability controls"
)
# Fail closed for controlled-input edits when the server grants read-only access.
for signature in (
    "  function createLocalDraft() {",
    "  function updatePlan(field, value) {",
    "  function updateTask(index, field, value) {",
    "  function updateDependencyForTask(index, field, value) {",
    "  function updateTaskResource(taskWbs, resourceUserId) {",
    "  function addTask(phaseWbs) {",
    "  function deleteTask(wbsNumber) {",
    "  function dropTask(targetWbs, targetPhaseWbs, placement = 'before') {",
    "  function changeTaskPhase(wbsNumber, phaseWbs) {",
    "  function moveTaskOffset(wbsNumber, offset) {",
    "  function updateTaskStartDate(index, value) {",
    "  function updateTaskEndDate(index, value, scheduledStart) {"
):
    flowhive_ui = replace_once(
        flowhive_ui,
        signature,
        signature + "\n    if (!canEditPlanner) return;",
        f"FlowHive read-only guard {signature}"
    )
write(flowhive_ui_path, flowhive_ui)

panels_path = "src/frontend/project-time-web/src/ProjectFlowHiveEnterprisePanels.jsx"
panels = read(panels_path)
panels = panels.replace(
    "export function FlowHiveStatusRaidPanel({ enterprise, draftPlan, statusDraft, setStatusDraft, newRaid, setNewRaid, canManage, busy, onCreateRaid, onDeleteRaid, onGenerateSummary, onCreateStatusReport })",
    "export function FlowHiveStatusRaidPanel({ enterprise, draftPlan, statusDraft, setStatusDraft, newRaid, setNewRaid, canEditPlanner, canAdministerPlanner, busy, onCreateRaid, onDeleteRaid, onGenerateSummary, onCreateStatusReport })"
)
panels = panels.replace("{canManage ? <div className=\"flowhive-raid-create\">", "{canEditPlanner ? <div className=\"flowhive-raid-create\">")
panels = panels.replace("<td>{canManage ? <button type=\"button\" className=\"danger-quiet\"", "<td>{canEditPlanner ? <button type=\"button\" className=\"danger-quiet\"")
panels = panels.replace("disabled={!canManage}", "disabled={!canAdministerPlanner}")
panels = panels.replace("disabled={!canManage || busy}", "disabled={!canAdministerPlanner || busy}")
write(panels_path, panels)

# Update the FlowHive panel call without changing financial/share PM controls.
flowhive_ui = read(flowhive_ui_path)
flowhive_ui = flowhive_ui.replace(
    "<FlowHiveStatusRaidPanel enterprise={enterprise} draftPlan={draftPlan} statusDraft={statusDraft} setStatusDraft={setStatusDraft} newRaid={newRaid} setNewRaid={setNewRaid} canManage={Boolean(enterprise?.access?.canManage)}",
    "<FlowHiveStatusRaidPanel enterprise={enterprise} draftPlan={draftPlan} statusDraft={statusDraft} setStatusDraft={setStatusDraft} newRaid={newRaid} setNewRaid={setNewRaid} canEditPlanner={canEditPlanner} canAdministerPlanner={canAdministerPlanner}"
)
write(flowhive_ui_path, flowhive_ui)

forge_ui_path = "src/frontend/project-time-web/src/ProjectForgeCenter.jsx"
forge_ui = read(forge_ui_path)
forge_ui = replace_once(
    forge_ui,
    """  const canManage = Boolean(data?.access?.canManage && !data?.access?.isViewAs);
  const canUseAi = Boolean(data?.access?.canUseAi && !data?.access?.isViewAs);
  const canMoveWorkflow = canManage || Boolean(data?.access?.canUpdateAssignedTaskStatus && !data?.access?.isViewAs);
  const canEditEstimate = Boolean(data?.access?.canEditAssignedEstimate && !data?.access?.isViewAs) || canManage;""",
    """  const canManage = Boolean(data?.access?.canManage && !data?.access?.isViewAs);
  const canEditReviewPlan = Boolean(data?.access?.canEditReviewPlan && !data?.access?.isViewAs);
  const canReviewPlan = Boolean(data?.access?.canReviewPlan && !data?.access?.isViewAs);
  const canEditWorkspace = workspace === 'review_plan' ? canEditReviewPlan : canManage;
  const canUseAi = Boolean(data?.access?.canUseAi && !data?.access?.isViewAs);
  const canMoveWorkflow = canEditWorkspace || Boolean(data?.access?.canUpdateAssignedTaskStatus && !data?.access?.isViewAs);
  const canEditEstimate = Boolean(data?.access?.canEditAssignedEstimate && !data?.access?.isViewAs) || canEditReviewPlan || canManage;""",
    "Project Forge frontend capabilities"
)
forge_ui = forge_ui.replace("canManage={canManage}", "canManage={canEditWorkspace}")
forge_ui = replace_once(
    forge_ui,
    """      <header className="forge-header">
        <div className="forge-brand"><img src={usSignalLogoUrl} alt="US Signal" /><span>MODULE 033</span><h2>Project Forge</h2><p>Live project planning, governed estimates, and document-grounded AI.</p></div>""",
    """      <header className="forge-header">
        <div className="forge-brand"><img src={usSignalLogoUrl} alt="US Signal" /><span>MODULE 033</span><h2>Project Forge</h2><p>Live project planning, governed estimates, and document-grounded AI.</p><small>{data?.access?.capabilityLabel || 'Project scope resolving'}{canReviewPlan && !canEditReviewPlan ? ' · Review access' : ''}</small></div>""",
    "Project Forge capability label"
)
write(forge_ui_path, forge_ui)


# ---------------------------------------------------------------------------
# Focused source validator.
# ---------------------------------------------------------------------------
validator = r'''import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const failures = [];
const requireText = (source, text, label) => {
  if (!source.includes(text)) failures.push(`${label}: missing ${JSON.stringify(text)}`);
};
const rejectText = (source, text, label) => {
  if (source.includes(text)) failures.push(`${label}: forbidden ${JSON.stringify(text)}`);
};

const migration = read('database/migrations/095_project_planning_collaboration_access.sql');
const rollback = read('database/rollback/095_project_planning_collaboration_access_rollback.sql');
const resolver = read('src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs');
const flowhive = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs');
const flowhivePortfolio = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs');
const flowhiveRepository = read('src/backend/ProjectTime.Api/Modules/PostgresProjectFlowHivePlanRepository.cs');
const forge = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const forgeInteractive = read('src/backend/ProjectTime.Api/Modules/ProjectForgeInteractiveModule.cs');
const flowhiveUi = read('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx');
const forgeUi = read('src/frontend/project-time-web/src/ProjectForgeCenter.jsx');

[
  'project_planning_collaborators',
  'project_planning_collaboration_audit_events',
  'EDIT_FLOWHIVE_PLANNER_066',
  'EDIT_PROJECT_FORGE_REVIEW_PLAN_033',
  'VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066',
  'VIEW_ASSOCIATED_PROJECT_FORGE_033',
  "('ACCOUNT_EXECUTIVE','VIEW_PROJECT_FLOWHIVE_066')",
  "('SOLUTION_ARCHITECT','VIEW_PROJECT_FORGE_033')",
  '095_project_planning_collaboration_access'
].forEach((text) => requireText(migration, text, 'Migration 095'));
requireText(rollback, 'Rollback 095 refused: project planning collaborator assignments exist.', 'guarded rollback');
requireText(rollback, 'immutable project planning collaboration audit evidence exists', 'guarded rollback');

[
  'PROJECT_PLANNING_COLLABORATION_V1',
  'associated_account_executive',
  'associated_solution_architect',
  'assigned_engineering_team_scope',
  'CanEditPlanner',
  'CanAdministerPlanner',
  'CanAdoptBaseline',
  'CanCreateCustomerShare',
  'Project Stakeholder — Read Only'
].forEach((text) => requireText(resolver, text, 'shared planning access resolver'));
rejectText(resolver, 'scoped_role_policy_modules', 'module owner metadata must not grant planning access');

[
  'FlowHiveAccessRequirement.EditPlanner',
  'FlowHiveAccessRequirement.AdministerPlanner',
  'FlowHiveAccessRequirement.CustomerShare',
  'ProjectPlanningAccessResolver.ResolveAsync',
  'CanReviewPlanner',
  'CanEditPlanner',
  'CanAdministerPlanner',
  'CapabilityLabel',
  'RedactedControls'
].forEach((text) => requireText(flowhive, text, 'FlowHive enterprise access'));
requireText(flowhiveRepository, 'return access.CanEditPlanner;', 'FlowHive plan-version edit authority');
requireText(flowhiveRepository, 'return access.CanAdoptBaseline;', 'FlowHive baseline PM authority');
requireText(flowhivePortfolio, 'p.account_executive_user_id = @user_id', 'FlowHive AE scope');
requireText(flowhivePortfolio, 'p.solution_architect_user_id = @user_id', 'FlowHive SA scope');

[
  'VIEW_ASSOCIATED_PROJECT_FORGE_033',
  'CanEditReviewPlan',
  'CanReviewPlan',
  'IsEngineeringLead',
  'IsAccountExecutive',
  'IsSolutionArchitect',
  'authorized_engineering_members',
  'p.account_executive_user_id=@effective_user_id',
  'p.solution_architect_user_id=@effective_user_id',
  'ProjectPlanningAccessResolver.ResolveForActorAsync'
].forEach((text) => requireText(forge, text, 'Project Forge collaboration access'));
requireText(forgeInteractive, 'state.RecordSource == "review_plan" && access.CanEditReviewPlan', 'Project Forge review-plan task edit');

requireText(flowhiveUi, 'canEditPlanner', 'FlowHive capability-driven UI');
requireText(flowhiveUi, 'canAdoptBaseline', 'FlowHive baseline control');
requireText(forgeUi, 'canEditReviewPlan', 'Project Forge capability-driven UI');
requireText(forgeUi, 'canEditWorkspace', 'Project Forge workspace capability');

if (failures.length) {
  console.error('Project planning collaboration validation failed:');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}
console.log('Project planning collaboration validation passed.');
'''
Path('tests/validate-project-planning-collaboration-access.mjs').write_text(validator)

# Temporary publisher removes itself from the final source commit.
Path('scripts/release-test/finalize-pr734-source.py').unlink(missing_ok=True)

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


def replace_count(source: str, old: str, new: str, expected: int, label: str) -> str:
    count = source.count(old)
    if count != expected:
        raise SystemExit(f"{label}: expected {expected} matches, found {count}")
    return source.replace(old, new)


def replace_regex(source: str, pattern: str, replacement: str, label: str, count: int = 1) -> str:
    updated, matched = re.subn(pattern, replacement, source, count=count, flags=re.S)
    if matched != count:
        raise SystemExit(f"{label}: expected {count} regex matches, found {matched}")
    return updated


def transform_method(source: str, method_name: str, transform) -> str:
    marker = f"    private static async Task<IResult> {method_name}("
    start = source.find(marker)
    if start < 0:
        raise SystemExit(f"{method_name}: method marker not found")
    end = source.find("\n    private static ", start + len(marker))
    if end < 0:
        raise SystemExit(f"{method_name}: method end not found")
    segment = source[start:end]
    updated = transform(segment)
    if updated == segment:
        raise SystemExit(f"{method_name}: transformation produced no change")
    return source[:start] + updated + source[end:]


# ---------------------------------------------------------------------------
# Fix capability-driven FlowHive UI variables so read-only users cannot mutate
# and Engineering collaborators can save only planning artifacts.
# ---------------------------------------------------------------------------
panels_path = 'src/frontend/project-time-web/src/ProjectFlowHiveEnterprisePanels.jsx'
panels = read(panels_path)
panels = replace_once(
    panels,
    '<button type="button" disabled={!canAdministerPlanner || busy} onClick={onSaveVersion}>',
    '<button type="button" disabled={!canManage || busy} onClick={onSaveVersion}>',
    'FlowHive save-bar immutable version capability'
)

financial_start = panels.index('export function FlowHiveFinancialsPanel(')
financial_end = panels.index('\nexport function FlowHiveStatusRaidPanel(', financial_start)
financial_segment = panels[financial_start:financial_end]
financial_count = financial_segment.count('canAdministerPlanner')
if financial_count < 8:
    raise SystemExit(f'FlowHive financial panel: expected at least 8 PM capability references, found {financial_count}')
financial_segment = financial_segment.replace('canAdministerPlanner', 'canManage')
panels = panels[:financial_start] + financial_segment + panels[financial_end:]

panels = replace_once(
    panels,
    'disabled={!canManage || busy || (statusDraft.executiveSummary || \'\').trim().length < 20}',
    'disabled={!canAdministerPlanner || busy || (statusDraft.executiveSummary || \'\').trim().length < 20}',
    'FlowHive immutable status-report capability'
)

sharing_start = panels.index('export function FlowHiveCustomerSharingPanel(')
sharing_segment = panels[sharing_start:]
sharing_count = sharing_segment.count('canAdministerPlanner')
if sharing_count != 1:
    raise SystemExit(f'FlowHive customer sharing panel: expected one stray capability reference, found {sharing_count}')
sharing_segment = sharing_segment.replace('canAdministerPlanner', 'canManage')
panels = panels[:sharing_start] + sharing_segment
write(panels_path, panels)


# ---------------------------------------------------------------------------
# Preserve established FlowHive read scopes while adding project association.
# PTC/Executive remain broad read-only and Managers remain assigned-team read.
# ---------------------------------------------------------------------------
resolver_path = 'src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs'
resolver = read(resolver_path)
resolver = replace_once(
    resolver,
    '''    private static readonly string[] SolutionArchitectRoles =
    [
        "SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT"
    ];
''',
    '''    private static readonly string[] SolutionArchitectRoles =
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
''',
    'shared resolver established read roles'
)
resolver = replace_once(
    resolver,
    '''        var accountExecutiveRole = HasAny(identity.Roles, AccountExecutiveRoles);
        var solutionArchitectRole = HasAny(identity.Roles, SolutionArchitectRoles);
''',
    '''        var accountExecutiveRole = HasAny(identity.Roles, AccountExecutiveRoles);
        var solutionArchitectRole = HasAny(identity.Roles, SolutionArchitectRoles);
        var projectCoordinatorRole = HasAny(identity.Roles, ProjectCoordinatorRoles);
        var executiveRole = HasAny(identity.Roles, ExecutiveRoles);
        var peopleManagerRole = HasAny(identity.Roles, PeopleManagerRoles);
''',
    'shared resolver role evaluation'
)
resolver = replace_once(
    resolver,
    '''        var solutionArchitect = solutionArchitectRole
            && association.SolutionArchitectUserId == effectiveUserId;

        var explicitLevel = association.ExplicitCollaborationLevel;
''',
    '''        var solutionArchitect = solutionArchitectRole
            && association.SolutionArchitectUserId == effectiveUserId;
        var businessBroadRead = normalizedModule == "066"
            && (projectCoordinatorRole || executiveRole);
        var peopleManagerScope = normalizedModule == "066"
            && peopleManagerRole
            && association.EngineeringLeadScope;

        var explicitLevel = association.ExplicitCollaborationLevel;
''',
    'shared resolver established association evaluation'
)
resolver = replace_once(
    resolver,
    '''            || explicitViewer
            || accountExecutive
            || solutionArchitect;
''',
    '''            || explicitViewer
            || accountExecutive
            || solutionArchitect
            || businessBroadRead
            || peopleManagerScope;
''',
    'shared resolver associated project scope'
)
resolver = replace_once(
    resolver,
    '''        var canView = associated && (administrator || viewPermission);
''',
    '''        var canView = associated && (administrator || businessBroadRead || viewPermission);
''',
    'shared resolver broad read preservation'
)
resolver = replace_once(
    resolver,
    '''        var scopeReason = administrator ? "administrator_support"
            : projectManagerOwner ? "assigned_project_manager"
''',
    '''        var scopeReason = administrator ? "administrator_support"
            : projectCoordinatorRole && businessBroadRead ? "project_team_coordinator_business_scope"
            : executiveRole && businessBroadRead ? "executive_read_scope"
            : peopleManagerScope ? "manager_team_scope"
            : projectManagerOwner ? "assigned_project_manager"
''',
    'shared resolver scope reason preservation'
)
write(resolver_path, resolver)


# ---------------------------------------------------------------------------
# Harden Migration 095 audit durability, prerequisites, and legacy module-view
# compatibility for roles created after the original 033/066 migrations.
# ---------------------------------------------------------------------------
migration_path = 'database/migrations/095_project_planning_collaboration_access.sql'
migration = read(migration_path)
migration = replace_once(
    migration,
    '''       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.project_forge_plans') IS NULL
       OR to_regclass('public.project_flowhive_plans') IS NULL THEN
''',
    '''       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.reporting_relationships') IS NULL
       OR to_regclass('public.projectpulse_team_scope_assignments') IS NULL
       OR to_regclass('public.project_forge_plans') IS NULL
       OR to_regclass('public.project_flowhive_plans') IS NULL THEN
''',
    'Migration 095 scope prerequisites'
)
migration = replace_once(
    migration,
    '''    project_planning_collaborator_id UUID NULL,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    module_code VARCHAR(16) NOT NULL,
    event_code VARCHAR(80) NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
''',
    '''    project_planning_collaborator_id UUID NULL,
    project_id UUID NOT NULL,
    user_id UUID NOT NULL,
    module_code VARCHAR(16) NOT NULL,
    event_code VARCHAR(80) NOT NULL,
    actor_user_id UUID NULL,
''',
    'Migration 095 immutable audit foreign-key removal'
)

compatibility_grants = '''
-- Reconcile legacy module-view permissions for supported Engineering role aliases.
-- These grants are recorded so rollback removes only rows introduced by Migration 095.
WITH desired(role_code,permission_code) AS (
    VALUES
        ('ENGINEER','VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEERING','VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEERING_LEAD','VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEERING_TEAM_LEAD','VIEW_PROJECT_FLOWHIVE_066'),
        ('SYSTEMS_ENGINEER','VIEW_PROJECT_FLOWHIVE_066'),
        ('NETWORK_ENGINEER','VIEW_PROJECT_FLOWHIVE_066'),
        ('ENTERPRISE_NETWORK_ENGINEER','VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEER','VIEW_PROJECT_FORGE_033'),
        ('ENGINEERING','VIEW_PROJECT_FORGE_033'),
        ('ENGINEERING_LEAD','VIEW_PROJECT_FORGE_033'),
        ('ENGINEERING_TEAM_LEAD','VIEW_PROJECT_FORGE_033'),
        ('SYSTEMS_ENGINEER','VIEW_PROJECT_FORGE_033'),
        ('NETWORK_ENGINEER','VIEW_PROJECT_FORGE_033'),
        ('ENTERPRISE_NETWORK_ENGINEER','VIEW_PROJECT_FORGE_033')
), candidates AS (
    SELECT role.app_role_id,permission.app_permission_id
    FROM desired
    JOIN app_roles role
      ON UPPER(role.role_code)=desired.role_code
     AND role.is_active=TRUE
    JOIN app_permissions permission
      ON permission.permission_code=desired.permission_code
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id=role.app_role_id
     AND existing.app_permission_id=permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id,app_permission_id,created_at)
    SELECT app_role_id,app_permission_id,NOW() FROM candidates
    ON CONFLICT(app_role_id,app_permission_id) DO NOTHING
    RETURNING app_role_id,app_permission_id
)
INSERT INTO project_planning_095_role_grants(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM inserted
ON CONFLICT DO NOTHING;

'''
migration = replace_once(
    migration,
    'GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE project_planning_collaborators TO "ptp_app";\n',
    compatibility_grants + 'GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE project_planning_collaborators TO "ptp_app";\n',
    'Migration 095 legacy module-view reconciliation'
)
write(migration_path, migration)


# ---------------------------------------------------------------------------
# Project Forge: Engineering collaborators may edit technical review-plan data,
# but PM-only cost, reviewer-assignment, AI, canonical, and adoption authority is
# preserved. Whole-plan saves retain restricted fields server-side.
# ---------------------------------------------------------------------------
forge_path = 'src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs'
forge = read(forge_path)


def patch_create_plan(segment: str) -> str:
    segment = replace_once(
        segment,
        '        if (!access.CanEditReviewPlan || access.IsViewAs) return WriteForbidden(access);\n',
        '        if (!access.CanEditReviewPlan || access.IsViewAs) return WriteForbidden(access);\n        var effectiveRequest = access.CanManage ? request : RestrictNewCollaboratorPlan(request);\n',
        'Project Forge create-plan collaborator restriction'
    )
    for old, new in (
        ('request.ProjectId', 'effectiveRequest.ProjectId'),
        ('request.PlanName', 'effectiveRequest.PlanName'),
        ('request.Tasks', 'effectiveRequest.Tasks'),
        ('request.Dependencies', 'effectiveRequest.Dependencies')
    ):
        segment = segment.replace(old, new)
    segment = segment.replace('ValidatePlan(request)', 'ValidatePlan(effectiveRequest)')
    segment = segment.replace('InsertPlanAsync(connection, transaction, planId, request,', 'InsertPlanAsync(connection, transaction, planId, effectiveRequest,')
    return segment


forge = transform_method(forge, 'CreatePlanAsync', patch_create_plan)


def patch_update_plan(segment: str) -> str:
    segment = replace_once(
        segment,
        '        if (!access.CanEditReviewPlan || access.IsViewAs) return WriteForbidden(access);\n',
        '''        if (!access.CanEditReviewPlan || access.IsViewAs) return WriteForbidden(access);
        var effectiveRequest = access.CanManage
            ? request
            : await PreserveCollaboratorRestrictedFieldsAsync(connection, planId, request, cancellationToken);
''',
        'Project Forge update-plan collaborator restriction'
    )
    for old, new in (
        ('request.ProjectId', 'effectiveRequest.ProjectId'),
        ('request.PlanName', 'effectiveRequest.PlanName'),
        ('request.Objective', 'effectiveRequest.Objective'),
        ('request.StartDate', 'effectiveRequest.StartDate'),
        ('request.Tasks', 'effectiveRequest.Tasks'),
        ('request.Dependencies', 'effectiveRequest.Dependencies'),
        ('request.ReviewNote', 'effectiveRequest.ReviewNote')
    ):
        segment = segment.replace(old, new)
    segment = segment.replace('ValidatePlan(request)', 'ValidatePlan(effectiveRequest)')
    return segment


forge = transform_method(forge, 'UpdatePlanAsync', patch_update_plan)
forge = replace_once(
    forge,
    '''        var canEdit = access.CanManage || (access.CanEditAssignedEstimate && task.Value.ReviewerUserId == access.EffectiveUserId);
''',
    '''        var canEdit = access.CanManage
            || access.CanEditReviewPlan
            || (access.CanEditAssignedEstimate && task.Value.ReviewerUserId == access.EffectiveUserId);
''',
    'Project Forge estimate collaborator authority'
)

restricted_helpers = r'''    private sealed record ProjectForgeRestrictedTaskFields(
        decimal HourlyRate,
        decimal MaterialUnits,
        decimal MaterialUnitCost,
        decimal FixedCost,
        decimal TravelCost,
        decimal EquipmentCost,
        decimal MiscCost,
        Guid? ReviewerUserId);

    private static ProjectForgePlanSaveRequest RestrictNewCollaboratorPlan(
        ProjectForgePlanSaveRequest request)
    {
        var tasks = (request.Tasks ?? [])
            .Select(task => task with
            {
                HourlyRate = 0,
                MaterialUnits = 0,
                MaterialUnitCost = 0,
                FixedCost = 0,
                TravelCost = 0,
                EquipmentCost = 0,
                MiscCost = 0,
                ReviewerUserId = null
            })
            .ToArray();
        return request with { Tasks = tasks };
    }

    private static async Task<ProjectForgePlanSaveRequest> PreserveCollaboratorRestrictedFieldsAsync(
        NpgsqlConnection connection,
        Guid planId,
        ProjectForgePlanSaveRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT plan_task_id,wbs_code,hourly_rate,material_units,material_unit_cost,
                   fixed_cost,travel_cost,equipment_cost,miscellaneous_cost,reviewer_user_id
            FROM project_forge_plan_tasks
            WHERE plan_id=@plan_id AND canonical_task_id IS NULL AND task_status<>'cancelled';
            """;
        var byId = new Dictionary<Guid, ProjectForgeRestrictedTaskFields>();
        var byWbs = new Dictionary<string, ProjectForgeRestrictedTaskFields>(StringComparer.OrdinalIgnoreCase);
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("plan_id", planId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var restricted = new ProjectForgeRestrictedTaskFields(
                    reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
                    reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8),
                    reader.IsDBNull(9) ? null : reader.GetGuid(9));
                byId[reader.GetGuid(0)] = restricted;
                var wbs = reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(wbs)) byWbs[wbs] = restricted;
            }
        }

        var tasks = (request.Tasks ?? []).Select(task =>
        {
            ProjectForgeRestrictedTaskFields? restricted = null;
            if (task.PlanTaskId.HasValue) byId.TryGetValue(task.PlanTaskId.Value, out restricted);
            if (restricted is null && !string.IsNullOrWhiteSpace(task.Wbs)) byWbs.TryGetValue(task.Wbs.Trim(), out restricted);
            restricted ??= new ProjectForgeRestrictedTaskFields(0, 0, 0, 0, 0, 0, 0, null);
            return task with
            {
                HourlyRate = restricted.HourlyRate,
                MaterialUnits = restricted.MaterialUnits,
                MaterialUnitCost = restricted.MaterialUnitCost,
                FixedCost = restricted.FixedCost,
                TravelCost = restricted.TravelCost,
                EquipmentCost = restricted.EquipmentCost,
                MiscCost = restricted.MiscCost,
                ReviewerUserId = restricted.ReviewerUserId
            };
        }).ToArray();
        return request with { Tasks = tasks };
    }

'''
forge = replace_once(
    forge,
    '    private static IResult? CandidateAiDraftMutationBlocked()\n',
    restricted_helpers + '    private static IResult? CandidateAiDraftMutationBlocked()\n',
    'Project Forge restricted-field preservation helpers'
)
write(forge_path, forge)


# ---------------------------------------------------------------------------
# Focused validator: reject UI/runtime regressions and prove deployment wiring.
# ---------------------------------------------------------------------------
validator_path = 'tests/validate-project-planning-collaboration-access.mjs'
validator = read(validator_path)
validator = replace_once(
    validator,
    "const forgeUi = read('src/frontend/project-time-web/src/ProjectForgeCenter.jsx');\n",
    """const forgeUi = read('src/frontend/project-time-web/src/ProjectForgeCenter.jsx');
const flowhivePanels = read('src/frontend/project-time-web/src/ProjectFlowHiveEnterprisePanels.jsx');
const deployment = read('.github/workflows/projectpulse-deploy-test.yml');
const migrationRunner = read('scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh');
""",
    'focused validator deployment sources'
)
validator = replace_once(
    validator,
    "requireText(forgeInteractive, 'state.RecordSource == \"review_plan\" && access.CanEditReviewPlan', 'Project Forge review-plan task edit');\n",
    """requireText(forgeInteractive, 'state.RecordSource == \"review_plan\" && access.CanEditReviewPlan', 'Project Forge review-plan task edit');
requireText(forge, 'PreserveCollaboratorRestrictedFieldsAsync', 'Project Forge restricted financial preservation');
requireText(forge, 'RestrictNewCollaboratorPlan', 'Project Forge new collaborator plan restriction');
requireText(forge, 'access.CanEditReviewPlan', 'Project Forge collaborator estimate authority');
""",
    'focused validator Forge hardening'
)
validator = replace_once(
    validator,
    "requireText(forgeUi, 'canEditWorkspace', 'Project Forge workspace capability');\n",
    """requireText(forgeUi, 'canEditWorkspace', 'Project Forge workspace capability');
requireText(resolver, 'project_team_coordinator_business_scope', 'FlowHive established PTC read scope');
requireText(resolver, 'executive_read_scope', 'FlowHive established Executive read scope');
requireText(resolver, 'manager_team_scope', 'FlowHive established Manager read scope');
requireText(flowhivePanels, 'export function FlowHiveSaveBar({ dirty, workingCopy, canManage', 'FlowHive save-bar capability contract');
requireText(flowhivePanels, 'export function FlowHiveStatusRaidPanel({ enterprise, draftPlan, statusDraft, setStatusDraft, newRaid, setNewRaid, canEditPlanner, canAdministerPlanner', 'FlowHive RAID/status split capability contract');
if (flowhivePanels.includes('export function FlowHiveFinancialsPanel') && flowhivePanels.slice(
  flowhivePanels.indexOf('export function FlowHiveFinancialsPanel'),
  flowhivePanels.indexOf('export function FlowHiveStatusRaidPanel')
).includes('canAdministerPlanner')) {
  failures.push('FlowHive financial panel references a capability not present in its props');
}
requireText(deployment, '095_project_planning_collaboration_access', 'protected-Test Migration 095 wiring');
requireText(deployment, 'MIGRATION_095=APPLIED_AND_VERIFIED', 'protected-Test Migration 095 verification');
requireText(migrationRunner, '086-088-093-095', 'private-network Migration 095 ownership tag');
""",
    'focused validator runtime and deployment contracts'
)
write(validator_path, validator)


# ---------------------------------------------------------------------------
# Permanent migration/source regression shell.
# ---------------------------------------------------------------------------
migration_test = r'''#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/095_project_planning_collaboration_access.sql"
ROLLBACK="$ROOT/database/rollback/095_project_planning_collaboration_access_rollback.sql"
RESOLVER="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs"
FLOWHIVE="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs"
FORGE="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs"
DEPLOYMENT="$ROOT/.github/workflows/projectpulse-deploy-test.yml"
RUNNER="$ROOT/scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh"

for file in "$MIGRATION" "$ROLLBACK" "$RESOLVER" "$FLOWHIVE" "$FORGE" "$DEPLOYMENT" "$RUNNER"; do
  test -f "$file" || { echo "Missing project planning collaboration artifact: $file" >&2; exit 1; }
done

for marker in \
  project_planning_collaborators \
  project_planning_collaboration_audit_events \
  VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066 \
  EDIT_FLOWHIVE_PLANNER_066 \
  VIEW_ASSOCIATED_PROJECT_FORGE_033 \
  EDIT_PROJECT_FORGE_REVIEW_PLAN_033 \
  095_project_planning_collaboration_access; do
  grep -Fq "$marker" "$MIGRATION"
done

grep -Fq 'PROJECT_PLANNING_COLLABORATION_V1' "$RESOLVER"
grep -Fq 'associated_account_executive' "$RESOLVER"
grep -Fq 'associated_solution_architect' "$RESOLVER"
grep -Fq 'assigned_engineering_team_scope' "$RESOLVER"
grep -Fq 'Project Stakeholder — Read Only' "$RESOLVER"
! grep -Fq 'scoped_role_policy_modules' "$RESOLVER"
grep -Fq 'PreserveCollaboratorRestrictedFieldsAsync' "$FORGE"
grep -Fq 'RestrictNewCollaboratorPlan' "$FORGE"
grep -Fq 'FlowHiveAccessRequirement.EditPlanner' "$FLOWHIVE"
grep -Fq 'FlowHiveAccessRequirement.CustomerShare' "$FLOWHIVE"
grep -Fq '095_project_planning_collaboration_access.sql' "$DEPLOYMENT"
grep -Fq 'MIGRATION_095=APPLIED_AND_VERIFIED' "$DEPLOYMENT"
grep -Fq '086-088-093-095' "$RUNNER"
grep -Fq 'Rollback 095 refused: project planning collaborator assignments exist.' "$ROLLBACK"
grep -Fq 'immutable project planning collaboration audit evidence exists' "$ROLLBACK"

echo 'PROJECT_PLANNING_COLLABORATION_MIGRATION_095=PASS'
'''
Path('tests/test-project-planning-collaboration-migration-095.sh').write_text(migration_test)
Path('tests/test-project-planning-collaboration-migration-095.sh').chmod(0o755)


# ---------------------------------------------------------------------------
# Update the private-network runner tag and its permanent system-wide validator.
# ---------------------------------------------------------------------------
runner_path = 'scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh'
runner = read(runner_path)
runner = replace_count(
    runner,
    '086-088-093',
    '086-088-093-095',
    2,
    'private-network migration ownership tag'
)
write(runner_path, runner)

systemwide_validator_path = 'tests/validate-systemwide-enterprise-reliability.mjs'
systemwide = read(systemwide_validator_path)
systemwide = replace_once(
    systemwide,
    '''const assignmentMigration = read('database/migrations/093_assigned_work_canonical_visibility_repair.sql');
''',
    '''const assignmentMigration = read('database/migrations/093_assigned_work_canonical_visibility_repair.sql');
const collaborationMigration = read('database/migrations/095_project_planning_collaboration_access.sql');
''',
    'system-wide collaboration migration source'
)
systemwide = replace_once(
    systemwide,
    "requireText(assignmentMigration, 'INSERT INTO project_assignments', 'Migration 093 canonical assignment backfill');\n",
    """requireText(assignmentMigration, 'INSERT INTO project_assignments', 'Migration 093 canonical assignment backfill');
requireText(collaborationMigration, '095_project_planning_collaboration_access', 'Migration 095 registration');
requireText(collaborationMigration, 'project_planning_collaborators', 'Migration 095 collaboration scope');
""",
    'system-wide Migration 095 contract'
)
systemwide = systemwide.replace('projectpulse-migration"] == "086-088-093"', 'projectpulse-migration"] == "086-088-093-095"')
systemwide = systemwide.replace("'Migrations 086, 088, and 093'", "'Migrations 086, 088, 093, and 095'")
systemwide = replace_once(
    systemwide,
    "  'migration093:\"applied_and_verified\"',\n",
    "  'migration093:\"applied_and_verified\"',\n  'MIGRATION_095=APPLIED_AND_VERIFIED',\n",
    'system-wide Migration 095 deployment marker'
)
write(systemwide_validator_path, systemwide)


Path('scripts/release-test/harden-pr734-source.py').unlink(missing_ok=True)

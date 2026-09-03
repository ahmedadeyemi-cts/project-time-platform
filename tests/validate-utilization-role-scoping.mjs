import assert from 'node:assert/strict';
import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const teamPanel = read('src/frontend/project-time-web/src/EngineeringTeamLeadUtilizationPanel.jsx');
const drawer = read('src/frontend/project-time-web/src/GlobalViewAsDrawer.jsx');
const app = read('src/frontend/project-time-web/src/App.jsx');
const protectedTestUat = read('scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh');
const scopedRules = read('src/backend/ProjectTime.Api/Modules/ScopedRolePolicyRules.cs');
const scopedEvaluator = read('src/backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs');

assert.match(program, /calculationStatus = "calculated"/);
assert.doesNotMatch(program, /calculationStatus = "placeholder"/);
assert.match(program, /WHERE ts\.user_id = @user_id/);
assert.match(program, /timesheet_status NOT IN \('manager_declined', 'rejected', 'voided'\)/);
assert.match(program, /roles\.Contains\("EXECUTIVE"\)/);
assert.match(program, /roles\.Contains\("ENGINEERING_DIRECTOR"\)/);
assert.match(program, /var canUseOwnScope = isEngineer \|\| isEngineeringTeamLead;/);
assert.match(program, /if \(canViewAll \|\| canUseTeamScope\)/);
assert.match(program, /SELECT @user_id/);
assert.match(program, /'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD'/);
assert.match(program, /else\s*\{\s*effectiveEngineerUserId = sessionUserId\.Value;\s*scope = "own_engineer_scope";\s*\}/);
assert.match(program, /Selected engineer is not available within your utilization scope\./);
const teamScopedQueryStart = program.indexOf('else if (canUseTeamScope)');
const ownScopedQueryStart = program.indexOf('else if (canUseOwnScope)', teamScopedQueryStart);
assert.ok(teamScopedQueryStart >= 0 && ownScopedQueryStart > teamScopedQueryStart);
const teamScopedQuery = program.slice(teamScopedQueryStart, ownScopedQueryStart);
assert.match(teamScopedQuery, /FROM user_admin_manager_team_assignments assignment/);
assert.match(teamScopedQuery, /assignment\.manager_user_id = @user_id/);
assert.match(teamScopedQuery, /assignment\.is_active = TRUE/);
assert.match(teamScopedQuery, /lower\(COALESCE\(u\.manager_email, ''\)\) = lower\(ap\.email\)/);
assert.match(teamScopedQuery, /lower\(COALESCE\(u\.team_name, ''\)\) = lower\(assignment\.team_name\)/);
assert.match(teamScopedQuery, /WHERE @is_manager = FALSE[\s\S]*?tm\.user_id = @user_id/);
assert.match(teamScopedQuery, /AND lower\(COALESCE\(u\.team_name, ''\)\) = lower\(ap\.team_name\)/);
assert.doesNotMatch(teamScopedQuery, /lower\(COALESCE\(u\.department_name, ''\)\) = lower/);
assert.doesNotMatch(teamScopedQuery, /lower\(COALESCE\(u\.department, ''\)\) = lower/);
assert.match(teamScopedQuery, /teamCommand\.Parameters\.AddWithValue\("is_manager", isManager\)/);
assert.match(scopedRules, /IsCompositeManagerUtilizationReadCompatibilityDeny/);
assert.match(scopedRules, /!hasManagerUtilizationGrant/);
assert.match(scopedRules, /string\.Equals\(moduleCode, "003"/);
assert.match(scopedRules, /string\.Equals\(actionCode, "UTILIZATION_VIEW"/);
assert.match(scopedRules, /string\.Equals\(denyActionCode, "MODULE_ACCESS"/);
assert.match(scopedRules, /CanonicalRole\(deniedRoleCode\)[\s\S]*?"PROJECT_MANAGEMENT"/);
assert.match(scopedRules, /Contains\("MANAGER", StringComparer\.OrdinalIgnoreCase\)/);
assert.match(scopedEvaluator, /var hasManagerUtilizationGrant = grants\.Any/);
assert.match(scopedEvaluator, /IsCompositeManagerUtilizationReadCompatibilityDeny\([\s\S]*?actor\.RoleCodes,[\s\S]*?hasManagerUtilizationGrant/);

for (const marker of [
  'KEVIN_USER_ID=',
  '.userId // empty',
  "OTHER_ENGINEER_ID='11111111-1111-1111-1111-111111111111'",
  'kevin-cross-engineer-normalized.json',
  '[[ \"$OTHER_ENGINEER_STATUS\" == 200 ]]',
  '.scope == \"own_engineer_scope\"',
  '.selectedEngineerUserId == $kevinUserId',
  '.selectedEngineerUserId != $requestedUserId',
  '.access.canSelectEngineer == false',
  '.access.canUseTeamScope == false',
  '.access.canUseOwnScope == true',
  '([.members[]?.userId] == [$kevinUserId])',
  'crossEngineerRequestOutcome:\"normalized_to_authenticated_engineer\"',
  '[[ \"$VIEW_AS_STATUS\" == 403 ]]',
  "MANAGER_EMAIL='demo.manager@ussignal.local'",
  'manager-utilization-security-context.json',
  '.scope == \"engineering_team_scope\"',
  '.access.canViewAll == false',
  '.access.canUseTeamScope == true',
  '.access.canSelectEngineer == true',
  'resolve_live_engineer_identity()',
  'candidate_cleanup()',
  'candidate_session=\'\'',
  'previous_err_trap=',
  "trap 'candidate_cleanup' ERR",
  'restore_candidate_err_trap()',
  '$BASE/api/auth/session/logout',
  'jason.mosier@ussignal.local|Jason Mosier',
  'jeremy.holt@ussignal.local|Jeremy Holt',
  'demo.engineer@ussignal.local|Demo Engineer',
  'manager-outsider-candidate-$slug-security-context.json',
  'RESOLVED_ENGINEER_USER_ID=',
  'MANAGER_OUTSIDER_EMAIL=',
  'managerCrossTeamIdentitySource:\"live_authenticated_security_context\"',
  '[[ \"$MANAGER_OUTSIDER_STATUS\" == 403 ]]',
  'Selected engineer is not available within your utilization scope.',
  'managerRoleScope:\"assigned_team_only\"',
  'managerCrossTeamOutcome:\"denied_outside_assigned_team\"'
]) {
  assert.ok(protectedTestUat.includes(marker), `Protected-Test utilization UAT is missing: ${marker}`);
}
assert.ok(
  !protectedTestUat.includes("JASON_USER_ID='73e58088-c70a-4a4f-a856-a38c0e43b089'"),
  'Protected-Test utilization UAT must not use a static Jason UUID for a cross-team denial proof.'
);
assert.ok(
  !protectedTestUat.includes('[[ \"$OTHER_ENGINEER_STATUS\" == 403 ]]'),
  'Protected-Test utilization UAT must not require a 403 where the backend securely normalizes Engineer scope.'
);
const candidateSessionExtraction = protectedTestUat.indexOf('candidate_session="$(jq -r');
const candidateErrTrap = protectedTestUat.indexOf("trap 'candidate_cleanup' ERR", candidateSessionExtraction);
const candidateMask = protectedTestUat.indexOf('echo "::add-mask::$candidate_session"', candidateSessionExtraction);
assert.ok(
  candidateSessionExtraction >= 0 && candidateErrTrap > candidateSessionExtraction && candidateErrTrap < candidateMask,
  'Candidate cleanup must be armed immediately after session extraction and before the credential mask is emitted.'
);
assert.match(teamPanel, /canLoadEngineeringTeamSummary/);
assert.match(teamPanel, /fetchJson\('\/api\/security\/context'\)/);
assert.match(teamPanel, /ENGINEERING_TEAM_SCOPE_ROLE_CODES/);
assert.match(teamPanel, /ENGINEERING_ORGANIZATION_SCOPE_ROLE_CODES/);

assert.match(drawer, /securityContextAllowsViewAs/);
assert.match(drawer, /contextResponse = await requestFetch\('\/api\/security\/context'/);
assert.match(drawer, /const timer = window\.setTimeout\(loadUsers, 250\);/);
assert.doesNotMatch(drawer, /\[250, 1200, 3000\]/);
assert.doesNotMatch(drawer, /addEventListener\('hashchange', loadUsers\)/);

assert.match(app, /Legacy DOM View-As preview disabled/);
assert.doesNotMatch(app, /^installProjectPulseGlobalViewAsPreview\(\);$/m);

console.log('UTILIZATION_ROLE_SCOPING_VALIDATION=PASS engineer=self lead=self+team managerDirector=assigned-team-and-direct-reports executive=all departmentExpansion=blocked viewAs=capability-gated liveManagerBoundary=required liveOutsiderIdentity=required failCleanCandidateSessions=required implicitErrorCleanup=required');

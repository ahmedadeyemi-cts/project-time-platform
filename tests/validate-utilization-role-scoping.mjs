import assert from 'node:assert/strict';
import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const teamPanel = read('src/frontend/project-time-web/src/EngineeringTeamLeadUtilizationPanel.jsx');
const drawer = read('src/frontend/project-time-web/src/GlobalViewAsDrawer.jsx');
const app = read('src/frontend/project-time-web/src/App.jsx');
const protectedTestUat = read('scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh');

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
  '[[ \"$VIEW_AS_STATUS\" == 403 ]]'
]) {
  assert.ok(protectedTestUat.includes(marker), `Protected-Test utilization UAT is missing: ${marker}`);
}
assert.ok(
  !protectedTestUat.includes('[[ \"$OTHER_ENGINEER_STATUS\" == 403 ]]'),
  'Protected-Test utilization UAT must not require a 403 where the backend securely normalizes Engineer scope.'
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

console.log('UTILIZATION_ROLE_SCOPING_VALIDATION=PASS engineer=self lead=self+team managerDirector=team executive=all viewAs=capability-gated');

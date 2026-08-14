import assert from 'node:assert/strict';
import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const teamPanel = read('src/frontend/project-time-web/src/EngineeringTeamLeadUtilizationPanel.jsx');
const drawer = read('src/frontend/project-time-web/src/GlobalViewAsDrawer.jsx');
const app = read('src/frontend/project-time-web/src/App.jsx');

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

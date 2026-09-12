import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ROLE_JOURNEYS, JOURNEY_ROUTE, LIFECYCLE, canonicalRole, roleCatalog, matchesJourney, userStory, validateJourneys } from '../src/role-journeys/role-journeys.js';
import roleJourneysPlugin, { APP_ANCHORS, transformJourneyApp, transformJourneyRegistry, verifyJourneySources } from '../scripts/role-journeys-vite-plugin.mjs';

const webRoot = fileURLToPath(new URL('../', import.meta.url));
const fixture = `${APP_ANCHORS.guide}\n${APP_ANCHORS.route}\nconst page = <>${APP_ANCHORS.navigation}</nav></>;`;
const registry = `const ROUTE_ALIASES = Object.freeze({\n  legacy: 'retained'\n});`;

test('all 15 playbooks and 60 steps satisfy the content contract', () => {
  assert.equal(Object.keys(ROLE_JOURNEYS).length, 15);
  assert.equal(Object.values(ROLE_JOURNEYS).flatMap((role) => role.steps).length, 60);
  assert.deepEqual(validateJourneys(), []);
});
test('every lifecycle stage is present in the map', () => {
  for (const phase of ['plan', 'design', 'implement', 'validate', 'release']) assert(LIFECYCLE.some(([id]) => id === phase));
});
test('canonical role aliases remain distinct from privilege boundaries', () => {
  assert.equal(canonicalRole('engineer'), 'ENGINEERING');
  assert.equal(canonicalRole('project-manager'), 'PROJECT_MANAGEMENT');
  assert.equal(canonicalRole('PTC'), 'PROJECT_TEAM_COORDINATOR');
  assert.equal(canonicalRole('PROJECT_COORDINATOR'), 'PROJECT_COORDINATOR');
  assert.equal(canonicalRole('ADMINISTRATOR'), 'ADMINISTRATOR');
  assert.notEqual(canonicalRole('ADMINISTRATOR'), canonicalRole('SUPER_ADMINISTRATOR'));
});
test('multiple assigned roles sort first without inventing grants', () => {
  const roles = roleCatalog({}, ['ENGINEER', 'SA']);
  assert.equal(roles.filter((role) => role.assigned).length, 2);
  assert(roles.slice(0, 2).every((role) => role.assigned));
  assert(roles.every((role) => !('permissions' in role)));
});
test('new assigned and catalog roles receive explicit guidance gaps', () => {
  const roles = roleCatalog({ AUDITOR: { title: 'Auditor' } }, ['NEW_ROLE']);
  for (const code of ['NEW_ROLE', 'AUDITOR']) {
    const role = roles.find((item) => item.code === code);
    assert(role);
    assert.deepEqual(role.steps, []);
    assert.match(userStory(role), /not yet available/);
  }
});
test('known roles use the canonical purpose but retain specific boundaries', () => {
  const roles = roleCatalog({ PROJECT_TEAM_COORDINATOR: { purpose: 'Time stewardship', boundary: 'Legacy general statement' } });
  const role = roles.find((item) => item.code === 'PROJECT_TEAM_COORDINATOR');
  assert.equal(role.purpose, 'Time stewardship');
  assert.notEqual(role.boundary, 'Legacy general statement');
});
test('search handles empty query, case, multiple words and no match', () => {
  const role = roleCatalog({}, ['ENGINEERING'])[0];
  assert(matchesJourney(role, ''));
  assert(matchesJourney(role, 'ENGINEER time'));
  assert.equal(matchesJourney(role, 'not-a-real-task-xyz'), false);
});
test('role and task user stories are complete and do not contain undefined', () => {
  for (const role of roleCatalog()) {
    for (const story of [userStory(role), ...role.steps.map((step) => userStory(role, step))]) {
      assert.match(story, /I want to .+ so that .+/);
      assert(!story.includes('undefined'));
    }
  }
});
test('unknown and nonregistered module routes fail content verification', () => {
  assert(validateJourneys(new Set(['user-guide'])).length > 0);
  const known = new Set(Object.values(ROLE_JOURNEYS).flatMap((role) => role.steps.map((step) => step.route)));
  assert.deepEqual(validateJourneys(known), []);
});
test('dedicated page shares Module 999; authenticated navigation is React-owned', () => {
  const result = transformJourneyApp(fixture);
  assert(result.includes("return route === 'my-role-in-pulse' ? 'user-guide' : route;"));
  assert(result.includes('data-module-number="999"'));
  assert(result.includes('RoleJourneyGuideRouter.jsx'));
  assert(!result.includes('createRoot'));
  assert(!result.includes('document.createElement'));
});
test('My Role refreshes authorized workspace links after the RBAC bridge publishes navigation', () => {
  const contextSource = fs.readFileSync(path.join(webRoot, 'src/role-journeys/use-role-journey-context.js'), 'utf8');
  assert.match(contextSource, /projectpulse:permission-navigation-updated/);
  assert.match(contextSource, /ROLE_JOURNEY_AUTHORITY_EVENTS/);
});
test('all other hash routes preserve the original result', () => {
  const parser = transformJourneyApp(fixture).match(/function getRouteFromHash\(\) \{[\s\S]*?\n\}/)[0];
  const run = (hash) => Function('window', `${parser}; return getRouteFromHash();`)({ location: { hash } });
  assert.equal(run('#my-role-in-pulse'), 'user-guide');
  for (const route of ['user-guide', 'timesheet', 'project-flowhive', 'sow-generator', 'role-admin']) assert.equal(run(`#${route}`), route);
  assert.equal(run(''), 'dashboard');
});
test('application transform is idempotent and rejects incomplete installation', () => {
  const result = transformJourneyApp(fixture);
  assert.equal(transformJourneyApp(result), result);
  assert.throws(() => transformJourneyApp('/* MY_ROLE_IN_PULSE_ROUTE_V1 */'), /Partial/);
});
test('missing and duplicate application anchors fail closed', () => {
  for (const anchor of Object.values(APP_ANCHORS)) {
    assert.throws(() => transformJourneyApp(fixture.replace(anchor, '')), /Expected one/);
    assert.throws(() => transformJourneyApp(`${fixture}\n${anchor}`), /Expected one/);
  }
});
test('registry alias is additive, unique and preserves legacy aliases', () => {
  const result = transformJourneyRegistry(registry);
  assert(result.includes(`'${JOURNEY_ROUTE}': 'user-guide'`));
  assert(result.includes("legacy: 'retained'"));
  assert.equal(transformJourneyRegistry(result), result);
  assert.throws(() => transformJourneyRegistry(''), /Expected one/);
  assert.throws(() => transformJourneyRegistry(`${result}\n  '${JOURNEY_ROUTE}': 'user-guide',`), /Duplicate/);
});
test('plugin modifies only generated App and the existing module registry', () => {
  const plugin = roleJourneysPlugin();
  assert.equal(plugin.enforce, 'pre');
  for (const name of ['App.jsx', 'main.jsx', 'SystemUserGuide.Module001.g.jsx', 'ProjectFlowHiveCenter.jsx', 'Module025SowGsdModule.cs']) {
    assert.equal(plugin.transform('untouched', `/src/${name}`), null);
  }
  assert(plugin.transform(fixture, '/src/App.Module001.g.jsx?test').code.includes('RoleJourneyGuideRouter'));
});
test('build source validation rejects broken modules and accepts a complete fixture', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pulse-role-journeys-'));
  try {
    fs.mkdirSync(path.join(dir, 'src'));
    const routes = new Set(['user-guide', ...Object.values(ROLE_JOURNEYS).flatMap((role) => role.steps.map((step) => step.route))]);
    const entries = [...routes].map((route) => `Object.freeze({ route: '${route}' })`).join(',\n');
    fs.writeFileSync(path.join(dir, 'src/module-availability-registry.js'), `const modules = [${entries}];\n${registry}`);
    fs.writeFileSync(path.join(dir, 'src/App.jsx'), fixture.replace(APP_ANCHORS.guide, "import SystemUserGuide from './SystemUserGuide.jsx';"));
    assert.doesNotThrow(() => verifyJourneySources(dir));
    fs.writeFileSync(path.join(dir, 'src/module-availability-registry.js'), registry);
    assert.throws(() => verifyJourneySources(dir), /unregistered route/);
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});
test('UI source has keyboard steps, explicit learning state and scoped action links', () => {
  const page = fs.readFileSync(path.join(webRoot, 'src/role-journeys/MyRoleInPulse.jsx'), 'utf8');
  for (const marker of ['aria-current=', 'aria-controls="rj-step-details"', 'type="search"', 'Who receives it next', 'Ready when', 'Learning only', 'context.accessReady', 'context.modules.find', 'Fictional example', 'No matching roles']) assert(page.includes(marker), marker);
  assert(!page.includes('dangerouslySetInnerHTML'));
  assert(!page.includes('localStorage.setItem'));
  assert(!page.includes('fetch('));
});
test('responsive styles support keyboard focus, reduced motion and forced colors', () => {
  const css = fs.readFileSync(path.join(webRoot, 'src/role-journeys/role-journeys.css'), 'utf8');
  for (const marker of [':focus-visible', 'max-width: 720px', 'prefers-reduced-motion', 'forced-colors', 'aria-current']) assert(css.includes(marker));
});

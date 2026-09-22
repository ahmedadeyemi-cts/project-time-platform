import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { ROLE_GUIDANCE } from '../src/role-permission-model.js';
import { ROLE_JOURNEYS, LIFECYCLE, matchesJourney } from '../src/role-journeys/role-journeys.js';
import { assignedRolePlaybooks, journeyRoleCode } from '../src/role-journeys/role-journey-playbooks.js';
const context = (roleCodes, patch = {}) => ({ roleCodes, accessReady: true, modules: [], ...patch });
const own = (codes, patch = {}) => assignedRolePlaybooks(ROLE_GUIDANCE, context(codes, patch));
const source = (name) => fs.readFileSync(new URL(`../src/role-journeys/${name}`, import.meta.url), 'utf8');

test('every canonical role and baseline legacy role gets only its own complete story', () => {
  for (const code of new Set([...Object.keys(ROLE_GUIDANCE), ...Object.keys(ROLE_JOURNEYS)])) {
    const roles = own([code]);
    assert.equal(roles.length, 1, code); assert.equal(roles[0].code, code); assert(roles[0].steps.length >= 4, code);
  }
});
test('every role pair is isolated, including privileged and nonprivileged roles', () => {
  const codes = Object.keys(ROLE_JOURNEYS);
  for (const code of codes) {
    const roles = own([code]);
    for (const other of codes.filter((candidate) => candidate !== code)) assert(!roles.some((role) => role.code === other), `${code} leaked ${other}`);
  }
});
test('Super Administrator sees its own story rather than an unrestricted role selector', () => {
  assert.deepEqual(own(['SUPER_ADMINISTRATOR']).map((role) => role.code), ['SUPER_ADMINISTRATOR']);
});
test('multiple effective assignments stay separate and duplicate aliases are removed', () => {
  const result = own(['ENGINEERING', 'ENGINEER', 'PROJECT_MANAGEMENT']);
  assert.deepEqual(new Set(result.map((role) => role.code)), new Set(['ENGINEERING', 'PROJECT_MANAGEMENT'])); assert.equal(result.length, 2);
});
test('unverified and unassigned users receive no catalog, even with privileged guidance available', () => {
  assert.deepEqual(own([]), []); assert.deepEqual(own(['SUPER_ADMINISTRATOR'], { accessReady: false }), []);
});
test('unknown assigned roles stay visible as their own explicit guidance gap', () => {
  const result = own(['NEW_REVIEWER']); assert.equal(result.length, 1); assert.equal(result[0].code, 'NEW_REVIEWER'); assert.deepEqual(result[0].steps, []);
});
test('search cannot reveal another role because its input is already scoped', () => {
  const result = own(['ENGINEERING']).filter((role) => matchesJourney(role, 'SUPER_ADMINISTRATOR'));
  assert.deepEqual(result, []);
});
test('server-recognized SA manager variants get the scoped manager workflow, not author authority', () => {
  for (const code of ['SOLUTION_ARCHITECT_MANAGER', 'SOLUTIONS_ARCHITECT_MANAGER']) {
    const result = own([code]); assert.equal(result.length, 1); assert.equal(result[0].code, 'SOLUTION_ARCHITECT_MANAGER');
    assert.match(result[0].boundary, /read-only/); assert.equal(result[0].steps.length, 4);
  }
});
test('documented role spellings normalize without conflating coordinators or admin levels', () => {
  assert.equal(journeyRoleCode('SOLUTIONS_ARCHITECT'), 'SOLUTION_ARCHITECT');
  assert.equal(journeyRoleCode('INSIDE_SALES_REPRESENTATIVE'), 'INSIDE_SALES');
  assert.notEqual(journeyRoleCode('PROJECT_COORDINATOR'), journeyRoleCode('PTC'));
  assert.notEqual(journeyRoleCode('ADMINISTRATOR'), journeyRoleCode('SUPER_ADMINISTRATOR'));
});
test('all expanded stories have unique step IDs, complete handoffs and registered routes', () => {
  const registry = fs.readFileSync(new URL('../src/module-availability-registry.js', import.meta.url), 'utf8');
  const routes = new Set([...registry.matchAll(/\broute:\s*'([^']+)'/g)].map((match) => match[1]));
  const phases = new Set(LIFECYCLE.map(([id]) => id));
  for (const role of own([...Object.keys(ROLE_JOURNEYS), 'SOLUTION_ARCHITECT_MANAGER'])) {
    assert.equal(new Set(role.steps.map((step) => step.id)).size, role.steps.length);
    for (const step of role.steps) {
      for (const key of ['id', 'title', 'stage', 'route', 'input', 'action', 'output', 'acceptance', 'nextOwner', 'exception']) assert.equal(typeof step[key], 'string');
      assert(routes.has(step.route), step.route); assert(phases.has(step.stage), step.stage);
    }
  }
});
test('expanded content does not mutate baseline playbooks', () => {
  const before = JSON.stringify(ROLE_JOURNEYS); own(Object.keys(ROLE_JOURNEYS)); assert.equal(JSON.stringify(ROLE_JOURNEYS), before);
});
test('SA, PM, Engineer and PTC retain their concrete workflow boundaries', () => {
  assert(own(['SOLUTION_ARCHITECT'])[0].steps.some((step) => step.action.includes('Module 064')));
  assert(own(['PROJECT_MANAGEMENT'])[0].steps.some((step) => step.route === 'create-work-register'));
  assert(own(['ENGINEERING'])[0].steps.some((step) => step.route === 'engineer-task-closeout'));
  assert.match(own(['PROJECT_TEAM_COORDINATOR'])[0].steps.find((step) => step.id === 'ptc-time-steward').exception, /Never submit another person/);
});
test('page implements controlled local motion, synchronous scope reset and permission-gated links', () => {
  const page = source('MyRoleInPulse.jsx');
  for (const marker of ['assignedRolePlaybooks', 'key={scopeKey}', 'Pause walkthrough', 'Pause animation', 'visibilitychange', 'prefers-reduced-motion', 'clearTimeout', 'aria-current=', 'context.modules.find', 'Learning only', 'not live project activity']) assert(page.includes(marker), marker);
  for (const prohibited of ['dangerouslySetInnerHTML', 'localStorage.setItem', 'fetch(', 'Exploring a role']) assert(!page.includes(prohibited), prohibited);
});
test('animation stylesheet supports pause, reduced motion, forced colors and narrow screens', () => {
  const css = source('role-journey-motion.css');
  for (const marker of ['animation-play-state: paused', 'prefers-reduced-motion', 'animation: none !important', 'forced-colors', 'max-width: 720px']) assert(css.includes(marker), marker);
  assert(!css.includes('https://'));
});

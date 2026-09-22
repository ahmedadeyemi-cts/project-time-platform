import test from 'node:test';
import assert from 'node:assert/strict';
import { createJourneyIdentityGuard, resolveJourneyAudience } from '../src/role-journeys/role-journey-access.js';
const ready = (patch = {}) => ({ state: 'ready', evidenceContract: 'projectpulse-rbac-v1', isViewAs: false, roleCodes: ['ENGINEERING'], journeyRoleCodes: ['ENGINEERING'], ...patch });
const modules = [{ route: 'timesheet', moduleNumber: '001', displayName: 'Timesheet' }];
const resolve = (navigation, patch = {}) => resolveJourneyAudience({ navigation, allowedModules: modules, ...patch });

test('no navigation, loading, failed and anonymous states reveal no roles or links', () => {
  for (const navigation of [undefined, ready({ state: 'loading' }), ready({ state: 'error' }), ready({ state: 'anonymous' })]) {
    const result = resolve(navigation);
    assert.deepEqual(result.roleCodes, []); assert.deepEqual(result.modules, []); assert.equal(result.accessReady, false);
  }
});
test('untrusted, stale and failed-refresh evidence fails closed', () => {
  for (const navigation of [ready({ evidenceContract: '' }), ready({ refreshFailed: true })]) assert.equal(resolve(navigation).accessReady, false);
  assert.equal(resolve(ready(), { navigationCurrent: false }).accessReady, false);
});
test('assigned roles are projected without expanding admin into every role', () => {
  assert.deepEqual(resolve(ready()).roleCodes, ['ENGINEERING']);
  assert.deepEqual(resolve(ready({ journeyRoleCodes: ['SUPER_ADMINISTRATOR'] })).roleCodes, ['SUPER_ADMINISTRATOR']);
});
test('server-derived PM educational assignment is preserved without becoming permission authority', () => {
  const result = resolve(ready({ journeyRoleCodes: ['ENGINEERING', 'PROJECT_MANAGEMENT'] }));
  assert.deepEqual(result.roleCodes, ['ENGINEERING', 'PROJECT_MANAGEMENT']);
  assert.deepEqual(result.modules.map((module) => module.route), ['timesheet']);
});
test('explicit empty assignment does not fall back to privileged permission roles', () => {
  assert.deepEqual(resolve(ready({ roleCodes: ['SUPER_ADMINISTRATOR'], journeyRoleCodes: [] })).roleCodes, []);
});
test('legacy navigation can use its own server roles only when the journey field is absent', () => {
  const navigation = ready(); delete navigation.journeyRoleCodes;
  assert.deepEqual(resolve(navigation).roleCodes, ['ENGINEERING']);
});
test('malformed assignments do not silently inherit other roles', () => {
  assert.equal(resolve(ready({ journeyRoleCodes: null })).accessReady, false);
  assert.deepEqual(resolve(ready({ journeyRoleCodes: [' engineer ', 'ENGINEER', null, {}, '<script>', 'NEW_ROLE'] })).roleCodes, ['ENGINEER', 'NEW_ROLE']);
});
test('View-As cannot consume actual-session navigation or the reverse', () => {
  assert.equal(resolve(ready(), { viewAsActive: true }).accessReady, false);
  assert.equal(resolve(ready({ isViewAs: true })).accessReady, false);
  assert.deepEqual(resolve(ready({ isViewAs: true }), { viewAsActive: true }).roleCodes, ['ENGINEERING']);
});
test('denied and retired links remain absent even if a shared list contains them', () => {
  const result = resolve(ready({ deniedModuleNumbers: ['001'], retiredModuleNumbers: ['019'] }), { allowedModules: [...modules, { route: 'project-workspace', moduleNumber: '019' }] });
  assert.deepEqual(result.modules, []);
});
test('unknown URI schemes, duplicate routes and malformed modules are discarded', () => {
  const result = resolve(ready(), { allowedModules: [...modules, ...modules, null, { route: 'javascript:alert(1)' }] });
  assert.equal(result.modules.length, 1);
  assert.equal(resolve(ready(), { allowedModules: null }).accessReady, false);
});
test('new session or View-As identity with the same navigation is blocked until republished', () => {
  const guard = createJourneyIdentityGuard(); const old = ready();
  assert.equal(guard.observe(old, 'user-a').current, true);
  assert.deepEqual(guard.observe(old, 'user-b'), { current: false, revision: 1 });
  assert.equal(guard.observe(old, 'user-b').current, false);
  assert.equal(guard.observe(ready(), 'user-b').current, true);
});
test('switching between two View-As users with identical role sets still invalidates stale links', () => {
  const guard = createJourneyIdentityGuard(); const old = ready({ isViewAs: true });
  guard.observe(old, 'view-as-engineer-a');
  assert.equal(guard.observe(old, 'view-as-engineer-b').current, false);
  assert.equal(guard.observe(ready({ isViewAs: true }), 'view-as-engineer-b').current, true);
});
test('same-identity refresh preserves verification; identity material never leaves the guard', () => {
  const guard = createJourneyIdentityGuard(); const navigation = ready();
  guard.observe(navigation, 'private-session-marker');
  assert.deepEqual(guard.observe(navigation, 'private-session-marker'), { current: true, revision: 0 });
  assert(!JSON.stringify(guard.observe(ready(), 'another-private-marker')).includes('private'));
});
test('same-identity in-flight refresh remains readable but a failed refresh hides stories', () => {
  assert.equal(resolve(ready({ refreshing: true })).accessReady, true);
  assert.equal(resolve(ready({ refreshing: false, refreshFailed: true })).accessReady, false);
});

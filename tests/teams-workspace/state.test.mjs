import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import {
  configurationChanged, deliveryPresentation, localTimestamp, recentDeliveries,
  validRecipient, validTeamsAppId, validWorkflowUrl, workspaceAccess,
} from '../../src/frontend/project-time-web/src/microsoft-teams-notification-state.mjs';

const appId = '11111111-2222-4333-8444-555555555555';
const workflowUrl = 'https://tenant.environment.api.powerplatform.com/powerautomate/automations/direct/workflows/example/triggers/manual/paths/invoke';
const saved = { environment: 'test', enabled: true, deliveryMode: 'power_automate', teamsAppId: appId, workflowTriggerUrl: workflowUrl, workflowAudience: 'https://service.flow.microsoft.com/', revision: 6 };
const state = { configuration: saved, readOnly: false };
const access = (overrides = {}, draft = { ...saved }, environment = 'test', busy = false, fresh = true) =>
  workspaceAccess({ ...state, ...overrides }, draft, environment, busy, fresh);

test('validates app GUIDs without accepting the zero identifier or malformed values', () => {
  assert.equal(validTeamsAppId(appId), true);
  assert.equal(validTeamsAppId(` ${appId.toUpperCase()} `), true);
  for (const value of ['', null, undefined, {}, '00000000-0000-0000-0000-000000000000', 'not-an-app', `${appId}/other`])
    assert.equal(validTeamsAppId(value), false);
});
test('validates supported Power Automate trigger URLs', () => {
  assert.equal(validWorkflowUrl(workflowUrl), true);
  assert.equal(validWorkflowUrl('https://example.logic.azure.com/workflows/x/triggers/manual/paths/invoke'), true);
  for (const value of ['', null, 'http://tenant.environment.api.powerplatform.com/x', 'https://example.com/x']) assert.equal(validWorkflowUrl(value), false);
});
test('recipient format is an input check, not identity verification', () => {
  assert.equal(validRecipient('pilot@example.invalid'), true);
  assert.equal(validRecipient(' pilot@example.invalid '), true);
  for (const value of ['', null, {}, 'not-an-address', 'a b@example.invalid', 'a@']) assert.equal(validRecipient(value), false);
});
test('unchanged saved settings cannot trigger an unnecessary save', () => {
  assert.equal(access().canSave, false);
  assert.equal(access().canTest, true);
  assert.equal(configurationChanged(saved, { ...saved, teamsAppId: appId.toUpperCase() }), false);
});
test('dirty configuration must be saved or reset before a test', () => {
  const dirty = access({}, { ...saved, workflowTriggerUrl: 'https://other.environment.api.powerplatform.com/powerautomate/automations/direct/workflows/other/triggers/manual/paths/invoke' });
  assert.equal(dirty.dirty, true); assert.equal(dirty.canSave, true); assert.equal(dirty.canTest, false);
});
test('Power Automate mode does not require a Teams app ID', () => {
  assert.equal(access({}, { ...saved, teamsAppId: '' }).canTest, true);
  assert.equal(access({}, { ...saved, workflowTriggerUrl: 'invalid' }).canSave, false);
});
test('legacy graph-app mode still requires a Teams app ID', () => {
  const legacy = { ...saved, deliveryMode: 'graph_app', workflowTriggerUrl: null };
  assert.equal(access({ configuration: legacy }, { ...legacy, teamsAppId: appId }).canTest, true);
  assert.equal(access({ configuration: legacy }, { ...legacy, teamsAppId: 'invalid' }).canTest, false);
});
test('missing, true, and malformed readOnly values fail closed', () => {
  for (const readOnly of [undefined, null, true, 'false']) {
    const result = access({ readOnly });
    assert.equal(result.canSave, false); assert.equal(result.canTest, false); assert.equal(result.readOnly, true);
  }
});
test('View-As blocks both writes and explicit test delivery', () => {
  const result = access({ readOnly: true }, { ...saved, enabled: false });
  assert.equal(result.canSave, false); assert.equal(result.canTest, false);
});
test('unknown or mismatching environments never permit mutation', () => {
  for (const environment of ['production', 'unknown', '', undefined]) {
    const result = workspaceAccess(state, { ...saved, enabled: false }, environment, false, true);
    assert.equal(result.canSave, false); assert.equal(result.canTest, false);
  }
});
test('the Production configuration can be edited but cannot send a manual test', () => {
  const production = { ...saved, environment: 'production' };
  const result = access({ configuration: production }, { ...production, enabled: false }, 'production');
  assert.equal(result.canSave, true); assert.equal(result.canTest, false);
});
test('loading, refreshing and in-flight mutation all block another mutation', () => {
  for (const busy of ['load', 'refresh', 'save', 'test', true]) {
    const result = access({}, { ...saved, enabled: false }, 'test', busy);
    assert.equal(result.canSave, false); assert.equal(result.canTest, false);
  }
});
test('stale reads block save and test', () => {
  const result = access({}, { ...saved, enabled: false }, 'test', false, false);
  assert.equal(result.canSave, false); assert.equal(result.canTest, false);
});
test('concurrent revision changes require explicit reset', () => {
  const result = access({ configuration: { ...saved, revision: 7 } });
  assert.equal(result.revisionConflict, true); assert.equal(result.canSave, false); assert.equal(result.canTest, false);
});
test('a malformed or missing server configuration cannot authorize writes', () => {
  for (const configuration of [null, undefined, {}, { ...saved, revision: -1 }, { ...saved, revision: '6' }, { ...saved, enabled: 'true' }]) {
    const result = access({ configuration });
    assert.equal(result.canSave, false); assert.equal(result.canTest, false);
  }
});
test('disabled delivery cannot be tested even with a valid recipient', () => {
  const configuration = { ...saved, enabled: false };
  assert.equal(access({ configuration }, configuration).canTest, false);
});
test('Microsoft acceptance is explicitly not a receipt or connection proof', () => {
  const result = deliveryPresentation({ status: 'sent' });
  assert.equal(result.label, 'Accepted by Microsoft');
  assert.match(result.detail, /still be checked/);
  assert.doesNotMatch(result.label, /delivered|connected|read/i);
});
test('an HTTP 400 is not presented as a confirmed installation or permission cause', () => {
  const result = deliveryPresentation({ status: 'failed', diagnosticCode: 'teams_graph_http_400' });
  assert.equal(result.label, 'Needs attention');
  assert.match(result.detail, /alone does not identify the cause/);
});
test('ambiguous and in-flight outcomes warn against automatic resend', () => {
  for (const status of ['sending', 'outcome_unknown', 'already_claimed']) {
    const result = deliveryPresentation({ status });
    assert.equal(result.tone, 'warning'); assert.match(result.detail, /Do not resend automatically/);
  }
});
test('known failure classes have bounded, useful next actions', () => {
  for (const diagnosticCode of ['teams_graph_http_403', 'teams_graph_http_404', 'teams_graph_http_429', 'teams_token_http_401', 'teams_configuration_incomplete']) {
    const result = deliveryPresentation({ status: 'failed', diagnosticCode });
    assert.equal(result.tone, 'danger'); assert.ok(result.detail.length > 40);
  }
});
test('unknown states are never displayed as verified success', () => {
  for (const row of [null, {}, { status: 'queued' }, { status: 'unexpected' }]) {
    const result = deliveryPresentation(row); assert.equal(result.label, 'Not verified');
  }
});
test('filters only the already-loaded history and does not invent all-time counts', () => {
  const rows = [{ status: 'sent' }, { status: 'failed' }, { status: 'outcome_unknown' }, null];
  assert.equal(recentDeliveries(rows).length, 3);
  assert.equal(recentDeliveries(rows, 'accepted').length, 1);
  assert.equal(recentDeliveries(rows, 'attention').length, 2);
  assert.deepEqual(recentDeliveries(null), []);
});
test('invalid dates do not crash delivery history', () => {
  for (const value of [null, '', undefined, 'not-a-date']) assert.equal(localTimestamp(value), 'Time unavailable');
  assert.notEqual(localTimestamp('2026-01-01T12:00:00Z'), 'Time unavailable');
});
test('component preserves endpoints, confirmation, revision concurrency and session headers', async () => {
  const source = await readFile(new URL('../../src/frontend/project-time-web/src/MicrosoftTeamsNotificationPanel.jsx', import.meta.url), 'utf8');
  for (const expected of ["'/api/microsoft-integration/teams'", "'SEND TEAMS TEST'", 'expectedRevision: draft.revision', "'X-ProjectPulse-Session'", 'key={environment}', 'AbortController', 'operation.current', "className=\"primary-action\"", 'aria-live="polite"']) assert.ok(source.includes(expected), expected);
  for (const forbidden of ['dangerouslySetInnerHTML', 'setInterval(', 'graph.microsoft.com', 'client_secret', 'production_governed']) assert.equal(source.includes(forbidden), false, forbidden);
});
test('all new CSS is scoped to this panel and includes mobile and dark-theme handling', async () => {
  const source = await readFile(new URL('../../src/frontend/project-time-web/src/microsoft-teams-notifications.css', import.meta.url), 'utf8');
  assert.match(source, /\[data-theme='dark'\] \.teams-notifications-workspace/);
  assert.match(source, /@media \(max-width: 680px\)/);
  assert.match(source, /:focus-visible/);
  assert.doesNotMatch(source, /^\s*(?:body|html|:root|button|input|table)\s*\{/m);
});

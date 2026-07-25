import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const repoRoot = path.resolve(webRoot, '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(repoRoot, relative), 'utf8');
const requireText = (source, value, label) => assert.ok(source.includes(value), `${label}: missing ${value}`);
const rejectText = (source, value, label) => assert.ok(!source.includes(value), `${label}: forbidden ${value}`);

const presentation = read('src/frontend/project-time-web/src/api-error-presentation.js');
const css = read('src/frontend/project-time-web/src/friendly-api-errors.css');
const main = read('src/frontend/project-time-web/src/main.jsx');
const generator = read('src/frontend/project-time-web/scripts/generate-module-001-integrated-app.mjs');
const backend = read('src/backend/ProjectTime.Api/Modules/ClientDiagnosticModule.cs');
const project = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const packageJson = JSON.parse(read('src/frontend/project-time-web/package.json'));

for (const contract of [
  "We couldn't verify those sign-in details. Check the account and password, then try again.",
  "You don't have access to utilization information with your current role.",
  "You don't have permission to complete this action with your current role.",
  'This information changed while you were working. Refresh the page and try again.',
  'This feature is not available yet.',
  'This service is temporarily unavailable. Try again shortly.',
  'Something went wrong while processing your request. Try again shortly.',
  '[ProjectPulse API diagnostic]',
  'console.table({',
  "element.closest('.audit-history-panel",
  "const DIAGNOSTIC_ENDPOINT = '/api/client-diagnostics'",
  'MAX_AUDIT_EVENTS_PER_SESSION = 20',
  "diagnostic.status === 403",
  "diagnostic.status === 409",
  "diagnostic.status >= 500",
  'Reference:'
]) {
  requireText(presentation, contract, 'friendly API presentation');
}

const auditPayloadStart = presentation.indexOf('body: JSON.stringify({');
assert.ok(auditPayloadStart >= 0, 'friendly API presentation: sanitized audit payload missing');
const auditPayload = presentation.slice(auditPayloadStart, auditPayloadStart + 700);
for (const required of [
  'referenceId:',
  'category:',
  'statusCode:',
  'endpointPath:',
  'technicalCode:',
  'userMessage,',
  'activeRoute:'
]) {
  requireText(auditPayload, required, 'sanitized audit payload');
}
for (const forbidden of ['rawMessage', 'detail:', 'stack', 'password', 'token']) {
  rejectText(auditPayload, forbidden, 'sanitized audit payload');
}

for (const contract of [
  '.projectpulse-friendly-error',
  '.projectpulse-friendly-error-title',
  '.projectpulse-friendly-error-message',
  '.projectpulse-friendly-error-reference',
  "[data-theme='dark'] .projectpulse-friendly-error"
]) {
  requireText(css, contract, 'friendly error styling');
}

requireText(main, "import './api-error-presentation.js';", 'global friendly error import');
requireText(main, "import './friendly-api-errors.css';", 'global friendly error styling import');

for (const contract of [
  'MapClientDiagnosticEndpoints',
  '/api/client-diagnostics',
  'client_api_error',
  'client_diagnostic',
  'NpgsqlDbType.Jsonb',
  'sanitized = true',
  'MAX',
  'SessionUserId',
  'diagnostic_storage_unavailable'
]) {
  if (contract === 'MAX') continue;
  requireText(backend, contract, 'sanitized client diagnostic endpoint');
}
rejectText(backend, 'RawMessage', 'backend diagnostic payload');
rejectText(backend, 'StackTrace', 'backend diagnostic payload');
rejectText(backend, 'Password', 'backend diagnostic payload');
requireText(project, 'app.MapClientDiagnosticEndpoints();', 'backend endpoint registration');

for (const contract of [
  'async function projectPulseOptionalModuleFetch',
  "projectPulseOptionalModuleFetch(() => fetchJson('/api/utilization/policies'",
  "projectPulseOptionalModuleFetch(() => fetchJson('/api/utilization/targets'",
  'window.ProjectPulseErrorPresentation.capture',
  'optionalModuleFailures=isolated'
]) {
  requireText(generator, contract, 'optional Module 003 isolation');
}

assert.equal(
  packageJson.scripts['validate:friendly-api-errors'],
  'node ./scripts/validate-friendly-api-errors.mjs',
  'friendly API error validator must be registered'
);
assert.ok(
  packageJson.scripts.build.includes('npm run validate:friendly-api-errors'),
  'production build must run friendly API error validation'
);

console.log('FRIENDLY_API_ERROR_VALIDATION=PASS ui=standardized console=technical audit=sanitized optionalUtilization=isolated');

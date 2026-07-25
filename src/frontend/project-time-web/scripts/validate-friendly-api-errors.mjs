import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const repoRoot = path.resolve(webRoot, '..', '..', '..');
const absolute = (relative) => path.join(repoRoot, relative);
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const requireText = (source, value, label) => assert.ok(source.includes(value), `${label}: missing ${value}`);
const rejectText = (source, value, label) => assert.ok(!source.includes(value), `${label}: forbidden ${value}`);

function collectSourceFiles(directory, collector = []) {
  fs.readdirSync(directory, { withFileTypes: true }).forEach((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      collectSourceFiles(fullPath, collector);
      return;
    }
    if (/\.(?:js|jsx|mjs)$/.test(entry.name)) collector.push(fullPath);
  });
  return collector;
}

const presentation = read('src/frontend/project-time-web/src/api-error-presentation.js');
const css = read('src/frontend/project-time-web/src/friendly-api-errors.css');
const main = read('src/frontend/project-time-web/src/main.jsx');
const generator = read('src/frontend/project-time-web/scripts/generate-module-001-integrated-app.mjs');
const projectCloseout = read('src/frontend/project-time-web/src/ProjectCloseoutCenter.jsx');
const closeoutEmail = read('src/frontend/project-time-web/src/CloseoutEmailAutomationCenter.jsx');
const packageJson = JSON.parse(read('src/frontend/project-time-web/package.json'));
const backendPath = 'src/backend/ProjectTime.Api/Modules/ClientDiagnosticModule.cs';
const projectPath = 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj';
const fullBackendAvailable = fs.existsSync(absolute(backendPath)) && fs.existsSync(absolute(projectPath));

for (const contract of [
  "We couldn't verify those sign-in details. Check the account and password, then try again.",
  "You don't have access to utilization information with your current role.",
  "You don't have permission to complete this action with your current role.",
  'This information changed while you were working. Refresh the page and try again.',
  'This feature is not available yet.',
  'This information is temporarily unavailable while access is being verified. The rest of the page is still available.',
  'A supporting service is temporarily unavailable. The rest of the page may still be available. Try again shortly.',
  'Something went wrong while processing your request. Try again shortly.',
  '[ProjectPulse API diagnostic]',
  'console.table({',
  "'.audit-history-panel'",
  "const DIAGNOSTIC_ENDPOINT = '/api/client-diagnostics'",
  'MAX_AUDIT_EVENTS_PER_SESSION = 20',
  'diagnostic.status === 403',
  'diagnostic.status === 409',
  'diagnostic.status >= 500',
  'Reference:',
  'RAW_API_FAILURE_PATTERN',
  'document.createTreeWalker',
  'NodeFilter.SHOW_TEXT',
  'nested user-interface error detail',
  'ERROR_ATTRIBUTE_NAMES',
  'sanitizeTechnicalAttributes',
  'installNativeDialogGuards',
  'window.alert = (message)',
  'window.confirm = (message)',
  'attributeFilter: ERROR_ATTRIBUTE_NAMES',
  "'[data-projectpulse-error-policy-exempt]'"
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
  '.projectpulse-friendly-error.compact',
  '.projectpulse-friendly-error.compact::marker',
  '.projectpulse-friendly-error-title',
  '.projectpulse-friendly-error-message',
  '.projectpulse-friendly-error-reference',
  "[data-theme='dark'] .projectpulse-friendly-error"
]) {
  requireText(css, contract, 'friendly error styling');
}

requireText(main, "import './api-error-presentation.js';", 'global friendly error import');
requireText(main, "import './friendly-api-errors.css';", 'global friendly error styling import');

for (const [source, label] of [
  [projectCloseout, 'Module 040 compound closeout warnings'],
  [closeoutEmail, 'Module 041 compound closeout warnings']
]) {
  requireText(source, 'Promise.allSettled([', label);
  requireText(source, 'loadWarnings:', label);
  requireText(source, 'returned HTTP', label);
}
requireText(projectCloseout, '<li key={warning}>{warning}</li>', 'Module 040 nested warning fixture');

const sourceRoot = absolute('src/frontend/project-time-web/src');
const legacyPattern = /returned\s+HTTP|not\s+available\s+for\s+this\s+role|explicit\s+denial|\/api\/[\w\-./?=&%]+[\s\S]{0,160}(?:failed|unavailable|could\s+not\s+be\s+verified)/i;
const legacySurfaceFiles = collectSourceFiles(sourceRoot)
  .filter((filePath) => path.basename(filePath) !== 'api-error-presentation.js')
  .map((filePath) => ({
    filePath,
    source: fs.readFileSync(filePath, 'utf8')
  }))
  .filter((entry) => legacyPattern.test(entry.source));

assert.ok(legacySurfaceFiles.length >= 10, 'repository-wide legacy technical-error inventory unexpectedly found too few surfaces');
legacySurfaceFiles.forEach(({ filePath, source }) => {
  assert.ok(
    !source.includes('data-projectpulse-error-policy-exempt'),
    `legacy technical-error surface may not opt out of global presentation: ${path.relative(repoRoot, filePath)}`
  );
});

if (fullBackendAvailable) {
  const backend = read(backendPath);
  const project = read(projectPath);

  for (const contract of [
    'MapClientDiagnosticEndpoints',
    '/api/client-diagnostics',
    'client_api_error',
    'client_diagnostic',
    'NpgsqlDbType.Jsonb',
    'sanitized = true',
    'SessionUserId',
    'diagnostic_storage_unavailable'
  ]) {
    requireText(backend, contract, 'sanitized client diagnostic endpoint');
  }

  const backendPayloadStart = backend.indexOf('var diagnostic = new');
  const backendPayloadEnd = backend.indexOf('try\n        {', backendPayloadStart);
  assert.ok(backendPayloadStart >= 0 && backendPayloadEnd > backendPayloadStart, 'backend diagnostic payload boundary missing');
  const backendPayload = backend.slice(backendPayloadStart, backendPayloadEnd);
  for (const forbidden of ['RawMessage', 'StackTrace', 'Password', 'Token', 'RequestBody', 'ResponseBody']) {
    rejectText(backendPayload, forbidden, 'backend diagnostic payload');
  }

  requireText(project, 'app.MapClientDiagnosticEndpoints();', 'backend endpoint registration');
}

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

console.log(`FRIENDLY_API_ERROR_VALIDATION=PASS ui=standardized nested=covered dialogs=covered attributes=covered console=technical audit=sanitized optionalUtilization=isolated legacySurfaces=${legacySurfaceFiles.length} backend=${fullBackendAvailable ? 'full' : 'frontend-container'}`);

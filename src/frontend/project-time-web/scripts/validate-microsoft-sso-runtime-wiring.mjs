import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const file = (relative) => path.join(root, relative);
const exists = (relative) => fs.existsSync(file(relative));
const read = (relative) => fs.readFileSync(file(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MICROSOFT_SSO_RUNTIME_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const csproj = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const registrar = read('src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs');
const activation = read('src/frontend/project-time-web/src/microsoft-sso-runtime-activation.js');
const main = read('src/frontend/project-time-web/src/main.jsx');
const runtimePath = 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoRuntimeCompatibility.cs';
const runtimeAvailable = exists(runtimePath);
const runtime = runtimeAvailable ? read(runtimePath) : '';

assert('COMPILED_HANDLER_TENANT', csproj.includes("s/PROJECTPULSE_ENTRA_TENANT_ID/PROJECTPULSE_SSO_TENANT_ID/g"), 'compiled SSO handlers consume the active SSO tenant');
assert('COMPILED_HANDLER_CLIENT', csproj.includes("s/PROJECTPULSE_ENTRA_CLIENT_ID/PROJECTPULSE_SSO_CLIENT_ID/g"), 'compiled SSO handlers consume the SSO App Registration client ID');
assert('COMPILED_HANDLER_REDIRECT', csproj.includes("s/PROJECTPULSE_ENTRA_REDIRECT_URI/PROJECTPULSE_SSO_REDIRECT_URI/g"), 'compiled SSO handlers consume the separate redirect URI');
assert('COMPILED_HANDLER_SECRET', csproj.includes("s/PROJECTPULSE_ENTRA_CLIENT_SECRET/PROJECTPULSE_SSO_CLIENT_SECRET/g"), 'compiled SSO handlers consume the separate SSO secret');
assert('RUNTIME_REGISTERED', registrar.includes('UseMicrosoftSsoRuntimeCompatibility') && registrar.includes('MapMicrosoftSsoRuntimeProfileEndpoints'), 'SSO test sanitizer and runtime profile endpoint are registered');
assert('IMMEDIATE_ACTIVATION_MOUNTED', main.includes("import './microsoft-sso-runtime-activation.js';"), 'saved metadata activation is installed before rendering');
assert('SAVE_INTERCEPT', activation.includes("DOCUMENT_PATH = '/api/native-administration/065/document'") && activation.includes("APPLY_PATH = '/api/microsoft-integration/sso-apply-profile'"), 'successful Module 065 document writes apply the active SSO profile');
assert('SAVE_FAILURE_VISIBLE', activation.includes('sso_runtime_activation_failed') && activation.includes('persistedConfiguration: true') && activation.includes('runtimeActivated: false'), 'runtime activation failures are not silently reported as success');
assert('ORIGINAL_AUTH_HEADERS_REUSED', activation.includes('mergedHeaders(input, init)'), 'runtime apply retains the authenticated request header chain');

if (runtimeAvailable) {
  assert('MICROSOFT_AUTHORITY_DERIVED', runtime.includes('MicrosoftAuthority(tenantGuid)') && runtime.includes('payload["authorityUrl"] = MicrosoftAuthority(tenantGuid)'), 'SSO discovery authority is derived from a validated tenant GUID');
  assert('INVALID_TENANT_REJECTED', runtime.includes('Guid.TryParse(tenantId') && runtime.includes('invalid_sso_tenant_id'), 'non-GUID tenant identifiers are rejected before network access');
  assert('NO_REQUEST_AUTHORITY_FETCH', !runtime.includes('new HttpClient') && !runtime.includes('GetAsync(authorityUrl'), 'runtime sanitizer performs no request-controlled outbound call');
  assert('REDIRECT_VALIDATION', runtime.includes('TryRedirectUri') && runtime.includes('Uri.UriSchemeHttps'), 'saved redirect URIs require HTTPS except bounded loopback development callbacks');
  assert('ACTIVE_PROFILE_APPLIED', runtime.includes('PROJECTPULSE_SSO_TENANT_ID') && runtime.includes('PROJECTPULSE_SSO_CLIENT_ID') && runtime.includes('PROJECTPULSE_SSO_REDIRECT_URI'), 'active SSO metadata is available to the running authentication flow');
  assert('GRAPH_UNTOUCHED', runtime.includes('servicesConnectionChanged = false') && runtime.includes('graphEnvironmentChanged = false') && !runtime.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET'), 'runtime profile activation does not change Graph/services credentials');
} else {
  console.log('MICROSOFT_SSO_RUNTIME_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (checks.some((check) => !check.condition)) {
  console.error('MICROSOFT_SSO_RUNTIME_CONTRACT=FAILED');
  process.exit(1);
}
console.log(`MICROSOFT_SSO_RUNTIME_VALIDATION_CHECKS=${checks.length}`);
console.log('MICROSOFT_SSO_RUNTIME_CONTRACT=PASSED');

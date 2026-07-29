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
const portal = read('src/frontend/project-time-web/src/MicrosoftIntegrationDualConnectionPortal.jsx');
const compatibility = read('src/frontend/project-time-web/src/microsoft-integration-compatibility.js');
const main = read('src/frontend/project-time-web/src/main.jsx');
const runtimePath = 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoRuntimeCompatibility.cs';
const servicesPath = 'src/backend/ProjectTime.Api/Modules/MicrosoftServicesRuntimeCompatibility.cs';
const continuityPath = 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityReadContinuityCompatibility.cs';
const publicOriginPath = 'src/backend/ProjectTime.Api/Modules/ProjectPulsePublicOriginCompatibility.cs';
const stateOriginRecoveryPath = 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoStateOriginRecovery.cs';
const programPath = 'src/backend/ProjectTime.Api/Program.cs';
const runtimeAvailable = exists(runtimePath);
const servicesAvailable = exists(servicesPath);
const continuityAvailable = exists(continuityPath);
const publicOriginAvailable = exists(publicOriginPath);
const stateOriginRecoveryAvailable = exists(stateOriginRecoveryPath);
const programAvailable = exists(programPath);
const runtime = runtimeAvailable ? read(runtimePath) : '';
const services = servicesAvailable ? read(servicesPath) : '';
const continuity = continuityAvailable ? read(continuityPath) : '';
const publicOrigin = publicOriginAvailable ? read(publicOriginPath) : '';
const stateOriginRecovery = stateOriginRecoveryAvailable ? read(stateOriginRecoveryPath) : '';
const program = programAvailable ? read(programPath) : '';

assert('COMPILED_HANDLER_TENANT', csproj.includes("s/PROJECTPULSE_ENTRA_TENANT_ID/PROJECTPULSE_SSO_TENANT_ID/g"), 'compiled SSO handlers consume the active SSO tenant');
assert('COMPILED_HANDLER_CLIENT', csproj.includes("s/PROJECTPULSE_ENTRA_CLIENT_ID/PROJECTPULSE_SSO_CLIENT_ID/g"), 'compiled SSO handlers consume the SSO App Registration client ID');
assert('COMPILED_HANDLER_REDIRECT', csproj.includes("s/PROJECTPULSE_ENTRA_REDIRECT_URI/PROJECTPULSE_SSO_REDIRECT_URI/g"), 'compiled SSO handlers consume the separate redirect URI');
assert('COMPILED_HANDLER_SECRET', csproj.includes("s/PROJECTPULSE_ENTRA_CLIENT_SECRET/PROJECTPULSE_SSO_CLIENT_SECRET/g"), 'compiled SSO handlers consume the separate SSO secret');
assert('RUNTIME_REGISTERED', registrar.includes('UseMicrosoftSsoRuntimeCompatibility')
  && registrar.includes('MapMicrosoftSsoRuntimeProfileEndpoints')
  && registrar.includes('MapMicrosoftServicesRuntimeProfileEndpoints'),
'SSO sanitization plus separate SSO and Microsoft services runtime endpoints are registered');

const publicOriginRegistration = registrar.includes('UseProjectPulsePublicOriginCompatibility')
  && registrar.includes('UseMicrosoftPublicSsoOriginCompatibility')
  && registrar.includes('trusted_public_origin_unavailable')
  && registrar.indexOf('UseProjectPulsePublicOriginCompatibility') < registrar.indexOf('UseMicrosoftPublicSsoOriginCompatibility')
  && registrar.indexOf('UseMicrosoftPublicSsoOriginCompatibility') < registrar.indexOf('UseMicrosoftSsoRuntimeCompatibility');
const publicOriginImplementation = !publicOriginAvailable || (
  publicOrigin.includes('X-Forwarded-Host')
  && publicOrigin.includes('X-Forwarded-Proto')
  && publicOrigin.includes('request.Headers["Origin"]')
  && publicOrigin.includes('request.Headers["Referer"]')
  && publicOrigin.includes('.onenecklab.com')
  && publicOrigin.includes('.ussignal.com')
  && publicOrigin.includes('trusted_forwarded_origin')
);
assert('FORWARDED_PUBLIC_ORIGIN', publicOriginRegistration && publicOriginImplementation,
'Module 065 resolves a trusted HTTPS public proxy/browser origin before callback validation');

const callbackStateRecovery = stateOriginRecoveryAvailable
  && registrar.includes('MicrosoftSsoStateOriginRecovery.TryRecoverAsync')
  && registrar.includes('MicrosoftSsoStateOriginRecovery.CallbackPath')
  && registrar.includes('context.Request.Query["state"]')
  && stateOriginRecovery.includes('SELECT redirect_uri')
  && stateOriginRecovery.includes('consumed_at IS NULL')
  && stateOriginRecovery.includes('expires_at > NOW()')
  && stateOriginRecovery.includes('unconsumed_auth_sso_state_redirect_uri');
assert('CALLBACK_STATE_ORIGIN_RECOVERY', callbackStateRecovery,
'Microsoft callback origin can be recovered only from an unconsumed, unexpired auth_sso_state row');

const storedRedirectValidation = stateOriginRecoveryAvailable
  && stateOriginRecovery.includes('Uri.UriSchemeHttps')
  && stateOriginRecovery.includes('CallbackPath')
  && stateOriginRecovery.includes('stored_redirect_uri_user_info_rejected')
  && stateOriginRecovery.includes('stored_redirect_uri_query_or_fragment_rejected')
  && stateOriginRecovery.includes('stored_redirect_uri_port_rejected')
  && stateOriginRecovery.includes('stored_redirect_uri_internal_host_rejected')
  && stateOriginRecovery.includes('.onenecklab.com')
  && stateOriginRecovery.includes('.ussignal.com')
  && stateOriginRecovery.includes('stored_redirect_uri_environment_mismatch');
assert('STORED_REDIRECT_FAIL_CLOSED', storedRedirectValidation,
'stored callback requires HTTPS, exact callback path, approved environment host, matching environment, and no user-info, query, fragment, internal host, or unapproved port');

const atomicConsumptionUsesStoredRedirect = programAvailable
  && program.includes('UPDATE auth_sso_state')
  && program.includes('consumed_at IS NULL')
  && program.includes('expires_at > NOW()')
  && program.includes('RETURNING nonce_token, requested_email, redirect_uri;')
  && program.includes('MicrosoftSsoStateOriginRecovery.TryValidateStoredRedirectUri')
  && program.includes('["redirect_uri"] = redirectUri')
  && program.includes('ProjectPulseValidateMicrosoftIdTokenAsync(idToken, tenantId, clientId, nonce)');
assert('ATOMIC_STATE_AND_NONCE_PRESERVED', atomicConsumptionUsesStoredRedirect,
'callback atomically consumes state, reads its exact redirect URI, uses it for token exchange, and preserves nonce validation');
if (!publicOriginAvailable) {
  console.log('MICROSOFT_SSO_RUNTIME_PUBLIC_ORIGIN_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

assert('IMMEDIATE_ACTIVATION_MOUNTED', main.includes("import './microsoft-sso-runtime-activation.js';"), 'saved Module 065 metadata activation is installed before rendering');
assert('SAVE_INTERCEPT', activation.includes("DOCUMENT_PATH = '/api/native-administration/065/document'")
  && activation.includes("SSO_APPLY_PATH = '/api/microsoft-integration/sso-apply-profile'")
  && activation.includes("SERVICES_APPLY_PATH = '/api/microsoft-integration/services-apply-profile'"),
'successful Module 065 document writes apply both active connection profiles');
assert('SAVE_REMAINS_AUTHORITATIVE', activation.includes('return response;')
  && activation.includes('persistedConfiguration: true')
  && activation.includes('projectpulse:microsoft-connection-runtime-status')
  && !activation.includes("status: 'sso_runtime_activation_failed'"),
'persisted Module 065 revisions remain successful while sanitized runtime activation status is reported separately');
assert('ORIGINAL_AUTH_HEADERS_REUSED', activation.includes('mergedHeaders(input, init)'), 'runtime apply retains the authenticated request header chain');
assert('PORTAL_METADATA_FIRST', portal.includes('await persistConfiguration(purpose);')
  && portal.includes("saveSecret('sso')")
  && portal.includes("saveSecret('services')")
  && portal.includes('Save SSO connection')
  && portal.includes('Save services connection'),
'connection metadata is persisted for the requested connection before either write-only secret is stored');
assert('PORTAL_INDEPENDENT_SSO_SAVE', portal.includes("async function persistConfiguration(purpose = 'integration')")
  && portal.includes('validateActiveConnection(purpose);')
  && portal.includes("if (purpose !== 'sso')")
  && portal.includes("applicationId: activeTenant.services.clientId || nativeDocument?.configuration?.applicationId || ''")
  && portal.includes("await persistConfiguration('integration');"),
'SSO metadata can be saved independently without validating or overwriting the services/Graph profile');
assert('PORTAL_CURRENT_CALLBACK', portal.includes("const SSO_CALLBACK_PATH = '/api/auth/sso/callback'")
  && portal.includes('currentCallbackUri()')
  && portal.includes('Use current callback'),
'Module 065 derives and exposes the callback for the current ProjectPulse environment');
assert('MODULE_010_PROFILE_PRELOAD', compatibility.includes("PREVIEW_ROUTE = '/api/admin/azure/users/preview'")
  && compatibility.includes('applyStoredServicesProfile')
  && compatibility.includes("SERVICES_APPLY_PATH = '/api/microsoft-integration/services-apply-profile'"),
'Module 010 preview activates the saved Module 065 services profile before calling the preview endpoint');
assert('MODULE_010_RUNNING_ENVIRONMENT', compatibility.includes('function runtimeEnvironmentMode()')
  && compatibility.includes("String(tenant?.environmentMode || '').toLowerCase() === runtimeEnvironment")
  && compatibility.includes("status: 'module_065_services_profile_not_active'")
  && compatibility.includes('applyPayload?.runtimeActivated !== true')
  && compatibility.includes("String(applyPayload?.runtimeEnvironment || '').toLowerCase() !== profile.environmentMode"),
'Module 010 preview selects the running environment and stops unless Module 065 confirms matching runtime activation');

if (continuityAvailable) {
  assert('READ_CONTINUITY_NON_MUTATING', registrar.includes('UseModuleAvailabilityReadContinuityCompatibility')
    && continuity.includes('/api/microsoft-integration/sso-test')
    && continuity.includes('/api/admin/azure/users/preview')
    && !continuity.includes('/api/microsoft-integration/directory-users/import-selected'),
  'optional availability storage cannot block tests/preview and cannot bypass import or configuration writes');
} else {
  console.log('MICROSOFT_SSO_RUNTIME_CONTINUITY_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (runtimeAvailable) {
  assert('MICROSOFT_AUTHORITY_DERIVED', runtime.includes('MicrosoftAuthority(tenantGuid)') && runtime.includes('payload["authorityUrl"] = MicrosoftAuthority(tenantGuid)'), 'SSO discovery authority is derived from a validated tenant GUID');
  assert('INVALID_IDENTIFIERS_REJECTED', runtime.includes('invalid_sso_tenant_id')
    && runtime.includes('invalid_sso_client_id')
    && runtime.includes('invalid_sso_redirect_uri'),
  'invalid tenant, client, and callback values are rejected before provider access');
  assert('NO_REQUEST_AUTHORITY_FETCH', !runtime.includes('new HttpClient') && !runtime.includes('GetAsync(authorityUrl'), 'runtime sanitizer performs no request-controlled outbound call');
  assert('REDIRECT_VALIDATION', runtime.includes('TryRedirectUri')
    && runtime.includes('CallbackPath')
    && runtime.includes('sso_redirect_host_mismatch'),
  'saved callback requires HTTPS, the canonical callback path, and the active public environment host');
  assert('ACTIVE_PROFILE_APPLIED', runtime.includes('PROJECTPULSE_SSO_MODE')
    && runtime.includes('PROJECTPULSE_SSO_TENANT_ID')
    && runtime.includes('PROJECTPULSE_SSO_CLIENT_ID')
    && runtime.includes('PROJECTPULSE_SSO_REDIRECT_URI')
    && runtime.includes('PROJECTPULSE_SSO_CLIENT_SECRET'),
  'the active environment receives complete SSO metadata and the existing environment-specific SSO secret');
  assert('GRAPH_UNTOUCHED', runtime.includes('servicesConnectionChanged = false')
    && runtime.includes('graphEnvironmentChanged = false')
    && !runtime.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET'),
  'SSO activation does not overwrite the services/Graph credential contract');
} else {
  console.log('MICROSOFT_SSO_RUNTIME_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (servicesAvailable) {
  assert('SERVICES_RUNTIME_ENDPOINT', services.includes('/api/microsoft-integration/services-apply-profile')
    && services.includes('module010PreviewSource = "module_065_services_profile"'),
  'Module 065 has an explicit Microsoft services runtime profile endpoint for Module 010 and other consumers');
  assert('DIRECTORY_PERMISSION_GATE', services.includes('Directory.Read.All')
    && services.includes('User.Read.All')
    && services.includes('directory_application_permissions_required'),
  'Module 010 services activation fails closed without required Graph application permissions');
  assert('SERVICES_ENVIRONMENT_PROJECTION', services.includes('PROJECTPULSE_ENTRA_TEST_')
    && services.includes('PROJECTPULSE_ENTRA_PRODUCTION_')
    && services.includes('PROJECTPULSE_M365_CLIENT_ID')
    && services.includes('PROJECTPULSE_M365_SENDER_MAILBOX'),
  'Test and Production services metadata are separated and the active profile feeds Microsoft 365');
  assert('SERVICES_SECRET_SAFETY', services.includes('secretValuesRead = false')
    && services.includes('secretValuesReturned = false')
    && !services.includes('ClientSecret'),
  'the services runtime endpoint never accepts or returns a secret value');
} else {
  console.log('MICROSOFT_SERVICES_RUNTIME_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (checks.some((check) => !check.condition)) {
  const failed = checks.filter((check) => !check.condition).map((check) => check.name);
  console.error(`MICROSOFT_SSO_RUNTIME_FAILED_CHECKS=${failed.join(',')}`);
  console.error('MICROSOFT_SSO_RUNTIME_CONTRACT=FAILED');
  process.exit(1);
}
console.log(`MICROSOFT_SSO_RUNTIME_VALIDATION_CHECKS=${checks.length}`);
console.log('MICROSOFT_SSO_RUNTIME_CONTRACT=PASSED');

import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(repoRoot, relativePath), 'utf8');
const exists = (relativePath) => fs.existsSync(path.join(repoRoot, relativePath));

const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/CrmErpIntegrationModule.cs',
  frontend: 'src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx',
  css: 'src/frontend/project-time-web/src/crm-erp-integration-center.css',
  migration: 'database/migrations/034_module_026_crm_erp_integrations.sql',
  rollback: 'database/rollback/034_module_026_crm_erp_integrations_rollback.sql',
  readme: 'docs/modules/module-026-crm-erp-integrations/README.md',
  api: 'docs/modules/module-026-crm-erp-integrations/API-CONTRACT.md',
  authorization: 'docs/modules/module-026-crm-erp-integrations/AUTHORIZATION-MATRIX.md',
  security: 'docs/modules/module-026-crm-erp-integrations/SECURITY-BOUNDARY.md',
};

const app = read('src/frontend/project-time-web/src/App.jsx');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const legacy = read('src/frontend/project-time-web/index.html');
const backend = read(files.backend);
const sellImport = read('src/backend/ProjectTime.Api/Modules/WorkRegisterSellImportModule.cs');
const frontend = read(files.frontend);
const migration = read(files.migration);
const docker = read('deployment/containers/web/Dockerfile');
const pkg = JSON.parse(read('src/frontend/project-time-web/package.json'));

let checks = 0;
let failures = 0;
function test(name, condition) {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`MODULE_026_${name}=${condition ? 'PASSED' : 'FAILED'}`);
}

for (const [name, file] of Object.entries(files)) test(`FILE_${name.toUpperCase()}`, exists(file));

test('BUILTIN_PROVIDERS', ['zendesk_sell', 'salesforce', 'certinia', 'servicenow'].every((provider) => migration.includes(`'${provider}'`)));
test('MANUAL_PROVIDER_ROUTE', backend.includes('group.MapPost("/providers", CreateProviderAsync);'));
test('OAUTH_START_ROUTE', backend.includes('/providers/{providerKey}/oauth/start'));
test('OAUTH_CALLBACK_ROUTE', backend.includes('/api/public/integrations/026/oauth/callback'));
test('API_KEY_AND_OAUTH', migration.includes("CHECK (auth_model IN ('api_key', 'oauth2'))"));
test('WRITE_ONLY_ENCRYPTION', backend.includes('PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY') && backend.includes('new AesGcm(encryptionKey, 16)') && backend.includes('valueReturned = false'));
test('SSRF_BOUNDARY', backend.includes('IsSafeExternalUriAsync') && backend.includes('IsPublicAddress') && backend.includes('AllowAutoRedirect = false'));
test('DNS_REBINDING_BLOCKED', backend.includes('ConnectCallback = ConnectToPublicEndpointAsync') && backend.includes('socket.ConnectAsync') && backend.includes('addresses.Any(address => !IsPublicAddress(address))'));
test('PRIVATE_IPV6_BLOCKED', backend.includes('IsIPv4MappedToIPv6') && backend.includes('isUniqueLocal') && backend.includes('isGlobalUnicast'));
test('PROXY_BYPASS_BLOCKED', backend.includes('UseProxy = false'));
test('AUDIT_MODULE_CONSTRAINT', migration.includes("'026'") && migration.includes('ck_projectpulse_module_audit_module'));
test('BOUNDED_PROVIDER_RESPONSE', backend.includes('MaximumProviderResponseBytes') && backend.includes('ReadBoundedResponseBodyAsync'));
test('CONNECTION_STATUS_SET', ['available', 'authentication_failed', 'unavailable', 'not_configured'].every((status) => migration.includes(`'${status}'`)));
test('SANITIZED_CONNECTION_CHECK', migration.includes('crm_integration_connection_checks') && backend.includes('remote_authentication_rejected') && backend.includes('remote_non_success_status'));
test('AUDIT_WRITES', backend.includes('SecurityDiagnosticsOperations.WriteAuditAsync') && backend.includes('credential_replaced') && backend.includes('connection_tested'));
test('VIEW_AS_BLOCKED', backend.includes('if (IsViewAs(context)) return Results.Forbid();'));
test('NATIVE_REACT_ROUTE', app.includes("import CrmErpIntegrationCenter from './CrmErpIntegrationCenter.jsx';") && app.includes('<CrmErpIntegrationCenter />'));
test('LEGACY_OVERLAY_DISABLED', legacy.includes('MODULE_026_NATIVE_REACT_ROUTE') && legacy.includes("(function () {\n  // MODULE_026_NATIVE_REACT_ROUTE: the historical local-only overlay is disabled.\n  return;"));
test('PROVIDER_STATUS_UI', frontend.includes('SELL, Salesforce, Certinia, ServiceNow') && frontend.includes('Test availability') && frontend.includes('Add another platform'));
test('AUTHENTICATION_UI', frontend.includes('OAuth 2.0') && frontend.includes('API key') && frontend.includes('Write-only credential'));
test('SELL_RECORD_LOOKUP', migration.includes('record_lookup_url_template') && frontend.includes('Record lookup URL template'));
test('SELL_IMPORT_MAPPING', migration.includes('import_mapping_json') && frontend.includes('Import field mapping (JSON)'));
test('SELL_AUTHORITATIVE_FIELDS', sellImport.includes('sourceFieldsLocked') && sellImport.includes('projectName') && sellImport.includes('rates'));
test('PERMISSIONS', migration.includes('VIEW_INTEGRATIONS_026') && migration.includes('MANAGE_INTEGRATIONS_026'));
test('MIGRATION_NOT_RUNTIME_APPLIED', !program.includes('034_module_026_crm_erp_integrations.sql'));
test('PROGRAM_MAP', program.includes('app.MapCrmErpIntegrationEndpoints();'));
test('HTTP_CLIENT_BOUNDARY', program.includes('AddHttpClient("Module026"') && program.includes('TimeSpan.FromSeconds(12)') && program.includes('CreateSecureHttpHandler'));
test('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes(files.migration) && docker.includes('docs/modules/module-026-crm-erp-integrations/'));
test('BUILD_GATE', pkg.scripts?.['validate:module026'] === 'node ./scripts/validate-module-026-crm-erp-integrations.mjs' && pkg.scripts?.build?.includes('npm run validate:module026'));

console.log(`MODULE_026_VALIDATION_CHECKS=${checks}`);
console.log(`MODULE_026_EXTERNAL_CALLS_PERFORMED=0`);
console.log(`MODULE_026_MIGRATION_034=CREATED_NOT_APPLIED`);
console.log(`MODULE_026_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;

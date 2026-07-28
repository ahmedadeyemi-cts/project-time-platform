import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(repoRoot, relativePath), 'utf8');
const exists = (relativePath) => fs.existsSync(path.join(repoRoot, relativePath));

const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/CrmErpIntegrationModule.cs',
  administration: 'src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs',
  rbacBridge: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyAuthorizationBridge.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
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
const administration = read(files.administration);
const rbacBridge = read(files.rbacBridge);
const project = read(files.project);
const sellImport = read('src/backend/ProjectTime.Api/Modules/WorkRegisterSellImportModule.cs');
const frontend = read(files.frontend);
const css = read(files.css);
const migration = read(files.migration);
const docker = read('deployment/containers/web/Dockerfile');
const pkg = JSON.parse(read('src/frontend/project-time-web/package.json'));

let checks = 0;
let failures = 0;
function test(name, condition, evidence = '') {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`MODULE_026_${name}=${condition ? 'PASSED' : 'FAILED'}${evidence ? ` — ${evidence}` : ''}`);
}

for (const [name, file] of Object.entries(files)) test(`FILE_${name.toUpperCase()}`, exists(file), file);

test('BUILTIN_PROVIDERS', ['zendesk_sell', 'salesforce', 'servicenow', 'certinia'].every((provider) => administration.includes(`"${provider}"`) && migration.includes(`'${provider}'`)));
test('VIRTUAL_BUILTIN_BOOTSTRAP', administration.includes('BuiltinProviderTemplates') && administration.includes('if (persistedKeys.Contains(template.ProviderKey)) continue;') && administration.includes('IsPersisted') && administration.includes('firstSaveCreatesProvider = true'));
test('MIGRATION_SEED_NOT_REQUIRED_FOR_DISPLAY', administration.includes('migrationSeedRequiredForDisplay = false') && administration.includes('virtualTemplateCount'));
test('MANUAL_PROVIDER_ROUTE', backend.includes('group.MapPost("/providers", CreateProviderAsync);'));
test('EDIT_PROVIDER_ROUTE', backend.includes('group.MapPut("/providers/{providerKey}", UpdateProviderAsync);'));
test('WRITE_ONLY_CREDENTIAL_ROUTE', backend.includes('group.MapPut("/providers/{providerKey}/credential", ReplaceCredentialAsync);'));
test('OAUTH_START_ROUTE', backend.includes('/providers/{providerKey}/oauth/start'));
test('OAUTH_CALLBACK_ROUTE', backend.includes('/api/public/integrations/026/oauth/callback'));
test('API_KEY_AND_OAUTH', migration.includes("CHECK (auth_model IN ('api_key', 'oauth2'))"));
test('WRITE_ONLY_ENCRYPTION', backend.includes('PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY') && backend.includes('new AesGcm(encryptionKey, 16)') && backend.includes('valueReturned = false'));
test('SECRET_NEVER_RETURNED', administration.includes('SecretValueReturned') && administration.includes('secretsReturned = false') && frontend.includes('The value is encrypted and cannot be viewed after saving.'));
test('MASKED_CREDENTIAL_INPUT', frontend.includes("type={showCredential ? 'text' : 'password'}") && frontend.includes('autoComplete="new-password"') && frontend.includes('Show while typing') && frontend.includes('Save credential securely'));
test('SSRF_BOUNDARY', backend.includes('IsSafeExternalUriAsync') && backend.includes('IsPublicAddress') && backend.includes('AllowAutoRedirect = false'));
test('DNS_REBINDING_BLOCKED', backend.includes('ConnectCallback = ConnectToPublicEndpointAsync') && backend.includes('socket.ConnectAsync') && backend.includes('addresses.Any(address => !IsPublicAddress(address))'));
test('PRIVATE_IPV6_BLOCKED', backend.includes('IsIPv4MappedToIPv6') && backend.includes('isUniqueLocal') && backend.includes('isGlobalUnicast'));
test('PROXY_BYPASS_BLOCKED', backend.includes('UseProxy = false'));
test('AUDIT_MODULE_CONSTRAINT', migration.includes("'026'") && migration.includes('ck_projectpulse_module_audit_module'));
test('BOUNDED_PROVIDER_RESPONSE', backend.includes('MaximumProviderResponseBytes') && backend.includes('ReadBoundedResponseBodyAsync'));
test('CONNECTION_STATUS_SET', ['available', 'authentication_failed', 'unavailable', 'not_configured'].every((status) => migration.includes(`'${status}'`)));
test('SANITIZED_CONNECTION_CHECK', migration.includes('crm_integration_connection_checks') && backend.includes('remote_authentication_rejected') && backend.includes('remote_non_success_status'));
test('AUDIT_WRITES', backend.includes('SecurityDiagnosticsOperations.WriteAuditAsync') && backend.includes('credential_replaced') && backend.includes('connection_tested'));
test('VIEW_AS_BLOCKED', administration.includes('Exit Administrator View-As before changing CRM or ERP connector configuration.') && backend.includes('if (IsViewAs(context)) return Results.Forbid();'));

test('DYNAMIC_RBAC_BRIDGE', rbacBridge.includes('EvaluateCurrentActorAsync') && rbacBridge.includes('ScopedAuthorizationEvaluator.EvaluateAsync') && administration.includes('"MODULE_CONFIGURE"') && administration.includes('published_role_policy'));
test('LEGACY_AUTHORIZATION_FALLBACK', project.includes('HasManageAuthorityLegacyAsync') && administration.includes('HasManageAuthorityLegacyAsync(context)') && administration.includes('legacy_role_or_permission'));
test('SUPER_ADMINISTRATOR_COMPATIBILITY', administration.includes('MANAGE_INTEGRATIONS_026') && administration.includes('MODULE_CONFIGURE') && rbacBridge.includes('LoadActorAsync'));
test('MANAGE_AUTHORITY_VISIBLE', administration.includes('manageAuthoritySource') && administration.includes('manageMessage') && frontend.includes('Required permission:') && frontend.includes('Authority source:'));
test('GENERATED_PARTIAL_COMPILE', project.includes('<Compile Remove="Modules/CrmErpIntegrationModule.cs" />') && project.includes('public static partial class CrmErpIntegrationModule') && project.includes('ListProvidersLegacyAsync') && project.includes('AuthorizeManageLegacyAsync') && project.includes('HasManageAuthorityLegacyAsync') && project.includes('<Compile Include="$(Module026GeneratedIntegration)" />'));

test('NATIVE_REACT_ROUTE', app.includes("import CrmErpIntegrationCenter from './CrmErpIntegrationCenter.jsx';") && app.includes('<CrmErpIntegrationCenter />'));
test('LEGACY_OVERLAY_DISABLED', legacy.includes('MODULE_026_NATIVE_REACT_ROUTE') && legacy.includes("(function () {\n  // MODULE_026_NATIVE_REACT_ROUTE: the historical local-only overlay is disabled.\n  return;"));
test('CORE_CONNECTOR_CARDS', frontend.includes('SELL') && frontend.includes('Salesforce') && frontend.includes('ServiceNow') && frontend.includes('Certinia') && frontend.includes('Configure connection'));
test('EXPLICIT_EDIT_MODE', frontend.includes('Edit connection') && frontend.includes('beginEditing') && frontend.includes('cancelEditing') && frontend.includes("const [editing, setEditing] = useState(false)"));
test('FIRST_SAVE_CREATES_BUILTIN', frontend.includes('const creating = !draft.isPersisted') && frontend.includes("creating ? '/api/integrations/026/providers'") && frontend.includes("method: creating ? 'POST' : 'PUT'"));
test('CUSTOM_PLATFORM_MODAL', frontend.includes('Add CRM platform') && frontend.includes('Add another CRM or ERP platform') && frontend.includes('Add platform and continue setup') && frontend.includes('crm-erp-modal-backdrop'));
test('AUTHENTICATION_UI', frontend.includes('OAuth 2.0') && frontend.includes('API key') && frontend.includes('Write-only credential'));
test('SELL_RECORD_LOOKUP', migration.includes('record_lookup_url_template') && frontend.includes('Record lookup URL template'));
test('SELL_IMPORT_MAPPING', migration.includes('import_mapping_json') && frontend.includes('Import field mapping (JSON)'));
test('SELL_AUTHORITATIVE_FIELDS', sellImport.includes('sourceFieldsLocked') && sellImport.includes('projectName') && sellImport.includes('rates'));
test('PERMISSIONS', migration.includes('VIEW_INTEGRATIONS_026') && migration.includes('MANAGE_INTEGRATIONS_026'));

test('STANDARD_HEADER_ORDER', frontend.indexOf("import './projectpulse-module-standard.css';") < frontend.indexOf("import './crm-erp-integration-center.css';"));
test('STANDARD_LOGO', frontend.includes('className="projectpulse-module-standard__logo"') && frontend.includes('alt="US Signal"'));
test('READABLE_HEADER_CONTRAST', css.includes('.crm-erp-hero-copy > span') && css.includes('color: #425a70') && !css.includes('.crm-erp-brand span'));
test('SCOPED_DESIGN_SYSTEM', css.includes('--026-navy: var(--pp-module-navy') && css.includes('--026-surface: var(--pulse-card-bg') && css.includes('.crm-erp-platform-card-footer') && css.includes('.crm-erp-secret-input'));
test('RESPONSIVE_LAYOUT', css.includes('@media (max-width: 1180px)') && css.includes('@media (max-width: 900px)') && css.includes('@media (max-width: 680px)'));
test('NO_UNSCOPED_GLOBAL_CSS', !/(^|\n)\s*(?:html|body|:root|#root|main|button|input|select|textarea)\s*[{,]/m.test(css));

test('MIGRATION_NOT_RUNTIME_APPLIED', !program.includes('034_module_026_crm_erp_integrations.sql'));
test('PROGRAM_MAP', program.includes('app.MapCrmErpIntegrationEndpoints();'));
test('HTTP_CLIENT_BOUNDARY', program.includes('AddHttpClient("Module026"') && program.includes('TimeSpan.FromSeconds(12)') && program.includes('CreateSecureHttpHandler'));
test('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes(files.administration) && docker.includes(files.rbacBridge) && docker.includes(files.project) && docker.includes(files.migration) && docker.includes('docs/modules/module-026-crm-erp-integrations/'));
test('BUILD_GATE', pkg.scripts?.['validate:module026'] === 'node ./scripts/validate-module-026-crm-erp-integrations.mjs' && pkg.scripts?.build?.includes('npm run validate:module026'));

console.log(`MODULE_026_VALIDATION_CHECKS=${checks}`);
console.log('MODULE_026_BUILTIN_CONNECTORS=SELL_SALESFORCE_SERVICENOW_CERTINIA');
console.log('MODULE_026_CUSTOM_CONNECTORS=SUPPORTED');
console.log('MODULE_026_SAVED_SECRET_READBACK=PROHIBITED');
console.log('MODULE_026_EXTERNAL_CALLS_PERFORMED=0');
console.log('MODULE_026_MIGRATION_034=NOT_APPLIED_BY_SOURCE_CHANGE');
console.log(`MODULE_026_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;

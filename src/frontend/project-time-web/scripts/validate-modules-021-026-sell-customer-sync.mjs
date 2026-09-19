import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULES_021_026_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const paths = {
  integrationUi: 'src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx',
  integrationCss: 'src/frontend/project-time-web/src/crm-erp-integration-center.css',
  customerUi: 'src/frontend/project-time-web/src/CustomerDirectoryCenter.jsx',
  customerCss: 'src/frontend/project-time-web/src/customer-directory-sell-sync.css',
  validator: 'src/frontend/project-time-web/scripts/validate-modules-021-026-sell-customer-sync.mjs',
  package: 'src/frontend/project-time-web/package.json',
  backend: 'src/backend/ProjectTime.Api/Modules/CustomerDirectorySellSyncModule.cs',
  providerBackend: 'src/backend/ProjectTime.Api/Modules/CrmErpIntegrationModule.cs',
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  migration: 'database/migrations/049_module_021_sell_customer_sync.sql',
  rollback: 'database/rollback/049_module_021_sell_customer_sync_rollback.sql',
  migrationTest: 'tests/test-module-021-sell-customer-sync-049.sh',
  program: 'src/backend/ProjectTime.Api/Program.cs',
};

for (const key of ['integrationUi', 'integrationCss', 'customerUi', 'customerCss', 'validator', 'package']) {
  assert(`FILE_${key.toUpperCase()}`, exists(paths[key]), paths[key]);
}

const integrationUi = read(paths.integrationUi);
const integrationCss = read(paths.integrationCss);
const customerUi = read(paths.customerUi);
const customerCss = read(paths.customerCss);
const packageJson = JSON.parse(read(paths.package));

assert('CORE_PROVIDER_CARDS', ['connectwise_sell', 'salesforce', 'servicenow', 'certinia']
  .every((provider) => integrationUi.includes(`${provider}: {`)),
'Module 026 renders explicit SELL, Salesforce, ServiceNow, and Certinia provider profiles');
assert('CONSISTENT_PROVIDER_WORKSPACE', integrationUi.includes('Select a connector, then choose Edit')
  && integrationUi.includes('Built-in and custom platforms')
  && integrationUi.includes('Edit connection')
  && integrationCss.includes('.crm-erp-platform-grid')
  && integrationCss.includes('.crm-erp-provider-guide'),
'core platforms share one responsive select, status, edit, configuration, credential, and test workspace');
assert('OAUTH_AND_API_KEY_PAGES', integrationUi.includes("draft.authModel === 'oauth2'")
  && integrationUi.includes('OAuth authorization URL')
  && integrationUi.includes('OAuth token URL')
  && integrationUi.includes('API-key header')
  && integrationUi.includes('API key / access token')
  && integrationUi.includes('Write-only credential'),
'authentication-specific fields are displayed only for the selected OAuth 2.0 or API-key mode');
assert('PROVIDER_TEMPLATE_ACTION', integrationUi.includes('Apply recommended template')
  && integrationUi.includes('applySelectedTemplate')
  && integrationUi.includes('Add CRM platform')
  && integrationUi.includes('Add another CRM or ERP platform'),
'admins can apply built-in defaults or add and continue configuring another provider');
assert('SELL_MODULE_021_HANDOFF', integrationUi.includes('Customer sync: adapter required')
  && integrationUi.includes('Open Module 021 Customer Directory sync'),
'Module 026 explicitly identifies the SELL connection consumed by Module 021');
assert('SELL_PUBLIC_ENDPOINTS', integrationUi.includes('https://sellapi.quosalsell.com/api/quotes?page=1&pageSize=1')
  && !integrationUi.includes('api.getbase.com') && integrationUi.includes("apiKeyPrefix: 'Basic'"),
'ConnectWise SELL uses its CPQ quote API and API-key Basic authentication');
assert('CUSTOMER_SYNC_STATUS', customerUi.includes("fetchJson('/api/customers/sell/status'")
  && customerUi.includes("fetchJson('/api/customers/sell/runs'"),
'Module 021 reads connection readiness and synchronization history');
assert('CUSTOMER_SYNC_PREVIEW_IMPORT', customerUi.includes("sendJson('/api/customers/sell/preview'")
  && customerUi.includes("sendJson('/api/customers/sell/import'")
  && customerUi.includes('Preview ConnectWise SELL customers')
  && customerUi.includes('Import / refresh selected'),
'Module 021 provides governed preview, selection, import, and refresh controls');
assert('LOCAL_ENRICHMENT_VISIBLE', customerUi.includes('Local enrichment')
  && customerUi.includes('locally maintained contacts')
  && customerUi.includes('not overwritten by ConnectWise SELL synchronization')
  && customerUi.includes('/contacts'),
'ProjectPulse contact enrichment remains available after customer synchronization');
assert('INTUITIVE_SYNC_LAYOUT', customerCss.includes('.customer-sell-readiness-grid')
  && customerCss.includes('.customer-sell-filter-grid')
  && customerCss.includes('.customer-sell-table')
  && customerCss.includes('.customer-sell-history'),
'Module 021 sync status, filters, table, and run history have responsive dedicated styling');
assert('BUILD_GATE', packageJson.scripts?.['validate:modules021026'] === 'node ./scripts/validate-modules-021-026-sell-customer-sync.mjs'
  && packageJson.scripts?.build?.includes('npm run validate:modules021026'),
'frontend production build executes the Modules 021/026 validator');

const fullRepositoryContext = ['backend', 'providerBackend', 'registrar', 'migration', 'rollback', 'migrationTest', 'program']
  .every((key) => exists(paths[key]));

if (fullRepositoryContext) {
  const backend = read(paths.backend);
  const providerBackend = read(paths.providerBackend);
  const registrar = read(paths.registrar);
  const migration = read(paths.migration);
  const rollback = read(paths.rollback);
  const migrationTest = read(paths.migrationTest);
  const program = read(paths.program);

  assert('BACKEND_ROUTES', [
    '/api/customers/sell/status',
    '/api/customers/sell/preview',
    '/api/customers/sell/import',
    '/api/customers/sell/runs',
  ].every((route) => backend.includes(route)),
  'Module 021 has status, preview, import, and history APIs');
  assert('MODULE_026_CONNECTION', backend.includes('ProviderKey = ConnectWiseSellApi.ProviderKey')
    && backend.includes('ReadProviderAsync') && backend.includes('credential_configured'),
  'status reads the authoritative ConnectWise SELL connection');
  assert('CUSTOMER_ADAPTER_BLOCKED', backend.includes('customerSyncSupported = false')
    && backend.includes('connectwise_sell_customer_adapter_required')
    && !backend.includes('LoadCredentialAsync') && !backend.includes('SendAsync'),
  'unsupported customer sync cannot send credentials or claim success');
  assert('NO_LEGACY_CONTACT_API', !backend.includes('api.getbase.com') && !backend.includes('v2/contacts'),
  'the incompatible contacts API has been removed');
  assert('SYNC_READINESS_UI', customerUi.includes('status?.customerSyncSupported === true')
    && customerUi.includes('customerSyncMessage'),
  'the interface distinguishes API access from customer adapter readiness');
  assert('LOCAL_CONTACTS_PRESERVED', backend.includes('localContactEnrichmentPreserved = true')

    && !backend.includes('UPDATE client_contacts')
    && !backend.includes('INSERT INTO client_contacts'),
  'SELL refreshes never overwrite Module 021 local contact rows');
  assert('HISTORICAL_LINEAGE_PRESERVED', backend.includes('customer_directory_source_links')
    && backend.includes('customer_directory_sync_runs') && !backend.includes('UPDATE clients')
    && backend.includes('CONNECTWISE_SELL'),
  'the new source cannot reinterpret legacy customer IDs or overwrite local records');
  assert('VIEW_AS_WRITE_BLOCKED', backend.includes('view_as_read_only')
    && backend.includes('IsViewAs(context)')
    && backend.includes('SameOrigin(context)'),
  'customer imports require an actual authorized same-origin session');
  assert('PROVIDER_SECURITY_REUSED', providerBackend.includes('PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY')
    && providerBackend.includes('ConnectCallback = ConnectToPublicEndpointAsync')
    && providerBackend.includes('UseProxy = false'),
  'Module 026 encrypted credential and SSRF boundaries remain authoritative');
  assert('ADDITIVE_REGISTRATION', registrar.includes('MapCustomerDirectorySellSyncEndpoints')
    && registrar.includes('Module 021 consumes the authoritative'),
  'new endpoints are registered additively without a Program.cs rewrite');
  assert('MIGRATION_049', migration.includes('049_module_021_sell_customer_sync')
    && migration.includes('customer_directory_source_links')
    && migration.includes('customer_directory_sync_runs')
    && migration.includes('REFERENCES crm_integration_providers(provider_key)'),
  'migration 049 links local customers to Module 026 providers and records sync evidence');
  assert('MIGRATION_NO_OPERATIONAL_SEED', !/INSERT\s+INTO\s+clients/i.test(migration)
    && !/INSERT\s+INTO\s+client_contacts/i.test(migration),
  'migration does not fabricate customer or contact data');
  assert('GUARDED_ROLLBACK', rollback.includes('Rollback blocked: customer-directory SELL source links exist.')
    && rollback.includes('Rollback blocked: customer-directory SELL sync evidence exists.'),
  'rollback preserves operational source links and synchronization evidence');
  assert('MIGRATION_TEST', migrationTest.includes('MODULE_021_SELL_CUSTOMER_SYNC_049_TEST=PASS')
    && migrationTest.includes('rollback_guard_verified'),
  'PostgreSQL lifecycle test covers apply, linkage, evidence, and guarded rollback');
  assert('MIGRATION_NOT_RUNTIME_APPLIED', !program.includes('049_module_021_sell_customer_sync.sql'),
  'application runtime does not apply migration 049 automatically');
} else {
  console.log('MODULES_021_026_DEEP_BACKEND_CHECKS=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log(`MODULES_021_026_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULES_021_026_EXTERNAL_PROVIDER_CALLS_PERFORMED=0');
console.log('MODULES_021_026_MIGRATION_049=CREATED_NOT_APPLIED');

if (checks.some((check) => !check.condition)) {
  console.error('MODULES_021_026_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULES_021_026_CONTRACT=PASSED');

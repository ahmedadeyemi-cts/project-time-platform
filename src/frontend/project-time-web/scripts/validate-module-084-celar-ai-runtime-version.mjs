import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (value) => fs.readFileSync(path.join(repoRoot, value), 'utf8');
const checks = [];

function check(name, condition) {
  checks.push([name, Boolean(condition)]);
  if (!condition) throw new Error(`${name}=FAILED`);
  console.log(`${name}=PASS`);
}

const injector = read('src/frontend/project-time-web/scripts/inject-module-084-celar-ai-runtime-version.mjs');
const buildBackup = read('src/frontend/project-time-web/scripts/backup-celar-ai-production-sources.mjs');
const buildRestore = read('src/frontend/project-time-web/scripts/restore-celar-ai-production-sources.mjs');
const buildProps = read('src/backend/ProjectTime.Api/Directory.Build.props');
const backend = read('src/backend/ProjectTime.Api/Modules/CelarAiRuntimeVersionModule.cs');
const component = read('src/frontend/project-time-web/src/CelarAiRuntimeVersionCenter.jsx');
const release = JSON.parse(read('deployment/oracle-celar/release.json'));
const timer = read('deployment/oracle-celar/systemd/celar-ollama-update.timer');
const gateway = read('deployment/oracle-celar/gateway/maintenance_gateway.py');
const reconcile = read('deployment/oracle-celar/maintenance-reconcile.sh');
const backup = read('deployment/oracle-celar/backup.sh');
const protectedTestController = read('.github/workflows/projectpulse-deploy-test.yml');

check('MODULE_084_BROWSER_IMPORT_INJECTED', injector.includes("import CelarAiRuntimeVersionCenter from './CelarAiRuntimeVersionCenter.jsx';"));
check('MODULE_084_BROWSER_ROUTE_INJECTED', injector.includes("activeRoute === 'celar-ai-runtime-version'") && injector.includes('<CelarAiRuntimeVersionCenter />'));
check('MODULE_084_ADMIN_NAV_INJECTED', injector.includes("navLabel: 'MODULE 084'") && injector.includes("permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']"));
check('MODULE_084_STATIC_REGISTRY_INJECTED', injector.includes("moduleNumber: '084'") && injector.includes("route: 'celar-ai-runtime-version'"));
check('MODULE_084_BROWSER_BUILD_TRANSACTION',
  buildBackup.includes("'App.jsx'")
  && buildBackup.includes("'module-availability-registry.js'")
  && buildBackup.includes("inject-module-084-celar-ai-runtime-version.mjs")
  && buildRestore.includes("'App.jsx'")
  && buildRestore.includes("'module-availability-registry.js'"));

check('MODULE_084_BACKEND_ROUTES', backend.includes('/api/celar-ai/v1/runtime-version/status') && backend.includes('/api/celar-ai/v1/runtime-version/schedule'));
check('MODULE_084_GENERATED_ENDPOINT_MAP',
  buildProps.includes('RegisterModule084CelarRuntimeVersionEndpoints')
  && buildProps.includes('app.MapCelarAiRuntimeVersionEndpoints();')
  && buildProps.includes('$(ScopedRbacGeneratedProgram)'));
check('MODULE_084_GENERATED_AVAILABILITY',
  buildProps.includes('RegisterModule084Availability')
  && buildProps.includes('celar-ai-runtime-version')
  && buildProps.includes('$(ModuleAvailabilityResilienceGenerated)'));
check('MODULE_084_ACTUAL_ADMIN_AUTHORITY', backend.includes('AdminExperienceCommon.AuthorizeAsync(context)') && backend.includes('AdminExperienceCommon.IsViewAs(context)'));
check('MODULE_084_VIEW_AS_FAIL_CLOSED', backend.includes('status = "view_as_read_only"') && backend.includes('mutationAuthorityTransferred = false'));
check('MODULE_084_TEST_ONLY_RUNTIME_POLICY', backend.includes('PulseAiExternalHttpsRuntimePolicy.Evaluate()') && backend.includes('productionMutationAllowed = false'));
check('MODULE_084_DEDICATED_MAINTENANCE_TOKEN', backend.includes('PROJECTPULSE_CELAR_AI_MAINTENANCE_BEARER_TOKEN') && backend.includes('PROJECTPULSE_CELAR_AI_MAINTENANCE_BEARER_TOKEN_SECRET_REFERENCE'));
check('MODULE_084_PROVIDER_ORDER_SEPARATION', backend.includes('providerOrderOwnedByModule064 = true') && backend.includes('providerOrderChanged = false'));
check('MODULE_084_AUDIT_EVIDENCE', backend.includes('celar_runtime_maintenance_schedule_changed') && backend.includes('secretValuesRecorded = false'));
check('MODULE_084_SSRF_GUARD', backend.includes('AddressesApproved(snapshot, addresses)') && backend.includes('AllowAutoRedirect = false') && backend.includes('UseProxy = false'));

check('MODULE_084_UI_ENGINE_VERSION', component.includes('Ollama engine') && component.includes('engineVersion'));
check('MODULE_084_UI_MODEL_DIGESTS', component.includes('Artifact digest') && component.includes('model.digest'));
check('MODULE_084_UI_UPDATE_HISTORY', component.includes('Last update result') && component.includes('lastSuccessfulUpdateAt') && component.includes('rollbackAvailable'));
check('MODULE_084_UI_CENTRAL_AND_BROWSER_TIME', component.includes('Next update · Central') && component.includes('Next update · your browser') && component.includes("const CENTRAL_ZONE = 'America/Chicago'"));
check('MODULE_084_UI_SCHEDULE_CONTROL', component.includes('Save maintenance window') && component.includes('type="time"') && component.includes('scheduleMutationConfigured'));
check('MODULE_084_UI_PROVIDER_ORDER_UNCHANGED', component.includes('DeepSeek v4 → Celar AI → Claude → OpenAI → governed local template'));

check('MODULE_084_DEFAULT_1AM_CENTRAL', release.modelMaintenance?.dayOfWeek === 'Sunday'
  && release.modelMaintenance?.localTime === '01:00'
  && release.modelMaintenance?.timeZone === 'America/Chicago'
  && timer.includes('OnCalendar=Sun *-*-* 01:00:00 America/Chicago')
  && timer.includes('RandomizedDelaySec=0'));
check('MODULE_084_APPROVED_PORTFOLIO', JSON.stringify(release.localGenerationModels) === JSON.stringify(['gemma3:4b', 'qwen3:4b-instruct', 'llama3.2:3b']) && release.embeddingModel === 'embeddinggemma');
check('MODULE_084_SPECIALIST_ORDER_PRESERVED',
  JSON.stringify(release.structuredGenerationOrder) === JSON.stringify(['gemma3:4b', 'qwen3:4b-instruct', 'llama3.2:3b'])
  && JSON.stringify(release.generalGenerationOrder) === JSON.stringify(['qwen3:4b-instruct', 'llama3.2:3b', 'gemma3:4b']));
check('MODULE_084_DNS_MANAGED_RUNTIME_PRESERVED',
  protectedTestController.includes('PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ADDRESS_MODE=dns')
  && !/^\s*ORACLE_RUNTIME_IP:\s*\d{1,3}(?:\.\d{1,3}){3}\s*$/m.test(protectedTestController));
check('MODULE_084_CLOSED_SCHEDULE_SCHEMA', gateway.includes('time_zone != "America/Chicago"') && gateway.includes('ALLOWED_DAYS') && gateway.includes('TIME_PATTERN'));
check('MODULE_084_UNPRIVILEGED_GATEWAY', gateway.includes('runtimeTokenMayChangeSchedule": False') && !/shell\s*=\s*True|os\.system|sudo/.test(gateway));
check('MODULE_084_ROOT_RECONCILER', reconcile.includes('systemctl enable celar-ollama-update.timer') && reconcile.includes('systemctl disable --now celar-ollama-update.timer') && reconcile.includes('.timeZone == "America/Chicago"'));
check('MODULE_084_DYNAMIC_STATE_BACKED_UP', backup.includes('add_path /var/lib/celar-ai'));
check('MODULE_084_MAINTENANCE_TOKEN_EXCLUDED_FROM_BACKUP', backup.includes("'etc/celar-ai/gateway/maintenance-token'") && backup.includes('maintenance-token'));

console.log(`MODULE_084_VALIDATION=PASS checks=${checks.length}`);

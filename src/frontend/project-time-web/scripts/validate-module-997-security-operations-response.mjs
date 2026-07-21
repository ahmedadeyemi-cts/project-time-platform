import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..');
const paths = {
  backend: 'src/backend/ProjectTime.Api/Modules/SecurityOperationsResponseModule.cs',
  shared: 'src/backend/ProjectTime.Api/Modules/SecurityDiagnosticsOperations.cs',
  frontend: 'src/frontend/project-time-web/src/SecurityOperationsResponseCenter.jsx',
  css: 'src/frontend/project-time-web/src/security-operations-response-center.css',
  migration: 'database/migrations/033_security_diagnostics_native_operations.sql',
  rollback: 'database/rollback/033_security_diagnostics_native_operations_rollback.sql',
  program: 'src/backend/ProjectTime.Api/Program.cs', app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json', docker: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-997-security-operations-response/README.md',
  api: 'docs/modules/module-997-security-operations-response/API-CONTRACT.md',
  authorization: 'docs/modules/module-997-security-operations-response/AUTHORIZATION-MATRIX.md',
  lifecycle: 'docs/modules/module-997-security-operations-response/INCIDENT-STATE-MACHINE.md',
  security: 'docs/modules/module-997-security-operations-response/SECURITY-BOUNDARY.md',
  integration: 'docs/modules/module-997-security-operations-response/INTEGRATION-BOUNDARY.md',
  catalog: 'docs/MODULE-CATALOG.md', register: 'docs/MODULE-WORK-REGISTER.md',
  tracker: 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'
};

const checks = [];
const read = (key) => {
  const file = path.join(root, paths[key]); const exists = fs.existsSync(file);
  check(`MODULE_997_${key.toUpperCase()}_EXISTS`, exists, paths[key]);
  return exists ? fs.readFileSync(file, 'utf8') : '';
};
function check(name, pass, detail = '') { checks.push({ name, pass, detail }); console.log(`${name}=${pass ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`); }
function count(source, marker) { return source.split(marker).length - 1; }

const backend = read('backend'); const shared = read('shared'); const frontend = read('frontend'); const css = read('css');
const migration = read('migration'); const rollback = read('rollback'); const program = read('program'); const app = read('app');
const packageJson = read('package'); const docker = read('docker'); const readme = read('readme'); const api = read('api');
const authorization = read('authorization'); const lifecycle = read('lifecycle'); const security = read('security'); const integration = read('integration');
const catalog = read('catalog'); const register = read('register'); const tracker = read('tracker');

check('MODULE_997_MAP_METHOD', backend.includes('MapSecurityOperationsResponseEndpoints'));
for (const route of [
  '/api/security-operations/overview','/api/security-operations/alerts','/api/security-operations/sessions',
  '/api/security-operations/incidents','/api/security-operations/incidents/{incidentId:guid}',
  '/api/security-operations/threat-intelligence','/api/security-operations/control-posture',
  '/api/security-operations/response-policy','/api/security-operations/reporting-policy','/api/security-operations/integration-policy',
  '/api/security-operations/incidents/declare','/api/security-operations/incidents/acknowledge',
  '/api/security-operations/response/contain','/api/security-operations/response/approve','/api/security-operations/response/execute',
  '/api/security-operations/response/eradicate','/api/security-operations/response/recover','/api/security-operations/case/close'
]) check(`MODULE_997_ROUTE_${route.replaceAll(/[^a-z0-9]/gi, '_').toUpperCase()}`, backend.includes(`"${route}"`), route);

check('MODULE_997_ACTUAL_SESSION_AUTHORITY', ['ProjectPulseActualUserId','ProjectPulseSessionUserId'].every((x) => shared.includes(x)) && !shared.includes('ProjectPulseEffectiveUserId'));
check('MODULE_997_SERVER_AUTHORIZATION', ['VIEW_SECURITY_OPERATIONS','MANAGE_SECURITY_RESPONSE','SECURITY_ANALYST','SECURITY_INCIDENT_COMMANDER','MANAGE_ALL'].every((x) => `${backend}\n${shared}`.includes(x)));
check('MODULE_997_VIEW_AS_WRITE_BLOCK', shared.includes('view_as_write_blocked') && shared.includes('RequireMutation') && frontend.includes('View-As is active'));
check('MODULE_997_BOUNDED_JSON', shared.includes('MaximumRequestBytes') && shared.includes('Status413PayloadTooLarge') && backend.includes('ReadBodyAsync'));
check('MODULE_997_SANITIZED_FAILURES', shared.includes('exception.GetType().Name') && !/(?:exception|ex)\.Message/i.test(`${backend}\n${shared}`));

for (const table of ['projectpulse_security_alerts','projectpulse_security_incidents','projectpulse_security_incident_events','projectpulse_security_response_requests'])
  check(`MODULE_997_TABLE_${table.toUpperCase()}`, migration.includes(`CREATE TABLE IF NOT EXISTS ${table}`));
check('MODULE_997_DUAL_APPROVAL_SCHEMA', migration.includes('approved_by <> requested_by') && backend.includes('requested_by <> @actor'));
check('MODULE_997_APPEND_ONLY_TIMELINE', backend.includes('InsertIncidentEventAsync') && backend.includes('projectpulse_security_incident_events'));
check('MODULE_997_AUDIT_EVIDENCE', backend.includes('SecurityDiagnosticsOperations.WriteAuditAsync') && migration.includes("'997'"));
check('MODULE_997_ROLLBACK', rollback.includes('DROP TABLE IF EXISTS projectpulse_security_incidents') && rollback.includes('033_security_diagnostics_native_operations'));

check('MODULE_997_LIVE_NATIVE_TELEMETRY', ['auth_login_events','auth_sessions','audit_logs','projectpulse_module_audit_events','ReadAuthenticationSignalsAsync'].every((x) => backend.includes(x)));
check('MODULE_997_STATUS_INTEGRITY', backend.includes('derived signals do not claim compromise') && backend.includes('missing external telemetry remains explicit'));
check('MODULE_997_NATIVE_SESSION_REVOCATION', backend.includes('PROJECTPULSE_SECURITY_NATIVE_SESSION_REVOCATION_ENABLED') && backend.includes('UPDATE auth_sessions') && backend.includes("action != \"revoke_session\""));
check('MODULE_997_EXTERNAL_ACTIONS_GATED', backend.includes('StatusCodes.Status423Locked') && backend.includes('adapter_required') && ['suspend_user','restrict_role','quarantine_integration','block_indicator'].every((x) => backend.includes(x)));
check('MODULE_997_NO_EXTERNAL_CONNECTOR', !/(?:HttpClient|TcpClient|UdpClient|Process\.Start|GraphServiceClient|OpenAIClient|SmtpClient)/.test(backend));
check('MODULE_997_INCIDENT_LIFECYCLE', ['detect','triage','declare','contain','eradicate','recover','review','close'].every((x) => backend.includes(`code = "${x}"`)));
check('MODULE_997_998_HANDOFF', frontend.includes("'/api/system-diagnostics/sessions'") && backend.includes('diagnosticHandoff'));

check('MODULE_997_FRONTEND_MARKERS', frontend.includes('data-module="997"') && frontend.includes('data-execution-mode="governed-native"') && frontend.includes('data-contract-version'));
check('MODULE_997_INDEPENDENT_LOADING', frontend.includes('Promise.allSettled') && frontend.includes('Some security surfaces are unavailable'));
for (const route of Object.values({ overview:SURFACE('/overview'), alerts:SURFACE('/alerts'), sessions:SURFACE('/sessions'), incidents:SURFACE('/incidents') }))
  check(`MODULE_997_FRONTEND_${route.replaceAll(/[^a-z0-9]/gi, '_').toUpperCase()}`, frontend.includes(`'${route}'`), route);
check('MODULE_997_FRONTEND_MUTATIONS', frontend.includes("method: 'POST'") && ['Declare incident','Run diagnostics','Prepare containment','Approve as separate actor','Execute approved action'].every((x) => frontend.includes(x)));
check('MODULE_997_EXTERNAL_CONTROLS_VISIBLE', count(frontend, '<button type="button" disabled>') >= 6 && ['Suspend Entra user','Block WAF indicator','Isolate endpoint'].every((x) => frontend.includes(x)));
check('MODULE_997_US_SIGNAL_BRAND', frontend.includes('usSignalLogoDataUrl') && frontend.includes('alt="US Signal"') && css.includes('--security-blue: #005baa') && css.includes('--security-navy: #002f5d'));
check('MODULE_997_SCOPED_STYLES', css.includes('.security-operations-center') && !/(^|\n)\s*(?:html|body|:root|#root)\s*[{,]/m.test(css));

check('MODULE_997_PROGRAM_REGISTRATION', count(program, 'app.MapSecurityOperationsResponseEndpoints();') === 1);
check('MODULE_997_APP_INTEGRATION', app.includes("route: 'security-operations'") && app.includes('VIEW_SECURITY_OPERATIONS') && app.includes('MANAGE_SECURITY_RESPONSE'));
const scripts = JSON.parse(packageJson || '{}').scripts ?? {};
check('MODULE_997_BUILD_GUARD', scripts.prebuild?.includes('validate:module997') && scripts['validate:module997']?.includes('validate-module-997-security-operations-response.mjs'));
check('MODULE_997_CONTAINER_CONTEXT', [paths.backend, paths.shared, 'docs/modules/module-997-security-operations-response/'].every((x) => docker.includes(x)));
check('MODULE_997_DOCUMENTATION', readme.includes('Operational activation') && api.includes('response/approve') && authorization.includes('separation of duties') && lifecycle.includes('diagnostic') && security.includes('session revocation') && integration.includes('Module 998'));
check('MODULE_997_GOVERNANCE', catalog.includes('| 997 |') && register.includes('| 997 |') && tracker.includes('Module 997'));

const failed = checks.filter((item) => !item.pass);
console.log(`\nMODULE_997_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_997_PHASE=NATIVE_SECURITY_OPERATIONS');
console.log('MODULE_997_EXTERNAL_CONTAINMENT=ADAPTER_GATED');
console.log(`MODULE_997_CONTRACT=${failed.length ? 'FAILED' : 'PASSED'}`);
if (failed.length) { failed.forEach((item) => console.error(`- ${item.name}: ${item.detail}`)); process.exit(1); }

function SURFACE(suffix) { return `/api/security-operations${suffix}`; }

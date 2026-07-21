import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..');
const paths = {
  backend: 'src/backend/ProjectTime.Api/Modules/SystemDiagnosticRemediationModule.cs',
  shared: 'src/backend/ProjectTime.Api/Modules/SecurityDiagnosticsOperations.cs',
  frontend: 'src/frontend/project-time-web/src/SystemDiagnosticRemediationCenter.jsx',
  css: 'src/frontend/project-time-web/src/system-diagnostic-remediation-center.css',
  migration: 'database/migrations/033_security_diagnostics_native_operations.sql',
  rollback: 'database/rollback/033_security_diagnostics_native_operations_rollback.sql',
  program: 'src/backend/ProjectTime.Api/Program.cs', app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json', docker: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-998-system-diagnostic-remediation/README.md',
  api: 'docs/modules/module-998-system-diagnostic-remediation/API-CONTRACT.md',
  authorization: 'docs/modules/module-998-system-diagnostic-remediation/AUTHORIZATION-MATRIX.md',
  lifecycle: 'docs/modules/module-998-system-diagnostic-remediation/REMEDIATION-STATE-MACHINE.md',
  security: 'docs/modules/module-998-system-diagnostic-remediation/SECURITY-AND-OPERATIONS.md',
  evidence: 'docs/modules/module-998-system-diagnostic-remediation/EVIDENCE-AND-REDACTION.md',
  overlap: 'docs/modules/module-998-system-diagnostic-remediation/OVERLAP-AND-INTEGRATION.md',
  catalog: 'docs/MODULE-CATALOG.md', register: 'docs/MODULE-WORK-REGISTER.md', tracker: 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'
};
const checks = [];
function check(name, pass, detail='') { checks.push({name,pass,detail}); console.log(`${name}=${pass?'PASSED':'FAILED'}${detail?` — ${detail}`:''}`); }
function read(key) { const full=path.join(root,paths[key]); const exists=fs.existsSync(full); check(`MODULE_998_${key.toUpperCase()}_EXISTS`,exists,paths[key]); return exists?fs.readFileSync(full,'utf8'):''; }
function count(source,marker){return source.split(marker).length-1;}

const backend=read('backend'), shared=read('shared'), frontend=read('frontend'), css=read('css'), migration=read('migration'), rollback=read('rollback');
const program=read('program'), app=read('app'), packageJson=read('package'), docker=read('docker'), readme=read('readme'), api=read('api');
const authorization=read('authorization'), lifecycle=read('lifecycle'), security=read('security'), evidence=read('evidence'), overlap=read('overlap');
const catalog=read('catalog'), register=read('register'), tracker=read('tracker');

check('MODULE_998_MAP_METHOD',backend.includes('MapSystemDiagnosticRemediationEndpoints'));
for(const route of [
  '/api/system-diagnostics/overview','/api/system-diagnostics/checks','/api/system-diagnostics/issues',
  '/api/system-diagnostics/sessions','/api/system-diagnostics/sessions/{sessionId:guid}',
  '/api/system-diagnostics/evidence-policy','/api/system-diagnostics/remediation-policy','/api/system-diagnostics/runbooks','/api/system-diagnostics/remediations',
  '/api/system-diagnostics/remediation/prepare','/api/system-diagnostics/remediation/approve','/api/system-diagnostics/remediation/stage',
  '/api/system-diagnostics/remediation/promote','/api/system-diagnostics/remediation/verify','/api/system-diagnostics/remediation/rollback','/api/system-diagnostics/remediation/close'
]) check(`MODULE_998_ROUTE_${route.replaceAll(/[^a-z0-9]/gi,'_').toUpperCase()}`,backend.includes(`"${route}"`),route);

check('MODULE_998_ACTUAL_SESSION_AUTHORITY',['ProjectPulseActualUserId','ProjectPulseSessionUserId'].every((x)=>shared.includes(x))&&!shared.includes('ProjectPulseEffectiveUserId'));
check('MODULE_998_SERVER_AUTHORIZATION',['VIEW_SYSTEM_DIAGNOSTICS','MANAGE_SYSTEM_REMEDIATION','MANAGE_ALL'].every((x)=>`${backend}\n${shared}`.includes(x)));
check('MODULE_998_VIEW_AS_WRITE_BLOCK',shared.includes('view_as_write_blocked')&&backend.includes('RequireMutation'));
check('MODULE_998_BOUNDED_JSON',shared.includes('MaximumRequestBytes')&&backend.includes('ReadBodyAsync'));
check('MODULE_998_SANITIZED_FAILURES',shared.includes('exception.GetType().Name')&&!/(?:exception|ex)\.Message/i.test(`${backend}\n${shared}`));

for(const table of ['projectpulse_diagnostic_sessions','projectpulse_diagnostic_findings','projectpulse_remediation_requests']) check(`MODULE_998_TABLE_${table.toUpperCase()}`,migration.includes(`CREATE TABLE IF NOT EXISTS ${table}`));
check('MODULE_998_FINDING_INTEGRITY',migration.includes('ux_projectpulse_diagnostic_finding')&&migration.includes("status IN ('healthy','warning','failed','unknown','not_applicable')"));
check('MODULE_998_DUAL_APPROVAL_SCHEMA',migration.includes('approved_by <> requested_by')&&backend.includes('requested_by <> @actor'));
check('MODULE_998_AUDIT_EVIDENCE',backend.includes('SecurityDiagnosticsOperations.WriteAuditAsync')&&migration.includes("'998'"));
check('MODULE_998_ROLLBACK',rollback.includes('DROP TABLE IF EXISTS projectpulse_diagnostic_sessions')&&rollback.includes('033_security_diagnostics_native_operations'));

check('MODULE_998_NATIVE_CHECKS',['database_connectivity','authentication_failures','security_incident_queue','containment_approval_queue','schema_migration_recency','external_infrastructure'].every((x)=>backend.includes(x)));
check('MODULE_998_DIAGNOSTIC_PERSISTENCE',backend.includes('PersistFindingsAsync')&&backend.includes('ReplaceFindingsAsync')&&backend.includes('projectpulse_diagnostic_sessions'));
check('MODULE_998_997_HANDOFF',backend.includes('diagnostic_session_created')&&backend.includes('projectpulse_security_incidents')&&frontend.includes('Incident ID (optional)'));
check('MODULE_998_REMEDIATION_LIFECYCLE',['prepare','approve','stage','promote','verify','rollback','close'].every((x)=>backend.includes(`code = "${x}"`)));
check('MODULE_998_NATIVE_EXECUTION',backend.includes('refresh_health_snapshot')&&backend.includes('native_health_refresh_executed')&&backend.includes('VerifyRemediationAsync'));
check('MODULE_998_EXTERNAL_ACTIONS_GATED',backend.includes('StatusCodes.Status423Locked')&&backend.includes('execution_adapter_required')&&['restart_service','scale_service','rollback_deployment','replay_integration_event','refresh_configuration','database_repair'].every((x)=>backend.includes(x)));
check('MODULE_998_STATUS_INTEGRITY',backend.includes('external_infrastructure')&&backend.includes('"unknown"')&&backend.includes('adapter = "azure_diagnostics"'));
check('MODULE_998_EVIDENCE_BOUNDARY',backend.includes('rawLogAccessEnabled = false')&&backend.includes('secretAccessEnabled = false')&&backend.includes('connectionStringAccessEnabled = false'));
check('MODULE_998_NO_EXTERNAL_CONNECTOR',!/(?:HttpClient|TcpClient|UdpClient|Process\.Start|GraphServiceClient|OpenAIClient|SmtpClient)/.test(backend));

check('MODULE_998_FRONTEND_MARKERS',frontend.includes('data-module="998"')&&frontend.includes('data-execution-mode="governed-native"')&&frontend.includes('data-contract-version'));
check('MODULE_998_INDEPENDENT_LOADING',frontend.includes('Promise.allSettled')&&frontend.includes('Some diagnostic surfaces are unavailable'));
for(const route of ['/api/system-diagnostics/overview','/api/system-diagnostics/checks','/api/system-diagnostics/issues','/api/system-diagnostics/sessions','/api/system-diagnostics/runbooks','/api/system-diagnostics/remediations']) check(`MODULE_998_FRONTEND_${route.replaceAll(/[^a-z0-9]/gi,'_').toUpperCase()}`,frontend.includes(`'${route}'`),route);
check('MODULE_998_FRONTEND_MUTATIONS',frontend.includes("method: 'POST'")&&['Run diagnostics','Prepare remediation','Approve as separate actor','Stage','Execute','Verify','Close'].every((x)=>frontend.includes(x)));
check('MODULE_998_ADAPTER_CONTROLS_VISIBLE',count(frontend,'<button type="button" disabled>')>=8&&['Restart service','Scale service','Rollback deployment','Replay integration event','Run database repair'].every((x)=>frontend.includes(x)));
check('MODULE_998_US_SIGNAL_BRAND',frontend.includes('usSignalLogoDataUrl')&&frontend.includes('alt="US Signal"')&&css.includes('--diagnostic-blue: #005baa')&&css.includes('--diagnostic-navy: #002f5d'));
check('MODULE_998_SCOPED_STYLES',css.includes('.system-diagnostic-center')&&!/(^|\n)\s*(?:html|body|:root|#root)\s*[{,]/m.test(css));

check('MODULE_998_PROGRAM_REGISTRATION',count(program,'app.MapSystemDiagnosticRemediationEndpoints();')===1);
check('MODULE_998_APP_INTEGRATION',app.includes("route: 'system-diagnostics'")&&app.includes('VIEW_SYSTEM_DIAGNOSTICS')&&app.includes('MANAGE_SYSTEM_REMEDIATION'));
const scripts=JSON.parse(packageJson||'{}').scripts??{};
check('MODULE_998_BUILD_GUARD',scripts.build?.includes('validate:module998')&&scripts['validate:module998']?.includes('validate-module-998-system-diagnostic-remediation.mjs'));
check('MODULE_998_CONTAINER_CONTEXT',[paths.backend,paths.shared,'docs/modules/module-998-system-diagnostic-remediation/'].every((x)=>docker.includes(x)));
check('MODULE_998_DOCUMENTATION',readme.includes('Operational activation')&&api.includes('/sessions')&&authorization.includes('separation of duties')&&lifecycle.includes('refresh_health_snapshot')&&security.includes('adapter')&&evidence.includes('sanitized findings')&&overlap.includes('Module 997'));
check('MODULE_998_GOVERNANCE',catalog.includes('| 998 |')&&register.includes('| 998 |')&&tracker.includes('Module 998'));

const failed=checks.filter((x)=>!x.pass);
console.log(`\nMODULE_998_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_998_PHASE=NATIVE_DIAGNOSTIC_OPERATIONS');
console.log('MODULE_998_PRODUCTION_ACTIONS=ADAPTER_GATED');
console.log(`MODULE_998_CONTRACT=${failed.length?'FAILED':'PASSED'}`);
if(failed.length){failed.forEach((x)=>console.error(`- ${x.name}: ${x.detail}`));process.exit(1);}

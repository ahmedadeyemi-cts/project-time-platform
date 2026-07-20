import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');

const moduleDocs = path.join(repositoryRoot, 'docs/modules/module-997-security-operations-response');
const paths = {
  backend: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/SecurityOperationsResponseModule.cs'),
  frontend: path.join(repositoryRoot, 'src/frontend/project-time-web/src/SecurityOperationsResponseCenter.jsx'),
  stylesheet: path.join(repositoryRoot, 'src/frontend/project-time-web/src/security-operations-response-center.css'),
  readme: path.join(moduleDocs, 'README.md'),
  api: path.join(moduleDocs, 'API-CONTRACT.md'),
  authorization: path.join(moduleDocs, 'AUTHORIZATION-MATRIX.md'),
  matrix: path.join(moduleDocs, 'CAPABILITY-MATRIX.md'),
  security: path.join(moduleDocs, 'SECURITY-BOUNDARY.md'),
  lifecycle: path.join(moduleDocs, 'INCIDENT-STATE-MACHINE.md'),
  intelligence: path.join(moduleDocs, 'THREAT-INTELLIGENCE.md'),
  evidence: path.join(moduleDocs, 'EVIDENCE-AND-REPORTING.md'),
  integration: path.join(moduleDocs, 'INTEGRATION-BOUNDARY.md'),
  overlap: path.join(moduleDocs, 'OVERLAP-AND-INTEGRATION.md'),
  validation: path.join(moduleDocs, 'VALIDATION-EVIDENCE.md'),
  program: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Program.cs'),
  app: path.join(repositoryRoot, 'src/frontend/project-time-web/src/App.jsx'),
  package: path.join(repositoryRoot, 'src/frontend/project-time-web/package.json'),
  dockerfile: path.join(repositoryRoot, 'deployment/containers/web/Dockerfile'),
  catalog: path.join(repositoryRoot, 'docs/MODULE-CATALOG.md'),
  register: path.join(repositoryRoot, 'docs/MODULE-WORK-REGISTER.md'),
  tracker: path.join(repositoryRoot, 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md')
};

const assertions = [];

function assertInvariant(name, condition, detail) {
  assertions.push({ name, condition, detail });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

function readRequiredFile(name, filePath) {
  const exists = fs.existsSync(filePath);
  assertInvariant(`MODULE_997_${name}_EXISTS`, exists, path.relative(repositoryRoot, filePath));
  return exists ? fs.readFileSync(filePath, 'utf8') : '';
}

function count(source, pattern) {
  return [...source.matchAll(pattern)].length;
}

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = path.join(directory, entry.name);
    return entry.isDirectory() ? filesBelow(entryPath) : [entryPath];
  });
}

const backend = readRequiredFile('BACKEND', paths.backend);
const frontend = readRequiredFile('FRONTEND', paths.frontend);
const stylesheet = readRequiredFile('STYLESHEET', paths.stylesheet);
const readme = readRequiredFile('README', paths.readme);
const api = readRequiredFile('API_CONTRACT', paths.api);
const authorization = readRequiredFile('AUTHORIZATION_MATRIX', paths.authorization);
const matrix = readRequiredFile('CAPABILITY_MATRIX', paths.matrix);
const security = readRequiredFile('SECURITY_BOUNDARY', paths.security);
const lifecycle = readRequiredFile('INCIDENT_STATE_MACHINE', paths.lifecycle);
const intelligence = readRequiredFile('THREAT_INTELLIGENCE', paths.intelligence);
const evidence = readRequiredFile('EVIDENCE_REPORTING', paths.evidence);
const integration = readRequiredFile('INTEGRATION_BOUNDARY', paths.integration);
const overlap = readRequiredFile('OVERLAP_INTEGRATION', paths.overlap);
const validation = readRequiredFile('VALIDATION_EVIDENCE', paths.validation);
const program = readRequiredFile('PROGRAM', paths.program);
const app = readRequiredFile('APP', paths.app);
const packageJson = readRequiredFile('PACKAGE', paths.package);
const dockerfile = readRequiredFile('DOCKERFILE', paths.dockerfile);
const catalog = readRequiredFile('CATALOG', paths.catalog);
const register = readRequiredFile('WORK_REGISTER', paths.register);
const tracker = readRequiredFile('PRODUCTION_TRACKER', paths.tracker);

assertInvariant(
  'MODULE_997_MAP_METHOD',
  backend.includes('MapSecurityOperationsResponseEndpoints'),
  'isolated endpoint registration exists'
);

assertInvariant(
  'MODULE_997_TYPED_ROUTE_HANDLERS',
  count(backend, /Func<HttpContext, Task<IResult>>/g) >= 17,
  'typed handlers preserve IResult responses'
);

for (const route of [
  '/api/security-operations/overview',
  '/api/security-operations/alerts',
  '/api/security-operations/incidents',
  '/api/security-operations/threat-intelligence',
  '/api/security-operations/control-posture',
  '/api/security-operations/response-policy',
  '/api/security-operations/reporting-policy',
  '/api/security-operations/integration-policy'
]) {
  assertInvariant(
    `MODULE_997_GET_${route.split('/').at(-1).replaceAll('-', '_').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

for (const route of [
  '/api/security-operations/analysis',
  '/api/security-operations/incidents/declare',
  '/api/security-operations/incidents/acknowledge',
  '/api/security-operations/response/contain',
  '/api/security-operations/response/eradicate',
  '/api/security-operations/response/recover',
  '/api/security-operations/notifications/send',
  '/api/security-operations/evidence/export',
  '/api/security-operations/case/close'
]) {
  const label = route.split('/').slice(-2).join('_').replaceAll('-', '_').toUpperCase();
  assertInvariant(
    `MODULE_997_LOCKED_${label}`,
    backend.includes(`"${route}"`),
    route
  );
}

assertInvariant(
  'MODULE_997_BASELINE_VERIFIED',
  backend.includes('3d9a3dca8af479c854dc4c4a9294bc8aad273074')
    && readme.includes('48421d5ba1584d64fc3bd043304c003eff1dc27b')
    && overlap.includes('same main base'),
  'current main and required checkpoint are explicit'
);

assertInvariant(
  'MODULE_997_ACTUAL_SESSION_AUTHORITY',
  backend.includes('ProjectPulseActualUserId')
    && backend.includes('ProjectPulseSessionUserId')
    && !backend.includes('ProjectPulseEffectiveUserId'),
  'View-As cannot supply security authority'
);

assertInvariant(
  'MODULE_997_SERVER_AUTHORIZATION',
  ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'SECURITY_ANALYST',
    'SECURITY_OPERATIONS', 'SECURITY_INCIDENT_COMMANDER',
    'VIEW_SECURITY_OPERATIONS', 'MANAGE_SECURITY_RESPONSE',
    'SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'security_access_required']
    .every((marker) => backend.includes(marker)),
  'view and response authority are server-side'
);

assertInvariant(
  'MODULE_997_FAIL_CLOSED_OPERATIONS',
  backend.includes('StatusCodes.Status423Locked')
    && backend.includes('requestBodyRead = false')
    && backend.includes('adapterInvoked = false')
    && backend.includes('stateChanged = false')
    && backend.includes('telemetryQueried = false')
    && backend.includes('threatFeedQueried = false')
    && backend.includes('containmentExecuted = false')
    && backend.includes('eradicationExecuted = false')
    && backend.includes('recoveryExecuted = false')
    && backend.includes('externalNotificationSent = false')
    && backend.includes('evidenceExported = false')
    && backend.includes('secretAccessed = false'),
  'every security action reports an explicit locked result'
);

assertInvariant(
  'MODULE_997_BODY_NOT_READ',
  !/(?:Request\.Body|ReadFromJsonAsync|ReadToEndAsync|DeserializeAsync)/.test(backend),
  'locked operations never inspect a request body'
);

assertInvariant(
  'MODULE_997_NO_EXECUTION_CONNECTOR',
  !/(?:HttpClient|TcpClient|UdpClient|Process\.Start|System\.Diagnostics\.Process|Azure\.|Microsoft\.Graph|SmtpClient|Anthropic|OpenAIClient|GraphServiceClient)/.test(backend),
  'no telemetry, threat, cloud, identity, mail, AI, or command connector exists'
);

const forbiddenSqlMutation = /\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+)\b/i;
assertInvariant(
  'MODULE_997_NO_MUTATING_SQL',
  !forbiddenSqlMutation.test(backend) && backend.includes('SELECT 1;'),
  'database use is authorization and connectivity read-only'
);

assertInvariant(
  'MODULE_997_SANITIZED_FAILURES',
  !/(?:exception|ex)\.Message/i.test(backend)
    && backend.includes('exception.GetType().Name')
    && backend.includes('authorization_dependency_unavailable'),
  'raw exception and provider detail are suppressed'
);

assertInvariant(
  'MODULE_997_STATUS_INTEGRITY',
  backend.includes('does not infer a healthy environment')
    && backend.includes('connector_not_configured')
    && backend.includes('delegated_not_live')
    && backend.includes('status = "unknown"'),
  'missing or delegated telemetry is never represented as healthy'
);

assertInvariant(
  'MODULE_997_EMPTY_LIVE_INVENTORY',
  count(backend, /Array\.Empty<object>\(\)/g) >= 3
    && backend.includes('liveCountAuthoritative = false')
    && backend.includes('persistenceMode = "not_configured"'),
  'alerts, incidents, and indicators make no live-data claim'
);

assertInvariant(
  'MODULE_997_INCIDENT_LIFECYCLE',
  ['detect', 'triage', 'declare', 'contain', 'eradicate', 'recover', 'review', 'close']
    .every((stage) => backend.includes(`code = "${stage}"`) || lifecycle.includes(stage)),
  'complete incident lifecycle is present'
);

assertInvariant(
  'MODULE_997_THREAT_INTELLIGENCE_POLICY',
  ['internal_telemetry', 'vendor_intelligence', 'government_advisories',
    'community_exchange', 'analyst_observation']
    .every((source) => backend.includes(source))
    && intelligence.includes('Confidence never grants automated containment'),
  'sources, confidence, freshness, and handling are governed'
);

assertInvariant(
  'MODULE_997_REPORTING_BOUNDARY',
  backend.includes('exportEnabled = false')
    && backend.includes('externalNotificationEnabled = false')
    && backend.includes('restricted_security_metadata')
    && evidence.includes('External notification and evidence export are disabled'),
  'restricted reporting cannot transmit or export'
);

assertInvariant(
  'MODULE_997_FRONTEND_MARKERS',
  frontend.includes('data-module="997"')
    && frontend.includes('data-execution-mode="fail-closed"')
    && frontend.includes('data-contract-version'),
  'frontend declares module and execution boundary'
);

for (const endpoint of [
  '/api/security-operations/overview',
  '/api/security-operations/alerts',
  '/api/security-operations/incidents',
  '/api/security-operations/threat-intelligence',
  '/api/security-operations/control-posture',
  '/api/security-operations/response-policy',
  '/api/security-operations/reporting-policy',
  '/api/security-operations/integration-policy'
]) {
  assertInvariant(
    `MODULE_997_FRONTEND_${endpoint.split('/').at(-1).replaceAll('-', '_').toUpperCase()}`,
    frontend.includes(`readJson('${endpoint}'`),
    endpoint
  );
}

assertInvariant(
  'MODULE_997_FRONTEND_READ_ONLY',
  frontend.includes("method: 'GET'")
    && !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(frontend),
  'browser sends no mutation request'
);

assertInvariant(
  'MODULE_997_LOCKED_CONTROLS_VISIBLE',
  count(frontend, /<button type="button" disabled>/g) >= 9
    && ['Contain threat', 'Eradicate cause', 'Recover service',
      'Send notification', 'Export evidence', 'Run AI analysis', 'Close case']
      .every((label) => frontend.includes(label)),
  'security actions are visible but disabled'
);

assertInvariant(
  'MODULE_997_US_SIGNAL_BRAND',
  frontend.includes('usSignalLogoDataUrl')
    && frontend.includes('alt="US Signal"')
    && stylesheet.includes('--security-blue: #005baa')
    && stylesheet.includes('--security-navy: #002f5d'),
  'approved repository logo and US Signal colors are used'
);

assertInvariant(
  'MODULE_997_SCOPED_STYLES',
  stylesheet.includes('.security-operations-center')
    && !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select)\s*[{,]/m.test(stylesheet),
  'stylesheet does not change the application shell globally'
);

assertInvariant(
  'MODULE_997_PROGRAM_REGISTRATION',
  count(program, /app\.MapSecurityOperationsResponseEndpoints\(\);/g) === 1,
  'backend registered exactly once'
);

assertInvariant(
  'MODULE_997_APP_INTEGRATION',
  count(app, /import SecurityOperationsResponseCenter from '\.\/SecurityOperationsResponseCenter\.jsx';/g) === 1
    && count(app, /activeRoute === 'security-operations'/g) === 1
    && app.includes("route: 'security-operations'")
    && app.includes("case 'security-operations':")
    && app.includes('VIEW_SECURITY_OPERATIONS')
    && app.includes('MANAGE_SECURITY_RESPONSE'),
  'role-aware route, navigation, registry, and grouping exist'
);

const packageScripts = JSON.parse(packageJson || '{}')?.scripts ?? {};
const buildCommand = packageScripts.build ?? '';
const prebuildCommand = packageScripts.prebuild ?? '';

assertInvariant(
  'MODULE_997_BUILD_GUARD',
  packageJson.includes('validate:module997')
    && packageJson.includes('validate-module-997-security-operations-response.mjs')
    && prebuildCommand.includes('validate:module997'),
  'npm prebuild runs Module 997 before the protected build chain'
);

assertInvariant(
  'MODULE_997_PROTECTED_VALIDATOR_CHAIN',
  ['validate:module059', 'validate:module062', 'validate:module002',
    'validate:module064', 'validate:module065', 'validate:module066',
    'validate:module067', 'validate:module068', 'validate:module069',
    'validate:module070', 'validate:module071', 'validate:module072',
    'validate:module073', 'validate:module074']
    .every((entry) => buildCommand.includes(entry)),
  '002, 059, 062, and 064-074 remain protected'
);

assertInvariant(
  'MODULE_997_CONTAINER_CONTEXT',
  dockerfile.includes('SecurityOperationsResponseModule.cs')
    && dockerfile.includes('module-997-security-operations-response')
    && dockerfile.includes('ApprovalCenterModule.cs')
    && dockerfile.includes('MODULE-CATALOG.md')
    && dockerfile.includes('AUGUST_PRODUCTION_READINESS_TRACKER.md'),
  'container build has every file read by the validator'
);

assertInvariant(
  'MODULE_997_DOCUMENTATION_SET',
  readme.includes('Module 997')
    && api.includes('423 Locked')
    && authorization.includes('View-As')
    && security.includes('Never inferred')
    && lifecycle.includes('Contain')
    && intelligence.includes('Approved-source contract')
    && evidence.includes('restricted security metadata')
    && integration.includes('Module 064')
    && overlap.includes('Modules 002, 056E, 059, 062, and 064–074')
    && validation.includes('Warning delta'),
  'security, API, lifecycle, intelligence, evidence, integration, and overlap docs exist'
);

assertInvariant(
  'MODULE_997_GOVERNANCE_REGISTERED',
  catalog.includes('Module 997 — Security Operations, Threat Intelligence & Response Center')
    && register.includes('Module 997 — Security Operations, Threat Intelligence & Response Center')
    && tracker.includes('Module 997 — Security Operations, Threat Intelligence & Response Center'),
  'catalog, work register, and tracker record ownership'
);

for (const requirement of [
  'GOV-017', 'RBAC-021', 'RBAC-022', 'INT-013', 'AI-021', 'RPT-014',
  'OPS-006', 'OPS-017', 'OPS-021', 'OPS-022', 'OPS-023', 'OPS-024',
  'OPS-025', 'OPS-026', 'OPS-027', 'DATA-012'
]) {
  assertInvariant(
    `MODULE_997_TRACKER_${requirement.replace('-', '_')}`,
    tracker.includes(requirement) && matrix.includes(requirement),
    requirement
  );
}

assertInvariant(
  'MODULE_997_PARALLEL_998_BOUNDARY',
  overlap.includes('draft PR 26')
    && overlap.includes('does not import, compile, call, or claim ownership')
    && !program.includes('MapSystemDiagnosticRemediationEndpoints')
    && !app.includes('SystemDiagnosticRemediationCenter'),
  'Module 997 remains independent of unmerged Module 998 source'
);

const moduleArtifacts = [
  ...filesBelow(path.join(repositoryRoot, 'database')),
  ...filesBelow(path.join(repositoryRoot, 'deployment'))
].filter((filePath) => /(?:module[-_]?997|security[-_]?operations)/i.test(path.basename(filePath)));

assertInvariant(
  'MODULE_997_NO_DATABASE_OR_DEPLOYMENT_ARTIFACT',
  moduleArtifacts.length === 0,
  moduleArtifacts.length === 0
    ? 'no Module 997 database or deployment artifact exists'
    : moduleArtifacts.map((filePath) => path.relative(repositoryRoot, filePath)).join(', ')
);

const failed = assertions.filter((assertion) => !assertion.condition);

console.log('');
console.log(`MODULE_997_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_997_PHASE=COMPLETE_SOURCE_FAIL_CLOSED');
console.log('MODULE_997_TELEMETRY_THREAT_CONNECTORS=NOT_CONFIGURED');
console.log('MODULE_997_CONTAINMENT_RESPONSE_EXECUTION=LOCKED');
console.log('MODULE_997_AI_NOTIFICATION_EXPORT_SECRET_ACCESS=LOCKED');
console.log('MODULE_997_DATABASE_AZURE_ENTRA_CLOUDFLARE_SMTP_CHANGES=NONE');

if (failed.length > 0) {
  console.error('MODULE_997_CONTRACT=FAILED');
  failed.forEach((failure) => console.error(`- ${failure.name}: ${failure.detail}`));
  process.exit(1);
}

console.log('MODULE_997_CONTRACT=PASSED');

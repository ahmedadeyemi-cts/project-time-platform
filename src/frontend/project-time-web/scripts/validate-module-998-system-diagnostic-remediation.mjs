import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');

const paths = {
  backend: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/SystemDiagnosticRemediationModule.cs'),
  frontend: path.join(repositoryRoot, 'src/frontend/project-time-web/src/SystemDiagnosticRemediationCenter.jsx'),
  stylesheet: path.join(repositoryRoot, 'src/frontend/project-time-web/src/system-diagnostic-remediation-center.css'),
  readme: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/README.md'),
  api: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/API-CONTRACT.md'),
  authorization: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/AUTHORIZATION-MATRIX.md'),
  matrix: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/CAPABILITY-MATRIX.md'),
  security: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/SECURITY-AND-OPERATIONS.md'),
  remediation: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/REMEDIATION-STATE-MACHINE.md'),
  evidence: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/EVIDENCE-AND-REDACTION.md'),
  overlap: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/OVERLAP-AND-INTEGRATION.md'),
  validation: path.join(repositoryRoot, 'docs/modules/module-998-system-diagnostic-remediation/VALIDATION-EVIDENCE.md'),
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
  assertInvariant(`MODULE_998_${name}_EXISTS`, exists, path.relative(repositoryRoot, filePath));
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
const security = readRequiredFile('SECURITY_OPERATIONS', paths.security);
const remediation = readRequiredFile('REMEDIATION_STATE_MACHINE', paths.remediation);
const evidence = readRequiredFile('EVIDENCE_REDACTION', paths.evidence);
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
  'MODULE_998_MAP_METHOD',
  backend.includes('MapSystemDiagnosticRemediationEndpoints'),
  'isolated endpoint registration exists'
);

assertInvariant(
  'MODULE_998_TYPED_ROUTE_HANDLERS',
  count(backend, /Func<HttpContext, Task<IResult>>/g) >= 14,
  'typed handlers preserve IResult responses'
);

for (const route of [
  '/api/system-diagnostics/overview',
  '/api/system-diagnostics/checks',
  '/api/system-diagnostics/issues',
  '/api/system-diagnostics/evidence-policy',
  '/api/system-diagnostics/remediation-policy',
  '/api/system-diagnostics/runbooks'
]) {
  assertInvariant(
    `MODULE_998_GET_${route.split('/').at(-1).replaceAll('-', '_').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

for (const route of [
  '/api/system-diagnostics/analysis',
  '/api/system-diagnostics/remediation/prepare',
  '/api/system-diagnostics/remediation/approve',
  '/api/system-diagnostics/remediation/stage',
  '/api/system-diagnostics/remediation/promote',
  '/api/system-diagnostics/remediation/verify',
  '/api/system-diagnostics/remediation/rollback',
  '/api/system-diagnostics/remediation/close'
]) {
  assertInvariant(
    `MODULE_998_LOCKED_${route.split('/').at(-1).replaceAll('-', '_').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

assertInvariant(
  'MODULE_998_BASELINE_VERIFIED',
  backend.includes('3d9a3dca8af479c854dc4c4a9294bc8aad273074')
    && readme.includes('48421d5ba1584d64fc3bd043304c003eff1dc27b')
    && overlap.includes('verified ancestor'),
  'current main and required checkpoint are explicit'
);

assertInvariant(
  'MODULE_998_ACTUAL_SESSION_AUTHORITY',
  backend.includes('ProjectPulseActualUserId')
    && backend.includes('ProjectPulseSessionUserId')
    && !backend.includes('ProjectPulseEffectiveUserId'),
  'View-As cannot supply authority'
);

assertInvariant(
  'MODULE_998_SERVER_AUTHORIZATION',
  backend.includes('SUPER_ADMINISTRATOR')
    && backend.includes('ADMINISTRATOR')
    && backend.includes('VIEW_SYSTEM_DIAGNOSTICS')
    && backend.includes('MANAGE_SYSTEM_REMEDIATION')
    && backend.includes('SYSTEM_ADMINISTRATION')
    && backend.includes('MANAGE_ALL')
    && backend.includes('diagnostic_access_required'),
  'role and permission checks are server-side'
);

assertInvariant(
  'MODULE_998_FAIL_CLOSED_OPERATIONS',
  backend.includes('StatusCodes.Status423Locked')
    && backend.includes('requestBodyRead = false')
    && backend.includes('adapterInvoked = false')
    && backend.includes('stateChanged = false')
    && backend.includes('containmentExecuted = false')
    && backend.includes('deploymentExecuted = false')
    && backend.includes('rollbackExecuted = false'),
  'all execution surfaces return locked evidence'
);

assertInvariant(
  'MODULE_998_BODY_NOT_READ',
  !/(?:Request\.Body|ReadFromJsonAsync|ReadToEndAsync|DeserializeAsync)/.test(backend),
  'locked operations never inspect a request payload'
);

assertInvariant(
  'MODULE_998_NO_EXECUTION_CONNECTOR',
  !/(?:HttpClient|TcpClient|UdpClient|Process\.Start|System\.Diagnostics\.Process|Azure\.|Microsoft\.Graph|SmtpClient|Anthropic|OpenAIClient)/.test(backend),
  'no telemetry, provider, command, mail, AI, or cloud connector exists'
);

const forbiddenSqlMutation = /\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+)\b/i;
assertInvariant(
  'MODULE_998_NO_MUTATING_SQL',
  !forbiddenSqlMutation.test(backend) && backend.includes('SELECT 1;'),
  'database use is read-only'
);

assertInvariant(
  'MODULE_998_SANITIZED_FAILURES',
  !/(?:exception|ex)\.Message/i.test(backend)
    && backend.includes('exception.GetType().Name')
    && backend.includes('authorization_dependency_unavailable'),
  'raw failure details are suppressed'
);

assertInvariant(
  'MODULE_998_STATUS_INTEGRITY',
  backend.includes('Delegated or unknown status is never represented as healthy.')
    && backend.includes('safe_local_observation_and_delegated_status')
    && backend.includes('future_owner'),
  'unknown/delegated states remain explicit'
);

assertInvariant(
  'MODULE_998_EVIDENCE_BOUNDARY',
  backend.includes('rawLogAccessEnabled = false')
    && backend.includes('secretAccessEnabled = false')
    && evidence.includes('Prohibited content')
    && security.includes('Data minimization'),
  'raw logs and secrets are excluded'
);

assertInvariant(
  'MODULE_998_REMEDIATION_LIFECYCLE',
  ['prepare', 'approve', 'stage', 'promote', 'verify', 'rollback', 'close']
    .every((stage) => backend.includes(`code = "${stage}"`) || remediation.includes(stage)),
  'complete controlled lifecycle is documented'
);

assertInvariant(
  'MODULE_998_FRONTEND_MARKERS',
  frontend.includes('data-module="998"')
    && frontend.includes('data-execution-mode="fail-closed"')
    && frontend.includes('data-contract-version'),
  'frontend declares module and execution boundary'
);

for (const endpoint of [
  '/api/system-diagnostics/overview',
  '/api/system-diagnostics/checks',
  '/api/system-diagnostics/issues',
  '/api/system-diagnostics/evidence-policy',
  '/api/system-diagnostics/remediation-policy',
  '/api/system-diagnostics/runbooks'
]) {
  assertInvariant(
    `MODULE_998_FRONTEND_${endpoint.split('/').at(-1).replaceAll('-', '_').toUpperCase()}`,
    frontend.includes(`readJson('${endpoint}'`),
    endpoint
  );
}

assertInvariant(
  'MODULE_998_FRONTEND_READ_ONLY',
  frontend.includes("method: 'GET'")
    && !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(frontend),
  'browser sends no mutation request'
);

assertInvariant(
  'MODULE_998_LOCKED_CONTROLS_VISIBLE',
  count(frontend, /<button type="button" disabled>/g) >= 8
    && frontend.includes('Promote to production')
    && frontend.includes('Verify outcome')
    && frontend.includes('Execute rollback')
    && frontend.includes('Close remediation')
    && frontend.includes('Run AI analysis'),
  'execution controls are present but disabled'
);

assertInvariant(
  'MODULE_998_US_SIGNAL_BRAND',
  frontend.includes('usSignalLogoDataUrl')
    && frontend.includes('alt="US Signal"')
    && stylesheet.includes('--diagnostic-blue: #005baa')
    && stylesheet.includes('--diagnostic-navy: #002f5d'),
  'approved repository logo and brand colors are used'
);

assertInvariant(
  'MODULE_998_SCOPED_STYLES',
  stylesheet.includes('.system-diagnostic-center')
    && !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select)\s*[{,]/m.test(stylesheet),
  'stylesheet does not change the application shell globally'
);

assertInvariant(
  'MODULE_998_PROGRAM_REGISTRATION',
  count(program, /app\.MapSystemDiagnosticRemediationEndpoints\(\);/g) === 1,
  'backend registered exactly once'
);

assertInvariant(
  'MODULE_998_APP_INTEGRATION',
  count(app, /import SystemDiagnosticRemediationCenter from '\.\/SystemDiagnosticRemediationCenter\.jsx';/g) === 1
    && count(app, /activeRoute === 'system-diagnostics'/g) === 1
    && app.includes("route: 'system-diagnostics'")
    && app.includes("case 'system-diagnostics':")
    && app.includes('VIEW_SYSTEM_DIAGNOSTICS')
    && app.includes('MANAGE_SYSTEM_REMEDIATION'),
  'role-aware route, navigation, registry, and grouping exist'
);

const buildCommand = JSON.parse(packageJson || '{}')?.scripts?.build ?? '';
assertInvariant(
  'MODULE_998_BUILD_GUARD',
  packageJson.includes('validate:module998')
    && packageJson.includes('validate-module-998-system-diagnostic-remediation.mjs')
    && buildCommand.indexOf('validate:module074') < buildCommand.indexOf('validate:module998')
    && buildCommand.indexOf('validate:module998') < buildCommand.indexOf('vite build'),
  'Module 998 follows the protected validator chain'
);

assertInvariant(
  'MODULE_998_PROTECTED_VALIDATOR_CHAIN',
  ['validate:module059', 'validate:module062', 'validate:module002',
    'validate:module064', 'validate:module065', 'validate:module066',
    'validate:module067', 'validate:module068', 'validate:module069',
    'validate:module070', 'validate:module071', 'validate:module072',
    'validate:module073', 'validate:module074', 'validate:module998']
    .every((entry) => buildCommand.includes(entry)),
  '002, 059, 062, and 064-074 remain protected'
);

assertInvariant(
  'MODULE_998_CONTAINER_CONTEXT',
  dockerfile.includes('SystemDiagnosticRemediationModule.cs')
    && dockerfile.includes('module-998-system-diagnostic-remediation')
    && dockerfile.includes('ApprovalCenterModule.cs')
    && dockerfile.includes('MODULE-CATALOG.md')
    && dockerfile.includes('AUGUST_PRODUCTION_READINESS_TRACKER.md'),
  'container build has every file read by the validator'
);

assertInvariant(
  'MODULE_998_DOCUMENTATION_SET',
  readme.includes('Module 998')
    && api.includes('423 Locked')
    && authorization.includes('View-As')
    && matrix.includes('GOV-016')
    && matrix.includes('AI-020')
    && matrix.includes('DATA-011')
    && remediation.includes('prepare (locked)')
    && overlap.includes('Modules 002, 056E, 059, 062, and 064–074')
    && validation.includes('Backend warning delta')
    && validation.includes('External-system changes'),
  'governance, API, security, lifecycle, evidence, and overlap docs exist'
);

assertInvariant(
  'MODULE_998_GOVERNANCE_REGISTERED',
  catalog.includes('| 998 | System Diagnostic & Controlled Remediation Center')
    && register.includes('| 998 |')
    && tracker.includes('Module 998 — System Diagnostic & Controlled Remediation Center'),
  'catalog, work register, and tracker record ownership'
);

for (const requirement of [
  'GOV-016', 'AI-020', 'AI-021', 'OPS-005', 'OPS-006', 'OPS-015',
  'OPS-017', 'OPS-018', 'OPS-019', 'OPS-020', 'DATA-011', 'RBAC-022'
]) {
  assertInvariant(
    `MODULE_998_TRACKER_${requirement.replace('-', '_')}`,
    tracker.includes(requirement) && matrix.includes(requirement),
    requirement
  );
}

const moduleArtifacts = [
  ...filesBelow(path.join(repositoryRoot, 'database')),
  ...filesBelow(path.join(repositoryRoot, 'deployment'))
].filter((filePath) => /(?:module[-_]?998|system[-_]?diagnostic)/i.test(path.basename(filePath)));

assertInvariant(
  'MODULE_998_NO_DATABASE_OR_DEPLOYMENT_ARTIFACT',
  moduleArtifacts.length === 0,
  moduleArtifacts.length === 0
    ? 'no Module 998 database or deployment artifact exists'
    : moduleArtifacts.map((filePath) => path.relative(repositoryRoot, filePath)).join(', ')
);

const failed = assertions.filter((assertion) => !assertion.condition);

console.log('');
console.log(`MODULE_998_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_998_PHASE=COMPLETE_SOURCE_FAIL_CLOSED');
console.log('MODULE_998_PRODUCTION_REMEDIATION=LOCKED');
console.log('MODULE_998_SECURITY_CONTAINMENT=LOCKED');
console.log('MODULE_998_AI_TELEMETRY_NOTIFICATION_EXECUTION=LOCKED');
console.log('MODULE_998_DATABASE_AZURE_ENTRA_CLOUDFLARE_SMTP_CHANGES=NONE');

if (failed.length > 0) {
  console.error('MODULE_998_CONTRACT=FAILED');
  failed.forEach((failure) => console.error(`- ${failure.name}: ${failure.detail}`));
  process.exit(1);
}

console.log('MODULE_998_CONTRACT=PASSED');

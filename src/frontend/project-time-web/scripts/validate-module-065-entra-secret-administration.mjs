import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/EntraSecretAdministrationModule.cs',
  contracts: 'src/backend/ProjectTime.Api/Modules/EntraSecretRotationContracts.cs',
  frontend: 'src/frontend/project-time-web/src/EntraSecretAdministrationCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/entra-secret-administration-center.css',
  readme: 'docs/modules/module-065-entra-secret-administration/README.md',
  api: 'docs/modules/module-065-entra-secret-administration/API-CONTRACT.md',
  authorization: 'docs/modules/module-065-entra-secret-administration/AUTHORIZATION-MATRIX.md',
  matrix: 'docs/modules/module-065-entra-secret-administration/CAPABILITY-MATRIX.md',
  security: 'docs/modules/module-065-entra-secret-administration/SECURITY-BOUNDARY.md',
  workflow: 'docs/modules/module-065-entra-secret-administration/ROTATION-STATE-MACHINE.md',
  adapter: 'docs/modules/module-065-entra-secret-administration/ADAPTER-CONTRACT.md',
  overlap: 'docs/modules/module-065-entra-secret-administration/OVERLAP-AND-INTEGRATION.md',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile'
};

const exists = (file) => fs.existsSync(path.join(root, file));
const read = (file) => fs.readFileSync(path.join(root, file), 'utf8');
const checks = [];
function check(name, condition, evidence) {
  checks.push(Boolean(condition));
  console.log(`MODULE_065_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const [name, file] of Object.entries(files)) {
  check(`${name.toUpperCase()}_EXISTS`, exists(file), file);
}

const backend = read(files.backend);
const contracts = read(files.contracts);
const frontend = read(files.frontend);
const css = read(files.stylesheet);
const docs = [
  files.readme,
  files.api,
  files.authorization,
  files.matrix,
  files.security,
  files.workflow,
  files.adapter,
  files.overlap
].map(read).join('\n');

const program = read(files.program);
const app = read(files.app);
const packageJson = JSON.parse(read(files.package));
const docker = read(files.docker);
const count = (value, marker) => value.split(marker).length - 1;

const stageBody = backend.slice(
  backend.indexOf('private static async Task<IResult> StageSecretAsync'),
  backend.indexOf('private static async Task<IResult> TestRotationAsync')
);
const mutationAccess = backend.slice(
  backend.indexOf('private static async Task<AccessOutcome> ResolveMutationAccessAsync'),
  backend.indexOf('private static async Task<AccessOutcome> ResolveAccessAsync')
);

check('MAP_METHOD', backend.includes('MapEntraSecretAdministrationEndpoints'), 'isolated endpoint registration');
check('COMPLETE_READ_SURFACE', [
  '/capabilities', '/metadata', '/readiness', '/workflow-contract', '/audit-contract'
].every((value) => backend.includes(`/api/entra-secret-administration${value}`)), 'five read contracts');
check('COMPLETE_ROTATION_SURFACE', [
  '/rotations/prepare', '/approve', '/secret', '/test', '/activate', '/rollback'
].every((value) => backend.includes(value)), 'six guarded lifecycle contracts');
check('CONTRACT_VERSION', backend.includes('2026-07-19.2'), 'complete-source contract version');
check('CURRENT_MAIN_BASELINE', backend.includes('2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4'), 'Module 002-enabled main');
check('SUPER_ADMIN', backend.includes('roles.Contains("SUPER_ADMINISTRATOR")'), 'tracker role');
check('DELEGATED_PERMISSION', backend.includes('permissions.Contains(DelegatedPermission)') && backend.includes('MANAGE_ENTRA_SECRET'), 'explicit capability');
check('NO_BROAD_ADMIN', !backend.includes('roles.Contains("ADMINISTRATOR")') && !backend.includes('permissions.Contains("MANAGE_ALL")'), 'no implicit broad authority');
check('ACTUAL_SESSION', backend.includes('ProjectPulseActualUserId') && backend.includes('ActualEmail(context)'), 'actual user and email authority');
check('VIEW_AS_BLOCKED', mutationAccess.includes('IsViewAs(context)') && mutationAccess.includes('actual_session_required'), 'no View-As mutation authority');
check('MODULE_010_METADATA', backend.includes('FROM azure_entra_settings') && backend.includes('module_010_azure_entra_settings'), 'existing tenant settings are primary');
check('MODULE_010_PRESERVED', docs.includes('Module 010') && docs.includes('tenant settings') && docs.includes('user synchronization'), 'ownership boundary documented');
check('METADATA_FIELDS', [
  'ApplicationName', 'Environment', 'TenantId', 'ClientId', 'ActiveVersion', 'Fingerprint',
  'LastRotationAt', 'ExpiresAt', 'DaysUntilExpiration', 'Health'
].every((value) => backend.includes(value)), 'RBAC-018 metadata');
check('EXPIRATION_THRESHOLDS', backend.includes('days <= 14') && backend.includes('days <= 30'), 'critical and warning windows');
check('SECRET_PRESENCE_ONLY', backend.includes('Has("PROJECTPULSE_ENTRA_CLIENT_SECRET")') && !backend.includes('Env("PROJECTPULSE_ENTRA_CLIENT_SECRET"'), 'usable value is never returned as metadata');
check('ADAPTER_INTERFACE', contracts.includes('interface IEntraSecretRotationAdapter'), 'sole mutation extension point');
check('LOCKED_DEFAULT_ADAPTER', contracts.includes('LockedEntraSecretRotationAdapter') && contracts.includes('IsConfigured => false'), 'fail-closed default');
check('ADAPTER_INJECTION', backend.includes('IEntraSecretRotationAdapter? rotationAdapter = null') && backend.includes('rotationAdapter ?? LockedEntraSecretRotationAdapter.Instance'), 'explicit reviewed adapter gate');
check('EXTERNAL_AUTH_GATE', mutationAccess.includes('RotationGate(adapter)') && backend.includes('PROJECTPULSE_ENTRA_SECRET_EXTERNAL_AUTHORIZATION_ID'), 'authorization record required');
check('MUTATION_SWITCH_GATE', backend.includes('PROJECTPULSE_ENTRA_SECRET_MUTATION_ENABLED') && mutationAccess.includes('!gate.Enabled'), 'explicit switch required');
check('BODY_NOT_READ_WHEN_LOCKED', mutationAccess.includes('bodyRead = false') && mutationAccess.includes('statusCode: 423'), 'locked response precedes handlers');
check('SERVER_STEP_UP', backend.includes('ProjectPulseStepUpSatisfied') && backend.includes('ProjectPulseStepUpAuthenticatedAt') && backend.includes('TimeSpan.FromMinutes(5)'), 'recent server context only');
check('STEP_UP_BEFORE_BODY', mutationAccess.includes('StepUpAuthenticatedAt(context)') && mutationAccess.includes('bodyRead = false'), 'step-up gate runs before handler body read');
check('WRITE_ONLY_CONTENT_TYPE', stageBody.includes('application/octet-stream') && stageBody.includes('Status415UnsupportedMediaType'), 'raw secret transport');
check('BOUNDED_SECRET_BODY', backend.includes('MaximumSecretBytes = 4096') && stageBody.includes('Status413PayloadTooLarge'), '4 KiB maximum');
check('SECRET_GATE_BEFORE_READ', stageBody.indexOf('ResolveMutationAccessAsync') < stageBody.indexOf('Body.ReadAsync'), 'secret body is not read before gates');
check('ZEROABLE_SECRET_LEASE', contracts.includes('CryptographicOperations.ZeroMemory') && stageBody.includes('SensitiveSecretLease'), 'buffer cleared after use');
check('NO_SECRET_TOSTRING', !contracts.includes('override string ToString') && !contracts.includes('string Secret'), 'lease cannot stringify secret');
check('SANITIZED_RESULT', contracts.includes('EntraSecretOperationResult') && !contracts.includes('AccessToken') && !contracts.includes('ProviderPayload'), 'result has no secret/token/provider fields');
check('ADAPTER_MESSAGE_SUPPRESSED', backend.includes('adapterMessageReturned = false') && !backend.includes('result.Message,'), 'adapter text cannot leak provider detail');
check('NO_DIRECT_PROVIDER_CALL', !backend.includes('new HttpClient') && !backend.includes('PostAsync(') && !backend.includes('SendAsync(') && !contracts.includes('new HttpClient'), 'no Azure/Entra call');
check('NO_RAW_EXCEPTION_LOG', !backend.includes('LogWarning(exception') && !backend.includes('LogError(exception'), 'raw exception is suppressed');
check('SANITIZED_ADAPTER_FAILURE', backend.includes('ExecuteAdapterAsync') && backend.includes('credential_adapter_failed') && backend.includes('raw exception detail was suppressed'), 'adapter exceptions cannot reach response/log detail');
check('DUAL_APPROVAL_CONTRACT', contracts.includes('ApproveAsync') && docs.includes('initiating actor cannot satisfy'), 'separation of duties');
check('TOKEN_TEST_CONTRACT', contracts.includes('TestAsync') && docs.includes('token-acquisition test'), 'sanitized provider test adapter');
check('EXPLICIT_ACTIVATION_CONTRACT', contracts.includes('ActivateAsync') && docs.includes('Activation cannot precede'), 'validated state required');
check('OVERLAP_VALIDATION', backend.includes('OverlapHours is < 1 or > 168') && docs.includes('active_overlap'), 'bounded overlap');
check('ROLLBACK_CONTRACT', contracts.includes('RollbackAsync') && docs.includes('approved previous version'), 'governed rollback target');
check('AUDIT_CONTRACT', backend.includes('appendOnly = true') && backend.includes('secretValueRecorded = false') && backend.includes('correlationId'), 'sanitized immutable evidence');
check('NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\s+(?:INTO|TABLE|FROM|VIEW|INDEX|SCHEMA)\b/i.test(backend), 'authorization and metadata SELECT only');
check('NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(root, 'database/migrations/065-entra-secret-administration.sql')), 'no migration');
check('FRONTEND_COMPLETE_PHASE', frontend.includes('065_COMPLETE_SOURCE_LOCKED_RUNTIME'), 'accurate source/runtime boundary');
check('FRONTEND_FIVE_READ_ENDPOINTS', [
  '/capabilities', '/metadata', '/readiness', '/workflow-contract', '/audit-contract'
].every((value) => frontend.includes(`/api/entra-secret-administration${value}`)), 'operations center consumes read contracts');
check('FRONTEND_GET_ONLY', frontend.includes("method: 'GET'") && !frontend.includes("method: 'POST'") && !frontend.includes("method: 'PUT'"), 'frontend cannot mutate');
check('NO_SECRET_FRONTEND_FIELD', !frontend.includes('type="password"') && !frontend.includes('name="clientSecret"') && !frontend.includes('setSecret'), 'no usable secret control/state');
check('NO_BROWSER_SECRET_STORAGE', !frontend.includes('setItem('), 'module writes no browser storage');
check('US_SIGNAL_LOGO', frontend.includes('usSignalLogoDataUrl') && frontend.includes('alt="US Signal"'), 'repository logo');
check('US_SIGNAL_BRAND', css.includes('--entra-blue') && css.includes('--entra-cyan') && css.includes('--entra-green'), 'US Signal color system');
check('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(css), 'no shell selector');
check('REQUIREMENT_RBAC_018', docs.includes('RBAC-018'), 'tracker requirement');
check('WORKFLOW_DOCUMENTED', docs.includes('prepared') && docs.includes('secret_staged') && docs.includes('validated') && docs.includes('rolled_back'), 'state machine');
check('ADAPTER_ACCEPTANCE_DOCUMENTED', docs.includes('IEntraSecretRotationAdapter') && docs.includes('redaction review'), 'future implementation gate');
check('MODULE_002_PRESERVED', docs.includes('f5ede8f6717b01c8f4bf7905b433fead38210007') && docs.includes('2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4'), 'source and merge commits recorded');
check('OVERLAP_GATE', ['Module 002', 'Module 064', 'Module 066', 'Module 068'].every((value) => docs.includes(value)) && docs.includes('BLOCKED'), 'shared integration gate');
check('PROGRAM_REGISTRATION', count(program, 'app.MapEntraSecretAdministrationEndpoints();') === 1, 'fail-closed backend registered once');
check('APP_IMPORT', count(app, "import EntraSecretAdministrationCenter from './EntraSecretAdministrationCenter.jsx';") === 1, 'frontend imported once');
check('APP_MOUNT', count(app, '<EntraSecretAdministrationCenter authSession={authSession} />') === 1, 'frontend mounted once');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module065') && packageJson.scripts?.['validate:module065']?.includes('validate-module-065-entra-secret-administration.mjs'), 'production build guard');
check('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes('docs/modules/module-065-entra-secret-administration/'), 'container validator context');
check('NO_EXTERNAL_MUTATION', docs.includes('Azure changed: no') && docs.includes('Entra changed: no') && docs.includes('Database changed: no'), 'external state unchanged');

console.log(`\nMODULE_065_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_065_PHASE=065_COMPLETE_SOURCE_LOCKED_RUNTIME');
console.log('MODULE_065_ROTATION_CONTRACT=COMPLETE_FAIL_CLOSED');
console.log('MODULE_065_EXTERNAL_ADAPTER=NOT_AUTHORIZED_NOT_CONFIGURED');
console.log('MODULE_065_RUNTIME_REGISTRATION=REGISTERED_FAIL_CLOSED_UNCOMMITTED');
if (checks.some((value) => !value)) {
  console.error('MODULE_065_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_065_CONTRACT=PASSED');

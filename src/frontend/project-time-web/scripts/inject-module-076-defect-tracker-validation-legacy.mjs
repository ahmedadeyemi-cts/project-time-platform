import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');
const moduleDocs = path.join(repositoryRoot, 'docs/modules/module-076-defect-tracker');

const paths = {
  backend: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/DefectTrackerModule.cs'),
  frontend: path.join(repositoryRoot, 'src/frontend/project-time-web/src/DefectTrackerCenter.jsx'),
  stylesheet: path.join(repositoryRoot, 'src/frontend/project-time-web/src/defect-tracker-center.css'),
  help: path.join(repositoryRoot, 'src/frontend/project-time-web/src/HelpAssistant.jsx'),
  helpStyles: path.join(repositoryRoot, 'src/frontend/project-time-web/src/help-assistant.css'),
  issueForm: path.join(repositoryRoot, '.github/ISSUE_TEMPLATE/projectpulse-defect.yml'),
  readme: path.join(moduleDocs, 'README.md'),
  api: path.join(moduleDocs, 'API-CONTRACT.md'),
  authorization: path.join(moduleDocs, 'AUTHORIZATION-MATRIX.md'),
  data: path.join(moduleDocs, 'DATA-AND-ID-CONTRACT.md'),
  notification: path.join(moduleDocs, 'NOTIFICATION-AND-INTEGRATION-CONTRACT.md'),
  matrix: path.join(moduleDocs, 'CAPABILITY-MATRIX.md'),
  overlap: path.join(moduleDocs, 'OVERLAP-AND-RELEASE-GATES.md'),
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
  assertInvariant(`MODULE_076_${name}_EXISTS`, exists, path.relative(repositoryRoot, filePath));
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
const help = readRequiredFile('HELP_ASSISTANT', paths.help);
const helpStyles = readRequiredFile('HELP_STYLES', paths.helpStyles);
const issueForm = readRequiredFile('GITHUB_ISSUE_FORM', paths.issueForm);
const readme = readRequiredFile('README', paths.readme);
const api = readRequiredFile('API_CONTRACT', paths.api);
const authorization = readRequiredFile('AUTHORIZATION_MATRIX', paths.authorization);
const data = readRequiredFile('DATA_ID_CONTRACT', paths.data);
const notification = readRequiredFile('NOTIFICATION_INTEGRATION', paths.notification);
const matrix = readRequiredFile('CAPABILITY_MATRIX', paths.matrix);
const overlap = readRequiredFile('OVERLAP_RELEASE_GATES', paths.overlap);
const program = readRequiredFile('PROGRAM', paths.program);
const app = readRequiredFile('APP', paths.app);
const packageJson = readRequiredFile('PACKAGE', paths.package);
const dockerfile = readRequiredFile('DOCKERFILE', paths.dockerfile);
const catalog = readRequiredFile('CATALOG', paths.catalog);
const register = readRequiredFile('WORK_REGISTER', paths.register);
const tracker = readRequiredFile('PRODUCTION_TRACKER', paths.tracker);

const legacyWorkspaceExclusion = app.match(
  /\{\s*!\s*\[([\s\S]*?)\]\.includes\(activeRoute\)\s*\?\s*\(/
)?.[1] ?? '';

assertInvariant(
  'MODULE_076_STANDALONE_ROUTE',
  legacyWorkspaceExclusion.includes("'defect-tracker'"),
  'Defect Tracker excludes the legacy workspace fallback'
);

assertInvariant(
  'MODULE_076_MAP_METHOD',
  backend.includes('MapDefectTrackerEndpoints'),
  'isolated endpoint registration exists'
);

assertInvariant(
  'MODULE_076_TYPED_ROUTE_HANDLERS',
  count(backend, /Func<HttpContext(?:, string)?, Task<IResult>>/g) >= 12,
  'typed handlers preserve IResult responses'
);

for (const route of [
  '/api/defect-tracker/overview',
  '/api/defect-tracker/defects',
  '/api/defect-tracker/assignee-options',
  '/api/defect-tracker/intake-policy',
  '/api/defect-tracker/notification-policy',
  '/api/defect-tracker/integration-policy'
]) {
  assertInvariant(
    `MODULE_076_GET_${route.split('/').at(-1).replaceAll('-', '_').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

for (const route of [
  '/api/defect-tracker/report',
  '/api/defect-tracker/defects/{defectId}',
  '/api/defect-tracker/defects/{defectId}/reassign',
  '/api/defect-tracker/defects/{defectId}/comments',
  '/api/defect-tracker/defects/{defectId}/resolve',
  '/api/defect-tracker/integrations/github/events'
]) {
  assertInvariant(
    `MODULE_076_LOCKED_${route.split('/').at(-1).replaceAll('-', '_').replace(/[{}]/g, '').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

assertInvariant(
  'MODULE_076_BASELINE_VERIFIED',
  backend.includes('3d9a3dca8af479c854dc4c4a9294bc8aad273074')
    && overlap.includes('48421d5ba1584d64fc3bd043304c003eff1dc27b')
    && overlap.includes('verified ancestor'),
  'current main and required checkpoint are explicit'
);

assertInvariant(
  'MODULE_076_AUTOMATIC_ID_POLICY',
  backend.includes('DEF-{YYYY}-{SEQUENCE:000000}')
    && backend.includes('DEF-2026-000001')
    && backend.includes('clientSuppliedIdsAccepted = false')
    && data.includes('Atomic and immutable'),
  'durable IDs are server allocated'
);

assertInvariant(
  'MODULE_076_REQUIRED_COLUMNS',
  [
    'defectId', 'status', 'description', 'category', 'priority',
    'assignee', 'raisedBy', 'dateAdded', 'dateResolved',
    'resolutionTime', 'comments'
  ].every((field) => backend.includes(`"${field}"`) || frontend.includes(field)),
  'tracker fields match the approved headers'
);

assertInvariant(
  'MODULE_076_SERVER_DATE_POLICY',
  backend.includes('dateAdded = "Assigned by the server in UTC')
    && backend.includes('dateResolved = "Assigned by the server in UTC')
    && backend.includes('resolutionTime = "Calculated by the server')
    && data.includes('dateResolved - dateAdded'),
  'date added, date resolved, and resolution time are server-owned'
);

assertInvariant(
  'MODULE_076_LIFECYCLE_TAXONOMY',
  ['Open', 'In Progress', 'Blocked', 'Resolved', 'Closed', 'Reopened']
    .every((status) => backend.includes(`"${status}"`)),
  'complete defect lifecycle exists'
);

assertInvariant(
  'MODULE_076_PRIORITY_CATEGORY_TAXONOMY',
  ['Critical', 'High', 'Medium', 'Low', 'Bug', 'Regression', 'User Interface', 'API', 'Authentication', 'Data', 'Integration', 'Performance', 'Documentation', 'Feature Gap', 'Other']
    .every((value) => backend.includes(`"${value}"`)),
  'priority and category allowlists exist'
);

assertInvariant(
  'MODULE_076_ALL_APPROVED_SOURCES',
  ['help', 'github', 'claude_github', 'chatgpt_github']
    .every((source) => backend.includes(`code = "${source}"`) || backend.includes(`channel = "${source}"`))
    && notification.includes('Claude and ChatGPT report **through GitHub**'),
  'Help, GitHub, Claude-through-GitHub, and ChatGPT-through-GitHub are represented'
);

assertInvariant(
  'MODULE_076_DEFAULT_AHMED_ASSIGNMENT',
  backend.includes('PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL')
    && backend.includes('ahmed.adeyemi@ussignal.com')
    && backend.includes('Ahmed Adeyemi')
    && !/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i.test(backend),
  'Ahmed is configured by identity email without a hardcoded user GUID'
);

assertInvariant(
  'MODULE_076_IDENTITY_DROPDOWN',
  backend.includes('FROM app_users u')
    && backend.includes('identityAuthority = "Module 062 / app_users.user_id"')
    && frontend.includes("readJson('/api/defect-tracker/assignee-options'")
    && frontend.includes('assigneeUserId')
    && frontend.includes('<select'),
  'reassignment uses stable ProjectPulse identities'
);

assertInvariant(
  'MODULE_076_ACTUAL_SESSION_AUTHORITY',
  backend.includes('ProjectPulseActualUserId')
    && backend.includes('ProjectPulseEffectiveUserId')
    && backend.includes('status = "view_as_read_only"')
    && backend.includes('effective_user_reported_or_assigned_defects')
    && backend.includes('viewAsTransfersMutationAuthority = false')
    && backend.includes('ProjectPulseIsViewAs'),
  'actual and effective session boundaries are explicit'
);

assertInvariant(
  'MODULE_076_ROLE_AUTHORIZATION',
  ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'MANAGER', 'ENGINEERING_MANAGER', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'MANAGE_DEFECTS', 'VIEW_ALL_DEFECTS']
    .every((code) => backend.includes(`"${code}"`) || authorization.includes(code)),
  'management and reassignment roles are server-side'
);

assertInvariant(
  'MODULE_076_FAIL_CLOSED_OPERATIONS',
  backend.includes('StatusCodes.Status423Locked')
    && backend.includes('requestBodyRead = false')
    && backend.includes('durableDefectWritten = false')
    && backend.includes('defectIdAllocated = false')
    && backend.includes('outboxEventWritten = false')
    && backend.includes('githubChanged = false')
    && backend.includes('aiExecuted = false')
    && backend.includes('stateChanged = false'),
  'all mutation and integration surfaces stop safely'
);

assertInvariant(
  'MODULE_076_LOCKED_BODY_NOT_READ',
  !/(?:Request\.Body|ReadFromJsonAsync|ReadToEndAsync|DeserializeAsync)/.test(backend),
  'locked operations never inspect a request body'
);

const forbiddenConnector = /(?:new\s+(?:HttpClient|SmtpClient|Octokit|AnthropicClient|OpenAIClient|BrevoClient)|using\s+(?:MailKit|MimeKit|Microsoft\.Graph)|api\.brevo\.com|Process\.Start)/;
assertInvariant(
  'MODULE_076_NO_DIRECT_CONNECTOR',
  !forbiddenConnector.test(backend),
  'no GitHub, mail, AI, cloud, or command connector exists'
);

const forbiddenSqlMutation = /\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+)\b/i;
assertInvariant(
  'MODULE_076_NO_MUTATING_SQL',
  !forbiddenSqlMutation.test(backend)
    && backend.includes('FROM app_users u'),
  'backend SQL is identity/authorization SELECT only'
);

assertInvariant(
  'MODULE_076_SANITIZED_FAILURES',
  !/(?:exception|ex)\.Message/i.test(backend)
    && backend.includes('exception.GetType().Name')
    && backend.includes('authorization_dependency_unavailable'),
  'raw exceptions are not returned or logged'
);

assertInvariant(
  'MODULE_076_OPEN_MANAGER_EMAIL_POLICY',
  backend.includes('eventCode = "defect_opened"')
    && backend.includes('recipients = "active manager role group"')
    && backend.includes('defect_opened:{defectId}')
    && notification.includes('Active managers'),
  'open defects queue a future manager notification'
);

assertInvariant(
  'MODULE_076_RESOLVED_REPORTER_EMAIL_POLICY',
  backend.includes('eventCode = "defect_resolved"')
    && backend.includes('recipients = "original reporter"')
    && backend.includes('defect_resolved:{defectId}:{resolutionVersion}')
    && notification.includes('Original reporter'),
  'resolved defects queue a future reporter notification'
);

assertInvariant(
  'MODULE_076_MODULE067_MAIL_OWNERSHIP',
  backend.includes('Module 067 Global Mail Configuration')
    && backend.includes('directSmtpClientPresent = false')
    && backend.includes('directBrevoClientPresent = false')
    && readme.includes('transactional outbox'),
  'Module 067 owns outbound delivery'
);

assertInvariant(
  'MODULE_076_GITHUB_SECURITY_CONTRACT',
  backend.includes('ahmedadeyemi-cts/project-time-platform')
    && backend.includes('signature validation before body processing')
    && backend.includes('delivery-ID deduplication')
    && backend.includes('repository and installation allowlist')
    && backend.includes('webhookEnabled = false'),
  'signed, allowlisted, idempotent GitHub intake remains locked'
);

assertInvariant(
  'MODULE_076_SHARED_AI_BOUNDARY',
  backend.includes('sharedModule064RequiredForFutureTriage = true')
    && backend.includes('directClaudeExecutionEnabled = false')
    && backend.includes('directOpenAiExecutionEnabled = false')
    && notification.includes('Module 064 shared routing'),
  'future AI triage must use Module 064'
);

assertInvariant(
  'MODULE_076_FRONTEND_MARKERS',
  frontend.includes('data-module="076"')
    && frontend.includes('data-persistence-mode="fail-closed"')
    && frontend.includes('data-contract-version'),
  'frontend declares its governed boundary'
);

for (const endpoint of [
  '/api/defect-tracker/overview',
  '/api/defect-tracker/defects',
  '/api/defect-tracker/intake-policy',
  '/api/defect-tracker/notification-policy',
  '/api/defect-tracker/integration-policy'
]) {
  assertInvariant(
    `MODULE_076_FRONTEND_${endpoint.split('/').at(-1).replaceAll('-', '_').toUpperCase()}`,
    frontend.includes(`'${endpoint}'`),
    endpoint
  );
}

assertInvariant(
  'MODULE_076_FRONTEND_GET_ONLY',
  frontend.includes("method: 'GET'")
    && !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(frontend),
  'browser performs no server mutation'
);

assertInvariant(
  'MODULE_076_FRONTEND_COMPLETE_HEADERS',
  ['Defect ID', 'Status', 'Description', 'Category', 'Priority', 'Assignee', 'Raised By', 'Source', 'Date Added', 'Date Resolved', 'Resolution Time', 'Comments']
    .every((header) => frontend.includes(`>${header}<`)),
  'approved tracker headers are rendered'
);

assertInvariant(
  'MODULE_076_FRONTEND_LOCAL_DRAFT_ONLY',
  frontend.includes('Review local draft')
    && frontend.includes('disabled={!writesEnabled}')
    && frontend.includes('Assigned after durable save')
    && frontend.includes('No durable inventory is connected.'),
  'draft review cannot be mistaken for a durable defect'
);

assertInvariant(
  'MODULE_076_HELP_INTEGRATION',
  help.includes('Report a defect — Module 076')
    && help.includes("destination.searchParams.set('defectSource', 'help')")
    && help.includes("destination.hash = 'defect-tracker'")
    && helpStyles.includes('.help-report-defect-button'),
  'global Help opens the Module 076 intake source'
);

assertInvariant(
  'MODULE_076_GITHUB_ISSUE_FORM',
  issueForm.includes('name: ProjectPulse defect')
    && issueForm.includes('module-076-intake')
    && issueForm.includes('ahmedadeyemi-cts')
    && issueForm.includes('Claude through GitHub')
    && issueForm.includes('ChatGPT through GitHub')
    && issueForm.includes('Affected module')
    && issueForm.includes('Priority')
    && issueForm.includes('Safety confirmation'),
  'GitHub issue form captures governed intake and defaults to Ahmed'
);

assertInvariant(
  'MODULE_076_US_SIGNAL_BRAND',
  frontend.includes('usSignalLogoUrl')
    && frontend.includes('alt="US Signal"')
    && stylesheet.includes('--defect-blue: #005baa')
    && stylesheet.includes('--defect-navy: #002f5d'),
  'approved repository logo and US Signal colors are used'
);

assertInvariant(
  'MODULE_076_SCOPED_STYLES',
  stylesheet.includes('.defect-tracker-center')
    && !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select|textarea)\s*[{,]/m.test(stylesheet),
  'stylesheet does not change the application shell globally'
);

assertInvariant(
  'MODULE_076_PROGRAM_REGISTRATION',
  count(program, /app\.MapDefectTrackerEndpoints\(\);/g) === 1,
  'backend is registered exactly once'
);

assertInvariant(
  'MODULE_076_APP_INTEGRATION',
  count(app, /import DefectTrackerCenter from '\.\/DefectTrackerCenter\.jsx';/g) === 1
    && count(app, /activeRoute === 'defect-tracker'/g) === 1
    && app.includes("route: 'defect-tracker'")
    && app.includes("case 'defect-tracker':")
    && app.includes("navLabel: 'MODULE 076'")
    && app.includes('<DefectTrackerCenter authSession={authSession} />'),
  'route, navigation, registry, grouping, and mount exist'
);

const buildCommand = JSON.parse(packageJson || '{}')?.scripts?.build ?? '';
assertInvariant(
  'MODULE_076_BUILD_GUARD',
  packageJson.includes('validate:module076')
    && packageJson.includes('validate-module-076-defect-tracker.mjs')
    && buildCommand.indexOf('validate:module074') < buildCommand.indexOf('validate:module076')
    && buildCommand.indexOf('validate:module076') < buildCommand.indexOf('vite build'),
  'Module 076 follows the current protected validator chain'
);

assertInvariant(
  'MODULE_076_PROTECTED_VALIDATOR_CHAIN',
  ['validate:module059', 'validate:module062', 'validate:module002',
    'validate:module064', 'validate:module065', 'validate:module066',
    'validate:module067', 'validate:module068', 'validate:module069',
    'validate:module070', 'validate:module071', 'validate:module072',
    'validate:module073', 'validate:module074', 'validate:module076']
    .every((entry) => buildCommand.includes(entry)),
  'Modules 002, 059, 062, and 064-074 remain protected'
);

assertInvariant(
  'MODULE_076_CONTAINER_CONTEXT',
  dockerfile.includes('DefectTrackerModule.cs')
    && dockerfile.includes('module-076-defect-tracker')
    && dockerfile.includes('.github/ISSUE_TEMPLATE/projectpulse-defect.yml')
    && dockerfile.includes('ApprovalCenterModule.cs')
    && dockerfile.includes('IdentityProfileModule.cs')
    && dockerfile.includes('GlobalMailConfigurationModule.cs')
    && dockerfile.includes('AUGUST_PRODUCTION_READINESS_TRACKER.md'),
  'container build includes every file read by Module 076 validation'
);

assertInvariant(
  'MODULE_076_DOCUMENTATION_SET',
  readme.includes('Module 076')
    && api.includes('423 Locked')
    && authorization.includes('Administrator View-As')
    && data.includes('Identifier allocation')
    && notification.includes('ProjectPulse Help')
    && matrix.includes('076_COMPLETE_SOURCE_FAIL_CLOSED')
    && overlap.includes('Modules 002, 056E, 059, 062, and 064–074'),
  'API, authorization, data, notification, capability, and overlap docs exist'
);

assertInvariant(
  'MODULE_076_NO_INVENTED_TRACKER_ID',
  matrix.includes('No new v1.8 requirement identifier is invented'),
  'user-approved scope is recorded without fabricating a tracker code'
);

assertInvariant(
  'MODULE_076_GOVERNANCE_REGISTERED',
  catalog.includes('| 076 | Defect Intake & Resolution Tracker')
    && register.includes('| 076 | Complete source in progress')
    && tracker.includes('Module 076 — Defect Intake & Resolution Tracker')
    && tracker.includes('MODULE_076_STATUS=COMPLETE_SOURCE_IN_PROGRESS_FAIL_CLOSED'),
  'catalog, work register, and status tracker record central ownership'
);

assertInvariant(
  'MODULE_076_PROTECTED_MODULE_MARKERS',
  program.includes('app.MapApprovalCenterEndpoints();')
    && program.includes('MapIdentityProfileEndpoints(app)')
    && app.includes('MODULE_059_GLOBAL_ROUTE_HOST')
    && packageJson.includes('validate:module002')
    && packageJson.includes('validate:module062')
    && packageJson.includes('validate:module064')
    && packageJson.includes('validate:module074'),
  'protected module registration and validator markers remain present'
);

const prohibitedArtifacts = [
  ...filesBelow(path.join(repositoryRoot, 'database')),
  ...filesBelow(path.join(repositoryRoot, 'deployment'))
].filter((filePath) => /(?:module[-_]?076|defect[-_]?tracker)/i.test(path.basename(filePath)));

assertInvariant(
  'MODULE_076_NO_DATABASE_OR_DEPLOYMENT_ARTIFACT',
  prohibitedArtifacts.length === 0,
  prohibitedArtifacts.length === 0
    ? 'no Module 076 database or deployment artifact exists'
    : prohibitedArtifacts.map((filePath) => path.relative(repositoryRoot, filePath)).join(', ')
);

const failed = assertions.filter((assertion) => !assertion.condition);

console.log('');
console.log(`MODULE_076_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_076_PHASE=COMPLETE_SOURCE_FAIL_CLOSED');
console.log('MODULE_076_DATABASE_PERSISTENCE=LOCKED');
console.log('MODULE_076_MANAGER_REPORTER_EMAIL=CONTRACT_READY_DELIVERY_LOCKED');
console.log('MODULE_076_GITHUB_CLAUDE_CHATGPT_INTAKE=CONTRACT_READY_WEBHOOK_LOCKED');
console.log('MODULE_076_COMMIT_PUSH_PR_MERGE_DEPLOYMENT=NONE');

if (failed.length > 0) {
  console.error('MODULE_076_CONTRACT=FAILED');
  failed.forEach((failure) => console.error(`- ${failure.name}: ${failure.detail}`));
  process.exit(1);
}

console.log('MODULE_076_CONTRACT=PASSED');

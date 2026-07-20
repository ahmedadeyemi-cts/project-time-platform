import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/OnCallSchedulingModule.cs',
  frontend: 'src/frontend/project-time-web/src/OnCallSchedulingCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/oncall-scheduling-center.css',
  readme: 'docs/modules/module-071-oncall-scheduling/README.md',
  api: 'docs/modules/module-071-oncall-scheduling/API-CONTRACT.md',
  authorization: 'docs/modules/module-071-oncall-scheduling/AUTHORIZATION-MATRIX.md',
  source: 'docs/modules/module-071-oncall-scheduling/SOURCE-ASSET-MAPPING.md',
  matrix: 'docs/modules/module-071-oncall-scheduling/CAPABILITY-MATRIX.md',
  overlap: 'docs/modules/module-071-oncall-scheduling/OVERLAP-AND-INTEGRATION.md',
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
  console.log(`MODULE_071_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const [name, file] of Object.entries(files)) check(`${name.toUpperCase()}_EXISTS`, exists(file), file);

const backend = read(files.backend);
const frontend = read(files.frontend);
const stylesheet = read(files.stylesheet);
const docs = [files.readme, files.api, files.authorization, files.source, files.matrix, files.overlap].map(read).join('\n');
const program = read(files.program);
const app = read(files.app);
const packageJson = JSON.parse(read(files.package));
const docker = read(files.docker);
const count = (value, marker) => value.split(marker).length - 1;

check('MAP_METHOD', backend.includes('MapOnCallSchedulingEndpoints'), 'isolated endpoint map method');
check('PUBLIC_CURRENT_API', backend.includes('/api/public/v1/oncall/current'), 'versioned current-assignment API');
check('PUBLIC_SCHEDULE_API', backend.includes('/api/public/v1/oncall/schedule'), 'versioned schedule API');
check('PUBLIC_GET_ONLY', !/Map(?:Post|Put|Patch|Delete)\(\s*"\/api\/public\//.test(backend) && backend.includes('AccessControlAllowOrigin'), 'public routes are GET-only and CORS-readable');
check('AUTHENTICATED_READS', ['/capabilities', '/schedule', '/roster', '/identity-options', '/history'].every((suffix) => backend.includes(`/api/oncall-scheduling${suffix}`)), 'authenticated read surfaces');
check('MANAGEMENT_ENDPOINTS', backend.includes('MapPut') && backend.includes('AutoGenerateAsync') && backend.includes('RestoreHistoryAsync'), 'save, generate, and restore handlers');
check('MANAGER_ROLE', backend.includes('roles.Contains("MANAGER")'), 'canonical Manager role');
check('TEAM_LEAD_ROLE', backend.includes('roles.Contains("ENGINEERING_TEAM_LEAD")'), 'canonical Engineering Team Lead role');
check('PLATFORM_ADMIN_ROLES', backend.includes('roles.Contains("SUPER_ADMINISTRATOR")') && backend.includes('roles.Contains("ADMINISTRATOR")') && backend.includes('platformAdministratorAccess = true'), 'platform administrators are explicit management roles');
check('ACTUAL_SESSION_AUTHORITY', backend.includes('ProjectPulseActualUserId') && docs.includes('actual ProjectPulse session'), 'View-As does not transfer authority');
check('IDENTITY_DROPDOWN', backend.includes('identity-options') && backend.includes('app_users') && frontend.includes('<select value={person?.userId'), 'stable Module 062 identity selection');
check('IDENTITY_VALIDATION', (backend.match(/ValidateAssignedIdentitiesAsync\(/g) ?? []).length >= 3 && backend.includes('EnumerateObjects') && backend.includes('inactive_or_unknown_identity'), 'server validates schedule and roster identities');
check('DATES_EDITABLE', frontend.includes('type="datetime-local"') && docs.includes('changed at any time'), 'dates editable at any time');
check('FRIDAY_WINDOW', backend.includes('T16:00:00') && backend.includes('T07:00:00'), 'Friday 16:00 through Friday 07:00');
check('TIME_ZONE', backend.includes('America/Chicago') && docs.includes('08:00 America/Chicago'), 'DST-safe business timezone contract');
check('ROTATION_PREVIEW', backend.includes('schedule_generation_previewed') && backend.includes('persistencePerformed = false'), 'generation is preview-first');
check('HISTORY', backend.includes('/api/oncall-scheduling/history/restore') && frontend.includes('restoreSnapshot'), 'history restore surface');
check('CLOUDFLARE_ADAPTER', backend.includes('PROJECTPULSE_ONCALL_UPSTREAM_BASE_URL') && backend.includes('CF-Access-Client-Id'), 'governed compatibility adapter');
check('HTTPS_UPSTREAM', backend.includes('Uri.UriSchemeHttps'), 'HTTPS required');
check('NO_SECRET_RETURN', docs.includes('never returns either Cloudflare Access credential') && !frontend.includes('ACCESS_CLIENT_SECRET'), 'credentials remain server-only');
check('GLOBAL_SMTP', backend.includes('Module 067 Global SMTP') && docs.includes('Module 067 Global SMTP'), 'shared mail ownership');
check(
  'NO_DIRECT_PROVIDER',
  !/api\.brevo\.com|sendBrevo|BREVO_API_KEY|SMS_PROVIDER|sendFridaySMS|twilio/i.test(backend + frontend),
  'no direct legacy provider or text-message implementation'
);
check('NOTIFICATION_DAYS', ['monday', 'tuesday', 'friday'].every((value) => backend.toLowerCase().includes(value)), 'three preserved notification days');
check('US_SIGNAL_LOGO', frontend.includes('usSignalLogoDataUrl') && frontend.includes('alt="US Signal"'), 'repository-owned US Signal logo');
check('US_SIGNAL_BRAND', stylesheet.includes('--oncall-blue') && stylesheet.includes('--oncall-cyan') && stylesheet.includes('--oncall-green'), 'US Signal brand tokens');
check('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(stylesheet), 'no application-shell selector');
check('NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(root, 'database/migrations/071-oncall-scheduling.sql')), 'no migration');
check('NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\s+(?:INTO|TABLE|FROM|VIEW|INDEX|SCHEMA)\b/i.test(backend), 'database access is authorization/identity SELECT only');
check('PROGRAM_REGISTRATION', count(program, 'app.MapOnCallSchedulingEndpoints();') === 1, 'backend registered once');
check('APP_IMPORT', count(app, "import OnCallSchedulingCenter from './OnCallSchedulingCenter.jsx';") === 1, 'frontend imported once');
check('APP_MOUNT', count(app, '<OnCallSchedulingCenter authSession={authSession} />') === 1, 'frontend mounted once');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module071') && packageJson.scripts?.['validate:module071']?.includes('validate-module-071-oncall-scheduling.mjs'), 'production build guard');
check('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes('docs/modules/module-071-oncall-scheduling/'), 'container validator context');
check('SOURCE_COMMIT_RECORDED', docs.includes('da634f7620c2f76d6129020133f27481232edfbd'), 'ussignal source head recorded');
check('REQUIREMENT_RECORDED', docs.includes('RES-015'), 'tracker requirement RES-015');
check('OVERLAP_GATE', ['Module 002', 'Module 064', 'Module 067', 'Module 068'].every((value) => docs.includes(value)) && docs.includes('BLOCKED'), 'shared commit gate owners');
check('NO_EXTERNAL_MUTATION', docs.includes('Cloudflare changes: none') && docs.includes('Database changes: none'), 'external systems unchanged');

console.log('');
console.log(`MODULE_071_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_071_IMPLEMENTATION=FULL_SOURCE_CLOUDFLARE_COMPATIBILITY_PACKAGE');
console.log('MODULE_071_MANAGE_ROLES=SUPER_ADMINISTRATOR_ADMINISTRATOR_MANAGER_ENGINEERING_TEAM_LEAD');
console.log('MODULE_071_PUBLIC_API=VERSIONED_READ_ONLY');
console.log('MODULE_071_MAIL_PROVIDER=MODULE_067_GLOBAL_SMTP');
console.log('MODULE_071_RUNTIME_REGISTRATION=REGISTERED_SOURCE_DRAFT_PR_24_OPEN');
console.log('MODULE_071_AZURE_DATABASE_ENTRA_CLOUDFLARE_CHANGES=NONE');
if (checks.some((value) => !value)) {
  console.error('MODULE_071_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_071_CONTRACT=PASSED');

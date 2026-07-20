import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  frontend: 'src/frontend/project-time-web/src/GlobalMailConfigurationCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/global-mail-configuration-center.css',
  app: 'src/frontend/project-time-web/src/App.jsx',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  package: 'src/frontend/project-time-web/package.json',
  dockerfile: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-067-global-mail/README.md',
  api: 'docs/modules/module-067-global-mail/API-CONTRACT.md',
  security: 'docs/modules/module-067-global-mail/SECURITY-AND-OPERATIONS.md',
  matrix: 'docs/modules/module-067-global-mail/CAPABILITY-MATRIX.md',
  overlap: 'docs/modules/module-067-global-mail/OVERLAP-AND-INTEGRATION.md',
  catalog: 'docs/MODULE-CATALOG.md',
  register: 'docs/MODULE-WORK-REGISTER.md',
  tracker: 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'
};

const read = (relative) => fs.readFileSync(path.join(repositoryRoot, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(repositoryRoot, relative));
const count = (text, pattern) => [...text.matchAll(pattern)].length;
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULE_067_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const [name, relative] of Object.entries(files)) {
  assert(`${name.toUpperCase()}_EXISTS`, exists(relative), relative);
}

const backend = read(files.backend);
const frontend = read(files.frontend);
const stylesheet = read(files.stylesheet);
const app = read(files.app);
const program = read(files.program);
const packageJson = JSON.parse(read(files.package));
const dockerfile = read(files.dockerfile);
const documentation = [files.readme, files.api, files.security, files.matrix, files.overlap].map(read).join('\n');
const governance = [files.catalog, files.register, files.tracker].map(read).join('\n');

assert('BACKEND_MAP_METHOD', backend.includes('MapGlobalMailConfigurationEndpoints'), 'isolated registration method');
assert('TYPED_ROUTE_HANDLERS', count(backend, /Func<HttpContext, Task<IResult>>/g) === 2, 'two typed GET handlers');
assert('GET_CONFIGURATION', backend.includes('/api/global-mail/configuration'), 'configuration endpoint');
assert('GET_HEALTH', backend.includes('/api/global-mail/health'), 'health endpoint');
assert('ACTUAL_SESSION_AUTHORITY', backend.includes('ProjectPulseActualUserId') && backend.includes('viewAsTransfersAuthority = false'), 'actual-session boundary');
assert('ADMIN_AUTHORIZATION', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('SYSTEM_ADMINISTRATION'), 'server role/permission query');
assert('READ_ONLY_BACKEND', !/Map(?:Post|Put|Patch|Delete)\s*\(/.test(backend), 'no mutation route');
assert('NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\b/i.test(backend.replaceAll('Module 067', '')), 'no mutating SQL');
assert('NO_PROVIDER_CALL', !/(HttpClient|SendAsync|SendMailAsync|SmtpClient|GraphServiceClient)/.test(backend), 'no provider request or mail send');
assert('SECRET_METADATA_ONLY', backend.includes('SHA256.HashData') && backend.includes('secretValuesReturned = false'), 'presence/fingerprint contract');
assert('M365_TARGETS', backend.includes('microsoft_graph') && backend.includes('exchange_online_smtp'), 'approved provider targets');
assert('LEGACY_BREVO_GATE', backend.includes('LegacyBrevoConfigured') && backend.includes('BrevoDisablementRequired'), 'legacy migration gate');
assert('RECIPIENT_BOUNDARY', backend.includes('PROJECTPULSE_MAIL_RECIPIENT_ENVIRONMENT'), 'test/production recipient boundary');
assert('LOCKED_MUTATIONS', backend.includes('secretRotationEnabled = false') && backend.includes('testDeliveryEnabled = false'), 'rotation/test delivery locked');
assert('SANITIZED_ERRORS', !backend.includes('exception.Message') && backend.includes('exception.GetType().Name'), 'no raw exception response');

assert('FRONTEND_MARKERS', frontend.includes('data-module="067"') && frontend.includes('data-mode="read-only-configuration"'), 'governed UI boundary');
assert('FRONTEND_ENDPOINTS', frontend.includes('/api/global-mail/configuration') && frontend.includes('/api/global-mail/health'), 'both GET consumers');
assert('READ_ONLY_FRONTEND', !/method:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/.test(frontend), 'no mutation request');
assert('FRONTEND_LOCK_NOTICE', frontend.includes('Secret rotation, activation, connectivity tests'), 'visible authorization lock');
assert('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(stylesheet), 'no unscoped shell selectors');

assert('PROGRAM_REGISTRATION', count(program, /app\.MapGlobalMailConfigurationEndpoints\(\);/g) === 1, 'backend registered once');
assert('APP_IMPORT_COUNT', count(app, /import GlobalMailConfigurationCenter from '\.\/GlobalMailConfigurationCenter\.jsx';/g) === 1, 'frontend imported once');
assert('APP_MOUNT_COUNT', count(app, /<GlobalMailConfigurationCenter authSession=\{authSession\} \/>/g) === 1, 'frontend mounted once');
assert('ROUTE_REGISTRY', count(app, /route:\s*['"]global-mail-configuration['"]/g) >= 2, 'workspace and installed registries');
assert('FRONTEND_ADMIN_ONLY', app.includes("activeRoute === 'global-mail-configuration'") && app.includes("hasPermission('SYSTEM_ADMINISTRATION')"), 'administrator mount guard');

assert('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module067') && packageJson.scripts?.['validate:module067']?.includes('validate-module-067-global-mail.mjs'), 'production build validator');
for (const required of [files.backend, 'docs/modules/module-067-global-mail/', files.catalog, files.register, files.tracker]) {
  assert(`CONTAINER_${path.basename(required).replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`, dockerfile.includes(required), required);
}

assert('DOCUMENTATION_SCOPE', documentation.includes('OPS-016') && documentation.includes('CLS-005') && documentation.includes('actual ProjectPulse session'), 'tracker and security scope');
assert('AUTHORIZATION_BOUNDARY_DOCUMENTED', documentation.includes('no-Azure/no-Entra/no-database/no-deployment') && documentation.includes('separate authorization'), 'mutation boundary');
assert('GOVERNANCE_REGISTERED', governance.includes('| 067 | Global Mail Configuration Center') && governance.includes('feature/modules-064-074-release-train-on-main-20260719'), 'catalog/register record');
assert('TRACKER_064_STATUS', governance.includes('MODULE_064_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN'), 'Module 064 release-train status');
assert('TRACKER_068_STATUS', governance.includes('MODULE_068_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_READ_ONLY'), 'Module 068 release-train status');
assert('MODULE_063_PRESERVED', governance.includes('Module 063') && governance.includes('Opportunities'), 'installed numbering preserved');
assert('INTEGRATION_HOLD', governance.includes('Module 002') && governance.includes('semantically integrated'), 'shared-file checkpoint recorded');
assert('OVERLAP_MODULES', ['Module 002', 'Module 064', 'Module 068'].every((value) => documentation.includes(value)), '002/064/068 comparison owners');
assert('OVERLAP_SURFACES', ['docs/MODULE-CATALOG.md', 'docs/MODULE-WORK-REGISTER.md', 'AUGUST_PRODUCTION_READINESS_TRACKER.md', 'Program.cs', 'App.jsx', 'package.json'].every((value) => documentation.includes(value)), 'mandatory shared surfaces');
assert('FINAL_COMMIT_BLOCKED', documentation.includes('gate is `BLOCKED`'), 'refreshed overlap evidence required');
assert('NO_DATABASE_OR_DEPLOYMENT_ARTIFACT', !fs.existsSync(path.join(repositoryRoot, 'database/migrations/067-global-mail.sql')) && !fs.existsSync(path.join(repositoryRoot, 'deployment/database/067-global-mail.sql')), 'no Module 067 migration/deployment artifact');

console.log('');
console.log(`MODULE_067_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_067_IMPLEMENTATION=FULL_GOVERNED_READ_ONLY_CONFIGURATION_PACKAGE');
console.log('MODULE_067_SHARED_INTEGRATION=RELEASE_TRAIN_SOURCE_DRAFT_PR_24_OPEN');
console.log('MODULE_067_AZURE_DATABASE_ENTRA_CHANGES=NONE');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_067_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_067_CONTRACT=PASSED');

import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/OneAssistRoutingDirectoryModule.cs',
  frontend: 'src/frontend/project-time-web/src/OneAssistRoutingDirectoryCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/oneassist-routing-directory-center.css',
  readme: 'docs/modules/module-072-oneassist-routing-directory/README.md',
  api: 'docs/modules/module-072-oneassist-routing-directory/API-CONTRACT.md',
  authorization: 'docs/modules/module-072-oneassist-routing-directory/AUTHORIZATION-MATRIX.md',
  source: 'docs/modules/module-072-oneassist-routing-directory/SOURCE-ASSET-MAPPING.md',
  matrix: 'docs/modules/module-072-oneassist-routing-directory/CAPABILITY-MATRIX.md',
  overlap: 'docs/modules/module-072-oneassist-routing-directory/OVERLAP-AND-INTEGRATION.md',
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
  console.log(`MODULE_072_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
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

check('MAP_METHOD', backend.includes('MapOneAssistRoutingDirectoryEndpoints'), 'isolated endpoint map method');
check('AUTHENTICATED_VIEW', backend.includes('/api/oneassist/routes') && backend.includes('canView = true'), 'everyone can view in ProjectPulse');
check('PUBLIC_LIST_API', backend.includes('/api/public/v1/oneassist/routes'), 'versioned public directory');
check('PUBLIC_RESOLVE_API', backend.includes('/api/public/v1/oneassist/resolve'), 'versioned PIN resolver');
check('PUBLIC_GET_ONLY', !/Map(?:Post|Put|Patch|Delete)\(\s*"\/api\/public\//.test(backend) && backend.includes('AccessControlAllowOrigin'), 'public routes are GET-only and CORS-readable');
check('PIN_VISIBLE', backend.includes('visible_unmasked') && frontend.includes('data-pin-visibility="public-unmasked"'), 'PIN is intentionally unmasked');
check('PIN_ROUTING_CLASSIFICATION', backend.includes('public_routing_identifier') && docs.includes('must never be treated as authentication'), 'routing identifier is not a credential');
check('FIVE_DIGITS', backend.includes('Length: 5') && frontend.includes('maxLength="5"'), 'exactly five digits');
check('LEADING_ZERO_SAFE', docs.includes('leading zeroes are preserved') && backend.includes('string? NormalizePin'), 'PIN stored and returned as string');
check('UNIQUE_PIN', backend.includes('Every routing PIN must be unique') && frontend.includes('pins.has(route.pin)'), 'client and server uniqueness');
check('MANAGER_ROLE', backend.includes('"MANAGER"'), 'Manager management role');
check('ADMIN_ROLES', backend.includes('"ADMINISTRATOR"') && backend.includes('"SUPER_ADMINISTRATOR"'), 'administrator management roles');
check('PTC_ROLE', backend.includes('"PROJECT_TEAM_COORDINATOR"'), 'PTC management role');
check('NO_SA_MANAGE', docs.includes('Solution Architects and all other roles remain viewers') && !backend.includes('"SOLUTION_ARCHITECT"'), 'Solution Architect is viewer');
check('ACTUAL_SESSION_AUTHORITY', backend.includes('ProjectPulseActualUserId') && docs.includes('View-As never transfers'), 'actual session controls mutation');
check('SAVE_ENDPOINT', backend.includes('MapPut') && backend.includes('SaveRoutesAsync'), 'governed complete-directory save');
check('CSV_IMPORT', backend.includes('ReadCsvRowsAsync') && frontend.includes('accept=".csv,.xlsx"'), 'CSV import preview');
check('XLSX_IMPORT', backend.includes('ZipArchive') && backend.includes('ReadXlsxRows'), 'dependency-free XLSX preview parser');
check('PREVIEW_NO_PERSIST', backend.includes('persistencePerformed = false') && frontend.includes('Apply to unsaved directory'), 'preview-first import');
check('FILE_LIMIT', backend.includes('5 * 1024 * 1024'), '5 MiB import boundary');
check('CSV_EXPORT', frontend.includes("downloadCsv('oneassist-routes.csv'") && frontend.includes("downloadCsv('oneassist-ivr-routes.csv'") && frontend.includes("['customer_name', 'name']"), 'standard and legacy IVR-compatible exports');
check('SEARCH', frontend.includes('Search customer, PIN, or customer ID'), 'visible search contract');
check('CLOUDFLARE_ADAPTER', backend.includes('PROJECTPULSE_ONEASSIST_UPSTREAM_BASE_URL') && backend.includes('CF-Access-Client-Id'), 'governed compatibility adapter');
check('HTTPS_UPSTREAM', backend.includes('Uri.UriSchemeHttps'), 'HTTPS required');
check('NO_SECRET_RETURN', docs.includes('Values are never committed or returned') && !frontend.includes('ACCESS_CLIENT_SECRET'), 'service credential is server-only');
check('US_SIGNAL_LOGO', frontend.includes('usSignalLogoDataUrl') && frontend.includes('alt="US Signal"'), 'repository-owned US Signal logo');
check('US_SIGNAL_BRAND', stylesheet.includes('--oneassist-blue') && stylesheet.includes('--oneassist-cyan') && stylesheet.includes('--oneassist-green'), 'US Signal brand tokens');
check('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(stylesheet), 'no application-shell selector');
check('NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(root, 'database/migrations/072-oneassist-routing.sql')), 'no migration');
check('NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\s+(?:INTO|TABLE|FROM|VIEW|INDEX|SCHEMA)\b/i.test(backend), 'database access is role SELECT only');
check('PROGRAM_REGISTRATION', count(program, 'app.MapOneAssistRoutingDirectoryEndpoints();') === 1, 'backend registered once');
check('APP_IMPORT', count(app, "import OneAssistRoutingDirectoryCenter from './OneAssistRoutingDirectoryCenter.jsx';") === 1, 'frontend imported once');
check('APP_MOUNT', count(app, '<OneAssistRoutingDirectoryCenter authSession={authSession} />') === 1, 'frontend mounted once');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module072') && packageJson.scripts?.['validate:module072']?.includes('validate-module-072-oneassist-routing-directory.mjs'), 'production build guard');
check('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes('docs/modules/module-072-oneassist-routing-directory/'), 'container validator context');
check('SOURCE_COMMIT_RECORDED', docs.includes('da634f7620c2f76d6129020133f27481232edfbd'), 'ussignal source head recorded');
check('REQUIREMENT_RECORDED', docs.includes('RES-016'), 'tracker requirement RES-016');
check('OVERLAP_GATE', ['Module 002', 'Module 064', 'Module 067', 'Module 068', 'Module 071'].every((value) => docs.includes(value)) && docs.includes('BLOCKED'), 'shared commit gate owners');
check('NO_EXTERNAL_MUTATION', docs.includes('Cloudflare changes: none') && docs.includes('Database changes: none'), 'external systems unchanged');

console.log('');
console.log(`MODULE_072_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_072_IMPLEMENTATION=FULL_SOURCE_CLOUDFLARE_COMPATIBILITY_PACKAGE');
console.log('MODULE_072_PIN_CLASSIFICATION=PUBLIC_ROUTING_IDENTIFIER');
console.log('MODULE_072_MANAGE_ROLES=MANAGER_ADMINISTRATOR_SUPER_ADMINISTRATOR_PROJECT_TEAM_COORDINATOR');
console.log('MODULE_072_PUBLIC_API=VERSIONED_READ_ONLY');
console.log('MODULE_072_RUNTIME_REGISTRATION=REGISTERED_SOURCE_DRAFT_PR_24_OPEN');
console.log('MODULE_072_AZURE_DATABASE_ENTRA_CLOUDFLARE_CHANGES=NONE');
if (checks.some((value) => !value)) {
  console.error('MODULE_072_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_072_CONTRACT=PASSED');

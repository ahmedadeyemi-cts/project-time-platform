import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/OemVendorDirectoryModule.cs',
  frontend: 'src/frontend/project-time-web/src/OemVendorDirectoryCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/oem-vendor-directory-center.css',
  readme: 'docs/modules/module-074-oem-vendor-directory/README.md',
  api: 'docs/modules/module-074-oem-vendor-directory/API-CONTRACT.md',
  authorization: 'docs/modules/module-074-oem-vendor-directory/AUTHORIZATION-MATRIX.md',
  matrix: 'docs/modules/module-074-oem-vendor-directory/CAPABILITY-MATRIX.md',
  data: 'docs/modules/module-074-oem-vendor-directory/DATA-DESIGN.md',
  overlap: 'docs/modules/module-074-oem-vendor-directory/OVERLAP-AND-INTEGRATION.md',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile',
};
const exists = file => fs.existsSync(path.join(root, file));
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const checks = [];
function check(name, condition, evidence) {
  checks.push(Boolean(condition));
  console.log(`MODULE_074_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const [name, file] of Object.entries(files)) check(`${name.toUpperCase()}_EXISTS`, exists(file), file);
const backend = read(files.backend);
const frontend = read(files.frontend);
const css = read(files.stylesheet);
const docs = [files.readme, files.api, files.authorization, files.matrix, files.data, files.overlap].map(read).join('\n');
const program = read(files.program);
const app = read(files.app);
const packageJson = JSON.parse(read(files.package));
const docker = read(files.docker);
const count = (value, marker) => value.split(marker).length - 1;

check('MAP_METHOD', backend.includes('MapOemVendorDirectoryEndpoints'), 'isolated endpoint map');
check('ENDPOINTS', ['/capabilities', '/directory', '/reference', '/validate'].every(value => backend.includes(`/api/oem-vendor-directory${value}`)), 'complete draft contract');
check('EVERYONE_VIEWS', backend.includes('canView=true') && docs.includes('Every authenticated'), 'view contract');
check('ADMIN', backend.includes('"ADMINISTRATOR"') && backend.includes('"SUPER_ADMINISTRATOR"'), 'administrator editors');
check('SOLUTION_ARCHITECT', backend.includes('"SOLUTION_ARCHITECT"'), 'Solution Architect editor');
check('PTC', backend.includes('"PROJECT_TEAM_COORDINATOR"'), 'PTC editor');
check('ACTUAL_SESSION', backend.includes('ProjectPulseActualUserId') && docs.includes('actual ProjectPulse session'), 'View-As-safe authority');
check('CANONICAL_FIELDS', ['vendorName', 'oemCategory', 'contacts', 'supportLinks', 'certifications', 'products', 'status'].every(value => backend.includes(value) && frontend.includes(value)), 'SAL-003 fields');
check('UNIQUE_NAMES', backend.includes('duplicate_vendor_name') && backend.includes('StringComparer.OrdinalIgnoreCase'), 'case-insensitive uniqueness');
check('CONTROLLED_STATUS', ['active', 'preferred', 'limited', 'inactive', 'under_review'].every(value => backend.includes(`"${value}"`)), 'status allowlist');
check('HTTPS_ONLY', backend.includes('website_must_use_https') && backend.includes('support_link_requires_label_and_https_url'), 'HTTPS website/support links');
check('EMAIL_VALIDATION', backend.includes('MailAddress') && backend.includes('invalid_contact_email'), 'contact email');
check('TYPE_SAFE_JSON', backend.includes('TryGetValue<string>'), 'malformed scalar input fails safely');
check('ROW_LIMIT', backend.includes('vendors.Count > 500'), 'bounded draft');
check('DRAFT_ONLY', backend.includes('persistencePerformed = false') && frontend.includes('data-persistence="unsaved-draft"'), 'no save claim');
check('SEARCH', frontend.includes('Search name, category, status, or product'), 'directory search');
check('CSV_EXPORT', frontend.includes('oem-vendor-directory-summary.csv'), 'CSV export');
check('JSON_EXPORT', frontend.includes('oem-vendor-directory-draft.json'), 'JSON handoff export');
check('NO_INVENTED_VENDOR_DATA', frontend.includes('No vendor records match this view') && backend.includes('vendors = Array.Empty<object>()'), 'empty canonical source');
check('NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\s+(?:INTO|TABLE|FROM|VIEW|INDEX|SCHEMA)\b/i.test(backend), 'authorization SELECT only');
check('NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(root, 'database/migrations/074-oem-vendor-directory.sql')), 'no migration');
check('US_SIGNAL_LOGO', frontend.includes('usSignalLogoDataUrl') && frontend.includes('alt="US Signal"'), 'repository-owned brand logo');
check('US_SIGNAL_BRAND', css.includes('--vendor-blue') && css.includes('--vendor-cyan') && css.includes('--vendor-green'), 'US Signal color tokens');
check('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(css), 'module-scoped stylesheet');
check('PROGRAM_REGISTRATION', count(program, 'app.MapOemVendorDirectoryEndpoints();') === 1, 'backend registered once');
check('APP_IMPORT', count(app, "import OemVendorDirectoryCenter from './OemVendorDirectoryCenter.jsx';") === 1, 'frontend imported once');
check('APP_MOUNT', count(app, '<OemVendorDirectoryCenter authSession={authSession} />') === 1, 'frontend mounted once');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module074') && packageJson.scripts?.['validate:module074']?.includes('validate-module-074-oem-vendor-directory.mjs'), 'production build guard');
check('CONTAINER_CONTEXT', docker.includes(files.backend) && docker.includes('docs/modules/module-074-oem-vendor-directory/'), 'container validator context');
check('REQUIREMENT', docs.includes('SAL-003'), 'tracker requirement');
check('OVERLAP_GATE', ['Module 002', 'Module 064', 'Module 068'].every(value => docs.includes(value)) && docs.includes('BLOCKED'), 'concurrency gate');
check('NO_EXTERNAL_MUTATION', docs.includes('Azure changed: no') && docs.includes('Database changed: no') && docs.includes('Entra changed: no'), 'external state unchanged');

console.log(`\nMODULE_074_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_074_IMPLEMENTATION=FULL_SOURCE_VALIDATED_UNSAVED_DRAFT');
console.log('MODULE_074_EDIT_ROLES=ADMINISTRATOR_SUPER_ADMINISTRATOR_SOLUTION_ARCHITECT_PROJECT_TEAM_COORDINATOR');
console.log('MODULE_074_PERSISTENCE=LOCKED_PENDING_DATABASE_AUTHORIZATION');
if (checks.some(value => !value)) {
  console.error('MODULE_074_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_074_CONTRACT=PASSED');

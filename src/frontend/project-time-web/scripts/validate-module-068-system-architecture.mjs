import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');
const absolute = (relative) => path.join(repositoryRoot, relative);
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const optional = (relative) => fs.existsSync(absolute(relative)) ? read(relative) : '';
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULE_068_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const frontend = read('src/frontend/project-time-web/src/SystemArchitectureCenter.jsx');
const stylesheet = read('src/frontend/project-time-web/src/system-architecture-center.css');
const legacyBackend = read('src/backend/ProjectTime.Api/Modules/SystemArchitectureModule.cs');
const app = read('src/frontend/project-time-web/src/App.jsx');
const packageJson = read('src/frontend/project-time-web/package.json');
const project = optional('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const contracts = optional('src/backend/ProjectTime.Api/Modules/PlatformOperationsContracts.cs');
const operations = optional('src/backend/ProjectTime.Api/Modules/PlatformOperationsModule.cs');
const architectureBackend = optional('src/backend/ProjectTime.Api/Modules/PlatformOperationsArchitecture.cs');
const fullProviderContract = Boolean(contracts && operations && architectureBackend);

check('LEGACY_BACKEND_PRESERVED',
  legacyBackend.includes('MapSystemArchitectureEndpoints')
    && legacyBackend.includes('/api/system-architecture/overview')
    && legacyBackend.includes('/api/system-architecture/dependency-status')
    && !/\.Map(?:Post|Put|Patch|Delete)\s*\(/.test(legacyBackend),
  'existing read-only architecture routes remain available as a compatibility fallback');
check('ACTUAL_SESSION_AUTHORITY',
  legacyBackend.includes('ProjectPulseActualUserId')
    && legacyBackend.includes('ProjectPulseSessionUserId')
    && !legacyBackend.includes('"ProjectPulseEffectiveUserId"'),
  'View-As effective identity cannot supply administrator authority');
check('PROVIDER_NEUTRAL_PRIMARY_ENDPOINT',
  frontend.includes("readJson('/api/platform-operations/architecture'")
    && frontend.includes("readJson('/api/system-architecture/overview'")
    && frontend.includes("readJson('/api/system-architecture/dependency-status'"),
  'Module 068 uses the shared contract first and retains the legacy fallback');
check('OFFICIAL_BRANDING',
  frontend.includes('usSignalLogoDataUrl')
    && frontend.includes('alt="US Signal"')
    && frontend.includes('Export branded architecture'),
  'approved US Signal branding and export action are visible');
check('PROVIDER_ADAPTER_PRESENTATION',
  frontend.includes('ProjectPulse Platform Operations')
    && frontend.includes('Azure adapter')
    && frontend.includes('OpenCloud adapter')
    && frontend.includes('Other provider adapter'),
  'current Azure and future provider adapters share one visual contract');
check('MODULE_API_RELATIONSHIPS',
  frontend.includes('Module-to-API relationships')
    && frontend.includes('moduleApiRelationships')
    && frontend.includes('apiAppendix'),
  'live module/API ownership and appendix data are displayed');
check('READ_ONLY_FRONTEND',
  frontend.includes("method: 'GET'")
    && !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(frontend)
    && !/<form\b/i.test(frontend)
    && frontend.includes('data-mode="read-only"'),
  'Module 068 performs GET observation, local filtering, and GET export only');
check('SCOPED_RESPONSIVE_STYLES',
  stylesheet.includes('.system-architecture-center')
    && stylesheet.includes('.provider-adapter-map')
    && stylesheet.includes('.module-api-relationship-list')
    && stylesheet.includes('@media (max-width: 700px)')
    && !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select)\s*[{,]/m.test(stylesheet),
  'architecture styling is module-scoped and responsive');
check('APP_MOUNT_PRESERVED',
  (app.match(/import SystemArchitectureCenter from '\.\/SystemArchitectureCenter\.jsx';/g) ?? []).length === 1
    && (app.match(/<SystemArchitectureCenter authSession=\{authSession\} \/>/g) ?? []).length === 1,
  'the component remains imported and mounted exactly once');
check('BUILD_GUARD',
  packageJson.includes('validate:module068')
    && packageJson.includes('validate-module-068-system-architecture.mjs')
    && packageJson.includes('npm run validate:module068'),
  'the existing production build continues running this compatibility validator');

if (fullProviderContract) {
  check('FULL_PROVIDER_CONTRACT',
    contracts.includes('private interface IPlatformAdapter')
      && contracts.includes('"azure_adapter"')
      && contracts.includes('"opencloud_adapter"')
      && operations.includes('/api/platform-operations/apis')
      && operations.includes('/api/platform-operations/evidence')
      && architectureBackend.includes('/api/platform-operations/architecture/export')
      && architectureBackend.includes('Created by Ahmed Adeyemi')
      && project.includes('app.UsePlatformOperationsTelemetry();')
      && project.includes('app.MapPlatformOperationsEndpoints();'),
    'full repository context contains the shared provider, diagnostics, evidence, and export contract');
} else {
  console.log('MODULE_068_FULL_PROVIDER_CONTRACT=SKIPPED_FRONTEND_CONTAINER_CONTEXT');
}

console.log(`MODULE_068_VALIDATION_CHECKS=${checks.length}`);
console.log(`MODULE_068_FULL_REPOSITORY_CONTEXT=${fullProviderContract ? 'YES' : 'NO'}`);
const failed = checks.filter((item) => !item.condition);
if (failed.length) {
  console.error('MODULE_068_CONTRACT=FAILED');
  failed.forEach((item) => console.error(`- ${item.name}: ${item.evidence}`));
  process.exit(1);
}
console.log('MODULE_068_CONTRACT=PASSED');

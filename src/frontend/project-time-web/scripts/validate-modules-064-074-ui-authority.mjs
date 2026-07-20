import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const components = {
  '064': 'src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx',
  '065': 'src/frontend/project-time-web/src/EntraSecretAdministrationCenter.jsx',
  '066': 'src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx',
  '067': 'src/frontend/project-time-web/src/GlobalMailConfigurationCenter.jsx',
  '068': 'src/frontend/project-time-web/src/SystemArchitectureCenter.jsx',
  '069': 'src/frontend/project-time-web/src/QualificationsCertificationCenter.jsx',
  '070': 'src/frontend/project-time-web/src/CapacityPipelineForecastCenter.jsx',
  '071': 'src/frontend/project-time-web/src/OnCallSchedulingCenter.jsx',
  '072': 'src/frontend/project-time-web/src/OneAssistRoutingDirectoryCenter.jsx',
  '073': 'src/frontend/project-time-web/src/SalesCoverageAlignmentCenter.jsx',
  '074': 'src/frontend/project-time-web/src/OemVendorDirectoryCenter.jsx'
};

const files = {
  sharedStyles: 'src/frontend/project-time-web/src/projectpulse-module-standard.css',
  module065: 'src/backend/ProjectTime.Api/Modules/EntraSecretAdministrationModule.cs',
  module071: 'src/backend/ProjectTime.Api/Modules/OnCallSchedulingModule.cs',
  module065Authorization: 'docs/modules/module-065-entra-secret-administration/AUTHORIZATION-MATRIX.md',
  module071Authorization: 'docs/modules/module-071-oncall-scheduling/AUTHORIZATION-MATRIX.md',
  tracker: 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md',
  package: 'src/frontend/project-time-web/package.json'
};

const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const checks = [];

function check(name, condition, evidence) {
  checks.push(Boolean(condition));
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const [moduleNumber, relative] of Object.entries(components)) {
  const source = read(relative);
  check(
    `MODULE_${moduleNumber}_SHARED_STYLE`,
    source.includes("import './projectpulse-module-standard.css';"),
    relative
  );
  check(
    `MODULE_${moduleNumber}_STANDARD_ROOT`,
    source.includes('projectpulse-module-standard')
      && source.includes(`data-module="${moduleNumber}"`)
      && source.includes('data-brand="us-signal"'),
    'shared root and US Signal brand contract'
  );
  check(
    `MODULE_${moduleNumber}_REPOSITORY_LOGO`,
    source.includes('usSignalLogoDataUrl')
      || source.includes('ussignal.png')
      || source.includes('brand/ussignal'),
    'repository-owned US Signal logo reference'
  );
}

const sharedStyles = read(files.sharedStyles);
const module065 = read(files.module065);
const module071 = read(files.module071);
const module065Authorization = read(files.module065Authorization);
const module071Authorization = read(files.module071Authorization);
const tracker = read(files.tracker);
const packageJson = JSON.parse(read(files.package));

check(
  'SHARED_STYLE_SCOPED',
  sharedStyles.includes('.projectpulse-module-standard')
    && !/(^|\n)\s*(body|html|\.app-shell|\.sidebar|\.topbar)\s*\{/m.test(sharedStyles),
  'shared stylesheet does not own the application shell'
);
check(
  'SHARED_US_SIGNAL_TOKENS',
  ['--pp-module-navy', '--pp-module-blue', '--pp-module-cyan', '--pp-module-green']
    .every((marker) => sharedStyles.includes(marker)),
  'US Signal module design tokens'
);
check(
  'MODULE_065_PLATFORM_ADMIN_AUTHORITY',
  module065.includes('roles.Contains("SUPER_ADMINISTRATOR")')
    && module065.includes('roles.Contains("ADMINISTRATOR")')
    && module065.includes('permissions.Contains(DelegatedPermission)'),
  'platform administrators and delegated permission'
);
check(
  'MODULE_065_VIEW_AS_GUARD',
  module065.includes('IsViewAs(context)')
    && module065.includes('actual_session_required'),
  'View-As still cannot transfer mutation authority'
);
check(
  'MODULE_071_PLATFORM_ADMIN_AUTHORITY',
  module071.includes('roles.Contains("SUPER_ADMINISTRATOR")')
    && module071.includes('roles.Contains("ADMINISTRATOR")')
    && module071.includes('roles.Contains("MANAGER")')
    && module071.includes('roles.Contains("ENGINEERING_TEAM_LEAD")')
    && module071.includes('platformAdministratorAccess = true'),
  'approved management role set'
);
check(
  'MODULE_071_ACTUAL_SESSION_AUTHORITY',
  module071.includes('ProjectPulseActualUserId')
    && module071.includes('ProjectPulseEffectiveUserId'),
  'actual/effective identity boundary'
);
check(
  'MODULE_065_AUTHORIZATION_DOCUMENTED',
  module065Authorization.includes('MODULES_064_074_PLATFORM_ADMIN_ALIGNMENT'),
  'Module 065 authority alignment'
);
check(
  'MODULE_071_AUTHORIZATION_DOCUMENTED',
  module071Authorization.includes('MODULES_064_074_PLATFORM_ADMIN_ALIGNMENT'),
  'Module 071 authority alignment'
);
check(
  'TRACKER_ALIGNMENT_RECORDED',
  tracker.includes('MODULES_064_074_UI_AUTHORITY_ALIGNMENT'),
  'production-readiness checkpoint'
);
check(
  'BUILD_GUARD',
  packageJson.scripts?.build?.includes('validate:modules064074-ui')
    && packageJson.scripts?.['validate:modules064074-ui']
      === 'node ./scripts/validate-modules-064-074-ui-authority.mjs',
  'production build guard'
);

console.log(`\nMODULES_064_074_UI_AUTHORITY_CHECKS=${checks.length}`);
if (checks.some((value) => !value)) {
  console.error('MODULES_064_074_UI_AUTHORITY_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULES_064_074_UI_AUTHORITY_CONTRACT=PASSED');
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const appPath = path.join(repoRoot, 'src/frontend/project-time-web/src/App.jsx');
const registryPath = path.join(repoRoot, 'src/frontend/project-time-web/src/module-availability-registry.js');
const availabilityPath = path.join(repoRoot, 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs');
const programPath = path.join(repoRoot, 'src/backend/ProjectTime.Api/Program.cs');

function requireFile(filePath, label) {
  if (!fs.existsSync(filePath)) throw new Error(`Module 084 injection requires ${label}: ${filePath}`);
}

function insertAfter(source, anchor, addition, label, marker = addition.trim()) {
  if (source.includes(marker)) return source;
  if (!source.includes(anchor)) throw new Error(`Module 084 injection anchor is missing: ${label}`);
  return source.replace(anchor, `${anchor}\n${addition}`);
}

function insertBefore(source, anchor, addition, label, marker = addition.trim()) {
  if (source.includes(marker)) return source;
  if (!source.includes(anchor)) throw new Error(`Module 084 injection anchor is missing: ${label}`);
  return source.replace(anchor, `${addition}\n${anchor}`);
}

function patchApp() {
  requireFile(appPath, 'App.jsx');
  let source = fs.readFileSync(appPath, 'utf8');

  const importAnchor = source.includes("import FullFutureLoopCenter from './FullFutureLoopCenter.jsx';")
    ? "import FullFutureLoopCenter from './FullFutureLoopCenter.jsx';"
    : "import ProjectRiskRegisterCenter from './ProjectRiskRegisterCenter.jsx';";
  source = insertAfter(
    source,
    importAnchor,
    "import CelarAiRuntimeVersionCenter from './CelarAiRuntimeVersionCenter.jsx';",
    'late-module import',
    "import CelarAiRuntimeVersionCenter from './CelarAiRuntimeVersionCenter.jsx';"
  );

  const route = `      {/* MODULE_084_CELAR_RUNTIME_VERSION_ROUTE_START */}
      {(activeRoute === 'celar-ai-runtime-version' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="celar-ai-runtime-version" className="panel celar-ai-runtime-version-route-panel">
          <CelarAiRuntimeVersionCenter />
        </section>
      ) : null}
      {/* MODULE_084_CELAR_RUNTIME_VERSION_ROUTE_END */}`;
  source = insertBefore(
    source,
    '      {/* MODULES_075_080_RUNTIME_ROUTES_END */}',
    route,
    'runtime route boundary',
    'MODULE_084_CELAR_RUNTIME_VERSION_ROUTE_START'
  );

  const navDefinition = `  /* MODULE_084_CELAR_RUNTIME_VERSION_NAV_START */
  {
    route: 'celar-ai-runtime-version',
    href: '#celar-ai-runtime-version',
    title: 'Celar AI Runtime & Version Center',
    navLabel: 'MODULE 084',
    description: 'View private Oracle Celar engine and model versions, update evidence, rollback readiness, and the governed automatic maintenance window.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'SYSTEM_ADMINISTRATOR']
  },
  /* MODULE_084_CELAR_RUNTIME_VERSION_NAV_END */`;
  if (source.includes('MODULE_083_FULL_FUTURE_LOOP_NAV_END')) {
    source = insertAfter(
      source,
      '  /* MODULE_083_FULL_FUTURE_LOOP_NAV_END */',
      navDefinition,
      'Module 083 navigation boundary',
      'MODULE_084_CELAR_RUNTIME_VERSION_NAV_START'
    );
  } else {
    const module082NavDefinition = `  {
    route: 'project-risk-register',
    href: '#project-risk-register',
    title: 'Enterprise Project Risk Register',
    navLabel: 'MODULE 082',
    description: 'Identify, analyze, respond to, review, realize, close, and export project risks and opportunities within authoritative project scope.',
    permissions: ['VIEW_PROJECT_RISKS_082', 'MANAGE_PROJECT_RISKS_082', 'UPDATE_ASSIGNED_RISK_ACTIONS_082', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD', 'ENGINEERING_MANAGER', 'ENGINEERING_LEAD', 'ENGINEER', 'ENGINEERING']
  },`;
    source = insertAfter(
      source,
      module082NavDefinition,
      navDefinition,
      'Module 082 navigation definition',
      'MODULE_084_CELAR_RUNTIME_VERSION_NAV_START'
    );
  }

  const installedDefinition = `  /* MODULE_084_CELAR_RUNTIME_VERSION_INSTALLED_REGISTRY_START */
  {
    route: 'celar-ai-runtime-version',
    title: 'Celar AI Runtime & Version Center',
    navLabel: 'MODULE 084',
    status: 'Protected Test runtime administration',
    group: 'Platform Operations',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Administrator-only visibility and governed scheduling for the private Oracle Celar runtime without changing Module 064 provider order.'
  },
  /* MODULE_084_CELAR_RUNTIME_VERSION_INSTALLED_REGISTRY_END */`;
  if (source.includes('MODULE_083_FULL_FUTURE_LOOP_INSTALLED_REGISTRY_END')) {
    source = insertAfter(
      source,
      '  /* MODULE_083_FULL_FUTURE_LOOP_INSTALLED_REGISTRY_END */',
      installedDefinition,
      'Module 083 installed registry boundary',
      'MODULE_084_CELAR_RUNTIME_VERSION_INSTALLED_REGISTRY_START'
    );
  } else {
    const module082InstalledDefinition = `  {
    route: 'project-risk-register',
    title: 'Enterprise Project Risk Register',
    navLabel: 'MODULE 082',
    status: 'Enterprise operational release',
    group: 'Project Delivery',
    permissions: ['VIEW_PROJECT_RISKS_082', 'MANAGE_PROJECT_RISKS_082', 'UPDATE_ASSIGNED_RISK_ACTIONS_082', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides PMI-aligned risk identification, analysis, response actions, heatmaps, review governance, decisions, versions, audit, and evidence exports.'
  },`;
    source = insertAfter(
      source,
      module082InstalledDefinition,
      installedDefinition,
      'Module 082 installed registry definition',
      'MODULE_084_CELAR_RUNTIME_VERSION_INSTALLED_REGISTRY_START'
    );
  }

  const groupAnchor = source.includes("    case 'full-future-loop':")
    ? "    case 'full-future-loop':"
    : "    case 'release-deployment-control':";
  source = insertAfter(
    source,
    groupAnchor,
    "    case 'celar-ai-runtime-version':",
    'Platform Operations navigation group',
    "    case 'celar-ai-runtime-version':"
  );

  const exclusionAnchor = source.includes("        'full-future-loop',")
    ? "        'full-future-loop',"
    : "        'project-risk-register',";
  source = insertAfter(
    source,
    exclusionAnchor,
    "        'celar-ai-runtime-version',",
    'standalone route exclusion list',
    "        'celar-ai-runtime-version',"
  );

  const expectations = [
    ["import CelarAiRuntimeVersionCenter from './CelarAiRuntimeVersionCenter.jsx';", 1, 'imports'],
    ['MODULE_084_CELAR_RUNTIME_VERSION_ROUTE_START', 1, 'routes'],
    ['MODULE_084_CELAR_RUNTIME_VERSION_NAV_START', 1, 'nav'],
    ['MODULE_084_CELAR_RUNTIME_VERSION_INSTALLED_REGISTRY_START', 1, 'installed'],
    ["    case 'celar-ai-runtime-version':", 1, 'groups'],
    ["        'celar-ai-runtime-version',", 1, 'exclusions']
  ];
  for (const [marker, expected, label] of expectations) {
    const count = source.split(marker).length - 1;
    if (count !== expected) throw new Error(`Module 084 App injection is not idempotent: ${label}=${count}`);
  }
  fs.writeFileSync(appPath, source, 'utf8');
}

function patchRegistry() {
  requireFile(registryPath, 'module availability registry');
  let source = fs.readFileSync(registryPath, 'utf8');
  const definition = "  Object.freeze({ moduleNumber: '084', route: 'celar-ai-runtime-version', displayName: 'Celar AI Runtime & Version Center', group: 'Platform Operations', description: 'Administrator-only private Oracle Celar engine/model version visibility, automatic update evidence, rollback readiness, and governed Central-time maintenance scheduling.' }),";
  const anchor = source.includes("moduleNumber: '083'")
    ? "  Object.freeze({ moduleNumber: '083', route: 'full-future-loop', displayName: 'Full Future Loop', group: 'Platform Operations', description: 'Governed persistent sandbox for selective governance, private development, canary verification, curated promotion, read-only production evidence, support, repair, re-promotion, and final verification.' }),"
    : "  Object.freeze({ moduleNumber: '082', route: 'project-risk-register', displayName: 'Enterprise Project Risk Register', group: 'Project Delivery', description: 'PMI-aligned project risks, opportunities, response actions, exposure heatmaps, review cadence, governed decisions, and immutable evidence.' }),";
  source = insertAfter(source, anchor, definition, 'late module registry definition', "moduleNumber: '084'");
  if ((source.split("moduleNumber: '084'").length - 1) !== 1) throw new Error('Module 084 registry injection produced a duplicate or missing definition.');
  fs.writeFileSync(registryPath, source, 'utf8');
}

function patchBackendAvailability() {
  requireFile(availabilityPath, 'ModuleAvailabilityModule.cs');
  let source = fs.readFileSync(availabilityPath, 'utf8');
  const definition = '            ["084"] = Module("084", "celar-ai-runtime-version", "Celar AI Runtime & Version Center", "Platform Operations"),';
  const anchor = source.includes('["083"] = Module("083"')
    ? '            ["083"] = Module("083", "full-future-loop", "Full Future Loop", "Platform Operations"),'
    : '            ["082"] = Module("082", "project-risk-register", "Enterprise Project Risk Register", "Project Delivery"),';
  source = insertAfter(source, anchor, definition, 'late backend availability definition', '["084"] = Module("084"');
  if ((source.split('["084"] = Module("084"').length - 1) !== 1) throw new Error('Module 084 backend availability injection produced a duplicate or missing definition.');
  fs.writeFileSync(availabilityPath, source, 'utf8');
}

function patchBackendEndpointMap() {
  requireFile(programPath, 'Program.cs');
  let source = fs.readFileSync(programPath, 'utf8');
  const mapping = `/* MODULE_084_CELAR_RUNTIME_VERSION_ENDPOINT_MAP_START */
app.MapCelarAiRuntimeVersionEndpoints();
/* MODULE_084_CELAR_RUNTIME_VERSION_ENDPOINT_MAP_END */`;
  source = insertBefore(
    source,
    '/* MODULE_998_SYSTEM_DIAGNOSTIC_ENDPOINT_MAP_START */',
    mapping,
    'central backend endpoint map',
    'MODULE_084_CELAR_RUNTIME_VERSION_ENDPOINT_MAP_START'
  );
  if ((source.split('app.MapCelarAiRuntimeVersionEndpoints();').length - 1) !== 1) {
    throw new Error('Module 084 backend endpoint mapping produced a duplicate or missing registration.');
  }
  fs.writeFileSync(programPath, source, 'utf8');
}

patchApp();
patchRegistry();
patchBackendAvailability();
patchBackendEndpointMap();
console.log('MODULE_084_CELAR_RUNTIME_VERSION_INJECTION=COMPLETE');

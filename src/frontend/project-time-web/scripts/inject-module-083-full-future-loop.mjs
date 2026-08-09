import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const appPath = path.join(repoRoot, 'src/frontend/project-time-web/src/App.jsx');
const registryPath = path.join(repoRoot, 'src/frontend/project-time-web/src/module-availability-registry.js');
const availabilityPath = path.join(repoRoot, 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs');

function requireFile(filePath, label) {
  if (!fs.existsSync(filePath)) throw new Error(`Module 083 injection requires ${label}: ${filePath}`);
}

function insertAfter(source, anchor, addition, label, marker = addition.trim()) {
  if (source.includes(marker)) return source;
  if (!source.includes(anchor)) throw new Error(`Module 083 injection anchor is missing: ${label}`);
  return source.replace(anchor, `${anchor}\n${addition}`);
}

function insertBefore(source, anchor, addition, label, marker = addition.trim()) {
  if (source.includes(marker)) return source;
  if (!source.includes(anchor)) throw new Error(`Module 083 injection anchor is missing: ${label}`);
  return source.replace(anchor, `${addition}\n${anchor}`);
}

function patchApp() {
  requireFile(appPath, 'App.jsx');
  let source = fs.readFileSync(appPath, 'utf8');
  source = insertAfter(
    source,
    "import ProjectRiskRegisterCenter from './ProjectRiskRegisterCenter.jsx';",
    "import FullFutureLoopCenter from './FullFutureLoopCenter.jsx';",
    'Module 082 import',
    "import FullFutureLoopCenter from './FullFutureLoopCenter.jsx';"
  );

  const route = `      {/* MODULE_083_FULL_FUTURE_LOOP_ROUTE_START */}
      {(activeRoute === 'full-future-loop' && canSeeAny(['VIEW_FULL_FUTURE_LOOP_083', 'RUN_FULL_FUTURE_LOOP_SANDBOX_083', 'MANAGE_FULL_FUTURE_LOOP_083', 'VIEW_FULL_FUTURE_LOOP_EVIDENCE_083', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="full-future-loop" className="panel full-future-loop-route-panel">
          <FullFutureLoopCenter authSession={authSession} />
        </section>
      ) : null}
      {/* MODULE_083_FULL_FUTURE_LOOP_ROUTE_END */}`;
  source = insertBefore(
    source,
    '      {/* MODULES_075_080_RUNTIME_ROUTES_END */}',
    route,
    'runtime route boundary',
    'MODULE_083_FULL_FUTURE_LOOP_ROUTE_START'
  );

  const installedDefinition = `  /* MODULE_083_FULL_FUTURE_LOOP_INSTALLED_REGISTRY_START */
  {
    route: 'full-future-loop',
    title: 'Full Future Loop',
    navLabel: 'MODULE 083',
    status: 'Safe persistent sandbox',
    group: 'Platform Operations',
    permissions: ['VIEW_FULL_FUTURE_LOOP_083', 'RUN_FULL_FUTURE_LOOP_SANDBOX_083', 'MANAGE_FULL_FUTURE_LOOP_083', 'VIEW_FULL_FUTURE_LOOP_EVIDENCE_083', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides a governed roadmap-to-delivery sandbox covering selective governance, private development, canary verification, curated promotion, production evidence, support, repair, re-promotion, and final verification.'
  },
  /* MODULE_083_FULL_FUTURE_LOOP_INSTALLED_REGISTRY_END */`;
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
    'MODULE_083_FULL_FUTURE_LOOP_INSTALLED_REGISTRY_START'
  );

  source = insertAfter(
    source,
    "        'project-risk-register',",
    "        'full-future-loop',",
    'standalone route exclusion list',
    "        'full-future-loop',"
  );

  const importCount = source.split("import FullFutureLoopCenter from './FullFutureLoopCenter.jsx';").length - 1;
  const routeCount = source.split('MODULE_083_FULL_FUTURE_LOOP_ROUTE_START').length - 1;
  const installedCount = source.split('MODULE_083_FULL_FUTURE_LOOP_INSTALLED_REGISTRY_START').length - 1;
  const exclusionCount = source.split("        'full-future-loop',").length - 1;
  if (importCount !== 1 || routeCount !== 1 || installedCount !== 1 || exclusionCount !== 1) {
    throw new Error(`Module 083 App injection is not idempotent: imports=${importCount}, routes=${routeCount}, installed=${installedCount}, exclusions=${exclusionCount}`);
  }
  fs.writeFileSync(appPath, source, 'utf8');
}

function patchRegistry() {
  requireFile(registryPath, 'module availability registry');
  let source = fs.readFileSync(registryPath, 'utf8');
  const definition = "  Object.freeze({ moduleNumber: '083', route: 'full-future-loop', displayName: 'Full Future Loop', group: 'Platform Operations', description: 'Governed persistent sandbox for selective governance, private development, canary verification, curated promotion, read-only production evidence, support, repair, re-promotion, and final verification.' }),";
  source = insertAfter(
    source,
    "  Object.freeze({ moduleNumber: '082', route: 'project-risk-register', displayName: 'Enterprise Project Risk Register', group: 'Project Delivery', description: 'PMI-aligned project risks, opportunities, response actions, exposure heatmaps, review cadence, governed decisions, and immutable evidence.' }),",
    definition,
    'Module 082 registry definition',
    "moduleNumber: '083'"
  );
  if ((source.split("moduleNumber: '083'").length - 1) !== 1) throw new Error('Module 083 registry injection produced a duplicate or missing definition.');
  fs.writeFileSync(registryPath, source, 'utf8');
}

function patchBackendAvailability() {
  requireFile(availabilityPath, 'ModuleAvailabilityModule.cs');
  let source = fs.readFileSync(availabilityPath, 'utf8');
  const definition = '            ["083"] = Module("083", "full-future-loop", "Full Future Loop", "Platform Operations"),';
  source = insertAfter(
    source,
    '            ["082"] = Module("082", "project-risk-register", "Enterprise Project Risk Register", "Project Delivery"),',
    definition,
    'Module 082 backend availability definition',
    '["083"] = Module("083"'
  );
  if ((source.split('["083"] = Module("083"').length - 1) !== 1) throw new Error('Module 083 backend availability injection produced a duplicate or missing definition.');
  fs.writeFileSync(availabilityPath, source, 'utf8');
}

patchApp();
patchRegistry();
patchBackendAvailability();
console.log('MODULE_083_INJECTION=COMPLETE');

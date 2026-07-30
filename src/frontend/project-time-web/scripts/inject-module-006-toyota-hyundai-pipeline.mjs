import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const appPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');
const modulesPortalPath = path.join(webRoot, 'src', 'ModulesDirectoryPortal.jsx');

function count(source, needle) {
  return source.split(needle).length - 1;
}

function installModule006Route() {
  if (!fs.existsSync(appPath)) throw new Error('Module 006 injection requires App.Module001.g.jsx.');
  let source = fs.readFileSync(appPath, 'utf8');

  const importAnchor = "import WorkRegisterCenter from './WorkRegisterCenter.jsx';";
  const centerImport = "import ProjectRegisterCenter from './ProjectRegisterCenter.jsx';";
  if (!source.includes(importAnchor)) throw new Error('Module 006 Work Register import anchor is missing.');
  if (!source.includes(centerImport)) source = source.replace(importAnchor, `${importAnchor}\n${centerImport}`);

  const oldDefinition = /\{\s*\n\s*route: 'psa-modules',[\s\S]*?\n\s*\},/;
  const newDefinition = `  {
  route: 'toyota-hyundai-pipelines',
  href: '#toyota-hyundai-pipelines',
  title: 'Toyota & Hyundai Pipelines',
  navLabel: 'MODULE 006',
  description: 'Track the reviewed Toyota and Hyundai workbook pipeline, active and archived records, ownership, SELL references, estimates, notes, and historical update evidence.',
  permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
},`;
  if (!oldDefinition.test(source) || !source.includes("title: 'PSA Modules'")) throw new Error('Module 006 legacy registry block is missing.');
  source = source.replace(oldDefinition, newDefinition);

  const oldGuide = "'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'";
  const newGuide = "'toyota-hyundai-pipelines': 'Tracks the reviewed Toyota and Hyundai workbook pipeline with active, archived, ownership, SELL, estimate, note, export, and historical-update context.',\n  'psa-modules': 'Compatibility address for Module 006; redirects to Toyota & Hyundai Pipelines.',\n  'project-register': 'Compatibility address for Module 006; redirects to Toyota & Hyundai Pipelines.'";
  if (!source.includes(oldGuide)) throw new Error('Module 006 legacy guide marker is missing.');
  source = source.replace(oldGuide, newGuide);

  const panelStart = "      {(activeRoute === 'dashboard') ? (\n<section id=\"psa-modules\"";
  const panelEnd = "\n\n\n      <section id=\"current-quarter-utilization\"";
  const start = source.indexOf(panelStart);
  const end = source.indexOf(panelEnd, start);
  if (start < 0 || end < 0) throw new Error('Module 006 legacy dashboard panel was not found.');
  const mount = `      {((activeRoute === 'toyota-hyundai-pipelines' || activeRoute === 'psa-modules' || activeRoute === 'project-register') && canViewPsaModules) ? (
  <section id="toyota-hyundai-pipelines" className="panel project-register-route-panel" data-module="006">
    <ProjectRegisterCenter legacyRoute={activeRoute !== 'toyota-hyundai-pipelines'} />
  </section>
) : null}`;
  source = source.slice(0, start) + mount + source.slice(end);

  for (const required of [
    centerImport,
    "route: 'toyota-hyundai-pipelines'",
    "href: '#toyota-hyundai-pipelines'",
    "title: 'Toyota & Hyundai Pipelines'",
    "activeRoute === 'toyota-hyundai-pipelines'",
    '<ProjectRegisterCenter legacyRoute={activeRoute !== \'toyota-hyundai-pipelines\'} />'
  ]) {
    if (!source.includes(required)) throw new Error(`Generated Module 006 source is missing: ${required}`);
  }
  if (source.includes("title: 'PSA Modules'") || source.includes('<section id="psa-modules"')) {
    throw new Error('Generated Module 006 source still exposes the retired PSA presentation.');
  }

  fs.writeFileSync(appPath, source, 'utf8');
}

function installAuthoritativeModulesDirectory() {
  if (!fs.existsSync(modulesPortalPath)) throw new Error('Module 006 authority patch requires ModulesDirectoryPortal.jsx.');
  let portal = fs.readFileSync(modulesPortalPath, 'utf8');
  if (portal.includes('MODULE_006_AUTHORITATIVE_MODULE_DIRECTORY_PATCH')) return;

  const importAnchor = "import { replaceTimesheetLabel } from './module-availability-registry.js';";
  const importReplacement = "import { PROJECTPULSE_MODULES, canonicalModuleRoute, moduleForRoute, replaceTimesheetLabel } from './module-availability-registry.js';";
  if (!portal.includes(importAnchor)) throw new Error('Modules directory registry import anchor is missing.');
  portal = portal.replace(importAnchor, `${importReplacement}\n// MODULE_006_AUTHORITATIVE_MODULE_DIRECTORY_PATCH`);

  const numberFunction = `function moduleNumberForRoute(route, source) {
  return moduleNumberFromLabel(source)
    || CANONICAL_MODULE_NUMBER_BY_ROUTE[route]
    || '';
}`;
  const numberReplacement = `function moduleNumberForRoute(route, source) {
  return moduleForRoute(route)?.moduleNumber
    || moduleNumberFromLabel(source)
    || CANONICAL_MODULE_NUMBER_BY_ROUTE[route]
    || '';
}`;
  if (!portal.includes(numberFunction)) throw new Error('Modules directory module-number resolver anchor is missing.');
  portal = portal.replace(numberFunction, numberReplacement);

  const labelBlock = `  const rawLabel = cleanText(anchor.querySelector('.enterprise-nav-label')?.textContent || anchor.textContent);
  const label = canonicalDisplayLabel(route, rawLabel);
  if (!label) return;`;
  const labelReplacement = `  const rawLabel = cleanText(anchor.querySelector('.enterprise-nav-label')?.textContent || anchor.textContent);
  const registryModule = moduleForRoute(route);
  const label = registryModule?.displayName || canonicalDisplayLabel(route, rawLabel);
  if (!label) return;`;
  if (!portal.includes(labelBlock)) throw new Error('Modules directory label-authority anchor is missing.');
  portal = portal.replace(labelBlock, labelReplacement);

  const pushBlock = `    label,
    moduleNumber: moduleNumberForRoute(route, moduleNumberSource),
    group: groupName,
    order: modules.length`;
  const pushReplacement = `    label,
    description: registryModule?.description || '',
    moduleNumber: moduleNumberForRoute(route, moduleNumberSource),
    group: registryModule?.group || groupName,
    order: modules.length`;
  if (!portal.includes(pushBlock)) throw new Error('Modules directory authorized-module projection anchor is missing.');
  portal = portal.replace(pushBlock, pushReplacement);

  const helperAnchor = `  return modules;
}

function moduleListsMatch`;
  const helperReplacement = `  return modules;
}

function superAdministratorModuleCatalog(authorizedModules) {
  const authorizedByRoute = new Map(
    authorizedModules.map((module) => [canonicalModuleRoute(module.route), module])
  );
  return PROJECTPULSE_MODULES.map((registryModule, index) => {
    const current = authorizedByRoute.get(registryModule.route);
    return {
      ...(current || {}),
      route: registryModule.route,
      href: current?.href || \`#\${registryModule.route}\`,
      label: registryModule.displayName,
      description: registryModule.description || current?.description || '',
      moduleNumber: registryModule.moduleNumber,
      group: registryModule.group,
      order: index
    };
  });
}

function moduleListsMatch`;
  if (!portal.includes(helperAnchor)) throw new Error('Modules directory Super Administrator catalog anchor is missing.');
  portal = portal.replace(helperAnchor, helperReplacement);

  const enrichedBlock = `  const enrichedModules = useMemo(
    () => modules.map((module) => effectiveModuleState(module, availability)),
    [modules, availability]
  );`;
  const enrichedReplacement = `  const directoryModules = useMemo(
    () => isSuperAdministrator ? superAdministratorModuleCatalog(modules) : modules,
    [isSuperAdministrator, modules]
  );

  const enrichedModules = useMemo(
    () => directoryModules.map((module) => effectiveModuleState(module, availability)),
    [directoryModules, availability]
  );`;
  if (!portal.includes(enrichedBlock)) throw new Error('Modules directory effective-catalog anchor is missing.');
  portal = portal.replace(enrichedBlock, enrichedReplacement);

  const descriptionLine = '              <p>Open the {module.label} workspace available to your current access scope.</p>';
  const descriptionReplacement = `              <p>{module.description || \`Open the \${module.label} workspace available to your current access scope.\`}</p>
              {isSuperAdministrator ? <div className="module-authority-full-control">Full Control · Organization-wide</div> : null}`;
  if (!portal.includes(descriptionLine)) throw new Error('Modules directory card-description anchor is missing.');
  portal = portal.replace(descriptionLine, descriptionReplacement);

  for (const required of [
    'MODULE_006_AUTHORITATIVE_MODULE_DIRECTORY_PATCH',
    'PROJECTPULSE_MODULES',
    'moduleForRoute(route)?.moduleNumber',
    'superAdministratorModuleCatalog',
    'isSuperAdministrator ? superAdministratorModuleCatalog(modules) : modules',
    'Full Control · Organization-wide'
  ]) {
    if (!portal.includes(required)) throw new Error(`Modules directory authority patch is missing: ${required}`);
  }
  if (count(portal, 'MODULE_006_AUTHORITATIVE_MODULE_DIRECTORY_PATCH') !== 1) {
    throw new Error('Modules directory authority patch must appear exactly once.');
  }

  fs.writeFileSync(modulesPortalPath, portal, 'utf8');
}

installModule006Route();
installAuthoritativeModulesDirectory();
console.log('MODULE_006_TOYOTA_HYUNDAI_PIPELINES_GENERATION=PASS route=toyota-hyundai-pipelines aliases=psa-modules,project-register modules_directory=authoritative_registry superadmin=full_catalog');

import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const appPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');
const modulesPortalPath = path.join(webRoot, 'src', 'ModulesDirectoryPortal.jsx');
const projectRegisterPath = path.join(webRoot, 'src', 'ProjectRegisterCenter.jsx');
const stackedLogoSourcePath = path.resolve(
  webRoot,
  '..',
  '..',
  'backend',
  'ProjectTime.Api',
  'Assets',
  'Branding',
  'USSNavyStacked.png'
);
const stackedLogoTargetPaths = [
  path.join(webRoot, 'brand', 'ussignal.png'),
  path.join(webRoot, 'brand', 'USSNavyStacked.png')
];

function count(source, needle) {
  return source.split(needle).length - 1;
}

function replaceExact(source, current, replacement, label) {
  if (!source.includes(current)) throw new Error(`Module 006 customer expansion anchor is missing: ${label}`);
  return source.replace(current, replacement);
}

function installCustomerExpansion() {
  if (!fs.existsSync(projectRegisterPath)) {
    throw new Error('Module 006 customer expansion requires ProjectRegisterCenter.jsx.');
  }

  let source = fs.readFileSync(projectRegisterPath, 'utf8');
  if (source.includes('MODULE_006_CUSTOMER_EXPANSION_START')) return;

  source = replaceExact(
    source,
    "import './module006-standalone.css';",
    "import './module006-standalone.css';\n\n// MODULE_006_CUSTOMER_EXPANSION_START",
    'installation marker'
  );

  source = replaceExact(
    source,
    '<nav className="project-register-pagination" aria-label="Toyota and Hyundai pipeline pagination">',
    '<nav className="project-register-pagination" aria-label="Customer pipeline pagination">',
    'pagination label'
  );

  source = replaceExact(
    source,
    `  async function saveDetails() {
    if (!selectedRecord || !canEdit) return;`,
    `  async function saveDetails() {
    if (!selectedRecord || !canEdit) return;
    if (clean(editForm.customer).length < 2) {
      setMessage('Enter a customer name containing at least two characters.');
      return;
    }`,
    'project details customer validation'
  );

  source = replaceExact(
    source,
    `  async function createProject() {
    if (!canEdit || !clean(newProjectForm.projectName)) return;`,
    `  async function createProject() {
    if (!canEdit) return;
    if (clean(newProjectForm.customer).length < 2) {
      setMessage('Enter a customer name containing at least two characters.');
      return;
    }
    if (clean(newProjectForm.projectName).length < 3) {
      setMessage('Enter a project name containing at least three characters.');
      return;
    }`,
    'new project validation'
  );

  source = replaceExact(
    source,
    'downloadText(workbook, `US-Signal-Toyota-Hyundai-Pipelines-${new Date().toISOString().slice(0, 10)}.xls`,',
    'downloadText(workbook, `US-Signal-Customer-Pipelines-${new Date().toISOString().slice(0, 10)}.xls`,',
    'export filename'
  );

  source = replaceExact(
    source,
    `    <section className="project-register-center projectpulse-module-standard module006-standalone" data-module="006" data-module-name="Toyota & Hyundai Pipelines" data-canonical-route="toyota-hyundai-pipelines" data-project-register-contract="module006-standalone-pipeline-v1">
      <header className="project-register-hero">`,
    `    <section className="project-register-center projectpulse-module-standard module006-standalone" data-module="006" data-module-name="Toyota & Hyundai Pipelines" data-canonical-route="toyota-hyundai-pipelines" data-project-register-contract="module006-standalone-pipeline-v1">
      <datalist id="module006-customer-options">
        {customerOptions.map((value) => <option value={value} key={value} />)}
      </datalist>
      <header className="project-register-hero">`,
    'customer datalist'
  );

  source = replaceExact(
    source,
    '<p>Manage Toyota and Hyundai pipeline projects, action items, review dates, status updates, and append-only note history directly in Module 006.</p>',
    '<p>Manage the reviewed Toyota and Hyundai pipeline baseline plus additional customer projects, action items, review dates, status updates, and append-only note history directly in Module 006.</p>',
    'workspace description'
  );

  source = replaceExact(
    source,
    '<div className="project-register-summary" aria-label="Toyota and Hyundai pipeline summary">',
    '<div className="project-register-summary" aria-label="Customer pipeline summary">',
    'summary label'
  );

  source = replaceExact(
    source,
    '<select value={customer} onChange={(event) => setCustomer(event.target.value)}><option value="all">Toyota and Hyundai</option>{customerOptions.map((value) => <option value={value} key={value}>{value}</option>)}</select>',
    '<select value={customer} onChange={(event) => setCustomer(event.target.value)}><option value="all">All customers</option>{customerOptions.map((value) => <option value={value} key={value}>{value}</option>)}</select>',
    'customer filter'
  );

  source = replaceExact(
    source,
    'No Toyota or Hyundai records match the current filters.',
    'No customer pipeline records match the current filters.',
    'empty state'
  );

  source = replaceExact(
    source,
    'aria-label="Toyota and Hyundai pipeline project editor"',
    'aria-label="Customer pipeline project editor"',
    'drawer label'
  );

  source = replaceExact(
    source,
    '<label>Customer<select value={editForm.customer} onChange={(event) => setEditForm((current) => ({ ...current, customer: event.target.value }))} disabled={!canEdit}><option>Toyota</option><option>Hyundai</option></select></label>',
    '<label>Customer<small>Choose an existing customer or type a new customer name.</small><input list="module006-customer-options" maxLength="120" value={editForm.customer} onChange={(event) => setEditForm((current) => ({ ...current, customer: event.target.value }))} disabled={!canEdit} /></label>',
    'editable customer input'
  );

  source = replaceExact(
    source,
    '<button type="button" className="primary-action" disabled={!canEdit || busy === \'details\'} onClick={() => void saveDetails()}>',
    '<button type="button" className="primary-action" disabled={!canEdit || busy === \'details\' || clean(editForm.customer).length < 2} onClick={() => void saveDetails()}>',
    'project details save guard'
  );

  source = replaceExact(
    source,
    'aria-label="Add new Toyota or Hyundai pipeline project"',
    'aria-label="Add new customer pipeline project"',
    'new project dialog label'
  );

  source = replaceExact(
    source,
    '<header><div><p className="eyebrow">MODULE 006</p><h3>Add New Project</h3><p>Create a standalone Toyota or Hyundai pipeline record.</p></div><button type="button" className="secondary-action" onClick={() => setNewProjectOpen(false)}>Close</button></header>',
    '<header><div><p className="eyebrow">MODULE 006</p><h3>Add New Project</h3><p>Create a standalone pipeline record for any customer.</p></div><button type="button" className="secondary-action" onClick={() => setNewProjectOpen(false)}>Close</button></header>',
    'new project description'
  );

  source = replaceExact(
    source,
    '<label>Customer<select value={newProjectForm.customer} onChange={(event) => setNewProjectForm((current) => ({ ...current, customer: event.target.value }))}><option>Toyota</option><option>Hyundai</option></select></label>',
    '<label>Customer<small>Choose an existing customer or type a new customer name.</small><input list="module006-customer-options" maxLength="120" value={newProjectForm.customer} onChange={(event) => setNewProjectForm((current) => ({ ...current, customer: event.target.value }))} /></label>',
    'new project customer input'
  );

  source = replaceExact(
    source,
    "disabled={busy === 'create' || clean(newProjectForm.projectName).length < 3}",
    "disabled={busy === 'create' || clean(newProjectForm.customer).length < 2 || clean(newProjectForm.projectName).length < 3}",
    'new project save guard'
  );

  for (const required of [
    'MODULE_006_CUSTOMER_EXPANSION_START',
    'module006-customer-options',
    'list="module006-customer-options"',
    'All customers',
    'any customer',
    'No customer pipeline records match the current filters.',
    'US-Signal-Customer-Pipelines-'
  ]) {
    if (!source.includes(required)) throw new Error(`Module 006 customer expansion is missing: ${required}`);
  }

  if (source.includes('<label>Customer<select value={editForm.customer}')
      || source.includes('<label>Customer<select value={newProjectForm.customer}')) {
    throw new Error('Module 006 customer expansion left a Toyota/Hyundai-only customer selector.');
  }

  fs.writeFileSync(projectRegisterPath, source, 'utf8');
}

function installStackedLogo() {
  if (!fs.existsSync(stackedLogoSourcePath)) {
    throw new Error(`Stacked US Signal logo source was not found: ${stackedLogoSourcePath}`);
  }

  const approvedLogo = fs.readFileSync(stackedLogoSourcePath);
  for (const stackedLogoTargetPath of stackedLogoTargetPaths) {
    const installedLogo = fs.existsSync(stackedLogoTargetPath)
      ? fs.readFileSync(stackedLogoTargetPath)
      : null;

    if (!installedLogo || !approvedLogo.equals(installedLogo)) {
      fs.mkdirSync(path.dirname(stackedLogoTargetPath), { recursive: true });
      fs.writeFileSync(stackedLogoTargetPath, approvedLogo);
    }
  }
}

function installModule006Route() {
  if (!fs.existsSync(appPath)) throw new Error('Module 006 injection requires App.Module001.g.jsx.');
  let source = fs.readFileSync(appPath, 'utf8');

  const importAnchor = "import WorkRegisterCenter from './WorkRegisterCenter.jsx';";
  const centerImport = "import ProjectRegisterCenter from './ProjectRegisterCenter.jsx';";
  if (!source.includes(importAnchor)) throw new Error('Module 006 Work Register import anchor is missing.');
  if (!source.includes(centerImport)) source = source.replace(importAnchor, `${importAnchor}\n${centerImport}`);

  const normalizeRoutePattern = /function normalizeRoute\(hash\) \{[\s\S]*?\n\}/;
  const normalizeRoute = `function normalizeRoute(hash) {
  const cleaned = (hash || window.location.hash || '#dashboard').replace('#', '').split('?')[0].trim();
  const route = cleaned || 'dashboard';
  return route === 'psa-modules' || route === 'project-register'
    ? 'toyota-hyundai-pipelines'
    : route;
}`;
  if (!normalizeRoutePattern.test(source)) throw new Error('Module 006 normalizeRoute anchor is missing.');
  source = source.replace(normalizeRoutePattern, normalizeRoute);

  const oldDefinition = /\{\s*\n\s*route: 'psa-modules',[\s\S]*?\n\s*\},/;
  const newDefinition = `  {
  route: 'toyota-hyundai-pipelines',
  href: '#toyota-hyundai-pipelines',
  title: 'Toyota & Hyundai Pipelines',
  navLabel: 'MODULE 006',
  description: 'Track the reviewed Toyota and Hyundai workbook baseline plus additional customer pipeline records, active and archived work, ownership, SELL references, estimates, notes, and historical update evidence.',
  permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
},`;
  if (!oldDefinition.test(source) || !source.includes("title: 'PSA Modules'")) throw new Error('Module 006 legacy registry block is missing.');
  source = source.replace(oldDefinition, newDefinition);

  const oldGuide = "'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'";
  const newGuide = "'toyota-hyundai-pipelines': 'Toyota & Hyundai Pipelines — Module 006. Reviewed baseline plus additional customer pipeline records, bounded history, filters, and exports.',\n  'psa-modules': 'Compatibility address for Module 006; redirects to Toyota & Hyundai Pipelines.',\n  'project-register': 'Compatibility address for Module 006; redirects to Toyota & Hyundai Pipelines.'";
  if (!source.includes(oldGuide)) throw new Error('Module 006 legacy guide marker is missing.');
  source = source.replace(oldGuide, newGuide);

  const retiredPanel = /\s*\{\(\(activeRoute === 'toyota-hyundai-pipelines'[\s\S]*?<ProjectRegisterCenter[\s\S]*?\) : null\}/g;
  source = source.replace(retiredPanel, '');

  const legacyPanelStart = "      {(activeRoute === 'dashboard') ? (\n<section id=\"psa-modules\"";
  const legacyPanelEnd = "\n\n\n      <section id=\"current-quarter-utilization\"";
  const legacyStart = source.indexOf(legacyPanelStart);
  const legacyEnd = source.indexOf(legacyPanelEnd, legacyStart);
  if (legacyStart >= 0 && legacyEnd >= 0) {
    source = source.slice(0, legacyStart) + legacyPanelEnd + source.slice(legacyEnd + legacyPanelEnd.length);
  }

  const routeBoundaryAnchor = '      {/* MODULE_060_CONTRACTS_ROOT_ROUTE_START */}';
  if (!source.includes(routeBoundaryAnchor)) throw new Error('Module 006 route boundary anchor is missing.');
  const exclusiveRoute = `      {/* PR467_MODULE_006_EXCLUSIVE_ROUTE_START */}
      {activeRoute === 'toyota-hyundai-pipelines' ? (
        <section id="toyota-hyundai-pipelines" className="panel project-register-route-panel" data-module="006">
          <ProjectRegisterCenter legacyRoute={false} />
        </section>
      ) : (
        <>
${routeBoundaryAnchor}`;
  source = source.replace(routeBoundaryAnchor, exclusiveRoute);

  const closeIndex = source.lastIndexOf('</main>');
  if (closeIndex < 0) throw new Error('Module 006 application-shell close anchor is missing.');
  source = `${source.slice(0, closeIndex)}        </>
      )}
      {/* PR467_MODULE_006_EXCLUSIVE_ROUTE_END */}
${source.slice(closeIndex)}`;

  for (const required of [
    centerImport,
    "route: 'toyota-hyundai-pipelines'",
    "return route === 'psa-modules' || route === 'project-register'",
    'PR467_MODULE_006_EXCLUSIVE_ROUTE_START',
    '<ProjectRegisterCenter legacyRoute={false} />',
    'PR467_MODULE_006_EXCLUSIVE_ROUTE_END'
  ]) {
    if (!source.includes(required)) throw new Error(`Generated Module 006 source is missing: ${required}`);
  }
  if ((source.match(/<ProjectRegisterCenter/g) || []).length !== 1) {
    throw new Error('Generated Module 006 source must contain exactly one ProjectRegisterCenter mount.');
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

installCustomerExpansion();
installStackedLogo();
installModule006Route();
installAuthoritativeModulesDirectory();
console.log('MODULE_006_TOYOTA_HYUNDAI_PIPELINES_GENERATION=PASS route=toyota-hyundai-pipelines aliases=psa-modules,project-register customers=extensible stacked_logo=approved modules_directory=authoritative_registry superadmin=full_catalog');

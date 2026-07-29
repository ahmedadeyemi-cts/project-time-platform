import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const appPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');

if (!fs.existsSync(appPath)) {
  throw new Error('Module 006 Project Register injection requires the generated App.Module001.g.jsx source.');
}

let source = fs.readFileSync(appPath, 'utf8');

const importAnchor = "import WorkRegisterCenter from './WorkRegisterCenter.jsx';";
const projectRegisterImport = "import ProjectRegisterCenter from './ProjectRegisterCenter.jsx';";
if (!source.includes(importAnchor)) {
  throw new Error('Module 006 could not locate the authoritative Work Register import anchor.');
}
if (!source.includes(projectRegisterImport)) {
  source = source.replace(importAnchor, `${importAnchor}\n${projectRegisterImport}`);
}

const legacyModuleDefinition = `  {
    route: 'psa-modules',
    href: '#psa-modules',
    title: 'PSA Modules',
    navLabel: 'MODULE 006',
    description: 'Review project intake, resource scheduling, expense management, and executive reporting workflows.',
    permissions: ['VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING']
  },`;
const projectRegisterDefinition = `  {
    route: 'project-register',
    href: '#project-register',
    title: 'Project Register',
    navLabel: 'MODULE 006',
    description: 'Search the authoritative project inventory, review active and archived work, and enter the governed project-management workspace without duplicating Modules 055C or 055D.',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },`;
if (!source.includes(legacyModuleDefinition)) {
  throw new Error('Module 006 could not locate the legacy PSA Modules registry block.');
}
source = source.replace(legacyModuleDefinition, projectRegisterDefinition);

const legacyGuide = "'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'";
const projectRegisterGuide = "'project-register': 'Searches the authoritative Project Register, separates active and archived projects, and routes authorized management actions to Module 055C.',\n  'psa-modules': 'Compatibility route for the retired Module 006 PSA Modules address; use Project Register instead.'";
if (!source.includes(legacyGuide)) {
  throw new Error('Module 006 could not locate the legacy route guide.');
}
source = source.replace(legacyGuide, projectRegisterGuide);

const legacyPanelStart = "      {(activeRoute === 'dashboard') ? (\n<section id=\"psa-modules\"";
const legacyPanelEnd = "\n\n\n      <section id=\"current-quarter-utilization\"";
const startIndex = source.indexOf(legacyPanelStart);
const endIndex = source.indexOf(legacyPanelEnd, startIndex);
if (startIndex < 0 || endIndex < 0) {
  throw new Error('Module 006 could not locate the legacy dashboard-only PSA panel.');
}

const projectRegisterMount = `      {((activeRoute === 'project-register' || activeRoute === 'psa-modules') && canViewPsaModules) ? (
        <section id="project-register" className="panel project-register-route-panel" data-module="006">
          <ProjectRegisterCenter legacyRoute={activeRoute === 'psa-modules'} />
        </section>
      ) : null}`;
source = source.slice(0, startIndex) + projectRegisterMount + source.slice(endIndex);

for (const required of [
  projectRegisterImport,
  "route: 'project-register'",
  "href: '#project-register'",
  "title: 'Project Register'",
  "activeRoute === 'project-register'",
  '<ProjectRegisterCenter legacyRoute={activeRoute === \'psa-modules\'} />',
  "'project-register': 'Searches the authoritative Project Register"
]) {
  if (!source.includes(required)) {
    throw new Error(`Generated Module 006 Project Register source is missing: ${required}`);
  }
}

for (const retired of [
  "title: 'PSA Modules'",
  "title: 'Toyota & Hyundai Pipeline'",
  '<section id="psa-modules"',
  '<h2>Remaining sections foundation</h2>',
  '<h2>Toyota &amp; Hyundai Pipeline</h2>'
]) {
  if (source.includes(retired)) {
    throw new Error(`Generated Module 006 source still exposes retired presentation: ${retired}`);
  }
}

fs.writeFileSync(appPath, source, 'utf8');
console.log('MODULE_006_PROJECT_REGISTER_GENERATION=PASS route=project-register compatibility=psa-modules authority=work-register');

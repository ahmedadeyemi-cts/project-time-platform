import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const appPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');
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
  description: 'Track active and archived Toyota and Hyundai project delivery, ownership, engineering, SELL references, tasks, documents, financial context, and lifecycle evidence.',
  permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
},`;
if (!oldDefinition.test(source) || !source.includes("title: 'PSA Modules'")) throw new Error('Module 006 legacy registry block is missing.');
source = source.replace(oldDefinition, newDefinition);

const oldGuide = "'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'";
const newGuide = "'toyota-hyundai-pipelines': 'Tracks the governed Toyota and Hyundai project pipeline with active, archived, ownership, task, document, SELL, financial, and audit context.',\n  'psa-modules': 'Compatibility address for Module 006; redirects to Toyota & Hyundai Pipelines.',\n  'project-register': 'Compatibility address for Module 006; redirects to Toyota & Hyundai Pipelines.'";
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
console.log('MODULE_006_TOYOTA_HYUNDAI_PIPELINES_GENERATION=PASS route=toyota-hyundai-pipelines aliases=psa-modules,project-register');

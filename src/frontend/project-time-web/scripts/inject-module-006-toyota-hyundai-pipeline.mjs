import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const appPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');

if (!fs.existsSync(appPath)) {
  throw new Error('Module 006 label injection requires the generated App.Module001.g.jsx source.');
}

let source = fs.readFileSync(appPath, 'utf8');
const requiredBefore = [
  "title: 'PSA Modules'",
  '<p className="eyebrow">PSA platform modules</p>',
  '<h2>Remaining sections foundation</h2>',
  "'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'"
];

for (const marker of requiredBefore) {
  if (!source.includes(marker)) {
    throw new Error(`Module 006 label injection could not locate: ${marker}`);
  }
}

source = source
  .replaceAll("title: 'PSA Modules'", "title: 'Toyota & Hyundai Pipeline'")
  .replaceAll(
    "description: 'Review project intake, resource scheduling, expense management, and executive reporting workflows.'",
    "description: 'Review Toyota and Hyundai opportunity, intake, delivery, resourcing, expense, billing, and executive-readiness workflows.'"
  )
  .replaceAll('<p className="eyebrow">PSA platform modules</p>', '<p className="eyebrow">Toyota &amp; Hyundai pipeline</p>')
  .replaceAll('<h2>Remaining sections foundation</h2>', '<h2>Toyota &amp; Hyundai Pipeline</h2>')
  .replaceAll(
    'These sections prepare the rest of Project Health Dashboard beyond time entry: intake, project management, resource scheduling, expenses, invoicing, reporting, and administrative workflow.',
    'Track Toyota and Hyundai opportunity intake, project delivery, resource readiness, expenses, invoicing, and executive reporting through one governed pipeline.'
  )
  .replaceAll(
    "'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'",
    "'psa-modules': 'Tracks Toyota and Hyundai opportunity, delivery, resource, expense, invoice, and executive-readiness workflows as they are connected.'"
  );

for (const requiredAfter of [
  "title: 'Toyota & Hyundai Pipeline'",
  'Toyota &amp; Hyundai Pipeline',
  'Toyota and Hyundai opportunity, intake, delivery, resourcing, expense, billing, and executive-readiness workflows.',
  "'psa-modules': 'Tracks Toyota and Hyundai opportunity, delivery, resource, expense, invoice, and executive-readiness workflows as they are connected.'"
]) {
  if (!source.includes(requiredAfter)) {
    throw new Error(`Generated Module 006 source is missing: ${requiredAfter}`);
  }
}

if (source.includes("title: 'PSA Modules'") || source.includes('<h2>Remaining sections foundation</h2>')) {
  throw new Error('Generated Module 006 source still exposes the retired PSA Modules title.');
}

fs.writeFileSync(appPath, source, 'utf8');
console.log('MODULE_006_TOYOTA_HYUNDAI_PIPELINE_GENERATION=PASS route=psa-modules module=006');

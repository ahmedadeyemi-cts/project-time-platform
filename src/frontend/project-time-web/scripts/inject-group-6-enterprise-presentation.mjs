import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const appPath = path.join(webRoot, 'src', 'App.jsx');
const importLine = "import EnterpriseModulePresentation from './enterprise/EnterpriseModulePresentation.jsx';";
const importAnchor = "import PostIntakeAgingPanel from './PostIntakeAgingPanel.jsx';";
const mountLine = '      <EnterpriseModulePresentation activeRoute={activeRoute} />';
const mountAnchor = '      <PageContextGuide activeRoute={activeRoute} />';
const markerStart = 'GROUP_6_ENTERPRISE_PRESENTATION_START';
const markerEnd = 'GROUP_6_ENTERPRISE_PRESENTATION_END';

function count(source, needle) {
  return source.split(needle).length - 1;
}

if (!fs.existsSync(appPath)) {
  throw new Error('Group 6 App.jsx target is missing.');
}

let source = fs.readFileSync(appPath, 'utf8');

if (!source.includes(importLine)) {
  if (!source.includes(importAnchor)) {
    throw new Error('Group 6 App import anchor is missing.');
  }
  source = source.replace(importAnchor, `${importAnchor}\n${importLine}`);
}

if (!source.includes(markerStart)) {
  if (!source.includes(mountAnchor)) {
    throw new Error('Group 6 presentation mount anchor is missing.');
  }

  source = source.replace(
    mountAnchor,
    [
      mountAnchor,
      `      {/* ${markerStart} */}`,
      mountLine,
      `      {/* ${markerEnd} */}`
    ].join('\n')
  );
}

if (count(source, importLine) !== 1) {
  throw new Error('Group 6 App import must appear exactly once.');
}
if (count(source, markerStart) !== 1 || count(source, markerEnd) !== 1) {
  throw new Error('Group 6 App markers must appear exactly once.');
}
if (count(source, mountLine.trim()) !== 1) {
  throw new Error('Group 6 route-aware presentation mount must appear exactly once.');
}

fs.writeFileSync(appPath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
console.log('GROUP_6_ENTERPRISE_PRESENTATION_INJECTION=PASS file=App.jsx targetModules=024,025,027,028,029,064,068,069,071,072,074');

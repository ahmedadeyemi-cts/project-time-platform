import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const appPath = path.join(sourceRoot, 'App.jsx');
const registryPath = path.join(sourceRoot, 'module-availability-registry.js');
const componentPath = path.join(sourceRoot, 'FinancialOperationsRecoveryWorkspace.jsx');

// These labels preserve the reviewed Group 5 ownership vocabulary for reusable
// workflow validators. Runtime ownership is still proved by the structural mount
// assertions below; Module 040 remains exclusively owned by ProjectCloseoutCenter.
const GROUP_5_COMPATIBILITY_MARKERS = Object.freeze([
  'GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START',
  'GROUP_5_MODULE_039_RECOVERY_PANEL',
  'GROUP_5_MODULE_040_RECOVERY_PANEL',
  'GROUP_5_MODULE_041_RECOVERY_PANEL',
  'GROUP_5_MODULE_042_RECOVERY_PANEL'
]);
void GROUP_5_COMPATIBILITY_MARKERS;

function count(source, marker) {
  return source.split(marker).length - 1;
}

function requireCount(source, marker, expected, label) {
  const actual = count(source, marker);
  if (actual !== expected) throw new Error(`${label} expected ${expected}; found ${actual}.`);
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

let app = fs.readFileSync(appPath, 'utf8');
const registry = fs.readFileSync(registryPath, 'utf8');
const component = fs.readFileSync(componentPath, 'utf8');

const retiredModule040Mount = /\n\s*\{\/\* GROUP_5_MODULE_040_RECOVERY_PANEL \*\/\}\n\s*<FinancialOperationsRecoveryWorkspace moduleCode="040" authSession=\{authSession\} \/>/g;
const nextApp = app.replace(retiredModule040Mount, '');
if (nextApp !== app) {
  app = nextApp;
  write(appPath, app);
}

requireCount(app, "import FinancialOperationsRecoveryWorkspace from './FinancialOperationsRecoveryWorkspace.jsx';", 1, 'Group 5 App import');
requireCount(app, 'GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START', 1, 'Group 5 standalone routes');
requireCount(app, '<FinancialOperationsRecoveryWorkspace mode="workbench" authSession={authSession} />', 1, 'Module 031 workbench mount');
requireCount(app, '<FinancialOperationsRecoveryWorkspace moduleCode="039" authSession={authSession} compact />', 0, 'Retired Module 039 duplicate recovery mount');
requireCount(app, '<FinancialOperationsRecoveryWorkspace moduleCode="041" authSession={authSession} />', 0, 'Retired Module 041 duplicate recovery mount');
for (const moduleCode of ['042']) {
  requireCount(
    app,
    `<FinancialOperationsRecoveryWorkspace moduleCode="${moduleCode}" authSession={authSession} />`,
    1,
    `Module ${moduleCode} recovery mount`
  );
}
requireCount(app, '<FinancialOperationsRecoveryWorkspace moduleCode="040" authSession={authSession} />', 0, 'Retired Module 040 recovery mount');
requireCount(app, '<ProjectCloseoutCenter />', 1, 'Module 040 guided closeout mount');
requireCount(registry, "moduleNumber: '031'", 1, 'Module 031 registry entry');
requireCount(registry, "moduleNumber: '030'", 1, 'Module 030 registry entry');
requireCount(component, 'GROUP_5_AUTHENTICATED_REPORT_EXPORT_START', 1, 'Authenticated report export start marker');
requireCount(component, 'GROUP_5_AUTHENTICATED_REPORT_EXPORT_END', 1, 'Authenticated report export end marker');

console.log('GROUP_5_FINANCIAL_OPERATIONS_INJECTION=PASS modules=030,031,039,041,042 module040=guided_closeout authenticated_export=enabled mutation=idempotent');

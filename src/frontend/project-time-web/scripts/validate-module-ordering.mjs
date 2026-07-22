import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import {
  compareProjectPulseModules,
  parseProjectPulseModuleCode,
  sortProjectPulseModules
} from '../src/module-ordering.js';

const frontendRoot = fileURLToPath(new URL('../', import.meta.url));
const readFrontend = (relativePath) => fs.readFileSync(path.join(frontendRoot, relativePath), 'utf8');

const app = readFrontend('src/App.jsx');
const guide = readFrontend('src/SystemUserGuide.jsx');
const packageJson = JSON.parse(readFrontend('package.json'));

let checks = 0;
let failures = 0;

function test(name, condition) {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`MODULE_ORDERING_${name}=${condition ? 'PASSED' : 'FAILED'}`);
}

const suffixSequence = sortProjectPulseModules([
  { navLabel: 'MODULE 998' },
  { navLabel: 'MODULE 055D' },
  { navLabel: 'MODULE 001' },
  { navLabel: 'MODULE 055C' },
  { navLabel: 'MODULE 055B' },
  { navLabel: 'MODULE 056E' },
  { navLabel: 'MODULE 997' },
  { navLabel: 'MODULE 999' }
]).map((module) => module.navLabel);

test(
  'SUFFIX_SEQUENCE',
  suffixSequence.join('|') === 'MODULE 001|MODULE 055B|MODULE 055C|MODULE 055D|MODULE 056E|MODULE 997|MODULE 998|MODULE 999'
);

test('MODULE_999_LAST', suffixSequence.at(-1) === 'MODULE 999');

test(
  'LABEL_INPUT',
  compareProjectPulseModules(
    { label: 'MODULE 009', title: 'User Administration' },
    { label: 'MODULE 010', title: 'Azure / Entra Admin' }
  ) < 0
);

test('BARE_CODE_PARSE', parseProjectPulseModuleCode('055D')?.code === '055D');
test('PADDED_CODE_PARSE', parseProjectPulseModuleCode('MODULE 1')?.code === '001');
test('NON_MODULE_REJECTED', parseProjectPulseModuleCode('Dashboard') === null);

test(
  'ROLE_REGISTRY_AUTOSORTED',
  app.includes('const roleWorkspaceModules = sortProjectPulseModules([')
);
test(
  'INSTALLED_REGISTRY_AUTOSORTED',
  app.includes('return sortProjectPulseModules([')
);
test(
  'NAVIGATION_AUTOSORTED',
  app.includes('.sort(compareProjectPulseModules);')
    && app.includes("{ name: 'Modules', expanded: true, items: orderedModuleItems }")
);
test(
  'DASHBOARD_SHOWS_MODULE_CODE',
  app.includes("{module.navLabel || 'Platform'} • {module.group}")
);
test(
  'USER_GUIDE_AUTOSORTED',
  guide.includes("import { compareProjectPulseModules } from './module-ordering.js';")
    && guide.includes('.sort(compareProjectPulseModules);')
);

const declaredModuleCodes = Array.from(
  app.matchAll(/navLabel:\s*['"]MODULE\s+(\d{3}[A-Z]*)['"]/g),
  (match) => match[1]
);

for (const requiredCode of ['001', '055B', '055C', '997', '998', '999']) {
  test(`REQUIRED_${requiredCode}_PRESENT`, declaredModuleCodes.includes(requiredCode));
}

test(
  'BUILD_GATE_ENABLED',
  packageJson.scripts?.build?.startsWith('npm run validate:module-ordering &&')
    && packageJson.scripts?.['validate:module-ordering'] === 'node ./scripts/validate-module-ordering.mjs'
);

console.log(`MODULE_ORDERING_VALIDATION_CHECKS=${checks}`);
console.log(`MODULE_ORDERING_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;

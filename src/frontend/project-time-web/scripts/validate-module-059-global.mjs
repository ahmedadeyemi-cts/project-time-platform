import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const frontendRoot = process.cwd();
const appPath = path.join(frontendRoot, 'src', 'App.jsx');
const drawerPath = path.join(frontendRoot, 'src', 'SessionIntelligenceDrawer.jsx');
const cssPath = path.join(frontendRoot, 'src', 'session-intelligence-drawer.css');

const app = fs.readFileSync(appPath, 'utf8');
const drawer = fs.readFileSync(drawerPath, 'utf8');
const css = fs.readFileSync(cssPath, 'utf8');

const importNeedle = "import SessionIntelligenceDrawer from './SessionIntelligenceDrawer.jsx';";
const mountNeedle = '<SessionIntelligenceDrawer authSession={authSession} />';
const markerNeedle = 'MODULE_059_GLOBAL_ROUTE_HOST';
const hostClassNeedle = 'module059-global-route-host';
const scopeNeedle = 'data-route-scope="all-authenticated-pages"';
const globalBoundaryNeedle = 'MODULE_060_NON_CONTRACT_ROUTE_CONTENT_END';
const helpNeedle = '<HelpAssistant />';
const mainCloseNeedle = '</main>';

const count = (text, needle) => text.split(needle).length - 1;
const assertions = [];

function assertInvariant(name, condition, detail) {
  assertions.push({ name, condition, detail });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

assertInvariant(
  'MODULE_059_IMPORT_COUNT',
  count(app, importNeedle) === 1,
  `found ${count(app, importNeedle)}, expected 1`
);

assertInvariant(
  'MODULE_059_MOUNT_COUNT',
  count(app, mountNeedle) === 1,
  `found ${count(app, mountNeedle)}, expected 1`
);

assertInvariant(
  'MODULE_059_GLOBAL_MARKER_COUNT',
  count(app, markerNeedle) === 1,
  `found ${count(app, markerNeedle)}, expected 1`
);

assertInvariant(
  'MODULE_059_GLOBAL_HOST_CLASS_COUNT',
  count(app, hostClassNeedle) === 1,
  `found ${count(app, hostClassNeedle)}, expected 1`
);

assertInvariant(
  'MODULE_059_GLOBAL_SCOPE_COUNT',
  count(app, scopeNeedle) === 1,
  `found ${count(app, scopeNeedle)}, expected 1`
);

const boundaryIndex = app.indexOf(globalBoundaryNeedle);
const markerIndex = app.indexOf(markerNeedle);
const mountIndex = app.indexOf(mountNeedle);
const helpIndex = app.indexOf(helpNeedle);
const mainCloseIndex = app.indexOf(mainCloseNeedle, helpIndex);

assertInvariant(
  'MODULE_059_AFTER_ALL_ROUTE_CONTENT',
  boundaryIndex >= 0 && markerIndex > boundaryIndex,
  `boundary=${boundaryIndex}, marker=${markerIndex}`
);

assertInvariant(
  'MODULE_059_BEFORE_GLOBAL_HELP',
  markerIndex >= 0 && mountIndex > markerIndex && helpIndex > mountIndex,
  `marker=${markerIndex}, mount=${mountIndex}, help=${helpIndex}`
);

assertInvariant(
  'MODULE_059_INSIDE_AUTHENTICATED_MAIN_SHELL',
  mainCloseIndex > helpIndex,
  `help=${helpIndex}, mainClose=${mainCloseIndex}`
);

const globalHostSlice =
  markerIndex >= 0 && helpIndex > markerIndex
    ? app.slice(markerIndex, helpIndex)
    : '';

const forbiddenHostPatterns = [
  ['ACTIVE_ROUTE_CONDITION', /activeRoute\s*===/],
  ['PERMISSION_CONDITION', /hasPermission\s*\(|canSeeAny\s*\(/],
  ['TERNARY_CONDITION', /\?\s*\(/],
  ['LOGICAL_AND_CONDITION', /&&\s*\(/]
];

for (const [name, pattern] of forbiddenHostPatterns) {
  assertInvariant(
    `MODULE_059_HOST_HAS_NO_${name}`,
    !pattern.test(globalHostSlice),
    pattern.test(globalHostSlice) ? `matched ${pattern}` : 'not conditional'
  );
}

assertInvariant(
  'MODULE_059_HANDLE_PRESENT',
  drawer.includes('uss-session-intelligence-handle') &&
    drawer.includes('US Signal Session Intelligence'),
  'drawer exposes the fixed Session Intelligence handle'
);

const handleBlockMatch = css.match(/\.uss-session-intelligence-handle\s*\{([\s\S]*?)\}/);
const handleBlock = handleBlockMatch?.[1] ?? '';

assertInvariant(
  'MODULE_059_HANDLE_FIXED',
  /position\s*:\s*fixed\s*;/.test(handleBlock),
  'handle must remain fixed in the global shell'
);

assertInvariant(
  'MODULE_059_HANDLE_NOT_HIDDEN',
  !/display\s*:\s*none\s*;/.test(handleBlock) &&
    !/visibility\s*:\s*hidden\s*;/.test(handleBlock) &&
    !/opacity\s*:\s*0(?:\.0+)?\s*;/.test(handleBlock),
  'handle must not be hidden by its base rule'
);

const routeMatches = [...app.matchAll(/\broute\s*:\s*'([^']+)'/g)];
const routes = [...new Set(routeMatches.map((match) => match[1]))].sort();

assertInvariant(
  'MODULE_059_REGISTERED_ROUTE_COUNT',
  routes.length > 0,
  `${routes.length} unique registered routes`
);

console.log(`MODULE_059_COVERED_ROUTES=${routes.join(',')}`);
console.log('MODULE_059_ROUTE_SCOPE=ALL_AUTHENTICATED_CURRENT_AND_FUTURE_APP_SHELL_MODULES');
console.log('MODULE_059_NUMBERING_POLICY=MODULES_001_THROUGH_999');

const failed = assertions.filter((assertion) => !assertion.condition);

if (failed.length > 0) {
  console.error('');
  console.error('Module 059 global route contract failed.');
  failed.forEach((failure) => {
    console.error(`- ${failure.name}: ${failure.detail}`);
  });
  process.exit(1);
}

console.log('');
console.log('MODULE_059_GLOBAL_CONTRACT=PASSED');

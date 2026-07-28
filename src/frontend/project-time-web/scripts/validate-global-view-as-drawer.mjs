import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const fail = (message) => {
  console.error(`GLOBAL_VIEW_AS_DRAWER_VALIDATION=FAIL: ${message}`);
  process.exit(1);
};
const requireText = (source, expected, label) => {
  if (!source.includes(expected)) fail(`${label} is missing: ${expected}`);
};
const rejectText = (source, rejected, label) => {
  if (source.includes(rejected)) fail(`${label} contains forbidden text: ${rejected}`);
};
const count = (source, token) => source.split(token).length - 1;

const main = read('src/main.jsx');
const drawer = read('src/GlobalViewAsDrawer.jsx');
const css = read('src/global-view-as-drawer.css');
const app = read('src/App.jsx');

requireText(main, "import GlobalViewAsDrawer from './GlobalViewAsDrawer.jsx';", 'main mount');
requireText(main, '<GlobalViewAsDrawer />', 'main mount');
if (count(main, '<GlobalViewAsDrawer />') !== 1) fail('GlobalViewAsDrawer must be mounted exactly once.');

requireText(drawer, "const VIEW_AS_STORAGE_KEY = 'projectPulseViewAsUser';", 'drawer storage contract');
requireText(drawer, "const VIEW_AS_CHANGED_EVENT = 'projectpulse:view-as-changed';", 'drawer event contract');
requireText(drawer, "const VIEW_AS_USERS_ENDPOINT = '/api/project-workspace/view-as/users';", 'drawer eligible-user contract');
requireText(drawer, 'window.__projectPulseOriginalFetch || window.fetch.bind(window)', 'underlying administrator fetch');
requireText(drawer, "'X-ProjectPulse-Session': session.sessionToken", 'administrator session header');
requireText(drawer, 'My Administrator view', 'administrator option');
requireText(drawer, 'Administrator View-As', 'drawer title');
requireText(drawer, 'View-As Active', 'active handle state');
requireText(drawer, 'Exit preview', 'exit control');
requireText(drawer, 'aria-controls="projectpulse-view-as-drawer"', 'accessible handle');
requireText(drawer, 'role="dialog"', 'accessible drawer');
requireText(drawer, 'aria-modal="true"', 'modal contract');
requireText(drawer, "event.key === 'Escape'", 'keyboard close contract');
requireText(drawer, 'projectpulse-view-as-backdrop', 'backdrop close contract');
requireText(drawer, 'Your underlying\n              Super Administrator role does not override that user&apos;s restrictions.', 'effective-user explanation');
requireText(drawer, 'Write operations remain blocked while a View-As identity is active.', 'read-only explanation');
rejectText(drawer, '/api/admin/users/roles', 'drawer mutation boundary');
rejectText(drawer, 'method: \'POST\'', 'drawer mutation boundary');
rejectText(drawer, 'method: \'PUT\'', 'drawer mutation boundary');
rejectText(drawer, 'method: \'DELETE\'', 'drawer mutation boundary');

requireText(css, '#projectpulse-global-view-as,', 'legacy widget suppression');
requireText(css, '#projectpulse-global-view-as-topbar-slot', 'legacy top-bar slot suppression');
requireText(css, 'display: none !important;', 'legacy top-bar suppression');
requireText(css, '.projectpulse-view-as-handle', 'left-edge handle');
requireText(css, 'left: 0;', 'left-edge placement');
requireText(css, 'bottom: 5.75rem;', 'non-overlapping lower-left placement');
requireText(css, 'writing-mode: vertical-rl;', 'collapsed vertical handle');
requireText(css, '.projectpulse-view-as-drawer.is-open', 'drawer open state');
requireText(css, 'transform: translateX(-105%) !important;', 'collapsed drawer state');
requireText(css, 'transform: translateX(0) !important;', 'expanded drawer state');
requireText(css, '.projectpulse-view-as-backdrop', 'drawer backdrop');
requireText(css, 'body.projectpulse-view-as-drawer-open', 'page scroll guard');

requireText(app, "const STORAGE_KEY = 'projectPulseViewAsUser';", 'existing View-As authorization bridge');
requireText(app, "headers.set('X-ProjectPulse-View-As-User', viewAs.userId);", 'effective-user request header');
requireText(app, "status: 'view_as_read_only'", 'read-only write guard');
requireText(app, "const isWrite = !['GET', 'HEAD', 'OPTIONS'].includes(method);", 'write-method guard');

console.log('GLOBAL_VIEW_AS_DRAWER_VALIDATION=PASS');
console.log('GLOBAL_VIEW_AS_LEGACY_TOPBAR=HIDDEN');
console.log('GLOBAL_VIEW_AS_LEFT_HANDLE=COLLAPSED_BY_DEFAULT');
console.log('GLOBAL_VIEW_AS_EFFECTIVE_USER_SECURITY=PRESERVED');

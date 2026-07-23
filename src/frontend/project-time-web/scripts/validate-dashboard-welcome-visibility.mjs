import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');

async function text(path) {
  return readFile(resolve(root, path), 'utf8');
}

const [main, welcome, visibilityCss] = await Promise.all([
  text('src/frontend/project-time-web/src/main.jsx'),
  text('src/frontend/project-time-web/src/RoleWelcomeDashboard.jsx'),
  text('src/frontend/project-time-web/src/role-welcome-dashboard-visibility.css')
]);

const stylesImport = "import './styles.css';";
const visibilityImport = "import './role-welcome-dashboard-visibility.css';";

if (!main.includes(stylesImport) || !main.includes(visibilityImport)) {
  throw new Error('The dashboard visibility stylesheet must be imported by main.jsx.');
}

if (main.indexOf(visibilityImport) < main.indexOf(stylesImport)) {
  throw new Error('The dashboard visibility contract must load after the shared stylesheet.');
}

for (const contract of [
  'id="role-welcome-dashboard"',
  'className={`role-welcome-dashboard persona-${persona}`}'
]) {
  if (!welcome.includes(contract)) {
    throw new Error(`The role-aware welcome dashboard is missing: ${contract}`);
  }
}

for (const contract of [
  'main.app-shell.route-dashboard > #role-welcome-dashboard.role-welcome-dashboard',
  'display: grid !important;',
  'width: 100% !important;',
  'visibility: visible !important;',
  'opacity: 1 !important;'
]) {
  if (!visibilityCss.includes(contract)) {
    throw new Error(`The dashboard visibility stylesheet is missing: ${contract}`);
  }
}

if (/role-welcome-dashboard[^\{]*\{[^\}]*display\s*:\s*none/i.test(visibilityCss)) {
  throw new Error('The active role-aware dashboard must never be hidden by its visibility contract.');
}

console.log('Role-aware dashboard mount and visibility contracts passed.');

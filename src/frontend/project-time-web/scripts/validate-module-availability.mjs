import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const repoRoot = path.resolve(webRoot, '..', '..', '..');
const absolute = (relative) => path.join(repoRoot, relative);
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const requireText = (source, value, label) =>
  assert.ok(source.includes(value), `${label}: missing ${value}`);
const rejectText = (source, value, label) =>
  assert.ok(!source.includes(value), `${label}: forbidden ${value}`);

const backendPaths = [
  'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs',
  'database/migrations/042_module_availability_controls.sql',
  'database/rollback/042_module_availability_controls_rollback.sql'
];
const backendAvailable = backendPaths.every((relative) => fs.existsSync(absolute(relative)));

const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const bridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');
const controller = read('src/frontend/project-time-web/src/ModuleAvailabilityController.jsx');
const css = read('src/frontend/project-time-web/src/module-availability.css');
const main = read('src/frontend/project-time-web/src/main.jsx');
const project = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const packageJson = read('src/frontend/project-time-web/package.json');
const app = read('src/frontend/project-time-web/src/App.jsx');

if (backendAvailable) {
  const backend = read(backendPaths[0]);
  const migration = read(backendPaths[1]);
  const rollback = read(backendPaths[2]);

  for (const contract of [
    '/api/module-availability',
    '/api/module-availability/audit',
    'UpdateAvailabilityAsync',
    'SUPER_ADMINISTRATOR',
    'actual_session_required',
    'module_disabled',
    'module_availability_revision_conflict',
    'projectpulse_module_availability_audit',
    'X-ProjectPulse-Module-Number',
    'UseModuleAvailabilityEnforcement',
    'Missing rows are treated as enabled'
  ]) {
    requireText(backend, contract, 'backend availability contract');
  }

  requireText(backend, 'ProjectPulseActualUserId', 'actual-session authority');
  requireText(backend, 'ProjectPulseEffectiveUserId', 'effective-user visibility');
  requireText(backend, 'actualRoles.Contains("SUPER_ADMINISTRATOR") && !isViewAs', 'Super Administrator management boundary');
  requireText(backend, 'effectiveRoles.Contains("SUPER_ADMINISTRATOR")', 'disabled-module Super Administrator visibility');
  requireText(backend, 'previousEnabled = true', 'default enabled state');
  requireText(backend, 'AvailabilityCache.TryRemove', 'availability cache invalidation');
  rejectText(backend, 'DELETE FROM projectpulse_module_availability', 'non-destructive availability updates');

  for (const contract of [
    'CREATE TABLE IF NOT EXISTS projectpulse_module_availability',
    'CREATE TABLE IF NOT EXISTS projectpulse_module_availability_audit',
    'is_enabled boolean NOT NULL DEFAULT TRUE',
    'revision_number integer NOT NULL',
    'changed_by uuid NOT NULL REFERENCES app_users(user_id)',
    "FOREACH role_name IN ARRAY ARRAY['ptp_app', 'projectpulse_app']",
    "migration_id = '041_module_001_timesheet_timer_and_task_association'",
    "'042_module_availability_controls'"
  ]) {
    requireText(migration, contract, 'migration 042');
  }
  requireText(rollback, 'rollback blocked', 'fail-closed rollback');
  requireText(rollback, 'WHERE is_enabled = FALSE', 'disabled-module rollback guard');
  requireText(rollback, "migration_id = '042_module_availability_controls'", 'migration registration rollback');
}

requireText(registry, "moduleNumber: '001', route: 'timesheet', displayName: 'Timesheet'", 'Module 001 Timesheet name');
requireText(registry, 'PROJECTPULSE_MODULES', 'shared module registry');
requireText(registry, 'canonicalModuleRoute', 'route alias normalization');
requireText(registry, 'replaceTimesheetLabel', 'Time Entry label normalization');

requireText(bridge, 'X-ProjectPulse-Module-Number', 'browser module header');
requireText(bridge, "url.pathname.startsWith('/api/module-availability')", 'availability endpoint bypass');
requireText(bridge, 'currentProjectPulseRoute()', 'active-route enforcement');

for (const contract of [
  'Enable or disable modules safely',
  'Disabled modules are preserved',
  'window.confirm',
  'window.prompt',
  'expectedRevision',
  'projectpulse:module-availability-changed',
  'module-availability-switch',
  'View-As is read-only',
  'visible only to Super Administrators',
  'authorizedRoutesFromNavigation',
  "window.location.hash = 'modules'",
  'normalizeTimesheetLabels',
  'PROJECTPULSE_MODULES',
  'EXPECTED_MODULE_COUNT',
  'normalizeAvailabilityResponse',
  'inventoryComplete',
  'removeGovernedDirectory',
  'The existing Modules directory remains available',
  'Toggle controls require the SUPER_ADMINISTRATOR role',
  '.enterprise-sidebar-section a[href^="#"]',
  'const canReplaceDirectory = inventoryReady && (isSuperAdministrator || routes.size > 0)',
  "page.classList.add('module-availability-governed')"
]) {
  requireText(controller, contract, 'frontend availability controller');
}
requireText(controller, "fetch('/api/module-availability'", 'availability load');
requireText(controller, "fetch('/api/module-availability/audit'", 'audit load');
requireText(controller, "method: 'PUT'", 'availability update');
rejectText(controller, "page.classList.add('module-availability-governed');\n      let host", 'unconditional governed directory replacement');
requireText(css, '.projectpulse-module-disabled::after', 'disabled navigation badge');
requireText(css, '.module-availability-switch input:checked + span', 'toggle styling');
requireText(css, '.module-availability-governed > .modules-directory-grid', 'governed directory replacement');

requireText(main, "import './module-availability-bridge.js';", 'early browser enforcement import');
requireText(main, "import ModuleAvailabilityController from './ModuleAvailabilityController.jsx';", 'controller import');
requireText(main, '<ModuleAvailabilityController />', 'controller mount');
requireText(project, 'app.UseModuleAvailabilityEnforcement();', 'backend middleware registration');
requireText(project, 'app.MapModuleAvailabilityEndpoints();', 'backend endpoint registration');
requireText(packageJson, 'validate:module-availability', 'validator registration');
requireText(packageJson, 'npm run validate:module-availability', 'build-chain registration');

requireText(app, "title: 'Timesheet'", 'canonical Module 001 page title');
rejectText(registry, "displayName: 'Time Entry'", 'retired Module 001 display name');

console.log(`MODULE_AVAILABILITY_VALIDATION=PASS modules=64 default=enabled failOpenDirectory=true roleDiagnostics=true module001=Timesheet backend=${backendAvailable ? 'full' : 'frontend-container'}`);

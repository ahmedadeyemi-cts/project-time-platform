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

const fullBackendPaths = [
  'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs',
  'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityOverridesModule.cs',
  'database/migrations/042_module_availability_controls.sql',
  'database/rollback/042_module_availability_controls_rollback.sql'
];
const fullBackendAvailable = fullBackendPaths.every((relative) => fs.existsSync(absolute(relative)));

const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const bridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');
const controller = read('src/frontend/project-time-web/src/ModuleAvailabilityController.jsx');
const directory = read('src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx');
const css = read('src/frontend/project-time-web/src/module-availability.css');
const main = read('src/frontend/project-time-web/src/main.jsx');
const project = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const packageJson = read('src/frontend/project-time-web/package.json');
const app = read('src/frontend/project-time-web/src/App.jsx');

if (fullBackendAvailable) {
  const backend = read(fullBackendPaths[0]);
  const overrides = read(fullBackendPaths[1]);
  const migration = read(fullBackendPaths[2]);
  const rollback = read(fullBackendPaths[3]);

  for (const contract of [
    '/api/module-availability',
    '/api/module-availability/audit',
    'UpdateAvailabilityAsync',
    'SUPER_ADMINISTRATOR',
    'module_disabled',
    'module_availability_revision_conflict',
    'projectpulse_module_availability_audit',
    'X-ProjectPulse-Module-Number',
    'UseModuleAvailabilityEnforcement',
    'Missing rows are treated as enabled'
  ]) {
    requireText(backend, contract, 'backend availability enforcement');
  }
  requireText(backend, 'previousEnabled = true', 'default enabled state');
  requireText(backend, 'AvailabilityCache.TryRemove', 'availability cache invalidation');
  rejectText(backend, 'DELETE FROM projectpulse_module_availability', 'non-destructive availability updates');

  for (const contract of [
    '/api/module-availability/overrides',
    'Only persisted overrides are returned; missing rows mean Enabled.',
    'registeredModuleCount = RegisteredModuleCount',
    'states,',
    'missingOverrideBehavior = "ENABLED"',
    'actualRoles.Contains("SUPER_ADMINISTRATOR") && !isViewAs',
    'effectiveRoles.Contains("SUPER_ADMINISTRATOR")',
    'SELECT module_number, is_enabled, revision_number, reason, updated_at'
  ]) {
    requireText(overrides, contract, 'lightweight override endpoint');
  }
  rejectText(overrides, 'Definitions.Values', 'override endpoint must not return a second module inventory');

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
requireText(registry, 'replaceTimesheetLabel', 'Time Entry label normalization');
rejectText(registry, "displayName: 'Time Entry'", 'retired Module 001 display name');

requireText(bridge, 'X-ProjectPulse-Module-Number', 'browser module header');
requireText(bridge, "url.pathname.startsWith('/api/module-availability')", 'availability endpoint bypass');
requireText(bridge, 'currentProjectPulseRoute()', 'active-route enforcement');

for (const contract of [
  "fetch('/api/module-availability/overrides'",
  'clearAvailabilityNavigationState',
  'applyModuleNavigationState',
  'window.__projectPulseModuleAvailabilityOverrides',
  "window.location.hash = 'modules'",
  'normalizeTimesheetLabels',
  'PROJECTPULSE_MODULES'
]) {
  requireText(controller, contract, 'navigation-only availability controller');
}
rejectText(controller, 'createPortal', 'controller must not replace the Modules directory');
rejectText(controller, 'module-availability-directory-host', 'controller must not create a second directory host');
rejectText(controller, 'module-availability-governed', 'controller must not suppress the original directory');
rejectText(controller, "fetch('/api/module-availability',", 'controller must use lightweight overrides');
rejectText(controller, "fetch('/api/module-availability/audit'", 'controller must not own directory audit rendering');
rejectText(controller, "method: 'PUT'", 'controller must not own card toggles');

for (const contract of [
  "fetch('/api/module-availability/overrides'",
  "method: 'PUT'",
  'window.confirm',
  'window.prompt',
  'expectedRevision: module.revision',
  'Missing overrides default to Enabled',
  'Toggle controls require SUPER_ADMINISTRATOR',
  'module-availability-switch',
  'data-module-number={module.moduleNumber}',
  'data-module-route={module.route}',
  "if (route === 'timesheet') return 'Timesheet'",
  'Existing module cards remain available',
  'availability.loaded && !isSuperAdministrator && !module.isEnabled'
]) {
  requireText(directory, contract, 'existing Modules directory availability controls');
}
rejectText(directory, 'module-availability-directory-host', 'existing directory must not create a replacement host');
rejectText(directory, 'module-availability-governed', 'existing directory must never be hidden by availability');

requireText(css, '.projectpulse-module-disabled::after', 'disabled navigation badge');
requireText(css, '.module-availability-switch input:checked + span', 'toggle styling');
requireText(css, '.modules-directory-availability-bar', 'existing directory availability summary');
requireText(css, '.modules-directory-card.disabled', 'disabled card styling');
rejectText(css, '.module-availability-governed > .modules-directory-grid', 'obsolete directory suppression');
rejectText(css, '.module-availability-directory', 'obsolete replacement directory styling');

requireText(main, "import './module-availability-bridge.js';", 'early browser enforcement import');
requireText(main, "import ModuleAvailabilityController from './ModuleAvailabilityController.jsx';", 'controller import');
requireText(main, '<ModuleAvailabilityController />', 'controller mount');
requireText(project, 'app.UseModuleAvailabilityEnforcement();', 'backend middleware registration');
requireText(project, 'app.MapModuleAvailabilityEndpoints();', 'backend write and audit endpoint registration');
requireText(project, 'app.MapModuleAvailabilityOverrideEndpoints();', 'lightweight override endpoint registration');
requireText(packageJson, 'validate:module-availability', 'validator registration');
requireText(packageJson, 'npm run validate:module-availability', 'build-chain registration');
requireText(app, "title: 'Timesheet'", 'canonical Module 001 page title');

console.log(`MODULE_AVAILABILITY_VALIDATION=PASS design=existing-directory overrides-only default=enabled module001=Timesheet backend=${fullBackendAvailable ? 'full' : 'frontend-container'}`);

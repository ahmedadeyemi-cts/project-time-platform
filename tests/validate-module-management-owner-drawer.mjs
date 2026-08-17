import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const files = {
  view: path.join(root, 'src/frontend/project-time-web/src/ModuleManagementTableView.jsx'),
  css: path.join(root, 'src/frontend/project-time-web/src/module-management-table.css'),
  migration: path.join(root, 'database/migrations/091_module_management_owner_storage_repair.sql'),
  rollback: path.join(root, 'database/rollback/091_module_management_owner_storage_repair_rollback.sql')
};

function fail(message) {
  console.error(`MODULE_MANAGEMENT_OWNER_DRAWER_VALIDATION_FAILED: ${message}`);
  process.exitCode = 1;
}

function requireFile(name, file) {
  if (!fs.existsSync(file)) {
    fail(`${name} is missing: ${path.relative(root, file)}`);
    return '';
  }
  return fs.readFileSync(file, 'utf8');
}

function requireMarkers(name, source, markers) {
  for (const marker of markers) {
    if (!source.includes(marker)) fail(`${name} is missing marker: ${marker}`);
  }
}

const view = requireFile('Table view', files.view);
const css = requireFile('Table styles', files.css);
const migration = requireFile('Migration 091', files.migration);
const rollback = requireFile('Rollback 091', files.rollback);

requireMarkers('Table view', view, [
  "import IdentityAvatar from './identity/IdentityAvatar.jsx'",
  "import module006CustomerBrands from './assets/module-006-customer-brands.svg'",
  "fetch('/api/module-catalog/owners'",
  "fetch('/api/identity/profile'",
  "fetch('/api/profile/preferences'",
  "projectpulse:profile-preferences-changed",
  'module-management-detail-panel',
  'role="tablist"',
  'Overview',
  'Access',
  'Configuration',
  'History',
  'Customer Programs',
  'module006CustomerBrands',
  'Only an actual Super Administrator session can change module ownership',
  'Module ownership is accountability metadata only',
  'View-As remains read-only',
  'Copy Module Link',
  'Audit History'
]);

requireMarkers('Table styles', css, [
  '.module-management-table-workspace.has-detail-panel',
  '.module-management-detail-panel',
  '.module-management-detail-tabs',
  '.module-management-drawer-backdrop',
  '@media (max-width: 1120px)',
  '@media (prefers-reduced-motion: reduce)'
]);

requireMarkers('Migration 091', migration, [
  "lower('Ahmed.Adeyemi@ussignal.local')",
  'ADD COLUMN IF NOT EXISTS owner_user_id',
  'MODULE_OWNER_DEFAULT_ASSIGNED',
  'ownershipDoesNotGrantAccess',
  '091_module_management_owner_storage_repair',
  'ON CONFLICT (migration_id) DO NOTHING',
  "to_regclass('public.auth_external_identity_links') IS NOT NULL"
]);

requireMarkers('Rollback 091', rollback, [
  'module_catalog_ownership_091_evidence',
  'assigned_owner_user_id',
  'assigned_owner_revision_number',
  'refusing an unprovable owner rollback',
  "migration_id = '091_module_management_owner_storage_repair'"
]);

if (/Ahmed Adeyemi/.test(view) || /Ahmed\.Adeyemi@ussignal\.local/i.test(view)) {
  fail('The React presentation must resolve the signed-in owner dynamically rather than hardcoding Ahmed identity values.');
}

const credentialMarkers = view.match(/credentials:\s*'include'/g) || [];
if (credentialMarkers.length < 4) {
  fail('Authenticated ownership, identity, profile-preference, and owner-write requests must preserve existing cookie/session behavior.');
}

if (!/role="button"/.test(view) || !/aria-controls="module-management-detail-panel"/.test(view)) {
  fail('Module rows must expose keyboard-accessible detail-panel behavior.');
}

if (!/owner_user_id IS DISTINCT FROM owner_id/.test(migration)) {
  fail('Migration 091 must avoid increasing owner revisions when the requested owner is already assigned.');
}

if (!/module\.owner_user_id IS NOT DISTINCT FROM evidence\.assigned_owner_user_id/.test(rollback)) {
  fail('Rollback 091 must preserve owner changes made after migration 091.');
}

if (view.includes('/assets/brand-customer-programs.svg')) {
  fail('Module 006 branding must reuse the existing bundled SVG asset rather than a missing public path.');
}

if (process.exitCode) process.exit(process.exitCode);
console.log('MODULE_MANAGEMENT_OWNER_DRAWER_VALIDATION=PASS');

import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const files = {
  view: path.join(root, 'src/frontend/project-time-web/src/ModuleManagementTableView.jsx'),
  css: path.join(root, 'src/frontend/project-time-web/src/module-management-table.css'),
  authority: path.join(root, 'src/backend/ProjectTime.Api/Modules/ProjectPulseActualSessionAuthority.cs'),
  ownership: path.join(root, 'src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs'),
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

const view = requireFile('Enterprise Module Management view', files.view);
const css = requireFile('Enterprise Module Management styles', files.css);
const authority = requireFile('Actual-session authority resolver', files.authority);
const ownership = requireFile('Module ownership API', files.ownership);
const migration = requireFile('Migration 091', files.migration);
const rollback = requireFile('Rollback 091', files.rollback);

requireMarkers('Enterprise Module Management view', view, [
  "import IdentityAvatar from './identity/IdentityAvatar.jsx'",
  "import module006CustomerBrands from './assets/module-006-customer-brands.svg'",
  "fetch('/api/module-catalog/owners'",
  "fetch('/api/identity/profile'",
  "fetch('/api/profile/preferences'",
  "projectpulse:profile-preferences-changed",
  "body?.access?.canManageOwners === true || body?.access?.canManage === true",
  "const canChangeOwner = ownership.canManage && !viewAsReadOnly",
  "if (!ownership.canManage || ownership.isViewAs",
  'module-management-enterprise-header',
  'module-management-enterprise-layout',
  'module-management-rail',
  'module-management-command-bar',
  'module-management-active-filters',
  'module-management-grid',
  'module-management-pagination',
  'All Modules',
  'My Available Modules',
  'Customer Solutions',
  'Core Operations',
  'Project Management',
  'Recently Updated',
  'Disabled Modules',
  'Assigned Roles',
  'Module Owner',
  'Recently Changed',
  'Search by module number, name, route, or customer',
  'Rows per page:',
  'module-management-detail-panel',
  'role="tablist"',
  'Overview',
  'Access',
  'Configuration',
  'History',
  'Customer Programs',
  'module006CustomerBrands',
  'Assign to me',
  'Module ownership is accountability metadata only',
  'View-As is read-only',
  'Copy Module Link',
  'View Change History',
  'Review Dependencies'
]);

requireMarkers('Enterprise Module Management styles', css, [
  'MODULE_MANAGEMENT_ENTERPRISE_WORKSPACE_V3',
  'body.module-management-enterprise-active #pulse-enterprise-page-chrome-host',
  '.module-management-enterprise-layout.has-detail-panel',
  '.module-management-rail',
  '.module-management-command-bar',
  '.module-management-active-filters',
  '.module-management-grid',
  '.module-management-pagination',
  '.module-management-detail-panel',
  '.module-management-drawer-backdrop',
  '.module-management-rail-backdrop.visible',
  '@media (max-width: 1280px)',
  '@media (max-width: 680px)',
  '@media (prefers-reduced-motion: reduce)'
]);

requireMarkers('Actual-session authority resolver', authority, [
  'ResolveByUserIdAsync',
  'ResolveByApplicationEmailAsync',
  'ResolveByExternalIdentityAsync',
  "to_regclass('public.auth_external_identity_links') IS NOT NULL",
  'auth_external_identity_links external_identity',
  'actual_session_user_id',
  'actual_session_application_email',
  'actual_session_external_identity',
  'IsAdministratorRoleCode(roleCode)',
  'ProjectPulsePermanentFullControl',
  'if (IsViewAs(context)) return false'
]);

requireMarkers('Module ownership API', ownership, [
  '/api/module-catalog/owners',
  '/api/module-catalog/{moduleNumber}/owner',
  'ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync',
  'ownerCandidates',
  'MODULE_OWNER_CHANGED',
  'ownershipDoesNotGrantAccess',
  'Exit View-As before changing module ownership',
  'developer_super_administrator_only',
  'DeveloperOwnerRoleCodes',
  'IsDeveloperModuleOwnerAsync',
  'The selected owner must be an active developer Super Administrator.'
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

if (/const canChangeOwner\s*=\s*canManage\s*&&\s*ownership\.canManage/.test(view)
    || /if \(!canManage \|\| !ownership\.canManage/.test(view)) {
  fail('Module ownership authority must come from the actual-session ownership API and must not depend on the separate availability-control response.');
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
console.log('MODULE_MANAGEMENT_ENTERPRISE_OWNER_AUTHORITY_VALIDATION=PASS');

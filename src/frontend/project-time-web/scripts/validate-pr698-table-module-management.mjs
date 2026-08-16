import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const failures = [];
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const requireText = (content, marker, label) => {
  if (!content.includes(marker)) failures.push(`${label}: missing ${marker}`);
};
const forbidText = (content, marker, label) => {
  if (content.includes(marker)) failures.push(`${label}: forbidden ${marker}`);
};

const experience = read('src/EnterpriseExperienceController.jsx');
const drawer = read('src/DisplayPreferencesDrawer.jsx');
const drawerCss = read('src/display-preferences-drawer.css');
const table = read('src/ModuleManagementTableView.jsx');
const tableCss = read('src/module-management-table.css');
const portal = read('src/ModulesDirectoryPortal.jsx');
const main = read('src/main.jsx');
const backend = read('../../backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs');
const availability = read('../../backend/ProjectTime.Api/Modules/ModuleAvailabilityOverridesModule.cs');
const migration = read('../../../database/migrations/090_module_management_table_and_ownership.sql');
const rollback = read('../../../database/rollback/090_module_management_table_and_ownership_rollback.sql');
const catalogMigration = read('../../../database/migrations/089_module_catalog_role_administration_reconciliation.sql');

for (const content of [experience, drawer]) {
  requireText(content, "const TABLE_EXPERIENCE = 'table';", 'Table experience registration');
  requireText(content, "const TABLE_DEFAULT_VERSION = 'table-v1';", 'one-time Table default');
  requireText(content, 'dataset.pulseLayout = normalized', 'Table layout dataset');
}
requireText(drawer, '>\n        Appearance\n      </button>', 'single Appearance handle');
forbidText(drawer, 'pulse-display-theme-handle', 'redundant Theme handle removed');
requireText(drawer, '<strong>Table</strong>', 'Table preference choice');
requireText(drawerCss, 'one accessible drawer', 'Appearance drawer contract');
requireText(table, '<table className="module-management-table">', 'semantic Module Management table');
requireText(table, 'Owner for Module', 'inline owner editor');
requireText(table, '/api/module-catalog/owners', 'owner read endpoint');
requireText(table, '/owner`, {', 'owner update endpoint');
requireText(tableCss, ":root[data-pulse-layout='table'] .modules-directory-grid", 'card suppression in Table view');
requireText(portal, "import ModuleManagementTableView from './ModuleManagementTableView.jsx';", 'table portal integration');
requireText(main, "import './module-management-table.css';", 'table stylesheet order');
requireText(backend, 'Only an actual Super Administrator session can change module ownership.', 'Super Administrator write boundary');
requireText(backend, 'ownershipDoesNotGrantAccess', 'ownership/access separation');
requireText(backend, "'MODULE_OWNER_CHANGED'", 'immutable owner-change audit');
requireText(availability, 'private const int RegisteredModuleCount = 71;', 'complete module count');
requireText(availability, 'app.MapModuleCatalogOwnershipEndpoints();', 'ownership endpoint mapping');
requireText(migration, 'ahmed.adeyemi@ussignal.com', 'requested default owner');
requireText(migration, 'WHERE is_active = TRUE', 'all active modules assigned');
requireText(migration, 'ownershipDoesNotGrantAccess', 'migration access boundary');
requireText(rollback, 'Rollback 090 refused', 'guarded owner rollback');
requireText(catalogMigration, '089_module_catalog_role_administration_reconciliation', 'Module 001A and full catalog reconciliation');

if (failures.length) {
  console.error('PR698_TABLE_MODULE_MANAGEMENT=FAIL');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}
console.log('PR698_TABLE_MODULE_MANAGEMENT=PASS');

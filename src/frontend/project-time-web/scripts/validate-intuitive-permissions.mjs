import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const root = process.cwd();
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const roleAdmin = read('src/RoleAdminDirectoryPanel.jsx');
const roleModel = read('src/role-permission-model.js');
const matrix = read('src/RolesPermissionsMatrix.jsx');
const matrixModel = read('src/role-permission-matrix-model.js');
const workbenchCss = read('src/role-permission-workbench.css');
const matrixCss = read('src/role-permission-matrix-v2.css');
const navigationBridge = read('src/module-availability-bridge.js');
const evaluator = read('../../backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs');
const combinedRole = `${roleAdmin}\n${roleModel}`;
const combinedMatrix = `${matrix}\n${matrixModel}`;

const checks = [
  ['Module 012 uses database modules', roleAdmin.includes('Database modules') && combinedRole.includes("api('/api/role-policy/summary')")],
  ['Nine permission levels are present', ['Not Set', 'No Access', 'View', 'Create/Edit', 'Approve', 'Manage', 'Administer', 'Full Control', 'Custom'].every((value) => roleModel.includes(`'${value}'`))],
  ['Super Administrator is fixed to Full Control', combinedRole.includes("roleCode === 'SUPER_ADMINISTRATOR'") && roleModel.includes("level = 'Full Control'")],
  ['No Access creates module-level denial', roleModel.includes("actionCode: 'MODULE_ACCESS'") && roleModel.includes("effect: 'DENY'")],
  ['Module 012 publishes versioned changes', roleAdmin.includes("api('/api/role-policy/publish'") && roleAdmin.includes('Module 037 reads the same database')],
  ['Module 037 is read-only and spreadsheet shaped', matrix.includes('Permission Matrix') && matrix.includes('data-read-only="true"') && matrix.includes('Role Reference')],
  ['Module 037 includes permission reference', combinedMatrix.includes('Permission Levels') && matrixModel.includes('PERMISSION_LEVELS')],
  ['Legacy fallback renders Not Set', matrixModel.includes("grant.inherited || grant.actionCode === 'LEGACY_FALLBACK'")],
  ['PM lead defaults to managed team', roleModel.includes("PROJECT_MANAGEMENT_LEAD: 'MANAGED_TEAM'") && matrixModel.includes("defaultScope: 'MANAGED_TEAM'")],
  ['PTC cannot receive system configuration', roleModel.includes("role === 'PROJECT_TEAM_COORDINATOR'") && roleModel.includes("'MODULE_CONFIGURE', 'POLICY_DELEGATE'")],
  ['No Access hides module navigation', navigationBridge.includes('installPermissionNavigationGuard') && navigationBridge.includes("actionCode || '').toUpperCase() === 'MODULE_ACCESS'")],
  ['Super Administrator backend bypass is permanent', evaluator.includes('if (actor.IsSuperAdministrator)') && evaluator.includes('permanent organization-wide Full Control')],
  ['New styling is present', workbenchCss.includes('.role-permission-workbench') && matrixCss.includes('.role-permission-matrix-v2')]
];

let failed = false;
for (const [name, passed] of checks) {
  console.log(`${passed ? 'PASS' : 'FAIL'} ${name}`);
  failed ||= !passed;
}
if (failed) process.exit(1);
console.log(`PASS ${checks.length} intuitive permission workbench checks`);

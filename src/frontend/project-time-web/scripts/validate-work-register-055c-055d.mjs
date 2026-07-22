import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(repoRoot, relativePath), 'utf8');
const exists = (relativePath) => fs.existsSync(path.join(repoRoot, relativePath));

const files = {
  authorization: 'src/backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs',
  sellImport: 'src/backend/ProjectTime.Api/Modules/WorkRegisterSellImportModule.cs',
  purchaseOrder: 'src/backend/ProjectTime.Api/Modules/WorkRegisterPurchaseOrderModule.cs',
  frontend: 'src/frontend/project-time-web/src/WorkRegisterCenter.jsx',
  css: 'src/frontend/project-time-web/src/work-register-center.css',
  app: 'src/frontend/project-time-web/src/App.jsx',
  migration: 'database/migrations/035_work_register_055c_055d_split.sql',
  rollback: 'database/rollback/035_work_register_055c_055d_split_rollback.sql',
  editReadme: 'docs/modules/module-055c-edit-work-register/README.md',
  createReadme: 'docs/modules/module-055d-create-work-register/README.md'
};

const program = read('src/backend/ProjectTime.Api/Program.cs');
const app = read(files.app);
const frontend = read(files.frontend);
const css = read(files.css);
const authorization = read(files.authorization);
const sellImport = read(files.sellImport);
const purchaseOrder = read(files.purchaseOrder);
const migration = read(files.migration);
const rollback = read(files.rollback);
const docker = read('deployment/containers/web/Dockerfile');
const pkg = JSON.parse(read('src/frontend/project-time-web/package.json'));

let checks = 0;
let failures = 0;
function test(name, condition) {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`WORK_REGISTER_055C_055D_${name}=${condition ? 'PASSED' : 'FAILED'}`);
}

for (const [name, file] of Object.entries(files)) test(`FILE_${name.toUpperCase()}`, exists(file));

test('SEPARATE_ROUTES', app.includes("route: 'work-register'") && app.includes("route: 'create-work-register'"));
test('SEPARATE_TITLES', app.includes("title: 'Manage Existing Projects'") && app.includes("title: 'Create New Project'"));
test('SEPARATE_MODES', app.includes('<WorkRegisterCenter mode="edit" />') && app.includes('<WorkRegisterCenter mode="create" />'));
test('CREATE_STRICT_PTC_NAV', app.includes("strictRoleCodes: ['PROJECT_TEAM_COORDINATOR']"));
test('EDIT_EXACT_ROLES', ['PROJECT_MANAGER', 'PROJECT_MANAGEMENT_LEAD', 'PROJECT_TEAM_COORDINATOR'].every((role) => authorization.includes(`"${role}"`)));
test('CREATE_PTC_ONLY', authorization.includes('private static readonly string[] CreateRoleCodes = ["PROJECT_TEAM_COORDINATOR"]'));
test('VIEW_AS_MUTATION_BLOCK', authorization.includes('CanEdit: !isViewAs') && authorization.includes('CanCreate: !isViewAs'));
test('CENTRAL_AUTHORIZATION_MIDDLEWARE', program.includes('app.UseWorkRegisterAuthorization();'));
test('CREATE_API_GUARDED', program.includes('HasCreateAuthorityAsync') && sellImport.includes('HasCreateAuthorityAsync'));
test('GSD_AND_SELL_OPTIONS', frontend.includes('Import from GSD') && frontend.includes('Import from SELL'));
test('SELL_ENDPOINT', sellImport.includes('/api/work-register/intake/packages/sell/import') && program.includes('app.MapWorkRegisterSellImportEndpoints();'));
test('SELL_MODULE_026_CREDENTIAL', sellImport.includes('CrmErpIntegrationModule.LoadCredentialAsync'));
test('SELL_SOURCE_LOCK_UI', frontend.includes('sellAuthoritativeReview') && frontend.includes('disabled={sellAuthoritativeReview}'));
test('SELL_SOURCE_LOCK_SERVER', program.includes("WHEN source_mode = 'sell_import'") && program.includes("'projectName', extracted_json->'projectName'") && program.includes("'rates', extracted_json->'rates'"));
const createEndpoint = program.slice(
  program.indexOf('app.MapPost("/api/work-register/intake/packages/{intakePackageId:guid}/commit"'),
  program.indexOf('/* 055D_4C_FINAL_SAVE_ENDPOINT_END */')
);
test('CREATE_AUDIT', createEndpoint.includes("'work_register_created'") && createEndpoint.includes('Project Team Coordinator created Work Register'));
test('CREATE_AUDIT_ATOMIC', createEndpoint.includes('BeginTransactionAsync') && createEndpoint.includes('connection, transaction') && createEndpoint.includes('auditGuardCommand') && createEndpoint.includes('transaction.CommitAsync'));
test('EXISTING_HISTORY_TABLE_USER_FK', migration.includes('ADD COLUMN IF NOT EXISTS changed_by_user_id') && migration.includes('fk_work_register_change_history_changed_by_user') && migration.includes('FOREIGN KEY (changed_by_user_id)'));
test('POST_CREATE_CONTROLS', frontend.includes('Create Another Project') && !css.includes('.work-register-center.create-mode > .work-register-create-button'));
test('EDIT_AUDIT', program.includes('work_register_change_history') && purchaseOrder.includes("'purchase_order_updated'"));
test('REASON_REQUIRED', purchaseOrder.includes('A change reason is required for Work Register audit history'));
test('PERMISSIONS', migration.includes('EDIT_WORK_REGISTER_055C') && migration.includes('CREATE_WORK_REGISTER_055D'));
test('ROLLBACK_PRESERVES_AUDIT', rollback.includes('Preserves Work Register audit history') && !rollback.includes('DROP TABLE'));
test('MIGRATION_NOT_RUNTIME_APPLIED', !program.includes('035_work_register_055c_055d_split.sql'));
test('CONTAINER_CONTEXT', Object.values(files).every((file) => (
  docker.includes(file)
  || (file.startsWith('src/frontend/project-time-web/') && docker.includes('COPY src/frontend/project-time-web/'))
  || (file.startsWith('docs/modules/') && docker.includes(`${path.dirname(file)}/`))
)));
test('BUILD_GATE', pkg.scripts?.['validate:work-register-055c-055d'] === 'node ./scripts/validate-work-register-055c-055d.mjs' && pkg.scripts?.build?.includes('npm run validate:work-register-055c-055d'));

console.log(`WORK_REGISTER_055C_055D_VALIDATION_CHECKS=${checks}`);
console.log('WORK_REGISTER_055C_EDIT_ROLES=PROJECT_MANAGER_PROJECT_MANAGEMENT_LEAD_PROJECT_TEAM_COORDINATOR');
console.log('WORK_REGISTER_055D_CREATE_ROLE=PROJECT_TEAM_COORDINATOR_ONLY');
console.log('WORK_REGISTER_055D_SOURCES=GSD_SELL');
console.log('WORK_REGISTER_055C_055D_MIGRATION_035=CREATED_NOT_APPLIED');
console.log(`WORK_REGISTER_055C_055D_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;

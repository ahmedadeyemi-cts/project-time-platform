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
  closeoutFrontend: 'src/frontend/project-time-web/src/ProjectCloseoutCenter.jsx',
  css: 'src/frontend/project-time-web/src/work-register-center.css',
  closeoutCss: 'src/frontend/project-time-web/src/project-closeout-center.css',
  app: 'src/frontend/project-time-web/src/App.jsx',
  migration: 'database/migrations/035_work_register_055c_055d_split.sql',
  scopeMigration: 'database/migrations/036_work_register_role_scope_and_closeout_handoff.sql',
  dateContractMigration: 'database/migrations/037_work_register_dates_and_contract_types.sql',
  rollback: 'database/rollback/035_work_register_055c_055d_split_rollback.sql',
  scopeRollback: 'database/rollback/036_work_register_role_scope_and_closeout_handoff_rollback.sql',
  dateContractRollback: 'database/rollback/037_work_register_dates_and_contract_types_rollback.sql',
  editReadme: 'docs/modules/module-055c-edit-work-register/README.md',
  createReadme: 'docs/modules/module-055d-create-work-register/README.md'
};

const program = read('src/backend/ProjectTime.Api/Program.cs');
const app = read(files.app);
const frontend = read(files.frontend);
const closeoutFrontend = read(files.closeoutFrontend);
const css = read(files.css);
const authorization = read(files.authorization);
const sellImport = read(files.sellImport);
const purchaseOrder = read(files.purchaseOrder);
const migration = read(files.migration);
const scopeMigration = read(files.scopeMigration);
const dateContractMigration = read(files.dateContractMigration);
const rollback = read(files.rollback);
const scopeRollback = read(files.scopeRollback);
const dateContractRollback = read(files.dateContractRollback);
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
test('CREATE_STRICT_AUTHORIZED_NAV', app.includes("strictRoleCodes: ['PROJECT_TEAM_COORDINATOR', 'ADMINISTRATOR', 'SUPER_ADMINISTRATOR']"));
test('EDIT_ALL_ROLES', ['PROJECT_TEAM_COORDINATOR', 'ADMINISTRATOR', 'SUPER_ADMINISTRATOR'].every((role) => authorization.includes(`"${role}"`)));
test('EDIT_ASSIGNED_ROLES', ['PROJECT_MANAGER', 'PROJECT_MANAGEMENT_LEAD', 'PM_TEAM_LEAD'].every((role) => authorization.includes(`"${role}"`)));
test('CREATE_AUTHORIZED_ROLES', authorization.includes('private static readonly string[] CreateRoleCodes') && ['PROJECT_TEAM_COORDINATOR', 'ADMINISTRATOR', 'SUPER_ADMINISTRATOR'].every((role) => authorization.includes(`"${role}"`)));
test('VIEW_AS_MUTATION_BLOCK', authorization.includes('CanEditAll: !isViewAs') && authorization.includes('CanEditAssigned: !isViewAs') && authorization.includes('CanCreate: !isViewAs'));
test('ASSIGNED_PM_SERVER_SCOPE', authorization.includes('IsAssignedProjectManagerAsync') && authorization.includes('project.project_manager_user_id = @user_id'));
test('PROJECT_ID_MUTATION_SCOPE', authorization.includes('ResolveProjectIdAsync') && authorization.includes('canonicalJsonPaths') && authorization.includes('aliasJsonPaths') && authorization.includes('ReadFormAsync') && authorization.includes('EnableBuffering'));
test('PROJECT_ID_CONFLICT_REJECTION', authorization.includes('WorkRegisterProjectIdResolutionStatus.Conflicting') && authorization.includes('conflicting_project_ids') && authorization.includes('IsEndpointProjectId'));
test('BUFFERED_JSON_BODY_READ', !authorization.includes('Request.Body.Length == 0') && authorization.includes('JsonDocument.ParseAsync'));
test('ARCHIVE_GUARD_SHARED_ID_RESOLUTION', program.includes('WorkRegisterAuthorization.ReadProjectUpdateIdText(lifecycleRoot)') && authorization.includes('internal static readonly string[] ProjectUpdateIdAliases'));
test('CENTRAL_AUTHORIZATION_MIDDLEWARE', program.includes('app.UseWorkRegisterAuthorization();'));
test('CREATE_API_GUARDED', program.includes('HasCreateAuthorityAsync') && sellImport.includes('HasCreateAuthorityAsync'));
test('GSD_AND_SELL_OPTIONS', frontend.includes('Import from GSD') && frontend.includes('Import from SELL'));
test('SELL_ENDPOINT', sellImport.includes('/api/work-register/intake/packages/sell/import') && program.includes('app.MapWorkRegisterSellImportEndpoints();'));
test('SELL_MODULE_026_CREDENTIAL', sellImport.includes('CrmErpIntegrationModule.LoadCredentialAsync'));
test('SELL_SOURCE_LOCK_UI', frontend.includes('sellAuthoritativeReview') && frontend.includes('disabled={sellAuthoritativeReview}'));
test('SELL_SOURCE_LOCK_SERVER', program.includes("WHEN source_mode = 'sell_import'") && program.includes("'projectName', extracted_json->'projectName'") && program.includes("'rates', extracted_json->'rates'"));
test(
  'CANONICAL_CONTRACT_TYPES',
  frontend.includes("return 'Time and Material';")
    && frontend.includes("return 'Fixed Price';")
    && frontend.includes('<option value="Time and Material">Time and Material</option>')
    && !frontend.includes('<option value="TM">Time and Material (TM)</option>')
    && sellImport.includes('"tm" or "timeandmaterial" or "timeandmaterials" => "Time and Material"')
);
test(
  'GSD_CONTRACT_CODE_MAPPING',
  dateContractMigration.includes("('tm', 'timeandmaterial', 'timeandmaterials')")
    && dateContractMigration.includes("THEN 'Time and Material'")
    && dateContractMigration.includes("('fp', 'fixedprice')")
    && dateContractMigration.includes("THEN 'Fixed Price'")
);
test(
  'EDIT_DATE_PERSISTENCE',
  frontend.includes('sowSignedDate: dateOnly(item.sowSignedDate)')
    && frontend.includes("addIfChanged('estimatedEndDate'")
    && frontend.includes("addIfChanged('sowSignedDate'")
    && dateContractMigration.includes("'estimatedEndDate'")
    && dateContractMigration.includes("'sowSignedDate'")
    && dateContractMigration.includes('trg_projectpulse037_after_edit_save')
);
test(
  'CREATE_DATE_PERSISTENCE',
  frontend.includes('estimatedEndDate: dateOnly(intakeReviewForm?.estimatedEndDate')
    && frontend.includes('estimatedEndDate: finalFields.estimatedEndDate')
    && frontend.includes('value={intakeReviewForm.estimatedEndDate ||')
    && dateContractMigration.includes('trg_projectpulse037_after_intake_commit')
    && dateContractMigration.includes("'estimatedEndDate', v_estimated_end_date")
);
test(
  'CREATE_INITIAL_DATE_ROUND_TRIP',
  frontend.includes('sowSignedDate: intakeForm.sowSignedDate')
    && frontend.includes('estimatedEndDate: intakeForm.estimatedEndDate')
    && program.includes('var sowSignedDateText = ReadFormString("sowSignedDate")')
    && program.includes('var estimatedEndDateText = ReadFormString("estimatedEndDate")')
    && program.includes("COALESCE(extracted_json->>'estimatedEndDate', '')")
    && program.includes('["estimatedEndDate"] = estimatedEndDate')
    && sellImport.includes('string? SowSignedDate')
    && sellImport.includes('string? EstimatedEndDate')
);
test('SOURCE_MODE_SCHEMA', migration.includes('ADD COLUMN IF NOT EXISTS source_mode') && migration.includes("SET source_mode = 'gsd_sow_upload'") && migration.includes("ALTER COLUMN source_mode SET DEFAULT 'gsd_sow_upload'") && migration.includes('ALTER COLUMN source_mode SET NOT NULL'));
const createEndpoint = program.slice(
  program.indexOf('app.MapPost("/api/work-register/intake/packages/{intakePackageId:guid}/commit"'),
  program.indexOf('/* 055D_4C_FINAL_SAVE_ENDPOINT_END */')
);
test(
  'CREATE_DATE_VALIDATION',
  frontend.includes('projectPulseCreateEstimatedEndDateValidationMessage')
    && frontend.includes('Estimated end date cannot be before the project creation date.')
    && createEndpoint.includes('CURRENT_DATE')
    && createEndpoint.includes('DateOnly.TryParseExact')
    && createEndpoint.includes('estimatedEndDate < projectStartDate')
    && sellImport.includes('parsedEstimatedEndDate < DateOnly.FromDateTime(DateTime.UtcNow)')
    && dateContractMigration.includes('v_estimated_end_date < v_project_start_date')
    && dateContractMigration.includes('violating chk_project_dates')
);
test('CREATE_AUDIT', createEndpoint.includes("'work_register_created'") && createEndpoint.includes('Authorized 055D user created Work Register'));
test('CREATE_AUDIT_ATOMIC', createEndpoint.includes('BeginTransactionAsync') && createEndpoint.includes('connection, transaction') && createEndpoint.includes('auditGuardCommand') && createEndpoint.includes('transaction.CommitAsync'));
test('EXISTING_HISTORY_TABLE_USER_FK', migration.includes('ADD COLUMN IF NOT EXISTS changed_by_user_id') && migration.includes('fk_work_register_change_history_changed_by_user') && migration.includes('FOREIGN KEY (changed_by_user_id)'));
test('POST_CREATE_CONTROLS', frontend.includes('Create Another Project') && !css.includes('.work-register-center.create-mode > .work-register-create-button'));
test(
  'ROUTE_ISOLATION_BOTH_MODES',
  css.includes('055C_WORK_REGISTER_ROUTE_ISOLATION_START')
    && css.includes('body[data-projectpulse-active-route="work-register"] section.panel:not(#work-register):not(.work-register-route-panel)')
    && css.includes('055D_CREATE_WORK_REGISTER_ROUTE_ISOLATION_START')
    && css.includes('body[data-projectpulse-active-route="create-work-register"] section.panel:not(#create-work-register):not(.work-register-route-panel)')
    && css.includes('body[data-projectpulse-active-route="create-work-register"] #create-work-register .work-register-center')
);
test('ROW_SPECIFIC_EDIT_SCOPE', program.includes('canEditProject = CanEditProject') && frontend.includes("item.canEditProject === true ? 'Edit work' : 'View details'") && frontend.includes('selectedWorkItem?.canEditProject === true'));
test('CLOSEOUT_HANDOFF_SOURCE', frontend.includes('startProjectCloseout') && frontend.includes('projectPulseProjectCloseoutHandoff') && frontend.includes("window.location.hash = 'project-closeout'"));
test('CLOSEOUT_HANDOFF_TARGET', closeoutFrontend.includes('readProjectCloseoutHandoff') && closeoutFrontend.includes('Opened ${handoffProject.projectCode} from Module 055C') && closeoutFrontend.includes("removeItem('projectPulseProjectCloseoutHandoff')"));
test('EDIT_AUDIT', program.includes('work_register_change_history') && purchaseOrder.includes("'purchase_order_updated'"));
test('REASON_REQUIRED', purchaseOrder.includes('A change reason is required for Work Register audit history'));
test('PERMISSIONS', migration.includes('EDIT_WORK_REGISTER_055C') && migration.includes('CREATE_WORK_REGISTER_055D'));
test('SCOPED_PERMISSION_MIGRATION', scopeMigration.includes("'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'") && scopeMigration.includes('036_work_register_role_scope_and_closeout_handoff'));
test('ROLLBACK_PRESERVES_AUDIT', rollback.includes('Preserves Work Register audit history') && !rollback.includes('DROP TABLE'));
test('SCOPED_ROLLBACK', scopeRollback.includes("DELETE FROM app_role_permissions") && scopeRollback.includes("036_work_register_role_scope_and_closeout_handoff"));
test('DATE_CONTRACT_ROLLBACK', dateContractRollback.includes('Existing human-readable contract data is') && dateContractRollback.includes('trg_projectpulse037_after_edit_save') && dateContractRollback.includes('037_work_register_dates_and_contract_types'));
test('MIGRATION_NOT_RUNTIME_APPLIED', !program.includes('035_work_register_055c_055d_split.sql') && !program.includes('036_work_register_role_scope_and_closeout_handoff.sql') && !program.includes('037_work_register_dates_and_contract_types.sql'));
test('CONTAINER_CONTEXT', Object.values(files).every((file) => (
  docker.includes(file)
  || (file.startsWith('src/frontend/project-time-web/') && docker.includes('COPY src/frontend/project-time-web/'))
  || (file.startsWith('docs/modules/') && docker.includes(`${path.dirname(file)}/`))
)));
test('BUILD_GATE', pkg.scripts?.['validate:work-register-055c-055d'] === 'node ./scripts/validate-work-register-055c-055d.mjs' && pkg.scripts?.build?.includes('npm run validate:work-register-055c-055d'));

console.log(`WORK_REGISTER_055C_055D_VALIDATION_CHECKS=${checks}`);
console.log('WORK_REGISTER_055C_ASSIGNED_EDIT_ROLES=PROJECT_MANAGER_PROJECT_MANAGEMENT_LEAD');
console.log('WORK_REGISTER_055C_EDIT_ALL_ROLES=PROJECT_TEAM_COORDINATOR_ADMINISTRATOR_SUPER_ADMINISTRATOR');
console.log('WORK_REGISTER_055D_CREATE_ROLES=PROJECT_TEAM_COORDINATOR_ADMINISTRATOR_SUPER_ADMINISTRATOR');
console.log('WORK_REGISTER_055D_SOURCES=GSD_SELL');
console.log('WORK_REGISTER_055C_055D_MIGRATION_037=CREATED_NOT_APPLIED');
console.log(`WORK_REGISTER_055C_055D_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;

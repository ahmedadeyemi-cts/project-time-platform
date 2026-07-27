import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  auditUi: 'src/frontend/project-time-web/src/AuditHistoryPanel.jsx',
  auditCss: 'src/frontend/project-time-web/src/audit-history.css',
  userUi: 'src/frontend/project-time-web/src/UserAdministrationPanel.jsx',
  userCss: 'src/frontend/project-time-web/src/user-administration-panel.css',
  themeJs: 'src/frontend/project-time-web/src/admin-experience-theme.js',
  themeCss: 'src/frontend/project-time-web/src/admin-experience-theme.css',
  common: 'src/backend/ProjectTime.Api/Modules/AdminExperienceCommon.cs',
  auditBackend: 'src/backend/ProjectTime.Api/Modules/AdminAuditHistoryModule.cs',
  teamBackend: 'src/backend/ProjectTime.Api/Modules/UserAdministrationTeamScopeModule.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  migration: 'database/migrations/048_admin_audit_and_manager_team_scope.sql',
  rollback: 'database/rollback/048_admin_audit_and_manager_team_scope_rollback.sql',
  package: 'src/frontend/project-time-web/package.json'
};

const absolute = (relative) => path.join(repositoryRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULES_008_009_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const name of ['auditUi', 'auditCss', 'userUi', 'userCss', 'themeJs', 'themeCss', 'package']) {
  check(`${name.toUpperCase()}_EXISTS`, exists(files[name]), files[name]);
}

const auditUi = read(files.auditUi);
const auditCss = read(files.auditCss);
const userUi = read(files.userUi);
const userCss = read(files.userCss);
const themeJs = read(files.themeJs);
const themeCss = read(files.themeCss);
const packageJson = JSON.parse(read(files.package));

check('AUDIT_UNIFIED_ENDPOINT', auditUi.includes('/api/admin/audit-history/events'), 'Module 008 consumes the unified endpoint');
check('AUDIT_CONDENSED_DETAILS', auditUi.includes('<details') && auditUi.includes('Sanitized evidence') && auditUi.includes('Immutable / append-only'), 'events are condensed and expandable');
check('AUDIT_FILTERS', ['Lookback', 'Category', 'Status', 'Source', 'Search history'].every((value) => auditUi.includes(value)), 'administrator filters are available');
check('AUDIT_ALL_SYSTEM_WORDING', auditUi.includes('service actions') && auditUi.includes('API lifecycle events') && auditUi.includes('other system history'), 'scope covers system-wide history');
check('AUDIT_SCOPED_STYLES', auditCss.includes('.audit-event-card') && auditCss.includes('.audit-event-facts') && auditCss.includes('.app-shell.route-audit-history'), 'Module 008 styles and route isolation');

check('USER_TABBED_INTERFACE', ['Manage users', 'Bulk updates', 'Create local user', 'Manager team scope'].every((value) => userUi.includes(value)), 'four clear Module 009 workspaces');
check('USER_SEARCH_FILTERS', userUi.includes('Search users') && userUi.includes('All roles') && userUi.includes('All teams') && userUi.includes('All accounts'), 'search and user filters');
check('USER_INDIVIDUAL_MANAGEMENT', userUi.includes('Individual user') && userUi.includes('Save user') && userUi.includes('Local account') && userUi.includes('Set password'), 'individual and local user management');
check('USER_BULK_MANAGEMENT', userUi.includes('/api/admin/user-admin/users/bulk-update') && userUi.includes('Apply one controlled change to several users'), 'bulk user tab');
check('USER_MULTI_TEAM_MANAGER', userUi.includes('/api/admin/user-admin/manager-team-assignments/') && userUi.includes('Assign one manager to multiple teams') && userUi.includes('selectedManagerTeams'), 'manager multiple-team assignment');
check('USER_MANAGER_EMAIL_AUTOMATION', userUi.includes('managerEmailForTeam') && userUi.includes('Automatically controlled by the active manager team assignment'), 'team manager email applied to user saves');
check('USER_SCOPED_STYLES', userCss.includes('.user-admin-v2-tabs') && userCss.includes('.user-admin-v2-team-grid') && userCss.includes('.user-admin-v2-user-list'), 'Module 009 scoped layout');

check('THEME_STRAY_TEXT_REMOVAL', themeJs.includes("/^(?:\\\\n|\\/n|n)$/i") && themeJs.includes('Node.TEXT_NODE'), 'literal newline artifact removed');
check('THEME_NO_APP_EDIT_REQUIRED', userUi.includes("import './admin-experience-theme.js';") && userUi.includes("import './admin-experience-theme.css';"), 'theme bridge loads through existing Module 009 import');
check('THEME_DESIGN', themeCss.includes('.theme-toggle.projectpulse-theme-control') && themeCss.includes("content: 'Dark mode'") && themeCss.includes("content: 'Light mode'"), 'branded light/dark control');

const backendAvailable = ['common', 'auditBackend', 'teamBackend', 'project'].every((name) => exists(files[name]));
if (backendAvailable) {
  const common = read(files.common);
  const auditBackend = read(files.auditBackend);
  const teamBackend = read(files.teamBackend);
  const project = read(files.project);

  check('BACKEND_REGISTRATION', project.includes('app.MapAdminAuditHistoryEndpoints();') && project.includes('app.MapUserAdministrationTeamScopeEndpoints();'), 'two additive generated Program registrations');
  check('BACKEND_ACTUAL_SESSION', common.includes('ProjectPulseActualUserId') && common.includes('ProjectPulseSessionUserId') && common.includes('ProjectPulseIsViewAs'), 'actual-session authority and View-As boundary');
  check('BACKEND_AUDIT_DISCOVERY', auditBackend.includes('information_schema.tables') && auditBackend.includes('(audit|history|event|log|outbox|sync_run|revision)'), 'existing history sources are dynamically discovered');
  check('BACKEND_AUDIT_REDACTION', auditBackend.includes('SensitiveKeys') && auditBackend.includes('[redacted]') && auditBackend.includes('connection_string'), 'sensitive evidence redaction');
  check('BACKEND_API_LIFECYCLE', auditBackend.includes('ApplicationStarted') && auditBackend.includes('ApplicationStopping') && auditBackend.includes('API_STARTED') && auditBackend.includes('API_STOPPING'), 'API start and stop evidence');
  check('BACKEND_MULTI_TEAM', teamBackend.includes('multipleTeamsPerManager = true') && teamBackend.includes('oneActiveManagerPerTeam = true'), 'manager scope contract');
  check('BACKEND_MEMBER_RECONCILIATION', teamBackend.includes('UPDATE app_users') && teamBackend.includes('manager_email = @manager_email') && teamBackend.includes('membersUpdated'), 'team members receive manager email');
  check('BACKEND_ATOMIC_SAVE', teamBackend.includes('BeginTransactionAsync') && teamBackend.includes('CommitAsync') && teamBackend.includes('RollbackAsync'), 'manager scope is transactional');
} else {
  console.log('MODULES_008_009_BACKEND_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (exists(files.migration) && exists(files.rollback)) {
  const migration = read(files.migration);
  const rollback = read(files.rollback);
  check('MIGRATION_048', migration.includes('048_admin_audit_and_manager_team_scope') && migration.includes('projectpulse_system_audit_events') && migration.includes('user_admin_manager_team_assignments'), 'additive migration 048');
  check('IMMUTABLE_LEDGER', migration.includes('BEFORE UPDATE OR DELETE') && migration.includes('projectpulse048_block_system_audit_mutation'), 'immutable audit trigger');
  check('ONE_MANAGER_PER_TEAM', migration.includes('ux_user_admin_one_active_manager_per_team') && migration.includes('lower(team_name)'), 'one active manager per team');
  check('GUARDED_ROLLBACK', rollback.includes('Rollback blocked: immutable ProjectPulse system audit evidence exists.') && rollback.includes('Rollback blocked: active manager-to-team assignments exist.'), 'operational evidence prevents rollback');
  check('NO_PROTECTED_MODULE_SCHEMA', !/azure_entra_settings|microsoft_integration|projectpulse_native_admin_documents|microsoft_integration_client_secrets|microsoft_integration_sso_client_secrets/i.test(migration), 'no Module 010/065 or Microsoft Integration tables are referenced');
} else {
  console.log('MODULES_008_009_MIGRATION_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:modules008009') && packageJson.scripts?.['validate:modules008009']?.includes('validate-modules-008-009-admin-experience.mjs'), 'validator is permanent in frontend build');

console.log('');
console.log(`MODULES_008_009_VALIDATION_CHECKS=${checks.length}`);
if (checks.some((item) => !item.condition)) {
  console.error('MODULES_008_009_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULES_008_009_CONTRACT=PASSED');

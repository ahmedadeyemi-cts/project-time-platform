import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  app: 'src/frontend/project-time-web/src/App.jsx',
  main: 'src/frontend/project-time-web/src/main.jsx',
  stableOwner: 'src/frontend/project-time-web/src/AdminRuntimeStabilityPortal.jsx',
  stableCss: 'src/frontend/project-time-web/src/admin-runtime-stability.css',
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

for (const name of ['app', 'main', 'stableOwner', 'stableCss', 'auditUi', 'auditCss', 'userUi', 'userCss', 'themeJs', 'themeCss', 'package']) {
  check(`${name.toUpperCase()}_EXISTS`, exists(files[name]), files[name]);
}

const app = read(files.app);
const main = read(files.main);
const stableOwner = read(files.stableOwner);
const stableCss = read(files.stableCss);
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
check('AUDIT_AUTH_FAILURE_VISIBLE', auditUi.includes('readApiErrorMessage') && auditUi.includes('audit-empty-state error') && auditUi.includes('Audit and History could not be loaded.'), 'backend authorization and session failures are visible rather than blank');
check(
  'AUDIT_SESSION_HEADER_COMPATIBILITY',
  auditUi.includes("const token = session?.sessionToken || session?.token || session?.accessToken")
    && auditUi.includes("'X-ProjectPulse-Session': session.token")
    && auditUi.includes("'X-Project-Pulse-Session': session.token")
    && auditUi.includes("'X-Session-Token': session.token")
    && auditUi.includes('Authorization: `Bearer ${session.token}`')
    && auditUi.includes("'X-ProjectPulse-Module-Number': '008'"),
  'Module 008 retains supported session headers and explicit module attribution'
);

const appAuditPanelMounts = app.match(/<AuditHistoryPanel\s*\/>/g) ?? [];
check(
  'AUDIT_STABLE_ROOT_OWNER',
  appAuditPanelMounts.length === 1
    && main.includes('<AdminRuntimeStabilityPortal />')
    && stableOwner.includes('window.__projectPulseModule008StableOwnerInstalled = true')
    && stableOwner.includes('<AuditHistoryPanel stableRouteOwner />')
    && auditUi.includes('window.__projectPulseModule008StableOwnerInstalled')
    && auditUi.includes('return null;'),
  'the root-mounted stable owner suppresses the permission-dependent App instance without creating a second query'
);
check(
  'AUDIT_NO_SELF_MOUNT',
  !auditUi.includes("from 'react-dom/client'")
    && !auditUi.includes('createRoot(')
    && !auditUi.includes('MutationObserver')
    && !stableOwner.includes("from 'react-dom/client'")
    && !stableOwner.includes('createRoot(')
    && !stableOwner.includes('MutationObserver'),
  'Module 008 uses the existing React root and never creates or mutates a competing DOM root'
);
check(
  'AUDIT_STABLE_ROUTE_PRESENTATION',
  stableCss.includes('body.projectpulse-route-audit-history .admin-runtime-stability-route-root')
    && stableCss.includes('.module010-audit-consolidation')
    && stableOwner.includes('Open Module 010 evidence in Audit and History')
    && auditUi.includes('Module 010 sync evidence'),
  'Module 008 remains visible and owns consolidated Module 010 synchronization evidence'
);
check(
  'AUDIT_BACKEND_AUTHORITY',
  auditUi.includes('/api/admin/audit-history/events')
    && !auditUi.includes('hasPermission(')
    && !auditUi.includes('VIEW_AUDIT_TRAIL')
    && !auditUi.includes('SYSTEM_ADMINISTRATION')
    && !auditUi.includes('MANAGE_ALL'),
  'component data access remains authorized by the API rather than duplicated client permission state'
);

check('USER_TABBED_INTERFACE', ['Manage users', 'Bulk updates', 'Create local user', 'Manager team scope'].every((value) => userUi.includes(value)), 'four clear Module 009 workspaces');
check('USER_SEARCH_FILTERS', userUi.includes('Search users') && userUi.includes('All roles') && userUi.includes('All teams') && userUi.includes('All accounts'), 'search and user filters');
check('USER_INDIVIDUAL_MANAGEMENT', userUi.includes('Individual user') && userUi.includes('Save user') && userUi.includes('Local account') && userUi.includes('Set password'), 'individual and local user management');
check('USER_BULK_MANAGEMENT', userUi.includes('/api/admin/user-admin/users/bulk-update') && userUi.includes('Apply one controlled change to several users'), 'bulk user tab');
check('USER_MULTI_TEAM_MANAGER', userUi.includes('/api/admin/user-admin/manager-team-assignments/') && userUi.includes('Assign one manager to multiple teams') && userUi.includes('selectedManagerTeams'), 'manager multiple-team assignment');
check('USER_MANAGER_EMAIL_AUTOMATION', userUi.includes('managerEmailForTeam') && userUi.includes('Automatically controlled by the active manager team assignment'), 'team manager email applied to user saves');
check('USER_SCOPED_STYLES', userCss.includes('.user-admin-v2-tabs') && userCss.includes('.user-admin-v2-team-grid') && userCss.includes('.user-admin-v2-user-list'), 'Module 009 scoped layout');

check(
  'THEME_STRAY_TEXT_NORMALIZATION',
  themeJs.includes('Node.TEXT_NODE')
    && themeJs.includes("String(node.textContent || '')")
    && themeJs.includes("replace(/\\u00a0/g, ' ')")
    && themeJs.includes('STRAY_THEME_TEXT.test(value)')
    && themeJs.includes("node.textContent = ''")
    && !themeJs.includes('node.remove()')
    && !themeJs.includes('removeChild('),
  'literal newline text is neutralized without removing React-owned nodes'
);
check(
  'THEME_NO_DOCUMENT_OBSERVER_OR_RELOAD',
  !themeJs.includes('MutationObserver')
    && !themeJs.includes('window.location.reload')
    && themeJs.includes("document.addEventListener('click', handleThemeClick, true)")
    && themeJs.includes("window.dispatchEvent(new CustomEvent('projectpulse:theme-changed'"),
  'theme changes use one event boundary without route-wide observation or page reload'
);
check('THEME_GLOBAL_BOOTSTRAP', userUi.includes("import './admin-experience-theme.js';") && userUi.includes("import './admin-experience-theme.css';") && main.includes("import './admin-experience-theme.js';") && main.includes("import './admin-experience-theme.css';") && themeJs.includes("document.createElement('button')"), 'theme bridge retains Module 009 compatibility and creates the global control from the application bootstrap');
check(
  'THEME_ICON_ONLY_DOCK',
  themeCss.includes("[data-projectpulse-theme-control='true']")
    && /left:\s*0\s*!important/.test(themeCss)
    && /width:\s*44px\s*!important/.test(themeCss)
    && /border-radius:\s*0 14px 14px 0\s*!important/.test(themeCss)
    && /::after\s*\{[\s\S]*display:\s*none\s*!important/.test(themeCss)
    && !themeCss.includes("content: 'Dark mode'")
    && !themeCss.includes("content: 'Light mode'"),
  'the accessible theme control is a compact icon-only button docked to the bottom-left edge'
);
check(
  'THEME_LIGHT_DARK_ICONS',
  themeCss.includes("content: '☾'") && themeCss.includes("content: '☀'"),
  'moon and sun states remain visible without text labels'
);

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
  const metadataProjectionStart = auditBackend.indexOf('private static readonly IReadOnlyDictionary<string, string[]> MetadataOnlySourceColumns');
  const metadataProjectionEnd = auditBackend.indexOf('public static WebApplication MapAdminAuditHistoryEndpoints', metadataProjectionStart);
  const metadataProjection = metadataProjectionStart >= 0 && metadataProjectionEnd > metadataProjectionStart
    ? auditBackend.slice(metadataProjectionStart, metadataProjectionEnd)
    : '';
  check(
    'BACKEND_PRIVATE_AI_METADATA_ONLY',
    metadataProjection.includes('["pulse_ai_answer_runs"]')
      && metadataProjection.includes('["pulse_ai_system_inquiry_runs"]')
      && metadataProjection.includes('["pulse_ai_system_tool_events"]')
      && metadataProjection.includes('["pulse_ai_retrieval_events"]')
      && metadataProjection.includes('["pulse_ai_document_processing_events"]')
      && !metadataProjection.includes('"question_text"')
      && !metadataProjection.includes('"answer_json"')
      && !metadataProjection.includes('"evidence_json"')
      && auditBackend.includes('MetadataOnlySourceColumns.TryGetValue(tableName, out var metadataColumns)')
      && auditBackend.includes('.Where(columns.ContainsKey)')
      && auditBackend.includes('SELECT {projection}'),
    'private Celar AI sources are projected to operational metadata before JSON serialization'
  );
  check(
    'BACKEND_PRIVATE_AI_PAYLOAD_DENYLIST',
    ['question_text', 'answer_json', 'request_filters_json', 'message_text', 'structured_response_json', 'corrected_answer_json']
      .every((field) => auditBackend.includes(`"${field}"`)),
    'known private AI payload fields remain explicitly redacted if another source exposes them'
  );
  const notificationDispatchProjection = metadataProjection.match(/\["project_notification_dispatches"\]\s*=\s*\[([\s\S]*?)\]\s*,/)?.[1] || '';
  const notificationAttemptProjection = metadataProjection.match(/\["project_notification_delivery_attempts"\]\s*=\s*\[([\s\S]*?)\]\s*(?:,|\n\s*\})/)?.[1] || '';
  check(
    'BACKEND_NOTIFICATION_METADATA_ONLY',
    ['project_notification_dispatch_id', 'project_id', 'notification_type', 'delivery_status', 'last_error_code', 'created_at']
      .every((field) => notificationDispatchProjection.includes(`"${field}"`))
      && ['subject', 'text_body', 'html_body', 'metadata_json', 'provider_message_id', 'last_error_message']
        .every((field) => !notificationDispatchProjection.includes(`"${field}"`))
      && ['project_notification_delivery_attempt_id', 'project_notification_dispatch_id', 'attempt_status', 'diagnostic_code', 'attempted_at']
        .every((field) => notificationAttemptProjection.includes(`"${field}"`))
      && ['provider_message_id', 'diagnostic_message']
        .every((field) => !notificationAttemptProjection.includes(`"${field}"`)),
    'notification audit sources expose operational metadata without bodies, document links, provider IDs, or diagnostic payloads'
  );
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
const failedChecks = checks.filter((item) => !item.condition).map((item) => item.name);
if (failedChecks.length > 0) {
  console.error(`MODULES_008_009_FAILED_CHECKS=${failedChecks.join(',')}`);
  console.error('MODULES_008_009_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULES_008_009_CONTRACT=PASSED');

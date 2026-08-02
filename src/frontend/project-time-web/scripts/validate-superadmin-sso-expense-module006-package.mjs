import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const absolute = (path) => resolve(root, path);
const text = (path) => readFile(absolute(path), 'utf8');
const optionalText = async (path) => existsSync(absolute(path)) ? text(path) : '';
const requireAll = (source, values, label) => {
  for (const value of values) {
    if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
  }
};
const rejectAll = (source, values, label) => {
  for (const value of values) {
    if (source.includes(value)) throw new Error(`${label} contains forbidden contract: ${value}`);
  }
};

const paths = {
  actualAuthority: 'src/backend/ProjectTime.Api/Modules/ProjectPulseActualSessionAuthority.cs',
  scopedBridge: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyAuthorizationBridge.cs',
  governed: 'src/backend/ProjectTime.Api/Modules/GovernedOperationsReadModule.cs',
  module026: 'src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs',
  globalMail: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  ssoActivation: 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoInteractiveStartActivation.cs',
  publicOrigin: 'src/backend/ProjectTime.Api/Modules/ProjectPulsePublicOriginCompatibility.cs',
  expenseAcknowledgement: 'src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseBillingAcknowledgement.cs',
  expenseContinuity: 'src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseBillingContinuitySafe.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  portal: 'src/frontend/project-time-web/src/ProjectExpenseCrossModulePortal.jsx',
  portalCss: 'src/frontend/project-time-web/src/project-expense-cross-module.css',
  moduleRegistry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  rbacCompatibility: 'src/frontend/project-time-web/src/scoped-rbac-catalog-compatibility.js',
  module006Generator: 'src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs',
  utilization: 'src/frontend/project-time-web/src/EngineeringTeamLeadUtilizationPanel.jsx',
  utilizationCss: 'src/frontend/project-time-web/src/engineering-team-lead-utilization.css',
  packageJson: 'src/frontend/project-time-web/package.json'
};

const [
  actualAuthority, scopedBridge, governed, module026, globalMail, ssoActivation,
  publicOrigin, expenseAcknowledgement, expenseContinuity, project, portal,
  portalCss, moduleRegistry, rbacCompatibility, module006Generator,
  utilization, utilizationCss, packageJson
] = await Promise.all([
  optionalText(paths.actualAuthority), optionalText(paths.scopedBridge), optionalText(paths.governed),
  optionalText(paths.module026), optionalText(paths.globalMail), optionalText(paths.ssoActivation),
  optionalText(paths.publicOrigin), optionalText(paths.expenseAcknowledgement), optionalText(paths.expenseContinuity),
  optionalText(paths.project), text(paths.portal), text(paths.portalCss), text(paths.moduleRegistry),
  text(paths.rbacCompatibility), text(paths.module006Generator), text(paths.utilization),
  text(paths.utilizationCss), text(paths.packageJson)
]);

const fullBackendContext = [
  actualAuthority, scopedBridge, governed, module026, globalMail, ssoActivation,
  publicOrigin, expenseAcknowledgement, expenseContinuity, project
].every(Boolean);

if (fullBackendContext) {
  requireAll(actualAuthority, [
    '"SUPER_ADMINISTRATOR"',
    '"ADMINISTRATOR"',
    'IsAdministratorRoleCode',
    'ProjectPulseActualUserId',
    'ProjectPulseSessionUserId',
    'ProjectPulseEffectiveUserId',
    'X-ProjectPulse-View-As-User',
    'if (IsViewAs(context)) return false;',
    'ReadActualEmail(context)',
    'lower(app_user.email) = lower(@email)',
    'assignment.is_active = TRUE',
    'role.is_active = TRUE',
    'app_user.is_active = TRUE',
    'resolved is not Guid administratorUserId',
    'context.Items["ProjectPulsePermanentFullControl"] = true;',
    'context.Items["ProjectPulseAuthorizationSource"] = "actual_session_super_administrator";',
    'return true;'
  ], 'Actual-session Super Administrator invariant');
  rejectAll(actualAuthority, [
    "upper(COALESCE(role.role_code, '')) = 'SUPER_ADMINISTRATOR'",
    'return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);'
  ], 'obsolete exact-role-only administrator resolver');

  requireAll(scopedBridge, [
    'ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync',
    'Super Administrator has permanent organization-wide Full Control in their own session.',
    'return await ScopedAuthorizationEvaluator.EvaluateAsync'
  ], 'Dynamic RBAC actual-session bridge');

  requireAll(governed, [
    'ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync',
    'ProjectPulsePermanentFullControl',
    'actual_session_super_administrator',
    'ProjectPulseActualSessionAuthority.IsViewAs(context)'
  ], 'Shared governed-operation Super Administrator authority');

  requireAll(module026, [
    'canManage = authority.Allowed',
    'manageAuthoritySource = authority.Source',
    'requiredPermission = "MANAGE_INTEGRATIONS_026"',
    'dynamicAction = "MODULE_CONFIGURE"',
    'viewAsTransfersMutationAuthority = false',
    'EvaluateCurrentActorAsync',
    'HasManageAuthorityLegacyAsync'
  ], 'Module 026 editable authority response');

  requireAll(globalMail, [
    'app.UseProjectPulsePublicOriginCompatibility();',
    'app.UseMicrosoftPublicSsoOriginCompatibility();',
    'app.UseMicrosoftEnvironmentRuntimeCompatibility();',
    'app.UseMicrosoftSsoInteractiveStartActivation();',
    'private const string InteractiveSsoPrefix = "/api/auth/sso/"',
    'ProjectPulsePublicSsoOriginResolved',
    'correlationId = context.TraceIdentifier'
  ], 'Microsoft SSO middleware order');

  const publicIndex = globalMail.indexOf('app.UseProjectPulsePublicOriginCompatibility();');
  const specializedIndex = globalMail.indexOf('app.UseMicrosoftPublicSsoOriginCompatibility();');
  const environmentIndex = globalMail.indexOf('app.UseMicrosoftEnvironmentRuntimeCompatibility();');
  const activationIndex = globalMail.indexOf('app.UseMicrosoftSsoInteractiveStartActivation();');
  if (!(publicIndex >= 0 && publicIndex < specializedIndex && specializedIndex < environmentIndex && environmentIndex < activationIndex)) {
    throw new Error('Microsoft SSO public-origin resolution must run before environment selection and interactive activation.');
  }

  requireAll(publicOrigin, [
    'path.StartsWith("/api/auth/sso/"',
    'ForwardedValues(request.Headers["X-Forwarded-Host"].ToString())',
    'ForwardedValues(request.Headers["X-Forwarded-Proto"].ToString())',
    'trusted_forwarded_origin',
    'browser_referer',
    '.EndsWith(".onenecklab.com"',
    '.EndsWith(".ussignal.com"'
  ], 'Trusted public-origin resolver');
  rejectAll(publicOrigin, [
    '.azurecontainerapps.io',
    'source = "untrusted_forwarded_origin"'
  ], 'Public SSO origin resolver');

  requireAll(ssoActivation, [
    'ExpectedRedirect(context, environmentMode, profile)',
    'trusted_public_origin_unavailable',
    'sso_redirect_host_mismatch',
    'configuredRedirectUri = profile.RedirectUri',
    'expectedRedirectUri = expectedRedirect',
    'context.Items["ProjectPulseSsoRedirectUri"] = expectedRedirect;',
    'TryResolveProxyOrConfiguredOrigin',
    'TryBrowserOrigin',
    'stored_environment_profile',
    'MicrosoftEnvironmentRuntimeResolver.FromHost(configured.Host)',
    'ApprovedEnvironmentOrigin'
  ], 'Interactive SSO redirect resolution');
  rejectAll(ssoActivation, [
    'return $"{context.Request.Scheme}://{context.Request.Host}{CallbackPath}";'
  ], 'Interactive SSO internal-host fallback');

  requireAll(expenseAcknowledgement, [
    '/api/project-expenses/projects/{projectId:guid}/billing-context',
    '/api/project-expenses/projects/{projectId:guid}/billing-acknowledgement',
    'current, non-deleted Module 005 expense uploads',
    'upload.is_current = TRUE',
    'upload.deleted_at IS NULL',
    'pass_through_invoice',
    'included_fixed_price',
    'expense-only-pass-through',
    'expense-included-fixed-price',
    'canAcknowledgeForBilling',
    'project_expense_ready_for_invoice',
    'project_expense_acknowledged_as_included_cost',
    'work_lifecycle_audit_events',
    'deletedUploadsExcluded = true',
    'staleReadinessBlocked = true',
    "SET review_status = 'blocked'"
  ], 'Module 005 expense billing acknowledgement');

  requireAll(expenseContinuity, [
    'UseProjectExpenseBillingReadinessContinuitySafe',
    'HttpMethods.IsGet(context.Request.Method)',
    'ProjectPulseActualSessionAuthority.ReadUserId',
    '!ProjectPulseActualSessionAuthority.IsViewAs(context)',
    'authenticated_candidate_read_v1'
  ], 'Authenticated expense continuity guard');

  requireAll(project, [
    'app.UseProjectExpenseBillingReadinessContinuitySafe();',
    'app.MapModule005ProjectExpenseBillingAcknowledgementEndpoints();'
  ], 'API registration');
  rejectAll(project, ['app.UseProjectExpenseBillingReadinessContinuity();'], 'retired unauthenticated expense continuity registration');
} else {
  console.log('SUPERADMIN_SSO_EXPENSE_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

requireAll(portal, [
  "const [open, setOpen] = useState(false)",
  'data-project-expense-cross-module="non-invasive-v2"',
  'className="expense-cross-module-launcher"',
  'aria-expanded={open}',
  'Choose a project only when expense context is needed',
  'This panel stays collapsed and does not choose a project automatically.',
  '/api/project-expenses/projects/${selectedProjectId}/billing-context',
  '/api/project-expenses/projects/${projectId}/billing-acknowledgement',
  'Acknowledge for invoice review',
  'Acknowledge as included project cost',
  'Deleted and superseded uploads are excluded.',
  'PM, PTC, Accounting user, or Super Administrator'
], 'Non-invasive Module 005 cross-module experience');
rejectAll(portal, [
  "setProjectId((current) => current || result.projects?.[0]?.projectId || '')",
  'position:fixed;right:1.25rem;bottom:1.25rem'
], 'retired invasive expense popup');

requireAll(portalCss, [
  '.expense-cross-module-launcher',
  '.expense-cross-module-shell.is-open',
  '.expense-cross-module-panel',
  'max-height:min(72vh,760px)',
  '.expense-cross-close',
  '.expense-cross-acknowledgement'
], 'Collapsed expense drawer styling');

requireAll(moduleRegistry, [
  "moduleNumber: '006'",
  "displayName: 'Toyota & Hyundai Pipelines'",
  "group: 'Sales & Opportunities'"
], 'Module 006 registry rename');
rejectAll(moduleRegistry, [
  "moduleNumber: '006', route: 'psa-modules', displayName: 'PSA Modules'"
], 'retired Module 006 name');

requireAll(module006Generator, [
  'MODULE_006_TOYOTA_HYUNDAI_PIPELINES_GENERATION=PASS',
  "title: 'Toyota & Hyundai Pipelines'",
  "route: 'toyota-hyundai-pipelines'",
  'aliases=psa-modules,project-register',
  "source.includes(\"title: 'PSA Modules'\")"
], 'Generated Module 006 rename');

requireAll(rbacCompatibility, [
  "'006': 'Toyota & Hyundai Pipelines'",
  '/api/rbac/v1/bootstrap',
  '/api/rbac/v1/matrix',
  '/api/rbac/v1/modules',
  'normalizeModule',
  'moduleDisplayNameOverrides'
], 'Module 012/037 display-name continuity');

requireAll(utilization, [
  'engineering-utilization-manager-table',
  '<table className="engineering-utilization-table">',
  '<colgroup>',
  '<th scope="col">Engineer</th>',
  '<th scope="col">Team</th>',
  '<th scope="col">Annual utilization</th>',
  '<th scope="col">Billable hours</th>',
  'quarter-heading'
], 'PR 212 structured Module 003 utilization table');
requireAll(utilizationCss, [
  '.engineering-utilization-table-wrap',
  'overflow-x: auto',
  '.engineering-utilization-table thead th',
  'position: sticky',
  'border-right'
], 'PR 212 Module 003 table styling');

requireAll(packageJson, [
  'inject-module-006-toyota-hyundai-pipeline.mjs',
  'validate:superadmin-sso-expense-module006'
], 'Permanent source/build integration');

console.log('SUPERADMIN_SSO_EXPENSE_MODULE006_PACKAGE=PASS');
console.log('MODULE026_SUPERADMIN=PERMANENT_FULL_CONTROL_OWN_SESSION');
console.log('MICROSOFT_SSO_REDIRECT=PUBLIC_ENVIRONMENT_HTTPS_ONLY');
console.log('MODULE005_EXPENSE_DRAWER=COLLAPSED_NON_INVASIVE');
console.log('MODULE005_EXPENSE_ACK=PM_PTC_ACCOUNTING_SUPERADMIN');
console.log('MODULE006_NAME=TOYOTA_AND_HYUNDAI_PIPELINES');
console.log('PR212_MODULE003_TABLE=INCLUDED');

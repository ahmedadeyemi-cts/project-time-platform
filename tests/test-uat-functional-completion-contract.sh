#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CATALOG="$ROOT/src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs"
ENGINE="$ROOT/src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs"
SOURCES="$ROOT/src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs"
ANALYTICS="$ROOT/src/frontend/project-time-web/src/AnalyticsCenter.jsx"
ANALYTICS_CSS="$ROOT/src/frontend/project-time-web/src/analytics-center.css"
AUTHORITY="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectPulseActualSessionAuthority.cs"
CRM="$ROOT/src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs"
ADMIN="$ROOT/src/backend/ProjectTime.Api/Modules/AdminExperienceCommon.cs"
CELAR="$ROOT/src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx"
CELAR_MOUNT="$ROOT/src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx"
MAIL_TEST="$ROOT/src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs"
MAIL_DELIVERY="$ROOT/src/backend/ProjectTime.Api/Modules/Module065ProjectNotificationDelivery.cs"
MAIL_UI="$ROOT/src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx"
MIGRATION="$ROOT/database/migrations/066_immutable_project_numbers.sql"
ROLLBACK="$ROOT/database/rollback/066_immutable_project_numbers_rollback.sql"
CLOSEOUT="$ROOT/src/frontend/project-time-web/src/ProjectCloseoutCenter.jsx"
ROUTE_AUTHORITY="$ROOT/src/frontend/project-time-web/scripts/inject-live-ui-route-authority.mjs"
APP_SOURCE="$ROOT/src/frontend/project-time-web/src/App.jsx"
CELAR_WORKFLOW="$ROOT/.github/workflows/celar-ai-enterprise-platform-ci.yml"
ANALYTICS_WORKFLOW="$ROOT/.github/workflows/module030-analytics-center-ci.yml"
ANALYTICS_ENTERPRISE_WORKFLOW="$ROOT/.github/workflows/module030-analytics-enterprise-experience-ci.yml"
MICROSOFT_WORKFLOW="$ROOT/.github/workflows/modules010065-microsoft-runtime-ci.yml"
ROLE_WORKFLOW="$ROOT/.github/workflows/role-access-repair-ci.yml"

for file in "$CATALOG" "$ENGINE" "$SOURCES" "$ANALYTICS" "$ANALYTICS_CSS" "$AUTHORITY" "$CRM" "$ADMIN" "$CELAR" "$CELAR_MOUNT" "$MAIL_TEST" "$MAIL_DELIVERY" "$MAIL_UI" "$MIGRATION" "$ROLLBACK" "$CLOSEOUT" "$ROUTE_AUTHORITY" "$APP_SOURCE" "$CELAR_WORKFLOW" "$ANALYTICS_WORKFLOW" "$ANALYTICS_ENTERPRISE_WORKFLOW" "$MICROSOFT_WORKFLOW" "$ROLE_WORKFLOW"; do
  test -s "$file"
done

require() {
  local file="$1" marker="$2" label="$3"
  grep -Fq "$marker" "$file" || {
    echo "CONTRACT_FAILED $label missing marker: $marker" >&2
    exit 1
  }
  echo "CONTRACT_PASSED $label"
}

require_not_contains() {
  local file="$1" marker="$2" label="$3"
  if grep -Fq "$marker" "$file"; then
    echo "CONTRACT_FAILED $label unexpected marker: $marker" >&2
    exit 1
  fi
  echo "CONTRACT_PASSED $label"
}

# Familiar report inventory and requested new report behavior.
for report in \
  'Executive Summary Dashboard' \
  'Accounting Invoice Detail Report' \
  'T&M Sales Report' \
  'Project Status Report — Billed Cost and Remaining Balance' \
  'Certify Expense + Accounting Invoice Breakdown' \
  'Engineer Project Over / Under Budget Report' \
  'Utilization Over / Under Report by Engineer' \
  'Engineer Vacation / PTO Used Report' \
  'Billable vs Non-Billable Report' \
  'Unbilled Time / Invoice Readiness Report' \
  'Approval Bottleneck Report' \
  'Missing Time / Late Timesheet Report' \
  'Project Margin Report' \
  'Rate / Amount Exception Report' \
  'Customer Profitability Report' \
  'Project Closeout Readiness Report' \
  'Sales-to-Delivery Handoff Quality Report' \
  'Customer Billing Summary Report' \
  'Project Report' \
  'PM Project Workload Report' \
  'Engineer Utilization Detail Report' \
  'Selected Engineers Report' \
  'Team Report' \
  'Organization Report' \
  'Workflow / Approval / Audit Report' \
  'System Stability Report' \
  'API Status Report' \
  'External Connection Report' \
  'Authentication / Security Report' \
  'AI / SOW Scope Report' \
  'Notification Report' \
  'UAT Evidence Report' \
  'Report Library'; do
  require "$CATALOG" "$report" "report_${report//[^A-Za-z0-9]/_}"
done

require "$ENGINE" '"engineer_project_over_under_budget" => EngineerProjectOverUnder' project_over_under_execution
require "$ENGINE" '"utilization_over_under" => UtilizationOverUnder' current_projected_utilization_execution
require "$ENGINE" '"engineer_vacation_pto_used" => VacationPtoUsed' vacation_pto_execution
require "$SOURCES" '["timesheet_day_statuses"]' timesheet_status_source
require "$SOURCES" '["billing_invoice_lines"]' invoice_line_source
require "$ANALYTICS" "['overview', '⌂', 'Home']" analytics_home_label
require "$ANALYTICS" "['reports', '▤', 'Reports']" analytics_reports_label
require "$ANALYTICS" 'aria-label={label}' analytics_navigation_accessible_label
require "$ANALYTICS_CSS" 'grid-template-columns: 252px minmax(0, 1fr);' analytics_desktop_labeled_sidebar
require "$ANALYTICS_CSS" '.analytics-sidebar-return strong { display: block; }' analytics_tablet_labels_visible
require "$ENGINE" 'Report completed with limited source coverage.' limited_source_notice_not_error

# Actual-session Super Administrator authority precedes dynamic policy and never
# transfers to View-As.
require "$AUTHORITY" 'GLOBAL_ADMINISTRATOR' global_admin_alias
require "$AUTHORITY" 'regexp_replace(' normalized_role_lookup
require "$AUTHORITY" 'if (IsViewAs(context)) return false;' view_as_bypass_denied
require "$CRM" 'Permanent actual-session Super Administrator authority is evaluated' crm_permanent_authority_first
require "$CRM" '"actual_session_super_administrator"' crm_authority_evidence
require "$ADMIN" 'ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync' admin_common_permanent_authority

# Module 011 owns one selected production workspace at a time, with architecture first.
require "$CELAR" "['overview', 'Overview', 'Architecture, readiness, trust, and solution composer']" celar_overview_default
require "$CELAR" "useState('overview')" celar_architecture_is_initial_workspace
require "$CELAR" "['ask', 'Ask Celar AI'" celar_shared_ask_tab
require "$CELAR" 'const tabContent = useMemo(() =>' celar_single_active_component
require "$CELAR" '{tabContent}' celar_selected_surface_only
require "$CELAR" "if (activeTab === 'overview') return <CelarAiEnterprisePlatform />;" celar_enterprise_workspace_registered
require "$CELAR_MOUNT" 'return <CelarAiProductionPlatform />;' celar_authoritative_production_mount
require "$CELAR_MOUNT" 'return <PulseAiCenter />;' celar_lifecycle_validator_compatibility

# Module 065 readiness and real governed Test delivery remain distinct.
require "$MAIL_TEST" '/api/microsoft-integration/mail-runtime/test-delivery' governed_delivery_endpoint
require "$MAIL_TEST" 'SEND MODULE 065 TEST' exact_delivery_confirmation
require "$MAIL_TEST" 'governed_mail_test_origin_rejected' governed_delivery_same_origin
require "$MAIL_TEST" 'PROJECTPULSE_MAIL_TEST_RECIPIENT_ALLOWLIST' server_side_recipient_allowlist
require "$MAIL_TEST" 'recipientEmail.Equals(ownEmail' signed_in_user_recipient
require "$MAIL_TEST" 'selfRecipient ? access.Context.UserId : null' allowlisted_recipient_not_misattributed
require "$MAIL_TEST" 'generalTestBoundaryChanged = false' test_boundary_unchanged
require "$MAIL_DELIVERY" 'DeliverGovernedTestAsync' governed_test_delivery_adapter
require "$MAIL_DELIVERY" 'delivery continues to honor test_only as outbox-only.' general_delivery_boundary_preserved
require "$MAIL_UI" 'Send Module 065 Test email' governed_test_ui
require "$MAIL_UI" 'A real email will be sent.' explicit_delivery_warning

# Immutable business project numbers and Module 040 guided closeout.
require "$MIGRATION" "'066_immutable_project_numbers'" migration_066_registered
require "$MIGRATION" "WHEN 'SERVICE_REQUEST' THEN 'SR'" service_request_prefix
require "$MIGRATION" "WHEN 'IQS' THEN 'IQS'" iqs_prefix
require "$MIGRATION" "WHEN 'INTERNAL' THEN 'INT'" internal_prefix
require "$MIGRATION" "WHEN 'PRE_SALES' THEN 'PRES'" presales_prefix
require "$MIGRATION" "ELSE 'PRO'" project_prefix
require "$MIGRATION" 'project_code_aliases' legacy_alias_store
require "$MIGRATION" 'projectpulse_resolve_project_id' alias_resolution_function
require "$MIGRATION" 'Issued ProjectPulse project numbers are immutable.' immutable_number_trigger
require "$MIGRATION" 'trg_projects_issued_project_number_guard_insert' direct_import_number_guard
require "$ROLLBACK" 'projectpulse055d4d_commit_intake_package_legacy_066' final_save_rollback
require "$CLOSEOUT" 'Request project closeout' guided_pm_closeout
require "$CLOSEOUT" 'Complete project closeout' guided_admin_closeout
require "$ROUTE_AUTHORITY" "source = source.replace(legacyCloseoutRecovery, '');" guided_module040_legacy_mount_removed
require "$ROUTE_AUTHORITY" 'Legacy Module 040 recovery surface remains mounted beside the guided closeout.' guided_module040_postcondition
require "$APP_SOURCE" '<ProjectCloseoutCenter />' guided_module040_canonical_mount
require_not_contains "$APP_SOURCE" '<FinancialOperationsRecoveryWorkspace moduleCode="040"' guided_module040_canonical_source_has_no_legacy_mount

# Existing path-triggered validators recognize the reusable UAT completion
# contract marker without hard-coding a one-off branch name.
for workflow in "$CELAR_WORKFLOW" "$ANALYTICS_WORKFLOW" "$ANALYTICS_ENTERPRISE_WORKFLOW" "$MICROSOFT_WORKFLOW" "$ROLE_WORKFLOW"; do
  require "$workflow" 'tests/test-uat-functional-completion-contract.sh' "uat_validator_$(basename "$workflow" .yml)"
  require_not_contains "$workflow" 'fix/uat-functional-completion-*' "uat_validator_has_no_one_off_branch_guard_$(basename "$workflow" .yml)"
done
require "$CELAR_WORKFLOW" '066_immutable_project_numbers' celar_validator_allows_only_migration_066
require "$MICROSOFT_WORKFLOW" 'DISALLOWED_DATABASE' microsoft_validator_rejects_unrelated_database_changes
require "$ANALYTICS_WORKFLOW" 'ANALYTICS_CENTER_UAT_OWNED_SUBSET=PASSED' analytics_validator_owned_subset
require "$ANALYTICS_ENTERPRISE_WORKFLOW" 'ANALYTICS_ENTERPRISE_UAT_OWNED_SUBSET=PASSED' analytics_enterprise_validator_owned_subset

echo 'UAT_FUNCTIONAL_COMPLETION_CONTRACT=PASS'

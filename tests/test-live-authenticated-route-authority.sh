#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/src/frontend/project-time-web/src/App.jsx"
ANALYTICS="$ROOT/src/frontend/project-time-web/src/AnalyticsCenter.jsx"
ANALYTICS_CSS="$ROOT/src/frontend/project-time-web/src/analytics-center.css"
MODULES="$ROOT/src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx"
MODULE_CSS="$ROOT/src/frontend/project-time-web/src/module-availability.css"
CRM_UI="$ROOT/src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx"
AUTHORITY="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectPulseActualSessionAuthority.cs"
CRM_SOURCE="$ROOT/src/backend/ProjectTime.Api/Modules/CrmErpIntegrationModule.cs"
CRM_RUNTIME="$ROOT/src/backend/ProjectTime.Api/Modules/CrmErpOAuthPersistence.cs"
NATIVE="$ROOT/src/backend/ProjectTime.Api/Modules/Module064074NativeAdministration.cs"

require_text() {
  local file="$1" needle="$2" label="$3"
  grep -Fq "$needle" "$file" || {
    echo "ASSERTION_FAILED $label missing=$needle file=$file" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label"
}

reject_text() {
  local file="$1" needle="$2" label="$3"
  if grep -Fq "$needle" "$file"; then
    echo "ASSERTION_FAILED $label forbidden=$needle file=$file" >&2
    exit 1
  fi
  echo "ASSERTION_PASSED $label"
}

require_text "$APP" 'LIVE_AUTHENTICATED_ROUTE_AUTHORITY_START' route_authority_marker
require_text "$APP" "window.localStorage.getItem('projectPulseViewAsUser')" view_as_fail_closed
require_text "$APP" 'actualSessionHasPermanentFullControl' permanent_frontend_authority
require_text "$APP" "'SUPER_ADMINISTRATOR'" canonical_super_admin_role
require_text "$APP" "activeRoute === 'work-task-builder' && canSeeAny" celar_route_uses_shared_authority
require_text "$APP" "activeRoute === 'entra-secret-administration' && canSeeAny" module065_route_uses_shared_authority
require_text "$APP" 'return actualSessionHasPermanentFullControl' route_permission_bypass

require_text "$ANALYTICS" "const [section, setSection] = useState('reports');" analytics_opens_report_library
require_text "$ANALYTICS_CSS" '.analytics-enterprise-shell.sidebar-collapsed .analytics-sidebar nav button strong,' analytics_collapse_scoped
if grep -Eq '^\.sidebar-collapsed[[:space:]]+\.analytics-' "$ANALYTICS_CSS"; then
  echo 'ASSERTION_FAILED analytics_outer_sidebar_collision_remains' >&2
  exit 1
fi
echo 'ASSERTION_PASSED analytics_outer_sidebar_collision_removed'

require_text "$MODULES" 'className="modules-directory-open-link"' module_open_link_class
require_text "$MODULES" 'href={module.href || `#${module.route}`}' module_open_link_fallback
require_text "$MODULE_CSS" 'display: inline-flex !important;' module_open_link_forced_visible

require_text "$CRM_UI" "jsonRequest('/api/module-availability/overrides')" crm_uses_authoritative_module_access
require_text "$CRM_UI" 'actual_session_super_administrator' crm_frontend_authority_source
require_text "$CRM_UI" 'viewAsTransfersMutationAuthority: false' crm_frontend_view_as_boundary

require_text "$AUTHORITY" 'HasPermanentAdministratorAuthority(' central_authority_helper
require_text "$AUTHORITY" 'if (IsViewAs(context)) return false;' central_view_as_boundary
require_text "$AUTHORITY" 'ProjectPulsePermanentFullControl' request_local_permanent_authority
require_text "$CRM_SOURCE" 'manageAuthoritySource = manageAuthority.Source' crm_provider_payload_authority_source
require_text "$CRM_SOURCE" 'requiredPermission = "MANAGE_INTEGRATIONS_026"' crm_provider_payload_required_permission
require_text "$CRM_RUNTIME" '"actual_session_super_administrator"' crm_runtime_permanent_source
require_text "$NATIVE" 'ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, roles)' native_module065_permanent_authority
require_text "$NATIVE" 'if (requireManage && isViewAs)' native_module065_view_as_boundary

MICROSOFT_FILES=(
  MicrosoftIntegrationModule.cs
  MicrosoftIntegrationSecurityCompatibility.cs
  MicrosoftMailRuntimeConfigurationModule.cs
  MicrosoftServicesRuntimeCompatibility.cs
  MicrosoftSsoConnectionProfilesModule.cs
  MicrosoftSsoRuntimeCompatibility.cs
  MicrosoftDirectorySyncModule.cs
  EntraSecretAdministrationModule.cs
)
for name in "${MICROSOFT_FILES[@]}"; do
  file="$ROOT/src/backend/ProjectTime.Api/Modules/$name"
  require_text "$file" 'ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, roles)' "canonical_authority_${name%.cs}"
  reject_text "$file" 'roles.Contains("SUPER_ADMINISTRATOR")' "no_exact_super_admin_${name%.cs}"
done

require_text "$ROOT/src/backend/ProjectTime.Api/Modules/MicrosoftDirectorySyncModule.cs" "'GLOBAL_ADMINISTRATOR'" directory_sync_scheduler_role_compatibility

python3 - "$ROOT" <<'PY'
from pathlib import Path
import sys
root = Path(sys.argv[1])
files = [
    root / 'src/frontend/project-time-web/src/App.jsx',
    root / 'src/frontend/project-time-web/src/AnalyticsCenter.jsx',
    root / 'src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx',
    root / 'src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx',
]
for file in files:
    text = file.read_text()
    for left, right, label in [('(', ')', 'parentheses'), ('[', ']', 'brackets'), ('{', '}', 'braces')]:
        if text.count(left) != text.count(right):
            raise SystemExit(f'ASSERTION_FAILED {file} unbalanced_{label} left={text.count(left)} right={text.count(right)}')
print('ASSERTION_PASSED frontend_structural_delimiters')
PY

echo 'LIVE_AUTHENTICATED_ROUTE_AUTHORITY_HOTFIX=PASS celar=visible module065=open crm=permanent_super_admin analytics=reports_first view_as=read_only'

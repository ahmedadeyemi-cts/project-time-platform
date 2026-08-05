#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TIMESHEET_CSS="$ROOT/src/frontend/project-time-web/src/timesheet.css"
PRODUCTION="$ROOT/src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx"
ENTERPRISE="$ROOT/src/frontend/project-time-web/src/CelarAiEnterprisePlatform.jsx"
ARCHITECTURE="$ROOT/src/frontend/project-time-web/src/CelarAiArchitectureOverview.jsx"
AVAILABILITY="$ROOT/src/frontend/project-time-web/src/ModuleAvailabilityController.jsx"
BRIDGE="$ROOT/src/frontend/project-time-web/src/module-availability-bridge.js"
DIRECTORY="$ROOT/src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx"
HELP="$ROOT/src/frontend/project-time-web/src/HelpAssistant.jsx"
TOOL_EXECUTOR="$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiSystemToolExecutor.cs"
PRODUCTION_MODULE="$ROOT/src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs"
RAG_CONTRACTS="$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagContracts.cs"
SYSTEM_CONTRACTS="$ROOT/src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceContracts.cs"
PRODUCTION_INJECTOR="$ROOT/src/frontend/project-time-web/scripts/inject-celar-ai-production-platform.mjs"

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

# Module 011: isolate only top-level route siblings; never hide nested Celar sections.
require_text "$TIMESHEET_CSS" '.app-shell.route-work-task-builder > section:not(#work-task-builder) {' module011_direct_child_isolation
reject_text "$TIMESHEET_CSS" '.app-shell.route-work-task-builder section:not(#work-task-builder):not(:has(#work-task-builder)) {' module011_recursive_section_hiding_removed
require_text "$PRODUCTION" '<CelarAiEnterprisePlatform />' module011_overview_mounts_enterprise_platform
require_text "$ENTERPRISE" '<CelarAiArchitectureOverview />' module011_enterprise_mounts_architecture
require_text "$ARCHITECTURE" '<svg' module011_architecture_svg_present
require_text "$ARCHITECTURE" 'Created by Dr. Ahmed Adeyemi' module011_architecture_creator_attribution

# Module 065: React Modules directory owns its card/action subtree.
require_text "$DIRECTORY" 'className="modules-directory-open-link"' module065_open_action_rendered
require_text "$DIRECTORY" 'href={module.href || `#${module.route}`}' module065_open_action_has_route_fallback
require_text "$AVAILABILITY" "closest?.('#modules-directory-portal-host')" availability_skips_react_directory
require_text "$AVAILABILITY" "element.removeAttribute('hidden');" availability_clears_stale_hidden
require_text "$AVAILABILITY" 'delete element.dataset.projectpulsePermissionHidden;' availability_clears_stale_permission_marker
require_text "$BRIDGE" "closest?.('#modules-directory-portal-host')" rbac_bridge_skips_react_directory
require_text "$BRIDGE" "element.removeAttribute('data-module-availability-hidden');" rbac_bridge_clears_stale_availability_marker
require_text "$BRIDGE" 'if (restoreModulesDirectoryAction(element)) return;' rbac_bridge_preserves_react_owned_actions

# Celar AI: safe root-relative APIs execute; external/traversal forms remain rejected.
require_text "$TOOL_EXECUTOR" 'candidate.StartsWith("/", StringComparison.Ordinal)' tool_accepts_root_relative_shape
require_text "$TOOL_EXECUTOR" 'candidate.StartsWith("//", StringComparison.Ordinal)' tool_rejects_protocol_relative
require_text "$TOOL_EXECUTOR" 'candidate.Contains("://", StringComparison.Ordinal)' tool_rejects_absolute_scheme
require_text "$TOOL_EXECUTOR" 'Uri.UnescapeDataString(cleanPath)' tool_checks_encoded_path
require_text "$TOOL_EXECUTOR" 'decodedPath.Contains("..", StringComparison.Ordinal)' tool_rejects_traversal
require_text "$TOOL_EXECUTOR" 'decodedPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)' tool_accepts_governed_api_root
require_text "$TOOL_EXECUTOR" 'decodedPath.Equals("/health", StringComparison.OrdinalIgnoreCase)' tool_accepts_health_only_exception

# API inventory questions receive direct runtime counts and concise default presentation.
require_text "$PRODUCTION_MODULE" 'value.Contains("how many")' api_intent_handles_how_many
require_text "$PRODUCTION_MODULE" 'value.Contains("do i have")' api_intent_handles_do_i_have
require_text "$PRODUCTION_MODULE" 'The running application currently registers' api_answer_is_direct
require_text "$PRODUCTION_MODULE" 'Source: live ASP.NET EndpointDataSource.' api_answer_names_authoritative_source
require_text "$PRODUCTION_MODULE" 'Open the collapsed API inventory below' api_answer_keeps_details_available
require_text "$RAG_CONTRACTS" 'public static class PulseAiRoleAuthority' ai_role_authority_shared
require_text "$RAG_CONTRACTS" 'GLOBAL_ADMINISTRATOR' ai_role_authority_global_admin_alias
require_text "$RAG_CONTRACTS" 'SYSTEM_ADMINISTRATOR' ai_role_authority_system_admin_alias
require_text "$RAG_CONTRACTS" 'PulseAiRoleAuthority.HasAdministratorRole(RoleCodes)' private_rag_uses_canonical_admin_authority
require_text "$SYSTEM_CONTRACTS" 'PulseAiRoleAuthority.HasAdministratorRole(RoleCodes)' system_intelligence_uses_canonical_admin_authority
require_text "$HELP" 'applyHelpAnswerPreferences(preferenceUrl, clean)' help_uses_saved_or_query_detail_preference
require_text "$HELP" "const detailLevel = result?.detailLevel ?? 'standard';" help_standard_default
reject_text "$HELP" "detailLevel: 'comprehensive'," help_no_forced_comprehensive_request
require_text "$HELP" 'open={troubleshootingProfile || detailedProfile}' help_current_state_intent_aware
require_text "$HELP" 'open={troubleshootingProfile || enhancementProfile || detailedProfile}' help_actions_intent_aware
require_text "$HELP" '<details className="pulse-ai-system-api-inventory">' api_inventory_collapsed_by_default
require_text "$PRODUCTION_INJECTOR" "const path = '/api/celar-ai/v2/chat';" production_chat_remains_v2

# The focused repair is source-only and adds no migration.
if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if git -C "$ROOT" diff --name-only -- database/migrations database/rollback | grep -q .; then
    echo 'ASSERTION_FAILED unexpected_database_migration_change' >&2
    exit 1
  fi
fi

echo 'POST_PR465_LIVE_UAT_REPAIR=PASS module011=architecture_visible module065=open_action_owned_by_react celar=live_api_and_intent_aware'

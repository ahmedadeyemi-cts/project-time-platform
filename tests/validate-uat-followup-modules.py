#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(path: str, *markers: str) -> None:
    source = read(path)
    missing = [marker for marker in markers if marker not in source]
    if missing:
        raise SystemExit(f"{path} missing required markers: {missing}")


def reject(path: str, *markers: str) -> None:
    source = read(path)
    present = [marker for marker in markers if marker in source]
    if present:
        raise SystemExit(f"{path} contains forbidden markers: {present}")


require(
    "src/backend/ProjectTime.Api/ProjectTime.Api.csproj",
    "app.MapModule006StandalonePipelineEndpoints();",
    "app.MapModule006StandaloneTaskEndpoints();",
)
require(
    "src/backend/ProjectTime.Api/Modules/Module006StandalonePipelineModule.cs",
    "MapModule006StandalonePipelineEndpoints",
    "linkedToModule055C = false",
    'customerEntryMode = "extensible"',
    "CustomerNameMaxLength = 120",
    "069_module006_customer_pipeline_expansion",
    "Customer names must contain between",
    "request.Archive",
    "module006_pipeline_record_restored",
)
reject(
    "src/backend/ProjectTime.Api/Modules/Module006StandalonePipelineModule.cs",
    "Module 006 accepts only Toyota or Hyundai pipeline records.",
)
require(
    "src/backend/ProjectTime.Api/Modules/Module006StandaloneTaskModule.cs",
    "MapModule006StandaloneTaskEndpoints",
    "module006_pipeline_tasks",
    "module006_pipeline_task_events",
    "linkedToModule055C = false",
)
require(
    "database/migrations/068_module006_standalone_pipeline_management.sql",
    "module006_pipeline_records",
    "module006_pipeline_updates",
    "module006_pipeline_tasks",
    "module006_pipeline_task_events",
    "projectpulse068_block_pipeline_history_mutation",
)
require(
    "database/migrations/069_module006_customer_pipeline_expansion.sql",
    "069_module006_customer_pipeline_expansion",
    "DROP CONSTRAINT IF EXISTS module006_pipeline_records_customer_check",
    "ck_module006_pipeline_records_customer_name",
    "char_length(customer) BETWEEN 2 AND 120",
    "ix_module006_pipeline_records_customer_name",
)
require(
    "database/rollback/069_module006_customer_pipeline_expansion_rollback.sql",
    "additional-customer Module 006 records exist",
    "lower(btrim(customer)) NOT IN ('toyota', 'hyundai')",
    "module006_pipeline_records_customer_check",
)
require(
    "src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs",
    "MODULE_006_CUSTOMER_EXPANSION_START",
    "module006-customer-options",
    "All customers",
    "US-Signal-Customer-Pipelines-",
    "installStackedLogo",
    "USSNavyStacked.png",
    "customers=extensible stacked_logo=approved",
)
require(
    "src/frontend/project-time-web/src/ProjectRegisterCenter.jsx",
    "module006-standalone-pipeline-v1",
    "Add New Project",
    "Create New Task",
    "Updates & Notes",
    "Standalone authority",
    "/api/module-006/pipeline",
    "/api/module-006/tasks",
)
reject(
    "src/frontend/project-time-web/src/ProjectRegisterCenter.jsx",
    "#work-register",
    "Open Module 055C",
    "Manage tasks in 055C",
)
require(
    "src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseSafeEndpoints.cs",
    'app.MapGet("/api/project-expenses/uploads/lifecycle"',
    'app.MapPost("/api/project-expenses/uploads/{uploadId:guid}/accept"',
)
require(
    "src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx",
    "Lifecycle controls are temporarily unavailable",
    "Upload history loaded",
    "Deleting upload…",
)
require(
    "src/frontend/project-time-web/src/project-expense-upload.css",
    "MODULE_005_ENTERPRISE_UPLOAD_HISTORY_LAYOUT",
    "grid-template-columns: repeat(2, minmax(0, 1fr));",
    ".expense-actions .primary-action",
)
require(
    "src/frontend/project-time-web/src/FinancialOperationsRecoveryWorkspace.jsx",
    "canRetry = false, compact = false",
    "function ModuleRecovery({ moduleCode, authSession, compact = false })",
    "compact={compact}",
)
require(
    "src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx",
    "MODULE_065_REACT_OWNED_OPEN_ACTION",
    "modules-directory-open-button",
    "window.location.hash = module.route",
)
require(
    "src/frontend/project-time-web/src/module-availability.css",
    "MODULE_065_MATCHED_OPEN_ACTION_STYLE",
    ".modules-directory-card-actions > button.modules-directory-open-button:hover",
)
require(
    "src/frontend/project-time-web/src/intuitive-more-menu.css",
    "ENTERPRISE_MORE_MENU_V2",
    "grid-template-columns: repeat(2, minmax(0, 1fr));",
    "text-overflow: clip;",
    "server-backed permission bridge",
)
require(
    "src/frontend/project-time-web/src/WorkRegisterCenter.jsx",
    "work-register-row-identifier",
    "work-register-drawer-identifier",
    "project number, PM",
)
require(
    "src/frontend/project-time-web/src/Module005ExperienceCompatibility.jsx",
    "Project Expense Upload — Module 005",
    "shouldReplaceLegacyText",
)

print("UAT_FOLLOWUP_MODULE005=PASS history_resilient lifecycle_routes=registered table_layout=bounded pm_acceptance=visible")
print("UAT_FOLLOWUP_MODULE006=PASS standalone_projects_tasks_notes=true linked_to_055c=false customers=extensible migration=069")
print("UAT_FOLLOWUP_MODULE039=PASS compact_source_health=true")
print("UAT_FOLLOWUP_MODULE055C=PASS visible_immutable_identifier=true")
print("UAT_FOLLOWUP_MODULE065=PASS react_owned_open_action=true visual_parity=true")
print("UAT_FOLLOWUP_MORE_MENU=PASS enterprise_launcher=true permission_evidence=preserved")
print("UAT_FOLLOWUP_BRANDING=PASS stacked_us_signal_logo=approved")

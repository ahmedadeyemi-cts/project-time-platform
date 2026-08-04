#!/usr/bin/env python3
from pathlib import Path
root = Path(__file__).resolve().parents[1]
checks = {
    "module006 injector": ("src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs", "PR467_MODULE_006_EXCLUSIVE_ROUTE_START"),
    "module005 lifecycle API": ("src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseLifecycle.cs", "expense_upload_approved_locked"),
    "module005 no auto restore": ("src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseCommands.cs", "priorVersionRestored = false"),
    "module005 frontend delete": ("src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx", "Replace / Re-upload"),
    "module039 compact": ("src/frontend/project-time-web/src/FinancialOperationsRecoveryWorkspace.jsx", "PR467_COMPACT_SOURCE_HEALTH"),
    "module039 handoffs": ("src/frontend/project-time-web/src/BillingReadinessCenter.jsx", "PR467_BILLING_CLOSEOUT_HANDOFFS"),
    "work receipt API": ("src/backend/ProjectTime.Api/Modules/Pr467UatRepairModule.cs", "work_creation_receipt_loaded"),
    "work receipt UI": ("src/frontend/project-time-web/src/WorkRegisterCenter.jsx", "work-register-creation-receipt"),
    "migration": ("database/migrations/067_uat_expense_lifecycle_work_identifiers.sql", "project_expense_upload_acceptances"),
    "rollback": ("database/rollback/067_uat_expense_lifecycle_work_identifiers_rollback.sql", "Rollback blocked"),
}
for label, (path, marker) in checks.items():
    text = (root / path).read_text()
    if marker not in text:
        raise SystemExit(f"{label} missing marker {marker}")
compat = (root / "src/frontend/project-time-web/src/Module005ExperienceCompatibility.jsx").read_text()
if "stopImmediatePropagation" in compat or "Re-upload ready" in compat:
    raise SystemExit("legacy Delete-to-Re-upload interception remains")
commands = (root / "src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseCommands.cs").read_text()
if "PRIOR_VERSION_RESTORED" in commands or "Guid? restoredId" in commands or "and the prior version was restored." in commands:
    raise SystemExit("automatic prior-version restoration remains")
print("PR467_FOCUSED_SOURCE_VALIDATION=PASS")

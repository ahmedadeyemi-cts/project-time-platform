#!/usr/bin/env python3
from __future__ import annotations

import subprocess
from pathlib import Path

SOURCE_COMMIT = "5ac67a4470bf0803889bdc9ef19528bebdf1979f"
SOURCE_PATH = ".github/scripts/temporary_apply_flowhive_authoritative_repair.py"
SEED_REF = "origin/fix/shared-project-document-planning-20260819"
SEED_PATHS = (
    ".github/workflows/project-planning-collaboration-ci.yml",
    ".github/workflows/shared-project-document-planning-ci.yml",
    ".github/workflows/temporary-flowhive-source-export.yml",
    "database/migrations/095_project_planning_collaboration_access.sql",
    "database/rollback/095_project_planning_collaboration_access_rollback.sql",
    "docs/architecture/flowhive-project-forge-protected-uat-release.md",
    "docs/architecture/project-planning-collaboration-access.md",
    "scripts/release-test/build-and-run-project-planning-collaboration-migration-job.sh",
    "scripts/release-test/run-flowhive-authority-migration-094-job.sh",
    "scripts/release-test/run-project-planning-collaboration-migration-job.sh",
    "scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh",
    "scripts/validate-module-033-project-forge-interactive.sh",
    "src/backend/ProjectTime.Api/Modules/FlowHiveProtectedTestReleaseMarker.cs",
    "src/backend/ProjectTime.Api/Modules/PostgresProjectFlowHivePlanRepository.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectForgeInteractiveModule.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs",
    "src/backend/ProjectTime.Api/Modules/ProjectPlanningCollaborationModule.cs",
    "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx",
    "src/frontend/project-time-web/src/ProjectFlowHiveEnterprisePanels.jsx",
    "src/frontend/project-time-web/src/ProjectForgeCenter.jsx",
    "src/frontend/project-time-web/src/flowhive-sow-evidence-autoadmission.js",
    "tests/test-project-planning-collaboration-migration-095.sh",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
    "tests/validate-flowhive-sow-evidence-autoadmission.mjs",
    "tests/validate-project-planning-collaboration-access.mjs",
    "tests/validate-project-planning-collaboration.mjs",
)

for seed_path in SEED_PATHS:
    subprocess.run(
        ["git", "checkout", SEED_REF, "--", seed_path],
        check=True,
    )

source = subprocess.check_output(
    ["git", "show", f"{SOURCE_COMMIT}:{SOURCE_PATH}"],
    text=True,
)
old = "ROOT = Path(__file__).resolve().parents[2]"
new = "ROOT = Path.cwd()"
if source.count(old) != 1:
    raise SystemExit("Pinned FlowHive repair generator root anchor is unavailable or ambiguous.")
source = source.replace(old, new, 1)
namespace = {
    "__name__": "__main__",
    "__file__": str(Path.cwd() / SOURCE_PATH),
}
exec(compile(source, SOURCE_PATH, "exec"), namespace)

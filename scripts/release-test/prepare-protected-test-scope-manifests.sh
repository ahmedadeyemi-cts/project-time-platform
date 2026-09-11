#!/usr/bin/env bash
set -Eeuo pipefail
CIT="${1:?runner temporary directory is required}"
HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME:-}}"
cat > "$CIT/allowed-release-files" <<'FILES'
.github/workflows/module-loading-assignment-propagation-ci.yml
.github/workflows/module-management-owner-drawer-ci.yml
.github/workflows/module025-protected-uat-control.yml
.github/workflows/module033-project-forge-ci.yml
.github/workflows/deep-intelligence-read-contract-ci.yml
.github/workflows/projectpulse-deploy-test.yml
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
.github/workflows/protected-uat-celar-ai-pr782-workflow-control.yml
.github/workflows/runtime-navigation-work-register-responsive-ci.yml
.github/workflows/systemwide-enterprise-reliability-ci.yml
.github/workflows/systemwide-enterprise-reliability-test-deployment.yml
database/migrations/040_scoped_role_policy_versions/00_schema.sql
database/migrations/097_project_planning_identity_safe_admission.sql
database/migrations/100_module001b_catalog_ownership_reconciliation.sql
database/rollback/097_project_planning_identity_safe_admission_rollback.sql
database/rollback/100_module001b_catalog_ownership_reconciliation_rollback.sql
deployment/containers/web/default.conf.template
scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh
scripts/release-test/prepare-protected-test-scope-manifests.sh
scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
scripts/release-test/run-assigned-work-protected-test-uat.sh
scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh
scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh
scripts/wait-containerapp-ready-revision.sh
src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs
src/backend/ProjectTime.Api/Ai/CelarAiInternalDataService.cs
src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs
src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs
src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagContracts.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs
src/backend/ProjectTime.Api/Modules/Module025ProtectedTestUatAccess.cs
src/backend/ProjectTime.Api/Modules/Module025SowGsdContracts.cs
src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
src/backend/ProjectTime.Api/Modules/Module025SowGsdDocumentExporter.cs
src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs
src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs
src/backend/ProjectTime.Api/Program.cs
tests/ProjectTime.Api.AuthorizationTests/Program.cs
tests/validate-celar-ai-pr630-consolidated.mjs
src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs
src/frontend/project-time-web/scripts/validate-modules-directory-page.mjs
src/frontend/project-time-web/src/App.jsx
src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx
src/frontend/project-time-web/src/background-request-role-gate.js
src/frontend/project-time-web/src/module-availability-bridge.js
src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx
src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx
src/frontend/project-time-web/src/project-flowhive-center.css
tests/CelarAiInternalDataTests/Program.cs
tests/FlowHiveDetailedPlannerTests/Program.cs
tests/test-module-catalog-owner-repair-migration-093.sh
tests/test-project-planning-identity-safe-admission-migration-097.sh
tests/test-pulse-ai-runtime-job-query-shape.sh
tests/validate-systemwide-enterprise-reliability.mjs
tests/validate-flowhive-sow-evidence-autoadmission.mjs
tests/validate-module-management-owner-drawer.mjs
tests/validate-systemwide-image-build-controller.mjs
tests/validate-utilization-role-scoping.mjs
FILES
sed -i 's/^[[:space:]]*//' "$CIT/allowed-release-files"
if [[ "$HEAD_BRANCH" == 'fix/ai-planner-evidence-fallback-20260905' ]]; then
  node tests/validate-planner-fallback-build-release-scope.mjs
  echo 'deployment/containers/api/Dockerfile' >> "$CIT/allowed-release-files"
  echo 'tests/validate-planner-fallback-build-release-scope.mjs' >> "$CIT/allowed-release-files"
elif [[ "$HEAD_BRANCH" == 'control/flowhive-sow-successor-approval-20260909' ]]; then
  cat >> "$CIT/allowed-release-files" <<'FILES'
.github/flowhive-psa-protected-test-candidate.json
.github/flowhive-psa-release-control-files.txt
.github/workflows/flowhive-psa-protected-test-admission.yml
docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md
scripts/release-test/apply-flowhive-psa-migrations.sh
scripts/release-test/dispatch-flowhive-psa-test.mjs
scripts/release-test/flowhive-psa-admission.mjs
tests/flowhive-psa-admission.test.mjs
tests/flowhive-psa-migration-fixture.py
tests/flowhive-psa-release-control.mjs
tests/flowhive-psa-release-workflow.test.py
FILES
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-release-stabilization-20260910' ]]; then
  cat >> "$CIT/allowed-release-files" <<'FILES'
.github/flowhive-psa-stale-run-supersession-authorization.json
.github/workflows/flowhive-psa-protected-test-admission.yml
.github/workflows/projectpulse-deploy-test.yml
docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md
scripts/release-test/dispatch-flowhive-psa-test.mjs
scripts/release-test/prepare-protected-test-scope-manifests.sh
tests/flowhive-psa-admission.test.mjs
tests/flowhive-psa-release-control.mjs
tests/flowhive-psa-release-workflow.test.py
FILES
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-installed-pm-readiness-candidate-20260911' ]]; then
  test -s .github/flowhive-enterprise-psa-release-files.txt
  grep -Ev '^[[:space:]]*(#|$)' .github/flowhive-enterprise-psa-release-files.txt >> "$CIT/allowed-release-files"
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-protected-cutover-20260910' ]]; then
  cat >> "$CIT/allowed-release-files" <<'FILES'
.github/flowhive-psa-protected-cutover.json
.github/flowhive-psa-release-control-files.txt
.github/workflows/flowhive-psa-protected-test-admission.yml
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md
scripts/release-test/dispatch-flowhive-psa-test.mjs
scripts/release-test/prepare-protected-test-scope-manifests.sh
tests/flowhive-psa-admission.test.mjs
tests/flowhive-psa-release-control.mjs
tests/flowhive-psa-release-workflow.test.py
FILES
fi
LC_ALL=C sort -u "$CIT/allowed-release-files" -o "$CIT/allowed-release-files"

cat > "$CIT/e-protected-uat-merged-main-relaunch-governance-files" <<'FILES'
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
.github/workflows/protected-uat-celar-ai-pr782-workflow-control.yml
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-protected-uat-merged-main-relaunch-governance-files"
LC_ALL=C sort -u "$CIT/e-protected-uat-merged-main-relaunch-governance-files" -o "$CIT/e-protected-uat-merged-main-relaunch-governance-files"

cat > "$CIT/e-runtime-repair-files" <<'FILES'
.github/workflows/projectpulse-deploy-test.yml
.github/workflows/runtime-navigation-work-register-responsive-ci.yml
src/backend/ProjectTime.Api/Modules/ProjectManagementWorkRegisterScope.cs
src/frontend/project-time-web/scripts/validate-runtime-navigation-work-register-responsive.mjs
src/frontend/project-time-web/src/App.jsx
src/frontend/project-time-web/src/ApprovalMailbox.jsx
src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx
src/frontend/project-time-web/src/approval-access-navigation-compatibility.js
src/frontend/project-time-web/src/module-directory-authority.js
src/frontend/project-time-web/src/unified-project-financial-workspace.css
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-runtime-repair-files"
LC_ALL=C sort -u "$CIT/e-runtime-repair-files" -o "$CIT/e-runtime-repair-files"

cat > "$CIT/e-assigned-work-repair-files" <<'FILES'
.github/workflows/module-loading-assignment-propagation-ci.yml
.github/workflows/projectpulse-deploy-test.yml
.github/workflows/projectpulse-release-test-control-ci.yml
scripts/release-test/run-assigned-work-protected-test-uat.sh
src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs
src/backend/ProjectTime.Api/Program.cs
src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs
src/frontend/project-time-web/src/App.jsx
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-assigned-work-repair-files"
LC_ALL=C sort -u "$CIT/e-assigned-work-repair-files" -o "$CIT/e-assigned-work-repair-files"

cat > "$CIT/e-pr719-module-directory-owner-001a-files" <<'FILES'
.github/workflows/module-loading-assignment-propagation-ci.yml
.github/workflows/module-management-owner-drawer-ci.yml
.github/workflows/projectpulse-release-test-control-ci.yml
.github/workflows/runtime-navigation-work-register-responsive-ci.yml
scripts/release-test/run-assigned-work-protected-test-uat.sh
src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs
src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs
src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs
src/frontend/project-time-web/scripts/validate-modules-directory-page.mjs
src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx
src/frontend/project-time-web/src/ModuleManagementTableView.jsx
src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx
src/frontend/project-time-web/src/background-request-role-gate.js
tests/validate-module-management-owner-drawer.mjs
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-pr719-module-directory-owner-001a-files"
LC_ALL=C sort -u "$CIT/e-pr719-module-directory-owner-001a-files" -o "$CIT/e-pr719-module-directory-owner-001a-files"

cat > "$CIT/e-modules-directory-authority-starvation-files" <<'FILES'
.github/workflows/module-loading-assignment-propagation-ci.yml
.github/workflows/projectpulse-release-test-control-ci.yml
.github/workflows/runtime-navigation-work-register-responsive-ci.yml
src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs
src/frontend/project-time-web/scripts/validate-modules-directory-page.mjs
src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx
src/frontend/project-time-web/src/background-request-role-gate.js
src/frontend/project-time-web/src/module-availability-bridge.js
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-modules-directory-authority-starvation-files"
LC_ALL=C sort -u "$CIT/e-modules-directory-authority-starvation-files" -o "$CIT/e-modules-directory-authority-starvation-files"

cat > "$CIT/e-module001b-live-uat-repair-files" <<'FILES'
.github/workflows/module001b-live-reallocation-protected-test-uat.yml
.github/workflows/projectpulse-release-test-control-ci.yml
scripts/release-test/run-assigned-work-protected-test-uat.sh
src/backend/ProjectTime.Api/Modules/Module001BProtectedTestUatFixtureModule.cs
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-module001b-live-uat-repair-files"
LC_ALL=C sort -u "$CIT/e-module001b-live-uat-repair-files" -o "$CIT/e-module001b-live-uat-repair-files"

cat > "$CIT/e-utilization-manager-authorization-repair-files" <<'FILES'
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
src/backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs
src/backend/ProjectTime.Api/Modules/ScopedRolePolicyRules.cs
tests/ProjectTime.Api.AuthorizationTests/Program.cs
tests/validate-utilization-role-scoping.mjs
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-utilization-manager-authorization-repair-files"
LC_ALL=C sort -u "$CIT/e-utilization-manager-authorization-repair-files" -o "$CIT/e-utilization-manager-authorization-repair-files"

cat > "$CIT/e-777-workspace" <<'FILES'
database/migrations/089_module_catalog_role_administration_reconciliation.sql
src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs
src/frontend/project-time-web/scripts/validate-group-6-enterprise-presentation.mjs
src/frontend/project-time-web/src/enterprise/SalesDeliveryWorkflowCenter.jsx
src/frontend/project-time-web/src/module-availability-registry.js
tests/validate-systemwide-enterprise-reliability.mjs
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-777-workspace"
LC_ALL=C sort -u "$CIT/e-777-workspace" -o "$CIT/e-777-workspace"

cat > "$CIT/e-uat-defects" <<'FILES'
.github/workflows/module-loading-assignment-propagation-ci.yml
.github/workflows/module-management-owner-drawer-ci.yml
.github/workflows/projectpulse-deploy-test.yml
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
database/migrations/100_module001b_catalog_ownership_reconciliation.sql
database/rollback/100_module001b_catalog_ownership_reconciliation_rollback.sql
scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh
scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
src/backend/ProjectTime.Api/Ai/CelarAiInternalDataService.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
src/backend/ProjectTime.Api/Modules/Module025SowGsdDocumentExporter.cs
src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs
src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs
src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx
src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx
src/frontend/project-time-web/src/project-flowhive-center.css
tests/CelarAiInternalDataTests/Program.cs
tests/test-module-catalog-owner-repair-migration-093.sh
tests/validate-celar-ai-pr630-consolidated.mjs
tests/validate-flowhive-sow-evidence-autoadmission.mjs
tests/validate-module-management-owner-drawer.mjs
tests/validate-systemwide-image-build-controller.mjs
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-uat-defects"
LC_ALL=C sort -u "$CIT/e-uat-defects" -o "$CIT/e-uat-defects"

cat > "$CIT/e-celar-internal-trust-evidence-files" <<'FILES'
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs
tests/validate-celar-ai-internal-data-intelligence.mjs
tests/validate-celar-ai-pr630-consolidated.mjs
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-celar-internal-trust-evidence-files"
LC_ALL=C sort -u "$CIT/e-celar-internal-trust-evidence-files" -o "$CIT/e-celar-internal-trust-evidence-files"

cat > "$CIT/e-flowhive-reviewed-regeneration-files" <<'FILES'
.github/flowhive-psa-protected-test-candidate.json
.github/workflows/projectpulse-deploy-test.yml
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md
scripts/release-test/apply-flowhive-psa-migrations.sh
scripts/release-test/build-and-run-flowhive-psa-migrations.sh
scripts/release-test/flowhive-psa-admission.mjs
scripts/release-test/run-flowhive-psa-live-uat.py
tests/flowhive-psa-live-uat.test.py
tests/flowhive-psa-migration-fixture.py
tests/flowhive-psa-release-control.mjs
tests/flowhive-psa-release-workflow.test.py
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-flowhive-reviewed-regeneration-files"
LC_ALL=C sort -u "$CIT/e-flowhive-reviewed-regeneration-files" -o "$CIT/e-flowhive-reviewed-regeneration-files"

cat > "$CIT/e-flowhive-protected-cutover-files" <<'FILES'
.github/flowhive-psa-protected-cutover.json
.github/flowhive-psa-release-control-files.txt
.github/workflows/flowhive-psa-protected-test-admission.yml
.github/workflows/projectpulse-release-test-control-ci-reregistered.yml
.github/workflows/projectpulse-release-test-control-ci.yml
docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md
scripts/release-test/dispatch-flowhive-psa-test.mjs
scripts/release-test/prepare-protected-test-scope-manifests.sh
tests/flowhive-psa-admission.test.mjs
tests/flowhive-psa-release-control.mjs
tests/flowhive-psa-release-workflow.test.py
FILES
sed -i 's/^[[:space:]]*//' "$CIT/e-flowhive-protected-cutover-files"
LC_ALL=C sort -u "$CIT/e-flowhive-protected-cutover-files" -o "$CIT/e-flowhive-protected-cutover-files"

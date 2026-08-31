#!/usr/bin/env bash
set -Eeuo pipefail

BASE_BRANCH="${GITHUB_BASE_REF:-main}"
HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME:-}}"
git fetch origin "$BASE_BRANCH" --no-tags
BASE_REF="$(git merge-base "origin/$BASE_BRANCH" HEAD)"
test -n "$BASE_REF"
CHANGED="$(git diff --name-only "$BASE_REF"...HEAD)"
printf '%s\n' "$CHANGED"

OWNED='^(\.github/workflows/(module030-analytics-center-ci|module030-analytics-enterprise-experience-ci)\.yml|database/migrations/060_analytics_center_enterprise_experience\.sql|database/rollback/060_analytics_center_enterprise_experience_rollback\.sql|docs/modules/module-030-analytics-enterprise-experience/README\.md|src/backend/ProjectTime.Api/Directory\.Build\.targets|src/backend/ProjectTime.Api/Modules/(AnalyticsBrandedExportBuilder|AnalyticsCenterEnterpriseContracts|AnalyticsCenterEnterpriseExperienceModule|AnalyticsCenterExperienceScope|AnalyticsCenterScheduler|AnalyticsCenterScheduleRepository|AnalyticsCenterScheduleService|Module065AnalyticsAttachmentDelivery)\.cs|src/frontend/project-time-web/scripts/validate-analytics-center\.mjs|src/frontend/project-time-web/src/AnalyticsCenter\.jsx|src/frontend/project-time-web/src/analytics/AnalyticsMultiSelect\.jsx|src/frontend/project-time-web/src/analytics-center\.css|tests/test-analytics-center-enterprise-migration-060\.sh)$'

if [[ "$HEAD_BRANCH" == feature/analytics-center-enterprise-experience-* || "$HEAD_BRANCH" == fix/module030-enterprise-regression-mode-* ]]; then
  UNEXPECTED="$(grep -Ev "$OWNED" <<<"$CHANGED" || true)"
  if [[ -n "$UNEXPECTED" ]]; then
    echo 'Unexpected Analytics enterprise source scope:' >&2
    printf '%s\n' "$UNEXPECTED" >&2
    exit 1
  fi
  echo 'ANALYTICS_ENTERPRISE_VALIDATION_MODE=OWNED_SOURCE' >> "$GITHUB_ENV"
elif grep -Fxq 'tests/test-uat-functional-completion-contract.sh' <<<"$CHANGED"; then
  UAT_DIRECT_ANALYTICS="$(grep -E '^(\.github/workflows/(module030-analytics-center-ci|module030-analytics-enterprise-experience-ci)\.yml|src/backend/ProjectTime.Api/Modules/(AnalyticsBrandedExportBuilder|AnalyticsCenterEnterpriseContracts|AnalyticsCenterEnterpriseExperienceModule|AnalyticsCenterExperienceScope|AnalyticsCenterScheduler|AnalyticsCenterScheduleRepository|AnalyticsCenterScheduleService|Module065AnalyticsAttachmentDelivery)\.cs|src/frontend/project-time-web/scripts/validate-analytics-center\.mjs|src/frontend/project-time-web/src/(AnalyticsCenter\.jsx|analytics/AnalyticsMultiSelect\.jsx|analytics-center\.css))$' <<<"$CHANGED" || true)"
  UAT_ALLOWED='^(\.github/workflows/(module030-analytics-center-ci|module030-analytics-enterprise-experience-ci)\.yml|src/backend/ProjectTime.Api/Modules/AnalyticsCenterExperienceScope\.cs|src/frontend/project-time-web/src/(AnalyticsCenter\.jsx|analytics-center\.css))$'
  UNEXPECTED="$(grep -Ev "$UAT_ALLOWED" <<<"$UAT_DIRECT_ANALYTICS" || true)"
  if [[ -n "$UNEXPECTED" ]]; then
    echo 'Unexpected Analytics enterprise source in UAT functional-completion mode:' >&2
    printf '%s\n' "$UNEXPECTED" >&2
    exit 1
  fi
  echo 'ANALYTICS_ENTERPRISE_VALIDATION_MODE=UAT_FUNCTIONAL_COMPLETION' >> "$GITHUB_ENV"
  echo 'ANALYTICS_ENTERPRISE_UAT_OWNED_SUBSET=PASSED'
elif [[ "$HEAD_BRANCH" == fix/systemwide-enterprise-reliability-final-* ]]; then
  SYSTEMWIDE_DIRECT_ANALYTICS="$(grep -E "$OWNED" <<<"$CHANGED" || true)"
  SYSTEMWIDE_ALLOWED='^\.github/workflows/(module030-analytics-center-ci|module030-analytics-enterprise-experience-ci)\.yml$'
  UNEXPECTED="$(grep -Ev "$SYSTEMWIDE_ALLOWED" <<<"$SYSTEMWIDE_DIRECT_ANALYTICS" || true)"
  if [[ -n "$UNEXPECTED" ]]; then
    echo 'The systemwide reliability package changed Analytics-owned source outside its exact CI convergence boundary:' >&2
    printf '%s\n' "$UNEXPECTED" >&2
    exit 1
  fi
  echo 'ANALYTICS_ENTERPRISE_VALIDATION_MODE=SYSTEMWIDE_RELIABILITY' >> "$GITHUB_ENV"
  echo 'ANALYTICS_ENTERPRISE_SYSTEMWIDE_SOURCE_BOUNDARY=PASSED'
elif [[ "$HEAD_BRANCH" == fix/shared-project-document-planning-* ]]; then
  FLOWHIVE_RELEASE_MANIFEST='.github/shared-project-document-planning-governed-release-files.txt'
  FLOWHIVE_DIRECT_ANALYTICS="$(grep -E "$OWNED" <<<"$CHANGED" || true)"
  FLOWHIVE_ALLOWED='^\.github/workflows/(module030-analytics-center-ci|module030-analytics-enterprise-experience-ci)\.yml$'
  UNEXPECTED="$(grep -Ev "$FLOWHIVE_ALLOWED" <<<"$FLOWHIVE_DIRECT_ANALYTICS" || true)"
  if [[ -n "$UNEXPECTED" ]]; then
    echo 'The FlowHive V2 package changed Analytics enterprise-owned source outside its exact CI convergence boundary:' >&2
    printf '%s\n' "$UNEXPECTED" >&2
    exit 1
  fi
  for required in \
    "$FLOWHIVE_RELEASE_MANIFEST" \
    '.github/workflows/module030-analytics-center-ci.yml' \
    '.github/workflows/module030-analytics-enterprise-experience-ci.yml' \
    'scripts/ci/validate-module030-analytics-enterprise-source-boundary.sh'; do
    test -f "$required"
    grep -Fxq "$required" <<<"$CHANGED"
    grep -Fxq "$required" "$FLOWHIVE_RELEASE_MANIFEST"
  done
  echo 'ANALYTICS_ENTERPRISE_VALIDATION_MODE=FLOWHIVE_V2_SHARED_PLANNING' >> "$GITHUB_ENV"
  echo 'ANALYTICS_ENTERPRISE_FLOWHIVE_SOURCE_BOUNDARY=PASSED'
else
  DIRECT_ANALYTICS="$(grep -E '^(database/(migrations/060_analytics_center_enterprise_experience\.sql|rollback/060_analytics_center_enterprise_experience_rollback\.sql)|docs/modules/module-030-analytics-enterprise-experience/README\.md|src/backend/ProjectTime.Api/Modules/(AnalyticsBrandedExportBuilder|AnalyticsCenterEnterpriseContracts|AnalyticsCenterEnterpriseExperienceModule|AnalyticsCenterExperienceScope|AnalyticsCenterScheduler|AnalyticsCenterScheduleRepository|AnalyticsCenterScheduleService|Module065AnalyticsAttachmentDelivery)\.cs|src/frontend/project-time-web/scripts/validate-analytics-center\.mjs|src/frontend/project-time-web/src/(AnalyticsCenter\.jsx|analytics/AnalyticsMultiSelect\.jsx|analytics-center\.css)|tests/test-analytics-center-enterprise-migration-060\.sh)$' <<<"$CHANGED" || true)"
  if [[ -n "$DIRECT_ANALYTICS" ]]; then
    echo 'An unrelated branch directly changes Analytics enterprise-owned source:' >&2
    printf '%s\n' "$DIRECT_ANALYTICS" >&2
    exit 1
  fi
  echo 'ANALYTICS_ENTERPRISE_VALIDATION_MODE=REGRESSION' >> "$GITHUB_ENV"
  echo "ANALYTICS_ENTERPRISE_REGRESSION_BRANCH=$HEAD_BRANCH"
fi

for protected in \
  '.github/workflows/projectpulse-deploy-runtime-direct-timer-recovery-test.yml' \
  '.github/workflows/validate-runtime-direct-timer-recovery-deployment.yml' \
  'scripts/validate-runtime-direct-timer-recovery-test-deployment.sh'; do
  if grep -Fxq "$protected" <<<"$CHANGED"; then
    echo "Protected deployment file changed: $protected" >&2
    exit 1
  fi
done

DEPLOYMENT_OVERLAP="$(grep -E '^(deployment/|\.github/workflows/projectpulse-deploy-|scripts/.*deploy)' <<<"$CHANGED" || true)"
if [[ "$HEAD_BRANCH" == release/consolidated-enterprise-validation-* ]]; then
  DEPLOYMENT_OVERLAP="$(grep -Fvx 'deployment/containers/web/Dockerfile' <<<"$DEPLOYMENT_OVERLAP" || true)"
fi
if [[ "$HEAD_BRANCH" == fix/systemwide-enterprise-reliability-final-* ]]; then
  GENERATED_API_SOURCE='deployment/rocky-linux/apply-remaining-psa-module-api-patch.sh'
  DEPLOYMENT_OVERLAP="$(grep -Fvx "$GENERATED_API_SOURCE" <<<"$DEPLOYMENT_OVERLAP" || true)"
  test -f "$GENERATED_API_SOURCE"
  grep -Fxq "$GENERATED_API_SOURCE" <<<"$CHANGED"
  echo 'ANALYTICS_ENTERPRISE_SYSTEMWIDE_DEPLOYMENT_BOUNDARY=PASSED'
fi
if [[ "$HEAD_BRANCH" == fix/shared-project-document-planning-* ]]; then
  FLOWHIVE_RELEASE_MANIFEST='.github/shared-project-document-planning-governed-release-files.txt'
  CONTROLLER='.github/workflows/projectpulse-deploy-test.yml'
  DEPLOYMENT_OVERLAP="$(grep -Fvx "$CONTROLLER" <<<"$DEPLOYMENT_OVERLAP" || true)"
  test -f "$CONTROLLER"
  grep -Fxq "$CONTROLLER" <<<"$CHANGED"
  grep -Fxq "$CONTROLLER" "$FLOWHIVE_RELEASE_MANIFEST"
  grep -Fq 'workflow_dispatch:' "$CONTROLLER"
  grep -Fq 'environment: test' "$CONTROLLER"
  grep -Fq 'Only the authorized FlowHive V2 candidate branch may use pre-merge Protected-Test deployment.' "$CONTROLLER"
  grep -Fq 'PRODUCTION_MUTATION=NONE' "$CONTROLLER"
  ! grep -Eq 'environment:[[:space:]]+production' "$CONTROLLER"
  echo 'ANALYTICS_ENTERPRISE_FLOWHIVE_PROTECTED_TEST_BOUNDARY=PASSED'
fi
if [[ -n "$DEPLOYMENT_OVERLAP" ]]; then
  echo 'Analytics enterprise validation detected deployment-control overlap:' >&2
  printf '%s\n' "$DEPLOYMENT_OVERLAP" >&2
  exit 1
fi
if [[ "$HEAD_BRANCH" == feature/analytics-center-enterprise-experience-* || "$HEAD_BRANCH" == fix/module030-enterprise-regression-mode-* ]] && \
   grep -E '^(src/frontend/project-time-web/src/(App|main)\.|src/frontend/project-time-web/src/module-availability-registry\.js)' <<<"$CHANGED"; then
  echo 'Analytics enterprise-owned source overlaps canonical application integration files.' >&2
  exit 1
fi

git diff --check "$BASE_REF"...HEAD
echo 'ANALYTICS_ENTERPRISE_SOURCE_OR_REGRESSION_SCOPE=PASS'

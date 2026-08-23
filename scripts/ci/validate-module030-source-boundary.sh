#!/usr/bin/env bash
set -Eeuo pipefail

BASE_BRANCH="${GITHUB_BASE_REF:-main}"
HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME:-}}"

git fetch origin "$BASE_BRANCH" --no-tags
BASE_REF="$(git merge-base "origin/$BASE_BRANCH" HEAD)"
test -n "$BASE_REF"
mapfile -t CHANGED_FILES < <(git diff --name-only "$BASE_REF"...HEAD)
printf '%s\n' "${CHANGED_FILES[@]}"

changed_exact() {
  local expected="$1" file
  for file in "${CHANGED_FILES[@]}"; do
    [[ "$file" == "$expected" ]] && return 0
  done
  return 1
}

publish_mode() {
  local mode="$1"
  if [[ -n "${GITHUB_ENV:-}" ]]; then
    printf 'ANALYTICS_CENTER_VALIDATION_MODE=%s\n' "$mode" >> "$GITHUB_ENV"
  fi
  printf 'ANALYTICS_CENTER_VALIDATION_MODE=%s\n' "$mode"
}

for protected in \
  '.github/workflows/projectpulse-deploy-runtime-direct-timer-recovery-test.yml' \
  '.github/workflows/validate-runtime-direct-timer-recovery-deployment.yml' \
  'scripts/validate-runtime-direct-timer-recovery-test-deployment.sh'; do
  if changed_exact "$protected"; then
    echo "Protected deployment file changed: $protected" >&2
    exit 1
  fi
done

is_module030_owned_path() {
  case "$1" in
    .github/workflows/module030-analytics-center-ci.yml|\
    .github/workflows/group5-financial-operations-recovery-ci.yml|\
    .github/workflows/pulse-ai-help-chat-usability-ci.yml|\
    database/migrations/055_analytics_center.sql|\
    database/rollback/055_analytics_center_rollback.sql|\
    docs/modules/module-030-analytics-center/README.md|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterContracts.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterDirectoryLoader.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterModule.cs|\
    src/backend/ProjectTime.Api/Modules/EnterpriseReportingContracts.cs|\
    src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs|\
    src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs|\
    src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs|\
    src/backend/ProjectTime.Api/Modules/EnterpriseReportingRepository.cs|\
    src/backend/ProjectTime.Api/Modules/EnterpriseReportingModule.cs|\
    src/backend/ProjectTime.Api/ProjectTime.Api.csproj|\
    src/frontend/project-time-web/package.json|\
    src/frontend/project-time-web/scripts/inject-analytics-center.mjs|\
    src/frontend/project-time-web/scripts/inject-group-4-project-notification-automation.mjs|\
    src/frontend/project-time-web/scripts/validate-analytics-center.mjs|\
    src/frontend/project-time-web/scripts/validate-group-5-financial-operations-recovery.mjs|\
    src/frontend/project-time-web/src/AnalyticsCenter.jsx|\
    src/frontend/project-time-web/src/analytics-center.css|\
    tests/test-analytics-center-migration-055.sh|\
    scripts/ci/validate-module030-source-boundary.sh|\
    scripts/ci/validate-module030-compiled.sh) return 0 ;;
    *) return 1 ;;
  esac
}

is_enterprise_extension_path() {
  case "$1" in
    .github/workflows/module030-analytics-center-ci.yml|\
    .github/workflows/module030-analytics-enterprise-experience-ci.yml|\
    database/migrations/060_analytics_center_enterprise_experience.sql|\
    database/rollback/060_analytics_center_enterprise_experience_rollback.sql|\
    docs/modules/module-030-analytics-enterprise-experience/README.md|\
    src/backend/ProjectTime.Api/Directory.Build.targets|\
    src/backend/ProjectTime.Api/Modules/AnalyticsBrandedExportBuilder.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterEnterpriseContracts.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterEnterpriseExperienceModule.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterExperienceScope.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterScheduler.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterScheduleRepository.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenterScheduleService.cs|\
    src/backend/ProjectTime.Api/Modules/Module065AnalyticsAttachmentDelivery.cs|\
    src/frontend/project-time-web/scripts/validate-analytics-center.mjs|\
    src/frontend/project-time-web/src/AnalyticsCenter.jsx|\
    src/frontend/project-time-web/src/analytics/AnalyticsMultiSelect.jsx|\
    src/frontend/project-time-web/src/analytics-center.css|\
    tests/test-analytics-center-enterprise-migration-060.sh|\
    scripts/ci/validate-module030-source-boundary.sh|\
    scripts/ci/validate-module030-compiled.sh) return 0 ;;
    *) return 1 ;;
  esac
}

is_direct_analytics_path() {
  case "$1" in
    database/migrations/055_analytics_center.sql|\
    database/migrations/060_analytics_center_enterprise_experience.sql|\
    database/rollback/055_analytics_center_rollback.sql|\
    database/rollback/060_analytics_center_enterprise_experience_rollback.sql|\
    docs/modules/module-030-analytics-center/README.md|\
    docs/modules/module-030-analytics-enterprise-experience/README.md|\
    src/backend/ProjectTime.Api/Modules/AnalyticsCenter*.cs|\
    src/backend/ProjectTime.Api/Modules/AnalyticsBrandedExportBuilder.cs|\
    src/backend/ProjectTime.Api/Modules/Module065AnalyticsAttachmentDelivery.cs|\
    src/backend/ProjectTime.Api/Modules/EnterpriseReporting*.cs|\
    src/frontend/project-time-web/scripts/inject-analytics-center.mjs|\
    src/frontend/project-time-web/scripts/validate-analytics-center.mjs|\
    src/frontend/project-time-web/src/AnalyticsCenter.jsx|\
    src/frontend/project-time-web/src/analytics/AnalyticsMultiSelect.jsx|\
    src/frontend/project-time-web/src/analytics-center.css|\
    tests/test-analytics-center*.sh) return 0 ;;
    *) return 1 ;;
  esac
}

reject_unexpected() {
  local predicate="$1" label="$2" file
  local -a unexpected=()
  for file in "${CHANGED_FILES[@]}"; do
    if ! "$predicate" "$file"; then
      unexpected+=("$file")
    fi
  done
  if ((${#unexpected[@]})); then
    echo "$label" >&2
    printf '%s\n' "${unexpected[@]}" >&2
    exit 1
  fi
}

if [[ "$HEAD_BRANCH" == feature/module-030-analytics-center-* ]]; then
  reject_unexpected is_module030_owned_path 'Unexpected Module 030 Analytics Center source scope:'
  publish_mode OWNED_SOURCE
  echo 'ANALYTICS_CENTER_SOURCE_ISOLATION=PASSED'
elif [[ "$HEAD_BRANCH" == feature/analytics-center-enterprise-experience-* ]]; then
  reject_unexpected is_enterprise_extension_path 'Unexpected Analytics enterprise extension scope:'
  publish_mode ENTERPRISE_EXTENSION
  echo 'ANALYTICS_CENTER_ENTERPRISE_SOURCE_ISOLATION=PASSED'
elif [[ "$HEAD_BRANCH" == feature/module-033-project-forge-* ]]; then
  for file in "${CHANGED_FILES[@]}"; do
    if is_direct_analytics_path "$file"; then
      case "$file" in
        src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs|\
        src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs|\
        src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs) ;;
        *) echo "Unexpected Module 030 source in Project Forge integration mode: $file" >&2; exit 1 ;;
      esac
    fi
  done
  publish_mode MODULE_033_PROJECT_FORGE
  echo 'ANALYTICS_CENTER_PROJECT_FORGE_INTEGRATION=PASSED'
elif changed_exact 'tests/test-uat-functional-completion-contract.sh'; then
  for file in "${CHANGED_FILES[@]}"; do
    if is_direct_analytics_path "$file"; then
      case "$file" in
        src/backend/ProjectTime.Api/Modules/AnalyticsCenterExperienceScope.cs|\
        src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs|\
        src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs|\
        src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs|\
        src/frontend/project-time-web/src/AnalyticsCenter.jsx|\
        src/frontend/project-time-web/src/analytics-center.css) ;;
        *) echo "Unexpected Module 030 source in UAT functional-completion mode: $file" >&2; exit 1 ;;
      esac
    fi
  done
  publish_mode UAT_FUNCTIONAL_COMPLETION
  echo 'ANALYTICS_CENTER_UAT_OWNED_SUBSET=PASSED'
else
  for file in "${CHANGED_FILES[@]}"; do
    if is_direct_analytics_path "$file"; then
      echo "An unrelated branch directly changes Module 030-owned source: $file" >&2
      exit 1
    fi
  done
  publish_mode REGRESSION
  echo "ANALYTICS_CENTER_REGRESSION_MODE=PASS branch=$HEAD_BRANCH"
fi

mapfile -t DEPLOYMENT_OVERLAP < <(printf '%s\n' "${CHANGED_FILES[@]}" | grep -E '^(deployment/|\.github/workflows/projectpulse-deploy-|scripts/.*deploy)' || true)
remove_overlap() {
  local allowed="$1" file
  local -a retained=()
  for file in "${DEPLOYMENT_OVERLAP[@]}"; do
    [[ "$file" == "$allowed" ]] || retained+=("$file")
  done
  DEPLOYMENT_OVERLAP=("${retained[@]}")
}

if [[ "$HEAD_BRANCH" == release/consolidated-enterprise-validation-* ]]; then
  remove_overlap 'deployment/containers/web/Dockerfile'
fi
if [[ "$HEAD_BRANCH" == fix/systemwide-enterprise-reliability-final-* ]]; then
  remove_overlap 'deployment/rocky-linux/apply-remaining-psa-module-api-patch.sh'
  test -f deployment/rocky-linux/apply-remaining-psa-module-api-patch.sh
  changed_exact 'deployment/rocky-linux/apply-remaining-psa-module-api-patch.sh'
  echo 'ANALYTICS_CENTER_SYSTEMWIDE_RELIABILITY_BOUNDARY=PASSED'
fi
if [[ "$HEAD_BRANCH" == fix/shared-project-document-planning-* ]]; then
  remove_overlap '.github/workflows/projectpulse-deploy-test.yml'
  test -f .github/workflows/projectpulse-deploy-test.yml
  changed_exact '.github/workflows/projectpulse-deploy-test.yml'
  echo 'ANALYTICS_CENTER_FLOWHIVE_RELEASE_BOUNDARY=PASSED'
fi
if ((${#DEPLOYMENT_OVERLAP[@]})); then
  echo 'Analytics Center validation detected deployment-control overlap:' >&2
  printf '%s\n' "${DEPLOYMENT_OVERLAP[@]}" >&2
  exit 1
fi

if [[ "$HEAD_BRANCH" == feature/module-030-analytics-center-* || "$HEAD_BRANCH" == feature/analytics-center-enterprise-experience-* ]]; then
  for file in "${CHANGED_FILES[@]}"; do
    case "$file" in
      src/frontend/project-time-web/src/App.*|\
      src/frontend/project-time-web/src/main.*|\
      src/frontend/project-time-web/src/module-availability-registry.js)
        echo 'Analytics-owned source overlaps canonical generated integration files.' >&2
        exit 1
        ;;
    esac
  done
fi

if changed_exact 'database/migrations/054_enterprise_reporting_center.sql'; then
  echo 'Migration 054 is owned by another PR and must not be reused.' >&2
  exit 1
fi

git diff --check "$BASE_REF"...HEAD

#!/usr/bin/env bash
set -Eeuo pipefail

cat dist/assets/*.js > /tmp/analytics-center.js
cat dist/assets/*.css > /tmp/analytics-center.css

require_marker() {
  local file="$1" marker="$2" kind="$3"
  if ! grep -aFq -- "$marker" "$file"; then
    echo "MISSING_${kind}_MARKER=$marker" >&2
    exit 1
  fi
}

if grep -Fq '.analytics-enterprise-shell' src/analytics-center.css; then
  echo 'ANALYTICS_CENTER_COMPILED_PROFILE=ENTERPRISE'
  for marker in \
    'Analytics Center' 'Back to Modules' 'Back to Dashboard' \
    'Recently Viewed Dashboards & Reports' 'Report Library' 'Set criteria' \
    'All customers' 'All projects' 'All engineers' 'All Project Managers' 'All teams' \
    'Preview report' 'Run & save' 'Scheduled Reports' 'US Signal PDF' \
    '/api/analytics/v2/overview' '/api/analytics/v2/schedules'; do
    require_marker /tmp/analytics-center.js "$marker" COMPILED_JS
  done
  for marker in \
    '.analytics-enterprise-shell' '.analytics-sidebar' '.analytics-kpi-grid' \
    '.analytics-report-categories' '.analytics-filter-grid' \
    '.analytics-multiselect-menu' '.analytics-schedule-panel'; do
    require_marker /tmp/analytics-center.css "$marker" COMPILED_CSS
  done
else
  echo 'ANALYTICS_CENTER_COMPILED_PROFILE=BASE'
  for marker in \
    'Analytics Center' 'Set criteria' 'All customers' 'All projects' \
    'All engineers' 'All Project Managers' 'All teams' 'Preview report' 'Run & save' \
    'Analytics run history' '/api/analytics/catalog' '/api/analytics/filter-options'; do
    require_marker /tmp/analytics-center.js "$marker" COMPILED_JS
  done
  for marker in \
    '.analytics-center' '.analytics-build-layout' '.analytics-filter-grid' \
    '.analytics-source-grid' '.analytics-history-list'; do
    require_marker /tmp/analytics-center.css "$marker" COMPILED_CSS
  done
fi

for forbidden in \
  'selectedEngineerSummaryText' '030Q Reporting Readiness Closeout' \
  'Build Export Layout' 'Save Report Definition Preview'; do
  if grep -aFq -- "$forbidden" /tmp/analytics-center.js; then
    echo "Legacy Analytics marker remains in compiled bundle: $forbidden" >&2
    exit 1
  fi
done

echo 'ANALYTICS_CENTER_FRONTEND_BUILD=PASSED'

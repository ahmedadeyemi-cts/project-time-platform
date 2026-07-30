#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_BRANCH="fix/module006-pipeline-modules-directory-superadmin-20260730-v2"
ACTUAL_BRANCH="$(git branch --show-current)"
[[ "$ACTUAL_BRANCH" == "$EXPECTED_BRANCH" ]] || {
  echo "ERROR: unexpected branch $ACTUAL_BRANCH" >&2
  exit 1
}

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cat .github/module006-payload/part-* > "$WORK/payload.b64"
base64 -d "$WORK/payload.b64" > "$WORK/payload.tar.gz"
tar -xzf "$WORK/payload.tar.gz" -C .

python3 - <<'PY'
from pathlib import Path

path = Path('src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs')
source = path.read_text()
old = "    moduleNumber: registryModule?.moduleNumber || moduleNumberForRoute(route, moduleNumberSource),"
new = "    moduleNumber: moduleNumberForRoute(route, moduleNumberSource),"
if old not in source:
    raise SystemExit('Expected authoritative Module 006 directory projection marker was not found.')
path.write_text(source.replace(old, new, 1))
PY

node --check src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs
node --check src/frontend/project-time-web/scripts/validate-module-006-toyota-hyundai-pipelines.mjs
for file in src/frontend/project-time-web/src/toyota-hyundai-pipeline-*.js; do
  node --check "$file"
done

git diff --check

git add \
  docs/modules/module-006-toyota-hyundai-pipelines/IMPLEMENTATION-PLAN.md \
  src/frontend/project-time-web/src/ProjectRegisterCenter.jsx \
  src/frontend/project-time-web/src/project-register-center.css \
  src/frontend/project-time-web/src/toyota-hyundai-pipeline-*.js \
  src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs \
  src/frontend/project-time-web/scripts/validate-module-006-toyota-hyundai-pipelines.mjs

git diff --cached --check
if git diff --cached --quiet; then
  echo "ERROR: no source changes were staged." >&2
  exit 1
fi

git commit -m "Fix Module 006 pipeline data and module directory authority"
echo "MODULE006_SOURCE_COMMIT=$(git rev-parse HEAD)"

#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-view-as-drawer-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-view-as-drawer-test-deployment.yml"
EXPECTED_RELEASE="c3e918b12d27e37373f0b0533d0e6c97e9aa0180"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require_file "$DEPLOY"
require_file "$VALIDATE"

require "$DEPLOY" 'name: ProjectPulse Deploy Administrator View-As Drawer Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-VIEW-AS-DRAWER-TO-TEST'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-view-as-drawer-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'Only the verified Administrator View-As drawer release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'

require "$DEPLOY" 'GlobalViewAsDrawer.jsx'
require "$DEPLOY" 'global-view-as-drawer.css'
require "$DEPLOY" 'validate-global-view-as-drawer.mjs'
require "$DEPLOY" 'Administrator View-As'
require "$DEPLOY" 'View-As Active'
require "$DEPLOY" 'Exit preview'
require "$DEPLOY" '/api/project-workspace/view-as/users'
require "$DEPLOY" 'projectPulseViewAsUser'
require "$DEPLOY" 'X-ProjectPulse-View-As-User'
require "$DEPLOY" "status: 'view_as_read_only'"
require "$DEPLOY" '#projectpulse-global-view-as-topbar-slot'
require "$DEPLOY" 'bottom: 5.75rem'
require "$DEPLOY" 'node scripts/validate-global-view-as-drawer.mjs'
require "$DEPLOY" 'node scripts/validate-admin-runtime-stability.mjs'
require "$DEPLOY" 'node scripts/validate-group-1-navigation-work-consolidation.mjs'
require "$DEPLOY" 'npm run build'

require "$DEPLOY" 'Capture immutable rollback web image and unchanged API image'
require "$DEPLOY" 'Build exact immutable View-As drawer web image'
require "$DEPLOY" 'Deploy exact View-As drawer web candidate'
require "$DEPLOY" 'Wait for exact View-As drawer web revision'
require "$DEPLOY" 'Validate served drawer assets and unchanged API image'
require "$DEPLOY" 'project-health-dashboard-web'
require "$DEPLOY" 'view-as-drawer-$SHA'
require "$DEPLOY" 'VIEW_AS_DRAWER_WEB_VALIDATION=PASS'
require "$DEPLOY" 'VIEW_AS_DRAWER_API_IMAGE=UNCHANGED'
require "$DEPLOY" 'legacyTopbarViewAs'
require "$DEPLOY" 'administratorViewAsDrawer'
require "$DEPLOY" 'viewAsReadOnlyBoundary'
require "$DEPLOY" 'apiDeployment'
require "$DEPLOY" '"database": "unchanged"'
require "$DEPLOY" '"migrations": "unchanged"'

require "$DEPLOY" 'Restore captured web image on failure'
require "$DEPLOY" 'Web rollback skipped because another image is active'
require "$DEPLOY" 'VIEW_AS_DRAWER_WEB_ROLLBACK=COMPLETE'
require "$DEPLOY" 'wait-containerapp-ready-revision.sh'
require "$DEPLOY" 'if [[ "$ACTIVE" =='
require "$DEPLOY" 'steps.build.outputs.web_image'
require "$DEPLOY" 'steps.before.outputs.old_web_image'

reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'psql|PROJECTPULSE_TEST_DATABASE_URL|database/migrations|database/rollback'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$DEPLOY" 'az[[:space:]]+containerapp[[:space:]]+job'
reject "$DEPLOY" 'project-health-dashboard-api:'
reject "$DEPLOY" 'API_REPOSITORY='
reject "$DEPLOY" 'Deploy exact API|Deploy API candidate|--revision-suffix[[:space:]]+"[^\"]*api'

require "$VALIDATE" 'name: Validate Administrator View-As Drawer Test Deployment'
require "$VALIDATE" 'release/view-as-drawer-web-test-*'
require "$VALIDATE" 'Enforce exact deployment-control scope'
require "$VALIDATE" 'scripts/validate-view-as-drawer-test-deployment.sh'
require "$VALIDATE" 'bash -n scripts/validate-view-as-drawer-test-deployment.sh'
require "$VALIDATE" 'node scripts/validate-global-view-as-drawer.mjs'
require "$VALIDATE" 'node scripts/validate-admin-runtime-stability.mjs'
require "$VALIDATE" 'node scripts/validate-group-1-navigation-work-consolidation.mjs'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'view-as-drawer/deployment-controls'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+update'
reject "$VALIDATE" 'environment:[[:space:]]*production'

echo 'VIEW_AS_DRAWER_TEST_DEPLOYMENT_GUARD=PASS'

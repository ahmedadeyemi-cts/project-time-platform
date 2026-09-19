"""Execute retention admission offline and constrain the exact repair scope."""
import os
from pathlib import Path
import subprocess
import tempfile
BASE='45074ea00496ef561fda25e7a2f148280689ed97'
HELPER='scripts/release-test/build-and-run-module025-retention-migration-106.sh'
SUP='.github/workflows/module025-protected-uat-control.yml'
REG='scripts/release-test/validate-protected-test-controller-branches.sh'
SELF='tests/module025-export-retention-scope.py'
CI='.github/workflows/flowhive-psa-release-control-ci.yml'
def show(p): return subprocess.check_output(['git','show',BASE+':'+p]).decode()
assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip()==BASE
assert set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines())=={HELPER,SUP,REG,SELF,CI}
old='[[ "${TARGET_RELEASE_BRANCH:-}" == main && "${ACCEPTANCE_SCOPE:-}" == sow_role ]]'
new='''[[ "${TARGET_RELEASE_BRANCH:-}" == main ]]
case "${ACCEPTANCE_SCOPE:-}" in
  sow_role|sow_exports) ;;
  *) echo "ERROR: Module 025 retention migrations require sow_role or sow_exports acceptance." >&2; exit 1 ;;
esac'''
assert Path(HELPER).read_text()==show(HELPER).replace(old,new,1)
assert Path(SUP).read_text()==show(SUP).replace("      - 'scripts/release-test/verify-module025-quarantine-controller.py'", "      - 'scripts/release-test/verify-module025-quarantine-controller.py'\n      - 'scripts/release-test/build-and-run-module025-retention-migration-106.sh'",1)
assert Path(REG).read_text()==show(REG).replace('if [[ "$HEAD_BRANCH" == fix/module025-export-orphan-recovery ]]; then','if [[ "$HEAD_BRANCH" == fix/module025-export-retention-scope ]]; then\n  python3 tests/module025-export-retention-scope.py\n  node tests/validate-systemwide-image-build-controller.mjs\nelif [[ "$HEAD_BRANCH" == fix/module025-export-orphan-recovery ]]; then',1)
sha=subprocess.check_output(['git','rev-parse','HEAD']).decode().strip()
with tempfile.TemporaryDirectory() as temp:
    az=Path(temp)/'az';az.write_text('#!/bin/sh\necho AZURE_READ_PREFLIGHT_REACHED >&2\nexit 77\n');az.chmod(0o755)
    env={**os.environ,'PATH':temp+':'+os.environ['PATH'],'PROJECTPULSE_RELEASE_ROOT':str(Path.cwd()),'RELIABILITY_RELEASE_COMMIT':sha,'AZURE_ACR_NAME':'testregistry','GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1','GITHUB_EVENT_NAME':'workflow_dispatch','GITHUB_REF':'refs/heads/main','TARGET_RELEASE_BRANCH':'main','AZURE_RESOURCE_GROUP':'test-group','AZURE_API_APP':'test-api'}
    for scope in ['sow_role','sow_exports','full','','invalid']:
        r=subprocess.run(['bash',HELPER],env={**env,'ACCEPTANCE_SCOPE':scope},capture_output=True,text=True)
        assert (r.returncode==77)==(scope in ['sow_role','sow_exports']), (scope,r.stderr)
        if scope not in ['sow_role','sow_exports']: assert 'AZURE_READ_PREFLIGHT_REACHED' not in r.stderr
    for change in [{'GITHUB_EVENT_NAME':'push'},{'GITHUB_REF':'refs/heads/other'},{'TARGET_RELEASE_BRANCH':'other'},{'RELIABILITY_RELEASE_COMMIT':'0'*40}]:
        r=subprocess.run(['bash',HELPER],env={**env,'ACCEPTANCE_SCOPE':'sow_exports',**change},capture_output=True,text=True)
        assert r.returncode!=77 and r.returncode!=0 and 'AZURE_READ_PREFLIGHT_REACHED' not in r.stderr
for p in ['.github/workflows/projectpulse-deploy-test.yml','.github/workflows/projectpulse-deploy-production.yml','database/migrations/106_module025_sow_sell_register.sql','database/migrations/110_module025_ungenerated_draft_delete.sql','database/migrations/111_connectwise_sell_provider.sql']:
    assert Path(p).read_text()==show(p)
print('MODULE025_EXPORT_RETENTION_SCOPE=PASS admission_cases=9 migration_sql=unchanged cloud_calls=0')

assert Path(CI).read_text()==show(CI).replace('          if [[ "$GITHUB_HEAD_REF" == fix/module025-standard-download-formats-20260919 ]]; then','          if [[ "$GITHUB_HEAD_REF" == fix/module025-export-retention-scope ]]; then\n            python3 tests/module025-export-retention-scope.py\n          elif [[ "$GITHUB_HEAD_REF" == fix/module025-standard-download-formats-20260919 ]]; then',1)

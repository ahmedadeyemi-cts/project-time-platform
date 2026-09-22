"""Exact deployment repair scope; no SQL, application, or release-authority changes."""
from pathlib import Path
import os
import subprocess
ROOT=Path(__file__).resolve().parents[1]
BASE='e81b458f77c6af114c06585475386568a060450d'
BRANCH='fix/uat-migration-throttle-recovery-20260922'
EXPECTED={'scripts/release-test/run-flowhive-authority-migration-094-job.sh', 'scripts/release-test/validate-protected-test-controller-branches.sh', 'scripts/release-test/recover-pr1140-migration-retry-orphan.py', '.github/workflows/module025-protected-uat-control.yml', 'tests/test-pr1140-migration-retry-recovery.py', 'tests/uat-migration-throttle-scope.py', 'scripts/release-test/azure-migration-throttle.sh', 'tests/test-azure-migration-throttle.py', '.github/workflows/uat-migration-throttle-recovery-ci.yml'}
EXPECTED.add('tests/pr1140-uat-recovery-scope.py')
def git(*args):return subprocess.check_output(['git',*args],cwd=ROOT,text=True)
def verify_paths(actual):assert set(actual)==EXPECTED,'Unexpected repair scope'
def main():
 for bad in (EXPECTED-{'tests/test-azure-migration-throttle.py'},EXPECTED|{'.github/workflows/projectpulse-deploy-production.yml'}):
  try:verify_paths(bad)
  except AssertionError:pass
  else:raise AssertionError('Invalid scope accepted')
 assert os.getenv('GITHUB_HEAD_REF')==BRANCH
 assert git('merge-base',BASE,'HEAD').strip()==BASE
 verify_paths(git('diff','--name-only',BASE,'HEAD').splitlines())
 assert not git('ls-files','--others','--exclude-standard').strip()
 for path,anchor,addition in [('.github/workflows/module025-protected-uat-control.yml', '            # PR1140_UAT_RECOVERY_END\n', '            # MIGRATION_RETRY_RECOVERY_BEGIN\n            if [[ "$run_id" == \'35761573008\' ]]; then\n              python3 scripts/release-test/recover-pr1140-migration-retry-orphan.py \\\n                || fail \'Migration retry orphan recovery did not meet its exact safety contract.\'\n              quarantined_runs+=("$run_id")\n              continue\n            fi\n            # MIGRATION_RETRY_RECOVERY_END\n'), ('scripts/release-test/validate-protected-test-controller-branches.sh', '# PR1140_RECOVERY_SCOPE_BEGIN\n', '# MIGRATION_THROTTLE_SCOPE_BEGIN\nif [[ "$HEAD_BRANCH" == fix/uat-migration-throttle-recovery-20260922 ]]; then\n  python3 tests/uat-migration-throttle-scope.py\n  python3 tests/test-azure-migration-throttle.py\n  python3 tests/test-pr1140-migration-retry-recovery.py\n  node tests/validate-systemwide-image-build-controller.mjs\n  return\nfi\n# MIGRATION_THROTTLE_SCOPE_END\n')]:
  prior=git('show',BASE+':'+path)
  assert prior.count(anchor)==1
  assert (ROOT/path).read_text()==prior.replace(anchor,addition+anchor,1),path
 path='scripts/release-test/run-flowhive-authority-migration-094-job.sh'
 source=git('show',BASE+':'+path)
 source=source.replace('set -Eeuo pipefail\n','set -Eeuo pipefail\n\nsource "$(dirname "${BASH_SOURCE[0]}")/azure-migration-throttle.sh"\n',1)
 for old,new in {'$(az account show ': '$(azure_migration_retry account_read az account show ', '$(az identity show ': '$(azure_migration_retry identity_read az identity show ', '$(az containerapp show ': '$(azure_migration_retry api_read az containerapp show ', '$(az account get-access-token ': '$(azure_migration_retry token_read az account get-access-token ', '$(az containerapp job execution list ': '$(azure_migration_retry executions_read az containerapp job execution list ', '\naz rest --method put ': '\nazure_migration_retry job_put az rest --method put '}.items():
  assert old in source;source=source.replace(old,new)
 assert (ROOT/path).read_text()==source,'Unrelated migration094 runner change'
 previous=git('show',BASE+':scripts/release-test/recover-pr1140-uat-orphan.py')
 for old,new in [("PR 1140's observed zero-job", "PR 1140's migration-retry zero-job"), ('35739149661', '35761573008'), ('4e8871cf9a7c4643483d178feecff67ec6d78d12', 'e81b458f77c6af114c06585475386568a060450d'), ('2026-09-22T14:16:21Z', '2026-09-22T17:34:19Z'), ('recover-pr1140-uat-orphan.py', 'recover-pr1140-migration-retry-orphan.py'), ('PR1140_UAT_ORPHAN_RECOVERY=', 'PR1140_MIGRATION_RETRY_ORPHAN_RECOVERY=')]:
  assert old in previous;previous=previous.replace(old,new)
 assert (ROOT/'scripts/release-test/recover-pr1140-migration-retry-orphan.py').read_text()==previous,'Recovery algorithm changed'
 previous_scope=git('show',BASE+':tests/pr1140-uat-recovery-scope.py')
 assert previous_scope.count("    assert after == before.replace(anchor, addition + anchor, 1), 'Unrelated supervisor or registry change'\n")==1
 assert (ROOT/'tests/pr1140-uat-recovery-scope.py').read_text()==previous_scope.replace("    assert after == before.replace(anchor, addition + anchor, 1), 'Unrelated supervisor or registry change'\n",'    expected = before.replace(anchor, addition + anchor, 1)\n    # A later reviewed repair adds one pinned recovery and its CI registration.\n    # Compare the entire exact extended source, not a regex-stripped projection.\n    if after != expected:\n        extensions = {\n            SUPERVISOR_ANCHOR: (\'            # PR1140_UAT_RECOVERY_END\\n\', \'            # MIGRATION_RETRY_RECOVERY_BEGIN\\n            if [[ "$run_id" == \\\'35761573008\\\' ]]; then\\n              python3 scripts/release-test/recover-pr1140-migration-retry-orphan.py \\\\\\n                || fail \\\'Migration retry orphan recovery did not meet its exact safety contract.\\\'\\n              quarantined_runs+=("$run_id")\\n              continue\\n            fi\\n            # MIGRATION_RETRY_RECOVERY_END\\n\'),\n            REGISTRY_ANCHOR: (\'# PR1140_RECOVERY_SCOPE_BEGIN\\n\', \'# MIGRATION_THROTTLE_SCOPE_BEGIN\\nif [[ "$HEAD_BRANCH" == fix/uat-migration-throttle-recovery-20260922 ]]; then\\n  python3 tests/uat-migration-throttle-scope.py\\n  python3 tests/test-azure-migration-throttle.py\\n  python3 tests/test-pr1140-migration-retry-recovery.py\\n  node tests/validate-systemwide-image-build-controller.mjs\\n  return\\nfi\\n# MIGRATION_THROTTLE_SCOPE_END\\n\'),\n        }\n        if anchor in extensions:\n            marker, extension = extensions[anchor]\n            assert expected.count(marker) == 1\n            expected = expected.replace(marker, extension + marker, 1)\n    assert after == expected, \'Unrelated supervisor or registry change\'\n',1),'Unrelated historical test changes'
 assert git('rev-parse','HEAD:.github/workflows/projectpulse-deploy-test.yml').strip()=='634983f88d5ce3161b626010c3e20c41a80e3758'
 subprocess.run(['git','diff','--exit-code',BASE,'HEAD','--','src','database','.github/workflows/projectpulse-deploy-test.yml','.github/workflows/projectpulse-deploy-production.yml','scripts/release-test/flowhive-psa-admission.mjs'],cwd=ROOT,check=True)
 workflow=(ROOT/'.github/workflows/uat-migration-throttle-recovery-ci.yml').read_text()
 assert 'permissions:\n  contents: read\n' in workflow
 assert not any(x in workflow for x in ['contents: write','actions: write','environment:','secrets.','git push','azure/login'])
 subprocess.run(['git','diff','--check',BASE,'HEAD'],cwd=ROOT,check=True)
 print('UAT_MIGRATION_THROTTLE_SCOPE=PASS exact_files=10 application_and_sql=unchanged release_authority=unchanged')
if __name__=='__main__':main()

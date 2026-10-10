#!/usr/bin/env python3
"""Exact read-only installed acceptance repair; no deployment or app changes."""
import argparse,subprocess
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--base',required=True);args=p.parse_args()
allowed={'.github/workflows/flowhive-psa-installed-acceptance.yml','.github/workflows/flowhive-psa-release-control-ci.yml','scripts/release-test/run-flowhive-my-role-browser.py','tests/role-journey-browser-contract.test.py','tests/installed-accounting-acceptance-scope.py'}
changed=set(subprocess.check_output(['git','diff','--name-only',args.base+'...HEAD'],text=True).splitlines())
assert changed and changed<=allowed, 'installed_acceptance_scope_changed'
subprocess.run(['git','diff','--check',args.base+'...HEAD'],check=True)
w=Path('.github/workflows/flowhive-psa-installed-acceptance.yml').read_text()
assert 'group: projectpulse-deploy-test' in w and 'cancel-in-progress: false' in w and 'name: test' in w
assert 'INSTALLED_ACCOUNTING_EXPORT_ACCEPTANCE=PASS CELAR_AI=PENDING_NOT_EXECUTED' in w
assert "[[ \"${{ steps.my_role.outcome }}\" == success ]]" in w
for forbidden in ('az containerapp update','az containerapp job','workflow enable','pull-requests: write'):
 assert forbidden not in w
print('INSTALLED_ACCOUNTING_EXACT_SCOPE=PASS APP_DATABASE_NATIVE_CONTROLLER=UNCHANGED')

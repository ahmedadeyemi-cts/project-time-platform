#!/usr/bin/env python3
"""Exact export-only preflight repair scope; never grants deployment authority."""
import argparse,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('--base',required=True);args=p.parse_args()
allowed={'.github/workflows/accounting-reporting-ci.yml','scripts/release-test/verify-oracle-sow-runtime.py',
 'tests/test-oracle-runtime-preflight.py','scripts/resilience/select_core_release.py','scripts/resilience/accounting_export_preflight_scope.py'}
changed=set(subprocess.check_output(['git','diff','--name-only',args.base,'HEAD'],cwd=ROOT,text=True).splitlines())
assert changed==allowed,'Export preflight repair file scope changed'
subprocess.run(['git','diff','--check',args.base,'HEAD'],cwd=ROOT,check=True)
subprocess.run(['python3','tests/security-release/test_controller_registration.py'],cwd=ROOT,check=True)
subprocess.run(['python3','tests/test-oracle-runtime-preflight.py'],cwd=ROOT,check=True)
print('EXPORT_PREFLIGHT_EXACT_SCOPE=PASS native_controller_app_database_and_production=UNCHANGED')

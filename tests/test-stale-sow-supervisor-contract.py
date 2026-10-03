from pathlib import Path

controller = Path('.github/workflows/projectpulse-deploy-test.yml').read_text()
supervisor = Path('.github/workflows/module025-protected-uat-control.yml').read_text()
cleanup = Path('scripts/release-test/cleanup-stale-sows-protected-test.py').read_text()

for marker in [
    'stale_sow_maintenance_mode:',
    'stale_sow_maintenance_confirmation:',
    'Evaluate and maintain stale Protected-Test SOWs',
    'PROJECTPULSE_M087_PASSWORD: ${{ secrets.PROJECTPULSE_M087_PASSWORD }}',
    'steps.module025_normal_sa_browser.outcome == \'success\'',
    'STALE_SOW_PROTECTED_TEST_MAINTENANCE=PASS',
]:
    assert marker in controller, marker

for marker in [
    "stale_sow_mode='dry-run'",
    "stale_sow_mode='apply'",
    'stale_sow_maintenance_mode:$stale_sow_mode',
    'stale_sow_maintenance_confirmation:$stale_sow_confirmation',
]:
    assert marker in supervisor, marker

assert 'DELETE STALE TEST SOWS' in cleanup
assert '.github/workflows/protected-test-stale-sow-maintenance.yml' not in controller
print('STALE_SOW_SUPERVISOR_CONTRACT=PASS')

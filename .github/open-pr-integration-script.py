from pathlib import Path

validator = Path('src/frontend/project-time-web/scripts/validate-pending-time-workflow.mjs').read_text()
required = [
    'PENDING_APPROVAL_ALL_WEEKS',
    'PM_PTC_BULK_APPROVAL_NO_COMMENT',
    'PTC_NON_PROJECT_TASK_DESTINATION',
]
missing = [marker for marker in required if marker not in validator]
if missing:
    raise SystemExit(f'Missing pending-time workflow markers: {missing}')

migration = Path('database/migrations/051a_pending_approval_day_status_lifecycle.sql')
rollback = Path('database/rollback/051a_pending_approval_day_status_lifecycle_rollback.sql')
for path in (migration, rollback):
    if not path.is_file():
        raise SystemExit(f'Missing reviewed migration source: {path}')
print('PR284_LATEST_MAIN_RECONCILIATION=PASS')

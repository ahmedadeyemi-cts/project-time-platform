from pathlib import Path

program = Path('src/backend/ProjectTime.Api/Program.cs').read_text()
required = [
    'app.UseProjectPulseSecurityHardening();',
    'SECURITY_20260729_SAFE_DOCUMENT_PATH_COMPONENT',
    'SECURITY_20260729_VIEW_AS_ALL_WRITES_BLOCKED',
    'SECURITY_20260729_ADMIN_USER_DIRECTORY',
]
missing = [marker for marker in required if marker not in program]
if missing:
    raise SystemExit(f'Missing reconciled security markers: {missing}')
print('PR285_LATEST_MAIN_RECONCILIATION=PASS')

#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.4.4"', api)

replacement = r'''static async Task<Guid> GetOrCreateDevelopmentUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
{
    const string sql = """
        INSERT INTO app_users (email, display_name, job_title, department, is_active)
        VALUES ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Development Engineer', 'Professional Services', TRUE)
        ON CONFLICT (email) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            job_title = EXCLUDED.job_title,
            department = EXCLUDED.department,
            is_active = TRUE,
            updated_at = NOW()
        RETURNING user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create development user."));
}
'''

pattern = r'static async Task<Guid> GetOrCreateDevelopmentUserIdAsync\(NpgsqlConnection connection(?:, NpgsqlTransaction\? transaction = null)?\)\s*\{.*?\n\}'
api, count = re.subn(pattern, replacement, api, count=1, flags=re.S)

if count == 0:
    # Some patched versions may not yet have the helper in the expected shape. Add it before the manager helper or DB config records.
    insertion_point = 'static async Task<Guid> GetOrCreateDevelopmentManagerUserIdAsync'
    if insertion_point in api:
        api = api.replace(insertion_point, replacement + '\n' + insertion_point, 1)
    else:
        record_point = 'internal sealed record TimesheetSaveRequest'
        if record_point not in api:
            raise SystemExit('ERROR: Could not find a safe insertion point for GetOrCreateDevelopmentUserIdAsync.')
        api = api.replace(record_point, replacement + '\n' + record_point, 1)

# Make any older hard-coded development engineer email references consistent.
api = api.replace('engineer@ussignal.local', 'ahmed.adeyemi@ussignal.com')
api = api.replace('developer@projectpulse.local', 'ahmed.adeyemi@ussignal.com')
api = api.replace('dev.engineer@ussignal.local', 'ahmed.adeyemi@ussignal.com')

api_file.write_text(api)
PY

echo "==> Current user / Open Tasks identity fix applied"
echo "==> Open Tasks now uses ahmed.adeyemi@ussignal.com as the development engineer identity."
echo "==> Expected API version after redeploy: 0.4.4"

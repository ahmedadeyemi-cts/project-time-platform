"""Verify execution and cleanup contracts for independent authenticated gates."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[1]
source = (root / '.github/workflows/projectpulse-deploy-test.yml').read_text()
# This complements the repository's full workflow parser with exact behavioral
# contracts for these named gates, without adding a CI package dependency.
blocks = re.split(r'^      - name: ', source, flags=re.M)[1:]
steps = []
for block in blocks:
    step = {'name': block.splitlines()[0], 'run': block}
    for key in ('id', 'if', 'continue-on-error'):
        found = re.findall(r'^        ' + key + r': (.+)$', block, re.M)
        assert len(found) <= 1
        if found:
            step[key] = found[0]
    steps.append(step)
by_id = {s['id']: s for s in steps if 'id' in s}
ids = [s.get('id') for s in steps]
for name in ('assigned_work_uat', 'utilization_uat', 'module025_fixture'):
    condition = by_id[name]['if']
    assert condition == "${{ !cancelled() && steps.uat.outcome == 'success' }}", (name, condition)
    assert by_id[name].get('continue-on-error', 'false') == 'false'
assert ids.index('assigned_work_uat') < ids.index('utilization_uat') < ids.index('module025_fixture') < ids.index('module025_uat')
assert by_id['module025_uat']['if'] == "${{ !cancelled() && steps.module025_fixture.outcome == 'success' }}"
assert by_id['module025_uat'].get('continue-on-error', 'false') == 'false'
cleanup = next(s for s in steps if s['name'].startswith('Disable exact-run Module 025'))
assert cleanup['if'] == "always() && steps.module025_fixture.outputs.started == 'true'"
assert '2700' in by_id['module025_fixture']['run']
assert 'MODULE025_UAT_EXPIRES_AT: ${{ steps.module025_fixture.outputs.expires_at }}' in by_id['module025_uat']['run']
script = (root / 'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh').read_text()
assert 'MODULE025_UAT_EXPIRES_AT - 180' in script
assert 'GENERATION_POLL_STARTED_AT + 2520' in script
assert '"$SA_SESSION" "$GENERATION_POLL_TIMEOUT"' in script
assert 'GENERATION_REMAINING_SECONDS' in script
assert 'terminal state within 42 minutes' in script
module = (root / 'src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs').read_text()
assert 'generationDeadline.CancelAfter(TimeSpan.FromMinutes(40))' in module
assert 'generationDeadline.Token).WaitAsync(generationDeadline.Token)' in module
assert 40 * 60 < 42 * 60 < 2700
print('SOW_UAT_INDEPENDENT_GATES_AND_CLEANUP_RESERVE=PASS')

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
for name in ('assigned_work_uat', 'utilization_uat'):
    condition = by_id[name]['if']
    assert condition == "${{ !cancelled() && (steps.uat.outcome == 'success' || steps.psa_live_uat.outputs.deployment_health_verified == 'true') }}", (name, condition)
    assert by_id[name].get('continue-on-error', 'false') == 'false'
assert ids.index('assigned_work_uat') < ids.index('utilization_uat') < ids.index('module025_fixture') < ids.index('module025_uat')
assert by_id['module025_fixture']['if'] == "${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.uat.outcome == 'success' }}"
assert by_id['module025_fixture'].get('continue-on-error', 'false') == 'false'
assert by_id['module025_uat']['if'] == "${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.module025_fixture.outcome == 'success' }}"
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

# Execute the real Bash functions with a deterministic clock and transport.
# No server is contacted and no credentials are read from the environment.
import os
import subprocess
import tempfile
preamble = script.split('\nwait_for_fixture_public_revision\n', 1)[0]
with tempfile.TemporaryDirectory() as tmp:
    home = Path(tmp)
    functions = home / 'functions.sh'
    functions.write_text(preamble)
    common = r'''
source "$FUNCTIONS"
trap - EXIT
rm -rf -- "$WORK_DIR"
date() { cat "$CLOCK"; }
sleep() { echo "$(( $(cat "$CLOCK") + $1 ))" > "$CLOCK"; }
curl() {
  local limit=0 output=''
  while (( $# )); do
    case "$1" in
      --max-time) limit="$2"; shift 2 ;;
      -o) output="$2"; shift 2 ;;
      *) shift ;;
    esac
  done
  printf '%s\n' "$limit" >> "$CALLS"
  [[ -z "$output" ]] || printf '{}\n' > "$output"
  echo "$(( $(cat "$CLOCK") + limit ))" > "$CLOCK"
  printf '503'
}
ENGAGEMENT_ID=fixture
SA_SESSION=synthetic
'''
    def run(now, expression):
        (home/'clock').write_text(str(now))
        (home/'calls').write_text('')
        env = {**os.environ, 'BASE':'https://phd-west-test.onenecklab.com',
            'TEST_LOGIN_PASSWORD':'synthetic', 'MODULE025_UAT_RUN_ID':'123-1',
            'MODULE025_UAT_EXPIRES_AT':'2000000300', 'EVIDENCE_DIR':str(home),
            'FUNCTIONS':str(functions), 'CLOCK':str(home/'clock'), 'CALLS':str(home/'calls'),
            'RESULT':str(home/'result')}
        result = subprocess.run(['bash','-c',common+expression], env=env, text=True,
            capture_output=True, timeout=5, check=True)
        return result.stdout.strip(), [int(x) for x in (home/'calls').read_text().splitlines()], int((home/'clock').read_text())
    for label in ('generated-readback','active-list-readback','archived-list-readback'):
        result, calls, now = run(2000000118,
            f'auth_get_with_transient_retry /api/readback "$RESULT" synthetic {label}')
        assert result=='0|503' and calls==[2] and now==2000000120, (label,result,calls,now)
    result,calls,now=run(2000000120, 'auth_get_with_transient_retry /api/readback "$RESULT" synthetic generated-readback')
    assert result=='28|000' and not calls
    result,calls,now=run(2000000120, 'auth_request POST /api/module025/sow-gsd/fixture/archive "$RESULT" synthetic 120')
    assert calls==[120] and now==2000000240
    result,calls,now=run(2000000239, 'auth_request POST /api/module025/sow-gsd/fixture/archive "$RESULT" synthetic 120')
    assert calls==[1] and now==2000000240
    result,calls,now=run(2000000240, 'auth_request POST /api/auth/session/logout "$RESULT" synthetic 60')
    assert calls==[55] and now==2000000295
    result,calls,now=run(2000000000, 'auth_get_with_transient_retry /api/readback "$RESULT" synthetic generated-readback')
    assert calls==[30,30,30,15] and now==2000000120, (calls,now)
    result,calls,now=run(2000000400, 'auth_request POST /api/module025/sow-gsd/fixture/archive "$RESULT" synthetic 120')
    assert result=='28|000' and not calls
print('SOW_UAT_POST_TERMINAL_AND_ARCHIVAL_DEADLINE_BEHAVIOR=PASS')

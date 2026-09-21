"""Exact repair scope; protected deployment authority remains unchanged."""
from pathlib import Path
import os
import subprocess
import sys
BASE = 'b693227f9ba0c483f5eb521f7fbe46e7ed697f3f'
BRANCH = 'fix/flowhive-installed-verifier-20260921'
REGISTRATIONS = [('.github/workflows/flowhive-psa-release-control-ci.yml', '          if [[ "$GITHUB_HEAD_REF" == fix/flowhive-installed-verifier-20260921 ]]; then\n            python3 tests/flowhive-installed-verifier-scope.py\n          elif [[ "$GITHUB_HEAD_REF" == fix/flowhive-sow-private-generation-20260921 ]]; then', '          if [[ "$GITHUB_HEAD_REF" == fix/flowhive-sow-private-generation-20260921 ]]; then'), ('scripts/release-test/validate-protected-test-controller-branches.sh', 'if [[ "$HEAD_BRANCH" == fix/flowhive-installed-verifier-20260921 ]]; then\n  python3 tests/flowhive-installed-verifier-scope.py\n  node tests/validate-systemwide-image-build-controller.mjs\nelif [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then', 'if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then'), ('tests/flowhive-psa-admission.test.mjs', "const installedVerifierRepair = process.env.GITHUB_HEAD_REF === 'fix/flowhive-installed-verifier-20260921';\nconst module025VerifierCorrection = installedVerifierRepair || privateGenerationRepair || ", 'const module025VerifierCorrection = privateGenerationRepair || '), ('tests/flowhive-psa-admission.test.mjs', "const module025VerifierBase = installedVerifierRepair ? 'b693227f9ba0c483f5eb521f7fbe46e7ed697f3f' : privateGenerationRepair ? ", 'const module025VerifierBase = privateGenerationRepair ? ')]

MANIFEST = '.github/flowhive-installed-verifier-files.txt'
def git(*args): return subprocess.check_output(['git', *args]).decode()
def verify(actual, expected):
    assert expected == sorted(set(expected))
    assert all(p and not any(v in p for v in ['..','*','?','\\']) for p in expected)
    assert set(actual) == set(expected), f'Unexpected repair scope: {set(actual) ^ set(expected)}'
expected = Path(MANIFEST).read_text().splitlines()
verify(expected, expected)
for mutation in [expected[1:], expected + ['.github/workflows/projectpulse-deploy-production.yml']]:
    try: verify(mutation, expected)
    except AssertionError: continue
    raise AssertionError('Scope accepted an omitted or unauthorized path')
if '--self-test' in sys.argv:
    print('INSTALLED_VERIFIER_SCOPE_NEGATIVE_TESTS=PASS')
    sys.exit(0)
if os.getenv('GITHUB_HEAD_REF'): assert os.environ['GITHUB_HEAD_REF'] == BRANCH
if os.getenv('GITHUB_EVENT_NAME'): assert os.environ['GITHUB_EVENT_NAME'] == 'pull_request'
assert git('merge-base', BASE, 'HEAD').strip() == BASE
actual = set(git('diff','--name-only',BASE).splitlines()) | set(git('ls-files','--others','--exclude-standard').splitlines())
verify(actual, expected)
normalized = {}
for path, addition, original in reversed(REGISTRATIONS):
    source = normalized.get(path, Path(path).read_text())
    assert source.count(addition) == 1, f'Missing or repeated registration: {path}'
    normalized[path] = source.replace(addition, original, 1)
for path, source in normalized.items():
    assert source == git('show',BASE+':'+path), f'Unrelated CI changes: {path}'
for path in ['.github/workflows/projectpulse-deploy-test.yml',
             '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml',
             '.github/workflows/flowhive-psa-installed-acceptance.yml',
             '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt',
             'scripts/release-test/flowhive-psa-admission.mjs',
             'scripts/release-test/dispatch-flowhive-psa-test.mjs']:
    assert Path(path).read_bytes() == subprocess.check_output(['git','show',BASE+':'+path]), path
# Limit the application change to the durable SOW deadline; retain all other
# provider behavior and every existing regression assertion byte-for-byte.
provider_path = 'src/backend/ProjectTime.Api/Ai/ProjectPulseDeepSeekProvider.cs'
provider_before = git('show', BASE + ':' + provider_path)
old_call = 'budget.CancelAfter(AttemptBudget(request.Feature));'
new_call = 'budget.CancelAfter(RequestAttemptBudget(request.Feature, request.BoundedPrivatePhase));'
assert provider_before.count(old_call) == 1
anchor = '    internal static TimeSpan AttemptBudget(string feature) => feature switch\n'
addition = '''    // Only server-marked durable SOW phases receive the longer attempt. Queue
    // time remains inside this ceiling, below the router's 330-second deadline.
    // The linked caller token still cancels immediately, including document expiry.
    internal static TimeSpan RequestAttemptBudget(string feature, bool boundedPrivatePhase) =>
        boundedPrivatePhase && feature == CelarAiCapabilityCatalog.SowGsdPlanning
            ? TimeSpan.FromSeconds(300)
            : AttemptBudget(feature);

'''
assert provider_before.count(anchor) == 1
assert Path(provider_path).read_text() == provider_before.replace(old_call, new_call, 1).replace(anchor, addition + anchor, 1), 'Unrelated provider change'
test_path = 'tests/DeepSeekProviderTests/Program.cs'
assert Path(test_path).read_text().startswith(git('show', BASE + ':' + test_path)), 'Existing provider tests changed'
assert 'DEEPSEEK_DURABLE_SOW_PHASE_DEADLINES=PASS' in Path(test_path).read_text()
assert not any(p.startswith('database/') for p in actual)
subprocess.run(['git','diff','--check',BASE],check=True)
print(f'INSTALLED_VERIFIER_SCOPE=PASS files={len(expected)} release_authority=unchanged')

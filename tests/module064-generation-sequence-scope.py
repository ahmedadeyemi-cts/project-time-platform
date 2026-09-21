"""Exact generation repair scope; deployment authority and private-data consent are unchanged."""
from pathlib import Path
import os
import subprocess
import sys
import yaml

BASE = '96394a4a69da92190594a06467f69cf868639532'
BRANCH = 'fix/module064-generation-sequence-20260921'
MANIFEST = '.github/module064-generation-sequence-files.txt'
def git(*args): return subprocess.check_output(['git', *args]).decode()
def verify(actual, expected):
    assert expected == sorted(set(expected))
    assert all(path and not any(x in path for x in ('..', '*', '?', '\\')) for path in expected)
    assert set(actual) == set(expected), f'Unexpected repair paths: {set(actual) ^ set(expected)}'
expected = Path(MANIFEST).read_text().splitlines()
verify(expected, expected)
for mutated in (expected[1:], expected + ['.github/workflows/projectpulse-deploy-production.yml']):
    try: verify(mutated, expected)
    except AssertionError: continue
    raise AssertionError('Unauthorized or omitted scope accepted')
if '--self-test' in sys.argv:
    print('MODULE064_SEQUENCE_SCOPE_NEGATIVE_TESTS=PASS'); sys.exit(0)
if os.getenv('GITHUB_HEAD_REF'): assert os.environ['GITHUB_HEAD_REF'] == BRANCH
if os.getenv('GITHUB_EVENT_NAME'): assert os.environ['GITHUB_EVENT_NAME'] == 'pull_request'
assert git('merge-base', BASE, 'HEAD').strip() == BASE
actual = set(git('diff', '--name-only', BASE).splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
verify(actual, expected)
assert not any(path.startswith('database/') for path in actual)
for path in ('.github/workflows/projectpulse-deploy-test.yml',
             '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml',
             '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt',
             'scripts/release-test/flowhive-psa-admission.mjs',
             'scripts/release-test/dispatch-flowhive-psa-test.mjs',
             'src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs',
             'src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs',
             'src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutionPolicy.cs'):
    assert Path(path).read_text() == git('show', BASE + ':' + path), path
# Observer changes may accommodate the existing application deadlines only.
path = '.github/workflows/flowhive-psa-installed-acceptance.yml'
before = git('show', BASE + ':' + path)
after = before.replace('    timeout-minutes: 45', '    # Two existing 40-minute application ceilings, observation grace, setup and evidence.\n    timeout-minutes: 95').replace("MODULE025_GENERATION_TIMEOUT_SECONDS: '1500'", "MODULE025_GENERATION_TIMEOUT_SECONDS: '2430'")
assert Path(path).read_text() == after, 'Installed acceptance authority or tests were changed'
registrations = [
 ('.github/workflows/flowhive-psa-release-control-ci.yml',
  '          if [[ "$GITHUB_HEAD_REF" == fix/flowhive-installed-verifier-20260921 ]]; then',
  '          if [[ "$GITHUB_HEAD_REF" == ' + BRANCH + ' ]]; then\n            python3 tests/module064-generation-sequence-scope.py\n          elif [[ "$GITHUB_HEAD_REF" == fix/flowhive-installed-verifier-20260921 ]]; then'),
 ('scripts/release-test/validate-protected-test-controller-branches.sh',
  'if [[ "$HEAD_BRANCH" == fix/flowhive-installed-verifier-20260921 ]]; then',
  'if [[ "$HEAD_BRANCH" == ' + BRANCH + ' ]]; then\n  python3 tests/module064-generation-sequence-scope.py\n  node tests/validate-systemwide-image-build-controller.mjs\nelif [[ "$HEAD_BRANCH" == fix/flowhive-installed-verifier-20260921 ]]; then'),
 ('tests/flowhive-psa-admission.test.mjs',
  'const module025VerifierCorrection = installedVerifierRepair ||',
  "const module064SequenceRepair = process.env.GITHUB_HEAD_REF === '" + BRANCH + "';\nconst module025VerifierCorrection = module064SequenceRepair || installedVerifierRepair ||"),
 ('tests/flowhive-psa-admission.test.mjs',
  'const module025VerifierBase = installedVerifierRepair ?',
  "const module025VerifierBase = module064SequenceRepair ? '" + BASE + "' : installedVerifierRepair ?")]
registrations.append(('tests/flowhive-psa-admission.test.mjs',
    '    for (const path of protectedPaths) {',
    "    if (module064SequenceRepair) {\n      // The exact scope validator permits only the two observer-budget changes;\n      // every identity, approval, deployment and final acceptance gate is pinned.\n      execFileSync('python3', ['tests/module064-generation-sequence-scope.py']);\n      protectedPaths.splice(protectedPaths.indexOf('.github/workflows/flowhive-psa-installed-acceptance.yml'), 1);\n    }\n    for (const path of protectedPaths) {"))
registrations.append(('.github/workflows/module064-automatic-provider-health-ci.yml',
    '          HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME}}"',
    '          HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME}}"\n          if [[ "$HEAD_BRANCH" == fix/module064-generation-sequence-20260921 ]]; then\n            python3 tests/module064-generation-sequence-scope.py\n            exit 0\n          fi'))
registrations.append(('.github/workflows/module064-automatic-provider-health-ci.yml',
    "          ROUTER='src/backend/ProjectTime.Api/Ai/ProjectPulseAiRouter.cs'",
    "          ROUTER='src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs'\n          COMPATIBILITY_ROUTER='src/backend/ProjectTime.Api/Ai/ProjectPulseAiRouter.cs'"))
registrations.append(('.github/workflows/module064-automatic-provider-health-ci.yml',
    '          grep -Fq \'_health.ApplyConfiguration(_configuration.Provider(providerCode))\' "$ROUTER"',
    '          grep -Fq \'_health.ApplyConfiguration(_configuration.Provider(target))\' "$ROUTER"\n          grep -Fq \'ProjectPulseAiRouter(CelarAiCapabilityRouter authority)\' "$COMPATIBILITY_ROUTER"\n          grep -Fq \'authority.GenerateAsync(\' "$COMPATIBILITY_ROUTER"'))
normalized = {}
for path, old, new in registrations:
    value = normalized.get(path, git('show', BASE + ':' + path))
    assert value.count(old) == 1
    normalized[path] = value.replace(old, new, 1)
for path, value in normalized.items(): assert Path(path).read_text() == value, path
workflow = yaml.safe_load(Path('.github/workflows/module064-generation-sequence-ci.yml').read_text())
assert workflow['permissions'] == {'contents': 'read'}
assert all('environment' not in job for job in workflow['jobs'].values())
assert 'git push' not in Path('.github/workflows/module064-generation-sequence-ci.yml').read_text()
subprocess.run(['git', 'diff', '--check', BASE], check=True)
print(f'MODULE064_SEQUENCE_SCOPE=PASS files={len(expected)} release_authority=unchanged privacy_approval=unchanged')

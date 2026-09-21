"""Exact additive ownership registration for PR 1139; retain all existing tests."""
from collections import Counter
from pathlib import Path
import os
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True)

base_branch = os.environ.get('GITHUB_BASE_REF') or 'main'
if base_branch != 'main':
    raise SystemExit('Laya release scope requires main as its base')
base = git('merge-base', 'origin/main', 'HEAD').strip()
allowed = set('''
.github/workflows/celar-laya-integration.yml
.github/workflows/module064-automatic-provider-health-ci.yml
deployment/laya/README.md
deployment/laya/deploy-gateway.sh
deployment/laya/grants.sql
deployment/laya/schema.sql
deployment/laya/verify-database.sql
deployment/laya/incremental-policy.py
deployment/oracle-celar/deploy.sh
deployment/oracle-celar/gateway/laya_decisions.py
deployment/oracle-celar/gateway/wsgi_decisions.py
scripts/release-test/build-and-run-module025-retention-migration-106.sh
src/backend/ProjectTime.Api/Ai/LayaDecisionContract.cs
src/backend/ProjectTime.Api/Ai/LayaDecisionTransport.cs
src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs
src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs
src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx
src/frontend/project-time-web/src/ai/LayaDecisionPanel.jsx
src/frontend/project-time-web/src/ai/laya-decisions.css
tests/laya/ContractChecks.csproj
tests/laya/Program.cs
tests/laya/schema_checks.sql
tests/laya/test_gateway.py
tests/laya/test_release_wiring.py
tests/laya/release-scope.py
tests/laya/backend/BackendChecks.csproj
tests/laya/backend/Fakes.cs
tests/laya/backend/Program.cs
'''.split())
changed = set(git('diff', '--name-only', base, 'HEAD', '--').splitlines())
if not changed or changed - allowed:
    raise SystemExit('Unexpected Laya release paths: ' + ', '.join(sorted(changed - allowed)))
for row in git('diff', '--name-status', base, 'HEAD', '--').splitlines():
    if row.split('\t', 1)[0] not in {'A', 'M'}:
        raise SystemExit('Renames and deletions are outside Laya release scope')

def equal_original(path, normalized):
    original = git('show', f'{base}:{path}')
    if normalized.rstrip('\n') != original.rstrip('\n'):
        raise SystemExit('Existing owned source changed outside the additive boundary: ' + path)

parent = 'src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx'
text = (ROOT/parent).read_text()
for insertion in ("import LayaDecisionPanel from './ai/LayaDecisionPanel.jsx';\n", '          <LayaDecisionPanel />\n'):
    if text.count(insertion) != 1:
        raise SystemExit('Expected one direct Module 064 Laya mount')
    text = text.replace(insertion, '', 1)
equal_original(parent, text)

mapping = 'src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs'
text = (ROOT/mapping).read_text()
insertion = '        endpoints.MapLayaDecisionEndpoints();\n'
if text.count(insertion) != 1:
    raise SystemExit('Expected one Laya endpoint registration')
equal_original(mapping, text.replace(insertion, '', 1))

boundaries = {
    '.github/workflows/module064-automatic-provider-health-ci.yml': {'ownership'},
    'deployment/oracle-celar/deploy.sh': {'incremental_gateway', 'full_deployment_adapter'},
    'scripts/release-test/build-and-run-module025-retention-migration-106.sh':
        {'runtime_role','migration_files','migration_apply','migration_image','migration_evidence'},
}
pattern = re.compile(r'^[ \t]*# LAYA_RELEASE_BEGIN ([a-z_]+)\n.*?^[ \t]*# LAYA_RELEASE_END \1\n', re.M | re.S)
for path, names in boundaries.items():
    text = (ROOT/path).read_text()
    seen = Counter(match.group(1) for match in pattern.finditer(text))
    if seen != Counter({name: 1 for name in names}):
        raise SystemExit('Unexpected release insertion blocks: ' + path)
    equal_original(path, pattern.sub('', text))

# These critical files are deliberately not in the allowlist. Check their exact
# contents too: no quarantine/admission edits or shared Group 7 ownership change.
for path in ('.github/workflows/projectpulse-deploy-test.yml',
             '.github/workflows/module025-protected-uat-control.yml',
             'src/frontend/project-time-web/src/ai/AiProviderReadinessPanel.jsx',
             'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
             'src/backend/ProjectTime.Api/Ai/ProjectPulseAiRouter.cs'):
    equal_original(path, (ROOT/path).read_text())
subprocess.run(['git','-C',str(ROOT),'diff','--check',base,'HEAD'],check=True)
print('LAYA_ADDITIVE_RELEASE_SCOPE=PASS')
print('EXISTING_PROVIDER_ORDER_AND_RELEASE_ADMISSION_UNCHANGED=PASS')

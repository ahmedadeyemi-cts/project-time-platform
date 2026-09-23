"""Incremental PR1153 source boundary; original PR1139 installation checks stay intact."""
from pathlib import Path
import subprocess
ROOT = Path(__file__).resolve().parents[2]
def git(*args):
    return subprocess.check_output(['git','-C',str(ROOT),*args],text=True)
base = git('merge-base','origin/main','HEAD').strip()
allowed = set('''
docs/implementation/automatic-document-admission-laya-20260923.md
src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs
src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs
src/frontend/project-time-web/src/ai/LayaDecisionPanel.jsx
src/frontend/project-time-web/src/ai/laya-processing-state.js
src/frontend/project-time-web/tests/laya-processing-state.test.mjs
tests/LayaProcessedSourceTests/LayaProcessedSourceTests.csproj
tests/LayaProcessedSourceTests/Fakes.cs
tests/LayaProcessedSourceTests/Program.cs
tests/LayaProcessedSourceTests/fixture.sql
tests/laya/backend/Fakes.cs
tests/laya/backend/Program.cs
tests/laya/release-scope.py
tests/laya/processed-source-scope.py
.github/workflows/laya-processed-source-ci.yml
'''.split())
changed=set(git('diff','--name-only',base,'HEAD').splitlines())
if not changed or changed-allowed:
    raise SystemExit('Unexpected incremental processed-source paths: '+','.join(sorted(changed-allowed)))
for row in git('diff','--name-status',base,'HEAD').splitlines():
    if row.split('\t',1)[0] not in {'A','M'}: raise SystemExit('No rename/delete is permitted in this source-reader change')
# New reader only consumes existing receipts; no migration, worker enablement,
# provider routing, scanner fallback, role grant, gateway, or release changes.
for path in (
    'src/backend/ProjectTime.Api/Ai/LayaDecisionContract.cs',
    'src/backend/ProjectTime.Api/Ai/LayaDecisionTransport.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeSourceResolver.cs',
    'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
    'src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs',
    'src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx',
    '.github/workflows/celar-laya-integration.yml',
    '.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/module025-protected-uat-control.yml'):
    if (ROOT/path).read_text()!=git('show',f'{base}:{path}'):
        raise SystemExit('Protected source changed: '+path)
module=(ROOT/'src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs').read_text()
for guard in ['AdministratorAsync(c, db, actual.Value, ct)','AiProviderConfigurationModule.SameOrigin(c)',
              'decision_view_as_not_allowed','LayaDecisionTransport.DeploymentAllowed()',
              'LayaProcessedSourceReader.SameEvidence','LockCurrentVersionAsync','human_review_only']:
    if guard not in module: raise SystemExit('Missing boundary: '+guard)
if 'BuildProcessingPreviewAsync' in module:
    raise SystemExit('The classification endpoint still reparses preview content')
subprocess.run(['git','-C',str(ROOT),'diff','--check',base,'HEAD'],check=True)
print('LAYA_PROCESSED_SOURCE_SCOPE=PASS; no universal-admission/automatic-classification completion is implied')

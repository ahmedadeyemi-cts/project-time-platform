"""PR1153 source boundary and automatic-admission safety checks."""
from pathlib import Path
import subprocess
import sys
ROOT = Path(__file__).resolve().parents[2]
# Release machinery must already be accepted in main. This application cannot
# alter inherited controllers, migration wiring, or its persistent owner gates.
subprocess.run([sys.executable, str(ROOT / 'tests/laya/release-registration.py'),
                '--application'], cwd=ROOT, check=True)
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
tests/FlowHivePreparationTests/Program.cs
tests/LayaLeaseTests/LayaLeaseTests.csproj
tests/LayaLeaseTests/Program.cs
src/backend/ProjectTime.Api/Ai/LayaWorkerLease.cs
src/backend/ProjectTime.Api/Ai/PulseAiDocumentIndexAuthorization.cs
tests/LayaIndexAuthorizationTests/LayaIndexAuthorizationTests.csproj
tests/LayaIndexAuthorizationTests/Program.cs
tests/laya/backend/Fakes.cs
tests/laya/backend/Program.cs
tests/laya/release-scope.py
tests/laya/processed-source-scope.py
.github/workflows/laya-processed-source-ci.yml
src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs
src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeSourceResolver.cs
src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs
src/backend/ProjectTime.Api/Modules/ProjectPlanningDocumentPreparation.cs
src/backend/ProjectTime.Api/Modules/ProjectIntakeModule.cs
'''.split())
changed=set(git('diff','--name-only',base,'HEAD').splitlines())
if not changed or changed-allowed:
    raise SystemExit('Unexpected incremental processed-source paths: '+','.join(sorted(changed-allowed)))
for row in git('diff','--name-status',base,'HEAD').splitlines():
    if row.split('\t',1)[0] not in {'A','M'}: raise SystemExit('No rename/delete is permitted in this source-reader change')
# This application owns durable admission and classification behavior only.
# Migration125 and release controls are inherited from the independent control
# change; provider routing, scanner fallback and chat intent remain outside scope.
for path in (
    'src/backend/ProjectTime.Api/Ai/LayaDecisionContract.cs',
    'src/backend/ProjectTime.Api/Ai/LayaDecisionTransport.cs',
    'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
    'src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs',
    'src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx',
    '.github/workflows/celar-laya-integration.yml',
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
resolver = (ROOT/'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeSourceResolver.cs').read_text()
if 'COALESCE(d.engineering_visible, FALSE) = TRUE' not in resolver:
    raise SystemExit('Normal retrieval lost the engineering-visibility boundary')
for marker in ('@processing_admission = TRUE', '@classification_admission = TRUE',
               '@can_queue = TRUE', 'document_version_id = d.pulse_ai_active_version_id',
               "job_status IN ('queued','running','retry_wait')", 'is_service_principal'):
    if marker not in resolver:
        raise SystemExit('Missing exact durable admission boundary: '+marker)
preparation = (ROOT/'src/backend/ProjectTime.Api/Modules/ProjectPlanningDocumentPreparation.cs').read_text()
for marker in ("'closed','completed','cancelled','canceled','archived'", "'celar_ai_chat_attachment'"):
    if marker not in preparation:
        raise SystemExit('Missing project or chat boundary: '+marker)
if 'd.project_id IS NULL' not in preparation:
    raise SystemExit('Projectless accepted intake documents are not admitted to security processing')
intake = (ROOT/'src/backend/ProjectTime.Api/Modules/ProjectIntakeModule.cs').read_text()
for marker in ('QueueAssociatedAsync', 'documentId: documentId', 'security-owned durable processing queue'):
    if marker not in intake:
        raise SystemExit('Project Intake upload is not durably admitted: '+marker)
migration = (ROOT/'database/migrations/125_automatic_document_admission_laya.sql').read_text()
for marker in ('pulse_ai_laya_classification_jobs', 'service_principal_user_id',
               'pulse_ai_laya_classification_status', 'ux_pulse_ai_laya_classification_jobs_identity',
               "'not_requested', 'queued', 'running', 'succeeded'"):
    if marker not in migration:
        raise SystemExit('Missing migration contract: '+marker)
if "WHERE job_status IN ('queued', 'running', 'retry_wait')" in migration:
    raise SystemExit('Classification identity must deduplicate terminal outcomes too')
release_workflow = (ROOT/'.github/workflows/projectpulse-deploy-test.yml').read_text()
for marker in ('database/migrations/125_automatic_document_admission_laya.sql',
               '125_automatic_document_admission_laya'):
    if marker not in release_workflow:
        raise SystemExit('Trusted Test release is missing migration 125 wiring: '+marker)
worker = (ROOT/'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs').read_text()
for marker in ('DocumentServicePrincipalUserId != job.ServicePrincipalUserId',
               'SaveDecisionAsync', 'MaximumAttempts', 'RenewLeaseAsync',
               'LayaWorkerLease.RunAsync', 'LayaProcessedSourceReader.SameEvidence'):
    if marker not in worker:
        raise SystemExit('Missing worker safety contract: '+marker)
classification_repository = (ROOT/'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs').read_text()
for marker in ('reviewRequired', 'automationApproved', 'workflowActionsPerformed'):
    if marker not in classification_repository:
        raise SystemExit('Missing review-only classification boundary: '+marker)
subprocess.run(['git','-C',str(ROOT),'diff','--check',base,'HEAD'],check=True)
print('LAYA_AUTOMATIC_ADMISSION_SCOPE=PASS; exact jobs, current versions, project boundaries and review-only Laya status are wired')

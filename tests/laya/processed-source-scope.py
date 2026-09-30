"""PR1153 source boundary and automatic-admission safety checks."""
from pathlib import Path
import os
import subprocess
import sys
ROOT = Path(__file__).resolve().parents[2]
CURRENT_FLOWHIVE_REPAIR = 'fix/flowhive-enterprise-usability-routing-20260926'
CURRENT_FLOWHIVE_FRONTEND_MARKER = 'fix/flowhive-frontend-convergence-marker-20260926'
CURRENT_FLOWHIVE_UAT_IDEMPOTENT = 'fix/flowhive-protected-uat-idempotent-20260927'

def current_branch():
    return os.environ.get('GITHUB_HEAD_REF') or subprocess.check_output(
        ['git','-C',str(ROOT),'rev-parse','--abbrev-ref','HEAD'], text=True).strip()

# This integration uses its own exact PR inventory; Laya owners and schema are unchanged.
if current_branch() == 'feature/pulse-services-cutover-laya-20260930':
    subprocess.run([sys.executable, str(ROOT/'tests/pulse-services-release/scope.py')], cwd=ROOT, check=True)
    for path in ['database/migrations/125_automatic_document_admission_laya.sql',
                 'src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaWorkerLease.cs',
                 'src/backend/ProjectTime.Api/Ai/PulseAiDocumentIndexAuthorization.cs']:
        expected = subprocess.check_output(['git','-C',str(ROOT),'show','c8ac122653b2d948343727815dfc3279d98c9cc6:'+path])
        if (ROOT/path).read_bytes() != expected:
            raise SystemExit('Document integration changed inherited Laya authority: '+path)
    print('LAYA_PULSE_SERVICES_OWNER_SCOPE=PASS; native_receipt_tests_required=true')
    raise SystemExit(0)
if current_branch() == 'feature/pulse-documents-integration-20260930':
    subprocess.run([sys.executable, str(ROOT/'tests/pulse-document-integration/scope.py')], cwd=ROOT, check=True)
    for path in ['database/migrations/125_automatic_document_admission_laya.sql',
                 'src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaWorkerLease.cs',
                 'src/backend/ProjectTime.Api/Ai/PulseAiDocumentIndexAuthorization.cs']:
        expected = subprocess.check_output(['git','-C',str(ROOT),'show','bb2c9cccaf4d95cbfe32818e3f55ccb58cf029dc:'+path])
        if (ROOT/path).read_bytes() != expected:
            raise SystemExit('Document integration changed inherited Laya authority: '+path)
    print('LAYA_DOCUMENT_INTEGRATION_INHERITED_SOURCE=PASS; native_receipt_tests_required=true')
    raise SystemExit(0)

# The security inventory includes no Laya authority or schema changes.
if current_branch() == 'fix/security-complete-20260929':
    subprocess.run([sys.executable, str(ROOT/'tests/security-completion/scope.py')], cwd=ROOT, check=True)
    for path in ['database/migrations/125_automatic_document_admission_laya.sql',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs']:
        expected = subprocess.check_output(['git','-C',str(ROOT),'show','e01f559e0bb7df91e233fd885c837f666802cb98:'+path])
        if (ROOT/path).read_bytes() != expected:
            raise SystemExit('Security repair changed inherited Laya authority: '+path)
    print('LAYA_SECURITY_INHERITED_SOURCE=PASS')
    raise SystemExit(0)

if current_branch() == 'fix/security-team-findings-20260928':
    subprocess.run([sys.executable, str(ROOT/'tests/security-release/scope.py')], cwd=ROOT, check=True)
    for path in ['database/migrations/125_automatic_document_admission_laya.sql',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs']:
        expected = subprocess.check_output(['git','-C',str(ROOT),'show','a562371a0bbed74e881c40a4c246928dac9ec72e:'+path])
        if (ROOT/path).read_bytes() != expected:
            raise SystemExit('Security repair changed inherited Laya authority: '+path)
    print('LAYA_SECURITY_INHERITED_SOURCE=PASS')
    raise SystemExit(0)

# Contracts reuse the private migration package; Laya code and migration are unchanged.
if current_branch() == 'codex/module060-approval-contract-funding':
    subprocess.run([sys.executable, str(ROOT/'tests/contracts-release/scope.py')], cwd=ROOT, check=True)
    for path in ['database/migrations/125_automatic_document_admission_laya.sql',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs']:
        expected = subprocess.check_output(['git','-C',str(ROOT),'show','6c70385d:'+path])
        if (ROOT/path).read_bytes() != expected:
            raise SystemExit('Contracts changed inherited Laya authority: '+path)
    print('LAYA_CONTRACTS_INHERITED_SOURCE=PASS')
    raise SystemExit(0)

# Only the exact reviewed notification migration packaging changes; Laya source stays frozen.
if current_branch() == 'feature/module065-email-teams-notification-parity-20260928':
    subprocess.run([sys.executable, str(ROOT/'tests/notification-parity/release_scope.py')], cwd=ROOT, check=True)
    for path in ['database/migrations/125_automatic_document_admission_laya.sql',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
                 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs']:
        expected = subprocess.check_output(['git','-C',str(ROOT),'show','3f30bf1c6b56a5fe49116834e16b4ec2c1e13ea3:'+path])
        if (ROOT/path).read_bytes() != expected:
            raise SystemExit('Notification parity changed inherited Laya authority: '+path)
    print('LAYA_NOTIFICATION_PARITY_INHERITED_SOURCE=PASS')
    raise SystemExit(0)

if current_branch() == CURRENT_FLOWHIVE_REPAIR:
    base = subprocess.check_output(['git','-C',str(ROOT),'merge-base','origin/main','HEAD'], text=True).strip()
    inherited = [
        'database/migrations/125_automatic_document_admission_laya.sql',
        'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
        'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs',
    ]
    for path in inherited:
        current = (ROOT/path).read_bytes()
        accepted = subprocess.check_output(['git','-C',str(ROOT),'show',f'{base}:{path}'])
        if current != accepted:
            raise SystemExit('FlowHive usability repair changed inherited Laya source: '+path)
    subprocess.run([sys.executable, str(ROOT/'tests/flowhive-enterprise-usability-routing-scope.py')],
                   cwd=ROOT, check=True)
    subprocess.run(['git','-C',str(ROOT),'diff','--check',base,'HEAD'], check=True)
    print('LAYA_AUTOMATIC_ADMISSION_SCOPE=PASS; inherited Laya source unchanged for FlowHive usability repair')
    raise SystemExit(0)

if current_branch() == CURRENT_FLOWHIVE_FRONTEND_MARKER:
    base = subprocess.check_output(['git','-C',str(ROOT),'merge-base','origin/main','HEAD'], text=True).strip()
    inherited = [
        'database/migrations/125_automatic_document_admission_laya.sql',
        'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
        'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs',
        'src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs',
    ]
    for path in inherited:
        current = (ROOT/path).read_bytes()
        accepted = subprocess.check_output(['git','-C',str(ROOT),'show',f'{base}:{path}'])
        if current != accepted:
            raise SystemExit('FlowHive frontend marker repair changed inherited Laya source: '+path)
    subprocess.run([sys.executable, str(ROOT/'tests/flowhive-frontend-convergence-marker-scope.py')],
                   cwd=ROOT, check=True)
    subprocess.run(['git','-C',str(ROOT),'diff','--check',base,'HEAD'], check=True)
    print('LAYA_AUTOMATIC_ADMISSION_SCOPE=PASS; inherited Laya source unchanged for FlowHive frontend marker repair')
    raise SystemExit(0)

if current_branch() == CURRENT_FLOWHIVE_UAT_IDEMPOTENT:
    base = subprocess.check_output(['git','-C',str(ROOT),'merge-base','origin/main','HEAD'], text=True).strip()
    inherited = [
        'database/migrations/125_automatic_document_admission_laya.sql',
        'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs',
        'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs',
        'src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs',
    ]
    for path in inherited:
        current = (ROOT/path).read_bytes()
        accepted = subprocess.check_output(['git','-C',str(ROOT),'show',f'{base}:{path}'])
        if current != accepted:
            raise SystemExit('FlowHive protected-UAT idempotency repair changed inherited Laya source: '+path)
    subprocess.run([sys.executable, str(ROOT/'tests/flowhive-protected-uat-idempotent-scope.py')],
                   cwd=ROOT, check=True)
    subprocess.run(['git','-C',str(ROOT),'diff','--check',base,'HEAD'], check=True)
    print('LAYA_AUTOMATIC_ADMISSION_SCOPE=PASS; inherited Laya source unchanged for FlowHive protected-UAT idempotency repair')
    raise SystemExit(0)

if current_branch() == 'codex/approval-routing-bulk-review':
    subprocess.run([sys.executable, str(ROOT/'tests/combined-release/scope.py')], cwd=ROOT, check=True)
    for path in ['database/migrations/125_automatic_document_admission_laya.sql', 'src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs', 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs', 'src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs']:
        expected = subprocess.check_output(['git', '-C', str(ROOT), 'show', '5c371854343cc8f5c26f2ae26cc30c904edc35ea:'+path])
        if (ROOT/path).read_bytes() != expected: raise SystemExit('Combined release changed inherited Laya authority: '+path)
    print('LAYA_COMBINED_RELEASE_INHERITED_SOURCE=PASS')
    raise SystemExit(0)

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

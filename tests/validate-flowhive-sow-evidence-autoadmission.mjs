import fs from 'node:fs';
import assert from 'node:assert/strict';
import {
  dedupeEvidence,
  evidenceIdentity,
  evidenceScore,
  isQueueCandidate,
  normalizeEvidenceBody,
  normalizePreparePayload,
  queueCandidatesFromEvidence,
  serverOwnedAiPlannerAdmission
} from '../src/frontend/project-time-web/src/flowhive-sow-evidence-autoadmission.js';

const source = fs.readFileSync('src/frontend/project-time-web/src/flowhive-sow-evidence-autoadmission.js', 'utf8');
const plannerOrchestration = fs.readFileSync('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs', 'utf8');
const plannerWorkspace = fs.readFileSync('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx', 'utf8');
const plannerStyles = fs.readFileSync('src/frontend/project-time-web/src/project-flowhive-center.css', 'utf8');
assert.equal(serverOwnedAiPlannerAdmission, true);
assert(!source.includes('window.fetch = async'), 'Browser-global FlowHive admission interception must remain retired.');
assert(source.includes('server-side AI Planner'), 'The server-owned admission boundary is not documented.');
assert(
  plannerOrchestration.includes('request.RetryTerminalDocumentProcessing\n            && PulseAiProtectedTestCandidatePolicy.AllowsPrivateDocumentProcessing(release)'),
  'Protected-Test terminal SOW recovery must rely on the exact-source private-processing policy.'
);
assert(
  !plannerOrchestration.includes('request.RetryTerminalDocumentProcessing\n            && release.IsCandidate'),
  'FlowHive must not add a release.IsCandidate gate that blocks governed unscoped exact-SHA Protected-Test recovery.'
);
assert.match(plannerOrchestration, /MaximumAiRouteRetries = 2/);
assert.match(
  plannerOrchestration,
  /priorRetryCount < MaximumAiRouteRetries[\s\S]*?retry \? "processing" : "needs_attention"[\s\S]*?completed: !retry/,
  'Transient private-generation failures must retry only within the bound and then become terminal needs-attention results.'
);
assert.match(
  plannerOrchestration,
  /catch \(Exception exception\)[\s\S]*?"failed",[\s\S]*?"background_generation_failed",[\s\S]*?completed: true/,
  'Unexpected background failures must persist a terminal failed result instead of leaving the run at an in-progress percentage.'
);
assert.match(
  plannerOrchestration,
  /PersistWorkingDraftAndCompleteAsync[\s\S]*?BeginTransactionAsync\(cancellationToken\)[\s\S]*?SaveWorkingCopyAsync\([\s\S]*?transaction,[\s\S]*?UpdateRunAsync\([\s\S]*?completed: true,[\s\S]*?transaction: transaction\)[\s\S]*?transaction\.CommitAsync\(cancellationToken\)/,
  'The mutable working draft and terminal run state must commit atomically so failure reporting cannot contradict persisted state.'
);
assert.match(plannerOrchestration, /status is "completed" or "completed_with_schedule_overrun" or "needs_attention" or "failed"/);
assert.match(plannerWorkspace, /AI_PLANNER_POLL_INTERVAL_MS = 1500/);
assert.match(plannerWorkspace, /AI_PLANNER_POLL_ATTEMPTS = 800/);
assert.match(plannerWorkspace, /AI Planner is still running in the governed background worker/);
assert.match(plannerWorkspace, /aria-live="polite"/);
assert.match(plannerWorkspace, /This private generation phase can take several minutes/);
assert.match(plannerWorkspace, /bounded automatic retry/);
assert.match(
  plannerStyles,
  /\.flowhive-ai-operation-progress[^\n]*background:var\(--flowhive-surface\);color:var\(--flowhive-ink\)/,
  'The AI Planner progress surface must derive its foreground and background from the active application theme.'
);
assert.doesNotMatch(
  plannerStyles,
  /@media\s*\(prefers-color-scheme:\s*dark\)/,
  'OS color preference must not override the application light/dark theme for the planner progress card.'
);

const payload = normalizePreparePayload({ approveCurrentVersion: false, correlationId: 'uat-correlation' });
assert.equal(payload.approveCurrentVersion, false);
assert.equal(payload.correlationId, 'uat-correlation');

const evidence = [
  { documentId: 'a', documentCategory: 'sow', processingStatus: 'not_requested', uploadedAt: '2026-08-18T00:00:00Z' },
  { documentId: 'a', documentCategory: 'sow', processingStatus: 'ready', authorityStatus: 'canonical', indexStatus: 'ready', citationCount: 5, scopeCitationCount: 2, readyForAiPlanner: true, uploadedAt: '2026-08-18T01:00:00Z' },
  { documentId: 'b', documentCategory: 'sow', processingStatus: 'not_requested', uploadedAt: '2026-08-19T00:00:00Z' }
];
assert.equal(evidenceIdentity(evidence[0]), 'document:a');
assert(evidenceScore(evidence[1]) > evidenceScore(evidence[0]));
assert.equal(dedupeEvidence(evidence).length, 2);
assert.equal(normalizeEvidenceBody({ sowEvidence: evidence }).sowEvidenceSummary.readyCount, 1);
assert.equal(isQueueCandidate(evidence[2]), true);
assert.deepEqual(queueCandidatesFromEvidence(evidence).map((item) => item.documentId), ['b', 'a']);
console.log('FLOWHIVE_SERVER_OWNED_SOW_ADMISSION_VALIDATION=PASS');

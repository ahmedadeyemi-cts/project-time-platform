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
assert.equal(serverOwnedAiPlannerAdmission, true);
assert(!source.includes('window.fetch = async'), 'Browser-global FlowHive admission interception must remain retired.');
assert(source.includes('server-side AI Planner'), 'The server-owned admission boundary is not documented.');

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

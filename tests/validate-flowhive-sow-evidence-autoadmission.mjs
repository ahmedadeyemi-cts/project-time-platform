import assert from 'node:assert/strict';
import {
  dedupeEvidence,
  evidenceIdentity,
  normalizeEvidenceBody,
  normalizePreparePayload,
  prepareRequestHeaderObject,
  queueCandidatesFromEvidence
} from '../src/frontend/project-time-web/src/flowhive-sow-evidence-autoadmission.js';

const queuePayload = normalizePreparePayload({
  approveCurrentVersion: false,
  approvalNote: '',
  correlationId: 'flowhive-queue-test'
});
assert.deepEqual(queuePayload, {
  approveCurrentVersion: false,
  approvalNote: null,
  correlationId: 'flowhive-queue-test'
}, 'Queue preparation must never send an empty approval-note string.');

const approvalPayload = normalizePreparePayload({
  approveCurrentVersion: true,
  approvalNote: '  Reviewed by the assigned PM.  ',
  correlationId: 'flowhive-approval-test'
});
assert.deepEqual(approvalPayload, {
  approveCurrentVersion: true,
  approvalNote: 'Reviewed by the assigned PM.',
  correlationId: 'flowhive-approval-test'
}, 'Approval preparation must preserve a trimmed governed review note.');

const prepareHeaders = prepareRequestHeaderObject(
  { 'X-Existing-Header': 'preserved' },
  'test-session-token'
);
assert.equal(prepareHeaders['x-projectpulse-module-number'], '066');
assert.equal(prepareHeaders.authorization, 'Bearer test-session-token');
assert.equal(prepareHeaders['x-projectpulse-session'], 'test-session-token');
assert.equal(prepareHeaders['content-type'], 'application/json; charset=utf-8');
assert.equal(prepareHeaders.accept, 'application/json');
assert.equal(prepareHeaders['x-existing-header'], 'preserved');

const oldReady = {
  documentId: '11111111-1111-1111-1111-111111111111',
  activeVersionId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  documentCategory: 'sow',
  originalFileName: 'Customer-SOW.doc',
  documentVersion: 'SOW-1',
  processingStatus: 'ready',
  authorityStatus: 'canonical',
  indexStatus: 'ready',
  citationCount: 14,
  scopeCitationCount: 5,
  readyForAiPlanner: true,
  uploadedAt: '2026-08-01T12:00:00Z'
};

const replacement = {
  documentId: '22222222-2222-2222-2222-222222222222',
  activeVersionId: null,
  documentCategory: 'sow',
  originalFileName: 'Customer-SOW.doc',
  documentVersion: '',
  processingStatus: 'not_requested',
  authorityStatus: '',
  indexStatus: '',
  citationCount: 0,
  scopeCitationCount: 0,
  readyForAiPlanner: false,
  uploadedAt: '2026-08-18T12:00:00Z'
};

const sameDocumentDuplicate = {
  ...replacement,
  processingStatus: 'not_started',
  uploadedAt: '2026-08-18T11:59:00Z'
};

const sameFileDifferentContent = {
  ...oldReady,
  documentId: '33333333-3333-3333-3333-333333333333',
  activeVersionId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  documentVersion: 'SOW-2',
  uploadedAt: '2026-08-18T13:00:00Z'
};

const displayedReplacementSet = dedupeEvidence([oldReady, replacement]);
assert.equal(
  displayedReplacementSet.length,
  2,
  'A replacement SOW with the same filename but a different document identity must remain visible.'
);
assert.ok(
  displayedReplacementSet.some((item) => item.documentId === replacement.documentId),
  'The replacement SOW must not be hidden by an older ready record.'
);

const replacementCandidates = queueCandidatesFromEvidence([oldReady, replacement]);
assert.deepEqual(
  replacementCandidates.map((item) => item.documentId),
  [replacement.documentId],
  'Automatic admission must queue the raw replacement SOW even when an older same-name SOW is ready.'
);

const duplicateCandidates = queueCandidatesFromEvidence([replacement, sameDocumentDuplicate]);
assert.equal(duplicateCandidates.length, 1, 'Repeated rows for one document must create only one processing request.');
assert.equal(duplicateCandidates[0].documentId, replacement.documentId);

const authoritativeDuplicateRows = dedupeEvidence([
  replacement,
  {
    ...replacement,
    processingStatus: 'queued',
    uploadedAt: '2026-08-18T12:05:00Z'
  }
]);
assert.equal(authoritativeDuplicateRows.length, 1, 'Rows for one authoritative document identity must consolidate.');
assert.equal(authoritativeDuplicateRows[0].duplicateCount, 2);
assert.deepEqual(authoritativeDuplicateRows[0].duplicateDocumentIds, [replacement.documentId]);

const differentDocuments = dedupeEvidence([oldReady, sameFileDifferentContent]);
assert.equal(
  differentDocuments.length,
  2,
  'Distinct document and version identities must never be consolidated solely because their filenames match.'
);
assert.notEqual(evidenceIdentity(oldReady), evidenceIdentity(sameFileDifferentContent));

const normalized = normalizeEvidenceBody({
  access: { canManage: true },
  sowEvidence: [oldReady, replacement, sameDocumentDuplicate]
});
assert.equal(normalized.sowEvidence.length, 2);
assert.equal(normalized.sowEvidenceSummary.candidateCount, 2);
assert.equal(normalized.sowEvidenceSummary.readyCount, 1);
assert.equal(normalized.sowEvidenceSummary.duplicateRecordsConsolidated, 1);
assert.equal(normalized.sowEvidenceSummary.approvedSowScopeReady, true);
assert.equal(normalized.sowEvidenceSummary.freshnessAuthority, 'project_scoped_server_gate');

console.log('FLOWHIVE_SOW_EVIDENCE_AUTOADMISSION_REGRESSION=PASS');

import test from 'node:test';
import assert from 'node:assert/strict';
import { processingStageLabel, processingMessage, shouldPollProcessing } from '../src/ai/laya-processing-state.js';
test('processing states do not claim classification or business approval', () => {
  assert.equal(processingStageLabel('scanning'), 'Scanning');
  assert.equal(processingStageLabel('ready'), 'Processed; source verification required');
  assert.equal(processingStageLabel('<script>'), 'Processing state unavailable');
});
test('only a verified ready result enables classification guidance', () => {
  assert.match(processingMessage({ stage:'ready' }), /verification/);
  assert.match(processingMessage({ stage:'ready', readyForClassification:true }), /Ready for Laya/);
  assert.match(processingMessage({ stage:'needs_attention', diagnosticCode:'document_source_integrity_failed' }), /no longer matches/);
});
test('quarantine and permanent failures do not continuously poll', () => {
  for (const stage of ['failed','quarantined','cancelled','needs_attention','unknown']) assert.equal(shouldPollProcessing({stage}),false);
  for (const stage of ['queued','scanning','extracting','indexing']) assert.equal(shouldPollProcessing({stage}),true);
  assert.equal(shouldPollProcessing({stage:'scanning',readyForClassification:true}),false);
});
test('unrecognized backend messages do not become raw UI content', () => {
  assert(!processingMessage({stage:'queued',diagnosticCode:'private_secret_value'}).includes('private_secret_value'));
});

import test from 'node:test';
import assert from 'node:assert/strict';
import { formatGenerationProgress, formatGenerationFailure, generationConfidence } from '../src/frontend/project-time-web/src/module025/generation-feedback.js';

test('progress identifies current phase, actual provider, saved phases and elapsed time', () => {
  const text = formatGenerationProgress({ phase: 'generating', completedPhases: ['Plan', 'Plan', 'Design'],
    currentPhase: 'Implement', currentProvider: 'claude', queuedAt: '2026-09-17T20:00:00Z' }, Date.parse('2026-09-17T20:03:15Z'));
  for (const part of ['2/5 phases saved', 'Phase: Implement', 'Provider: Claude', 'Elapsed: 3m 15s', 'previously saved version']) assert.ok(text.includes(part));
});
test('terminal failure explains stale content and exposes safe provider diagnostics', () => {
  const text = formatGenerationFailure({ currentPhase: 'Plan', currentProvider: 'celar_ai', diagnosticCode: 'private_model_timeout',
    message: 'The saved draft was not changed.', targetDecisions: [{ Target: 'claude', ReasonCode: 'unsupported_contract' }] });
  for (const part of ['previous saved result', 'Phase: Plan', 'Provider: Celar AI', 'private_model_timeout', 'Claude: unsupported_contract']) assert.ok(text.includes(part));
  assert.ok(!formatGenerationFailure({ diagnosticCode: 'secret@example.invalid', targetDecisions: [{ target: 'claude', reasonCode: 'secret@example.invalid' }] }).includes('example.invalid'));
});
test('legacy metadata never labels existing generated content as not generated', () => {
  assert.equal(generationConfidence({ lastGeneratedAt: '2026-09-03', aiMetadata: { Confidence: .8 } }), '80%');
  assert.equal(generationConfidence({ lastGeneratedAt: '2026-09-03' }), 'Not recorded for this saved scope');
  assert.equal(generationConfidence({ aiMetadata: { confidence: 0 } }), '0%');
  assert.equal(generationConfidence({}), 'Not generated');
});

test('an explicitly incompatible checkpoint never promises phase reuse', () => {
  const text = formatGenerationFailure({ diagnosticCode: 'provider_deadline_exceeded', canResume: false });
  assert.match(text, /not eligible for reuse/);
  assert.doesNotMatch(text, /will be reused/);
});

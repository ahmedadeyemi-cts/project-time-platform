import assert from 'node:assert/strict';
import test from 'node:test';
import { formatGenerationFailure, formatGenerationProgress } from '../../src/frontend/project-time-web/src/module025/generation-feedback.js';

test('every provider decision is visible in the supplied saved order', () => {
  const targets = ['gemini','claude','openai','deepseek_v4','celar_ai','copilot_studio','local_template'];
  const result = formatGenerationFailure({ targetDecisions: targets.map(target => ({target, outcome:'skipped', reasonCode:'synthetic_unavailable'})) });
  for (const label of ['Gemini','Claude','OpenAI','DeepSeek','Celar AI','Microsoft Copilot Studio','Governed local template']) assert.ok(result.includes(label), label);
  assert.ok(result.indexOf('Gemini') < result.indexOf('Claude'));
});
test('full-text approval blocker is distinct from a provider failure', () => {
  const result = formatGenerationFailure({ targetDecisions: [{target:'gemini',outcome:'skipped',reasonCode:'module025_full_service_scope_approval_required'}] });
  assert.match(result, /Module 064/);
  assert.match(result, /[Ff]ull/);
  assert.match(result, /[Ss]kipped/);
});
test('unrecognized diagnostic content is not displayed as a code', () => {
  const result = formatGenerationFailure({ diagnosticCode:'source text with private details',targetDecisions:[{target:'gemini',reasonCode:'not/a/closed/code'}] });
  assert.ok(!result.includes('source text with private details'));
  assert.ok(!result.includes('not/a/closed/code'));
});
test('Gemini is named during progress, not hidden behind Celar branding', () => {
  assert.match(formatGenerationProgress({currentProvider:'gemini', currentPhase:'Plan',completedPhases:[]}), /Gemini/);
});

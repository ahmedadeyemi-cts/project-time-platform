import assert from 'node:assert/strict';
import fs from 'node:fs';
import { formatGenerationFailure, formatGenerationProgress } from '../src/frontend/project-time-web/src/module025/generation-feedback.js';

const progress = formatGenerationProgress({
  phase:'generating', currentPhase:'Plan', currentProvider:'celar_ai',
  completedPhases:[], queuedAt:new Date(Date.now()-65_000).toISOString()
}, Date.now());
assert.match(progress,/Elapsed: 1m 5s/);
assert.match(progress,/Provider: Celar AI/);

const failure = formatGenerationFailure({
  currentPhase:'Plan', currentProvider:'celar_ai', elapsedSeconds:241,
  diagnosticCode:'provider_deadline_exceeded',
  targetDecisions:[
    {target:'deepseek_v4',reasonCode:'provider_deadline_exceeded'},
    {target:'celar_ai',reasonCode:'provider_deadline_exceeded'},
    {target:'claude',reasonCode:'structured_sow_adapter_unavailable'},
    {target:'openai',reasonCode:'structured_sow_adapter_unavailable'}
  ],
  message:'The saved draft was preserved.'
});
assert.match(failure,/Elapsed: 4m 1s/);
assert.match(failure,/privacy-safe structured cloud fallback was eligible/);
assert.match(failure,/completed phase checkpoints will be reused/i);

const editor=fs.readFileSync('src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx','utf8');
assert.match(editor,/window\.confirm\(\`Delete draft/);
assert.match(editor,/Review Requirements to Confirm/);
assert.match(editor,/SA Final LOE is greater than 0 hours/);
assert.match(editor,/Running · \{formatDuration\(generationElapsedSeconds\)\}/);

console.log('MODULE025_REVIEW_TIMING_DELETE_BEHAVIOR=PASS');

const phases = ['Plan', 'Design', 'Implement', 'Validate', 'Release', 'assembly'];
const providers = { claude: 'Claude', openai: 'OpenAI', deepseek_v4: 'DeepSeek', celar_ai: 'Celar AI' };
const code = value => typeof value === 'string' && /^[a-z][a-z0-9_]{0,159}$/i.test(value) ? value : '';

function position(progress) {
  const phase = phases.find(item => item.toLowerCase() === String(progress?.currentPhase || '').toLowerCase());
  const provider = providers[progress?.currentProvider];
  return [phase ? `Phase: ${phase === 'assembly' ? 'document assembly' : phase}.` : '', provider ? `Provider: ${provider}.` : ''].filter(Boolean).join(' ');
}

export function formatGenerationProgress(progress, now = Date.now()) {
  const completed = new Set((progress?.completedPhases || []).filter(phase => phases.slice(0, 5).includes(phase))).size;
  const queued = Date.parse(progress?.queuedAt || '');
  const elapsed = Number.isFinite(queued) ? Math.max(0, Math.floor((now - queued) / 1000)) : null;
  return [progress?.phase === 'queued' ? 'Waiting to start.' : 'Generating detailed scope.',
    `${completed}/5 phases saved.`, position(progress),
    elapsed === null ? '' : `Elapsed: ${Math.floor(elapsed / 60)}m ${elapsed % 60}s.`,
    'The scope below remains the previously saved version until all five phases finish.'].filter(Boolean).join(' ');
}

export function formatGenerationFailure(payload) {
  const decisions = (payload?.targetDecisions || []).map(decision => {
    const provider = providers[decision.target ?? decision.Target];
    const reason = code(decision.reasonCode ?? decision.ReasonCode);
    return provider && reason ? `${provider}: ${reason}` : '';
  }).filter(Boolean);
  const diagnostic = code(payload?.diagnosticCode);
  return ['Generation did not complete. The scope and hours below are the previous saved result.',
    position(payload), diagnostic ? `Diagnostic: ${diagnostic}.` : '',
    decisions.length ? `Provider results: ${decisions.join('; ')}.` : '',
    payload?.message || 'The saved draft was preserved.'].filter(Boolean).join(' ');
}

export function generationConfidence(engagement) {
  const value = engagement?.aiMetadata?.confidence ?? engagement?.aiMetadata?.Confidence;
  return value === null || value === undefined
    ? engagement?.lastGeneratedAt ? 'Not recorded for this saved scope' : 'Not generated'
    : `${Math.round(Number(value) * 100)}%`;
}

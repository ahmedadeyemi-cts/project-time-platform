const phases = ['Plan', 'Design', 'Implement', 'Validate', 'Release', 'assembly'];
const providers = { gemini: 'Gemini', claude: 'Claude', openai: 'OpenAI', deepseek_v4: 'DeepSeek', celar_ai: 'Celar AI', copilot_studio: 'Microsoft Copilot Studio', local_template: 'Governed local template' };
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
  const decisionRows = (payload?.targetDecisions || []).map(decision => ({
    provider: providers[decision.target ?? decision.Target] || '',
    reason: code(decision.reasonCode ?? decision.ReasonCode),
    outcome: ({ skipped: 'skipped', failed: 'failed', used: 'completed', refused: 'refused' })[decision.outcome ?? decision.Outcome] || 'reported'
  })).filter(item => item.provider && item.reason);
  const decisions = decisionRows.map(item => `${item.provider} (${item.outcome}): ${item.reason}`);
  const diagnostic = code(payload?.diagnosticCode);
  const elapsed = Number(payload?.elapsedSeconds);
  const elapsedText = Number.isFinite(elapsed)
    ? `Elapsed: ${Math.floor(elapsed / 60)}m ${Math.max(0, Math.floor(elapsed % 60))}s.`
    : '';
  const deadlineHit = decisionRows.some(item => item.reason === 'provider_deadline_exceeded')
    || diagnostic === 'provider_deadline_exceeded';
  const adapterUnavailable = decisionRows.some(item => item.reason === 'structured_sow_adapter_unavailable');
  const recovery = payload?.canResume === false
    ? 'Review the saved scope before retrying. Earlier phase checkpoints are not eligible for reuse.'
    : 'Any completed phase checkpoints will be reused.';
  const fullTextApprovalRequired = decisionRows.some(item => item.reason === 'module025_full_service_scope_approval_required');
  const recommendation = fullTextApprovalRequired
    ? 'Full Service Scope submission requires its separate approval in Module 064. Sanitized-generation approval does not authorize full-text submission. Your saved provider order is unchanged.'
    : deadlineHit && adapterUnavailable
    ? `Private providers reached their bounded deadline and no privacy-safe structured cloud fallback was eligible. Verify that Customer is selected and the Service Overview clearly names the technology and requested operation, then retry. ${recovery}`
    : deadlineHit
      ? `A provider reached its bounded deadline. ${recovery}`
      : adapterUnavailable
        ? 'The privacy-safe structured cloud fallback was not eligible for this Service Overview. Clearly identify the technology and requested operation, then retry.'
        : '';
  return ['Generation did not complete. The scope and hours below are the previous saved result.',
    position(payload), elapsedText, diagnostic ? `Diagnostic: ${diagnostic}.` : '',
    decisions.length ? `Provider results: ${decisions.join('; ')}.` : '',
    recommendation,
    payload?.message || 'The saved draft was preserved.'].filter(Boolean).join(' ');
}

export function generationConfidence(engagement) {
  const value = engagement?.aiMetadata?.confidence ?? engagement?.aiMetadata?.Confidence;
  return value === null || value === undefined
    ? engagement?.lastGeneratedAt ? 'Not recorded for this saved scope' : 'Not generated'
    : `${Math.round(Number(value) * 100)}%`;
}

export const HELP_DETAIL_LEVELS = Object.freeze([
  Object.freeze({ value: 'concise', label: 'Concise' }),
  Object.freeze({ value: 'standard', label: 'Standard' }),
  Object.freeze({ value: 'detailed', label: 'Detailed' }),
  Object.freeze({ value: 'highly_detailed', label: 'Highly detailed' }),
  Object.freeze({ value: 'technical', label: 'Technical' }),
  Object.freeze({ value: 'executive', label: 'Executive' }),
  Object.freeze({ value: 'step_by_step', label: 'Step-by-step' })
]);

export const DEFAULT_HELP_ANSWER_PREFERENCES = Object.freeze({
  detailLevel: 'standard',
  includeRepositoryContext: false,
  includeAssumptions: true,
  includeSourceCitations: true
});

function userKey() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    const identity = session?.userId ?? session?.username ?? session?.email ?? 'authenticated-user';
    return `projectPulse.helpAnswerPreferences.v1.${String(identity).toLowerCase()}`;
  } catch {
    return 'projectPulse.helpAnswerPreferences.v1.authenticated-user';
  }
}

function normalize(value = {}) {
  const detailLevel = HELP_DETAIL_LEVELS.some((choice) => choice.value === value.detailLevel)
    ? value.detailLevel
    : DEFAULT_HELP_ANSWER_PREFERENCES.detailLevel;
  return {
    detailLevel,
    includeRepositoryContext: Boolean(value.includeRepositoryContext),
    includeAssumptions: value.includeAssumptions !== false,
    includeSourceCitations: value.includeSourceCitations !== false
  };
}

export function loadHelpAnswerPreferences() {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(userKey()) || 'null');
    return normalize(parsed ?? DEFAULT_HELP_ANSWER_PREFERENCES);
  } catch {
    return { ...DEFAULT_HELP_ANSWER_PREFERENCES };
  }
}

export function saveHelpAnswerPreferences(next) {
  const normalized = normalize(next);
  window.localStorage.setItem(userKey(), JSON.stringify(normalized));
  window.dispatchEvent(new CustomEvent('projectpulse:help-answer-preferences-changed', { detail: normalized }));
  return normalized;
}

export function queryPreferenceOverrides(question = '') {
  const normalized = question.toLowerCase();
  const detailLevel = normalized.includes('/concise') || normalized.includes('be concise')
    ? 'concise'
    : normalized.includes('/highly-detailed') || normalized.includes('highly detailed')
      ? 'highly_detailed'
      : normalized.includes('/technical') || normalized.includes('technical detail')
        ? 'technical'
        : normalized.includes('/executive') || normalized.includes('executive summary')
          ? 'executive'
          : normalized.includes('/step-by-step') || normalized.includes('step by step')
            ? 'step_by_step'
            : normalized.includes('/detailed') || normalized.includes('detailed answer')
              ? 'detailed'
              : null;
  return {
    ...(detailLevel ? { detailLevel } : {}),
    ...(normalized.includes('include repository context') ? { includeRepositoryContext: true } : {}),
    ...(normalized.includes('exclude repository context') ? { includeRepositoryContext: false } : {}),
    ...(normalized.includes('include assumptions') ? { includeAssumptions: true } : {}),
    ...(normalized.includes('exclude assumptions') ? { includeAssumptions: false } : {}),
    ...(normalized.includes('include source citations') || normalized.includes('cite sources') ? { includeSourceCitations: true } : {}),
    ...(normalized.includes('exclude source citations') || normalized.includes('no citations') ? { includeSourceCitations: false } : {})
  };
}

export function effectiveHelpAnswerPreferences(question = '') {
  const saved = loadHelpAnswerPreferences();
  const overrides = queryPreferenceOverrides(question);
  return {
    ...saved,
    ...overrides,
    preferenceSource: Object.keys(overrides).length ? 'query_override' : 'saved_preference'
  };
}

export function applyHelpAnswerPreferences(url, question = '') {
  const preferences = effectiveHelpAnswerPreferences(question);
  url.searchParams.set('answerDetail', preferences.detailLevel);
  url.searchParams.set('includeRepositoryContext', String(preferences.includeRepositoryContext));
  url.searchParams.set('includeAssumptions', String(preferences.includeAssumptions));
  url.searchParams.set('includeSourceCitations', String(preferences.includeSourceCitations));
  url.searchParams.set('answerPreferenceSource', preferences.preferenceSource);
  return preferences;
}

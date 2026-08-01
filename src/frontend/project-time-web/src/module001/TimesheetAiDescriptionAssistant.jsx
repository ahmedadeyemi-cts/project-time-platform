import { useMemo, useState } from 'react';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';

const PROVIDER_LABELS = Object.freeze({
  claude: 'Claude',
  openai: 'OpenAI',
  local: 'Private ProjectPulse grounding',
  local_template: 'Governed local template fallback'
});

function localIsoDate() {
  const now = new Date();
  const offset = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offset).toISOString().slice(0, 10);
}

function targetLabel(target) {
  return target?.selectionLabel
    || [target?.customerName, target?.projectCode, target?.taskName || target?.nonProjectCategoryName]
      .filter(Boolean)
      .join(' · ')
    || 'Authorized activity';
}

function isNonProject(target) {
  return target?.targetType === 'category'
    || Boolean(target?.nonProjectCategoryId)
    || Boolean(target?.nonProjectCategoryCode);
}

function isServiceRequest(target) {
  const text = [
    target?.groupLabel,
    target?.workTaskCategory,
    target?.workType,
    target?.serviceRequestNumber,
    target?.requestNumber
  ].filter(Boolean).join(' ').toLowerCase();
  return Boolean(target?.serviceRequestNumber || target?.requestNumber)
    || /service[ _-]?request|request/.test(text);
}

function rowType(target) {
  if (isNonProject(target)) return 'non_project';
  if (isServiceRequest(target)) return 'service_request';
  return 'project_task';
}

export default function TimesheetAiDescriptionAssistant({
  targets = [],
  classification = 'normal',
  value = '',
  disabled = false,
  compact = false,
  onApply = () => {}
}) {
  const [state, setState] = useState({
    loading: false,
    suggestion: '',
    provider: '',
    warning: '',
    error: ''
  });

  const primaryTarget = targets[0] || {};
  const combinedLabel = useMemo(
    () => targets.map(targetLabel).filter(Boolean).join('; '),
    [targets]
  );
  const roughNoteReady = String(value || '').trim().length >= 4;
  const oneTargetSelected = targets.length === 1;
  const projectAware = oneTargetSelected && !isNonProject(primaryTarget);

  async function generateSuggestion() {
    if (!oneTargetSelected) {
      setState({
        loading: false,
        suggestion: '',
        provider: '',
        warning: '',
        error: targets.length === 0
          ? 'Select one project task, service request, or non-project activity before generating a suggestion.'
          : 'For accurate project-document grounding, generate the description for one selected activity at a time. You can start all selected timers together, then generate a separate SOW/GSD-grounded description inside each running timer.'
      });
      return;
    }

    if (!roughNoteReady) {
      setState({
        loading: false,
        suggestion: '',
        provider: '',
        warning: '',
        error: 'Type a short rough work note first so the suggestion remains grounded in work you actually performed.'
      });
      return;
    }

    setState({ loading: true, suggestion: '', provider: '', warning: '', error: '' });
    try {
      const result = await authoritativeApi('/api/timesheets/ai-description-suggestions', {
        method: 'POST',
        moduleNumber: '001',
        body: JSON.stringify({
          workDate: localIsoDate(),
          timeType: classification,
          rowType: rowType(primaryTarget),
          rowLabel: combinedLabel,
          projectName: primaryTarget.projectName || '',
          projectCode: primaryTarget.projectCode || '',
          taskName: primaryTarget.taskName || primaryTarget.categoryName || primaryTarget.nonProjectCategoryName || '',
          taskCode: primaryTarget.taskCode || '',
          categoryCode: primaryTarget.categoryCode || primaryTarget.nonProjectCategoryCode || '',
          hours: null,
          currentDescription: value
        })
      });

      setState({
        loading: false,
        suggestion: result.suggestion || '',
        provider: result.provider || '',
        warning: result.warning || '',
        error: ''
      });
    } catch (error) {
      setState({
        loading: false,
        suggestion: '',
        provider: '',
        warning: '',
        error: error instanceof Error ? error.message : 'Unable to generate an AI description suggestion.'
      });
    }
  }

  function applySuggestion() {
    if (!state.suggestion || disabled) return;
    onApply(state.suggestion);
    setState((current) => ({
      ...current,
      warning: [current.warning, 'Suggestion applied. Review it before stopping the timer.']
        .filter(Boolean)
        .join(' ')
    }));
  }

  return (
    <section className={`module001-ai-description-assistant ${compact ? 'is-compact' : ''}`} aria-label="AI description assistant">
      <div className="module001-ai-description-copy">
        <p className="eyebrow">AI description assistant</p>
        <strong>Generate a customer-facing description</strong>
        {!compact ? (
          <span>
            {projectAware
              ? 'ProjectPulse first checks the authorized documents attached to this project or service request—including SOW, GSD, design or architecture files, orders, proposals or quotes, and supporting documents—then combines that context with your rough note. Restricted documents remain permission scoped.'
              : 'Enter a rough note using your own words. AI can suggest only the description; it cannot change hours, stop timers, submit time, create tasks, or modify allocations.'}
          </span>
        ) : projectAware ? (
          <span>Uses authorized project and service-request documents before suggesting wording.</span>
        ) : null}
      </div>

      <div className="module001-ai-description-actions">
        <button
          type="button"
          className="secondary-action"
          disabled={disabled || state.loading}
          onClick={generateSuggestion}
        >
          {state.loading ? 'Checking project documents…' : 'Generate AI suggestion'}
        </button>
        {state.suggestion ? (
          <button type="button" className="primary-action" disabled={disabled} onClick={applySuggestion}>
            Use suggestion
          </button>
        ) : null}
      </div>

      {state.error ? <p className="error-text">{state.error}</p> : null}
      {state.warning ? <p className="module001-ai-description-warning">{state.warning}</p> : null}
      {state.suggestion ? (
        <div className="module001-ai-description-preview">
          <strong>Suggested description</strong>
          <p>{state.suggestion}</p>
          <small>Provider: {PROVIDER_LABELS[state.provider] || 'Shared AI router'}</small>
        </div>
      ) : null}
    </section>
  );
}

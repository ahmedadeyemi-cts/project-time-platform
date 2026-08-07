import { useMemo, useState } from 'react';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import './timesheet-ai-description-assistant.css';

const PROVIDER_LABELS = Object.freeze({
  celar_ai: 'Celar AI',
  claude: 'Claude',
  openai: 'OpenAI',
  local: 'Private ProjectPulse grounding',
  local_template: 'Governed local template fallback'
});

const ROUTE_REASON_LABELS = Object.freeze({
  generation_succeeded: 'Completed',
  private_model_completed: 'Completed with private evidence',
  private_context_withheld_from_external_route: 'Private evidence stayed inside Celar AI',
  celar_ai_private_model_not_configured: 'Private model is not configured',
  celar_ai_private_model_disabled: 'Private model is disabled',
  provider_not_registered: 'Provider is not configured',
  provider_circuit_open: 'Provider is temporarily unavailable',
  sanitized_external_policy_disabled: 'Sanitized fallback is disabled',
  sanitized_external_request_blocked: 'The privacy gate blocked this route',
  sanitized_external_problem_ready: 'Completed with a sanitized work note',
  sanitized_external_problem_ready_after_deidentification: 'Completed after de-identifying the work note',
  private_document_pipeline_not_ready: 'Private document processing is not ready',
  external_output_identity_validation_failed: 'The response failed identity-safety validation',
  external_output_privacy_validation_failed: 'The response failed privacy validation',
  local_fallback: 'Used the mandatory governed fallback'
});

function routeDecisionLabel(decision) {
  return ROUTE_REASON_LABELS[decision?.reasonCode]
    || String(decision?.reasonCode || decision?.outcome || 'not attempted').replaceAll('_', ' ');
}

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
  const targetType = String(target?.targetType || '')
    .trim()
    .toLowerCase()
    .replaceAll('-', '_')
    .replaceAll(' ', '_');
  return targetType === 'category'
    || targetType === 'categorycode'
    || targetType === 'category_code'
    || targetType === 'nonproject'
    || targetType === 'non_project'
    || Boolean(target?.nonProjectTimeCategoryId)
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
    targetDecisions: [],
    warning: '',
    error: ''
  });

  const primaryTarget = targets[0] || {};
  const combinedLabel = useMemo(
    () => targets.map(targetLabel).filter(Boolean).join('; '),
    [targets]
  );
  const roughNote = String(value || '');
  const roughNoteReady = roughNote.replace(/\s/g, '').length >= 12
    && (roughNote.match(/[\p{L}\p{N}]/gu) || []).length >= 8;
  const roughNoteWithinLimit = roughNote.length <= 4000;
  const oneTargetSelected = targets.length === 1;
  const projectAware = oneTargetSelected && !isNonProject(primaryTarget);

  async function generateSuggestion() {
    if (!oneTargetSelected) {
      setState({
        loading: false,
        suggestion: '',
        provider: '',
        targetDecisions: [],
        warning: '',
        error: targets.length === 0
          ? 'Select one project task, service request, or non-project activity before generating a suggestion.'
          : 'For accurate project-document grounding, generate the description for one selected activity at a time. You can start all selected timers together, then generate a separate SOW/GSD-grounded description inside each running timer.'
      });
      return;
    }

    if (!roughNoteWithinLimit) {
      setState({
        loading: false,
        suggestion: '',
        provider: '',
        targetDecisions: [],
        warning: '',
        error: 'Keep the rough work note at 4,000 characters or fewer before generating a suggestion.'
      });
      return;
    }

    if (!roughNoteReady) {
      setState({
        loading: false,
        suggestion: '',
        provider: '',
        targetDecisions: [],
        warning: '',
        error: 'Type a short rough work note first so the suggestion remains grounded in work you actually performed.'
      });
      return;
    }

    setState({ loading: true, suggestion: '', provider: '', targetDecisions: [], warning: '', error: '' });
    try {
      const nonProject = isNonProject(primaryTarget);
      const targetType = String(primaryTarget.targetType || '').trim().toLowerCase();
      const assignmentId = primaryTarget.assignmentId
        || primaryTarget.projectAssignmentId
        || (targetType === 'assignment' ? primaryTarget.targetId : null);
      const nonProjectTimeCategoryId = primaryTarget.nonProjectTimeCategoryId
        || primaryTarget.nonProjectCategoryId
        || (targetType === 'category' ? primaryTarget.targetId : null);
      const result = await authoritativeApi('/api/timesheets/ai-description-suggestions', {
        method: 'POST',
        moduleNumber: '001',
        body: JSON.stringify({
          workDate: localIsoDate(),
          timeType: classification,
          rowType: rowType(primaryTarget),
          rowLabel: combinedLabel,
          assignmentId: nonProject ? null : (assignmentId || null),
          projectId: nonProject ? null : (primaryTarget.projectId || null),
          projectName: nonProject ? '' : (primaryTarget.projectName || ''),
          projectCode: nonProject ? '' : (primaryTarget.projectCode || ''),
          taskId: nonProject ? null : (primaryTarget.taskId || null),
          taskName: primaryTarget.taskName || primaryTarget.categoryName || primaryTarget.nonProjectCategoryName || '',
          taskCode: primaryTarget.taskCode || '',
          nonProjectTimeCategoryId: nonProject ? (nonProjectTimeCategoryId || null) : null,
          categoryCode: nonProject ? (primaryTarget.categoryCode || primaryTarget.nonProjectCategoryCode || primaryTarget.targetCode || '') : '',
          hours: null,
          currentDescription: value
        })
      });

      setState({
        loading: false,
        suggestion: result.suggestion || '',
        provider: result.provider || '',
        targetDecisions: Array.isArray(result.targetDecisions) ? result.targetDecisions : [],
        warning: result.warning || '',
        error: result.suggestion
          ? ''
          : (result.message || 'No configured AI target completed this request. Review the route details and try again.')
      });
    } catch (error) {
      setState({
        loading: false,
        suggestion: '',
        provider: '',
        targetDecisions: [],
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
          {state.targetDecisions.length ? (
            <details className="module001-ai-route-trace">
              <summary>AI route details</summary>
              <ol>
                {state.targetDecisions.map((decision, index) => (
                  <li key={`${decision.target || 'target'}-${index}`}>
                    <strong>{PROVIDER_LABELS[decision.target] || decision.target || 'AI target'}</strong>
                    <span>{routeDecisionLabel(decision)}</span>
                  </li>
                ))}
              </ol>
            </details>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

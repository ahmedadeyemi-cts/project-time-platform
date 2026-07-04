import { useMemo, useState } from 'react';

const STORAGE_KEY = 'projectPulseProductionDataRemediationChecklist';

function normalizeStatus(status) {
  const value = String(status || '').toLowerCase();
  if (value.includes('ready')) return 'ready';
  if (value.includes('missing')) return 'missing';
  if (value.includes('need')) return 'review';
  return value || 'unknown';
}

function readStoredState() {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return {
        checked: {},
        notes: '',
        lastUpdated: null,
        copyStatus: ''
      };
    }

    const parsed = JSON.parse(raw);
    return {
      checked: parsed?.checked || {},
      notes: parsed?.notes || '',
      lastUpdated: parsed?.lastUpdated || null,
      copyStatus: ''
    };
  } catch {
    return {
      checked: {},
      notes: '',
      lastUpdated: null,
      copyStatus: ''
    };
  }
}

function writeStoredState(nextState) {
  window.localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      checked: nextState.checked || {},
      notes: nextState.notes || '',
      lastUpdated: new Date().toISOString()
    })
  );
}

function buildRemediationPlan(items, state) {
  const completed = items.filter((item) => state.checked[item.key]);
  const remaining = items.filter((item) => !state.checked[item.key]);

  return [
    'Project Health Dashboard - Production Data Remediation Plan',
    `Generated: ${new Date().toLocaleString()}`,
    '',
    `Completed: ${completed.length} of ${items.length}`,
    '',
    'Remaining items:',
    ...(remaining.length
      ? remaining.map((item) => `- ${item.label}: ${item.webpageCheck || item.purpose || 'Review data readiness row.'}`)
      : ['- None']),
    '',
    'Completed items:',
    ...(completed.length ? completed.map((item) => `- ${item.label}`) : ['- None']),
    '',
    'Notes:',
    state.notes || 'No notes captured.'
  ].join('\n');
}

export default function ProductionDataReadinessRemediationPanel({ checks = [], loading = false }) {
  const [state, setState] = useState(readStoredState);

  const remediationItems = useMemo(() => {
    return checks
      .filter((check) => normalizeStatus(check.status) !== 'ready')
      .map((check) => ({
        key: check.key || check.tableName || check.label,
        label: check.label || check.key || 'Data readiness item',
        tableName: check.tableName,
        status: check.status,
        purpose: check.purpose,
        webpageCheck: check.webpageCheck,
        count: check.count,
        readyMinimum: check.readyMinimum
      }));
  }, [checks]);

  const planText = useMemo(() => buildRemediationPlan(remediationItems, state), [remediationItems, state]);

  const completedCount = remediationItems.filter((item) => state.checked[item.key]).length;
  const allComplete = remediationItems.length > 0 && completedCount === remediationItems.length;

  function toggleItem(key) {
    setState((current) => {
      const nextState = {
        ...current,
        checked: {
          ...current.checked,
          [key]: !current.checked[key]
        },
        lastUpdated: new Date().toISOString(),
        copyStatus: ''
      };

      writeStoredState(nextState);
      return nextState;
    });
  }

  function updateNotes(value) {
    setState((current) => {
      const nextState = {
        ...current,
        notes: value,
        lastUpdated: new Date().toISOString(),
        copyStatus: ''
      };

      writeStoredState(nextState);
      return nextState;
    });
  }

  async function copyPlan() {
    try {
      await navigator.clipboard.writeText(planText);
      setState((current) => ({
        ...current,
        copyStatus: 'Copied remediation plan to clipboard.'
      }));
    } catch {
      setState((current) => ({
        ...current,
        copyStatus: 'Clipboard copy was blocked. Use the text box below to copy manually.'
      }));
    }
  }

  function resetChecklist() {
    const nextState = {
      checked: {},
      notes: '',
      lastUpdated: new Date().toISOString(),
      copyStatus: ''
    };

    writeStoredState(nextState);
    setState(nextState);
  }

  return (
    <section className="production-data-panel production-data-remediation-panel">
      <div className="production-data-panel-heading remediation-heading">
        <div>
          <p className="eyebrow">Data remediation</p>
          <h2>Production data remediation checklist</h2>
          <p>
            Use this checklist to track missing or incomplete production data before go-live. It only shows
            rows that are not ready from the backend data-readiness check.
          </p>
        </div>

        <div className={allComplete ? 'remediation-badge ready' : 'remediation-badge pending'}>
          <strong>{allComplete ? 'Ready' : loading ? 'Loading' : 'Needs Review'}</strong>
          <span>{completedCount} of {remediationItems.length} complete</span>
        </div>
      </div>

      {loading ? (
        <div className="manager-empty-state">Loading remediation checklist...</div>
      ) : remediationItems.length === 0 ? (
        <div className="production-data-remediation-ready">
          <strong>No remediation items found.</strong>
          <span>All returned production data readiness rows are currently marked ready.</span>
        </div>
      ) : (
        <div className="remediation-list">
          {remediationItems.map((item) => (
            <label className={state.checked[item.key] ? 'remediation-item complete' : 'remediation-item'} key={item.key}>
              <input
                type="checkbox"
                checked={Boolean(state.checked[item.key])}
                onChange={() => toggleItem(item.key)}
              />
              <span>
                <strong>{item.label}</strong>
                <small>
                  Backend: <code>{item.tableName}</code> | Status: {item.status} | Count: {item.count ?? 0} / Minimum: {item.readyMinimum ?? 1}
                </small>
                <small>{item.webpageCheck || item.purpose}</small>
              </span>
            </label>
          ))}
        </div>
      )}

      <label className="remediation-notes">
        Data readiness notes
        <textarea
          value={state.notes}
          placeholder="Example: Users and roles are ready. Projects and project tasks still need production records before go-live."
          onChange={(event) => updateNotes(event.target.value)}
        />
      </label>

      <div className="remediation-actions">
        <button type="button" className="secondary-action" onClick={copyPlan}>
          Copy remediation plan
        </button>
        <button type="button" className="secondary-action" onClick={resetChecklist}>
          Reset checklist
        </button>
        <span>{state.copyStatus || (state.lastUpdated ? `Last updated ${new Date(state.lastUpdated).toLocaleString()}` : 'Not started')}</span>
      </div>

      <details className="data-readiness-copy-details">
        <summary>Show remediation plan text</summary>
        <textarea className="remediation-plan-copybox" readOnly value={planText} />
      </details>
    </section>
  );
}

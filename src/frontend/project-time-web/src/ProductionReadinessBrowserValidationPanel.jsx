import { useMemo, useState } from 'react';

const STORAGE_KEY = 'projectPulseProductionReadinessBrowserValidation';

const validationItems = [
  {
    key: 'dashboard',
    label: 'Dashboard',
    route: '#dashboard',
    purpose: 'Confirm the landing page loads, navigation is usable, and no blank screen appears.'
  },
  {
    key: 'production-readiness',
    label: 'Production Readiness Center',
    route: '#production-readiness',
    purpose: 'Confirm readiness cards, backend endpoint status, check table, and validation checklist are visible.'
  },
  {
    key: 'project-intake',
    label: 'Project Intake',
    route: '#project-intake',
    purpose: 'Confirm intake summary, aging, post-intake, documents, and handoff views load.'
  },
  {
    key: 'project-workspace',
    label: 'Resource / Project Workspace',
    route: '#project-workspace',
    purpose: 'Confirm project workspace, assigned tasks, documents, and resource context load.'
  },
  {
    key: 'workflow',
    label: 'Approval / Export / Audit Workflows',
    route: '#workflow',
    purpose: 'Confirm approvals, export package readiness, reconciliation, lock evidence, and workflow validation areas load.'
  },
  {
    key: 'manager-approval',
    label: 'Manager Approvals',
    route: '#manager-approval',
    purpose: 'Confirm approval queues display only for roles with approval access.'
  },
  {
    key: 'role-admin',
    label: 'Role / Security Administration',
    route: '#role-admin',
    purpose: 'Confirm role access matrix, route permission contracts, and View-As controls remain restricted.'
  },
  {
    key: 'audit-history',
    label: 'Audit History',
    route: '#audit-history',
    purpose: 'Confirm audit records, filters, and operational evidence load.'
  }
];

function readStoredValidation() {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return {
        checked: {},
        notes: '',
        lastUpdated: null
      };
    }

    const parsed = JSON.parse(raw);
    return {
      checked: parsed?.checked || {},
      notes: parsed?.notes || '',
      lastUpdated: parsed?.lastUpdated || null
    };
  } catch {
    return {
      checked: {},
      notes: '',
      lastUpdated: null
    };
  }
}

function writeStoredValidation(nextState) {
  window.localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      ...nextState,
      lastUpdated: new Date().toISOString()
    })
  );
}

export default function ProductionReadinessBrowserValidationPanel() {
  const [validationState, setValidationState] = useState(readStoredValidation);

  const completeCount = useMemo(() => {
    return validationItems.filter((item) => validationState.checked[item.key]).length;
  }, [validationState.checked]);

  const completionPercent = validationItems.length === 0
    ? 0
    : Math.round((completeCount / validationItems.length) * 100);

  function toggleItem(key) {
    setValidationState((current) => {
      const nextState = {
        ...current,
        checked: {
          ...current.checked,
          [key]: !current.checked[key]
        },
        lastUpdated: new Date().toISOString()
      };

      writeStoredValidation(nextState);
      return nextState;
    });
  }

  function updateNotes(value) {
    setValidationState((current) => {
      const nextState = {
        ...current,
        notes: value,
        lastUpdated: new Date().toISOString()
      };

      writeStoredValidation(nextState);
      return nextState;
    });
  }

  function resetValidation() {
    const nextState = {
      checked: {},
      notes: '',
      lastUpdated: new Date().toISOString()
    };

    writeStoredValidation(nextState);
    setValidationState(nextState);
  }

  return (
    <section className="production-readiness-panel browser-validation-panel">
      <div className="production-readiness-panel-heading">
        <div>
          <p className="eyebrow">Manual browser validation</p>
          <h2>Webpage validation checklist</h2>
          <p>
            Use this checklist while clicking through the app. It gives you a clear way to track
            what has been verified on the webpage before we merge or move to the next build.
          </p>
        </div>

        <div className="browser-validation-summary">
          <strong>{completionPercent}%</strong>
          <span>{completeCount} of {validationItems.length} complete</span>
        </div>
      </div>

      <div className="browser-validation-progress" aria-label="Browser validation progress">
        <span style={{ width: `${completionPercent}%` }} />
      </div>

      <div className="browser-validation-list">
        {validationItems.map((item) => (
          <article className={validationState.checked[item.key] ? 'browser-validation-item complete' : 'browser-validation-item'} key={item.key}>
            <label>
              <input
                type="checkbox"
                checked={Boolean(validationState.checked[item.key])}
                onChange={() => toggleItem(item.key)}
              />
              <span>
                <strong>{item.label}</strong>
                <small>{item.purpose}</small>
              </span>
            </label>

            <a href={item.route}>Open</a>
          </article>
        ))}
      </div>

      <label className="browser-validation-notes">
        Validation notes / issues found
        <textarea
          value={validationState.notes}
          placeholder="Example: Project Intake loads, but the empty state wording needs to be clearer. Manager Approvals looks good for Administrator."
          onChange={(event) => updateNotes(event.target.value)}
        />
      </label>

      <div className="browser-validation-footer">
        <span>
          Last updated:{' '}
          {validationState.lastUpdated
            ? new Date(validationState.lastUpdated).toLocaleString()
            : 'Not started'}
        </span>

        <button type="button" className="secondary-action" onClick={resetValidation}>
          Reset checklist
        </button>
      </div>
    </section>
  );
}

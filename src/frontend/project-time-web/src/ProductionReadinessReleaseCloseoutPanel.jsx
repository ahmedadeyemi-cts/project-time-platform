import { useMemo, useState } from 'react';

const STORAGE_KEY = 'projectPulseReleaseCandidateCloseout';

const closeoutItems = [
  {
    key: 'readiness-center',
    label: 'Production Readiness Center reviewed',
    detail: 'Readiness cards, backend check table, purpose map, and browser validation checklist were reviewed.'
  },
  {
    key: 'page-context-guide',
    label: 'Page context guide reviewed',
    detail: 'The guide appears on signed-in pages and explains purpose, backend support, and what to check.'
  },
  {
    key: 'critical-navigation',
    label: 'Critical navigation checked',
    detail: 'Dashboard, Project Intake, Project Workspace, Workflow, Manager Approvals, Role Admin, and Audit History were opened.'
  },
  {
    key: 'role-behavior',
    label: 'Role behavior reviewed',
    detail: 'Restricted areas and access behavior were reviewed for Administrator/system roles and unauthorized access.'
  },
  {
    key: 'readiness-refresh',
    label: 'Backend readiness refresh checked',
    detail: 'Refresh readiness was clicked and endpoint status/readiness cards responded clearly.'
  },
  {
    key: 'browser-notes',
    label: 'Browser validation notes captured',
    detail: 'Any visual, navigation, empty-state, role, or wording issue was captured in the browser validation notes.'
  },
  {
    key: 'no-blockers',
    label: 'No release-blocking webpage issue remains',
    detail: 'Any issue still present is acceptable as follow-up and does not block the branch from PR/merge review.'
  }
];

function readStoredCloseout() {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return {
        checked: {},
        decisionNotes: '',
        lastUpdated: null
      };
    }

    const parsed = JSON.parse(raw);
    return {
      checked: parsed?.checked || {},
      decisionNotes: parsed?.decisionNotes || '',
      lastUpdated: parsed?.lastUpdated || null
    };
  } catch {
    return {
      checked: {},
      decisionNotes: '',
      lastUpdated: null
    };
  }
}

function writeStoredCloseout(nextState) {
  window.localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      ...nextState,
      lastUpdated: new Date().toISOString()
    })
  );
}

export default function ProductionReadinessReleaseCloseoutPanel() {
  const [closeoutState, setCloseoutState] = useState(readStoredCloseout);

  const completeCount = useMemo(() => {
    return closeoutItems.filter((item) => closeoutState.checked[item.key]).length;
  }, [closeoutState.checked]);

  const isReadyForCloseout = completeCount === closeoutItems.length;

  function toggleItem(key) {
    setCloseoutState((current) => {
      const nextState = {
        ...current,
        checked: {
          ...current.checked,
          [key]: !current.checked[key]
        },
        lastUpdated: new Date().toISOString()
      };

      writeStoredCloseout(nextState);
      return nextState;
    });
  }

  function updateDecisionNotes(value) {
    setCloseoutState((current) => {
      const nextState = {
        ...current,
        decisionNotes: value,
        lastUpdated: new Date().toISOString()
      };

      writeStoredCloseout(nextState);
      return nextState;
    });
  }

  function resetCloseout() {
    const nextState = {
      checked: {},
      decisionNotes: '',
      lastUpdated: new Date().toISOString()
    };

    writeStoredCloseout(nextState);
    setCloseoutState(nextState);
  }

  return (
    <section className="production-readiness-panel release-closeout-panel">
      <div className="production-readiness-panel-heading">
        <div>
          <p className="eyebrow">Release candidate closeout</p>
          <h2>Final webpage readiness decision</h2>
          <p>
            Use this panel after reviewing the visible app. When all items are complete, this branch
            is ready for final PR/merge validation unless a release-blocking issue is found.
          </p>
        </div>

        <div className={isReadyForCloseout ? 'release-closeout-badge ready' : 'release-closeout-badge pending'}>
          <strong>{isReadyForCloseout ? 'Ready' : 'Pending'}</strong>
          <span>{completeCount} of {closeoutItems.length} complete</span>
        </div>
      </div>

      <div className="release-closeout-list">
        {closeoutItems.map((item) => (
          <label className={closeoutState.checked[item.key] ? 'release-closeout-item complete' : 'release-closeout-item'} key={item.key}>
            <input
              type="checkbox"
              checked={Boolean(closeoutState.checked[item.key])}
              onChange={() => toggleItem(item.key)}
            />
            <span>
              <strong>{item.label}</strong>
              <small>{item.detail}</small>
            </span>
          </label>
        ))}
      </div>

      <label className="release-closeout-notes">
        Final decision notes
        <textarea
          value={closeoutState.decisionNotes}
          placeholder="Example: Production Readiness Center, Project Intake, Workflow, Role Admin, and Audit History all load. No blocking webpage issue found."
          onChange={(event) => updateDecisionNotes(event.target.value)}
        />
      </label>

      <div className="release-closeout-footer">
        <span>
          Last updated:{' '}
          {closeoutState.lastUpdated
            ? new Date(closeoutState.lastUpdated).toLocaleString()
            : 'Not started'}
        </span>

        <button type="button" className="secondary-action" onClick={resetCloseout}>
          Reset closeout
        </button>
      </div>
    </section>
  );
}

import { useMemo, useState } from 'react';

const STORAGE_KEY = 'projectPulseProductionDataGoLiveGate';

const gateChecklist = [
  {
    key: 'users-roles',
    label: 'Users and roles reviewed',
    detail: 'Confirmed real users exist and role assignments are ready for production access.'
  },
  {
    key: 'customers-projects',
    label: 'Customers and projects reviewed',
    detail: 'Confirmed customer, project, and project task records are ready for intake, work assignment, and time entry.'
  },
  {
    key: 'time-workflow',
    label: 'Time workflow reviewed',
    detail: 'Confirmed timesheets, time entries, approvals, and export workflow data are ready or have accepted remediation notes.'
  },
  {
    key: 'audit-notifications',
    label: 'Audit and notification evidence reviewed',
    detail: 'Confirmed audit and notification evidence are available or have a documented remediation path.'
  },
  {
    key: 'page-validation',
    label: 'Related webpages validated',
    detail: 'Opened User Admin, Role Admin, Customer Directory, Project Intake, Project Workspace, Workflow, Manager Approvals, and Audit History.'
  },
  {
    key: 'go-live-decision',
    label: 'Go-live data decision captured',
    detail: 'Captured final data readiness decision notes before PR or production cutover.'
  }
];

function normalizeStatus(status) {
  const value = String(status || '').toLowerCase();
  if (value.includes('ready')) return 'ready';
  if (value.includes('missing')) return 'missing';
  if (value.includes('need')) return 'review';
  return value || 'unknown';
}

function readStoredGate() {
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

function writeStoredGate(nextState) {
  window.localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      checked: nextState.checked || {},
      notes: nextState.notes || '',
      lastUpdated: new Date().toISOString()
    })
  );
}

function buildGateEvidence(checks, gateState, gateSummary) {
  const completed = gateChecklist.filter((item) => gateState.checked[item.key]);
  const remaining = gateChecklist.filter((item) => !gateState.checked[item.key]);
  const blockers = checks.filter((check) => normalizeStatus(check.status) !== 'ready');

  return [
    'Project Health Dashboard - Production Data Go-Live Gate Evidence',
    `Generated: ${new Date().toLocaleString()}`,
    '',
    `Gate status: ${gateSummary.statusLabel}`,
    `Backend checks: ${gateSummary.readyCount} ready / ${gateSummary.totalCount} total`,
    `Data blockers: ${gateSummary.blockerCount}`,
    `Missing tables: ${gateSummary.missingCount}`,
    '',
    'Backend data blockers:',
    ...(blockers.length
      ? blockers.map((item) => `- ${item.label || item.key}: ${item.status}; count ${item.count ?? 0}; table ${item.tableName || 'unknown'}`)
      : ['- None']),
    '',
    'Completed go-live checklist:',
    ...(completed.length ? completed.map((item) => `- ${item.label}`) : ['- None']),
    '',
    'Remaining go-live checklist:',
    ...(remaining.length ? remaining.map((item) => `- ${item.label}`) : ['- None']),
    '',
    'Decision notes:',
    gateState.notes || 'No decision notes captured.'
  ].join('\n');
}

export default function ProductionDataReadinessGoLiveGatePanel({ checks = [], loading = false }) {
  const [gateState, setGateState] = useState(readStoredGate);

  const gateSummary = useMemo(() => {
    const totalCount = checks.length;
    const readyCount = checks.filter((check) => normalizeStatus(check.status) === 'ready').length;
    const missingCount = checks.filter((check) => normalizeStatus(check.status) === 'missing').length;
    const reviewCount = checks.filter((check) => normalizeStatus(check.status) === 'review').length;
    const blockerCount = totalCount - readyCount;
    const checklistComplete = gateChecklist.every((item) => gateState.checked[item.key]);

    let statusLabel = 'Not started';
    if (loading) {
      statusLabel = 'Loading';
    } else if (blockerCount > 0) {
      statusLabel = 'Blocked by data readiness';
    } else if (!checklistComplete) {
      statusLabel = 'Ready for checklist review';
    } else {
      statusLabel = 'Ready for go-live validation';
    }

    return {
      totalCount,
      readyCount,
      missingCount,
      reviewCount,
      blockerCount,
      checklistComplete,
      statusLabel
    };
  }, [checks, gateState.checked, loading]);

  const evidenceText = useMemo(
    () => buildGateEvidence(checks, gateState, gateSummary),
    [checks, gateState, gateSummary]
  );

  function toggleItem(key) {
    setGateState((current) => {
      const nextState = {
        ...current,
        checked: {
          ...current.checked,
          [key]: !current.checked[key]
        },
        lastUpdated: new Date().toISOString(),
        copyStatus: ''
      };

      writeStoredGate(nextState);
      return nextState;
    });
  }

  function updateNotes(value) {
    setGateState((current) => {
      const nextState = {
        ...current,
        notes: value,
        lastUpdated: new Date().toISOString(),
        copyStatus: ''
      };

      writeStoredGate(nextState);
      return nextState;
    });
  }

  async function copyEvidence() {
    try {
      await navigator.clipboard.writeText(evidenceText);
      setGateState((current) => ({
        ...current,
        copyStatus: 'Copied go-live gate evidence to clipboard.'
      }));
    } catch {
      setGateState((current) => ({
        ...current,
        copyStatus: 'Clipboard copy was blocked. Use the evidence box below to copy manually.'
      }));
    }
  }

  function resetGate() {
    const nextState = {
      checked: {},
      notes: '',
      lastUpdated: new Date().toISOString(),
      copyStatus: ''
    };

    writeStoredGate(nextState);
    setGateState(nextState);
  }

  return (
    <section className="production-data-panel production-data-golive-panel">
      <div className="production-data-panel-heading golive-heading">
        <div>
          <p className="eyebrow">Go-live gate</p>
          <h2>Production data go-live gate</h2>
          <p>
            This panel converts the backend data-readiness results into a go-live decision. Use it
            before merging, production cutover, or final stakeholder signoff.
          </p>
        </div>

        <div className={gateSummary.blockerCount === 0 && gateSummary.checklistComplete ? 'golive-badge ready' : 'golive-badge blocked'}>
          <strong>{gateSummary.statusLabel}</strong>
          <span>{gateSummary.readyCount} of {gateSummary.totalCount} backend checks ready</span>
        </div>
      </div>

      <div className="golive-summary-grid">
        <article>
          <span>Backend blockers</span>
          <strong>{gateSummary.blockerCount}</strong>
          <small>Should be 0 before production cutover</small>
        </article>

        <article>
          <span>Missing tables</span>
          <strong>{gateSummary.missingCount}</strong>
          <small>Should be 0 before go-live</small>
        </article>

        <article>
          <span>Needs data review</span>
          <strong>{gateSummary.reviewCount}</strong>
          <small>Requires remediation or accepted risk</small>
        </article>

        <article>
          <span>Checklist complete</span>
          <strong>{gateSummary.checklistComplete ? 'Yes' : 'No'}</strong>
          <small>Tracks browser-side signoff</small>
        </article>
      </div>

      <div className="golive-checklist">
        {gateChecklist.map((item) => (
          <label className={gateState.checked[item.key] ? 'golive-check complete' : 'golive-check'} key={item.key}>
            <input
              type="checkbox"
              checked={Boolean(gateState.checked[item.key])}
              onChange={() => toggleItem(item.key)}
            />
            <span>
              <strong>{item.label}</strong>
              <small>{item.detail}</small>
            </span>
          </label>
        ))}
      </div>

      <label className="golive-notes">
        Go-live data decision notes
        <textarea
          value={gateState.notes}
          placeholder="Example: Backend data-readiness checks reviewed. Customer and project data still need production import before final go-live, but the remediation path is documented."
          onChange={(event) => updateNotes(event.target.value)}
        />
      </label>

      <div className="golive-actions">
        <button type="button" className="secondary-action" onClick={copyEvidence}>
          Copy go-live evidence
        </button>
        <button type="button" className="secondary-action" onClick={resetGate}>
          Reset go-live gate
        </button>
        <span>{gateState.copyStatus || (gateState.lastUpdated ? `Last updated ${new Date(gateState.lastUpdated).toLocaleString()}` : 'Not started')}</span>
      </div>

      <details className="data-readiness-copy-details">
        <summary>Show go-live evidence text</summary>
        <textarea className="golive-evidence-copybox" readOnly value={evidenceText} />
      </details>
    </section>
  );
}

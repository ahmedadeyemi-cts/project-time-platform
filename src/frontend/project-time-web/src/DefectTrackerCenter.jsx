import { useEffect, useMemo, useState } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import './defect-tracker-center.css';

const endpointList = [
  '/api/defect-tracker/overview',
  '/api/defect-tracker/defects',
  '/api/defect-tracker/intake-policy',
  '/api/defect-tracker/notification-policy',
  '/api/defect-tracker/integration-policy'
];

const initialDraft = {
  title: '',
  description: '',
  category: 'Bug',
  priority: 'Medium',
  affectedModule: '',
  affectedRoute: '',
  environment: '',
  assigneeUserId: ''
};

function authHeaders(authSession) {
  const token = authSession?.sessionToken
    || authSession?.token
    || authSession?.accessToken
    || '';

  return token ? { 'X-ProjectPulse-Session': token } : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, {
    method: 'GET',
    headers: authHeaders(authSession)
  });

  if (!response.ok) {
    let message = `Request failed (${response.status}).`;
    try {
      const payload = await response.json();
      message = payload?.message || payload?.status || message;
    } catch {
      // Keep the sanitized status-only message.
    }
    throw new Error(message);
  }

  return response.json();
}

function sourceFromLocation() {
  const value = new URLSearchParams(window.location.search)
    .get('defectSource')
    ?.trim()
    .toLowerCase();

  const supported = new Set([
    'help', 'tracker', 'github', 'claude_github', 'chatgpt_github'
  ]);
  return supported.has(value) ? value : 'tracker';
}

function sourceLabel(source) {
  return {
    help: 'ProjectPulse Help',
    tracker: 'Module 076 Tracker',
    github: 'GitHub',
    claude_github: 'Claude through GitHub',
    chatgpt_github: 'ChatGPT through GitHub'
  }[source] || 'Module 076 Tracker';
}

function formatDate(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

function resolutionLabel(defect) {
  if (defect?.resolutionTime) return defect.resolutionTime;
  if (!defect?.dateAdded || !defect?.dateResolved) return '—';
  const added = Date.parse(defect.dateAdded);
  const resolved = Date.parse(defect.dateResolved);
  if (!Number.isFinite(added) || !Number.isFinite(resolved) || resolved < added) return '—';
  const totalMinutes = Math.round((resolved - added) / 60000);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  const minutes = totalMinutes % 60;
  return `${days ? `${days}d ` : ''}${hours ? `${hours}h ` : ''}${minutes}m`;
}

function PolicyState({ enabled, enabledLabel, lockedLabel }) {
  return (
    <span className={enabled ? 'defect-state ready' : 'defect-state locked'}>
      {enabled ? enabledLabel : lockedLabel}
    </span>
  );
}

export default function DefectTrackerCenter({ authSession }) {
  const [payloads, setPayloads] = useState({});
  const [assigneeOptions, setAssigneeOptions] = useState([]);
  const [draft, setDraft] = useState(initialDraft);
  const [draftPreview, setDraftPreview] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const sourceChannel = useMemo(sourceFromLocation, []);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      setError('');
      try {
        const values = await Promise.all(endpointList.map((path) => readJson(path, authSession)));
        if (cancelled) return;
        const nextPayloads = Object.fromEntries(endpointList.map((path, index) => [path, values[index]]));
        setPayloads(nextPayloads);

        const overview = nextPayloads['/api/defect-tracker/overview'];
        const defaultAssignee = overview?.defaultAssignee;
        setDraft((current) => ({
          ...current,
          assigneeUserId: defaultAssignee?.userId || ''
        }));

        if (overview?.access?.canReassign) {
          const options = await readJson('/api/defect-tracker/assignee-options', authSession);
          if (!cancelled) setAssigneeOptions(options?.identities || []);
        }
      } catch (loadError) {
        if (!cancelled) setError(loadError?.message || 'Defect Tracker is temporarily unavailable.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [authSession?.sessionToken, authSession?.token, authSession?.accessToken]);

  const overview = payloads['/api/defect-tracker/overview'];
  const inventory = payloads['/api/defect-tracker/defects'];
  const intakePolicy = payloads['/api/defect-tracker/intake-policy'];
  const notificationPolicy = payloads['/api/defect-tracker/notification-policy'];
  const integrationPolicy = payloads['/api/defect-tracker/integration-policy'];
  const defects = inventory?.defects || [];
  const categories = overview?.categories || ['Bug', 'Regression', 'Other'];
  const priorities = overview?.priorities || ['Critical', 'High', 'Medium', 'Low'];
  const defaultAssignee = overview?.defaultAssignee;
  const writesEnabled = Boolean(overview?.persistence?.writesEnabled);

  function updateDraft(field, value) {
    setDraft((current) => ({ ...current, [field]: value }));
    setDraftPreview(null);
  }

  function previewDraft(event) {
    event.preventDefault();
    const assignee = assigneeOptions.find((identity) => identity.userId === draft.assigneeUserId)
      || defaultAssignee;
    setDraftPreview({
      ...draft,
      sourceChannel,
      sourceLabel: sourceLabel(sourceChannel),
      assignee,
      defectId: 'Assigned after durable save',
      dateAdded: 'Assigned by server after durable save',
      dateResolved: null,
      status: 'Open'
    });
  }

  return (
    <section
      className="defect-tracker-center"
      data-module="076"
      data-contract-version={overview?.contractVersion || '2026-07-20.1'}
      data-persistence-mode="fail-closed"
      aria-labelledby="defect-tracker-title"
    >
      <header className="defect-tracker-hero">
        <div className="defect-brand-lockup">
          <img src={usSignalLogoUrl} alt="US Signal" />
          <span>ProjectPulse quality operations</span>
        </div>
        <div className="defect-hero-content">
          <div>
            <p className="defect-eyebrow">Module 076</p>
            <h1 id="defect-tracker-title">Defect Intake &amp; Resolution Tracker</h1>
            <p>
              One governed queue for defects raised from ProjectPulse Help, GitHub,
              Claude through GitHub, and ChatGPT through GitHub.
            </p>
          </div>
          <PolicyState
            enabled={writesEnabled}
            enabledLabel="Durable intake active"
            lockedLabel="Source complete · persistence locked"
          />
        </div>
      </header>

      {loading ? <p className="defect-banner neutral">Loading governed defect contracts…</p> : null}
      {error ? <p className="defect-banner error" role="alert">{error}</p> : null}
      {!loading && !error && !writesEnabled ? (
        <p className="defect-banner warning">
          The complete tracking and integration contract is loaded. Durable defect IDs,
          database writes, manager email, reporter email, and GitHub webhook processing
          remain locked pending their separate activation approvals.
        </p>
      ) : null}

      <div className="defect-summary-grid" aria-label="Defect summary">
        {[
          ['Total', overview?.summary?.total ?? 0],
          ['Open', overview?.summary?.open ?? 0],
          ['In progress', overview?.summary?.inProgress ?? 0],
          ['Blocked', overview?.summary?.blocked ?? 0],
          ['Resolved', overview?.summary?.resolved ?? 0],
          ['Critical', overview?.summary?.critical ?? 0]
        ].map(([label, value]) => (
          <article key={label} className="defect-summary-card">
            <span>{label}</span>
            <strong>{value}</strong>
          </article>
        ))}
      </div>

      <div className="defect-policy-grid">
        <article className="defect-policy-card">
          <span className="defect-card-kicker">Default ownership</span>
          <h2>{defaultAssignee?.displayName || 'Ahmed Adeyemi'}</h2>
          <p>{defaultAssignee?.email || 'ahmed.adeyemi@ussignal.com'}</p>
          <small>{defaultAssignee?.state || 'Identity resolution pending'}</small>
        </article>
        <article className="defect-policy-card">
          <span className="defect-card-kicker">Automatic identifier</span>
          <h2>{overview?.idPolicy?.format || 'DEF-{YYYY}-{SEQUENCE:000000}'}</h2>
          <p>Allocated atomically only after a durable create succeeds.</p>
          <small>Example: {overview?.idPolicy?.example || 'DEF-2026-000001'}</small>
        </article>
        <article className="defect-policy-card">
          <span className="defect-card-kicker">Notification workflow</span>
          <h2>Managers on open</h2>
          <p>Original reporter on resolution.</p>
          <small>Delivery owner: {notificationPolicy?.owner || 'Module 067 Global Mail'}</small>
        </article>
        <article className="defect-policy-card">
          <span className="defect-card-kicker">Current intake source</span>
          <h2>{sourceLabel(sourceChannel)}</h2>
          <p>Source attribution is captured with the defect.</p>
          <small>Direct AI execution: disabled</small>
        </article>
      </div>

      <div className="defect-content-grid">
        <form className="defect-intake-card" onSubmit={previewDraft}>
          <div className="defect-section-heading">
            <div>
              <span className="defect-card-kicker">New defect</span>
              <h2>Prepare an intake record</h2>
            </div>
            <span className="defect-source-pill">{sourceLabel(sourceChannel)}</span>
          </div>

          <label>
            Summary
            <input
              value={draft.title}
              maxLength={180}
              placeholder="Short description of the defect"
              onChange={(event) => updateDraft('title', event.target.value)}
            />
          </label>
          <label>
            Description
            <textarea
              value={draft.description}
              maxLength={8000}
              rows={5}
              placeholder="What happened, what was expected, and how to reproduce it"
              onChange={(event) => updateDraft('description', event.target.value)}
            />
          </label>

          <div className="defect-form-grid">
            <label>
              Category
              <select value={draft.category} onChange={(event) => updateDraft('category', event.target.value)}>
                {categories.map((category) => <option key={category}>{category}</option>)}
              </select>
            </label>
            <label>
              Priority
              <select value={draft.priority} onChange={(event) => updateDraft('priority', event.target.value)}>
                {priorities.map((priority) => <option key={priority}>{priority}</option>)}
              </select>
            </label>
            <label>
              Affected module
              <input
                value={draft.affectedModule}
                placeholder="Example: 002"
                onChange={(event) => updateDraft('affectedModule', event.target.value)}
              />
            </label>
            <label>
              Affected route
              <input
                value={draft.affectedRoute}
                placeholder="Example: manager-approval"
                onChange={(event) => updateDraft('affectedRoute', event.target.value)}
              />
            </label>
            <label>
              Environment
              <input
                value={draft.environment}
                placeholder="Test, production, local…"
                onChange={(event) => updateDraft('environment', event.target.value)}
              />
            </label>
            <label>
              Assignee
              <select
                value={draft.assigneeUserId}
                onChange={(event) => updateDraft('assigneeUserId', event.target.value)}
                disabled={!overview?.access?.canReassign || assigneeOptions.length === 0}
              >
                <option value={defaultAssignee?.userId || ''}>
                  {defaultAssignee?.displayName || 'Ahmed Adeyemi'} (default)
                </option>
                {assigneeOptions
                  .filter((identity) => identity.userId !== defaultAssignee?.userId)
                  .map((identity) => (
                    <option key={identity.userId} value={identity.userId}>
                      {identity.displayName} — {identity.email}
                    </option>
                  ))}
              </select>
            </label>
          </div>

          <div className="defect-form-actions">
            <button type="submit" className="defect-secondary-action">Review local draft</button>
            <button type="button" className="defect-primary-action" disabled={!writesEnabled}>
              Create defect
            </button>
          </div>

          {draftPreview ? (
            <div className="defect-draft-preview" aria-live="polite">
              <strong>{draftPreview.defectId}</strong>
              <span>{draftPreview.status} · {draftPreview.priority} · {draftPreview.category}</span>
              <p>{draftPreview.title || 'Summary required before durable creation.'}</p>
              <small>Assigned to {draftPreview.assignee?.displayName || 'Ahmed Adeyemi'} · {draftPreview.dateAdded}</small>
            </div>
          ) : null}
        </form>

        <aside className="defect-integration-card">
          <div className="defect-section-heading">
            <div>
              <span className="defect-card-kicker">Intake channels</span>
              <h2>Automatic synchronization contract</h2>
            </div>
          </div>
          <ul className="defect-channel-list">
            {(integrationPolicy?.integrations || [
              { channel: 'help', state: 'source_connected', mechanism: 'ProjectPulse Help opens this intake route.' },
              { channel: 'github', state: 'issue_form_present_webhook_locked', mechanism: 'GitHub issue form with signed webhook pending.' },
              { channel: 'claude_github', state: 'contract_ready_webhook_locked', mechanism: 'Claude reports through GitHub.' },
              { channel: 'chatgpt_github', state: 'contract_ready_webhook_locked', mechanism: 'ChatGPT reports through GitHub.' }
            ]).map((integration) => (
              <li key={integration.channel}>
                <div>
                  <strong>{sourceLabel(integration.channel)}</strong>
                  <p>{integration.mechanism}</p>
                </div>
                <span>{integration.state.replaceAll('_', ' ')}</span>
              </li>
            ))}
          </ul>
          <div className="defect-integration-note">
            <strong>GitHub security boundary</strong>
            <p>
              Only a signed event from the allowlisted repository may create or
              update a durable record. Text inside an issue cannot impersonate a
              trusted Claude or ChatGPT source.
            </p>
          </div>
        </aside>
      </div>

      <section className="defect-table-card" aria-labelledby="defect-table-title">
        <div className="defect-section-heading">
          <div>
            <span className="defect-card-kicker">Defect register</span>
            <h2 id="defect-table-title">Open and resolved defects</h2>
          </div>
          <span>{inventory?.scope?.replaceAll('_', ' ') || 'authorized scope'}</span>
        </div>
        <div className="defect-table-scroll">
          <table>
            <thead>
              <tr>
                <th>Defect ID</th>
                <th>Status</th>
                <th>Description</th>
                <th>Category</th>
                <th>Priority</th>
                <th>Assignee</th>
                <th>Raised By</th>
                <th>Source</th>
                <th>Date Added</th>
                <th>Date Resolved</th>
                <th>Resolution Time</th>
                <th>Comments</th>
              </tr>
            </thead>
            <tbody>
              {defects.length === 0 ? (
                <tr>
                  <td colSpan="12" className="defect-empty-state">
                    <strong>No durable inventory is connected.</strong>
                    <span>{inventory?.statement || 'Persistence remains locked pending authorization.'}</span>
                  </td>
                </tr>
              ) : defects.map((defect) => (
                <tr key={defect.defectId}>
                  <td><strong>{defect.defectId}</strong></td>
                  <td><span className={`defect-status ${String(defect.status || '').toLowerCase().replaceAll(' ', '-')}`}>{defect.status}</span></td>
                  <td>{defect.description}</td>
                  <td>{defect.category}</td>
                  <td><span className={`defect-priority ${String(defect.priority || '').toLowerCase()}`}>{defect.priority}</span></td>
                  <td>{defect.assignee?.displayName || '—'}</td>
                  <td>{defect.raisedBy?.displayName || '—'}</td>
                  <td>{sourceLabel(defect.sourceChannel)}</td>
                  <td>{formatDate(defect.dateAdded)}</td>
                  <td>{formatDate(defect.dateResolved)}</td>
                  <td>{resolutionLabel(defect)}</td>
                  <td>{defect.comments?.length ?? defect.commentCount ?? 0}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <footer className="defect-contract-footer">
        <div>
          <strong>Date integrity</strong>
          <p>{intakePolicy?.datePolicy?.resolutionTime || 'Resolution time is calculated by the server.'}</p>
        </div>
        <div>
          <strong>Mail boundary</strong>
          <p>{notificationPolicy?.controls?.reason || 'Global Mail delivery requires separate activation.'}</p>
        </div>
        <div>
          <strong>Preservation</strong>
          <p>Modules 002, 056E, 059, 062, and 064–074 remain protected.</p>
        </div>
      </footer>
    </section>
  );
}

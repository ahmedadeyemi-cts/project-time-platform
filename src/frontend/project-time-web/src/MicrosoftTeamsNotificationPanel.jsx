import { useEffect, useId, useRef, useState } from 'react';
import {
  deliveryPresentation, localTimestamp, recentDeliveries, validRecipient,
  validTeamsAppId, workspaceAccess,
} from './microsoft-teams-notification-state.mjs';
import './microsoft-teams-notifications.css';

const path = '/api/microsoft-integration/teams';
async function request(url, init = {}) {
  let session;
  try { session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null'); } catch { session = null; }
  const response = await fetch(url, { credentials: 'same-origin', ...init, headers: {
    ...(session?.sessionToken ? { Authorization: `Bearer ${session.sessionToken}`, 'X-ProjectPulse-Session': session.sessionToken } : {}),
    ...init.headers,
  } });
  let body;
  try { body = await response.json(); } catch {
    throw new Error(`The server response could not be read (HTTP ${response.status}). Refresh the results before retrying.`);
  }
  if (!response.ok) {
    const error = new Error(typeof body?.message === 'string'
      ? body.message.slice(0, 1000) : `The request was not confirmed (HTTP ${response.status}).`);
    error.httpStatus = response.status;
    throw error;
  }
  return body;
}

function Badge({ tone = 'neutral', children }) {
  return <span className={`teams-notifications-badge ${tone}`}>{children}</span>;
}

// Reset drafts, recipient, confirmation and request ownership when the environment changes.
export default function MicrosoftTeamsNotificationPanel({ environment }) {
  return <TeamsNotificationWorkspace key={environment} environment={environment} />;
}

function TeamsNotificationWorkspace({ environment }) {
  const id = useId();
  const [state, setState] = useState(null);
  const [draft, setDraft] = useState(null);
  const [busy, setBusy] = useState('load');
  const [notice, setNotice] = useState(null);
  const [fresh, setFresh] = useState(false);
  const [recipient, setRecipient] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [filter, setFilter] = useState('all');
  const active = useRef(false);
  const operation = useRef(null);
  const access = workspaceAccess(state, draft, environment, busy, fresh);
  const saved = state?.configuration;
  const allRows = recentDeliveries(state?.deliveries);
  const rows = recentDeliveries(allRows, filter);
  const latest = allRows[0] ? deliveryPresentation(allRows[0]) : null;
  const environmentName = environment === 'test' ? 'Test' : environment === 'production' ? 'Production' : 'Unknown';

  async function perform(mode) {
    // Synchronous guard prevents a double click before React rerenders.
    if (!active.current || operation.current) return;
    if (mode === 'save' && !access.canSave) return;
    if (mode === 'test' && (!access.canTest || !validRecipient(recipient) || confirmation !== 'SEND TEAMS TEST')) return;
    const token = { controller: new AbortController() };
    operation.current = token;
    const current = () => active.current && operation.current === token;
    setBusy(mode);
    setNotice(null);
    if (mode === 'test') setConfirmation('');
    let resultNotice = null;
    let mutationFailed = false;
    try {
      if (mode === 'save' || mode === 'test') {
        const result = await request(mode === 'test' ? `${path}/test-delivery` : path, {
          method: mode === 'test' ? 'POST' : 'PUT', signal: token.controller.signal,
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(mode === 'test'
            ? { recipient: recipient.trim(), confirmation: 'SEND TEAMS TEST' }
            : { enabled: draft.enabled, teamsAppId: draft.teamsAppId?.trim() || null, expectedRevision: draft.revision }),
        });
        resultNotice = mode === 'save'
          ? { tone: 'neutral', text: 'Configuration saved. This does not verify notification delivery.' }
          : result.status === 'sent'
            ? { tone: 'success', text: 'Microsoft accepted the test request. Confirm the notification and its link in the recipient’s Teams Activity feed.' }
            : { tone: 'warning', text: 'The test outcome is not confirmed. Review the latest result before sending another request.' };
      }
    } catch (error) {
      mutationFailed = true;
      resultNotice = { tone: 'danger', text: `${error.message} A save or send may have reached the server; it will not be retried automatically.` };
    }
    if (!current()) return;
    try {
      // A read never replays an external notification.
      const next = await request(path, { signal: token.controller.signal });
      if (!current()) return;
      if (!next || typeof next !== 'object' || !next.configuration || typeof next.configuration !== 'object')
        throw new Error('The configuration response is incomplete. No delivery readiness was established.');
      setState(next);
      // Keep edits after failed saves and ordinary refresh; retain their original revision.
      if (mode === 'load' || (mode === 'save' && !mutationFailed) || !access.dirty) setDraft(next.configuration);
      setFresh(true);
      setNotice(resultNotice);
    } catch (error) {
      if (!current()) return;
      setFresh(false);
      if (error.httpStatus === 401 || error.httpStatus === 403) {
        setState(null); setDraft(null); setRecipient(''); setConfirmation('');
      }
      setNotice({ tone: 'danger', text: [resultNotice?.text, `Current configuration and history could not be refreshed. ${error.message}`].filter(Boolean).join(' ') });
    } finally {
      if (current()) { operation.current = null; setBusy(null); }
    }
  }

  useEffect(() => {
    active.current = true;
    void perform('load');
    return () => { active.current = false; operation.current?.controller.abort(); operation.current = null; };
  }, []);

  const inputBlocked = access.blocked || access.revisionConflict;
  const testBlockedReason = !saved ? 'Load the saved configuration before testing.'
    : access.wrongEnvironment ? 'Open the application matching the selected environment.'
      : access.readOnly ? 'View-As or read-only access cannot send a test.'
        : environment !== 'test' ? 'Manual test delivery is available only in the Test application.'
          : !fresh ? 'Refresh the saved configuration before testing.'
            : access.revisionConflict ? 'Configuration changed elsewhere. Reset to the latest saved values.'
              : access.dirty ? 'Save or reset your configuration changes before testing.'
                : !saved.enabled || !validTeamsAppId(saved.teamsAppId) ? 'Save an enabled Teams app ID before testing.' : '';

  return <article className="microsoft-integration-card wide teams-notifications-workspace" data-module="065-teams" aria-labelledby={`${id}-heading`}>
    <header className="microsoft-integration-card-heading teams-notifications-heading">
      <div><p className="eyebrow">MODULE 065 · MICROSOFT TEAMS</p>
        <h2 id={`${id}-heading`}>Teams notifications</h2>
        <p className="teams-notifications-copy">Activity-feed alerts for the people who need to act. Configure delivery, test one recipient, and review the results here.</p>
      </div>
      <div className="teams-notifications-heading-actions">
        <Badge>{environmentName} environment</Badge>
        {state?.readOnly && <Badge tone="warning">Read-only / View-As</Badge>}
        <button className="secondary-action" type="button" disabled={Boolean(busy)} onClick={() => void perform('refresh')}>Refresh status</button>
      </div>
    </header>

    <div className="teams-notifications-summary">
      <div><span>Saved configuration</span><strong>{!saved ? 'Not loaded' : saved.enabled ? 'Delivery enabled' : 'Delivery disabled'}</strong>
        <small>{saved ? `Revision ${saved.revision}. Settings alone do not prove delivery.` : 'Readiness has not been established.'}</small></div>
      <div><span>Last recorded request</span><strong>{latest ? <Badge tone={latest.tone}>{latest.label}</Badge> : 'No requests recorded'}</strong>
        <small>{allRows[0] ? `${localTimestamp(allRows[0].updatedAt)}. History may reflect earlier settings.` : 'Send an explicit test after configuring the app.'}</small></div>
      <div><span>Automatic delivery</span><strong>Follows recipient policy</strong>
        <small>Test-only does not send automatically. This panel does not change the shared recipient boundary.</small></div>
    </div>

    {access.wrongEnvironment && <div className="teams-notifications-notice warning" role="alert">Open the {environmentName} application to change its connection. The returned configuration belongs to {saved.environment}.</div>}
    {!fresh && !busy && state && <div className="teams-notifications-notice warning" role="status">Displayed information may be stale. Refresh status before saving or sending.</div>}
    {notice && <div className={`teams-notifications-notice ${notice.tone}`} role={notice.tone === 'danger' ? 'alert' : 'status'}>{notice.text}</div>}
    <p className="teams-notifications-progress" role="status" aria-live="polite">{busy === 'load' ? 'Loading Teams configuration…' : busy === 'refresh' ? 'Refreshing configuration and delivery history…' : busy === 'save' ? 'Saving configuration…' : busy === 'test' ? 'Submitting one test and checking the recorded result…' : ''}</p>

    <div className="teams-notifications-grid">
      <section className="teams-notifications-section" aria-labelledby={`${id}-configuration`}>
        <div className="teams-notifications-section-title"><span aria-hidden="true" className="teams-notifications-step">01</span><div><h3 id={`${id}-configuration`}>Delivery configuration</h3><p>Uses the existing Module 065 Microsoft services connection.</p></div></div>
        <label className="teams-notifications-switch"><input type="checkbox" checked={Boolean(draft?.enabled)} disabled={inputBlocked} onChange={event => setDraft({ ...draft, enabled: event.target.checked })} /><span><strong>Enable Teams delivery</strong><small>Permits delivery only within the existing environment and recipient rules.</small></span></label>
        <label className="microsoft-integration-field" htmlFor={`${id}-app`}><span>Teams app ID</span>
          <input id={`${id}-app`} value={draft?.teamsAppId || ''} disabled={inputBlocked} spellCheck={false} autoComplete="off" aria-describedby={`${id}-app-help`} aria-invalid={Boolean(draft?.teamsAppId && !validTeamsAppId(draft.teamsAppId))} onChange={event => setDraft({ ...draft, teamsAppId: event.target.value.trim() })} placeholder="App ID from the installed Teams manifest" />
          <small id={`${id}-app-help`}>Use the Teams package ID, not the Entra client ID. No secret is entered here.</small>
        </label>
        {draft?.teamsAppId && !validTeamsAppId(draft.teamsAppId) && <p className="teams-notifications-validation">Enter a valid, nonempty Teams app GUID.</p>}
        <div className="microsoft-integration-actions teams-notifications-actions">
          <button className="primary-action" type="button" disabled={!access.canSave} onClick={() => void perform('save')}>{busy === 'save' ? 'Saving…' : 'Save Teams configuration'}</button>
          <button className="secondary-action" type="button" disabled={access.blocked || (!access.dirty && !access.revisionConflict)} onClick={() => { setDraft({ ...saved }); setNotice(null); }}>Reset changes</button>
        </div>
        <p className="teams-notifications-help">{access.revisionConflict ? 'Configuration changed elsewhere. Reset to the latest saved revision before editing.' : access.dirty ? 'Unsaved changes. Save before sending a test.' : saved ? 'Saved settings are shown above. App installation and notification receipt are verified separately.' : 'Configuration is not available yet.'}</p>
      </section>

      <section className="teams-notifications-section" aria-labelledby={`${id}-test`}>
        <div className="teams-notifications-section-title"><span aria-hidden="true" className="teams-notifications-step">02</span><div><h3 id={`${id}-test`}>Test one recipient</h3><p>This sends a real notification. It does not enable automatic delivery.</p></div></div>
        <label className="microsoft-integration-field" htmlFor={`${id}-recipient`}><span>Recipient sign-in address</span>
          <input id={`${id}-recipient`} type="email" value={recipient} disabled={inputBlocked || environment !== 'test'} autoComplete="off" aria-describedby={`${id}-recipient-help`} onChange={event => setRecipient(event.target.value)} placeholder="user@your-tenant.example" />
          <small id={`${id}-recipient-help`}>The user needs PulseApp installed in the matching Teams tenant. SuperAdmins may test another tenant user; other administrators may test only themselves.</small>
        </label>
        <label className="microsoft-integration-field" htmlFor={`${id}-confirm`}><span>Type SEND TEAMS TEST to confirm</span>
          <input id={`${id}-confirm`} value={confirmation} disabled={inputBlocked || environment !== 'test'} autoComplete="off" spellCheck={false} onChange={event => setConfirmation(event.target.value)} placeholder="SEND TEAMS TEST" />
        </label>
        <div className="microsoft-integration-actions teams-notifications-actions"><button className="primary-action" type="button" disabled={!access.canTest || !validRecipient(recipient) || confirmation !== 'SEND TEAMS TEST'} onClick={() => void perform('test')}>{busy === 'test' ? 'Sending one test…' : 'Send Teams test'}</button></div>
        <p className="teams-notifications-help">{testBlockedReason || 'Check the recipient’s Teams Activity feed and open the notification link. Microsoft acceptance is not a read receipt.'}</p>
      </section>
    </div>

    <section className="teams-notifications-history" aria-labelledby={`${id}-history`}>
      <div className="microsoft-integration-card-heading"><div><h3 id={`${id}-history`}>Recent delivery results</h3><p className="teams-notifications-help">{allRows.length} recent records loaded. Results are not a complete delivery audit.</p></div>
        <label className="microsoft-integration-field teams-notifications-filter" htmlFor={`${id}-filter`}><span>Show</span><select id={`${id}-filter`} value={filter} onChange={event => setFilter(event.target.value)}><option value="all">All results</option><option value="attention">Needs review</option><option value="accepted">Accepted by Microsoft</option></select></label>
      </div>
      {rows.length > 0 ? <div className="teams-notifications-table" role="region" aria-label="Teams delivery history" tabIndex={0}><table><caption className="teams-notifications-visually-hidden">Recent Teams delivery requests. Updated times use your local time zone.</caption><thead><tr><th scope="col">Recipient</th><th scope="col">Result and next step</th><th scope="col">Updated (local time)</th></tr></thead><tbody>{rows.map((row, index) => {
        const presentation = deliveryPresentation(row);
        return <tr key={`${row.recipient}-${row.updatedAt}-${index}`}><td>{row.recipient}</td><td><Badge tone={presentation.tone}>{presentation.label}</Badge><p>{presentation.detail}</p><details><summary>Technical details</summary><code>{row.diagnosticCode || 'No diagnostic code supplied'}</code></details></td><td>{localTimestamp(row.updatedAt)}</td></tr>;
      })}</tbody></table></div> : <div className="teams-notifications-empty">{!state ? 'Delivery history has not been loaded.' : allRows.length ? 'No recent results match this filter.' : 'No delivery requests are recorded yet. Saving configuration does not send a test.'}</div>}
    </section>
  </article>;
}

import { useEffect, useMemo, useState } from 'react';
import './manager-approval.css';

function session() {
  try { return JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null'); }
  catch { return null; }
}
function headers(hasBody = false) {
  const token = session()?.sessionToken;
  return { ...(hasBody ? { 'Content-Type': 'application/json' } : {}), ...(token ? { 'X-ProjectPulse-Session': token } : {}) };
}
async function requestJson(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { ...headers(Boolean(options.body)), ...(options.headers ?? {}) } });
  const raw = await response.text();
  let payload = {};
  try { payload = raw ? JSON.parse(raw) : {}; } catch { payload = { message: raw }; }
  if (!response.ok) throw new Error(payload.message || `${path} returned HTTP ${response.status}`);
  return payload;
}
function dateTime(value) { return value ? new Date(value).toLocaleString() : 'Not available'; }
function label(status) { return status === 'pending_approval' ? 'Pending approval' : status === 'approved' ? 'Ready for temporary password' : status; }

export default function LocalAdminPasswordResetApprovalsPanel() {
  const [data, setData] = useState({ loading: true, approvals: [], error: null });
  const [message, setMessage] = useState('Ready.');
  const [busy, setBusy] = useState(false);
  const [decision, setDecision] = useState(null);
  const [notes, setNotes] = useState('');
  const [passwords, setPasswords] = useState({});

  const pending = useMemo(() => data.approvals.filter((item) => item.status === 'pending_approval').length, [data.approvals]);
  const ready = useMemo(() => data.approvals.filter((item) => item.status === 'approved').length, [data.approvals]);

  async function load() {
    setData((current) => ({ ...current, loading: true, error: null }));
    try {
      const result = await requestJson('/api/auth/password-reset/approvals');
      setData({ loading: false, approvals: result.approvals ?? [], error: null });
    } catch (error) {
      setData({ loading: false, approvals: [], error: error instanceof Error ? error.message : 'Unable to load password-reset approvals.' });
    }
  }

  useEffect(() => { load(); }, []);

  function openDecision(request, action) {
    setDecision({ request, action });
    setNotes(action === 'approve' ? 'Approved from Module 002 Approval Center.' : '');
  }

  async function submitDecision(event) {
    event.preventDefault();
    if (!decision) return;
    if (decision.action === 'decline' && !notes.trim()) return;
    setBusy(true);
    setMessage(`${decision.action === 'approve' ? 'Approving' : 'Declining'} password-reset request…`);
    try {
      const result = await requestJson(`/api/auth/password-reset/${decision.action}`, {
        method: 'POST',
        body: JSON.stringify({ resetRequestId: decision.request.resetRequestId, actionByEmail: session()?.username ?? '', notes: notes.trim() })
      });
      setMessage(result.message || 'Password-reset decision completed.');
      setDecision(null);
      setNotes('');
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
      await load();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Password-reset decision failed.');
    } finally {
      setBusy(false);
    }
  }

  async function complete(request) {
    const temporaryPassword = passwords[request.resetRequestId] ?? '';
    if (!temporaryPassword.trim()) {
      setMessage('Enter a temporary password before completing the reset.');
      return;
    }
    setBusy(true);
    setMessage('Setting temporary password…');
    try {
      const result = await requestJson('/api/auth/password-reset/complete', {
        method: 'POST',
        body: JSON.stringify({ resetRequestId: request.resetRequestId, temporaryPassword, actionByEmail: session()?.username ?? '', notes: 'Completed from Module 002 Approval Center.' })
      });
      setPasswords((current) => { const next = { ...current }; delete next[request.resetRequestId]; return next; });
      setMessage(result.message || 'Temporary password set.');
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
      await load();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to complete the reset.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="panel password-reset-approval-shell">
      <div className="manager-approval-header compact">
        <div>
          <p className="eyebrow">Password-reset approvals</p>
          <h2>Local administrator reset queue</h2>
          <p>Approve or decline local administrator reset requests, then set the required temporary password after approval.</p>
        </div>
        <button type="button" onClick={load} disabled={busy || data.loading}>Refresh</button>
      </div>

      <div className="manager-status-row">
        <span>Pending decision <strong>{pending}</strong></span>
        <span>Ready for password <strong>{ready}</strong></span>
        <span className="manager-status-message">{message}</span>
      </div>

      {data.error ? <div className="manager-empty-state error">{data.error}</div> : null}
      {data.loading ? <div className="manager-empty-state">Loading password-reset requests…</div> : null}
      {!data.loading && !data.error && data.approvals.length === 0 ? <div className="manager-empty-state">No password-reset approvals require action.</div> : null}

      <div className="password-reset-card-grid">
        {data.approvals.map((request) => (
          <article className="password-reset-card" key={request.resetRequestId}>
            <div className="password-reset-card-heading">
              <span className={`manager-status-badge ${request.status}`}>{label(request.status)}</span>
              <small>Requested {dateTime(request.requestedAt)}</small>
            </div>
            <h3>{request.accountDisplayName}</h3>
            <p>{request.accountEmail}</p>
            <dl>
              <div><dt>Requested by</dt><dd>{request.requestedByEmail}</dd></div>
              <div><dt>Expires</dt><dd>{dateTime(request.expiresAt)}</dd></div>
              <div><dt>Notes</dt><dd>{request.notes || 'No notes provided'}</dd></div>
            </dl>

            {request.status === 'pending_approval' ? (
              <div className="manager-row-actions">
                <button type="button" className="approve" onClick={() => openDecision(request, 'approve')} disabled={busy}>Approve</button>
                <button type="button" className="decline" onClick={() => openDecision(request, 'decline')} disabled={busy}>Decline</button>
              </div>
            ) : null}

            {request.status === 'approved' ? (
              <div className="password-reset-completion-box">
                <label htmlFor={`temporary-password-${request.resetRequestId}`}>Temporary password</label>
                <input id={`temporary-password-${request.resetRequestId}`} type="password" autoComplete="new-password" value={passwords[request.resetRequestId] ?? ''} placeholder="Enter temporary password" onChange={(event) => setPasswords((current) => ({ ...current, [request.resetRequestId]: event.target.value }))} />
                <button type="button" className="approve" onClick={() => complete(request)} disabled={busy}>Set temporary password</button>
                <small>The local administrator must change this password at next login.</small>
              </div>
            ) : null}
          </article>
        ))}
      </div>

      {decision ? (
        <div className="approval-modal-backdrop" role="presentation" onMouseDown={() => !busy && setDecision(null)}>
          <form className="approval-decision-modal" onSubmit={submitDecision} onMouseDown={(event) => event.stopPropagation()}>
            <p className="eyebrow">Password-reset decision</p>
            <h3>{decision.action === 'approve' ? 'Approve' : 'Decline'} {decision.request.accountEmail}</h3>
            <label>
              <span>{decision.action === 'decline' ? 'Required reason' : 'Approval note'}</span>
              <textarea autoFocus required={decision.action === 'decline'} rows="5" value={notes} onChange={(event) => setNotes(event.target.value)} placeholder={decision.action === 'decline' ? 'Explain why this request is being declined.' : 'Optional approval context.'} />
            </label>
            <div className="approval-modal-actions">
              <button type="button" onClick={() => setDecision(null)} disabled={busy}>Cancel</button>
              <button type="submit" className={decision.action === 'approve' ? 'approve' : 'decline'} disabled={busy || (decision.action === 'decline' && !notes.trim())}>{busy ? 'Processing…' : `${decision.action === 'approve' ? 'Approve' : 'Decline'} request`}</button>
            </div>
          </form>
        </div>
      ) : null}
    </section>
  );
}

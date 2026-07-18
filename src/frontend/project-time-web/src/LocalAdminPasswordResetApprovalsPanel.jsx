import { useEffect, useMemo, useState } from 'react';
import './manager-approval.css';

function getAuthSession() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders() {
  const session = getAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;
  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload)
  });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function formatDateTime(value) {
  if (!value) return 'Not available';
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
}

function getStatusLabel(status) {
  if (status === 'pending_approval') return 'Pending approval';
  if (status === 'approved') return 'Ready for temporary password';
  return status || 'Unknown';
}

export default function LocalAdminPasswordResetApprovalsPanel() {
  const [data, setData] = useState({ loading: true, approvals: [], error: null });
  const [status, setStatus] = useState('Ready.');
  const [busy, setBusy] = useState(false);
  const [temporaryPasswords, setTemporaryPasswords] = useState({});
  const [decisionModes, setDecisionModes] = useState({});
  const [decisionNotes, setDecisionNotes] = useState({});

  const pendingApprovalCount = useMemo(
    () => data.approvals.filter((request) => request.status === 'pending_approval').length,
    [data.approvals]
  );
  const readyForPasswordCount = useMemo(
    () => data.approvals.filter((request) => request.status === 'approved').length,
    [data.approvals]
  );

  async function loadApprovals() {
    setData((current) => ({ ...current, loading: true, error: null }));
    try {
      const result = await fetchJson('/api/auth/password-reset/approvals');
      setData({ loading: false, approvals: result.approvals ?? [], error: null });
    } catch (error) {
      setData({
        loading: false,
        approvals: [],
        error: error instanceof Error ? error.message : 'Unable to load local admin password reset approvals.'
      });
    }
  }

  useEffect(() => {
    void loadApprovals();
  }, []);

  function openDecision(request, mode) {
    setDecisionModes((current) => ({ ...current, [request.resetRequestId]: mode }));
    setDecisionNotes((current) => ({
      ...current,
      [request.resetRequestId]: current[request.resetRequestId]
        ?? (mode === 'approve' ? 'Approved from the Approval Center.' : '')
    }));
  }

  function closeDecision(request) {
    setDecisionModes((current) => ({ ...current, [request.resetRequestId]: '' }));
  }

  async function submitDecision(request) {
    const mode = decisionModes[request.resetRequestId];
    const notes = (decisionNotes[request.resetRequestId] ?? '').trim();
    if (mode === 'decline' && !notes) {
      setStatus('A decline reason is required before declining a password reset request.');
      return;
    }
    if (!['approve', 'decline'].includes(mode)) return;

    const session = getAuthSession();
    setBusy(true);
    setStatus(mode === 'approve' ? 'Approving password reset request...' : 'Declining password reset request...');
    try {
      const result = await postJson(
        mode === 'approve' ? '/api/auth/password-reset/approve' : '/api/auth/password-reset/decline',
        {
          resetRequestId: request.resetRequestId,
          actionByEmail: session?.username ?? '',
          notes
        }
      );
      setStatus(result.message ?? (mode === 'approve'
        ? 'Password reset request approved. Set a temporary password to complete the reset.'
        : 'Password reset request declined.'));
      closeDecision(request);
      await loadApprovals();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to process password reset request.');
    } finally {
      setBusy(false);
    }
  }

  async function completeReset(request) {
    const temporaryPassword = temporaryPasswords[request.resetRequestId] ?? '';
    if (!temporaryPassword.trim()) {
      setStatus('Enter a temporary password before completing the reset.');
      return;
    }

    const session = getAuthSession();
    setBusy(true);
    setStatus('Completing password reset and setting temporary password...');
    try {
      const result = await postJson('/api/auth/password-reset/complete', {
        resetRequestId: request.resetRequestId,
        temporaryPassword,
        actionByEmail: session?.username ?? '',
        notes: 'Temporary password set from the Approval Center.'
      });
      setTemporaryPasswords((current) => {
        const next = { ...current };
        delete next[request.resetRequestId];
        return next;
      });
      setStatus(result.message ?? 'Temporary password was set. The user must change it at next login.');
      await loadApprovals();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to complete password reset.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section id="password-reset-approvals" className="administrative-approval-panel">
      <div className="manager-approval-header compact">
        <div>
          <p className="eyebrow">Password Resets</p>
          <h2>Local administrator reset requests</h2>
          <p>Review requests in a focused inbox, record the decision, and set a temporary password only after approval.</p>
        </div>
        <div className="manager-toolbar">
          <button type="button" onClick={loadApprovals} disabled={busy || data.loading}>Refresh</button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Pending approval: <strong>{data.loading ? 'Loading...' : pendingApprovalCount}</strong></span>
        <span>Ready for temp password: <strong>{data.loading ? 'Loading...' : readyForPasswordCount}</strong></span>
        <span>Status: <strong>{status}</strong></span>
      </div>

      {data.error ? <div className="manager-empty-state error">{data.error}</div> : null}
      {data.loading ? <div className="manager-empty-state">Loading password reset inbox...</div> : null}
      {!data.loading && !data.error && data.approvals.length === 0 ? (
        <div className="manager-empty-state">No local administrator password reset actions are pending.</div>
      ) : null}

      <div className="admin-reset-list">
        {data.approvals.map((request) => {
          const decisionMode = decisionModes[request.resetRequestId] ?? '';
          const decisionLabel = decisionMode === 'approve' ? 'Approval note' : 'Reason for declining';

          return (
            <article className="admin-reset-card" key={request.resetRequestId}>
              <div className="admin-reset-card-header">
                <div>
                  <span className={`badge ${request.status === 'pending_approval' ? 'active' : ''}`}>
                    {getStatusLabel(request.status)}
                  </span>
                  <h3>{request.accountDisplayName}</h3>
                  <p>{request.accountEmail}</p>
                </div>
                <div className="admin-reset-request-meta">
                  <span>Requested by <strong>{request.requestedByEmail}</strong></span>
                  <span>Requested <strong>{formatDateTime(request.requestedAt)}</strong></span>
                  {request.approvedAt ? <span>Approved <strong>{formatDateTime(request.approvedAt)}</strong></span> : null}
                </div>
              </div>

              <div className="admin-reset-notes">
                <strong>Request notes</strong>
                <p>{request.notes || 'No notes provided.'}</p>
              </div>

              {request.status === 'pending_approval' ? (
                <div className="manager-row-actions">
                  <button type="button" className="approve" onClick={() => openDecision(request, 'approve')} disabled={busy}>
                    Approve request
                  </button>
                  <button type="button" className="decline" onClick={() => openDecision(request, 'decline')} disabled={busy}>
                    Decline request
                  </button>
                </div>
              ) : null}

              {request.status === 'pending_approval' && decisionMode ? (
                <div className="approval-inline-decision">
                  <label htmlFor={`reset-decision-${request.resetRequestId}`}>{decisionLabel}</label>
                  <textarea
                    id={`reset-decision-${request.resetRequestId}`}
                    value={decisionNotes[request.resetRequestId] ?? ''}
                    placeholder={decisionMode === 'approve'
                      ? 'Add an approval note for the audit record.'
                      : 'Explain why the request is being declined.'}
                    onChange={(event) => setDecisionNotes((current) => ({
                      ...current,
                      [request.resetRequestId]: event.target.value
                    }))}
                  />
                  <div className="manager-row-actions">
                    <button
                      type="button"
                      className={decisionMode === 'approve' ? 'approve' : 'decline'}
                      onClick={() => submitDecision(request)}
                      disabled={busy}
                    >
                      {decisionMode === 'approve' ? 'Confirm approval' : 'Confirm decline'}
                    </button>
                    <button type="button" onClick={() => closeDecision(request)} disabled={busy}>Cancel</button>
                  </div>
                </div>
              ) : null}

              {request.status === 'approved' ? (
                <div className="password-reset-completion-box">
                  <label htmlFor={`temporary-password-${request.resetRequestId}`}>Temporary password</label>
                  <input
                    id={`temporary-password-${request.resetRequestId}`}
                    type="password"
                    value={temporaryPasswords[request.resetRequestId] ?? ''}
                    placeholder="Set temporary password"
                    onChange={(event) => setTemporaryPasswords((current) => ({
                      ...current,
                      [request.resetRequestId]: event.target.value
                    }))}
                  />
                  <button type="button" className="approve" onClick={() => completeReset(request)} disabled={busy}>
                    Set temporary password
                  </button>
                  <small>The local administrator must change this password at next login.</small>
                </div>
              ) : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}

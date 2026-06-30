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
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...getProjectPulseAuthHeaders()
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

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
  if (status === 'approved') return 'Approved - set temporary password';
  return status || 'Unknown';
}

export default function LocalAdminPasswordResetApprovalsPanel() {
  const [data, setData] = useState({ loading: true, approvals: [], error: null });
  const [status, setStatus] = useState('Ready.');
  const [busy, setBusy] = useState(false);
  const [temporaryPasswords, setTemporaryPasswords] = useState({});

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
      setData({
        loading: false,
        approvals: result.approvals ?? [],
        error: null
      });
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

  async function approveReset(request) {
    const note = window.prompt('Approval note for this local admin password reset:', 'Approved from administrator approval queue.');
    if (note === null) return;

    const session = getAuthSession();
    setBusy(true);
    setStatus('Approving password reset request...');

    try {
      const result = await postJson('/api/auth/password-reset/approve', {
        resetRequestId: request.resetRequestId,
        actionByEmail: session?.username ?? '',
        notes: note.trim()
      });

      setStatus(result.message ?? 'Password reset request approved. Set a temporary password to complete the reset.');
      await loadApprovals();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to approve password reset request.');
    } finally {
      setBusy(false);
    }
  }

  async function declineReset(request) {
    const reason = window.prompt('Reason for declining this local admin password reset request:');
    if (reason === null) return;

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      setStatus('A decline reason is required before declining a password reset request.');
      return;
    }

    const session = getAuthSession();
    setBusy(true);
    setStatus('Declining password reset request...');

    try {
      const result = await postJson('/api/auth/password-reset/decline', {
        resetRequestId: request.resetRequestId,
        actionByEmail: session?.username ?? '',
        notes: trimmedReason
      });

      setStatus(result.message ?? 'Password reset request declined.');
      await loadApprovals();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to decline password reset request.');
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
        notes: 'Temporary password set from local admin password reset approval queue.'
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
    <div className="panel administrative-approval-panel">
      <div className="manager-approval-header compact">
        <div>
          <p className="eyebrow">Administrative Approval Requests</p>
          <h2>Local admin password reset approvals</h2>
          <p>
            Review local administrator password reset requests, approve or decline the request, then set a temporary password after approval.
          </p>
        </div>

        <div className="manager-toolbar">
          <button type="button" onClick={loadApprovals} disabled={busy || data.loading}>
            Refresh
          </button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Pending approval: <strong>{data.loading ? 'Loading...' : pendingApprovalCount}</strong></span>
        <span>Ready for temp password: <strong>{data.loading ? 'Loading...' : readyForPasswordCount}</strong></span>
        <span>Status: <strong>{status}</strong></span>
      </div>

      {data.error ? (
        <div className="manager-empty-state error">{data.error}</div>
      ) : null}

      {!data.loading && !data.error && data.approvals.length === 0 ? (
        <div className="manager-empty-state">No local admin password reset approvals are pending.</div>
      ) : null}

      {data.approvals.length > 0 ? (
        <div className="manager-table-wrap">
          <table className="manager-table administrative-approval-table">
            <thead>
              <tr>
                <th>Status</th>
                <th>Local account</th>
                <th>Requested by</th>
                <th>Requested</th>
                <th>Approved</th>
                <th>Notes</th>
                <th>Decision / Completion</th>
              </tr>
            </thead>
            <tbody>
              {data.approvals.map((request) => (
                <tr key={request.resetRequestId}>
                  <td>
                    <strong>{getStatusLabel(request.status)}</strong>
                    <span>{request.approvalDescription}</span>
                  </td>
                  <td>
                    <strong>{request.accountDisplayName}</strong>
                    <span>{request.accountEmail}</span>
                  </td>
                  <td>{request.requestedByEmail}</td>
                  <td>{formatDateTime(request.requestedAt)}</td>
                  <td>
                    {request.approvedAt ? formatDateTime(request.approvedAt) : 'Not approved yet'}
                    {request.approvedByEmail ? <span>{request.approvedByEmail}</span> : null}
                  </td>
                  <td>{request.notes || 'No notes provided'}</td>
                  <td>
                    {request.status === 'pending_approval' ? (
                      <div className="manager-row-actions">
                        <button type="button" className="approve" onClick={() => approveReset(request)} disabled={busy}>
                          Approve request
                        </button>
                        <button type="button" className="decline" onClick={() => declineReset(request)} disabled={busy}>
                          Decline
                        </button>
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
                        <small>The local admin will be required to change this password at next login.</small>
                      </div>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </div>
  );
}

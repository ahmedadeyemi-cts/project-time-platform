import { useEffect, useState } from 'react';
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

export default function LocalAdminPasswordResetApprovalsPanel() {
  const [data, setData] = useState({ loading: true, approvals: [], error: null });
  const [status, setStatus] = useState('Ready.');
  const [busy, setBusy] = useState(false);

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

      setStatus(result.message ?? 'Password reset request approved.');
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

  return (
    <div className="panel administrative-approval-panel">
      <div className="manager-approval-header compact">
        <div>
          <p className="eyebrow">Administrative Approval Requests</p>
          <h2>Local admin password reset approvals</h2>
          <p>
            Review password reset approval requests for local Project Pulse administrator accounts only.
            This does not apply to Entra ID users.
          </p>
        </div>

        <div className="manager-toolbar">
          <button type="button" onClick={loadApprovals} disabled={busy || data.loading}>
            Refresh
          </button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Pending local reset requests: <strong>{data.loading ? 'Loading...' : data.approvals.length}</strong></span>
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
                <th>Request</th>
                <th>Local account</th>
                <th>Requested by</th>
                <th>Requested</th>
                <th>Expires</th>
                <th>Notes</th>
                <th>Decision</th>
              </tr>
            </thead>
            <tbody>
              {data.approvals.map((request) => (
                <tr key={request.resetRequestId}>
                  <td>
                    <strong>{request.approvalTitle}</strong>
                    <span>{request.approvalDescription}</span>
                  </td>
                  <td>
                    <strong>{request.accountDisplayName}</strong>
                    <span>{request.accountEmail}</span>
                  </td>
                  <td>{request.requestedByEmail}</td>
                  <td>{formatDateTime(request.requestedAt)}</td>
                  <td>{formatDateTime(request.expiresAt)}</td>
                  <td>{request.notes || 'No notes provided'}</td>
                  <td>
                    <div className="manager-row-actions">
                      <button type="button" className="approve" onClick={() => approveReset(request)} disabled={busy}>
                        Approve reset
                      </button>
                      <button type="button" className="decline" onClick={() => declineReset(request)} disabled={busy}>
                        Decline
                      </button>
                    </div>
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

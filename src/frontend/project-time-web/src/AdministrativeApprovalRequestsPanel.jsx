import { useEffect, useState } from 'react';


function getProjectPulseAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};

    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}



async function readApiErrorMessage(response, path) {
  const raw = await response.text();

  if (!raw) {
    return `${path} returned HTTP ${response.status}`;
  }

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
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
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
    return value;
  }
}

export default function AdministrativeApprovalRequestsPanel() {
  const [approvalData, setApprovalData] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('Ready');
  const [isWorking, setIsWorking] = useState(false);

  async function loadAdministrativeApprovals() {
    setApprovalData({ loading: true, data: null, error: null });

    try {
      const result = await fetchJson('/api/auth/password-reset/approvals');
      setApprovalData({ loading: false, data: result, error: null });
    } catch (error) {
      setApprovalData({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Failed to load administrative approvals'
      });
    }
  }

  useEffect(() => {
    loadAdministrativeApprovals();
  }, []);

  async function approveRequest(item) {
    const notes = window.prompt('Approval note for this local admin password reset:', 'Approved from approval queue.');
    if (notes === null) return;

    setIsWorking(true);
    setActionStatus('Approving password reset request...');

    try {
      const result = await postJson('/api/auth/password-reset/approve', {
        resetRequestId: item.resetRequestId,
        actionByEmail: 'ahmed.adeyemi@ussignal.com',
        notes
      });

      setActionStatus(result.message ?? 'Password reset approved');
      await loadAdministrativeApprovals();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Approval failed');
    } finally {
      setIsWorking(false);
    }
  }

  async function declineRequest(item) {
    const notes = window.prompt('Reason for declining this password reset request:');
    if (notes === null) return;

    const cleanNotes = notes.trim();
    if (!cleanNotes) {
      setActionStatus('A decline reason is required.');
      return;
    }

    setIsWorking(true);
    setActionStatus('Declining password reset request...');

    try {
      const result = await postJson('/api/auth/password-reset/decline', {
        resetRequestId: item.resetRequestId,
        actionByEmail: 'ahmed.adeyemi@ussignal.com',
        notes: cleanNotes
      });

      setActionStatus(result.message ?? 'Password reset declined');
      await loadAdministrativeApprovals();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Decline failed');
    } finally {
      setIsWorking(false);
    }
  }

  const approvals = approvalData.data?.approvals ?? [];

  return (
    <section className="administrative-approval-panel">
      <div className="manager-approval-header compact">
        <div>
          <p className="eyebrow">Administrative Approval Requests</p>
          <h2>Password reset approvals</h2>
          <p>
            Review local administrator password reset requests. Approval does not set the password yet; the temporary password step is handled in the local password hashing phase.
          </p>
        </div>

        <div className="manager-toolbar">
          <button type="button" onClick={loadAdministrativeApprovals} disabled={isWorking}>
            Refresh
          </button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Pending admin requests: <strong>{approvalData.loading ? 'Loading...' : approvals.length}</strong></span>
        <span>Action: <strong>{actionStatus}</strong></span>
      </div>

      {approvalData.error ? (
        <div className="manager-empty-state error">{approvalData.error}</div>
      ) : null}

      {!approvalData.loading && !approvalData.error && approvals.length === 0 ? (
        <div className="manager-empty-state">No administrative approval requests are currently pending.</div>
      ) : null}

      {approvals.length > 0 ? (
        <div className="manager-table-wrap">
          <table className="manager-table administrative-approval-table">
            <thead>
              <tr>
                <th>Request</th>
                <th>Account</th>
                <th>Requested By</th>
                <th>Requested</th>
                <th>Expires</th>
                <th>Notes</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {approvals.map((item) => (
                <tr key={item.resetRequestId}>
                  <td>
                    <strong>{item.approvalTitle}</strong>
                    <span>{item.approvalDescription}</span>
                  </td>
                  <td>
                    <strong>{item.accountDisplayName}</strong>
                    <span>{item.accountEmail}</span>
                  </td>
                  <td>{item.requestedByEmail}</td>
                  <td>{formatDateTime(item.requestedAt)}</td>
                  <td>{formatDateTime(item.expiresAt)}</td>
                  <td>{item.notes || 'No notes provided'}</td>
                  <td>
                    <div className="manager-row-actions">
                      <button type="button" className="approve" disabled={isWorking} onClick={() => approveRequest(item)}>
                        Approve reset
                      </button>
                      <button type="button" className="decline" disabled={isWorking} onClick={() => declineRequest(item)}>
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
    </section>
  );
}

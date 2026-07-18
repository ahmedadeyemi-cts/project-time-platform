import { useEffect, useState } from 'react';
import ManagerApprovalPanel from './ManagerApprovalPanel.jsx';
import LocalAdminPasswordResetApprovalsPanel from './LocalAdminPasswordResetApprovalsPanel.jsx';
import './approval-center.css';

function headers() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

export default function ApprovalCenter() {
  const [summary, setSummary] = useState({ loading: true, data: null, error: null });
  const [activeTab, setActiveTab] = useState('time');

  async function loadSummary() {
    try {
      const response = await fetch('/api/manager/approval-count', { headers: headers() });
      const raw = await response.text();
      const payload = raw ? JSON.parse(raw) : {};
      if (!response.ok) throw new Error(payload.message || `Approval summary returned HTTP ${response.status}`);
      setSummary({ loading: false, data: payload, error: null });
      if (!payload.access?.canViewTimeApprovals && payload.access?.canViewPasswordResetApprovals) setActiveTab('password-reset');
    } catch (error) {
      setSummary({ loading: false, data: null, error: error instanceof Error ? error.message : 'Unable to load Approval Center.' });
    }
  }

  useEffect(() => {
    loadSummary();
    window.addEventListener('projectpulse:approval-queue-changed', loadSummary);
    return () => window.removeEventListener('projectpulse:approval-queue-changed', loadSummary);
  }, []);

  if (summary.loading) return <section className="panel approval-center-loading">Loading Approval Center…</section>;
  if (summary.error) return <section className="panel approval-center-loading error">{summary.error}</section>;

  const data = summary.data;
  const access = data?.access ?? {};
  const timeCount = Number(data?.submittedTimePending ?? 0);
  const resetPending = Number(data?.localResetPendingApproval ?? 0);
  const resetReady = Number(data?.localResetReadyForTempPassword ?? 0);

  return (
    <div className="approval-center-shell">
      <section className="panel approval-center-hero">
        <div>
          <p className="eyebrow">MODULE 002</p>
          <h2>Approval Center</h2>
          <p>Review only the approval items assigned to your role and scope. Draft time is never counted as an approval request.</p>
        </div>
        <div className="approval-center-scope">
          <span>Approval scope</span>
          <strong>{access.scopeLabel}</strong>
          <small>{access.primaryRoleLabel}</small>
        </div>
      </section>

      <section className="approval-summary-grid" aria-label="Approval summary">
        {access.canViewTimeApprovals ? (
          <article><span>Time requiring action</span><strong>{timeCount}</strong><small>Submitted days only</small></article>
        ) : null}
        {access.canViewPasswordResetApprovals ? (
          <>
            <article><span>Password reset approval</span><strong>{resetPending}</strong><small>Pending decisions</small></article>
            <article><span>Ready for password</span><strong>{resetReady}</strong><small>Approved requests to complete</small></article>
          </>
        ) : null}
        <article><span>Total requiring action</span><strong>{Number(data?.actionableTotal ?? 0)}</strong><small>Role-specific count</small></article>
      </section>

      <nav className="approval-center-tabs" aria-label="Approval types">
        {access.canViewTimeApprovals ? (
          <button type="button" className={activeTab === 'time' ? 'active' : ''} onClick={() => setActiveTab('time')}>
            Time approvals <span>{timeCount}</span>
          </button>
        ) : null}
        {access.canViewPasswordResetApprovals ? (
          <button type="button" className={activeTab === 'password-reset' ? 'active' : ''} onClick={() => setActiveTab('password-reset')}>
            Password resets <span>{resetPending + resetReady}</span>
          </button>
        ) : null}
      </nav>

      {activeTab === 'time' && access.canViewTimeApprovals ? <ManagerApprovalPanel /> : null}
      {activeTab === 'password-reset' && access.canViewPasswordResetApprovals ? <LocalAdminPasswordResetApprovalsPanel /> : null}
    </div>
  );
}

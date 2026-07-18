import { useEffect, useRef, useState } from 'react';
import './approval-mailbox.css';

function sessionHeaders() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

export default function ApprovalMailbox() {
  const [summary, setSummary] = useState(null);
  const [open, setOpen] = useState(false);
  const shellRef = useRef(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const response = await fetch('/api/manager/approval-count', { headers: sessionHeaders() });
        if (response.status === 401 || response.status === 403) {
          if (!cancelled) setSummary(null);
          return;
        }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const payload = await response.json();
        if (!cancelled) setSummary(payload);
      } catch {
        if (!cancelled) setSummary(null);
      }
    }

    function closeOnOutsideClick(event) {
      if (shellRef.current && !shellRef.current.contains(event.target)) setOpen(false);
    }

    load();
    const timer = window.setInterval(load, 30000);
    window.addEventListener('projectpulse:approval-queue-changed', load);
    document.addEventListener('mousedown', closeOnOutsideClick);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
      window.removeEventListener('projectpulse:approval-queue-changed', load);
      document.removeEventListener('mousedown', closeOnOutsideClick);
    };
  }, []);

  if (!summary?.access) return null;

  const total = Number(summary.actionableTotal ?? summary.totalPendingCount ?? 0);
  const time = Number(summary.submittedTimePending ?? 0);
  const resetPending = Number(summary.localResetPendingApproval ?? 0);
  const resetReady = Number(summary.localResetReadyForTempPassword ?? 0);

  return (
    <div className="approval-mailbox-shell" ref={shellRef}>
      <button
        type="button"
        className={open ? 'approval-mailbox-button active' : 'approval-mailbox-button'}
        aria-label={`${total} approval items require attention`}
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <span className="approval-mailbox-icon" aria-hidden="true">✉</span>
        <span className="approval-mailbox-label">Approvals</span>
        {total > 0 ? <span className="approval-mailbox-badge">{total > 99 ? '99+' : total}</span> : null}
      </button>

      {open ? (
        <div className="approval-mailbox-popover">
          <div className="approval-mailbox-heading">
            <div>
              <strong>Approval Inbox</strong>
              <small>{summary.access.scopeLabel}</small>
            </div>
            <span className={total > 0 ? 'approval-mailbox-total pending' : 'approval-mailbox-total'}>{total}</span>
          </div>

          <div className="approval-mailbox-breakdown">
            {summary.access.canViewTimeApprovals ? (
              <div><span>Time approvals</span><strong>{time}</strong></div>
            ) : null}
            {summary.access.canViewPasswordResetApprovals ? (
              <>
                <div><span>Password-reset approvals</span><strong>{resetPending}</strong></div>
                <div><span>Ready for temporary password</span><strong>{resetReady}</strong></div>
              </>
            ) : null}
          </div>

          <a href="#manager-approval" onClick={() => setOpen(false)}>Open Approval Center</a>
        </div>
      ) : null}
    </div>
  );
}

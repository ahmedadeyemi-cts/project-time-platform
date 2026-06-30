import { useEffect, useState } from 'react';
import './approval-notification.css';


function getProjectPulseSessionHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};

    const session = JSON.parse(raw);
    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
  } catch {
    return {};
  }
}


async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseSessionHeaders() });
  if (!response.ok) throw new Error(`${path} returned HTTP ${response.status}`);
  return response.json();
}

export default function ApprovalNotificationBanner() {
  const [summary, setSummary] = useState({ loading: true, data: null, error: null });

  useEffect(() => {
    let cancelled = false;

    async function loadSummary() {
      try {
        const result = await fetchJson('/api/manager/approval-summary');
        if (!cancelled) setSummary({ loading: false, data: result, error: null });
      } catch (error) {
        if (!cancelled) setSummary({ loading: false, data: null, error: error instanceof Error ? error.message : 'Unable to load approvals summary' });
      }
    }

    loadSummary();
    const timer = window.setInterval(loadSummary, 60000);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  if (summary.loading || summary.error) return null;

  const pendingManagerApprovals = Number(summary.data?.pendingManagerApprovals ?? 0);
  const pendingProjectApprovals = Number(summary.data?.pendingProjectApprovals ?? 0);
  const totalPending = pendingManagerApprovals + pendingProjectApprovals;

  if (totalPending <= 0) return null;

  return (
    <aside className="approval-notification" aria-label="Approval notification">
      <div>
        <strong>{totalPending}</strong>
        <span>pending approval{totalPending === 1 ? '' : 's'}</span>
      </div>
      <a href="#manager-approval">Open Approval Inbox</a>
    </aside>
  );
}

import { useEffect, useMemo, useState } from 'react';
import ManagerApprovalPanel from './ManagerApprovalPanel.jsx';
import ProjectManagerApprovalPanel from './ProjectManagerApprovalPanel.jsx';
import LocalAdminPasswordResetApprovalsPanel from './LocalAdminPasswordResetApprovalsPanel.jsx';
import './approval-center.css';

const TAB_LABELS = {
  manager: 'Manager Review',
  pm: 'PM Review',
  password: 'Password Resets',
  history: 'History'
};

export default function ApprovalCenter({
  canViewManagerApprovalPanel = false,
  canViewPmApprovalPanel = false,
  canViewLocalAdminPasswordResetApprovals = false
}) {
  const availableTabs = useMemo(() => {
    const tabs = [];
    if (canViewManagerApprovalPanel) tabs.push('manager');
    if (canViewPmApprovalPanel) tabs.push('pm');
    if (canViewLocalAdminPasswordResetApprovals) tabs.push('password');
    if (canViewManagerApprovalPanel) tabs.push('history');
    return tabs;
  }, [canViewManagerApprovalPanel, canViewPmApprovalPanel, canViewLocalAdminPasswordResetApprovals]);

  const [activeTab, setActiveTab] = useState(availableTabs[0] ?? 'manager');

  useEffect(() => {
    if (!availableTabs.includes(activeTab)) {
      setActiveTab(availableTabs[0] ?? 'manager');
    }
  }, [activeTab, availableTabs]);

  if (availableTabs.length === 0) return null;

  return (
    <section id="manager-approval" className="panel approval-center-shell">
      <header className="approval-center-header">
        <div>
          <p className="eyebrow">Approval Center</p>
          <h2>Review, decide, and track approval work</h2>
          <p>
            Time approvals, local administrator password resets, and decision history are organized into focused inboxes without covering the dashboard.
          </p>
        </div>
        <div className="approval-center-flow" aria-label="Approval sequence">
          <span>Engineer submits</span>
          <span aria-hidden="true">→</span>
          <span>Manager reviews</span>
          <span aria-hidden="true">→</span>
          <span>PM reviews</span>
        </div>
      </header>

      <div className="approval-center-tabs" role="tablist" aria-label="Approval Center inboxes">
        {availableTabs.map((tab) => (
          <button
            key={tab}
            type="button"
            role="tab"
            aria-selected={activeTab === tab}
            className={activeTab === tab ? 'active' : ''}
            onClick={() => setActiveTab(tab)}
          >
            {TAB_LABELS[tab]}
          </button>
        ))}
      </div>

      <div className="approval-center-content" role="tabpanel">
        {activeTab === 'manager' && canViewManagerApprovalPanel ? (
          <ManagerApprovalPanel mode="review" />
        ) : null}

        {activeTab === 'pm' && canViewPmApprovalPanel ? (
          <ProjectManagerApprovalPanel />
        ) : null}

        {activeTab === 'password' && canViewLocalAdminPasswordResetApprovals ? (
          <LocalAdminPasswordResetApprovalsPanel />
        ) : null}

        {activeTab === 'history' && canViewManagerApprovalPanel ? (
          <ManagerApprovalPanel mode="history" />
        ) : null}
      </div>
    </section>
  );
}

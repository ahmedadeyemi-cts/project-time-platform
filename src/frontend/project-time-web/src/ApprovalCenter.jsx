import { useEffect, useMemo, useState } from 'react';
import ManagerApprovalPanel from './ManagerApprovalPanel.jsx';
import ProjectManagerApprovalPanel from './ProjectManagerApprovalPanel.jsx';
import PtcTimeEntryCorrectionPanel from './PtcTimeEntryCorrectionPanel.jsx';
import LocalAdminPasswordResetApprovalsPanel from './LocalAdminPasswordResetApprovalsPanel.jsx';
import './approval-center.css';

const TAB_LABELS = {
  manager: 'Time approvals',
  pm: 'PM Review',
  corrections: 'PTC Corrections',
  password: 'Password resets',
  history: 'History'
};

const PM_ROLE_CODES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT'
]);

const PTC_ROLE_CODES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR'
]);

function sessionHeaders() {
  try {
    const session = JSON.parse(
      window.localStorage.getItem(
        'projectPulseAuthSession'
      ) || 'null'
    );

    return session?.sessionToken
      ? {
          'X-ProjectPulse-Session':
            session.sessionToken
        }
      : {};
  } catch {
    return {};
  }
}

async function loadApprovalSummary() {
  const response = await fetch(
    '/api/manager/approval-count',
    {
      headers: sessionHeaders()
    }
  );

  const raw = await response.text();

  let payload = {};

  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = {
      message: raw
    };
  }

  if (!response.ok) {
    throw new Error(
      payload.message
      || `Approval summary returned HTTP ${response.status}`
    );
  }

  return payload;
}

export default function ApprovalCenter() {
  const [summary, setSummary] = useState({
    loading: true,
    data: null,
    error: null
  });

  const [activeTab, setActiveTab] = useState(
    'manager'
  );

  async function refreshSummary() {
    setSummary((current) => ({
      ...current,
      loading: true,
      error: null
    }));

    try {
      const data = await loadApprovalSummary();

      setSummary({
        loading: false,
        data,
        error: null
      });
    } catch (error) {
      setSummary({
        loading: false,
        data: null,
        error:
          error instanceof Error
            ? error.message
            : 'Unable to load Approval Center.'
      });
    }
  }

  useEffect(() => {
    void refreshSummary();

    window.addEventListener(
      'projectpulse:approval-queue-changed',
      refreshSummary
    );

    return () => {
      window.removeEventListener(
        'projectpulse:approval-queue-changed',
        refreshSummary
      );
    };
  }, []);

  const data = summary.data;
  const access = data?.access ?? {};

  const roleCodes = useMemo(
    () => new Set(
      (access.roleCodes ?? [])
        .map((role) =>
          String(role).trim().toUpperCase()
        )
        .filter(Boolean)
    ),
    [access.roleCodes]
  );

  const canViewPmApprovalPanel = useMemo(
    () => [...roleCodes].some(
      (role) => PM_ROLE_CODES.has(role)
    ),
    [roleCodes]
  );

  const canViewPtcTimeEntryCorrections =
    useMemo(
      () => [...roleCodes].some(
        (role) => PTC_ROLE_CODES.has(role)
      ),
      [roleCodes]
    );

  const availableTabs = useMemo(() => {
    const tabs = [];

    if (access.canViewTimeApprovals) {
      tabs.push('manager');
    }

    if (canViewPmApprovalPanel) {
      tabs.push('pm');
    }

    if (canViewPtcTimeEntryCorrections) {
      tabs.push('corrections');
    }

    if (access.canViewPasswordResetApprovals) {
      tabs.push('password');
    }

    if (access.canViewTimeApprovals) {
      tabs.push('history');
    }

    return tabs;
  }, [
    access.canViewTimeApprovals,
    access.canViewPasswordResetApprovals,
    canViewPmApprovalPanel,
    canViewPtcTimeEntryCorrections
  ]);

  useEffect(() => {
    if (!availableTabs.includes(activeTab)) {
      setActiveTab(
        availableTabs[0] ?? 'manager'
      );
    }
  }, [activeTab, availableTabs]);

  if (summary.loading) {
    return (
      <section
        id="manager-approval"
        className="panel approval-center-loading"
      >
        Loading Approval Center…
      </section>
    );
  }

  if (summary.error) {
    return (
      <section
        id="manager-approval"
        className="panel approval-center-loading error"
      >
        {summary.error}
      </section>
    );
  }

  if (availableTabs.length === 0) {
    return (
      <section
        id="manager-approval"
        className="panel approval-center-loading"
      >
        Approval Center access is not available for
        this account.
      </section>
    );
  }

  const timeCount = Number(
    data?.submittedTimePending ?? 0
  );

  const resetPending = Number(
    data?.localResetPendingApproval ?? 0
  );

  const resetReady = Number(
    data?.localResetReadyForTempPassword ?? 0
  );

  const total = Number(
    data?.actionableTotal ?? 0
  );

  return (
    <section
      id="manager-approval"
      className="approval-center-shell"
    >
      <section className="panel approval-center-hero">
        <div>
          <p className="eyebrow">MODULE 002</p>
          <h2>Approval Center</h2>
          <p>
            Review only the approval work assigned to
            your authenticated role and organizational
            scope. Draft time is never counted as an
            approval request.
          </p>
        </div>

        <div className="approval-center-scope">
          <span>Approval scope</span>
          <strong>
            {access.scopeLabel
              || 'Assigned approvals'}
          </strong>
          <small>
            {access.primaryRoleLabel
              || 'Authorized reviewer'}
          </small>
        </div>
      </section>

      <section
        className="approval-summary-grid"
        aria-label="Approval summary"
      >
        {access.canViewTimeApprovals ? (
          <article>
            <span>Time requiring action</span>
            <strong>{timeCount}</strong>
            <small>Submitted days only</small>
          </article>
        ) : null}

        {access.canViewPasswordResetApprovals ? (
          <>
            <article>
              <span>Password reset approval</span>
              <strong>{resetPending}</strong>
              <small>Pending decisions</small>
            </article>

            <article>
              <span>Ready for password</span>
              <strong>{resetReady}</strong>
              <small>
                Approved requests to complete
              </small>
            </article>
          </>
        ) : null}

        <article>
          <span>Total requiring action</span>
          <strong>{total}</strong>
          <small>Role-specific actionable work</small>
        </article>
      </section>

      <nav
        className="approval-center-tabs"
        aria-label="Approval Center inboxes"
      >
        {availableTabs.map((tab) => {
          const count =
            tab === 'manager'
              ? timeCount
              : tab === 'password'
                ? resetPending + resetReady
                : null;

          return (
            <button
              type="button"
              key={tab}
              className={
                activeTab === tab
                  ? 'active'
                  : ''
              }
              aria-selected={activeTab === tab}
              onClick={() => setActiveTab(tab)}
            >
              {TAB_LABELS[tab]}
              {count !== null ? (
                <span>{count}</span>
              ) : null}
            </button>
          );
        })}
      </nav>

      <div className="approval-center-content">
        {activeTab === 'manager'
          && access.canViewTimeApprovals ? (
            <ManagerApprovalPanel mode="review" />
          ) : null}

        {activeTab === 'pm'
          && canViewPmApprovalPanel ? (
            <ProjectManagerApprovalPanel />
          ) : null}

        {activeTab === 'corrections'
          && canViewPtcTimeEntryCorrections ? (
            <PtcTimeEntryCorrectionPanel />
          ) : null}

        {activeTab === 'password'
          && access.canViewPasswordResetApprovals ? (
            <LocalAdminPasswordResetApprovalsPanel />
          ) : null}

        {activeTab === 'history'
          && access.canViewTimeApprovals ? (
            <ManagerApprovalPanel mode="history" />
          ) : null}
      </div>
    </section>
  );
}

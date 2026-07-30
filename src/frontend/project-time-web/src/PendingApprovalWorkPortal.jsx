import { useCallback, useEffect, useLayoutEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  STAGES,
  completePendingWork,
  displayDate,
  displayDateTime,
  ensureHost,
  fetchPendingWork,
  formatHours,
  groupKey,
  itemKey,
  readPendingTarget,
  rememberPendingTarget
} from './pending-approval-work-support.js';
import './pending-approval-work.css';


function DashboardPendingWork({ data, loading, error, openWeek }) {
  const total = Number(data?.totalPending || 0);
  const weeks = Array.isArray(data?.pendingWeeks) ? data.pendingWeeks : [];

  return (
    <section className="pending-approval-dashboard" aria-label="Pending approval work across all weeks">
      <button
        type="button"
        className="pending-approval-dashboard-total"
        onClick={() => {
          const first = weeks[0];
          if (first) openWeek(first.stage, first.weekStart);
          else window.location.hash = 'manager-approval';
        }}
      >
        <span>Time approvals across all weeks</span>
        <strong>{loading || error ? '—' : total}</strong>
      </button>

      {!loading && error ? (
        <p role="alert">Pending approval totals are temporarily unavailable. Open Approval Center to continue working.</p>
      ) : null}

      {!loading && !error && total === 0 ? (
        <p>No pending time approvals were found in any week.</p>
      ) : null}

      {!loading && !error && total > 0 ? (
        <div className="pending-approval-dashboard-weeks">
          {weeks.slice(0, 4).map((week) => (
            <button
              type="button"
              key={groupKey(week.stage, week.weekStart)}
              onClick={() => openWeek(week.stage, week.weekStart)}
            >
              <span>{STAGES[week.stage]?.shortLabel || week.stage}</span>
              <strong>{week.count}</strong>
              <small>Week of {displayDate(week.weekStart, { year: false })}</small>
            </button>
          ))}
          {weeks.length > 4 ? (
            <a href="#manager-approval">View {weeks.length - 4} more pending week(s) →</a>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function PendingStageCard({ stage, count }) {
  const config = STAGES[stage];
  if (!config) return null;
  return (
    <article className={`pending-approval-stage-card stage-${stage}`}>
      <span>{config.label}</span>
      <strong>{Number(count || 0)}</strong>
      <small>{config.help}</small>
    </article>
  );
}

function PendingWeekGroup({
  week,
  items,
  selected,
  busy,
  targeted,
  onToggle,
  onToggleAll,
  onApproveSelected,
  onApproveAll,
  onOpenWeek
}) {
  const key = groupKey(week.stage, week.weekStart);
  const selectedCount = items.filter((item) => selected.has(itemKey(item))).length;
  const allSelected = items.length > 0 && selectedCount === items.length;
  const stage = STAGES[week.stage] || { label: week.stage, shortLabel: week.stage };

  return (
    <article
      id={`pending-approval-${week.stage}-${week.weekStart}`}
      className={`pending-approval-week${targeted ? ' is-targeted' : ''}`}
      data-pending-approval-group={key}
    >
      <header>
        <div>
          <p className="eyebrow">{stage.label}</p>
          <h4>Week of {displayDate(week.weekStart)}</h4>
          <p>
            {week.count} pending day(s) · {formatHours(week.totalHours)} hour(s) · oldest item {displayDateTime(week.oldestPendingAt)}
          </p>
        </div>
        <button
          type="button"
          className="pending-approval-open-week"
          onClick={() => onOpenWeek(week.stage, week.weekStart)}
        >
          Open pending week
        </button>
      </header>

      <div className="pending-approval-week-actions">
        <button
          type="button"
          className="secondary-action"
          disabled={Boolean(busy) || items.length === 0}
          onClick={() => onToggleAll(items, !allSelected)}
        >
          {allSelected ? 'Clear selection' : `Select all ${items.length}`}
        </button>
        <button
          type="button"
          className="secondary-action"
          disabled={Boolean(busy) || selectedCount === 0}
          onClick={() => onApproveSelected(week, items)}
        >
          {busy === `${key}:selected` ? 'Approving…' : `Approve selected (${selectedCount})`}
        </button>
        <button
          type="button"
          className="primary-action"
          disabled={Boolean(busy) || items.length === 0}
          onClick={() => onApproveAll(week)}
        >
          {busy === `${key}:all` ? 'Approving…' : `Approve all ${items.length} for week`}
        </button>
      </div>

      <div className="pending-approval-items">
        {items.map((item) => {
          const keyValue = itemKey(item);
          return (
            <label key={keyValue} className="pending-approval-item">
              <input
                type="checkbox"
                checked={selected.has(keyValue)}
                disabled={Boolean(busy)}
                onChange={(event) => onToggle(keyValue, event.target.checked)}
              />
              <span>
                <strong>{item.resourceName}</strong>
                <small>{displayDate(item.workDate)} · {formatHours(item.totalHours)} hour(s)</small>
                <small>
                  {item.projectCodes || item.projectNames || 'Non-project time'}
                  {item.entryCount ? ` · ${item.entryCount} entry or entries` : ''}
                </small>
              </span>
            </label>
          );
        })}
      </div>
    </article>
  );
}

function ApprovalCenterPendingWork({ data, loading, error, actionStatus, busy, selected, setSelected, complete, openWeek }) {
  const route = readPendingTarget();
  const items = Array.isArray(data?.items) ? data.items : [];
  const weeks = useMemo(() => {
    const source = Array.isArray(data?.pendingWeeks) ? [...data.pendingWeeks] : [];
    return source.sort((left, right) => {
      const leftTarget = left.stage === route.pendingStage && left.weekStart === route.weekStart;
      const rightTarget = right.stage === route.pendingStage && right.weekStart === route.weekStart;
      if (leftTarget !== rightTarget) return leftTarget ? -1 : 1;
      return String(left.weekStart).localeCompare(String(right.weekStart));
    });
  }, [data, route.pendingStage, route.weekStart]);

  useEffect(() => {
    if (!route.pendingStage || !route.weekStart) return;
    const id = `pending-approval-${route.pendingStage}-${route.weekStart}`;
    window.setTimeout(() => {
      document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }, 100);
  }, [route.pendingStage, route.weekStart, weeks.length]);

  const toggle = (key, checked) => {
    setSelected((current) => {
      const next = new Set(current);
      if (checked) next.add(key);
      else next.delete(key);
      return next;
    });
  };

  const toggleAll = (groupItems, checked) => {
    setSelected((current) => {
      const next = new Set(current);
      for (const item of groupItems) {
        const key = itemKey(item);
        if (checked) next.add(key);
        else next.delete(key);
      }
      return next;
    });
  };

  return (
    <section className="pending-approval-center" aria-label="All pending approval work">
      <header className="pending-approval-center-header">
        <div>
          <p className="eyebrow">ALL WEEKS · ALL ASSIGNED STAGES</p>
          <h3>Pending approval work</h3>
          <p>
            This list includes earlier weeks, not just the current week. Select a pending week to open the exact work, or approve the entire authorized week without entering a comment.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={() => window.dispatchEvent(new CustomEvent('projectpulse:approval-work-refresh'))}>
          Refresh
        </button>
      </header>

      {actionStatus?.text ? (
        <p
          className={`pending-approval-message${actionStatus.type === 'error' ? ' error' : ''}`}
          role={actionStatus.type === 'error' ? 'alert' : 'status'}
        >
          {actionStatus.text}
        </p>
      ) : null}
      {error ? <p className="pending-approval-message error" role="alert">{error}</p> : null}

      <div className="pending-approval-stage-grid">
        <PendingStageCard stage="manager" count={data?.stageCounts?.manager} />
        <PendingStageCard stage="pm" count={data?.stageCounts?.pm} />
        <PendingStageCard stage="ptc" count={data?.stageCounts?.ptc} />
        <article className="pending-approval-stage-card total">
          <span>Total pending</span>
          <strong>{loading ? '—' : Number(data?.totalPending || 0)}</strong>
          <small>{data?.access?.scopeLabel || 'Your authorized approval scope'}</small>
        </article>
      </div>

      <p className="pending-approval-no-comment">
        Approvals use a server-generated audit note. No approval comment is required. Rejections and returns still require a reason in the existing role workspace.
      </p>

      {loading ? <p className="pending-approval-loading">Loading pending weeks…</p> : null}
      {!loading && !error && weeks.length === 0 ? (
        <div className="pending-approval-empty">
          <strong>No pending time approvals</strong>
          <span>No assigned approval stage contains unfinished work in any week.</span>
        </div>
      ) : null}

      <div className="pending-approval-week-list">
        {weeks.map((week) => {
          const groupItems = items.filter((item) => item.stage === week.stage && item.weekStart === week.weekStart);
          const targeted = week.stage === route.pendingStage && week.weekStart === route.weekStart;
          return (
            <PendingWeekGroup
              key={groupKey(week.stage, week.weekStart)}
              week={week}
              items={groupItems}
              selected={selected}
              busy={busy}
              targeted={targeted}
              onToggle={toggle}
              onToggleAll={toggleAll}
              onApproveSelected={(group, currentItems) => {
                const chosen = currentItems
                  .filter((item) => selected.has(itemKey(item)))
                  .map((item) => ({ timesheetId: item.timesheetId, workDate: item.workDate }));
                complete(group, chosen, 'selected');
              }}
              onApproveAll={(group) => complete(group, null, 'all')}
              onOpenWeek={openWeek}
            />
          );
        })}
      </div>
    </section>
  );
}

export default function PendingApprovalWorkPortal() {
  const [dashboardHost, setDashboardHost] = useState(null);
  const [approvalHost, setApprovalHost] = useState(null);
  const [state, setState] = useState({ loading: true, data: null, error: null, authorized: true });
  const [actionStatus, setActionStatus] = useState({ type: '', text: '' });
  const [busy, setBusy] = useState('');
  const [selected, setSelected] = useState(new Set());

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      const data = await fetchPendingWork();
      setState({ loading: false, data, error: null, authorized: true });
      setSelected(new Set());
    } catch (error) {
      if (error?.status === 401 || error?.status === 403) {
        setState({ loading: false, data: null, error: null, authorized: false });
        return;
      }
      setState({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load pending approval work.',
        authorized: true
      });
    }
  }, []);

  useLayoutEffect(() => {
    const syncHosts = () => {
      const dashboardCard = document.querySelector('.welcome-attention-card');
      if (dashboardCard) {
        const metricList = dashboardCard.querySelector('.welcome-metric-list');
        const existingTimeRow = [...(metricList?.children || [])].find((row) =>
          row.querySelector('dt')?.textContent?.trim() === 'Time approvals'
        );
        if (existingTimeRow) {
          existingTimeRow.dataset.projectpulseReplacedByPendingWork = 'true';
          existingTimeRow.style.display = state.authorized ? 'none' : '';
        }
        setDashboardHost(ensureHost('pending-approval-dashboard-host', dashboardCard, metricList));
      } else {
        setDashboardHost(null);
      }

      const approvalCenter = document.querySelector('#manager-approval.approval-center-shell');
      if (approvalCenter) {
        const summary = approvalCenter.querySelector('.approval-summary-grid');
        setApprovalHost(ensureHost('pending-approval-center-host', approvalCenter, summary));
      } else {
        setApprovalHost(null);
      }
    };

    syncHosts();
    const observer = new MutationObserver(syncHosts);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', syncHosts);

    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', syncHosts);
      document.querySelectorAll('[data-projectpulse-replaced-by-pending-work="true"]').forEach((row) => {
        row.style.display = '';
        delete row.dataset.projectpulseReplacedByPendingWork;
      });
      document.getElementById('pending-approval-dashboard-host')?.remove();
      document.getElementById('pending-approval-center-host')?.remove();
    };
  }, [state.authorized]);

  useEffect(() => {
    void load();
    const interval = window.setInterval(load, 30000);
    const refresh = () => void load();
    window.addEventListener('hashchange', refresh);
    window.addEventListener('projectpulse:approval-queue-changed', refresh);
    window.addEventListener('projectpulse:approval-work-refresh', refresh);
    window.addEventListener('projectpulse:pending-approval-target-changed', refresh);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('hashchange', refresh);
      window.removeEventListener('projectpulse:approval-queue-changed', refresh);
      window.removeEventListener('projectpulse:approval-work-refresh', refresh);
      window.removeEventListener('projectpulse:pending-approval-target-changed', refresh);
    };
  }, [load]);

  const openWeek = (stage, weekStart) => {
    rememberPendingTarget(stage, weekStart);
    const route = String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0];
    if (route === 'manager-approval') {
      window.dispatchEvent(new CustomEvent('projectpulse:pending-approval-target-changed', {
        detail: { pendingStage: stage, weekStart }
      }));
      window.setTimeout(() => {
        document.getElementById(`pending-approval-${stage}-${weekStart}`)?.scrollIntoView({
          behavior: 'smooth',
          block: 'start'
        });
      }, 50);
      return;
    }
    window.location.hash = 'manager-approval';
  };

  const complete = async (week, items, mode) => {
    const key = `${groupKey(week.stage, week.weekStart)}:${mode}`;
    setBusy(key);
    setActionStatus({ type: '', text: '' });
    try {
      const result = await completePendingWork({
        stage: week.stage,
        weekStart: week.weekStart,
        items
      });
      setActionStatus({ type: 'success', text: result.message || 'Pending approval work was completed.' });
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
      await load();
    } catch (error) {
      setActionStatus({ type: 'error', text: error instanceof Error ? error.message : 'Approval work could not be completed.' });
    } finally {
      setBusy('');
    }
  };

  if (!state.authorized) return null;

  return (
    <>
      {dashboardHost ? createPortal(
        <DashboardPendingWork
          data={state.data}
          loading={state.loading}
          error={state.error}
          openWeek={openWeek}
        />,
        dashboardHost
      ) : null}

      {approvalHost ? createPortal(
        <ApprovalCenterPendingWork
          data={state.data}
          loading={state.loading}
          error={state.error}
          actionStatus={actionStatus}
          busy={busy}
          selected={selected}
          setSelected={setSelected}
          complete={complete}
          openWeek={openWeek}
        />,
        approvalHost
      ) : null}
    </>
  );
}

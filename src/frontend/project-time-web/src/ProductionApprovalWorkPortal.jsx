import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState
} from 'react';
import { createPortal } from 'react-dom';
import './pending-approval-work.css';
import './production-approval-work.css';
import './production-approval-work-hardening.css';

const CONTRACT = 'approval-work-production-v2-2026-07-30';
const TARGET_STORAGE_KEY = 'projectPulsePendingApprovalTarget';
const SEARCH_DEBOUNCE_MS = 300;

const STAGES = Object.freeze({
  manager: {
    label: 'Manager review',
    shortLabel: 'Manager',
    help: 'Submitted employee days awaiting the employee’s Manager.'
  },
  pm: {
    label: 'PM review',
    shortLabel: 'PM',
    help: 'Manager-approved project scopes awaiting the assigned Project Manager.'
  },
  ptc: {
    label: 'PTC final review',
    shortLabel: 'PTC',
    help: 'PM-complete project time and Manager-approved non-project time awaiting PTC final review.'
  }
});

function authHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    const session = raw ? JSON.parse(raw) : null;
    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
  } catch {
    return {};
  }
}

async function readResponse(response, path) {
  const raw = await response.text();
  let payload = {};
  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = {};
  }

  if (!response.ok) {
    const error = new Error(
      payload.message || payload.detail || raw || `${path} returned HTTP ${response.status}.`
    );
    error.status = response.status;
    error.payload = payload;
    throw error;
  }

  return payload;
}

async function fetchPending({
  stage = '',
  weekStart = '',
  search = '',
  page = 1,
  pageSize = 200
} = {}) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  if (stage) params.set('stage', stage);
  if (weekStart) params.set('weekStart', weekStart);
  if (search) params.set('search', search);

  const path = `/api/approval-work/v2/pending?${params.toString()}`;
  const response = await fetch(path, {
    headers: authHeaders(),
    cache: 'no-store'
  });
  return readResponse(response, path);
}

async function completePending(payload) {
  const path = '/api/approval-work/v2/bulk-complete';
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders()
    },
    body: JSON.stringify(payload)
  });
  return readResponse(response, path);
}

function ensureHost(id, parent, beforeNode = null) {
  if (!parent) return null;
  let host = document.getElementById(id);
  if (!host) {
    host = document.createElement('div');
    host.id = id;
    host.dataset.projectpulseProductionApprovalHost = 'true';
    if (beforeNode) parent.insertBefore(host, beforeNode);
    else parent.appendChild(host);
  }
  return host;
}

function displayDate(value, { year = true } = {}) {
  if (!value) return 'Unknown date';
  const date = new Date(`${value}T12:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    year: year ? 'numeric' : undefined
  });
}

function displayDateTime(value) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function formatHours(value) {
  return Number(value || 0).toLocaleString(undefined, {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2
  });
}

function sundayFor(date = new Date()) {
  const copy = new Date(date);
  copy.setHours(12, 0, 0, 0);
  copy.setDate(copy.getDate() - copy.getDay());
  return copy.toISOString().slice(0, 10);
}

function weekContextLabel(weekStart) {
  if (!weekStart) return 'Historical backlog';
  const current = new Date(`${sundayFor()}T12:00:00`);
  const week = new Date(`${weekStart}T12:00:00`);
  if (Number.isNaN(week.getTime())) return 'Historical backlog';
  const difference = Math.round((current.getTime() - week.getTime()) / 604800000);
  if (difference === 0) return 'Current week';
  if (difference === 1) return '1 week overdue';
  if (difference > 1) return `${difference} weeks overdue`;
  return 'Future week';
}

function pendingAgeLabel(value) {
  if (!value) return 'Age unavailable';
  const timestamp = new Date(value);
  if (Number.isNaN(timestamp.getTime())) return 'Age unavailable';
  const days = Math.max(0, Math.floor((Date.now() - timestamp.getTime()) / 86400000));
  if (days === 0) return 'Added today';
  if (days === 1) return 'Pending 1 day';
  return `Pending ${days} days`;
}

function groupKey(stage, weekStart) {
  return `${stage}|${weekStart}`;
}

function itemKey(item) {
  return [
    item.timesheetId,
    item.workDate,
    item.stage,
    item.projectId || 'day'
  ].join('|');
}

function selection(item) {
  return {
    timesheetId: item.timesheetId,
    workDate: item.workDate,
    stage: item.stage,
    projectId: item.projectId || null,
    scopeKey: item.scopeKey || null
  };
}

function rememberTarget(stage, weekStart) {
  try {
    window.sessionStorage.setItem(
      TARGET_STORAGE_KEY,
      JSON.stringify({ stage, weekStart })
    );
  } catch {
    // Navigation remains usable when browser storage is unavailable.
  }
}

function readTarget() {
  try {
    const value = JSON.parse(
      window.sessionStorage.getItem(TARGET_STORAGE_KEY) || 'null'
    );
    return {
      stage: String(value?.stage || ''),
      weekStart: String(value?.weekStart || '')
    };
  } catch {
    return { stage: '', weekStart: '' };
  }
}

function sortWeeks(weeks) {
  return [...weeks].sort((left, right) => {
    const leftPending = Date.parse(left.oldestPendingAt || '') || Number.MAX_SAFE_INTEGER;
    const rightPending = Date.parse(right.oldestPendingAt || '') || Number.MAX_SAFE_INTEGER;
    if (leftPending !== rightPending) return leftPending - rightPending;
    const weekDifference = String(left.weekStart).localeCompare(String(right.weekStart));
    if (weekDifference !== 0) return weekDifference;
    const order = { manager: 0, pm: 1, ptc: 2 };
    return (order[left.stage] ?? 9) - (order[right.stage] ?? 9);
  });
}

function DashboardQueue({ data, loading, error, openWeek }) {
  const weeks = sortWeeks(Array.isArray(data?.pendingWeeks) ? data.pendingWeeks : []);
  const total = Number(data?.totalPending || 0);

  return (
    <section className="production-approval-dashboard" aria-label="All pending approval work">
      <button
        type="button"
        className="production-approval-dashboard-total"
        onClick={() => {
          const first = weeks[0];
          if (first) openWeek(first.stage, first.weekStart);
          else window.location.hash = 'manager-approval';
        }}
      >
        <span>
          <strong>Time approvals across all weeks</strong>
          <small>Oldest authorized Manager, PM, or PTC work is shown first</small>
        </span>
        <b>{loading || error ? '—' : total}</b>
      </button>

      {!loading && error ? (
        <p role="alert">Approval totals are temporarily unavailable. Open Approval Center to continue.</p>
      ) : null}
      {!loading && !error && total === 0 ? <p>No pending time approval work was found.</p> : null}

      {!loading && !error && total > 0 ? (
        <div className="production-approval-dashboard-weeks">
          {weeks.slice(0, 4).map((week) => (
            <button
              type="button"
              key={groupKey(week.stage, week.weekStart)}
              onClick={() => openWeek(week.stage, week.weekStart)}
            >
              <span>{STAGES[week.stage]?.shortLabel || week.stage}</span>
              <strong>{week.count}</strong>
              <small>Week of {displayDate(week.weekStart, { year: false })}</small>
              <em>{weekContextLabel(week.weekStart)} · {pendingAgeLabel(week.oldestPendingAt)}</em>
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

function StageCard({ stage, count, active, onSelect }) {
  const config = STAGES[stage];
  return (
    <button
      type="button"
      className={`production-approval-stage stage-${stage}${active ? ' active' : ''}`}
      onClick={() => onSelect(active ? '' : stage)}
      aria-pressed={active}
    >
      <span>{config.label}</span>
      <strong>{Number(count || 0)}</strong>
      <small>{config.help}</small>
    </button>
  );
}

function ApprovalItem({ item, checked, disabled, onToggle }) {
  const projectLabel = item.projectCodes || item.projectNames || '';
  return (
    <label className="production-approval-item">
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onToggle(itemKey(item), event.target.checked)}
      />
      <span>
        <strong>{item.resourceName}</strong>
        <small>{displayDate(item.workDate)} · {formatHours(item.totalHours)} hour(s)</small>
        {item.stage === 'pm' ? (
          <small className="production-approval-project-scope">
            Project scope: {projectLabel || 'Project not named'}
          </small>
        ) : (
          <small>{projectLabel || 'Non-project time'}</small>
        )}
        <span className="production-approval-badges">
          {item.approvalUnitType === 'project_scope' ? <em>PM approves this project only</em> : null}
          {item.nonProjectOnly ? <em>Non-project · PM not required</em> : null}
          {item.containsNonProjectTime && !item.nonProjectOnly ? (
            <em>Mixed day · non-project remains outside PM approval</em>
          ) : null}
        </span>
      </span>
    </label>
  );
}

function WeekCard({
  week,
  expanded,
  targeted,
  detail,
  selected,
  busy,
  onOpen,
  onToggle,
  onToggleAll,
  onApproveSelected,
  onApproveWeek,
  onLoadMore
}) {
  const config = STAGES[week.stage] || { label: week.stage };
  const items = Array.isArray(detail?.items) ? detail.items : [];
  const selectedItems = items.filter((item) => selected.has(itemKey(item)));
  const allLoadedSelected = items.length > 0 && selectedItems.length === items.length;
  const key = groupKey(week.stage, week.weekStart);

  return (
    <article
      id={`production-approval-${week.stage}-${week.weekStart}`}
      className={`production-approval-week${targeted ? ' is-targeted' : ''}`}
      data-production-approval-group={key}
    >
      <header>
        <div>
          <p className="eyebrow">{config.label}</p>
          <h4>Week of {displayDate(week.weekStart)}</h4>
          <p>
            {week.count} approval unit(s) · {formatHours(week.totalHours)} hour(s) · {weekContextLabel(week.weekStart)}
          </p>
          <small>{pendingAgeLabel(week.oldestPendingAt)} · oldest item {displayDateTime(week.oldestPendingAt)}</small>
          {week.stage === 'pm' ? (
            <small>Each approval unit contains one managed project. Non-project time is excluded.</small>
          ) : null}
        </div>
        <button type="button" className="secondary-action" onClick={() => onOpen(week)}>
          {expanded ? 'Hide details' : 'Open pending work'}
        </button>
      </header>

      <div className="production-approval-week-actions">
        <button
          type="button"
          className="secondary-action"
          disabled={Boolean(busy) || detail?.loading || items.length === 0}
          onClick={() => onToggleAll(items, !allLoadedSelected)}
        >
          {allLoadedSelected ? 'Clear loaded selection' : `Select all loaded (${items.length})`}
        </button>
        <button
          type="button"
          className="secondary-action"
          disabled={Boolean(busy) || selectedItems.length === 0}
          onClick={() => onApproveSelected(week, selectedItems)}
        >
          {busy === `${key}:selected` ? 'Approving…' : `Approve selected (${selectedItems.length})`}
        </button>
        <button
          type="button"
          className="primary-action"
          disabled={Boolean(busy) || Number(week.count || 0) === 0}
          onClick={() => onApproveWeek(week)}
        >
          {busy === `${key}:week` ? 'Approving…' : `Approve entire week (${week.count})`}
        </button>
      </div>

      {expanded ? (
        <div className="production-approval-detail">
          {detail?.loading ? <p>Loading authorized approval details…</p> : null}
          {detail?.error ? <p className="error" role="alert">{detail.error}</p> : null}
          {!detail?.loading && !detail?.error && items.length === 0 ? (
            <p>No actionable items match the current search.</p>
          ) : null}
          <div className="production-approval-items">
            {items.map((item) => (
              <ApprovalItem
                key={itemKey(item)}
                item={item}
                checked={selected.has(itemKey(item))}
                disabled={Boolean(busy)}
                onToggle={onToggle}
              />
            ))}
          </div>
          {detail?.hasMore ? (
            <button
              type="button"
              className="production-approval-load-more"
              disabled={Boolean(busy) || detail.loadingMore}
              onClick={() => onLoadMore(week)}
            >
              {detail.loadingMore
                ? 'Loading more…'
                : `Load more (${detail.loadedCount} of ${detail.filteredCount})`}
            </button>
          ) : null}
        </div>
      ) : null}
    </article>
  );
}

function ApprovalCenterWorkspace({
  data,
  loading,
  error,
  filterStage,
  setFilterStage,
  searchInput,
  setSearchInput,
  searchPending,
  expanded,
  target,
  weekData,
  selected,
  busy,
  actionStatus,
  openWeek,
  toggleItem,
  toggleAll,
  approveSelected,
  approveWeek,
  loadMore,
  refresh
}) {
  const weeks = useMemo(() => {
    const source = sortWeeks(Array.isArray(data?.pendingWeeks) ? data.pendingWeeks : []);
    return source
      .filter((week) => !filterStage || week.stage === filterStage)
      .sort((left, right) => {
        const leftTarget = left.stage === target.stage && left.weekStart === target.weekStart;
        const rightTarget = right.stage === target.stage && right.weekStart === target.weekStart;
        if (leftTarget !== rightTarget) return leftTarget ? -1 : 1;
        return 0;
      });
  }, [data, filterStage, target]);

  return (
    <section className="production-approval-center" aria-label="Production approval work center">
      <header className="production-approval-center-header">
        <div>
          <p className="eyebrow">ALL WEEKS · AUTHORIZED WORK ONLY</p>
          <h3>Pending approval work</h3>
          <p>
            This is the only approval surface. Manager, project-scoped PM, and PTC decisions use one authoritative workflow. Approvals require no typed comment.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={refresh}>Refresh</button>
      </header>

      <aside className="production-approval-return-guidance">
        <strong>Need to return or unlock time?</strong>
        <span>The role tabs below remain available for returns, exceptions, unlocks, password resets, and history only. Returns still require a clear reason.</span>
      </aside>

      {actionStatus?.text ? (
        <p
          className={`production-approval-message${actionStatus.type === 'error' ? ' error' : ''}`}
          role={actionStatus.type === 'error' ? 'alert' : 'status'}
        >
          {actionStatus.text}
        </p>
      ) : null}
      {error ? <p className="production-approval-message error" role="alert">{error}</p> : null}

      <div className="production-approval-stage-grid">
        <StageCard
          stage="manager"
          count={data?.stageCounts?.manager}
          active={filterStage === 'manager'}
          onSelect={setFilterStage}
        />
        <StageCard
          stage="pm"
          count={data?.stageCounts?.pm}
          active={filterStage === 'pm'}
          onSelect={setFilterStage}
        />
        <StageCard
          stage="ptc"
          count={data?.stageCounts?.ptc}
          active={filterStage === 'ptc'}
          onSelect={setFilterStage}
        />
        <article className="production-approval-stage total">
          <span>Total pending</span>
          <strong>{loading ? '—' : Number(data?.totalPending || 0)}</strong>
          <small>{data?.access?.scopeLabel || 'Your authorized approval scope'}</small>
        </article>
      </div>

      <div className="production-approval-rules">
        <strong>Routing rules</strong>
        <span>Project time: Manager → assigned PM → PTC</span>
        <span>Non-project time: Manager → PTC; PM is never asked to approve it</span>
        <span>Mixed days remain at PM review until every project scope is complete</span>
      </div>

      <div className="production-approval-filters">
        <label>
          <span>Approval stage</span>
          <select value={filterStage} onChange={(event) => setFilterStage(event.target.value)}>
            <option value="">All assigned stages</option>
            <option value="manager">Manager review</option>
            <option value="pm">PM review</option>
            <option value="ptc">PTC final review</option>
          </select>
        </label>
        <label>
          <span>Search opened weeks</span>
          <input
            type="search"
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            placeholder="Employee, email, date, project, or non-project"
          />
          <small>{searchPending ? 'Updating opened weeks…' : 'Search applies to every expanded week.'}</small>
        </label>
      </div>

      {loading ? <p className="production-approval-loading">Loading complete approval totals…</p> : null}
      {!loading && !error && weeks.length === 0 ? (
        <div className="production-approval-empty">
          <strong>No pending approval work</strong>
          <span>No assigned approval stage contains unfinished work in any week.</span>
        </div>
      ) : null}

      <div className="production-approval-week-list">
        {weeks.map((week) => {
          const key = groupKey(week.stage, week.weekStart);
          return (
            <WeekCard
              key={key}
              week={week}
              expanded={expanded.has(key)}
              targeted={week.stage === target.stage && week.weekStart === target.weekStart}
              detail={weekData[key]}
              selected={selected}
              busy={busy}
              onOpen={openWeek}
              onToggle={toggleItem}
              onToggleAll={toggleAll}
              onApproveSelected={approveSelected}
              onApproveWeek={approveWeek}
              onLoadMore={loadMore}
            />
          );
        })}
      </div>
    </section>
  );
}

export default function ProductionApprovalWorkPortal() {
  const [dashboardHost, setDashboardHost] = useState(null);
  const [approvalHost, setApprovalHost] = useState(null);
  const [state, setState] = useState({ loading: true, data: null, error: '', authorized: true });
  const [filterStage, setFilterStage] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [searchPending, setSearchPending] = useState(false);
  const [expanded, setExpanded] = useState(new Set());
  const [weekData, setWeekData] = useState({});
  const [selected, setSelected] = useState(new Set());
  const [busy, setBusy] = useState('');
  const [actionStatus, setActionStatus] = useState({ type: '', text: '' });
  const [target, setTarget] = useState(() => readTarget());
  const requestSequence = useRef(0);
  const requestTokenByWeek = useRef(new Map());
  const previousSearch = useRef('');

  const loadSummary = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await fetchPending({ page: 1, pageSize: 1 });
      if (data?.apiContractVersion !== CONTRACT) {
        throw new Error('The server returned an unexpected approval-work contract.');
      }
      setState({ loading: false, data, error: '', authorized: true });
    } catch (requestError) {
      if (requestError?.status === 401 || requestError?.status === 403) {
        setState({ loading: false, data: null, error: '', authorized: false });
        return;
      }
      setState({
        loading: false,
        data: null,
        error: requestError instanceof Error
          ? requestError.message
          : 'Unable to load pending approval work.',
        authorized: true
      });
    }
  }, []);

  const loadWeek = useCallback(async (
    week,
    { page = 1, append = false, query = searchQuery } = {}
  ) => {
    const key = groupKey(week.stage, week.weekStart);
    const normalizedQuery = String(query || '').trim();
    const requestToken = `${++requestSequence.current}:${normalizedQuery}:${page}`;
    requestTokenByWeek.current.set(key, requestToken);

    setWeekData((current) => ({
      ...current,
      [key]: {
        ...(current[key] || {}),
        query: normalizedQuery,
        loading: !append,
        loadingMore: append,
        error: ''
      }
    }));

    try {
      const data = await fetchPending({
        stage: week.stage,
        weekStart: week.weekStart,
        search: normalizedQuery,
        page,
        pageSize: 200
      });

      if (requestTokenByWeek.current.get(key) !== requestToken) return;

      setWeekData((current) => {
        const canAppend = append && current[key]?.query === normalizedQuery;
        const previousItems = canAppend ? current[key]?.items || [] : [];
        const merged = [...previousItems, ...(Array.isArray(data.items) ? data.items : [])];
        const deduped = [...new Map(merged.map((item) => [itemKey(item), item])).values()];
        return {
          ...current,
          [key]: {
            loading: false,
            loadingMore: false,
            error: '',
            query: normalizedQuery,
            items: deduped,
            page: Number(data.page || page),
            hasMore: Boolean(data.hasMore),
            nextPage: data.nextPage,
            loadedCount: deduped.length,
            filteredCount: Number(data.filteredCount || deduped.length)
          }
        };
      });
    } catch (requestError) {
      if (requestTokenByWeek.current.get(key) !== requestToken) return;
      setWeekData((current) => ({
        ...current,
        [key]: {
          ...(current[key] || {}),
          loading: false,
          loadingMore: false,
          query: normalizedQuery,
          error: requestError instanceof Error
            ? requestError.message
            : 'Unable to load this pending week.'
        }
      }));
    }
  }, [searchQuery]);

  useLayoutEffect(() => {
    const synchronizeHosts = () => {
      const dashboardCard = document.querySelector('.welcome-attention-card');
      if (dashboardCard) {
        const metrics = dashboardCard.querySelector('.welcome-metric-list');
        const oldTimeRow = [...(metrics?.children || [])].find((row) =>
          row.querySelector('dt')?.textContent?.trim() === 'Time approvals'
        );
        if (oldTimeRow) {
          oldTimeRow.dataset.projectpulseReplacedByProductionApproval = 'true';
          oldTimeRow.style.display = state.authorized ? 'none' : '';
        }
        setDashboardHost(ensureHost('production-approval-dashboard-host', dashboardCard, metrics));
      } else {
        setDashboardHost(null);
      }

      const approvalCenter = document.querySelector('#manager-approval.approval-center-shell');
      if (approvalCenter) {
        approvalCenter.dataset.productionApprovalAuthoritative = 'true';
        const summary = approvalCenter.querySelector('.approval-summary-grid');
        setApprovalHost(ensureHost('production-approval-center-host', approvalCenter, summary));
      } else {
        setApprovalHost(null);
      }
    };

    synchronizeHosts();
    const observer = new MutationObserver(synchronizeHosts);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', synchronizeHosts);

    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', synchronizeHosts);
      document.querySelectorAll('[data-projectpulse-replaced-by-production-approval="true"]').forEach((row) => {
        row.style.display = '';
        delete row.dataset.projectpulseReplacedByProductionApproval;
      });
      document.querySelectorAll('[data-production-approval-authoritative="true"]').forEach((element) => {
        delete element.dataset.productionApprovalAuthoritative;
      });
      document.getElementById('production-approval-dashboard-host')?.remove();
      document.getElementById('production-approval-center-host')?.remove();
    };
  }, [state.authorized]);

  useEffect(() => {
    void loadSummary();
    const interval = window.setInterval(loadSummary, 30000);
    const refresh = () => void loadSummary();
    window.addEventListener('hashchange', refresh);
    window.addEventListener('projectpulse:approval-queue-changed', refresh);
    window.addEventListener('projectpulse:approval-work-refresh', refresh);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('hashchange', refresh);
      window.removeEventListener('projectpulse:approval-queue-changed', refresh);
      window.removeEventListener('projectpulse:approval-work-refresh', refresh);
    };
  }, [loadSummary]);

  useEffect(() => {
    setSearchPending(true);
    const timer = window.setTimeout(() => {
      setSearchQuery(searchInput.trim());
      setSearchPending(false);
    }, SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [searchInput]);

  useEffect(() => {
    if (previousSearch.current === searchQuery) return;
    previousSearch.current = searchQuery;
    setSelected(new Set());

    const openedWeeks = (state.data?.pendingWeeks || []).filter((week) =>
      expanded.has(groupKey(week.stage, week.weekStart))
    );
    for (const week of openedWeeks) {
      void loadWeek(week, { page: 1, append: false, query: searchQuery });
    }
  }, [searchQuery, state.data, expanded, loadWeek]);

  useEffect(() => {
    if (!target.stage || !target.weekStart) return;
    const week = (state.data?.pendingWeeks || []).find((candidate) =>
      candidate.stage === target.stage && candidate.weekStart === target.weekStart
    );
    if (!week) return;

    const key = groupKey(week.stage, week.weekStart);
    setExpanded((current) => new Set(current).add(key));
    if (!weekData[key] || weekData[key]?.query !== searchQuery) {
      void loadWeek(week, { query: searchQuery });
    }

    window.setTimeout(() => {
      document.getElementById(`production-approval-${week.stage}-${week.weekStart}`)?.scrollIntoView({
        behavior: 'smooth',
        block: 'start'
      });
    }, 120);
  }, [state.data, target, weekData, searchQuery, loadWeek]);

  const openWeek = useCallback((weekOrStage, optionalWeekStart) => {
    const week = typeof weekOrStage === 'string'
      ? { stage: weekOrStage, weekStart: optionalWeekStart }
      : weekOrStage;
    if (!week?.stage || !week?.weekStart) return;

    const key = groupKey(week.stage, week.weekStart);
    const willOpen = !expanded.has(key);
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

    if (willOpen || weekData[key]?.query !== searchQuery) {
      void loadWeek(week, { query: searchQuery });
    }

    rememberTarget(week.stage, week.weekStart);
    setTarget({ stage: week.stage, weekStart: week.weekStart });

    const route = String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0];
    if (route !== 'manager-approval') window.location.hash = 'manager-approval';
    else {
      window.setTimeout(() => {
        document.getElementById(`production-approval-${week.stage}-${week.weekStart}`)?.scrollIntoView({
          behavior: 'smooth',
          block: 'start'
        });
      }, 50);
    }
  }, [expanded, weekData, searchQuery, loadWeek]);

  const toggleItem = (key, checked) => {
    setSelected((current) => {
      const next = new Set(current);
      if (checked) next.add(key);
      else next.delete(key);
      return next;
    });
  };

  const toggleAll = (items, checked) => {
    setSelected((current) => {
      const next = new Set(current);
      for (const item of items) {
        const key = itemKey(item);
        if (checked) next.add(key);
        else next.delete(key);
      }
      return next;
    });
  };

  const complete = async (week, mode, items = null) => {
    const key = `${groupKey(week.stage, week.weekStart)}:${mode}`;
    setBusy(key);
    setActionStatus({ type: '', text: '' });

    const payload = mode === 'selected'
      ? {
          mode: 'selected',
          stage: week.stage,
          weekStart: week.weekStart,
          items: items.map(selection),
          requestId: window.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`
        }
      : {
          mode: 'week',
          stage: week.stage,
          weekStart: week.weekStart,
          items: null,
          requestId: window.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`
        };

    try {
      const result = await completePending(payload);
      setActionStatus({
        type: 'success',
        text: `${result.message || 'Approval work was completed.'} Immutable evidence: ${result.immutableEvidenceRecorded === false ? 'unavailable' : 'recorded'}.`
      });
      setSelected(new Set());
      setWeekData((current) => {
        const next = { ...current };
        delete next[groupKey(week.stage, week.weekStart)];
        return next;
      });
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
      await loadSummary();
      if (expanded.has(groupKey(week.stage, week.weekStart))) {
        await loadWeek(week, { query: searchQuery });
      }
    } catch (requestError) {
      setActionStatus({
        type: 'error',
        text: requestError instanceof Error
          ? requestError.message
          : 'Approval work could not be completed.'
      });
    } finally {
      setBusy('');
    }
  };

  const loadMore = (week) => {
    const detail = weekData[groupKey(week.stage, week.weekStart)];
    if (!detail?.hasMore || !detail.nextPage) return;
    void loadWeek(week, {
      page: detail.nextPage,
      append: true,
      query: detail.query ?? searchQuery
    });
  };

  const refresh = () => {
    setWeekData({});
    setSelected(new Set());
    setActionStatus({ type: '', text: '' });
    void loadSummary();
    for (const week of state.data?.pendingWeeks || []) {
      if (expanded.has(groupKey(week.stage, week.weekStart))) {
        void loadWeek(week, { query: searchQuery });
      }
    }
  };

  if (!state.authorized) return null;

  return (
    <>
      {dashboardHost ? createPortal(
        <DashboardQueue
          data={state.data}
          loading={state.loading}
          error={state.error}
          openWeek={(stage, weekStart) => openWeek(stage, weekStart)}
        />,
        dashboardHost
      ) : null}

      {approvalHost ? createPortal(
        <ApprovalCenterWorkspace
          data={state.data}
          loading={state.loading}
          error={state.error}
          filterStage={filterStage}
          setFilterStage={setFilterStage}
          searchInput={searchInput}
          setSearchInput={setSearchInput}
          searchPending={searchPending}
          expanded={expanded}
          target={target}
          weekData={weekData}
          selected={selected}
          busy={busy}
          actionStatus={actionStatus}
          openWeek={openWeek}
          toggleItem={toggleItem}
          toggleAll={toggleAll}
          approveSelected={(week, items) => complete(week, 'selected', items)}
          approveWeek={(week) => complete(week, 'week')}
          loadMore={loadMore}
          refresh={refresh}
        />,
        approvalHost
      ) : null}
    </>
  );
}

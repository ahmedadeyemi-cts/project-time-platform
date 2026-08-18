import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './engineer-task-closeout-center.css';

const EMPTY_OVERVIEW = Object.freeze({
  active: [],
  history: [],
  events: [],
  summary: {
    activeCount: 0,
    historyCount: 0,
    reopenEligibleCount: 0,
    billingLockedCount: 0
  }
});
const PAGE_SIZE = 50;
const OVERVIEW_RETRY_DELAYS_MS = Object.freeze([0, 250, 750]);
const OVERVIEW_RETRYABLE_STATUS = new Set([401, 408, 425, 429, 502, 503, 504]);
const MODULE001A_VISIBLE_REQUEST_FAMILIES_CONTRACT = 'MODULE001A_VISIBLE_REQUEST_FAMILIES_V2';

function sessionHeaders(authSession, json = false) {
  const token = authSession?.sessionToken || authSession?.token || authSession?.accessToken || '';
  return {
    Accept: 'application/json',
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {}),
    ...(json ? { 'Content-Type': 'application/json' } : {})
  };
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return String(value);
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  }).format(parsed);
}

function formatHours(value) {
  const number = Number(value ?? 0);
  return Number.isFinite(number) ? number.toFixed(1) : '0.0';
}

function compareCloseoutItems(left, right) {
  const leftDate = Date.parse(left?.effectiveStartDate || '') || 0;
  const rightDate = Date.parse(right?.effectiveStartDate || '') || 0;
  if (leftDate !== rightDate) return rightDate - leftDate;
  const projectComparison = String(left?.projectCode || '').localeCompare(String(right?.projectCode || ''));
  if (projectComparison !== 0) return projectComparison;
  return String(left?.taskCode || '').localeCompare(String(right?.taskCode || ''));
}

function waitForOverviewRetry(milliseconds, signal) {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(resolve, milliseconds);
    signal?.addEventListener('abort', () => {
      window.clearTimeout(timer);
      reject(new DOMException('Aborted', 'AbortError'));
    }, { once: true });
  });
}

async function readResponse(response) {
  const text = await response.text();
  let payload = {};
  try {
    payload = text ? JSON.parse(text) : {};
  } catch {
    payload = { message: text };
  }
  if (!response.ok) {
    throw new Error(payload.message || payload.detail || `Request failed (${response.status}).`);
  }
  return payload;
}

function CloseoutIcon({ name }) {
  if (name === 'history') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 8v5l3 2M3.5 12a8.5 8.5 0 1 0 2.1-5.6L3.5 8.5M3.5 4.5v4h4" /></svg>;
  }
  if (name === 'lock') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="10" width="14" height="10" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></svg>;
  }
  if (name === 'mail') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m4 7 8 6 8-6" /></svg>;
  }
  if (name === 'refresh') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6v5h-5M4 18v-5h5M18.5 10a7 7 0 0 0-12-3L4 11m16 2-2.5 4a7 7 0 0 1-12-3" /></svg>;
  }
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6" /></svg>;
}

function StatusBadge({ item }) {
  const status = item.closeoutStatus || 'active';
  const label = status === 'ptc_final_closed'
    ? 'Final closed in 055C'
    : status === 'engineer_closed'
      ? 'Engineer closed'
      : status === 'reopened'
        ? 'Reopened'
        : 'Active';
  return <span className={`engineer-closeout-status engineer-closeout-status--${status}`}>{label}</span>;
}

function RequestTypeBadge({ value }) {
  const token = String(value || 'Internal').toLowerCase().replace(/[^a-z]+/g, '-');
  return <span className={`engineer-closeout-type engineer-closeout-type--${token}`}>{value || 'Internal'}</span>;
}

function TaskCard({ item, historical, onClose, onReopen }) {
  const allocation = Number(item.assignedHours || 0);
  const used = Number(item.usedHours || 0);
  const progress = allocation > 0 ? Math.min((used / allocation) * 100, 100) : 0;
  return (
    <article className={`engineer-closeout-task ${historical ? 'engineer-closeout-task--historical' : ''}`}>
      <div className="engineer-closeout-task__accent" aria-hidden="true" />
      <div className="engineer-closeout-task__heading">
        <div>
          <div className="engineer-closeout-task__badges">
            <RequestTypeBadge value={item.requestType} />
            <StatusBadge item={item} />
          </div>
          <p className="engineer-closeout-task__customer">{item.customerName}</p>
          <h3>{item.taskName}</h3>
          <p className="engineer-closeout-task__reference">
            <strong>{item.serviceRequestNumber || item.projectCode}</strong>
            <span>{item.projectName}</span>
            <span>{item.taskCode}</span>
          </p>
        </div>
        <div className="engineer-closeout-task__action">
          {!historical && item.canClose ? (
            <button type="button" className="engineer-closeout-button engineer-closeout-button--primary" onClick={() => onClose(item)}>
              <CloseoutIcon name="check" />
              Close task
            </button>
          ) : null}
          {historical && item.canReopen ? (
            <button type="button" className="engineer-closeout-button engineer-closeout-button--outline" onClick={() => onReopen(item)}>
              <CloseoutIcon name="refresh" />
              Reopen task
            </button>
          ) : null}
          {historical && !item.canReopen ? (
            <span className="engineer-closeout-final-lock"><CloseoutIcon name="lock" /> Reopen unavailable</span>
          ) : null}
        </div>
      </div>

      <div className="engineer-closeout-task__details">
        <div><span>Assigned</span><strong>{formatHours(item.assignedHours)}h</strong></div>
        <div><span>Recorded</span><strong>{formatHours(item.usedHours)}h</strong></div>
        <div><span>Remaining</span><strong>{formatHours(item.remainingHours)}h</strong></div>
        <div><span>Coordinator</span><strong>{item.projectTeamCoordinatorName || 'Role-based routing'}</strong></div>
      </div>

      <div className="engineer-closeout-progress" aria-label={`${Math.round(progress)} percent of assigned hours recorded`}>
        <span style={{ width: `${progress}%` }} />
      </div>

      {historical ? (
        <div className="engineer-closeout-task__history-detail">
          <div>
            <span>{item.closeoutStatus === 'ptc_final_closed' ? 'Final closure' : 'Engineer closure'}</span>
            <strong>{formatDate(item.ptcFinalClosedAt || item.engineerClosedAt)}</strong>
          </div>
          <div>
            <span>Completion summary</span>
            <strong>{item.completionSummary || 'No summary available'}</strong>
          </div>
          <div>
            <span>PTC notification</span>
            <strong>{String(item.notificationStatus || 'not queued').replaceAll('_', ' ')}</strong>
          </div>
        </div>
      ) : null}
    </article>
  );
}

function TransitionDialog({ transition, busy, error, onDismiss, onSubmit }) {
  const [reason, setReason] = useState('');
  const reopening = transition?.mode === 'reopen';
  const minimum = reopening ? 10 : 5;
  if (!transition) return null;

  return (
    <div className="engineer-closeout-dialog-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget && !busy) onDismiss();
    }}>
      <section className="engineer-closeout-dialog" role="dialog" aria-modal="true" aria-labelledby="engineer-closeout-dialog-title">
        <div className={`engineer-closeout-dialog__icon ${reopening ? 'is-reopen' : ''}`}>
          <CloseoutIcon name={reopening ? 'refresh' : 'check'} />
        </div>
        <p className="engineer-closeout-eyebrow">Module 001A · Engineer action</p>
        <h2 id="engineer-closeout-dialog-title">{reopening ? 'Reopen this task?' : 'Close this task?'}</h2>
        <p className="engineer-closeout-dialog__task">{transition.item.taskCode} · {transition.item.taskName}</p>
        <div className="engineer-closeout-dialog__notice">
          <CloseoutIcon name={reopening ? 'mail' : 'lock'} />
          <span>{reopening
            ? 'Reopening is allowed only while the original request remains open in Module 055C. Your reason is emailed to the Project Team Coordinator, with you copied.'
            : 'Closing removes this assignment from Module 001 immediately and blocks new or increased billing time. The coordinator is emailed, with you copied.'}</span>
        </div>
        <label htmlFor="engineer-closeout-reason">
          {reopening ? 'Required reopen reason' : 'Completion summary'}
          <textarea
            id="engineer-closeout-reason"
            value={reason}
            maxLength={2000}
            autoFocus
            onChange={(event) => setReason(event.target.value)}
            placeholder={reopening
              ? 'Explain what additional work is required and why the task must be reopened…'
              : 'Summarize the work completed and any handoff details for the coordinator…'}
          />
        </label>
        <div className="engineer-closeout-dialog__count">
          <span>{reason.trim().length < minimum ? `${minimum - reason.trim().length} more characters required` : 'Ready to submit'}</span>
          <span>{reason.length}/2000</span>
        </div>
        {error ? <div className="engineer-closeout-alert engineer-closeout-alert--error">{error}</div> : null}
        <div className="engineer-closeout-dialog__actions">
          <button type="button" className="engineer-closeout-button engineer-closeout-button--quiet" onClick={onDismiss} disabled={busy}>Cancel</button>
          <button
            type="button"
            className="engineer-closeout-button engineer-closeout-button--primary"
            onClick={() => onSubmit(reason.trim())}
            disabled={busy || reason.trim().length < minimum}
          >
            <CloseoutIcon name={reopening ? 'refresh' : 'check'} />
            {busy ? 'Saving…' : reopening ? 'Reopen and notify' : 'Close and notify'}
          </button>
        </div>
      </section>
    </div>
  );
}

export default function EngineerTaskCloseoutCenter({ authSession }) {
  const [overview, setOverview] = useState(EMPTY_OVERVIEW);
  const [tab, setTab] = useState('active');
  const [requestType, setRequestType] = useState('All');
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [transition, setTransition] = useState(null);
  const [transitionBusy, setTransitionBusy] = useState(false);
  const [transitionError, setTransitionError] = useState('');
  const [page, setPage] = useState(1);
  const overviewAbortRef = useRef(null);
  const overviewRequestSequenceRef = useRef(0);

  const loadOverview = useCallback(async () => {
    const requestSequence = overviewRequestSequenceRef.current + 1;
    overviewRequestSequenceRef.current = requestSequence;
    overviewAbortRef.current?.abort();
    const controller = new AbortController();
    overviewAbortRef.current = controller;
    setLoading(true);
    setError('');

    let lastError = null;
    try {
      for (let attempt = 0; attempt < OVERVIEW_RETRY_DELAYS_MS.length; attempt += 1) {
        if (OVERVIEW_RETRY_DELAYS_MS[attempt] > 0) {
          await waitForOverviewRetry(OVERVIEW_RETRY_DELAYS_MS[attempt], controller.signal);
        }

        try {
          const response = await fetch('/api/engineer-task-closeout/overview', {
            credentials: 'include',
            cache: 'no-store',
            signal: controller.signal,
            headers: {
              ...sessionHeaders(authSession),
              'X-ProjectPulse-Module001A-Read-Contract': MODULE001A_VISIBLE_REQUEST_FAMILIES_CONTRACT
            }
          });
          if (!response.ok && OVERVIEW_RETRYABLE_STATUS.has(response.status)
              && attempt < OVERVIEW_RETRY_DELAYS_MS.length - 1) {
            await response.text();
            continue;
          }
          const result = await readResponse(response);
          if (requestSequence === overviewRequestSequenceRef.current) {
            setOverview({ ...EMPTY_OVERVIEW, ...result });
          }
          return;
        } catch (loadError) {
          if (loadError?.name === 'AbortError') throw loadError;
          lastError = loadError;
          if (attempt === OVERVIEW_RETRY_DELAYS_MS.length - 1) throw loadError;
        }
      }
    } catch (loadError) {
      if (loadError?.name !== 'AbortError' && requestSequence === overviewRequestSequenceRef.current) {
        setError(loadError instanceof Error ? loadError.message : 'Unable to load Engineer task closeout.');
      }
    } finally {
      if (requestSequence === overviewRequestSequenceRef.current) setLoading(false);
      if (overviewAbortRef.current === controller) overviewAbortRef.current = null;
      if (lastError?.name === 'AbortError') return;
    }
  }, [authSession]);

  useEffect(() => {
    void loadOverview();
    return () => overviewAbortRef.current?.abort();
  }, [loadOverview]);

  useEffect(() => {
    const refresh = () => void loadOverview();
    const events = [
      'projectpulse:timesheet-work-queue-changed',
      'projectpulse:work-register-assignment-changed',
      'projectpulse:auth-session-ready',
      'projectpulse:view-as-changed'
    ];
    events.forEach((eventName) => window.addEventListener(eventName, refresh));
    window.addEventListener('pageshow', refresh);
    return () => {
      events.forEach((eventName) => window.removeEventListener(eventName, refresh));
      window.removeEventListener('pageshow', refresh);
    };
  }, [loadOverview]);

  const sourceItems = tab === 'active' ? overview.active : overview.history;
  const filteredItems = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return (sourceItems || []).filter((item) => {
      if (requestType !== 'All' && item.requestType !== requestType) return false;
      if (!needle) return true;
      return [item.projectCode, item.projectName, item.taskCode, item.taskName, item.customerName, item.serviceRequestNumber]
        .some((value) => String(value || '').toLowerCase().includes(needle));
    }).sort(compareCloseoutItems);
  }, [query, requestType, sourceItems]);
  const pageCount = Math.max(1, Math.ceil(filteredItems.length / PAGE_SIZE));
  const visibleItems = useMemo(() => {
    const start = (page - 1) * PAGE_SIZE;
    return filteredItems.slice(start, start + PAGE_SIZE);
  }, [filteredItems, page]);

  useEffect(() => {
    setPage(1);
  }, [tab, requestType, query]);

  useEffect(() => {
    setPage((current) => Math.min(current, pageCount));
  }, [pageCount]);

  const submitTransition = async (reason) => {
    if (!transition) return;
    setTransitionBusy(true);
    setTransitionError('');
    setSuccess('');
    const reopening = transition.mode === 'reopen';
    try {
      const response = await fetch(
        `/api/engineer-task-closeout/assignments/${transition.item.assignmentId}/${reopening ? 'reopen' : 'close'}`,
        {
          method: 'POST',
          credentials: 'include',
          headers: sessionHeaders(authSession, true),
          body: JSON.stringify(reopening ? { reason } : { completionSummary: reason })
        }
      );
      const result = await readResponse(response);
      setTransition(null);
      setSuccess(result.message || (reopening ? 'Task reopened.' : 'Task closed.'));
      setTab(reopening ? 'active' : 'history');
      await loadOverview();
      window.dispatchEvent(new CustomEvent('projectpulse:timesheet-work-queue-changed', {
        detail: { assignmentId: transition.item.assignmentId, action: reopening ? 'reopened' : 'closed' }
      }));
    } catch (transitionFailure) {
      setTransitionError(transitionFailure instanceof Error ? transitionFailure.message : 'Unable to save the task transition.');
    } finally {
      setTransitionBusy(false);
    }
  };

  return (
    <div className="engineer-closeout-center">
      <header className="engineer-closeout-hero">
        <div className="engineer-closeout-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="engineer-closeout-eyebrow">Pulse · Module 001A</p>
            <h1>Engineer Request Closeout</h1>
            <p>Finish assigned request work, stop additional billing, and hand final closure to the Project Team Coordinator.</p>
          </div>
        </div>
        <div className="engineer-closeout-hero__authority">
          <span className="engineer-closeout-live"><i /> Engineer workspace</span>
          <strong>Close here. Finalize in 055C.</strong>
          <small>Every transition is recorded and routed through Module 065.</small>
        </div>
      </header>

      <section className="engineer-closeout-workflow" aria-label="Closeout workflow">
        <div><span>1</span><p><strong>Complete assigned work</strong><small>Service Request · Pre-Sales · Internal</small></p></div>
        <i aria-hidden="true" />
        <div><span>2</span><p><strong>Close in Module 001A</strong><small>Module 001 billing locks immediately</small></p></div>
        <i aria-hidden="true" />
        <div><span>3</span><p><strong>PTC receives notification</strong><small>Engineer included as CC</small></p></div>
        <i aria-hidden="true" />
        <div><span>4</span><p><strong>PTC finalizes in 055C</strong><small>Reopen is no longer available</small></p></div>
      </section>

      <section className="engineer-closeout-metrics" aria-label="Closeout summary">
        <article><span className="engineer-closeout-metric-icon is-blue"><CloseoutIcon /></span><p>Active tasks<strong>{overview.summary?.activeCount ?? 0}</strong><small>Ready for Engineer action</small></p></article>
        <article><span className="engineer-closeout-metric-icon is-navy"><CloseoutIcon name="history" /></span><p>History<strong>{overview.summary?.historyCount ?? 0}</strong><small>Engineer and final closures</small></p></article>
        <article><span className="engineer-closeout-metric-icon is-cyan"><CloseoutIcon name="refresh" /></span><p>Reopen eligible<strong>{overview.summary?.reopenEligibleCount ?? 0}</strong><small>Original request still open</small></p></article>
        <article><span className="engineer-closeout-metric-icon is-green"><CloseoutIcon name="lock" /></span><p>Billing locked<strong>{overview.summary?.billingLockedCount ?? 0}</strong><small>New or increased time blocked</small></p></article>
      </section>

      {success ? <div className="engineer-closeout-alert engineer-closeout-alert--success"><CloseoutIcon /> {success}</div> : null}
      {error ? <div className="engineer-closeout-alert engineer-closeout-alert--error">{error}</div> : null}

      <section className="engineer-closeout-workspace">
        <div className="engineer-closeout-toolbar">
          <div className="engineer-closeout-tabs" role="tablist" aria-label="Engineer task lists">
            <button type="button" role="tab" aria-selected={tab === 'active'} className={tab === 'active' ? 'is-active' : ''} onClick={() => setTab('active')}>
              Active <span>{overview.summary?.activeCount ?? 0}</span>
            </button>
            <button type="button" role="tab" aria-selected={tab === 'history'} className={tab === 'history' ? 'is-active' : ''} onClick={() => setTab('history')}>
              Historical <span>{overview.summary?.historyCount ?? 0}</span>
            </button>
          </div>
          <button type="button" className="engineer-closeout-refresh" onClick={() => void loadOverview()} disabled={loading} aria-label="Refresh Engineer closeout tasks">
            <CloseoutIcon name="refresh" /> {loading ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>

        <div className="engineer-closeout-filters">
          <label className="engineer-closeout-search">
            <span aria-hidden="true">⌕</span>
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search request, project, customer, or task" />
          </label>
          <div className="engineer-closeout-filter-pills" aria-label="Filter by request type">
            {['All', 'Service Request', 'Pre-Sales', 'Internal'].map((type) => (
              <button type="button" key={type} className={requestType === type ? 'is-active' : ''} onClick={() => setRequestType(type)}>{type}</button>
            ))}
          </div>
        </div>

        {loading ? (
          <div className="engineer-closeout-empty"><span className="engineer-closeout-spinner" /><h2>Loading your assigned work</h2><p>Checking current request and closeout status.</p></div>
        ) : filteredItems.length === 0 ? (
          <div className="engineer-closeout-empty">
            <span className="engineer-closeout-empty__icon"><CloseoutIcon name={tab === 'active' ? 'check' : 'history'} /></span>
            <h2>{tab === 'active'
              ? (sourceItems.length === 0 ? 'No tasks are available for closeout' : 'No tasks match the selected filters')
              : (sourceItems.length === 0 ? 'No closeout history is available' : 'No historical tasks match the selected filters')}</h2>
            <p>{tab === 'active'
              ? (sourceItems.length === 0
                ? 'This engineer currently has no Service Request, Pre-Sales, or Internal assignment eligible for closeout. No action is required.'
                : 'Adjust the search or request-type filter to review the engineer’s eligible assignments.')
              : (sourceItems.length === 0
                ? 'Completed closeout evidence will appear here after an engineer closes an eligible assignment.'
                : 'Adjust the search or request-type filter to review retained closeout evidence.')}</p>
          </div>
        ) : (
          <div className="engineer-closeout-task-list">
            {visibleItems.map((item) => (
              <TaskCard
                key={item.assignmentId}
                item={item}
                historical={tab === 'history'}
                onClose={(selected) => { setTransitionError(''); setTransition({ mode: 'close', item: selected }); }}
                onReopen={(selected) => { setTransitionError(''); setTransition({ mode: 'reopen', item: selected }); }}
              />
            ))}
          </div>
        )}
        {!loading && filteredItems.length > PAGE_SIZE ? (
          <nav className="engineer-closeout-pagination" aria-label={`${tab} task pages`}>
            <span>
              Showing {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, filteredItems.length)} of {filteredItems.length}
            </span>
            <div>
              <button type="button" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page === 1}>Previous</button>
              <strong>Page {page} of {pageCount}</strong>
              <button type="button" onClick={() => setPage((current) => Math.min(pageCount, current + 1))} disabled={page === pageCount}>Next</button>
            </div>
          </nav>
        ) : null}
      </section>

      {tab === 'history' && overview.events?.length > 0 ? (
        <section className="engineer-closeout-evidence">
          <div><p className="engineer-closeout-eyebrow">Immutable workflow evidence</p><h2>Recent task activity</h2></div>
          <div className="engineer-closeout-timeline">
            {overview.events.slice(0, 12).map((event) => (
              <article key={event.eventId}>
                <span className={`engineer-closeout-timeline__dot is-${event.eventType}`} />
                <p><strong>{String(event.eventType).replaceAll('_', ' ')}</strong><span>{event.reason}</span></p>
                <small>{event.actorName} · {formatDate(event.occurredAt)} · Email {String(event.notificationStatus || 'not queued').replaceAll('_', ' ')}</small>
              </article>
            ))}
          </div>
        </section>
      ) : null}

      <footer className="engineer-closeout-footer">
        <div><CloseoutIcon name="lock" /><span><strong>Billing protection</strong> Prior time remains auditable; new or increased time is blocked after Engineer closeout.</span></div>
        <div><CloseoutIcon name="mail" /><span><strong>Accountable handoff</strong> PTC receives the action and required reason; the assigned Engineer is copied.</span></div>
      </footer>

      <TransitionDialog
        transition={transition}
        busy={transitionBusy}
        error={transitionError}
        onDismiss={() => { if (!transitionBusy) setTransition(null); }}
        onSubmit={submitTransition}
      />
    </div>
  );
}

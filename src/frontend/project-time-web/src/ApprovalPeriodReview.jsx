import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './approval-period-review.css';

export const localDay = (date = new Date()) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
export function approvalPeriod(kind, date) {
  const value = new Date(`${date}T12:00:00`);
  if (Number.isNaN(value.getTime())) return null;
  if (kind === 'month') return { monthStart: `${date.slice(0, 7)}-01` };
  value.setDate(value.getDate() - value.getDay());
  return { weekStart: localDay(value) };
}
const key = item => [item.timesheetId, item.workDate, item.stage, item.projectId || 'day'].join('|');
const hours = value => Number(value || 0).toLocaleString(undefined, { maximumFractionDigits: 2 });

export default function ApprovalPeriodReview({ fetchPending, completePending, readOnly = false, access }) {
  const [kind, setKind] = useState('week');
  const [date, setDate] = useState(localDay());
  const [stage, setStage] = useState('manager');
  const [rows, setRows] = useState([]);
  const [selected, setSelected] = useState(new Set());
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [reviewing, setReviewing] = useState(false);
  const [total, setTotal] = useState(0);
  const revision = useRef(0);
  useEffect(() => { if (access && !access.canManagerApprove && access.canProjectApprove) setStage('pm'); }, [access]);
  const period = useMemo(() => approvalPeriod(kind, date), [kind, date]);
  const chosen = rows.filter(item => selected.has(key(item)));
  const load = useCallback(async () => {
    const version = ++revision.current;
    setRows([]); setSelected(new Set()); setReviewing(false); setError(''); setTotal(0);
    if (!period) return;
    setLoading(true);
    try {
      const found = new Map();
      let page = 1;
      let result;
      do {
        result = await fetchPending({ ...period, stage, page, pageSize: 500 });
        if (version !== revision.current) return;
        for (const item of result.items || []) found.set(key(item), item);
        page = result.nextPage;
      } while (result.hasMore && page && found.size < 2000);
      setRows([...found.values()].slice(0, 2000));
      setTotal(result.filteredCount || 0);
    } catch (failure) {
      if (version === revision.current) setError(failure.message || 'Unable to load unapproved time.');
    } finally { if (version === revision.current) setLoading(false); }
  }, [period, stage, fetchPending]);
  useEffect(() => { void load(); return () => { revision.current++; }; }, [load]);
  useEffect(() => {
    const refresh = () => { void load(); };
    window.addEventListener('projectpulse:approval-queue-changed', refresh);
    return () => window.removeEventListener('projectpulse:approval-queue-changed', refresh);
  }, [load]);
  const all = rows.length > 0 && chosen.length === rows.length;
  function toggle(item) {
    setReviewing(false);
    setSelected(previous => { const next = new Set(previous); next.has(key(item)) ? next.delete(key(item)) : next.add(key(item)); return next; });
  }
  async function approve() {
    if (!chosen.length || readOnly || busy || !reviewing) return;
    setBusy(true); setError(''); setNotice('');
    try {
      const result = await completePending({ mode: 'selected', stage, ...period,
        items: chosen.map(({ timesheetId, workDate, stage, projectId, scopeKey, reviewToken }) => ({ timesheetId, workDate, stage, projectId, scopeKey, reviewToken })) });
      setNotice(`${result.completedCount || 0} approval units completed. ${stage === 'manager' ? 'Projects needing project review now move to that stage.' : 'Project review is complete for these selected scopes.'}`);
      await load();
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
    } catch (failure) { setError(failure.message || 'Approval failed. Refresh and review the selection.'); setReviewing(false); }
    finally { setBusy(false); }
  }
  return <section className="approval-period-review" aria-labelledby="approval-period-title" aria-busy={loading || busy}>
    <header><div><h3 id="approval-period-title">All unapproved time</h3>
      <p>Select a week or month, review the selection, then approve.</p></div>
      <button type="button" onClick={load} disabled={busy || loading}>Refresh time</button></header>
    <div className="approval-period-filters">
      <div className="approval-period-switch" role="group" aria-label="Period"><span>Period</span>{['week','month'].map(value => <button type="button" key={value} aria-pressed={kind === value} disabled={busy} onClick={() => { setKind(value); setNotice(''); }}>{value === 'week' ? 'Week' : 'Month'}</button>)}</div>
      <label>{kind === 'month' ? 'Month' : 'Date in week'}<input type={kind === 'month' ? 'month' : 'date'} value={kind === 'month' ? date.slice(0, 7) : date} disabled={busy}
        onChange={event => { setDate(kind === 'month' ? `${event.target.value}-01` : event.target.value); setNotice(''); }} /></label>
      <label>Approval stage<select value={stage} disabled={busy} onChange={event => { setStage(event.target.value); setNotice(''); }}><option value="manager" disabled={access && !access.canManagerApprove}>Manager review</option><option value="pm" disabled={access && !access.canProjectApprove}>PM / coordinator review</option></select></label>
    </div>
    {period ? <p className="approval-period-count">{total} records · {hours(rows.reduce((sum, item) => sum + Number(item.totalHours), 0))} hours awaiting {stage === 'manager' ? 'manager' : 'project'} approval</p> : <p>Select a valid period.</p>}
    <details className="approval-period-help"><summary>Who approves this time?</summary><p>Project: Manager → PM / coordinator. Service requests, internal tasks and presales: Manager only. PTCs may complete either stage. Your own time is excluded.</p></details>
    {readOnly ? <p role="status">Administrator View-As is read-only. Return to your own session to approve time.</p> : null}
    {error ? <p role="alert" className="approval-period-error">{error}</p> : null}
    {notice ? <p role="status">{notice}</p> : null}
    {loading ? <p role="status">Loading all authorized time in this period…</p> : <>
      {total > rows.length ? <p role="status">Showing the first {rows.length} of {total} units. Approve this reviewed batch, then refresh for the remaining time.</p> : null}
      <div className="approval-period-selection"><label className="approval-period-select"><input type="checkbox" checked={all} disabled={!rows.length || busy || readOnly} onChange={() => { setSelected(all ? new Set() : new Set(rows.map(key))); setReviewing(false); }} />
        {total > rows.length ? 'Select all loaded time' : `Select all ${kind}`} ({rows.length})</label><button type="button" disabled={!chosen.length || busy} onClick={() => { setSelected(new Set()); setReviewing(false); }}>Clear selection</button></div>
      {!rows.length ? <p>No unapproved time at this stage in the selected period.</p> : <div className="approval-period-table"><table><caption className="approval-visually-hidden">Authorized unapproved time</caption><thead><tr><th scope="col">Select</th><th scope="col">Employee</th><th scope="col">Date</th><th scope="col">Work</th><th scope="col">Hours</th><th scope="col">Status</th></tr></thead>
        <tbody>{rows.map(item => <tr key={key(item)} data-selected={selected.has(key(item))}><td><input type="checkbox" aria-label={`Select ${item.resourceName}, ${item.workDate}, ${item.projectCodes || 'employee day'}`} checked={selected.has(key(item))} disabled={busy || readOnly} onChange={() => toggle(item)} /></td>
          <th scope="row">{item.resourceName}</th><td>{item.workDate}</td><td>{item.projectNames || item.projectCodes || 'Non-project time'}<small>{stage === 'pm' ? 'This project scope only' : 'All submitted entries for this employee day'}</small></td><td>{hours(item.totalHours)}</td><td><span className="approval-period-badge">Awaiting {stage === 'manager' ? 'manager' : 'PM / coordinator'}</span></td></tr>)}</tbody></table></div>}
      <footer><strong>{chosen.length} selected · {hours(chosen.reduce((sum, item) => sum + Number(item.totalHours), 0))} hours</strong>
        {!reviewing ? <button type="button" disabled={!chosen.length || busy || readOnly} onClick={() => setReviewing(true)} className="approval-period-primary">Review selected ({chosen.length})</button> : <div role="region" aria-label="Confirm selected approvals">
          <p>Complete {stage === 'manager' ? 'Manager' : 'PM / coordinator'} approval for these {chosen.length} units? This will be recorded under your account. Newly submitted time is excluded.</p>
          <button type="button" disabled={busy || readOnly} onClick={approve}>{busy ? 'Approving…' : 'Confirm approval'}</button>
          <button type="button" disabled={busy} onClick={() => setReviewing(false)}>Keep reviewing</button></div>}
      </footer>
    </>}
  </section>;
}

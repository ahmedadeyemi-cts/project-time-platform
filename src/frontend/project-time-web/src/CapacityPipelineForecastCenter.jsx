import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './capacity-pipeline-forecast-center.css';
import './projectpulse-module-standard.css';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function headers(authSession) {
  const token = sessionToken(authSession);
  return token ? {
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  } : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    headers: headers(authSession)
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new Error(payload?.message ?? `Forecast request returned HTTP ${response.status}.`);
  return payload;
}

function mondayIso() {
  const date = new Date();
  const day = date.getDay();
  const offset = (day + 6) % 7;
  date.setDate(date.getDate() - offset);
  return date.toISOString().slice(0, 10);
}

function number(value, digits = 1) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed.toLocaleString(undefined, { maximumFractionDigits: digits }) : '—';
}

function dateText(value) {
  if (!value) return 'Not dated';
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString();
}

function label(value) {
  return String(value ?? '').replace(/_/g, ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export default function CapacityPipelineForecastCenter({ authSession }) {
  const [filters, setFilters] = useState({
    startDate: mondayIso(),
    weeks: '14',
    practice: 'all',
    engineerUserId: '',
    supplementalHoursPerWeek: '0'
  });
  const [state, setState] = useState({ loading: true, model: null, engineers: [], forecast: null, error: '' });

  const loadReferenceData = useCallback(async () => {
    const [model, engineers] = await Promise.all([
      readJson('/api/capacity-forecast/model', authSession),
      readJson('/api/capacity-forecast/engineers', authSession)
    ]);
    return { model, engineers: engineers.engineers ?? [] };
  }, [authSession]);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const reference = state.model
        ? { model: state.model, engineers: state.engineers }
        : await loadReferenceData();
      const query = new URLSearchParams({
        startDate: filters.startDate,
        weeks: String(Math.max(4, Math.min(52, Number(filters.weeks) || 14))),
        practice: filters.practice,
        supplementalHoursPerWeek: String(Math.max(0, Number(filters.supplementalHoursPerWeek) || 0))
      });
      if (filters.engineerUserId) query.set('engineerUserId', filters.engineerUserId);
      const forecast = await readJson(`/api/capacity-forecast/forecast?${query.toString()}`, authSession);
      setState({ loading: false, model: reference.model, engineers: reference.engineers, forecast, error: '' });
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: error?.message ?? 'Capacity forecast is unavailable.' }));
    }
  }, [authSession, filters, loadReferenceData, state.engineers, state.model]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 180);
    return () => window.clearTimeout(timer);
  }, [load]);

  const refreshIdentities = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const reference = await loadReferenceData();
      const selectedStillExists = reference.engineers.some((engineer) => engineer.userId === filters.engineerUserId);
      if (filters.engineerUserId && !selectedStillExists) {
        setFilters((current) => ({ ...current, engineerUserId: '' }));
      }
      setState((current) => ({ ...current, model: reference.model, engineers: reference.engineers, loading: false }));
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: error?.message ?? 'Identity choices are unavailable.' }));
    }
  }, [filters.engineerUserId, loadReferenceData]);

  const summary = state.forecast?.summary ?? {};
  const selectedEngineer = useMemo(
    () => state.engineers.find((engineer) => engineer.userId === filters.engineerUserId),
    [filters.engineerUserId, state.engineers]
  );

  return (
    <section
      id="capacity-pipeline-forecast"
      className="panel capacity-forecast-center projectpulse-module-standard"
      data-module="070"
      data-brand="us-signal"
      data-mode="read-only-live-scenario"
      aria-labelledby="capacity-forecast-title"
    >
      <header className="capacity-forecast-hero">
        <img
          className="projectpulse-module-standard__logo"
          src={usSignalLogoDataUrl}
          alt="US Signal"
        />
        <div>
          <p className="eyebrow">Module 070 · Professional Services capacity planning</p>
          <h1 id="capacity-forecast-title">Capacity &amp; Pipeline Forecasting</h1>
          <p>
            Compare committed hours, weighted future demand, supplemental scenario
            capacity, and available engineering hours across continuous Monday-based weeks.
          </p>
        </div>
        <div className="capacity-forecast-scope">
          <span>{label(state.forecast?.access?.scope ?? state.model?.access?.scope ?? 'loading')}</span>
          <small>{state.loading ? 'Refreshing…' : 'Server-authorized scope'}</small>
        </div>
      </header>

      {state.error ? <div className="capacity-forecast-banner error" role="alert">{state.error}</div> : null}
      <div className="capacity-forecast-banner governed">
        Engineer names come from the shared identity source. Add or rename people in <a href="#user-admin">User Administration</a>;
        update staffing dates in <a href="#project-intake">Project Intake</a>. Refresh here to see either change immediately.
      </div>

      <section className="capacity-forecast-card">
        <div className="capacity-forecast-heading">
          <div><p className="eyebrow">Editable scenario</p><h2>Forecast controls</h2></div>
          <div className="capacity-forecast-actions">
            <button type="button" className="secondary-action" onClick={refreshIdentities} disabled={state.loading}>Refresh names</button>
            <button type="button" className="primary-action" onClick={load} disabled={state.loading}>Refresh forecast</button>
          </div>
        </div>
        <div className="capacity-forecast-filters">
          <label>
            <span>Forecast start date</span>
            <input type="date" value={filters.startDate} onChange={(event) => setFilters((current) => ({ ...current, startDate: event.target.value }))} />
            <small>Normalized to the Monday of the selected week.</small>
          </label>
          <label>
            <span>Horizon (weeks)</span>
            <input type="number" min="4" max="52" step="1" value={filters.weeks} onChange={(event) => setFilters((current) => ({ ...current, weeks: event.target.value }))} />
            <small>4–52 continuous weeks; workbook default is 14.</small>
          </label>
          <label>
            <span>Practice</span>
            <select value={filters.practice} onChange={(event) => setFilters((current) => ({ ...current, practice: event.target.value }))}>
              {(state.model?.practices ?? [
                { code: 'all', label: 'All practices' },
                { code: 'collaboration', label: 'Collaboration' },
                { code: 'systems', label: 'Systems' },
                { code: 'networking', label: 'Networking' },
                { code: 'other', label: 'Other' }
              ]).map((practice) => <option value={practice.code} key={practice.code}>{practice.label}</option>)}
            </select>
          </label>
          <label>
            <span>Engineer</span>
            <select value={filters.engineerUserId} onChange={(event) => setFilters((current) => ({ ...current, engineerUserId: event.target.value }))}>
              <option value="">All authorized engineers</option>
              {state.engineers.map((engineer) => (
                <option value={engineer.userId} key={engineer.userId}>
                  {engineer.displayName} · {engineer.teamName || engineer.departmentName || engineer.email}
                </option>
              ))}
            </select>
            <small>{selectedEngineer ? `${selectedEngineer.email} · stable identity ID` : 'Identity-backed, not copied from the workbook.'}</small>
          </label>
          <label>
            <span>Supplemental / LTE hours per week</span>
            <input type="number" min="0" max="10000" step="0.25" value={filters.supplementalHoursPerWeek} onChange={(event) => setFilters((current) => ({ ...current, supplementalHoursPerWeek: event.target.value }))} />
            <small>Scenario only; never written to the database.</small>
          </label>
        </div>
      </section>

      <div className="capacity-forecast-summary">
        <article><span>Available</span><strong>{number(summary.availableHours)}h</strong><small>Recorded capacity</small></article>
        <article><span>Committed</span><strong>{number(summary.committedHours)}h</strong><small>Assigned capacity plans</small></article>
        <article><span>Weighted pipeline</span><strong>{number(summary.weightedPipelineHours)}h</strong><small>Unfilled future demand</small></article>
        <article><span>Remaining</span><strong>{number(summary.remainingHours)}h</strong><small>{summary.overCapacityWeeks ?? 0} constrained weeks</small></article>
        <article><span>Utilization</span><strong>{summary.utilizationPercent == null ? '—' : `${number(summary.utilizationPercent)}%`}</strong><small>Zero-capacity guarded</small></article>
      </div>

      <section className="capacity-forecast-card">
        <div className="capacity-forecast-heading">
          <div><p className="eyebrow">Weekly calculation</p><h2>Capacity outlook</h2></div>
          <span>{state.forecast?.weeks?.length ?? 0} weeks</span>
        </div>
        <div className="capacity-forecast-table-wrap">
          <table>
            <thead><tr><th>Week</th><th>Available</th><th>Committed</th><th>Weighted pipeline</th><th>Supplemental</th><th>Net demand</th><th>Remaining</th><th>Utilization</th><th>State</th></tr></thead>
            <tbody>
              {(state.forecast?.weeks ?? []).map((week) => (
                <tr key={week.weekStart}>
                  <td><strong>{dateText(week.weekStart)}</strong><small>through {dateText(week.weekEnd)}</small></td>
                  <td>{number(week.availableHours)}h</td>
                  <td>{number(week.committedHours)}h</td>
                  <td>{number(week.weightedPipelineHours)}h</td>
                  <td>{number(week.supplementalHours)}h</td>
                  <td>{number(week.netDemandHours)}h</td>
                  <td className={week.remainingHours < 0 ? 'capacity-negative' : ''}>{number(week.remainingHours)}h</td>
                  <td>{week.utilizationPercent == null ? '—' : `${number(week.utilizationPercent)}%`}</td>
                  <td><span className={`capacity-state ${week.state}`}>{label(week.state)}</span></td>
                </tr>
              ))}
              {!state.loading && !(state.forecast?.weeks ?? []).length ? <tr><td colSpan="9" className="capacity-forecast-empty">No weekly forecast rows are available.</td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>

      <section className="capacity-forecast-card">
        <div className="capacity-forecast-heading">
          <div><p className="eyebrow">Pipeline evidence</p><h2>Included engineering requests</h2></div>
          <span>{state.forecast?.demand?.length ?? 0} requests</span>
        </div>
        <div className="capacity-forecast-table-wrap">
          <table>
            <thead><tr><th>Request / project</th><th>Function / skills</th><th>Dates</th><th>Requested</th><th>Committed allocation</th><th>Unfilled</th><th>Weight</th><th>Weighted</th><th>Status</th></tr></thead>
            <tbody>
              {(state.forecast?.demand ?? []).map((request) => (
                <tr key={request.requestId}>
                  <td><strong>{request.requestNumber}</strong><small>{request.projectCode || request.projectName || 'No project linked'}</small></td>
                  <td>{request.requestedFunction}<small>{request.skillRequirements || label(request.practice)}</small></td>
                  <td>{dateText(request.startDate)}<small>through {dateText(request.endDate)}</small></td>
                  <td>{number(request.requestedHours)}h</td>
                  <td>{number(request.committedAllocationHours)}h</td>
                  <td>{number(request.unfilledHours)}h</td>
                  <td>{number(Number(request.probabilityWeight) * 100, 0)}%</td>
                  <td>{number(request.weightedHours)}h</td>
                  <td><span className="capacity-state demand">{label(request.requestStatus)}</span><small>{label(request.priority)} priority</small></td>
                </tr>
              ))}
              {!state.loading && !(state.forecast?.demand ?? []).length ? <tr><td colSpan="9" className="capacity-forecast-empty">No open engineering requests overlap this scenario.</td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>

      <footer className="capacity-forecast-footnote">
        <strong>Calculation:</strong> max(committed + weighted unfilled pipeline − supplemental, 0) = net demand;
        available − net demand = remaining capacity. Opportunity dollars are never converted to hours.
      </footer>
    </section>
  );
}

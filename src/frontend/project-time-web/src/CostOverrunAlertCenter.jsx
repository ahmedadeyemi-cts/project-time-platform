import { loadFinancialPortfolio } from './project-financial-portfolio.js';
import { useEffect, useMemo, useRef, useState } from 'react';
import './cost-overrun-alert-center.css';
import { authoritativeApi } from './projectpulse-authoritative-api.js';

async function request(path, options = {}) {
  return authoritativeApi(path, options);
}

const money = (value) => value == null
  ? 'Not available'
  : Number(value).toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
const percent = (value) => value == null || !Number.isFinite(Number(value)) ? 'Not available' : `${Number(value).toFixed(1)}%`;
const words = (value) => String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
const when = (value) => value ? new Date(value).toLocaleString() : 'Not recorded';

function authoritativeState(project) {
  const sourceIncomplete = (project.missing || []).some((item) => (
    item === 'labor_budget'
    || item === 'expense_budget'
    || item.startsWith('source:')
  ));
  const varianceIncomplete = String(project.varianceCompleteness || '').includes('missing');
  if (sourceIncomplete || varianceIncomplete || project.forecastedFinalCost == null || project.currentVariance == null) {
    return { key: 'data_incomplete', label: 'Data incomplete', tone: 'incomplete', reason: 'A required budget, expense, rate, or financial source is missing. No over-budget conclusion is asserted.' };
  }
  if (project.budgetStatus === 'over_budget') {
    return { key: 'over_budget', label: 'Over budget', tone: 'critical', reason: 'Estimated forecast exceeds the recorded labor and expense budget.' };
  }
  if (project.budgetStatus === 'approaching_budget') {
    return { key: 'approaching_budget', label: 'Approaching budget', tone: 'warning', reason: 'Forecast at completion is at least 85% of the approved budget.' };
  }
  return { key: 'within_budget', label: 'Within budget', tone: 'healthy', reason: 'The estimated forecast remains below the alert threshold; verify internal costs and remaining work before concluding the project is within budget.' };
}

export default function CostOverrunAlertCenter({ canManageCostAlerts = false }) {
  const [state, setState] = useState({ loading: true, financial: null, alerts: null, errors: [] });
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState('action');
  const [selectedProjectId, setSelectedProjectId] = useState('');
  const generation = useRef(0);
  const [actionStatus, setActionStatus] = useState('');
  const [notes, setNotes] = useState({});

  async function load() {
    const currentGeneration = ++generation.current;
    setState((current) => ({ ...current, loading: true, errors: [] }));
    const [financialResult, alertResult] = await Promise.allSettled([
      loadFinancialPortfolio(request, { workspace: 'pm', limit: '250' }, () => currentGeneration === generation.current),
      request('/api/projects/cost-alerts')
    ]);
    if (currentGeneration !== generation.current) return;
    const errors = [];
    if (financialResult.status === 'rejected') errors.push(`Authoritative project financials: ${financialResult.reason?.message || 'unavailable'}`);
    if (alertResult.status === 'rejected') errors.push(`Persisted alert workflow: ${alertResult.reason?.message || 'unavailable'}`);
    setState({
      loading: false,
      financial: financialResult.status === 'fulfilled' ? financialResult.value : null,
      alerts: alertResult.status === 'fulfilled' ? alertResult.value : null,
      errors
    });
  }

  useEffect(() => {
    const reset = () => {
      generation.current += 1;
      setState({ loading: true, financial: null, alerts: null, errors: [] });
      setSelectedProjectId(''); setNotes({}); setActionStatus('');
      void load();
    };
    void load();
    window.addEventListener('projectpulse:auth-session-ready', reset);
    window.addEventListener('projectpulse:view-as-changed', reset);
    return () => {
      generation.current += 1;
      window.removeEventListener('projectpulse:auth-session-ready', reset);
      window.removeEventListener('projectpulse:view-as-changed', reset);
    };
  }, []);

  const persistedByProject = useMemo(() => {
    const map = new Map();
    (state.alerts?.alerts || []).forEach((alert) => {
      const key = String(alert.projectId || '').toLowerCase();
      if (!map.has(key)) map.set(key, []);
      map.get(key).push(alert);
    });
    return map;
  }, [state.alerts]);

  const rows = useMemo(() => (state.financial?.projects || []).map((project) => {
    const posture = authoritativeState(project);
    const budget = project.laborBudget == null || project.expenseBudget == null
      ? null
      : Number(project.laborBudget) + Number(project.expenseBudget);
    const forecast = project.forecastedFinalCost == null ? null : Number(project.forecastedFinalCost);
    const variance = project.currentVariance == null ? null : Number(project.currentVariance);
    const variancePercent = budget && forecast != null ? ((forecast - budget) / budget) * 100 : null;
    const persisted = persistedByProject.get(String(project.projectId).toLowerCase()) || [];
    return { project, posture, budget, forecast, variance, variancePercent, persisted };
  }), [persistedByProject, state.financial]);

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    return rows.filter((row) => {
      if (filter === 'action' && !['over_budget', 'approaching_budget', 'data_incomplete'].includes(row.posture.key)) return false;
      if (filter !== 'all' && filter !== 'action' && row.posture.key !== filter) return false;
      if (!query) return true;
      return [row.project.customerName, row.project.projectCode, row.project.projectName, row.project.projectManagerName]
        .some((value) => String(value || '').toLowerCase().includes(query));
    });
  }, [filter, rows, search]);

  const selectedRow = filtered.find(row => row.project.projectId === selectedProjectId) || filtered[0];

  const summary = useMemo(() => ({
    over: rows.filter((row) => row.posture.key === 'over_budget').length,
    approaching: rows.filter((row) => row.posture.key === 'approaching_budget').length,
    incomplete: rows.filter((row) => row.posture.key === 'data_incomplete').length,
    total: rows.length
  }), [rows]);

  async function updateStatus(alert, alertStatus) {
    if (!canManageCostAlerts) return;
    setActionStatus(`Updating ${alert.projectCode || 'cost alert'}…`);
    try {
      const result = await request(`/api/projects/cost-alerts/${alert.alertId}/status`, {
        method: 'POST',
        body: JSON.stringify({ alertStatus, note: notes[alert.alertId] || '' })
      });
      setActionStatus(`Stored alert updated to ${words(result.alertStatus)}.`);
      setNotes((current) => ({ ...current, [alert.alertId]: '' }));
      await load();
    } catch (error) {
      setActionStatus(error.message);
    }
  }

  async function releaseNotification(alert) {
    if (!canManageCostAlerts) return;
    setActionStatus(`Validating notification routing for ${alert.projectCode || 'cost alert'}…`);
    try {
      const result = await request(`/api/projects/cost-alerts/${alert.alertId}/release-notification`, {
        method: 'POST',
        body: JSON.stringify({ routingNote: notes[alert.alertId] || '' })
      });
      setActionStatus(result.message || 'Notification routing released.');
      setNotes((current) => ({ ...current, [alert.alertId]: '' }));
      await load();
    } catch (error) {
      setActionStatus(error.message);
    }
  }

  return (
    <section className="cost-alert-center" data-module="022">
      <aside className="cost-alert-notice" role="note"><strong>What needs attention?</strong><p>Start with projects over budget or approaching budget. Select a project, review its budget and expense sources, then record the follow-up action. A missing budget or failed source means Data incomplete.</p><p>Forecast = estimated labor value + recorded project expenses. These estimates do not verify actual internal labor cost.</p></aside>
      <details className="cost-alert-panel"><summary>How is overbudget calculated?</summary>
        <p>Budget is the recorded engineering and PM labor budget plus the expense allowance. Forecast is logged hours plus remaining allocated hours, valued at the governed billing-rate estimate, plus current expense uploads. Negative budget minus forecast means an estimated overrun; the warning threshold is 85%.</p>
        <p>Billing rates are not internal labor costs. Purchase commitments, approved changes, future expenses, and independently estimated remaining work are not yet fully reconciled here. Treat this as a forecast warning, not a verified final project cost. Missing budget components, failed sources, or expenses requiring currency conversion prevent a complete conclusion.</p>
      </details>
      <header className="cost-alert-header">
        <div>
          <p className="eyebrow">Module 022 · Project financial control</p>
          <h2>Project Cost Alerts</h2>
          <p className="muted">Monitor approved budget, committed cost, forecast at completion, and project variance from the governed project-financial source. Incomplete data is shown as incomplete rather than misclassified as an overrun.</p>
        </div>
        <div className="cost-alert-header-actions"><span className="cost-alert-mode">{canManageCostAlerts ? 'Alert workflow enabled' : 'Read only'}</span><button type="button" className="secondary-action" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh financial posture'}</button></div>
      </header>

      {state.errors.length ? <div className="cost-alert-banner error" role="alert"><strong>Source status</strong><ul>{state.errors.map((error) => <li key={error}>{error}</li>)}</ul></div> : null}
      {actionStatus ? <div className="cost-alert-banner" role="status">{actionStatus}</div> : null}

      <div className="cost-alert-summary-grid">
        <article><span>Over budget</span><strong>{state.loading ? '…' : summary.over}</strong><small>Forecast exceeds approved budget</small></article>
        <article><span>Approaching budget</span><strong>{state.loading ? '…' : summary.approaching}</strong><small>Forecast is at least 85% of budget</small></article>
        <article><span>Data incomplete</span><strong>{state.loading ? '…' : summary.incomplete}</strong><small>No unsupported cost conclusion</small></article>
        <article><span>Visible projects</span><strong>{state.loading ? '…' : summary.total}</strong><small>Within current role and PM scope</small></article>
      </div>

      <section className="cost-alert-panel cost-alert-filter-panel" aria-label="Cost alert filters">
        <label>Search projects<input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Customer, project, or Project Manager" /></label>
        <label>Financial posture<select value={filter} onChange={(event) => setFilter(event.target.value)}><option value="action">Needs attention</option><option value="over_budget">Over budget</option><option value="approaching_budget">Approaching budget</option><option value="data_incomplete">Data incomplete</option><option value="within_budget">Within budget</option><option value="all">All visible projects</option></select></label>
        <label>Project to review<select value={selectedRow?.project.projectId || ''} onChange={event => setSelectedProjectId(event.target.value)} disabled={!filtered.length}>
          {!filtered.length && <option value="">No matching projects</option>}
          {filtered.map(row => <option key={row.project.projectId} value={row.project.projectId}>{row.project.customerName} · {row.project.projectName} · {row.posture.label}</option>)}
        </select></label>
        <span>{filtered.length} of {rows.length} project(s)</span>
      </section>

      <div className="cost-alert-card-list">
        {(selectedRow ? [selectedRow] : []).map(({ project, posture, budget, forecast, variance, variancePercent, persisted }) => (
          <article className={`cost-alert-card posture-${posture.tone}`} key={project.projectId}>
            <div className="cost-alert-card-header">
              <div><span>{project.customerName || 'Customer not recorded'}</span><strong>{project.projectCode} · {project.projectName}</strong><small>Project Manager: {project.projectManagerName || 'Unassigned'}</small></div>
              <em>{posture.label}</em>
            </div>
            <p className="cost-alert-posture-reason">{posture.reason}</p>
            <div className="cost-alert-financial-grid">
              <span>Approved budget<strong>{money(budget)}</strong><small>Labor + expense budget</small></span>
              <span>Estimated cost to date<strong>{money(project.committedCost)}</strong><small>Rate-based labor estimate and uploaded expenses</small></span>
              <span>Forecast at completion<strong>{money(forecast)}</strong><small>Governed forecast basis</small></span>
              <span>Remaining budget<strong>{money(variance)}</strong><small>{variance != null && variance < 0 ? 'Negative indicates forecast overrun' : 'Budget less forecast'}</small></span>
              <span>Forecast variance<strong>{percent(variancePercent)}</strong><small>Relative to approved budget</small></span>
              <span>Completion<strong>{project.completionPercentage == null ? 'Not available' : percent(Number(project.completionPercentage))}</strong><small>{project.plannedHours} planned · {project.usedHours} used hours</small></span>
            </div>
            <div className="cost-alert-source-strip"><span>Financial source: <strong>{state.financial?.status ? words(state.financial.status) : 'Unavailable'}</strong></span><span>Calculated: <strong>{when(project.calculatedAt || state.financial?.generatedAt)}</strong></span><span>ConnectWise SELL readiness: <strong>{words(project.sell?.readinessStatus || 'not available')}</strong></span></div>
            {project.missing?.length ? <div className="cost-alert-missing"><strong>Missing authoritative evidence</strong><span>{project.missing.map(words).join(' · ')}</span></div> : null}
            <div className="cost-alert-project-actions"><a href="#project-workload">Open project financial workspace</a></div>

            {persisted.length ? <div className="cost-alert-persisted-workflow"><h3>Stored alert workflow</h3>{persisted.map((alert) => <div className="cost-alert-stored-item" key={alert.alertId}><div><strong>{words(alert.alertType)} · {words(alert.alertStatus)}</strong><span>{alert.alertSummary}</span><small>Last detected {when(alert.lastDetectedAt)} · Routing {words(alert.routingStatus || 'hold')} · {alert.notificationRecipientCount || 0} recipient(s)</small></div>{canManageCostAlerts ? <div className="cost-alert-action-panel"><textarea value={notes[alert.alertId] || ''} onChange={(event) => setNotes((current) => ({ ...current, [alert.alertId]: event.target.value }))} placeholder="Acknowledgement, resolution, or routing note" /><div className="cost-alert-action-row"><button type="button" className="secondary-action" onClick={() => updateStatus(alert, 'acknowledged')}>Acknowledge</button><button type="button" className="secondary-action" onClick={() => updateStatus(alert, 'resolved')}>Resolve</button><button type="button" className="secondary-action" onClick={() => updateStatus(alert, 'open')}>Reopen</button><button type="button" className="primary-action" onClick={() => releaseNotification(alert)} disabled={Boolean(alert.notificationQueuedAt) || alert.alertStatus === 'resolved'}>{alert.notificationQueuedAt ? 'Already queued' : 'Release notification'}</button></div></div> : null}</div>)}</div> : <div className="cost-alert-no-workflow"><strong>No stored alert action is required.</strong><span>The financial posture remains visible and will not be presented as a notification event until the governed alert workflow creates one.</span></div>}
          </article>
        ))}
        {!state.loading && !filtered.length ? <div className="cost-alert-panel"><strong>No projects match the selected financial posture.</strong></div> : null}
      </div>
    </section>
  );
}

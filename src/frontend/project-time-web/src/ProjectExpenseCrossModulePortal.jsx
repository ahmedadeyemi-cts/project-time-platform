import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import './project-expense-cross-module.css';

const ACTIVE_ROUTES = new Set(['invoice-billing-center', 'work-register']);

function route() {
  return window.location.hash.replace(/^#/, '').split('?')[0];
}

function money(value) {
  return Number(value || 0).toLocaleString(undefined, { style: 'currency', currency: 'USD' });
}

async function readJson(response) {
  const text = await response.text();
  if (!text.trim()) return {};
  try { return JSON.parse(text); } catch { return { status: 'invalid_json_response', message: text }; }
}

async function getJson(path, init) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...init,
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(init?.headers || {})
    }
  });
  const payload = await readJson(response);
  if (!response.ok) throw new Error(payload?.message || payload?.status || `Request failed with HTTP ${response.status}.`);
  return payload;
}

function projectLabel(project) {
  return `${project.customerName || 'Customer'} — ${project.projectCode || project.projectName || 'Project'}`;
}

export default function ProjectExpenseCrossModulePortal() {
  const [activeRoute, setActiveRoute] = useState(route);
  const [open, setOpen] = useState(false);
  const [context, setContext] = useState({ projects: [], loading: false, error: '' });
  const [projectId, setProjectId] = useState('');
  const [summary, setSummary] = useState({ loading: false, error: '', data: null });
  const [reason, setReason] = useState('');
  const [action, setAction] = useState({ running: false, error: '', success: '' });

  useEffect(() => {
    const sync = () => {
      const next = route();
      setActiveRoute(next);
      if (!ACTIVE_ROUTES.has(next)) setOpen(false);
    };
    const selectProject = (event) => {
      const selected = String(event?.detail?.projectId || '').trim();
      if (selected) setProjectId(selected);
    };
    window.addEventListener('hashchange', sync);
    window.addEventListener('projectpulse:project-context-changed', selectProject);
    sync();
    return () => {
      window.removeEventListener('hashchange', sync);
      window.removeEventListener('projectpulse:project-context-changed', selectProject);
    };
  }, []);

  useEffect(() => {
    if (!ACTIVE_ROUTES.has(activeRoute)) return;
    let active = true;
    setContext((current) => ({ ...current, loading: true, error: '' }));
    getJson('/api/project-expenses/context')
      .then((result) => {
        if (!active) return;
        const projects = Array.isArray(result?.projects) ? result.projects : [];
        setContext({ projects, loading: false, error: '' });
        setProjectId((current) => projects.some((project) => project.projectId === current) ? current : '');
      })
      .catch((error) => {
        if (!active) return;
        setContext({ projects: [], loading: false, error: error instanceof Error ? error.message : 'Unable to load project expense context.' });
      });
    return () => { active = false; };
  }, [activeRoute]);

  async function loadSummary(selectedProjectId = projectId) {
    if (!selectedProjectId) {
      setSummary({ loading: false, error: '', data: null });
      return;
    }
    setSummary((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await getJson(`/api/project-expenses/projects/${selectedProjectId}/billing-context`);
      setSummary({ loading: false, error: '', data });
    } catch (error) {
      setSummary({ loading: false, error: error instanceof Error ? error.message : 'Unable to load project expense billing context.', data: null });
    }
  }

  useEffect(() => {
    if (!projectId) {
      setSummary({ loading: false, error: '', data: null });
      return;
    }
    void loadSummary(projectId);
  }, [projectId]);

  async function acknowledge() {
    if (!projectId) return;
    if (reason.trim().length < 5) {
      setAction({ running: false, error: 'Enter a specific acknowledgement reason of at least five characters.', success: '' });
      return;
    }
    setAction({ running: true, error: '', success: '' });
    try {
      const result = await getJson(`/api/project-expenses/projects/${projectId}/billing-acknowledgement`, {
        method: 'POST',
        body: JSON.stringify({ reason: reason.trim() })
      });
      setAction({ running: false, error: '', success: result?.message || 'Project expenses were acknowledged.' });
      setReason('');
      await loadSummary(projectId);
      window.dispatchEvent(new CustomEvent('projectpulse:billing-data-changed', { detail: { projectId } }));
    } catch (error) {
      setAction({ running: false, error: error instanceof Error ? error.message : 'Unable to acknowledge project expenses.', success: '' });
    }
  }

  const selectedProject = useMemo(
    () => context.projects.find((project) => project.projectId === projectId) || null,
    [context.projects, projectId]
  );
  const data = summary.data;
  const currentUploadCount = Number(data?.currentUploadCount || 0);
  const hasCurrentExpenses = currentUploadCount > 0;
  const canAcknowledge = data?.actor?.canAcknowledgeForBilling === true;
  const treatment = data?.project?.billingTreatment || selectedProject?.billingTreatment || '';
  const acknowledgementText = treatment === 'pass_through_invoice'
    ? 'Acknowledge for invoice review'
    : treatment === 'included_fixed_price'
      ? 'Acknowledge as included project cost'
      : 'Acknowledge as tracked non-billable cost';

  if (!ACTIVE_ROUTES.has(activeRoute)) return null;

  return createPortal(
    <div className={`expense-cross-module-shell ${open ? 'is-open' : ''}`} data-project-expense-cross-module="non-invasive-v2">
      <button
        type="button"
        className="expense-cross-module-launcher"
        aria-expanded={open}
        aria-controls="project-expense-cross-module-panel"
        onClick={() => setOpen((value) => !value)}
      >
        <span>Project expenses</span>
        {projectId && hasCurrentExpenses ? <strong>{currentUploadCount}</strong> : null}
      </button>

      {open ? (
        <aside id="project-expense-cross-module-panel" className="expense-cross-module-panel" aria-label="Project expense billing context">
          <header>
            <div>
              <p className="eyebrow">Module 005 expense link</p>
              <h2>Project expenses</h2>
              <p>Review current uploads and acknowledge their billing treatment without leaving this page.</p>
            </div>
            <div className="expense-cross-header-actions">
              <a href="#project-allocation-info" onClick={() => setOpen(false)}>Open Module 005</a>
              <button type="button" className="expense-cross-close" aria-label="Close project expense panel" onClick={() => setOpen(false)}>×</button>
            </div>
          </header>

          <label>
            Project
            <select value={projectId} onChange={(event) => setProjectId(event.target.value)}>
              <option value="">Choose a project only when expense context is needed</option>
              {context.projects.map((project) => (
                <option key={project.projectId} value={project.projectId}>{projectLabel(project)}</option>
              ))}
            </select>
          </label>

          {context.loading || summary.loading ? <p className="expense-cross-state">Loading current expense context…</p> : null}
          {context.error || summary.error ? <p className="expense-cross-error">{context.error || summary.error}</p> : null}

          {!projectId && !context.loading ? (
            <div className="expense-cross-empty">
              <strong>No project selected</strong>
              <span>This panel stays collapsed and does not choose a project automatically.</span>
            </div>
          ) : null}

          {projectId && data && !hasCurrentExpenses ? (
            <div className="expense-cross-empty">
              <strong>No current project expenses</strong>
              <span>Deleted and superseded uploads are excluded. No billing acknowledgement is required.</span>
            </div>
          ) : null}

          {projectId && data && hasCurrentExpenses ? (
            <>
              <div className="expense-cross-kpis">
                <div><span>Current uploads</span><strong>{currentUploadCount}</strong></div>
                <div><span>Tracked expense</span><strong>{money(data.trackedExpenseTotal)}</strong></div>
                <div><span>Invoice eligible</span><strong>{money(data.invoiceEligibleExpenseTotal)}</strong></div>
                <div><span>Fixed-price included cost</span><strong>{money(data.fixedPriceIncludedCostTotal)}</strong></div>
              </div>

              <div className="expense-cross-treatment">
                <strong>{treatment === 'pass_through_invoice' ? 'Time and Material / pass-through' : treatment === 'included_fixed_price' ? 'Fixed Price included cost' : 'Internal non-billable'}</strong>
                <span>{treatment === 'pass_through_invoice'
                  ? 'An authorized acknowledgement makes the current reimbursable total available to Module 042.'
                  : 'An authorized acknowledgement records the expense as project cost without creating a separate invoice charge.'}</span>
              </div>

              <div className={`expense-cross-ack-status ${data.acknowledgementCurrent ? 'current' : 'attention'}`}>
                <strong>{data.acknowledgementCurrent ? 'Acknowledgement current' : 'Acknowledgement required'}</strong>
                <span>{data.acknowledgementCurrent
                  ? `${data.acknowledgement?.reviewedBy || 'Authorized user'} acknowledged this current upload set.`
                  : 'A PM, PTC, Accounting user, or Super Administrator must confirm the current upload set.'}</span>
              </div>

              {canAcknowledge && !data.acknowledgementCurrent ? (
                <div className="expense-cross-acknowledgement">
                  <label>
                    Acknowledgement reason
                    <textarea value={reason} onChange={(event) => setReason(event.target.value)} rows={3} placeholder="Explain why these current expenses are ready for billing treatment." />
                  </label>
                  <button type="button" disabled={action.running} onClick={() => void acknowledge()}>
                    {action.running ? 'Recording…' : acknowledgementText}
                  </button>
                </div>
              ) : null}

              {action.error ? <p className="expense-cross-error">{action.error}</p> : null}
              {action.success ? <p className="expense-cross-success">{action.success}</p> : null}

              <div className="expense-cross-list">
                {(data.uploads || []).map((upload) => (
                  <article key={upload.uploadId}>
                    <div>
                      <strong>v{upload.versionNumber} · {upload.expenseOwnerName}</strong>
                      <small>{upload.periodStart || 'Period not set'} – {upload.periodEnd || 'Period not set'}</small>
                    </div>
                    <strong>{money(upload.totalAmount)}</strong>
                  </article>
                ))}
              </div>
            </>
          ) : null}
        </aside>
      ) : null}
    </div>,
    document.body
  );
}

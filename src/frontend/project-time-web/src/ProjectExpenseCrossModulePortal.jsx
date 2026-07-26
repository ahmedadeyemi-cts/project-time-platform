import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import './project-expense-cross-module.css';

function route() { return String(window.location.hash || '').replace('#', ''); }
function visibleRoute() { return ['invoice-billing-center', 'work-register'].includes(route()); }
function headers() {
  const result = { Accept: 'application/json' };
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    if (session?.sessionToken) result['X-ProjectPulse-Session'] = session.sessionToken;
    const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    if (viewAs?.userId) result['X-ProjectPulse-View-As-User'] = viewAs.userId;
  } catch { /* global bridge fallback */ }
  return result;
}
async function json(path) {
  const response = await fetch(path, { headers: headers(), cache: 'no-store' });
  const raw = await response.text();
  let body = null;
  try { body = raw ? JSON.parse(raw) : null; } catch { body = null; }
  if (!response.ok) throw new Error(body?.message || raw || `HTTP ${response.status}`);
  return body;
}
function money(value) { return Number(value || 0).toLocaleString(undefined, { style: 'currency', currency: 'USD' }); }
function date(value) { const parsed = new Date(value); return value && !Number.isNaN(parsed.getTime()) ? parsed.toLocaleString() : 'Not available'; }

export default function ProjectExpenseCrossModulePortal() {
  const [active, setActive] = useState(visibleRoute());
  const [context, setContext] = useState(null);
  const [projectId, setProjectId] = useState('');
  const [summary, setSummary] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    const onRoute = () => setActive(visibleRoute());
    window.addEventListener('hashchange', onRoute);
    return () => window.removeEventListener('hashchange', onRoute);
  }, []);

  useEffect(() => {
    if (!active) return;
    let cancelled = false;
    void json('/api/project-expenses/context').then((result) => {
      if (cancelled) return;
      setContext(result);
      setProjectId((current) => current || result.projects?.[0]?.projectId || '');
    }).catch((failure) => !cancelled && setError(failure.message));
    return () => { cancelled = true; };
  }, [active]);

  useEffect(() => {
    if (!active || !projectId) { setSummary(null); return; }
    let cancelled = false;
    void json(`/api/project-expenses/projects/${projectId}/summary`).then((result) => {
      if (!cancelled) { setSummary(result); setError(''); }
    }).catch((failure) => !cancelled && setError(failure.message));
    return () => { cancelled = true; };
  }, [active, projectId]);

  const selected = useMemo(() => context?.projects?.find((project) => project.projectId === projectId), [context, projectId]);
  if (!active) return null;

  const panel = (
    <section className="expense-cross-module-panel" data-project-expense-cross-module={route()}>
      <header>
        <div><p className="eyebrow">MODULE 005 EXPENSE LINK</p><h2>Project expenses</h2><p>Read-only expense evidence from Project Expense Upload.</p></div>
        <a href="#project-allocation-info">Open Module 005</a>
      </header>
      <label>Project
        <select value={projectId} onChange={(event) => setProjectId(event.target.value)}>
          <option value="">Select project</option>
          {(context?.projects || []).map((project) => <option key={project.projectId} value={project.projectId}>{project.customerName} — {project.projectCode} {project.projectName}</option>)}
        </select>
      </label>
      {error ? <div className="expense-cross-error">{error}</div> : null}
      {selected && summary ? (
        <>
          <div className="expense-cross-kpis">
            <div><span>Current uploads</span><strong>{summary.currentUploadCount || 0}</strong></div>
            <div><span>Tracked expense</span><strong>{money(summary.trackedExpenseTotal)}</strong></div>
            <div><span>Invoice eligible</span><strong>{money(summary.invoiceEligibleExpenseTotal)}</strong></div>
            <div><span>Fixed-price included cost</span><strong>{money(summary.fixedPriceIncludedCostTotal)}</strong></div>
          </div>
          <div className="expense-cross-treatment"><strong>{selected.contractType || 'Contract type not configured'}</strong><span>{selected.billingTreatment === 'pass_through_invoice' ? 'Reimbursable expenses may be selected as customer invoice pass-through costs.' : 'Expenses remain project cost evidence and are included in the fixed project price.'}</span></div>
          <div className="expense-cross-list">
            {(summary.uploads || []).slice(0, 8).map((upload) => (
              <article key={upload.uploadId}><div><strong>{upload.expenseOwnerName}</strong><small>{upload.sourceMode === 'certify' ? 'Certify API' : 'CSV / Excel'} · v{upload.versionNumber}</small></div><div><strong>{money(upload.totalAmount)}</strong><small>{upload.lineCount} lines · {date(upload.uploadedAt)}</small></div></article>
            ))}
            {!summary.uploads?.length ? <p>No current project expense upload is associated with this project.</p> : null}
          </div>
        </>
      ) : null}
    </section>
  );

  return createPortal(panel, document.body);
}

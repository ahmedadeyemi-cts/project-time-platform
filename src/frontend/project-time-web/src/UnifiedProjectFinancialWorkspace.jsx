import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './projectpulse-module-standard.css';
import './unified-project-financial-workspace.css';

const workspaceConfig = {
  pm: {
    module: '018',
    eyebrow: 'Project Management workspace',
    title: 'Project portfolio command center',
    description: 'Assigned-project hours, financial position, delivery risk, notifications, and attention items for the selected Project Manager scope.',
    authorityTitle: 'Project Management authority',
    authorityDescription: 'Only projects allowed by the server-enforced Project Manager or Project Management Lead scope are returned. Portfolio costs, current expenses, forecasts, alerts, and project teams are reconciled for delivery decisions.',
    searchPlaceholder: 'Customer, project, Project Manager, or SELL quote…',
    refreshLabel: 'Refresh PM portfolio',
    defaultTab: 'overview',
    tabs: [['overview', 'Overview'], ['financials', 'Financials'], ['team', 'Team & SELL'], ['expenses', 'Expenses'], ['documents', 'Documents']]
  },
  engineering: {
    module: '019',
    eyebrow: 'Project workspace',
    title: 'Engineering assignments and project evidence',
    description: 'Assigned engineers, allocated and used hours, remaining work, project progress, and role-visible IQS, service-request, project, and customer documents.',
    authorityTitle: 'Engineering delivery context',
    authorityDescription: 'This workspace prioritizes assignments, remaining hours, task context, and downloadable project evidence. Commercial values remain limited to the current user’s role and assigned-project scope.',
    searchPlaceholder: 'Project, customer, PM, engineer, or document context…',
    refreshLabel: 'Refresh project workspace',
    defaultTab: 'team',
    tabs: [['team', 'Assignments'], ['documents', 'Documents'], ['overview', 'Project context'], ['financials', 'Cost context']]
  },
  sales: {
    module: '036',
    eyebrow: 'Sales insights workspace',
    title: 'Customer, SELL, and delivery readiness',
    description: 'Sales-owned and assigned projects with governed SELL association, Account Executive context, commercial status, forecast, budget signals, and delivery ownership.',
    authorityTitle: 'Sales and delivery handoff',
    authorityDescription: 'Module 036 emphasizes customer, Account Executive, SELL quote, contracted value, commercial readiness, and delivery risk without exposing restricted labor-cost detail.',
    searchPlaceholder: 'Customer, project, Account Executive, or SELL quote…',
    refreshLabel: 'Refresh sales portfolio',
    defaultTab: 'team',
    tabs: [['team', 'SELL & ownership'], ['financials', 'Commercial position'], ['overview', 'Delivery readiness']]
  },
  'rate-card': {
    module: '055B',
    eyebrow: 'Rate Card Administration context',
    title: 'Projects, customers, SELL, and governed rates',
    description: 'Project and customer rate coverage from the Module 026 governed SELL connection, including contract type, matched rate card, effective status, and missing-rate attention.',
    authorityTitle: 'Governed rate context',
    authorityDescription: 'Module 055B retains rate-card administration while this context identifies which customers and projects have a governed rate basis, SELL association, and complete rate coverage without another provider credential.',
    searchPlaceholder: 'Customer, project, contract type, rate card, or quote…',
    refreshLabel: 'Refresh governed rate context',
    defaultTab: 'team',
    tabs: [['team', 'Rate card & SELL'], ['financials', 'Rate impact'], ['overview', 'Project context']]
  }
};

function storedSession() {
  try {
    return JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
  } catch {
    return null;
  }
}

function requestHeaders() {
  const session = storedSession();
  const headers = {};
  const token = session?.sessionToken || session?.token || session?.accessToken || '';
  if (token) {
    headers.Authorization = `Bearer ${token}`;
    headers['X-ProjectPulse-Session'] = token;
    headers['X-Project-Pulse-Session'] = token;
    headers['X-Session-Token'] = token;
  }

  try {
    const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    if (viewAs?.userId) headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
  } catch {
    // A malformed browser preview cache is ignored.
  }

  return headers;
}

async function readJson(path) {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store',
    headers: { Accept: 'application/json', ...requestHeaders() }
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message || `${path} returned HTTP ${response.status}.`);
  }
  return payload;
}

function text(value, fallback = 'Not recorded') {
  if (value === null || value === undefined || value === '') return fallback;
  if (['not_recorded', 'not_available', 'not_resolved'].includes(String(value).toLowerCase())) return fallback;
  return String(value);
}

function label(value) {
  return text(value, 'Unknown')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function number(value, digits = 1) {
  if (value === null || value === undefined || value === '') return 'Not recorded';
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return 'Not recorded';
  return parsed.toLocaleString(undefined, { maximumFractionDigits: digits });
}

function money(value) {
  if (value === null || value === undefined || value === '') return 'Restricted or unavailable';
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return 'Restricted or unavailable';
  return parsed.toLocaleString(undefined, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0
  });
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function date(value) {
  if (!value) return 'Not scheduled';
  const parsed = new Date(`${value}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? 'Not scheduled' : parsed.toLocaleDateString();
}

function bytes(value) {
  const parsed = Number(value || 0);
  if (!Number.isFinite(parsed) || parsed <= 0) return '0 KB';
  if (parsed >= 1024 * 1024) return `${(parsed / 1024 / 1024).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(parsed / 1024))} KB`;
}

function tone(value) {
  const normalized = String(value || '').toLowerCase();
  if (normalized.includes('over_budget') || normalized.includes('unavailable') || normalized.includes('failed')) return 'critical';
  if (normalized.includes('approaching') || normalized.includes('partial') || normalized.includes('missing') || normalized.includes('not_')) return 'warning';
  if (normalized.includes('on_track') || normalized.includes('healthy') || normalized.includes('ready') || normalized.includes('active') || normalized.includes('queued')) return 'healthy';
  return 'neutral';
}

function Status({ value }) {
  return <span className={`group3-status ${tone(value)}`}>{label(value)}</span>;
}

function Metric({ label: metricLabel, value, detail, status }) {
  return (
    <article className="group3-metric">
      <div className="group3-metric-heading">
        <span>{metricLabel}</span>
        {status ? <Status value={status} /> : null}
      </div>
      <strong>{value}</strong>
      {detail ? <p>{detail}</p> : null}
    </article>
  );
}

function Person({ label: personLabel, person, fallback = 'Not assigned' }) {
  return (
    <div className="group3-person">
      <span>{personLabel}</span>
      <strong>{person?.displayName || fallback}</strong>
      <small>{person?.email || person?.jobTitle || ''}</small>
    </div>
  );
}

function sumProjects(projects, selector) {
  return (projects || []).reduce((total, project) => {
    const value = Number(selector(project));
    return Number.isFinite(value) ? total + value : total;
  }, 0);
}

function projectDocumentCount(project) {
  return (project.documentGroups || []).reduce((total, group) => total + Number(group.count || 0), 0);
}

function summaryMetrics(workspace, summary, projects, loading) {
  if (loading && !summary) {
    return Array.from({ length: 6 }, (_, index) => ({
      label: `Loading ${index + 1}`,
      value: '…',
      detail: 'Loading authoritative data'
    }));
  }

  const projectList = projects || [];
  const linkedSellCount = projectList.filter((project) => project.sell?.sellQuoteNumber).length;
  const riskCount = projectList.filter((project) => ['approaching_budget', 'over_budget', 'missing_financial_information'].includes(project.budgetStatus)).length;
  const uniqueEngineers = new Set(projectList.flatMap((project) => (project.engineers || []).map((engineer) => engineer.userId))).size;
  const documentCount = sumProjects(projectList, projectDocumentCount);
  const governedRateCards = projectList.filter((project) => project.sell?.rateCard?.rateCardId).length;
  const customerSpecificRateCards = projectList.filter((project) => project.sell?.rateCard?.isCustomerSpecific).length;
  const missingRateCoverage = projectList.filter((project) => (project.missing || []).includes('governed_labor_rate_basis')).length;

  switch (workspace) {
    case 'engineering':
      return [
        { label: 'Assigned projects', value: number(summary?.projectCount, 0), detail: `${number(summary?.customerCount, 0)} customer(s)` },
        { label: 'Assigned engineers', value: number(uniqueEngineers, 0), detail: 'Unique role-visible engineers' },
        { label: 'Used / planned hours', value: `${number(summary?.usedHours)} / ${number(summary?.plannedHours)}`, detail: `${number(summary?.remainingHours)} remaining` },
        { label: 'Project documents', value: number(documentCount, 0), detail: 'IQS, service request, project, and customer files' },
        { label: 'Projects needing attention', value: number(riskCount, 0), detail: `${number(summary?.missingFinancialInformationCount, 0)} with incomplete financial context`, status: riskCount > 0 ? 'approaching_budget' : 'healthy' },
        { label: 'Open delivery alerts', value: number(summary?.openAlertCount, 0), detail: `${number(summary?.notificationQueuedCount, 0)} notification(s) queued`, status: Number(summary?.openAlertCount || 0) > 0 ? 'approaching_budget' : 'healthy' }
      ];
    case 'sales':
      return [
        { label: 'Sales-visible projects', value: number(summary?.projectCount, 0), detail: `${number(summary?.customerCount, 0)} customer(s)` },
        { label: 'SELL-linked projects', value: number(linkedSellCount, 0), detail: `${Math.max(projectList.length - linkedSellCount, 0)} awaiting quote association`, status: linkedSellCount === projectList.length ? 'healthy' : 'approaching_budget' },
        { label: 'Contracted value', value: money(sumProjects(projectList, (project) => project.contractedValue)), detail: 'Only role-visible commercial values' },
        { label: 'Forecast final cost', value: money(summary?.forecastedFinalCost), detail: `Portfolio variance ${money(summary?.currentVariance)}` },
        { label: 'Delivery risks', value: number(riskCount, 0), detail: `${number(summary?.approachingBudgetCount, 0)} approaching · ${number(summary?.overBudgetCount, 0)} over`, status: riskCount > 0 ? 'over_budget' : 'healthy' },
        { label: 'Open notifications', value: number(summary?.notificationQueuedCount, 0), detail: `${number(summary?.openAlertCount, 0)} open alert(s)`, status: Number(summary?.openAlertCount || 0) > 0 ? 'approaching_budget' : 'healthy' }
      ];
    case 'rate-card':
      return [
        { label: 'Projects in context', value: number(summary?.projectCount, 0), detail: `${number(summary?.customerCount, 0)} customer(s)` },
        { label: 'Governed rate cards', value: number(governedRateCards, 0), detail: `${customerSpecificRateCards} customer-specific` },
        { label: 'Missing rate coverage', value: number(missingRateCoverage, 0), detail: 'Projects without a governed labor-rate basis', status: missingRateCoverage > 0 ? 'approaching_budget' : 'healthy' },
        { label: 'SELL-linked projects', value: number(linkedSellCount, 0), detail: 'Quote association owned by Module 026' },
        { label: 'Forecast using rate basis', value: money(summary?.forecastedFinalCost), detail: `Variance ${money(summary?.currentVariance)}` },
        { label: 'Rate-impact warnings', value: number(riskCount, 0), detail: `${number(summary?.missingFinancialInformationCount, 0)} incomplete financial record(s)`, status: riskCount > 0 ? 'approaching_budget' : 'healthy' }
      ];
    default:
      return [
        { label: 'Managed projects', value: number(summary?.projectCount, 0), detail: `${number(summary?.customerCount, 0)} customer(s)` },
        { label: 'Used / planned hours', value: `${number(summary?.usedHours)} / ${number(summary?.plannedHours)}`, detail: `${number(summary?.remainingHours)} remaining` },
        { label: 'Current expenses', value: money(summary?.uploadedExpenses), detail: 'Current non-deleted Module 005 uploads' },
        { label: 'Forecast final cost', value: money(summary?.forecastedFinalCost), detail: `Variance ${money(summary?.currentVariance)}` },
        { label: 'Budget attention', value: `${number(summary?.approachingBudgetCount, 0)} approaching · ${number(summary?.overBudgetCount, 0)} over`, detail: `${number(summary?.missingFinancialInformationCount, 0)} incomplete`, status: Number(summary?.overBudgetCount || 0) > 0 ? 'over_budget' : Number(summary?.approachingBudgetCount || 0) > 0 ? 'approaching_budget' : 'on_track' },
        { label: 'Notifications', value: number(summary?.notificationQueuedCount, 0), detail: `${number(summary?.openAlertCount, 0)} open alert(s)`, status: Number(summary?.openAlertCount || 0) > 0 ? 'approaching_budget' : 'healthy' }
      ];
  }
}

function SummaryStrip({ workspace, summary, projects, loading }) {
  return (
    <div className="group3-summary-grid" data-workspace-summary={workspace}>
      {summaryMetrics(workspace, summary, projects, loading).map((metric) => (
        <Metric key={metric.label} {...metric} />
      ))}
    </div>
  );
}

function workspaceColumns(workspace) {
  switch (workspace) {
    case 'engineering':
      return ['Customer / project', 'Project Manager', 'Engineers', 'Hours remaining', 'Documents', 'Delivery status'];
    case 'sales':
      return ['Customer / project', 'Account Executive', 'SELL quote', 'Contracted value', 'Forecast', 'Delivery risk'];
    case 'rate-card':
      return ['Customer / project', 'Contract type', 'Governed rate card', 'Rate lines', 'SELL quote', 'Coverage'];
    default:
      return ['Customer / project', 'Project Manager', 'Hours', 'Forecast', 'Variance', 'Status'];
  }
}

function workspaceCells(workspace, project) {
  switch (workspace) {
    case 'engineering':
      return [
        text(project.projectManagerName, 'Not assigned'),
        `${number(project.engineers?.length || 0, 0)} assigned`,
        number(project.remainingHours),
        number(projectDocumentCount(project), 0),
        <Status key="status" value={project.projectStatus} />
      ];
    case 'sales':
      return [
        text(project.accountExecutive?.displayName, 'Not assigned'),
        text(project.sell?.sellQuoteNumber, label(project.sell?.readinessStatus)),
        money(project.contractedValue),
        money(project.forecastedFinalCost),
        <Status key="status" value={project.budgetStatus} />
      ];
    case 'rate-card':
      return [
        label(project.contractType),
        text(project.sell?.rateCard?.rateCardName, 'No governed rate card'),
        number(project.sell?.rateCard?.rateLineCount || 0, 0),
        text(project.sell?.sellQuoteNumber, 'No SELL quote'),
        <Status key="status" value={project.sell?.rateCard?.rateCardId ? project.sell?.rateCard?.status || 'ready' : 'missing_rate_context'} />
      ];
    default:
      return [
        text(project.projectManagerName, 'Not assigned'),
        `${number(project.usedHours)} / ${number(project.plannedHours)}`,
        money(project.forecastedFinalCost),
        money(project.currentVariance),
        <Status key="status" value={project.budgetStatus} />
      ];
  }
}

function ProjectTable({ workspace, projects, selectedProjectId, onSelect }) {
  const columns = workspaceColumns(workspace);
  return (
    <div className="group3-table-wrap" data-workspace-table={workspace}>
      <table className={`group3-project-table workspace-${workspace}`}>
        <thead>
          <tr>{columns.map((column) => <th key={column}>{column}</th>)}</tr>
        </thead>
        <tbody>
          {projects.map((project) => (
            <tr
              key={project.projectId}
              className={selectedProjectId === project.projectId ? 'selected' : ''}
              onClick={() => onSelect(project.projectId)}
            >
              <td>
                <button type="button" className="group3-project-select" onClick={() => onSelect(project.projectId)}>
                  <strong>{project.projectCode} · {project.projectName}</strong>
                  <small>{project.customerName}</small>
                </button>
              </td>
              {workspaceCells(workspace, project).map((cell, index) => <td key={`${project.projectId}-${columns[index + 1]}`}>{cell}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function OverviewTab({ project }) {
  return (
    <div className="group3-detail-grid">
      <section className="group3-card">
        <p className="group3-eyebrow">Project identity</p>
        <h4>{project.projectCode} · {project.projectName}</h4>
        <dl className="group3-facts">
          <div><dt>Customer</dt><dd>{text(project.customerName)}</dd></div>
          <div><dt>Status</dt><dd>{label(project.projectStatus)}</dd></div>
          <div><dt>Contract type</dt><dd>{label(project.contractType)}</dd></div>
          <div><dt>Schedule</dt><dd>{date(project.startDate)} → {date(project.endDate)}</dd></div>
          <div><dt>Completion</dt><dd>{project.completionPercentage === null ? 'Not calculable' : `${number(project.completionPercentage)}%`}</dd></div>
          <div><dt>Notification</dt><dd>{label(project.notificationStatus)}</dd></div>
        </dl>
      </section>

      <section className="group3-card">
        <p className="group3-eyebrow">Role-aware visibility</p>
        <h4>{label(project.visibility?.level)}</h4>
        <p>{project.visibility?.explanation}</p>
        <div className="group3-status-row">
          <Status value={project.budgetStatus} />
          <span>{project.openAlertCount} open alert(s)</span>
          <span>{project.highAlertCount} high severity</span>
        </div>
      </section>

      <section className="group3-card group3-span-two">
        <p className="group3-eyebrow">Delivery team</p>
        <div className="group3-people-grid">
          <Person label="Project Manager" person={{ displayName: project.projectManagerName, email: project.projectManagerEmail }} />
          <Person label="Project Team Coordinator" person={project.projectTeamCoordinator} />
          <Person label="Solution Architect" person={project.solutionArchitect} />
          <Person label="Account Executive" person={project.accountExecutive} />
        </div>
      </section>
    </div>
  );
}

function FinancialTab({ project }) {
  return (
    <>
      <div className="group3-financial-grid">
        <Metric label="Contracted value" value={money(project.contractedValue)} detail="Customer commercial value when recorded" />
        <Metric label="Labor budget" value={money(project.laborBudget)} detail="Project labor plan" />
        <Metric label="Expense budget" value={money(project.expenseBudget)} detail="Separate expense allowance" />
        <Metric label="Calculated labor cost" value={money(project.laborCost)} detail="Role-restricted estimate, not payroll cost" />
        <Metric label="Uploaded expenses" value={money(project.uploadedExpenses)} detail="Module 005 current upload set" />
        <Metric label="Committed cost" value={money(project.committedCost)} detail="Calculated labor plus current expenses" />
        <Metric label="Forecasted final cost" value={money(project.forecastedFinalCost)} detail="Remaining work at governed rate basis" status={project.budgetStatus} />
        <Metric label="Current variance" value={money(project.currentVariance)} detail={label(project.varianceCompleteness)} status={project.currentVariance !== null && Number(project.currentVariance) < 0 ? 'over_budget' : project.budgetStatus} />
      </div>

      <section className="group3-card">
        <p className="group3-eyebrow">How values were calculated</p>
        <div className="group3-calculation-list">
          {(project.calculations || []).map((calculation) => (
            <article key={calculation.key}>
              <strong>{calculation.label}</strong>
              <code>{calculation.formula}</code>
              <p>{calculation.explanation}</p>
            </article>
          ))}
        </div>
      </section>

      {project.missing?.length ? (
        <section className="group3-warning">
          <strong>Missing financial information</strong>
          <p>{project.missing.map(label).join(' · ')}</p>
        </section>
      ) : null}
    </>
  );
}

function TeamTab({ project }) {
  return (
    <div className="group3-detail-grid">
      <section className="group3-card group3-span-two">
        <div className="group3-section-heading">
          <div>
            <p className="group3-eyebrow">Engineers</p>
            <h4>Allocated, used, and remaining hours</h4>
          </div>
          <span>{project.engineers?.length || 0} engineer(s)</span>
        </div>
        <div className="group3-engineer-list">
          {(project.engineers || []).map((engineer) => {
            const remaining = Math.max(Number(engineer.assignedHours || 0) - Number(engineer.usedHours || 0), 0);
            return (
              <article key={engineer.userId}>
                <div>
                  <strong>{engineer.displayName}</strong>
                  <small>{engineer.email}</small>
                </div>
                <div>
                  <span>{number(engineer.assignedHours)} allocated</span>
                  <span>{number(engineer.usedHours)} used</span>
                  <span>{number(remaining)} remaining</span>
                </div>
                <small>{engineer.tasks?.join(' · ') || 'No task names recorded'}</small>
              </article>
            );
          })}
          {!project.engineers?.length ? <div className="group3-empty">No current engineering assignments were returned.</div> : null}
        </div>
      </section>

      <section className="group3-card group3-span-two">
        <p className="group3-eyebrow">SELL relationship</p>
        <div className="group3-sell-grid">
          <div><span>Connection owner</span><strong>{project.sell?.connectionOwner}</strong></div>
          <div><span>Quote</span><strong>{text(project.sell?.sellQuoteNumber)}</strong></div>
          <div><span>Billing method</span><strong>{label(project.sell?.billingMethod)}</strong></div>
          <div><span>Readiness</span><Status value={project.sell?.readinessStatus} /></div>
          <div><span>Rate card</span><strong>{text(project.sell?.rateCard?.rateCardName)}</strong></div>
          <div><span>Last successful sync</span><strong>{dateTime(project.sell?.lastSuccessfulSyncAt)}</strong></div>
        </div>
        <p className="group3-contract-note">{project.sell?.governanceNote}</p>
      </section>
    </div>
  );
}

function ExpenseTab({ project }) {
  return (
    <section className="group3-card">
      <div className="group3-section-heading">
        <div>
          <p className="group3-eyebrow">Module 005 expenses</p>
          <h4>Current authoritative uploads</h4>
          <p>Deleted and superseded uploads are excluded from current totals.</p>
        </div>
        <strong>{money(project.uploadedExpenses)}</strong>
      </div>
      <div className="group3-expense-list">
        {(project.expenses || []).map((expense) => (
          <article key={expense.uploadId}>
            <div>
              <strong>{expense.originalFileName || `${label(expense.sourceMode)} upload`}</strong>
              <small>{expense.ownerName} · {expense.lineCount} line(s) · {dateTime(expense.uploadedAt)}</small>
            </div>
            <div>
              <strong>{money(expense.totalAmount)}</strong>
              <small>{label(expense.billingTreatment)}</small>
            </div>
          </article>
        ))}
        {!project.expenses?.length ? <div className="group3-empty">No current Module 005 expense uploads were returned for this project.</div> : null}
      </div>
    </section>
  );
}

function DocumentsTab({ project, onDownload, downloadState }) {
  return (
    <section className="group3-card">
      <div className="group3-section-heading">
        <div>
          <p className="group3-eyebrow">Module 019 documents</p>
          <h4>IQS, service request, project, and customer files</h4>
          <p>Downloads use the existing role-scoped Module 019 endpoint. No Module 011 dependency is introduced.</p>
        </div>
      </div>
      <div className="group3-document-groups">
        {(project.documentGroups || []).map((group) => (
          <article key={group.group}>
            <header>
              <strong>{group.group}</strong>
              <span>{group.count}</span>
            </header>
            <div>
              {(group.documents || []).map((document) => (
                <button
                  type="button"
                  key={document.documentId}
                  onClick={() => onDownload(document)}
                  disabled={downloadState.documentId === document.documentId && downloadState.loading}
                >
                  <span>{document.originalFileName}</span>
                  <small>{label(document.documentCategory)} · {bytes(document.sizeBytes)}</small>
                </button>
              ))}
            </div>
          </article>
        ))}
        {!project.documentGroups?.length ? <div className="group3-empty">No role-visible project documents were returned.</div> : null}
      </div>
      {downloadState.message ? (
        <div className={`group3-download-status ${downloadState.error ? 'error' : ''}`}>{downloadState.message}</div>
      ) : null}
    </section>
  );
}

function SourceHealth({ sources, onRetry }) {
  const unavailable = (sources || []).filter((source) => source.status !== 'healthy');
  return (
    <section className="group3-card group3-source-card">
      <div className="group3-section-heading">
        <div>
          <p className="group3-eyebrow">Source health</p>
          <h4>Independent data-source results</h4>
          <p>One unavailable optional source does not blank the complete workspace.</p>
        </div>
        <button type="button" className="group3-secondary" onClick={onRetry}>Retry sources</button>
      </div>
      <div className="group3-source-grid">
        {(sources || []).map((source) => (
          <div key={source.key}>
            <div>
              <strong>{source.name}</strong>
              <small>{source.recordCount} record(s) · {source.diagnosticCode || 'No diagnostic code'}</small>
            </div>
            <Status value={source.status} />
          </div>
        ))}
      </div>
      {unavailable.length ? (
        <p className="group3-source-warning">{unavailable.map((source) => `${source.name}: ${source.message}`).join(' · ')}</p>
      ) : null}
    </section>
  );
}

export default function UnifiedProjectFinancialWorkspace({
  workspace = 'engineering',
  projectManagerUserId = ''
}) {
  const config = workspaceConfig[workspace] || workspaceConfig.engineering;
  const [state, setState] = useState({ loading: true, data: null, error: '' });
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [selectedProjectId, setSelectedProjectId] = useState('');
  const [activeTab, setActiveTab] = useState(config.defaultTab);
  const [downloadState, setDownloadState] = useState({
    documentId: '',
    loading: false,
    error: false,
    message: ''
  });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const parameters = new URLSearchParams({ workspace, limit: '250' });
      if (projectManagerUserId) parameters.set('projectManagerUserId', projectManagerUserId);
      const data = await readJson(`/api/project-financials/portfolio?${parameters}`);
      setState({ loading: false, data, error: '' });
      setSelectedProjectId((current) => {
        if (current && data.projects?.some((project) => project.projectId === current)) return current;
        return data.projects?.[0]?.projectId || '';
      });
    } catch (error) {
      setState({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load project financial truth.'
      });
    }
  }, [workspace, projectManagerUserId]);

  useEffect(() => {
    setActiveTab(config.defaultTab);
    load();
  }, [load, config.defaultTab]);

  const projects = state.data?.projects || [];
  const filteredProjects = useMemo(() => {
    const normalized = search.trim().toLowerCase();
    return projects.filter((project) => {
      const statusMatches = statusFilter === 'all'
        || project.budgetStatus === statusFilter
        || project.projectStatus === statusFilter;
      const engineerNames = (project.engineers || []).map((engineer) => engineer.displayName).join(' ');
      const searchMatches = !normalized
        || `${project.customerName} ${project.projectCode} ${project.projectName} ${project.projectManagerName} ${project.accountExecutive?.displayName || ''} ${engineerNames} ${project.contractType || ''} ${project.sell?.sellQuoteNumber || ''} ${project.sell?.rateCard?.rateCardName || ''}`
          .toLowerCase()
          .includes(normalized);
      return statusMatches && searchMatches;
    });
  }, [projects, search, statusFilter]);

  const selectedProject = projects.find((project) => project.projectId === selectedProjectId)
    || filteredProjects[0]
    || null;

  async function downloadDocument(document) {
    setDownloadState({
      documentId: document.documentId,
      loading: true,
      error: false,
      message: `Downloading ${document.originalFileName}…`
    });

    try {
      const response = await fetch(document.downloadUrl, {
        method: 'GET',
        credentials: 'include',
        headers: requestHeaders()
      });
      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        throw new Error(payload?.message || `Document download returned HTTP ${response.status}.`);
      }

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const anchor = window.document.createElement('a');
      anchor.href = url;
      anchor.download = document.originalFileName || 'project-document';
      window.document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 30000);
      setDownloadState({
        documentId: document.documentId,
        loading: false,
        error: false,
        message: `${document.originalFileName} downloaded.`
      });
    } catch (error) {
      setDownloadState({
        documentId: document.documentId,
        loading: false,
        error: true,
        message: error instanceof Error ? error.message : 'Unable to download this document.'
      });
    }
  }

  return (
    <section
      className="group3-financial-workspace projectpulse-module-standard"
      data-projectpulse-group3="authoritative-financial-truth"
      data-workspace={workspace}
      data-module={config.module}
    >
      <header className="group3-hero">
        <div className="group3-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="group3-eyebrow">Module {config.module} · {config.eyebrow}</p>
            <h2>{config.title}</h2>
            <p>{config.description}</p>
          </div>
        </div>
        <div className="group3-hero-actions">
          <Status value={state.data?.status || (state.loading ? 'loading' : 'unavailable')} />
          <button type="button" className="group3-primary" onClick={load} disabled={state.loading}>
            {state.loading ? 'Refreshing…' : config.refreshLabel}
          </button>
        </div>
      </header>

      <div className="group3-authority-banner" data-workspace-authority={workspace}>
        <strong>{config.authorityTitle}</strong>
        <span>{config.authorityDescription}</span>
      </div>

      {state.error ? (
        <div className="group3-error">
          <strong>Project financial truth is unavailable.</strong>
          <p>{state.error}</p>
          <button type="button" onClick={load}>Retry</button>
        </div>
      ) : null}

      <SummaryStrip workspace={workspace} summary={state.data?.summary} projects={projects} loading={state.loading} />

      <section className="group3-card">
        <div className="group3-filterbar">
          <label>
            Search projects
            <input
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder={config.searchPlaceholder}
            />
          </label>
          <label>
            Status
            <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option value="all">All statuses</option>
              <option value="on_track">On track</option>
              <option value="on_track_partial_expense_budget">On track, incomplete budget</option>
              <option value="approaching_budget">Approaching budget</option>
              <option value="over_budget">Over budget</option>
              <option value="missing_financial_information">Missing financial information</option>
            </select>
          </label>
          <span>{filteredProjects.length} of {projects.length} project(s)</span>
        </div>

        {state.loading && !state.data ? (
          <div className="group3-empty">Loading authoritative project financial data…</div>
        ) : filteredProjects.length ? (
          <ProjectTable
            workspace={workspace}
            projects={filteredProjects}
            selectedProjectId={selectedProject?.projectId}
            onSelect={(projectId) => {
              setSelectedProjectId(projectId);
              setActiveTab(config.defaultTab);
            }}
          />
        ) : (
          <div className="group3-empty">No projects match this role scope and filter.</div>
        )}
      </section>

      {selectedProject ? (
        <section className="group3-detail">
          <header className="group3-detail-header">
            <div>
              <p className="group3-eyebrow">Module {config.module} · {config.eyebrow}</p>
              <h3>{selectedProject.projectCode} · {selectedProject.projectName}</h3>
              <p>{selectedProject.customerName} · PM {text(selectedProject.projectManagerName, 'not assigned')}</p>
            </div>
            <Status value={selectedProject.budgetStatus} />
          </header>

          <div className="group3-tabs" role="tablist" aria-label="Project financial detail sections">
            {config.tabs.map(([key, tabLabel]) => (
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === key}
                className={activeTab === key ? 'active' : ''}
                key={key}
                onClick={() => setActiveTab(key)}
              >
                {tabLabel}
              </button>
            ))}
          </div>

          {activeTab === 'overview' ? <OverviewTab project={selectedProject} /> : null}
          {activeTab === 'financials' ? <FinancialTab project={selectedProject} /> : null}
          {activeTab === 'team' ? <TeamTab project={selectedProject} /> : null}
          {activeTab === 'expenses' ? <ExpenseTab project={selectedProject} /> : null}
          {activeTab === 'documents' ? (
            <DocumentsTab
              project={selectedProject}
              onDownload={downloadDocument}
              downloadState={downloadState}
            />
          ) : null}
        </section>
      ) : null}

      <SourceHealth sources={state.data?.sources || []} onRetry={load} />

      <footer className="group3-footer">
        <span>Contract {state.data?.contractVersion || 'pending'}</span>
        <span>Generated {dateTime(state.data?.generatedAt)}</span>
        <span>{config.authorityTitle} · server-enforced scope</span>
        <span>SELL connection owner: Module 026</span>
      </footer>
    </section>
  );
}

import { useEffect, useMemo, useState } from 'react';
import './sales-insights-dashboard.css';

function getStoredProjectPulseAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;

    const parsed = JSON.parse(rawSession);
    if (!parsed?.sessionToken) return null;

    if (parsed?.expiresAt && Date.now() >= Date.parse(parsed.expiresAt)) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders() {
  const session = getStoredProjectPulseAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });

  if (!response.ok) {
    let details = '';
    try {
      const body = await response.json();
      details = body.message || body.detail || body.status || JSON.stringify(body);
    } catch {
      details = await response.text();
    }

    throw new Error(`${path} returned HTTP ${response.status}${details ? `: ${details}` : ''}`);
  }

  return response.json();
}

function normalizeStatus(value) {
  return String(value ?? '').trim().toLowerCase();
}

function fmt(value) {
  return value === null || value === undefined || value === '' ? 'Not set' : value;
}

function fmtNumber(value) {
  return Number(value ?? 0).toLocaleString();
}

function fmtMoney(value) {
  const amount = Number(value ?? 0);
  return amount.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function getTone(status) {
  const normalized = normalizeStatus(status);

  if (['assigned', 'approved', 'ready', 'converted', 'posted', 'complete', 'completed', 'active'].includes(normalized)) return 'safe';
  if (['blocked', 'declined', 'cancelled', 'rejected', 'at_risk'].includes(normalized)) return 'danger';
  if (['pending', 'triage', 'review', 'new', 'draft', 'open', 'partially_assigned'].includes(normalized)) return 'attention';

  return 'neutral';
}

function StatusBadge({ children, tone = 'neutral' }) {
  return <span className={`sales-insights-badge ${tone}`}>{children}</span>;
}

function itemHasSourceDocument(item) {
  const documentCount = Number(
    item?.documentCount ??
    item?.documentsCount ??
    item?.sourceDocumentCount ??
    item?.attachedDocumentCount ??
    0
  );

  return Boolean(
    documentCount > 0 ||
    item?.externalRecordUrl ||
    item?.sourceDocumentUploaded ||
    item?.sourceDocumentReceived ||
    item?.hasSourceDocument ||
    item?.sourceDocumentRequired === false
  );
}

function getProjectLabel(item) {
  return item?.requestNumber || item?.projectCode || item?.opportunityReference || item?.externalReferenceId || 'Unnumbered';
}

export default function SalesInsightsDashboard() {
  const [intakeOverview, setIntakeOverview] = useState({ loading: true, data: null, error: null });
  const [customerOverview, setCustomerOverview] = useState({ loading: true, data: null, error: null });
  const [workspaceOverview, setWorkspaceOverview] = useState({ loading: true, data: null, error: null });
  const [searchTerm, setSearchTerm] = useState('');
  const [riskFilter, setRiskFilter] = useState('all');

  async function loadDashboard() {
    setIntakeOverview((current) => ({ ...current, loading: true, error: null }));
    setCustomerOverview((current) => ({ ...current, loading: true, error: null }));
    setWorkspaceOverview((current) => ({ ...current, loading: true, error: null }));

    const [intakeResult, customerResult, workspaceResult] = await Promise.allSettled([
      fetchJson('/api/project-intake/overview'),
      fetchJson('/api/customers/overview'),
      fetchJson('/api/project-workspace/overview')
    ]);

    if (intakeResult.status === 'fulfilled') {
      setIntakeOverview({ loading: false, data: intakeResult.value, error: null });
    } else {
      setIntakeOverview({ loading: false, data: null, error: intakeResult.reason instanceof Error ? intakeResult.reason.message : 'Unable to load intake overview.' });
    }

    if (customerResult.status === 'fulfilled') {
      setCustomerOverview({ loading: false, data: customerResult.value, error: null });
    } else {
      setCustomerOverview({ loading: false, data: null, error: customerResult.reason instanceof Error ? customerResult.reason.message : 'Unable to load customer overview.' });
    }

    if (workspaceResult.status === 'fulfilled') {
      setWorkspaceOverview({ loading: false, data: workspaceResult.value, error: null });
    } else {
      setWorkspaceOverview({ loading: false, data: null, error: workspaceResult.reason instanceof Error ? workspaceResult.reason.message : 'Unable to load project workspace overview.' });
    }
  }

  useEffect(() => {
    loadDashboard();
  }, []);

  const intakes = intakeOverview.data?.intakes ?? [];
  const resourceRequests = intakeOverview.data?.resourceRequests ?? workspaceOverview.data?.resourceRequests ?? [];
  const projects = workspaceOverview.data?.projects ?? intakeOverview.data?.projects ?? [];
  const documents = workspaceOverview.data?.documents ?? [];
  const assignments = workspaceOverview.data?.assignments ?? [];
  const customers = customerOverview.data?.customers ?? [];

  const linkedResourceRequestIds = useMemo(() => {
    const map = new Map();

    resourceRequests.forEach((request) => {
      const key = request.projectIntakeRequestId || request.intakeId || request.sourceIntakeId;
      if (!key) return;
      const existing = map.get(key) ?? [];
      existing.push(request);
      map.set(key, existing);
    });

    return map;
  }, [resourceRequests]);

  const salesQueue = useMemo(() => {
    return intakes.map((intake) => {
      const linkedRequests = linkedResourceRequestIds.get(intake.id) ?? [];
      const sourceReady = itemHasSourceDocument(intake);
      const pmReady = Boolean(intake.assignedPmUserId || intake.assignedPmName);
      const customerReady = Boolean(intake.clientId || intake.clientName);
      const engineeringReady = linkedRequests.some((request) => ['assigned', 'partially_assigned', 'fulfilled', 'ready', 'complete', 'completed'].includes(normalizeStatus(request.status)));
      const engineeringStarted = linkedRequests.length > 0;
      const blockers = [];

      if (!customerReady) blockers.push('Customer missing');
      if (!sourceReady) blockers.push('Source document/reference missing');
      if (!pmReady) blockers.push('PM unassigned');
      if (!engineeringStarted) blockers.push('Engineering request not staged');
      if (engineeringStarted && !engineeringReady) blockers.push('Engineering assignment pending');

      const riskScore = blockers.length;
      const riskLevel = riskScore >= 3 ? 'High' : riskScore >= 1 ? 'Medium' : 'Ready';

      return {
        ...intake,
        linkedRequests,
        sourceReady,
        pmReady,
        customerReady,
        engineeringReady,
        engineeringStarted,
        blockers,
        riskScore,
        riskLevel
      };
    });
  }, [intakes, linkedResourceRequestIds]);

  const filteredSalesQueue = useMemo(() => {
    const search = searchTerm.trim().toLowerCase();

    return salesQueue
      .filter((item) => {
        const riskMatches = riskFilter === 'all' || item.riskLevel.toLowerCase() === riskFilter;
        const haystack = `${item.requestNumber ?? ''} ${item.clientName ?? ''} ${item.requestTitle ?? ''} ${item.opportunityReference ?? ''} ${item.assignedPmName ?? ''} ${item.externalReferenceId ?? ''}`.toLowerCase();
        const searchMatches = !search || haystack.includes(search);
        return riskMatches && searchMatches;
      })
      .sort((a, b) => b.riskScore - a.riskScore || String(a.clientName ?? '').localeCompare(String(b.clientName ?? '')));
  }, [salesQueue, searchTerm, riskFilter]);

  const unassignedPm = salesQueue.filter((item) => !item.pmReady);
  const missingDocuments = salesQueue.filter((item) => !item.sourceReady);
  const engineeringPending = resourceRequests.filter((request) => !['assigned', 'partially_assigned', 'fulfilled', 'ready', 'complete', 'completed'].includes(normalizeStatus(request.status)));
  const highRisk = salesQueue.filter((item) => item.riskLevel === 'High');
  const readyForLaunch = salesQueue.filter((item) => item.riskLevel === 'Ready');
  const activeProjectCount = projects.length;
  const activeCustomerCount = customers.length;

  const insightCards = [
    {
      label: 'Sold / intake queue',
      value: intakes.length,
      detail: `${salesQueue.filter((item) => ['new', 'draft', 'pending', 'triage', 'review', 'open'].includes(normalizeStatus(item.status))).length} open handoff item(s)`,
      tone: 'neutral'
    },
    {
      label: 'Needs PM assignment',
      value: unassignedPm.length,
      detail: 'Sales handoff cannot move cleanly without PM ownership.',
      tone: unassignedPm.length > 0 ? 'attention' : 'safe'
    },
    {
      label: 'Missing source document',
      value: missingDocuments.length,
      detail: 'SOW, GSD, quote, order form, or source URL is needed.',
      tone: missingDocuments.length > 0 ? 'attention' : 'safe'
    },
    {
      label: 'Engineering pending',
      value: engineeringPending.length,
      detail: 'Resource requests not fully assigned or ready.',
      tone: engineeringPending.length > 0 ? 'attention' : 'safe'
    },
    {
      label: 'High-risk handoffs',
      value: highRisk.length,
      detail: 'Multiple blockers before project delivery can start.',
      tone: highRisk.length > 0 ? 'danger' : 'safe'
    },
    {
      label: 'Ready for launch',
      value: readyForLaunch.length,
      detail: 'Customer, document, PM, and engineering path look ready.',
      tone: 'safe'
    }
  ];

  const customerSignals = useMemo(() => {
    const customerMap = new Map();

    salesQueue.forEach((item) => {
      const key = item.clientName || item.clientId || 'Unknown customer';
      const existing = customerMap.get(key) ?? {
        customerName: key,
        intakeCount: 0,
        highRiskCount: 0,
        readyCount: 0,
        totalEstimatedHours: 0,
        totalPlannedCost: 0
      };

      existing.intakeCount += 1;
      existing.highRiskCount += item.riskLevel === 'High' ? 1 : 0;
      existing.readyCount += item.riskLevel === 'Ready' ? 1 : 0;
      existing.totalEstimatedHours += Number(item.estimatedHours ?? 0);
      existing.totalPlannedCost += Number(item.plannedTotalProjectCost ?? item.plannedEngineeringCost ?? 0);

      customerMap.set(key, existing);
    });

    return [...customerMap.values()]
      .sort((a, b) => b.highRiskCount - a.highRiskCount || b.intakeCount - a.intakeCount)
      .slice(0, 8);
  }, [salesQueue]);

  const loading = intakeOverview.loading || customerOverview.loading || workspaceOverview.loading;
  const errors = [intakeOverview.error, customerOverview.error, workspaceOverview.error].filter(Boolean);

  return (
    <section className="sales-insights-dashboard">
      <div className="sales-insights-header">
        <div>
          <p className="eyebrow">Module 036</p>
          <h2>Sales Insights Dashboard</h2>
          <p className="muted">
            Sales-facing visibility into project handoff health, PM assignment, missing documents, engineering readiness, and launch blockers after a project is sold.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadDashboard}>
          Refresh insights
        </button>
      </div>

      {errors.length > 0 ? (
        <div className="sales-insights-error">
          {errors.map((error) => <p key={error}>{error}</p>)}
        </div>
      ) : null}

      <div className="sales-insights-summary-grid">
        {insightCards.map((card) => (
          <article className={`sales-insights-summary-card ${card.tone}`} key={card.label}>
            <span>{card.label}</span>
            <strong>{loading ? '...' : fmtNumber(card.value)}</strong>
            <small>{card.detail}</small>
          </article>
        ))}
      </div>

      <div className="sales-insights-two-column">
        <article className="sales-insights-panel sales-handoff-panel">
          <div className="sales-insights-panel-header">
            <div>
              <h3>Sales handoff queue</h3>
              <p className="muted">Focus on sold or incoming project records that need PM ownership, documents, or engineering assignment.</p>
            </div>
            <StatusBadge tone={highRisk.length > 0 ? 'danger' : 'safe'}>{highRisk.length} high risk</StatusBadge>
          </div>

          <div className="sales-insights-filters">
            <label>
              Search
              <input
                value={searchTerm}
                placeholder="Customer, intake, opportunity, PM..."
                onChange={(event) => setSearchTerm(event.target.value)}
              />
            </label>
            <label>
              Risk
              <select value={riskFilter} onChange={(event) => setRiskFilter(event.target.value)}>
                <option value="all">All risk levels</option>
                <option value="high">High</option>
                <option value="medium">Medium</option>
                <option value="ready">Ready</option>
              </select>
            </label>
          </div>

          <div className="sales-handoff-list">
            {filteredSalesQueue.length === 0 ? (
              <div className="sales-insights-empty">No sales handoff items match the current filters.</div>
            ) : (
              filteredSalesQueue.slice(0, 12).map((item) => (
                <article className="sales-handoff-card" key={item.id ?? `${item.clientName}-${item.requestTitle}`}>
                  <div className="sales-handoff-card-title">
                    <div>
                      <span>{getProjectLabel(item)}</span>
                      <strong>{item.requestTitle || item.projectName || 'Untitled project handoff'}</strong>
                      <small>{fmt(item.clientName)} · {fmt(item.opportunityReference || item.externalReferenceId)}</small>
                    </div>
                    <StatusBadge tone={item.riskLevel === 'High' ? 'danger' : item.riskLevel === 'Medium' ? 'attention' : 'safe'}>{item.riskLevel}</StatusBadge>
                  </div>

                  <div className="sales-handoff-facts">
                    <span><strong>PM</strong>{fmt(item.assignedPmName)}</span>
                    <span><strong>Status</strong>{fmt(item.status)}</span>
                    <span><strong>Hours</strong>{fmtNumber(item.estimatedHours)}</span>
                    <span><strong>Planned</strong>{fmtMoney(item.plannedTotalProjectCost ?? item.plannedEngineeringCost)}</span>
                  </div>

                  <div className="sales-readiness-strip">
                    <span className={item.customerReady ? 'ready' : 'attention'}>Customer</span>
                    <span className={item.sourceReady ? 'ready' : 'attention'}>Document</span>
                    <span className={item.pmReady ? 'ready' : 'attention'}>PM</span>
                    <span className={item.engineeringStarted ? 'ready' : 'attention'}>Engineering request</span>
                    <span className={item.engineeringReady ? 'ready' : 'attention'}>Assignment</span>
                  </div>

                  {item.blockers.length > 0 ? (
                    <p className="sales-blocker-line">Blockers: {item.blockers.join(' · ')}</p>
                  ) : (
                    <p className="sales-blocker-line ready">No visible launch blockers from the current intake data.</p>
                  )}
                </article>
              ))
            )}
          </div>
        </article>

        <article className="sales-insights-panel sales-signal-panel">
          <div className="sales-insights-panel-header">
            <div>
              <h3>Customer and launch signals</h3>
              <p className="muted">Sales can quickly see where follow-up is needed before delivery starts.</p>
            </div>
            <StatusBadge>{activeCustomerCount} customers</StatusBadge>
          </div>

          <div className="sales-signal-grid">
            <div>
              <span>Active projects</span>
              <strong>{loading ? '...' : fmtNumber(activeProjectCount)}</strong>
              <small>Workspace-visible records</small>
            </div>
            <div>
              <span>Documents visible</span>
              <strong>{loading ? '...' : fmtNumber(documents.length)}</strong>
              <small>Workspace/intake documents</small>
            </div>
            <div>
              <span>Assignments</span>
              <strong>{loading ? '...' : fmtNumber(assignments.length)}</strong>
              <small>Engineering handoff rows</small>
            </div>
            <div>
              <span>Resource requests</span>
              <strong>{loading ? '...' : fmtNumber(resourceRequests.length)}</strong>
              <small>Demand records</small>
            </div>
          </div>

          <div className="sales-customer-signal-list">
            <h4>Customer handoff concentration</h4>
            {customerSignals.length === 0 ? (
              <div className="sales-insights-empty">No customer handoff signals are available yet.</div>
            ) : (
              customerSignals.map((customer) => (
                <div className="sales-customer-signal-row" key={customer.customerName}>
                  <div>
                    <strong>{customer.customerName}</strong>
                    <small>{customer.intakeCount} intake(s) · {customer.highRiskCount} high-risk · {customer.readyCount} ready</small>
                  </div>
                  <span>{fmtMoney(customer.totalPlannedCost)} · {fmtNumber(customer.totalEstimatedHours)} hrs</span>
                </div>
              ))
            )}
          </div>

          <div className="sales-next-actions">
            <h4>Recommended sales follow-up</h4>
            <ul>
              <li>Confirm missing SOW, GSD, quote, order form, or source URL before delivery kickoff.</li>
              <li>Follow up on sold projects without PM assignment so ownership is clear.</li>
              <li>Watch engineering requests that are open but not assigned to avoid launch delays.</li>
              <li>Use Module 020 for actual intake creation and assignment actions.</li>
            </ul>
          </div>
        </article>
      </div>
    </section>
  );
}

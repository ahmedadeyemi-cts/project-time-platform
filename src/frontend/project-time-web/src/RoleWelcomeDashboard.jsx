import { useEffect, useMemo, useState } from 'react';
/* ROLE_WORKSPACE_WELCOME_IMPORT */
import {
  getRoleWorkspaceLabel,
  getRoleWorkspaceName,
  roleCodesFrom
} from './role-workspace-governance.js';
import './role-welcome-dashboard.css';

const PROJECT_MANAGEMENT_ROLES = new Set([
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'PM_TEAM_LEAD'
]);

const TIME_ENTRY_ROLES = new Set([
  'ENGINEER',
  'ENGINEERING',
  'SOLUTION_ARCHITECT',
  'ARCHITECT',
  'SA',
  'SAA',
  ...PROJECT_MANAGEMENT_ROLES
]);

const TIME_ENTRY_EXCLUDED_ROLES = new Set([
  'MANAGER',
  'PEOPLE_MANAGER',
  'SALES',
  'INSIDE_SALES',
  'ACCOUNT_EXECUTIVE',
  'SALES_MANAGER',
  'EXECUTIVE',
  'PROJECT_TEAM_COORDINATOR',
  'ACCOUNTING',
  'ACCOUNTING_BILLING',
  'BILLING',
  'FINANCE',
  'RESALE'
]);

const ROLE_ACTIONS = Object.freeze({
  engineering: Object.freeze([
    ['Add Time', 'timesheet'],
    ['My Timesheet', 'timesheet'],
    ['Project Workspace', 'project-workspace'],
    ['My Utilization', 'utilization']
  ]),
  projectManagement: Object.freeze([
    ['Add Time', 'timesheet'],
    ['Approval Center', 'manager-approval'],
    ['Project Expense Upload', 'project-allocation-info'],
    ['Project Workspace', 'project-workspace'],
    ['Qualifications & Certifications', 'qualifications-certifications']
  ]),
  management: Object.freeze([
    ['Approval Center', 'manager-approval'],
    ['Project Health', 'project-workload'],
    ['Team Utilization', 'utilization'],
    ['Work Register', 'work-register']
  ]),
  sales: Object.freeze([
    ['Opportunities', 'opportunities'],
    ['Project Intake', 'project-intake'],
    ['Customers', 'customer-directory'],
    ['CRM / ERP', 'crm-integration']
  ]),
  executive: Object.freeze([
    ['Executive Reporting', 'reporting'],
    ['Portfolio Health', 'project-workload'],
    ['Billing Snapshot', 'billing-readiness'],
    ['Invoice Center', 'invoice-billing-center']
  ]),
  coordinator: Object.freeze([
    ['Create Project', 'create-work-register'],
    ['Work Register', 'work-register'],
    ['Billing Readiness', 'billing-readiness'],
    ['Project Closeout', 'project-closeout']
  ]),
  accounting: Object.freeze([
    ['Reconciliation', 'workflow'],
    ['Audit History', 'audit-history'],
    ['Billing Readiness', 'billing-readiness'],
    ['Analytics Center', 'reporting']
  ]),
  billing: Object.freeze([
    ['Financial Operations', 'financial-operations-workbench'],
    ['Billing Readiness', 'billing-readiness'],
    ['Invoice & Billing', 'invoice-billing-center'],
    ['Analytics Center', 'reporting']
  ]),
  administration: Object.freeze([
    ['Work Register', 'work-register'],
    ['Create Project', 'create-work-register'],
    ['Role Administration', 'role-admin'],
    ['Audit History', 'audit-history']
  ])
});

function getAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};
    const session = JSON.parse(raw);
    const token = session?.sessionToken || session?.token || session?.accessToken || '';
    return token ? { 'X-ProjectPulse-Session': token } : {};
  } catch {
    return {};
  }
}

async function fetchDashboard() {
  const response = await fetch('/api/work-lifecycle/dashboard', {
    headers: getAuthHeaders(),
    cache: 'no-store'
  });
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `Dashboard returned HTTP ${response.status}.`);
  }
  return response.json();
}

function normalizeRoleCodes(roleCodes) {
  return roleCodesFrom(Array.isArray(roleCodes) ? roleCodes : { roleCodes });
}

function hasAny(roleCodes, values) {
  return roleCodes.some((roleCode) => values.includes(roleCode));
}

function getPersona(roleCodes) {
  if (hasAny(roleCodes, ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR'])) return 'administration';
  if (roleCodes.includes('ACCOUNTING')) return 'accounting';
  if (hasAny(roleCodes, ['ACCOUNTING_BILLING', 'BILLING', 'FINANCE'])) return 'billing';
  if (roleCodes.includes('PROJECT_TEAM_COORDINATOR')) return 'coordinator';
  if (roleCodes.includes('EXECUTIVE')) return 'executive';
  if (hasAny(roleCodes, ['SALES', 'INSIDE_SALES', 'ACCOUNT_EXECUTIVE', 'SALES_MANAGER'])) return 'sales';
  if (roleCodes.some((roleCode) => PROJECT_MANAGEMENT_ROLES.has(roleCode))) return 'projectManagement';
  if (hasAny(roleCodes, ['MANAGER', 'PEOPLE_MANAGER'])) return 'management';
  return 'engineering';
}

function routeHref(routeModules, route) {
  const module = (routeModules ?? []).find((item) => item.route === route);
  return module?.href || '';
}

function percent(value, total) {
  if (!total) return 0;
  return Math.max(0, Math.min(100, Math.round((Number(value || 0) / Number(total)) * 100)));
}

function formatHours(value) {
  return Number(value || 0).toLocaleString('en-US', { maximumFractionDigits: 2 });
}

function formatDate(value) {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

function titleCase(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function priorityRoute(persona) {
  if (persona === 'sales') return 'opportunities';
  if (persona === 'billing') return 'billing-readiness';
  if (persona === 'accounting') return 'workflow';
  if (persona === 'projectManagement' || persona === 'management') return 'manager-approval';
  return 'timesheet';
}

function noTimeEntryMessage(persona) {
  if (persona === 'billing') {
    return 'This workspace focuses on billing readiness, reconciliation, invoicing, closeout, financial operations, and authorized reporting.';
  }
  if (persona === 'accounting') {
    return 'This workspace focuses on reconciliation, accounting review, audit evidence, billing readiness, and authorized reporting.';
  }
  return 'Your dashboard focuses on the operational actions authorized for this role.';
}

export default function RoleWelcomeDashboard({
  displayName,
  roleCodes,
  roleModules,
  approvalPendingCount = 0
}) {
  const normalizedRoles = useMemo(() => normalizeRoleCodes(roleCodes), [roleCodes]);
  const persona = useMemo(() => getPersona(normalizedRoles), [normalizedRoles]);
  const clientShowTimeEntry = useMemo(
    () => normalizedRoles.some((roleCode) => TIME_ENTRY_ROLES.has(roleCode))
      && !normalizedRoles.some((roleCode) => TIME_ENTRY_EXCLUDED_ROLES.has(roleCode)),
    [normalizedRoles]
  );
  const [state, setState] = useState({ loading: true, error: null, data: null });

  useEffect(() => {
    let cancelled = false;
    setState((current) => ({ ...current, loading: true, error: null }));
    fetchDashboard()
      .then((data) => {
        if (!cancelled) setState({ loading: false, error: null, data });
      })
      .catch((error) => {
        if (!cancelled) {
          setState({
            loading: false,
            error: error instanceof Error ? error.message : 'Unable to load the operational dashboard.',
            data: null
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [normalizedRoles.join('|')]);

  const timesheetHref = routeHref(roleModules, 'timesheet');
  const showTimeEntry = clientShowTimeEntry && Boolean(timesheetHref);
  const welcomeDisplayName = String(displayName || 'there').trim() || 'there';
  const todayLabel = new Date().toLocaleDateString('en-US', {
    weekday: 'long',
    month: 'long',
    day: 'numeric'
  });
  const actions = (ROLE_ACTIONS[persona] ?? ROLE_ACTIONS.engineering)
    .filter(([, route]) => showTimeEntry || route !== 'timesheet')
    .map(([label, route]) => ({ label, route, href: routeHref(roleModules, route) }))
    .filter((action) => action.href);
  const priorityHref = routeHref(roleModules, priorityRoute(persona));
  const projectHealthHref = routeHref(roleModules, 'project-workload');
  const billingReadinessHref = routeHref(roleModules, 'billing-readiness');
  const workRegisterHref = routeHref(roleModules, 'work-register');
  const week = state.data?.week ?? { applicable: showTimeEntry, enteredHours: 0, targetHours: 40, days: [] };
  const attention = state.data?.attention ?? {
    timeApprovals: approvalPendingCount,
    rejectedEntries: 0,
    projectAlerts: 0,
    closeoutPending: 0
  };
  const projectHealth = state.data?.projectHealth ?? { healthy: 0, needsReview: 0, atRisk: 0 };
  const billing = state.data?.billing ?? { unbilledHours: 0, readyToInvoice: 0, openInvoices: 0 };
  const projects = state.data?.projects ?? [];
  const recent = state.data?.recent ?? [];
  const weekPercent = percent(week.enteredHours, week.targetHours);

  return (
    <section id="role-welcome-dashboard" className={`role-welcome-dashboard persona-${persona}`}>
      <header className="welcome-dashboard-hero">
        <div>
          <p className="eyebrow">Project Health Platform</p>
          <h1>Good {new Date().getHours() < 12 ? 'morning' : new Date().getHours() < 18 ? 'afternoon' : 'evening'}, {welcomeDisplayName}</h1>
          <p>Here is what needs your attention today.</p>
        </div>
        <div className="welcome-dashboard-date">
          <span>{todayLabel}</span>
          <small>{getRoleWorkspaceLabel(normalizedRoles)}</small>
        </div>
        <nav className="welcome-quick-actions" aria-label="Recommended actions">
          {actions.map((action) => (
            <a href={action.href} key={`${action.route}-${action.label}`}>
              {action.label}
            </a>
          ))}
        </nav>
      </header>

      {state.error ? (
        <div className="welcome-dashboard-warning">
          Live dashboard totals are temporarily unavailable. Your authorized actions remain available above.
        </div>
      ) : null}

      <div className="welcome-dashboard-grid">
        {showTimeEntry ? (
          <article className="welcome-card welcome-week-card">
            <div className="welcome-card-heading">
              <div>
                <span>My week</span>
                <h2>{formatHours(week.enteredHours)} of {formatHours(week.targetHours)} hours entered</h2>
              </div>
              <strong>{weekPercent}%</strong>
            </div>
            <div className="welcome-progress" aria-label={`${weekPercent}% of weekly time entered`}>
              <span style={{ width: `${weekPercent}%` }} />
            </div>
            <div className="welcome-week-days">
              {(week.days ?? []).map((day) => (
                <div key={day.date}>
                  <span>{new Date(`${day.date}T12:00:00`).toLocaleDateString('en-US', { weekday: 'short' })}</span>
                  <strong>{formatHours(day.hours)}</strong>
                </div>
              ))}
            </div>
            <a className="welcome-card-link" href={timesheetHref}>Open weekly timesheet →</a>
          </article>
        ) : (
          <article className="welcome-card welcome-priorities-card">
            <div className="welcome-card-heading">
              <div>
                <span>Today&apos;s priorities</span>
                <h2>{getRoleWorkspaceName(normalizedRoles)} operations</h2>
              </div>
            </div>
            <p className="welcome-card-copy">{noTimeEntryMessage(persona)}</p>
            <div className="welcome-priority-links">
              {actions.slice(0, 4).map((action) => (
                <a href={action.href} key={`priority-${action.route}`}>{action.label} →</a>
              ))}
            </div>
          </article>
        )}

        <article className="welcome-card welcome-attention-card">
          <div className="welcome-card-heading">
            <div><span>Requires attention</span><h2>Action queue</h2></div>
          </div>
          <dl className="welcome-metric-list">
            <div><dt>Time approvals</dt><dd>{attention.timeApprovals}</dd></div>
            {showTimeEntry ? <div><dt>Rejected entries</dt><dd>{attention.rejectedEntries}</dd></div> : null}
            <div><dt>Project alerts</dt><dd>{attention.projectAlerts}</dd></div>
            <div><dt>Closeout pending</dt><dd>{attention.closeoutPending}</dd></div>
          </dl>
          {priorityHref ? <a className="welcome-card-link" href={priorityHref}>Open priority workspace →</a> : null}
        </article>

        <article className="welcome-card welcome-project-health-card">
          <div className="welcome-card-heading"><div><span>Project health</span><h2>Delivery snapshot</h2></div></div>
          <dl className="welcome-health-list">
            <div className="healthy"><dt>Healthy</dt><dd>{projectHealth.healthy}</dd></div>
            <div className="review"><dt>Needs review</dt><dd>{projectHealth.needsReview}</dd></div>
            <div className="risk"><dt>At risk</dt><dd>{projectHealth.atRisk}</dd></div>
          </dl>
          {projectHealthHref ? <a className="welcome-card-link" href={projectHealthHref}>Open project health →</a> : null}
        </article>

        <article className="welcome-card welcome-billing-card">
          <div className="welcome-card-heading"><div><span>Team / billing snapshot</span><h2>Work-to-Cash</h2></div></div>
          <dl className="welcome-metric-list">
            <div><dt>Unbilled approved hours</dt><dd>{formatHours(billing.unbilledHours)}</dd></div>
            <div><dt>Ready to invoice</dt><dd>{billing.readyToInvoice}</dd></div>
            <div><dt>Open invoices</dt><dd>{billing.openInvoices}</dd></div>
          </dl>
          {billingReadinessHref ? <a className="welcome-card-link" href={billingReadinessHref}>Open billing readiness →</a> : null}
        </article>

        <article className="welcome-card welcome-projects-card">
          <div className="welcome-card-heading"><div><span>{state.data?.scope === 'portfolio' ? 'Portfolio projects' : 'My projects'}</span><h2>Current work</h2></div></div>
          <div className="welcome-project-list">
            {projects.length === 0 ? (
              <p className="welcome-empty">No scoped projects require attention.</p>
            ) : projects.map((project) => (
              <a href={workRegisterHref || undefined} aria-disabled={workRegisterHref ? undefined : 'true'} key={project.projectId}>
                <div><strong>{project.projectCode || project.projectName}</strong><span>{project.customerName || project.projectName}</span></div>
                <div><strong>{project.activeTaskCount ?? 0}</strong><span>{project.activeTaskCount === 1 ? 'active task' : 'active tasks'}</span><span>{titleCase(project.status)}</span></div>
              </a>
            ))}
          </div>
        </article>

        <article className="welcome-card welcome-recent-card">
          <div className="welcome-card-heading"><div><span>Recent items</span><h2>Tracked activity</h2></div></div>
          <div className="welcome-recent-list">
            {recent.length === 0 ? (
              <p className="welcome-empty">{state.loading ? 'Loading current activity…' : 'No recent scoped activity.'}</p>
            ) : recent.slice(0, 6).map((item, index) => {
              const itemHref = routeHref(roleModules, item.processArea === 'invoice' ? 'invoice-billing-center' : 'work-register');
              return (
                <a href={itemHref || undefined} aria-disabled={itemHref ? undefined : 'true'} key={`${item.createdAt}-${index}`}>
                  <span>{titleCase(item.processArea)}</span>
                  <strong>{item.summary}</strong>
                  <small>{item.projectCode} {formatDate(item.createdAt)}</small>
                </a>
              );
            })}
          </div>
        </article>
      </div>
    </section>
  );
}

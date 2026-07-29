import USSignalLogo from './USSignalLogo.jsx';
import './enterprise-module-system.css';

const MODULE_PRESENTATION = Object.freeze({
  '024': Object.freeze({
    group: 'Sales & Opportunities',
    title: 'Sales Intake',
    summary: 'Capture qualified sales opportunities and prepare a governed handoff into delivery.',
    owner: 'Sales and Project Operations',
    posture: 'Intake workflow'
  }),
  '025': Object.freeze({
    group: 'Sales & Opportunities',
    title: 'SOW Generator',
    summary: 'Prepare governed statements of work with consistent scope, assumptions, deliverables, and review evidence.',
    owner: 'Sales and Solution Architecture',
    posture: 'Document workflow'
  }),
  '027': Object.freeze({
    group: 'Project Delivery',
    title: 'Signed Handoff',
    summary: 'Move an approved sales package into delivery with accountable ownership and resource-handoff evidence.',
    owner: 'Project Operations',
    posture: 'Delivery handoff'
  }),
  '028': Object.freeze({
    group: 'Time Management',
    title: 'AI Time Entry',
    summary: 'Assist users with customer-facing time descriptions while preserving human review and time-entry authority.',
    owner: 'Engineering and Project Delivery',
    posture: 'Governed assistance'
  }),
  '029': Object.freeze({
    group: 'Platform Operations',
    title: 'UAT Validation',
    summary: 'Validate role, workflow, and release behavior with clear expected outcomes and durable evidence.',
    owner: 'Application Operations',
    posture: 'Validation workspace'
  }),
  '064': Object.freeze({
    group: 'Security',
    title: 'AI Provider Configuration Center',
    summary: 'Review governed AI-provider readiness, non-secret configuration, routing, and operational health.',
    owner: 'AI and Platform Administration',
    posture: 'Provider administration'
  }),
  '068': Object.freeze({
    group: 'Platform Operations',
    title: 'Provider-Neutral System Architecture',
    summary: 'Present the live platform, integration, regional, redundancy, and module-to-API architecture.',
    owner: 'Platform Engineering',
    posture: 'Architecture evidence'
  }),
  '069': Object.freeze({
    group: 'Resources',
    title: 'Qualifications & Certification Matrix',
    summary: 'Manage role-appropriate qualification, certification, renewal, and evidence visibility.',
    owner: 'Resource and Practice Management',
    posture: 'Workforce readiness'
  }),
  '071': Object.freeze({
    group: 'Platform Operations',
    title: 'On-Call Scheduling',
    summary: 'Coordinate on-call coverage, rotations, conflicts, and operational ownership.',
    owner: 'Operations Management',
    posture: 'Coverage planning'
  }),
  '072': Object.freeze({
    group: 'Platform Operations',
    title: 'OneAssist Routing Directory',
    summary: 'Maintain governed OneAssist routing information with clear ownership and access boundaries.',
    owner: 'Support Operations',
    posture: 'Operational directory'
  }),
  '074': Object.freeze({
    group: 'Sales & Opportunities',
    title: 'OEM & Vendor Directory',
    summary: 'Maintain an authoritative operational directory for OEM and vendor relationships.',
    owner: 'Sales and Partner Operations',
    posture: 'Partner directory'
  })
});

export function EnterprisePrintHeader({ moduleCode, title }) {
  return (
    <div className="uss-print-header" aria-hidden="true">
      <USSignalLogo decorative size="compact" />
      <div>
        <strong>{title}</strong>
        <span>Module {moduleCode} · ProjectPulse</span>
      </div>
    </div>
  );
}

export function EnterpriseModuleLabel({ moduleCode, group }) {
  return (
    <p className="uss-module-label">
      <span>Module {moduleCode}</span>
      <i aria-hidden="true" />
      <span>{group}</span>
    </p>
  );
}

export function EnterprisePageHeader({
  moduleCode,
  group,
  title,
  summary,
  actions = null
}) {
  return (
    <header className="uss-enterprise-page-header">
      <div className="uss-enterprise-page-header__identity">
        <USSignalLogo size="large" />
        <div>
          <EnterpriseModuleLabel moduleCode={moduleCode} group={group} />
          <h1>{title}</h1>
          <p>{summary}</p>
        </div>
      </div>
      {actions ? <div className="uss-enterprise-page-header__actions">{actions}</div> : null}
    </header>
  );
}

export function EnterpriseStatusCard({ label, value, detail, tone = 'neutral' }) {
  const normalizedTone = ['neutral', 'healthy', 'warning', 'critical', 'informational'].includes(tone)
    ? tone
    : 'neutral';

  return (
    <article className={`uss-status-card uss-status-card--${normalizedTone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
      {detail ? <small>{detail}</small> : null}
    </article>
  );
}

export function EnterpriseSummaryStrip({ children, ariaLabel = 'Module summary' }) {
  return (
    <section className="uss-summary-strip" aria-label={ariaLabel}>
      {children}
    </section>
  );
}

export function EnterpriseFilterBar({ children, actions = null, ariaLabel = 'Filters' }) {
  return (
    <section className="uss-filter-bar" aria-label={ariaLabel}>
      <div className="uss-filter-bar__fields">{children}</div>
      {actions ? <div className="uss-filter-bar__actions">{actions}</div> : null}
    </section>
  );
}

export function EnterpriseTabs({ tabs = [], activeTab, onChange, ariaLabel = 'Workspace views' }) {
  return (
    <div className="uss-tabs" role="tablist" aria-label={ariaLabel}>
      {tabs.map((tab) => (
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === tab.key}
          className={activeTab === tab.key ? 'active' : ''}
          key={tab.key}
          onClick={() => onChange?.(tab.key)}
        >
          <strong>{tab.label}</strong>
          {tab.description ? <small>{tab.description}</small> : null}
        </button>
      ))}
    </div>
  );
}

export function EnterpriseTable({ columns = [], rows = [], rowKey = 'id', caption = '' }) {
  return (
    <div className="uss-table-wrap">
      <table className="uss-table">
        {caption ? <caption>{caption}</caption> : null}
        <thead>
          <tr>{columns.map((column) => <th key={column.key}>{column.label}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={row?.[rowKey] ?? index}>
              {columns.map((column) => (
                <td key={column.key} data-label={column.label}>
                  {column.render ? column.render(row, index) : row?.[column.key]}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function EnterpriseEmptyState({ title, message, action = null }) {
  return (
    <div className="uss-empty-state" role="status">
      <strong>{title}</strong>
      <p>{message}</p>
      {action ? <div>{action}</div> : null}
    </div>
  );
}

export function EnterpriseWarning({ title, message, tone = 'warning', action = null }) {
  const normalizedTone = tone === 'critical' ? 'critical' : tone === 'informational' ? 'informational' : 'warning';
  return (
    <div className={`uss-warning uss-warning--${normalizedTone}`} role="alert">
      <div>
        <strong>{title}</strong>
        <p>{message}</p>
      </div>
      {action ? <div className="uss-warning__action">{action}</div> : null}
    </div>
  );
}

export function EnterpriseModulePage({
  moduleCode,
  group,
  title,
  summary,
  actions = null,
  children,
  className = ''
}) {
  return (
    <section
      className={`uss-enterprise-module-page ${className}`.trim()}
      data-uss-enterprise-module={moduleCode}
    >
      <EnterprisePrintHeader moduleCode={moduleCode} title={title} />
      <EnterprisePageHeader
        moduleCode={moduleCode}
        group={group}
        title={title}
        summary={summary}
        actions={actions}
      />
      {children}
    </section>
  );
}

export default function EnterpriseModulePresentation({ moduleCode }) {
  const metadata = MODULE_PRESENTATION[moduleCode] ?? {
    group: 'ProjectPulse',
    title: `Module ${moduleCode}`,
    summary: 'Enterprise ProjectPulse workspace.',
    owner: 'Application Operations',
    posture: 'Enterprise workspace'
  };

  return (
    <section
      className="uss-enterprise-module-presentation"
      data-group6-enterprise-presentation={moduleCode}
    >
      <EnterprisePrintHeader moduleCode={moduleCode} title={metadata.title} />
      <EnterprisePageHeader
        moduleCode={moduleCode}
        group={metadata.group}
        title={metadata.title}
        summary={metadata.summary}
      />
      <EnterpriseSummaryStrip>
        <EnterpriseStatusCard
          label="Presentation"
          value="US Signal standard"
          detail="One approved image asset and shared enterprise components"
          tone="healthy"
        />
        <EnterpriseStatusCard
          label="Functional scope"
          value="Preserved"
          detail="Existing APIs, workflows, and permissions remain authoritative"
          tone="informational"
        />
        <EnterpriseStatusCard
          label="Responsible owner"
          value={metadata.owner}
          detail={metadata.posture}
          tone="neutral"
        />
      </EnterpriseSummaryStrip>
    </section>
  );
}

export { MODULE_PRESENTATION };

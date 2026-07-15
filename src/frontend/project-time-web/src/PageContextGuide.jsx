import './page-context-guide.css';

const routeContext = {
  'production-data-readiness': {
    page: 'Production Data Readiness Center',
    purpose: 'Shows whether core production data exists for users, roles, customers, projects, tasks, time, approvals, exports, audit evidence, and notifications.',
    backend: '/api/production/data-readiness',
    check: 'Refresh data readiness, review each table count/status, then open the linked pages to validate data visibility.'
  },

  /* 039_042_BILLING_CONTEXT_GUIDE_START */
  'billing-readiness': {
    page: 'Billing Readiness Center — Module 039',
    purpose: 'Pre-invoice quality gate. It checks whether approved labor, expenses, customer/project mapping, notes, evidence, and exceptions are ready to become a billing package. It does not create or send the customer invoice.',
    backend: '/api/project-intake/overview and billing-readiness data sources',
    check: 'Resolve every blocking item, confirm the billing package is eligible, and then move the package to Module 042 for invoice preparation.'
  },
  'invoice-billing-center': {
    page: 'Invoice & Billing Center — Module 042',
    purpose: 'Live invoice operations using approved uninvoiced time, explicit effective stored-rate selection, purchase-order readiness, and immutable partial or final invoice snapshots. Fixed Price dollars remain blocked until stored milestone billing is implemented.',
    backend: '/api/billing/candidates, /api/billing/projects/{projectId}/invoices, and /api/billing/invoices/{invoiceId}',
    check: 'Confirm approved source lines, select only effective stored rates, resolve required purchase orders, create a partial or final invoice, and verify its PHD-XXXXXX-N number and immutable history. Missing values remain visibly unconfigured.'
  },
  /* 039_042_BILLING_CONTEXT_GUIDE_END */

  dashboard: {
    page: 'Dashboard',
    purpose: 'Role-based landing page showing the modules, alerts, and work areas available to the signed-in user.',
    backend: '/api/dashboard/module-visibility-smoke and role/module visibility checks',
    check: 'Confirm the app loads, navigation is usable, and visible modules match the current role.'
  },
  'production-readiness': {
    page: 'Production Readiness Center',
    purpose: 'Visible release-readiness view that connects backend readiness checks to browser validation.',
    backend: '/api/production/readiness-command-center',
    check: 'Click Refresh readiness, confirm cards and check rows appear, then use the validation checklist.'
  },
  'project-intake': {
    page: 'Project Intake',
    purpose: 'Captures intake requests, triage state, documents, post-intake movement, project linking, and work-task handoff.',
    backend: '/api/project-intake/* and /api/work-tasks/*',
    check: 'Confirm intake summary, aging, post-intake, documents, project link, and handoff areas load.'
  },
  'project-workspace': {
    page: 'Project Workspace',
    purpose: 'Gives project and engineering roles a workspace for assigned projects, tasks, documents, hours, and remaining work.',
    backend: '/api/project-workspace/*, /api/resource-scheduling/capacity, and /api/project-allocation-info/*',
    check: 'Confirm assigned projects, tasks, documents, and resource context are visible for the current role.'
  },
  workflow: {
    page: 'Approval / Export / Audit Workflows',
    purpose: 'Coordinates workflow validation, approvals, export package readiness, reconciliation, lock evidence, and audit evidence.',
    backend: '/api/workflow/*, /api/time-exports, and /api/export-packages/readiness-summary',
    check: 'Confirm approval, export, reconciliation, validation rules, and evidence sections load.'
  },
  'manager-approval': {
    page: 'Manager Approvals',
    purpose: 'Allows managers and authorized roles to review submitted time, approve, decline, unlock, and monitor approval queues.',
    backend: '/api/manager/approvals',
    check: 'Confirm approval queues are visible only for roles with approval authority.'
  },
  'role-admin': {
    page: 'Role / Security Administration',
    purpose: 'Manages role access, permissions, route contracts, role visibility, and administrator View-As governance.',
    backend: '/api/admin/roles, /api/admin/users, and role enforcement middleware',
    check: 'Confirm restricted role/security controls are visible only to administrator or system roles.'
  },
  'audit-history': {
    page: 'Audit History',
    purpose: 'Shows traceability for login, role, approval, export, notification, and administrative actions.',
    backend: '/api/audit/history and /api/audit-history/events',
    check: 'Confirm audit records, filters, and event evidence load.'
  },
  'customer-directory': {
    page: 'Customer Directory',
    purpose: 'Maintains customer/account records, contacts, and customer data used by intake, billing, and reconciliation workflows.',
    backend: '/api/customers/*',
    check: 'Confirm customer records, contacts, and empty states load clearly.'
  },
  'user-admin': {
    page: 'User Administration',
    purpose: 'Manages users, local account status, active status, and role assignment readiness.',
    backend: '/api/admin/users and /api/admin/roles',
    check: 'Confirm user records load and role-changing controls remain restricted.'
  },
  'azure-admin': {
    page: 'Azure / Entra Admin',
    purpose: 'Supports Entra configuration, import preview, selected import, and identity readiness.',
    backend: '/api/azure-admin/*',
    check: 'Confirm configuration, preview, and import controls are visible only to authorized roles.'
  },
  'service-control': {
    page: 'Service Control',
    purpose: 'Provides operational service status and controlled restart visibility.',
    backend: 'system service controls through protected API operations',
    check: 'Confirm service controls are restricted and service status is understandable.'
  },
  'backup-dr': {
    page: 'Backup / DR',
    purpose: 'Shows backup and disaster recovery readiness, backup state, and restore preparedness.',
    backend: 'backup status, restore-readiness, and operational evidence endpoints',
    check: 'Confirm backup status, DR readiness, and restore evidence are visible.'
  },
  'restore-validation': {
    page: 'Restore Validation',
    purpose: 'Validates restore points, restore readiness, and restore test evidence before relying on backups.',
    backend: 'restore validation and backup evidence endpoints',
    check: 'Confirm restore evidence is clear and current.'
  },
  'backup-retention': {
    page: 'Backup Retention',
    purpose: 'Manages backup retention review, cleanup readiness, and restore-point protection.',
    backend: 'backup retention and cleanup-readiness endpoints',
    check: 'Confirm retention status and protected restore points are understandable.'
  },
  'replication-sync': {
    page: 'Replication Sync',
    purpose: 'Shows replication and synchronization status across backup, database, and operational readiness workflows.',
    backend: 'replication/sync status endpoints',
    check: 'Confirm sync status and failure states are clear.'
  },
  timesheet: {
    page: 'Timesheet',
    purpose: 'Allows users to enter, save, submit, and review project and non-project time.',
    backend: '/api/timesheets/*',
    check: 'Confirm time entry, save, submit, and empty-state behavior work for the role.'
  },
  utilization: {
    page: 'Utilization',
    purpose: 'Shows utilization performance against quarterly and annual targets.',
    backend: '/api/utilization/*',
    check: 'Confirm utilization metrics load and match expected role visibility.'
  },
  'time-compliance': {
    page: 'Time Compliance',
    purpose: 'Supports missing-time visibility, reminders, manager/PTC review, and month-end time controls.',
    backend: '/api/time-compliance/* and email notification readiness endpoints',
    check: 'Confirm compliance status and reminder readiness are visible.'
  }
};

function getContext(route) {
  return routeContext[route] || {
    page: route || 'Current page',
    purpose: 'Installed Project Health Dashboard page for the current role.',
    backend: 'Role-protected application endpoints',
    check: 'Confirm the page loads, navigation works, and role restrictions make sense.'
  };
}

export default function PageContextGuide({ activeRoute }) {
  const context = getContext(activeRoute);

  return (
    <aside className="page-context-guide" aria-label="Page context guide">
      <details open>
        <summary>
          <span>
            <strong>{context.page}</strong>
            <small>What this page does and what backend process supports it</small>
          </span>
        </summary>

        <div className="page-context-guide-grid">
          <div>
            <span>Purpose</span>
            <p>{context.purpose}</p>
          </div>

          <div>
            <span>Backend support</span>
            <p><code>{context.backend}</code></p>
          </div>

          <div>
            <span>What to check</span>
            <p>{context.check}</p>
          </div>
        </div>
      </details>
    </aside>
  );
}

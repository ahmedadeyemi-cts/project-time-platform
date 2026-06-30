import './production-readiness-center.css';

const purposeRows = [
  {
    webpage: 'Production Readiness Center',
    route: '#production-readiness',
    backend: '/api/production/readiness-command-center',
    purpose: 'Shows whether the system has enough users, projects, time, audit, export, and route evidence for release readiness.',
    check: 'Open the page, click Refresh readiness, and confirm readiness cards and backend check rows appear.'
  },
  {
    webpage: 'Dashboard',
    route: '#dashboard',
    backend: '/api/dashboard/module-visibility-smoke',
    purpose: 'Confirms role-based module visibility and helps prove users see the right workspace areas.',
    check: 'Confirm the dashboard loads and the modules shown match the signed-in user role.'
  },
  {
    webpage: 'Project Intake',
    route: '#project-intake',
    backend: '/api/project-intake/summary and intake handoff endpoints',
    purpose: 'Supports intake review, aging, post-intake movement, project linking, documents, and work-task handoff.',
    check: 'Confirm intake sections load and empty states make sense when no records are available.'
  },
  {
    webpage: 'Resource / Project Workspace',
    route: '#project-workspace',
    backend: '/api/resource-scheduling/capacity and /api/project-allocation-info/*',
    purpose: 'Supports resource assignment, allocation review, project workspace context, and engineer workload visibility.',
    check: 'Confirm project/resource panels load and assigned work appears for the correct role.'
  },
  {
    webpage: 'Approval / Export / Audit Workflows',
    route: '#workflow',
    backend: '/api/workflow/*, /api/time-exports, /api/export-packages/readiness-summary',
    purpose: 'Supports approval workflow readiness, export package preparation, accounting reconciliation, lock evidence, and operational validation.',
    check: 'Confirm workflow cards, export readiness, reconciliation, and validation areas load.'
  },
  {
    webpage: 'Manager Approvals',
    route: '#manager-approval',
    backend: '/api/manager/approvals',
    purpose: 'Supports manager review of submitted time, approve/decline actions, unlock requests, and approval queue visibility.',
    check: 'Confirm approval queues display only for roles with approval authority.'
  },
  {
    webpage: 'Role / Security Administration',
    route: '#role-admin',
    backend: '/api/admin/roles, /api/admin/users, role enforcement middleware',
    purpose: 'Supports role assignments, permission visibility, route contracts, View-As behavior, and access governance.',
    check: 'Confirm restricted security controls are visible only to administrator/system roles.'
  },
  {
    webpage: 'Audit History',
    route: '#audit-history',
    backend: '/api/audit/history and /api/audit-history/events',
    purpose: 'Supports traceability for login, role, approval, export, notification, and administrative events.',
    check: 'Confirm audit filters and event records load.'
  },
  {
    webpage: 'Operational Runbook / Smoke Checks',
    route: '#production-readiness',
    backend: 'scripts/021-production-readiness-smoke.sh',
    purpose: 'Confirms service health, frontend availability, protected endpoint behavior, and deployment evidence after each test deployment.',
    check: 'Review smoke status in command output and use this page to track manual browser validation.'
  }
];

export default function ProductionReadinessPurposeMapPanel() {
  return (
    <section className="production-readiness-panel purpose-map-panel">
      <div className="production-readiness-panel-heading">
        <div>
          <p className="eyebrow">What you are checking</p>
          <h2>Webpage and backend purpose map</h2>
          <p>
            This map explains what each visible webpage area is supposed to prove and which backend
            process supports it. Use this when reviewing the app so the page, endpoint, and validation
            purpose are clear.
          </p>
        </div>
      </div>

      <div className="purpose-map-table">
        <table>
          <thead>
            <tr>
              <th>Webpage area</th>
              <th>Backend support</th>
              <th>Purpose</th>
              <th>What to check</th>
            </tr>
          </thead>
          <tbody>
            {purposeRows.map((row) => (
              <tr key={`${row.webpage}-${row.backend}`}>
                <td>
                  <a href={row.route}>{row.webpage}</a>
                </td>
                <td>
                  <code>{row.backend}</code>
                </td>
                <td>{row.purpose}</td>
                <td>{row.check}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

import './intuitive-more-menu.js';

export const PROJECTPULSE_MODULES = Object.freeze([
  Object.freeze({ moduleNumber: '001', route: 'timesheet', displayName: 'Timesheet', group: 'Time Management' }),
  Object.freeze({ moduleNumber: '002', route: 'manager-approval', displayName: 'Approval Inbox', group: 'Approvals' }),
  Object.freeze({ moduleNumber: '003', route: 'utilization', displayName: 'Utilization', group: 'Resource Management' }),
  Object.freeze({ moduleNumber: '004', route: 'holiday-admin', displayName: 'Holiday Administration', group: 'Time Management' }),
  Object.freeze({ moduleNumber: '005', route: 'project-allocation-info', displayName: 'Project Expense Upload', group: 'Project Management' }),
  Object.freeze({ moduleNumber: '006', route: 'toyota-hyundai-pipelines', displayName: 'Toyota & Hyundai Pipelines', group: 'Sales & Opportunities', description: 'Governed Toyota and Hyundai project pipeline with active and archived delivery context, ownership, engineering assignments, SELL references, tasks, documents, financial context, and lifecycle evidence.' }),
  Object.freeze({ moduleNumber: '007', route: 'workflow', displayName: 'Approval, Export & Audit Workflow', group: 'Approvals', description: 'Post-time-entry approval, accounting reconciliation, export preparation, package download, preflight validation, and workflow audit evidence.' }),
  Object.freeze({ moduleNumber: '008', route: 'audit-history', displayName: 'Audit History', group: 'Security & Audit' }),
  Object.freeze({ moduleNumber: '009', route: 'user-admin', displayName: 'User Administration', group: 'Administration' }),
  Object.freeze({ moduleNumber: '010', route: 'azure-admin', displayName: 'Azure / Entra Directory Users', group: 'Administration' }),
  Object.freeze({
    moduleNumber: '011',
    route: 'work-task-builder',
    displayName: 'Pulse AI',
    group: 'AI & Automation',
    lifecycle: 'source_foundation',
    description: 'Governed ProjectPulse workspace for knowledge sources, datasets, external training orchestration, evaluations, model registry, and controlled promotion through Module 064.',
    compatibilityRoute: true,
    previousIdentity: Object.freeze({
      displayName: 'Work Task Builder',
      lifecycle: 'retired_non_destructively',
      replacementRoutes: Object.freeze(['work-register', 'create-work-register']),
      recoveryCheckpoint: 'main@ad9fa2c76f6aba8df9bbdd4ab6970dcb0748fbb2',
      retirementReason: 'Project creation and project/task management moved to Modules 055D and 055C.'
    })
  }),
  Object.freeze({ moduleNumber: '012', route: 'role-admin', displayName: 'Role Administration', group: 'Administration' }),
  Object.freeze({ moduleNumber: '013', route: 'service-control', displayName: 'System Health & API Diagnostics', group: 'Platform Operations', description: 'Provider-neutral first-response troubleshooting for platform identity, resource use, dependencies, integrations, workers, deployments, capabilities, and every registered API.' }),
  Object.freeze({ moduleNumber: '014', route: 'backup-dr', displayName: 'Backup & Disaster Recovery', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '015', route: 'restore-validation', displayName: 'Restore Validation', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '016', route: 'backup-retention', displayName: 'Operational Evidence & Backup Retention', group: 'Platform Operations', description: 'Searchable sanitized request evidence, failures, correlation IDs, workers, scheduled-job readiness, diagnostic export, and preserved backup-retention controls.' }),
  Object.freeze({ moduleNumber: '017', route: 'replication-sync', displayName: 'Replication & Sync', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '018', route: 'project-workload', displayName: 'Project Workload', group: 'Project Management' }),
  Object.freeze({ moduleNumber: '019', route: 'project-workspace', displayName: 'Project Engineering Workspace', group: 'Project Delivery', description: 'Role-scoped project assignments and document access. It consumes project data directly and has no Module 011 dependency.' }),
  Object.freeze({ moduleNumber: '020', route: 'project-intake', displayName: 'Project Intake & Resource Handoff', group: 'Project Delivery', description: 'Pre-project request, signed-date aging, project-link confirmation, engineering demand, and resource handoff before Modules 055D and 055C own the project record.' }),
  Object.freeze({ moduleNumber: '021', route: 'customer-directory', displayName: 'Customer Directory', group: 'Customers' }),
  Object.freeze({ moduleNumber: '022', route: 'cost-alerts', displayName: 'Cost Alerts', group: 'Reports & Workflow' }),
  Object.freeze({ moduleNumber: '023', route: 'time-compliance', displayName: 'Time Compliance', group: 'Time Management' }),
  Object.freeze({ moduleNumber: '024', route: 'sales-intake', displayName: 'Sales Intake', group: 'Sales & Opportunities' }),
  Object.freeze({ moduleNumber: '025', route: 'sow-generator', displayName: 'SOW Generator', group: 'Sales & Opportunities' }),
  Object.freeze({ moduleNumber: '026', route: 'crm-integration', displayName: 'CRM / ERP Integration Center', group: 'Integrations' }),
  Object.freeze({ moduleNumber: '027', route: 'signed-handoff', displayName: 'Signed Handoff', group: 'Project Delivery' }),
  Object.freeze({ moduleNumber: '028', route: 'ai-time-entry', displayName: 'AI Time Entry', group: 'Time Management' }),
  Object.freeze({ moduleNumber: '029', route: 'uat-validation', displayName: 'UAT Validation', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '030', route: 'reporting', displayName: 'Financial Report Center', group: 'Reports & Workflow', description: 'Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.' }),
  Object.freeze({ moduleNumber: '031', route: 'financial-operations-workbench', displayName: 'Financial Operations Workbench', group: 'Reports & Workflow', description: 'Accountable queue for source failures, billing and closeout blockers, reconciliation exceptions, notification failures, retry, and resolution evidence.' }),
  Object.freeze({ moduleNumber: '032', route: 'notification-delivery-monitor', displayName: 'Notification Delivery Monitor', group: 'Reports & Workflow', description: 'Operational inbox for project notification dispatches, recipient derivation, Module 065 readiness, source failures, release, retry, and delivery evidence.' }),
  Object.freeze({ moduleNumber: '036', route: 'sales-insights', displayName: 'Sales Insights Dashboard', group: 'Sales & Opportunities' }),
  Object.freeze({ moduleNumber: '037', route: 'roles-permissions-matrix', displayName: 'Roles & Permissions Matrix', group: 'Administration' }),
  Object.freeze({ moduleNumber: '038', route: 'certify-integration', displayName: 'Certify Connection & Sync Center', group: 'Integrations' }),
  Object.freeze({ moduleNumber: '039', route: 'billing-readiness', displayName: 'Billing Readiness', group: 'Reports & Workflow' }),
  Object.freeze({ moduleNumber: '040', route: 'project-closeout', displayName: 'Project Closeout', group: 'Reports & Workflow' }),
  Object.freeze({ moduleNumber: '041', route: 'closeout-email', displayName: 'Closeout Email Automation', group: 'Reports & Workflow' }),
  Object.freeze({ moduleNumber: '042', route: 'invoice-billing-center', displayName: 'Invoice & Billing Center', group: 'Reports & Workflow' }),
  Object.freeze({ moduleNumber: '055B', route: 'rate-card-administration', displayName: 'Rate Card Administration', group: 'Project Operations' }),
  Object.freeze({ moduleNumber: '055C', route: 'work-register', displayName: 'Manage Existing Projects', group: 'Project Operations', description: 'Authoritative workspace for editing existing project records and maintaining project delivery details after creation.' }),
  Object.freeze({ moduleNumber: '055D', route: 'create-work-register', displayName: 'Create New Project', group: 'Project Operations', description: 'Authoritative project-creation workflow using GSD or SELL source information.' }),
  Object.freeze({ moduleNumber: '057', route: 'calendar-capacity', displayName: 'Calendar & Capacity', group: 'Resource Management' }),
  Object.freeze({ moduleNumber: '058', route: 'cicd-pipeline', displayName: 'CI/CD Pipeline', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '060', route: 'contracts', displayName: 'Contracts', group: 'Project Operations' }),
  Object.freeze({ moduleNumber: '063', route: 'opportunities', displayName: 'Opportunities', group: 'Sales & Opportunities' }),
  Object.freeze({ moduleNumber: '064', route: 'ai-provider-configuration', displayName: 'AI Provider Configuration Center', group: 'Security' }),
  Object.freeze({ moduleNumber: '065', route: 'entra-secret-administration', displayName: 'Microsoft Integration Connection', group: 'Integrations' }),
  Object.freeze({ moduleNumber: '066', route: 'project-flowhive', displayName: 'Project FlowHive', group: 'Project Delivery' }),
  Object.freeze({ moduleNumber: '068', route: 'system-architecture', displayName: 'Provider-Neutral System Architecture', group: 'Platform Operations', description: 'Live provider, system, integration, module-to-API, region, redundancy, and data-flow architecture generated from the shared platform registry with a branded export.' }),
  Object.freeze({ moduleNumber: '069', route: 'qualifications-certifications', displayName: 'Qualifications & Certification Matrix', group: 'Resources' }),
  Object.freeze({ moduleNumber: '070', route: 'capacity-pipeline-forecast', displayName: 'Capacity & Pipeline Forecasting', group: 'Resource Management', description: 'Reads capacity, assignments, and project-intake demand directly. It has no Module 011 dependency.' }),
  Object.freeze({ moduleNumber: '071', route: 'oncall-scheduling', displayName: 'On-Call Scheduling', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '072', route: 'oneassist-routing-directory', displayName: 'OneAssist Routing Directory', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '073', route: 'sales-coverage-alignment', displayName: 'Sales Coverage Alignment', group: 'Sales & Opportunities' }),
  Object.freeze({ moduleNumber: '074', route: 'oem-vendor-directory', displayName: 'OEM & Vendor Directory', group: 'Sales & Opportunities' }),
  Object.freeze({ moduleNumber: '075', route: 'integration-event-gateway', displayName: 'Integration Automation & Event Gateway', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '076', route: 'defect-tracker', displayName: 'Defect Intake & Resolution Tracker', group: 'Help & Documentation' }),
  Object.freeze({ moduleNumber: '077', route: 'release-deployment-control', displayName: 'Release, Deployment & Rollback Control Center', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '078', route: 'observability-slo-health', displayName: 'Observability, SLO & Application Health Center', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '079', route: 'data-governance-retention', displayName: 'Data Governance, Retention & Privacy Center', group: 'Security & Audit' }),
  Object.freeze({ moduleNumber: '080', route: 'customer-delivery-acceptance', displayName: 'Customer Delivery & Acceptance Portal', group: 'Project Operations' }),
  Object.freeze({ moduleNumber: '997', route: 'security-operations', displayName: 'Security Operations, Threat Intelligence & Response Center', group: 'Security & Audit' }),
  Object.freeze({ moduleNumber: '998', route: 'system-diagnostics', displayName: 'System Diagnostic & Controlled Remediation Center', group: 'Platform Operations' }),
  Object.freeze({ moduleNumber: '999', route: 'user-guide', displayName: 'System User Guide', group: 'Help & Documentation' }),
]);

export const RETIRED_PROJECTPULSE_MODULES = Object.freeze(
  PROJECTPULSE_MODULES.filter((module) => module.isRetired === true)
);

const ROUTE_ALIASES = Object.freeze({
  'psa-modules': 'toyota-hyundai-pipelines',
  'project-register': 'toyota-hyundai-pipelines',
  'project-manager-workload': 'project-workload',
  'project-management-workload': 'project-workload',
  'resource-assignment-handoff': 'signed-handoff',
  'global-mail-configuration': 'entra-secret-administration'
});

export const MODULE_BY_NUMBER = new Map(PROJECTPULSE_MODULES.map((module) => [module.moduleNumber.toUpperCase(), module]));
export const MODULE_BY_ROUTE = new Map(PROJECTPULSE_MODULES.map((module) => [module.route, module]));
export function rawModuleRoute(route) { return String(route || '').replace(/^#/, '').trim(); }
export function canonicalModuleRoute(route) { const normalized = rawModuleRoute(route); return ROUTE_ALIASES[normalized] || normalized; }
export function moduleForRoute(route) { return MODULE_BY_ROUTE.get(canonicalModuleRoute(route)) || null; }
export function moduleForNumber(moduleNumber) { return MODULE_BY_NUMBER.get(String(moduleNumber || '').trim().toUpperCase()) || null; }
export function retiredModuleForRoute(route) { const normalized = rawModuleRoute(route); return RETIRED_PROJECTPULSE_MODULES.find((module) => module.route === normalized) || null; }
export function isRetiredModuleRoute(route) { return Boolean(retiredModuleForRoute(route)); }
export function currentProjectPulseRoute() { return canonicalModuleRoute(window.location.hash || '#dashboard') || 'dashboard'; }
export function replaceTimesheetLabel(value) {
  return String(value ?? '')
    .replace(/\bTime Entry\b/g, 'Timesheet')
    .replace(/\bProject Allocation(?:\s*(?:\/|&|and)\s*)Info\b/gi, 'Project Expense Upload');
}

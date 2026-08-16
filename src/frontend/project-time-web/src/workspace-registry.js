import { PROJECTPULSE_MODULES, canonicalModuleRoute } from './module-availability-registry.js';

const CATEGORY_BY_GROUP = Object.freeze({
  'Time Management': 'Core Operations',
  'Resource Management': 'Core Operations',
  'Project Operations': 'Core Operations',
  'Project Management': 'Project Management',
  'Project Delivery': 'Project Management',
  Approvals: 'Requests & Approvals',
  'Sales & Opportunities': 'Customer Programs',
  Customers: 'Customer Programs',
  Administration: 'Administration',
  Integrations: 'Platform & Integrations',
  'Platform Operations': 'Platform & Integrations',
  'AI & Automation': 'AI & Intelligence',
  'Reports & Workflow': 'Reports & Analytics',
  'Security & Audit': 'Security & Compliance',
  Security: 'Security & Compliance',
  Resources: 'Resources',
  'Help & Documentation': 'Help & Support'
});

const CATEGORY_DESCRIPTIONS = Object.freeze({
  'Core Operations': 'Essential day-to-day operational workspaces.',
  'Requests & Approvals': 'Submit, review, approve, and audit governed requests.',
  'Customer Programs': 'Customer-specific programs, documents, pipeline, and collaboration.',
  'Project Management': 'Plan, deliver, monitor, and close project work.',
  Administration: 'Manage people, roles, access, identity, and platform configuration.',
  'Platform & Integrations': 'Operate integrations, infrastructure, automation, and platform services.',
  'AI & Intelligence': 'Use governed intelligence, assistance, and automation.',
  'Reports & Analytics': 'Review operational, financial, delivery, and compliance insights.',
  'Security & Compliance': 'Review security, audit, policy, and governance evidence.',
  Resources: 'Manage resource qualifications, certifications, and readiness.',
  'Help & Support': 'Find guidance, diagnostics, and support resources.'
});

const WORKSPACE_METADATA_OVERRIDES = Object.freeze({
  '001': Object.freeze({ searchAliases: ['time entry', 'hours', 'weekly time'] }),
  '001A': Object.freeze({ searchAliases: ['engineer closeout', 'service request closeout', 'presales closeout', 'internal request closeout'] }),
  '002': Object.freeze({ searchAliases: ['approval', 'inbox', 'pending approvals'] }),
  '003': Object.freeze({ searchAliases: ['capacity', 'utilization', 'billable hours'] }),
  '006': Object.freeze({
    workspaceName: 'Customer Programs',
    category: 'Customer Programs',
    customerBrands: ['Toyota', 'Hyundai', 'Turion Space'],
    searchAliases: ['Toyota', 'Hyundai', 'Turion', 'Turion Space', 'customer pipeline', 'customer programs'],
    description: 'Unified workspace for Toyota, Hyundai, and Turion Space programs, documents, and collaboration.',
    featured: true
  }),
  '008': Object.freeze({ searchAliases: ['audit', 'history', 'change history'] }),
  '011': Object.freeze({ searchAliases: ['Celar AI', 'AI assistant', 'Ask Celar AI', 'workbench'] }),
  '013': Object.freeze({ searchAliases: ['system health', 'API diagnostics', 'troubleshooting'] }),
  '071': Object.freeze({ searchAliases: ['on call', 'on-call', 'schedule', 'rotation'] }),
  '072': Object.freeze({ searchAliases: ['OneAssist', 'PIN', 'routing directory'] })
});

function clean(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function categoryFor(module) {
  const override = WORKSPACE_METADATA_OVERRIDES[module.moduleNumber?.toUpperCase()]?.category;
  return override || CATEGORY_BY_GROUP[module.group] || module.group || 'Other Workspaces';
}

function iconKeyFor(module, category) {
  const number = String(module.moduleNumber || '').toUpperCase();
  if (number === '001') return 'clock';
  if (number === '001A') return 'briefcase';
  if (number === '002' || category === 'Requests & Approvals') return 'approval';
  if (number === '003' || category === 'Reports & Analytics') return 'chart';
  if (number === '006') return 'customers';
  if (number === '011' || category === 'AI & Intelligence') return 'spark';
  if (number === '013' || category === 'Platform & Integrations') return 'pulse';
  if (category === 'Administration') return 'admin';
  if (category === 'Security & Compliance') return 'shield';
  if (category === 'Project Management') return 'project';
  if (category === 'Help & Support') return 'help';
  return 'workspace';
}

export function toWorkspace(module) {
  const moduleNumber = clean(module?.moduleNumber).toUpperCase();
  const override = WORKSPACE_METADATA_OVERRIDES[moduleNumber] || {};
  const route = canonicalModuleRoute(module?.route);
  const category = categoryFor(module || {});
  const workspaceName = clean(override.workspaceName || module?.displayName || `Module ${moduleNumber}`);
  const searchAliases = Array.from(new Set([
    ...(Array.isArray(override.searchAliases) ? override.searchAliases : []),
    ...(Array.isArray(module?.searchAliases) ? module.searchAliases : []),
    ...(Array.isArray(override.customerBrands) ? override.customerBrands : [])
  ].map(clean).filter(Boolean)));

  return Object.freeze({
    moduleNumber,
    route,
    href: `#${route}`,
    workspaceName,
    description: clean(override.description || module?.description || `Open the ${workspaceName} workspace available to your current access scope.`),
    category,
    categoryDescription: CATEGORY_DESCRIPTIONS[category] || 'Authorized Pulse workspaces.',
    customerBrands: Object.freeze([...(override.customerBrands || module?.customerBrands || [])]),
    searchAliases: Object.freeze(searchAliases),
    iconKey: iconKeyFor(module || {}, category),
    featured: override.featured === true,
    sourceModule: module
  });
}

export const PULSE_WORKSPACES = Object.freeze(PROJECTPULSE_MODULES.map(toWorkspace));

export const WORKSPACE_BY_NUMBER = new Map(
  PULSE_WORKSPACES.map((workspace) => [workspace.moduleNumber, workspace])
);

export const WORKSPACE_BY_ROUTE = new Map(
  PULSE_WORKSPACES.map((workspace) => [workspace.route, workspace])
);

export function workspaceForRoute(route) {
  return WORKSPACE_BY_ROUTE.get(canonicalModuleRoute(route)) || null;
}

export function workspaceSearchText(workspace) {
  return [
    workspace.moduleNumber,
    workspace.workspaceName,
    workspace.description,
    workspace.route,
    workspace.category,
    ...workspace.searchAliases,
    ...workspace.customerBrands
  ].map(clean).join(' ').toLowerCase();
}

export function groupWorkspacesByCategory(workspaces) {
  const groups = new Map();
  for (const workspace of workspaces) {
    if (!groups.has(workspace.category)) groups.set(workspace.category, []);
    groups.get(workspace.category).push(workspace);
  }

  return Array.from(groups.entries())
    .map(([name, items]) => ({
      name,
      description: CATEGORY_DESCRIPTIONS[name] || 'Authorized Pulse workspaces.',
      workspaces: [...items].sort((left, right) => left.workspaceName.localeCompare(right.workspaceName))
    }))
    .sort((left, right) => left.name.localeCompare(right.name));
}

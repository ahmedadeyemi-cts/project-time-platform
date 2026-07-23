import { useEffect, useMemo, useState } from 'react';
import './project-closeout-center.css';

function getProjectPulseAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};
    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

function readProjectCloseoutHandoff() {
  try {
    const raw = window.sessionStorage.getItem('projectPulseProjectCloseoutHandoff');
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return parsed?.projectId || parsed?.projectCode ? parsed : null;
  } catch {
    return null;
  }
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders(), cache: 'no-store' });

  if (response.status === 403) {
    return { forbidden: true, message: `${path} is not available for this role.` };
  }

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...getProjectPulseAuthHeaders()
    },
    cache: 'no-store',
    body: JSON.stringify(payload)
  });
  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }
  return response.json();
}

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function downloadTextFile(filename, content, type = 'text/plain') {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function formatDateTime(value) {
  if (!value) return 'Not recorded';

  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) return String(value);

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(new Date(parsed));
}

function formatNumber(value, digits = 2) {
  const numericValue = Number(value ?? 0);
  return numericValue.toLocaleString(undefined, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits
  });
}

function formatCurrency(value) {
  const numericValue = Number(value ?? 0);
  return numericValue.toLocaleString(undefined, {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  });
}

function normalizeText(value) {
  return String(value ?? '').trim();
}

function normalizeStatus(value) {
  return String(value ?? '')
    .trim()
    .toLowerCase()
    .replaceAll('-', '_')
    .replaceAll(' ', '_');
}

function isGuid(value) {
  return /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i
    .test(normalizeText(value));
}

function titleCase(value) {
  return normalizeText(value)
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase()) || 'Not specified';
}

/* 040A_CLOSEOUT_SCROLL_CONTAINMENT_START */
function collectObjects(payload, collector = [], options = {}) {
  const depth = Number(options.depth ?? 0);
  const maxDepth = Number(options.maxDepth ?? 5);
  const maxObjects = Number(options.maxObjects ?? 1200);
  const seen = options.seen ?? new WeakSet();

  if (!payload || typeof payload !== 'object') return collector;
  if (collector.length >= maxObjects) return collector;
  if (depth > maxDepth) return collector;

  try {
    if (seen.has(payload)) return collector;
    seen.add(payload);
  } catch {
    // Primitive or non-weakset-compatible value; ignore.
  }

  if (Array.isArray(payload)) {
    payload.slice(0, maxObjects).forEach((item) => {
      if (collector.length < maxObjects) {
        collectObjects(item, collector, { depth: depth + 1, maxDepth, maxObjects, seen });
      }
    });
    return collector;
  }

  collector.push(payload);

  Object.values(payload).forEach((value) => {
    if (collector.length < maxObjects && value && typeof value === 'object') {
      collectObjects(value, collector, { depth: depth + 1, maxDepth, maxObjects, seen });
    }
  });

  return collector;
}

function looksLikeProject(item) {
  if (!item || typeof item !== 'object') return false;

  const explicitProjectIdentifier = getFirstValue(
    item,
    ['projectId', 'projectID', 'project_id', 'linkedProjectId', 'createdProjectId']
  );

  const hasProjectName = Boolean(item.projectName || item.name || item.title || item.displayName);
  const hasCustomer = Boolean(item.customerName || item.clientName || item.accountName || item.companyName);
  const hasProjectOwner = Boolean(
    item.projectManagerName ||
    item.pmName ||
    item.salesExecutiveName ||
    item.solutionArchitectName
  );

  const recordType = normalizeStatus(item.recordType ?? item.type ?? item.entityType ?? item.objectType);
  const isProjectRecordType = ['project', 'customer_project', 'project_summary', 'project_workspace'].includes(recordType);

  const taskOnlyRecord = Boolean(item.taskId || item.taskName || item.workTaskId || item.workTaskName) &&
    !hasProjectName &&
    !hasCustomer &&
    !hasProjectOwner &&
    !isProjectRecordType;

  if (taskOnlyRecord) return false;

  return isGuid(explicitProjectIdentifier)
    && (hasProjectName || hasCustomer || hasProjectOwner || isProjectRecordType);
}
/* 040A_CLOSEOUT_SCROLL_CONTAINMENT_END */

function getFirstValue(item, keys) {
  for (const key of keys) {
    if (item?.[key] !== undefined && item?.[key] !== null && String(item[key]).trim() !== '') {
      return item[key];
    }
  }

  return '';
}

function getNumericValue(item, keys) {
  for (const key of keys) {
    const value = Number(item?.[key]);
    if (Number.isFinite(value) && value !== 0) return value;
  }

  return 0;
}

function normalizeProjectCandidate(item, source) {
  const projectId = getFirstValue(
    item,
    ['projectId', 'projectID', 'project_id', 'linkedProjectId', 'createdProjectId']
  );
  if (!isGuid(projectId)) return null;

  const projectCode = getFirstValue(item, ['projectCode', 'projectNumber', 'projectNo', 'projectKey', 'code', 'number']);
  const projectName = getFirstValue(item, ['projectName', 'name', 'title', 'displayName']);
  const customerName = getFirstValue(item, ['customerName', 'clientName', 'accountName', 'companyName', 'customer']);
  const status = getFirstValue(item, ['status', 'projectStatus', 'workflowStatus', 'deliveryStatus']);
  const projectManagerName = getFirstValue(item, ['projectManagerName', 'pmName', 'managerName', 'projectManager']);
  const projectManagerEmail = getFirstValue(item, ['projectManagerEmail', 'pmEmail', 'managerEmail']);
  const salesExecutiveName = getFirstValue(item, ['salesExecutiveName', 'salesName', 'accountExecutiveName', 'salesExecutive']);
  const salesExecutiveEmail = getFirstValue(item, ['salesExecutiveEmail', 'salesEmail', 'accountExecutiveEmail']);
  const solutionArchitectName = getFirstValue(item, ['solutionArchitectName', 'architectName', 'saName', 'solutionArchitect']);
  const solutionArchitectEmail = getFirstValue(item, ['solutionArchitectEmail', 'architectEmail', 'saEmail']);
  const engineerName = getFirstValue(item, ['engineerName', 'resourceName', 'assignedEngineerName', 'primaryEngineerName']);
  const engineerEmail = getFirstValue(item, ['engineerEmail', 'resourceEmail', 'assignedEngineerEmail', 'primaryEngineerEmail']);
  const plannedHours = getNumericValue(item, ['plannedHours', 'assignedHours', 'budgetHours']);
  const usedHours = getNumericValue(item, ['usedHours', 'actualHours', 'approvedHours', 'hours']);
  const remainingHours = getNumericValue(item, ['remainingHours', 'hoursRemaining']);
  const plannedCost = getNumericValue(item, ['plannedCost', 'budgetAmount', 'estimatedCost', 'projectAmount']);
  const actualCost = getNumericValue(item, ['actualCost', 'usedCost', 'approvedCost', 'totalCost']);

  const key = normalizeText(projectId || projectCode || `${customerName}-${projectName}-${source}`);
  if (!key) return null;

  return {
    key,
    projectId,
    projectCode: normalizeText(projectCode || projectId || 'Unnumbered project'),
    projectName: normalizeText(projectName || 'Unnamed project'),
    customerName: normalizeText(customerName || 'Customer not linked'),
    status: normalizeText(status || 'Unknown'),
    projectManagerName: normalizeText(projectManagerName),
    projectManagerEmail: normalizeText(projectManagerEmail),
    salesExecutiveName: normalizeText(salesExecutiveName),
    salesExecutiveEmail: normalizeText(salesExecutiveEmail),
    solutionArchitectName: normalizeText(solutionArchitectName),
    solutionArchitectEmail: normalizeText(solutionArchitectEmail),
    engineerName: normalizeText(engineerName),
    engineerEmail: normalizeText(engineerEmail),
    plannedHours,
    usedHours,
    remainingHours,
    plannedCost,
    actualCost,
    source
  };
}

function extractProjectCandidates(payloads) {
  const projectsByKey = new Map();
  const maxCandidates = 80;

  payloads.forEach(({ payload, source }) => {
    collectObjects(payload, [], { maxDepth: 5, maxObjects: 1200 }).forEach((item) => {
      if (projectsByKey.size >= maxCandidates) return;
      if (!looksLikeProject(item)) return;

      const normalized = normalizeProjectCandidate(item, source);
      if (!normalized) return;

      const existing = projectsByKey.get(normalized.key);
      if (!existing) {
        projectsByKey.set(normalized.key, normalized);
        return;
      }

      projectsByKey.set(normalized.key, {
        ...existing,
        ...Object.fromEntries(
          Object.entries(normalized).filter(([, value]) => (
            value !== undefined &&
            value !== null &&
            String(value).trim() !== '' &&
            String(value).trim() !== '0'
          ))
        ),
        source: Array.from(new Set(`${existing.source}, ${normalized.source}`.split(',').map((value) => value.trim()).filter(Boolean))).join(', ')
      });
    });
  });

  return [...projectsByKey.values()]
    .filter((project) => project.projectCode !== 'Unnumbered project' || project.projectName !== 'Unnamed project')
    .sort((a, b) => a.customerName.localeCompare(b.customerName) || a.projectCode.localeCompare(b.projectCode))
    .slice(0, maxCandidates);
}

function countActionableApprovals(payload) {
  const actionableStatuses = new Set([
    'submitted',
    'pending',
    'pending_approval',
    'manager_pending',
    'awaiting_review',
    'awaiting_manager_review',
    'ready_for_temp_password',
    'ready_for_temporary_password',
    'temp_password_ready',
    'temporary_password_ready'
  ]);

  return collectObjects(payload).filter((item) => {
    const status = normalizeStatus(item.status ?? item.approvalStatus ?? item.workflowStatus ?? item.dayStatus);
    if (!status) return false;
    if (!actionableStatuses.has(status)) return false;

    const haystack = Object.keys(item).join(' ').toLowerCase();
    return (
      haystack.includes('workdate') ||
      haystack.includes('timesheet') ||
      haystack.includes('timeentry') ||
      haystack.includes('approval') ||
      haystack.includes('reset') ||
      haystack.includes('password')
    );
  }).length;
}

function countCertifyExceptions(payload) {
  const objects = collectObjects(payload);

  const exceptionLikeObjects = objects.filter((item) => {
    const status = normalizeStatus(item.status ?? item.exceptionStatus ?? item.workflowStatus);
    const haystack = [
      ...Object.keys(item || {}),
      item.message,
      item.reason,
      item.exceptionReason,
      item.category
    ].join(' ').toLowerCase();

    if (status && ['resolved', 'closed', 'cleared', 'approved', 'complete', 'completed'].includes(status)) {
      return false;
    }

    return (
      haystack.includes('exception') ||
      haystack.includes('blocked') ||
      haystack.includes('missing') ||
      haystack.includes('unmapped') ||
      haystack.includes('failed')
    );
  });

  const numericTotal = objects.reduce((total, item) => {
    return total +
      Number(item.openExceptionCount ?? 0) +
      Number(item.exceptionCount ?? 0) +
      Number(item.blockedCount ?? 0) +
      Number(item.unmappedCount ?? 0);
  }, 0);

  return Math.max(exceptionLikeObjects.length, numericTotal);
}

function countStagedExpenses(payload) {
  const objects = collectObjects(payload);

  const expenseLikeObjects = objects.filter((item) => {
    const keys = Object.keys(item || {}).join(' ').toLowerCase();
    return (
      keys.includes('expense') ||
      keys.includes('receipt') ||
      keys.includes('certify') ||
      keys.includes('reimburs')
    );
  });

  const numericTotal = objects.reduce((total, item) => {
    return total +
      Number(item.stagedExpenseCount ?? 0) +
      Number(item.expenseCount ?? 0) +
      Number(item.receiptCount ?? 0);
  }, 0);

  return Math.max(expenseLikeObjects.length, numericTotal);
}

function extractStakeholders(project) {
  const candidates = [
    {
      role: 'Project Manager',
      name: project.projectManagerName,
      email: project.projectManagerEmail
    },
    {
      role: 'Sales Executive',
      name: project.salesExecutiveName,
      email: project.salesExecutiveEmail
    },
    {
      role: 'Solution Architect',
      name: project.solutionArchitectName,
      email: project.solutionArchitectEmail
    },
    {
      role: 'Primary Engineer',
      name: project.engineerName,
      email: project.engineerEmail
    }
  ];

  return candidates.filter((stakeholder) => stakeholder.name || stakeholder.email);
}

function getCloseoutStage(project, counts) {
  const status = normalizeStatus(project?.status);
  const completedStatus = ['complete', 'completed', 'delivered', 'closed', 'ready_for_closeout', 'customer_accepted'].includes(status);
  const approvalsClear = counts.actionableApprovals === 0;
  const expensesClear = counts.certifyExceptions === 0;
  const stakeholdersPresent = counts.stakeholderCount > 0;

  if (completedStatus && approvalsClear && expensesClear && stakeholdersPresent) return 'Ready';
  if (!approvalsClear || !expensesClear) return 'Blocked';
  return 'Review';
}

function buildCloseoutChecks(project, counts) {
  const status = normalizeStatus(project?.status);
  const completedStatus = ['complete', 'completed', 'delivered', 'closed', 'ready_for_closeout', 'customer_accepted'].includes(status);

  return [
    {
      label: 'Project selected',
      status: project ? 'Ready' : 'Blocked',
      detail: project ? `${project.projectCode} • ${project.projectName}` : 'Select a project to evaluate closeout readiness.'
    },
    {
      label: 'Delivery status reviewed',
      status: completedStatus ? 'Ready' : 'Review',
      detail: completedStatus
        ? `Current status is ${titleCase(project.status)}.`
        : `Current status is ${titleCase(project?.status || 'Unknown')}; PM should confirm the project is actually complete before closeout.`
    },
    {
      label: 'Time approvals cleared',
      status: counts.actionableApprovals === 0 ? 'Ready' : 'Blocked',
      detail: counts.actionableApprovals === 0
        ? 'No actionable approval items are currently detected.'
        : `${counts.actionableApprovals} actionable approval item(s) must be cleared before closeout.`
    },
    {
      label: 'Certify expenses reviewed',
      status: counts.certifyExceptions === 0 ? 'Ready' : 'Blocked',
      detail: counts.certifyExceptions === 0
        ? `${counts.stagedExpenses} staged Certify expense item(s) detected with no blocking exception count.`
        : `${counts.certifyExceptions} Certify exception item(s) require review.`
    },
    {
      label: 'Billing readiness reviewed',
      status: counts.actionableApprovals === 0 && counts.certifyExceptions === 0 ? 'Ready' : 'Blocked',
      detail: 'Billing readiness should be reviewed before project closure so approved labor and staged expenses are not missed.'
    },
    {
      label: 'Project documents and handoff',
      status: 'Review',
      detail: 'Confirm SOW, GSD, implementation notes, acceptance evidence, and final customer handoff documents are attached or referenced.'
    },
    {
      label: 'Stakeholder notification prepared',
      status: counts.stakeholderCount > 0 ? 'Ready' : 'Review',
      detail: counts.stakeholderCount > 0
        ? `${counts.stakeholderCount} stakeholder contact(s) detected for notification preview.`
        : 'No stakeholder contacts were detected; PM should manually add engineers, Sales, Solution Architect, and Accounting/PTC as needed.'
    },
    {
      label: 'Lessons learned reminder',
      status: 'Ready',
      detail: 'Closeout notice includes a prompt to schedule a customer lessons-learned conversation.'
    }
  ];
}

function buildNotificationText(project, stakeholders) {
  const stakeholderLine = stakeholders.length
    ? stakeholders.map((stakeholder) => `${stakeholder.role}: ${stakeholder.name || stakeholder.email}`).join('\n')
    : 'Stakeholders: add Project Manager, Sales Executive, Solution Architect, assigned engineers, and Accounting/PTC as needed.';

  return `Subject: Project Closeout Notice - ${project.projectCode} / ${project.customerName}

Project ${project.projectCode} for ${project.customerName} has been marked as ready for closeout review in Project Health Dashboard.

Project: ${project.projectName}
Customer: ${project.customerName}
Current status: ${project.status || 'Not specified'}

Please review the closeout readiness items, confirm final time and expense activity has been reviewed, and set up any necessary lessons learned with the customer to understand how the project went and what we can improve.

Recommended notification audience:
${stakeholderLine}

Closeout guardrail:
This notice confirms closeout readiness only. It does not finalize accounting, send an invoice, or replace customer acceptance evidence.`;
}

function buildCloseoutCsv(project, checks, counts, stakeholders) {
  const rows = [
    ['Project Code', project.projectCode],
    ['Project Name', project.projectName],
    ['Customer', project.customerName],
    ['Status', project.status],
    ['Actionable Approval Count', counts.actionableApprovals],
    ['Staged Certify Expenses', counts.stagedExpenses],
    ['Certify Exceptions', counts.certifyExceptions],
    ['Stakeholder Count', counts.stakeholderCount],
    ['Generated At', new Date().toISOString()],
    [],
    ['Checklist Item', 'Status', 'Detail'],
    ...checks.map((check) => [check.label, check.status, check.detail]),
    [],
    ['Stakeholder Role', 'Name', 'Email'],
    ...stakeholders.map((stakeholder) => [stakeholder.role, stakeholder.name, stakeholder.email])
  ];

  return rows.map((row) => row.map(csvEscape).join(',')).join('\n');
}

export default function ProjectCloseoutCenter() {
  const [payload, setPayload] = useState({
    loading: true,
    error: null,
    data: null
  });
  const [selectedProjectKey, setSelectedProjectKey] = useState('');
  const [copiedStatus, setCopiedStatus] = useState('');
  const [closeoutHandoff] = useState(() => readProjectCloseoutHandoff());
  const [handoffStatus, setHandoffStatus] = useState('');
  const [lifecycle, setLifecycle] = useState({ loading: false, error: null, data: null });
  const [closeoutForm, setCloseoutForm] = useState({
    billingDisposition: '',
    deliveryComplete: false,
    customerAcceptanceComplete: false,
    timeExpenseComplete: false,
    billingComplete: false,
    reason: '',
    notes: ''
  });
  const [isSavingCloseout, setIsSavingCloseout] = useState(false);

  async function loadCloseoutData() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const [
        workspaceResult,
        intakeResult,
        customersResult,
        approvalsResult,
        approvalCountResult,
        certifyStagedResult,
        certifyExceptionsResult
      ] = await Promise.allSettled([
        fetchJson('/api/project-workspace/overview'),
        fetchJson('/api/project-intake/overview'),
        fetchJson('/api/customers/overview'),
        fetchJson('/api/manager/approvals'),
        fetchJson('/api/manager/approval-count'),
        fetchJson('/api/certify/expenses/staged'),
        fetchJson('/api/certify/exceptions')
      ]);

      const data = {
        workspace: workspaceResult.status === 'fulfilled' ? workspaceResult.value : null,
        intake: intakeResult.status === 'fulfilled' ? intakeResult.value : null,
        customers: customersResult.status === 'fulfilled' ? customersResult.value : null,
        approvals: approvalsResult.status === 'fulfilled' ? approvalsResult.value : null,
        approvalCount: approvalCountResult.status === 'fulfilled' ? approvalCountResult.value : null,
        certifyStaged: certifyStagedResult.status === 'fulfilled' ? certifyStagedResult.value : null,
        certifyExceptions: certifyExceptionsResult.status === 'fulfilled' ? certifyExceptionsResult.value : null,
        loadWarnings: [
          workspaceResult,
          intakeResult,
          customersResult,
          approvalsResult,
          approvalCountResult,
          certifyStagedResult,
          certifyExceptionsResult
        ]
          .filter((result) => result.status === 'rejected')
          .map((result) => result.reason instanceof Error ? result.reason.message : 'A closeout data source failed to load.')
      };

      setPayload({ loading: false, error: null, data });
    } catch (error) {
      setPayload({
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load project closeout data.',
        data: null
      });
    }
  }

  useEffect(() => {
    loadCloseoutData();
  }, []);

  /* 040A_CLOSEOUT_ROUTE_TOP_START */
  useEffect(() => {
    const resetTargets = [
      window,
      document.scrollingElement,
      document.documentElement,
      document.body,
      document.querySelector('.app-main'),
      document.querySelector('.workspace-content'),
      document.querySelector('.project-closeout-route-panel')
    ].filter(Boolean);

    window.requestAnimationFrame(() => {
      resetTargets.forEach((target) => {
        try {
          if (target === window) {
            target.scrollTo({ top: 0, left: 0, behavior: 'auto' });
          } else {
            target.scrollTop = 0;
            target.scrollLeft = 0;
          }
        } catch {
          // Ignore non-scrollable targets.
        }
      });
    });
  }, []);
  /* 040A_CLOSEOUT_ROUTE_TOP_END */

  /* 040B_CLOSEOUT_HEIGHT_GUARD_START */
  useEffect(() => {
    const routePanel = document.querySelector('.project-closeout-route-panel');
    const center = document.querySelector('.project-closeout-center');

    if (!routePanel || !center) return undefined;

    routePanel.style.overflowAnchor = 'none';
    center.style.overflowAnchor = 'none';

    const trimExcessiveSpacer = () => {
      const viewportHeight = window.innerHeight || 900;
      const centerHeight = center.getBoundingClientRect().height;
      const maxReasonableHeight = Math.max(1800, viewportHeight * 3.25);

      if (centerHeight > maxReasonableHeight) {
        center.style.maxHeight = `${maxReasonableHeight}px`;
        center.style.overflow = 'auto';
        center.style.overscrollBehavior = 'contain';
      }
    };

    trimExcessiveSpacer();
    const timer = window.setTimeout(trimExcessiveSpacer, 500);

    return () => window.clearTimeout(timer);
  }, [selectedProjectKey, payload.loading]);
  /* 040B_CLOSEOUT_HEIGHT_GUARD_END */

  const projects = useMemo(() => {
    if (!payload.data) return [];

    return extractProjectCandidates([
      { payload: payload.data.workspace, source: 'Project Workspace' },
      { payload: payload.data.intake, source: 'Project Intake' },
      { payload: payload.data.customers, source: 'Customer Directory' }
    ]);
  }, [payload.data]);

  useEffect(() => {
    if (selectedProjectKey || projects.length === 0) return;

    const handoffProject = closeoutHandoff
      ? projects.find((project) => (
          (closeoutHandoff.projectId
            && String(project.projectId).toLowerCase() === String(closeoutHandoff.projectId).toLowerCase())
          || (closeoutHandoff.projectCode
            && String(project.projectCode).toLowerCase() === String(closeoutHandoff.projectCode).toLowerCase())
        ))
      : null;

    if (handoffProject) {
      setSelectedProjectKey(handoffProject.key);
      setHandoffStatus(`Opened ${handoffProject.projectCode} from Module 055C for governed closeout review.`);
      try {
        window.sessionStorage.removeItem('projectPulseProjectCloseoutHandoff');
      } catch {
        // The selected project is already preserved in component state.
      }
      return;
    }

    if (closeoutHandoff) {
      setHandoffStatus('The Module 055C project could not be matched in the available closeout data. Select the project manually.');
      try {
        window.sessionStorage.removeItem('projectPulseProjectCloseoutHandoff');
      } catch {
        // Ignore browser storage cleanup failures.
      }
    }

    setSelectedProjectKey(projects[0].key);
  }, [closeoutHandoff, projects, selectedProjectKey]);

  const selectedProject = useMemo(() => {
    return projects.find((project) => project.key === selectedProjectKey) ?? projects[0] ?? null;
  }, [projects, selectedProjectKey]);

  const counts = useMemo(() => {
    if (!payload.data || !selectedProject) {
      return {
        actionableApprovals: 0,
        stagedExpenses: 0,
        certifyExceptions: 0,
        stakeholderCount: 0
      };
    }

    const actionableApprovals = Math.max(
      countActionableApprovals(payload.data.approvals),
      Number(payload.data.approvalCount?.actionableTotal ?? payload.data.approvalCount?.pendingActionableCount ?? 0)
    );

    const stagedExpenses = countStagedExpenses(payload.data.certifyStaged);
    const certifyExceptions = countCertifyExceptions(payload.data.certifyExceptions);
    const stakeholderCount = extractStakeholders(selectedProject).length;

    return {
      actionableApprovals,
      stagedExpenses,
      certifyExceptions,
      stakeholderCount
    };
  }, [payload.data, selectedProject]);

  const stakeholders = useMemo(() => selectedProject ? extractStakeholders(selectedProject) : [], [selectedProject]);

  useEffect(() => {
    let cancelled = false;

    if (!selectedProject?.projectId) {
      setLifecycle({ loading: false, error: null, data: null });
      return () => {
        cancelled = true;
      };
    }

    setLifecycle({ loading: true, error: null, data: null });
    fetchJson(`/api/work-lifecycle/projects/${selectedProject.projectId}`)
      .then((data) => {
        if (cancelled) return;
        if (data?.forbidden) {
          setLifecycle({ loading: false, error: data.message, data: null });
          return;
        }
        setLifecycle({ loading: false, error: null, data });
        const saved = data?.closeout;
        if (!saved) {
          setCloseoutForm({
            billingDisposition: '',
            deliveryComplete: false,
            customerAcceptanceComplete: false,
            timeExpenseComplete: false,
            billingComplete: false,
            reason: '',
            notes: ''
          });
          return;
        }

        setCloseoutForm({
          billingDisposition: saved.billingDisposition || '',
          deliveryComplete: Boolean(saved.deliveryComplete),
          customerAcceptanceComplete: Boolean(saved.customerAcceptanceComplete),
          timeExpenseComplete: Boolean(saved.timeExpenseComplete),
          billingComplete: Boolean(saved.billingComplete),
          reason: '',
          notes: saved.notes || ''
        });
      })
      .catch((error) => {
        if (!cancelled) {
          setLifecycle({
            loading: false,
            error: error instanceof Error ? error.message : 'Unable to load governed closeout state.',
            data: null
          });
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedProject?.projectId]);

  const checks = useMemo(() => selectedProject ? buildCloseoutChecks(selectedProject, counts) : [], [selectedProject, counts]);
  const closeoutStage = selectedProject ? getCloseoutStage(selectedProject, counts) : 'Blocked';
  const notificationText = selectedProject ? buildNotificationText(selectedProject, stakeholders) : '';

  function updateCloseoutField(field, value) {
    setCloseoutForm((current) => ({ ...current, [field]: value }));
  }

  async function saveGovernedCloseout(operation) {
    if (!selectedProject?.projectId) {
      setCopiedStatus('Select a project before saving closeout.');
      return;
    }

    if (closeoutForm.reason.trim().length < 5) {
      setCopiedStatus('Enter a specific audit reason before saving closeout.');
      return;
    }

    setIsSavingCloseout(true);
    setCopiedStatus('');

    try {
      const path = operation === 'reopen'
        ? `/api/work-lifecycle/projects/${selectedProject.projectId}/closeout/reopen`
        : `/api/work-lifecycle/projects/${selectedProject.projectId}/closeout/${operation}`;
      const body = operation === 'reopen'
        ? { reason: closeoutForm.reason.trim() }
        : { ...closeoutForm, reason: closeoutForm.reason.trim() };
      const result = await postJson(path, body);
      setCopiedStatus(result.message || 'Closeout state saved.');
      const refreshed = await fetchJson(`/api/work-lifecycle/projects/${selectedProject.projectId}`);
      setLifecycle({ loading: false, error: null, data: refreshed });
      setCloseoutForm((current) => ({ ...current, reason: '' }));
      await loadCloseoutData();
    } catch (error) {
      setCopiedStatus(error instanceof Error ? error.message : 'Unable to save closeout state.');
    } finally {
      setIsSavingCloseout(false);
    }
  }

  function exportCloseoutCsv() {
    if (!selectedProject) return;

    const csv = buildCloseoutCsv(selectedProject, checks, counts, stakeholders);
    downloadTextFile('phd-project-closeout-readiness.csv', csv, 'text/csv');
  }

  async function copyNotification() {
    if (!notificationText) return;

    try {
      await navigator.clipboard.writeText(notificationText);
      setCopiedStatus('Closeout notification copied to clipboard.');
    } catch {
      setCopiedStatus('Unable to copy automatically. Select the notification text and copy it manually.');
    }
  }

  const summaryCards = [
    {
      label: 'Closeout stage',
      value: closeoutStage,
      detail: closeoutStage === 'Ready'
        ? 'Project is ready for closeout review.'
        : closeoutStage === 'Blocked'
          ? 'Blocking items must be cleared.'
          : 'PM review is required.'
    },
    {
      label: 'Pending approvals',
      value: counts.actionableApprovals,
      detail: 'Actionable time/reset approval items'
    },
    {
      label: 'Certify exceptions',
      value: counts.certifyExceptions,
      detail: `${counts.stagedExpenses} staged expense item(s)`
    },
    {
      label: 'Stakeholders',
      value: counts.stakeholderCount,
      detail: 'Detected notification contacts'
    }
  ];

  return (
    <div className="project-closeout-center">
      <section className="project-closeout-hero">
        <div>
          <p className="eyebrow">Module 040</p>
          <h1>Project Closeout Center</h1>
          <p>
            Confirm project completion readiness before closeout. This view checks approval, billing, expense,
            documentation, stakeholder, and lessons-learned readiness before a project is treated as closed.
          </p>
        </div>
        <div className={`project-closeout-stage stage-${String(closeoutStage).toLowerCase()}`}>
          <span>Closeout stage</span>
          <strong>{closeoutStage}</strong>
        </div>
      </section>

      <section className="project-closeout-guardrail">
        <strong>Governed closeout</strong>
        <span>
          Assigned PMs can request closeout. PTCs and administrators complete or reopen it only after the server
          verifies tasks, time, billing readiness, invoice disposition, and required confirmations.
        </span>
      </section>

      {handoffStatus ? (
        <section className="project-closeout-handoff">
          <strong>Module 055C handoff</strong>
          <span>{handoffStatus}</span>
        </section>
      ) : null}

      <section className="project-closeout-toolbar">
        <label>
          Project selected for closeout review
          <select
            value={selectedProject?.key ?? ''}
            onChange={(event) => {
              setSelectedProjectKey(event.target.value);
              setCopiedStatus('');
            }}
            disabled={payload.loading || projects.length === 0}
          >
            {projects.length === 0 ? (
              <option value="">No projects loaded</option>
            ) : (
              projects.map((project) => (
                <option value={project.key} key={project.key}>
                  {project.projectCode} • {project.customerName} • {project.projectName}
                </option>
              ))
            )}
          </select>
        </label>

        <div className="project-closeout-actions">
          <button type="button" className="secondary-action" onClick={loadCloseoutData} disabled={payload.loading}>
            {payload.loading ? 'Refreshing...' : 'Refresh closeout data'}
          </button>
          <button type="button" className="primary-action" onClick={exportCloseoutCsv} disabled={!selectedProject}>
            Export closeout CSV
          </button>
        </div>
      </section>

      {payload.error ? (
        <div className="project-closeout-error">{payload.error}</div>
      ) : null}
      {lifecycle.error ? (
        <div className="project-closeout-error">{lifecycle.error}</div>
      ) : null}

      {payload.data?.loadWarnings?.length ? (
        <section className="project-closeout-warning">
          <strong>Some closeout data sources were unavailable.</strong>
          <ul>
            {payload.data.loadWarnings.map((warning) => (
              <li key={warning}>{warning}</li>
            ))}
          </ul>
        </section>
      ) : null}

      {selectedProject ? (
        <>
          <section className="project-closeout-summary-grid">
            {summaryCards.map((card) => (
              <article className="project-closeout-summary-card" key={card.label}>
                <span>{card.label}</span>
                <strong>{card.value}</strong>
                <small>{card.detail}</small>
              </article>
            ))}
          </section>

          <section className="project-closeout-detail-grid">
            <article className="project-closeout-card project-closeout-project-card">
              <div className="project-closeout-card-heading">
                <div>
                  <p className="eyebrow">Project profile</p>
                  <h2>{selectedProject.projectCode}</h2>
                </div>
                <span className={`project-closeout-pill stage-${String(closeoutStage).toLowerCase()}`}>{closeoutStage}</span>
              </div>

              <dl className="project-closeout-profile-list">
                <div>
                  <dt>Customer</dt>
                  <dd>{selectedProject.customerName}</dd>
                </div>
                <div>
                  <dt>Project</dt>
                  <dd>{selectedProject.projectName}</dd>
                </div>
                <div>
                  <dt>Status</dt>
                  <dd>{titleCase(selectedProject.status)}</dd>
                </div>
                <div>
                  <dt>Source</dt>
                  <dd>{selectedProject.source}</dd>
                </div>
                <div>
                  <dt>Planned / used hours</dt>
                  <dd>{formatNumber(selectedProject.plannedHours)} / {formatNumber(selectedProject.usedHours)}</dd>
                </div>
                <div>
                  <dt>Planned / actual cost</dt>
                  <dd>{formatCurrency(selectedProject.plannedCost)} / {formatCurrency(selectedProject.actualCost)}</dd>
                </div>
              </dl>
            </article>

            <article className="project-closeout-card">
              <div className="project-closeout-card-heading">
                <div>
                  <p className="eyebrow">Stakeholders</p>
                  <h2>Closeout notification audience</h2>
                </div>
                <span className="project-closeout-pill">{stakeholders.length} found</span>
              </div>

              {stakeholders.length === 0 ? (
                <div className="project-closeout-empty">
                  No stakeholder contacts were detected. Add PM, Sales Executive, Solution Architect, engineers,
                  and Accounting/PTC contacts before sending a closeout notification.
                </div>
              ) : (
                <div className="project-closeout-stakeholder-list">
                  {stakeholders.map((stakeholder) => (
                    <div className="project-closeout-stakeholder" key={`${stakeholder.role}-${stakeholder.email || stakeholder.name}`}>
                      <strong>{stakeholder.role}</strong>
                      <span>{stakeholder.name || 'Name not recorded'}</span>
                      <small>{stakeholder.email || 'Email not recorded'}</small>
                    </div>
                  ))}
                </div>
              )}
            </article>
          </section>

          <section className="project-closeout-card">
            <div className="project-closeout-card-heading">
              <div>
                <p className="eyebrow">Governed decision</p>
                <h2>Request or complete closeout</h2>
                <p className="muted">
                  Every saved decision includes the actor, reason, confirmations, blockers, and final billing disposition.
                </p>
              </div>
              <span className={`project-closeout-pill stage-${String(lifecycle.data?.closeout?.closeoutStatus || 'not-started').toLowerCase()}`}>
                {titleCase(lifecycle.data?.closeout?.closeoutStatus || 'Not started')}
              </span>
            </div>

            <div className="project-closeout-form-grid">
              <label>
                Final billing disposition
                <select
                  value={closeoutForm.billingDisposition}
                  onChange={(event) => updateCloseoutField('billingDisposition', event.target.value)}
                >
                  <option value="">Select disposition</option>
                  <option value="final_invoice_complete">Final invoice complete</option>
                  <option value="no_further_billing">No further billing</option>
                  <option value="non_billable">Non-billable project</option>
                  <option value="write_off_approved">Approved write-off</option>
                </select>
              </label>

              <label>
                Audit reason
                <input
                  value={closeoutForm.reason}
                  onChange={(event) => updateCloseoutField('reason', event.target.value)}
                  placeholder="Why closeout is being requested, completed, or reopened"
                />
              </label>

              <label className="project-closeout-form-wide">
                Closeout notes
                <textarea
                  value={closeoutForm.notes}
                  onChange={(event) => updateCloseoutField('notes', event.target.value)}
                  placeholder="Customer acceptance, billing disposition, exceptions, or handoff notes"
                />
              </label>
            </div>

            <div className="project-closeout-confirmations">
              {[
                ['deliveryComplete', 'Delivery complete'],
                ['customerAcceptanceComplete', 'Customer acceptance complete'],
                ['timeExpenseComplete', 'Final time and expense review complete'],
                ['billingComplete', 'Billing complete']
              ].map(([field, label]) => (
                <label key={field}>
                  <input
                    type="checkbox"
                    checked={closeoutForm[field]}
                    onChange={(event) => updateCloseoutField(field, event.target.checked)}
                  />
                  <span>{label}</span>
                </label>
              ))}
            </div>

            <div className="project-closeout-server-blockers">
              <strong>Server-validated blockers</strong>
              {(lifecycle.data?.closeoutBlockers || []).length === 0 ? (
                <p>No blockers remain. An authorized PTC or administrator can complete closeout.</p>
              ) : (
                <ul>
                  {(lifecycle.data?.closeoutBlockers || []).map((blocker) => <li key={blocker}>{blocker}</li>)}
                </ul>
              )}
            </div>

            <div className="project-closeout-actions">
              <button
                type="button"
                className="secondary-action"
                disabled={isSavingCloseout || !lifecycle.data?.capabilities?.canRequestCloseout}
                onClick={() => saveGovernedCloseout('request')}
              >
                {isSavingCloseout ? 'Saving…' : 'Request closeout'}
              </button>
              <button
                type="button"
                className="primary-action"
                disabled={isSavingCloseout || !lifecycle.data?.capabilities?.canCompleteCloseout || (lifecycle.data?.closeoutBlockers || []).length > 0}
                onClick={() => saveGovernedCloseout('complete')}
              >
                Complete project closeout
              </button>
              <button
                type="button"
                className="secondary-action"
                disabled={isSavingCloseout || !lifecycle.data?.capabilities?.canReopenProject || lifecycle.data?.closeout?.closeoutStatus !== 'closed'}
                onClick={() => saveGovernedCloseout('reopen')}
              >
                Reopen project
              </button>
            </div>
          </section>

          <section className="project-closeout-card">
            <div className="project-closeout-card-heading">
              <div>
                <p className="eyebrow">Readiness checklist</p>
                <h2>Closeout controls</h2>
              </div>
              <span className="project-closeout-pill">Generated {formatDateTime(new Date().toISOString())}</span>
            </div>

            <div className="project-closeout-checklist">
              {checks.map((check) => (
                <div className={`project-closeout-check status-${check.status.toLowerCase()}`} key={check.label}>
                  <span>{check.status}</span>
                  <div>
                    <strong>{check.label}</strong>
                    <small>{check.detail}</small>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="project-closeout-card project-closeout-notification-card">
            <div className="project-closeout-card-heading">
              <div>
                <p className="eyebrow">Notification readiness</p>
                <h2>Closeout notification preview</h2>
              </div>
              <button type="button" className="secondary-action" onClick={copyNotification}>
                Copy notification
              </button>
            </div>

            <pre>{notificationText}</pre>
            {copiedStatus ? <div className="project-closeout-copy-status">{copiedStatus}</div> : null}
          </section>
        </>
      ) : (
        <section className="project-closeout-card">
          <div className="project-closeout-empty">
            {payload.loading ? 'Loading project closeout data...' : 'No project candidates were found in the available project workspace, intake, or customer data.'}
          </div>
        </section>
      )}
    </div>
  );
}

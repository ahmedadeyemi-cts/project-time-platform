import { useEffect, useMemo, useState } from 'react';
import './closeout-email-automation-center.css';

const AUDIT_STORAGE_KEY = 'phdCloseoutEmailAuditLog';

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

function getCurrentSessionUser() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return 'Unknown PHD user';
    const session = JSON.parse(rawSession);
    return session?.username || session?.email || session?.displayName || 'Unknown PHD user';
  } catch {
    return 'Unknown PHD user';
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
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });

  if (response.status === 403) {
    return { forbidden: true, message: `${path} is not available for this role.` };
  }

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

/* 041A_CLOSEOUT_AUTOMATIC_SEND_START */
async function postJson(path, body) {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...getProjectPulseAuthHeaders()
    },
    body: JSON.stringify(body)
  });

  const raw = await response.text();
  let parsed = {};

  try {
    parsed = raw ? JSON.parse(raw) : {};
  } catch {
    parsed = { message: raw };
  }

  if (!response.ok && response.status !== 202) {
    throw new Error(parsed.message || `${path} returned HTTP ${response.status}`);
  }

  return parsed;
}
/* 041A_CLOSEOUT_AUTOMATIC_SEND_END */

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

function normalizeText(value) {
  return String(value ?? '').trim();
}

function normalizeKey(value) {
  return String(value ?? '').trim().toLowerCase();
}

function isEmail(value) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(value ?? '').trim());
}

function uniqueRecipients(recipients) {
  const byEmailOrRole = new Map();

  recipients.forEach((recipient) => {
    const email = normalizeText(recipient.email);
    const name = normalizeText(recipient.name);
    const role = normalizeText(recipient.role || 'Project Team');

    if (!email && !name) return;

    const key = email ? email.toLowerCase() : `${role.toLowerCase()}|${name.toLowerCase()}`;

    if (!byEmailOrRole.has(key)) {
      byEmailOrRole.set(key, { role, name, email });
      return;
    }

    const existing = byEmailOrRole.get(key);
    byEmailOrRole.set(key, {
      role: existing.role || role,
      name: existing.name || name,
      email: existing.email || email
    });
  });

  return [...byEmailOrRole.values()];
}

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
    // Ignore non-weakset-compatible values.
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

function looksLikeProject(item) {
  if (!item || typeof item !== 'object') return false;

  const explicitProjectIdentifier = Boolean(
    item.projectId ||
    item.projectCode ||
    item.projectNumber ||
    item.projectNo ||
    item.projectKey
  );

  const hasProjectName = Boolean(item.projectName || item.name || item.title || item.displayName);
  const hasCustomer = Boolean(item.customerName || item.clientName || item.accountName || item.companyName);
  const hasProjectOwner = Boolean(
    item.projectManagerName ||
    item.pmName ||
    item.salesExecutiveName ||
    item.accountExecutiveName ||
    item.salesRepName ||
    item.solutionArchitectName
  );

  return explicitProjectIdentifier && (hasProjectName || hasCustomer || hasProjectOwner);
}

function normalizeProjectCandidate(item, source) {
  const projectId = getFirstValue(item, ['projectId', 'id', 'projectID', 'project_id']);
  const projectCode = getFirstValue(item, ['projectCode', 'projectNumber', 'projectNo', 'projectKey', 'code', 'number']);
  const projectName = getFirstValue(item, ['projectName', 'name', 'title', 'displayName']);
  const customerName = getFirstValue(item, ['customerName', 'clientName', 'accountName', 'companyName', 'customer']);
  const status = getFirstValue(item, ['status', 'projectStatus', 'workflowStatus', 'deliveryStatus']);

  const projectManagerName = getFirstValue(item, ['projectManagerName', 'pmName', 'managerName', 'projectManager']);
  const projectManagerEmail = getFirstValue(item, ['projectManagerEmail', 'pmEmail', 'managerEmail']);

  const salesExecutiveName = getFirstValue(item, ['salesExecutiveName', 'salesName', 'accountExecutiveName', 'salesExecutive', 'salesRepName', 'accountManagerName']);
  const salesExecutiveEmail = getFirstValue(item, ['salesExecutiveEmail', 'salesEmail', 'accountExecutiveEmail', 'salesRepEmail', 'accountManagerEmail']);

  const solutionArchitectName = getFirstValue(item, ['solutionArchitectName', 'architectName', 'saName', 'solutionArchitect']);
  const solutionArchitectEmail = getFirstValue(item, ['solutionArchitectEmail', 'architectEmail', 'saEmail']);

  const engineerName = getFirstValue(item, ['engineerName', 'resourceName', 'assignedEngineerName', 'primaryEngineerName']);
  const engineerEmail = getFirstValue(item, ['engineerEmail', 'resourceEmail', 'assignedEngineerEmail', 'primaryEngineerEmail']);

  const plannedHours = getNumericValue(item, ['plannedHours', 'assignedHours', 'budgetHours']);
  const usedHours = getNumericValue(item, ['usedHours', 'actualHours', 'approvedHours', 'hours']);
  const remainingHours = getNumericValue(item, ['remainingHours', 'hoursRemaining']);

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
    source
  };
}

function extractProjectCandidates(payloads) {
  const projectsByKey = new Map();

  payloads.forEach(({ payload, source }) => {
    collectObjects(payload, [], { maxDepth: 5, maxObjects: 1200 }).forEach((item) => {
      if (!looksLikeProject(item)) return;

      const normalized = normalizeProjectCandidate(item, source);
      if (!normalized) return;

      const existing = projectsByKey.get(normalized.key);
      projectsByKey.set(normalized.key, existing ? { ...existing, ...normalized, source: `${existing.source}, ${normalized.source}` } : normalized);
    });
  });

  return [...projectsByKey.values()]
    .filter((project) => project.projectCode !== 'Unnumbered project' || project.projectName !== 'Unnamed project')
    .sort((a, b) => a.customerName.localeCompare(b.customerName) || a.projectCode.localeCompare(b.projectCode))
    .slice(0, 80);
}

function projectMatchesObject(project, item) {
  const projectIds = [
    project.projectId,
    project.projectCode,
    project.projectName
  ].filter(Boolean).map(normalizeKey);

  const itemValues = [
    item.projectId,
    item.projectCode,
    item.projectNumber,
    item.projectNo,
    item.projectKey,
    item.projectName,
    item.name,
    item.title
  ].filter(Boolean).map(normalizeKey);

  return projectIds.some((projectValue) => itemValues.includes(projectValue));
}

function recipientFromFields(item, role, nameKeys, emailKeys) {
  const name = getFirstValue(item, nameKeys);
  const email = getFirstValue(item, emailKeys);

  return {
    role,
    name: normalizeText(name),
    email: normalizeText(email)
  };
}

/* 041H_CLOSEOUT_USER_DIRECTORY_EMAIL_RESOLUTION_START */
function closeoutUserDirectoryAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};

    const session = JSON.parse(rawSession);
    const sessionToken = session?.sessionToken || session?.token || '';

    return sessionToken ? { 'X-ProjectPulse-Session': sessionToken } : {};
  } catch {
    return {};
  }
}

async function loadCloseoutUserDirectory() {
  const response = await fetch('/api/admin/user-admin/users', {
    headers: {
      ...closeoutUserDirectoryAuthHeaders(),
      Accept: 'application/json'
    }
  });

  if (!response.ok) {
    throw new Error(`/api/admin/user-admin/users returned HTTP ${response.status}`);
  }

  const data = await response.json();
  return Array.isArray(data?.users) ? data.users : [];
}

function normalizeDirectoryKey(value) {
  return normalizeText(value).toLowerCase().replace(/\s+/g, ' ').trim();
}

function buildUserDirectory(users) {
  const directory = new Map();

  (Array.isArray(users) ? users : []).forEach((user) => {
    const email = normalizeText(user.email);
    const displayName = normalizeText(user.displayName || user.name || user.fullName);
    const localUsername = normalizeText(user.localUsername);
    const sourceProvider = normalizeText(user.sourceProvider);

    const normalizedUser = {
      email,
      displayName,
      localUsername,
      sourceProvider,
      teamName: normalizeText(user.teamName),
      departmentName: normalizeText(user.departmentName),
      roleCodes: Array.isArray(user.roleCodes) ? user.roleCodes : [],
      roleNames: Array.isArray(user.roleNames) ? user.roleNames : []
    };

    [
      displayName,
      email,
      localUsername
    ].forEach((keyValue) => {
      const key = normalizeDirectoryKey(keyValue);
      if (!key) return;

      const existing = directory.get(key);

      if (!existing || (!isEmail(existing.email) && isEmail(normalizedUser.email))) {
        directory.set(key, normalizedUser);
      }
    });
  });

  return directory;
}

function resolveRecipientFromUserDirectory(recipient, userDirectory) {
  const currentEmail = normalizeText(recipient.email);
  const currentName = normalizeText(recipient.name);

  if (isEmail(currentEmail)) {
    return {
      ...recipient,
      name: currentName,
      email: currentEmail
    };
  }

  const candidateKeys = [
    currentName,
    currentEmail
  ].map(normalizeDirectoryKey).filter(Boolean);

  for (const key of candidateKeys) {
    const user = userDirectory.get(key);

    if (user && isEmail(user.email)) {
      return {
        ...recipient,
        name: currentName || user.displayName,
        email: user.email,
        userDirectoryResolved: true
      };
    }
  }

  return {
    ...recipient,
    name: currentName,
    email: currentEmail
  };
}

function enrichRecipientsWithUserDirectory(recipients, userDirectory) {
  return uniqueRecipients(
    recipients.map((recipient) => resolveRecipientFromUserDirectory(recipient, userDirectory))
  );
}
/* 041H_CLOSEOUT_USER_DIRECTORY_EMAIL_RESOLUTION_END */

/* 041K_CLOSEOUT_EDITABLE_RECIPIENTS_CC_START */
function closeoutRecipientOverrideKey(recipient) {
  const role = normalizeDirectoryKey(recipient.role || 'Project Team');
  const name = normalizeDirectoryKey(recipient.name);
  const email = normalizeDirectoryKey(recipient.email);

  return `${role}|${name || email || 'recipient'}`;
}

function applyCloseoutRecipientOverrides(recipients, overrides) {
  return uniqueRecipients(
    recipients.map((recipient) => {
      const key = closeoutRecipientOverrideKey(recipient);
      const overrideEmail = normalizeText(overrides[key]);

      if (!overrideEmail) {
        return recipient;
      }

      return {
        ...recipient,
        email: overrideEmail,
        manuallyEdited: true
      };
    })
  );
}

function parseCloseoutCcRecipients(value) {
  return uniqueRecipients(
    String(value ?? '')
      .split(/[\n,;]+/)
      .map((entry) => entry.trim())
      .filter(Boolean)
      .map((entry) => {
        const bracketMatch = entry.match(/^(.*?)<([^>]+)>$/);
        const email = bracketMatch ? bracketMatch[2].trim() : entry.trim();
        const name = bracketMatch ? bracketMatch[1].trim() : '';

        return {
          role: 'CC',
          name: normalizeText(name || email),
          email: normalizeText(email)
        };
      })
      .filter((recipient) => recipient.email)
  );
}

function closeoutRecipientNeedsReview(recipient) {
  const email = normalizeText(recipient.email);

  if (!email) return true;
  if (!isEmail(email)) return true;
  if (email.toLowerCase().endsWith('.local')) return true;

  return false;
}
/* 041K_CLOSEOUT_EDITABLE_RECIPIENTS_CC_END */

function extractProjectTeam(project, payloads) {
  const recipients = [
    recipientFromFields(project, 'Project Manager', ['projectManagerName'], ['projectManagerEmail']),
    recipientFromFields(project, 'Account Executive / Sales Rep', ['salesExecutiveName'], ['salesExecutiveEmail']),
    recipientFromFields(project, 'Solution Architect', ['solutionArchitectName'], ['solutionArchitectEmail']),
    recipientFromFields(project, 'Project Team', ['engineerName'], ['engineerEmail'])
  ];

  payloads.forEach((payload) => {
    collectObjects(payload, [], { maxDepth: 5, maxObjects: 1200 }).forEach((item) => {
      if (!projectMatchesObject(project, item)) return;

      recipients.push(recipientFromFields(item, 'Project Team', ['engineerName', 'resourceName', 'assignedEngineerName', 'primaryEngineerName', 'displayName', 'name'], ['engineerEmail', 'resourceEmail', 'assignedEngineerEmail', 'primaryEngineerEmail', 'email']));
      recipients.push(recipientFromFields(item, 'Project Manager', ['projectManagerName', 'pmName', 'managerName'], ['projectManagerEmail', 'pmEmail', 'managerEmail']));
      recipients.push(recipientFromFields(item, 'Account Executive / Sales Rep', ['salesExecutiveName', 'salesName', 'accountExecutiveName', 'salesRepName', 'accountManagerName'], ['salesExecutiveEmail', 'salesEmail', 'accountExecutiveEmail', 'salesRepEmail', 'accountManagerEmail']));
      recipients.push(recipientFromFields(item, 'Solution Architect', ['solutionArchitectName', 'architectName', 'saName'], ['solutionArchitectEmail', 'architectEmail', 'saEmail']));
    });
  });

  return uniqueRecipients(recipients);
}

function getPrimaryRecipient(recipients, roleIncludes) {
  const lowerRoleIncludes = roleIncludes.toLowerCase();
  return recipients.find((recipient) => recipient.role.toLowerCase().includes(lowerRoleIncludes)) ?? null;
}

function buildCloseoutEmail(project, recipients, auditFacts) {
  const pm = getPrimaryRecipient(recipients, 'project manager');
  const sales = recipients.filter((recipient) => {
    const role = recipient.role.toLowerCase();
    return role.includes('sales') || role.includes('account executive');
  });
  const solutionArchitects = recipients.filter((recipient) => recipient.role.toLowerCase().includes('architect'));
  const engineers = recipients.filter((recipient) => {
    const role = recipient.role.toLowerCase();
    return role.includes('project team') || role.includes('engineer');
  });
  const projectTeam = uniqueRecipients([...solutionArchitects, ...engineers]);

  const pmName = pm?.name || project.projectManagerName || 'Project Manager';
  const customerName = project.customerName || 'the customer';

  const subject = `Project Closeout and Lessons Learned Required - ${project.projectCode} / ${customerName}`;

  const body = `Hello Project Team,

Project ${project.projectCode} for ${customerName} has been closed out by the Project Manager in Project Health Dashboard. This automatic notice is being sent to the full project closeout team so everyone is aware the project is moving into closure.

Project: ${project.projectName}
Customer: ${customerName}
Current project status: ${project.status || 'Not specified'}

${pmName}, please schedule the required lessons learned session with ${customerName}. The session should review how the project went, what went well, what could be improved, and any follow-up items that should be captured before the project is archived.

Required closeout audience from intake and project assignment tracking:
- PM Assignment: ${pm?.name || project.projectManagerName || 'Not identified'}${pm?.email ? ` <${pm.email}>` : ''}
- Engineer(s) Assignment: ${engineers.length ? engineers.map((recipient) => `${recipient.name || recipient.email}${recipient.email ? ` <${recipient.email}>` : ''}`).join('; ') : 'Not identified'}
- Sales Executive / Account Executive: ${sales.length ? sales.map((recipient) => `${recipient.name || recipient.email}${recipient.email ? ` <${recipient.email}>` : ''}`).join('; ') : 'Not identified'}
- Solution Architect: ${solutionArchitects.length ? solutionArchitects.map((recipient) => `${recipient.name || recipient.email}${recipient.email ? ` <${recipient.email}>` : ''}`).join('; ') : 'Not identified'}
- Project Team Distribution: ${projectTeam.length ? projectTeam.map((recipient) => `${recipient.name || recipient.email}${recipient.email ? ` <${recipient.email}>` : ''}`).join('; ') : 'Not identified'}

Closeout readiness snapshot:
- Pending approvals: ${auditFacts.pendingApprovals}
- Certify exceptions: ${auditFacts.certifyExceptions}
- Staged Certify expenses: ${auditFacts.stagedExpenses}
- Generated by: ${auditFacts.triggeredBy}
- Generated at: ${auditFacts.generatedAt}

Please confirm any final customer acceptance evidence, project documentation, approved labor, and approved expense activity before final archive.

This automated closeout email does not finalize accounting, send an invoice, or replace the customer acceptance record.`;

  return { subject, body };
}

function readAuditLog() {
  try {
    const raw = window.localStorage.getItem(AUDIT_STORAGE_KEY);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function writeAuditLog(entries) {
  window.localStorage.setItem(AUDIT_STORAGE_KEY, JSON.stringify(entries.slice(0, 60)));
}

function createMailtoUrl(recipients, subject, body) {
  const to = recipients
    .filter((recipient) => isEmail(recipient.email))
    .map((recipient) => recipient.email)
    .join(',');

  return `mailto:${encodeURIComponent(to)}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
}

function buildAuditCsv(entries) {
  const rows = [
    ['Timestamp', 'Status', 'Project Code', 'Customer', 'PM', 'Recipient Count', 'CC Count', 'Triggered By', 'Subject'],
    ...entries.map((entry) => [
      entry.generatedAt,
      entry.status,
      entry.projectCode,
      entry.customerName,
      entry.projectManagerName,
      entry.recipientCount,
      entry.ccRecipientCount ?? 0,
      entry.triggeredBy,
      entry.subject
    ])
  ];

  return rows.map((row) => row.map(csvEscape).join(',')).join('\n');
}


/* 041K_CLOSEOUT_EDITABLE_RECIPIENTS_CC_STYLE_NOTE
   Styles are class-based and intentionally rely on the existing closeout-email card theme.
   If a stricter visual layout is needed later, these classes can be moved into the closeout stylesheet:
   closeout-email-inline-editor, closeout-email-review-text, closeout-email-cc-editor, closeout-email-cc-preview.
*/

export default function CloseoutEmailAutomationCenter() {
  const [payload, setPayload] = useState({ loading: true, error: null, data: null });
  const [userDirectoryPayload, setUserDirectoryPayload] = useState({ loading: true, users: [], error: null });
  const [recipientEmailOverrides, setRecipientEmailOverrides] = useState({});
  const [ccDraft, setCcDraft] = useState('');
  const [selectedProjectKey, setSelectedProjectKey] = useState('');
  const [status, setStatus] = useState('');
  const [auditEntries, setAuditEntries] = useState(() => readAuditLog());

  async function loadData() {
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
          .map((result) => result.reason instanceof Error ? result.reason.message : 'A closeout email data source failed to load.')
      };

      setPayload({ loading: false, error: null, data });
    } catch (error) {
      setPayload({
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load closeout email automation data.',
        data: null
      });
    }
  }

  useEffect(() => {
    loadData();
  }, []);

  const projects = useMemo(() => {
    if (!payload.data) return [];

    return extractProjectCandidates([
      { payload: payload.data.workspace, source: 'Project Workspace' },
      { payload: payload.data.intake, source: 'Project Intake' },
      { payload: payload.data.customers, source: 'Customer Directory' }
    ]);
  }, [payload.data]);

  useEffect(() => {
    if (!selectedProjectKey && projects.length > 0) {
      setSelectedProjectKey(projects[0].key);
    }
  }, [projects, selectedProjectKey]);

  useEffect(() => {
    let isMounted = true;

    loadCloseoutUserDirectory()
      .then((users) => {
        if (!isMounted) return;
        setUserDirectoryPayload({ loading: false, users, error: null });
      })
      .catch((error) => {
        if (!isMounted) return;
        setUserDirectoryPayload({
          loading: false,
          users: [],
          error: error instanceof Error ? error.message : 'Unable to load User Administration directory.'
        });
      });

    return () => {
      isMounted = false;
    };
  }, []);

  const selectedProject = useMemo(() => {
    return projects.find((project) => project.key === selectedProjectKey) ?? projects[0] ?? null;
  }, [projects, selectedProjectKey]);

  const userDirectory = useMemo(() => buildUserDirectory(userDirectoryPayload.users), [userDirectoryPayload.users]);

  const detectedRecipients = useMemo(() => {
    if (!payload.data || !selectedProject) return [];

    const rawRecipients = extractProjectTeam(selectedProject, [
      payload.data.workspace,
      payload.data.intake,
      payload.data.customers
    ]);

    return enrichRecipientsWithUserDirectory(rawRecipients, userDirectory);
  }, [payload.data, selectedProject, userDirectory]);

  const recipients = useMemo(() => {
    return applyCloseoutRecipientOverrides(detectedRecipients, recipientEmailOverrides);
  }, [detectedRecipients, recipientEmailOverrides]);

  const ccRecipients = useMemo(() => parseCloseoutCcRecipients(ccDraft), [ccDraft]);

  const emailReadyRecipients = useMemo(() => recipients.filter((recipient) => isEmail(recipient.email)), [recipients]);
  const ccEmailReadyRecipients = useMemo(() => ccRecipients.filter((recipient) => isEmail(recipient.email)), [ccRecipients]);

  const auditFacts = useMemo(() => {
    return {
      pendingApprovals: Number(payload.data?.approvalCount?.actionableTotal ?? payload.data?.approvalCount?.pendingActionableCount ?? 0),
      certifyExceptions: Number(payload.data?.certifyExceptions?.exceptionCount ?? payload.data?.certifyExceptions?.openExceptionCount ?? 0),
      stagedExpenses: Number(payload.data?.certifyStaged?.stagedExpenseCount ?? payload.data?.certifyStaged?.expenseCount ?? 0),
      triggeredBy: getCurrentSessionUser(),
      generatedAt: new Date().toISOString()
    };
  }, [payload.data]);

  const emailDraft = useMemo(() => {
    if (!selectedProject) return { subject: '', body: '' };
    return buildCloseoutEmail(selectedProject, recipients, auditFacts);
  }, [selectedProject, recipients, auditFacts]);

  const pmRecipient = getPrimaryRecipient(recipients, 'project manager');
  const salesRecipients = recipients.filter((recipient) => {
    const role = recipient.role.toLowerCase();
    return role.includes('sales') || role.includes('account executive');
  });
  const teamRecipients = recipients.filter((recipient) => {
    const role = recipient.role.toLowerCase();
    return role.includes('project team') || role.includes('engineer') || role.includes('architect');
  });

  const readinessChecks = [
    {
      label: 'Project Manager named',
      status: pmRecipient?.name ? 'Ready' : 'Review',
      detail: pmRecipient?.name ? `${pmRecipient.name} will be named in the lessons-learned reminder.` : 'PM name was not detected; update project data before sending.'
    },
    {
      label: 'Project Manager email',
      status: isEmail(pmRecipient?.email) ? 'Ready' : 'Review',
      detail: isEmail(pmRecipient?.email) ? pmRecipient.email : 'PM email was not detected.'
    },
    {
      label: 'Account Executive / Sales Rep',
      status: salesRecipients.some((recipient) => isEmail(recipient.email)) ? 'Ready' : 'Review',
      detail: salesRecipients.length ? `${salesRecipients.length} sales/account recipient(s) detected.` : 'No sales/account executive recipient detected.'
    },
    {
      label: 'Project team',
      status: teamRecipients.some((recipient) => isEmail(recipient.email)) ? 'Ready' : 'Review',
      detail: teamRecipients.length ? `${teamRecipients.length} project team recipient(s) detected.` : 'No project team recipient detected.'
    },
    {
      label: 'Intake team tracking',
      status: pmRecipient?.name && salesRecipients.length && teamRecipients.length ? 'Ready' : 'Review',
      detail: 'Closeout requires PM Assignment, Engineer(s) Assignment, Sales Executive name, and Solution Architect name to be tracked from intake through closure.'
    },
    {
      label: 'Automatic email send audit',
      status: 'Ready',
      detail: 'Automatic send captures project, PM assignment, engineer assignment, Sales Executive / Account Executive, Solution Architect, sender, timestamp, recipients, and message body.'
    }
  ];

  async function sendAutomaticCloseoutEmail() {
    if (!selectedProject) return;

    const projectManagerName = pmRecipient?.name || selectedProject.projectManagerName || 'Project Manager';

    setStatus(`Sending automatic closeout email for ${selectedProject.projectCode}. PM lessons-learned reminder is addressed to ${projectManagerName}.`);

    try {
      const response = await postJson('/api/project-closeout/email/send', {
        projectCode: selectedProject.projectCode,
        projectName: selectedProject.projectName,
        customerName: selectedProject.customerName,
        projectStatus: selectedProject.status,
        projectManagerName,
        projectManagerEmail: pmRecipient?.email || selectedProject.projectManagerEmail || '',
        recipients,
        ccRecipients,
        subject: emailDraft.subject,
        body: emailDraft.body,
        triggeredBy: getCurrentSessionUser()
      });

      const entry = {
        id: `closeout-email-${Date.now()}`,
        status: response.sent ? 'Automatic closeout email sent' : `Automatic closeout email ${response.status || 'queued'}`,
        projectCode: selectedProject.projectCode,
        projectName: selectedProject.projectName,
        customerName: selectedProject.customerName,
        projectManagerName,
        recipientCount: Number(response.recipientCount ?? emailReadyRecipients.length),
        ccRecipientCount: Number(response.ccRecipientCount ?? ccEmailReadyRecipients.length),
        recipients,
        ccRecipients,
        subject: emailDraft.subject,
        body: emailDraft.body,
        triggeredBy: getCurrentSessionUser(),
        generatedAt: new Date().toISOString(),
        backendStatus: response.status,
        backendMessage: response.message,
        backendAuditPath: response.auditPath,
        backendOutboxPath: response.outboxPath
      };

      const nextEntries = [entry, ...auditEntries].slice(0, 60);
      writeAuditLog(nextEntries);
      setAuditEntries(nextEntries);

      if (response.sent) {
        setStatus(`Automatic closeout email sent to ${entry.recipientCount} recipient(s) with ${entry.ccRecipientCount || 0} CC recipient(s). ${projectManagerName} was reminded to schedule lessons learned with ${selectedProject.customerName}.`);
      } else {
        setStatus(`Closeout email was not sent by SMTP/sendmail yet. Backend status: ${response.status}. Audit/outbox evidence was recorded.`);
      }
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Automatic closeout email send failed.');
    }
  }

  async function copyEmailBody() {
    try {
      await navigator.clipboard.writeText(`${emailDraft.subject}\n\n${emailDraft.body}`);
      setStatus('Closeout email subject and body copied to clipboard.');
    } catch {
      setStatus('Unable to copy automatically. Select the preview text and copy it manually.');
    }
  }

  function openEmailClient() {
    if (!selectedProject) return;
    window.location.href = createMailtoUrl(emailReadyRecipients, emailDraft.subject, emailDraft.body);
  }

  function exportAuditCsv() {
    downloadTextFile('project-closeout-email-audit.csv', buildAuditCsv(auditEntries), 'text/csv');
  }

  return (
    <div className="closeout-email-center">
      <section className="closeout-email-hero">
        <div>
          <p className="eyebrow">Module 041</p>
          <h1>Closeout Email Automation Center</h1>
          <p>
            Automatically send the project closeout email when the PM closes the project out. The message goes
            to the project team, Project Manager, Sales Executive / Account Executive, and Solution Architect,
            and it names the PM directly for the required lessons learned with the customer.
          </p>
        </div>
        <div className="closeout-email-hero-metric">
          <span>Email-ready recipients</span>
          <strong>{emailReadyRecipients.length}{ccEmailReadyRecipients.length ? ` + ${ccEmailReadyRecipients.length} CC` : ''}</strong>
        </div>
      </section>

      <section className="closeout-email-guardrail">
        <strong>Automation guardrail</strong>
        <span>
          This workflow sends the automatic PHD closeout email and records audit evidence. It does not finalize accounting,
          send an invoice, close the project in PSA/accounting, or replace customer acceptance evidence.
        </span>
      </section>

      <section className="closeout-email-toolbar">
        <label>
          Project selected for closeout email
          <select
            value={selectedProject?.key ?? ''}
            onChange={(event) => {
              setSelectedProjectKey(event.target.value);
              setStatus('');
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

        <div className="closeout-email-actions">
          <button type="button" className="secondary-action" onClick={loadData} disabled={payload.loading}>
            {payload.loading ? 'Refreshing...' : 'Refresh data'}
          </button>
          <button type="button" className="primary-action" onClick={sendAutomaticCloseoutEmail} disabled={!selectedProject || emailReadyRecipients.length === 0 || recipients.some(closeoutRecipientNeedsReview)}>
            PM closeout complete - send automatic email
          </button>
        </div>
      </section>

      {payload.error ? <div className="closeout-email-error">{payload.error}</div> : null}

      {payload.data?.loadWarnings?.length ? (
        <section className="closeout-email-warning">
          <strong>Some closeout email data sources were unavailable.</strong>
          <ul>
            {payload.data.loadWarnings.map((warning) => <li key={warning}>{warning}</li>)}
          </ul>
        </section>
      ) : null}

      {status ? <section className="closeout-email-status">{status}</section> : null}

      {selectedProject ? (
        <>
          <section className="closeout-email-summary-grid">
            <article>
              <span>Project</span>
              <strong>{selectedProject.projectCode}</strong>
              <small>{selectedProject.projectName}</small>
            </article>
            <article>
              <span>Customer</span>
              <strong>{selectedProject.customerName}</strong>
              <small>Lessons learned customer session required</small>
            </article>
            <article>
              <span>Project Manager</span>
              <strong>{pmRecipient?.name || selectedProject.projectManagerName || 'Not detected'}</strong>
              <small>{pmRecipient?.email || selectedProject.projectManagerEmail || 'PM email not detected'}</small>
            </article>
            <article>
              <span>Sales / Account</span>
              <strong>{salesRecipients.length}</strong>
              <small>Account Executive / Sales Rep recipient(s)</small>
            </article>
          </section>

          <section className="closeout-email-grid">
            <article className="closeout-email-card">
              <div className="closeout-email-card-heading">
                <div>
                  <p className="eyebrow">Recipient readiness</p>
                  <h2>Automatic email audience</h2>
                </div>
                <span className="closeout-email-pill">{emailReadyRecipients.length} email-ready</span>
              </div>

              <div className="closeout-email-recipient-list">
                {recipients.length === 0 ? (
                  <div className="closeout-email-empty">No recipients were detected for this project.</div>
                ) : (
                  recipients.map((recipient) => {
                    const overrideKey = closeoutRecipientOverrideKey(recipient);
                    const needsReview = closeoutRecipientNeedsReview(recipient);

                    return (
                      <div className={needsReview ? 'closeout-email-recipient review' : 'closeout-email-recipient ready'} key={`${recipient.role}-${recipient.email || recipient.name}`}>
                        <strong>{recipient.role}</strong>
                        <span>{recipient.name || 'Name not recorded'}</span>
                        <label className="closeout-email-inline-editor">
                          <small>Email</small>
                          <input
                            value={recipient.email || ''}
                            placeholder="name@example.com"
                            onChange={(event) => setRecipientEmailOverrides((current) => ({
                              ...current,
                              [overrideKey]: event.target.value
                            }))}
                          />
                        </label>
                        {needsReview ? <small className="closeout-email-review-text">Review or replace this email before sending.</small> : null}
                      </div>
                    );
                  })
                )}
              </div>

              <div className="closeout-email-cc-editor">
                <label>
                  <strong>Additional CC recipients</strong>
                  <span>Enter comma, semicolon, or line-separated emails. You can use Name &lt;email@domain.com&gt; format.</span>
                  <textarea
                    value={ccDraft}
                    placeholder="example.person@company.com\nAnother Person <another.person@company.com>"
                    onChange={(event) => setCcDraft(event.target.value)}
                    rows={4}
                  />
                </label>

                {ccRecipients.length ? (
                  <div className="closeout-email-cc-preview">
                    <strong>{ccEmailReadyRecipients.length} CC email-ready</strong>
                    {ccRecipients.map((recipient) => (
                      <small key={`${recipient.name}-${recipient.email}`}>{recipient.name} · {recipient.email}</small>
                    ))}
                  </div>
                ) : null}
              </div>
            </article>

            <article className="closeout-email-card">
              <div className="closeout-email-card-heading">
                <div>
                  <p className="eyebrow">Controls</p>
                  <h2>Email readiness checks</h2>
                </div>
              </div>

              <div className="closeout-email-check-list">
                {readinessChecks.map((check) => (
                  <div className={`closeout-email-check status-${check.status.toLowerCase()}`} key={check.label}>
                    <span>{check.status}</span>
                    <div>
                      <strong>{check.label}</strong>
                      <small>{check.detail}</small>
                    </div>
                  </div>
                ))}
              </div>
            </article>
          </section>

          <section className="closeout-email-card closeout-email-preview-card">
            <div className="closeout-email-card-heading">
              <div>
                <p className="eyebrow">Automatic message</p>
                <h2>Closeout email preview</h2>
              </div>
              <div className="closeout-email-actions">
                <button type="button" className="secondary-action" onClick={copyEmailBody}>
                  Copy email
                </button>
                <button type="button" className="secondary-action" onClick={openEmailClient} disabled={emailReadyRecipients.length === 0}>
                  Open email client
                </button>
              </div>
            </div>

            <div className="closeout-email-subject">
              <strong>Subject:</strong> {emailDraft.subject}
            </div>
            <pre>{emailDraft.body}</pre>
          </section>

          <section className="closeout-email-card">
            <div className="closeout-email-card-heading">
              <div>
                <p className="eyebrow">Audit evidence</p>
                <h2>Automatic closeout email send history</h2>
              </div>
              <button type="button" className="secondary-action" onClick={exportAuditCsv} disabled={auditEntries.length === 0}>
                Export email audit CSV
              </button>
            </div>

            <div className="closeout-email-audit-list">
              {auditEntries.length === 0 ? (
                <div className="closeout-email-empty">No automatic closeout email send events recorded in this browser yet.</div>
              ) : (
                auditEntries.map((entry) => (
                  <div className="closeout-email-audit-row" key={entry.id}>
                    <div>
                      <strong>{entry.projectCode} • {entry.customerName}</strong>
                      <span>{entry.status}</span>
                      <small>{entry.generatedAt} • Triggered by {entry.triggeredBy}</small>
                    </div>
                    <div>
                      <small>PM: {entry.projectManagerName}</small>
                      <small>Recipients: {entry.recipientCount}</small>
                    </div>
                  </div>
                ))
              )}
            </div>
          </section>
        </>
      ) : (
        <section className="closeout-email-card">
          <div className="closeout-email-empty">
            {payload.loading ? 'Loading closeout email automation data...' : 'No project candidates were found.'}
          </div>
        </section>
      )}
    </div>
  );
}

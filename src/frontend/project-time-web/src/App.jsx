
/*
 * 019M-V Global View-As User Experience Preview
 * Admin-only preview selector. Applies a read-only effective user to API calls
 * using X-ProjectPulse-View-As-User. Backend modules opt in by honoring the header.
 */
function installProjectPulseGlobalViewAsPreview() {
  if (typeof window === 'undefined' || typeof document === 'undefined') return;
  if (window.__projectPulseGlobalViewAsInstalled) return;

  window.__projectPulseGlobalViewAsInstalled = true;

  const STORAGE_KEY = 'projectPulseViewAsUser';
  const SESSION_KEY = 'projectPulseAuthSession';

  const readSession = () => {
    try {
      const raw = window.localStorage.getItem(SESSION_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw);
      if (!parsed?.sessionToken) return null;
      if (parsed?.expiresAt && Date.now() >= Date.parse(parsed.expiresAt)) return null;
      return parsed;
    } catch {
      return null;
    }
  };

  const readViewAs = () => {
    try {
      const raw = window.localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw);
      return parsed?.userId ? parsed : null;
    } catch {
      return null;
    }
  };

  const clearViewAs = () => {
    window.localStorage.removeItem(STORAGE_KEY);
    window.dispatchEvent(new CustomEvent('projectpulse:view-as-changed'));
    window.location.reload();
  };

  const writeViewAs = (user) => {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    window.dispatchEvent(new CustomEvent('projectpulse:view-as-changed', { detail: user }));
    window.location.reload();
  };

  const isApiUrl = (input) => {
    const raw = typeof input === 'string' ? input : input?.url;
    if (!raw) return false;

    try {
      const url = new URL(raw, window.location.origin);
      return url.pathname.startsWith('/api/');
    } catch {
      return String(raw).startsWith('/api/');
    }
  };

  const originalFetch = window.fetch.bind(window);
  window.__projectPulseOriginalFetch = window.__projectPulseOriginalFetch || originalFetch;

  window.fetch = async (input, init = {}) => {
    const viewAs = readViewAs();

    if (!viewAs?.userId || !isApiUrl(input)) {
      return originalFetch(input, init);
    }

    const method = String(init?.method || (typeof input !== 'string' ? input?.method : '') || 'GET').toUpperCase();
    const rawUrl = typeof input === 'string' ? input : input?.url || '';

    const isWrite = !['GET', 'HEAD', 'OPTIONS'].includes(method);
    const isAuthRoute = rawUrl.includes('/api/auth/');

    if (isWrite && !isAuthRoute) {
      return new Response(JSON.stringify({
        status: 'view_as_read_only',
        message: 'Write actions are disabled while using Administrator View-As preview. Exit preview to make changes.'
      }), {
        status: 403,
        headers: { 'Content-Type': 'application/json' }
      });
    }

    const headers = new Headers(init?.headers || (typeof input !== 'string' ? input?.headers || {} : {}));
    headers.set('X-ProjectPulse-View-As-User', viewAs.userId);

    return originalFetch(input, {
      ...init,
      headers
    });
  };

  const injectStyles = () => {
    if (document.getElementById('projectpulse-global-view-as-style')) return;

    const style = document.createElement('style');
    style.id = 'projectpulse-global-view-as-style';
    style.textContent = `
      #projectpulse-global-view-as {
        position: fixed;
        top: 0.65rem;
        right: 5.25rem;
        z-index: 9999;
        display: flex;
        align-items: center;
        gap: 0.45rem;
        max-width: min(560px, calc(100vw - 7rem));
        padding: 0.42rem 0.55rem;
        border: 1px solid rgba(14, 165, 233, 0.35);
        border-radius: 999px;
        background: rgba(15, 23, 42, 0.92);
        color: #fff;
        box-shadow: 0 12px 28px rgba(15, 23, 42, 0.22);
        backdrop-filter: blur(12px);
        font-size: 0.78rem;
      }

      #projectpulse-global-view-as.viewing {
        border-color: rgba(245, 158, 11, 0.62);
        background: rgba(120, 53, 15, 0.94);
      }

      #projectpulse-global-view-as label {
        display: flex;
        align-items: center;
        gap: 0.35rem;
        margin: 0;
        white-space: nowrap;
        font-weight: 800;
      }

      #projectpulse-global-view-as select {
        width: min(330px, 42vw);
        padding: 0.32rem 0.45rem;
        border: 1px solid rgba(255, 255, 255, 0.28);
        border-radius: 999px;
        background: rgba(255, 255, 255, 0.95);
        color: #111827;
        font-size: 0.78rem;
      }

      #projectpulse-global-view-as button {
        border: 0;
        border-radius: 999px;
        padding: 0.32rem 0.55rem;
        background: rgba(255, 255, 255, 0.16);
        color: #fff;
        cursor: pointer;
        font-weight: 900;
        font-size: 0.76rem;
      }

      #projectpulse-global-view-as .view-as-active-text {
        max-width: 240px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-weight: 800;
      }

      @media (max-width: 920px) {
        #projectpulse-global-view-as {
          position: static;
          margin: 0.5rem;
          max-width: calc(100vw - 1rem);
          border-radius: 0.8rem;
          justify-content: space-between;
        }

        #projectpulse-global-view-as label {
          flex: 1;
        }

        #projectpulse-global-view-as select {
          width: 100%;
        }
      }

      .project-workspace-center .admin-view-as-panel,
      .project-workspace-center .admin-view-as-banner {
        display: none !important;
      }
    `;

    document.head.appendChild(style);
  };

  const ensureContainer = () => {
    let container = document.getElementById('projectpulse-global-view-as');

    if (!container) {
      container = document.createElement('div');
      container.id = 'projectpulse-global-view-as';
      container.setAttribute('aria-live', 'polite');
      document.body.appendChild(container);
    }

    return container;
  };

  const renderLoading = () => {
    injectStyles();
    const container = ensureContainer();
    const active = readViewAs();
    container.className = active ? 'viewing' : '';
    container.innerHTML = `
      <label>
        View as
        <select disabled>
          <option>Loading users...</option>
        </select>
      </label>
    `;
  };

  const renderHidden = () => {
    const container = document.getElementById('projectpulse-global-view-as');
    if (container) container.remove();
  };

  const renderUsers = (users) => {
    injectStyles();

    const container = ensureContainer();
    const active = readViewAs();
    container.className = active ? 'viewing' : '';

    const options = ['<option value="">My Administrator view</option>']
      .concat(users.map((user) => {
        const label = `${user.displayName || user.email} — ${user.roleCodes || 'No role'}${user.teamOrDepartment ? ` — ${user.teamOrDepartment}` : ''}`;
        return `<option value="${user.userId}">${label.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')}</option>`;
      }))
      .join('');

    container.innerHTML = `
      <label>
        View as
        <select id="projectpulse-global-view-as-select">${options}</select>
      </label>
      ${active ? `<span class="view-as-active-text">Previewing: ${String(active.displayName || active.email || 'selected user').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')}</span><button id="projectpulse-global-view-as-exit" type="button">Exit</button>` : ''}
    `;

    const select = container.querySelector('#projectpulse-global-view-as-select');
    if (select) {
      select.value = active?.userId || '';
      select.addEventListener('change', (event) => {
        const selectedUser = users.find((user) => user.userId === event.target.value);
        if (!selectedUser) {
          clearViewAs();
          return;
        }

        writeViewAs(selectedUser);
      });
    }

    const exit = container.querySelector('#projectpulse-global-view-as-exit');
    if (exit) {
      exit.addEventListener('click', clearViewAs);
    }
  };

  const loadUsers = async () => {
    const session = readSession();

    if (!session?.sessionToken) {
      renderHidden();
      return;
    }

    renderLoading();

    try {
      const response = await window.__projectPulseOriginalFetch('/api/project-workspace/view-as/users', {
        headers: {
          'X-ProjectPulse-Session': session.sessionToken
        }
      });

      if (!response.ok) {
        renderHidden();
        return;
      }

      const body = await response.text();
      if (!body.trim()) {
        renderHidden();
        return;
      }

      const result = JSON.parse(body);
      const users = Array.isArray(result?.users) ? result.users : [];

      if (!users.length) {
        renderHidden();
        return;
      }

      renderUsers(users);
    } catch {
      renderHidden();
    }
  };

  const boot = () => {
    setTimeout(loadUsers, 500);
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }

  window.addEventListener('storage', (event) => {
    if (event.key === STORAGE_KEY || event.key === SESSION_KEY) {
      loadUsers();
    }
  });
}

installProjectPulseGlobalViewAsPreview();



import { useEffect, useMemo, useState, useRef } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import './timesheet.css';
import UserAdministrationPanel from './UserAdministrationPanel.jsx';
import YearlyUtilizationPanel from './YearlyUtilizationPanel.jsx';
import ProjectAllocationInfoPanel from './ProjectAllocationInfoPanel.jsx';
import ManagerTeamUtilizationPanel from './ManagerTeamUtilizationPanel.jsx';
import ManagerApprovalPanel from './ManagerApprovalPanel.jsx';
import LocalAdminPasswordResetApprovalsPanel from './LocalAdminPasswordResetApprovalsPanel.jsx';
import AuditHistoryPanel from './AuditHistoryPanel.jsx';
import ServiceControlCenter from './ServiceControlCenter.jsx';
import BackupDrCenter from './BackupDrCenter.jsx';
import ReplicationSyncStatusCenter from './ReplicationSyncStatusCenter.jsx';
import RestoreValidationCenter from './RestoreValidationCenter.jsx';
import BackupRetentionCenter from './BackupRetentionCenter.jsx';
import TimeComplianceCenter from './TimeComplianceCenter.jsx';
import ProjectIntakeCenter from './ProjectIntakeCenter.jsx';
import ProjectWorkspaceCenter from './ProjectWorkspaceCenter.jsx';

const workflowCards = [
  {
    title: 'Time Entry',
    description: 'Engineers enter weekly project-task, non-project, normal, and afterhours time before submission.',
    status: 'In progress'
  },
  {
    title: 'Manager Approval',
    description: 'Managers review submitted regular and OT hours by resource, task, and date.',
    status: 'Next phase'
  },
  {
    title: 'Project Approval',
    description: 'Project managers validate project and task allocation accuracy before accounting review.',
    status: 'Next phase'
  },
  {
    title: 'Accounting Reconciliation',
    description: 'Accounting reviews approved time and reconciles the period before lock.',
    status: 'Planned'
  },
  {
    title: 'Utilization',
    description: 'Monthly and quarterly summaries compare billable, PTO, and approved eligible time against target.',
    status: 'Policy loaded'
  },
  {
    title: 'Audit Trail',
    description: 'Role, approval, decline, reconciliation, and administrative actions are logged.',
    status: 'Planned'
  }
];

const timeTypes = [
  { key: 'normal', label: 'Normal' },
  { key: 'afterhours', label: 'Afterhours' }
];

const activitySourceOptions = [
  {
    key: 'nonProject',
    label: 'Non-project time',
    emptyTitle: 'No non-project time available.',
    emptyDescription: 'Non-project categories will appear here once they are loaded from the API.'
  },
  {
    key: 'openTasks',
    label: 'Regular tasks',
    emptyTitle: 'No regular tasks assigned.',
    emptyDescription: 'Assigned project tasks will appear here after a PM assigns work to the engineer.'
  },
  {
    key: 'requests',
    label: 'Requests / Service Requests',
    emptyTitle: 'No requests available.',
    emptyDescription: 'Service request activities will appear here after the request workflow is connected.'
  }
];


async function readApiErrorMessage(response, path) {
  const raw = await response.text();

  if (!raw) {
    return `${path} returned HTTP ${response.status}`;
  }

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}


async function fetchJson(path, sessionOverride = null) {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders(sessionOverride)
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

async function postJson(path, payload, sessionOverride = null) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders(sessionOverride) },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}



const PROJECT_PULSE_SESSION_WARNING_MS = 10 * 60 * 1000;


function getRouteFromHash() {
  const hash = window.location.hash || '#dashboard';
  return hash.replace('#', '') || 'dashboard';
}

function getStoredAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;

    const parsed = JSON.parse(rawSession);
    if (!parsed?.username || !parsed?.loginMethod || !parsed?.sessionToken || !parsed?.expiresAt) return null;

    if (Date.now() >= Date.parse(parsed.expiresAt)) {
      window.localStorage.removeItem('projectPulseAuthSession');
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders(sessionOverride = null) {
  const session = sessionOverride?.sessionToken ? sessionOverride : getStoredAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

function hasProjectPulseSession(session) {
  return Boolean(session?.sessionToken && session?.expiresAt && Date.now() < Date.parse(session.expiresAt));
}


function getInitialAuthSession() {
  return getStoredAuthSession();
}

function saveAuthSession(session) {
  window.localStorage.setItem('projectPulseAuthSession', JSON.stringify(session));
}

function clearAuthSession() {
  window.localStorage.removeItem('projectPulseAuthSession');
}



function getPreferenceStorageKey(session) {
  const username = session?.username || 'anonymous';
  return `projectPulseUserPreferences:${username.toLowerCase()}`;
}

function getDefaultUserPreferences(session) {
  return {
    theme: getInitialTheme(),
    profilePhotoDataUrl: '',
    awardsAndCertificates: '',
    displayNameOverride: '',
    titleOverride: '',
    username: session?.username || ''
  };
}

function getStoredUserPreferences(session) {
  try {
    const key = getPreferenceStorageKey(session);
    const stored = window.localStorage.getItem(key);
    if (!stored) return getDefaultUserPreferences(session);

    return {
      ...getDefaultUserPreferences(session),
      ...JSON.parse(stored),
      username: session?.username || ''
    };
  } catch {
    return getDefaultUserPreferences(session);
  }
}

function saveStoredUserPreferences(session, preferences) {
  const key = getPreferenceStorageKey(session);
  window.localStorage.setItem(key, JSON.stringify({
    ...preferences,
    username: session?.username || ''
  }));
}

function getInitials(value) {
  const cleanValue = String(value || 'Project Pulse').replace(/@.*/, '').replace(/[._-]/g, ' ');
  const parts = cleanValue.split(' ').filter(Boolean);

  if (parts.length === 0) return 'PP';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();

  return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
}


function getInitialTheme() {
  const savedTheme = window.localStorage.getItem('ptp-theme');
  if (savedTheme === 'dark' || savedTheme === 'light') return savedTheme;
  return "light";
}

function toIsoDate(date) {
  return date.toISOString().slice(0, 10);
}

function getSundayIso(date = new Date()) {
  const normalized = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  normalized.setUTCDate(normalized.getUTCDate() - normalized.getUTCDay());
  return toIsoDate(normalized);
}

function addDaysIso(isoDate, numberOfDays) {
  const [year, month, day] = isoDate.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  date.setUTCDate(date.getUTCDate() + numberOfDays);
  return toIsoDate(date);
}

function getEntryKey(rowId, date, type) {
  return `${rowId}|${date}|${type}`;
}

function formatNumber(value) {
  return Number(value || 0).toFixed(2);
}

function formatHoursValue(value) {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed.toFixed(2) : '0.00';
}

function taskToRow(task) {
  return {
    id: `project-task-${task.projectId}-${task.taskId}`,
    type: 'projectTask',
    state: 'Draft',
    activity: task.taskName,
    projectDescription: `${task.projectCode} • ${task.projectName}`,
    projectId: task.projectId,
    taskId: task.taskId,
    taskCode: task.taskCode,
    clientName: task.clientName,
    projectManagerName: task.projectManagerName
  };
}

function categoryToRow(category) {
  return {
    id: `non-project-${category.code}`,
    type: 'nonProject',
    state: 'Draft',
    activity: category.name,
    projectDescription: 'Non-project time',
    categoryCode: category.code,
    utilizationBucket: category.utilizationBucket,
    requiresApproval: category.requiresApproval
  };
}

function getVacationHolidayReminder(row) {
  if (!row) return null;
  const code = (row.categoryCode ?? '').toUpperCase();
  const activity = (row.activity ?? '').toUpperCase();
  if (!['VACATION', 'HOLIDAY'].includes(code) && !['VACATION', 'HOLIDAY'].includes(activity)) return null;
  return 'The code "Vacation" should be used for PTO. "Holiday" should be used only for company-paid holidays and your floating holiday. If you are taking PTO and a time entry deadline is approaching, your time should be submitted before you take your time off. All resources are required to submit 40 hours of time each week.';
}

function statusToLabel(status, totalHours = 0) {
  if (status === 'submitted') return `Submitted for manager approval (${formatNumber(totalHours)} hours).`;
  if (status === 'manager_declined') return 'Returned by manager for correction.';
  if (status === 'manager_approved') return 'Manager approved.';
  if (status === 'pm_approved') return 'Project manager approved.';
  if (status === 'accounting_ready') return 'Ready for accounting reconciliation.';
  if (status === 'reconciled') return 'Reconciled.';
  if (status === 'locked') return 'Locked.';
  return 'Draft';
}


const roleWorkspaceModules = [
  {
    route: 'project-workspace',
    href: '#project-workspace',
    title: 'Project Workspace & Engineering Documents',
    navLabel: 'Project Workspace',
    description: 'View project workspace readiness, engineering-visible documents, assignments, and timesheet-context artifacts.',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'project-intake',
    href: '#project-intake',
    title: 'Project Intake & Engineering Resource Requests',
    navLabel: 'Project Intake',
    description: 'Create and review project intake requests, engineering resource demand, capacity, and assignment readiness.',
    permissions: ['VIEW_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'time-compliance',
    href: '#time-compliance',
    title: 'Time Compliance & Notification Center',
    navLabel: 'Time Compliance',
    description: 'Dry-run preview for missing weekly time, manager and Project Team Coordinator copy visibility, month-end rules, holiday reminders, and notification history.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'VIEW_TIME_COMPLIANCE', 'VIEW_AUDIT_HISTORY']
  },
  {
    route: 'timesheet',
    href: '#timesheet',
    title: 'Time Entry',
    navLabel: 'Timesheet',
    description: 'Enter weekly and daily time by project, task, non-project work, and afterhours.',
    permissions: ['VIEW_TIME_ENTRY']
  },
  {
    route: 'manager-approval',
    href: '#manager-approval',
    title: 'Approval Inbox',
    navLabel: 'Approvals',
    description: 'Approve, reject, and review submitted time.',
    permissions: ['VIEW_APPROVAL_INBOX', 'APPROVE_TIME']
  },
  {
    route: 'utilization',
    href: '#utilization',
    title: 'Utilization',
    navLabel: 'Utilization',
    description: 'Review utilization progress, team utilization, and remaining hours.',
    permissions: ['VIEW_OWN_UTILIZATION', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION']
  },
  {
    route: 'holiday-admin',
    href: '#holiday-admin',
    title: 'Holiday Calendar',
    navLabel: 'Holidays',
    description: 'View or manage company holidays and calendar availability.',
    permissions: ['VIEW_HOLIDAYS', 'MANAGE_HOLIDAYS']
  },
  {
    route: 'project-allocation-info',
    href: '#project-allocation-info',
    title: 'Project Allocation and Info',
    navLabel: 'Project Info',
    description: 'View project allocations, engineer hours, and SOW/GSD documents.',
    permissions: ['VIEW_PROJECT_ALLOCATION_INFO', 'MANAGE_PROJECT_ALLOCATION_INFO', 'MANAGE_ALL']
  },
  {
    route: 'psa-modules',
    href: '#psa-modules',
    title: 'PSA Modules',
    navLabel: 'Modules',
    description: 'Review project intake, resource scheduling, expense management, and executive reporting workflows.',
    permissions: ['VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING']
  },
  {
    route: 'workflow',
    href: '#workflow',
    title: 'Workflow',
    navLabel: 'Workflow',
    description: 'Review project approval, account reconciliation, exports, and reporting workflow.',
    permissions: ['PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF']
  },
  {
    route: 'audit-history',
    href: '#audit-history',
    title: 'Audit / Security History',
    navLabel: 'Audit',
    description: 'Review login history, password reset history, Azure sync failures, notification failures, and system audit events.',
    permissions: ['VIEW_AUDIT_TRAIL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'user-admin',
    href: '#user-admin',
    title: 'User Administration',
    navLabel: 'User Admin',
    description: 'Manage users, local passwords, roles, teams, departments, and login access.',
    permissions: ['VIEW_USER_ADMIN', 'MANAGE_USER_ADMIN', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'azure-admin',
    href: '#azure-admin',
    title: 'Azure / Entra Admin',
    navLabel: 'Azure Admin',
    description: 'Configure Azure SSO, run user sync, and review imported directory users.',
    permissions: ['VIEW_AZURE_ADMIN', 'MANAGE_AZURE_SYNC', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'role-admin',
    href: '#role-admin',
    title: 'Role Administration',
    navLabel: 'Role Admin',
    description: 'Manage users, roles, access, and administrative configuration.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'service-control',
    href: '#service-control',
    title: 'Service Control Center',
    navLabel: 'Services',
    description: 'Monitor platform services, API status, recent logs, and controlled restart actions.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'backup-dr',
    href: '#backup-dr',
    title: 'Backup / DR Center',
    navLabel: 'Backup / DR',
    description: 'Create and validate full ProjectPulse backup bundles.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'restore-validation',
    href: '#restore-validation',
    title: 'Restore Validation',
    navLabel: 'Restore Validation',
    description: 'Validate selected backup restore points without restoring over production.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'backup-retention',
    href: '#backup-retention',
    title: 'Backup Retention',
    navLabel: 'Backup Retention',
    description: 'Review and safely remove older backup points with restore-point protection.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'replication-sync',
    href: '#replication-sync',
    title: 'Replication & Sync Status',
    navLabel: 'Replication / Sync',
    description: 'Review failover readiness, database role, service health, backup freshness, deployment state, and peer configuration.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  }
];

function normalizeRoute(hash) {
  const cleaned = (hash || window.location.hash || '#dashboard').replace('#', '').trim();
  return cleaned || 'dashboard';
}

function userPermissionSet(user) {
  return new Set(user?.permissions ?? []);
}

function userIsAdministrator(user) {
  const roles = user?.roles ?? [];
  const permissions = userPermissionSet(user);
  return permissions.has('MANAGE_ALL') || permissions.has('SYSTEM_ADMINISTRATION') || roles.some((role) => role.roleCode === 'ADMINISTRATOR');
}

function userHasAnyPermission(user, permissions) {
  if (!permissions || permissions.length === 0) return true;
  if (userIsAdministrator(user)) return true;

  const granted = userPermissionSet(user);
  return permissions.some((permission) => granted.has(permission));
}

function getVisibleRoleModules(user) {
  if (!user) return [];
  return roleWorkspaceModules.filter((module) => userHasAnyPermission(user, module.permissions));
}

function getRoleDisplayName(user) {
  const roles = user?.roles ?? [];
  if (roles.length === 0) return 'Workspace';
  if (roles.some((role) => role.roleCode === 'ADMINISTRATOR')) return 'Administrator';
  return roles.map((role) => role.roleName).join(' + ');
}

function getRoleNavigation(user) {
  const modules = getVisibleRoleModules(user);
  const routeMap = new Map();

  routeMap.set('dashboard', {
    route: 'dashboard',
    href: '#dashboard',
    label: 'Dashboard'
  });

  modules.forEach((module) => {
    if (!routeMap.has(module.route)) {
      routeMap.set(module.route, {
        route: module.route,
        href: module.href,
        label: module.navLabel
      });
    }
  });

  return [...routeMap.values()];
}


function userHasRoleText(user, fragments) {
  const normalizedFragments = fragments.map((fragment) => fragment.toLowerCase());
  return (user?.roles ?? []).some((role) => {
    const roleCode = String(role.roleCode ?? '').toLowerCase();
    const roleName = String(role.roleName ?? '').toLowerCase();
    return normalizedFragments.some((fragment) => roleCode.includes(fragment) || roleName.includes(fragment));
  });
}

function userHasPermissionCode(user, permissionCode) {
  return userPermissionSet(user).has(permissionCode);
}

function getPrimaryNavigationPriority(user) {
  return ['dashboard'];
}

function getNavigationGroup(item) {
  switch (item.route) {
    case 'timesheet':
    case 'manager-approval':
    case 'utilization':
    case 'holiday-admin':
      return 'Work Management';

    case 'project-allocation-info':
    case 'project-workspace':
      return 'Project Workspace';
    case 'project-intake':
      return 'Project Intake';
    case 'time-compliance':
      return 'Time Compliance';
    case 'psa-modules':
      return 'Project Operations';

    case 'audit-history':
      return 'Security & Audit';

    case 'user-admin':
    case 'azure-admin':
    case 'role-admin':
      return 'Admin & Identity';

    case 'service-control':
      return 'Platform Operations';

    case 'backup-dr':
    case 'restore-validation':
    case 'backup-retention':
    case 'replication-sync':
      return 'Resilience & Recovery';

    case 'workflow':
      return 'Reports & Workflow';

    default:
      return 'Other';
  }
}

function buildRoleNavigationModel(user, navigationItems) {
  const availableItems = navigationItems ?? [];
  const availableByRoute = new Map();

  availableItems.forEach((item) => {
    if (!availableByRoute.has(item.route)) {
      availableByRoute.set(item.route, item);
    }
  });

  const dashboardItem = availableByRoute.get('dashboard') ?? {
    route: 'dashboard',
    href: '#dashboard',
    label: 'Dashboard'
  };

  const primary = [dashboardItem];

  const groupOrder = [
    'Work Management',
    'Project Operations',
    'Security & Audit',
    'Admin & Identity',
    'Platform Operations',
    'Resilience & Recovery',
    'Reports & Workflow',
    'Other'
  ];

  const routeOrder = [
    'timesheet',
    'manager-approval',
    'utilization',
    'holiday-admin',
    'project-allocation-info',
    'time-compliance',
    'psa-modules',
    'audit-history',
    'user-admin',
    'azure-admin',
    'role-admin',
    'service-control',
    'backup-dr',
    'restore-validation',
    'backup-retention',
    'replication-sync',
    'workflow'
  ];

  const routeRank = new Map(routeOrder.map((route, index) => [route, index]));
  const groupMap = new Map(groupOrder.map((name) => [name, {
    name,
    expanded: true,
    items: []
  }]));

  [...availableByRoute.values()]
    .filter((item) => item.route !== 'dashboard')
    .sort((a, b) => {
      const aRank = routeRank.has(a.route) ? routeRank.get(a.route) : 999;
      const bRank = routeRank.has(b.route) ? routeRank.get(b.route) : 999;

      if (aRank !== bRank) return aRank - bRank;
      return String(a.label || '').localeCompare(String(b.label || ''));
    })
    .forEach((item) => {
      const groupName = getNavigationGroup(item);

      if (!groupMap.has(groupName)) {
        groupMap.set(groupName, {
          name: groupName,
          expanded: true,
          items: []
        });
      }

      groupMap.get(groupName).items.push(item);
    });

  const groups = [...groupMap.values()].filter((group) => group.items.length > 0);

  return {
    primary,
    groups
  };
}


function SignalLogo() {

  return (
    <div className="brand-lockup" aria-label="US Signal Project Pulse">
      <img className="brand-logo-image" src={usSignalLogoUrl} alt="US Signal" />
      <div>
        <strong>Project Pulse</strong>
        <small>Time • Approval • Utilization</small>
      </div>
    </div>
  );
}

function DataState({ loading, error, children }) {
  if (loading) return <span className="muted">Loading...</span>;
  if (error) return <span className="error-text">{error}</span>;
  return children;
}

export default function App() {
  const [theme, setTheme] = useState(getInitialTheme);
  const [authSession, setAuthSession] = useState(getInitialAuthSession);
  const [loginUsername, setLoginUsername] = useState('');
  const [loginPassword, setLoginPassword] = useState('');
  const [loginRoute, setLoginRoute] = useState(null);
  const [loginStatus, setLoginStatus] = useState('');
  const [isResolvingLogin, setIsResolvingLogin] = useState(false);
  const [passwordResetNotes, setPasswordResetNotes] = useState('');
  const [passwordResetStatus, setPasswordResetStatus] = useState('');
  const [forcedCurrentPassword, setForcedCurrentPassword] = useState('');
  const [forcedNewPassword, setForcedNewPassword] = useState('');
  const [forcedConfirmPassword, setForcedConfirmPassword] = useState('');
  const [forcedPasswordStatus, setForcedPasswordStatus] = useState('');
  const [isChangingForcedPassword, setIsChangingForcedPassword] = useState(false);
  const [isProfileMenuOpen, setIsProfileMenuOpen] = useState(false);
  const [isSideNavigationOpen, setIsSideNavigationOpen] = useState(true);
  const [expandedNavigationGroups, setExpandedNavigationGroups] = useState(() => ({
    'Time & Approvals': true,
    'Projects & Allocations': true,
    'Security & Audit': true
  }));
  const profileMenuRef = useRef(null);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [profileSettingsPanel, setProfileSettingsPanel] = useState('profile');
  const [userPreferences, setUserPreferences] = useState(() => getStoredUserPreferences(getInitialAuthSession()));
  const [profileDraft, setProfileDraft] = useState(() => getStoredUserPreferences(getInitialAuthSession()));
  const [profileSettingsStatus, setProfileSettingsStatus] = useState('');
  const [sessionWarning, setSessionWarning] = useState({ visible: false, remainingMs: 0 });
  const [activeRoute, setActiveRoute] = useState(() => normalizeRoute(window.location.hash));

  useEffect(() => {
    window.requestAnimationFrame(() => {
      window.scrollTo({ top: 0, behavior: 'auto' });
    });
  },

  {
    route: 'restore-validation',
    href: '#restore-validation',
    title: 'Restore Validation',
    navLabel: 'Restore Validation',
    description: 'Validate backup integrity, database dump readability, configuration archives, application snapshots, and DR runbook readiness.',
    status: 'Operational',
    group: 'System Operations',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },


  {
    route: 'backup-retention',
    href: '#backup-retention',
    title: 'Backup Retention',
    navLabel: 'Backup Retention',
    description: 'Review backup points and safely remove older backups with restore-point protection.',
    status: 'Operational',
    group: 'System Operations',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  } [activeRoute]); // project-pulse-route-scroll-reset
  const [selectedWeekStart, setSelectedWeekStart] = useState(getSundayIso);
  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });
  const [roleAdminUsers, setRoleAdminUsers] = useState({ loading: true, data: null, error: null });
  const [roleAdminRoles, setRoleAdminRoles] = useState({ loading: true, data: null, error: null });
  const [roleAdminStatus, setRoleAdminStatus] = useState('No role changes yet');
  const [securityContext, setSecurityContext] = useState({ loading: true, data: null, error: null });
  const [dbHealth, setDbHealth] = useState({ loading: true, data: null, error: null });
  const [schema, setSchema] = useState({ loading: true, data: null, error: null });
  const [currentUser, setCurrentUser] = useState({ loading: true, data: null, error: null });
  const [timesheet, setTimesheet] = useState({ loading: true, data: null, error: null });
  const [locationGroups, setLocationGroups] = useState({ loading: true, data: null, error: null });
  const [locations, setLocations] = useState({ loading: true, data: null, error: null });
  const [utilizationPolicies, setUtilizationPolicies] = useState({ loading: true, data: null, error: null });
  const [utilizationTargets, setUtilizationTargets] = useState({ loading: true, data: null, error: null });
  const [currentQuarterUtilization, setCurrentQuarterUtilization] = useState({ loading: true, data: null, error: null });
  const [approvalPendingCount, setApprovalPendingCount] = useState(0);
  const [azureAdminData, setAzureAdminData] = useState({
    loading: false,
    config: null,
    importSettings: null,
    users: [],
    runs: [],
    roles: [],
    error: null
  });
  const [azureConfigDraft, setAzureConfigDraft] = useState({
    tenantId: '',
    clientId: '',
    authorityUrl: '',
    redirectUri: '',
    graphScope: 'User.Read.All Directory.Read.All',
    syncEnabled: false,
    defaultRoleCode: 'ENGINEER',
    syncFrequencyHours: 24
  });
  const [azurePreviewUsers, setAzurePreviewUsers] = useState([]);
  const [selectedAzurePreviewKeys, setSelectedAzurePreviewKeys] = useState([]);
  const [azurePreviewLoading, setAzurePreviewLoading] = useState(false);
  const [azureTenantProfile, setAzureTenantProfile] = useState('onenecklab');
  const [customAzureTenantDomain, setCustomAzureTenantDomain] = useState('');
  const [customAzureTenantName, setCustomAzureTenantName] = useState('');
  const [azureImportFilters, setAzureImportFilters] = useState({
    searchText: '',
    domain: 'all',
    departmentName: '',
    includeExisting: false,
    onlyEnabled: true
  });
  const [azureDirectoryFilters, setAzureDirectoryFilters] = useState({
    searchText: '',
    sourceProvider: 'entra',
    syncState: 'all'
  });
  const [azureAdminStatus, setAzureAdminStatus] = useState('Ready');

  const [activeRows, setActiveRows] = useState([]);
  const [entries, setEntries] = useState({});
  const [selectedCell, setSelectedCell] = useState(null);
  const [aiSuggestionState, setAiSuggestionState] = useState({ loading: false, suggestion: '', provider: '', warning: '', error: '' });
  const [submissionStatus, setSubmissionStatus] = useState('Draft');
  const [saveStatus, setSaveStatus] = useState('Not saved yet');
  const [isSaving, setIsSaving] = useState(false);
  const [activitySource, setActivitySource] = useState('nonProject');
  const holidayYearOptions = useMemo(() => {
    const currentYear = new Date().getFullYear();
    return Array.from({ length: 11 }, (_, index) => String(currentYear + index));
  }, []);
  const [holidayUploadText, setHolidayUploadText] = useState('');
  const [holidayUploadStatus, setHolidayUploadStatus] = useState('No holiday upload yet');
  const [holidayUploadYear, setHolidayUploadYear] = useState(String(new Date().getFullYear()));
  const [hiddenRowsRevision, setHiddenRowsRevision] = useState(0);
  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });
  const [timesheetPreferences, setTimesheetPreferences] = useState({ loading: true, data: null, error: null });
  const [companyHolidays, setCompanyHolidays] = useState({ loading: true, data: null, error: null });
  const [remainingModules, setRemainingModules] = useState({ loading: true, data: null, error: null });


  useEffect(() => {
    if (!window.location.hash) {
      window.location.hash = '#dashboard';
    }

    function handleHashChange() {
      setActiveRoute(normalizeRoute(window.location.hash));
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    handleHashChange();
    window.addEventListener('hashchange', handleHashChange);

    return () => {
      window.removeEventListener('hashchange', handleHashChange);
    };
  }, []);


  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    window.localStorage.setItem('ptp-theme', theme);
  }, [theme]);


  useEffect(() => {
    document.body.dataset.projectPulseRoute = activeRoute || 'dashboard';

    return () => {
      delete document.body.dataset.projectPulseRoute;
    };
  }, [activeRoute]);




  useEffect(() => {
    let cancelled = false;

    async function loadCurrentUserAndQuarterUtilization() {
      if (!hasProjectPulseSession(authSession)) {
        setCurrentUser({ loading: false, data: null, error: null });
        setCurrentQuarterUtilization({ loading: false, data: null, error: null });
        return;
      }

      setCurrentUser((current) => ({ ...current, loading: true, error: null }));
      setCurrentQuarterUtilization((current) => ({ ...current, loading: true, error: null }));

      try {
        const [userResult, quarterResult] = await Promise.all([
          fetchJson('/api/security/me', authSession),
          fetchJson('/api/utilization/current-quarter', authSession)
        ]);

        if (!cancelled) {
          setCurrentUser({ loading: false, data: userResult, error: null });
          setCurrentQuarterUtilization({ loading: false, data: quarterResult, error: null });
        }
      } catch (error) {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : 'Unknown error';
          setCurrentUser((current) => ({ ...current, loading: false, error: message }));
          setCurrentQuarterUtilization((current) => ({ ...current, loading: false, error: message }));
        }
      }
    }

    loadCurrentUserAndQuarterUtilization();

    return () => {
      cancelled = true;
    };
  }, [authSession?.sessionToken]);


  useEffect(() => {
    let cancelled = false;

    async function loadSecurityContext() {
      if (!hasProjectPulseSession(authSession)) {
        setSecurityContext({ loading: false, data: null, error: null });
        return;
      }

      setSecurityContext((current) => ({ ...current, loading: true, error: null }));

      try {
        const result = await fetchJson('/api/security/me', authSession);
        if (!cancelled) setSecurityContext({ loading: false, data: result, error: null });
      } catch (error) {
        if (!cancelled) setSecurityContext({ loading: false, data: null, error: error instanceof Error ? error.message : 'Unable to load security context' });
      }
    }

    loadSecurityContext();

    return () => {
      cancelled = true;
    };
  }, [authSession?.sessionToken]);

  useEffect(() => {
    let cancelled = false;

    async function loadStatus() {
      if (!hasProjectPulseSession(authSession)) {
        setApiHealth({ loading: false, data: null, error: null });
        setDbHealth({ loading: false, data: null, error: null });
        setSchema({ loading: false, data: null, error: null });
        setTimesheet({ loading: false, data: null, error: null });
        setLocationGroups({ loading: false, data: null, error: null });
        setLocations({ loading: false, data: null, error: null });
        setUtilizationPolicies({ loading: false, data: null, error: null });
        setUtilizationTargets({ loading: false, data: null, error: null });
        setOpenTasks({ loading: false, data: null, error: null });
        setTimesheetPreferences({ loading: false, data: null, error: null });
        setCompanyHolidays({ loading: false, data: null, error: null });
        setRemainingModules({ loading: false, data: null, error: null });
        return;
      }

      setTimesheet({ loading: true, data: null, error: null });
      setTimesheetPreferences((current) => ({ ...current, loading: true, error: null }));
      setCompanyHolidays((current) => ({ ...current, loading: true, error: null }));
      setOpenTasks((current) => ({ ...current, loading: true, error: null }));
      setRemainingModules((current) => ({ ...current, loading: true, error: null }));

      try {
        const [
          healthResult,
          dbResult,
          schemaResult,
          timesheetResult,
          groupResult,
          locationsResult,
          policyResult,
          targetsResult,
          openTasksResult,
          preferencesResult,
          holidaysResult,
          projectIntakeResult,
          projectManagementResult,
          resourceCapacityResult,
          expenseSummaryResult,
          invoicingSummaryResult,
          executiveDashboardResult
        ] = await Promise.all([
          fetchJson('/health', authSession),
          fetchJson('/api/db-health', authSession),
          fetchJson('/api/schema/tables', authSession),
          fetchJson(`/api/timesheets/week?weekStart=${selectedWeekStart}`, authSession),
          fetchJson('/api/work-location-groups', authSession),
          fetchJson('/api/work-locations', authSession),
          fetchJson('/api/utilization/policies', authSession),
          fetchJson('/api/utilization/targets', authSession),
          fetchJson(`/api/assignments/available-tasks?weekStart=${selectedWeekStart}`, authSession),
          fetchJson('/api/users/timesheet-preferences', authSession),
          fetchJson(`/api/holidays?year=${selectedWeekStart.slice(0, 4)}`, authSession),
          fetchJson('/api/project-intake/summary', authSession),
          fetchJson('/api/project-management/summary', authSession),
          fetchJson(`/api/resource-scheduling/capacity?weekStart=${selectedWeekStart}`, authSession),
          fetchJson('/api/expenses/summary', authSession),
          fetchJson('/api/invoicing/summary', authSession),
          fetchJson('/api/reporting/executive-dashboard', authSession)
        ]);

        if (!cancelled) {
          setApiHealth({ loading: false, data: healthResult, error: null });
          setDbHealth({ loading: false, data: dbResult, error: null });
          setSchema({ loading: false, data: schemaResult, error: null });
          setTimesheet({ loading: false, data: timesheetResult, error: null });
          setLocationGroups({ loading: false, data: groupResult, error: null });
          setLocations({ loading: false, data: locationsResult, error: null });
          setUtilizationPolicies({ loading: false, data: policyResult, error: null });
          setUtilizationTargets({ loading: false, data: targetsResult, error: null });
          setOpenTasks({ loading: false, data: openTasksResult, error: null });
          setTimesheetPreferences({ loading: false, data: preferencesResult, error: null });
          setCompanyHolidays({ loading: false, data: holidaysResult, error: null });
          setRemainingModules({
            loading: false,
            error: null,
            data: {
              projectIntake: projectIntakeResult,
              projectManagement: projectManagementResult,
              resourceCapacity: resourceCapacityResult,
              expenses: expenseSummaryResult,
              invoicing: invoicingSummaryResult,
              executiveDashboard: executiveDashboardResult
            }
          });
        }
      } catch (error) {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : 'Unknown error';
          setApiHealth((current) => ({ ...current, loading: false, error: message }));
          setDbHealth((current) => ({ ...current, loading: false, error: message }));
          setSchema((current) => ({ ...current, loading: false, error: message }));
          setTimesheet((current) => ({ ...current, loading: false, error: message }));
          setLocationGroups((current) => ({ ...current, loading: false, error: message }));
          setLocations((current) => ({ ...current, loading: false, error: message }));
          setUtilizationPolicies((current) => ({ ...current, loading: false, error: message }));
          setUtilizationTargets((current) => ({ ...current, loading: false, error: message }));
          setOpenTasks((current) => ({ ...current, loading: false, error: message }));
          setTimesheetPreferences((current) => ({ ...current, loading: false, error: message }));
          setCompanyHolidays((current) => ({ ...current, loading: false, error: message }));
          setRemainingModules((current) => ({ ...current, loading: false, error: message }));
        }
      }
    }

    loadStatus();

    return () => {
      cancelled = true;
    };
  }, [selectedWeekStart, authSession?.sessionToken]);

  useEffect(() => {
    const categories = timesheet.data?.nonProjectCategories ?? [];
  const assignedOpenTasks = openTasks.data?.tasks ?? [];
    const savedEntries = timesheet.data?.entries ?? [];
    const hiddenRows = getHiddenRows(timesheet.data?.weekStart ?? selectedWeekStart);
    const daysForWeek = timesheet.data?.days ?? [];

    const userDefaultCodes = timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? [];
    const rowMap = new Map();

    // Hyper-personalized default rows: no global defaults. Rows are added only from the user's saved defaults,
    // saved time entries, manually selected tasks/categories, or auto-added holidays.
    categories
      .filter((category) => userDefaultCodes.includes(category.code) && !hiddenRows.has(`non-project-${category.code}`))
      .forEach((category) => rowMap.set(`non-project-${category.code}`, categoryToRow(category)));

    const holidaysForWeek = (companyHolidays.data?.holidays ?? []).filter((holiday) => daysForWeek.some((day) => day.date === holiday.holidayDate));
    const shouldAutoAddHolidays = timesheetPreferences.data?.autoAddHolidays !== false;
    const holidayCategory = categories.find((category) => category.code === 'HOLIDAY');
    if (shouldAutoAddHolidays && holidayCategory && holidaysForWeek.length > 0 && !hiddenRows.has('non-project-HOLIDAY')) {
      rowMap.set('non-project-HOLIDAY', categoryToRow(holidayCategory));
    }

    savedEntries.forEach((entry) => {
      if (entry.rowType === 'nonProject' && entry.categoryCode && !rowMap.has(`non-project-${entry.categoryCode}`)) {
        rowMap.set(`non-project-${entry.categoryCode}`, {
          id: `non-project-${entry.categoryCode}`,
          type: 'nonProject',
          state: 'Saved',
          activity: entry.categoryName ?? entry.categoryCode,
          projectDescription: 'Non-project time',
          categoryCode: entry.categoryCode
        });
      }

      if (entry.rowType === 'projectTask' && entry.projectId && entry.taskId) {
        const matchingTask = assignedOpenTasks.find((task) => task.projectId === entry.projectId && task.taskId === entry.taskId);
        const rowId = `project-task-${entry.projectId}-${entry.taskId}`;
        rowMap.set(rowId, matchingTask ? taskToRow(matchingTask) : {
          id: rowId,
          type: 'projectTask',
          state: 'Saved',
          activity: entry.taskName ?? entry.taskCode ?? 'Project task',
          projectDescription: entry.projectCode ? `${entry.projectCode} • ${entry.projectName ?? 'Project'}` : (entry.projectName ?? 'Project task'),
          projectId: entry.projectId,
          taskId: entry.taskId,
          taskCode: entry.taskCode ?? null,
          clientName: entry.clientName ?? null,
          projectManagerName: null
        });
      }
    });

    const entryMap = {};

    if (shouldAutoAddHolidays && holidayCategory && rowMap.has('non-project-HOLIDAY')) {
      holidaysForWeek.forEach((holiday) => {
        const key = getEntryKey('non-project-HOLIDAY', holiday.holidayDate, 'normal');
        const alreadySaved = savedEntries.some((entry) => entry.workDate === holiday.holidayDate && entry.categoryCode === 'HOLIDAY');
        if (!alreadySaved) {
          entryMap[key] = {
            hours: (holiday.autoPopulateHours ?? 8).toString(),
            comment: holiday.holidayName ?? 'Company holiday',
            workLocationGroupId: locationGroups.data?.groups?.[0]?.id ?? '',
            workLocationId: locations.data?.locations?.[0]?.id ?? '',
            savedStatus: 'draft'
          };
        }
      });
    }

    savedEntries.forEach((entry) => {
      let rowId = null;
      if (entry.rowType === 'nonProject' && entry.categoryCode) rowId = `non-project-${entry.categoryCode}`;
      if (entry.rowType === 'projectTask' && entry.projectId && entry.taskId) rowId = `project-task-${entry.projectId}-${entry.taskId}`;
      if (!rowId) return;

      entryMap[getEntryKey(rowId, entry.workDate, entry.timeType)] = {
        hours: entry.hours?.toString() ?? '',
        comment: entry.description ?? '',
        workLocationGroupId: entry.workLocationGroupId ?? '',
        workLocationId: entry.workLocationId ?? '',
        savedStatus: entry.status ?? 'draft'
      };
    });

    setActiveRows([...rowMap.values()]);
    setEntries(entryMap);
    setSelectedCell(null);

    const savedTotal = savedEntries.reduce((total, entry) => total + Number(entry.hours || 0), 0);
    const holidayDraftTotal = Object.values(entryMap).reduce((total, entry) => total + Number(entry.hours || 0), 0);
    setSubmissionStatus(statusToLabel(timesheet.data?.status, savedTotal || holidayDraftTotal));
    setSaveStatus(savedEntries.length > 0 ? `Loaded ${savedEntries.length} saved time entr${savedEntries.length === 1 ? 'y' : 'ies'}` : 'Not saved yet');
  }, [timesheet.data?.weekStart, timesheet.data?.timesheetId, timesheet.data?.status, timesheet.data?.entries?.length, openTasks.data?.count, timesheetPreferences.data?.defaultNonProjectCategoryCodes?.join(','), timesheetPreferences.data?.autoAddHolidays, companyHolidays.data?.count, hiddenRowsRevision]);

  const days = timesheet.data?.days ?? [];
  const categories = timesheet.data?.nonProjectCategories ?? [];
  const assignedOpenTasks = openTasks.data?.tasks ?? [];
  const activePolicy = utilizationPolicies.data?.policies?.[0];
  const selectedActivitySource = activitySourceOptions.find((option) => option.key === activitySource) ?? activitySourceOptions[0];
  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isAnyDayEditable = days.length === 0 || days.some((day) => isDayEditable(day.date));

  const databaseSummary = useMemo(() => {
    if (dbHealth.loading) return 'Checking database connection...';
    if (dbHealth.error) return dbHealth.error;
    return `${dbHealth.data?.status ?? 'unknown'} as ${dbHealth.data?.user ?? 'unknown user'}`;
  }, [dbHealth]);


  function getHiddenRowsKey(weekStart = selectedWeekStart) {
    return `projectPulseHiddenRows:${weekStart}`;
  }

  function getHiddenRows(weekStart = selectedWeekStart) {
    try {
      return new Set(JSON.parse(window.localStorage.getItem(getHiddenRowsKey(weekStart)) ?? '[]'));
    } catch {
      return new Set();
    }
  }

  function saveHiddenRows(hiddenRows, weekStart = selectedWeekStart) {
    window.localStorage.setItem(getHiddenRowsKey(weekStart), JSON.stringify([...hiddenRows]));
    setHiddenRowsRevision((value) => value + 1);
  }

  function hideRowForCurrentWeek(rowId) {
    const hiddenRows = getHiddenRows();
    hiddenRows.add(rowId);
    saveHiddenRows(hiddenRows);
  }

  function unhideRowForCurrentWeek(rowId) {
    const hiddenRows = getHiddenRows();
    if (hiddenRows.delete(rowId)) saveHiddenRows(hiddenRows);
  }



  useEffect(() => {
    const preferences = getStoredUserPreferences(authSession);
    setUserPreferences(preferences);
    setProfileDraft(preferences);

    if (preferences.theme && preferences.theme !== theme) {
      setTheme(preferences.theme);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authSession]);


  useEffect(() => {
    if (!isProfileMenuOpen) return;

    function closeProfileMenuOnOutsideClick(event) {
      if (profileMenuRef.current && !profileMenuRef.current.contains(event.target)) {
        setIsProfileMenuOpen(false);
      }
    }

    function closeProfileMenuOnEscape(event) {
      if (event.key === 'Escape') {
        setIsProfileMenuOpen(false);
      }
    }

    document.addEventListener('mousedown', closeProfileMenuOnOutsideClick);
    document.addEventListener('touchstart', closeProfileMenuOnOutsideClick);
    document.addEventListener('keydown', closeProfileMenuOnEscape);

    return () => {
      document.removeEventListener('mousedown', closeProfileMenuOnOutsideClick);
      document.removeEventListener('touchstart', closeProfileMenuOnOutsideClick);
      document.removeEventListener('keydown', closeProfileMenuOnEscape);
    };
  }, [isProfileMenuOpen]);



  useEffect(() => {
    if (!authSession?.expiresAt) {
      setSessionWarning({ visible: false, remainingMs: 0 });
      return;
    }

    function evaluateSessionExpiration() {
      const remainingMs = Date.parse(authSession.expiresAt) - Date.now();

      if (remainingMs <= 0) {
        void signOut();
        return;
      }

      if (remainingMs <= PROJECT_PULSE_SESSION_WARNING_MS) {
        setSessionWarning({ visible: true, remainingMs });
      } else {
        setSessionWarning({ visible: false, remainingMs });
      }
    }

    evaluateSessionExpiration();
    const timer = window.setInterval(evaluateSessionExpiration, 30000);

    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authSession?.expiresAt]);


  function openProfileSettings(panel = 'profile') {
    setProfileSettingsPanel(panel);
    setProfileDraft(userPreferences);
    setProfileSettingsStatus('');
    setIsSettingsOpen(true);
    setIsProfileMenuOpen(false);
  }

  function closeProfileSettings() {
    setIsSettingsOpen(false);
    setProfileSettingsStatus('');
  }

  function saveProfileSettings(event) {
    event.preventDefault();

    const savedPreferences = {
      ...profileDraft,
      theme: profileDraft.theme === 'dark' ? 'dark' : 'light'
    };

    try {
      saveStoredUserPreferences(authSession, savedPreferences);
      setUserPreferences(savedPreferences);
      setTheme(savedPreferences.theme);
      setProfileSettingsStatus('Profile settings saved.');
      setIsSettingsOpen(false);
    } catch {
      setProfileSettingsStatus('Unable to save profile settings. Try using a smaller profile picture.');
    }
  }

  function handleProfilePhotoUpload(event) {
    const file = event.target.files?.[0];
    if (!file) return;

    if (!file.type.startsWith('image/')) {
      setProfileSettingsStatus('Please select an image file.');
      return;
    }

    if (file.size > 2 * 1024 * 1024) {
      setProfileSettingsStatus('Please select an image smaller than 2 MB for now.');
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      setProfileDraft((current) => ({
        ...current,
        profilePhotoDataUrl: String(reader.result || '')
      }));
      setProfileSettingsStatus('Profile picture loaded. Select Save settings to keep it.');
    };
    reader.onerror = () => {
      setProfileSettingsStatus('Unable to read the selected profile picture.');
    };
    reader.readAsDataURL(file);
  }

  function removeProfilePhoto() {
    setProfileDraft((current) => ({
      ...current,
      profilePhotoDataUrl: ''
    }));
    setProfileSettingsStatus('Profile picture removed. Select Save settings to keep this change.');
  }


  async function resolveLoginRoute(event) {
    event.preventDefault();

    const username = loginUsername.trim().toLowerCase();
    if (!username) {
      setLoginStatus('Enter your US Signal email address or local administrator account.');
      return;
    }

    setIsResolvingLogin(true);
    setLoginStatus('Checking login route...');
    setPasswordResetStatus('');

    try {
      const result = await fetchJson(`/api/auth/login/route?username=${encodeURIComponent(username)}`);
      setLoginRoute(result);

      if (result.loginMethod === 'sso') {
        setLoginStatus('US Signal SSO route selected.');
      } else if (result.loginMethod === 'local') {
        setLoginStatus(result.status === 'route_resolved'
          ? 'Local administrator account found. Enter the local password.'
          : result.message ?? 'Local account route selected.');
      } else {
        setLoginStatus(result.message ?? 'Login route resolved.');
      }
    } catch (error) {
      setLoginRoute(null);
      setLoginStatus(error instanceof Error ? error.message : 'Unable to resolve login route.');
    } finally {
      setIsResolvingLogin(false);
    }
  }

  async function continueWithSsoPlaceholder() {
    const username = loginUsername.trim().toLowerCase();

    setLoginStatus('Creating US Signal SSO session...');

    try {
      const result = await postJson('/api/auth/sso/dev-login', { email: username });
      const session = {
        username: result.username,
        displayName: result.displayName,
        loginMethod: 'sso',
        provider: result.provider,
        sessionToken: result.sessionToken,
        expiresAt: result.expiresAt,
        signedInAt: new Date().toISOString()
      };

      saveAuthSession(session);
      setAuthSession(session);
      setSessionWarning({ visible: false, remainingMs: 0 });
      setLoginStatus('');
      window.location.hash = '#dashboard';
    } catch (error) {
      setLoginStatus(error instanceof Error ? error.message : 'Unable to create SSO session.');
    }
  }

  async function continueWithLocalShell(event) {
    event.preventDefault();

    const username = loginUsername.trim().toLowerCase();
    if (!username.endsWith('.local') && !username.endsWith('@ussignal.local')) {
      setLoginStatus('Local login is only available for .local administrator accounts.');
      return;
    }

    if (!loginPassword.trim()) {
      setLoginStatus('Enter the local administrator password.');
      return;
    }

    setLoginStatus('Signing in with local administrator credentials...');

    try {
      const result = await postJson('/api/auth/local/login', {
        username,
        password: loginPassword
      });

      const session = {
        username: result.username,
        displayName: result.displayName,
        loginMethod: 'local',
        provider: result.provider,
        sessionToken: result.sessionToken,
        expiresAt: result.expiresAt,
        mustChangePassword: result.mustChangePassword,
        signedInAt: new Date().toISOString()
      };

      saveAuthSession(session);
      setAuthSession(session);
      setSessionWarning({ visible: false, remainingMs: 0 });
      setLoginPassword('');
      setLoginStatus('');
      window.location.hash = '#dashboard';
    } catch (error) {
      setLoginStatus(error instanceof Error ? error.message : 'Local administrator login failed.');
    }
  }

  async function requestLocalPasswordReset() {
    const username = loginUsername.trim().toLowerCase();

    if (!username.endsWith('.local') && !username.endsWith('@ussignal.local')) {
      setPasswordResetStatus('Password reset approval is only available for .local administrator accounts.');
      return;
    }

    setPasswordResetStatus('Submitting password reset approval request...');

    try {
      const result = await postJson('/api/auth/password-reset/request', {
        username,
        notes: passwordResetNotes || 'Password reset requested from Project Pulse login screen.'
      });

      setPasswordResetStatus(result.message ?? 'Password reset request queued for approval.');
    } catch (error) {
      setPasswordResetStatus(error instanceof Error ? error.message : 'Unable to request password reset approval.');
    }
  }

  async function completeForcedPasswordChange(event) {
    event.preventDefault();

    if (!forcedCurrentPassword.trim()) {
      setForcedPasswordStatus('Enter your temporary password as the current password.');
      return;
    }

    if (!forcedNewPassword.trim()) {
      setForcedPasswordStatus('Enter a new password.');
      return;
    }

    if (forcedNewPassword !== forcedConfirmPassword) {
      setForcedPasswordStatus('The new password and confirmation do not match.');
      return;
    }

    if (forcedCurrentPassword === forcedNewPassword) {
      setForcedPasswordStatus('The new password must be different from the temporary password.');
      return;
    }

    setIsChangingForcedPassword(true);
    setForcedPasswordStatus('Updating password...');

    try {
      const result = await postJson('/api/auth/local/change-password', {
        currentPassword: forcedCurrentPassword,
        newPassword: forcedNewPassword
      });

      const updatedSession = {
        ...authSession,
        mustChangePassword: false
      };

      saveAuthSession(updatedSession);
      setAuthSession(updatedSession);
      setForcedCurrentPassword('');
      setForcedNewPassword('');
      setForcedConfirmPassword('');
      setForcedPasswordStatus(result.message ?? 'Password changed successfully.');
      window.location.hash = '#dashboard';
    } catch (error) {
      setForcedPasswordStatus(error instanceof Error ? error.message : 'Unable to change password.');
    } finally {
      setIsChangingForcedPassword(false);
    }
  }

  async function signOut() {
    try {
      await postJson('/api/auth/session/logout', {});
    } catch {
      // Continue local sign-out even if the backend session was already expired.
    }

    clearAuthSession();
    setAuthSession(null);
    setLoginUsername('');
    setLoginPassword('');
    setLoginRoute(null);
    setLoginStatus('');
    setPasswordResetNotes('');
    setPasswordResetStatus('');
    setSessionWarning({ visible: false, remainingMs: 0 });
    window.location.hash = '#dashboard';
  }

  async function extendCurrentSession() {
    setLoginStatus('');

    try {
      const result = await postJson('/api/auth/session/extend', {});
      const updatedSession = {
        ...authSession,
        expiresAt: result.expiresAt
      };

      saveAuthSession(updatedSession);
      setAuthSession(updatedSession);
      setSessionWarning({ visible: false, remainingMs: 0 });
    } catch {
      await signOut();
    }
  }



  function getAzurePreviewKey(user) {
    return String(
      user.previewKey ??
      user.entraObjectId ??
      user.id ??
      user.userId ??
      user.userPrincipalName ??
      user.mail ??
      user.email ??
      ''
    ).toLowerCase();
  }

  function normalizeAzurePreviewUser(user) {
    const email = user.email ?? user.mail ?? user.userPrincipalName ?? user.upn ?? '';
    return {
      ...user,
      previewKey: getAzurePreviewKey(user),
      email,
      displayName: user.displayName ?? user.name ?? email,
      jobTitle: user.jobTitle ?? '',
      departmentName: user.departmentName ?? user.department ?? '',
      officeLocation: user.officeLocation ?? user.location ?? '',
      managerEmail: user.managerEmail ?? '',
      accountEnabled: user.accountEnabled ?? user.enabled ?? true,
      importStatus: user.importStatus ?? user.status ?? 'Ready to import'
    };
  }

  function getImportedEmailSet() {
    return new Set((azureAdminData.users ?? []).map((user) => String(user.email ?? '').toLowerCase()));
  }

  function getFilteredAzurePreviewUsers() {
    const importedEmailSet = getImportedEmailSet();
    const searchText = azureImportFilters.searchText.trim().toLowerCase();
    const departmentName = azureImportFilters.departmentName.trim().toLowerCase();

    return azurePreviewUsers.filter((user) => {
      const email = String(user.email ?? '').toLowerCase();
      const displayName = String(user.displayName ?? '').toLowerCase();
      const jobTitle = String(user.jobTitle ?? '').toLowerCase();
      const department = String(user.departmentName ?? '').toLowerCase();
      const imported = isAzurePreviewUserAlreadyImported(user);

      if (!azureImportFilters.includeExisting && imported) return false;
      if (azureImportFilters.onlyEnabled && user.accountEnabled === false) return false;
      if (azureImportFilters.domain !== 'all' && !email.endsWith(`@${azureImportFilters.domain}`)) return false;
      if (departmentName && !department.includes(departmentName)) return false;
      if (searchText && !`${displayName} ${email} ${jobTitle} ${department}`.includes(searchText)) return false;

      return true;
    });
  }

  function getFilteredAzureDirectoryUsers() {
    const searchText = azureDirectoryFilters.searchText.trim().toLowerCase();

    return (azureAdminData.users ?? []).filter((user) => {
      const sourceProvider = String(user.sourceProvider ?? '').toUpperCase();
      const email = String(user.email ?? '').toLowerCase();
      const displayName = String(user.displayName ?? '').toLowerCase();
      const department = String(user.departmentName ?? '').toLowerCase();
      const roles = (user.roleNames ?? []).join(' ').toLowerCase();
      const hasSyncDate = Boolean(user.lastDirectorySyncAt);

      if (azureDirectoryFilters.sourceProvider === 'entra' && !['ENTRA_ID', 'ENTRA_ID_TEST'].includes(sourceProvider)) {
        return false;
      }

      if (
        azureDirectoryFilters.sourceProvider !== 'all' &&
        azureDirectoryFilters.sourceProvider !== 'entra' &&
        sourceProvider !== azureDirectoryFilters.sourceProvider
      ) {
        return false;
      }

      if (azureDirectoryFilters.syncState === 'synced' && !hasSyncDate) return false;
      if (azureDirectoryFilters.syncState === 'not_synced' && hasSyncDate) return false;
      if (azureDirectoryFilters.syncState === 'disabled' && user.loginEnabled && user.isActive) return false;
      if (searchText && !`${displayName} ${email} ${department} ${roles}`.includes(searchText)) return false;

      return true;
    });
  }

  function getAzureUserSyncLabel(user) {
    const sourceProvider = String(user.sourceProvider ?? '').toUpperCase();

    if (sourceProvider === 'ENTRA_ID') {
      return user.lastDirectorySyncAt ? 'Production Entra synced' : 'Production Entra imported / sync pending';
    }

    if (sourceProvider === 'ENTRA_ID_TEST') {
      return user.lastDirectorySyncAt ? 'Test Entra synced' : 'Test Entra imported / sync pending';
    }

    return 'Local app user';
  }

  function getAzureUserDomain(email) {
    const value = String(email ?? '');
    const index = value.lastIndexOf('@');
    return index >= 0 ? value.slice(index + 1).toLowerCase() : 'unknown';
  }

  function getStoredAzureTenantProfiles() {
    try {
      return JSON.parse(localStorage.getItem('projectPulseAzureTenantProfiles') ?? '{}');
    } catch {
      return {};
    }
  }

  function saveStoredAzureTenantProfile(profileKey, profilePayload) {
    const existing = getStoredAzureTenantProfiles();
    localStorage.setItem('projectPulseAzureTenantProfiles', JSON.stringify({
      ...existing,
      [profileKey]: profilePayload
    }));
  }

  function getAzureProfileDefaults(profile) {
    if (profile === 'ussignal') {
      return {
        tenantName: 'US Signal Production',
        tenantDomain: 'ussignal.com',
        sourceProvider: 'ENTRA_ID',
        environmentMode: 'production',
        config: {
          tenantId: '',
          clientId: '',
          authorityUrl: '',
          redirectUri: '',
          graphScope: 'User.Read.All Directory.Read.All',
          syncEnabled: false,
          defaultRoleCode: 'ENGINEER',
          syncFrequencyHours: 24
        },
        domainFilter: 'ussignal.com'
      };
    }

    if (profile === 'custom') {
      return {
        tenantName: customAzureTenantName || 'Create New',
        tenantDomain: customAzureTenantDomain || '',
        sourceProvider: 'ENTRA_ID_TEST',
        environmentMode: 'custom',
        config: {
          tenantId: '',
          clientId: '',
          authorityUrl: '',
          redirectUri: '',
          graphScope: 'User.Read.All Directory.Read.All',
          syncEnabled: false,
          defaultRoleCode: 'ENGINEER',
          syncFrequencyHours: 24
        },
        domainFilter: 'all'
      };
    }

    return {
      tenantName: 'OneNeck Lab',
      tenantDomain: 'onenecklab.com,onitdemo.com',
      sourceProvider: 'ENTRA_ID_TEST',
      environmentMode: 'test',
      config: {
        tenantId: '',
        clientId: '',
        authorityUrl: '',
        redirectUri: 'https://projectpulse-test.onenecklab.com/auth/callback',
        graphScope: 'User.Read.All Directory.Read.All',
        syncEnabled: true,
        defaultRoleCode: 'ENGINEER',
        syncFrequencyHours: 24
      },
      domainFilter: 'all'
    };
  }

  function getAzureTenantProfilePayload() {
    const defaults = getAzureProfileDefaults(azureTenantProfile);

    return {
      environmentMode: defaults.environmentMode,
      tenantDomain: defaults.tenantDomain,
      sourceProvider: defaults.sourceProvider,
      tenantName: defaults.tenantName
    };
  }

  function applyAzureTenantProfile(profile) {
    setAzureTenantProfile(profile);

    const stored = getStoredAzureTenantProfiles();
    const defaults = getAzureProfileDefaults(profile);
    const profilePayload = stored[profile] ?? defaults;

    setAzureConfigDraft((current) => ({
      ...current,
      ...defaults.config,
      ...(profilePayload.config ?? {})
    }));

    setCustomAzureTenantName(profilePayload.tenantName ?? defaults.tenantName ?? '');
    setCustomAzureTenantDomain(profilePayload.tenantDomain ?? defaults.tenantDomain ?? '');

    setAzureImportFilters((current) => ({
      ...current,
      domain: profilePayload.domainFilter ?? defaults.domainFilter ?? 'all'
    }));

    setAzurePreviewUsers([]);
    setSelectedAzurePreviewKeys([]);
  }

  function isAzurePreviewUserAlreadyImported(user) {
    const importedEmailSet = getImportedEmailSet();
    const email = String(user.email ?? '').toLowerCase();

    return Boolean(user.alreadyImported) || importedEmailSet.has(email);
  }

  function toggleAzurePreviewUser(previewKey) {
    setSelectedAzurePreviewKeys((current) => (
      current.includes(previewKey)
        ? current.filter((key) => key !== previewKey)
        : [...current, previewKey]
    ));
  }

  function toggleAllFilteredAzurePreviewUsers() {
    const filteredKeys = getFilteredAzurePreviewUsers()
      .filter((user) => !isAzurePreviewUserAlreadyImported(user))
      .map((user) => user.previewKey)
      .filter(Boolean);
    const allSelected = filteredKeys.length > 0 && filteredKeys.every((key) => selectedAzurePreviewKeys.includes(key));

    setSelectedAzurePreviewKeys(allSelected ? [] : Array.from(new Set([...selectedAzurePreviewKeys, ...filteredKeys])));
  }

  async function loadAzureAdmin() {
    setAzureAdminData((current) => ({ ...current, loading: true, error: null }));

    try {
      const [configResult, usersResult, runsResult, importSettingsResult, rolesResult] = await Promise.all([
        fetchJson('/api/admin/azure/config'),
        fetchJson('/api/admin/azure/users'),
        fetchJson('/api/admin/azure/sync/runs'),
        fetchJson('/api/admin/azure/import-settings').catch(() => null),
        fetchJson('/api/admin/roles').catch(() => ({ roles: [] }))
      ]);

      setAzureConfigDraft({
        tenantId: configResult.tenantId ?? '',
        clientId: configResult.clientId ?? '',
        authorityUrl: configResult.authorityUrl ?? '',
        redirectUri: configResult.redirectUri ?? '',
        graphScope: configResult.graphScope ?? 'User.Read.All Directory.Read.All',
        syncEnabled: Boolean(configResult.syncEnabled),
        defaultRoleCode: configResult.defaultRoleCode ?? 'ENGINEER',
        syncFrequencyHours: configResult.syncFrequencyHours ?? 24
      });

      const importSettings = importSettingsResult?.settings ?? importSettingsResult;
      const importTenantDomain = String(importSettings?.tenantDomain ?? '').toLowerCase();

      if (importTenantDomain.includes('ussignal.com')) {
        setAzureTenantProfile('ussignal');
      } else if (importTenantDomain.includes('onenecklab.com') || importTenantDomain.includes('onitdemo.com')) {
        setAzureTenantProfile('onenecklab');
      } else if (importTenantDomain) {
        setAzureTenantProfile('custom');
        setCustomAzureTenantDomain(importTenantDomain);
      }

      setAzureAdminData({
        loading: false,
        config: configResult,
        importSettings: importSettingsResult,
        users: usersResult.users ?? [],
        runs: runsResult.runs ?? [],
        roles: rolesResult.roles ?? [],
        error: null
      });
    } catch (error) {
      setAzureAdminData((current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load Azure Admin data.'
      }));
    }
  }

  useEffect(() => {
    if (activeRoute === 'azure-admin' && authSession?.sessionToken) {
      loadAzureAdmin();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeRoute, authSession?.sessionToken]);

  async function saveAzureConfig(event) {
    event.preventDefault();
    setAzureAdminStatus('Saving Azure/Entra configuration...');

    try {
      const result = await postJson('/api/admin/azure/config', azureConfigDraft);
      const tenantProfile = getAzureTenantProfilePayload();

      const settingsResult = await postJson('/api/admin/azure/import-settings', {
        ...tenantProfile,
        importSourceType: 'ALL_USERS',
        graphGroupId: '',
        graphFilter: '',
        defaultRoleCode: azureConfigDraft.defaultRoleCode || 'ENGINEER',
        disableMissingFromSource: false
      });

      saveStoredAzureTenantProfile(azureTenantProfile, {
        tenantName: azureTenantProfile === 'custom' ? customAzureTenantName : getAzureProfileDefaults(azureTenantProfile).tenantName,
        tenantDomain: tenantProfile.tenantDomain,
        sourceProvider: tenantProfile.sourceProvider,
        environmentMode: tenantProfile.environmentMode,
        domainFilter: azureImportFilters.domain,
        config: azureConfigDraft
      });

      setAzureAdminStatus(settingsResult.message ?? result.message ?? 'Azure/Entra configuration and import profile saved.');
      await loadAzureAdmin();
    } catch (error) {
      setAzureAdminStatus(error instanceof Error ? error.message : 'Unable to save Azure configuration.');
    }
  }

  async function previewAzureUsers() {
    setAzurePreviewLoading(true);
    setAzureAdminStatus('Loading Entra preview users...');

    try {
      const result = await postJson('/api/admin/azure/users/preview', {
        filters: azureImportFilters,
        domain: azureImportFilters.domain,
        searchText: azureImportFilters.searchText,
        departmentName: azureImportFilters.departmentName,
        includeExisting: azureImportFilters.includeExisting,
        onlyEnabled: azureImportFilters.onlyEnabled
      });

      const rawUsers = result.users ?? result.previewUsers ?? result.candidates ?? result.availableUsers ?? [];
      const previewUsers = rawUsers.map(normalizeAzurePreviewUser).filter((user) => user.previewKey && user.email);

      setAzurePreviewUsers(previewUsers);
      setSelectedAzurePreviewKeys([]);
      setAzureAdminStatus(result.message ?? `Preview loaded with ${previewUsers.length} user(s). Use filters to narrow the import list.`);
    } catch (error) {
      setAzureAdminStatus(error instanceof Error ? error.message : 'Unable to preview Entra users.');
    } finally {
      setAzurePreviewLoading(false);
    }
  }

  async function importSelectedAzureUsers() {
    const selectedUsers = azurePreviewUsers.filter((user) => (
      selectedAzurePreviewKeys.includes(user.previewKey) &&
      !isAzurePreviewUserAlreadyImported(user)
    ));

    if (selectedUsers.length === 0) {
      setAzureAdminStatus('Select at least one preview user before importing.');
      return;
    }

    setAzureAdminStatus(`Importing ${selectedUsers.length} selected Entra user(s)...`);

    try {
      const payload = {
        users: selectedUsers,
        selectedUsers,
        emails: selectedUsers.map((user) => user.email).filter(Boolean),
        selectedEmails: selectedUsers.map((user) => user.email).filter(Boolean),
        userIds: selectedUsers.map((user) => user.id ?? user.userId ?? user.entraObjectId ?? user.email).filter(Boolean),
        selectedUserIds: selectedUsers.map((user) => user.id ?? user.userId ?? user.entraObjectId ?? user.email).filter(Boolean),
        entraObjectIds: selectedUsers.map((user) => user.entraObjectId ?? user.id).filter(Boolean),
        selectedEntraObjectIds: selectedUsers.map((user) => user.entraObjectId ?? user.id).filter(Boolean)
      };

      const result = await postJson('/api/admin/azure/users/import-selected', payload);
      setAzureAdminStatus(result.message ?? `Imported ${selectedUsers.length} selected user(s).`);
      setSelectedAzurePreviewKeys([]);
      await loadAzureAdmin();
    } catch (error) {
      setAzureAdminStatus(error instanceof Error ? error.message : 'Unable to import selected Entra users.');
    }
  }

  async function runAzureFoundationSync() {
    setAzureAdminStatus('Running Azure/Entra Sync Now...');

    try {
      const result = await postJson('/api/admin/azure/sync/run', {});
      setAzureAdminStatus(result.message ?? 'Sync Now completed.');
      await loadAzureAdmin();
    } catch (error) {
      setAzureAdminStatus(error instanceof Error ? error.message : 'Unable to run Azure Sync Now.');
    }
  }

  async function reconcileAzureUsers() {
    setAzureAdminStatus('Reconciling Entra users...');

    try {
      const result = await postJson('/api/admin/azure/users/reconcile', {});
      setAzureAdminStatus(result.message ?? 'Azure/Entra reconciliation completed.');
      await loadAzureAdmin();
    } catch (error) {
      setAzureAdminStatus(error instanceof Error ? error.message : 'Unable to reconcile Entra users.');
    }
  }



  useEffect(() => {
    let cancelled = false;

    async function loadApprovalPendingCount() {
      if (!authSession?.sessionToken) {
        setApprovalPendingCount(0);
        return;
      }

      try {
        const result = await fetchJson('/api/manager/approval-count');
        if (!cancelled) {
          setApprovalPendingCount(Number(result.totalPendingCount ?? 0));
        }
      } catch {
        if (!cancelled) {
          setApprovalPendingCount(0);
        }
      }
    }

    loadApprovalPendingCount();

    const intervalId = window.setInterval(loadApprovalPendingCount, 60000);

    return () => {
      cancelled = true;
      window.clearInterval(intervalId);
    };
  }, [authSession?.sessionToken, activeRoute]);



  useEffect(() => {
    const syncRouteFromHash = () => {
      setActiveRoute(getRouteFromHash());
    };

    window.addEventListener('hashchange', syncRouteFromHash);
    syncRouteFromHash();

    return () => window.removeEventListener('hashchange', syncRouteFromHash);
  }, []);


  const visibleRoleModules = useMemo(() => getVisibleRoleModules(currentUser.data), [currentUser.data]);
  const roleNavigation = useMemo(() => getRoleNavigation(currentUser.data), [currentUser.data]);
  const navigationModel = useMemo(() => buildRoleNavigationModel(currentUser.data, roleNavigation), [currentUser.data, roleNavigation]);
  const activeNavigationItem = useMemo(
    () => roleNavigation.find((item) => item.route === activeRoute) ?? { label: 'Dashboard', route: 'dashboard', href: '#dashboard' },
    [roleNavigation, activeRoute]
  );
  const workspaceRoleName = getRoleDisplayName(currentUser.data);
  const showQuarterUtilizationSummary = userHasAnyPermission(currentUser.data, ['VIEW_OWN_UTILIZATION', 'VIEW_TEAM_UTILIZATION', 'MANAGE_ALL']);

  function getDayStatus(workDate) {
    const apiDayStatus = timesheet.data?.dayStatuses?.find((dayStatus) => dayStatus.workDate === workDate);
    const savedEntryStatus = (timesheet.data?.entries ?? [])
      .filter((entry) => entry.workDate === workDate)
      .map((entry) => entry.status)
      .find((entryStatus) => entryStatus && entryStatus !== 'draft');

    const status = apiDayStatus?.status ?? savedEntryStatus ?? 'draft';
    const editableStatuses = ['draft', 'manager_declined'];
    const canEdit = editableStatuses.includes(status);
    const canUnlock = status === 'submitted' && (apiDayStatus?.canUnlock ?? Boolean(timesheet.data?.canUnlock ?? true));

    let unlockMessage = 'This day is open for time entry.';
    if (status === 'submitted') {
      unlockMessage = 'This submitted day is locked. Use Unlock if it is within the allowed correction window, or contact your manager.';
    } else if (status === 'manager_declined') {
      unlockMessage = apiDayStatus?.managerDecisionComment ?? 'This day was returned by the manager and can be corrected/resubmitted.';
    } else if (status === 'manager_approved') {
      unlockMessage = 'This day has been approved by the manager and can no longer be edited by the engineer.';
    } else if (['pm_approved', 'accounting_ready', 'reconciled', 'locked'].includes(status)) {
      unlockMessage = 'This day has moved forward in the approval workflow and can no longer be edited by the engineer.';
    }

    return {
      ...apiDayStatus,
      workDate,
      status,
      canEdit,
      canUnlock,
      unlockMessage
    };
  }

  function isDayEditable(workDate) {
    return getDayStatus(workDate).canEdit !== false;
  }

  function getEntry(rowId, date, type) {
    return entries[getEntryKey(rowId, date, type)] ?? {
      hours: '',
      comment: '',
      workLocationGroupId: locationGroups.data?.groups?.[0]?.id ?? '',
      workLocationId: locations.data?.locations?.[0]?.id ?? '',
      savedStatus: 'draft'
    };
  }

  function updateEntry(rowId, date, type, patch) {
    if (!isDayEditable(date)) return;

    const key = getEntryKey(rowId, date, type);
    setEntries((current) => ({
      ...current,
      [key]: {
        ...getEntry(rowId, date, type),
        ...patch
      }
    }));
    setSaveStatus('Unsaved changes');
  }


  async function savePersonalDefaults(defaultCodes) {
    const currentPreferences = timesheetPreferences.data ?? {};
    const result = await postJson('/api/users/timesheet-preferences', {
      defaultNonProjectCategoryCodes: defaultCodes,
      defaultProjectTaskIds: currentPreferences.defaultProjectTaskIds ?? [],
      autoAddHolidays: currentPreferences.autoAddHolidays !== false,
      weeklyReminderEnabled: currentPreferences.weeklyReminderEnabled !== false
    });
    setTimesheetPreferences({ loading: false, data: result.preferences, error: null });
    setSaveStatus('Personal defaults saved');
  }

  async function toggleCategoryDefault(categoryCode) {
    const currentCodes = new Set(timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []);
    if (currentCodes.has(categoryCode)) currentCodes.delete(categoryCode);
    else currentCodes.add(categoryCode);
    await savePersonalDefaults([...currentCodes]);
  }

  async function savePersonalDefaults(defaultCodes, defaultTaskIds) {
    const currentPreferences = timesheetPreferences.data ?? {};
    const result = await postJson('/api/users/timesheet-preferences', {
      defaultNonProjectCategoryCodes: defaultCodes,
      defaultProjectTaskIds: defaultTaskIds,
      autoAddHolidays: currentPreferences.autoAddHolidays !== false,
      weeklyReminderEnabled: currentPreferences.weeklyReminderEnabled !== false
    });
    setTimesheetPreferences({ loading: false, data: result.preferences, error: null });
    setSaveStatus('Personal defaults saved');
  }

  async function setRowAsPersonalDefault(row) {
    const defaultCodes = new Set(timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []);
    const defaultTaskIds = new Set(timesheetPreferences.data?.defaultProjectTaskIds ?? []);

    if (row.type === 'nonProject' && row.categoryCode) defaultCodes.add(row.categoryCode);
    if (row.type === 'projectTask' && row.taskId) defaultTaskIds.add(row.taskId);

    await savePersonalDefaults([...defaultCodes], [...defaultTaskIds]);
  }

  async function removeRowAsPersonalDefault(row) {
    const defaultCodes = new Set(timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []);
    const defaultTaskIds = new Set(timesheetPreferences.data?.defaultProjectTaskIds ?? []);

    if (row.type === 'nonProject' && row.categoryCode) defaultCodes.delete(row.categoryCode);
    if (row.type === 'projectTask' && row.taskId) defaultTaskIds.delete(row.taskId);

    await savePersonalDefaults([...defaultCodes], [...defaultTaskIds]);
  }

  function isRowPersonalDefault(row) {
    if (row.type === 'nonProject' && row.categoryCode) {
      return (timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []).includes(row.categoryCode);
    }
    if (row.type === 'projectTask' && row.taskId) {
      return (timesheetPreferences.data?.defaultProjectTaskIds ?? []).includes(row.taskId);
    }
    return false;
  }

  async function importHolidayCsv() {
    setHolidayUploadStatus('Importing holidays...');
    try {
      const result = await postJson('/api/holidays/import-text', {
        year: Number.parseInt(holidayUploadYear, 10),
        filename: `holidays-${holidayUploadYear}.csv`,
        csvText: holidayUploadText
      });
      setHolidayUploadStatus(`Imported ${result.importedCount} holidays for ${result.year}`);
      const refreshed = await fetchJson(`/api/holidays?year=${holidayUploadYear}`);
      setCompanyHolidays({ loading: false, data: refreshed, error: null });
    } catch (error) {
      setHolidayUploadStatus(error instanceof Error ? error.message : 'Holiday import failed');
    }
  }

  function handleHolidayFileUpload(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => setHolidayUploadText(String(reader.result ?? ''));
    reader.readAsText(file);
  }

  async function loadHolidayAdminYear(year) {
    setHolidayUploadYear(year);
    try {
      const refreshed = await fetchJson(`/api/holidays?year=${year}`);
      setCompanyHolidays({ loading: false, data: refreshed, error: null });
    } catch (error) {
      setCompanyHolidays((current) => ({ ...current, loading: false, error: error instanceof Error ? error.message : 'Failed to load holidays' }));
    }
  }

  function addCategory(category) {
    if (!isAnyDayEditable) return;

    const row = categoryToRow(category);
    if (typeof unhideRowForCurrentWeek === 'function') unhideRowForCurrentWeek(row.id);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');
  }

  function addTask(task) {
    if (!isAnyDayEditable) return;

    const row = taskToRow(task);
    if (typeof unhideRowForCurrentWeek === 'function') unhideRowForCurrentWeek(row.id);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');
  }

  function removeRow(rowId) {
    if (!isAnyDayEditable) return;

    hideRowForCurrentWeek(rowId);
    setActiveRows((current) => current.filter((row) => row.id !== rowId));
    setEntries((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(`${rowId}|`))));
    setSelectedCell((current) => (current?.rowId === rowId ? null : current));
    setSaveStatus('Unsaved changes');
  }


  function getRowWorkflowState(rowId) {
    const rowEntries = Object.entries(entries)
      .filter(([key]) => key.startsWith(`${rowId}|`))
      .map(([, entry]) => entry)
      .filter((entry) => Number.parseFloat(entry.hours) > 0);

    if (rowEntries.length === 0) return 'Draft';

    const statuses = new Set(rowEntries.map((entry) => entry.savedStatus ?? 'draft'));

    if (statuses.has('manager_declined')) return 'Correction';

    const activeStatuses = new Set([
      'submitted',
      'manager_approved',
      'pm_approved',
      'accounting_ready',
      'reconciled',
      'locked'
    ]);

    if ([...statuses].some((status) => activeStatuses.has(status))) return 'Active';

    return 'Draft';
  }

  function getCellHours(rowId, date, type) {
    return Number.parseFloat(getEntry(rowId, date, type).hours) || 0;
  }

  function getRowTotal(rowId) {
    return days.reduce((total, day) => total + timeTypes.reduce((subtotal, type) => subtotal + getCellHours(rowId, day.date, type.key), 0), 0);
  }

  function getDayTotal(date) {
    return activeRows.reduce((total, row) => total + timeTypes.reduce((subtotal, type) => subtotal + getCellHours(row.id, date, type.key), 0), 0);
  }

  const grandTotal = activeRows.reduce((total, row) => total + getRowTotal(row.id), 0);
  const afterhoursTotal = activeRows.reduce(
    (total, row) => total + days.reduce((subtotal, day) => subtotal + getCellHours(row.id, day.date, 'afterhours'), 0),
    0
  );
  const normalTotal = grandTotal - afterhoursTotal;
  const moduleData = remainingModules.data ?? {};
  const intakeCount = moduleData.projectIntake?.count ?? 0;
  const milestoneCount = moduleData.projectManagement?.milestoneCount ?? 0;
  const riskCount = moduleData.projectManagement?.riskCount ?? 0;
  const capacityCount = moduleData.resourceCapacity?.count ?? 0;
  const expenseCount = moduleData.expenses?.count ?? 0;
  const invoiceCount = moduleData.invoicing?.count ?? 0;
  const executiveMetricCount = moduleData.executiveDashboard?.count ?? 0;

  const selectedRow = activeRows.find((row) => row.id === selectedCell?.rowId);
  const selectedEntry = selectedCell ? getEntry(selectedCell.rowId, selectedCell.date, selectedCell.type) : null;
  const selectedDayStatus = selectedCell ? getDayStatus(selectedCell.date) : null;
  const selectedEntryIsEditable = Boolean(selectedCell && isDayEditable(selectedCell.date));

  function openEntryDetails(rowId, date, type) {
    setAiSuggestionState({ loading: false, suggestion: '', provider: '', warning: '', error: '' });
    setSelectedCell({ rowId, date, type });
  }

  async function closeEntryDetails({ autoSave = true } = {}) {
    const shouldAutoSave = autoSave && selectedCell && selectedEntryIsEditable && Object.keys(entries).length > 0;
    setSelectedCell(null);
    setAiSuggestionState({ loading: false, suggestion: '', provider: '', warning: '', error: '' });

    if (shouldAutoSave) {
      await autoSaveDraft('Auto-saving draft...');
    }
  }

  function buildTimesheetPayload() {
    const payloadEntries = Object.entries(entries)
      .map(([key, entry]) => {
        const [rowId, workDate, timeType] = key.split('|');
        const row = activeRows.find((item) => item.id === rowId);
        const hours = Number.parseFloat(entry.hours);

        if (!row || Number.isNaN(hours) || hours <= 0) return null;

        return {
          rowType: row.type,
          categoryCode: row.categoryCode ?? null,
          workDate,
          timeType,
          hours,
          description: entry.comment || null,
          workLocationGroupId: entry.workLocationGroupId || null,
          workLocationId: entry.workLocationId || null,
          projectId: row.projectId ?? null,
          taskId: row.taskId ?? null
        };
      })
      .filter(Boolean);

    return {
      weekStart: selectedWeekStart,
      entries: payloadEntries
    };
  }

  function getEntriesForDay(workDate) {
    return buildTimesheetPayload().entries.filter((entry) => entry.workDate === workDate);
  }

  function hasRequiredTimeEntryDescription(value) {
    return String(value ?? '').trim().length > 0;
  }

  function getEntriesMissingDescriptions(payloadEntries) {
    return payloadEntries.filter((entry) => Number(entry.hours) > 0 && !hasRequiredTimeEntryDescription(entry.description));
  }

  function getMissingDescriptionMessage(missingEntries) {
    if (!missingEntries.length) {
      return '';
    }

    const first = missingEntries[0];
    return `Description/comment is required before saving or submitting time. Add a description for ${first.workDate}.`;
  }

  function getSelectedDayTotal() {
    if (!selectedCell) return 0;
    return getDayTotal(selectedCell.date);
  }

  async function generateAiTimeEntrySuggestion() {
    if (!selectedCell || !selectedRow || !selectedEntry) return;

    if (!selectedEntryIsEditable) {
      setAiSuggestionState({
        loading: false,
        suggestion: '',
        provider: '',
        warning: '',
        error: 'This day is locked or submitted. Unlock the day before generating a new suggestion.'
      });
      return;
    }

    setAiSuggestionState({ loading: true, suggestion: '', provider: '', warning: '', error: '' });

    try {
      const hours = Number.parseFloat(selectedEntry.hours);

      const result = await postJson('/api/timesheets/ai-description-suggestions', {
        workDate: selectedCell.date,
        timeType: selectedCell.type,
        rowType: selectedRow.type,
        rowLabel: selectedRow.activity ?? selectedRow.label ?? selectedRow.projectDescription ?? '',
        projectName: selectedRow.projectName ?? selectedRow.projectDescription ?? '',
        projectCode: selectedRow.projectCode ?? '',
        taskName: selectedRow.taskName ?? selectedRow.activity ?? '',
        taskCode: selectedRow.taskCode ?? '',
        categoryCode: selectedRow.categoryCode ?? '',
        hours: Number.isNaN(hours) ? null : hours,
        currentDescription: selectedEntry.comment ?? ''
      });

      setAiSuggestionState({
        loading: false,
        suggestion: result.suggestion ?? '',
        provider: result.provider ?? '',
        warning: result.warning ?? '',
        error: ''
      });
    } catch (error) {
      setAiSuggestionState({
        loading: false,
        suggestion: '',
        provider: '',
        warning: '',
        error: error instanceof Error ? error.message : 'Unable to generate AI suggestion.'
      });
    }
  }

  function applyAiTimeEntrySuggestion() {
    if (!selectedCell || !aiSuggestionState.suggestion) return;

    updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, {
      comment: aiSuggestionState.suggestion
    });

    setSaveStatus('AI suggestion applied to description. Review and save or submit when ready.');
  }

  async function autoSaveDraft(statusMessage = 'Auto-saving draft...') {
    if (!isAnyDayEditable) return;

    const payload = buildTimesheetPayload();
    if (payload.entries.length === 0) return;

    const missingDescriptions = getEntriesMissingDescriptions(payload.entries);
    if (missingDescriptions.length > 0) {
      setSaveStatus(getMissingDescriptionMessage(missingDescriptions));
      return;
    }

    setSaveStatus(statusMessage);

    try {
      const result = await postJson('/api/timesheets/week/draft', payload);
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(statusToLabel(result.timesheet?.status, grandTotal));
      setSaveStatus('Draft autosaved');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Autosave failed');
    }
  }

  async function saveDraft() {
    if (!isAnyDayEditable || isSaving) return;

    setIsSaving(true);
    setSaveStatus('Saving draft...');

    try {
      const payload = buildTimesheetPayload();
      const missingDescriptions = getEntriesMissingDescriptions(payload.entries);

      if (missingDescriptions.length > 0) {
        setSaveStatus(getMissingDescriptionMessage(missingDescriptions));
        return;
      }

      const result = await postJson('/api/timesheets/week/draft', payload);
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(statusToLabel(result.timesheet?.status, grandTotal));
      setSaveStatus('Draft saved');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Failed to save draft');
    } finally {
      setIsSaving(false);
    }
  }

  async function submitSelectedDay() {
    if (!selectedCell || isSaving) return;

    if (!selectedEntryIsEditable) {
      setSaveStatus('This day is locked and cannot be submitted or edited by the engineer.');
      return;
    }

    const dayTotal = getDayTotal(selectedCell.date);
    if (dayTotal < 8) {
      setSaveStatus(`A minimum of 8.00 hours is required before submitting ${selectedCell.date}. Current total is ${formatNumber(dayTotal)} hours.`);
      return;
    }

    setIsSaving(true);
    setSaveStatus(`Submitting ${selectedCell.date}...`);

    try {
      const result = await postJson('/api/timesheets/day/submit', {
        weekStart: selectedWeekStart,
        workDate: selectedCell.date,
        entries: dayEntries
      });
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(`${selectedCell.date} submitted (${formatNumber(dayTotal)} hours).`);
      setSaveStatus(result.message ?? 'Day submitted');
      setSelectedCell(null);
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Failed to submit selected day');
      window.alert(error instanceof Error ? error.message : 'Failed to submit selected day');
    } finally {
      setIsSaving(false);
    }
  }

  async function unlockSelectedDay() {
    if (!selectedCell || isSaving) return;

    setIsSaving(true);
    setSaveStatus(`Requesting unlock for ${selectedCell.date}...`);

    try {
      const result = await postJson('/api/timesheets/day/unlock', {
        weekStart: selectedWeekStart,
        workDate: selectedCell.date
      });
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus('Draft');
      setSaveStatus(result.message ?? 'Day unlocked');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Please contact your manager to unlock this submitted day.');
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSubmit() {
    if (!isAnyDayEditable || isSaving) return;

    if (grandTotal <= 0) {
      setSubmissionStatus('Draft');
      setSaveStatus('Layout saved. No time entries for this week yet.');
      return;
    }

    setIsSaving(true);
    setSaveStatus('Saving weekly draft...');

    try {
      const payload = buildTimesheetPayload();
      const missingDescriptions = getEntriesMissingDescriptions(payload.entries);

      if (missingDescriptions.length > 0) {
        setSaveStatus(getMissingDescriptionMessage(missingDescriptions));
        return;
      }

      const result = await postJson('/api/timesheets/week/draft', payload);
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(statusToLabel(result.timesheet?.status, grandTotal));
      setSaveStatus('Weekly draft saved. Submit each day from the time-entry window when the day reaches 8.00 hours.');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Failed to save weekly draft');
    } finally {
      setIsSaving(false);
    }
  }

  function resetTimesheet() {
    if (!isAnyDayEditable) return;

    saveHiddenRows(new Set());
    setEntries({});
    setSelectedCell(null);
    setSubmissionStatus('Draft');
    setSaveStatus('Layout reset');
  }

  async function loadRoleAdminData() {
    try {
      const [usersResult, rolesResult] = await Promise.all([
        fetchJson('/api/admin/users'),
        fetchJson('/api/admin/roles')
      ]);
      setRoleAdminUsers({ loading: false, data: usersResult, error: null });
      setRoleAdminRoles({ loading: false, data: rolesResult, error: null });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to load role administration data';
      setRoleAdminUsers((current) => ({ ...current, loading: false, error: message }));
      setRoleAdminRoles((current) => ({ ...current, loading: false, error: message }));
    }
  }

  async function updateUserRole(email, roleCode) {
    setRoleAdminStatus(`Updating ${email}...`);
    try {
      await postJson('/api/admin/users/roles', {
        email,
        roleCodes: [roleCode],
        reason: 'Updated from Project Pulse role administration screen'
      });
      setRoleAdminStatus(`Updated ${email} to ${roleCode}`);
      await loadRoleAdminData();
    } catch (error) {
      setRoleAdminStatus(error instanceof Error ? error.message : 'Role update failed');
    }
  }

  function closeSideNavigation() {
    setIsSideNavigationOpen(false);
  }

  function toggleNavigationGroup(groupName) {
    setExpandedNavigationGroups((current) => ({
      ...current,
      [groupName]: !current[groupName]
    }));
  }

  function hasPermission(permissionCode) {
    return securityContext.data?.permissions?.includes(permissionCode) ?? false;
  }

  function canSeeAny(permissionCodes) {
    return permissionCodes.some((permissionCode) => hasPermission(permissionCode));
  }

  const roleNames = securityContext.data?.roles?.map((role) => role.roleName).join(', ') || 'No role assigned';
  const workspaceFeatures = securityContext.data?.features ?? [];
  const canManageHolidays = hasPermission('MANAGE_HOLIDAYS') || hasPermission('MANAGE_ALL');
  const canViewHolidayCalendar = hasPermission('VIEW_HOLIDAYS') || canManageHolidays;
  const canViewPsaModules = canSeeAny(['VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']);
  const currentRoleCodes = securityContext.data?.roles?.map((role) => String(role.roleCode ?? '').toUpperCase()) ?? [];
  const currentRoleNames = securityContext.data?.roles?.map((role) => String(role.roleName ?? '').toLowerCase()) ?? [];
  const canViewManagerApprovalPanel = hasPermission('APPROVE_TIME') || hasPermission('REJECT_TIME') || hasPermission('MANAGE_ALL') || hasPermission('SYSTEM_ADMINISTRATION');
  const canViewLocalAdminPasswordResetApprovals =
    hasPermission('MANAGE_ALL') ||
    hasPermission('SYSTEM_ADMINISTRATION') ||
    currentRoleCodes.includes('ADMINISTRATOR') ||
    currentRoleCodes.includes('PROJECT_TEAM_COORDINATOR') ||
    currentRoleCodes.includes('PROJECT_COORDINATOR') ||
    currentRoleCodes.includes('TEAM_COORDINATOR') ||
    currentRoleNames.some((roleName) => roleName.includes('project/team coordinator') || roleName.includes('project team coordinator') || roleName.includes('team coordinator'));



  if (!authSession) {
    return (
      <main className="app-shell auth-shell">
        <section className="auth-landing-panel">
          <div className="auth-brand-block">
            <SignalLogo />
            <p className="eyebrow">Project Pulse Access</p>
            <h1>Sign in to your role-based workspace</h1>
            <p>
              Use your US Signal email for SSO. Use the local administrator account only for break-glass access when SSO is unavailable.
            </p>
          </div>

          <div className="auth-card">
            <form onSubmit={resolveLoginRoute}>
              <label htmlFor="login-username">Email or local admin account</label>
              <input
                id="login-username"
                type="text"
                value={loginUsername}
                placeholder="name@ussignal.com or admin@ussignal.local"
                onChange={(event) => {
                  setLoginUsername(event.target.value);
                  setLoginRoute(null);
                  setLoginStatus('');
                  setPasswordResetStatus('');
                }}
              />

              <button className="primary-action" type="submit" disabled={isResolvingLogin}>
                {isResolvingLogin ? 'Checking...' : 'Continue'}
              </button>
            </form>

            {loginStatus && <p className="auth-status">{loginStatus}</p>}

            {loginRoute?.loginMethod === 'sso' && (
              <div className="auth-route-box">
                <p className="eyebrow">US Signal SSO</p>
                <h2>Continue with Microsoft Entra ID</h2>
                <p>Production SSO will redirect to Microsoft Entra ID. This development shell records the selected SSO route.</p>
                <button className="primary-action" type="button" onClick={continueWithSsoPlaceholder}>
                  Continue with US Signal SSO
                </button>
              </div>
            )}

            {loginRoute?.loginMethod === 'local' && (
              <div className="auth-route-box">
                <p className="eyebrow">Local administrator</p>
                <h2>Break-glass local sign-in</h2>
                <form onSubmit={continueWithLocalShell}>
                  <label htmlFor="login-password">Password</label>
                  <input
                    id="login-password"
                    type="password"
                    value={loginPassword}
                    placeholder="Enter local administrator password"
                    onChange={(event) => setLoginPassword(event.target.value)}
                  />

                  <button className="primary-action" type="submit">
                    Sign in as local administrator
                  </button>
                </form>

                <div className="password-reset-box">
                  <label htmlFor="password-reset-notes">Forgot password notes</label>
                  <textarea
                    id="password-reset-notes"
                    value={passwordResetNotes}
                    placeholder="Optional reason for reset request"
                    onChange={(event) => setPasswordResetNotes(event.target.value)}
                  />

                  <button className="secondary-action" type="button" onClick={requestLocalPasswordReset}>
                    Request password reset approval
                  </button>

                  {passwordResetStatus && <p className="auth-status">{passwordResetStatus}</p>}
                </div>
              </div>
            )}
          </div>
        </section>
      </main>
    );
  }

  if (authSession?.loginMethod === 'local' && authSession?.mustChangePassword) {
    return (
      <main className="app-shell auth-shell forced-password-shell">
        <section className="auth-landing-panel">
          <div className="auth-brand-block">
            <SignalLogo />
            <p className="eyebrow">Password Change Required</p>
            <h1>Set a new local administrator password</h1>
            <p>
              You signed in with a temporary password. Before accessing Project Pulse, choose a new password for the local administrator account.
            </p>
          </div>

          <div className="auth-card">
            <div className="auth-route-box">
              <p className="eyebrow">Local administrator</p>
              <h2>{authSession.displayName || authSession.username}</h2>
              <p className="muted">This step protects the break-glass account and completes the approved password reset workflow.</p>

              <form onSubmit={completeForcedPasswordChange} className="forced-password-form">
                <label htmlFor="forced-current-password">Temporary password</label>
                <input
                  id="forced-current-password"
                  type="password"
                  value={forcedCurrentPassword}
                  placeholder="Enter temporary password"
                  autoComplete="current-password"
                  onChange={(event) => setForcedCurrentPassword(event.target.value)}
                />

                <label htmlFor="forced-new-password">New password</label>
                <input
                  id="forced-new-password"
                  type="password"
                  value={forcedNewPassword}
                  placeholder="Enter new password"
                  autoComplete="new-password"
                  onChange={(event) => setForcedNewPassword(event.target.value)}
                />

                <label htmlFor="forced-confirm-password">Confirm new password</label>
                <input
                  id="forced-confirm-password"
                  type="password"
                  value={forcedConfirmPassword}
                  placeholder="Confirm new password"
                  autoComplete="new-password"
                  onChange={(event) => setForcedConfirmPassword(event.target.value)}
                />

                <button className="primary-action" type="submit" disabled={isChangingForcedPassword}>
                  {isChangingForcedPassword ? 'Changing password...' : 'Change password and continue'}
                </button>
              </form>

              <button className="secondary-action" type="button" onClick={signOut}>
                Sign out instead
              </button>

              {forcedPasswordStatus && <p className="auth-status">{forcedPasswordStatus}</p>}
            </div>
          </div>
        </section>
      </main>
    );
  }

  return (
    <main className={`app-shell route-${activeRoute} enterprise-nav-enabled ${isSideNavigationOpen ? 'sidebar-expanded' : 'sidebar-collapsed'}`}>

      {sessionWarning.visible && (
        <div className="session-timeout-backdrop">
          <section className="session-timeout-modal" role="dialog" aria-modal="true" aria-label="Session timeout warning">
            <p className="eyebrow">Session timeout</p>
            <h2>Your Project Pulse session is about to expire</h2>
            <p>
              For security, each session is limited to two hours. Extend your session now to continue working, or you will be signed out when the timer reaches zero.
            </p>
            <strong className="session-countdown">
              Time remaining: {Math.max(1, Math.ceil(sessionWarning.remainingMs / 60000))} minute(s)
            </strong>
            <div className="session-timeout-actions">
              <button type="button" className="secondary-action" onClick={signOut}>
                Sign out now
              </button>
              <button type="button" className="primary-action" onClick={extendCurrentSession}>
                Extend session
              </button>
            </div>
          </section>
        </div>
      )}




      {isSettingsOpen && (
        <div className="profile-settings-backdrop">
          <section className="profile-settings-modal strong-profile-modal" role="dialog" aria-modal="true" aria-label="Profile settings">
            <div className="profile-settings-header">
              <div>
                <p className="eyebrow">User profile</p>
                <h2>{profileSettingsPanel === 'profile' ? 'My profile' : 'My settings'}</h2>
                <p>
                  {profileSettingsPanel === 'profile'
                    ? 'Update your profile picture, title, and awards or certificates earned.'
                    : 'Update your personal application preferences.'}
                </p>
              </div>
              <button type="button" className="modal-close-button" onClick={closeProfileSettings}>
                ×
              </button>
            </div>

            <div className="profile-settings-tabs">
              <button
                type="button"
                className={profileSettingsPanel === 'profile' ? 'active' : ''}
                onClick={() => setProfileSettingsPanel('profile')}
              >
                My profile
              </button>
              <button
                type="button"
                className={profileSettingsPanel === 'settings' ? 'active' : ''}
                onClick={() => setProfileSettingsPanel('settings')}
              >
                My settings
              </button>
            </div>

            <form className="profile-settings-form" onSubmit={saveProfileSettings}>
              {profileSettingsPanel === 'profile' ? (
                <>
                  <div className="profile-picture-editor">
                    <div className="profile-picture-preview">
                      {profileDraft.profilePhotoDataUrl ? (
                        <img src={profileDraft.profilePhotoDataUrl} alt="Profile preview" />
                      ) : (
                        <span>{getInitials(authSession?.username ?? currentUser.data?.displayName ?? currentUser.data?.email)}</span>
                      )}
                    </div>

                    <div>
                      <label htmlFor="profile-photo-upload">Profile picture</label>
                      <input
                        id="profile-photo-upload"
                        type="file"
                        accept="image/*"
                        onChange={handleProfilePhotoUpload}
                      />
                      <small>Use a small square image. Current limit is 2 MB.</small>
                      <button type="button" className="secondary-action" onClick={removeProfilePhoto}>
                        Remove picture
                      </button>
                    </div>
                  </div>

                  <label htmlFor="display-name-override">Display name</label>
                  <input
                    id="display-name-override"
                    type="text"
                    value={profileDraft.displayNameOverride}
                    placeholder={currentUser.data?.displayName ?? authSession?.username ?? 'Display name'}
                    onChange={(event) => setProfileDraft((current) => ({ ...current, displayNameOverride: event.target.value }))}
                  />

                  <label htmlFor="title-override">Title / role description</label>
                  <input
                    id="title-override"
                    type="text"
                    value={profileDraft.titleOverride}
                    placeholder="Example: Collaboration Team Lead"
                    onChange={(event) => setProfileDraft((current) => ({ ...current, titleOverride: event.target.value }))}
                  />

                  <label htmlFor="awards-certificates">Awards / certificates earned</label>
                  <textarea
                    id="awards-certificates"
                    value={profileDraft.awardsAndCertificates}
                    placeholder={`Cisco - CCNA Collaboration
Cisco - CCNP Collaboration
Analytics - Variphy / Infortel`}
                    onChange={(event) => setProfileDraft((current) => ({ ...current, awardsAndCertificates: event.target.value }))}
                  />
                </>
              ) : (
                <>
                  <div className="settings-section-card">
                    <p className="eyebrow">Appearance</p>
                    <h3>Theme preference</h3>
                    <p>Select how Project Pulse should appear for your account on this browser.</p>

                    <div className="theme-choice-grid">
                      <label className={profileDraft.theme === 'light' ? 'theme-choice active' : 'theme-choice'}>
                        <input
                          type="radio"
                          name="theme"
                          value="light"
                          checked={profileDraft.theme === 'light'}
                          onChange={(event) => setProfileDraft((current) => ({ ...current, theme: event.target.value }))}
                        />
                        <strong>Light mode</strong>
                        <span>Bright workspace view</span>
                      </label>

                      <label className={profileDraft.theme === 'dark' ? 'theme-choice active' : 'theme-choice'}>
                        <input
                          type="radio"
                          name="theme"
                          value="dark"
                          checked={profileDraft.theme === 'dark'}
                          onChange={(event) => setProfileDraft((current) => ({ ...current, theme: event.target.value }))}
                        />
                        <strong>Dark mode</strong>
                        <span>Reduced brightness view</span>
                      </label>
                    </div>
                  </div>

                  <div className="settings-section-card">
                    <p className="eyebrow">Account</p>
                    <h3>Current session</h3>
                    <p><strong>User:</strong> {authSession?.username ?? currentUser.data?.email ?? 'Unknown user'}</p>
                    <p><strong>Workspace:</strong> {workspaceRoleName}</p>
                  </div>
                </>
              )}

              {profileSettingsStatus && (
                <p className="profile-settings-status">{profileSettingsStatus}</p>
              )}

              <div className="profile-settings-actions">
                <button type="button" className="secondary-action" onClick={closeProfileSettings}>
                  Cancel
                </button>
                <button type="submit" className="primary-action">
                  Save settings
                </button>
              </div>
            </form>
          </section>
        </div>
      )}



      <header className="top-bar">
        <SignalLogo />
        <div className="workspace-header-context">
          <button
            type="button"
            className="sidebar-toggle-button"
            onClick={() => setIsSideNavigationOpen((current) => !current)}
            aria-label={isSideNavigationOpen ? 'Collapse workspace navigation' : 'Expand workspace navigation'}
          >
            ☰
          </button>

          <div>
            <p className="eyebrow">Workspace</p>
            <h1>{activeNavigationItem.label}</h1>
          </div>
        </div>
        <div className="profile-menu-shell" ref={profileMenuRef}>
          <button
            className="profile-avatar-button"
            type="button"
            onClick={() => setIsProfileMenuOpen((value) => !value)}
            aria-label="Open profile menu"
          >
            {userPreferences.profilePhotoDataUrl ? (
              <img src={userPreferences.profilePhotoDataUrl} alt="Profile" />
            ) : (
              <span>{getInitials(authSession?.username ?? currentUser.data?.displayName ?? currentUser.data?.email)}</span>
            )}
          </button>

          {isProfileMenuOpen && (
            <div className="profile-dropdown-menu">
              <div className="profile-dropdown-header">
                <div className="profile-dropdown-avatar">
                  {userPreferences.profilePhotoDataUrl ? (
                    <img src={userPreferences.profilePhotoDataUrl} alt="Profile" />
                  ) : (
                    <span>{getInitials(authSession?.username ?? currentUser.data?.displayName ?? currentUser.data?.email)}</span>
                  )}
                </div>
                <div>
                  <strong>{userPreferences.displayNameOverride || currentUser.data?.displayName || authSession?.username || 'Project Pulse User'}</strong>
                  <small>{authSession?.username ?? currentUser.data?.email ?? 'Current user'}</small>
                  <small>{workspaceRoleName}</small>
                </div>
              </div>

              <button type="button" onClick={() => openProfileSettings('profile')}>
                My profile
              </button>
              <button type="button" onClick={() => openProfileSettings('settings')}>
                My settings
              </button>
              <button type="button" onClick={signOut}>
                Sign out
              </button>
            </div>
          )}
        </div>
      </header>

      <aside className="enterprise-sidebar" aria-label="Workspace navigation">
        <div className="enterprise-sidebar-header">
          <div>
            <p className="eyebrow">Project Pulse</p>
            <h2>{workspaceRoleName}</h2>
          </div>
        </div>

        <div className="enterprise-sidebar-section">
          <p className="enterprise-sidebar-section-title">Pinned</p>
          <div className="enterprise-sidebar-links">
            {navigationModel.primary.map((item) => (
              <a
                href={item.href}
                key={`enterprise-primary-${item.route}`}
                className={activeRoute === item.route ? 'active' : ''}
              >
                <span className="enterprise-nav-icon">{item.label.slice(0, 1)}</span>
                <span className="enterprise-nav-label">{item.label}</span>
              </a>
            ))}
          </div>
        </div>

        <div className="enterprise-sidebar-section">
          <p className="enterprise-sidebar-section-title">Modules</p>
          <div className="enterprise-sidebar-groups">
            {navigationModel.groups.map((group) => (
              <div className="enterprise-sidebar-group" key={group.name}>
                <button
                  type="button"
                  className="enterprise-sidebar-group-toggle"
                  onClick={() => toggleNavigationGroup(group.name)}
                  aria-expanded={Boolean(expandedNavigationGroups[group.name])}
                >
                  <span className="enterprise-nav-label">{group.name}</span>
                  <strong>{expandedNavigationGroups[group.name] ? '−' : '+'}</strong>
                </button>

                {expandedNavigationGroups[group.name] ? (
                  <div className="enterprise-sidebar-links nested">
                    {group.items.map((item) => (
                      <a
                        href={item.href}
                        key={`enterprise-${group.name}-${item.route}`}
                        className={activeRoute === item.route ? 'active' : ''}
                      >
                        <span className="enterprise-nav-icon">{item.label.slice(0, 1)}</span>
                        <span className="enterprise-nav-label">{item.label}</span>
                      </a>
                    ))}
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        </div>
      </aside>

<section id="user-admin" className="panel user-admin-panel">
        <UserAdministrationPanel />
      </section>

      <section id="azure-admin" className="panel azure-admin-panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Azure Admin</p>
            <h1>Azure / Entra user sync</h1>
            <p className="section-copy">
              Preview Entra users, filter the import list, import selected users, and clearly track who has been imported or synced. Local @ussignal.local users stay in User Administration.
            </p>
          </div>
          <div className="azure-admin-heading-actions">
            <button type="button" className="secondary-action" onClick={loadAzureAdmin}>
              Refresh
            </button>
            <button type="button" className="primary-action" onClick={runAzureFoundationSync}>
              Sync Now
            </button>
          </div>
        </div>

        {azureAdminData.error && (
          <div className="error-text">{azureAdminData.error}</div>
        )}

        {azureAdminStatus && (
          <div className="azure-admin-status-banner">{azureAdminStatus}</div>
        )}

        <div className="azure-admin-workspace">
          <form className="azure-admin-card azure-config-card" onSubmit={saveAzureConfig}>
            <div className="azure-card-heading">
              <div>
                <p className="eyebrow">Configuration</p>
                <h2>Tenant and sync settings</h2>
                <p className="section-copy">
                  Use onenecklab.com for test and ussignal.com for production. Imported users default to Engineer.
                </p>
              </div>
              <button type="submit" className="primary-action">
                Save configuration
              </button>
            </div>

            <div className="azure-admin-form-grid">
              <label>Tenant profile</label>
              <select
                value={azureTenantProfile}
                onChange={(event) => applyAzureTenantProfile(event.target.value)}
              >
                <option value="onenecklab">OneNeck Lab - onenecklab.com + ONITDemo.com</option>
                <option value="ussignal">US Signal Production - ussignal.com</option>
                <option value="custom">Create New</option>
              </select>

              {azureTenantProfile === 'custom' && (
                <>
                  <label>Tenant display name</label>
                  <input
                    value={customAzureTenantName}
                    onChange={(event) => setCustomAzureTenantName(event.target.value)}
                    placeholder="Example: Customer Tenant, Lab Tenant, Partner Tenant"
                  />

                  <label>Custom tenant domains</label>
                  <input
                    value={customAzureTenantDomain}
                    onChange={(event) => setCustomAzureTenantDomain(event.target.value)}
                    placeholder="example.com or example.com,otherdomain.com"
                  />
                </>
              )}

              <label>Tenant ID</label>
              <input
                value={azureConfigDraft.tenantId}
                onChange={(event) => setAzureConfigDraft((current) => ({ ...current, tenantId: event.target.value }))}
                placeholder="Microsoft Entra tenant ID"
              />

              <label>Client ID</label>
              <input
                value={azureConfigDraft.clientId}
                onChange={(event) => setAzureConfigDraft((current) => ({ ...current, clientId: event.target.value }))}
                placeholder="Application client ID"
              />

              <label>Authority URL</label>
              <input
                value={azureConfigDraft.authorityUrl}
                onChange={(event) => setAzureConfigDraft((current) => ({ ...current, authorityUrl: event.target.value }))}
                placeholder="https://login.microsoftonline.com/{tenantId}"
              />

              <label>Redirect URI</label>
              <input
                value={azureConfigDraft.redirectUri}
                onChange={(event) => setAzureConfigDraft((current) => ({ ...current, redirectUri: event.target.value }))}
                placeholder="https://projectpulse-test.onenecklab.com/auth/callback"
              />

              <label>Graph scope</label>
              <input
                value={azureConfigDraft.graphScope}
                onChange={(event) => setAzureConfigDraft((current) => ({ ...current, graphScope: event.target.value }))}
                placeholder="User.Read.All Directory.Read.All"
              />

              <label>Default role</label>
              <select
                value={azureConfigDraft.defaultRoleCode}
                onChange={(event) => setAzureConfigDraft((current) => ({ ...current, defaultRoleCode: event.target.value }))}
              >
                {(azureAdminData.roles?.length ? azureAdminData.roles : [{ roleCode: 'ENGINEER', roleName: 'Engineer' }]).map((role) => (
                  <option value={role.roleCode} key={role.roleCode}>
                    {role.roleName}
                  </option>
                ))}
              </select>

              <label>Sync frequency hours</label>
              <input
                type="number"
                min="1"
                value={azureConfigDraft.syncFrequencyHours}
                onChange={(event) => setAzureConfigDraft((current) => ({ ...current, syncFrequencyHours: Number(event.target.value) || 24 }))}
              />

              <label className="checkbox-row azure-checkbox-row">
                <input
                  type="checkbox"
                  checked={azureConfigDraft.syncEnabled}
                  onChange={(event) => setAzureConfigDraft((current) => ({ ...current, syncEnabled: event.target.checked }))}
                />
                Sync enabled
              </label>
              <div className="user-admin-helper-text">
                Azure sync applies to Entra users only. Local @ussignal.local users are never imported from Entra.
              </div>
            </div>
          </form>

          <div className="azure-admin-card azure-sync-summary-card">
            <p className="eyebrow">Sync status</p>
            <h2>Last sync result</h2>
            <div className="azure-sync-facts">
              <span>
                <strong>Status</strong>
                {azureAdminData.config?.lastSyncStatus ?? 'Not available'}
              </span>
              <span>
                <strong>Last sync</strong>
                {azureAdminData.config?.lastSyncAt ?? 'Never'}
              </span>
              <span>
                <strong>Updated by</strong>
                {azureAdminData.config?.updatedByEmail ?? 'Unknown'}
              </span>
            </div>
            <p className="section-copy">
              {azureAdminData.config?.lastSyncMessage ?? 'No sync has been recorded yet.'}
            </p>
            <div className="azure-admin-action-row">
              <button type="button" className="primary-action" onClick={runAzureFoundationSync}>
                Sync Now
              </button>
              <button type="button" className="secondary-action" onClick={reconcileAzureUsers}>
                Reconcile inactive users
              </button>
            </div>
          </div>

          <div className="azure-admin-card azure-preview-card">
            <div className="azure-card-heading">
              <div>
                <p className="eyebrow">Filtered import</p>
                <h2>Preview, filter, and import Entra users</h2>
                <p className="section-copy">
                  Load the Entra preview, filter by domain, department, existing import status, or enabled status, then import only the selected users.
                </p>
              </div>
              <div className="azure-admin-heading-actions">
                <button type="button" className="secondary-action" onClick={previewAzureUsers} disabled={azurePreviewLoading}>
                  {azurePreviewLoading ? 'Loading preview...' : 'Preview users'}
                </button>
                <button type="button" className="primary-action" onClick={importSelectedAzureUsers} disabled={selectedAzurePreviewKeys.length === 0}>
                  Import selected
                </button>
              </div>
            </div>

            <div className="azure-filter-grid">
              <label>
                Search
                <input
                  value={azureImportFilters.searchText}
                  onChange={(event) => setAzureImportFilters((current) => ({ ...current, searchText: event.target.value }))}
                  placeholder="Name, email, title, department"
                />
              </label>

              <label>
                Domain
                <select
                  value={azureImportFilters.domain}
                  onChange={(event) => setAzureImportFilters((current) => ({ ...current, domain: event.target.value }))}
                >
                  <option value="all">All selected tenant domains</option>
                  <option value="onenecklab.com">onenecklab.com - OneNeck Lab</option>
                  <option value="onitdemo.com">ONITDemo.com - OneNeck Lab</option>
                  <option value="ussignal.com">ussignal.com - Production</option>
                </select>
              </label>

              <label>
                Department contains
                <input
                  value={azureImportFilters.departmentName}
                  onChange={(event) => setAzureImportFilters((current) => ({ ...current, departmentName: event.target.value }))}
                  placeholder="Engineering, Project Management, etc."
                />
              </label>

              <label className="checkbox-row azure-filter-checkbox">
                <input
                  type="checkbox"
                  checked={azureImportFilters.includeExisting}
                  onChange={(event) => setAzureImportFilters((current) => ({ ...current, includeExisting: event.target.checked }))}
                />
                Include already imported users
              </label>

              <label className="checkbox-row azure-filter-checkbox">
                <input
                  type="checkbox"
                  checked={azureImportFilters.onlyEnabled}
                  onChange={(event) => setAzureImportFilters((current) => ({ ...current, onlyEnabled: event.target.checked }))}
                />
                Enabled Entra accounts only
              </label>
            </div>

            <div className="azure-selection-toolbar">
              <button type="button" className="secondary-action" onClick={toggleAllFilteredAzurePreviewUsers} disabled={getFilteredAzurePreviewUsers().length === 0}>
                Select filtered users
              </button>
              <button type="button" className="secondary-action" onClick={() => setSelectedAzurePreviewKeys([])} disabled={selectedAzurePreviewKeys.length === 0}>
                Clear selection
              </button>
              <span className="bulk-selection-pill active">
                {selectedAzurePreviewKeys.length} selected
              </span>
              <span className="bulk-selection-pill">
                {getFilteredAzurePreviewUsers().length} filtered
              </span>
              <span className="bulk-selection-pill">
                {azurePreviewUsers.length} previewed
              </span>
            </div>

            {azurePreviewUsers.length === 0 ? (
              <div className="manager-empty-state">
                Use Preview users to load the Entra user list before applying filters.
              </div>
            ) : getFilteredAzurePreviewUsers().length === 0 ? (
              <div className="manager-empty-state">
                No preview users match the current filters.
              </div>
            ) : (
              <div className="azure-preview-table">
                <table>
                  <thead>
                    <tr>
                      <th>Select</th>
                      <th>User</th>
                      <th>Domain</th>
                      <th>Job title</th>
                      <th>Department</th>
                      <th>Import status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {getFilteredAzurePreviewUsers().map((user) => {
                      const alreadyImported = isAzurePreviewUserAlreadyImported(user);
                      return (
                        <tr key={user.previewKey}>
                          <td>
                            <input
                              type="checkbox"
                              checked={selectedAzurePreviewKeys.includes(user.previewKey)}
                              onChange={() => toggleAzurePreviewUser(user.previewKey)}
                              disabled={alreadyImported}
                              aria-label={`Select ${user.displayName}`}
                            />
                          </td>
                          <td>
                            <strong>{user.displayName}</strong>
                            <small>{user.email}</small>
                          </td>
                          <td>{getAzureUserDomain(user.email)}</td>
                          <td>{user.jobTitle || '—'}</td>
                          <td>{user.departmentName || '—'}</td>
                          <td>
                            <span className={alreadyImported ? 'badge active' : 'badge'}>
                              {alreadyImported ? 'Already imported / synced' : 'Ready to import'}
                            </span>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div className="azure-admin-card azure-imported-users-card">
            <div className="azure-card-heading">
              <div>
                <p className="eyebrow">Imported and synced users</p>
                <h2>Azure / Entra directory users</h2>
                <p className="section-copy">
                  This table shows whether each user came from Entra, whether the user has a sync timestamp, and whether login is currently enabled.
                </p>
              </div>
              <span className="badge">{getFilteredAzureDirectoryUsers().length} shown / {azureAdminData.users.length} total</span>
            </div>

            <div className="azure-filter-grid compact-directory-filter">
              <label>
                Search imported users
                <input
                  value={azureDirectoryFilters.searchText}
                  onChange={(event) => setAzureDirectoryFilters((current) => ({ ...current, searchText: event.target.value }))}
                  placeholder="Name, email, department, role"
                />
              </label>

              <label>
                Source
                <select
                  value={azureDirectoryFilters.sourceProvider}
                  onChange={(event) => setAzureDirectoryFilters((current) => ({ ...current, sourceProvider: event.target.value }))}
                >
                  <option value="entra">All Entra sources</option>
                  <option value="ENTRA_ID_TEST">Test Entra - ENTRA_ID_TEST</option>
                  <option value="ENTRA_ID">Production Entra - ENTRA_ID</option>
                  <option value="LOCAL_APP">Local app - LOCAL_APP</option>
                  <option value="all">All sources including local</option>
                </select>
              </label>

              <label>
                Sync state
                <select
                  value={azureDirectoryFilters.syncState}
                  onChange={(event) => setAzureDirectoryFilters((current) => ({ ...current, syncState: event.target.value }))}
                >
                  <option value="all">All sync states</option>
                  <option value="synced">Has sync timestamp</option>
                  <option value="not_synced">No sync timestamp</option>
                  <option value="disabled">Disabled / inactive</option>
                </select>
              </label>
            </div>

            <div className="azure-preview-table imported-directory-table">
              {getFilteredAzureDirectoryUsers().length === 0 ? (
                <div className="manager-empty-state">No users match the current imported-user filters.</div>
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th>User</th>
                      <th>Source</th>
                      <th>Domain</th>
                      <th>Department</th>
                      <th>Roles</th>
                      <th>Login</th>
                      <th>Sync state</th>
                      <th>Last sync</th>
                    </tr>
                  </thead>
                  <tbody>
                    {getFilteredAzureDirectoryUsers().map((user) => (
                      <tr key={user.userId ?? user.email}>
                        <td>
                          <strong>{user.displayName}</strong>
                          <small>{user.email}</small>
                        </td>
                        <td>{user.sourceProvider ?? 'Unknown'}</td>
                        <td>{getAzureUserDomain(user.email)}</td>
                        <td>{user.departmentName || '—'}</td>
                        <td>{user.roleNames?.length ? user.roleNames.join(', ') : 'No active roles'}</td>
                        <td>
                          <span className={user.loginEnabled && user.isActive ? 'badge active' : 'badge'}>
                            {user.loginEnabled && user.isActive ? 'Enabled' : 'Disabled'}
                          </span>
                        </td>
                        <td>{getAzureUserSyncLabel(user)}</td>
                        <td>{user.lastDirectorySyncAt ?? 'Not synced'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>

          <div className="azure-admin-card azure-sync-runs-card">
            <div className="azure-card-heading">
              <div>
                <p className="eyebrow">Audit</p>
                <h2>Sync run history</h2>
              </div>
              <span className="badge">{azureAdminData.runs.length} runs</span>
            </div>

            <div className="azure-sync-run-list">
              {azureAdminData.runs.length === 0 ? (
                <div className="manager-empty-state">No sync runs recorded yet.</div>
              ) : (
                azureAdminData.runs.map((run) => (
                  <div className="azure-sync-run-row" key={run.syncRunId}>
                    <div>
                      <strong>{run.status}</strong>
                      <span>{run.message ?? 'No message recorded.'}</span>
                      <small>Triggered by: {run.triggeredByEmail ?? 'Unknown'}</small>
                    </div>
                    <div>
                      <small>Seen: {run.usersSeen ?? 0}</small>
                      <small>Imported: {run.usersImported ?? 0}</small>
                      <small>Updated: {run.usersUpdated ?? 0}</small>
                      <small>Skipped: {run.usersSkipped ?? 0}</small>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      </section>
                          {(activeRoute === 'backup-dr' && (hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL'))) ? (
        <BackupDrCenter authSession={authSession} />
      ) : null}

{(activeRoute === 'backup-retention' && (hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL'))) ? (
        <BackupRetentionCenter authSession={authSession} />
      ) : null}

{(activeRoute === 'restore-validation' && (hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL'))) ? (
        <RestoreValidationCenter authSession={authSession} />
      ) : null}

{(activeRoute === 'replication-sync' && (hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL'))) ? (
        <ReplicationSyncStatusCenter authSession={authSession} />
      ) : null}

{(activeRoute === 'service-control' && (hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL'))) ? (
        <ServiceControlCenter authSession={authSession} />
      ) : null}

{(activeRoute === 'audit-history' && (hasPermission('VIEW_AUDIT_TRAIL') || hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL'))) ? (
        <AuditHistoryPanel />
      ) : null}

      <section id="role-dashboard" className="panel role-dashboard-panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Role-based workspace</p>
            <h1>{workspaceRoleName}</h1>
            <p className="section-copy">
              Select a workspace module below. The dashboard only shows the views and actions available to your assigned role.
            </p>
          </div>
          <span className="badge">{visibleRoleModules.length} available views</span>
        </div>

        {currentUser.error && (
          <div className="error-text">Unable to load current user: {currentUser.error}</div>
        )}

        <div className="role-dashboard-grid">
          {visibleRoleModules.map((module) => (
            <a className="role-dashboard-card" href={module.href} key={`${module.title}-${module.route}`}>
              <span><span className="nav-label-with-badge">
                    {module.navLabel}
                    {module.route === 'manager-approval' && approvalPendingCount > 0 && (
                      <span className="nav-pending-badge">{approvalPendingCount}</span>
                    )}
                  </span></span>
              <strong>{module.title}</strong>
              <small>{module.description}</small>
            </a>
          ))}
        </div>

        {showQuarterUtilizationSummary && (
          <div className="dashboard-quarter-summary">
            <div className="section-heading compact-heading">
              <div>
                <p className="eyebrow">Current quarter utilization</p>
                <h2>{currentQuarterUtilization.data?.quarter ?? 'Current Quarter'}</h2>
              </div>
              <span className="badge">{currentQuarterUtilization.data?.targetPercent ?? 0}% target</span>
            </div>

            <DataState loading={currentQuarterUtilization.loading} error={currentQuarterUtilization.error}>
              <div className="quarter-utilization-grid">
                <article className="quarter-utilization-card">
                  <span>Target utilization</span>
                  <strong>{currentQuarterUtilization.data?.targetPercent ?? 0}%</strong>
                  <small>{formatNumber(currentQuarterUtilization.data?.targetHours)} target hrs</small>
                </article>

                <article className="quarter-utilization-card">
                  <span>Current utilization</span>
                  <strong>{formatNumber(currentQuarterUtilization.data?.currentUtilizationPercent)}%</strong>
                  <small>{formatNumber(currentQuarterUtilization.data?.currentBillableHours)} billable hrs</small>
                </article>

                <article className="quarter-utilization-card">
                  <span>Hours left to target</span>
                  <strong>{formatNumber(currentQuarterUtilization.data?.hoursLeftToTarget)}</strong>
                  <small>remaining billable hrs</small>
                </article>

                <article className="quarter-utilization-card">
                  <span>Standard quarter</span>
                  <strong>{formatNumber(currentQuarterUtilization.data?.standardPeriodHours)}</strong>
                  <small>standard hrs</small>
                </article>
              </div>
            </DataState>
          </div>
        )}
      </section>

      <section id="dashboard" className="hero hero-polished">
        <div className="hero-content-block">
          <p className="eyebrow">US Signal Project Pulse</p>
          <h1>Operational command center for time, approvals, utilization, and billing readiness.</h1>
          <p className="hero-copy">
            Project Pulse brings weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting into one internal workflow.
          </p>
          <div className="hero-pill-row">
            <span>Time entry</span>
            <span>Approval workflow</span>
            <span>Utilization</span>
            <span>Accounting readiness</span>
          </div>
        </div>
        <aside className="hero-side-card" aria-label="Platform direction">
          <strong>Built for Professional Services</strong>
          <span>Personalized defaults, holiday automation, reminders, approvals, and reporting.</span>
        </aside>
      </section>

      <section className="status-grid" aria-label="Platform status">
        <article className="status-card">
          <span className="status-label">API</span>
          <strong>{apiHealth.loading ? 'Checking...' : apiHealth.error ? 'Unavailable' : apiHealth.data?.status}</strong>
          <small>{apiHealth.data?.service ?? apiHealth.error ?? 'Project Pulse API'}</small>
        </article>

        <article className="status-card">
          <span className="status-label">Database</span>
          <strong>{dbHealth.loading ? 'Checking...' : dbHealth.error ? 'Unavailable' : dbHealth.data?.database}</strong>
          <small>{databaseSummary}</small>
        </article>

        <article className="status-card">
          <span className="status-label">Schema</span>
          <strong>{schema.loading ? 'Checking...' : schema.error ? 'Unavailable' : `${schema.data?.count ?? 0} tables`}</strong>
          <small>PostgreSQL platform schema validation</small>
        </article>
      </section>

      <section className="panel role-workspace-panel" aria-label="Role-based workspace">
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Role-based workspace</p>
            <h2>{securityContext.loading ? 'Loading workspace access...' : roleNames}</h2>
            <p className="muted">Views and actions are personalized by assigned role. Engineers see their time and utilization, managers see approvals and team reporting, project roles see project operations, and administrators see all modules.</p>
          </div>
          <span className="pill">{workspaceFeatures.length} available views</span>
        </div>
        {securityContext.error ? <p className="error-text">{securityContext.error}</p> : null}
        <div className="role-feature-grid">
          {workspaceFeatures.map((feature) => (
            <a className="role-feature-card" href={feature.routeAnchor ?? '#dashboard'} key={feature.featureCode}>
              <strong>{feature.featureName}</strong>
              <span>{feature.description}</span>
            </a>
          ))}
        </div>
      </section>

      <section id="timesheet" className="panel timesheet-page">
        <div className="timesheet-toolbar">
          <div>
            <p className="eyebrow">Timesheet</p>
            <h2>Weekly time entry</h2>
            <DataState loading={timesheet.loading} error={timesheet.error}>
              <p className="muted week-range">Week starts: {timesheet.data?.weekStart} • Week ends: {timesheet.data?.weekEnd}</p>
            </DataState>
          </div>

          <div className="toolbar-actions">
            <button type="button" onClick={() => setSelectedWeekStart(addDaysIso(selectedWeekStart, -7))}>← Previous</button>
            <button type="button" onClick={() => setSelectedWeekStart(getSundayIso())}>Current week</button>
            <button type="button" onClick={() => setSelectedWeekStart(addDaysIso(selectedWeekStart, 7))}>Next →</button>
            <button type="button" onClick={resetTimesheet} disabled={!isAnyDayEditable || isSaving}>Reset</button>
            <button type="button" onClick={saveDraft} disabled={!isAnyDayEditable || isSaving}>Save draft</button>
            <button type="button" className="primary-action" onClick={handleSubmit} disabled={!isAnyDayEditable || isSaving}>Save week</button>
          </div>
        </div>

        <div className="timesheet-status-bar">
          <span className="pill">Status: {submissionStatus}</span>
          <span>Save: <strong>{saveStatus}</strong></span>
          <span>Normal: <strong>{formatNumber(normalTotal)}</strong></span>
          <span>Afterhours: <strong>{formatNumber(afterhoursTotal)}</strong></span>
          <span>Total: <strong>{formatNumber(grandTotal)}</strong></span>
          {currentTimesheetStatus === 'submitted' ? (
            <span className="unlock-message">Submitted days are locked individually. Open days remain editable.</span>
          ) : null}
        </div>

        <DataState loading={timesheet.loading} error={timesheet.error}>
          <div className="timesheet-workspace">
            <aside className="activities-panel" aria-label="Activities">
              <div className="panel-title-row">
                <h3>Activities</h3>
                <span>{activitySource === 'nonProject' ? categories.length : activitySource === 'openTasks' ? assignedOpenTasks.length : 0}</span>
              </div>

              <div className="activity-selector-row">


                <label htmlFor="activity-source">Activity type</label>
                <select
                  id="activity-source"
                  value={activitySource}
                  onChange={(event) => setActivitySource(event.target.value)}
                >
                  {activitySourceOptions.map((option) => (
                    <option value={option.key} key={option.key}>{option.label}</option>
                  ))}
                </select>
              </div>

              {activitySource === 'nonProject' ? (
                <div className="activity-group activity-results">
                  <h4>Non-project time</h4>
                  {categories.map((category) => {
                    const alreadyAdded = activeRows.some((row) => row.categoryCode === category.code);
                    return (
                      <button
                        className="activity-card"
                        type="button"
                        key={category.code}
                        disabled={alreadyAdded || !isAnyDayEditable}
                        onClick={() => addCategory(category)}
                      >
                        <strong>{category.name}</strong>
                        <span>{category.description ?? category.utilizationBucket}</span>
                        <small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>
                         <span className="default-toggle" onClick={(event) => { event.stopPropagation(); void setRowAsPersonalDefault(categoryToRow(category)); }}>Set as my default</span>
                      </button>
                    );
                  })}
                </div>
              ) : activitySource === 'openTasks' ? (
                <div className="activity-group activity-results">
                  <h4>Open tasks</h4>
                  {openTasks.loading ? <span className="muted">Loading assigned tasks...</span> : null}
                  {openTasks.error ? <span className="error-text">{openTasks.error}</span> : null}
                  {!openTasks.loading && !openTasks.error && assignedOpenTasks.length === 0 ? (
                    <div className="empty-activity-state">
                      <strong>{selectedActivitySource.emptyTitle}</strong>
                      <span>{selectedActivitySource.emptyDescription}</span>
                    </div>
                  ) : null}
                  {assignedOpenTasks.map((task) => {
                    const alreadyAdded = activeRows.some((row) => row.projectId === task.projectId && row.taskId === task.taskId);
                    return (
                      <button
                        className="activity-card"
                        type="button"
                        key={`${task.projectId}-${task.taskId}`}
                        disabled={alreadyAdded || !isAnyDayEditable}
                        onClick={() => addTask(task)}
                      >
                        <strong>{task.taskName}</strong>
                        <span>{task.projectCode} • {task.projectName}</span>
                        <small>{task.projectManagerName ? `PM: ${task.projectManagerName}` : 'Project task'}</small>
                        <small className="timesheet-task-detail-line">
                          {task.clientName ? <strong>Customer: {task.clientName}</strong> : null}
                          {task.assignedHours !== undefined ? <span>Assigned: {Number(task.assignedHours || 0).toFixed(2)} hrs</span> : null}
                          {task.usedHours !== undefined ? <span>Used: {Number(task.usedHours || 0).toFixed(2)} hrs</span> : null}
                          {task.remainingHours !== undefined ? <span>Left: {Number(task.remainingHours || 0).toFixed(2)} hrs</span> : null}
                        </small>
                      </button>
                    );
                  })}
                </div>
              ) : (
                <div className="empty-activity-state">
                  <strong>{selectedActivitySource.emptyTitle}</strong>
                  <span>{selectedActivitySource.emptyDescription}</span>
                </div>
              )}
            </aside>

            <p className="timesheet-mobile-hint">Tip: on smaller screens, swipe horizontally to view all days and actions.</p>
            <div className="entry-grid-wrap">
              <div className="entry-grid" role="table" aria-label="Weekly time entry grid">
                <div className="entry-grid-row entry-grid-header" role="row">
                  <div role="columnheader">State</div>
                  <div role="columnheader">Activity</div>
                  <div role="columnheader">Project / Description</div>
                  {days.map((day) => (
                    <div className="day-header" role="columnheader" key={day.date}>
                      <strong>{day.dayName.slice(0, 3)}</strong>
                      <span>{day.date.slice(5)}</span>
                      <em>N / AH</em>
                    </div>
                  ))}
                  <div role="columnheader">Total</div>
                  <div role="columnheader">Action</div>
                </div>

                {activeRows.map((row) => (
                  <div className="entry-grid-row" role="row" key={row.id}>
                    <div role="cell"><span className="state-dot">•</span> {getRowWorkflowState(row.id)}</div>
                    <div role="cell" className="activity-name">{row.activity}</div>
                    <div role="cell">{row.projectDescription}</div>
                    {days.map((day) => (
                      <div className="time-cell-pair" role="cell" key={`${row.id}-${day.date}`}>
                        {timeTypes.map((type) => {
                          const entry = getEntry(row.id, day.date, type.key);
                          const isSelected = selectedCell?.rowId === row.id && selectedCell?.date === day.date && selectedCell?.type === type.key;
                          const dayIsEditable = isDayEditable(day.date);
                          return (
                            <button
                              aria-label={`${row.activity} ${day.date} ${type.label}`}
                              className={isSelected ? 'time-entry-button selected-time-input' : 'time-entry-button'}
                              key={type.key}
                              type="button"
                              title={`${type.label}: ${formatHoursValue(entry.hours)} hours`}
                              onClick={() => openEntryDetails(row.id, day.date, type.key)}
                              disabled={false}
                            >
                              {formatHoursValue(entry.hours)}
                            </button>
                          );
                        })}
                      </div>
                    ))}
                    <div role="cell" className="row-total">{formatNumber(getRowTotal(row.id))}</div>
                    <div role="cell">
                      <div className="row-action-stack">
                        <button className="link-button" type="button" onClick={() => isRowPersonalDefault(row) ? void removeRowAsPersonalDefault(row) : void setRowAsPersonalDefault(row)}>
                          {isRowPersonalDefault(row) ? 'Remove default' : 'Set default'}
                        </button>
                        <button className="link-button" type="button" onClick={() => removeRow(row.id)} disabled={!isAnyDayEditable}>Remove</button>
                      </div>
                    </div>
                  </div>
                ))}

                <div className="entry-grid-row total-row" role="row">
                  <div role="cell">Total</div>
                  <div role="cell"></div>
                  <div role="cell"></div>
                  {days.map((day) => (
                    <div role="cell" key={`total-${day.date}`}>{formatNumber(getDayTotal(day.date))}</div>
                  ))}
                  <div role="cell">{formatNumber(grandTotal)}</div>
                  <div role="cell"></div>
                </div>
              </div>
            </div>
          </div>
        </DataState>
      </section>

      {selectedCell && selectedRow && selectedEntry ? (
        <div className="details-modal-backdrop" role="presentation" onMouseDown={(event) => {
          if (event.target === event.currentTarget) void closeEntryDetails({ autoSave: true });
        }}>
          <section className="details-modal" role="dialog" aria-modal="true" aria-label="Time entry details">
            <div className="modal-title-row">
              <div>
                <p className="eyebrow">Time entry details</p>
                <h2>{selectedRow.activity}</h2>
                <p className="muted small-text">
                  {selectedCell.date} • {selectedCell.type === 'afterhours' ? 'Afterhours' : 'Normal time'}
                </p>

                {getSelectedDayTotal() < 8 && (
                  <div className="day-total-warning-banner">
                    <strong>Daily minimum not met.</strong>
                    <span>
                      This day has {formatNumber(getSelectedDayTotal())} hour(s). Add {formatNumber(Math.max(0, 8 - getSelectedDayTotal()))} more hour(s) across any combination of project, service request, non-project, or approved time rows before submitting.
                    </span>
                  </div>
                )}
              </div>
              <div className="modal-actions">
                {selectedDayStatus?.status === 'submitted' ? (
                  <button type="button" className="unlock-action" onClick={unlockSelectedDay} disabled={isSaving || !selectedDayStatus?.canUnlock}>
                    Unlock this day
                  </button>
                ) : selectedEntryIsEditable ? (
                  <button type="button" className="primary-action" onClick={submitSelectedDay} disabled={isSaving || getSelectedDayTotal() < 8}>
                    Submit this day
                  </button>
                ) : (
                  <span className="read-only-pill">Read only</span>
                )}
                <button type="button" className="modal-close-button" onClick={() => void closeEntryDetails({ autoSave: true })}>Close</button>
              </div>
            </div>

            {getVacationHolidayReminder(selectedRow) ? (
                <div className="policy-reminder">{getVacationHolidayReminder(selectedRow)}</div>
              ) : null}

              <div className="detail-form modal-detail-form">
              <label>
                Hours
                <input
                  inputMode="decimal"
                  min="0"
                  step="0.25"
                  type="number"
                  value={selectedEntry.hours}
                  placeholder="0.00"
                  autoFocus
                  disabled={!selectedEntryIsEditable}
                  
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { hours: event.target.value })}
                />
              </label>
              <label>
                Description / comment
                <textarea
                  value={selectedEntry.comment}
                  placeholder="Enter the reportable comment for this time entry."
                  disabled={!selectedEntryIsEditable}
                  
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { comment: event.target.value })}
                />

                <div className="ai-time-suggestion-card">
                  <div>
                    <p className="eyebrow">AI description assistant</p>
                    <h3>Generate a customer-facing description</h3>
                    <p className="section-copy">
                      Type a rough work note first, such as what was reviewed, configured, tested, documented, coordinated, or troubleshot. AI will use your typed words as the primary context and can suggest only the description. It cannot change hours, submit time, create tasks, or modify allocations.
                    </p>
                  </div>

                  <div className="ai-time-suggestion-actions">
                    <button
                      type="button"
                      className="secondary-action"
                      onClick={generateAiTimeEntrySuggestion}
                      disabled={aiSuggestionState.loading || !selectedEntryIsEditable}
                    >
                      {aiSuggestionState.loading ? 'Generating...' : 'Generate AI suggestion'}
                    </button>

                    {aiSuggestionState.suggestion && (
                      <button type="button" className="primary-action" onClick={applyAiTimeEntrySuggestion}>
                        Use suggestion
                      </button>
                    )}
                  </div>

                  {aiSuggestionState.error && (
                    <p className="error-text">{aiSuggestionState.error}</p>
                  )}

                  {aiSuggestionState.warning && (
                    <p className="ai-suggestion-warning">{aiSuggestionState.warning}</p>
                  )}

                  {aiSuggestionState.suggestion && (
                    <div className="ai-suggestion-preview">
                      <strong>Suggested description</strong>
                      <p>{aiSuggestionState.suggestion}</p>
                      <small>
                        Provider: {aiSuggestionState.provider === 'claude' ? 'Claude' : 'Local template fallback'}
                      </small>
                    </div>
                  )}
                </div>

              </label>
              <label>
                Work location group
                <select
                  value={selectedEntry.workLocationGroupId}
                  disabled={!selectedEntryIsEditable}
                  
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationGroupId: event.target.value })}
                >
                  {(locationGroups.data?.groups ?? []).map((group) => (
                    <option value={group.id} key={group.id}>{group.name}</option>
                  ))}
                </select>
              </label>
              <label>
                Work location
                <select
                  value={selectedEntry.workLocationId}
                  disabled={!selectedEntryIsEditable}
                  
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationId: event.target.value })}
                >
                  {(locations.data?.locations ?? []).map((location) => (
                    <option value={location.id} key={location.id}>{location.name}</option>
                  ))}
                </select>
              </label>
            </div>

            <div className="day-submit-actions">
              <span>
                Day total: <strong>{formatNumber(getSelectedDayTotal())}</strong> / minimum 8.00 hours
              </span>
              {selectedDayStatus?.status === 'submitted' || !selectedEntryIsEditable ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}
              {isSaving ? <small className="modal-save-note">Saving...</small> : null}
            </div>
          </section>
        </div>
      ) : null}




      <section id="holiday-admin" className={`panel holiday-admin-panel ${canViewHolidayCalendar ? '' : 'access-hidden'}`}>
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Holiday administration</p>
            <h2>Holiday calendar</h2>
          </div>
          <span className="pill">{companyHolidays.data?.count ?? 0} holiday{(companyHolidays.data?.count ?? 0) === 1 ? '' : 's'} for {holidayUploadYear}</span>
        </div>

        <div className={`holiday-upload-grid ${canManageHolidays ? '' : 'holiday-upload-grid-readonly'}`}>
          <label>
            Year
            <select value={holidayUploadYear} onChange={(event) => void loadHolidayAdminYear(event.target.value)}>
              {holidayYearOptions.map((year) => <option value={year} key={year}>{year}</option>)}
            </select>
          </label>

          {canManageHolidays ? (
            <label>
              Upload CSV
              <input type="file" accept=".csv,text/csv" onChange={handleHolidayFileUpload} />
            </label>
          ) : null}
        </div>

        {canManageHolidays ? (
          <>
            <textarea
              className="holiday-upload-textarea"
              value={holidayUploadText}
              onChange={(event) => setHolidayUploadText(event.target.value)}
              placeholder="holiday_date,holiday_name,holiday_type,is_floating_holiday,auto_populate_hours
2026-01-01,New Year's Day,company_paid,false,8"
            />

            <div className="toolbar-actions holiday-upload-actions">
              <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
              <span className="muted">{holidayUploadStatus}</span>
            </div>
          </>
        ) : null}

        <div className="holiday-calendar-card-panel">
          <div className="holiday-calendar-card-header">
            <h3>Holidays {holidayUploadYear}</h3>
            <span>{companyHolidays.data?.count ?? 0} total</span>
          </div>

          {companyHolidays.loading ? <p className="muted">Loading holidays...</p> : null}
          {companyHolidays.error ? <p className="error-text">{companyHolidays.error}</p> : null}

          {!companyHolidays.loading && !companyHolidays.error && (companyHolidays.data?.holidays ?? []).length === 0 ? (
            <p className="muted">No holidays uploaded for {holidayUploadYear} yet.</p>
          ) : null}

          <div className="holiday-calendar-card-grid">
            {(companyHolidays.data?.holidays ?? []).map((holiday, index) => {
              const holidayDate = new Date(`${holiday.holidayDate}T00:00:00Z`);
              const monthName = holidayDate.toLocaleString('en-US', { month: 'long', timeZone: 'UTC' });
              const dayNumber = holidayDate.toLocaleString('en-US', { day: 'numeric', timeZone: 'UTC' });
              const weekdayName = holidayDate.toLocaleString('en-US', { weekday: 'long', timeZone: 'UTC' });
              const normalizedType = String(holiday.holidayType ?? 'holiday').replaceAll('_', ' ');

              return (
                <article className="holiday-calendar-card" key={`${holiday.holidayDate}-${holiday.holidayName}-${index}`}>
                  <div className="holiday-calendar-card-month">{monthName}</div>
                  <div className="holiday-calendar-card-day">{dayNumber}</div>
                  <div className="holiday-calendar-card-weekday">{weekdayName}</div>
                  <div className="holiday-calendar-card-name">{holiday.holidayName}</div>
                  <div className="holiday-calendar-card-meta">{normalizedType} • {formatNumber(holiday.autoPopulateHours)} hours</div>
                </article>
              );
            })}
          </div>
        </div>
      </section>
<section id="project-allocation-info" className="panel project-allocation-info-panel">
        <ProjectAllocationInfoPanel />
      </section>

      {(activeRoute === 'project-workspace' && canSeeAny(['VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="project-workspace" className="panel project-workspace-route-panel">
          <ProjectWorkspaceCenter />
        </section>
      ) : null}

      {(activeRoute === 'project-intake' && canSeeAny(['VIEW_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="project-intake" className="panel project-intake-route-panel">
          <ProjectIntakeCenter />
        </section>
      ) : null}

      {(activeRoute === 'time-compliance' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'VIEW_TIME_COMPLIANCE', 'VIEW_AUDIT_HISTORY'])) ? (
        <section id="time-compliance" className="panel time-compliance-route-panel">
          <TimeComplianceCenter />
        </section>
      ) : null}

      <section id="psa-modules" className={`panel module-foundation-panel ${canViewPsaModules ? '' : 'access-hidden'}`}>
        <div className="section-header compact">
          <div>
            <p className="eyebrow">PSA platform modules</p>
            <h2>Remaining sections foundation</h2>
            <p className="muted">These sections prepare the rest of Project Pulse beyond time entry: intake, project management, resource scheduling, expenses, invoicing, reporting, and administrative workflow.</p>
          </div>
          <span className="pill">Foundation ready</span>
        </div>

        {remainingModules.error ? <p className="error-text">{remainingModules.error}</p> : null}

        <div className="module-grid">
          <article className="module-card">
            <span className="status-label">Project intake</span>
            <strong>{intakeCount} request{intakeCount === 1 ? '' : 's'}</strong>
            <small>Sales handoff, client intake, project templates, and PM assignment.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Project management</span>
            <strong>{milestoneCount} milestones</strong>
            <small>{riskCount} tracked risk{riskCount === 1 ? '' : 's'} for project delivery governance.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Resource scheduling</span>
            <strong>{capacityCount} capacity rows</strong>
            <small>Weekly availability, assigned hours, and utilization capacity planning.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Expense management</span>
            <strong>{expenseCount} report{expenseCount === 1 ? '' : 's'}</strong>
            <small>Expense report shell, receipt tracking, reimbursable expenses, and approval state.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Invoicing</span>
            <strong>{invoiceCount} invoice{invoiceCount === 1 ? '' : 's'}</strong>
            <small>Draft client invoice staging for labor, expenses, export, and accounting review.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Executive reporting</span>
            <strong>{executiveMetricCount} metrics</strong>
            <small>Snapshot-based executive dashboard foundation for operational reporting.</small>
          </article>
        </div>

        <div className="module-detail-grid">
          <article>
            <h3>Project milestones</h3>
            {(moduleData.projectManagement?.milestones ?? []).slice(0, 7).map((milestone) => (
              <div className="module-list-row" key={`${milestone.projectCode}-${milestone.name}`}>
                <strong>{milestone.name}</strong>
                <span>{milestone.projectCode} • {milestone.status} • due {milestone.dueDate ?? 'TBD'}</span>
              </div>
            ))}
          </article>
          <article>
            <h3>Resource capacity</h3>
            {(moduleData.resourceCapacity?.capacity ?? []).map((capacity) => (
              <div className="module-list-row" key={`${capacity.resourceEmail}-${capacity.weekStart}`}>
                <strong>{capacity.resourceName}</strong>
                <span>{capacity.weekStart}: {formatNumber(capacity.assignedHours)} assigned / {formatNumber(capacity.availableHours)} available • {capacity.status}</span>
              </div>
            ))}
          </article>
        </div>
      </section>


      <section id="current-quarter-utilization" className="panel current-quarter-utilization-panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Current quarter utilization</p>
            <h2>{currentQuarterUtilization.data?.quarter ?? 'Current Quarter'}</h2>
            <p className="section-copy">Track target, current utilization, and remaining billable hours needed to meet the quarter goal.</p>
          </div>
          <span className="badge">{currentQuarterUtilization.data?.targetPercent ?? 0}% target</span>
        </div>

        <DataState loading={currentQuarterUtilization.loading} error={currentQuarterUtilization.error}>
          <div className="quarter-utilization-grid">
            <article className="quarter-utilization-card">
              <span>Target utilization</span>
              <strong>{currentQuarterUtilization.data?.targetPercent ?? 0}%</strong>
              <small>{formatNumber(currentQuarterUtilization.data?.targetHours)} target hrs</small>
            </article>

            <article className="quarter-utilization-card">
              <span>Current utilization</span>
              <strong>{formatNumber(currentQuarterUtilization.data?.currentUtilizationPercent)}%</strong>
              <small>{formatNumber(currentQuarterUtilization.data?.currentBillableHours)} billable hrs</small>
            </article>

            <article className="quarter-utilization-card">
              <span>Hours left to target</span>
              <strong>{formatNumber(currentQuarterUtilization.data?.hoursLeftToTarget)}</strong>
              <small>remaining billable hrs</small>
            </article>

            <article className="quarter-utilization-card">
              <span>Standard quarter</span>
              <strong>{formatNumber(currentQuarterUtilization.data?.standardPeriodHours)}</strong>
              <small>standard hrs</small>
            </article>
          </div>
        </DataState>
      </section>

      <section id="utilization" className="panel">
        <YearlyUtilizationPanel />
        <ManagerTeamUtilizationPanel />
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Utilization policy</p>
            <h2>Quarterly targets</h2>
          </div>
          <DataState loading={utilizationPolicies.loading} error={utilizationPolicies.error}>
            <span className="pill">{activePolicy?.standardPeriodHours ?? 0} standard hours</span>
          </DataState>
        </div>

        <DataState loading={utilizationTargets.loading} error={utilizationTargets.error}>
          <div className="target-grid">
            {utilizationTargets.data?.targets?.map((target) => (
              <article className="target-card" key={target.targetPercent}>
                <strong>{Number(target.targetPercent).toFixed(0)}%</strong>
                <span>{Number(target.targetHours).toFixed(1)} hrs</span>
                <small>{target.targetHours ? `${Number(target.targetHours).toLocaleString()} target hrs` : 'No target hours'}</small>
              </article>
            ))}
          </div>
        </DataState>
      </section>


      {(hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL')) ? (
        <section id="role-admin" className="panel role-admin-panel">
          <div className="section-header compact">
            <div>
              <p className="eyebrow">Administration</p>
              <h2>User role administration</h2>
              <p className="muted">Assign each user to the workspace role that controls their available views and actions. PMO and PM/Project Manager have been consolidated as Project Management.</p>
            </div>
            <span className="pill">{roleAdminUsers.data?.count ?? 0} users</span>
          </div>

          {roleAdminUsers.error || roleAdminRoles.error ? <p className="error-text">{roleAdminUsers.error ?? roleAdminRoles.error}</p> : null}
          <p className="muted">{roleAdminStatus}</p>

          <div className="role-admin-table" role="table" aria-label="User role assignments">
            <div className="role-admin-row role-admin-header" role="row">
              <div role="columnheader">User</div>
              <div role="columnheader">Current role</div>
              <div role="columnheader">Assign role</div>
            </div>
            {(roleAdminUsers.data?.users ?? []).map((user) => (
              <div className="role-admin-row" role="row" key={user.email}>
                <div role="cell">
                  <strong>{user.displayName}</strong>
                  <span>{user.email}</span>
                  <small>{user.jobTitle || 'No title'}{user.department ? ` • ${user.department}` : ''}</small>
                </div>
                <div role="cell">
                  <span className="role-chip">{user.roleNames?.length ? user.roleNames.join(', ') : 'No role assigned'}</span>
                </div>
                <div role="cell">
                  <select
                    value={user.roleCodes?.[0] ?? ''}
                    onChange={(event) => void updateUserRole(user.email, event.target.value)}
                  >
                    <option value="" disabled>Select role</option>
                    {(roleAdminRoles.data?.roles ?? []).map((role) => (
                      <option value={role.roleCode} key={role.roleCode}>{role.roleName}</option>
                    ))}
                  </select>
                </div>
              </div>
            ))}
          </div>
        </section>
      ) : null}

      {(canViewManagerApprovalPanel || canViewLocalAdminPasswordResetApprovals) ? (
        <section id="manager-approval" className="approvals-workspace-panel">
          {canViewManagerApprovalPanel ? <ManagerApprovalPanel /> : null}
          {canViewLocalAdminPasswordResetApprovals ? <LocalAdminPasswordResetApprovalsPanel /> : null}
        </section>
      ) : null}

      <section id="workflow" className="section-header">
        <h2>Core workflow areas</h2>
        <p>These modules reflect the approved platform direction and will be implemented incrementally.</p>
      </section>

      <section className="module-grid" aria-label="Core workflow modules">
        {workflowCards.map((card) => (
          <article className="module-card" key={card.title}>
            <div>
              <h3>{card.title}</h3>
              <p>{card.description}</p>
            </div>
            <span>{card.status}</span>
          </article>
        ))}
      </section>
</main>
  );
}

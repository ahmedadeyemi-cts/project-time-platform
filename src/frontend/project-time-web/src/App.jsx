import HelpAssistant from './HelpAssistant.jsx';
import SessionIntelligenceDrawer from './SessionIntelligenceDrawer.jsx';
import ProfileIdentitySurface from './identity/ProfileIdentitySurface.jsx';
import ApprovalMailbox from './ApprovalMailbox.jsx';
import NativeModuleAdministrationPanel from './NativeModuleAdministrationPanel.jsx';
import CrmErpIntegrationCenter from './CrmErpIntegrationCenter.jsx';
import {
  compareProjectPulseModules,
  sortProjectPulseModules
} from './module-ordering.js';

const MODULE_064_074_NATIVE_ADMINISTRATION_ROUTES = Object.freeze({
  'ai-provider-configuration': '064',
  'entra-secret-administration': '065',
  'global-mail-configuration': '067',
  'system-architecture': '068',
  'qualifications-certifications': '069',
  'capacity-pipeline-forecast': '070',
  'sales-coverage-alignment': '073',
  'oem-vendor-directory': '074'
});

const MODULE_002_APPROVAL_ROLE_CODES = Object.freeze([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR',
  'MANAGER',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT'
]);

import OpportunitiesCenter from './OpportunitiesCenter.jsx';
import SystemUserGuide from './SystemUserGuide.jsx';
import PostIntakeAgingPanel from './PostIntakeAgingPanel.jsx';



/* 050A_BROWSER_API_SESSION_HEADER_BRIDGE_START */
function getProjectPulse050ABrowserSessionToken() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');

    return session?.sessionToken
      || session?.token
      || session?.accessToken
      || '';
  } catch {
    return '';
  }
}

function isProjectPulse050AApiRequest(input) {
  try {
    const rawUrl = typeof input === 'string' ? input : input?.url;

    if (!rawUrl) return false;

    const url = new URL(rawUrl, window.location.origin);

    return url.origin === window.location.origin && url.pathname.startsWith('/api/');
  } catch {
    return false;
  }
}

function installProjectPulse050ABrowserApiSessionHeaderBridge() {
  if (typeof window === 'undefined' || window.__projectPulse050ABrowserApiSessionHeaderBridgeInstalled) {
    return;
  }

  const nativeFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    if (!isProjectPulse050AApiRequest(input)) {
      return nativeFetch(input, init);
    }

    const token = getProjectPulse050ABrowserSessionToken();

    if (!token) {
      return nativeFetch(input, init);
    }

    const headers = new Headers(init?.headers || (input instanceof Request ? input.headers : undefined));

    if (!headers.has('X-ProjectPulse-Session')) {
      headers.set('X-ProjectPulse-Session', token);
    }

    if (!headers.has('X-Project-Pulse-Session')) {
      headers.set('X-Project-Pulse-Session', token);
    }

    if (!headers.has('X-Session-Token')) {
      headers.set('X-Session-Token', token);
    }

    if (!headers.has('Authorization')) {
      headers.set('Authorization', `Bearer ${token}`);
    }

    return nativeFetch(input, {
      ...init,
      headers
    });
  };

  window.__projectPulse050ABrowserApiSessionHeaderBridgeInstalled = true;
}

installProjectPulse050ABrowserApiSessionHeaderBridge();
/* 050A_BROWSER_API_SESSION_HEADER_BRIDGE_END */
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
        top: 4.85rem;
        right: 1.1rem;
        z-index: 9999;
        display: flex;
        align-items: center;
        gap: 0.45rem;
        max-width: min(620px, calc(100vw - 2rem));
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
    setTimeout(loadUsers, 250);
    setTimeout(loadUsers, 1200);
    setTimeout(loadUsers, 3000);
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

  window.addEventListener('hashchange', loadUsers);
  window.addEventListener('projectpulse:auth-session-ready', loadUsers);
}

installProjectPulseGlobalViewAsPreview();

// 022F_TOPBAR_VIEWAS_MOUNT_START
function installProjectPulseGlobalViewAsTopbarMount() {
  if (window.__projectPulseGlobalViewAsTopbarMountInstalled) return;
  window.__projectPulseGlobalViewAsTopbarMountInstalled = true;

  const slotId = 'projectpulse-global-view-as-topbar-slot';

  const cleanText = (value) => String(value || '').replace(/\s+/g, ' ').trim();

  const isVisible = (element) => {
    if (!element || !element.isConnected) return false;
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const findTopbar = () => {
    const candidates = Array.from(document.querySelectorAll('header, nav, section, div'))
      .filter((element) => {
        if (element.id === slotId || element.closest(`#${slotId}`)) return false;

        const text = cleanText(element.textContent);
        if (!text.includes('Project Health Dashboard')) return false;
        if (!text.includes('Dashboard')) return false;
        if (!text.includes('More')) return false;

        const rect = element.getBoundingClientRect();
        return rect.top >= 0 && rect.top < 170 && rect.width > 800 && rect.height >= 48 && rect.height <= 150;
      })
      .sort((a, b) => {
        const ar = a.getBoundingClientRect();
        const br = b.getBoundingClientRect();
        return (ar.height - br.height) || (cleanText(a.textContent).length - cleanText(b.textContent).length);
      });

    return candidates[0] || null;
  };

  const findRightNavCluster = (topbar) => {
    if (!topbar) return null;

    const buttons = Array.from(topbar.querySelectorAll('button, a, [role="button"], div'))
      .filter(isVisible);

    const more = buttons.find((button) => cleanText(button.textContent).includes('More'));
    if (more) return more.closest('div') || more;

    const dashboard = buttons.find((button) => cleanText(button.textContent) === 'Dashboard');
    if (dashboard) return dashboard.closest('div') || dashboard;

    return null;
  };

  const ensureSlot = () => {
    const topbar = findTopbar();
    if (!topbar) return null;

    let slot = document.getElementById(slotId);
    if (!slot) {
      slot = document.createElement('div');
      slot.id = slotId;
    }

    const rightCluster = findRightNavCluster(topbar);

    if (rightCluster?.parentElement && rightCluster.parentElement !== slot.parentElement) {
      rightCluster.parentElement.insertBefore(slot, rightCluster);
    } else if (!slot.parentElement) {
      topbar.appendChild(slot);
    }

    return slot;
  };

  const mount = () => {
    const viewAs = document.getElementById('projectpulse-global-view-as');
    if (!viewAs) return;

    const slot = ensureSlot();
    if (!slot) return;

    if (viewAs.parentElement !== slot) {
      slot.appendChild(viewAs);
    }

    viewAs.setAttribute('data-topbar-mounted', 'true');
  };

  const run = () => {
    mount();
    setTimeout(mount, 100);
    setTimeout(mount, 500);
    setTimeout(mount, 1200);
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', run);
  } else {
    run();
  }

  window.addEventListener('hashchange', run);
  window.addEventListener('resize', run);
  window.addEventListener('projectpulse:view-as-changed', run);

  const observer = new MutationObserver(() => {
    clearTimeout(window.__projectPulseGlobalViewAsTopbarMountTimer);
    window.__projectPulseGlobalViewAsTopbarMountTimer = setTimeout(mount, 80);
  });

  if (document.body) {
    observer.observe(document.body, { childList: true, subtree: true });
  }
}

installProjectPulseGlobalViewAsTopbarMount();

/* MODULE_059_LEGACY_EFFECTIVE_SESSION_WIDGET_REMOVED_START */
/* Backend /api/security/effective-session remains available to Module 059. */
/* MODULE_059_LEGACY_EFFECTIVE_SESSION_WIDGET_REMOVED_END */

// 022F_TOPBAR_VIEWAS_MOUNT_END

// 022E_REAL_VIEWAS_PLACEMENT_CONFIRMED: placement controlled by timesheet.css top-bar override.



import { useEffect, useLayoutEffect, useMemo, useState, useRef } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import './timesheet.css';
import './mobile-readiness.css';
import UserAdministrationPanel from './UserAdministrationPanel.jsx';
import YearlyUtilizationPanel from './YearlyUtilizationPanel.jsx';
import ProjectAllocationInfoPanel from './ProjectAllocationInfoPanel.jsx';
import ManagerTeamUtilizationPanel from './ManagerTeamUtilizationPanel.jsx';
import ApprovalCenter from './ApprovalCenter.jsx';
import AuditHistoryPanel from './AuditHistoryPanel.jsx';
import ApprovalExportAuditWorkflowCenter from './ApprovalExportAuditWorkflowCenter.jsx';
import ServiceControlCenter from './ServiceControlCenter.jsx';
import BackupDrCenter from './BackupDrCenter.jsx';
import ReplicationSyncStatusCenter from './ReplicationSyncStatusCenter.jsx';
import RestoreValidationCenter from './RestoreValidationCenter.jsx';
import BackupRetentionCenter from './BackupRetentionCenter.jsx';
import TimeComplianceCenter from './TimeComplianceCenter.jsx';
import ProjectIntakeCenter from './ProjectIntakeCenter.jsx';
import SalesInsightsDashboard from './SalesInsightsDashboard.jsx';
import CertifyIntegrationCenter from './CertifyIntegrationCenter.jsx';
import BillingReadinessCenter from './BillingReadinessCenter.jsx';
import ProjectCloseoutCenter from './ProjectCloseoutCenter.jsx';
import CloseoutEmailAutomationCenter from './CloseoutEmailAutomationCenter.jsx';
import InvoiceBillingCenter from './InvoiceBillingCenter.jsx';
import CalendarCapacityCenter from './CalendarCapacityCenter.jsx';
import SecurityOperationsResponseCenter from './SecurityOperationsResponseCenter.jsx';
import CiCdPipelineCenter from './CiCdPipelineCenter.jsx';
import CustomerDirectoryCenter from './CustomerDirectoryCenter.jsx';
import ContractsCenter from './ContractsCenter.jsx';
import RateCardAdministrationCenter from './RateCardAdministrationCenter.jsx';
import WorkRegisterCenter from './WorkRegisterCenter.jsx';
import CostOverrunAlertCenter from './CostOverrunAlertCenter.jsx';
import ProjectWorkspaceCenter from './ProjectWorkspaceCenter.jsx';
import ProjectFlowHiveCenter from './ProjectFlowHiveCenter.jsx';
import AiProviderConfigurationCenter from './AiProviderConfigurationCenter.jsx';
import EntraSecretAdministrationCenter from './EntraSecretAdministrationCenter.jsx';
import GlobalMailConfigurationCenter from './GlobalMailConfigurationCenter.jsx';
import SystemArchitectureCenter from './SystemArchitectureCenter.jsx';
import QualificationsCertificationCenter from './QualificationsCertificationCenter.jsx';
import CapacityPipelineForecastCenter from './CapacityPipelineForecastCenter.jsx';
import OnCallSchedulingCenter from './OnCallSchedulingCenter.jsx';
import OneAssistRoutingDirectoryCenter from './OneAssistRoutingDirectoryCenter.jsx';
import SalesCoverageAlignmentCenter from './SalesCoverageAlignmentCenter.jsx';
import OemVendorDirectoryCenter from './OemVendorDirectoryCenter.jsx';
import SystemDiagnosticRemediationCenter from './SystemDiagnosticRemediationCenter.jsx';
import DefectTrackerCenter from './DefectTrackerCenter.jsx';
import IntegrationEventGatewayCenter from './IntegrationEventGatewayCenter.jsx';
import ReleaseDeploymentControlCenter from './ReleaseDeploymentControlCenter.jsx';
import ObservabilitySloHealthCenter from './ObservabilitySloHealthCenter.jsx';
import DataGovernanceRetentionCenter from './DataGovernanceRetentionCenter.jsx';
import CustomerDeliveryAcceptanceCenter from './CustomerDeliveryAcceptanceCenter.jsx';
import ProjectManagerWorkloadCenter from './ProjectManagerWorkloadCenter.jsx';
import EngineeringTeamLeadUtilizationPanel from './EngineeringTeamLeadUtilizationPanel.jsx';
import WorkTaskBuilderPanel from './WorkTaskBuilderPanel.jsx';
import RoleAdminDirectoryPanel from './RoleAdminDirectoryPanel.jsx';
import RolesPermissionsMatrix from './RolesPermissionsMatrix.jsx';
import IntakeWorkTaskHandoffPanel from './IntakeWorkTaskHandoffPanel.jsx';
import ResourceAssignmentHandoffPanel from './ResourceAssignmentHandoffPanel.jsx';

import './production-workflow-operations.css';
import './local-admin-password-reset-clear-actions.css';
import './production-operations-acknowledgments.css';
import './time-compliance-email-notifications.css';
import './production-readiness-center.css';
import './production-data-readiness-center.css';
import './page-context-guide.css';
import ProductionOperationsPanel from './ProductionOperationsPanel.jsx';
import LocalAdminPasswordResetClearActions from './LocalAdminPasswordResetClearActions.jsx';
import ProductionOperationsAcknowledgmentsPanel from './ProductionOperationsAcknowledgmentsPanel.jsx';
import TimeComplianceEmailNotificationsPanel from './TimeComplianceEmailNotificationsPanel.jsx';
import ProductionReadinessCenterPanel from './ProductionReadinessCenterPanel.jsx';
import ProductionDataReadinessCenter from './ProductionDataReadinessCenter.jsx';
import PageContextGuide from './PageContextGuide.jsx';
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
    emptyDescription: 'Assigned Project and IQS tasks appear here.'
  },
  {
    key: 'requests',
    label: 'Requests / Service Requests',
    emptyTitle: 'No requests available.',
    emptyDescription: 'Assigned Service Request, Pre-sales, Internal Project, and Other tasks appear here.'
  }
];

function projectPulseTaskTimeEntrySection(task = {}) {
  const explicitSection = String(task.timeEntrySection || task.time_entry_section || '').trim().toLowerCase();
  if (explicitSection === 'requests') return 'requests';
  if (explicitSection === 'regular') return 'regular';

  const workType = String(task.workType || task.work_type || 'Project')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '');

  return workType === 'project' || workType === 'iqs' ? 'regular' : 'requests';
}


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
    headers: {
      ...getProjectPulse051CActiveSessionHeaders(typeof authSession !== 'undefined' ? authSession : null),
      'Content-Type': 'application/json', ...getProjectPulseAuthHeaders(sessionOverride) },
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

/* 039A_ROUTE_REFRESH_RESTORE_START */
function installProjectPulseManualScrollRestoration() {
  try {
    if ('scrollRestoration' in window.history) {
      window.history.scrollRestoration = 'manual';
    }
  } catch {
    // Browser does not support manual scroll restoration or blocked access.
  }
}

function resetProjectPulseViewportForRoute(route = getRouteFromHash()) {
  const normalizedRoute = String(route || 'dashboard').replace('#', '') || 'dashboard';

  const resetNow = () => {
    try {
      window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
    } catch {
      window.scrollTo(0, 0);
    }

    const possibleScrollTargets = [
      document.scrollingElement,
      document.documentElement,
      document.body,
      document.querySelector('.app-shell'),
      document.querySelector('.app-layout'),
      document.querySelector('.app-main'),
      document.querySelector('.workspace-shell'),
      document.querySelector('.workspace-body'),
      document.querySelector('.workspace-content'),
      document.querySelector('.installed-modules-dashboard-panel')
    ].filter(Boolean);

    possibleScrollTargets.forEach((target) => {
      try {
        target.scrollTop = 0;
        target.scrollLeft = 0;
      } catch {
        // Ignore non-scrollable targets.
      }
    });

    const activePanel = normalizedRoute === 'dashboard'
      ? document.querySelector('.installed-modules-dashboard-panel')
      : document.getElementById(normalizedRoute);

    if (activePanel) {
      try {
        activePanel.scrollIntoView({ block: 'start', inline: 'nearest', behavior: 'auto' });
        window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
      } catch {
        // Ignore scrollIntoView edge cases.
      }
    }
  };

  window.requestAnimationFrame(() => {
    resetNow();
    window.requestAnimationFrame(resetNow);
  });
}

installProjectPulseManualScrollRestoration();
/* 039A_ROUTE_REFRESH_RESTORE_END */

/* 039C_APPROVAL_INDICATOR_FIX_START */
function getProjectPulseCountAfterLabel(text, labels) {
  const normalizedText = String(text || '').replace(/\s+/g, ' ');

  for (const label of labels) {
    const escapedLabel = String(label).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const match = normalizedText.match(new RegExp(`${escapedLabel}\\s*[:\\-]?\\s*(\\d+)`, 'i'));
    if (match) return Number(match[1] || 0);
  }

  return 0;
}

function findProjectPulseSmallestContainerWithText(requiredTextValues) {
  const required = requiredTextValues.map((value) => String(value).toLowerCase());

  return [...document.querySelectorAll('section, article, div')]
    .filter((element) => {
      const text = String(element.textContent || '').toLowerCase();
      return required.every((value) => text.includes(value));
    })
    .sort((a, b) => String(a.textContent || '').length - String(b.textContent || '').length)[0] || null;
}

function getProjectPulseActionableApprovalCounts() {
  /* 039E_ACTIONABLE_APPROVAL_COUNT_DOM_CACHE */
  const cachedCounts = getProjectPulseCachedApprovalActionableCounts();
  if (cachedCounts) {
    return {
      submittedTimePending: Number(cachedCounts.submittedTimePending ?? 0),
      localResetPendingApproval: Number(cachedCounts.localResetPendingApproval ?? 0),
      localResetReadyForTempPassword: Number(cachedCounts.localResetReadyForTempPassword ?? 0),
      actionableTotal: Number(cachedCounts.actionableTotal ?? 0)
    };
  }

  const managerPanel = document.getElementById('manager-approval');

  const submittedTimePanel =
    findProjectPulseSmallestContainerWithText(['Submitted time awaiting review', 'Pending:'])
    || managerPanel;

  const localAdminPanel =
    findProjectPulseSmallestContainerWithText(['Local admin password reset approvals', 'Pending approval'])
    || managerPanel;

  const submittedTimeText = submittedTimePanel?.textContent || '';
  const localAdminText = localAdminPanel?.textContent || '';

  const submittedTimePending = getProjectPulseCountAfterLabel(submittedTimeText, [
    'Submitted time pending',
    'Pending'
  ]);

  const localResetPendingApproval = getProjectPulseCountAfterLabel(localAdminText, [
    'Pending approval'
  ]);

  const localResetReadyForTempPassword = getProjectPulseCountAfterLabel(localAdminText, [
    'Ready for temp password',
    'Ready for temp-password'
  ]);

  return {
    submittedTimePending,
    localResetPendingApproval,
    localResetReadyForTempPassword,
    actionableTotal: submittedTimePending + localResetPendingApproval + localResetReadyForTempPassword
  };
}

function getProjectPulseApprovalDashboardCard() {
  return [...document.querySelectorAll('article, section, div')]
    .filter((element) => {
      const text = String(element.textContent || '');
      return text.includes('MODULE 002') && text.includes('Approval Inbox');
    })
    .sort((a, b) => String(a.textContent || '').length - String(b.textContent || '').length)[0] || null;
}

function getOrCreateProjectPulseApprovalBadge(container) {
  if (!container) return null;

  let badge = container.querySelector('[data-project-pulse-approval-actionable-badge="true"]');

  if (!badge) {
    badge = [...container.querySelectorAll('span, strong, small, div')]
      .filter((element) => /^\s*\d+\s*$/.test(String(element.textContent || '')))
      .filter((element) => String(element.textContent || '').trim() !== '002')
      .sort((a, b) => String(a.textContent || '').length - String(b.textContent || '').length)[0] || null;
  }

  if (!badge) {
    badge = document.createElement('span');
    const moduleLabel = [...container.querySelectorAll('span, strong, small, p, div')]
      .find((element) => String(element.textContent || '').trim() === 'MODULE 002');

    if (moduleLabel?.parentElement) {
      moduleLabel.parentElement.insertBefore(badge, moduleLabel.nextSibling);
    } else {
      container.insertBefore(badge, container.firstChild);
    }
  }

  badge.dataset.projectPulseApprovalActionableBadge = 'true';
  badge.classList.add('project-pulse-approval-actionable-badge');
  return badge;
}

function normalizeProjectPulseApprovalBadge() {
  const counts = getProjectPulseActionableApprovalCounts();

  const dashboardCard =
    document.querySelector('a.role-dashboard-card[href="#manager-approval"]') ||
    document.querySelector('a[href="#manager-approval"]') ||
    getProjectPulseApprovalDashboardCard();

  const managerLinks = [...document.querySelectorAll('a[href="#manager-approval"]')];

  if (dashboardCard) {
    dashboardCard.classList.toggle('project-pulse-approval-card-has-pending', counts.actionableTotal > 0);

    const staleBadges = [...dashboardCard.querySelectorAll('.nav-pending-badge, [data-project-pulse-approval-actionable-badge="true"]')];

    if (counts.actionableTotal <= 0) {
      staleBadges.forEach((badge) => {
        badge.textContent = '';
        badge.classList.add('is-hidden');
        badge.setAttribute('aria-hidden', 'true');
        badge.style.display = 'none';
      });
    } else {
      const badge = staleBadges[0] || getOrCreateProjectPulseApprovalBadge(dashboardCard);
      if (badge) {
        badge.dataset.projectPulseApprovalActionableBadge = 'true';
        badge.classList.add('project-pulse-approval-actionable-badge');
        badge.classList.remove('is-hidden');
        badge.style.display = '';
        badge.textContent = String(counts.actionableTotal);
        badge.setAttribute('aria-hidden', 'false');
        badge.setAttribute('aria-label', `${counts.actionableTotal} actionable approval item(s)`);
      }
    }
  }

  managerLinks.forEach((link) => {
    let badge = link.querySelector('[data-project-pulse-approval-actionable-badge="true"]');

    if (!badge && counts.actionableTotal > 0) {
      badge = document.createElement('span');
      badge.dataset.projectPulseApprovalActionableBadge = 'true';
      badge.className = 'project-pulse-approval-actionable-badge nav';
      link.appendChild(badge);
    }

    if (badge) {
      badge.textContent = counts.actionableTotal > 0 ? String(counts.actionableTotal) : '';
      badge.classList.toggle('is-hidden', counts.actionableTotal <= 0);
      badge.setAttribute('aria-hidden', counts.actionableTotal > 0 ? 'false' : 'true');
      badge.style.display = counts.actionableTotal > 0 ? '' : 'none';
    }
  });

  return counts;
}

function normalizeProjectPulseResetQueuePanel() {
  const counts = getProjectPulseActionableApprovalCounts();

  const clearButtons = [...document.querySelectorAll('button')]
    .filter((button) => String(button.textContent || '').toLowerCase().includes('clear ready reset queue'));

  const panels = clearButtons.map((button) => {
    let best = button;

    for (let index = 0; index < 8 && best?.parentElement; index += 1) {
      const parent = best.parentElement;
      const text = String(parent.textContent || '').toLowerCase();
      const style = window.getComputedStyle(parent);
      const rect = parent.getBoundingClientRect();

      best = parent;

      if (
        text.includes('reset queue') ||
        text.includes('ready temp') ||
        style.position === 'fixed' ||
        style.position === 'absolute' ||
        rect.width >= 260
      ) {
        break;
      }
    }

    return best;
  }).filter(Boolean);

  const legacyPanel = findProjectPulseSmallestContainerWithText([
    'Clear ready temp-password requests',
    'Total local reset requests'
  ]);

  if (legacyPanel) panels.push(legacyPanel);

  const uniquePanels = [...new Set(panels)];

  const hasActionableResetWork =
    Number(counts.localResetPendingApproval || 0) > 0 ||
    Number(counts.localResetReadyForTempPassword || 0) > 0;

  uniquePanels.forEach((panel) => {
    panel.classList.add('project-pulse-reset-queue-panel');
    panel.classList.toggle('is-hidden', !hasActionableResetWork);
    panel.setAttribute('aria-hidden', hasActionableResetWork ? 'false' : 'true');

    if (!hasActionableResetWork) {
      panel.style.display = 'none';
      panel.style.visibility = 'hidden';
      panel.style.pointerEvents = 'none';
    } else {
      panel.style.display = '';
      panel.style.visibility = '';
      panel.style.pointerEvents = '';
    }
  });

  return counts;
}

/* 040A_APPROVAL_NORMALIZER_ROUTE_SCOPE_START */
function shouldNormalizeProjectPulseApprovalUiForCurrentRoute() {
  const route = String(getRouteFromHash() || 'dashboard').replace('#', '') || 'dashboard';

  return route === 'dashboard' || route === 'manager-approval';
}

function normalizeProjectPulseApprovalUi() {
  if (!shouldNormalizeProjectPulseApprovalUiForCurrentRoute()) return;

  normalizeProjectPulseApprovalBadge();
  normalizeProjectPulseResetQueuePanel();
}
/* 040A_APPROVAL_NORMALIZER_ROUTE_SCOPE_END */

function installProjectPulseApprovalUiNormalizer() {
  if (window.__projectPulseApprovalUiNormalizerInstalled) return;
  window.__projectPulseApprovalUiNormalizerInstalled = true;

  const run = () => {
    if (!shouldNormalizeProjectPulseApprovalUiForCurrentRoute()) return;
    window.requestAnimationFrame(normalizeProjectPulseApprovalUi);
  };

  run();
  window.setTimeout(run, 250);
  window.setTimeout(run, 750);
  window.setTimeout(run, 1500);

  window.addEventListener('hashchange', run);
  window.addEventListener('pageshow', run);
  window.addEventListener('focus', run);

  try {
    const observer = new MutationObserver(() => run());
    observer.observe(document.body, {
      childList: true,
      subtree: true,
      characterData: true
    });
    window.__projectPulseApprovalUiNormalizerObserver = observer;
  } catch {
    window.setInterval(run, 2000);
  }
}
/* 039C_APPROVAL_INDICATOR_FIX_END */

/* 039E_ACTIONABLE_APPROVAL_COUNT_START */
function normalizeProjectPulseApprovalStatus(value) {
  return String(value ?? '')
    .trim()
    .toLowerCase()
    .replaceAll('-', '_')
    .replaceAll(' ', '_');
}

function isProjectPulseClosedApprovalStatus(status) {
  const normalized = normalizeProjectPulseApprovalStatus(status);
  return [
    'approved',
    'declined',
    'rejected',
    'completed',
    'complete',
    'closed',
    'resolved',
    'cancelled',
    'canceled',
    'expired',
    'ready',
    'draft'
  ].includes(normalized);
}

function objectLooksLikeProjectPulseTimeApproval(item) {
  const haystack = Object.keys(item || {}).join(' ').toLowerCase();
  return (
    haystack.includes('workdate') ||
    haystack.includes('work_date') ||
    haystack.includes('timesheet') ||
    haystack.includes('timeentry') ||
    haystack.includes('time_entry') ||
    haystack.includes('submitted')
  );
}

function objectLooksLikeProjectPulseLocalReset(item) {
  const haystack = [
    ...Object.keys(item || {}),
    item?.requestType,
    item?.type,
    item?.category,
    item?.queueName,
    item?.approvalType,
    item?.username,
    item?.userName,
    item?.localUsername,
    item?.message,
    item?.notes
  ].join(' ').toLowerCase();

  return (
    haystack.includes('password') ||
    haystack.includes('reset') ||
    haystack.includes('temp') ||
    haystack.includes('local')
  );
}

function collectProjectPulseObjects(payload, collector = []) {
  if (!payload || typeof payload !== 'object') return collector;

  if (Array.isArray(payload)) {
    payload.forEach((item) => collectProjectPulseObjects(item, collector));
    return collector;
  }

  collector.push(payload);

  Object.values(payload).forEach((value) => {
    if (value && typeof value === 'object') {
      collectProjectPulseObjects(value, collector);
    }
  });

  return collector;
}

function readProjectPulseNumericFields(payload, patterns, exclusions = []) {
  let total = 0;

  function visit(value, path = '') {
    if (!value || typeof value !== 'object') return;

    if (Array.isArray(value)) {
      value.forEach((item, index) => visit(item, `${path}.${index}`));
      return;
    }

    Object.entries(value).forEach(([key, entryValue]) => {
      const normalizedKey = String(key || '').toLowerCase();
      const fullPath = `${path}.${normalizedKey}`;
      const excluded = exclusions.some((pattern) => pattern.test(fullPath));

      if (!excluded && typeof entryValue === 'number' && patterns.some((pattern) => pattern.test(fullPath))) {
        total += Number(entryValue || 0);
      }

      if (entryValue && typeof entryValue === 'object') {
        visit(entryValue, fullPath);
      }
    });
  }

  visit(payload);
  return total;
}

function deriveProjectPulseActionableApprovalCounts(primaryPayload, secondaryPayload = null) {
  const payloads = [primaryPayload, secondaryPayload].filter(Boolean);
  const objects = payloads.flatMap((payload) => collectProjectPulseObjects(payload));

  const submittedTimeObjects = objects.filter((item) => {
    const status = normalizeProjectPulseApprovalStatus(item.status ?? item.approvalStatus ?? item.dayStatus ?? item.workflowStatus);
    if (!status || isProjectPulseClosedApprovalStatus(status)) return false;

    return objectLooksLikeProjectPulseTimeApproval(item) && [
      'submitted',
      'pending',
      'pending_approval',
      'manager_pending',
      'awaiting_review',
      'awaiting_manager_review'
    ].includes(status);
  });

  const localResetPendingObjects = objects.filter((item) => {
    const status = normalizeProjectPulseApprovalStatus(item.status ?? item.approvalStatus ?? item.workflowStatus);
    if (!status || isProjectPulseClosedApprovalStatus(status)) return false;

    return objectLooksLikeProjectPulseLocalReset(item) && [
      'pending',
      'pending_approval',
      'requested',
      'awaiting_approval'
    ].includes(status);
  });

  const localResetReadyObjects = objects.filter((item) => {
    const status = normalizeProjectPulseApprovalStatus(item.status ?? item.approvalStatus ?? item.workflowStatus);
    if (!status || isProjectPulseClosedApprovalStatus(status)) return false;

    return objectLooksLikeProjectPulseLocalReset(item) && [
      'ready_for_temp_password',
      'ready_for_temporary_password',
      'temp_password_ready',
      'temporary_password_ready'
    ].includes(status);
  });

  const submittedTimeNumeric = payloads.reduce((total, payload) => total + readProjectPulseNumericFields(payload, [
    /submitted.*time.*pending/,
    /manager.*approval.*pending/,
    /submitted.*pending/,
    /pending.*submitted/,
  ], [
    /total/,
    /local/,
    /reset/,
    /password/,
    /ready/
  ]), 0);

  const localResetPendingNumeric = payloads.reduce((total, payload) => total + readProjectPulseNumericFields(payload, [
    /local.*reset.*pending.*approval/,
    /reset.*pending.*approval/,
    /pending.*approval/
  ], [
    /total/,
    /time/,
    /submitted/,
    /ready/
  ]), 0);

  const localResetReadyNumeric = payloads.reduce((total, payload) => total + readProjectPulseNumericFields(payload, [
    /ready.*temp/,
    /ready.*temporary/,
    /temp.*ready/,
    /temporary.*ready/
  ], [
    /total/
  ]), 0);

  const submittedTimePending = Math.max(submittedTimeObjects.length, submittedTimeNumeric);
  const localResetPendingApproval = Math.max(localResetPendingObjects.length, localResetPendingNumeric);
  const localResetReadyForTempPassword = Math.max(localResetReadyObjects.length, localResetReadyNumeric);

  const counts = {
    submittedTimePending,
    localResetPendingApproval,
    localResetReadyForTempPassword,
    actionableTotal: submittedTimePending + localResetPendingApproval + localResetReadyForTempPassword,
    updatedAt: Date.now()
  };

  return counts;
}

function setProjectPulseApprovalActionableCounts(counts) {
  try {
    window.__projectPulseApprovalActionableCounts = {
      submittedTimePending: Number(counts?.submittedTimePending ?? 0),
      localResetPendingApproval: Number(counts?.localResetPendingApproval ?? 0),
      localResetReadyForTempPassword: Number(counts?.localResetReadyForTempPassword ?? 0),
      actionableTotal: Number(counts?.actionableTotal ?? 0),
      updatedAt: Date.now()
    };

    window.dispatchEvent(new CustomEvent('projectpulse:approval-actionable-counts-updated', {
      detail: window.__projectPulseApprovalActionableCounts
    }));
  } catch {
    // Ignore event/cache restrictions.
  }
}

function getProjectPulseCachedApprovalActionableCounts() {
  try {
    const cached = window.__projectPulseApprovalActionableCounts;
    if (!cached) return null;
    if (cached.updatedAt && Date.now() - Number(cached.updatedAt) > 120000) return null;
    return cached;
  } catch {
    return null;
  }
}
/* 039E_ACTIONABLE_APPROVAL_COUNT_END */


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


/* 051C_POST_JSON_SESSION_HEADER_REPAIR_START */
function getProjectPulse051CActiveSessionHeaders(explicitSession = null) {
  let session = explicitSession || null;

  if (!session && typeof getStoredProjectPulseAuthSession === 'function') {
    try {
      session = getStoredProjectPulseAuthSession();
    } catch {
      session = null;
    }
  }

  if (!session && typeof window !== 'undefined') {
    try {
      session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    } catch {
      session = null;
    }
  }

  const token = session?.sessionToken
    || session?.token
    || session?.accessToken
    || '';

  if (!token) {
    return {};
  }

  return {
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token,
    Authorization: `Bearer ${token}`
  };
}
/* 051C_POST_JSON_SESSION_HEADER_REPAIR_END */

/* 051D_FORCE_TIME_ENTRY_POST_SESSION_START */
function getProjectPulse051DTimeEntrySessionToken() {
  if (typeof window === 'undefined') {
    return '';
  }

  const candidateKeys = [
    'projectPulseAuthSession',
    'ProjectPulseAuthSession',
    'projectPulseSession'
  ];

  for (const key of candidateKeys) {
    try {
      const raw = window.localStorage.getItem(key) || window.sessionStorage.getItem(key);

      if (!raw) {
        continue;
      }

      const session = JSON.parse(raw);
      const token = session?.sessionToken
        || session?.token
        || session?.accessToken
        || session?.session_token
        || '';

      if (token) {
        return token;
      }
    } catch {
      // Try the next possible storage location.
    }
  }

  try {
    const storedSession = typeof getStoredProjectPulseAuthSession === 'function'
      ? getStoredProjectPulseAuthSession()
      : null;

    return storedSession?.sessionToken
      || storedSession?.token
      || storedSession?.accessToken
      || '';
  } catch {
    return '';
  }
}

function getProjectPulse051DTimeEntryPostHeaders() {
  const token = getProjectPulse051DTimeEntrySessionToken();

  const headers = {
    'Content-Type': 'application/json',
    'X-ProjectPulse-Client-Guard': token ? '051D_TIME_ENTRY_POST_WITH_SESSION' : '051D_TIME_ENTRY_POST_NO_SESSION'
  };

  if (token) {
    headers['X-ProjectPulse-Session'] = token;
    headers['X-Project-Pulse-Session'] = token;
    headers['X-Session-Token'] = token;
    headers.Authorization = `Bearer ${token}`;
  }

  return headers;
}

async function postProjectPulse051DTimeEntryJson(path, payload) {
  try {
    const rawViewAs = window.localStorage.getItem('projectPulseViewAsUser');
    const activeViewAs = rawViewAs ? JSON.parse(rawViewAs) : null;

    if (activeViewAs?.userId) {
      throw new Error('Exit Administrator View-As before saving or submitting time. View-As is read-only.');
    }
  } catch (error) {
    if (error instanceof Error && error.message.startsWith('Exit Administrator View-As')) {
      throw error;
    }
  }

  const response = await fetch(path, {
    method: 'POST',
    headers: getProjectPulse051DTimeEntryPostHeaders(),
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    let detail = '';

    try {
      const text = await response.text();

      if (text) {
        try {
          const parsed = JSON.parse(text);
          const validationErrors = Array.isArray(parsed?.errors)
            ? parsed.errors
                .map((item) => typeof item === 'string' ? item : item?.message || String(item ?? ''))
                .filter(Boolean)
                .join(' ')
            : '';

          detail = parsed?.message
            || parsed?.detail
            || validationErrors
            || parsed?.status
            || text;
        } catch {
          detail = text;
        }
      }
    } catch {
      detail = response.statusText || 'Request failed';
    }

    throw new Error(`${path} returned HTTP ${response.status}: ${detail || response.statusText}`);
  }

  return response.json();
}
/* 051D_FORCE_TIME_ENTRY_POST_SESSION_END */


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




/* 043B_PROFILE_IMAGE_PERSISTENCE_FRONTEND_START */
function isProjectPulseProfilePhotoDataUrl(value) {
  return String(value ?? '').startsWith('data:image/');
}

async function loadPersistentProfilePreferences(session) {
  if (!session?.sessionToken) return null;

  const response = await fetch('/api/profile/preferences', {
    headers: getProjectPulseAuthHeaders(session)
  });

  if (!response.ok) {
    throw new Error(`Profile preference load returned HTTP ${response.status}`);
  }

  return response.json();
}

async function savePersistentProfilePreferences(session, preferences) {
  if (!session?.sessionToken) return preferences;

  const response = await fetch('/api/profile/preferences', {
    method: 'POST',
    headers: {
      ...getProjectPulse051CActiveSessionHeaders(typeof authSession !== 'undefined' ? authSession : null),
      'Content-Type': 'application/json',
      ...getProjectPulseAuthHeaders(session)
    },
    body: JSON.stringify({
      profilePhotoDataUrl: preferences?.profilePhotoDataUrl ?? ''
    })
  });

  const result = await response.json().catch(() => ({}));

  if (!response.ok) {
    throw new Error(result.message || `Profile preference save returned HTTP ${response.status}`);
  }

  return {
    ...preferences,
    profilePhotoDataUrl: result.profilePhotoDataUrl ?? ''
  };
}
/* 043B_PROFILE_IMAGE_PERSISTENCE_FRONTEND_END */

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
  const cleanValue = String(value || 'Project Health Dashboard').replace(/@.*/, '').replace(/[._-]/g, ' ');
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


const roleWorkspaceModules = sortProjectPulseModules([
  {
    route: 'project-workload',
    href: '#project-workload',
    title: 'Project Workload',
    navLabel: 'MODULE 018',
    description: 'Project Manager dashboard for active projects, closed projects, project status, assigned project list, and workload risks.',
    permissions: ['VIEW_PROJECT_WORKLOAD', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'project-workspace',
    href: '#project-workspace',
    title: 'Project Workspace & Engineering Documents',
    navLabel: 'MODULE 019',
    description: 'View project workspace readiness, engineering-visible documents, assignments, and timesheet-context artifacts.',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* MODULE_066A1_PROJECT_FLOWHIVE_NAV_START */
  {
    route: 'project-flowhive',
    href: '#project-flowhive',
    title: 'Project FlowHive',
    navLabel: 'MODULE 066',
    description: 'Review the role-scoped Project FlowHive portfolio, canonical task grid, assignments, and controlled planning capability roadmap.',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_PROJECT_WORKLOAD', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['ENGINEER', 'ENGINEERING', 'ENGINEERING_MANAGER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_COORDINATOR', 'MANAGER', 'EXECUTIVE', 'EXECUTIVE_LEADERSHIP']
  },
  /* MODULE_066A1_PROJECT_FLOWHIVE_NAV_END */
  {
    route: 'project-intake',
    href: '#project-intake',
    title: 'Project Intake & Engineering Resource Requests',
    navLabel: 'MODULE 020',
    description: 'Create and review project intake requests, signed date aging, engineering resource demand, capacity, project handoff, and work-task assignment readiness.',
    permissions: ['VIEW_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 036_SALES_INSIGHTS_DASHBOARD_START */
  {
    route: 'sales-insights',
    href: '#sales-insights',
    title: 'Sales Insights Dashboard',
    navLabel: 'MODULE 036',
    description: 'Sales-facing view of sold project handoff health, missing documents, PM assignment, engineering assignment readiness, and launch blockers.',
    permissions: ['VIEW_PROJECT_INTAKE', 'VIEW_CUSTOMERS', 'VIEW_PROJECT_WORKLOAD', 'VIEW_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 036_SALES_INSIGHTS_DASHBOARD_END */
  /* 055C_WORK_REGISTER_NAV_START */
  {
    route: 'work-register',
    href: '#work-register',
    title: 'Work Register',
    navLabel: 'MODULE 055C',
    description: 'Search and filter active, closed, archived, and historical work across customers, projects, intakes, stakeholders, tasks, documents, hours, and cost indicators.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'MANAGE_PROJECT_INTAKE', 'VIEW_CUSTOMERS', 'MANAGE_CUSTOMERS', 'VIEW_REPORTS', 'MANAGE_REPORTS', 'MANAGE_TIME', 'APPROVE_TIME'],
    roleCodes: ['PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'ENGINEER', 'ENGINEERING', 'SALES', 'ACCOUNT_EXECUTIVE', 'SOLUTION_ARCHITECT', 'SA', 'SAA', 'INSIDE_SALES']
  },
  /* 055C_WORK_REGISTER_NAV_END */
  /* 055B_RATE_CARD_ADMIN_NAV_START */
  {
    route: 'rate-card-administration',
    href: '#rate-card-administration',
    title: 'Rate Card Administration',
    navLabel: 'MODULE 055B',
    description: 'Manage standard, Toyota, Hyundai, customer-specific, service request, emergency, travel, and GSD-imported rate cards with audit history.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'MANAGE_PROJECT_INTAKE'],
    roleCodes: ['PROJECT_TEAM_COORDINATOR', 'SOLUTION_ARCHITECT']
  },
  /* 055B_RATE_CARD_ADMIN_NAV_END */
  {
    route: 'customer-directory',
    href: '#customer-directory',
    title: 'Customer Directory',
    navLabel: 'MODULE 021',
    description: 'Manage customer records, contacts, and customer intake/project cost readiness.',
    permissions: ['VIEW_CUSTOMERS', 'MANAGE_CUSTOMERS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* MODULE_999_COMPLETE_USER_GUIDE_NAV_START */
  {
    route: 'user-guide',
    href: '#user-guide',
    title: 'ProjectPulse Complete User Guide',
    navLabel: 'MODULE 999',
    description: 'Searchable documentation for every global platform function and every installed ProjectPulse module.',
    permissions: []
  },
  /* MODULE_999_COMPLETE_USER_GUIDE_NAV_END */
  /* MODULE_063_ROLE_WORKSPACE_NAV_START */
  {
    route: 'opportunities',
    href: '#opportunities',
    title: 'Opportunities & Action Tracker',
    navLabel: 'MODULE 063',
    description: 'Create and track active and closed opportunities, shared Sales, Presales, and Engineering actions, owners, dates, revenue, and accountable history.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['SALES', 'ACCOUNT_EXECUTIVE', 'ACCOUNT_EXECUTIVES', 'PRESALES', 'PRE_SALES', 'ENGINEER', 'ENGINEERING']
  },
  /* MODULE_063_ROLE_WORKSPACE_NAV_END */
  {
    route: 'contracts',
    href: '#contracts',
    title: 'Contracts & Block of Hours',
    navLabel: 'MODULE 060',
    description: 'Manage prepaid customer hours, credits, expiration, work consumption, and weekly AE balance reporting.',
    permissions: ['VIEW_CUSTOMERS', 'VIEW_REPORTS', 'MANAGE_REPORTS', 'MANAGE_PROJECT_INTAKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['PROJECT_TEAM_COORDINATOR', 'SALES', 'ACCOUNT_EXECUTIVE', 'EXECUTIVE', 'EXECUTIVE_LEADERSHIP']
  },
  {
    route: 'cost-alerts',
    href: '#cost-alerts',
    title: 'Cost Overrun Alerts',
    navLabel: 'MODULE 022',
    description: 'Detect missing cost plans, over-assigned projects, and route PM/manager/PTC alerts.',
    permissions: ['VIEW_COST_ALERTS', 'MANAGE_COST_ALERTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'time-compliance',
    href: '#time-compliance',
    title: 'Time Compliance & Notification Center',
    navLabel: 'MODULE 023',
    description: 'Production-safe preview for missing weekly time, manager and Project Team Coordinator copy visibility, month-end rules, holiday reminders, and notification history.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'VIEW_TIME_COMPLIANCE', 'VIEW_AUDIT_HISTORY']
  },
  /* MODULE_REGISTRY_RECOVERY_024_030_058_START */
  {
    route: "sales-intake",
    href: "#sales-intake",
    title: "Sales-to-Delivery Intake Foundation",
    navLabel: "MODULE 024",
    description: "Validate intake, signed SOW and GSD artifacts, handoff readiness, and assignment preparation.",
    permissions: ["VIEW_PROJECT_INTAKE", "MANAGE_PROJECT_INTAKE", "VIEW_CUSTOMERS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["SALES", "ACCOUNT_EXECUTIVE", "INSIDE_SALES", "SOLUTION_ARCHITECT", "SA", "SAA", "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR"],
  },
  {
    route: "sow-generator",
    href: "#sow-generator",
    title: "SOW Generator + Claude Review Workflow",
    navLabel: "MODULE 025",
    description: "Prepare SOW drafts, technical reviews, signed-document readiness, and controlled delivery handoff.",
    permissions: ["VIEW_PROJECT_INTAKE", "VIEW_PROJECT_WORKSPACE", "VIEW_CUSTOMERS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["SALES", "ACCOUNT_EXECUTIVE", "INSIDE_SALES", "SOLUTION_ARCHITECT", "SA", "SAA", "PROJECT_TEAM_COORDINATOR", "PROJECT_MANAGER", "PROJECT_MANAGEMENT"],
  },
  {
    route: "crm-integration",
    href: "#crm-integration",
    title: "CRM/ERP Integration Control Center",
    navLabel: "MODULE 026",
    description: "Connect SELL, Salesforce, Certinia, ServiceNow, and manually registered CRM/ERP platforms and review sanitized availability status.",
    permissions: ["VIEW_INTEGRATIONS_026", "MANAGE_INTEGRATIONS_026", "VIEW_CUSTOMERS", "VIEW_PROJECT_INTAKE", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["SALES", "ACCOUNT_EXECUTIVE", "INSIDE_SALES", "SOLUTION_ARCHITECT", "SA", "SAA", "PROJECT_TEAM_COORDINATOR"],
  },
  {
    route: "signed-handoff",
    href: "#signed-handoff",
    title: "Signed SOW Handoff + Assignment Trigger",
    navLabel: "MODULE 027",
    description: "Prepare signed handoff, stakeholder notification, and PM and engineering assignment workflows.",
    permissions: ["VIEW_PROJECT_INTAKE", "VIEW_PROJECT_WORKSPACE", "VIEW_RESOURCE_SCHEDULING", "MANAGE_RESOURCE_SCHEDULING", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT", "SA", "SAA", "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR", "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "ENGINEER", "ENGINEERING"],
  },
  {
    route: "ai-time-entry",
    href: "#ai-time-entry",
    title: "SOW-Aware AI Time Entry Generator",
    navLabel: "MODULE 028",
    description: "Create engineer-reviewed AI time-entry drafts using assigned work and signed scope context.",
    permissions: ["VIEW_TIME_ENTRY", "VIEW_PROJECT_WORKSPACE", "VIEW_ENGINEERING_PROJECT_DOCUMENTS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["ENGINEER", "ENGINEERING", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD", "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_TEAM_COORDINATOR"],
  },
  {
    route: "uat-validation",
    href: "#uat-validation",
    title: "User Acceptance / Role + Workflow Validation Center",
    navLabel: "MODULE 029",
    description: "Validate role access, workflows, View-As protection, module routes, and readiness evidence.",
    permissions: ["VIEW_AUDIT_TRAIL", "VIEW_REPORTS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["PROJECT_TEAM_COORDINATOR", "EXECUTIVE", "EXECUTIVE_LEADERSHIP"],
  },
  {
    route: "reporting",
    href: "#reporting",
    title: "Reporting / Accounting / Invoicing / Analytics",
    navLabel: "MODULE 030",
    description: "Provide operational, accounting, invoicing, workflow, system, and executive reporting.",
    permissions: ["VIEW_REPORTS", "MANAGE_REPORTS", "VIEW_EXECUTIVE_REPORTING", "VIEW_ACCOUNT_RECONCILIATION", "EXPORT_TIME_EXCEL", "EXPORT_TIME_PDF", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["ACCOUNTING", "PROJECT_TEAM_COORDINATOR", "EXECUTIVE", "EXECUTIVE_LEADERSHIP", "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "ENGINEER", "ENGINEERING", "MANAGER", "SALES", "ACCOUNT_EXECUTIVE"],
  },
  {
    route: "cicd-pipeline",
    href: "#cicd-pipeline",
    title: "Autonomous CI/CD Foundation",
    navLabel: "MODULE 058",
    description: "Audit and operate CI/CD workflows, deployment validation, smoke testing, summaries, and rollback controls.",
    permissions: ["SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
  },
  /* MODULE_REGISTRY_RECOVERY_024_030_058_END */

  {
    route: 'timesheet',
    href: '#timesheet',
    title: 'Time Entry',
    navLabel: 'MODULE 001',
    description: 'Enter weekly and daily time by project, task, non-project work, and afterhours.',
    permissions: ['VIEW_TIME_ENTRY']
  },
  {
    route: 'manager-approval',
    href: '#manager-approval',
    title: 'Approval Inbox',
    navLabel: 'MODULE 002',
    description: 'Approve, reject, and review submitted time.',
    permissions: ['VIEW_APPROVAL_INBOX', 'APPROVE_TIME']
  },
  {
    route: 'utilization',
    href: '#utilization',
    title: 'Utilization',
    navLabel: 'MODULE 003',
    description: 'Review own utilization, manager team utilization, engineering team lead utilization, remaining hours, and work task classification readiness.',
    permissions: ['VIEW_OWN_UTILIZATION', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION']
  },
  {
    route: 'holiday-admin',
    href: '#holiday-admin',
    title: 'Holiday Calendar',
    navLabel: 'MODULE 004',
    description: 'View or manage company holidays and calendar availability.',
    permissions: ['VIEW_HOLIDAYS', 'MANAGE_HOLIDAYS']
  },
  {
    route: 'project-allocation-info',
    href: '#project-allocation-info',
    title: 'Project Allocation and Info',
    navLabel: 'MODULE 005',
    description: 'View project allocations, engineer hours, and SOW/GSD documents.',
    permissions: ['VIEW_PROJECT_ALLOCATION_INFO', 'MANAGE_PROJECT_ALLOCATION_INFO', 'MANAGE_ALL']
  },
  {
    route: 'psa-modules',
    href: '#psa-modules',
    title: 'PSA Modules',
    navLabel: 'MODULE 006',
    description: 'Review project intake, resource scheduling, expense management, and executive reporting workflows.',
    permissions: ['VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING']
  },
  {
    route: 'workflow',
    href: '#workflow',
    title: 'Workflow',
    navLabel: 'MODULE 007',
    description: 'Review project approval, account reconciliation, exports, and reporting workflow.',
    permissions: ['PROJECT_TIME_APPROVAL', 'VIEW_APPROVAL_WORKFLOW', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_WORKFLOW_OPERATIONAL_READINESS', 'VIEW_WORKFLOW_AUDIT_EVIDENCE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'audit-history',
    href: '#audit-history',
    title: 'Audit / Security History',
    navLabel: 'MODULE 008',
    description: 'Review login history, password reset history, Azure sync failures, notification failures, and system audit events.',
    permissions: ['VIEW_AUDIT_TRAIL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* MODULE_997_SECURITY_OPERATIONS_NAV_START */
  {
    route: 'security-operations',
    href: '#security-operations',
    title: 'Security Operations, Threat Intelligence & Response Center',
    navLabel: 'MODULE 997',
    description: 'Review sanitized security readiness, alert and incident contracts, threat-intelligence policy, control ownership, and fail-closed response governance.',
    permissions: ['VIEW_SECURITY_OPERATIONS', 'MANAGE_SECURITY_RESPONSE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'SECURITY_ANALYST', 'SECURITY_OPERATIONS', 'SECURITY_INCIDENT_COMMANDER']
  },
  /* MODULE_997_SECURITY_OPERATIONS_NAV_END */
  {
    route: 'user-admin',
    href: '#user-admin',
    title: 'User Administration',
    navLabel: 'MODULE 009',
    description: 'Manage users, local passwords, roles, teams, departments, and login access.',
    permissions: ['VIEW_USER_ADMIN', 'MANAGE_USER_ADMIN', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'azure-admin',
    href: '#azure-admin',
    title: 'Azure / Entra Admin',
    navLabel: 'MODULE 010',
    description: 'Configure Azure SSO, run user sync, and review imported directory users.',
    permissions: ['VIEW_AZURE_ADMIN', 'MANAGE_AZURE_SYNC', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'work-task-builder',
    href: '#work-task-builder',
    title: 'Work Task Builder',
    navLabel: 'MODULE 011',
    description: 'Build, classify, and assign project, service request, open, and non-project work tasks with billing and utilization treatment.',
    permissions: ['VIEW_WORK_TASK_BUILDER', 'MANAGE_WORK_TASK_BUILDER', 'ASSIGN_WORK_TASKS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'role-admin',
    href: '#role-admin',
    title: 'Role Administration',
    navLabel: 'MODULE 012',
    description: 'Manage users, roles, access, and administrative configuration.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 037_ROLES_PERMISSIONS_MATRIX_START */
  {
    route: 'roles-permissions-matrix',
    href: '#roles-permissions-matrix',
    title: 'Roles and Permissions Matrix',
    navLabel: 'MODULE 037',
    description: 'Read-only governance matrix showing role definitions, permission grants, recommended access groups, module coverage, and least-privilege review signals.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 037_ROLES_PERMISSIONS_MATRIX_END */
  /* 038_CERTIFY_INTEGRATION_MODULE_START */
  {
    route: 'certify-integration',
    href: '#certify-integration',
    title: 'Certify Integration Center',
    navLabel: 'MODULE 038',
    description: 'Plan and validate Certify expense integration readiness, employee/project/category mapping, receipt evidence, approval status, exception handling, and accounting handoff.',
    permissions: ['VIEW_EXPENSES', 'VIEW_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 038_CERTIFY_INTEGRATION_MODULE_END */
  /* 039_BILLING_READINESS_CENTER_START */
  {
    route: 'billing-readiness',
    href: '#billing-readiness',
    title: 'Billing Readiness Center',
    navLabel: 'MODULE 039',
    description: 'Review project invoice packages and month-end billing runs with approved labor, staged Certify expenses, customer/project mapping, blocked dollars, exception review, and accounting export readiness.',
    permissions: ['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 039_BILLING_READINESS_CENTER_END */
  /* 040_PROJECT_CLOSEOUT_CENTER_START */
  {
    route: 'project-closeout',
    href: '#project-closeout',
    title: 'Project Closeout Center',
    navLabel: 'MODULE 040',
    description: 'Review closeout readiness, cleared approvals, billing readiness, Certify expense exceptions, stakeholder notification audience, lessons-learned reminder, and closeout evidence export before marking a project complete.',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 040_PROJECT_CLOSEOUT_CENTER_END */
  /* 041_CLOSEOUT_EMAIL_AUTOMATION_START */
  {
    route: 'closeout-email',
    href: '#closeout-email',
    title: 'Closeout Email Automation Center',
    navLabel: 'MODULE 041',
    description: 'Automatically send and audit the PM closeout email to the project team, PM assignment, engineer assignment, Sales Executive or Account Executive, and Solution Architect when the PM closes the project out, including the named PM lessons-learned reminder.',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 041_CLOSEOUT_EMAIL_AUTOMATION_END */
  /* 042_INVOICE_BILLING_CENTER_START */
  {
    route: 'invoice-billing-center',
    href: '#invoice-billing-center',
    title: 'Invoice & Billing Center',
    navLabel: 'MODULE 042',
    description: 'Prepare partial and final invoices, review recently closed projects, preserve detailed customer-facing time and rate evidence, customize invoice headers, and preview Over / Under and T&M balance reporting.',
    permissions: ['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  /* 042_INVOICE_BILLING_CENTER_END */
  /* 057_CALENDAR_CAPACITY_START */
  {
    route: 'calendar-capacity',
    href: '#calendar-capacity',
    title: 'Resource & Team Calendar Capacity',
    navLabel: 'MODULE 057',
    description: 'View individual, team, and department calendars with day, workweek, week, month, agenda, timeline, and future month navigation.',
    permissions: ['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['ENGINEER', 'ENGINEERING', 'ENGINEERING_MANAGER', 'ENGINEERING_TEAM_LEAD', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT']
  },
  /* 057_CALENDAR_CAPACITY_END */
  /* MODULES_064_074_RELEASE_TRAIN_NAV_START */
  {
    route: 'ai-provider-configuration',
    href: '#ai-provider-configuration',
    title: 'AI Provider Configuration Center',
    navLabel: 'MODULE 064',
    description: 'Review the shared Claude-first, OpenAI-second, governed-local-fallback configuration, routing, health, usage, and locked secret controls.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'entra-secret-administration',
    href: '#entra-secret-administration',
    title: 'Entra Secret Administration',
    navLabel: 'MODULE 065',
    description: 'Review privileged application-credential readiness and the fail-closed prepare, approval, activation, and rollback contract.',
    permissions: ['MANAGE_ENTRA_SECRET', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']
  },
  {
    route: 'global-mail-configuration',
    href: '#global-mail-configuration',
    title: 'Global Mail Configuration Center',
    navLabel: 'MODULE 067',
    description: 'Review non-secret Microsoft 365 mail configuration, shared consumers, migration readiness, and controlled activation gates.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'system-architecture',
    href: '#system-architecture',
    title: 'System Architecture & Dependency Map',
    navLabel: 'MODULE 068',
    description: 'View the role-safe ProjectPulse component, data, authentication, integration, environment, and live-status ownership map.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'qualifications-certifications',
    href: '#qualifications-certifications',
    title: 'Qualifications & Certification Matrix',
    navLabel: 'MODULE 069',
    description: 'Review identity-backed skills, certifications, competency, experience, expiration, and role-scoped coverage.',
    permissions: ['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_OWN_UTILIZATION', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['ENGINEER', 'ENGINEERING', 'MANAGER', 'ENGINEERING_MANAGER', 'ENGINEERING_TEAM_LEAD', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'EXECUTIVE']
  },
  {
    route: 'capacity-pipeline-forecast',
    href: '#capacity-pipeline-forecast',
    title: 'Capacity & Pipeline Forecasting',
    navLabel: 'MODULE 070',
    description: 'Forecast committed work, weighted pipeline demand, supplemental scenarios, remaining capacity, and utilization by week and engineer.',
    permissions: ['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'VIEW_PROJECT_INTAKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['ENGINEER', 'ENGINEERING', 'ENGINEERING_MANAGER', 'ENGINEERING_TEAM_LEAD', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'EXECUTIVE', 'EXECUTIVE_LEADERSHIP']
  },
  {
    route: 'oncall-scheduling',
    href: '#oncall-scheduling',
    title: 'On-Call Scheduling',
    navLabel: 'MODULE 071',
    description: 'View the US Signal on-call schedule and roster; Managers and Engineering Team Leads receive governed management controls.',
    permissions: []
  },
  {
    route: 'oneassist-routing-directory',
    href: '#oneassist-routing-directory',
    title: 'OneAssist Routing Directory',
    navLabel: 'MODULE 072',
    description: 'View the customer routing directory and unmasked five-digit PINs; authorized roles receive governed editing controls.',
    permissions: []
  },
  {
    route: 'sales-coverage-alignment',
    href: '#sales-coverage-alignment',
    title: 'Sales Coverage Alignment',
    navLabel: 'MODULE 073',
    description: 'View current sales-coverage signals and build a validated, effective-dated identity-backed alignment draft.',
    permissions: []
  },
  {
    route: 'oem-vendor-directory',
    href: '#oem-vendor-directory',
    title: 'OEM & Vendor Directory',
    navLabel: 'MODULE 074',
    description: 'View and prepare a validated US Signal-branded OEM and vendor directory draft with governed role-aware editing.',
    permissions: []
  },
  {
    route: 'defect-tracker',
    href: '#defect-tracker',
    title: 'Defect Intake & Resolution Tracker',
    navLabel: 'MODULE 076',
    description: 'Report and track defects from ProjectPulse Help, GitHub, Claude through GitHub, and ChatGPT through GitHub with identity-backed assignment and governed notification contracts.',
    permissions: []
  },
  /* MODULES_075_080_RUNTIME_NAV_START */
  {
    route: 'integration-event-gateway',
    href: '#integration-event-gateway',
    title: 'Integration Automation & Event Gateway',
    navLabel: 'MODULE 075',
    description: 'Review signed event contracts, sources, deliveries, dead-letter policy, and locked integration controls.',
    permissions: []
  },
  {
    route: 'release-deployment-control',
    href: '#release-deployment-control',
    title: 'Release, Deployment & Rollback Control Center',
    navLabel: 'MODULE 077',
    description: 'Review release evidence, environments, gates, and locked deployment and rollback controls.',
    permissions: []
  },
  {
    route: 'observability-slo-health',
    href: '#observability-slo-health',
    title: 'Observability, SLO & Application Health Center',
    navLabel: 'MODULE 078',
    description: 'Review services, signals, SLOs, alerts, integrations, and governed retention boundaries.',
    permissions: []
  },
  {
    route: 'data-governance-retention',
    href: '#data-governance-retention',
    title: 'Data Governance, Retention & Privacy Center',
    navLabel: 'MODULE 079',
    description: 'Review data domains, classifications, retention policies, lineage, legal holds, and privacy boundaries.',
    permissions: []
  },
  {
    route: 'customer-delivery-acceptance',
    href: '#customer-delivery-acceptance',
    title: 'Customer Delivery & Acceptance Portal',
    navLabel: 'MODULE 080',
    description: 'Review delivery engagements, milestones, artifacts, reviews, acceptance policy, and sharing boundaries.',
    permissions: []
  },
  /* MODULES_075_080_RUNTIME_NAV_END */
  /* MODULES_064_074_RELEASE_TRAIN_NAV_END */
  /* MODULE_998_SYSTEM_DIAGNOSTICS_NAV_START */
  {
    route: 'system-diagnostics',
    href: '#system-diagnostics',
    title: 'System Diagnostic & Controlled Remediation Center',
    navLabel: 'MODULE 998',
    description: 'Review sanitized platform diagnostics, issue classification, ownership, evidence, runbooks, and fail-closed remediation readiness.',
    permissions: ['VIEW_SYSTEM_DIAGNOSTICS', 'MANAGE_SYSTEM_REMEDIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    roleCodes: ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']
  },
  /* MODULE_998_SYSTEM_DIAGNOSTICS_NAV_END */
  {
    route: 'service-control',
    href: '#service-control',
    title: 'Service Control Center',
    navLabel: 'MODULE 013',
    description: 'Monitor platform services, API status, recent logs, and controlled restart actions.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'backup-dr',
    href: '#backup-dr',
    title: 'Backup / DR Center',
    navLabel: 'MODULE 014',
    description: 'Create and validate full PHD backup bundles.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'restore-validation',
    href: '#restore-validation',
    title: 'Restore Validation',
    navLabel: 'MODULE 015',
    description: 'Validate selected backup restore points without restoring over production.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'backup-retention',
    href: '#backup-retention',
    title: 'Backup Retention',
    navLabel: 'MODULE 016',
    description: 'Review and safely remove older backup points with restore-point protection.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  },
  {
    route: 'replication-sync',
    href: '#replication-sync',
    title: 'Replication & Sync Status',
    navLabel: 'MODULE 017',
    description: 'Review failover readiness, database role, service health, backup freshness, deployment state, and peer configuration.',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']
  }
]);

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
  return permissions.has('MANAGE_ALL') || permissions.has('SYSTEM_ADMINISTRATION') || roles.some((role) => ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR'].includes(role.roleCode));
}

function userHasAnyPermission(user, permissions) {
  if (!permissions || permissions.length === 0) return true;
  if (userIsAdministrator(user)) return true;

  const granted = userPermissionSet(user);
  return permissions.some((permission) => granted.has(permission));
}

function getVisibleRoleModules(user) {
  if (!user) return [];

  const assignedRoleCodes = new Set((user?.roles ?? []).map((role) => String(role.roleCode ?? '').toUpperCase()));
  const modules = roleWorkspaceModules.filter((module) => (
    userHasAnyPermission(user, module.permissions) ||
    (module.roleCodes ?? []).some((roleCode) => assignedRoleCodes.has(String(roleCode).toUpperCase()))
  ));

  if (userIsProjectManagementRole(user) && !userIsAdministrator(user)) {
    return modules.filter((module) => module.route !== 'utilization');
  }

  return modules;
}

function getRoleDisplayName(user) {
  const roles = user?.roles ?? [];
  if (roles.length === 0) return 'Workspace';
  if (roles.some((role) => ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR'].includes(role.roleCode))) return 'Administrator';
  return roles.map((role) => role.roleName).join(' + ');
}

function getRoleNavigation(user) {
  const modules = getVisibleRoleModules(user);
  const routeMap = new Map();

  routeMap.set('dashboard', {
    route: 'dashboard',
    href: '#dashboard',
    label: 'Dashboard',
    title: 'Dashboard'
  });

  modules.forEach((module) => {
    if (!routeMap.has(module.route)) {
      routeMap.set(module.route, {
        route: module.route,
        href: module.href,
        label: module.navLabel,
        title: module.title
      });
    }
  });

  return [...routeMap.values()];
}


function getNavigationDisplayLabel(item) {
  return item?.title || item?.label || 'Dashboard';
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

function userIsProjectManagementRole(user) {
  return userHasRoleText(user, ['project_management', 'project manager', 'project management']);
}


function getPrimaryNavigationPriority(user) {
  if (userIsAdministrator(user)) {
    return ['dashboard', 'timesheet', 'manager-approval', 'project-workspace'];
  }

  if (userIsProjectManagementRole(user) && !userIsAdministrator(user)) {
    return ['dashboard', 'project-workload', 'project-workspace', 'project-intake'];
  }

  if (
    userHasRoleText(user, ['project/team coordinator', 'project team coordinator', 'team coordinator', 'project coordinator']) ||
    userHasPermissionCode(user, 'MANAGE_PROJECT_COORDINATION') ||
    userHasPermissionCode(user, 'MANAGE_PROJECT_INTAKE') ||
    userHasPermissionCode(user, 'MANAGE_CUSTOMERS')
  ) {
    return ['dashboard', 'timesheet', 'project-intake', 'customer-directory'];
  }

  if (
    userHasPermissionCode(user, 'APPROVE_TIME') ||
    userHasPermissionCode(user, 'VIEW_APPROVAL_INBOX') ||
    userHasPermissionCode(user, 'VIEW_TEAM_UTILIZATION')
  ) {
    return ['dashboard', 'manager-approval', 'utilization', 'project-workspace'];
  }

  return ['timesheet', 'utilization', 'holiday-admin', 'project-workspace'];
}

function getNavigationGroup(item) {
  switch (item.route) {
    case 'timesheet':
    case 'manager-approval':
    case 'utilization':
    case 'holiday-admin':
    case 'calendar-capacity':
    case 'qualifications-certifications':
    case 'capacity-pipeline-forecast':
    case 'oncall-scheduling':
    case 'ai-time-entry':
      return 'Work Management';

    case 'project-workload':
    case 'project-allocation-info':
    case 'project-workspace':
    case 'project-flowhive':
      return 'Project Workspace';
    case 'user-guide':
    case 'defect-tracker':
      return 'Help & Documentation';
    case 'opportunities':
    case 'sales-intake':
    case 'sow-generator':
    case 'crm-integration':
    case 'signed-handoff':
    case 'sales-coverage-alignment':
    case 'oem-vendor-directory':
      return 'Sales & Opportunities';

    case 'cost-alerts':
    case 'customer-directory':
    case 'contracts':
    case 'work-register':
      return 'Work Register';
    case 'rate-card-administration':
      return 'Rate Card Administration';
    case 'project-intake':
    case 'sales-insights':
      return 'Project Intake';
    case 'time-compliance':
      return 'Time Compliance';
    case 'psa-modules':
    case 'certify-integration':
    case 'oneassist-routing-directory':
    case 'customer-delivery-acceptance':
      return 'Project Operations';

    case 'audit-history':
    case 'security-operations':
    case 'data-governance-retention':
      return 'Security & Audit';

    case 'work-task-builder':
      return 'Work Task Builder';

    case 'user-admin':
    case 'azure-admin':
    case 'ai-provider-configuration':
    case 'entra-secret-administration':
    case 'role-admin':
    case 'roles-permissions-matrix':
      return 'Admin & Identity';

    case 'service-control':
    case 'global-mail-configuration':
    case 'system-architecture':
    case 'system-diagnostics':
    case 'uat-validation':
    case 'cicd-pipeline':
    case 'integration-event-gateway':
    case 'release-deployment-control':
    case 'observability-slo-health':
      return 'Platform Operations';

    case 'backup-dr':
    case 'restore-validation':
    case 'backup-retention':
    case 'replication-sync':
      return 'Resilience & Recovery';

    case 'workflow':
    case 'billing-readiness':
    case 'project-closeout':
    case 'closeout-email':
    case 'invoice-billing-center':
    case 'reporting':
      return 'Reports & Workflow';

    default:
      return 'Other';
  }
}


/* 031_SOURCE_GLOBAL_SEARCH_START */
const PROJECT_PULSE_GLOBAL_SEARCH_SESSION_KEY = 'projectPulseAuthSession';

function readProjectPulseGlobalSearchSession() {
  try {
    const raw = window.localStorage.getItem(PROJECT_PULSE_GLOBAL_SEARCH_SESSION_KEY);
    if (!raw) return null;

    const session = JSON.parse(raw);
    if (!session?.sessionToken) return null;
    if (session?.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;

    return session;
  } catch {
    return null;
  }
}

function getProjectPulseGlobalSearchHeaders() {
  const session = readProjectPulseGlobalSearchSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

function projectPulseGlobalSearchText(value) {
  return String(value ?? '')
    .toLowerCase()
    .replace(/[_\-:/|#]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function projectPulseGlobalSearchValue(source, keys) {
  if (!source) return '';

  for (const key of keys) {
    const value = source[key];
    if (value !== null && value !== undefined && String(value).trim()) {
      return String(value).trim();
    }
  }

  return '';
}

function projectPulseGlobalSearchJoin(parts) {
  return parts
    .map((part) => String(part ?? '').trim())
    .filter(Boolean)
    .filter((part, index, array) => array.indexOf(part) === index)
    .join(' · ');
}

function projectPulseGlobalSearchOptionLabel(option) {
  if (option && typeof option === 'object') {
    return option.label || option.name || option.text || option.value || '';
  }

  return option === null || option === undefined ? '' : String(option);
}

function projectPulseGlobalSearchOptionValue(option) {
  if (option && typeof option === 'object') {
    return option.value || option.email || option.id || option.label || option.name || '';
  }

  return option === null || option === undefined ? '' : String(option);
}

function addProjectPulseGlobalSearchItem(items, type, title, subtitle, meta, route, source) {
  if (!title && !subtitle) return;

  let sourceText = '';

  try {
    sourceText = JSON.stringify(source || {});
  } catch {
    sourceText = '';
  }

  items.push({
    type,
    title: title || subtitle,
    subtitle: subtitle || '',
    meta: meta || '',
    route: route || '#dashboard',
    sourceText
  });
}

async function fetchProjectPulseGlobalSearchJson(path) {
  const response = await fetch(`${path}${path.includes('?') ? '&' : '?'}_ts=${Date.now()}`, {
    headers: getProjectPulseGlobalSearchHeaders(),
    credentials: 'include',
    cache: 'no-store'
  });

  const raw = await response.text();
  let payload = {};

  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = {};
  }

  if (!response.ok) {
    throw new Error(`${path} returned HTTP ${response.status}`);
  }

  return payload;
}

function buildProjectPulseGlobalSearchItems(workspace, filters, dashboard) {
  const items = [];
  const customers = new Map();

  (workspace?.projects || []).forEach((project) => {
    const projectCode = projectPulseGlobalSearchValue(project, ['projectCode', 'code', 'projectNumber', 'number', 'engagementCode']);
    const projectName = projectPulseGlobalSearchValue(project, ['projectName', 'name', 'engagementName', 'title']);
    const customerName = projectPulseGlobalSearchValue(project, ['customerName', 'customer', 'clientName', 'accountName']);
    const status = projectPulseGlobalSearchValue(project, ['status', 'projectStatus', 'deliveryStatus']);
    const pm = projectPulseGlobalSearchValue(project, ['projectManagerName', 'pmName', 'projectManager', 'engagementManagerName']);
    const team = projectPulseGlobalSearchValue(project, ['teamName', 'team', 'department']);

    if (customerName) {
      customers.set(customerName, { customerName, projectCode, projectName, status });
    }

    addProjectPulseGlobalSearchItem(
      items,
      'Project',
      projectPulseGlobalSearchJoin([projectCode, projectName]) || 'Project',
      customerName,
      projectPulseGlobalSearchJoin([status, pm, team]),
      '#project-workspace',
      project
    );
  });

  (workspace?.documents || []).forEach((documentItem) => {
    const fileName = projectPulseGlobalSearchValue(documentItem, ['originalFileName', 'fileName', 'name', 'title']);
    const projectName = projectPulseGlobalSearchValue(documentItem, ['projectOrIntakeName', 'projectName', 'intakeName']);
    const projectCode = projectPulseGlobalSearchValue(documentItem, ['projectCode', 'code', 'requestNumber']);
    const category = projectPulseGlobalSearchValue(documentItem, ['documentCategory', 'category', 'type']);
    const status = projectPulseGlobalSearchValue(documentItem, ['extractionStatus', 'status']);

    addProjectPulseGlobalSearchItem(
      items,
      'Document',
      fileName || projectPulseGlobalSearchJoin([projectCode, projectName]),
      projectPulseGlobalSearchJoin([projectCode, projectName]),
      projectPulseGlobalSearchJoin([category, status]),
      '#project-workspace',
      documentItem
    );
  });

  (workspace?.assignments || []).forEach((assignment) => {
    const person = projectPulseGlobalSearchValue(assignment, ['engineerDisplayName', 'assignedUserDisplayName', 'displayName', 'engineerName', 'userEmail']);
    const projectCode = projectPulseGlobalSearchValue(assignment, ['projectCode', 'code', 'projectNumber']);
    const projectName = projectPulseGlobalSearchValue(assignment, ['projectName', 'engagementName', 'name']);
    const role = projectPulseGlobalSearchValue(assignment, ['assignmentRole', 'roleName', 'role', 'workRole']);
    const status = projectPulseGlobalSearchValue(assignment, ['status', 'assignmentStatus']);

    addProjectPulseGlobalSearchItem(
      items,
      'Assignment',
      projectPulseGlobalSearchJoin([person, role]) || 'Assignment',
      projectPulseGlobalSearchJoin([projectCode, projectName]),
      status,
      '#project-workspace',
      assignment
    );
  });

  (workspace?.resourceRequests || []).forEach((request) => {
    const requestNumber = projectPulseGlobalSearchValue(request, ['requestNumber', 'intakeNumber', 'resourceRequestNumber', 'id']);
    const projectName = projectPulseGlobalSearchValue(request, ['projectOrIntakeName', 'projectName', 'intakeName', 'name']);
    const customerName = projectPulseGlobalSearchValue(request, ['customerName', 'customer', 'clientName']);
    const role = projectPulseGlobalSearchValue(request, ['requestedRole', 'roleName', 'role']);
    const status = projectPulseGlobalSearchValue(request, ['status', 'requestStatus']);

    if (customerName) {
      customers.set(customerName, { customerName, requestNumber, projectName, status });
    }

    addProjectPulseGlobalSearchItem(
      items,
      'Request',
      projectPulseGlobalSearchJoin([requestNumber, role]) || 'Resource Request',
      projectPulseGlobalSearchJoin([customerName, projectName]),
      status,
      '#project-workspace',
      request
    );
  });

  Array.from(customers.values()).forEach((customer) => {
    addProjectPulseGlobalSearchItem(
      items,
      'Customer',
      customer.customerName,
      projectPulseGlobalSearchJoin([customer.projectCode || customer.requestNumber, customer.projectName]),
      customer.status || 'Customer',
      '#project-workspace',
      customer
    );
  });

  [
    ['customers', 'Customer', '#reporting', 'Report filter'],
    ['projects', 'Project', '#reporting', 'Report filter'],
    ['pms', 'PM', '#reporting', 'Project manager'],
    ['engineers', 'Engineer', '#reporting', 'Engineer'],
    ['teams', 'Team', '#reporting', 'Team'],
    ['contractTypes', 'Contract', '#reporting', 'Contract type']
  ].forEach(([key, type, route, meta]) => {
    (filters?.[key] || []).forEach((option) => {
      const label = projectPulseGlobalSearchOptionLabel(option);
      const value = projectPulseGlobalSearchOptionValue(option);

      if (label && !label.toLowerCase().startsWith('all ')) {
        addProjectPulseGlobalSearchItem(
          items,
          type,
          label,
          value && value !== label ? value : '',
          meta,
          route,
          option
        );
      }
    });
  });

  const modules = dashboard?.modules || dashboard?.visibleModules || dashboard?.availableModules || [];

  if (Array.isArray(modules)) {
    modules.forEach((moduleItem) => {
      const title = projectPulseGlobalSearchValue(moduleItem, ['title', 'moduleTitle', 'name', 'label']);
      const key = projectPulseGlobalSearchValue(moduleItem, ['moduleKey', 'key', 'code', 'route']);
      const route = projectPulseGlobalSearchValue(moduleItem, ['route', 'path', 'href']) || '#dashboard';
      const description = projectPulseGlobalSearchValue(moduleItem, ['description', 'summary', 'purpose']);

      addProjectPulseGlobalSearchItem(
        items,
        'Module',
        title || key,
        key,
        description,
        route.startsWith('#') ? route : `#${route}`,
        moduleItem
      );
    });
  }

  const seen = new Set();

  return items.filter((item) => {
    const key = [item.type, item.title, item.subtitle, item.meta].join('|').toLowerCase();

    if (seen.has(key)) return false;

    seen.add(key);
    return true;
  });
}

function scoreProjectPulseGlobalSearchItem(item, query) {
  const normalizedQuery = projectPulseGlobalSearchText(query);
  if (!normalizedQuery) return 0;

  const tokens = normalizedQuery.split(' ').filter(Boolean);
  const title = projectPulseGlobalSearchText(item.title);
  const subtitle = projectPulseGlobalSearchText(item.subtitle);
  const haystack = projectPulseGlobalSearchText([item.type, item.title, item.subtitle, item.meta, item.sourceText].join(' '));

  if (!tokens.every((token) => haystack.includes(token))) return 0;

  let score = 10;

  if (title === normalizedQuery) score += 120;
  if (title.startsWith(normalizedQuery)) score += 80;
  if (title.includes(normalizedQuery)) score += 45;
  if (subtitle.includes(normalizedQuery)) score += 25;

  tokens.forEach((token) => {
    if (title.startsWith(token)) score += 20;
    if (title.includes(token)) score += 12;
    if (haystack.includes(token)) score += 5;
  });

  if (item.type === 'Project') score += 14;
  if (item.type === 'Customer') score += 12;
  if (item.type === 'Engineer' || item.type === 'PM') score += 7;

  return score;
}

function ProjectPulseGlobalSearch() {
  const [isOpen, setIsOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [items, setItems] = useState([]);
  const [hasLoaded, setHasLoaded] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [status, setStatus] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const inputRef = useRef(null);

  const results = useMemo(() => {
    const trimmed = query.trim();

    if (trimmed.length < 2) return [];

    return items
      .map((item) => ({ item, score: scoreProjectPulseGlobalSearchItem(item, trimmed) }))
      .filter((entry) => entry.score > 0)
      .sort((left, right) => right.score - left.score)
      .slice(0, 12)
      .map((entry) => entry.item);
  }, [items, query]);

  async function loadSearchData(force = false) {
    if (isLoading) return;
    if (hasLoaded && !force) return;

    const headers = getProjectPulseGlobalSearchHeaders();

    if (!headers['X-ProjectPulse-Session']) {
      setStatus('Sign in is required before PHD Search can load.');
      setHasLoaded(false);
      return;
    }

    setIsLoading(true);
    setStatus('Loading PHD Search...');

    try {
      const [workspace, filters, dashboard] = await Promise.all([
        fetchProjectPulseGlobalSearchJson('/api/project-workspace/overview').catch(() => ({})),
        fetchProjectPulseGlobalSearchJson('/api/reports/030/filter-options').catch(() => ({})),
        fetchProjectPulseGlobalSearchJson('/api/dashboard/module-visibility-smoke').catch(() => ({}))
      ]);

      const searchItems = buildProjectPulseGlobalSearchItems(workspace, filters, dashboard);

      setItems(searchItems);
      setHasLoaded(true);
      setStatus(`${searchItems.length} searchable records loaded`);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'PHD Search could not load.');
      setHasLoaded(false);
    } finally {
      setIsLoading(false);
    }
  }

  function openSearch() {
    setIsOpen(true);
    setTimeout(() => {
      inputRef.current?.focus();
      inputRef.current?.select();
    }, 0);

    loadSearchData(false);
  }

  function closeSearch() {
    setIsOpen(false);
  }

  function openResult(item) {
    try {
      window.localStorage.setItem('projectPulse031SearchLastSelection', JSON.stringify({
        selectedAt: new Date().toISOString(),
        type: item.type,
        title: item.title,
        subtitle: item.subtitle,
        meta: item.meta,
        route: item.route
      }));
    } catch {
      // Non-blocking.
    }

    closeSearch();

    if (item.route) {
      window.location.hash = item.route;
    }
  }

  useEffect(() => {
    function handleKeydown(event) {
      const isMac = navigator.platform?.toLowerCase().includes('mac');
      const modifierPressed = isMac ? event.metaKey : event.ctrlKey;

      if (modifierPressed && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        openSearch();
      }

      if (event.key === 'Escape' && isOpen) {
        closeSearch();
      }
    }

    document.addEventListener('keydown', handleKeydown);
    return () => document.removeEventListener('keydown', handleKeydown);
  }, [isOpen, hasLoaded, isLoading]);

  useEffect(() => {
    if (results.length === 0) {
      setActiveIndex(0);
      return;
    }

    setActiveIndex((current) => Math.min(current, results.length - 1));
  }, [results.length]);

  function handleInputKeydown(event) {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((current) => Math.min(results.length - 1, current + 1));
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((current) => Math.max(0, current - 1));
      return;
    }

    if (event.key === 'Enter' && results[activeIndex]) {
      event.preventDefault();
      openResult(results[activeIndex]);
    }
  }

  return (
    <div className="projectpulse-global-search" data-031-real-global-search="true">
      <button
        type="button"
        className="projectpulse-global-search-button"
        onClick={openSearch}
        aria-haspopup="dialog"
        aria-expanded={isOpen}
      >
        <span aria-hidden="true">⌕</span>
        <strong>Search</strong>
        <kbd>Ctrl K</kbd>
      </button>

      {isOpen ? (
        <div className="projectpulse-global-search-backdrop" onMouseDown={(event) => {
          if (event.target === event.currentTarget) closeSearch();
        }}>
          <section className="projectpulse-global-search-modal" role="dialog" aria-modal="true" aria-label="PHD Search">
            <div className="projectpulse-global-search-header">
              <div className="projectpulse-global-search-icon" aria-hidden="true">⌕</div>
              <input
                ref={inputRef}
                type="search"
                value={query}
                placeholder="Search everything in Project Health Dashboard..."
                aria-label="Search everything in Project Health Dashboard"
                autoComplete="off"
                spellCheck="false"
                onChange={(event) => setQuery(event.target.value)}
                onKeyDown={handleInputKeydown}
              />
              <button type="button" className="projectpulse-global-search-close" onClick={closeSearch}>
                Close
              </button>
            </div>

            <div className="projectpulse-global-search-meta">
              <span>PHD Search</span>
              <span>{isLoading ? 'Loading...' : status || 'Type at least two characters'}</span>
            </div>

            <div className="projectpulse-global-search-results">
              {query.trim().length < 2 ? (
                <p className="projectpulse-global-search-state">
                  Search projects, customers, project numbers, documents, assignments, resource requests, engineers, PMs, teams, reports, and modules.
                </p>
              ) : results.length > 0 ? (
                results.map((item, index) => (
                  <button
                    type="button"
                    key={`${item.type}-${item.title}-${item.subtitle}-${index}`}
                    className={index === activeIndex ? 'projectpulse-global-search-result active' : 'projectpulse-global-search-result'}
                    onMouseEnter={() => setActiveIndex(index)}
                    onClick={() => openResult(item)}
                  >
                    <div className="projectpulse-global-search-result-row">
                      <strong>{item.title}</strong>
                      <span>{item.type}</span>
                    </div>
                    {item.subtitle ? <p>{item.subtitle}</p> : null}
                    {item.meta ? <small>{item.meta}</small> : null}
                  </button>
                ))
              ) : (
                <p className="projectpulse-global-search-state">
                  No results found for <strong>{query}</strong>.
                </p>
              )}
            </div>
          </section>
        </div>
      ) : null}
    </div>
  );
}
/* 031_SOURCE_GLOBAL_SEARCH_END */


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

  const orderedModuleItems = [...availableByRoute.values()]
    .filter((item) => item.route !== 'dashboard')
    .sort(compareProjectPulseModules);

  const groups = orderedModuleItems.length > 0
    ? [{ name: 'Modules', expanded: true, items: orderedModuleItems }]
    : [];

  return {
    primary,
    groups
  };
}


function SignalLogo() {

  return (
    <div className="brand-lockup" aria-label="Project Health Dashboard">
      <img className="brand-logo-image" src={usSignalLogoUrl} alt="US Signal" />
      <div>
        <strong>Project Health Dashboard</strong>
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



function getInstalledProjectPulseModuleRegistry() {
  return sortProjectPulseModules([
    {
      route: 'timesheet',
      title: 'Timesheet',
    navLabel: 'MODULE 001',
      group: 'Time Management',
      permissions: [],
      description: 'Allows users to enter, save, submit, and review weekly or daily time entries.'
    },
    {
      route: 'manager-approval',
      title: 'Approval Inbox',
    navLabel: 'MODULE 002',
      group: 'Approvals',
      permissions: ['APPROVE_TIME', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Lets managers review submitted time, approve valid entries, and return entries that need correction.'
    },
    {
      route: 'workflow',
      title: 'Approval / Export / Audit Workflow',
    navLabel: 'MODULE 007',
      group: 'Approvals',
      permissions: ['VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'MANAGE_ACCOUNT_RECONCILIATION', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'VIEW_AUDIT_TRAIL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Coordinates PM validation, accounting readiness, reconciliation, lock, export preparation, and audit visibility after manager approval.'
    },
    {
      route: 'utilization',
      title: 'Utilization',
    navLabel: 'MODULE 003',
      group: 'Resource Management',
      permissions: ['VIEW_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Shows billable and utilization-eligible performance against quarterly and annual targets.'
    },
    {
      route: 'project-workload',
      title: 'Project Workload',
    navLabel: 'MODULE 018',
      group: 'Project Management',
      permissions: ['VIEW_PROJECT_WORKLOAD', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Shows project managers their active projects, closed projects, status mix, hours, and workload risk.'
    },
    {
      route: 'project-workspace',
      title: 'Project Workspace',
    navLabel: 'MODULE 019',
      group: 'Project Delivery',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides a role-scoped workspace for assigned projects, tasks, documents, assigned hours, used hours, and remaining hours.'
  },
  /* MODULE_066A1_PROJECT_FLOWHIVE_INSTALLED_REGISTRY_START */
  {
    route: 'project-flowhive',
    title: 'Project FlowHive',
    navLabel: 'MODULE 066',
    group: 'Project Delivery',
    permissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_PROJECT_WORKLOAD', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides the read-only, server-scoped Project FlowHive portfolio, canonical task grid, assignments, and controlled planning capability roadmap.'
  },
  /* MODULE_066A1_PROJECT_FLOWHIVE_INSTALLED_REGISTRY_END */
  /* MODULES_064_074_RELEASE_TRAIN_INSTALLED_REGISTRY_START */
  {
    route: 'ai-provider-configuration',
    title: 'AI Provider Configuration Center',
    navLabel: 'MODULE 064',
    status: 'Release candidate',
    group: 'Security',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides shared Claude-first, OpenAI-second, governed-local-fallback routing, health, circuit, usage, and feature configuration.'
  },
  {
    route: 'entra-secret-administration',
    title: 'Entra Secret Administration',
    navLabel: 'MODULE 065',
    status: 'Locked runtime release candidate',
    group: 'Security',
    permissions: ['MANAGE_ENTRA_SECRET', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides fail-closed application credential lifecycle readiness without storing, activating, or rotating a secret.'
  },
  {
    route: 'global-mail-configuration',
    title: 'Global Mail Configuration Center',
    navLabel: 'MODULE 067',
    status: 'Read-only release candidate',
    group: 'Operations',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows non-secret Microsoft 365 mail configuration, shared consumer ownership, and controlled migration gates.'
  },
  {
    route: 'system-architecture',
    title: 'System Architecture & Dependency Map',
    navLabel: 'MODULE 068',
    status: 'Read-only release candidate',
    group: 'Operations',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows the versioned logical component, data, authentication, integration, environment, and live-status ownership map.'
  },
  {
    route: 'qualifications-certifications',
    title: 'Qualifications & Certification Matrix',
    navLabel: 'MODULE 069',
    status: 'Read-only release candidate',
    group: 'Resources',
    permissions: ['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_OWN_UTILIZATION', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides role-scoped, identity-backed qualification and certification visibility with expiration status.'
  },
  {
    route: 'capacity-pipeline-forecast',
    title: 'Capacity & Pipeline Forecasting',
    navLabel: 'MODULE 070',
    status: 'Read-only release candidate',
    group: 'Resource Management',
    permissions: ['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'VIEW_PROJECT_INTAKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Calculates continuous weekly capacity, committed work, weighted unfilled demand, scenarios, remaining capacity, and utilization.'
  },
  {
    route: 'oncall-scheduling',
    title: 'On-Call Scheduling',
    navLabel: 'MODULE 071',
    status: 'Compatibility-adapter release candidate',
    group: 'Operations',
    permissions: [],
    description: 'Provides a US Signal-branded on-call schedule, role-governed management, identity dropdowns, and versioned public routing APIs.'
  },
  {
    route: 'oneassist-routing-directory',
    title: 'OneAssist Routing Directory',
    navLabel: 'MODULE 072',
    status: 'Compatibility-adapter release candidate',
    group: 'Operations',
    permissions: [],
    description: 'Provides the unmasked five-digit OneAssist routing directory, governed editing, import previews, exports, and public resolution APIs.'
  },
  {
    route: 'sales-coverage-alignment',
    title: 'Sales Coverage Alignment',
    navLabel: 'MODULE 073',
    status: 'Validated unsaved-draft release candidate',
    group: 'Sales & Opportunities',
    permissions: [],
    description: 'Provides current alignment signals and a role-governed, effective-dated identity-backed draft editor without persistence.'
  },
  {
    route: 'oem-vendor-directory',
    title: 'OEM & Vendor Directory',
    navLabel: 'MODULE 074',
    status: 'Validated unsaved-draft release candidate',
    group: 'Sales & Opportunities',
    permissions: [],
    description: 'Provides a US Signal-branded, role-governed OEM and vendor directory draft with validation and export.'
  },
  {
    route: 'defect-tracker',
    title: 'Defect Intake & Resolution Tracker',
    navLabel: 'MODULE 076',
    status: 'Complete source · fail-closed persistence',
    group: 'Help & Documentation',
    permissions: [],
    description: 'Provides a US Signal-branded defect register, Help and GitHub intake contracts, identity-backed assignment, dates, comments, resolution timing, and locked notification automation.'
  },
  /* MODULES_075_080_RUNTIME_REGISTRY_START */
  {
    route: 'integration-event-gateway',
    title: 'Integration Automation & Event Gateway',
    navLabel: 'MODULE 075',
    status: 'Runtime registered · mutations locked',
    group: 'Platform Operations',
    permissions: [],
    description: 'Provides governed read-only integration contracts and delivery evidence while connector actions remain locked.'
  },
  {
    route: 'release-deployment-control',
    title: 'Release, Deployment & Rollback Control Center',
    navLabel: 'MODULE 077',
    status: 'Runtime registered · deployment locked',
    group: 'Platform Operations',
    permissions: [],
    description: 'Provides governed release and environment evidence while deployment and rollback actions remain locked.'
  },
  {
    route: 'observability-slo-health',
    title: 'Observability, SLO & Application Health Center',
    navLabel: 'MODULE 078',
    status: 'Runtime registered · delivery locked',
    group: 'Platform Operations',
    permissions: [],
    description: 'Provides governed health, signal, SLO, alert, and retention-policy read surfaces without external telemetry delivery.'
  },
  {
    route: 'data-governance-retention',
    title: 'Data Governance, Retention & Privacy Center',
    navLabel: 'MODULE 079',
    status: 'Runtime registered · actions locked',
    group: 'Security & Audit',
    permissions: [],
    description: 'Provides governed data classification, lineage, retention, legal-hold, and privacy read surfaces while actions remain locked.'
  },
  {
    route: 'customer-delivery-acceptance',
    title: 'Customer Delivery & Acceptance Portal',
    navLabel: 'MODULE 080',
    status: 'Runtime registered · sharing locked',
    group: 'Project Operations',
    permissions: [],
    description: 'Provides governed delivery, milestone, artifact, review, acceptance, and sharing-policy read surfaces while external actions remain locked.'
  },
  /* MODULES_075_080_RUNTIME_REGISTRY_END */
  /* MODULES_064_074_RELEASE_TRAIN_INSTALLED_REGISTRY_END */
  /* MODULE_998_SYSTEM_DIAGNOSTICS_INSTALLED_REGISTRY_START */
  {
    route: 'system-diagnostics',
    title: 'System Diagnostic & Controlled Remediation Center',
    navLabel: 'MODULE 998',
    status: 'Complete fail-closed source checkpoint',
    group: 'Operations',
    permissions: ['VIEW_SYSTEM_DIAGNOSTICS', 'MANAGE_SYSTEM_REMEDIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides sanitized diagnostics, issue classification, evidence policy, runbook ownership, and a locked controlled-remediation lifecycle.'
  },
  /* MODULE_998_SYSTEM_DIAGNOSTICS_INSTALLED_REGISTRY_END */
  {
    route: 'project-intake',
      title: 'Project Intake',
    navLabel: 'MODULE 020',
      group: 'Project Intake',
      permissions: ['VIEW_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Captures project requests, customer selection, planned costs, intake documents, signed date aging, triage details, and resource request readiness.'
    },
    {
      route: 'customer-directory',
      title: 'Customer Directory',
    navLabel: 'MODULE 021',
      group: 'Customers',
      permissions: ['VIEW_CUSTOMERS', 'MANAGE_CUSTOMERS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Maintains customer records, contacts, and customer data used by project, billing, cost, and reconciliation workflows.'
    },
    {
      route: 'user-guide',
      href: '#user-guide',
      title: 'ProjectPulse Complete User Guide',
      navLabel: 'MODULE 999',
      status: 'Active',
      group: 'Help & Documentation',
      permissions: [],
      description: 'Searchable documentation for every global platform function and every installed ProjectPulse module.'
    },
    {
      route: 'opportunities',
      href: '#opportunities',
      title: 'Opportunities & Action Tracker',
      navLabel: 'MODULE 063',
      status: 'Active',
      group: 'Sales & Opportunities',
      permissions: ['VIEW_CUSTOMERS', 'VIEW_PROJECT_INTAKE', 'VIEW_PROJECT_WORKSPACE', 'VIEW_WORK_TASK_BUILDER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Lets Sales, Presales, and Engineering create opportunities, add collaborative tasks, complete actions, and track active, closed, creator, and last-updated history.'
    },
    {
      route: 'cost-alerts',
      title: 'Cost Alert Overrun',
    navLabel: 'MODULE 022',
      group: 'Cost Control',
      permissions: ['VIEW_COST_ALERTS', 'MANAGE_COST_ALERTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Monitors planned cost, assigned hours, used hours, over-assignment risk, and notification routing for cost overrun alerts.'
    },
    {
      route: 'time-compliance',
      title: 'Time Compliance',
    navLabel: 'MODULE 023',
      group: 'Compliance',
      permissions: ['VIEW_TIME_COMPLIANCE', 'MANAGE_TIME_COMPLIANCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Previews missing time, reminder readiness, manager/PTC visibility, compliance notifications, and month-end time controls.'
    },
    /* MODULE_REGISTRY_RECOVERY_INSTALLED_024_030_058_START */
    {
      route: "sales-intake",
      href: "#sales-intake",
      title: "Sales-to-Delivery Intake Foundation",
      navLabel: "MODULE 024",
      group: "Sales & Opportunities",
      description: "Validate intake, signed SOW and GSD artifacts, handoff readiness, and assignment preparation.",
      permissions: ["VIEW_PROJECT_INTAKE", "MANAGE_PROJECT_INTAKE", "VIEW_CUSTOMERS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "sow-generator",
      href: "#sow-generator",
      title: "SOW Generator + Claude Review Workflow",
      navLabel: "MODULE 025",
      group: "Sales & Opportunities",
      description: "Prepare SOW drafts, technical reviews, signed-document readiness, and controlled delivery handoff.",
      permissions: ["VIEW_PROJECT_INTAKE", "VIEW_PROJECT_WORKSPACE", "VIEW_CUSTOMERS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "crm-integration",
      href: "#crm-integration",
      title: "CRM/ERP Integration Control Center",
      navLabel: "MODULE 026",
      group: "Sales & Opportunities",
      description: "Connect SELL, Salesforce, Certinia, ServiceNow, and manually registered CRM/ERP platforms and review sanitized availability status.",
      permissions: ["VIEW_INTEGRATIONS_026", "MANAGE_INTEGRATIONS_026", "VIEW_CUSTOMERS", "VIEW_PROJECT_INTAKE", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "signed-handoff",
      href: "#signed-handoff",
      title: "Signed SOW Handoff + Assignment Trigger",
      navLabel: "MODULE 027",
      group: "Sales & Opportunities",
      description: "Prepare signed handoff, stakeholder notification, and PM and engineering assignment workflows.",
      permissions: ["VIEW_PROJECT_INTAKE", "VIEW_PROJECT_WORKSPACE", "VIEW_RESOURCE_SCHEDULING", "MANAGE_RESOURCE_SCHEDULING", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "ai-time-entry",
      href: "#ai-time-entry",
      title: "SOW-Aware AI Time Entry Generator",
      navLabel: "MODULE 028",
      group: "Work Management",
      description: "Create engineer-reviewed AI time-entry drafts using assigned work and signed scope context.",
      permissions: ["VIEW_TIME_ENTRY", "VIEW_PROJECT_WORKSPACE", "VIEW_ENGINEERING_PROJECT_DOCUMENTS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "uat-validation",
      href: "#uat-validation",
      title: "User Acceptance / Role + Workflow Validation Center",
      navLabel: "MODULE 029",
      group: "Platform Operations",
      description: "Validate role access, workflows, View-As protection, module routes, and readiness evidence.",
      permissions: ["VIEW_AUDIT_TRAIL", "VIEW_REPORTS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "reporting",
      href: "#reporting",
      title: "Reporting / Accounting / Invoicing / Analytics",
      navLabel: "MODULE 030",
      group: "Reports & Workflow",
      description: "Provide operational, accounting, invoicing, workflow, system, and executive reporting.",
      permissions: ["VIEW_REPORTS", "MANAGE_REPORTS", "VIEW_EXECUTIVE_REPORTING", "VIEW_ACCOUNT_RECONCILIATION", "EXPORT_TIME_EXCEL", "EXPORT_TIME_PDF", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "cicd-pipeline",
      href: "#cicd-pipeline",
      title: "Autonomous CI/CD Foundation",
      navLabel: "MODULE 058",
      group: "Platform Operations",
      description: "Audit and operate CI/CD workflows, deployment validation, smoke testing, summaries, and rollback controls.",
      permissions: ["SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    /* MODULE_REGISTRY_RECOVERY_INSTALLED_024_030_058_END */

    {
      route: 'holiday-admin',
      title: 'Holiday Management',
    navLabel: 'MODULE 004',
      group: 'Administration',
      permissions: ['VIEW_HOLIDAYS', 'MANAGE_HOLIDAYS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Manages company holidays, holiday visibility, uploads, and timesheet holiday readiness.'
    },
    {
      route: 'audit-history',
      title: 'Audit History',
    navLabel: 'MODULE 008',
      group: 'Audit',
      permissions: ['VIEW_AUDIT_TRAIL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Shows login, admin, notification, approval, export, service, and system audit events for accountability.'
    },
    /* MODULE_997_SECURITY_OPERATIONS_INSTALLED_REGISTRY_START */
    {
      route: 'security-operations',
      title: 'Security Operations, Threat Intelligence & Response Center',
      navLabel: 'MODULE 997',
      status: 'Complete fail-closed source checkpoint',
      group: 'Security',
      permissions: ['VIEW_SECURITY_OPERATIONS', 'MANAGE_SECURITY_RESPONSE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Provides sanitized security posture, alert and incident contracts, threat-intelligence policy, control ownership, reporting boundaries, and a locked response lifecycle.'
    },
    /* MODULE_997_SECURITY_OPERATIONS_INSTALLED_REGISTRY_END */
    {
      route: 'user-admin',
      title: 'User Administration',
    navLabel: 'MODULE 009',
      group: 'Security',
      permissions: ['MANAGE_USERS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Manages users, active status, local account settings, role assignments, and access controls.'
    },
    {
      route: 'work-task-builder',
      title: 'Work Task Builder',
    navLabel: 'MODULE 011',
      status: 'Active',
      permissions: ['VIEW_WORK_TASK_BUILDER', 'MANAGE_WORK_TASK_BUILDER', 'ASSIGN_WORK_TASKS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Defines work task categories, billing classification, utilization classification, and scoped project task assignment.'
    },
    {
      route: 'role-admin',
      title: 'Role Administration',
    navLabel: 'MODULE 012',
      group: 'Security',
      permissions: ['VIEW_ROLE_ADMIN_DIRECTORY', 'MANAGE_ROLES', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Defines roles, shows assigned users, displays permissions by module, and manages role-based security configuration.'
    },
    {
      route: 'azure-admin',
      title: 'Azure / Entra Administration',
    navLabel: 'MODULE 010',
      group: 'Security',
      permissions: ['MANAGE_AZURE_AD', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Manages Entra import, reconciliation, sync settings, and identity-readiness checks.'
    },
    {
      route: 'service-control',
      title: 'Service Control Center',
    navLabel: 'MODULE 013',
      group: 'Operations',
      permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Provides operational service restart controls, service health checks, and audit-backed service management.'
    },
    {
      route: 'backup-dr',
      title: 'Backup / DR Center',
    navLabel: 'MODULE 014',
      group: 'Operations',
      permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Shows backup and disaster recovery readiness, backup state, service backup status, and restore preparedness.'
    },
    {
      route: 'restore-validation',
      title: 'Restore Validation Center',
    navLabel: 'MODULE 015',
      group: 'Operations',
      permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Validates restore points, restore readiness, and restore test evidence before relying on backups.'
    },
    {
      route: 'backup-retention',
      title: 'Backup Retention Center',
    navLabel: 'MODULE 016',
      group: 'Operations',
      permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Manages backup retention policy, cleanup readiness, and retention compliance visibility.'
    },
    {
      route: 'replication-sync',
      title: 'Replication / Sync Status',
    navLabel: 'MODULE 017',
      group: 'Operations',
      permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
      description: 'Shows replication and synchronization status across database, backup, and operational readiness workflows.'
    },
  {
    route: 'workflow',
    title: 'Workflow Operational Readiness',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_WORKFLOW_OPERATIONAL_READINESS', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows manager review, PM validation, accounting review, export readiness, role guidance, and workflow stage health.'
  },
  {
    route: 'workflow',
    title: 'Export Packages',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['DOWNLOAD_TIME_EXPORT_PACKAGE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Generates and downloads CSV/Excel-ready export packages with item counts, hours, package metadata, and audit evidence.'
  },
  {
    route: 'workflow',
    title: 'Workflow Audit Evidence',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_WORKFLOW_AUDIT_EVIDENCE', 'VIEW_AUDIT_TRAIL', 'VIEW_ACCOUNT_RECONCILIATION', 'PROJECT_TIME_APPROVAL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Displays detailed audit evidence for approval, reconciliation, lock, and export package events.'
  },
  {
    route: 'workflow',
    title: 'Audit History Events',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_AUDIT_HISTORY_EVENTS', 'VIEW_WORKFLOW_AUDIT_EVIDENCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows workflow, export, reconciliation, lock, and approval audit history events.'
  },
  {
    route: 'workflow',
    title: 'Workflow Preflight Validation',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_WORKFLOW_ACTION_CAPABILITIES', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows production workflow preflight checks, role capabilities, eligible statuses, blockers, and readiness evidence.'
  },
  {
    route: 'dashboard',
    title: 'Dashboard Module Visibility Smoke',
    group: 'System',
    permissions: ['VIEW_MODULE_VISIBILITY_SMOKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Checks module registry coverage so new modules appear on the dashboard for the correct roles.'
  },
  {
    route: 'workflow',
    title: 'Export Package Readiness Summary',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_EXPORT_PACKAGE_READINESS_SUMMARY', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'EXPORT_TIME_EXCEL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows export package readiness, generated package metadata, download counts, and last-download evidence.'
  },
  {
    route: 'workflow',
    title: 'Export Package Evidence Detail',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_EXPORT_PACKAGE_EVIDENCE_DETAIL', 'VIEW_WORKFLOW_AUDIT_EVIDENCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Connects export packages to item-level and audit evidence detail.'
  },
  {
    route: 'workflow',
    title: 'Accounting Reconciliation Workbench',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_ACCOUNTING_RECONCILIATION_WORKBENCH', 'MANAGE_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Groups accounting reconciliation queues, exception status, missing project/task links, and attention items.'
  },
  {
    route: 'workflow',
    title: 'Locked Period Audit Evidence',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_LOCKED_PERIOD_AUDIT_EVIDENCE', 'VIEW_WORKFLOW_AUDIT_EVIDENCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows locked and reconciled time evidence for period-close audit review.'
  },
  {
    route: 'role-admin',
    title: 'Role Access Matrix',
    navLabel: 'MODULE 012',
    group: 'Security',
    permissions: ['VIEW_ROLE_ACCESS_MATRIX', 'VIEW_ROLE_ADMIN_DIRECTORY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows role-to-permission coverage for governance, route visibility, and role enforcement validation.'
  },  {
    route: 'production-data-readiness',
    href: '#production-data-readiness',
    title: 'Production Data Readiness Center',
    navLabel: 'Data Readiness',
    status: 'Operational',
    group: 'System Operations',
    permissions: ['VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows whether users, roles, customers, projects, tasks, time entries, approvals, exports, audit events, and notification evidence are ready for production.'
  },

  {
    route: 'production-readiness',
    href: '#production-readiness',
    title: 'Production Readiness Command Center',
    navLabel: 'Production Readiness',
    status: 'Operational',
    group: 'System Operations',
    permissions: ['VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows a web-visible readiness center with protected backend checks, workflow links, route governance, and release validation guidance.'
  },
  {
    route: 'workflow',
    title: 'Workflow Validation Rules',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_WORKFLOW_VALIDATION_RULES', 'VIEW_APPROVAL_WORKFLOW', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Documents workflow validation rules and current evidence for export, audit, and production preflight controls.'
  },
  {
    route: 'workflow',
    title: 'Workflow Operations Center',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_WORKFLOW_OPERATIONS_CENTER', 'VIEW_APPROVAL_WORKFLOW', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Central status view for workflow operations, audit history, export readiness, reconciliation, and validation.'
  },
  {
    route: 'dashboard',
    title: 'Production Validation Automation',
    group: 'System',
    permissions: ['VIEW_MODULE_VISIBILITY_SMOKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Provides production validation script coverage for endpoint smoke checks, dashboard registry verification, and access enforcement.'
  },
  {
    route: 'workflow',
    title: 'Production Export Evidence',
    navLabel: 'MODULE 007',
    group: 'Approval / Export / Audit',
    permissions: ['VIEW_PRODUCTION_EXPORT_EVIDENCE', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Shows export package evidence, package download history, audit event counts, and production evidence readiness.'
  },
  {
    route: 'role-admin',
    title: 'Route Permission Contracts',
    navLabel: 'MODULE 012',
    group: 'Security',
    permissions: ['VIEW_ROUTE_PERMISSION_CONTRACTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Defines route-level permission contracts, allowed roles, restricted roles, and production guardrails.'
  },
  {
    route: 'dashboard',
    title: 'Navigation Registry Integrity Guard',
    group: 'System',
    permissions: ['VIEW_NAVIGATION_REGISTRY_INTEGRITY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Validates dashboard, navigation, route contract, and production module registry integrity.'
  },
  {
    route: 'dashboard',
    title: 'Engineer Negative Access Smoke',
    group: 'Security',
    permissions: ['VIEW_ENGINEER_NEGATIVE_ACCESS_SMOKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Confirms engineer-only users remain denied from restricted workflow, export, accounting, and role matrix controls.'
  },
  {
    route: 'invoice-billing-center',
    title: 'Invoice & Billing Center',
    navLabel: 'MODULE 042',
    group: 'Reports & Workflow',
    permissions: ['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    description: 'Prepares detailed partial and final invoice packages with customer identifiers, time-entry evidence, rates, hours, amounts, flexible headers, recently closed work, and billing reports.'
  },
  ]);
}


function getInstalledModuleDescription(module) {
  const route = module?.route ?? '';

  const descriptions = {
    dashboard: 'Provides a role-based landing page with the modules, alerts, and workflow areas available to the signed-in user.',
    'production-readiness': 'Shows a web-visible production readiness command center backed by protected readiness checks, route governance, and release validation guidance.',
    'production-data-readiness': 'Shows production data readiness for users, roles, customers, projects, tasks, timesheets, approvals, exports, audit evidence, and notifications.',
    timesheet: 'Allows engineers and eligible users to enter, save, submit, and review weekly or day-level time entries.',
    utilization: 'Shows billable and eligible utilization performance against quarterly and annual targets.',
    'project-workload': 'Shows project managers their assigned project workload, active and closed project counts, status mix, hours, and workload risk.',
    'manager-approval': 'Lets managers review submitted time, approve valid days, return days for correction, and monitor pending approval counts.',
    'project-workspace': 'Gives engineers and project roles a scoped project workspace with assigned projects, tasks, documents, assigned hours, used hours, and remaining hours.',
    'project-intake': 'Captures project intake requests, customer selection, planned costs, documents, triage information, and resource request readiness.',
    'work-register': 'Searches active, closed, archived, and historical work across customers, stakeholders, tasks, documents, hours, and costs.',
    'rate-card-administration': 'Manages standard, customer-specific, Toyota, Hyundai, service request, emergency, and travel rate cards.',
    'customer-directory': 'Maintains customer/account records, customer contacts, and customer data used by intake, project, cost, billing, and reconciliation workflows.',
    'user-guide': 'Explains every global ProjectPulse function and every installed module with searchable procedures, roles, statuses, and troubleshooting.',
    opportunities: 'Tracks active and closed sales opportunities, collaborative Sales, Presales, and Engineering actions, ownership, completion, dates, and accountable history.',
    'cost-alerts': 'Monitors project planned cost, assigned hours, used hours, over-assignment risk, and notification routing for cost overrun alerts.',
    workflow: 'Coordinates PM validation, accounting readiness, reconciliation, locking, export preparation, and audit visibility after manager approval.',
    'audit-history': 'Shows login, admin, notification, approval, export, and system audit events for accountability and troubleshooting.',
    'security-operations': 'Shows sanitized security readiness, alert and incident contracts, threat-intelligence policy, control ownership, and fail-closed incident-response governance.',
    'time-compliance': 'Previews missing time, reminder scenarios, manager/PTC visibility, compliance notification readiness, and month-end time controls.',
    'holiday-admin': 'Manages company holidays, holiday upload, holiday visibility, and holiday-related timesheet automation.',
    'user-admin': 'Manages Project Health Dashboard users, roles, active status, local account status, and administrator-controlled access settings.',
    'azure-admin': 'Manages Azure/Entra import, reconciliation, sync settings, and identity-readiness checks.',
    'ai-provider-configuration': 'Shows sanitized shared AI configuration, provider health, feature routing, circuit state, and locked secret lifecycle controls.',
    'entra-secret-administration': 'Shows privileged application-credential readiness and fail-closed rotation workflow contracts without exposing secret values.',
    'role-admin': 'Manages application roles, permissions, module access, and role-based security configuration.',
    'service-control': 'Provides operational service restart controls, service health checks, and audit-backed service management.',
    'global-mail-configuration': 'Shows non-secret shared Microsoft 365 mail configuration, consumer ownership, and controlled activation readiness.',
    'system-architecture': 'Shows the versioned ProjectPulse component, data, authentication, integration, environment, and status-ownership map.',
    'qualifications-certifications': 'Shows identity-backed skills, certifications, competency, experience, and expiration within server-authorized scope.',
    'capacity-pipeline-forecast': 'Shows continuous weekly engineering capacity, committed work, weighted demand, scenarios, remaining capacity, and utilization.',
    'oncall-scheduling': 'Shows the US Signal on-call schedule and roster with Manager and Engineering Team Lead management controls.',
    'oneassist-routing-directory': 'Shows the OneAssist customer routing directory and unmasked five-digit PINs with role-governed editing.',
    'sales-coverage-alignment': 'Shows current coverage signals and a validated effective-dated alignment draft backed by ProjectPulse identities.',
    'oem-vendor-directory': 'Shows a validated, US Signal-branded OEM and vendor directory draft with governed role-aware editing.',
    'system-diagnostics': 'Shows sanitized system diagnostics, ownership, evidence, runbooks, and fail-closed controlled-remediation readiness.',
    'defect-tracker': 'Shows the governed defect register and intake surface for Help, GitHub, Claude through GitHub, and ChatGPT through GitHub with automatic-ID, identity assignment, date, resolution, comment, and notification contracts.',
    'backup-dr': 'Shows backup and disaster recovery readiness, backup state, service backup status, and restore preparedness.',
    'backup-retention': 'Manages backup retention policy, cleanup readiness, and retention compliance visibility.',
    'restore-validation': 'Validates restore points, restore readiness, and restore test evidence before relying on backups.',
    'replication-sync': 'Shows replication and synchronization status across backup, database, and operational readiness workflows.',
    'invoice-billing-center': 'Prepares partial and final invoice packages, preserves detailed time and rate evidence, and supports billing and Over / Under reporting.',
    'psa-modules': 'Displays PSA workflow modules such as expense, invoice, project, and billing readiness areas as they are connected.'
  };

  return module?.description || descriptions[route] || 'Installed Project Health Dashboard module available to this role. Review the module for workflow details, operational status, and next actions.';
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
  const [isSideNavigationOpen, setIsSideNavigationOpen] = useState(false);
  const [isTopMoreNavigationOpen, setIsTopMoreNavigationOpen] = useState(false);
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

  /* 056A_SHARED_ROUTE_DATASET_START */
  useLayoutEffect(() => {
    const routeKey = normalizeRoute(activeRoute);

    /*
     * Maintain all historical attribute spellings while older page-specific
     * styles are migrated. Updating them in a layout effect prevents one route
     * from painting with another route's visibility rules.
     */
    document.documentElement.dataset.projectPulseActiveRoute = routeKey;
    document.body.dataset.projectPulseActiveRoute = routeKey;
    document.body.dataset.projectpulseActiveRoute = routeKey;
    document.body.dataset.projectPulseRoute = routeKey;

    window.dispatchEvent(new CustomEvent('projectpulse:route-state-ready', {
      detail: { route: routeKey }
    }));

    return () => {
      if (document.documentElement.dataset.projectPulseActiveRoute === routeKey) {
        delete document.documentElement.dataset.projectPulseActiveRoute;
      }

      if (document.body.dataset.projectPulseActiveRoute === routeKey) {
        delete document.body.dataset.projectPulseActiveRoute;
      }

      if (document.body.dataset.projectpulseActiveRoute === routeKey) {
        delete document.body.dataset.projectpulseActiveRoute;
      }

      if (document.body.dataset.projectPulseRoute === routeKey) {
        delete document.body.dataset.projectPulseRoute;
      }
    };
  }, [activeRoute]);
  /* 056A_SHARED_ROUTE_DATASET_END */
  /* 039A_ROUTE_REFRESH_RESTORE_EFFECT_START */
  useEffect(() => {
    installProjectPulseManualScrollRestoration();

    const handlePageShow = () => resetProjectPulseViewportForRoute(getRouteFromHash());
    const handleHashRefresh = () => resetProjectPulseViewportForRoute(getRouteFromHash());

    window.addEventListener('pageshow', handlePageShow);
    window.addEventListener('popstate', handleHashRefresh);

    return () => {
      window.removeEventListener('pageshow', handlePageShow);
      window.removeEventListener('popstate', handleHashRefresh);
    };
  }, []);

  useEffect(() => {
    resetProjectPulseViewportForRoute(activeRoute);
  }, [activeRoute]);
  /* 039A_ROUTE_REFRESH_RESTORE_EFFECT_END */

  /* 039D_APPROVAL_INIT_CRASH_FIX */
  /* 039C_APPROVAL_INDICATOR_EFFECT_START */
  useEffect(() => {
    installProjectPulseApprovalUiNormalizer();
    normalizeProjectPulseApprovalUi();
  }, []);

  useEffect(() => {
    normalizeProjectPulseApprovalUi();
    const timeoutId = window.setTimeout(normalizeProjectPulseApprovalUi, 500);
    return () => window.clearTimeout(timeoutId);
  }, [activeRoute, authSession?.sessionToken]);
  /* 039C_APPROVAL_INDICATOR_EFFECT_END */



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
    defaultRoleCode: 'ENGINEERING',
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
  /* MODULE_001_TIMESHEET_MULTIVIEW_START */
  const [timesheetView, setTimesheetView] = useState(() => {
    const allowedViews = ['weekly', 'daily', 'queue', 'quick', 'calendar'];
    const savedView = window.localStorage.getItem('projectPulseTimesheetView');
    if (allowedViews.includes(savedView)) return savedView;
    return window.matchMedia('(max-width: 760px)').matches ? 'daily' : 'weekly';
  });
  const [activitySearch, setActivitySearch] = useState('');
  const [focusedDayDate, setFocusedDayDate] = useState('');
  /* MODULE_001_TIMESHEET_MULTIVIEW_STATE_END */
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
          (canViewExecutiveOrAccountingSummaries ? fetchJson('/api/expenses/summary', authSession) : Promise.resolve({ count: 0, skipped: '052C_restricted_for_effective_role' })),
          (canViewExecutiveOrAccountingSummaries ? fetchJson('/api/invoicing/summary', authSession) : Promise.resolve({ count: 0, skipped: '052C_restricted_for_effective_role' })),
          (canViewExecutiveOrAccountingSummaries ? fetchJson('/api/reporting/executive-dashboard', authSession) : Promise.resolve({ count: 0, skipped: '052C_restricted_for_effective_role' }))
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
  const regularAssignedTasks = assignedOpenTasks.filter((task) => projectPulseTaskTimeEntrySection(task) === 'regular');
  const requestAssignedTasks = assignedOpenTasks.filter((task) => projectPulseTaskTimeEntrySection(task) === 'requests');
  const activePolicy = utilizationPolicies.data?.policies?.[0];
  const selectedActivitySource = activitySourceOptions.find((option) => option.key === activitySource) ?? activitySourceOptions[0];
  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isAnyDayEditable = days.length === 0 || days.some((day) => isDayEditable(day.date));

  /* MODULE_001_TIMESHEET_MULTIVIEW_DERIVED_START */
  const normalizedActivitySearch = activitySearch.trim().toLowerCase();
  const activityMatchesSearch = (...values) =>
    !normalizedActivitySearch ||
    values.some((value) => String(value ?? '').toLowerCase().includes(normalizedActivitySearch));

  const filteredCategories = categories.filter((category) =>
    activityMatchesSearch(
      category.name,
      category.code,
      category.description,
      category.utilizationBucket
    )
  );

  const filteredRegularAssignedTasks = regularAssignedTasks.filter((task) =>
    activityMatchesSearch(
      task.taskName,
      task.taskCode,
      task.projectName,
      task.projectCode,
      task.clientName,
      task.projectManagerName
    )
  );

  const filteredRequestAssignedTasks = requestAssignedTasks.filter((task) =>
    activityMatchesSearch(
      task.taskName,
      task.taskCode,
      task.projectName,
      task.projectCode,
      task.clientName,
      task.projectManagerName,
      task.workType
    )
  );

  const todayIso = new Date().toISOString().slice(0, 10);
  const focusedDay =
    days.find((day) => day.date === focusedDayDate) ??
    days.find((day) => day.date === todayIso) ??
    days[0] ??
    null;
  /* MODULE_001_TIMESHEET_MULTIVIEW_DERIVED_END */

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


  /* 043B_PROFILE_IMAGE_PERSISTENCE_LOAD_EFFECT_START */
  useEffect(() => {
    let cancelled = false;

    async function loadServerProfilePreferences() {
      if (!authSession?.sessionToken) return;

      try {
        const serverPreferences = await loadPersistentProfilePreferences(authSession);
        if (cancelled || !serverPreferences) return;

        const serverProfilePhotoDataUrl = serverPreferences.profilePhotoDataUrl ?? '';

        if (serverProfilePhotoDataUrl) {
          setUserPreferences((current) => ({
            ...current,
            profilePhotoDataUrl: serverProfilePhotoDataUrl
          }));

          setProfileDraft((current) => ({
            ...current,
            profilePhotoDataUrl: serverProfilePhotoDataUrl
          }));
        }
      } catch {
        // Keep local preferences available if backend preference load is unavailable.
      }
    }

    loadServerProfilePreferences();

    return () => {
      cancelled = true;
    };
  }, [authSession?.sessionToken, authSession?.username]);
  /* 043B_PROFILE_IMAGE_PERSISTENCE_LOAD_EFFECT_END */


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

  async function saveProfileSettings(event) {
    event.preventDefault();

    setProfileSettingsStatus('Saving profile settings...');

    try {
      const persistentPreferences = await savePersistentProfilePreferences(authSession, {
        ...profileDraft,
        theme: profileDraft.theme === 'dark' ? 'dark' : 'light'
      });

      const savedPreferences = {
        ...persistentPreferences,
        theme: persistentPreferences.theme === 'dark' ? 'dark' : 'light'
      };

      saveStoredUserPreferences(authSession, savedPreferences);
      setUserPreferences(savedPreferences);
      setProfileDraft(savedPreferences);
      setTheme(savedPreferences.theme);
      setProfileSettingsStatus('Profile settings saved to persistent profile storage.');
      setIsSettingsOpen(false);
    } catch (error) {
      setProfileSettingsStatus(error instanceof Error ? error.message : 'Unable to save profile settings. Try using a smaller profile picture.');
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
        setLoginStatus('Microsoft Entra SSO route selected.');
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

  function continueWithSsoPlaceholder() {
    const username = loginUsername.trim().toLowerCase();

    if (!username) {
      setLoginStatus('Enter your Microsoft Entra email address.');
      return;
    }

    setLoginStatus('Redirecting to Microsoft Entra ID...');

    const parameters = new URLSearchParams({
      loginHint: username,
      prompt: 'select_account'
    });

    window.location.assign(`/api/auth/sso/start?${parameters.toString()}`);
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
        notes: passwordResetNotes || 'Password reset requested from Project Health Dashboard login screen.'
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
          defaultRoleCode: 'ENGINEERING',
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
          defaultRoleCode: 'ENGINEERING',
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
        defaultRoleCode: 'ENGINEERING',
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
        defaultRoleCode: configResult.defaultRoleCode ?? 'ENGINEERING',
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
        defaultRoleCode: azureConfigDraft.defaultRoleCode || 'ENGINEERING',
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
  /* MODULE_002_ROLE_AWARE_APPROVAL_COUNT_EFFECT_START */
  useEffect(() => {
    let cancelled = false;

    async function loadApprovalPendingCount() {
      if (!authSession?.sessionToken) {
        const emptyCounts = {
          submittedTimePending: 0,
          localResetPendingApproval: 0,
          localResetReadyForTempPassword: 0,
          actionableTotal: 0
        };

        setProjectPulseApprovalActionableCounts(emptyCounts);
        setApprovalPendingCount(0);
        return;
      }

      try {
        const counts = await fetchJson('/api/manager/approval-count');

        if (!cancelled) {
          setProjectPulseApprovalActionableCounts(counts);
          setApprovalPendingCount(
            Number(counts.actionableTotal ?? 0)
          );
          window.setTimeout(
            normalizeProjectPulseApprovalUi,
            100
          );
          window.setTimeout(
            normalizeProjectPulseApprovalUi,
            600
          );
        }
      } catch {
        if (!cancelled) {
          const emptyCounts = {
            submittedTimePending: 0,
            localResetPendingApproval: 0,
            localResetReadyForTempPassword: 0,
            actionableTotal: 0
          };

          setProjectPulseApprovalActionableCounts(emptyCounts);
          setApprovalPendingCount(0);
          window.setTimeout(
            normalizeProjectPulseApprovalUi,
            100
          );
        }
      }
    }

    void loadApprovalPendingCount();

    const intervalId = window.setInterval(
      loadApprovalPendingCount,
      30000
    );

    window.addEventListener(
      'projectpulse:approval-queue-changed',
      loadApprovalPendingCount
    );

    return () => {
      cancelled = true;
      window.clearInterval(intervalId);
      window.removeEventListener(
        'projectpulse:approval-queue-changed',
        loadApprovalPendingCount
      );
    };
  }, [authSession?.sessionToken, activeRoute]);
  /* MODULE_002_ROLE_AWARE_APPROVAL_COUNT_EFFECT_END */





  const visibleRoleModules = useMemo(() => getVisibleRoleModules(currentUser.data), [currentUser.data]);
  const canViewProjectFlowHive = visibleRoleModules.some((module) => module.route === 'project-flowhive');

  useEffect(() => {
    if (authSession?.sessionToken) {
      window.dispatchEvent(new CustomEvent('projectpulse:auth-session-ready'));
    }
  }, [authSession?.sessionToken]);

  const roleNavigation = useMemo(() => getRoleNavigation(currentUser.data), [currentUser.data]);
  const navigationModel = useMemo(() => buildRoleNavigationModel(currentUser.data, roleNavigation), [currentUser.data, roleNavigation]);

  useEffect(() => {
    setIsTopMoreNavigationOpen(false);
  }, [activeRoute]);

  const activeNavigationItem = useMemo(
    () => (
      roleNavigation.find((item) => item.route === activeRoute) ??
      roleWorkspaceModules.find((item) => item.route === activeRoute) ??
      { label: 'Dashboard', title: 'Dashboard', route: 'dashboard', href: '#dashboard' }
    ),
    [roleNavigation, activeRoute]
  );

  // 034_DASHBOARD_MODULE_NUMBERS_PAGE_NAMES_START
  // Keep module numbers visible on dashboard cards, but show actual page names
  // in the opened Workspace header and navigation menus.
  const activeWorkspaceTitle = useMemo(() => getNavigationDisplayLabel(activeNavigationItem), [activeNavigationItem]);
  // 034_DASHBOARD_MODULE_NUMBERS_PAGE_NAMES_END

  useEffect(() => {
    if (activeRoute === 'utilization' && userIsProjectManagementRole(currentUser.data) && !userIsAdministrator(currentUser.data)) {
      window.location.hash = 'project-workload';
    }
  }, [activeRoute, currentUser.data]);

  const workspaceRoleName = getRoleDisplayName(currentUser.data);

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

      const result = await postProjectPulse051DTimeEntryJson('/api/timesheets/ai-description-suggestions', {
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
      const result = await postProjectPulse051DTimeEntryJson('/api/timesheets/week/draft', payload);
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

      const result = await postProjectPulse051DTimeEntryJson('/api/timesheets/week/draft', payload);
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
      const result = await postProjectPulse051DTimeEntryJson('/api/timesheets/day/submit', {
        weekStart: selectedWeekStart,
        workDate: selectedCell.date,
        entries: buildTimesheetPayload().entries.filter((entry) => entry.workDate === selectedCell.date) /* 051B_DAY_SUBMIT_ENTRIES_FIX */
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
      const result = await postProjectPulse051DTimeEntryJson('/api/timesheets/day/unlock', {
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

      const result = await postProjectPulse051DTimeEntryJson('/api/timesheets/week/draft', payload);
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
        reason: 'Updated from PHD role administration screen'
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
  /* 052C_STOP_RESTRICTED_EAGER_LOADS_START */
  const canViewAdminProductionReadiness =
    canSeeAny(['VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']);

  const canViewExecutiveOrAccountingSummaries =
    canSeeAny(['VIEW_EXPENSES', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']);
/* 052C_STOP_RESTRICTED_EAGER_LOADS_END */

  const canManageHolidays = hasPermission('MANAGE_HOLIDAYS') || hasPermission('MANAGE_ALL');
  const canViewHolidayCalendar = hasPermission('VIEW_HOLIDAYS') || canManageHolidays;
  const canViewPsaModules = canSeeAny(['VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']);
  const currentRoleCodes = securityContext.data?.roles?.map((role) => String(role.roleCode ?? '').toUpperCase()) ?? [];
  const currentRoleNames = securityContext.data?.roles?.map((role) => String(role.roleName ?? '').toLowerCase()) ?? [];
  const canManageRateCards =
    canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'MANAGE_PROJECT_INTAKE']) ||
    currentRoleCodes.includes('PROJECT_TEAM_COORDINATOR') ||
    currentRoleCodes.includes('SOLUTION_ARCHITECT') ||
    currentRoleCodes.includes('SA') ||
    currentRoleCodes.includes('ARCHITECT');

  /* 055C_1_WORK_REGISTER_ACCESS_SCOPE_START */
  const canViewWorkRegister =
    canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'MANAGE_PROJECT_INTAKE', 'VIEW_CUSTOMERS', 'MANAGE_CUSTOMERS', 'VIEW_REPORTS', 'MANAGE_REPORTS', 'MANAGE_TIME', 'APPROVE_TIME']) ||
    currentRoleCodes.some((roleCode) => [
      'SUPER_ADMINISTRATOR',
      'ADMINISTRATOR',
      'PROJECT_TEAM_COORDINATOR',
      'PROJECT_MANAGER',
      'PROJECT_MANAGEMENT',
      'ENGINEER',
      'ENGINEERING',
      'SALES',
      'ACCOUNT_EXECUTIVE',
      'SOLUTION_ARCHITECT',
      'SA',
      'SAA',
      'INSIDE_SALES'
    ].includes(roleCode));
  /* 055C_1_WORK_REGISTER_ACCESS_SCOPE_END */
  const canViewManagerApprovalPanel = hasPermission('APPROVE_TIME') || hasPermission('REJECT_TIME') || hasPermission('MANAGE_ALL') || hasPermission('SYSTEM_ADMINISTRATION');
  const canViewPmApprovalPanel =
    hasPermission('PROJECT_TIME_APPROVAL') ||
    hasPermission('MANAGE_ALL') ||
    hasPermission('SYSTEM_ADMINISTRATION') ||
    currentRoleCodes.includes('PROJECT_MANAGEMENT') ||
    currentRoleCodes.includes('PROJECT_MANAGER') ||
    currentRoleCodes.includes('PROJECT_TEAM_COORDINATOR') ||
    currentRoleCodes.includes('SUPER_ADMINISTRATOR') ||
    currentRoleCodes.includes('ADMINISTRATOR');
  const canViewPtcTimeEntryCorrections =
    hasPermission('MANAGE_ALL') ||
    hasPermission('SYSTEM_ADMINISTRATION') ||
    currentRoleCodes.includes('PROJECT_TEAM_COORDINATOR') ||
    currentRoleCodes.includes('SUPER_ADMINISTRATOR') ||
    currentRoleCodes.includes('ADMINISTRATOR');
  const canViewLocalAdminPasswordResetApprovals =
    hasPermission('MANAGE_ALL') ||
    hasPermission('SYSTEM_ADMINISTRATION') ||
    (currentRoleCodes.includes('SUPER_ADMINISTRATOR') || currentRoleCodes.includes('ADMINISTRATOR')) ||
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
            <p className="eyebrow">PHD Access</p>
            <h1>Sign in to your role-based workspace</h1>
            <p>
              Use your approved Microsoft Entra email for SSO. Use the local administrator account only for break-glass access when SSO is unavailable.
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
                  Continue with Microsoft Entra SSO
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
              You signed in with a temporary password. Before accessing Project Health Dashboard, choose a new password for the local administrator account.
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
            <h2>Your PHD session is about to expire</h2>
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
                  <ProfileIdentitySurface
                    mode="settings"
                    authSession={authSession}
                    currentUser={currentUser}
                    userPreferences={userPreferences}
                  />

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
                      <small>Use a small square image. Current limit is 2 MB. Pictures are saved to persistent profile storage after you select Save settings.</small>
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
                    <p>Select how PHD should appear for your account on this browser.</p>

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



      <header className="top-bar enterprise-top-bar">
        <SignalLogo />

        <div className="workspace-header-context enterprise-top-context">
          <div>
            <p className="eyebrow">Workspace</p>
            <h1>{activeWorkspaceTitle}</h1>
          </div>
        </div>

        <nav className="enterprise-top-navigation" aria-label="Workspace navigation">
          {navigationModel.primary.map((item) => (
            <a
              href={item.href}
              key={`enterprise-top-primary-${item.route}`}
              className={activeRoute === item.route ? 'active' : ''}
              onClick={() => setIsTopMoreNavigationOpen(false)}
            >
              {item.label}
            </a>
          ))}

          {navigationModel.groups.length > 0 ? (
            <div
              className="enterprise-more-navigation"
            >
              <button
                type="button"
                className={isTopMoreNavigationOpen ? 'enterprise-more-button active' : 'enterprise-more-button'}
                onClick={() => setIsTopMoreNavigationOpen((current) => !current)}
                aria-expanded={isTopMoreNavigationOpen}
                aria-controls="enterprise-more-navigation-menu"
              >
                ☰ More
              </button>

              {isTopMoreNavigationOpen ? (
                <div id="enterprise-more-navigation-menu" className="enterprise-more-dropdown">
                  {navigationModel.groups.map((group) => (
                    <div className="enterprise-more-group" key={group.name}>
                      <strong>{group.name}</strong>
                      <div className="enterprise-more-links">
                        {group.items.map((item) => (
                          <a
                            href={item.href}
                            key={`enterprise-more-${group.name}-${item.route}`}
                            className={activeRoute === item.route ? 'active' : ''}
                            onClick={() => setIsTopMoreNavigationOpen(false)}
                          >
                            {getNavigationDisplayLabel(item)}
                          </a>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              ) : null}
            </div>
          ) : null}
        </nav>

        <div className="enterprise-header-utilities">
          <ApprovalMailbox />
          <ProjectPulseGlobalSearch />
        <div className="profile-menu-shell" ref={profileMenuRef}>
          <button
            className="profile-avatar-button"
            type="button"
            onClick={() => setIsProfileMenuOpen((value) => !value)}
            aria-label="Open profile menu"
          >
            <ProfileIdentitySurface
              mode="avatar"
              authSession={authSession}
              currentUser={currentUser}
              userPreferences={userPreferences}
            />
          </button>

          {isProfileMenuOpen && (
            <div className="profile-dropdown-menu">
              <div className="profile-dropdown-header">
                <ProfileIdentitySurface
                  mode="menu"
                  authSession={authSession}
                  currentUser={currentUser}
                  userPreferences={userPreferences}
                />
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
        </div>
      </header>

      <aside className="enterprise-sidebar enterprise-sidebar-legacy" aria-label="Workspace navigation">
        <div className="enterprise-sidebar-header">
          <div>
            <p className="eyebrow">Project Health Dashboard</p>
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
                <span className="enterprise-nav-icon">{getNavigationDisplayLabel(item).slice(0, 1)}</span>
                <span className="enterprise-nav-label">{getNavigationDisplayLabel(item)}</span>
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
                        <span className="enterprise-nav-icon">{getNavigationDisplayLabel(item).slice(0, 1)}</span>
                        <span className="enterprise-nav-label">{getNavigationDisplayLabel(item)}</span>
                      </a>
                    ))}
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        </div>
      </aside>

      <PageContextGuide activeRoute={activeRoute} />

      {/* MODULE_060_CONTRACTS_ROOT_ROUTE_START */}
      {(activeRoute === 'contracts' && canSeeAny(['VIEW_CUSTOMERS', 'VIEW_REPORTS', 'MANAGE_REPORTS', 'MANAGE_PROJECT_INTAKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="contracts" className="panel contracts-route-panel">
          <ContractsCenter />
        </section>
      ) : null}
      {/* MODULE_060_CONTRACTS_ROOT_ROUTE_END */}

      {/* MODULE_060_NON_CONTRACT_ROUTE_CONTENT_START */}
      {activeRoute !== 'contracts' ? (
        <>

      {(activeRoute === 'production-data-readiness' && canViewAdminProductionReadiness) ? (
        <section id="production-data-readiness" className="panel production-data-readiness-route-panel">
          <ProductionDataReadinessCenter />
        </section>
      ) : null}

      {(activeRoute === 'production-readiness' && canViewAdminProductionReadiness) ? (
        <section id="production-readiness" className="panel production-readiness-route-panel">
          <ProductionReadinessCenterPanel />
        </section>
      ) : null}

{(activeRoute === 'user-admin') ? (
<section id="user-admin" className="panel user-admin-panel">
        <UserAdministrationPanel />
      </section>
) : null}

      {(activeRoute === 'azure-admin') ? (
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
                <option value="onenecklab">OneNeck Lab - primary and secondary domains</option>
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
                {(azureAdminData.roles?.length ? azureAdminData.roles : [{ roleCode: 'ENGINEERING', roleName: 'Engineering' }]).map((role) => (
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
                  <option value="onitdemo.com">OneNeck Lab secondary domain</option>
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
      ) : null}
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

      {/* MODULES_064_074_RELEASE_TRAIN_ROUTES_START */}
      {(activeRoute === 'ai-provider-configuration' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="ai-provider-configuration" className="panel ai-provider-configuration-route-panel">
          <AiProviderConfigurationCenter />
        </section>
      ) : null}

      {(activeRoute === 'entra-secret-administration' && canSeeAny(['MANAGE_ENTRA_SECRET', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="entra-secret-administration" className="panel entra-secret-administration-route-panel">
          <EntraSecretAdministrationCenter authSession={authSession} />
        </section>
      ) : null}

      {(activeRoute === 'global-mail-configuration' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="global-mail-configuration" className="panel global-mail-configuration-route-panel">
          <GlobalMailConfigurationCenter authSession={authSession} />
        </section>
      ) : null}

      {(activeRoute === 'system-architecture' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="system-architecture" className="panel system-architecture-route-panel">
          <SystemArchitectureCenter authSession={authSession} />
        </section>
      ) : null}

      {(activeRoute === 'system-diagnostics' && canSeeAny(['VIEW_SYSTEM_DIAGNOSTICS', 'MANAGE_SYSTEM_REMEDIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="system-diagnostics" className="panel system-diagnostics-route-panel">
          <SystemDiagnosticRemediationCenter authSession={authSession} />
        </section>
      ) : null}

      {(activeRoute === 'qualifications-certifications' && canSeeAny(['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_OWN_UTILIZATION', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="qualifications-certifications" className="panel qualifications-certifications-route-panel">
          <QualificationsCertificationCenter authSession={authSession} />
        </section>
      ) : null}

      {(activeRoute === 'capacity-pipeline-forecast' && canSeeAny(['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'VIEW_PROJECT_INTAKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="capacity-pipeline-forecast" className="panel capacity-pipeline-forecast-route-panel">
          <CapacityPipelineForecastCenter authSession={authSession} />
        </section>
      ) : null}

      {activeRoute === 'oncall-scheduling' ? (
        <section id="oncall-scheduling" className="panel oncall-scheduling-route-panel">
          <OnCallSchedulingCenter authSession={authSession} />
        </section>
      ) : null}

      {activeRoute === 'oneassist-routing-directory' ? (
        <section id="oneassist-routing-directory" className="panel oneassist-routing-directory-route-panel">
          <OneAssistRoutingDirectoryCenter authSession={authSession} />
        </section>
      ) : null}

      {activeRoute === 'sales-coverage-alignment' ? (
        <section id="sales-coverage-alignment" className="panel sales-coverage-alignment-route-panel">
          <SalesCoverageAlignmentCenter authSession={authSession} />
        </section>
      ) : null}

      {activeRoute === 'oem-vendor-directory' ? (
        <section id="oem-vendor-directory" className="panel oem-vendor-directory-route-panel">
          <OemVendorDirectoryCenter authSession={authSession} />
        </section>
      ) : null}

      {activeRoute === 'defect-tracker' ? (
        <section id="defect-tracker" className="panel defect-tracker-route-panel">
          <DefectTrackerCenter authSession={authSession} />
        </section>
      ) : null}
      {/* MODULES_064_074_RELEASE_TRAIN_ROUTES_END */}
      {/* MODULES_075_080_RUNTIME_ROUTES_START */}
      {activeRoute === 'integration-event-gateway' ? (
        <section id="integration-event-gateway" className="panel integration-event-gateway-route-panel">
          <IntegrationEventGatewayCenter authSession={authSession} />
        </section>
      ) : null}
      {activeRoute === 'release-deployment-control' ? (
        <section id="release-deployment-control" className="panel release-deployment-control-route-panel">
          <ReleaseDeploymentControlCenter authSession={authSession} />
        </section>
      ) : null}
      {activeRoute === 'observability-slo-health' ? (
        <section id="observability-slo-health" className="panel observability-slo-health-route-panel">
          <ObservabilitySloHealthCenter authSession={authSession} />
        </section>
      ) : null}
      {activeRoute === 'data-governance-retention' ? (
        <section id="data-governance-retention" className="panel data-governance-retention-route-panel">
          <DataGovernanceRetentionCenter authSession={authSession} />
        </section>
      ) : null}
      {activeRoute === 'customer-delivery-acceptance' ? (
        <section id="customer-delivery-acceptance" className="panel customer-delivery-acceptance-route-panel">
          <CustomerDeliveryAcceptanceCenter authSession={authSession} />
        </section>
      ) : null}
      {/* MODULES_075_080_RUNTIME_ROUTES_END */}


{(activeRoute === 'audit-history' && (hasPermission('VIEW_AUDIT_TRAIL') || hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL'))) ? (
        <AuditHistoryPanel />
      ) : null}

      {(activeRoute === 'security-operations' && canSeeAny(['VIEW_SECURITY_OPERATIONS', 'MANAGE_SECURITY_RESPONSE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="security-operations" className="panel security-operations-route-panel">
          <SecurityOperationsResponseCenter authSession={authSession} />
        </section>
      ) : null}

      {(activeRoute === 'project-closeout' && canSeeAny(['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="project-closeout" className="panel project-closeout-route-panel">
          <ProjectCloseoutCenter />
        </section>
      ) : null}

      {(activeRoute === 'closeout-email' && canSeeAny(['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="closeout-email" className="panel closeout-email-route-panel">
          <CloseoutEmailAutomationCenter />
        </section>
      ) : null}

      {(activeRoute === 'invoice-billing-center' && canSeeAny(['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="invoice-billing-center" className="panel invoice-billing-center-route-panel">
          <InvoiceBillingCenter
            usSignalLogoUrl={usSignalLogoUrl}
            userKey={authSession?.username ?? currentUser.data?.email ?? 'current-user'}
          />
        </section>
      ) : null}


      {(activeRoute === 'calendar-capacity' && canSeeAny(['VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="calendar-capacity" className="panel calendar-capacity-route-panel">
          <CalendarCapacityCenter />
        </section>
      ) : null}

      {(activeRoute === 'cicd-pipeline' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="cicd-pipeline" className="panel cicd-pipeline-route-panel">
          <CiCdPipelineCenter />
        </section>
      ) : null}

      {(activeRoute === 'crm-integration' && canSeeAny(['VIEW_INTEGRATIONS_026', 'MANAGE_INTEGRATIONS_026', 'VIEW_CUSTOMERS', 'VIEW_PROJECT_INTAKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="crm-integration" className="panel crm-erp-integration-route-panel">
          <CrmErpIntegrationCenter />
        </section>
      ) : null}

      {/* MODULE_999_STRUCTURAL_ROUTE_BOUNDARY */}
      {(activeRoute === 'user-guide') ? (
        <section id="user-guide" className="panel system-user-guide-route-panel">
          <SystemUserGuide modules={getInstalledProjectPulseModuleRegistry()} />
        </section>
      ) : null}

      {/* MODULE_063_STRUCTURAL_ROUTE_BOUNDARY_V2 */}
      {(activeRoute === 'opportunities') ? (
        <section id="opportunities" className="panel opportunities-route-panel">
          <OpportunitiesCenter />
        </section>
      ) : null}

      {/* MODULE_066A1_PROJECT_FLOWHIVE_ROUTE_START */}
      {(activeRoute === 'project-flowhive' && canViewProjectFlowHive) ? (
        <section id="project-flowhive" className="panel project-flowhive-route-panel">
          <ProjectFlowHiveCenter />
        </section>
      ) : null}
      {/* MODULE_066A1_PROJECT_FLOWHIVE_ROUTE_END */}

      {/* MODULE_070_STRUCTURAL_ROUTE_BOUNDARY */}
      {/* MODULE_057_STRUCTURAL_ROUTE_BOUNDARY_V6 */}
      {![
        'ai-provider-configuration',
        'entra-secret-administration',
        'global-mail-configuration',
        'system-architecture',
        'system-diagnostics',
        'qualifications-certifications',
        'capacity-pipeline-forecast',
        'oncall-scheduling',
        'oneassist-routing-directory',
        'project-flowhive',
        'sales-coverage-alignment',
        'oem-vendor-directory',
        'defect-tracker',
        'integration-event-gateway',
        'release-deployment-control',
        'observability-slo-health',
        'data-governance-retention',
        'customer-delivery-acceptance',
        'security-operations',
        'calendar-capacity',
        'cicd-pipeline',
        'contracts',
        'opportunities',
        'sales-intake',
        'sow-generator',
        'crm-integration',
        'signed-handoff',
        'ai-time-entry',
        'uat-validation',
        'reporting',
        'user-guide',
      ].includes(activeRoute) ? (
        <>

      {(activeRoute === 'dashboard') ? (
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

      </section>
      ) : null}

      <section id="dashboard" className="hero hero-polished">
        <div className="hero-content-block">
          <p className="eyebrow">Project Health Dashboard</p>
          <h1>Operational command center for time, approvals, utilization, and billing readiness.</h1>
          <p className="hero-copy">
            PHD brings weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting into one internal workflow.
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
          <small>{apiHealth.data?.service ?? apiHealth.error ?? 'PHD API'}</small>
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
            <p className="eyebrow">MODULE 001</p>
            <h2>Timesheet</h2>
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

        <div className="timesheet-view-switcher" role="tablist" aria-label="Timesheet views">
          {[
            { key: 'weekly', label: 'Weekly Grid', description: 'Full seven-day grid' },
            { key: 'daily', label: 'Daily Focus', description: 'Mobile-friendly day entry' },
            { key: 'queue', label: 'My Work Queue', description: 'Assigned tasks and requests' },
            { key: 'quick', label: 'Quick Entry List', description: 'Compact activity entry' },
            { key: 'calendar', label: 'Calendar / Timeline', description: 'Week-at-a-glance totals' }
          ].map((view) => (
            <button
              type="button"
              role="tab"
              aria-selected={timesheetView === view.key}
              className={timesheetView === view.key ? 'timesheet-view-button active' : 'timesheet-view-button'}
              key={view.key}
              onClick={() => {
                setTimesheetView(view.key);
                window.localStorage.setItem('projectPulseTimesheetView', view.key);
              }}
            >
              <strong>{view.label}</strong>
              <small>{view.description}</small>
            </button>
          ))}
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
                <span>{activitySource === 'nonProject' ? filteredCategories.length : activitySource === 'openTasks' ? filteredRegularAssignedTasks.length : filteredRequestAssignedTasks.length}</span>
              </div>

              <div className="activity-selector-row">


                <label htmlFor="activity-source">Activity type</label>
                <select
                  id="activity-source"
                  value={activitySource}
                  onChange={(event) => {
                    setActivitySource(event.target.value);
                    setActivitySearch('');
                  }}
                >
                  {activitySourceOptions.map((option) => (
                    <option value={option.key} key={option.key}>{option.label}</option>
                  ))}
                </select>

                <label htmlFor="activity-search">Search this activity type</label>
                <div className="activity-search-field">
                  <span aria-hidden="true">⌕</span>
                  <input
                    id="activity-search"
                    type="search"
                    value={activitySearch}
                    placeholder={
                      activitySource === 'nonProject'
                        ? 'Search non-project categories'
                        : activitySource === 'openTasks'
                          ? 'Search task, project, customer, or PM'
                          : 'Search request, project, customer, or PM'
                    }
                    onChange={(event) => setActivitySearch(event.target.value)}
                  />
                  {activitySearch ? (
                    <button type="button" onClick={() => setActivitySearch('')} aria-label="Clear activity search">
                      Clear
                    </button>
                  ) : null}
                </div>
              </div>

              {activitySource === 'nonProject' ? (
                <div className="activity-group activity-results">
                  <h4>Non-project time</h4>
                  {filteredCategories.map((category) => {
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
                  <h4>Regular tasks</h4>
                  {openTasks.loading ? <span className="muted">Loading assigned tasks...</span> : null}
                  {openTasks.error ? <span className="error-text">{openTasks.error}</span> : null}
                  {!openTasks.loading && !openTasks.error && filteredRegularAssignedTasks.length === 0 ? (
                    <div className="empty-activity-state">
                      <strong>{selectedActivitySource.emptyTitle}</strong>
                      <span>{selectedActivitySource.emptyDescription}</span>
                    </div>
                  ) : null}
                  {filteredRegularAssignedTasks.map((task) => {
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
                <div className="activity-group activity-results">
                  <h4>Requests / Service Requests</h4>
                  {openTasks.loading ? <span className="muted">Loading assigned requests...</span> : null}
                  {openTasks.error ? <span className="error-text">{openTasks.error}</span> : null}
                  {!openTasks.loading && !openTasks.error && filteredRequestAssignedTasks.length === 0 ? (
                    <div className="empty-activity-state">
                      <strong>{selectedActivitySource.emptyTitle}</strong>
                      <span>{selectedActivitySource.emptyDescription}</span>
                    </div>
                  ) : null}
                  {filteredRequestAssignedTasks.map((task) => {
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
                        <small>{task.workType || 'Request'}{task.projectManagerName ? ` • PM: ${task.projectManagerName}` : ''}</small>
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
              )}
            </aside>

            {timesheetView === 'weekly' ? (
              <div className="timesheet-view-panel weekly-grid-view" role="tabpanel" aria-label="Weekly Grid">
                <p className="timesheet-mobile-hint">The complete seven-day grid is preserved. On smaller screens, swipe horizontally or switch to Daily Focus.</p>
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
            ) : null}

            {timesheetView === 'daily' ? (
              <div className="timesheet-view-panel daily-focus-view" role="tabpanel" aria-label="Daily Focus">
                <div className="timesheet-day-picker" aria-label="Choose a day">
                  {days.map((day) => (
                    <button
                      type="button"
                      className={day.date === focusedDay?.date ? 'active' : ''}
                      key={day.date}
                      onClick={() => setFocusedDayDate(day.date)}
                    >
                      <span>{day.dayName.slice(0, 3)}</span>
                      <strong>{day.date.slice(5)}</strong>
                      <small>{formatNumber(getDayTotal(day.date))} hrs</small>
                    </button>
                  ))}
                </div>

                {focusedDay ? (
                  <>
                    <div className="daily-focus-summary">
                      <div>
                        <p className="eyebrow">Daily Focus</p>
                        <h3>{focusedDay.dayName} • {focusedDay.date}</h3>
                      </div>
                      <span className="pill">{formatNumber(getDayTotal(focusedDay.date))} hours</span>
                    </div>

                    <div className="daily-entry-list">
                      {activeRows.map((row) => {
                        const normalEntry = getEntry(row.id, focusedDay.date, 'normal');
                        const afterhoursEntry = getEntry(row.id, focusedDay.date, 'afterhours');
                        return (
                          <article className="daily-entry-card" key={`${row.id}-${focusedDay.date}`}>
                            <div className="daily-entry-card-copy">
                              <span className="state-dot">•</span>
                              <div>
                                <strong>{row.activity}</strong>
                                <small>{row.projectDescription}</small>
                              </div>
                            </div>
                            <div className="daily-entry-actions">
                              <button type="button" onClick={() => openEntryDetails(row.id, focusedDay.date, 'normal')}>
                                <span>Normal</span>
                                <strong>{formatHoursValue(normalEntry.hours)}</strong>
                              </button>
                              <button type="button" onClick={() => openEntryDetails(row.id, focusedDay.date, 'afterhours')}>
                                <span>Afterhours</span>
                                <strong>{formatHoursValue(afterhoursEntry.hours)}</strong>
                              </button>
                            </div>
                          </article>
                        );
                      })}
                    </div>
                  </>
                ) : (
                  <div className="empty-activity-state">
                    <strong>No day is available</strong>
                    <span>Select another week to enter time.</span>
                  </div>
                )}
              </div>
            ) : null}

            {timesheetView === 'queue' ? (
              <div className="timesheet-view-panel work-queue-view" role="tabpanel" aria-label="My Work Queue">
                <div className="timesheet-view-heading">
                  <div>
                    <p className="eyebrow">Assigned work</p>
                    <h3>My Work Queue</h3>
                  </div>
                  <span className="pill">{filteredRegularAssignedTasks.length + filteredRequestAssignedTasks.length} items</span>
                </div>

                <div className="work-queue-list">
                  {[...filteredRegularAssignedTasks, ...filteredRequestAssignedTasks].map((task) => {
                    const alreadyAdded = activeRows.some((row) => row.projectId === task.projectId && row.taskId === task.taskId);
                    return (
                      <article className="work-queue-card" key={`${task.projectId}-${task.taskId}`}>
                        <div>
                          <span className="queue-type">{projectPulseTaskTimeEntrySection(task) === 'requests' ? 'Request / Service Request' : 'Regular Task'}</span>
                          <h4>{task.taskName}</h4>
                          <p>{task.projectCode} • {task.projectName}</p>
                          <small>{task.clientName ? `Customer: ${task.clientName}` : 'Assigned work'}{task.projectManagerName ? ` • PM: ${task.projectManagerName}` : ''}</small>
                        </div>
                        <div className="queue-metrics">
                          <span><small>Assigned</small><strong>{Number(task.assignedHours || 0).toFixed(2)}</strong></span>
                          <span><small>Used</small><strong>{Number(task.usedHours || 0).toFixed(2)}</strong></span>
                          <span><small>Remaining</small><strong>{Number(task.remainingHours || 0).toFixed(2)}</strong></span>
                        </div>
                        <button type="button" className="primary-action" disabled={alreadyAdded || !isAnyDayEditable} onClick={() => addTask(task)}>
                          {alreadyAdded ? 'Added to Timesheet' : 'Add to Timesheet'}
                        </button>
                      </article>
                    );
                  })}

                  {!openTasks.loading && !openTasks.error && filteredRegularAssignedTasks.length + filteredRequestAssignedTasks.length === 0 ? (
                    <div className="empty-activity-state">
                      <strong>No assigned work matches the search</strong>
                      <span>Clear the activity search or select another week.</span>
                    </div>
                  ) : null}
                </div>
              </div>
            ) : null}

            {timesheetView === 'quick' ? (
              <div className="timesheet-view-panel quick-entry-view" role="tabpanel" aria-label="Quick Entry List">
                <div className="timesheet-view-heading">
                  <div>
                    <p className="eyebrow">Compact entry</p>
                    <h3>Quick Entry List</h3>
                  </div>
                  <span className="pill">{activeRows.length} activities</span>
                </div>

                <div className="quick-entry-list">
                  {activeRows.map((row) => (
                    <article className="quick-entry-card" key={row.id}>
                      <header>
                        <div>
                          <strong>{row.activity}</strong>
                          <small>{row.projectDescription}</small>
                        </div>
                        <span>{formatNumber(getRowTotal(row.id))} hrs</span>
                      </header>
                      <div className="quick-entry-days">
                        {days.map((day) => (
                          <div className="quick-entry-day" key={`${row.id}-${day.date}`}>
                            <span>{day.dayName.slice(0, 3)} {day.date.slice(5)}</span>
                            <div>
                              {timeTypes.map((type) => {
                                const entry = getEntry(row.id, day.date, type.key);
                                return (
                                  <button type="button" key={type.key} onClick={() => openEntryDetails(row.id, day.date, type.key)}>
                                    <small>{type.key === 'afterhours' ? 'AH' : 'N'}</small>
                                    <strong>{formatHoursValue(entry.hours)}</strong>
                                  </button>
                                );
                              })}
                            </div>
                          </div>
                        ))}
                      </div>
                    </article>
                  ))}
                </div>
              </div>
            ) : null}

            {timesheetView === 'calendar' ? (
              <div className="timesheet-view-panel calendar-timeline-view" role="tabpanel" aria-label="Calendar and Timeline">
                <div className="timesheet-view-heading">
                  <div>
                    <p className="eyebrow">Week at a glance</p>
                    <h3>Calendar / Timeline</h3>
                  </div>
                  <span className="pill">{formatNumber(grandTotal)} total hours</span>
                </div>

                <div className="calendar-week-grid">
                  {days.map((day) => {
                    const dayItems = activeRows
                      .flatMap((row) => timeTypes.map((type) => ({
                        row,
                        type,
                        entry: getEntry(row.id, day.date, type.key)
                      })))
                      .filter((item) => Number.parseFloat(item.entry.hours || '0') > 0);

                    return (
                      <article className="calendar-day-card" key={day.date}>
                        <header>
                          <div>
                            <strong>{day.dayName}</strong>
                            <span>{day.date}</span>
                          </div>
                          <span className="calendar-day-total">{formatNumber(getDayTotal(day.date))} hrs</span>
                        </header>
                        <div className="calendar-day-items">
                          {dayItems.map((item) => (
                            <button
                              type="button"
                              key={`${item.row.id}-${day.date}-${item.type.key}`}
                              onClick={() => openEntryDetails(item.row.id, day.date, item.type.key)}
                            >
                              <span>{item.row.activity}</span>
                              <small>{item.type.key === 'afterhours' ? 'Afterhours' : 'Normal'}</small>
                              <strong>{formatHoursValue(item.entry.hours)}</strong>
                            </button>
                          ))}
                          {dayItems.length === 0 ? <span className="muted">No time entered</span> : null}
                        </div>
                      </article>
                    );
                  })}
                </div>
              </div>
            ) : null}
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
                        Provider: {{
                          claude: 'Claude',
                          openai: 'OpenAI',
                          local_template: 'Governed local template fallback'
                        }[aiSuggestionState.provider] || 'Shared AI router'}
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




      {(activeRoute === 'holiday-admin') ? (
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
      ) : null}
<section id="project-allocation-info" className="panel project-allocation-info-panel">
        <ProjectAllocationInfoPanel />
      </section>

      {(activeRoute === 'project-workload' && canSeeAny(['VIEW_PROJECT_WORKLOAD', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="project-workload" className="panel project-workload-route-panel">
          <ProjectManagerWorkloadCenter />
        </section>
      ) : null}

      {(activeRoute === 'project-workspace' && canSeeAny(['VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="project-workspace" className="panel project-workspace-route-panel">
          <ProjectWorkspaceCenter />
        </section>
      ) : null}

      {(activeRoute === 'work-task-builder' && canSeeAny(['VIEW_WORK_TASK_BUILDER', 'MANAGE_WORK_TASK_BUILDER', 'ASSIGN_WORK_TASKS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <WorkTaskBuilderPanel />
      ) : null}

      {(activeRoute === 'cost-alerts' && canSeeAny(['VIEW_COST_ALERTS', 'MANAGE_COST_ALERTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="cost-alerts" className="panel cost-alert-route-panel">
          <CostOverrunAlertCenter canManageCostAlerts={canSeeAny(['MANAGE_COST_ALERTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])} />
        </section>
      ) : null}

      {/* 055C_WORK_REGISTER_ROUTE_START */}
      {activeRoute === 'work-register' ? (
        canViewWorkRegister ? (
          <section id="work-register" className="panel work-register-route-panel">
            <WorkRegisterCenter />
          </section>
        ) : (
          <section id="work-register" className="panel work-register-route-panel">
            <div className="work-register-center">
              <div className="work-register-banner error">
                Work Register access is restricted to authorized ProjectPulse users.
              </div>
            </div>
          </section>
        )
      ) : null}
      {/* 055C_WORK_REGISTER_ROUTE_END */}

      {/* 055B_RATE_CARD_ADMIN_ROUTE_START */}
      {activeRoute === 'rate-card-administration' ? (
        canManageRateCards ? (
          <section id="rate-card-administration" className="panel rate-card-administration-route-panel">
            <RateCardAdministrationCenter />
          </section>
        ) : (
          <section id="rate-card-administration" className="panel rate-card-administration-route-panel">
            <div className="rate-card-admin-center">
              <div className="rate-card-admin-banner error">
                Rate Card Administration is restricted to Super Administrators, Administrators, Project Team Coordinators, and Solution Architects.
              </div>
            </div>
          </section>
        )
      ) : null}
      {/* 055B_RATE_CARD_ADMIN_ROUTE_END */}

      {(activeRoute === 'customer-directory' && canSeeAny(['VIEW_CUSTOMERS', 'MANAGE_CUSTOMERS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="customer-directory" className="panel customer-directory-route-panel">
          <CustomerDirectoryCenter canManageCustomers={canSeeAny(['MANAGE_CUSTOMERS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])} />
        </section>
      ) : null}

      {(activeRoute === 'sales-insights' && canSeeAny(['VIEW_PROJECT_INTAKE', 'VIEW_CUSTOMERS', 'VIEW_PROJECT_WORKLOAD', 'VIEW_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="sales-insights" className="panel sales-insights-route-panel">
          <SalesInsightsDashboard />
        </section>
      ) : null}

      {(activeRoute === 'project-intake' && canSeeAny(['VIEW_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="project-intake" className="panel project-intake-route-panel">
          <ProjectIntakeCenter />
          <PostIntakeAgingPanel />
        <IntakeWorkTaskHandoffPanel />
        <ResourceAssignmentHandoffPanel />
        </section>
      ) : null}

      {(activeRoute === 'certify-integration' && canSeeAny(['VIEW_EXPENSES', 'VIEW_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="certify-integration" className="panel certify-integration-route-panel">
          <CertifyIntegrationCenter />
        </section>
      ) : null}

      {(activeRoute === 'billing-readiness' && canSeeAny(['VIEW_ACCOUNT_RECONCILIATION', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="billing-readiness" className="panel billing-readiness-route-panel">
          <BillingReadinessCenter />
        </section>
      ) : null}

      {activeRoute === 'dashboard' ? (
        <section className="panel installed-modules-dashboard-panel">
          {(() => {
            const installedModules = getInstalledProjectPulseModuleRegistry()
              .filter((module) => {
                if (!module.permissions?.length) return true;
                if (typeof canSeeAny !== 'function') return true;
                return canSeeAny(module.permissions);
              });

            return (
              <>
                <div className="installed-modules-header">
                  <div>
                    <p className="eyebrow">Installed Modules</p>
                    <h2>Role-based module dashboard</h2>
                    <p className="muted">
                      These are the PHD modules available to your current role. Each card explains what the module is intended to do so new workflow areas are visible from the dashboard.
                    </p>
                  </div>
                  <span className="installed-modules-count">{installedModules.length} available</span>
                </div>

                <div className="installed-module-grid">
                  {installedModules.map((module) => (
                    <a className="installed-module-card" href={`#${module.route}`} key={module.route}>
                      <span>{module.navLabel || 'Platform'} • {module.group}</span>
                      <strong>{module.title}</strong>
                      <p>{module.description}</p>
                      <small>Open module →</small>
                    </a>
                  ))}
                </div>
              </>
            );
          })()}
        </section>
      ) : null}

      {(activeRoute === 'time-compliance' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'VIEW_TIME_COMPLIANCE', 'VIEW_AUDIT_HISTORY'])) ? (
        <section id="time-compliance" className="panel time-compliance-route-panel">
          <TimeComplianceCenter />
        </section>
      ) : null}

      {(activeRoute === 'dashboard') ? (
<section id="psa-modules" className={`panel module-foundation-panel ${canViewPsaModules ? '' : 'access-hidden'}`}>
        <div className="section-header compact">
          <div>
            <p className="eyebrow">PSA platform modules</p>
            <h2>Remaining sections foundation</h2>
            <p className="muted">These sections prepare the rest of Project Health Dashboard beyond time entry: intake, project management, resource scheduling, expenses, invoicing, reporting, and administrative workflow.</p>
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
      ) : null}


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
        <EngineeringTeamLeadUtilizationPanel />
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


      {(activeRoute === 'roles-permissions-matrix' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="roles-permissions-matrix" className="panel roles-permissions-matrix-route-panel">
          <RolesPermissionsMatrix />
        </section>
      ) : null}

      {(hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL')) ? (
        <section id="role-admin" className="panel role-admin-panel">
          {/* 042F_ROLE_ADMIN_ROUTE_SIMPLIFIED */}
          <RoleAdminDirectoryPanel />
        </section>
      ) : null}

      {(activeRoute === 'manager-approval') ? (
        <ApprovalCenter />
      ) : null}

      {(activeRoute === 'workflow' && canSeeAny(['VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'MANAGE_ACCOUNT_RECONCILIATION', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'VIEW_AUDIT_TRAIL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (
        <section id="workflow" className="panel workflow-route-panel approval-export-workflow-route-panel">
          <ApprovalExportAuditWorkflowCenter />
        </section>
      ) : null}


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
      <LocalAdminPasswordResetClearActions />
      <ProductionOperationsPanel />
      <ProductionOperationsAcknowledgmentsPanel />
      <TimeComplianceEmailNotificationsPanel />

</>
      ) : null}

        </>
      ) : null}
      {/* MODULE_060_NON_CONTRACT_ROUTE_CONTENT_END */}

      {MODULE_064_074_NATIVE_ADMINISTRATION_ROUTES[activeRoute] ? (
        <NativeModuleAdministrationPanel
          moduleNumber={MODULE_064_074_NATIVE_ADMINISTRATION_ROUTES[activeRoute]}
        />
      ) : null}

      {/* MODULE_059_GLOBAL_ROUTE_HOST */}
      <div
        className="module059-global-route-host"
        data-module="059"
        data-route-scope="all-authenticated-pages"
      >
        <SessionIntelligenceDrawer authSession={authSession} />
      </div>

      <HelpAssistant />
</main>
  );
}


/* 030_ROLE_CLEANUP_PHASE2_COMPATIBILITY
   Frontend recognizes canonical roles while legacy role assignments remain temporarily active.
*/

/* 050B_FINAL_BROWSER_API_SESSION_HEADER_BRIDGE_START */
function getProjectPulse050BFinalBrowserSessionToken() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');

    return session?.sessionToken
      || session?.token
      || session?.accessToken
      || '';
  } catch {
    return '';
  }
}

function isProjectPulse050BProtectedApiUrl(rawUrl) {
  try {
    if (!rawUrl) return false;

    const url = new URL(rawUrl, window.location.origin);

    return url.origin === window.location.origin && url.pathname.startsWith('/api/');
  } catch {
    return false;
  }
}

function applyProjectPulse050BSessionHeaders(headers) {
  const token = getProjectPulse050BFinalBrowserSessionToken();

  if (!token) {
    return headers;
  }

  if (headers instanceof Headers) {
    if (!headers.has('X-ProjectPulse-Session')) headers.set('X-ProjectPulse-Session', token);
    if (!headers.has('X-Project-Pulse-Session')) headers.set('X-Project-Pulse-Session', token);
    if (!headers.has('X-Session-Token')) headers.set('X-Session-Token', token);
    if (!headers.has('Authorization')) headers.set('Authorization', `Bearer ${token}`);
    return headers;
  }

  const nextHeaders = { ...(headers || {}) };

  if (!nextHeaders['X-ProjectPulse-Session']) nextHeaders['X-ProjectPulse-Session'] = token;
  if (!nextHeaders['X-Project-Pulse-Session']) nextHeaders['X-Project-Pulse-Session'] = token;
  if (!nextHeaders['X-Session-Token']) nextHeaders['X-Session-Token'] = token;
  if (!nextHeaders.Authorization) nextHeaders.Authorization = `Bearer ${token}`;

  return nextHeaders;
}

function installProjectPulse050BFinalFetchBridge() {
  if (typeof window === 'undefined' || typeof window.fetch !== 'function') {
    return;
  }

  const currentFetch = window.fetch;

  if (currentFetch.__projectPulse050BFinalWrapped) {
    return;
  }

  const wrappedFetch = async (input, init = {}) => {
    const rawUrl = typeof input === 'string' ? input : input?.url;

    if (!isProjectPulse050BProtectedApiUrl(rawUrl)) {
      return currentFetch(input, init);
    }

    const headers = applyProjectPulse050BSessionHeaders(
      new Headers(init?.headers || (input instanceof Request ? input.headers : undefined))
    );

    return currentFetch(input, {
      ...init,
      headers
    });
  };

  wrappedFetch.__projectPulse050BFinalWrapped = true;
  window.fetch = wrappedFetch;
}

function installProjectPulse050BFinalXhrBridge() {
  if (typeof window === 'undefined' || typeof window.XMLHttpRequest === 'undefined') {
    return;
  }

  const xhrPrototype = window.XMLHttpRequest.prototype;

  if (xhrPrototype.__projectPulse050BFinalWrapped) {
    return;
  }

  const nativeOpen = xhrPrototype.open;
  const nativeSend = xhrPrototype.send;

  xhrPrototype.open = function projectPulse050BXhrOpen(method, url, async, user, password) {
    this.__projectPulse050BRequestUrl = url;
    return nativeOpen.call(this, method, url, async, user, password);
  };

  xhrPrototype.send = function projectPulse050BXhrSend(body) {
    if (isProjectPulse050BProtectedApiUrl(this.__projectPulse050BRequestUrl)) {
      const token = getProjectPulse050BFinalBrowserSessionToken();

      if (token) {
        try {
          this.setRequestHeader('X-ProjectPulse-Session', token);
          this.setRequestHeader('X-Project-Pulse-Session', token);
          this.setRequestHeader('X-Session-Token', token);
          this.setRequestHeader('Authorization', `Bearer ${token}`);
        } catch {
          // Preserve original request behavior if the browser refuses header mutation.
        }
      }
    }

    return nativeSend.call(this, body);
  };

  xhrPrototype.__projectPulse050BFinalWrapped = true;
}

function installProjectPulse050BFinalBrowserApiSessionBridge() {
  installProjectPulse050BFinalFetchBridge();
  installProjectPulse050BFinalXhrBridge();
}

if (typeof window !== 'undefined') {
  installProjectPulse050BFinalBrowserApiSessionBridge();

  window.setTimeout(installProjectPulse050BFinalBrowserApiSessionBridge, 0);
  window.setTimeout(installProjectPulse050BFinalBrowserApiSessionBridge, 250);
  window.setTimeout(installProjectPulse050BFinalBrowserApiSessionBridge, 1000);
  window.setTimeout(installProjectPulse050BFinalBrowserApiSessionBridge, 3000);

  window.addEventListener('storage', installProjectPulse050BFinalBrowserApiSessionBridge);
}
/* 050B_FINAL_BROWSER_API_SESSION_HEADER_BRIDGE_END */

import './runtime-browser-compatibility.js';
import React from 'react';
import { createRoot } from 'react-dom/client';
import { protectReactOwnedRoot } from './external-dom-mutation-resilience.js';
import './projectpulse-authoritative-api.js';
import './view-as-storage-compatibility.js';
import './module-availability-bridge.js';
import './api-error-presentation.js';
import './runtime-data-compatibility.js';
import './role-workspace-effective-identity-compatibility.js';
import './role-policy-authoritative-transport.js';
import './microsoft-integration-compatibility.js';
import './microsoft-sso-runtime-activation.js';
import './microsoft-mail-runtime-activation.js';
import './legacy-analytics-overlay-authority.js';
import './react-dom-ownership-prelude.js';
import './pulse-ai-help-chat-usability.js';
import './admin-experience-theme.js';
import './pulse-shell-frontend-compatibility.js';
import './enterprise-navigation-parity.js';
import './enterprise-ui-polish.js';
import App from './App.Module001.g.jsx';
import AdminRuntimeStabilityPortal from './AdminRuntimeStabilityPortal.jsx';
import GlobalViewAsDrawer from './GlobalViewAsDrawer.jsx';
import DisplayPreferencesDrawer from './DisplayPreferencesDrawer.jsx';
import AccountCenterPortal from './AccountCenterPortal.jsx';
import AuthenticatedHelpAssistant from './AuthenticatedHelpAssistant.jsx';
import ModulesDirectoryPortal from './ModulesDirectoryPortal.jsx';
import ModuleAvailabilityController from './ModuleAvailabilityController.jsx';
import DashboardPersonalCalendarPortal from './DashboardPersonalCalendarPortal.jsx';
import CriticalRoutePresentationBoundary from './CriticalRoutePresentationBoundary.jsx';
import TimesheetEnhancementPortal from './module001/TimesheetEnhancementPortal.jsx';
import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';
import Module001BTimeReallocationGate from './module001b/Module001BTimeReallocationGate.jsx';
import ProjectExpenseCrossModulePortal from './ProjectExpenseCrossModulePortal.jsx';
import Module005ExperienceCompatibility from './Module005ExperienceCompatibility.jsx';
import MicrosoftIntegrationDualConnectionPortal from './MicrosoftIntegrationDualConnectionPortal.jsx';
import MicrosoftMailTransportReadinessPanel from './MicrosoftMailTransportReadinessPanel.jsx';
import EnterpriseExperienceController from './EnterpriseExperienceController.jsx';
import ProjectForgeFlowHiveSyncPortal from './ProjectForgeFlowHiveSyncPortal.jsx';
import CustomerSourceAuthorityPortal from './CustomerSourceAuthorityPortal.jsx';
import ApplicationErrorBoundary from './ApplicationErrorBoundary.jsx';
import WorkspaceNavigationPortal from './WorkspaceNavigationPortal.jsx';
import './approval-access-navigation-compatibility.js';
import './work-register-document-integrity.js';
import './scoped-rbac-catalog-compatibility.js';
import './background-request-role-gate.js';
import './styles.css';
import './friendly-api-errors.css';
import './role-welcome-dashboard-visibility.css';
import './scoped-role-policy-admin.css';
import './scoped-role-policy-matrix.css';
import './module001/module001-uat-fixes.css';
import './admin-experience-theme.css';
import './pulse-shell-frontend-compatibility.css';
import './profile-settings-enterprise.css';
import './enterprise-theme-completion.css';
import './enterprise-experience-system.css';
import './enterprise-experience-components.css';
import './enterprise-experience-data.css';
import './enterprise-module-management.css';
import './enterprise-module-cards.css';
import './enterprise-overlay-responsive.css';
import './enterprise-systemwide-reliability.css';
import './enterprise-feedback-fixes.css';
import './display-preferences-drawer.css';
import './module-management-table.css';
import './enterprise-header-navigation-layout.css';
import './enterprise-ui-polish.css';
import './celar-ai-control-followup.css';
import './account-center.css';
import './workspace-navigation.css';
import './customer-source-authority.css';
import './enterprise-contrast-guard.css';

createRoot(
  protectReactOwnedRoot(document.getElementById('root'))
).render(
  <React.StrictMode>
    <ApplicationErrorBoundary>
      <App />
      <EnterpriseExperienceController />
      <CriticalRoutePresentationBoundary />
      <AdminRuntimeStabilityPortal />
      <GlobalViewAsDrawer />
      <DisplayPreferencesDrawer />
      <AccountCenterPortal />
      <ModulesDirectoryPortal />
      <ModuleAvailabilityController />
      <DashboardPersonalCalendarPortal />
      <TimesheetEnhancementPortal />
      <PtcTimeStewardGate />
      <Module001BTimeReallocationGate />
      <ProjectExpenseCrossModulePortal />
      <Module005ExperienceCompatibility />
      <MicrosoftIntegrationDualConnectionPortal />
      <MicrosoftMailTransportReadinessPanel />
      <ProjectForgeFlowHiveSyncPortal />
      <CustomerSourceAuthorityPortal />
      <AuthenticatedHelpAssistant />
      <WorkspaceNavigationPortal />
    </ApplicationErrorBoundary>
  </React.StrictMode>
);
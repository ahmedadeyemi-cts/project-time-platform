import React from 'react';
import { createRoot } from 'react-dom/client';
import './projectpulse-authoritative-api.js';
import './module-availability-bridge.js';
import './api-error-presentation.js';
import './runtime-data-compatibility.js';
import './role-policy-authoritative-transport.js';
import './microsoft-integration-compatibility.js';
import './microsoft-sso-runtime-activation.js';
import './microsoft-mail-runtime-activation.js';
import './react-dom-ownership-prelude.js';
import App from './App.Module001.g.jsx';
import AdminRuntimeStabilityPortal from './AdminRuntimeStabilityPortal.jsx';
import GlobalViewAsDrawer from './GlobalViewAsDrawer.jsx';
import HelpAssistant from './HelpAssistant.jsx';
import ModulesDirectoryPortal from './ModulesDirectoryPortal.jsx';
import ModuleAvailabilityController from './ModuleAvailabilityController.jsx';
import DashboardPersonalCalendarPortal from './DashboardPersonalCalendarPortal.jsx';
import CriticalRoutePresentationBoundary from './CriticalRoutePresentationBoundary.jsx';
import TimesheetEnhancementPortal from './module001/TimesheetEnhancementPortal.jsx';
import Module001ActiveTimerRecoveryPortal from './module001/Module001ActiveTimerRecoveryPortal.jsx';
import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';
import ProjectExpenseCrossModulePortal from './ProjectExpenseCrossModulePortal.jsx';
import Module005ExperienceCompatibility from './Module005ExperienceCompatibility.jsx';
import MicrosoftIntegrationDualConnectionPortal from './MicrosoftIntegrationDualConnectionPortal.jsx';
import MicrosoftMailTransportReadinessPanel from './MicrosoftMailTransportReadinessPanel.jsx';
import './approval-access-navigation-compatibility.js';
import './scoped-rbac-catalog-compatibility.js';
import './styles.css';
import './friendly-api-errors.css';
import './role-welcome-dashboard-visibility.css';
import './scoped-role-policy-admin.css';
import './scoped-role-policy-matrix.css';
import './module001/module001-uat-fixes.css';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
    <CriticalRoutePresentationBoundary />
    <AdminRuntimeStabilityPortal />
    <GlobalViewAsDrawer />
    <ModulesDirectoryPortal />
    <ModuleAvailabilityController />
    <DashboardPersonalCalendarPortal />
    <TimesheetEnhancementPortal />
    <Module001ActiveTimerRecoveryPortal />
    <PtcTimeStewardGate />
    <ProjectExpenseCrossModulePortal />
    <Module005ExperienceCompatibility />
    <MicrosoftIntegrationDualConnectionPortal />
    <MicrosoftMailTransportReadinessPanel />
    <HelpAssistant />
  </React.StrictMode>
);

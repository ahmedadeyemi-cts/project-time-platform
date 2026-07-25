import React from 'react';
import { createRoot } from 'react-dom/client';
import './module-availability-bridge.js';
import './api-error-presentation.js';
import App from './App.Module001.g.jsx';
import HelpAssistant from './HelpAssistant.jsx';
import ModulesDirectoryPortal from './ModulesDirectoryPortal.jsx';
import ModuleAvailabilityController from './ModuleAvailabilityController.jsx';
import DashboardPersonalCalendarPortal from './DashboardPersonalCalendarPortal.jsx';
import TimesheetEnhancementPortal from './module001/TimesheetEnhancementPortal.jsx';
import './approval-access-navigation-compatibility.js';
import './scoped-rbac-catalog-compatibility.js';
import './styles.css';
import './friendly-api-errors.css';
import './role-welcome-dashboard-visibility.css';
import './scoped-role-policy-admin.css';
import './scoped-role-policy-matrix.css';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
    <ModulesDirectoryPortal />
    <ModuleAvailabilityController />
    <DashboardPersonalCalendarPortal />
    <TimesheetEnhancementPortal />
    <HelpAssistant />
  </React.StrictMode>
);

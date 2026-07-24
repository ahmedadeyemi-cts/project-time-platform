import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './.module001-generated/App.Module001.g.jsx';
import HelpAssistant from './HelpAssistant.jsx';
import ModulesDirectoryPortal from './ModulesDirectoryPortal.jsx';
import DashboardPersonalCalendarPortal from './DashboardPersonalCalendarPortal.jsx';
import TimesheetEnhancementPortal from './module001/TimesheetEnhancementPortal.jsx';
import './approval-access-navigation-compatibility.js';
import './scoped-rbac-catalog-compatibility.js';
import './styles.css';
import './role-welcome-dashboard-visibility.css';
import './scoped-role-policy-admin.css';
import './scoped-role-policy-matrix.css';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
    <ModulesDirectoryPortal />
    <DashboardPersonalCalendarPortal />
    <TimesheetEnhancementPortal />
    <HelpAssistant />
  </React.StrictMode>
);

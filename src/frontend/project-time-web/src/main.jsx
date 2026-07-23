import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App.jsx';
import HelpAssistant from './HelpAssistant.jsx';
import ModulesDirectoryPortal from './ModulesDirectoryPortal.jsx';
import DashboardPersonalCalendarPortal from './DashboardPersonalCalendarPortal.jsx';
import './approval-access-navigation-compatibility.js';
import './styles.css';
import './role-welcome-dashboard-visibility.css';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
    <ModulesDirectoryPortal />
    <DashboardPersonalCalendarPortal />
    <HelpAssistant />
  </React.StrictMode>
);

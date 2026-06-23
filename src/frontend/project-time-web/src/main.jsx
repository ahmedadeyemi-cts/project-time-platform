import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App.jsx';
import ManagerApprovalPanel from './ManagerApprovalPanel.jsx';
import HelpAssistant from './HelpAssistant.jsx';
import './styles.css';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
    <ManagerApprovalPanel />
    <HelpAssistant />
  </React.StrictMode>
);

import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App.jsx';
import HelpAssistant from './HelpAssistant.jsx';
import './styles.css';

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
    <HelpAssistant />
  </React.StrictMode>
);

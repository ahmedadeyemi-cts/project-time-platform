import { useEffect, useState } from 'react';
import AuditHistoryPanel from './AuditHistoryPanel.jsx';
import './admin-runtime-stability.css';

if (typeof window !== 'undefined') {
  window.__projectPulseModule008StableOwnerInstalled = true;
}

function currentRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0] || 'dashboard';
}

export default function AdminRuntimeStabilityPortal() {
  const [route, setRoute] = useState(currentRoute);

  useEffect(() => {
    const synchronize = () => setRoute(currentRoute());
    window.addEventListener('hashchange', synchronize);
    window.addEventListener('pageshow', synchronize);
    return () => {
      window.removeEventListener('hashchange', synchronize);
      window.removeEventListener('pageshow', synchronize);
    };
  }, []);

  if (route === 'audit-history') {
    return (
      <div
        className="admin-runtime-stability-route-root"
        data-module-008-stable-route-root="true"
        aria-label="Audit and History route"
      >
        <AuditHistoryPanel stableRouteOwner />
      </div>
    );
  }

  if (route === 'azure-admin') {
    return (
      <aside className="module010-audit-consolidation" aria-label="Module 010 audit evidence">
        <div>
          <p className="eyebrow">MODULE 010 AUDIT EVIDENCE</p>
          <h2>Synchronization history is consolidated in Module 008</h2>
          <p>
            Entra preview, import, duplicate, failure, and synchronization events are retained in Audit and History
            with the actor, time, outcome counts, source, correlation ID, and sanitized evidence.
          </p>
        </div>
        <a href="#audit-history?category=integration&search=Azure%20Entra%20Module%20010">
          Open Module 010 evidence in Audit and History
        </a>
      </aside>
    );
  }

  return null;
}

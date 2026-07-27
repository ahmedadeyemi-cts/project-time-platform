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

  if (route !== 'audit-history') return null;

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

import { useLayoutEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import AuditHistoryPanel from './AuditHistoryPanel.jsx';

function readActiveRoute() {
  if (typeof window === 'undefined') return '';
  return String(window.location.hash || '')
    .replace(/^#\/?/, '')
    .split('?')[0]
    .trim();
}

export default function AuditHistoryRouteRecoveryPortal() {
  const [activeRoute, setActiveRoute] = useState(readActiveRoute);
  const [portalTarget, setPortalTarget] = useState(null);

  useLayoutEffect(() => {
    const handleHashChange = () => setActiveRoute(readActiveRoute());
    window.addEventListener('hashchange', handleHashChange);
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  useLayoutEffect(() => {
    setPortalTarget(null);
    if (activeRoute !== 'audit-history') return undefined;

    let cancelled = false;
    let retryTimer = 0;

    const mountWhenReady = () => {
      if (cancelled) return;

      const shell = document.querySelector('.app-shell.route-audit-history');
      if (!shell) {
        retryTimer = window.setTimeout(mountWhenReady, 50);
        return;
      }

      const appOwnedPanel = shell.querySelector('#audit-history');
      if (!appOwnedPanel) setPortalTarget(shell);
    };

    mountWhenReady();

    return () => {
      cancelled = true;
      if (retryTimer) window.clearTimeout(retryTimer);
    };
  }, [activeRoute]);

  if (activeRoute !== 'audit-history' || !portalTarget) return null;

  return createPortal(
    <AuditHistoryPanel recoveryMode />,
    portalTarget
  );
}

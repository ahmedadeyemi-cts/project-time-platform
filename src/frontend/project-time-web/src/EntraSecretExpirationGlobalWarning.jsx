import { useCallback, useEffect, useState } from 'react';
import './entra-secret-expiration-governance.css';

function readToken(authSession) {
  if (authSession?.sessionToken || authSession?.token || authSession?.accessToken) {
    return authSession.sessionToken || authSession.token || authSession.accessToken;
  }

  try {
    const stored = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return stored?.sessionToken || stored?.token || stored?.accessToken || '';
  } catch {
    return '';
  }
}

function headers(authSession) {
  const token = readToken(authSession);
  return token ? {
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  } : {};
}

function dateLabel(value) {
  if (!value) return 'an unknown date';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return String(value);
  return parsed.toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' });
}

export default function EntraSecretExpirationGlobalWarning({ authSession }) {
  const [status, setStatus] = useState(null);

  const load = useCallback(async () => {
    if (!readToken(authSession)) return;
    try {
      const response = await fetch('/api/entra-secret-expiration/status', {
        credentials: 'include',
        cache: 'no-store',
        headers: headers(authSession)
      });
      const payload = await response.json().catch(() => null);
      if (response.ok) setStatus(payload);
    } catch {
      // A global alert must never prevent the rest of ProjectPulse from rendering.
    }
  }, [authSession]);

  useEffect(() => {
    void load();
    const interval = window.setInterval(() => void load(), 15 * 60 * 1000);
    window.addEventListener('focus', load);
    window.addEventListener('projectpulse:entra-secret-expiration-changed', load);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', load);
      window.removeEventListener('projectpulse:entra-secret-expiration-changed', load);
    };
  }, [load]);

  if (!status?.showGlobalWarning) return null;

  const days = Number(status.daysUntilExpiration);
  const timing = Number.isFinite(days)
    ? days < 0
      ? `expired ${Math.abs(days)} day${Math.abs(days) === 1 ? '' : 's'} ago`
      : days === 0
        ? 'expires today'
        : `expires in ${days} day${days === 1 ? '' : 's'}`
    : `expires on ${dateLabel(status.expiresAt)}`;

  return (
    <aside className="entra-expiration-global-warning" role="alert" aria-live="assertive">
      <span className="entra-expiration-global-warning__signal" aria-hidden="true">!</span>
      <div>
        <strong>Microsoft Integration client secret {timing}.</strong>
        <span>
          ProjectPulse authentication and Microsoft Graph services are at risk. An authorized administrator must update the expiration date or secret version in Module 065 now.
        </span>
      </div>
      <a href="#entra-secret-administration">Open Module 065</a>
    </aside>
  );
}

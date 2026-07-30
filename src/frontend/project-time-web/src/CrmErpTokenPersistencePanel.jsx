import { useCallback, useEffect, useMemo, useState } from 'react';
import './crm-erp-token-persistence.css';

function token() {
  try {
    const stored = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return stored?.sessionToken || stored?.token || stored?.accessToken
      || window.localStorage.getItem('projectPulseSessionToken')
      || window.sessionStorage.getItem('projectPulseSessionToken')
      || '';
  } catch {
    return window.localStorage.getItem('projectPulseSessionToken')
      || window.sessionStorage.getItem('projectPulseSessionToken')
      || '';
  }
}

function headers(json = false) {
  const value = token();
  return {
    ...(json ? { 'Content-Type': 'application/json' } : {}),
    ...(value ? {
      Authorization: `Bearer ${value}`,
      'X-ProjectPulse-Session': value,
      'X-Project-Pulse-Session': value,
      'X-Session-Token': value
    } : {})
  };
}

async function request(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: { ...headers(Boolean(options.body)), ...(options.headers || {}) }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || `Module 026 returned HTTP ${response.status}.`);
  return payload;
}

function dateTime(value, fallback = 'Not recorded') {
  if (!value) return fallback;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function statusText(value) {
  return String(value || 'not evaluated').replaceAll('_', ' ');
}

export default function CrmErpTokenPersistencePanel({ provider, canManage, onRefresh }) {
  const [state, setState] = useState({ loading: true, payload: null, error: '' });
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState({ tone: '', message: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await request('/api/integrations/026/token-refresh/status');
      setState({ loading: false, payload, error: '' });
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: error?.message || 'OAuth persistence status is unavailable.' }));
    }
  }, []);

  useEffect(() => {
    void load();
    const interval = window.setInterval(() => void load(), 5 * 60 * 1000);
    window.addEventListener('focus', load);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', load);
    };
  }, [load]);

  const selected = useMemo(
    () => (state.payload?.providers || []).find((item) => item.providerKey === provider?.providerKey) || null,
    [state.payload, provider?.providerKey]
  );

  async function refreshNow() {
    if (!canManage || !provider?.providerKey) return;
    setBusy(true);
    setNotice({ tone: '', message: '' });
    try {
      const result = await request(`/api/integrations/026/providers/${encodeURIComponent(provider.providerKey)}/refresh-token`, {
        method: 'POST',
        body: JSON.stringify({ reason: 'Manual OAuth persistence refresh from Module 026.' })
      });
      setNotice({ tone: result.refreshed ? 'success' : 'warning', message: result.message || 'Token refresh evaluation completed.' });
      await load();
      if (onRefresh) await onRefresh();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'The OAuth connection could not be refreshed.' });
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="crm-token-persistence" aria-label="OAuth connection persistence">
      <div className="crm-token-persistence__heading">
        <div>
          <p>Persistent connection service</p>
          <h3>Automatic OAuth token renewal</h3>
          <span>
            ProjectPulse renews enabled OAuth connections server-side before access tokens expire. Refresh tokens, client secrets, and access tokens are never displayed, logged, or returned here.
          </span>
        </div>
        <span className={`crm-token-persistence__service ${state.payload?.backgroundRefreshEnabled ? 'ready' : 'attention'}`}>
          {state.payload?.backgroundRefreshEnabled ? 'Automatic renewal active' : 'Renewal service attention'}
        </span>
      </div>

      {state.error ? <div className="crm-token-persistence__notice error" role="alert">{state.error}</div> : null}
      {notice.message ? <div className={`crm-token-persistence__notice ${notice.tone}`} role="status">{notice.message}</div> : null}

      <div className="crm-token-persistence__facts">
        <div><span>Selected connector</span><strong>{provider?.providerName || 'None selected'}</strong></div>
        <div><span>Token expiration</span><strong>{dateTime(selected?.expiresAt, provider?.authModel === 'oauth2' ? 'Not connected' : 'Not applicable')}</strong></div>
        <div><span>Last refresh</span><strong>{dateTime(selected?.lastRefreshAt)}</strong></div>
        <div><span>Refresh status</span><strong>{statusText(selected?.lastRefreshStatus || selected?.refreshState)}</strong></div>
      </div>

      <div className="crm-token-persistence__footer">
        <div>
          <small>Renewal window: {state.payload?.refreshWindowMinutes ?? 15} minutes before expiration</small>
          {selected?.lastDiagnosticCode ? <small>Sanitized diagnostic: {selected.lastDiagnosticCode}</small> : null}
        </div>
        {provider?.authModel === 'oauth2' ? (
          <button type="button" className="secondary-action" onClick={refreshNow} disabled={!canManage || !selected?.refreshEligible || busy}>
            {busy ? 'Refreshing…' : 'Refresh OAuth token now'}
          </button>
        ) : <span className="crm-token-persistence__not-applicable">API-key connections do not require OAuth renewal.</span>}
      </div>
    </section>
  );
}

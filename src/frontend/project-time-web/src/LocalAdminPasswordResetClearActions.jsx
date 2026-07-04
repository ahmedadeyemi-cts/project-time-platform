import { useCallback, useEffect, useState } from 'react';

const AUTH_STORAGE_KEY = 'projectPulseAuthSession';

function readSessionToken() {
  try {
    const raw = window.localStorage.getItem(AUTH_STORAGE_KEY);
    if (!raw) return '';

    const parsed = JSON.parse(raw);
    const token = parsed?.sessionToken || parsed?.token || parsed?.accessToken;

    return typeof token === 'string' ? token.trim() : '';
  } catch {
    return '';
  }
}

function currentRoute() {
  return (window.location.hash || '').replace(/^#\/?/, '').split(/[/?]/)[0];
}

async function readResponse(response, path) {
  const text = await response.text();

  let payload = {};
  try {
    payload = text ? JSON.parse(text) : {};
  } catch {
    payload = { raw: text };
  }

  if (!response.ok) {
    const message = payload.message || payload.detail || payload.status || `${path} returned HTTP ${response.status}`;
    throw new Error(message);
  }

  return payload;
}

async function fetchJson(path, token, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-ProjectPulse-Session': token,
      ...(options.headers || {})
    },
    credentials: 'same-origin'
  });

  return readResponse(response, path);
}

export default function LocalAdminPasswordResetClearActions() {
  const [route, setRoute] = useState(() => currentRoute());
  const [token, setToken] = useState(() => readSessionToken());
  const [summary, setSummary] = useState(null);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);

  const isManagerApprovalRoute = route === 'manager-approval';
  const readyToClear = Number(summary?.readyToClear || 0);
  const totalLocalResetRequests = Number(summary?.totalLocalResetRequests || 0);

  const syncRuntimeState = useCallback(() => {
    setRoute(currentRoute());
    setToken(readSessionToken());
  }, []);

  const loadSummary = useCallback(async () => {
    if (!isManagerApprovalRoute || !token) {
      setSummary(null);
      return;
    }

    const data = await fetchJson('/api/auth/password-reset/clear-ready-summary', token);
    setSummary(data.summary || null);
  }, [isManagerApprovalRoute, token]);

  useEffect(() => {
    const onHashChange = () => syncRuntimeState();
    const onFocus = () => syncRuntimeState();
    const onStorage = (event) => {
      if (event.key === AUTH_STORAGE_KEY) syncRuntimeState();
    };

    window.addEventListener('hashchange', onHashChange);
    window.addEventListener('focus', onFocus);
    window.addEventListener('storage', onStorage);

    const interval = window.setInterval(syncRuntimeState, 2000);

    return () => {
      window.removeEventListener('hashchange', onHashChange);
      window.removeEventListener('focus', onFocus);
      window.removeEventListener('storage', onStorage);
      window.clearInterval(interval);
    };
  }, [syncRuntimeState]);

  useEffect(() => {
    let cancelled = false;

    if (!isManagerApprovalRoute || !token) {
      setSummary(null);
      setMessage('');
      return () => {
        cancelled = true;
      };
    }

    loadSummary()
      .then(() => {
        if (!cancelled) setMessage('');
      })
      .catch((error) => {
        if (!cancelled) {
          setSummary(null);
          setMessage(error?.message || 'Unable to load local admin password reset queue summary.');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [isManagerApprovalRoute, token, refreshKey, loadSummary]);

  async function clearReadyQueue() {
    if (!token || readyToClear <= 0 || busy) return;

    const confirmed = window.confirm(
      `Clear ${readyToClear} approved local admin password reset request${readyToClear === 1 ? '' : 's'} from the temporary-password queue?`
    );

    if (!confirmed) return;

    setBusy(true);
    setMessage('');

    try {
      const result = await fetchJson('/api/auth/password-reset/clear-ready', token, {
        method: 'POST',
        body: JSON.stringify({})
      });

      setMessage(result.message || 'Ready local admin password reset requests were cleared.');
      setRefreshKey((value) => value + 1);

      window.setTimeout(() => {
        window.location.reload();
      }, 750);
    } catch (error) {
      setMessage(error?.message || 'Unable to clear the local admin password reset queue.');
    } finally {
      setBusy(false);
    }
  }

  if (!isManagerApprovalRoute || !token) {
    return null;
  }

  return (
    <aside className="local-admin-reset-clear-floating-panel" aria-label="Local admin reset queue clear controls">
      <div>
        <span className="local-admin-reset-clear-eyebrow">Reset Queue</span>
        <h3>Clear ready temp-password requests</h3>
        <p>
          Ready for temp password: <strong>{readyToClear}</strong>
        </p>
        <p>Total local reset requests: {totalLocalResetRequests}</p>
        {message ? <p className="local-admin-reset-clear-message">{message}</p> : null}
      </div>

      <button
        type="button"
        className="local-admin-reset-clear-button"
        disabled={busy || readyToClear <= 0}
        onClick={clearReadyQueue}
      >
        {busy ? 'Clearing…' : 'Clear ready reset queue'}
      </button>
    </aside>
  );
}

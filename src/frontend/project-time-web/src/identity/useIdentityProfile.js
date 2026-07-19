import {
  useCallback,
  useEffect,
  useRef,
  useState
} from 'react';

function authHeaders() {
  try {
    const session = JSON.parse(
      window.localStorage.getItem(
        'projectPulseAuthSession'
      ) || 'null'
    );

    return session?.sessionToken
      ? {
          Authorization:
            `Bearer ${session.sessionToken}`,
          'X-ProjectPulse-Session':
            session.sessionToken
        }
      : {};
  } catch {
    return {};
  }
}

async function fetchIdentityProfile(signal) {
  const response = await fetch(
    '/api/identity/profile',
    {
      headers: authHeaders(),
      signal
    }
  );

  const raw = await response.text();
  let payload = {};

  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = {
      message: raw
    };
  }

  if (!response.ok) {
    throw new Error(
      payload.message
      || `Identity profile returned HTTP ${response.status}`
    );
  }

  return payload.profile ?? payload;
}

export default function useIdentityProfile({
  refreshSeconds = 60,
  enabled = true
} = {}) {
  const [state, setState] = useState({
    loading: enabled,
    profile: null,
    error: null,
    refreshedAt: null
  });

  const requestRef = useRef(null);

  const refresh = useCallback(async () => {
    if (!enabled) {
      setState({
        loading: false,
        profile: null,
        error: null,
        refreshedAt: null
      });

      return;
    }

    requestRef.current?.abort();

    const controller = new AbortController();
    requestRef.current = controller;

    setState((current) => ({
      ...current,
      loading: current.profile === null,
      error: null
    }));

    try {
      const profile = await fetchIdentityProfile(
        controller.signal
      );

      setState({
        loading: false,
        profile,
        error: null,
        refreshedAt: new Date().toISOString()
      });
    } catch (error) {
      if (error?.name === 'AbortError') {
        return;
      }

      setState((current) => ({
        ...current,
        loading: false,
        error:
          error instanceof Error
            ? error.message
            : 'Unable to load the identity profile.'
      }));
    }
  }, [enabled]);

  useEffect(() => {
    void refresh();

    const intervalMilliseconds =
      Math.max(15, Number(refreshSeconds) || 60)
      * 1000;

    const interval = window.setInterval(
      () => void refresh(),
      intervalMilliseconds
    );

    const handleIdentityChange = () => {
      void refresh();
    };

    window.addEventListener(
      'projectpulse:view-as-changed',
      handleIdentityChange
    );

    window.addEventListener(
      'projectpulse:identity-profile-changed',
      handleIdentityChange
    );

    return () => {
      window.clearInterval(interval);
      window.removeEventListener(
        'projectpulse:view-as-changed',
        handleIdentityChange
      );
      window.removeEventListener(
        'projectpulse:identity-profile-changed',
        handleIdentityChange
      );
      requestRef.current?.abort();
    };
  }, [refresh, refreshSeconds]);

  return {
    ...state,
    refresh
  };
}

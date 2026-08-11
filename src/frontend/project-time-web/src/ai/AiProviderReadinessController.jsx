import { useEffect, useState } from 'react';
import {
  startAiProviderReadinessMonitoring,
  stopAiProviderReadinessMonitoring
} from './ai-provider-readiness-store.js';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? '';
}

function viewAsActive() {
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return Boolean(value?.userId);
  } catch {
    return false;
  }
}

export default function AiProviderReadinessController({ authSession }) {
  const token = sessionToken(authSession);
  const [authorityVersion, setAuthorityVersion] = useState(0);

  useEffect(() => {
    const refreshAuthority = () => setAuthorityVersion((value) => value + 1);
    window.addEventListener('projectpulse:view-as-changed', refreshAuthority);
    window.addEventListener('storage', refreshAuthority);
    return () => {
      window.removeEventListener('projectpulse:view-as-changed', refreshAuthority);
      window.removeEventListener('storage', refreshAuthority);
    };
  }, []);

  useEffect(() => {
    if (!token || viewAsActive()) {
      stopAiProviderReadinessMonitoring();
      return undefined;
    }

    const stop = startAiProviderReadinessMonitoring();
    return () => stop?.();
  }, [token, authorityVersion]);

  return null;
}

import { useEffect } from 'react';
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

export default function AiProviderReadinessController({ authSession }) {
  const token = sessionToken(authSession);

  useEffect(() => {
    if (!token) {
      stopAiProviderReadinessMonitoring();
      return undefined;
    }

    const stop = startAiProviderReadinessMonitoring();
    return () => stop?.();
  }, [token]);

  return null;
}

import { useEffect, useState } from 'react';
import HelpAssistant from './HelpAssistant.jsx';

const AUTH_SESSION_STORAGE_KEY = 'projectPulseAuthSession';

function hasUsableAuthSession() {
  try {
    const session = JSON.parse(window.localStorage.getItem(AUTH_SESSION_STORAGE_KEY) || 'null');
    return Boolean(
      session?.sessionToken
      && session?.expiresAt
      && Date.now() < Date.parse(session.expiresAt)
    );
  } catch {
    return false;
  }
}

export default function AuthenticatedHelpAssistant() {
  const [hasAuthenticatedSession, setHasAuthenticatedSession] = useState(hasUsableAuthSession);

  useEffect(() => {
    const refreshAuthVisibility = () => setHasAuthenticatedSession(hasUsableAuthSession());

    window.addEventListener('storage', refreshAuthVisibility);
    window.addEventListener('projectpulse:auth-session-ready', refreshAuthVisibility);
    window.addEventListener('projectpulse:auth-session-cleared', refreshAuthVisibility);
    return () => {
      window.removeEventListener('storage', refreshAuthVisibility);
      window.removeEventListener('projectpulse:auth-session-ready', refreshAuthVisibility);
      window.removeEventListener('projectpulse:auth-session-cleared', refreshAuthVisibility);
    };
  }, []);

  return hasAuthenticatedSession ? <HelpAssistant /> : null;
}

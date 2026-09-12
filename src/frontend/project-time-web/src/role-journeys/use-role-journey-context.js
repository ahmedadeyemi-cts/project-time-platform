import { useSyncExternalStore } from 'react';
import { EFFECTIVE_ROLE_AUTHORITY_EVENTS, readEffectiveRoleAuthority } from '../effective-role-authority.js';
import { authorizedModulesFromNavigationState } from '../module-directory-authority.js';
import { PROJECTPULSE_MODULES } from '../module-availability-registry.js';

const EMPTY = JSON.stringify({ roleCodes: [], viewAsActive: false, accessReady: false, modules: [] });
// The module-availability bridge publishes this event after the server-backed
// RBAC refresh completes. Without it, My Role remains stuck on its initial
// loading snapshot and never exposes the effective user's authorized links.
export const ROLE_JOURNEY_AUTHORITY_EVENTS = Object.freeze([
  ...new Set([...EFFECTIVE_ROLE_AUTHORITY_EVENTS,
    'projectpulse:auth-session-cleared',
    'projectpulse:permission-navigation-updated'])
]);
function subscribe(listener) {
  ROLE_JOURNEY_AUTHORITY_EVENTS.forEach((event) => window.addEventListener(event, listener));
  return () => ROLE_JOURNEY_AUTHORITY_EVENTS.forEach((event) => window.removeEventListener(event, listener));
}
function snapshot() {
  if (typeof window === 'undefined') return EMPTY;
  const authority = readEffectiveRoleAuthority();
  const navigation = window.__projectPulseEffectiveNavigation;
  // A role description is not a grant. Withhold action links until navigation
  // is ready; use the same authority as the existing Modules directory.
  const allowed = navigation?.state === 'ready'
    ? authorizedModulesFromNavigationState(PROJECTPULSE_MODULES, navigation) : null;
  return JSON.stringify({
    roleCodes: authority.roleCodes || [], viewAsActive: Boolean(authority.viewAsActive),
    accessReady: allowed !== null,
    modules: (allowed || []).map(({ route, displayName }) => ({ route, displayName }))
  });
}
export default function useRoleJourneyContext() {
  // The primitive snapshot remains stable when no relevant value changes.
  // No credentials, customer records or learning progress are persisted here.
  return JSON.parse(useSyncExternalStore(subscribe, snapshot, () => EMPTY));
}

import { useSyncExternalStore } from 'react';
import { EFFECTIVE_ROLE_AUTHORITY_EVENTS, readEffectiveRoleAuthority } from '../effective-role-authority.js';
import { authorizedModulesFromNavigationState } from '../module-directory-authority.js';
import { PROJECTPULSE_MODULES } from '../module-availability-registry.js';
import { createJourneyIdentityGuard, resolveJourneyAudience } from './role-journey-access.js';

const EMPTY = JSON.stringify(resolveJourneyAudience());
const identityGuard = createJourneyIdentityGuard();
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
  try {
    const authority = readEffectiveRoleAuthority();
    const navigation = window.__projectPulseEffectiveNavigation;
    // Compare session boundaries only in memory. Never expose credentials or
    // View-As identity in the React snapshot, markup, logs, or saved progress.
    const session = window.localStorage.getItem('projectPulseAuthSession') || '';
    const viewAs = window.localStorage.getItem('projectPulseViewAsUser') || '';
    const guard = identityGuard.observe(navigation, JSON.stringify([session, viewAs]));
    const navigationCurrent = Boolean(session) && guard.current;
    const allowed = navigationCurrent && navigation?.state === 'ready'
      ? authorizedModulesFromNavigationState(PROJECTPULSE_MODULES, navigation) : null;
    // navigation.journeyRoleCodes is server-derived learning assignment only;
    // it is never passed to the module authorization function above.
    return JSON.stringify(resolveJourneyAudience({
      navigation, viewAsActive: authority.viewAsActive,
      allowedModules: allowed, navigationCurrent, revision: guard.revision
    }));
  } catch {
    return EMPTY;
  }
}
export default function useRoleJourneyContext() {
  return JSON.parse(useSyncExternalStore(subscribe, snapshot, () => EMPTY));
}

import { useEffect, useState } from 'react';
import PtcTimesheetManagementPortal from './PtcTimesheetManagementPortal.jsx';

const ALLOWED_ROLES = new Set([
  'PROJECT_TEAM_COORDINATOR',
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR'
]);

function normalizeRoles(value) {
  if (Array.isArray(value)) {
    return value.map((role) => String(role).trim().toUpperCase()).filter(Boolean);
  }
  return String(value || '')
    .split(',')
    .map((role) => role.trim().toUpperCase())
    .filter(Boolean);
}

function viewAsState() {
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    if (!value?.userId) return { active: false, allowed: true, roles: [] };
    const roles = normalizeRoles(value.roleCodes ?? value.roles ?? value.roleCode);
    return {
      active: true,
      allowed: roles.some((role) => ALLOWED_ROLES.has(role)),
      roles
    };
  } catch {
    return { active: false, allowed: true, roles: [] };
  }
}

export default function PtcTimeStewardGate() {
  const [state, setState] = useState(() => viewAsState());

  useEffect(() => {
    const refresh = () => setState(viewAsState());
    window.addEventListener('projectpulse:view-as-changed', refresh);
    window.addEventListener('storage', refresh);
    window.addEventListener('hashchange', refresh);
    return () => {
      window.removeEventListener('projectpulse:view-as-changed', refresh);
      window.removeEventListener('storage', refresh);
      window.removeEventListener('hashchange', refresh);
    };
  }, []);

  // View-As remains read-only and must not expose the time-steward workspace for
  // an effective identity that is not itself a PTC or administrator. In an own
  // session the backend remains authoritative and hides the portal on a 403.
  if (state.active && !state.allowed) return null;

  return <PtcTimesheetManagementPortal />;
}

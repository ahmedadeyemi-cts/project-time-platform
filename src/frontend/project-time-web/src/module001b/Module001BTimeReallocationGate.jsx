import { useEffect, useState } from 'react';
import {
  EFFECTIVE_ROLE_AUTHORITY_EVENTS,
  hasAnyEffectiveRole,
  readEffectiveRoleAuthority
} from '../effective-role-authority.js';
import Module001BTimeReallocationPortal from './Module001BTimeReallocationPortal.jsx';

// Module 001B is a fixed-access administrative correction module.
// Its access boundary is deliberately not delegated through Module 001.
const MODULE001B_ROLES = new Set([
  'PROJECT_TEAM_COORDINATOR',
  'SUPER_ADMINISTRATOR'
]);

export default function Module001BTimeReallocationGate() {
  const [authority, setAuthority] = useState(() => readEffectiveRoleAuthority());

  useEffect(() => {
    const refresh = () => setAuthority(readEffectiveRoleAuthority());
    EFFECTIVE_ROLE_AUTHORITY_EVENTS.forEach((eventName) => window.addEventListener(eventName, refresh));
    const interval = window.setInterval(refresh, 1000);

    return () => {
      EFFECTIVE_ROLE_AUTHORITY_EVENTS.forEach((eventName) => window.removeEventListener(eventName, refresh));
      window.clearInterval(interval);
    };
  }, []);

  if (!authority.ready) return null;

  return (
    <Module001BTimeReallocationPortal
      allowed={hasAnyEffectiveRole(authority, MODULE001B_ROLES)}
    />
  );
}

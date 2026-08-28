import { useEffect, useState } from 'react';
import ProductionApprovalWorkPortal from '../ProductionApprovalWorkPortal.jsx';
import Module001BTimeReallocationPortal from '../module001b/Module001BTimeReallocationPortal.jsx';
import {
  EFFECTIVE_ROLE_AUTHORITY_EVENTS,
  hasAnyEffectiveRole,
  readEffectiveRoleAuthority
} from '../effective-role-authority.js';
import PtcTimesheetManagementPortal from './PtcTimesheetManagementPortal.jsx';
import './module001b-reallocation-retirement.css';

// View-As storage compatibility remains centralized in effective-role-authority.js
// under the canonical key 'projectPulseViewAsUser'.
const TIME_STEWARD_ROLES = new Set([
  'PROJECT_TEAM_COORDINATOR',
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR'
]);

// Module 001B is intentionally stricter than the legacy Module 001 steward shell.
// Normal Administrators, Managers, PMs, Engineers, and every other role are No Access.
const MODULE001B_ROLES = new Set([
  'PROJECT_TEAM_COORDINATOR',
  'SUPER_ADMINISTRATOR'
]);

const APPROVAL_ROLES = new Set([
  'PROJECT_TEAM_COORDINATOR',
  'PROJECT_COORDINATOR',
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'MANAGER',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'PM_TEAM_LEAD'
]);

function currentRoute() {
  if (typeof window === 'undefined') return '';
  return String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0];
}

export default function PtcTimeStewardGate() {
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

  const canStewardTime = hasAnyEffectiveRole(authority, TIME_STEWARD_ROLES);
  const canUseModule001B = hasAnyEffectiveRole(authority, MODULE001B_ROLES);
  const canReviewApprovals = hasAnyEffectiveRole(authority, APPROVAL_ROLES);
  const showModule001BLauncher = canUseModule001B && currentRoute() === 'timesheet';

  return (
    <>
      <Module001BTimeReallocationPortal allowed={canUseModule001B} />
      {showModule001BLauncher ? (
        <button
          type="button"
          className="module001b-reallocation-launcher"
          onClick={() => { window.location.hash = '#time-reallocation'; }}
        >
          <strong>Time Reallocation</strong>
          <span>Open Module 001B</span>
        </button>
      ) : null}
      {canStewardTime ? <PtcTimesheetManagementPortal /> : null}
      {canReviewApprovals ? <ProductionApprovalWorkPortal /> : null}
    </>
  );
}

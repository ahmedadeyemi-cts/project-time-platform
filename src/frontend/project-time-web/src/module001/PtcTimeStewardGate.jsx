import { useEffect, useState } from 'react';
import ProductionApprovalWorkPortal from '../ProductionApprovalWorkPortal.jsx';
import {
  EFFECTIVE_ROLE_AUTHORITY_EVENTS,
  hasAnyEffectiveRole,
  readEffectiveRoleAuthority
} from '../effective-role-authority.js';
import PtcGuidedMovePortal from './PtcGuidedMovePortal.jsx';
import PtcTimesheetManagementPortal from './PtcTimesheetManagementPortal.jsx';

const TIME_STEWARD_ROLES = new Set([
  'PROJECT_TEAM_COORDINATOR',
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR'
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
  const canReviewApprovals = hasAnyEffectiveRole(authority, APPROVAL_ROLES);

  if (!canStewardTime && !canReviewApprovals) return null;

  return (
    <>
      {canStewardTime ? <PtcTimesheetManagementPortal /> : null}
      {canStewardTime ? <PtcGuidedMovePortal /> : null}
      {canReviewApprovals ? <ProductionApprovalWorkPortal /> : null}
    </>
  );
}

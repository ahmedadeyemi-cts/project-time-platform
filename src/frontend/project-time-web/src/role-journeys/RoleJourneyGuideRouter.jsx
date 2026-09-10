import { useSyncExternalStore } from 'react';
import SystemUserGuide from '../SystemUserGuide.Module001.g.jsx';
import MyRoleInPulse from './MyRoleInPulse.jsx';
import { JOURNEY_ROUTE } from './role-journeys.js';

function subscribe(listener) {
  window.addEventListener('hashchange', listener);
  return () => window.removeEventListener('hashchange', listener);
}
const snapshot = () => typeof window === 'undefined' ? '' : window.location.hash;

// Mounted only by the existing authenticated Module 999 route. The dedicated
// URL is an alias of that module, never a new permission or a separate React root.
export default function RoleJourneyGuideRouter(props) {
  const hash = useSyncExternalStore(subscribe, snapshot, () => '');
  if (hash === `#${JOURNEY_ROUTE}`) return <MyRoleInPulse />;
  return <>
    <aside className="rj-guide-launch" aria-label="Visual role playbooks">
      <div><strong>New to Pulse or changing roles?</strong><p>See your responsibilities, visual steps and handoffs on one dedicated page.</p></div>
      <a href={`#${JOURNEY_ROUTE}`} data-module-number="999">Open My Role in Pulse →</a>
    </aside>
    <SystemUserGuide {...props} />
  </>;
}

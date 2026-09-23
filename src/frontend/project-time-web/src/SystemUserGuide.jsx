import { useMemo } from 'react';
import './system-user-guide.css';
import { compareProjectPulseModules } from './module-ordering.js';
import { PROJECTPULSE_MODULES } from './module-availability-registry.js';
import { SystemUserGuideGovernancePanel } from './help/HelpGovernancePanel.jsx';
import USSignalLogo from './enterprise/USSignalLogo.jsx';
import GuideWorkbench from './guide/GuideWorkbench.jsx';
import { buildGuideCatalog } from './guide/guide-catalog.js';
import { GUIDE_EDITION } from './guide/guide-model.js';

// Module 001's existing generator owns this active foundation in the generated
// guide. Preserve its timesheet -> manager-approval boundary; render the result
// beside the detailed procedures so generator-owned timer rules stay current.
const detailedModuleGuides = {
  timesheet: {
    purpose: 'Record your own actual effort against permitted assigned work.',
    functions: [
      'Weekly Grid, Daily Focus, Guided Add and Quick Entry List provide supported entry views.',
      'Start / Stop Timer uses server-authoritative UTC timestamps; Mobile mode provides supported compact views.',
      'Up to five distinct activity timers may run per user, each timer rounds upward once to a quarter hour, and the server caps each timer at 24 hours.',
      'Save draft persists editable work; Submit week validates and sends your own eligible time to the Module 002 Approval Inbox.'
    ]
  },
  'manager-approval': {
    purpose: 'Review only actionable entries in the stage and scope granted to you.',
    functions: ['Manager, project and PTC final reviews are separate responsibilities.', 'An approval or return must be confirmed by its saved decision, not inferred from a click.']
  }
};

export default function SystemUserGuide({ modules = [] }) {
  const catalog = useMemo(() => {
    const entries = buildGuideCatalog(PROJECTPULSE_MODULES, modules);
    return entries.sort(compareProjectPulseModules);
  }, [modules]);
  return (
    <div className="system-user-guide current-user-guide" data-user-guide-edition={GUIDE_EDITION}>
      <header className="system-user-guide-header">
        {/* GROUP_7_SYSTEM_GUIDE_LOGO */}
        <USSignalLogo size="large" />
        <div><p className="eyebrow">MODULE 999 · LEARNING AND OPERATING REFERENCE</p>
          <h1>System User Guide</h1>
          <p>Current features, role responsibilities, step-by-step procedures and accountable handoffs.</p>
        </div>
      </header>
      {/* GROUP_7_SYSTEM_GUIDE_GOVERNANCE_START */}
      <SystemUserGuideGovernancePanel />
      {/* GROUP_7_SYSTEM_GUIDE_GOVERNANCE_END */}
      <GuideWorkbench catalog={catalog} foundations={detailedModuleGuides} />
    </div>
  );
}

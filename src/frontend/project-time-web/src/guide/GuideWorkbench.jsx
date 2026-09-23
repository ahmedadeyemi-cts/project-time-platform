import { useEffect, useMemo, useState } from 'react';
import { flushSync } from 'react-dom';
import useRoleJourneyContext from '../role-journeys/use-role-journey-context.js';
import { assignedGuideRoles, ROLE_REFERENCE, ROLE_NOTES, PLATFORM_GUIDES, HANDOFFS, GLOSSARY } from './guide-content.js';
import { GUIDE_BASELINE, GUIDE_EDITION, filterGuideEntries, guideLinkAllowed, flattenGuide } from './guide-model.js';
import './user-guide-workbench.css';

const roleTitle = code => ROLE_REFERENCE.find(role => role.code === code)?.title || (code === '*' ? 'Everyone' : code.replaceAll('_', ' '));
const initialQuery = () => {
  if (typeof window === 'undefined') return '';
  return new URLSearchParams(window.location.search).get('guide')?.slice(0, 160) || '';
};
function BulletText({ items }) { return <ul>{items.map((text, index) => <li key={index}>{text}</li>)}</ul>; }
function WorkspaceLink({ route, context, title = 'Open workspace' }) {
  return guideLinkAllowed(route, context)
    ? <a className="guide-open-workspace" href={`#${route}`}>{title}</a>
    : <span className="guide-access-note">Workspace link unavailable until your access to this route is verified.</span>;
}
function Procedure({ procedure }) {
  return <section className="guide-procedure"><h4>{procedure.title}</h4>
    <ol>{procedure.steps.map((step, index) => <li key={index}>{step}</li>)}</ol>
    <dl className="guide-outcome"><div><dt>Expected result</dt><dd>{procedure.outcome}</dd></div>
      <div><dt>Next owner / handoff</dt><dd>{procedure.handoff}</dd></div></dl>
  </section>;
}
function RoleHandbook({ context, referenceMode, setReferenceMode, printMode, openProcedure }) {
  const assigned = assignedGuideRoles(context);
  const roles = referenceMode ? ROLE_REFERENCE : assigned;
  const [selectedCode, setSelectedCode] = useState('');
  const current = roles.find(role => role.code === selectedCode) || roles[0];
  return <section className="guide-card" aria-labelledby="guide-role-heading">
    <div className="guide-section-heading"><div><h2 id="guide-role-heading">{referenceMode ? 'All role reference' : 'My responsibilities'}</h2>
      <p>{referenceMode ? 'Learning reference only. This selection does not assign a role, change View-As or grant access.' : 'These roles come from the current verified access context.'}</p></div>
      <button type="button" className="guide-no-print" aria-pressed={referenceMode} onClick={() => setReferenceMode(value => !value)}>
        {referenceMode ? 'Show my responsibilities' : 'Browse all role reference'}
      </button></div>
    {!referenceMode && !context.accessReady ? <p role="status">Verifying your assigned roles. Reference documentation remains available; workspace links stay closed.</p> : null}
    {!referenceMode && context.accessReady && !assigned.length ? <p>No learning role is assigned in this session. Ask your role owner for reviewed responsibilities; do not infer an administrator role.</p> : null}
    {roles.length ? <>
      <label className="guide-no-print">{referenceMode ? 'Reference role' : 'Your assigned role'}
        <select value={current?.code || ''} onChange={event => setSelectedCode(event.target.value)}>
          {roles.map(role => <option value={role.code} key={role.code}>{role.title}</option>)}
        </select></label>
      <article className="guide-role-detail" data-guide-role={current.code}>
        <h3>{current.title}</h3><p>{current.purpose}</p>
        <p className="guide-boundary"><strong>Responsibility boundary: </strong>{current.boundary}</p>
        <BulletText items={ROLE_NOTES[current.code] || ['This custom role requires instructions reviewed by its accountable owner. The current module/action scope remains authoritative.']} />
        <p>{referenceMode ? 'Reference playbook, not an assignment.' : 'Assigned-role playbook.'} Read each module’s prerequisites before acting.</p>
        <div className="guide-role-steps">{current.steps.map(step => <details key={step.id} open={printMode || undefined}>
          <summary>{step.title}</summary><dl>
            <div><dt>Starting input</dt><dd>{step.input}</dd></div>
            <div><dt>Your action</dt><dd>{step.action}</dd></div>
            <div><dt>Output</dt><dd>{step.output}</dd></div>
            <div><dt>Acceptance check</dt><dd>{step.acceptance}</dd></div>
            <div><dt>Next owner</dt><dd>{step.nextOwner}</dd></div>
            <div><dt>Exception / restriction</dt><dd>{step.exception}</dd></div>
          </dl><button type="button" className="guide-no-print" onClick={() => openProcedure(step.route)}>Read module procedure</button>
          <WorkspaceLink route={step.route} context={context} /></details>)}</div>
      </article></> : null}
  </section>;
}

export default function GuideWorkbench({ catalog, foundations }) {
  const context = useRoleJourneyContext();
  const [query, setQuery] = useState(initialQuery);
  const [category, setCategory] = useState('*');
  const [role, setRole] = useState('*');
  const [availableOnly, setAvailableOnly] = useState(false);
  const [referenceMode, setReferenceMode] = useState(false);
  const [expanded, setExpanded] = useState(() => new Set());
  const [printMode, setPrintMode] = useState(false);
  const categories = useMemo(() => [...new Set(catalog.map(entry => entry.group))].sort(), [catalog]);
  const entries = useMemo(() => filterGuideEntries(catalog, { query, category, role, availableOnly, context }), [catalog, query, category, role, availableOnly, context]);
  const covered = catalog.filter(entry => entry.guide).length;
  const procedures = catalog.reduce((count, entry) => count + (entry.guide?.procedures.length || 0), 0);
  const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const globalEntries = PLATFORM_GUIDES.filter(entry => terms.every(term => flattenGuide(entry).toLowerCase().includes(term)));
  useEffect(() => {
    const before = () => flushSync(() => setPrintMode(true));
    const after = () => setPrintMode(false);
    window.addEventListener('beforeprint', before); window.addEventListener('afterprint', after);
    return () => { window.removeEventListener('beforeprint', before); window.removeEventListener('afterprint', after); };
  }, []);
  function openProcedure(route) {
    setQuery(route); setCategory('*'); setRole('*'); setAvailableOnly(false); setExpanded(new Set([route]));
    window.setTimeout(() => document.getElementById('guide-module-heading')?.scrollIntoView({ block: 'start' }), 0);
  }
  function reset() { setQuery(''); setCategory('*'); setRole('*'); setAvailableOnly(false); }
  function toggle(route, open) {
    if (printMode) return;
    setExpanded(previous => {
      if (previous.has(route) === open) return previous;
      const next = new Set(previous); open ? next.add(route) : next.delete(route); return next;
    });
  }
  return <div className="guide-workbench" data-guide-ready={context.accessReady ? 'true' : 'false'}>
    <section className="guide-card guide-edition" aria-label="Guide coverage and access boundary">
      <p><strong>Edition {GUIDE_EDITION}</strong> · {covered} reviewed workspaces · {procedures} module procedures · {ROLE_REFERENCE.length} role handbooks</p>
      <p>The guide describes implemented controls and their prerequisites. Configuration, role permissions, record state and actual service readiness still determine what you can do. Source review is not a guarantee that every external integration is live.</p>
      {context.viewAsActive ? <p className="guide-boundary">View-As is active. You are reading the effective user’s context; it remains a read-only preview.</p> : null}
      <p className="guide-caption">Documentation reference: <code>{GUIDE_BASELINE.slice(0, 12)}</code>. Source paths appear inside each module entry.</p>
    </section>
    <RoleHandbook context={context} referenceMode={referenceMode} setReferenceMode={setReferenceMode} printMode={printMode} openProcedure={openProcedure} />
    <section className="guide-card guide-no-print" aria-label="Find a how-to">
      <div className="guide-filters">
        <label>Search the guide<input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="025, PTO transfer, timers, billing, role…" /></label>
        <label>Category<select value={category} onChange={event => setCategory(event.target.value)}><option value="*">All categories</option>{categories.map(item => <option key={item}>{item}</option>)}</select></label>
        <label>Audience reference<select value={role} onChange={event => setRole(event.target.value)}><option value="*">All responsibilities</option>{ROLE_REFERENCE.map(item => <option value={item.code} key={item.code}>{item.title}</option>)}</select></label>
      </div>
      <div className="guide-toolbar"><label className="guide-checkbox"><input type="checkbox" checked={availableOnly} onChange={event => setAvailableOnly(event.target.checked)} />Only my available workspaces</label>
        <button type="button" onClick={reset}>Reset filters</button>
        <button type="button" onClick={() => setExpanded(new Set(entries.map(entry => entry.route)))}>Expand results</button>
        <button type="button" onClick={() => setExpanded(new Set())}>Collapse results</button>
        <button type="button" onClick={() => window.print()}>Print current guide</button>
      </div><p role="status" aria-live="polite">{entries.length} module entries and {globalEntries.length} platform how-tos match the search.</p>
      <p className="guide-caption">Category, audience and available-workspace filters apply to modules. Platform how-tos are searched by text. Choosing an audience is a learning filter, not an access change.</p>
    </section>
    <section className="guide-card" aria-labelledby="guide-platform-heading"><h2 id="guide-platform-heading">Platform essentials</h2>
      <div className="guide-global-list">{globalEntries.map(entry => <details key={entry.id} open={printMode || undefined}>
        <summary>{entry.title}</summary><ol>{entry.steps.map((step, index) => <li key={index}>{step}</li>)}</ol><p><strong>Expected result: </strong>{entry.outcome}</p>
      </details>)}</div>{!globalEntries.length ? <p>No platform how-tos match this search.</p> : null}
    </section>
    <section className="guide-modules" aria-labelledby="guide-module-heading"><h2 id="guide-module-heading" tabIndex={-1}>Module how-to library</h2>
      <p className="guide-print-only">Search: {query || 'All'} · Category: {category} · Audience: {roleTitle(role)}. Available-workspace filter: {availableOnly ? 'on' : 'off'}.</p>
      {!entries.length ? <div className="guide-card"><h3>No matching module procedures</h3><p>Clear the search or reset the category, audience and available-workspace filters.</p><button type="button" onClick={reset}>Show all module procedures</button></div> : null}
      {entries.map(entry => <details className="guide-card guide-module" key={entry.route} data-guide-route={entry.route} open={printMode || expanded.has(entry.route)}>
        <summary onClick={event => { event.preventDefault(); toggle(entry.route, !expanded.has(entry.route)); }}><span className="guide-module-number">{entry.moduleNumber ? `Module ${entry.moduleNumber}` : 'Platform'}</span> <strong>{entry.title}</strong><span className="guide-module-category">{entry.group}</span></summary>
        {entry.guide ? <div className="guide-module-content"><h3>Responsibility and scope</h3><p>{entry.guide.responsibility}</p>
          <p><strong>Audience reference: </strong>{entry.guide.roles.map(roleTitle).join(', ')}</p>
          <h3>Before you begin</h3><BulletText items={entry.guide.prerequisites} />
          {foundations[entry.route] ? <section className="guide-foundation"><h3>Current workflow foundation</h3><p>{foundations[entry.route].purpose}</p><BulletText items={foundations[entry.route].functions} /></section> : null}
          <h3>How-to procedures</h3>{entry.guide.procedures.map(procedure => <Procedure key={procedure.title} procedure={procedure} />)}
          <section className="guide-boundary"><h3>Limitations and important distinctions</h3><BulletText items={entry.guide.limitations} /></section>
          <WorkspaceLink route={entry.route} context={context} title={`Open ${entry.title}`} />
          <details className="guide-provenance"><summary>Implementation references</summary><p>Reviewed repository paths at {GUIDE_BASELINE.slice(0, 12)}; not a live integration-health claim.</p><ul>{entry.guide.sources.map(path => <li key={path}><code>{path}</code></li>)}</ul></details>
        </div> : <div className="guide-module-content"><h3>Reviewed instructions not yet published</h3><p>This newly registered route has metadata only and is not counted as covered. Ask the owning team to publish prerequisites, role responsibilities, procedures and acceptance checks.</p></div>}
      </details>)}
    </section>
    <section className="guide-card" aria-labelledby="guide-handoff-heading"><h2 id="guide-handoff-heading">Cross-team handoffs</h2>
      {HANDOFFS.map(flow => <details key={flow.title} open={printMode || undefined}><summary>{flow.title}</summary><ol>{flow.stages.map(([owner, route, action], index) => <li key={index}><strong>{owner}</strong><p>{action}</p><button type="button" className="guide-no-print" onClick={() => openProcedure(route)}>Read related procedure</button></li>)}</ol></details>)}
    </section>
    <section className="guide-card" aria-labelledby="guide-glossary-heading"><h2 id="guide-glossary-heading">Terms and status meanings</h2><dl className="guide-glossary">{GLOSSARY.map(([term, meaning]) => <div key={term}><dt>{term}</dt><dd>{meaning}</dd></div>)}</dl></section>
    <section className="guide-card"><h2>Need help or found an outdated instruction?</h2><p>Provide the guide edition, module/route, your actual and effective role, the specific procedure, expected versus actual behavior, timestamp and sanitized error/correlation reference. Do not include credentials, session tokens, secrets or unnecessary customer data. Missing reviewed instructions should be reported rather than treated as an authorization workaround.</p></section>
  </div>;
}

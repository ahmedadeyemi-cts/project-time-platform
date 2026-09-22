import { useEffect, useMemo, useRef, useState } from 'react';
import USSignalLogo from '../enterprise/USSignalLogo.jsx';
import { ROLE_GUIDANCE } from '../role-permission-model.js';
import useRoleJourneyContext from './use-role-journey-context.js';
import { JOURNEY_REVIEW, LIFECYCLE, matchesJourney, userStory } from './role-journeys.js';
import { assignedRolePlaybooks, EXPERIENCE_VERSION } from './role-journey-playbooks.js';
import './role-journeys.css';
import './role-journey-motion.css';

function useMotionPreference() {
  const [reduced, setReduced] = useState(() => typeof window === 'undefined' || !window.matchMedia || window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const [visible, setVisible] = useState(() => typeof document === 'undefined' || !document.hidden);
  useEffect(() => {
    const media = window.matchMedia?.('(prefers-reduced-motion: reduce)');
    const update = () => setReduced(media ? media.matches : true);
    const visibility = () => setVisible(!document.hidden);
    media?.addEventListener?.('change', update);
    document.addEventListener('visibilitychange', visibility);
    update(); visibility();
    return () => { media?.removeEventListener?.('change', update); document.removeEventListener('visibilitychange', visibility); };
  }, []);
  return { reduced, visible };
}

function JourneyIllustration({ step, running }) {
  return <figure className="rj-scene" data-motion={running ? 'running' : 'paused'}>
    <svg viewBox="0 0 720 170" aria-hidden="true" focusable="false">
      <path className="rj-scene-connector" d="M194 83H294 M426 83H526" />
      <path className="rj-scene-arrow" d="m281 76 10 7-10 7m232-14 10 7-10 7" />
      {[34, 294, 526].map((x) => <rect className="rj-scene-card" key={x} x={x} y="25" width="160" height="116" rx="16" />)}
      <g className="rj-scene-ink"><path d="M90 45h34l15 15v55H90zM124 45v17h15M99 77h29M99 87h29M99 97h19" />
        <rect x="323" y="48" width="102" height="62" rx="6" /><path d="M323 64h102M333 79h42M333 91h61M361 110v10m-16 0h56" />
        <path d="M562 64h30l9 10h47v43h-86z" /><circle cx="619" cy="59" r="20" /><path d="m610 59 6 6 12-13" />
      </g>
      <g className="rj-moving-page"><rect x="209" y="68" width="18" height="25" rx="3" /><path d="M213 76h10m-10 6h10m-10 5h7" /></g>
      <text x="114" y="157" textAnchor="middle">Inputs</text><text x="374" y="157" textAnchor="middle">Your action</text><text x="606" y="157" textAnchor="middle">Handoff</text>
    </svg>
    <figcaption><strong>{step.title}</strong><span>Illustrated workflow, not live project activity.</span></figcaption>
  </figure>;
}

function WorkspaceMap({ modules }) {
  const groups = [...new Set(modules.map((module) => module.group || 'Other workspaces'))];
  return <details className="rj-workspace-map">
    <summary>Your available workspaces · {modules.length}</summary>
    <p className="rj-small">From the current authorized module registry. Opening a workspace does not grant every action inside it.</p>
    {groups.map((group) => <section key={group}><h3>{group}</h3><ul>
      {modules.filter((module) => (module.group || 'Other workspaces') === group).map((module) => <li key={module.route}>
        <a href={`#${module.route}`}><small>Module {module.moduleNumber || '—'}</small><strong>{module.displayName || module.route}</strong></a>
        {module.description && <p>{module.description}</p>}
      </li>)}
    </ul></section>)}
  </details>;
}

function RoleJourneyExperience({ context, roles }) {
  const [selectedCode, setSelectedCode] = useState('');
  const [query, setQuery] = useState('');
  const [stepIndex, setStepIndex] = useState(0);
  const [showExample, setShowExample] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [animate, setAnimate] = useState(true);
  const { reduced, visible } = useMotionPreference();
  const stepHeading = useRef(null);
  const focusPending = useRef(false);
  const selection = roles.find((role) => role.code === selectedCode) || roles[0];
  const filtered = roles.filter((role) => matchesJourney(role, query));
  const visibleIndex = Math.min(stepIndex, Math.max(0, (selection?.steps.length || 0) - 1));
  const step = selection?.steps[visibleIndex];
  const allowedModule = step && context.accessReady ? context.modules.find((module) => module.route === step.route) : null;
  const activePhases = new Set(selection?.steps.map((item) => item.stage) || []);
  const motionRunning = animate && !reduced && visible;

  useEffect(() => {
    if (focusPending.current) { stepHeading.current?.focus(); focusPending.current = false; }
  }, [visibleIndex, selection?.code]);
  useEffect(() => {
    if (reduced || !visible) setPlaying(false);
  }, [reduced, visible]);
  useEffect(() => {
    if (!playing || reduced || !visible || !step) return undefined;
    if (visibleIndex === selection.steps.length - 1) { setPlaying(false); return undefined; }
    const timer = window.setTimeout(() => { setStepIndex((index) => index + 1); setShowExample(false); }, 8000);
    return () => window.clearTimeout(timer);
  }, [playing, reduced, visible, visibleIndex, selection?.code, selection?.steps.length, step]);

  function selectStep(index) {
    setPlaying(false); setStepIndex(index); setShowExample(false);
    if (index === visibleIndex) stepHeading.current?.focus(); else focusPending.current = true;
  }
  function selectRole(code) {
    setPlaying(false); setSelectedCode(code); setStepIndex(0); setShowExample(false);
  }
  function toggleWalkthrough() {
    if (playing) { setPlaying(false); return; }
    if (visibleIndex === selection.steps.length - 1) setStepIndex(0);
    setShowExample(false); setPlaying(true);
  }

  return <section className="role-journeys rj-visual-experience" id="my-role-in-pulse" data-module="999" aria-labelledby="role-journeys-title">
    <header className="rj-hero"><div>
      <p className="rj-eyebrow">US Signal · Your role. Your workflow.</p>
      <h1 id="role-journeys-title">My Role in Pulse</h1>
      <p>See what you own, how to do it, and who needs your work next.</p>
      <nav className="rj-links" aria-label="Role journey navigation"><a href="#user-guide">System User Guide</a><a href="#dashboard">Back to dashboard</a></nav>
    </div><USSignalLogo size="large" /></header>
    <p className="rj-notice"><strong>Only your assigned stories.</strong> Learning only: illustrations and walkthrough progress do not change work, grant access, or represent live status.</p>
    {context.viewAsActive && <p className="rj-notice">View-As is active. Stories and workspace links follow the effective user, not the administrator. Operational write restrictions still apply.</p>}
    {!context.accessReady ? <div className="rj-empty" role="status"><h2>{context.state === 'unavailable' ? 'Role verification is unavailable' : 'Verifying your role and workspace access'}</h2><p>No stories or action links are shown until the current effective user is verified. Refresh the page or contact support if verification does not recover.</p></div>
      : roles.length === 0 ? <div className="rj-empty" role="status"><h2>No role story is assigned yet</h2><p>Ask your administrator to review your assignment. This page will not substitute another role’s story.</p></div>
      : <div className="rj-layout"><aside className="rj-selector" aria-label="Your assigned roles">
        <h2>1. Your role stories</h2><label htmlFor="rj-search">Find your role or task</label>
        <input id="rj-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search your stories" />
        <p className="rj-small">Only roles assigned to your effective user appear here. Multiple assignments stay separate.</p>
        {filtered.length ? <ul className="rj-role-list">{filtered.map((role) => <li key={role.code}><button type="button" aria-pressed={selection?.code === role.code} onClick={() => selectRole(role.code)}><span>{role.title}</span><small>Your role</small></button></li>)}</ul>
          : <div role="status" className="rj-empty"><p>No matching roles or tasks in your assignments.</p><button type="button" onClick={() => setQuery('')}>Clear search</button></div>}
        <p className="rj-small" aria-live="polite">{filtered.length} assigned playbooks shown</p>
        <div className="rj-at-a-glance"><strong>{selection?.steps.length || 0} visual steps</strong><span>Clear inputs, actions and handoffs</span></div>
      </aside><div className="rj-content">{selection && <>
        <section className="rj-overview" aria-labelledby="rj-role-title"><p className="rj-eyebrow">2. Understand your responsibility</p><h2 id="rj-role-title">{selection.title}</h2>
          {selection.steps.length ? <blockquote>{userStory(selection)}</blockquote> : <p role="status">This assigned role does not have a reviewed playbook yet. Ask your process owner to define its responsibilities; no other role’s authority is substituted.</p>}
          <dl className="rj-definition-grid"><div><dt>My responsibility</dt><dd>{selection.purpose}</dd></div><div><dt>My boundary</dt><dd>{selection.boundary}</dd></div></dl>
          {selection.incoming && <p><strong>My work starts with:</strong> {selection.incoming}</p>}
        </section>
        {step && <><section className="rj-lifecycle" aria-labelledby="rj-lifecycle-title"><h3 id="rj-lifecycle-title">Where your role fits</h3><p className="rj-small">These are workflow touchpoints, not completion indicators.</p>
          <ol>{LIFECYCLE.filter(([id]) => activePhases.has(id)).map(([id, title]) => <li key={id} data-involved="true"><span>{title}</span><small>Your role touchpoint</small></li>)}</ol>
        </section><section className="rj-workflow" aria-labelledby="rj-workflow-title">
          <p className="rj-eyebrow">3. Follow the visual steps</p><h2 id="rj-workflow-title">{selection.title} workflow</h2>
          <div className="rj-motion-controls"><button type="button" onClick={toggleWalkthrough} disabled={reduced || selection.steps.length < 2} aria-pressed={playing}>{playing ? 'Pause walkthrough' : 'Play walkthrough'}</button>
            <button type="button" onClick={() => setAnimate((value) => !value)} disabled={reduced} aria-pressed={animate && !reduced}>{motionRunning ? 'Pause animation' : 'Animate diagram'}</button>
            <span className="rj-small">{reduced ? 'Reduced motion is on. All steps remain available manually.' : 'Walkthrough advances every 8 seconds; pause at any time.'}</span></div>
          <JourneyIllustration step={step} running={motionRunning} />
          <ol className="rj-stepper" aria-label={`${selection.title} workflow steps`}>{selection.steps.map((item, index) => <li key={item.id}><button type="button" aria-current={index === visibleIndex ? 'step' : undefined} onClick={() => selectStep(index)} aria-controls="rj-step-details"><span className="rj-step-number" aria-hidden="true">{index + 1}</span><span><strong>{item.title}</strong><small>{LIFECYCLE.find(([id]) => id === item.stage)?.[1]}</small></span></button></li>)}</ol>
          <article id="rj-step-details" className="rj-step-details" aria-labelledby="rj-step-title">
            <p className="rj-eyebrow" role="status">Viewing step {visibleIndex + 1} of {selection.steps.length} · Learning only</p>
            <h3 id="rj-step-title" ref={stepHeading} tabIndex={-1}>{step.title}</h3><p className="rj-task-story">{userStory(selection, step)}</p>
            <div className="rj-io-flow" aria-label="Input, action and outcome"><section><span className="rj-flow-icon" aria-hidden="true">1</span><h4>What I need</h4><p>{step.input}</p></section><section><span className="rj-flow-icon" aria-hidden="true">2</span><h4>What I do</h4><p>{step.action}</p></section><section><span className="rj-flow-icon" aria-hidden="true">3</span><h4>What I produce</h4><p>{step.output}</p></section></div>
            <div className="rj-handoff"><div><h4>Ready when</h4><p>{step.acceptance}</p></div><div><h4>Who receives it next</h4><p><strong>{step.nextOwner}</strong></p></div></div>
            <aside className="rj-exception"><h4>Something missing or not working?</h4><p>{step.exception}</p></aside>
            <div className="rj-actions">{allowedModule ? <a className="rj-primary" href={`#${allowedModule.route}`}>Open {allowedModule.displayName || 'workspace'} <span aria-hidden="true">↗</span></a> : <p className="rj-access-message">This step’s workspace is not available in your current access. Ask the authorized owner for help; this story does not grant access.</p>}
              <button type="button" aria-expanded={showExample} aria-controls="rj-example" onClick={() => { setPlaying(false); setShowExample((value) => !value); }}>{showExample ? 'Hide example' : 'Show a simple example'}</button></div>
            {showExample && <div className="rj-example" id="rj-example"><h4>Practice walkthrough · Fictional example</h4><p>A sample engagement reaches <strong>{step.title.toLowerCase()}</strong>. No real project is loaded or changed.</p><ol><li>Check the starting information: {step.input}</li><li>Walk through the action: {step.action}</li><li>Compare the result with: {step.acceptance}</li><li>Explain the handoff to {step.nextOwner}.</li></ol><p><strong>Exception exercise:</strong> {step.exception}</p></div>}
            <div className="rj-step-navigation"><button type="button" disabled={visibleIndex === 0} onClick={() => selectStep(visibleIndex - 1)}>← Previous step</button><span>Step {visibleIndex + 1} / {selection.steps.length}</span><button type="button" disabled={visibleIndex === selection.steps.length - 1} onClick={() => selectStep(visibleIndex + 1)}>Next step →</button></div>
          </article>
        </section></>}
        <WorkspaceMap modules={context.modules} />
        <details className="rj-maintenance"><summary>About this playbook and its review status</summary><p><strong>{JOURNEY_REVIEW}</strong> · Experience version {EXPERIENCE_VERSION} · Updated September 22, 2026.</p><p>Stories use the existing role guidance, module registry and reviewed source paths. Process owners must approve operating sequences. A registered capability is not proof of deployed availability or completed UAT.</p><p>This page does not fetch business records, invoke AI, save learning progress or change permissions. Unknown assigned roles show a guidance gap. Use the existing Help workflow to report a missing step.</p></details>
      </>}</div></div>}
  </section>;
}

export function RoleJourneysPage({ context, guidance = ROLE_GUIDANCE }) {
  const roles = useMemo(() => assignedRolePlaybooks(guidance, context), [guidance, context.accessReady, context.roleCodes]);
  // Remount learning state synchronously when effective identity, assignments or
  // access changes. An old role, expanded example or autoplay never flashes back.
  const scopeKey = JSON.stringify([context.scopeRevision, context.accessReady, context.viewAsActive, roles.map((role) => role.code), context.modules.map((module) => module.route)]);
  return <RoleJourneyExperience key={scopeKey} context={context} roles={roles} />;
}
export default function MyRoleInPulse() {
  return <RoleJourneysPage context={useRoleJourneyContext()} />;
}

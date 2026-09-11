import { useEffect, useMemo, useRef, useState } from 'react';
import USSignalLogo from '../enterprise/USSignalLogo.jsx';
import { ROLE_GUIDANCE } from '../role-permission-model.js';
import useRoleJourneyContext from './use-role-journey-context.js';
import { JOURNEY_REVIEW, JOURNEY_VERSION, LIFECYCLE, matchesJourney, roleCatalog, userStory } from './role-journeys.js';
import './role-journeys.css';

export function RoleJourneysPage({ context, guidance = ROLE_GUIDANCE }) {
  const roles = useMemo(() => roleCatalog(guidance, context.roleCodes), [guidance, context.roleCodes]);
  const [selectedCode, setSelectedCode] = useState('');
  const [query, setQuery] = useState('');
  const [stepIndex, setStepIndex] = useState(0);
  const [showExample, setShowExample] = useState(false);
  const stepHeading = useRef(null);
  const focusPending = useRef(false);
  const selection = roles.find((role) => role.code === selectedCode) || roles[0];
  const filtered = roles.filter((role) => matchesJourney(role, query));
  const step = selection?.steps[Math.min(stepIndex, Math.max(0, selection.steps.length - 1))];
  const visibleIndex = step ? selection.steps.indexOf(step) : 0;
  const allowedModule = step && context.accessReady
    ? context.modules.find((module) => module.route === step.route) : null;
  const activePhases = new Set(selection?.steps.map((item) => item.stage) || []);

  useEffect(() => {
    setStepIndex(0);
    setShowExample(false);
  }, [selection?.code]);
  useEffect(() => {
    if (focusPending.current) {
      stepHeading.current?.focus();
      focusPending.current = false;
    }
  }, [visibleIndex, selection?.code]);

  function selectStep(index) {
    focusPending.current = true;
    setStepIndex(index);
    setShowExample(false);
    if (index === visibleIndex) {
      stepHeading.current?.focus();
      focusPending.current = false;
    }
  }
  function selectRole(code) {
    setSelectedCode(code);
    setStepIndex(0);
    setShowExample(false);
  }

  return (
    <section className="role-journeys" id="my-role-in-pulse" data-module="999" aria-labelledby="role-journeys-title">
      <header className="rj-hero">
        <div>
          <p className="rj-eyebrow">US Signal · Role journeys & playbooks</p>
          <h1 id="role-journeys-title">My Role in Pulse</h1>
          <p>Know where to start, what to do, and who needs your work next.</p>
          <nav className="rj-links" aria-label="Role journey navigation">
            <a href="#user-guide">System User Guide</a>
            <a href="#dashboard">Back to dashboard</a>
          </nav>
        </div>
        <USSignalLogo size="large" />
      </header>

      <p className="rj-notice"><strong>Learn the workflow.</strong> These are draft operating playbooks, not live project status. Reading or stepping through them does not complete work or change your access.</p>
      {context.viewAsActive && <p className="rj-notice">View-As is active. Workspace links follow the effective user’s access; this guide does not authorize changes.</p>}

      <div className="rj-layout">
        <aside className="rj-selector" aria-label="Choose a role">
          <h2>1. Choose a role</h2>
          <label htmlFor="rj-search">Find a role or task</label>
          <input id="rj-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Try engineer, handoff or time" />
          <p className="rj-small">Your assigned roles appear first. Exploring a role is not impersonation.</p>
          {filtered.length ? (
            <ul className="rj-role-list">
              {filtered.map((role) => <li key={role.code}>
                <button type="button" aria-pressed={selection?.code === role.code} onClick={() => selectRole(role.code)}>
                  <span>{role.title}</span>{role.assigned && <small>Your role</small>}
                </button>
              </li>)}
            </ul>
          ) : <div role="status" className="rj-empty"><p>No matching roles or tasks.</p><button type="button" onClick={() => setQuery('')}>Clear search</button></div>}
          <p className="rj-small" aria-live="polite">{filtered.length} role playbooks shown</p>
        </aside>

        <div className="rj-content">
          {selection && <>
            <section className="rj-overview" aria-labelledby="rj-role-title">
              <p className="rj-eyebrow">2. Understand your responsibility</p>
              <h2 id="rj-role-title">{selection.title}</h2>
              {selection.steps.length ? <blockquote>{userStory(selection)}</blockquote> : <p>Detailed stories for this role have not been reviewed yet. Ask your process owner to define responsibilities and handoffs before relying on a workflow.</p>}
              <dl className="rj-definition-grid">
                <div><dt>My responsibility</dt><dd>{selection.purpose}</dd></div>
                <div><dt>My boundary</dt><dd>{selection.boundary}</dd></div>
              </dl>
              {selection.incoming && <p><strong>My work starts with:</strong> {selection.incoming}</p>}
            </section>

            <section className="rj-lifecycle" aria-labelledby="rj-lifecycle-title">
              <h3 id="rj-lifecycle-title">Where this role fits</h3>
              <p className="rj-small">Highlighted stages show this playbook’s touchpoints. Time, communication, approvals and risk review continue throughout delivery.</p>
              <ol>{LIFECYCLE.map(([id, title]) => <li key={id} data-involved={activePhases.has(id)}>
                <span>{title}</span>{activePhases.has(id) && <small>Role touchpoint</small>}
              </li>)}</ol>
            </section>

            {step && <section className="rj-workflow" aria-labelledby="rj-workflow-title">
              <p className="rj-eyebrow">3. Follow the visual steps</p>
              <h2 id="rj-workflow-title">{selection.title} workflow</h2>
              <p>Choose any step to see exactly what it needs and what it produces.</p>
              <ol className="rj-stepper" aria-label={`${selection.title} workflow steps`}>
                {selection.steps.map((item, index) => <li key={item.id}>
                  <button type="button" aria-current={index === visibleIndex ? 'step' : undefined} onClick={() => selectStep(index)} aria-controls="rj-step-details">
                    <span className="rj-step-number" aria-hidden="true">{index + 1}</span>
                    <span><strong>{item.title}</strong><small>{LIFECYCLE.find(([id]) => id === item.stage)?.[1]}</small></span>
                  </button>
                </li>)}
              </ol>

              <article id="rj-step-details" className="rj-step-details" aria-labelledby="rj-step-title">
                <p className="rj-eyebrow" role="status">Viewing step {visibleIndex + 1} of {selection.steps.length} · Learning only</p>
                <h3 id="rj-step-title" ref={stepHeading} tabIndex={-1}>{step.title}</h3>
                <p className="rj-task-story">{userStory(selection, step)}</p>
                <div className="rj-io-flow" aria-label="Input, action and outcome">
                  <section><span className="rj-flow-icon" aria-hidden="true">1</span><h4>What I need</h4><p>{step.input}</p></section>
                  <section><span className="rj-flow-icon" aria-hidden="true">2</span><h4>What I do</h4><p>{step.action}</p></section>
                  <section><span className="rj-flow-icon" aria-hidden="true">3</span><h4>What I produce</h4><p>{step.output}</p></section>
                </div>
                <div className="rj-handoff">
                  <div><h4>Ready when</h4><p>{step.acceptance}</p></div>
                  <div><h4>Who receives it next</h4><p><strong>{step.nextOwner}</strong></p></div>
                </div>
                <aside className="rj-exception"><h4>Something missing or not working?</h4><p>{step.exception}</p></aside>
                <div className="rj-actions">
                  {allowedModule ? <a className="rj-primary" href={`#${allowedModule.route}`}>Open {allowedModule.displayName || 'workspace'} <span aria-hidden="true">↗</span></a>
                    : <p className="rj-access-message">{context.accessReady ? 'This workspace is not available in your current access. You can still read and learn this step.' : 'Workspace access is being verified. You can read this guide now; action links appear only after access is confirmed.'}</p>}
                  <button type="button" aria-expanded={showExample} aria-controls="rj-example" onClick={() => setShowExample((current) => !current)}>{showExample ? 'Hide example' : 'Show a simple example'}</button>
                </div>
                {showExample && <div className="rj-example" id="rj-example">
                  <h4>Practice walkthrough · Fictional example</h4>
                  <p>A sample engagement reaches <strong>{step.title.toLowerCase()}</strong>. No real project is loaded or changed.</p>
                  <ol><li>Check the starting information: {step.input}</li><li>Walk through the action: {step.action}</li><li>Compare the result with: {step.acceptance}</li><li>Explain the handoff to {step.nextOwner}.</li></ol>
                  <p><strong>Exception exercise:</strong> The required information is missing. {step.exception}</p>
                </div>}
                <div className="rj-step-navigation">
                  <button type="button" disabled={visibleIndex === 0} onClick={() => selectStep(visibleIndex - 1)}>← Previous step</button>
                  <span>Step {visibleIndex + 1} / {selection.steps.length}</span>
                  <button type="button" disabled={visibleIndex === selection.steps.length - 1} onClick={() => selectStep(visibleIndex + 1)}>Next step →</button>
                </div>
              </article>
            </section>}
            <details className="rj-maintenance">
              <summary>About this playbook and its review status</summary>
              <p><strong>{JOURNEY_REVIEW}</strong> · Content version {JOURNEY_VERSION} · Prepared September 9, 2026.</p>
              <p>Maintained from the existing role guidance and module registry, with proposed operating handoffs. Process owners must approve the operating sequence. A registered module or documented workflow is not proof of live availability, customer acceptance or completed UAT.</p>
              <p>New or unrecognized assigned roles appear with an explicit guidance gap rather than inheriting another role’s authority. This page does not fetch business records, call AI or save progress.</p>
              <p>For a missing step, ask your process owner to review the playbook. For an application issue, use the existing Help and support workflow.</p>
            </details>
          </>}
        </div>
      </div>
    </section>
  );
}

export default function MyRoleInPulse() {
  return <RoleJourneysPage context={useRoleJourneyContext()} />;
}

import { useState } from 'react';
import ProjectIntakeCenter from './ProjectIntakeCenter.jsx';
import PostIntakeAgingPanel from './PostIntakeAgingPanel.jsx';
import IntakeWorkTaskHandoffPanel from './IntakeWorkTaskHandoffPanel.jsx';
import ResourceAssignmentHandoffPanel from './ResourceAssignmentHandoffPanel.jsx';
import './work-intake-creation-center.css';

const views = [
  { id: 'intake', label: 'Intake requests', helper: 'Capture and validate the upstream sales or delivery request.' },
  { id: 'aging', label: 'Aging & follow-up', helper: 'Find requests stalled before creation and resolve ownership gaps.' },
  { id: 'tasks', label: 'Work-task handoff', helper: 'Prepare approved intake detail for the canonical project work register.' },
  { id: 'resources', label: 'Resource handoff', helper: 'Review role demand before assignment and scheduling.' }
];

export default function WorkIntakeCreationCenter() {
  const [view, setView] = useState('intake');
  const active = views.find((item) => item.id === view) ?? views[0];

  return (
    <section className="work-intake-creation-center" data-module="020">
      <header className="work-intake-hero">
        <div>
          <p className="eyebrow">Module 020 · Work Intake &amp; Creation</p>
          <h1>Turn a governed intake into delivery-ready work</h1>
          <p>Module 020 owns the upstream request, validation, aging, and handoff. Module 055D creates the canonical project; Module 055C manages it after creation.</p>
        </div>
        <a className="primary-action" href="#create-work-register">Create approved project in 055D</a>
      </header>

      <div className="work-intake-boundary" role="note">
        <strong>Why this module remains required</strong>
        <span>It preserves the original opportunity/request evidence and separates intake approval from project creation. It does not duplicate the 055C/055D work register.</span>
      </div>

      <nav className="work-intake-tabs" aria-label="Work intake views">
        {views.map((item) => <button type="button" key={item.id} className={view === item.id ? 'is-active' : ''} aria-current={view === item.id ? 'page' : undefined} onClick={() => setView(item.id)}><strong>{item.label}</strong><span>{item.helper}</span></button>)}
      </nav>

      <div className="work-intake-active-view" aria-label={active.label}>
        {view === 'intake' ? <ProjectIntakeCenter /> : null}
        {view === 'aging' ? <PostIntakeAgingPanel /> : null}
        {view === 'tasks' ? <IntakeWorkTaskHandoffPanel /> : null}
        {view === 'resources' ? <ResourceAssignmentHandoffPanel /> : null}
      </div>
    </section>
  );
}

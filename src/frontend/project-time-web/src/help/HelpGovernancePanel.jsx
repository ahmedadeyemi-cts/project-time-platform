import { useEffect, useState } from 'react';
import {
  DEFAULT_HELP_ANSWER_PREFERENCES,
  HELP_DETAIL_LEVELS,
  loadHelpAnswerPreferences,
  saveHelpAnswerPreferences
} from './help-answer-preferences.js';
import './help-governance.css';

const GOVERNED_HELP_HIERARCHY = Object.freeze([
  Object.freeze({ order: 1, source: 'System User Guide', detail: 'Module 999 procedures, role guidance, troubleshooting, workflows, glossary, setup, and reporting instructions.' }),
  Object.freeze({ order: 2, source: 'Module descriptions and API metadata', detail: 'Current registered module purpose, route, permissions, source APIs, and supported actions.' }),
  Object.freeze({ order: 3, source: 'Repository documentation', detail: 'Approved implementation, architecture, runbook, and governance documentation.' }),
  Object.freeze({ order: 4, source: 'Permission-aware AI repository search', detail: 'AI-assisted retrieval limited to evidence the effective user is authorized to access.' }),
  Object.freeze({ order: 5, source: 'Escalation or issue creation', detail: 'Use when no verified answer exists; do not replace missing evidence with a confident answer.' })
]);

function updatePreference(current, key, value) {
  const next = { ...current, [key]: value };
  saveHelpAnswerPreferences(next);
  return next;
}

export function HelpAnswerPreferenceControls({ compact = false }) {
  const [preferences, setPreferences] = useState(DEFAULT_HELP_ANSWER_PREFERENCES);

  useEffect(() => {
    setPreferences(loadHelpAnswerPreferences());
    const update = (event) => setPreferences(event.detail ?? loadHelpAnswerPreferences());
    window.addEventListener('projectpulse:help-answer-preferences-changed', update);
    return () => window.removeEventListener('projectpulse:help-answer-preferences-changed', update);
  }, []);

  return (
    <section className={`group7-help-preferences ${compact ? 'is-compact' : ''}`} aria-label="Saved Help answer preferences">
      <div className="group7-help-preferences__heading">
        <div>
          <strong>Answer detail preference</strong>
          <span>Saved for this signed-in browser identity. An individual question can override it.</span>
        </div>
        <select
          aria-label="Default answer detail"
          value={preferences.detailLevel}
          onChange={(event) => setPreferences((current) => updatePreference(current, 'detailLevel', event.target.value))}
        >
          {HELP_DETAIL_LEVELS.map((choice) => <option value={choice.value} key={choice.value}>{choice.label}</option>)}
        </select>
      </div>
      <div className="group7-help-preferences__checks">
        <label><input type="checkbox" checked={preferences.includeRepositoryContext} onChange={(event) => setPreferences((current) => updatePreference(current, 'includeRepositoryContext', event.target.checked))} />Include repository context</label>
        <label><input type="checkbox" checked={preferences.includeAssumptions} onChange={(event) => setPreferences((current) => updatePreference(current, 'includeAssumptions', event.target.checked))} />Include assumptions</label>
        <label><input type="checkbox" checked={preferences.includeSourceCitations} onChange={(event) => setPreferences((current) => updatePreference(current, 'includeSourceCitations', event.target.checked))} />Include source citations</label>
      </div>
      <small>Query overrides: /concise, /detailed, /highly-detailed, /technical, /executive, or /step-by-step.</small>
    </section>
  );
}

export default function HelpGovernancePanel({ compact = true }) {
  return (
    <section className={`group7-help-governance ${compact ? 'is-compact' : ''}`} data-group7-help-hierarchy="governed">
      <div className="group7-help-governance__heading">
        <div>
          <strong>Governed answer hierarchy</strong>
          <span>Pulse AI should use the highest verified source available and identify when evidence is insufficient.</span>
        </div>
        <a href="#user-guide">Open System User Guide</a>
      </div>
      <ol>
        {GOVERNED_HELP_HIERARCHY.map((tier) => (
          <li key={tier.order}>
            <span>{tier.order}</span>
            <div><strong>{tier.source}</strong><small>{tier.detail}</small></div>
          </li>
        ))}
      </ol>
      <HelpAnswerPreferenceControls compact />
      <div className="group7-help-governance__actions">
        <a href="#defect-tracker?reportType=issue">Report an Issue</a>
        <a href="#defect-tracker?reportType=feature">Feature Request</a>
      </div>
    </section>
  );
}

export function SystemUserGuideGovernancePanel() {
  return (
    <section className="group7-system-guide-overview" data-group7-system-guide="authoritative">
      <div>
        <p>Authoritative internal help source</p>
        <h2>System User Guide</h2>
        <span>
          Covers every active module, role access, common tasks, screenshots or evidence references where maintained,
          expected outcomes, troubleshooting, errors, support references, workflows, glossary, integration setup, and reporting instructions.
        </span>
      </div>
      <div className="group7-system-guide-overview__actions">
        <a href="#defect-tracker?reportType=issue">Report an Issue</a>
        <a href="#defect-tracker?reportType=feature">Request a Feature</a>
      </div>
    </section>
  );
}

export { GOVERNED_HELP_HIERARCHY };

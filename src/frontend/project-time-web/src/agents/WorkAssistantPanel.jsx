import React, { useEffect, useRef, useState } from 'react';
import { availableAgentCapabilities, visibleAgentSnapshot, agentStatusLabel } from './agent-workspace-state.js';
import './work-assistant.css';

// Reusable presentation component only. Deliberately not mounted in any live
// workspace by this PR. Hosts must supply current server-authorized context,
// persist runs, and reauthorize API actions; props are not a security boundary.
export default function WorkAssistantPanel({ contextKey, snapshot, onRequestProposal }) {
  const current = visibleAgentSnapshot(contextKey, snapshot);
  const capabilities = availableAgentCapabilities(contextKey, snapshot);
  const [form, setForm] = useState({ key: contextKey, goal: '', busy: false, error: '' });
  const request = useRef(null);
  const activeContext = useRef(contextKey);
  activeContext.current = contextKey;
  useEffect(() => {
    request.current?.abort();
    setForm({ key: contextKey, goal: '', busy: false, error: '' });
    return () => request.current?.abort();
  }, [contextKey]);
  useEffect(() => { if (!current) request.current?.abort(); }, [current]);
  if (!current) return null;
  const value = form.key === contextKey ? form : { goal: '', busy: false, error: '' };
  const run = current.run?.contextKey === contextKey ? current.run : null;

  async function propose(capability) {
    if (!onRequestProposal || value.busy || !value.goal.trim() || value.goal.length > 4000) return;
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    const key = contextKey;
    setForm(previous => ({ ...previous, key, busy: true, error: '' }));
    try {
      // Do not add actor IDs, recipient IDs, roles, provider names or arbitrary
      // URLs. The host resolves the opaque context against the current session.
      await onRequestProposal({ capability, goal: value.goal.trim(), contextKey: key }, controller.signal);
    } catch {
      if (!controller.signal.aborted && activeContext.current === key)
        setForm(previous => ({ ...previous, error: 'The request was not confirmed. Refresh its status before retrying.' }));
    } finally {
      if (!controller.signal.aborted && activeContext.current === key)
        setForm(previous => ({ ...previous, busy: false }));
    }
  }

  return <section className="celar-work-assistant" aria-label="Celar work assistant">
    <h2>Celar work assistant</h2>
    <p>Prepare evidence-backed proposals for your authorized work. No staffing, time, billing, or project approval is implied.</p>
    <label>What needs your attention?
      <textarea maxLength={4000} value={value.goal} disabled={value.busy || capabilities.length === 0}
        onChange={event => setForm(previous => ({ ...previous, key: contextKey, goal: event.target.value }))} />
    </label>
    <div className="celar-agent-actions">
      {capabilities.map(item => <button key={item.code} type="button"
        disabled={!onRequestProposal || value.busy || !value.goal.trim() || run?.status === 'Running'}
        onClick={() => propose(item.code)}>{item.name}</button>)}
    </div>
    {capabilities.length === 0 && <p>No qualified agent action is enabled for this context.</p>}
    <p role="status" aria-live="polite">{value.busy ? 'Submitting a proposal request…' : run ? agentStatusLabel(run.status) : 'No active run'}</p>
    {value.error && <p role="alert">{value.error}</p>}
    {run?.note && <p className="celar-agent-proposal">{run.note}</p>}
    {run?.provider && <p>Provider reported by Module 064: {run.provider}</p>}
    {run?.handoff && <p>Handoff proposal prepared. It has not been delivered or approved.</p>}
  </section>;
}

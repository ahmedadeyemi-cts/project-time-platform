import { useEffect, useState } from 'react';
import { operationDuration } from './operation-progress.js';
import './ai-operation-progress.css';

export const planningPhases = ['Plan', 'Design', 'Implement', 'Validate', 'Release'];

export default function AiPhaseProgress({ phases, terminal = false, completedAt }) {
  const [clock, setClock] = useState(Date.now());
  const rows = planningPhases.map((name, index) => phases?.find(p => p.name === name)
    || { name, number: index + 1, status: 'pending' });
  const running = !terminal && rows.some(p => ['processing', 'retrying'].includes(p.status));
  useEffect(() => {
    setClock(Date.now());
    if (!running) return undefined;
    const tick = window.setInterval(() => setClock(Date.now()), 1000);
    return () => window.clearInterval(tick);
  }, [running, phases]);
  const finished = rows.filter(p => p.status === 'completed').length;
  const active = rows.find(p => ['processing', 'retrying', 'needs_attention'].includes(p.status));
  return <section className="ai-phase-progress" aria-label="Five-stage WBS generation">
    <p role="status">{terminal ? `${finished} of 5 stages completed` : active
      ? `Stage ${active.number} of 5: ${active.name}` : finished === 5
        ? 'All five stages generated. Checking the WBS and schedule.' : 'Five stages queued after document preparation'}</p>
    <ol>{rows.map(row => {
      const status = terminal && row.status === 'processing' ? 'stopped' : row.status;
      const label = { retrying: 'Waiting for provider retry', pending: 'Not started', processing: 'Processing', completed: 'Complete', needs_attention: 'Needs attention', stopped: 'Stopped' }[status] || status;
      return <li key={row.name} className={`ai-phase-${status}`} aria-current={status === 'processing' ? 'step' : undefined}>
        <strong>{row.number} of 5 · {row.name}</strong><span>{label}</span>
        {row.startedAt ? <span role="timer" aria-live="off" aria-label={`${row.name} elapsed time`}>
          {operationDuration(row.startedAt, row.completedAt || (terminal ? completedAt : null) || clock)} elapsed
        </span> : <span>—</span>}
        {row.taskCount > 0 && <small>{row.taskCount} WBS tasks validated</small>}
      </li>;
    })}</ol>
    <p>Each stage uses the same prepared project scope. The complete WBS is checked for dependencies and schedule before it becomes a review draft.</p>
  </section>;
}

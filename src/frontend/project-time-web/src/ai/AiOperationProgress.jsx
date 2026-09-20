import { useEffect, useState } from 'react';
import { operationDuration } from './operation-progress.js';
import './ai-operation-progress.css';

export default function AiOperationProgress({ title, startedAt, completedAt, active, stage, message, progress }) {
  const [clock, setClock] = useState(Date.now());
  useEffect(() => {
    setClock(Date.now());
    if (!active) return undefined;
    const timer = window.setInterval(() => setClock(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [active, startedAt]);
  return <section className="ai-operation-progress" aria-label={`${title} progress`}>
    <div role="status"><strong>{title}</strong><span>{stage || (active ? 'Processing' : 'Finished')}</span></div>
    <span role="timer" aria-live="off" aria-label={`${title} elapsed time`}>{operationDuration(startedAt, completedAt || clock)} elapsed</span>
    {active && <span className="ai-operation-progress-indicator" aria-hidden="true" />}
    {Number.isFinite(progress) && <progress max="100" value={Math.min(100, Math.max(0, progress))} aria-label={`${title} progress reported by server`} />}
    {message && <p>{message}</p>}
  </section>;
}

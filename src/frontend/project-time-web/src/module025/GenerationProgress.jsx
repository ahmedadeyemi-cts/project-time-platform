import { generationDuration, generationPhases, generationIsActive } from './generation-progress.js';
import { formatGenerationFailure } from './generation-feedback.js';
import './generation-progress.css';

const labels = { pending: 'Waiting', running: 'Generating', retrying: 'Retrying phase', completed: 'Saved', resumed: 'Saved phase reused', failed: 'Needs attention', interrupted: 'Interrupted' };
export default function GenerationProgress({ monitor, now, canGenerate, dirty, onResume }) {
  const { payload, receivedAt, observing, checking, error } = monitor;
  const hasJob = Boolean(payload?.generationId);
  const active = generationIsActive(payload);
  const phases = generationPhases(payload, receivedAt, now, observing);
  const current = phases.find(item => ['running', 'retrying'].includes(item.status));
  const stage = payload?.stage || payload?.phase;
  const provider = { celar_ai: 'Celar AI', deepseek_v4: 'DeepSeek', claude: 'Claude', openai: 'OpenAI', gemini: 'Gemini', copilot_studio: 'Microsoft Copilot Studio' }[payload?.currentProvider];
  const previousCompleted = stage === 'obsolete' && payload?.previousGenerationCompleted === true;
  const headline = checking ? 'Checking saved generation status…'
    : error ? 'Generation status needs attention'
      : previousCompleted ? 'Previous generation'
        : stage === 'assembly' ? 'Preparing SOW and GSD'
        : payload?.status === 'module025_detailed_scope_generated' ? 'SOW and GSD draft ready for review'
          : active && current ? `${current.phase} · ${current.ordinal} of 5`
            : active ? stage === 'queued' ? 'Waiting to start' : 'Preparing the shared scope'
              : hasJob ? 'Generation needs attention' : 'One scope. Five connected phases.';
  return <section className="m025-generation-progress" aria-label="AI generation progress" aria-busy={active && !error}>
    <div className="m025-generation-progress__heading" role="status" aria-live="polite"><strong>{headline}</strong></div>
    <p>Enter the project scope once in Service Scope. AI uses it to generate Plan, Design, Implement, Validate and Release in order, then prepares the SOW and detailed GSD for your review.</p>
    {active && provider ? <p>Current provider: <strong>{provider}</strong></p> : null}
    <ol className="m025-generation-phases">
      {phases.map(item => <li key={item.phase} className={`m025-generation-phase is-${item.status}`} aria-current={['running', 'retrying'].includes(item.status) ? 'step' : undefined}>
        <span className="m025-generation-phase__number">{item.ordinal} of 5</span><strong>{item.phase}</strong>
        <span>{item.resumed ? 'Saved phase reused' : labels[item.status] || 'Waiting'}</span>
        {item.seconds !== null && !item.resumed ? <span className="m025-generation-phase__timer" role="timer" aria-label={`${item.phase} elapsed time`}>{generationDuration(item.seconds)}</span> : null}
      </li>)}
    </ol>
    {previousCompleted ? <p>This generation completed successfully. The saved record has later changes; review its current scope and hours before confirmation.</p> : null}
    {active ? <p>{stage === 'assembly' ? 'All five phases are saved. Preparing the shared SOW and GSD draft.' : 'Each completed phase is saved automatically. Your previous document scope remains visible until the full draft is ready.'}</p> : null}
    {error ? <div role="alert"><p>{error} Your saved work is preserved. Rechecking status does not start generation.</p><button type="button" className="m025-button" onClick={monitor.recheck}>Check generation status</button></div> : null}
    {!error && !active && hasJob && !previousCompleted && payload?.status !== 'module025_detailed_scope_generated' ? <div className="m025-generation-recovery">
      <p>{formatGenerationFailure(payload)}</p>
      {payload.canResume || payload.canRetry ? <><button type="button" className="m025-button m025-button--primary" disabled={!canGenerate || dirty} onClick={onResume}>{payload.canResume ? 'Resume remaining phases' : 'Retry generation'}</button><p>{dirty ? 'Save your changes first. Updated scope may require generating all five phases again.' : payload.canResume ? 'The saved scope is unchanged. Reuse its completed phases and continue the remaining work.' : 'Start another attempt using this saved scope.'}</p></> : <p>{payload?.stage === 'obsolete' ? 'The saved scope changed. Generate a new draft using the current Service Scope.' : 'Review the current Service Scope and generation requirements before starting a new attempt.'}</p>}
    </div> : null}
  </section>;
}

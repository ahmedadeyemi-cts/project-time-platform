import { useEffect, useRef, useState } from 'react';
import './project-flowhive-planner-review.css';

const REVIEW_CONTRACT = 'flowhive-reviewed-regeneration-v1';
const isRowVersion = value => typeof value === 'string' && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);

/** A separate persisted AI proposal. Never replaces the displayed working plan on load. */
export default function ProjectFlowHivePlannerReview({ projectId, runId, getJson, postJson, canEdit,
  hasLocalEdits, onApplied, onRegenerate }) {
  const [review, setReview] = useState(null);
  const [choices, setChoices] = useState({});
  const [note, setNote] = useState('');
  const [preview, setPreview] = useState(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState('loading');
  const [error, setError] = useState('');
  const [uncertain, setUncertain] = useState(false);
  const [reload, setReload] = useState(0);
  const generation = useRef(0);
  const base = `/api/project-flowhive/projects/${projectId}/ai-planner/runs/${runId}`;
  useEffect(() => {
    const controller = new AbortController();
    const epoch = ++generation.current;
    setReview(null); setChoices({}); setNote(''); setPreview(null); setAcknowledged(false); setUncertain(false);
    setBusy('loading'); setError('');
    getJson(`${base}/review`, controller.signal).then(result => {
      if (epoch !== generation.current || controller.signal.aborted) return;
      if (result?.projectId !== projectId || result?.runId !== runId || result.contract !== REVIEW_CONTRACT ||
          result.currentPlan?.projectId !== projectId || result.candidatePlan?.projectId !== projectId ||
          !result.candidateSchedule?.valid || !result.candidateValidation?.valid ||
          !(result.expectedWorkingRowVersion === null || isRowVersion(result.expectedWorkingRowVersion)))
        throw new Error('The review response does not match this project and operation.');
      setReview(result); setBusy('');
    }).catch(failure => {
      if (epoch === generation.current && !controller.signal.aborted) { setError(failure.message); setBusy(''); }
    });
    return () => { ++generation.current; controller.abort(); };
  }, [projectId, runId, base, getJson, reload]);
  const existing = (review?.currentPlan?.tasks || []).filter(task => !task.isSummary);
  const proposed = (review?.candidatePlan?.tasks || []).filter(task => !task.isSummary);
  const ready = review && existing.every(task => Object.hasOwn(choices, task.wbsNumber)) && note.trim().length >= 10;
  const invalidatesPreview = () => { setPreview(null); setAcknowledged(false); setError(''); };
  const request = () => ({ expectedWorkingRowVersion: review.expectedWorkingRowVersion,
    decisions: existing.map(task => ({ existingWbs: task.wbsNumber, candidateWbs: choices[task.wbsNumber] || null })),
    reviewNote: note.trim(), previewFingerprint: preview?.previewFingerprint });
  async function previewMerge() {
    if (!ready || busy || uncertain || hasLocalEdits) return;
    const epoch = generation.current;
    setBusy('preview'); setError(''); setAcknowledged(false);
    try {
      const result = await postJson(`${base}/review-preview`, request());
      if (epoch !== generation.current) return;
      if (result?.projectId !== projectId || result?.runId !== runId || result.plan?.projectId !== projectId ||
          !/^[0-9a-f]{64}$/.test(result.previewFingerprint || '') || !result.validation?.valid || !result.schedule?.valid ||
          result.expectedWorkingRowVersion !== review.expectedWorkingRowVersion ||
          !['mappedTaskCount', 'retainedTaskCount', 'preservedMilestoneCount', 'previousPlannedHours', 'plannedHours']
            .every(key => Number.isFinite(result.reviewSummary?.[key]) && result.reviewSummary[key] >= 0))
        throw new Error('The server did not return a valid, project-matched merge preview.');
      setPreview(result);
    } catch (failure) { if (epoch === generation.current) { setPreview(null); setError(failure.message); } }
    finally { if (epoch === generation.current) setBusy(''); }
  }
  async function applyMerge() {
    if (!preview || !acknowledged || busy || uncertain || !canEdit || hasLocalEdits) return;
    const epoch = generation.current;
    setBusy('apply'); setError('');
    try {
      // One write only. An ambiguous response is observed, never blindly retried.
      const result = await postJson(`${base}/apply-reviewed`, request());
      if (epoch !== generation.current) return;
      if (result?.projectId !== projectId || result?.runId !== runId || result.plan?.projectId !== projectId ||
          result.terminal !== true || result.phase !== 'working_draft_ready' ||
          !['completed', 'completed_with_schedule_overrun'].includes(result.status) || !result.workingDraft?.persisted ||
          !isRowVersion(result.workingDraft?.rowVersion) || !Number.isInteger(result.workingDraft?.workingRevision) || result.workingDraft.workingRevision < 1)
        throw new Error('The apply response did not include the exact saved working-copy receipt.');
      await onApplied(result);
    } catch (failure) {
      if (epoch === generation.current) {
        if (failure.status === 409) { setPreview(null); setAcknowledged(false); }
        setUncertain(!Number.isInteger(failure.status) || failure.status >= 500);
        setError(`${failure.message}${!Number.isInteger(failure.status) || failure.status >= 500 ? ' The save outcome must be checked with Load working copy; this action will not automatically retry.' : ''}`);
      }
    } finally { if (epoch === generation.current) setBusy(''); }
  }
  const display = preview?.plan || review?.candidatePlan;
  const schedule = preview?.schedule || review?.candidateSchedule;
  return <section className="flowhive-planner-review" aria-label="Review generated work breakdown">
    <header><div><h3>AI Planner work breakdown — {preview ? 'reviewed merge preview' : 'saved proposal'}</h3>
      <p>The AI proposal is stored separately. Your working tasks, milestones and assignments remain unchanged until you preview and apply a merge.</p></div>
      {onRegenerate ? <button type="button" onClick={onRegenerate} disabled={Boolean(busy) || !canEdit || uncertain}>Generate another proposal</button> : null}</header>
    {busy === 'loading' ? <p role="status">Loading the saved proposal and current working plan…</p> : null}
    {error ? <div><p role="alert" className="flowhive-review-error">{error}</p>
      {!uncertain ? <button type="button" disabled={Boolean(busy)} onClick={() => setReload(value => value + 1)}>Reload review without regenerating</button> : null}</div> : null}
    {hasLocalEdits ? <p role="status">Save or discard your unsaved working-plan edits before previewing this merge. No local edits will be replaced.</p> : null}
    {review ? <>
      {review.workingCopyChangedSinceGeneration ? <p>The working plan changed after generation. Review against the current revision shown here; older edits will not overwrite it.</p> : null}
      <div className="flowhive-review-grid" role="region" aria-label="Proposed AI work breakdown" tabIndex="0"><table>
        <thead><tr><th>Phase / WBS</th><th>Work package</th><th>Start</th><th>Finish</th><th>Effort</th></tr></thead>
        <tbody>{(display?.tasks || []).filter(task => !task.isSummary).map(task => {
          const dates = schedule?.tasks?.find(row => row.wbsNumber === task.wbsNumber);
          return <tr key={task.wbsNumber}><td>{task.phase}<br />{task.wbsNumber}</td><td><details><summary>{task.name}</summary>
            <p>{task.description}</p>{(task.detailedSteps || []).map((step, index) => <p key={index}>{index + 1}. {step}</p>)}
            <p>Acceptance: {(task.acceptanceCriteria || []).join(' ')}</p><small>Private citations: {(task.citationIds || []).join(', ') || 'Existing work'}</small>
          </details></td><td>{dates?.startDate || 'Not scheduled'}</td><td>{dates?.endDate || 'Not scheduled'}</td><td>{task.remainingEffortHours} h</td></tr>;
        })}</tbody></table></div>
      <h4>Reconcile existing work</h4>
      <p>Retain an existing activity as separate work, or map it to the corresponding AI activity. Mapped activities keep their identity, progress, scheduling constraints, notes and existing assignments. Every existing milestone and dependency is preserved with its reviewed task reference.</p>
      {existing.length ? <button type="button" disabled={Boolean(busy) || uncertain} onClick={() => {
        setChoices(Object.fromEntries(existing.map(task => [task.wbsNumber, '']))); invalidatesPreview();
      }}>Retain all existing activities separately</button> : <p>There are no existing activities to map.</p>}
      <div className="flowhive-review-grid"><table><thead><tr><th>Existing activity</th><th>Reviewed disposition</th></tr></thead><tbody>
        {existing.map(task => <tr key={task.wbsNumber}><td>{task.wbsNumber} · {task.name}<small>{task.status} · {task.percentComplete}% complete</small></td><td>
          <select aria-label={`Review disposition for ${task.name}`} disabled={Boolean(busy) || uncertain} value={choices[task.wbsNumber] ?? '__choose__'} onChange={event => {
            setChoices(current => ({ ...current, [task.wbsNumber]: event.target.value })); invalidatesPreview();
          }}><option value="__choose__" disabled>Choose an explicit disposition</option><option value="">Retain as separate work</option>
            {!task.isMilestone && !(review.currentPlan.tasks || []).some(child => child.parentWbsNumber === task.wbsNumber)
              ? proposed.map(candidate => <option key={candidate.wbsNumber} value={candidate.wbsNumber}>{candidate.wbsNumber} · {candidate.name}</option>) : null}
          </select></td></tr>)}
      </tbody></table></div>
      <p><strong>{(review.currentPlan.milestones || []).length} existing milestone(s)</strong> will be retained. Their acceptance evidence, target dates and identity are not regenerated.</p>
      <label className="flowhive-review-note">Review note<textarea aria-label="Regeneration review note" value={note} maxLength="4000" rows="3" disabled={Boolean(busy) || uncertain}
        onChange={event => { setNote(event.target.value); invalidatesPreview(); }} placeholder="Explain the task mappings and confirm that retained work is not duplicate scope." /></label>
      <button type="button" onClick={previewMerge} disabled={!ready || Boolean(busy) || uncertain || !canEdit || hasLocalEdits}>{busy === 'preview' ? 'Calculating reviewed schedule…' : 'Preview merged work breakdown'}</button>
      {preview ? <div className="flowhive-review-confirm">
        <p>{preview.reviewSummary.mappedTaskCount} mapped · {preview.reviewSummary.retainedTaskCount} retained separately · {preview.reviewSummary.preservedMilestoneCount} milestones preserved.
          Planned hours: {preview.reviewSummary.previousPlannedHours} → {preview.reviewSummary.plannedHours}. Calculated finish: {preview.schedule.projectFinishDate}.</p>
        <label><input type="checkbox" aria-label="Confirm reviewed scope and schedule" checked={acknowledged} disabled={Boolean(busy) || uncertain} onChange={event => setAcknowledged(event.target.checked)} />
          I reviewed scope duplication, task mappings, retained milestone gates, effort and the calculated schedule.</label>
        <button className="primary" type="button" onClick={applyMerge} disabled={!acknowledged || Boolean(busy) || uncertain || hasLocalEdits || !canEdit}>
          {busy === 'apply' ? 'Saving reviewed work breakdown…' : 'Apply reviewed work breakdown'}</button>
      </div> : null}
    </> : null}
  </section>;
}

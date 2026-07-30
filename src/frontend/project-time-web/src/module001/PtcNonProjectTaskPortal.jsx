import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import './ptc-non-project-task.css';

const CLASSIFICATIONS = Object.freeze([
  ['non_billable', 'Non-billable work'],
  ['administrative', 'Administrative'],
  ['training', 'Training'],
  ['leave', 'Leave'],
  ['paid_time_off', 'Paid time off'],
  ['unpaid_time_off', 'Unpaid time off']
]);

function getAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    const session = raw ? JSON.parse(raw) : null;
    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
  } catch {
    return {};
  }
}

function normalizeCode(value) {
  return String(value || '')
    .trim()
    .toUpperCase()
    .replace(/\s+/g, '_')
    .replace(/[^A-Z0-9._-]+/g, '_')
    .replace(/^[_\-.]+|[_\-.]+$/g, '')
    .slice(0, 100);
}

async function createNonProjectTask(payload) {
  const path = '/api/timesheet/ptc/non-project-tasks';
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...getAuthHeaders()
    },
    body: JSON.stringify(payload)
  });

  const raw = await response.text();
  let data = {};
  try {
    data = raw ? JSON.parse(raw) : {};
  } catch {
    data = {};
  }

  if (!response.ok) {
    throw new Error(data.message || data.detail || raw || `${path} returned HTTP ${response.status}.`);
  }
  return data;
}

function ensureHost() {
  const header = document.querySelector('.ptc-destination-catalog > header');
  if (!header) return null;

  let host = document.getElementById('ptc-non-project-task-host');
  if (!host) {
    host = document.createElement('div');
    host.id = 'ptc-non-project-task-host';
    host.dataset.projectpulsePtcNonProjectTaskHost = 'true';
    const destinationCount = header.querySelector(':scope > span');
    if (destinationCount) header.insertBefore(host, destinationCount);
    else header.appendChild(host);
  }
  return host;
}

function NonProjectTaskDialog({ onClose, onCreated }) {
  const [taskCode, setTaskCode] = useState('');
  const [taskName, setTaskName] = useState('');
  const [taskDescription, setTaskDescription] = useState('');
  const [classification, setClassification] = useState('non_billable');
  const [requiresApproval, setRequiresApproval] = useState(true);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const suggestedCode = useMemo(() => normalizeCode(taskName), [taskName]);
  const effectiveCode = normalizeCode(taskCode || suggestedCode);
  const canSave = effectiveCode.length >= 2
    && taskName.trim().length >= 2
    && reason.trim().length >= 5
    && !busy;

  const submit = async () => {
    setBusy(true);
    setError('');
    try {
      const result = await createNonProjectTask({
        taskCode: effectiveCode,
        taskName: taskName.trim(),
        taskDescription: taskDescription.trim(),
        utilizationClassification: classification,
        requiresApproval,
        displayOrder: 100,
        reason: reason.trim()
      });
      onCreated(result);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'The non-project task could not be created.');
    } finally {
      setBusy(false);
    }
  };

  return createPortal(
    <div className="ptc-non-project-task-modal" role="presentation">
      <article role="dialog" aria-modal="true" aria-labelledby="ptc-non-project-task-title">
        <header>
          <div>
            <p className="eyebrow">PROJECT TEAM COORDINATOR · MODULE 001</p>
            <h2 id="ptc-non-project-task-title">Create a non-project task</h2>
            <p>
              Create an activity that is not tied to a project. It will appear under Non-Project Time and can be selected immediately when moving an entry.
            </p>
          </div>
          <button type="button" className="close" onClick={onClose} aria-label="Close">×</button>
        </header>

        {error ? <p className="ptc-non-project-task-alert error" role="alert">{error}</p> : null}

        <div className="ptc-non-project-task-grid">
          <label>
            <span>Task name</span>
            <input
              value={taskName}
              onChange={(event) => setTaskName(event.target.value)}
              maxLength={255}
              placeholder="Internal coordination"
              autoFocus
            />
          </label>

          <label>
            <span>Task code</span>
            <input
              value={taskCode}
              onChange={(event) => setTaskCode(normalizeCode(event.target.value))}
              maxLength={100}
              placeholder={suggestedCode || 'INTERNAL_COORDINATION'}
            />
            <small>{effectiveCode || 'A code will be generated from the task name.'}</small>
          </label>

          <label>
            <span>Utilization classification</span>
            <select value={classification} onChange={(event) => setClassification(event.target.value)}>
              {CLASSIFICATIONS.map(([value, label]) => (
                <option key={value} value={value}>{label}</option>
              ))}
            </select>
          </label>

          <label className="ptc-non-project-task-checkbox">
            <input
              type="checkbox"
              checked={requiresApproval}
              onChange={(event) => setRequiresApproval(event.target.checked)}
            />
            <span>Time entered against this task requires approval</span>
          </label>
        </div>

        <label>
          <span>Description</span>
          <textarea
            value={taskDescription}
            onChange={(event) => setTaskDescription(event.target.value)}
            maxLength={2000}
            placeholder="Describe when this non-project task should be used."
          />
        </label>

        <label>
          <span>Required business reason</span>
          <textarea
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Why is this standalone task needed?"
          />
          <small>The reason is stored in audit history. It is not an approval comment.</small>
        </label>

        <aside>
          Project tasks must belong to a project in the database. This standalone task is therefore stored as a governed Non-Project Time activity, which is the supported destination type for project-independent work.
        </aside>

        <footer>
          <button type="button" className="secondary-action" disabled={busy} onClick={onClose}>Cancel</button>
          <button type="button" className="primary-action" disabled={!canSave} onClick={submit}>
            {busy ? 'Creating…' : 'Create non-project task'}
          </button>
        </footer>
      </article>
    </div>,
    document.body
  );
}

export default function PtcNonProjectTaskPortal() {
  const [host, setHost] = useState(null);
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState('');

  useEffect(() => {
    const sync = () => setHost(ensureHost());
    sync();
    const observer = new MutationObserver(sync);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', sync);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', sync);
      document.getElementById('ptc-non-project-task-host')?.remove();
    };
  }, []);

  if (!host) return null;

  return createPortal(
    <div className="ptc-non-project-task-launcher">
      <button type="button" onClick={() => setOpen(true)}>
        Create non-project task
      </button>
      {message ? <small role="status">{message}</small> : null}
      {open ? <NonProjectTaskDialog
        onClose={() => setOpen(false)}
        onCreated={(result) => {
          setOpen(false);
          setMessage(result.message || 'The non-project task is available as a Move Time destination.');
          window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed'));
          window.dispatchEvent(new CustomEvent('projectpulse:ptc-non-project-task-created', { detail: result }));
        }}
      /> : null}
    </div>,
    host
  );
}

import { useCallback, useEffect, useRef, useState } from 'react';
import { sessionHeaders } from './protected-download.js';
import './canonical-reference-library.css';

// Admin-managed canonical SOW reference (content/citation) library. This view is
// DISTINCT from the immutable TemplateCatalog candidates view: these references
// steer what a generated SOW cites, they are editable in-app, and they are gated
// by the same dark-launch kill-switch as the backend endpoints
// (PROJECTPULSE_MODULE025_REFERENCE_SOURCES_ENABLED). When the flag is OFF the
// endpoints 404 and the parent workspace never surfaces this entry.
const BASE = '/api/module025/canonical-references';

// Session-scoped JSON call reusing the module's protected-download auth pattern.
async function requestJson(url, options = {}) {
  const response = await fetch(url, {
    credentials: 'include',
    ...options,
    headers: sessionHeaders({
      Accept: 'application/json',
      ...(options.body && typeof options.body === 'string' ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    })
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload?.message || `Request failed with status ${response.status}.`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function formatTime(value) {
  if (!value) return '—';
  try {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  } catch {
    return String(value);
  }
}

export default function CanonicalReferenceLibrary({ access = null, identityKey = '' }) {
  // Role gating is SERVER-DRIVEN. Read the flags the workspace bootstrap already
  // provides; never compute a role client-side. Writes are administrator-only and
  // never available in an administrator View-As session.
  const isAdmin = Boolean(access?.isAdministrator) && !access?.isViewAs;

  const [references, setReferences] = useState([]);
  const [scope, setScope] = useState('author');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState('');
  const [refresh, setRefresh] = useState(0);

  const [selectedId, setSelectedId] = useState('');
  const [detail, setDetail] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [edit, setEdit] = useState({ label: '', projectName: '', sourceText: '', active: true });

  const [createForm, setCreateForm] = useState({ label: '', projectName: '' });
  const [createFile, setCreateFile] = useState(null);

  const session = useRef(0);
  const createInput = useRef(null);
  const replaceInput = useRef(null);

  // A new identity (sign-in / View-As change) resets every field so no prior
  // admin's staged edit or file leaks into the next session.
  useEffect(() => {
    session.current += 1;
    setSelectedId('');
    setDetail(null);
    setCreateForm({ label: '', projectName: '' });
    setCreateFile(null);
    setError('');
    setNotice('');
    setBusy('');
    if (createInput.current) createInput.current.value = '';
  }, [identityKey]);

  const loadList = useCallback(async () => {
    const current = session.current;
    setLoading(true);
    setError('');
    try {
      // Administrators get the full management list (all references); everyone
      // else the server returns active-only. Ask for management scope explicitly.
      const payload = await requestJson(BASE);
      if (current !== session.current) return;
      setReferences(Array.isArray(payload?.references) ? payload.references : []);
      setScope(payload?.scope || 'author');
    } catch (failure) {
      if (current !== session.current) return;
      setReferences([]);
      setError(failure?.message || 'The canonical reference library could not be loaded.');
    } finally {
      if (current === session.current) setLoading(false);
    }
  }, []);

  useEffect(() => { void loadList(); }, [loadList, identityKey, refresh]);

  const openDetail = useCallback(async (id) => {
    if (!id) return;
    const current = session.current;
    setSelectedId(id);
    setDetailLoading(true);
    setError('');
    try {
      const payload = await requestJson(`${BASE}/${id}`);
      if (current !== session.current) return;
      setDetail(payload);
      setEdit({
        label: payload?.label || '',
        projectName: payload?.projectName || '',
        sourceText: payload?.extractedTextPreview || '',
        active: Boolean(payload?.active)
      });
    } catch (failure) {
      if (current !== session.current) return;
      setDetail(null);
      setError(failure?.message || 'The selected reference could not be opened.');
    } finally {
      if (current === session.current) setDetailLoading(false);
    }
  }, []);

  async function createReference(event) {
    event.preventDefault();
    if (!isAdmin || busy || !createFile) return;
    setBusy('create');
    setError('');
    setNotice('');
    try {
      if (!createForm.label.trim()) throw new Error('Provide a label for the canonical reference.');
      const form = new FormData();
      form.append('file', createFile);
      form.append('label', createForm.label.trim());
      if (createForm.projectName.trim()) form.append('projectName', createForm.projectName.trim());
      const payload = await requestJson(BASE, { method: 'POST', body: form });
      setNotice(`Canonical reference “${payload?.label || createForm.label.trim()}” created and active.`);
      setCreateForm({ label: '', projectName: '' });
      setCreateFile(null);
      if (createInput.current) createInput.current.value = '';
      setRefresh((value) => value + 1);
    } catch (failure) {
      setError(failure?.message || 'The canonical reference could not be created.');
    } finally {
      setBusy('');
    }
  }

  async function saveEdit() {
    if (!isAdmin || busy || !selectedId) return;
    setBusy('edit');
    setError('');
    setNotice('');
    try {
      const payload = await requestJson(`${BASE}/${selectedId}`, {
        method: 'PUT',
        body: JSON.stringify({
          label: edit.label,
          projectName: edit.projectName,
          sourceText: edit.sourceText,
          active: edit.active
        })
      });
      setNotice('Canonical reference updated.');
      setRefresh((value) => value + 1);
      await openDetail(payload?.id || selectedId);
    } catch (failure) {
      setError(failure?.message || 'The canonical reference could not be updated.');
    } finally {
      setBusy('');
    }
  }

  async function replaceFile(event) {
    const file = event.target.files?.[0] || null;
    if (replaceInput.current) replaceInput.current.value = '';
    if (!isAdmin || busy || !selectedId || !file) return;
    setBusy('replace');
    setError('');
    setNotice('');
    try {
      const form = new FormData();
      form.append('file', file);
      await requestJson(`${BASE}/${selectedId}/replace`, { method: 'POST', body: form });
      setNotice('Reference document replaced and re-extracted.');
      setRefresh((value) => value + 1);
      await openDetail(selectedId);
    } catch (failure) {
      setError(failure?.message || 'The reference document could not be replaced.');
    } finally {
      setBusy('');
    }
  }

  async function deleteReference() {
    if (!isAdmin || busy || !selectedId) return;
    if (!window.confirm('Delete this canonical reference? It will no longer be selectable by authors.')) return;
    setBusy('delete');
    setError('');
    setNotice('');
    try {
      await requestJson(`${BASE}/${selectedId}`, { method: 'DELETE' });
      setNotice('Canonical reference deleted.');
      setSelectedId('');
      setDetail(null);
      setRefresh((value) => value + 1);
    } catch (failure) {
      setError(failure?.message || 'The canonical reference could not be deleted.');
    } finally {
      setBusy('');
    }
  }

  return (
    <section className="m025-canonical" aria-labelledby="m025-canonical-title">
      <header className="m025-canonical-head">
        <p className="m025-canonical-kicker">Content &amp; citation references</p>
        <h2 id="m025-canonical-title">Canonical SOW references</h2>
        <p>
          Manage the canonical SOW templates that authors can attach to steer a generated SOW’s
          reference citation. These are editable content references, distinct from the immutable
          template candidates catalog. Reference text is stored and used server-side only.
        </p>
        {!isAdmin ? (
          <p className="m025-canonical-readonly" role="note">
            {access?.isViewAs
              ? 'Administrator View-As is read-only. Exit View-As to create, edit, replace, or delete references.'
              : 'You can review active references. Creating, editing, replacing, or deleting is administrator-only.'}
          </p>
        ) : null}
      </header>

      {error ? <div className="m025-canonical-message m025-canonical-message--error" role="alert">{error}</div> : null}
      {notice ? <div className="m025-canonical-message" role="status">{notice}</div> : null}

      {isAdmin ? (
        <form className="m025-canonical-create" onSubmit={createReference}>
          <h3>Add a canonical reference</h3>
          <p>Upload a clean SOW template .docx (no customer data). Its text is extracted on upload and stored for author selection.</p>
          <div className="m025-canonical-fields">
            <label>Label
              <input
                required
                maxLength={300}
                value={createForm.label}
                disabled={Boolean(busy)}
                placeholder="For example, Standard Managed Services SOW reference"
                onChange={(event) => setCreateForm((current) => ({ ...current, label: event.target.value }))}
              />
            </label>
            <label>Project name (optional)
              <input
                maxLength={500}
                value={createForm.projectName}
                disabled={Boolean(busy)}
                onChange={(event) => setCreateForm((current) => ({ ...current, projectName: event.target.value }))}
              />
            </label>
            <label>Template .docx
              <input
                ref={createInput}
                type="file"
                required
                accept=".docx"
                disabled={Boolean(busy)}
                onChange={(event) => setCreateFile(event.target.files?.[0] || null)}
              />
            </label>
          </div>
          <button type="submit" className="m025-button m025-button--primary" disabled={Boolean(busy) || !createFile || !createForm.label.trim()}>
            {busy === 'create' ? 'Creating…' : 'Create canonical reference'}
          </button>
        </form>
      ) : null}

      <div className="m025-canonical-layout">
        <div className="m025-canonical-list-panel">
          <h3>{scope === 'management' ? 'All references' : 'Active references'}</h3>
          {loading ? <p role="status">Loading canonical references…</p>
            : references.length === 0 ? <p className="m025-canonical-empty">No canonical references are available.</p>
              : (
                <ul className="m025-canonical-list">
                  {references.map((reference) => (
                    <li key={reference.id}>
                      <button
                        type="button"
                        className={selectedId === reference.id ? 'm025-canonical-card is-selected' : 'm025-canonical-card'}
                        aria-pressed={selectedId === reference.id}
                        onClick={() => openDetail(reference.id)}
                      >
                        <span className="m025-canonical-card-title">
                          <strong>{reference.label}</strong>
                          <span className={`m025-canonical-pill m025-canonical-pill--${reference.active ? 'active' : 'inactive'}`}>
                            {reference.active ? 'Active' : 'Inactive'}
                          </span>
                        </span>
                        {reference.projectName ? <span>{reference.projectName}</span> : null}
                        <small>{reference.originalFilename || 'No source file'} · {reference.sourceTextLength ?? 0} characters</small>
                        <small>Updated {formatTime(reference.updatedAt)}</small>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
        </div>

        <div className="m025-canonical-detail-panel">
          {!selectedId ? (
            <div className="m025-canonical-empty m025-canonical-empty--detail">
              <strong>Select a reference</strong>
              <p>Choose a canonical reference to review its extracted text{isAdmin ? ' or edit it.' : '.'}</p>
            </div>
          ) : detailLoading ? (
            <p role="status">Opening reference…</p>
          ) : detail ? (
            <div className="m025-canonical-detail">
              <div className="m025-canonical-card-title">
                <h3>{detail.label}</h3>
                <span className={`m025-canonical-pill m025-canonical-pill--${detail.active ? 'active' : 'inactive'}`}>
                  {detail.active ? 'Active' : 'Inactive'}
                </span>
              </div>
              <small>{detail.originalFilename || 'No source file'} · {detail.sourceTextLength ?? 0} characters · Updated {formatTime(detail.updatedAt)}</small>

              {isAdmin ? (
                <div className="m025-canonical-edit">
                  <label className="m025-field"><span>Label</span>
                    <input maxLength={300} value={edit.label} disabled={Boolean(busy)}
                      onChange={(event) => setEdit((current) => ({ ...current, label: event.target.value }))} />
                  </label>
                  <label className="m025-field"><span>Project name</span>
                    <input maxLength={500} value={edit.projectName} disabled={Boolean(busy)}
                      onChange={(event) => setEdit((current) => ({ ...current, projectName: event.target.value }))} />
                  </label>
                  <label className="m025-field"><span>Reference text</span>
                    <textarea rows={12} maxLength={30000} value={edit.sourceText} disabled={Boolean(busy)}
                      onChange={(event) => setEdit((current) => ({ ...current, sourceText: event.target.value }))} />
                  </label>
                  <label className="m025-canonical-active-toggle">
                    <input type="checkbox" checked={edit.active} disabled={Boolean(busy)}
                      onChange={(event) => setEdit((current) => ({ ...current, active: event.target.checked }))} />
                    <span>Active (author-selectable)</span>
                  </label>
                  <div className="m025-canonical-actions">
                    <button type="button" className="m025-button m025-button--primary" onClick={saveEdit} disabled={Boolean(busy)}>
                      {busy === 'edit' ? 'Saving…' : 'Save changes'}
                    </button>
                    <label className="m025-button m025-button--secondary m025-canonical-replace">
                      {busy === 'replace' ? 'Replacing…' : 'Replace .docx'}
                      <input ref={replaceInput} type="file" accept=".docx" disabled={Boolean(busy)} onChange={replaceFile} hidden />
                    </label>
                    <button type="button" className="m025-button m025-button--danger" onClick={deleteReference} disabled={Boolean(busy)}>
                      {busy === 'delete' ? 'Deleting…' : 'Delete'}
                    </button>
                  </div>
                </div>
              ) : (
                <div className="m025-canonical-preview" tabIndex={0} role="region" aria-label="Extracted reference text">
                  <p>{detail.extractedTextPreview || 'No extracted text available.'}</p>
                </div>
              )}
            </div>
          ) : (
            <p className="m025-canonical-empty">This reference could not be opened.</p>
          )}
        </div>
      </div>
    </section>
  );
}

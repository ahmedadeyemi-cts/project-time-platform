import { useEffect, useRef, useState } from 'react';
import { sessionHeaders } from './protected-download.js';
import './template-catalog.css';

const PROGRAM_LABELS = { standard: 'Standard', toyota: 'Toyota', hyundai: 'Hyundai' };

export default function TemplateCatalog({ identityKey = '' }) {
  const [catalog, setCatalog] = useState(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ documentKind: 'gsd', customerProgram: 'standard', label: '', changeNotes: '' });
  const [file, setFile] = useState(null);
  const [refresh, setRefresh] = useState(0);
  const session = useRef(0);
  const uploadController = useRef(null);
  const fileInput = useRef(null);

  useEffect(() => {
    session.current += 1;
    setCatalog(null);
    setError('');
    setNotice('');
    setFile(null);
    setBusy(false);
    setForm({ documentKind: 'gsd', customerProgram: 'standard', label: '', changeNotes: '' });
    if (fileInput.current) fileInput.current.value = '';
    return () => { session.current += 1; uploadController.current?.abort(); };
  }, [identityKey]);

  useEffect(() => {
    const controller = new AbortController();
    const currentSession = session.current;
    fetch('/api/module025/sow-gsd/templates', { credentials: 'include', headers: sessionHeaders({ Accept: 'application/json' }), signal: controller.signal })
      .then(async (response) => {
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(payload.message || 'Unable to load templates.');
        if (!controller.signal.aborted && currentSession === session.current) setCatalog(payload);
      })
      .catch((failure) => { if (!controller.signal.aborted && currentSession === session.current) setError(failure.message); });
    return () => controller.abort();
  }, [identityKey, refresh]);

  async function stageTemplate(event) {
    event.preventDefault();
    if (!file || busy || !catalog?.canStage) return;
    const currentSession = session.current;
    const controller = new AbortController();
    uploadController.current = controller;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      if (file.size > catalog.maximumFileBytes) throw new Error('Select a file no larger than 4 MB.');
      const bytes = new Uint8Array(await file.arrayBuffer());
      let binary = '';
      for (let start = 0; start < bytes.length; start += 16384) binary += String.fromCharCode(...bytes.subarray(start, start + 16384));
      if (controller.signal.aborted || currentSession !== session.current) return;
      const response = await fetch('/api/module025/sow-gsd/templates', {
        method: 'POST', credentials: 'include', signal: controller.signal,
        headers: sessionHeaders({ Accept: 'application/json', 'Content-Type': 'application/json' }),
        body: JSON.stringify({ ...form, fileName: file.name, contentBase64: btoa(binary) })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.message || 'Unable to retain this template.');
      if (controller.signal.aborted || currentSession !== session.current) return;
      setNotice(`Version ${payload.version} retained for review. It is not yet used for document exports.`);
      setFile(null);
      if (fileInput.current) fileInput.current.value = '';
      setRefresh((value) => value + 1);
    } catch (failure) {
      if (!controller.signal.aborted && currentSession === session.current) setError(failure.message);
    } finally {
      if (!controller.signal.aborted && currentSession === session.current) setBusy(false);
    }
  }

  async function downloadOriginal(candidate) {
    const currentSession = session.current;
    setError('');
    try {
      const response = await fetch(`/api/module025/sow-gsd/templates/${candidate.versionId}/file`, { credentials: 'include', headers: sessionHeaders() });
      if (!response.ok) throw new Error('Unable to download this original. Refresh the catalog and try again.');
      const blob = await response.blob();
      if (currentSession !== session.current) return;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = candidate.fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (failure) { if (currentSession === session.current) setError(failure.message); }
  }

  return <section className="m025-templates" aria-labelledby="m025-templates-title">
    <header>
      <p className="m025-templates-kicker">Shared document standards</p>
      <h2 id="m025-templates-title">SOW &amp; GSD templates</h2>
      <p>Retain the original templates for your team and track each proposed version. Current document formats stay in use until a replacement’s fields, formulas, and output have been verified.</p>
    </header>
    {error && <div className="m025-template-message m025-template-message--error" role="alert">{error}</div>}
    {notice && <div className="m025-template-message" role="status">{notice}</div>}
    {!catalog && !error && <p role="status">Loading template inventory…</p>}
    {catalog && <>
      <h3>Formats used for exports today</h3>
      <div className="m025-template-grid">
        {catalog.activeExporters.map((item) => <article className="m025-template-card" key={item.key}>
          <span className="m025-template-badge">Current format</span><h4>{item.label}</h4><strong>{item.programs}</strong>
          <p>{item.implementation}</p><p>{item.readiness}</p>
        </article>)}
      </div>
      {!catalog.schemaReady && <p className="m025-template-message">Template storage will be available when the workspace database update is installed. Current downloads remain available.</p>}
      {catalog.canStage ? <form className="m025-template-upload" onSubmit={stageTemplate}>
        <h3>Retain a template for review</h3>
        <p>Upload a clean master without customer data. Each upload is immutable. Managers’ candidates are visible to their current reporting team; administrator candidates are visible across the workspace.</p>
        <div className="m025-template-fields">
          <label>Document<select value={form.documentKind} disabled={busy} onChange={(event) => { setForm({ ...form, documentKind: event.target.value }); setFile(null); if (fileInput.current) fileInput.current.value = ''; }}><option value="gsd">GSD workbook (.xlsx)</option><option value="sow">SOW document (.docx)</option></select></label>
          <label>Customer program<select value={form.customerProgram} disabled={busy} onChange={(event) => setForm({ ...form, customerProgram: event.target.value })}>{Object.entries(PROGRAM_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
          <label>Version label<input required maxLength={160} value={form.label} disabled={busy} placeholder="For example, Standard GSD September 2026" onChange={(event) => setForm({ ...form, label: event.target.value })} /></label>
          <label>Original file<input ref={fileInput} type="file" required disabled={busy} accept={form.documentKind === 'gsd' ? '.xlsx' : '.docx'} onChange={(event) => setFile(event.target.files?.[0] || null)} /></label>
        </div>
        <label>What changed, and which sections should Pulse populate?<textarea required maxLength={2000} rows={3} disabled={busy} value={form.changeNotes} onChange={(event) => setForm({ ...form, changeNotes: event.target.value })} /></label>
        <p className="m025-template-help">Maximum 4 MB. Macro-enabled files, embedded objects, and external links are not accepted. Formulas are retained without modification; their correctness requires a separate output review.</p>
        <button className="m025-button m025-button--primary" type="submit" disabled={busy || !file}>{busy ? 'Retaining original…' : 'Retain review candidate'}</button>
      </form> : catalog.schemaReady && <p>Managers with assigned SAs can retain template candidates for their teams. View As sessions are read-only.</p>}
      <h3>Retained review candidates</h3>
      <p>These originals are awaiting input mapping and output validation. Uploading a candidate does not change any SOW or GSD export.</p>
      {catalog.candidates.length === 0 ? <p>No template candidates are available in your reporting scope.</p> : <div className="m025-template-list">
        {catalog.candidates.map((candidate) => <article className="m025-template-card" key={candidate.versionId}>
          <div className="m025-template-card-title"><h4>{candidate.label}</h4><span className="m025-template-badge m025-template-badge--review">Awaiting mapping</span></div>
          <p><strong>{candidate.documentKind.toUpperCase()} · {PROGRAM_LABELS[candidate.customerProgram]} · Version {candidate.version}</strong></p>
          <p>{candidate.ownerDisplayName}{candidate.teamName ? ` · ${candidate.teamName}` : ''} · {new Date(candidate.createdAt).toLocaleString()}</p>
          <p>{candidate.changeNotes}</p>
          {candidate.documentKind === 'gsd' && <p>{candidate.validation.worksheetCount} worksheets · {candidate.validation.formulaCount} formula cells retained</p>}
          <details><summary>Original file details</summary><p>{candidate.fileName} · {Math.ceil(candidate.sizeBytes / 1024)} KB</p><code>{candidate.sha256}</code></details>
          <button type="button" className="m025-button m025-button--secondary" onClick={() => downloadOriginal(candidate)}>Download original</button>
        </article>)}
      </div>}
      {catalog.candidates.length === catalog.catalogLimit && <p>Showing the latest {catalog.catalogLimit} retained candidates in your reporting scope.</p>}
      <details className="m025-template-next"><summary>What is required before a template can become active?</summary><ol>{catalog.activationRequirements.map((requirement) => <li key={requirement}>{requirement}</li>)}</ol></details>
    </>}
  </section>;
}

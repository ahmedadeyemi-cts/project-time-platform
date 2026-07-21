import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './oneassist-routing-directory-center.css';
import './projectpulse-module-standard.css';

function token(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function sessionHeaders(authSession, extra = {}) {
  const value = token(authSession);
  return {
    ...(value ? {
      Authorization: `Bearer ${value}`,
      'X-ProjectPulse-Session': value,
      'X-Project-Pulse-Session': value,
      'X-Session-Token': value
    } : {}),
    ...extra
  };
}

async function requestJson(path, authSession, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    ...options,
    headers: sessionHeaders(authSession, options.headers)
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message ?? `OneAssist request returned HTTP ${response.status}.`);
  }
  return payload;
}

function downloadCsv(filename, rows, columns = [
  ['id', 'id'],
  ['name', 'name'],
  ['pin', 'pin']
]) {
  const quote = (value) => `"${String(value ?? '').replace(/"/g, '""')}"`;
  const content = [
    columns.map(([header]) => header).join(','),
    ...rows.map((row) => columns.map(([, field]) => quote(row[field])).join(','))
  ].join('\n');
  const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function clone(value) {
  return JSON.parse(JSON.stringify(value ?? null));
}

export default function OneAssistRoutingDirectoryCenter({ authSession }) {
  const fileRef = useRef(null);
  const [tab, setTab] = useState('directory');
  const [search, setSearch] = useState('');
  const [state, setState] = useState({
    loading: true,
    saving: false,
    capabilities: null,
    routes: [],
    importPreview: null,
    error: '',
    notice: '',
    dirty: false
  });

  const canManage = state.capabilities?.access?.canManage === true;
  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return state.routes;
    return state.routes.filter((route) =>
      String(route.name ?? '').toLowerCase().includes(query)
      || String(route.pin ?? '').includes(query)
      || String(route.id ?? '').toLowerCase().includes(query));
  }, [search, state.routes]);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '', notice: '' }));
    try {
      const [capabilities, routesPayload] = await Promise.all([
        requestJson('/api/oneassist/capabilities', authSession),
        requestJson('/api/oneassist/routes', authSession)
      ]);
      setState({
        loading: false,
        saving: false,
        capabilities,
        routes: clone(routesPayload.routes) ?? [],
        importPreview: null,
        error: '',
        notice: '',
        dirty: false
      });
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message ?? 'The OneAssist directory is unavailable.'
      }));
    }
  }, [authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const updateRoute = useCallback((id, field, value) => {
    setState((current) => ({
      ...current,
      routes: current.routes.map((route) => route.id === id ? { ...route, [field]: value } : route),
      dirty: true,
      notice: ''
    }));
  }, []);

  const addRoute = useCallback(() => {
    setState((current) => ({
      ...current,
      routes: [...current.routes, { id: crypto.randomUUID(), name: '', pin: '' }],
      dirty: true,
      notice: 'A blank OneAssist routing row was added.'
    }));
  }, []);

  const removeRoute = useCallback((id) => {
    setState((current) => ({
      ...current,
      routes: current.routes.filter((route) => route.id !== id),
      dirty: true,
      notice: 'The routing row was removed from the draft.'
    }));
  }, []);

  const validateDraft = useCallback(() => {
    const pins = new Set();
    for (const route of state.routes) {
      if (!String(route.name ?? '').trim()) return 'Every routing row requires a customer name.';
      if (!/^\d{5}$/.test(String(route.pin ?? ''))) return `PIN for ${route.name || 'an unnamed customer'} must contain exactly five digits.`;
      if (pins.has(route.pin)) return `Routing PIN ${route.pin} appears more than once.`;
      pins.add(route.pin);
    }
    return '';
  }, [state.routes]);

  const save = useCallback(async () => {
    const validation = validateDraft();
    if (validation) {
      setState((current) => ({ ...current, error: validation, notice: '' }));
      return;
    }
    setState((current) => ({ ...current, saving: true, error: '', notice: '' }));
    try {
      await requestJson('/api/oneassist/routes', authSession, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ routes: state.routes })
      });
      setState((current) => ({ ...current, saving: false, dirty: false, notice: 'OneAssist routing directory saved.' }));
      await load();
    } catch (error) {
      setState((current) => ({ ...current, saving: false, error: error?.message ?? 'OneAssist save failed.' }));
    }
  }, [authSession, load, state.routes, validateDraft]);

  const previewImport = useCallback(async (file) => {
    if (!file) return;
    setState((current) => ({ ...current, saving: true, error: '', notice: '' }));
    try {
      const body = new FormData();
      body.append('file', file);
      const preview = await requestJson('/api/oneassist/import/preview', authSession, { method: 'POST', body });
      setState((current) => ({
        ...current,
        saving: false,
        importPreview: preview,
        notice: `${preview.validCount ?? 0} valid routing rows loaded for preview.`
      }));
      setTab('import');
    } catch (error) {
      setState((current) => ({ ...current, saving: false, error: error?.message ?? 'Import preview failed.' }));
    } finally {
      if (fileRef.current) fileRef.current.value = '';
    }
  }, [authSession]);

  const applyPreview = useCallback(() => {
    const imported = state.importPreview?.routes ?? [];
    setState((current) => {
      const byPin = new Map(current.routes.map((route) => [route.pin, route]));
      imported.forEach((route) => byPin.set(route.pin, route));
      return {
        ...current,
        routes: [...byPin.values()],
        dirty: true,
        importPreview: null,
        notice: 'Import preview applied to the unsaved directory.'
      };
    });
    setTab('directory');
  }, [state.importPreview]);

  return (
    <section
      id="oneassist-routing-directory"
      className="panel oneassist-center projectpulse-module-standard"
      data-module="072"
      data-brand="us-signal"
      data-persistence="projectpulse-postgresql"
      data-pin-visibility="public-unmasked"
      aria-labelledby="oneassist-title"
    >
      <header className="oneassist-hero">
        <div className="oneassist-brand-lockup">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="oneassist-eyebrow">Module 072 · US Signal Professional Services</p>
            <h1 id="oneassist-title">OneAssist Routing Directory</h1>
            <p>Visible five-digit customer routing identifiers for engineers, coordinators, integrations, and public routing clients.</p>
          </div>
        </div>
        <div className="oneassist-authority">
          <span>{canManage ? 'Directory editor' : 'Directory viewer'}</span>
          <small>{canManage ? 'Super Administrator / Administrator / Manager / PTC' : 'Everyone can view routing PINs'}</small>
        </div>
      </header>

      <div className="oneassist-stripe" aria-hidden="true"><i /><i /><i /></div>
      {state.error ? <div className="oneassist-banner error" role="alert">{state.error}</div> : null}
      {state.notice ? <div className="oneassist-banner success" role="status">{state.notice}</div> : null}
      <div className="oneassist-banner governed">
        OneAssist PINs are public routing identifiers and are intentionally displayed without masking. They must never be accepted as proof of identity.
      </div>
      <div className="oneassist-banner governed">
        Directory edits and revision history are stored in the ProjectPulse PostgreSQL application database.
      </div>

      <nav className="oneassist-tabs" aria-label="OneAssist workspace sections">
        <button type="button" className={tab === 'directory' ? 'active' : ''} onClick={() => setTab('directory')}>Directory</button>
        <button type="button" className={tab === 'import' ? 'active' : ''} onClick={() => setTab('import')}>Import preview</button>
        <button type="button" className={tab === 'api' ? 'active' : ''} onClick={() => setTab('api')}>Public API</button>
      </nav>

      <input
        id="oneassist-import-file"
        ref={fileRef}
        className="oneassist-file-input"
        type="file"
        accept=".csv,.xlsx"
        onChange={(event) => void previewImport(event.target.files?.[0])}
      />

      {tab === 'directory' ? (
        <section className="oneassist-card">
          <div className="oneassist-card-head">
            <div><p className="oneassist-eyebrow">Public routing data</p><h2>Customer PIN directory</h2></div>
            <div className="oneassist-actions">
              <button type="button" className="oneassist-secondary" onClick={load} disabled={state.loading || state.saving}>Refresh</button>
              <button type="button" className="oneassist-secondary" onClick={() => downloadCsv('oneassist-routes.csv', state.routes)}>Download CSV</button>
              <button type="button" className="oneassist-secondary" onClick={() => downloadCsv('oneassist-ivr-routes.csv', state.routes, [['pin', 'pin'], ['customer_name', 'name'], ['customer_id', 'id']])}>Download IVR CSV</button>
              {canManage ? <label className="oneassist-secondary oneassist-file-picker" htmlFor="oneassist-import-file">Import CSV/XLSX</label> : null}
              {canManage ? <button type="button" className="oneassist-secondary" onClick={addRoute}>Add customer</button> : null}
              {canManage ? <button type="button" className="oneassist-primary" onClick={save} disabled={!state.dirty || state.saving}>Save directory</button> : null}
            </div>
          </div>
          <label className="oneassist-search">
            <span>Search customer, PIN, or customer ID</span>
            <input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search OneAssist routing…" />
          </label>
          <div className="oneassist-table-wrap">
            <table>
              <thead><tr><th>Customer</th><th>Routing PIN</th><th>Customer ID</th>{canManage ? <th>Actions</th> : null}</tr></thead>
              <tbody>
                {filtered.map((route) => (
                  <tr key={route.id}>
                    <td>{canManage ? <input value={route.name} onChange={(event) => updateRoute(route.id, 'name', event.target.value)} aria-label={`Customer name for PIN ${route.pin || 'new'}`} /> : <strong>{route.name}</strong>}</td>
                    <td>{canManage ? <input className="oneassist-pin-input" inputMode="numeric" maxLength="5" value={route.pin} onChange={(event) => updateRoute(route.id, 'pin', event.target.value.replace(/\D/g, '').slice(0, 5))} aria-label={`Routing PIN for ${route.name || 'new customer'}`} /> : <code className="oneassist-pin">{route.pin}</code>}</td>
                    <td><code className="oneassist-id">{route.id}</code></td>
                    {canManage ? <td><button type="button" className="oneassist-danger" onClick={() => removeRoute(route.id)}>Remove</button></td> : null}
                  </tr>
                ))}
                {!state.loading && !filtered.length ? <tr><td colSpan={canManage ? 4 : 3} className="oneassist-empty">No OneAssist routes match this search.</td></tr> : null}
              </tbody>
            </table>
          </div>
          <footer className="oneassist-table-footer"><span>{filtered.length} of {state.routes.length} routes</span><small>PINs require exactly five digits and must be unique.</small></footer>
        </section>
      ) : null}

      {tab === 'import' ? (
        <section className="oneassist-card">
          <div className="oneassist-card-head"><div><p className="oneassist-eyebrow">Preview before apply</p><h2>CSV/XLSX import</h2></div>{canManage ? <label className="oneassist-secondary oneassist-file-picker" htmlFor="oneassist-import-file">Choose file</label> : null}</div>
          {!canManage ? <p className="oneassist-help">Only platform administrators, Managers, and Project Team Coordinators can import directory changes.</p> : null}
          {state.importPreview ? (
            <>
              <div className="oneassist-import-summary">
                <article><span>Valid rows</span><strong>{state.importPreview.validCount}</strong></article>
                <article><span>Warnings</span><strong>{state.importPreview.warningCount}</strong></article>
                <article><span>Source</span><strong>{state.importPreview.sourceType?.toUpperCase()}</strong><small>{state.importPreview.sourceFileName}</small></article>
              </div>
              <div className="oneassist-preview-grid">
                {(state.importPreview.routes ?? []).map((route) => <article key={`${route.pin}-${route.id}`}><strong>{route.name}</strong><code>{route.pin}</code><small>{route.id}</small></article>)}
              </div>
              {(state.importPreview.warnings ?? []).length ? <pre className="oneassist-warnings">{JSON.stringify(state.importPreview.warnings, null, 2)}</pre> : null}
              <div className="oneassist-actions"><button type="button" className="oneassist-primary" onClick={applyPreview}>Apply to unsaved directory</button></div>
            </>
          ) : <div className="oneassist-empty">Choose a CSV or XLSX file. Importing creates a preview and never saves automatically.</div>}
        </section>
      ) : null}

      {tab === 'api' ? (
        <section className="oneassist-card oneassist-api">
          <div className="oneassist-card-head"><div><p className="oneassist-eyebrow">Versioned read-only contract</p><h2>Public routing API</h2></div><span className="oneassist-live">Version 1</span></div>
          <code>GET /api/public/v1/oneassist/routes</code>
          <code>GET /api/public/v1/oneassist/resolve?pin=12345</code>
          <p>The public API intentionally returns visible routing PINs and customer routing identities. It exposes no add, edit, delete, import, or save operation.</p>
        </section>
      ) : null}

      <footer className="oneassist-footer">
        <img src={usSignalLogoDataUrl} alt="" aria-hidden="true" />
        <span>US Signal · OneAssist Routing Administration</span>
        <small>Module 072 · Source-only integration candidate</small>
      </footer>
    </section>
  );
}

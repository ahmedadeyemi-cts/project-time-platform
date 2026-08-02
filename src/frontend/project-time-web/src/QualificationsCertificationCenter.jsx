import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './qualifications-certification-center.css';
import './qualifications-self-service.css';
import './projectpulse-module-standard.css';

const EMPTY_FORM = Object.freeze({
  qualificationId: '',
  category: '',
  name: '',
  competency: '',
  yearsOfExperience: '',
  effectiveStartDate: new Date().toISOString().slice(0, 10),
  effectiveEndDate: ''
});

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function headers(authSession, json = false) {
  const token = sessionToken(authSession);
  return {
    ...(json ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {})
  };
}

async function requestJson(path, authSession, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...headers(authSession, Boolean(options.body)),
      ...(options.headers || {})
    }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload?.message ?? `Qualifications request returned HTTP ${response.status}.`);
  return payload;
}

function words(value) {
  return String(value ?? 'unknown').replace(/_/g, ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function dateText(value) {
  if (!value) return 'No expiration recorded';
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString();
}

function formFromQualification(value) {
  if (!value) return { ...EMPTY_FORM };
  return {
    qualificationId: value.qualificationId || '',
    category: value.category || '',
    name: value.name || '',
    competency: value.competency || '',
    yearsOfExperience: value.yearsOfExperience ?? '',
    effectiveStartDate: value.effectiveStartDate || new Date().toISOString().slice(0, 10),
    effectiveEndDate: value.effectiveEndDate || ''
  };
}

export default function QualificationsCertificationCenter({ authSession }) {
  const [filters, setFilters] = useState({ search: '', category: '', status: 'all' });
  const [state, setState] = useState({ loading: true, capabilities: null, matrix: null, selfService: null, error: '' });
  const [form, setForm] = useState(() => ({ ...EMPTY_FORM }));
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState({ tone: '', message: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    const query = new URLSearchParams();
    if (filters.search.trim()) query.set('search', filters.search.trim());
    if (filters.category) query.set('category', filters.category);
    if (filters.status) query.set('status', filters.status);

    const [capabilitiesResult, matrixResult, selfServiceResult] = await Promise.allSettled([
      requestJson('/api/qualifications/capabilities', authSession),
      requestJson(`/api/qualifications/matrix?${query.toString()}`, authSession),
      requestJson('/api/qualifications/self-service', authSession)
    ]);

    const capabilities = capabilitiesResult.status === 'fulfilled' ? capabilitiesResult.value : null;
    const matrix = matrixResult.status === 'fulfilled' ? matrixResult.value : null;
    const selfService = selfServiceResult.status === 'fulfilled' ? selfServiceResult.value : null;
    const errors = [capabilitiesResult, matrixResult, selfServiceResult]
      .filter((result) => result.status === 'rejected')
      .map((result) => result.reason?.message || 'A qualifications source is unavailable.');

    setState({
      loading: false,
      capabilities,
      matrix,
      selfService,
      error: matrix ? '' : errors[0] || 'Qualifications are unavailable.'
    });
  }, [authSession, filters]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 180);
    return () => window.clearTimeout(timer);
  }, [load]);

  const summary = state.matrix?.summary ?? {};
  const peopleById = useMemo(
    () => new Map((state.matrix?.people ?? []).map((person) => [person.userId, person])),
    [state.matrix]
  );
  const canEditOwn = Boolean(state.selfService?.access?.canEditOwn) && !state.selfService?.access?.isViewAs;

  function updateForm(field, value) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  function editQualification(qualification) {
    setForm(formFromQualification(qualification));
    setNotice({ tone: '', message: '' });
    document.getElementById('qualification-self-service-form')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  function resetForm() {
    setForm({ ...EMPTY_FORM, effectiveStartDate: new Date().toISOString().slice(0, 10) });
    setNotice({ tone: '', message: '' });
  }

  async function saveQualification(event) {
    event.preventDefault();
    if (!canEditOwn || saving) return;
    setSaving(true);
    setNotice({ tone: '', message: '' });
    try {
      const payload = {
        category: form.category.trim(),
        name: form.name.trim(),
        competency: form.competency.trim(),
        yearsOfExperience: form.yearsOfExperience === '' ? null : Number(form.yearsOfExperience),
        effectiveStartDate: form.effectiveStartDate || null,
        effectiveEndDate: form.effectiveEndDate || null
      };
      const result = await requestJson(
        form.qualificationId
          ? `/api/qualifications/self-service/${form.qualificationId}`
          : '/api/qualifications/self-service',
        authSession,
        {
          method: form.qualificationId ? 'PUT' : 'POST',
          body: JSON.stringify(payload)
        }
      );
      setNotice({ tone: 'success', message: result.message || 'Qualification saved.' });
      resetForm();
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'The qualification could not be saved.' });
    } finally {
      setSaving(false);
    }
  }

  return (
    <section
      id="qualifications-certifications"
      className="panel qualifications-center projectpulse-module-standard"
      data-module="069"
      data-brand="us-signal"
      data-mode={canEditOwn ? 'self-service' : 'read-only-matrix'}
      aria-labelledby="qualifications-title"
    >
      <header className="qualifications-hero">
        <img className="projectpulse-module-standard__logo" src={usSignalLogoDataUrl} alt="US Signal" />
        <div>
          <p className="eyebrow">Module 069 · Role-scoped workforce capability</p>
          <h1 id="qualifications-title">Qualifications &amp; Certification Matrix</h1>
          <p>
            Search authorized workforce capability and maintain your own qualification and certification record without changing another user&apos;s profile.
          </p>
        </div>
        <div className="qualifications-scope">
          <span>{words(state.matrix?.access?.scope)}</span>
          <small>{state.loading ? 'Refreshing…' : 'Server-authorized scope'}</small>
        </div>
      </header>

      {state.error ? <div className="qualifications-banner error" role="alert">{state.error}</div> : null}
      {notice.message ? <div className={`qualifications-banner ${notice.tone}`} role="status">{notice.message}</div> : null}
      <div className="qualifications-banner governed">
        You may edit only your own qualifications in your own authenticated session. Administrator View-As remains read-only, and organization-wide administration requires separate authority.
      </div>

      <div className="qualifications-summary">
        <article><span>People</span><strong>{summary.peopleCount ?? 0}</strong><small>{summary.unrecordedPeopleCount ?? 0} without records</small></article>
        <article><span>Qualifications</span><strong>{summary.qualificationCount ?? 0}</strong><small>{summary.categoryCount ?? 0} categories</small></article>
        <article><span>Expiring within 90 days</span><strong>{summary.expiringCount ?? 0}</strong><small>Needs renewal planning</small></article>
        <article><span>Expired</span><strong>{summary.expiredCount ?? 0}</strong><small>Do not treat as current</small></article>
      </div>

      <section className="qualifications-card qualification-self-service" id="qualification-self-service-form">
        <div className="qualifications-heading">
          <div>
            <p className="eyebrow">My profile</p>
            <h2>{form.qualificationId ? 'Update qualification or certification' : 'Add qualification or certification'}</h2>
          </div>
          {form.qualificationId ? <button type="button" className="secondary-action" onClick={resetForm}>Cancel edit</button> : null}
        </div>
        {canEditOwn ? (
          <form onSubmit={saveQualification} className="qualification-self-service__form">
            <label><span>Category</span><input required maxLength="255" value={form.category} onChange={(event) => updateForm('category', event.target.value)} placeholder="Certification, platform, product, methodology…" /></label>
            <label><span>Qualification or certification</span><input required maxLength="255" value={form.name} onChange={(event) => updateForm('name', event.target.value)} placeholder="Cisco CCNP Collaboration" /></label>
            <label><span>Competency or level</span><input maxLength="100" value={form.competency} onChange={(event) => updateForm('competency', event.target.value)} placeholder="Professional, Advanced, Expert…" /></label>
            <label><span>Years of experience</span><input type="number" min="0" max="99.99" step="0.01" value={form.yearsOfExperience} onChange={(event) => updateForm('yearsOfExperience', event.target.value)} /></label>
            <label><span>Effective or issued date</span><input type="date" required value={form.effectiveStartDate} onChange={(event) => updateForm('effectiveStartDate', event.target.value)} /></label>
            <label><span>Expiration or end date</span><input type="date" value={form.effectiveEndDate} onChange={(event) => updateForm('effectiveEndDate', event.target.value)} /></label>
            <div className="qualification-self-service__actions">
              <button type="submit" disabled={saving}>{saving ? 'Saving…' : form.qualificationId ? 'Update record' : 'Add record'}</button>
            </div>
          </form>
        ) : (
          <p className="qualifications-empty">
            {state.selfService?.access?.isViewAs
              ? 'Exit Administrator View-As to change qualification records.'
              : 'Your current role can view this matrix but is not configured for self-service editing.'}
          </p>
        )}
      </section>

      <section className="qualifications-card">
        <div className="qualifications-heading">
          <div><p className="eyebrow">Filters</p><h2>Find qualified people</h2></div>
          <button type="button" className="secondary-action" onClick={load} disabled={state.loading}>Refresh</button>
        </div>
        <div className="qualifications-filters">
          <label><span>Search person, function, skill, or certification</span><input value={filters.search} onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))} placeholder="Search workforce capability" /></label>
          <label><span>Category</span><select value={filters.category} onChange={(event) => setFilters((current) => ({ ...current, category: event.target.value }))}><option value="">All categories</option>{(state.matrix?.categories ?? []).map((category) => <option value={category} key={category}>{category}</option>)}</select></label>
          <label><span>Lifecycle</span><select value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))}><option value="all">All states</option><option value="current">Current</option><option value="expiring">Expiring</option><option value="expired">Expired</option><option value="unrecorded">Unrecorded</option></select></label>
        </div>
      </section>

      <section className="qualifications-card">
        <div className="qualifications-heading">
          <div><p className="eyebrow">Identity-backed matrix</p><h2>People and capability records</h2></div>
          <span>{state.matrix?.qualifications?.length ?? 0} visible rows</span>
        </div>
        <div className="qualifications-table-wrap">
          <table>
            <thead><tr><th>Person</th><th>Function / team</th><th>Category</th><th>Qualification</th><th>Competency</th><th>Experience</th><th>Expiration</th><th>Status</th><th>Action</th></tr></thead>
            <tbody>
              {(state.matrix?.qualifications ?? []).map((row) => {
                const person = peopleById.get(row.userId);
                const ownEditable = canEditOwn && row.userId === state.selfService?.access?.effectiveUserId;
                return (
                  <tr key={row.qualificationId}>
                    <td><strong>{row.displayName}</strong><small>{row.email}</small></td>
                    <td>{row.primaryFunction || 'Not recorded'}<small>{row.teamName || row.departmentName || 'No team recorded'}</small></td>
                    <td>{row.category}</td>
                    <td><strong>{row.name}</strong></td>
                    <td>{row.competency || 'Not recorded'}</td>
                    <td>{row.yearsOfExperience == null ? 'Not recorded' : `${row.yearsOfExperience} years`}</td>
                    <td>{dateText(row.effectiveEndDate)}</td>
                    <td><span className={`qualifications-state ${row.lifecycle}`}>{words(row.lifecycle)}</span><small>{person?.qualificationCount ?? 0} total</small></td>
                    <td>{ownEditable ? <button type="button" className="secondary-action" onClick={() => editQualification(row)}>Edit</button> : <span>View</span>}</td>
                  </tr>
                );
              })}
              {!state.loading && !(state.matrix?.qualifications ?? []).length ? <tr><td colSpan="9" className="qualifications-empty">No qualification rows match the current scope and filters.</td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>

      <section className="qualifications-card">
        <div className="qualifications-heading"><div><p className="eyebrow">Coverage</p><h2>People without recorded qualifications</h2></div></div>
        <div className="qualifications-people-grid">
          {(state.matrix?.people ?? []).filter((person) => person.qualificationCount === 0).map((person) => (
            <article key={person.userId}><strong>{person.displayName}</strong><span>{person.primaryFunction || 'Function not recorded'}</span><small>{person.teamName || person.departmentName || person.email}</small></article>
          ))}
          {!state.loading && !(state.matrix?.people ?? []).some((person) => person.qualificationCount === 0) ? <p>No visible identity is missing qualification records.</p> : null}
        </div>
      </section>
    </section>
  );
}

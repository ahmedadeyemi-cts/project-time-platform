import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './qualifications-certification-center.css';
import './projectpulse-module-standard.css';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function headers(authSession) {
  const token = sessionToken(authSession);
  return token ? {
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  } : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, { method: 'GET', credentials: 'include', headers: headers(authSession) });
  const payload = await response.json().catch(() => null);
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

export default function QualificationsCertificationCenter({ authSession }) {
  const [filters, setFilters] = useState({ search: '', category: '', status: 'all' });
  const [state, setState] = useState({ loading: true, capabilities: null, matrix: null, error: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const query = new URLSearchParams();
      if (filters.search.trim()) query.set('search', filters.search.trim());
      if (filters.category) query.set('category', filters.category);
      if (filters.status) query.set('status', filters.status);
      const [capabilities, matrix] = await Promise.all([
        readJson('/api/qualifications/capabilities', authSession),
        readJson(`/api/qualifications/matrix?${query.toString()}`, authSession)
      ]);
      setState({ loading: false, capabilities, matrix, error: '' });
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: error?.message ?? 'Qualifications are unavailable.' }));
    }
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

  return (
    <section
      id="qualifications-certifications"
      className="panel qualifications-center projectpulse-module-standard"
      data-module="069"
      data-brand="us-signal"
      data-mode="read-only-matrix"
      aria-labelledby="qualifications-title"
    >
      <header className="qualifications-hero">
        <img
          className="projectpulse-module-standard__logo"
          src={usSignalLogoDataUrl}
          alt="US Signal"
        />
        <div>
          <p className="eyebrow">Module 069 · Role-scoped workforce capability</p>
          <h1 id="qualifications-title">Qualifications &amp; Certification Matrix</h1>
          <p>
            Search current ProjectPulse identity, role scope, resource function,
            skills, certifications, competency, experience, and expiration state
            without duplicating user records or changing qualification data.
          </p>
        </div>
        <div className="qualifications-scope">
          <span>{words(state.matrix?.access?.scope)}</span>
          <small>{state.loading ? 'Refreshing…' : 'Server-authorized scope'}</small>
        </div>
      </header>

      {state.error ? <div className="qualifications-banner error" role="alert">{state.error}</div> : null}
      <div className="qualifications-banner governed">
        Profile editing, renewal acknowledgement, evidence upload, and expiration
        email remain locked until persistence and Module 067 notification controls are authorized.
      </div>

      <div className="qualifications-summary">
        <article><span>People</span><strong>{summary.peopleCount ?? 0}</strong><small>{summary.unrecordedPeopleCount ?? 0} without records</small></article>
        <article><span>Qualifications</span><strong>{summary.qualificationCount ?? 0}</strong><small>{summary.categoryCount ?? 0} categories</small></article>
        <article><span>Expiring within 90 days</span><strong>{summary.expiringCount ?? 0}</strong><small>Needs renewal planning</small></article>
        <article><span>Expired</span><strong>{summary.expiredCount ?? 0}</strong><small>Do not treat as current</small></article>
      </div>

      <section className="qualifications-card">
        <div className="qualifications-heading">
          <div><p className="eyebrow">Filters</p><h2>Find qualified people</h2></div>
          <button type="button" className="secondary-action" onClick={load} disabled={state.loading}>Refresh</button>
        </div>
        <div className="qualifications-filters">
          <label>
            <span>Search person, function, skill, or certification</span>
            <input value={filters.search} onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))} placeholder="Search workforce capability" />
          </label>
          <label>
            <span>Category</span>
            <select value={filters.category} onChange={(event) => setFilters((current) => ({ ...current, category: event.target.value }))}>
              <option value="">All categories</option>
              {(state.matrix?.categories ?? []).map((category) => <option value={category} key={category}>{category}</option>)}
            </select>
          </label>
          <label>
            <span>Lifecycle</span>
            <select value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))}>
              <option value="all">All states</option>
              <option value="current">Current</option>
              <option value="expiring">Expiring</option>
              <option value="expired">Expired</option>
              <option value="unrecorded">Unrecorded</option>
            </select>
          </label>
        </div>
      </section>

      <section className="qualifications-card">
        <div className="qualifications-heading">
          <div><p className="eyebrow">Identity-backed matrix</p><h2>People and capability records</h2></div>
          <span>{state.matrix?.qualifications?.length ?? 0} visible rows</span>
        </div>
        <div className="qualifications-table-wrap">
          <table>
            <thead>
              <tr><th>Person</th><th>Function / team</th><th>Category</th><th>Qualification</th><th>Competency</th><th>Experience</th><th>Expiration</th><th>Status</th></tr>
            </thead>
            <tbody>
              {(state.matrix?.qualifications ?? []).map((row) => {
                const person = peopleById.get(row.userId);
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
                  </tr>
                );
              })}
              {!state.loading && !(state.matrix?.qualifications ?? []).length ? (
                <tr><td colSpan="8" className="qualifications-empty">No qualification rows match the current scope and filters.</td></tr>
              ) : null}
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

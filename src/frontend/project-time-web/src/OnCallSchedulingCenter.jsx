import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './oncall-scheduling-center.css';
import './projectpulse-module-standard.css';

const DEFAULT_DEPARTMENTS = ['enterprise_network', 'collaboration', 'system_storage'];

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
    throw new Error(payload?.message ?? `On-call request returned HTTP ${response.status}.`);
  }
  return payload;
}

function clone(value) {
  return JSON.parse(JSON.stringify(value ?? null));
}

function label(value) {
  return String(value ?? '').replace(/_/g, ' ').replace(/\b\w/g, (character) => character.toUpperCase());
}

function localInput(value) {
  return String(value ?? '').slice(0, 16);
}

function chicagoDate(value) {
  if (!value) return 'Not scheduled';
  const parts = String(value).match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/);
  if (!parts) return value;
  const [, year, month, day, hour, minute] = parts;
  const date = new Date(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute));
  if (Number.isNaN(date.getTime())) return value;
  return `${date.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  })} CT`;
}

function nextFridayWindow() {
  const start = new Date();
  const day = start.getDay();
  start.setDate(start.getDate() + ((5 - day + 7) % 7));
  start.setHours(16, 0, 0, 0);
  const end = new Date(start);
  end.setDate(end.getDate() + 7);
  end.setHours(7, 0, 0, 0);
  const iso = (date) => {
    const part = (number) => String(number).padStart(2, '0');
    return `${date.getFullYear()}-${part(date.getMonth() + 1)}-${part(date.getDate())}T${part(date.getHours())}:${part(date.getMinutes())}:00`;
  };
  return { startISO: iso(start), endISO: iso(end) };
}

function dateOnly(date) {
  const part = (number) => String(number).padStart(2, '0');
  return `${date.getFullYear()}-${part(date.getMonth() + 1)}-${part(date.getDate())}`;
}

function blankEntry(departments) {
  const window = nextFridayWindow();
  return {
    id: crypto.randomUUID(),
    ...window,
    departments: Object.fromEntries(departments.map((department) => [department, null]))
  };
}

export default function OnCallSchedulingCenter({ authSession }) {
  const [tab, setTab] = useState('schedule');
  const [state, setState] = useState({
    loading: true,
    saving: false,
    capabilities: null,
    schedule: { version: 1, tz: 'America/Chicago', entries: [] },
    roster: {},
    identities: [],
    history: [],
    error: '',
    notice: '',
    scheduleDirty: false,
    rosterDirty: false
  });
  const [generation, setGeneration] = useState(() => {
    const start = new Date();
    const end = new Date(start);
    end.setMonth(end.getMonth() + 6);
    return { startDate: dateOnly(start), endDate: dateOnly(end), seedIndex: '0' };
  });

  const canManage = state.capabilities?.access?.canManage === true;
  const departments = useMemo(() => {
    const values = new Set(DEFAULT_DEPARTMENTS);
    Object.keys(state.roster ?? {}).forEach((value) => values.add(value));
    (state.schedule?.entries ?? []).forEach((entry) => {
      Object.keys(entry.departments ?? {}).forEach((value) => values.add(value));
    });
    return [...values];
  }, [state.roster, state.schedule]);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '', notice: '' }));
    try {
      const capabilities = await requestJson('/api/oncall-scheduling/capabilities', authSession);
      const [schedulePayload, rosterPayload, historyPayload, identityPayload] = await Promise.all([
        requestJson('/api/oncall-scheduling/schedule', authSession),
        requestJson('/api/oncall-scheduling/roster', authSession),
        requestJson('/api/oncall-scheduling/history', authSession),
        capabilities.access?.canManage
          ? requestJson('/api/oncall-scheduling/identity-options', authSession)
          : Promise.resolve({ identities: [] })
      ]);
      setState((current) => ({
        ...current,
        loading: false,
        capabilities,
        schedule: clone(schedulePayload.schedule) ?? { version: 1, tz: 'America/Chicago', entries: [] },
        roster: clone(rosterPayload.roster) ?? {},
        history: historyPayload.history?.history ?? historyPayload.history ?? [],
        identities: identityPayload.identities ?? [],
        error: '',
        notice: '',
        scheduleDirty: false,
        rosterDirty: false
      }));
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message ?? 'The on-call workspace is unavailable.'
      }));
    }
  }, [authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const setEntry = useCallback((index, updater) => {
    setState((current) => {
      const schedule = clone(current.schedule);
      schedule.entries[index] = updater(schedule.entries[index]);
      return { ...current, schedule, scheduleDirty: true, notice: '' };
    });
  }, []);

  const assignIdentity = useCallback((entryIndex, department, userId) => {
    const identity = state.identities.find((candidate) => candidate.userId === userId);
    setEntry(entryIndex, (entry) => ({
      ...entry,
      departments: {
        ...(entry.departments ?? {}),
        [department]: identity ? {
          userId: identity.userId,
          name: identity.displayName,
          email: identity.email,
          phone: entry.departments?.[department]?.phone ?? '',
          teamName: identity.teamName,
          departmentName: identity.departmentName
        } : null
      }
    }));
  }, [setEntry, state.identities]);

  const addEntry = useCallback(() => {
    setState((current) => ({
      ...current,
      schedule: {
        ...current.schedule,
        entries: [...(current.schedule?.entries ?? []), blankEntry(departments)]
      },
      scheduleDirty: true,
      notice: 'A new Friday coverage window was added. Save when ready.'
    }));
  }, [departments]);

  const removeEntry = useCallback((index) => {
    setState((current) => ({
      ...current,
      schedule: {
        ...current.schedule,
        entries: current.schedule.entries.filter((_, candidateIndex) => candidateIndex !== index)
      },
      scheduleDirty: true,
      notice: 'The coverage window was removed from the draft.'
    }));
  }, []);

  const saveSchedule = useCallback(async () => {
    setState((current) => ({ ...current, saving: true, error: '', notice: '' }));
    try {
      await requestJson('/api/oncall-scheduling/schedule', authSession, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ schedule: state.schedule })
      });
      setState((current) => ({ ...current, saving: false, scheduleDirty: false, notice: 'On-call schedule saved.' }));
    } catch (error) {
      setState((current) => ({ ...current, saving: false, error: error?.message ?? 'Schedule save failed.' }));
    }
  }, [authSession, state.schedule]);

  const previewRotation = useCallback(async () => {
    setState((current) => ({ ...current, saving: true, error: '', notice: '' }));
    try {
      const payload = await requestJson('/api/oncall-scheduling/autogenerate', authSession, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ ...generation, seedIndex: Number(generation.seedIndex) || 0 })
      });
      setState((current) => ({
        ...current,
        saving: false,
        schedule: clone(payload.schedule),
        scheduleDirty: true,
        notice: `${payload.entriesGenerated ?? 0} weekly entries generated as an unsaved preview.`
      }));
      setTab('schedule');
    } catch (error) {
      setState((current) => ({ ...current, saving: false, error: error?.message ?? 'Rotation preview failed.' }));
    }
  }, [authSession, generation]);

  const addRosterIdentity = useCallback((department, userId) => {
    const identity = state.identities.find((candidate) => candidate.userId === userId);
    if (!identity) return;
    setState((current) => {
      const roster = clone(current.roster) ?? {};
      const existing = roster[department] ?? [];
      if (existing.some((person) => person.userId === identity.userId)) return current;
      roster[department] = [...existing, {
        userId: identity.userId,
        name: identity.displayName,
        email: identity.email,
        phone: ''
      }];
      return { ...current, roster, rosterDirty: true, notice: `${identity.displayName} added to ${label(department)}.` };
    });
  }, [state.identities]);

  const updateRosterPerson = useCallback((department, index, field, value) => {
    setState((current) => {
      const roster = clone(current.roster) ?? {};
      roster[department] = [...(roster[department] ?? [])];
      roster[department][index] = { ...roster[department][index], [field]: value };
      return { ...current, roster, rosterDirty: true, notice: '' };
    });
  }, []);

  const removeRosterIdentity = useCallback((department, index) => {
    setState((current) => {
      const roster = clone(current.roster) ?? {};
      roster[department] = (roster[department] ?? []).filter((_, candidateIndex) => candidateIndex !== index);
      return { ...current, roster, rosterDirty: true, notice: 'The roster identity was removed from the draft.' };
    });
  }, []);

  const saveRoster = useCallback(async () => {
    setState((current) => ({ ...current, saving: true, error: '', notice: '' }));
    try {
      await requestJson('/api/oncall-scheduling/roster', authSession, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ roster: state.roster })
      });
      setState((current) => ({ ...current, saving: false, rosterDirty: false, notice: 'On-call roster saved.' }));
    } catch (error) {
      setState((current) => ({ ...current, saving: false, error: error?.message ?? 'Roster save failed.' }));
    }
  }, [authSession, state.roster]);

  const restoreSnapshot = useCallback(async (id) => {
    if (!window.confirm('Restore this historical on-call schedule?')) return;
    setState((current) => ({ ...current, saving: true, error: '', notice: '' }));
    try {
      await requestJson('/api/oncall-scheduling/history/restore', authSession, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ id })
      });
      setState((current) => ({ ...current, saving: false, notice: 'Historical schedule restored.' }));
      await load();
    } catch (error) {
      setState((current) => ({ ...current, saving: false, error: error?.message ?? 'History restore failed.' }));
    }
  }, [authSession, load]);

  return (
    <section
      id="oncall-scheduling"
      className="panel oncall-center projectpulse-module-standard"
      data-module="071"
      data-brand="us-signal"
      data-persistence="cloudflare-compatibility"
      aria-labelledby="oncall-title"
    >
      <header className="oncall-hero">
        <div className="oncall-brand-lockup">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="oncall-eyebrow">Module 071 · US Signal Professional Services</p>
            <h1 id="oncall-title">On-Call Scheduling</h1>
            <p>Identity-backed weekly engineering coverage, history, routing, and governed email readiness.</p>
          </div>
        </div>
        <div className="oncall-authority">
          <span>{canManage ? 'Schedule manager' : 'Read-only viewer'}</span>
          <small>{canManage ? 'Super Administrator / Administrator / Manager / Engineering Team Lead' : 'All ProjectPulse users can view'}</small>
        </div>
      </header>

      <div className="oncall-stripe" aria-hidden="true"><i /><i /><i /></div>
      {state.error ? <div className="oncall-banner error" role="alert">{state.error}</div> : null}
      {state.notice ? <div className="oncall-banner success" role="status">{state.notice}</div> : null}
      <div className="oncall-banner governed">
        Email automation is owned by Global SMTP: Monday upcoming notice, Tuesday acknowledgement escalation, and Friday start notice at 8:00 AM America/Chicago.
      </div>

      <nav className="oncall-tabs" aria-label="On-call workspace sections">
        {['schedule', 'rotation', 'roster', 'history', 'api'].map((value) => (
          <button type="button" key={value} className={tab === value ? 'active' : ''} onClick={() => setTab(value)}>
            {value === 'api' ? 'Public API' : label(value)}
          </button>
        ))}
      </nav>

      {tab === 'schedule' ? (
        <section className="oncall-card">
          <div className="oncall-card-head">
            <div><p className="oncall-eyebrow">America/Chicago</p><h2>Coverage schedule</h2></div>
            <div className="oncall-actions">
              <button type="button" className="oncall-secondary" onClick={load} disabled={state.loading || state.saving}>Refresh</button>
              {canManage ? <button type="button" className="oncall-secondary" onClick={addEntry}>Add coverage week</button> : null}
              {canManage ? <button type="button" className="oncall-primary" onClick={saveSchedule} disabled={!state.scheduleDirty || state.saving}>Save schedule</button> : null}
            </div>
          </div>
          <div className="oncall-schedule-grid">
            {(state.schedule?.entries ?? []).map((entry, entryIndex) => (
              <article className="oncall-week" key={entry.id}>
                <header>
                  <div><span>Coverage window</span><strong>{chicagoDate(entry.startISO)} → {chicagoDate(entry.endISO)}</strong></div>
                  {canManage ? <button type="button" className="oncall-danger" onClick={() => removeEntry(entryIndex)}>Remove</button> : null}
                </header>
                {canManage ? (
                  <div className="oncall-window-editor">
                    <label><span>Starts</span><input type="datetime-local" value={localInput(entry.startISO)} onChange={(event) => setEntry(entryIndex, (current) => ({ ...current, startISO: `${event.target.value}:00` }))} /></label>
                    <label><span>Ends</span><input type="datetime-local" value={localInput(entry.endISO)} onChange={(event) => setEntry(entryIndex, (current) => ({ ...current, endISO: `${event.target.value}:00` }))} /></label>
                  </div>
                ) : null}
                <div className="oncall-departments">
                  {departments.map((department) => {
                    const person = entry.departments?.[department];
                    return (
                      <div className="oncall-person" key={department}>
                        <span>{label(department)}</span>
                        {canManage ? (
                          <select value={person?.userId ?? ''} onChange={(event) => assignIdentity(entryIndex, department, event.target.value)}>
                            <option value="">Unassigned</option>
                            {state.identities.map((identity) => <option key={identity.userId} value={identity.userId}>{identity.displayName} · {identity.teamName}</option>)}
                          </select>
                        ) : <strong>{person?.name ?? 'Unassigned'}</strong>}
                        <small>{person?.email ?? 'No email'}{person?.phone ? ` · ${person.phone}` : ''}</small>
                      </div>
                    );
                  })}
                </div>
              </article>
            ))}
            {!state.loading && !(state.schedule?.entries ?? []).length ? <div className="oncall-empty">No schedule entries are available.</div> : null}
          </div>
        </section>
      ) : null}

      {tab === 'rotation' ? (
        <section className="oncall-card oncall-narrow">
          <div className="oncall-card-head"><div><p className="oncall-eyebrow">Unsaved preview first</p><h2>Generate Friday rotation</h2></div></div>
          <div className="oncall-form-grid">
            <label><span>Start date</span><input type="date" value={generation.startDate} onChange={(event) => setGeneration((current) => ({ ...current, startDate: event.target.value }))} /></label>
            <label><span>End date</span><input type="date" value={generation.endDate} onChange={(event) => setGeneration((current) => ({ ...current, endDate: event.target.value }))} /></label>
            <label><span>Rotation seed</span><input type="number" min="0" step="1" value={generation.seedIndex} onChange={(event) => setGeneration((current) => ({ ...current, seedIndex: event.target.value }))} /></label>
          </div>
          <p className="oncall-help">Each generated entry begins Friday at 4:00 PM and ends the following Friday at 7:00 AM Central. Previewing never saves.</p>
          {canManage ? <button type="button" className="oncall-primary" onClick={previewRotation} disabled={state.saving}>Preview generated schedule</button> : <p className="oncall-readonly">Only Managers and Engineering Team Leads can generate rotations.</p>}
        </section>
      ) : null}

      {tab === 'roster' ? (
        <section className="oncall-card">
          <div className="oncall-card-head">
            <div><p className="oncall-eyebrow">Module 062 identities</p><h2>Rotation roster</h2></div>
            {canManage ? <button type="button" className="oncall-primary" onClick={saveRoster} disabled={!state.rosterDirty || state.saving}>Save roster</button> : null}
          </div>
          <div className="oncall-roster-grid">
            {departments.map((department) => (
              <article key={department}>
                <h3>{label(department)}</h3>
                <ul>
                  {(state.roster?.[department] ?? []).map((person, index) => (
                    <li key={person.userId ?? `${person.email}-${index}`}>
                      <strong>{person.name}</strong>
                      <span>{person.email}</span>
                      {canManage ? (
                        <div className="oncall-roster-editor">
                          <label><span>Routing phone</span><input type="tel" value={person.phone ?? ''} onChange={(event) => updateRosterPerson(department, index, 'phone', event.target.value)} /></label>
                          <button type="button" className="oncall-danger" onClick={() => removeRosterIdentity(department, index)}>Remove</button>
                        </div>
                      ) : person.phone ? <span>{person.phone}</span> : null}
                    </li>
                  ))}
                </ul>
                {canManage ? (
                  <select defaultValue="" onChange={(event) => { addRosterIdentity(department, event.target.value); event.target.value = ''; }}>
                    <option value="">Add an identity…</option>
                    {state.identities.map((identity) => <option key={identity.userId} value={identity.userId}>{identity.displayName} · {identity.teamName}</option>)}
                  </select>
                ) : null}
              </article>
            ))}
          </div>
        </section>
      ) : null}

      {tab === 'history' ? (
        <section className="oncall-card">
          <div className="oncall-card-head"><div><p className="oncall-eyebrow">Immutable evidence</p><h2>Schedule history</h2></div></div>
          <div className="oncall-history">
            {(Array.isArray(state.history) ? state.history : []).map((item) => (
              <article key={item.id}>
                <div><strong>{new Date(item.savedAt ?? item.id).toLocaleString()}</strong><span>{item.entriesCount ?? item.schedule?.entries?.length ?? 0} entries · {item.savedBy ?? 'Unknown editor'}</span></div>
                {canManage ? <button type="button" className="oncall-secondary" onClick={() => restoreSnapshot(item.id)}>Restore</button> : null}
              </article>
            ))}
            {!state.history?.length ? <div className="oncall-empty">No historical snapshots are available.</div> : null}
          </div>
        </section>
      ) : null}

      {tab === 'api' ? (
        <section className="oncall-card oncall-api">
          <div className="oncall-card-head"><div><p className="oncall-eyebrow">Read-only routing contract</p><h2>Public On-Call API</h2></div><span className="oncall-live">Version 1</span></div>
          <code>GET /api/public/v1/oncall/current</code>
          <code>GET /api/public/v1/oncall/current?department=collaboration</code>
          <code>GET /api/public/v1/oncall/schedule</code>
          <p>Public routes expose current routing assignments only. Schedule and roster mutations remain protected by the Manager and Engineering Team Lead permission boundary.</p>
        </section>
      ) : null}

      <footer className="oncall-footer">
        <img src={usSignalLogoDataUrl} alt="" aria-hidden="true" />
        <span>US Signal · Professional Services On-Call Administration</span>
        <small>Module 071 · Source-only integration candidate</small>
      </footer>
    </section>
  );
}

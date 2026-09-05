import React, { useCallback, useEffect, useMemo, useState } from 'react';

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const CENTRAL_ZONE = 'America/Chicago';

async function readJson(response) {
  const text = await response.text();
  let payload = {};
  try { payload = text ? JSON.parse(text) : {}; } catch { payload = {}; }
  if (!response.ok) {
    const message = payload?.message || payload?.error?.message || payload?.status || `Request failed (${response.status})`;
    const error = new Error(message);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function formatUtc(value, timeZone) {
  if (!value) return 'Not scheduled';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Unavailable';
  return new Intl.DateTimeFormat(undefined, {
    timeZone,
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZoneName: 'short'
  }).format(date);
}

function localFormat(value) {
  if (!value) return 'Not scheduled';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Unavailable';
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZoneName: 'short'
  }).format(date);
}

function shortDigest(value) {
  const text = String(value || '');
  if (!text) return 'Unavailable';
  return text.length > 18 ? `${text.slice(0, 10)}…${text.slice(-6)}` : text;
}

function Stat({ label, value, detail }) {
  return (
    <div className="settings-card" style={{ minWidth: 0 }}>
      <div style={{ fontSize: '.78rem', textTransform: 'uppercase', letterSpacing: '.06em', opacity: .72 }}>{label}</div>
      <div style={{ fontSize: '1.05rem', fontWeight: 700, marginTop: '.35rem', overflowWrap: 'anywhere' }}>{value || 'Unavailable'}</div>
      {detail ? <div style={{ marginTop: '.3rem', fontSize: '.84rem', opacity: .78 }}>{detail}</div> : null}
    </div>
  );
}

export default function CelarAiRuntimeVersionCenter() {
  const [payload, setPayload] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [form, setForm] = useState({ enabled: true, dayOfWeek: 'Sunday', localTime: '01:00', timeZone: CENTRAL_ZONE });

  const refresh = useCallback(async ({ quiet = false } = {}) => {
    if (!quiet) setLoading(true);
    setError('');
    try {
      const next = await readJson(await fetch('/api/celar-ai/v1/runtime-version/status', {
        method: 'GET',
        headers: { Accept: 'application/json' },
        cache: 'no-store'
      }));
      setPayload(next);
      const desired = next?.runtime?.maintenance?.desired;
      if (desired) {
        setForm({
          enabled: desired.enabled !== false,
          dayOfWeek: desired.dayOfWeek || 'Sunday',
          localTime: desired.localTime || '01:00',
          timeZone: desired.timeZone || CENTRAL_ZONE
        });
      }
    } catch (caught) {
      setError(caught?.message || 'Celar runtime version status is unavailable.');
    } finally {
      if (!quiet) setLoading(false);
    }
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  const runtime = payload?.runtime || {};
  const maintenance = runtime?.maintenance || {};
  const applied = maintenance?.applied || {};
  const update = maintenance?.update || {};
  const models = runtime?.ollama?.models || [];
  const control = payload?.control || {};
  const canSave = Boolean(control.scheduleMutationConfigured)
    && !Boolean(payload?.access?.isViewAs)
    && !saving;

  const nextWindow = applied?.nextMaintenanceAtUtc || null;
  const lastResult = String(update?.lastResult || 'No automated update recorded').replaceAll('_', ' ');
  const installedCount = useMemo(() => models.filter((model) => model?.installed).length, [models]);

  async function saveSchedule(event) {
    event.preventDefault();
    if (!canSave) return;
    setSaving(true);
    setError('');
    setMessage('');
    try {
      const result = await readJson(await fetch('/api/celar-ai/v1/runtime-version/schedule', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(form)
      }));
      setMessage(result?.message || 'Maintenance schedule accepted.');
      window.setTimeout(() => refresh({ quiet: true }), 2500);
    } catch (caught) {
      setError(caught?.message || 'The maintenance schedule could not be changed.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="admin-center" style={{ display: 'grid', gap: '1rem' }}>
      <header className="panel" style={{ padding: '1.15rem' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <div>
            <div className="eyebrow">Module 084 · Platform Operations</div>
            <h1 style={{ margin: '.25rem 0' }}>Celar AI Runtime &amp; Version Center</h1>
            <p style={{ margin: 0, maxWidth: '76ch' }}>
              View the private Oracle Celar runtime, approved local model artifacts, automatic update evidence, and the governed maintenance window. Module 064 continues to own provider order and provider credentials.
            </p>
          </div>
          <button type="button" className="secondary" onClick={() => refresh()} disabled={loading || saving}>
            {loading ? 'Refreshing…' : 'Refresh status'}
          </button>
        </div>
      </header>

      {error ? <div className="alert alert-error" role="alert">{error}</div> : null}
      {message ? <div className="alert alert-success" role="status">{message}</div> : null}

      <section className="panel" style={{ padding: '1rem' }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: '.75rem' }}>
          <Stat label="Celar gateway" value={runtime?.gatewayVersion} detail="Private HTTPS runtime" />
          <Stat label="Ollama engine" value={runtime?.ollama?.engineVersion} detail={`${installedCount}/${models.length || 4} approved artifacts installed`} />
          <Stat label="Tesseract" value={runtime?.components?.tesseractVersion} detail="Private OCR" />
          <Stat label="ClamAV" value={runtime?.components?.clamavVersion} detail="Engine + signature evidence" />
        </div>
      </section>

      <section className="panel" style={{ padding: '1rem', overflowX: 'auto' }}>
        <div style={{ marginBottom: '.75rem' }}>
          <div className="eyebrow">Approved local portfolio</div>
          <h2 style={{ margin: '.2rem 0' }}>Installed model artifacts</h2>
          <p style={{ margin: 0 }}>
            Automatic maintenance refreshes these approved tags. A future Gemma, Qwen, or Llama major family is not promoted automatically.
          </p>
        </div>
        <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '760px' }}>
          <thead>
            <tr>
              <th style={{ textAlign: 'left', padding: '.6rem' }}>Approved tag</th>
              <th style={{ textAlign: 'left', padding: '.6rem' }}>Installed</th>
              <th style={{ textAlign: 'left', padding: '.6rem' }}>Artifact digest</th>
              <th style={{ textAlign: 'left', padding: '.6rem' }}>Parameters</th>
              <th style={{ textAlign: 'left', padding: '.6rem' }}>Quantization</th>
              <th style={{ textAlign: 'left', padding: '.6rem' }}>Last artifact change</th>
            </tr>
          </thead>
          <tbody>
            {models.length ? models.map((model) => (
              <tr key={model.configuredName} style={{ borderTop: '1px solid var(--border-color, rgba(127,127,127,.25))' }}>
                <td style={{ padding: '.6rem', fontWeight: 700 }}>{model.configuredName}</td>
                <td style={{ padding: '.6rem' }}>{model.installed ? 'Yes' : 'Missing'}</td>
                <td style={{ padding: '.6rem', fontFamily: 'monospace' }} title={model.digest || ''}>{shortDigest(model.digest)}</td>
                <td style={{ padding: '.6rem' }}>{model.parameterSize || '—'}</td>
                <td style={{ padding: '.6rem' }}>{model.quantizationLevel || '—'}</td>
                <td style={{ padding: '.6rem' }}>{model.modifiedAt ? localFormat(model.modifiedAt) : '—'}</td>
              </tr>
            )) : (
              <tr><td colSpan="6" style={{ padding: '1rem' }}>{loading ? 'Loading model inventory…' : 'Model inventory is unavailable.'}</td></tr>
            )}
          </tbody>
        </table>
      </section>

      <section className="panel" style={{ padding: '1rem' }}>
        <div className="eyebrow">Automatic maintenance</div>
        <h2 style={{ margin: '.2rem 0 .75rem' }}>Update window &amp; history</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(210px,1fr))', gap: '.75rem', marginBottom: '1rem' }}>
          <Stat label="Current policy" value={`${maintenance?.desired?.dayOfWeek || form.dayOfWeek} ${maintenance?.desired?.localTime || form.localTime}`} detail="America/Chicago · weekly" />
          <Stat label="Next update · Central" value={formatUtc(nextWindow, CENTRAL_ZONE)} detail="Tracks CST/CDT automatically" />
          <Stat label="Next update · your browser" value={localFormat(nextWindow)} detail="Displayed in your device time zone" />
          <Stat label="Last update result" value={lastResult} detail={update?.completedAt ? localFormat(update.completedAt) : 'No completed automated run recorded yet'} />
          <Stat label="Last successful update" value={update?.lastSuccessfulUpdateAt ? localFormat(update.lastSuccessfulUpdateAt) : 'Not recorded yet'} detail={update?.currentEngineVersion || ''} />
          <Stat label="Rollback evidence" value={update?.rollbackAvailable ? 'Available' : 'Not currently recorded'} detail={update?.rollbackPerformed ? 'Last failed update was rolled back' : 'No rollback recorded for the last attempt'} />
        </div>

        <form onSubmit={saveSchedule} style={{ display: 'grid', gap: '.85rem', maxWidth: '760px' }}>
          <label style={{ display: 'flex', gap: '.6rem', alignItems: 'center' }}>
            <input
              type="checkbox"
              checked={form.enabled}
              onChange={(event) => setForm((current) => ({ ...current, enabled: event.target.checked }))}
            />
            Enable automatic Celar model/engine maintenance
          </label>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(190px,1fr))', gap: '.75rem' }}>
            <label>
              <span style={{ display: 'block', marginBottom: '.3rem', fontWeight: 650 }}>Weekly day</span>
              <select value={form.dayOfWeek} onChange={(event) => setForm((current) => ({ ...current, dayOfWeek: event.target.value }))} style={{ width: '100%' }}>
                {DAYS.map((day) => <option key={day} value={day}>{day}</option>)}
              </select>
            </label>
            <label>
              <span style={{ display: 'block', marginBottom: '.3rem', fontWeight: 650 }}>Central start time</span>
              <input type="time" value={form.localTime} onChange={(event) => setForm((current) => ({ ...current, localTime: event.target.value }))} style={{ width: '100%' }} />
            </label>
            <label>
              <span style={{ display: 'block', marginBottom: '.3rem', fontWeight: 650 }}>Time zone</span>
              <input value="America/Chicago" readOnly style={{ width: '100%' }} />
            </label>
          </div>

          {!control.scheduleMutationConfigured ? (
            <div className="alert alert-warning">
              Version/status viewing is available, but schedule changes stay disabled until the dedicated Protected-Test maintenance credential is synchronized. The inference credential cannot change the schedule.
            </div>
          ) : null}
          {payload?.access?.isViewAs ? (
            <div className="alert alert-warning">Schedule changes are disabled in Administrator View-As. Return to your actual administrator session to make a change.</div>
          ) : null}

          <div>
            <button type="submit" className="primary" disabled={!canSave}>
              {saving ? 'Saving…' : 'Save maintenance window'}
            </button>
          </div>
        </form>
      </section>

      <section className="panel" style={{ padding: '1rem' }}>
        <div className="eyebrow">Boundary</div>
        <p style={{ margin: '.25rem 0 0' }}>
          This module manages only the private Celar runtime. It does not change the platform route <strong>DeepSeek v4 → Celar AI → Claude → OpenAI → governed local template</strong>, and it never returns runtime or maintenance token values to the browser.
        </p>
      </section>
    </div>
  );
}

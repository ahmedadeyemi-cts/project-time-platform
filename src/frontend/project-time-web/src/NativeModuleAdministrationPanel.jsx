import { useCallback, useEffect, useMemo, useState } from 'react';
import './native-module-administration.css';

function emptyValue(field) {
  if (field.type === 'checkbox') return false;
  if (field.type === 'number') return field.min ?? 0;
  if (field.type === 'select') return field.options?.[0] ?? '';
  return '';
}

function createRecord(fields) {
  return Object.fromEntries(fields.map((field) => [field.name, emptyValue(field)]));
}

function asErrorMessage(payload, fallback) {
  return payload?.message || payload?.status || fallback;
}

async function readJson(response) {
  const text = await response.text();
  if (!text.trim()) return {};
  try {
    return JSON.parse(text);
  } catch {
    return { status: 'invalid_json_response' };
  }
}

function formatTimestamp(value) {
  if (!value) return 'Unknown time';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

export default function NativeModuleAdministrationPanel({ moduleNumber }) {
  const [schema, setSchema] = useState(null);
  const [document, setDocument] = useState(null);
  const [revision, setRevision] = useState(0);
  const [history, setHistory] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const baseUrl = `/api/native-administration/${moduleNumber}`;

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    setMessage('');

    try {
      const [schemaResponse, documentResponse, historyResponse] = await Promise.all([
        fetch(`${baseUrl}/schema`),
        fetch(`${baseUrl}/document`),
        fetch(`${baseUrl}/history`)
      ]);

      const schemaBody = await readJson(schemaResponse);
      const documentBody = await readJson(documentResponse);
      const historyBody = await readJson(historyResponse);

      if (!schemaResponse.ok) throw new Error(asErrorMessage(schemaBody, 'Unable to load the management schema.'));
      if (!documentResponse.ok) throw new Error(asErrorMessage(documentBody, 'Unable to load the saved document.'));
      if (!historyResponse.ok) throw new Error(asErrorMessage(historyBody, 'Unable to load revision history.'));

      setSchema(schemaBody);
      setDocument(documentBody.document || {});
      setRevision(Number(documentBody.revision || 0));
      setHistory(Array.isArray(historyBody.history) ? historyBody.history : []);
    } catch (loadError) {
      setError(loadError?.message || 'Native administration data could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [baseUrl]);

  useEffect(() => {
    void load();
  }, [load]);

  const canManage = Boolean(schema?.access?.canManage) && !schema?.access?.isViewAs;
  const fields = Array.isArray(schema?.fields) ? schema.fields : [];
  const identityOptions = Array.isArray(schema?.identityOptions) ? schema.identityOptions : [];
  const collectionKey = schema?.collectionKey || 'records';
  const records = schema?.mode === 'collection' && Array.isArray(document?.[collectionKey])
    ? document[collectionKey]
    : [];
  const configuration = schema?.mode === 'configuration' && document?.configuration && typeof document.configuration === 'object'
    ? document.configuration
    : {};

  const identityLookup = useMemo(
    () => new Map(identityOptions.map((item) => [item.userId, item])),
    [identityOptions]
  );

  function updateConfiguration(fieldName, value) {
    setDocument((current) => ({
      ...(current || {}),
      configuration: {
        ...(current?.configuration || {}),
        [fieldName]: value
      }
    }));
  }

  function updateRecord(index, fieldName, value) {
    setDocument((current) => {
      const nextRecords = Array.isArray(current?.[collectionKey])
        ? current[collectionKey].map((record) => ({ ...record }))
        : [];
      nextRecords[index] = { ...(nextRecords[index] || {}), [fieldName]: value };
      return { ...(current || {}), [collectionKey]: nextRecords };
    });
  }

  function addRecord() {
    setDocument((current) => ({
      ...(current || {}),
      [collectionKey]: [
        ...(Array.isArray(current?.[collectionKey]) ? current[collectionKey] : []),
        createRecord(fields)
      ]
    }));
  }

  function removeRecord(index) {
    setDocument((current) => ({
      ...(current || {}),
      [collectionKey]: (Array.isArray(current?.[collectionKey]) ? current[collectionKey] : [])
        .filter((_, recordIndex) => recordIndex !== index)
    }));
  }

  async function save() {
    if (!canManage || !document) return;
    setSaving(true);
    setError('');
    setMessage('');

    try {
      const response = await fetch(`${baseUrl}/document`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, document })
      });
      const body = await readJson(response);
      if (!response.ok) throw new Error(asErrorMessage(body, 'The document could not be saved.'));

      setDocument(body.document || document);
      setRevision(Number(body.revision || revision));
      setMessage(`Saved revision ${body.revision}.`);
      await load();
    } catch (saveError) {
      setError(saveError?.message || 'The document could not be saved.');
    } finally {
      setSaving(false);
    }
  }

  async function restore(revisionId) {
    if (!canManage || !revisionId) return;
    setSaving(true);
    setError('');
    setMessage('');

    try {
      const response = await fetch(`${baseUrl}/history/${revisionId}/restore`, { method: 'POST' });
      const body = await readJson(response);
      if (!response.ok) throw new Error(asErrorMessage(body, 'The revision could not be restored.'));
      setMessage(`Restored as revision ${body.revision}.`);
      await load();
    } catch (restoreError) {
      setError(restoreError?.message || 'The revision could not be restored.');
    } finally {
      setSaving(false);
    }
  }

  function renderField(field, value, onChange, keyPrefix) {
    const inputId = `${keyPrefix}-${field.name}`;
    const common = {
      id: inputId,
      disabled: !canManage || saving,
      value: value ?? '',
      onChange: (event) => onChange(event.target.value)
    };

    let control;
    if (field.type === 'checkbox') {
      control = (
        <input
          id={inputId}
          type="checkbox"
          disabled={!canManage || saving}
          checked={Boolean(value)}
          onChange={(event) => onChange(event.target.checked)}
        />
      );
    } else if (field.type === 'select') {
      control = (
        <select {...common}>
          {(field.options || []).map((option) => (
            <option value={option} key={option}>{option.replaceAll('_', ' ')}</option>
          ))}
        </select>
      );
    } else if (field.type === 'identity') {
      const selected = value ? identityLookup.get(value) : null;
      control = (
        <select {...common}>
          <option value="">Unassigned</option>
          {value && !selected ? <option value={value}>{value}</option> : null}
          {identityOptions.map((identity) => (
            <option value={identity.userId} key={identity.userId}>
              {identity.displayName || identity.email} — {identity.jobTitle || identity.teamName || 'ProjectPulse user'}
            </option>
          ))}
        </select>
      );
    } else if (field.type === 'textarea') {
      control = <textarea {...common} rows={4} placeholder={field.placeholder || ''} />;
    } else {
      const type = field.type === 'number' || field.type === 'date' || field.type === 'email' || field.type === 'url'
        ? field.type
        : 'text';
      control = (
        <input
          {...common}
          type={type}
          min={field.min ?? undefined}
          max={field.max ?? undefined}
          placeholder={field.placeholder || ''}
          onChange={(event) => onChange(type === 'number' ? Number(event.target.value) : event.target.value)}
        />
      );
    }

    return (
      <label className={field.type === 'checkbox' ? 'native-admin-field checkbox' : 'native-admin-field'} key={field.name} htmlFor={inputId}>
        <span>{field.label}{field.required ? ' *' : ''}</span>
        {control}
        {field.help ? <small>{field.help}</small> : null}
      </label>
    );
  }

  return (
    <section className="native-module-administration projectpulse-module-standard" data-module-administration={moduleNumber}>
      <div className="native-admin-heading">
        <div>
          <p className="eyebrow">Native edit and save</p>
          <h2>{schema?.moduleName || `Module ${moduleNumber}`} management</h2>
          <p className="section-copy">
            Saved changes are versioned in the ProjectPulse PostgreSQL application database and audited against the actual session. This surface does not activate Entra, Key Vault, AI-provider secrets, SMTP delivery, or any external system.
          </p>
        </div>
        <div className="native-admin-heading-actions">
          <span className="badge">Revision {revision}</span>
          <button type="button" className="secondary-action" onClick={() => void load()} disabled={loading || saving}>Refresh</button>
          <button type="button" className="primary-action" onClick={() => void save()} disabled={!canManage || loading || saving}>
            {saving ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </div>

      {schema?.access?.isViewAs ? (
        <div className="native-admin-banner warning">Administrator View-As is read-only. Exit preview to save changes.</div>
      ) : null}
      {!schema?.access?.canManage ? (
        <div className="native-admin-banner">Your current role can view this native record but cannot modify it.</div>
      ) : null}
      {error ? <div className="native-admin-banner error">{error}</div> : null}
      {message ? <div className="native-admin-banner success">{message}</div> : null}
      {loading ? <div className="native-admin-empty">Loading native administration data…</div> : null}

      {!loading && schema?.mode === 'configuration' ? (
        <div className="native-admin-form-grid">
          {fields.map((field) => renderField(
            field,
            configuration[field.name],
            (value) => updateConfiguration(field.name, value),
            `${moduleNumber}-configuration`
          ))}
        </div>
      ) : null}

      {!loading && schema?.mode === 'collection' ? (
        <>
          <div className="native-admin-collection-toolbar">
            <div>
              <strong>{records.length} saved record{records.length === 1 ? '' : 's'}</strong>
              <small>Maximum 1,000 records per module document.</small>
            </div>
            <button type="button" className="secondary-action" onClick={addRecord} disabled={!canManage || saving}>Add record</button>
          </div>

          {records.length === 0 ? (
            <div className="native-admin-empty">No records have been added yet.</div>
          ) : (
            <div className="native-admin-record-list">
              {records.map((record, index) => (
                <article className="native-admin-record-card" key={record.id || record.planId || record.vendorId || record.alignmentId || index}>
                  <div className="native-admin-record-heading">
                    <strong>Record {index + 1}</strong>
                    <button type="button" className="danger-action" onClick={() => removeRecord(index)} disabled={!canManage || saving}>Remove</button>
                  </div>
                  <div className="native-admin-form-grid">
                    {fields.map((field) => renderField(
                      field,
                      record?.[field.name],
                      (value) => updateRecord(index, field.name, value),
                      `${moduleNumber}-${index}`
                    ))}
                  </div>
                </article>
              ))}
            </div>
          )}
        </>
      ) : null}

      <div className="native-admin-history">
        <div className="native-admin-history-heading">
          <div>
            <p className="eyebrow">Revision history</p>
            <h3>Audited saved versions</h3>
          </div>
          <span className="badge">{history.length} revision{history.length === 1 ? '' : 's'}</span>
        </div>
        {history.length === 0 ? (
          <div className="native-admin-empty">No saved revisions yet.</div>
        ) : (
          <div className="native-admin-history-list">
            {history.map((item) => (
              <div className="native-admin-history-row" key={item.revisionId}>
                <div>
                  <strong>Revision {item.revision}</strong>
                  <span>{formatTimestamp(item.savedAt)} · {item.reason || 'save'}</span>
                </div>
                <button type="button" className="secondary-action" onClick={() => void restore(item.revisionId)} disabled={!canManage || saving}>Restore</button>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

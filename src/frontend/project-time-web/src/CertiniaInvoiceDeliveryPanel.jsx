import { useCallback, useEffect, useMemo, useState } from 'react';
import './certinia-invoice-delivery.css';

function text(value, fallback = '') {
  const normalized = String(value ?? '').trim();
  return normalized || fallback;
}

function formatDateTime(value) {
  if (!value) return 'Not available';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function readSessionToken() {
  for (const storage of [window.localStorage, window.sessionStorage]) {
    try {
      const raw = storage.getItem('projectPulseAuthSession');
      if (!raw) continue;
      const parsed = JSON.parse(raw);
      const token = text(
        parsed?.sessionToken
        || parsed?.token
        || parsed?.accessToken
        || parsed?.session?.token
      );
      if (token) return token;
    } catch {
      // Cookie-backed sessions continue without the optional header.
    }
  }
  return '';
}

async function api(path, options = {}) {
  const token = readSessionToken();
  const response = await fetch(path, {
    credentials: 'include',
    ...options,
    headers: {
      Accept: 'application/json',
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { 'X-ProjectPulse-Session': token } : {}),
      ...(options.headers || {})
    }
  });

  const raw = await response.text();
  let payload = null;

  try {
    payload = raw ? JSON.parse(raw) : null;
  } catch {
    payload = null;
  }

  if (!response.ok) {
    const detail = payload?.message || payload?.detail || raw || `HTTP ${response.status}`;
    throw new Error(`${path} returned HTTP ${response.status}: ${detail}`);
  }

  return payload;
}

function deliveryTone(status) {
  const normalized = text(status).toLowerCase();
  if (normalized === 'succeeded') return 'success';
  if (normalized === 'processing' || normalized === 'pending') return 'pending';
  if (normalized === 'failed' || normalized === 'dead_letter') return 'error';
  return 'neutral';
}

export default function CertiniaInvoiceDeliveryPanel({
  invoice,
  includeResourceNames,
  onIncludeResourceNamesChange
}) {
  const invoiceId = text(invoice?.header?.billingInvoiceId);
  const invoiceNumber = text(invoice?.header?.invoiceNumber, 'Invoice');
  const [documentFormat, setDocumentFormat] = useState('pdf');
  const [configuration, setConfiguration] = useState(null);
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(false);
  const [action, setAction] = useState({ running: '', error: '', message: '' });

  const load = useCallback(async () => {
    if (!invoiceId) return;
    setLoading(true);

    try {
      const [configurationResult, statusResult] = await Promise.all([
        api('/api/billing/certinia/configuration'),
        api(`/api/billing/invoices/${invoiceId}/certinia-status`)
      ]);
      setConfiguration(configurationResult?.configuration || null);
      setStatus(statusResult || null);
      const configuredDefault = text(configurationResult?.configuration?.defaultDocumentFormat).toLowerCase();
      if (configuredDefault === 'excel' || configuredDefault === 'pdf') {
        setDocumentFormat(configuredDefault);
      }
    } catch (error) {
      setAction({
        running: '',
        error: error instanceof Error ? error.message : 'Unable to load Certinia delivery status.',
        message: ''
      });
    } finally {
      setLoading(false);
    }
  }, [invoiceId]);

  useEffect(() => {
    setStatus(null);
    setAction({ running: '', error: '', message: '' });
    void load();
  }, [load]);

  const latest = status?.latest || null;
  const deliveries = Array.isArray(status?.deliveries) ? status.deliveries : [];
  const events = Array.isArray(status?.events) ? status.events : [];
  const canTransmit = configuration?.canTransmit === true;
  const selectedExtension = documentFormat === 'excel' ? 'xls' : 'pdf';

  const documentUrl = useMemo(() => {
    if (!invoiceId) return '';
    const query = new URLSearchParams({
      format: documentFormat,
      includeResourceNames: includeResourceNames ? 'true' : 'false'
    });
    return `/api/billing/invoices/${invoiceId}/document?${query.toString()}`;
  }, [documentFormat, includeResourceNames, invoiceId]);

  async function downloadDocument() {
    if (!documentUrl) return;
    setAction({ running: 'download', error: '', message: '' });

    try {
      const token = readSessionToken();
      const response = await fetch(documentUrl, {
        credentials: 'include',
        headers: {
          ...(token ? { 'X-ProjectPulse-Session': token } : {})
        }
      });

      if (!response.ok) {
        const detail = await response.text();
        throw new Error(`Document download returned HTTP ${response.status}: ${detail}`);
      }

      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') || '';
      const fileNameMatch = disposition.match(/filename\*?=(?:UTF-8''|\")?([^";]+)/i);
      const fileName = fileNameMatch
        ? decodeURIComponent(fileNameMatch[1].replaceAll('"', '').trim())
        : `${invoiceNumber}.${selectedExtension}`;
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = fileName;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      setAction({
        running: '',
        error: '',
        message: `${fileName} was generated from the immutable invoice snapshot.`
      });
    } catch (error) {
      setAction({
        running: '',
        error: error instanceof Error ? error.message : 'Unable to download the invoice artifact.',
        message: ''
      });
    }
  }

  async function queueOrSend(transmitNow) {
    if (!invoiceId) return;
    setAction({ running: transmitNow ? 'send' : 'queue', error: '', message: '' });

    try {
      const result = await api(`/api/billing/invoices/${invoiceId}/certinia/send`, {
        method: 'POST',
        body: JSON.stringify({
          documentFormat,
          includeResourceNames,
          transmitNow
        })
      });

      setAction({
        running: '',
        error: '',
        message: result?.message || (transmitNow
          ? 'Certinia delivery was processed.'
          : 'The invoice artifact was queued for Certinia delivery.')
      });
      await load();
    } catch (error) {
      setAction({
        running: '',
        error: error instanceof Error ? error.message : 'Unable to process the Certinia delivery request.',
        message: ''
      });
    }
  }

  if (!invoiceId) return null;

  return (
    <section className="certinia-delivery" aria-label="Certinia invoice delivery">
      <header className="certinia-delivery__header">
        <div>
          <p>MODULE 042 • Delivery foundation</p>
          <h3>Certinia invoice delivery</h3>
          <span>
            Generate an immutable PDF or Excel-compatible artifact, queue it safely,
            and transmit it only after the connector is explicitly configured.
          </span>
        </div>
        <button
          type="button"
          className="secondary-action"
          disabled={loading || Boolean(action.running)}
          onClick={() => void load()}
        >
          {loading ? 'Refreshing…' : 'Refresh status'}
        </button>
      </header>

      {action.error ? <div className="certinia-delivery__notice error" role="alert">{action.error}</div> : null}
      {action.message ? <div className="certinia-delivery__notice" role="status">{action.message}</div> : null}

      <div className="certinia-delivery__configuration">
        <article>
          <span>Connector</span>
          <strong>{text(configuration?.connectorStatus, 'Loading')}</strong>
          <small>{canTransmit ? 'Manual send is available.' : 'Queueing remains available; transmission is disabled.'}</small>
        </article>
        <article>
          <span>Latest delivery</span>
          <strong className={`certinia-delivery__status ${deliveryTone(latest?.deliveryStatus)}`}>
            {text(latest?.deliveryStatus, 'Not queued')}
          </strong>
          <small>{latest?.attemptCount ? `${latest.attemptCount} attempt(s)` : 'No transmission attempt'}</small>
        </article>
        <article>
          <span>Certinia reference</span>
          <strong>{text(latest?.externalId, 'Not assigned')}</strong>
          <small>{text(latest?.certiniaStatus, 'No remote status')}</small>
        </article>
      </div>

      <div className="certinia-delivery__controls">
        <label>
          <span>Delivery document</span>
          <select value={documentFormat} onChange={(event) => setDocumentFormat(event.target.value)}>
            <option value="pdf">PDF invoice</option>
            <option value="excel">Excel-compatible .xls invoice</option>
          </select>
        </label>

        <label className="certinia-delivery__privacy">
          <input
            type="checkbox"
            checked={Boolean(includeResourceNames)}
            onChange={(event) => onIncludeResourceNamesChange?.(event.target.checked)}
          />
          <span>
            Include engineer and PM/PC names
            <small>Names are hidden by default on downloads and Certinia payloads.</small>
          </span>
        </label>
      </div>

      <div className="certinia-delivery__actions">
        <button
          type="button"
          className="secondary-action"
          disabled={Boolean(action.running)}
          onClick={() => void downloadDocument()}
        >
          {action.running === 'download' ? 'Generating…' : `Download ${documentFormat === 'excel' ? 'Excel' : 'PDF'}`}
        </button>
        <button
          type="button"
          className="secondary-action"
          disabled={Boolean(action.running)}
          onClick={() => void queueOrSend(false)}
        >
          {action.running === 'queue' ? 'Queueing…' : 'Queue for Certinia'}
        </button>
        <button
          type="button"
          className="primary-action"
          disabled={!canTransmit || Boolean(action.running)}
          title={canTransmit
            ? 'Queue the immutable artifact and transmit it now.'
            : 'Set PROJECTPULSE_CERTINIA_ENABLED=true and complete the connector configuration before sending.'}
          onClick={() => void queueOrSend(true)}
        >
          {action.running === 'send' ? 'Sending…' : 'Send to Certinia'}
        </button>
      </div>

      {!canTransmit ? (
        <div className="certinia-delivery__safety">
          <strong>Safe foundation mode</strong>
          <span>
            PROJECTPULSE_CERTINIA_ENABLED remains false. Queue records are idempotent,
            and no Certinia transmission occurs until configuration is complete.
          </span>
        </div>
      ) : null}

      {deliveries.length ? (
        <details className="certinia-delivery__history">
          <summary>Delivery history ({deliveries.length})</summary>
          <div className="certinia-delivery__table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Queued</th>
                  <th>Format</th>
                  <th>Names</th>
                  <th>Status</th>
                  <th>Attempts</th>
                  <th>Certinia</th>
                </tr>
              </thead>
              <tbody>
                {deliveries.map((delivery) => (
                  <tr key={delivery.outboxId}>
                    <td>{formatDateTime(delivery.createdAt)}</td>
                    <td>{text(delivery.documentFormat).toUpperCase()}</td>
                    <td>{delivery.resourceNamesIncluded ? 'Included' : 'Hidden'}</td>
                    <td>{text(delivery.deliveryStatus)}</td>
                    <td>{delivery.attemptCount}</td>
                    <td>{text(delivery.externalId, text(delivery.certiniaStatus, 'Not sent'))}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      ) : null}

      {events.length ? (
        <details className="certinia-delivery__history">
          <summary>Immutable Certinia events ({events.length})</summary>
          <ol>
            {events.map((event) => (
              <li key={event.eventId}>
                <strong>{text(event.eventType).replaceAll('_', ' ')}</strong>
                <span>{event.reason}</span>
                <small>{formatDateTime(event.createdAt)}</small>
              </li>
            ))}
          </ol>
        </details>
      ) : null}
    </section>
  );
}

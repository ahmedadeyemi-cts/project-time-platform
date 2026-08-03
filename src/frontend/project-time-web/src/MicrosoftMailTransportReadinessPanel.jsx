import { useEffect, useMemo, useState } from 'react';
import './microsoft-mail-transport-readiness.css';

const ROUTE = 'entra-secret-administration';
const TEST_PATH = '/api/microsoft-integration/mail-runtime/test';
const DELIVERY_TEST_PATH = '/api/microsoft-integration/mail-runtime/test-delivery';
const DELIVERY_CONFIRMATION = 'SEND MODULE 065 TEST';
const ROUTE_ALIASES = Object.freeze({
  'microsoft-integration': ROUTE,
  'module-065': ROUTE,
  'global-mail-configuration': ROUTE
});

function currentRoute() {
  const raw = String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0] || 'dashboard';
  return ROUTE_ALIASES[raw] || raw;
}

function runtimeEnvironmentMode() {
  const host = window.location.hostname.toLowerCase();
  if (host.includes('-test.') || host.endsWith('.onenecklab.com') || host === 'localhost' || host === '127.0.0.1') return 'test';
  return 'production';
}

function sessionToken() {
  for (const storage of [window.localStorage, window.sessionStorage]) {
    for (const key of ['projectPulseAuthSession', 'ProjectPulseAuthSession', 'projectPulseSession']) {
      try {
        const session = JSON.parse(storage.getItem(key) || 'null');
        const token = session?.sessionToken || session?.token || session?.accessToken || session?.session_token || '';
        if (token && (!session?.expiresAt || Date.now() < Date.parse(session.expiresAt))) return token;
      } catch {
        // Continue through supported storage contracts.
      }
    }
  }
  return '';
}

function sessionEmail() {
  for (const storage of [window.localStorage, window.sessionStorage]) {
    for (const key of ['projectPulseAuthSession', 'ProjectPulseAuthSession', 'projectPulseSession']) {
      try {
        const session = JSON.parse(storage.getItem(key) || 'null');
        const email = String(session?.email || session?.userEmail || session?.actualEmail || '').trim().toLowerCase();
        if (email.includes('@')) return email;
      } catch {
        // Continue through supported storage contracts.
      }
    }
  }
  return '';
}

async function readJson(response) {
  const text = await response.text();
  if (!text.trim()) return {};
  try { return JSON.parse(text); } catch { return { message: text }; }
}

function title(value) {
  return String(value || 'Not reported').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function Fact({ label, value, tone = '' }) {
  return (
    <div className={`microsoft-mail-readiness-fact ${tone}`.trim()}>
      <span>{label}</span>
      <strong>{value || 'Not reported'}</strong>
    </div>
  );
}

export default function MicrosoftMailTransportReadinessPanel() {
  const [route, setRoute] = useState(currentRoute);
  const [environmentMode, setEnvironmentMode] = useState(runtimeEnvironmentMode);
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [deliveryRecipient, setDeliveryRecipient] = useState(sessionEmail);
  const [deliveryConfirmation, setDeliveryConfirmation] = useState('');
  const [deliveryTesting, setDeliveryTesting] = useState(false);
  const [deliveryResult, setDeliveryResult] = useState(null);
  const [deliveryError, setDeliveryError] = useState('');

  useEffect(() => {
    const synchronize = () => setRoute(currentRoute());
    const synchronizeEnvironment = (event) => {
      const next = String(event?.detail?.environmentMode || '').toLowerCase();
      if (next === 'test' || next === 'production') {
        setEnvironmentMode(next);
        setResult(null);
        setError('');
        setDeliveryResult(null);
        setDeliveryError('');
      }
    };
    window.addEventListener('hashchange', synchronize);
    window.addEventListener('pageshow', synchronize);
    window.addEventListener('projectpulse:microsoft-environment-changed', synchronizeEnvironment);
    return () => {
      window.removeEventListener('hashchange', synchronize);
      window.removeEventListener('pageshow', synchronize);
      window.removeEventListener('projectpulse:microsoft-environment-changed', synchronizeEnvironment);
    };
  }, []);

  const selectedProvider = useMemo(() => {
    const provider = result?.configuredProvider || result?.provider;
    if (!provider) return 'Not tested';
    if (provider === 'microsoft_graph') return 'Microsoft Graph';
    if (provider === 'smtp_relay') return 'Microsoft 365 SMTP relay';
    if (provider === 'locked') return 'Locked';
    return title(provider);
  }, [result]);

  async function runTest() {
    const token = sessionToken();
    if (!token) {
      setError('Sign in again before running the sender and transport readiness test.');
      return;
    }

    setTesting(true);
    setError('');
    setResult(null);
    try {
      const response = await fetch(TEST_PATH, {
        method: 'POST',
        cache: 'no-store',
        credentials: 'include',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
          'X-ProjectPulse-Session': token,
          'X-Project-Pulse-Session': token,
          'X-Session-Token': token,
          'X-ProjectPulse-Module-Number': '065'
        },
        body: JSON.stringify({ environmentMode })
      });
      const payload = await readJson(response);
      if (!response.ok) {
        const details = [
          payload?.message,
          payload?.status,
          payload?.correlationId ? `Correlation: ${payload.correlationId}` : '',
          payload?.expectedRedirectUri ? `Expected callback: ${payload.expectedRedirectUri}` : '',
          payload?.configuredRedirectUri ? `Configured callback: ${payload.configuredRedirectUri}` : ''
        ].filter(Boolean).join(' ');
        throw new Error(details || `Readiness test returned HTTP ${response.status}.`);
      }
      setResult(payload);
    } catch (testError) {
      setError(testError instanceof Error ? testError.message : 'The readiness test could not complete.');
    } finally {
      setTesting(false);
    }
  }

  async function runDeliveryTest() {
    const token = sessionToken();
    if (!token) {
      setDeliveryError('Sign in again before sending a governed Module 065 Test message.');
      return;
    }
    if (environmentMode !== 'test') {
      setDeliveryError('Select the Test environment. A real governed test message cannot be sent from the Production profile here.');
      return;
    }
    if (deliveryConfirmation !== DELIVERY_CONFIRMATION) {
      setDeliveryError(`Type ${DELIVERY_CONFIRMATION} exactly before sending.`);
      return;
    }

    setDeliveryTesting(true);
    setDeliveryError('');
    setDeliveryResult(null);
    try {
      const response = await fetch(DELIVERY_TEST_PATH, {
        method: 'POST',
        cache: 'no-store',
        credentials: 'include',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
          'X-ProjectPulse-Session': token,
          'X-Project-Pulse-Session': token,
          'X-Session-Token': token,
          'X-ProjectPulse-Module-Number': '065'
        },
        body: JSON.stringify({
          environmentMode,
          recipientEmail: deliveryRecipient,
          confirmation: deliveryConfirmation
        })
      });
      const payload = await readJson(response);
      if (!response.ok) {
        throw new Error([
          payload?.message,
          payload?.diagnosticCode,
          payload?.correlationId ? `Correlation: ${payload.correlationId}` : ''
        ].filter(Boolean).join(' ') || `Governed delivery test returned HTTP ${response.status}.`);
      }
      setDeliveryResult(payload);
      setDeliveryConfirmation('');
    } catch (testError) {
      setDeliveryError(testError instanceof Error ? testError.message : 'The governed Test message could not be sent.');
    } finally {
      setDeliveryTesting(false);
    }
  }

  if (route !== ROUTE) return null;

  return (
    <section className="microsoft-mail-readiness-panel" data-module="065" aria-label="Microsoft sender and transport readiness">
      <div className="microsoft-mail-readiness-heading">
        <div>
          <p className="eyebrow">MODULE 065 · NON-DELIVERY TEST</p>
          <h2>{environmentMode === 'production' ? 'Production' : 'Test'} sender and transport readiness</h2>
          <p>
            Verify the configured {environmentMode === 'production' ? 'Production' : 'Test'} Microsoft Graph or Microsoft 365 SMTP transport even when the recipient boundary intentionally keeps live delivery disabled. This check never sends an email and never returns a secret.
          </p>
        </div>
        <div className="microsoft-mail-readiness-controls">
          <label>
            <span>Environment</span>
            <select value={environmentMode} onChange={(event) => { setEnvironmentMode(event.target.value); setResult(null); setError(''); }}>
              <option value="test">Test</option>
              <option value="production">Production</option>
            </select>
          </label>
          <button type="button" className="primary-action" onClick={() => void runTest()} disabled={testing}>
            {testing ? 'Testing configuration…' : 'Test sender and transport'}
          </button>
        </div>
      </div>

      <div className="microsoft-mail-readiness-safety">
        <strong>No live message is sent.</strong>
        <span>The configured provider is tested independently from the live-delivery boundary, and the sanitized result is recorded as Module 008 audit evidence when available.</span>
      </div>

      {error ? <div className="microsoft-mail-readiness-message error">{error}</div> : null}
      {result ? (
        <>
          <div className={`microsoft-mail-readiness-message ${result.runtimeReady ? 'success' : 'warning'}`}>
            {result.message}
          </div>
          <div className="microsoft-mail-readiness-facts">
            <Fact label="Selected environment" value={title(result.environmentMode)} />
            <Fact label="Running environment" value={title(result.runtimeEnvironment)} />
            <Fact label="Configured provider" value={selectedProvider} />
            <Fact label="Active delivery provider" value={title(result.activeDeliveryProvider)} />
            <Fact label="Recipient boundary" value={title(result.recipientBoundary)} />
            <Fact label="Sender mailbox" value={result.senderMailbox} />
            <Fact label="Configured transport" value={result.configuredTransportReady ? 'Ready' : 'Attention required'} tone={result.configuredTransportReady ? 'ready' : 'attention'} />
            <Fact label="Live delivery" value={result.liveDeliveryEnabled ? 'Eligible' : 'Disabled'} />
            <Fact label="Delivery mode" value={title(result.deliveryMode)} />
            <Fact label="Live message sent" value={result.liveMessageSent ? 'Yes' : 'No'} />
            <Fact label="Secret returned" value={result.secretValuesReturned ? 'Yes' : 'No'} />
          </div>

          <div className="microsoft-mail-readiness-details">
            <article>
              <p className="eyebrow">Microsoft Graph</p>
              <h3>{title(result.graph?.status || 'Not selected')}</h3>
              <p>{result.graph?.message || 'Microsoft Graph was not the configured transport for this environment.'}</p>
              <ul>
                <li>Application authentication: {result.graph?.authenticationReady ? 'Ready' : 'Not ready'}</li>
                <li>Mail.Send application role: {result.graph?.mailSendRoleDeclared ? 'Present' : 'Not confirmed'}</li>
                <li>Directory roles: {result.graph?.directoryRolesDeclared ? 'Present' : 'Not confirmed'}</li>
                <li>Sender resolved: {result.graph?.senderResolved ? 'Yes' : 'No'}</li>
              </ul>
            </article>
            <article>
              <p className="eyebrow">Microsoft 365 SMTP</p>
              <h3>{title(result.smtp?.status || 'Not selected')}</h3>
              <p>{result.smtp?.message || 'Microsoft 365 SMTP was not the configured transport for this environment.'}</p>
              <ul>
                <li>Approved host: {result.smtp?.hostAccepted ? 'Yes' : 'No'}</li>
                <li>Network reachable: {result.smtp?.networkReachable ? 'Yes' : 'No'}</li>
                <li>Credential pair available: {result.smtp?.credentialAvailable ? 'Yes' : 'No'}</li>
                <li>Port: {result.smtp?.port || 'Not reported'}</li>
              </ul>
            </article>
          </div>
        </>
      ) : (
        <div className="microsoft-mail-readiness-empty">
          Select Test or Production, save that environment’s sender and transport configuration, then run the non-delivery readiness test.
        </div>
      )}

      <section className="microsoft-mail-governed-test" aria-label="Governed Module 065 Test delivery">
        <div className="microsoft-mail-governed-test-heading">
          <div>
            <p className="eyebrow">MODULE 065 · REAL TEST DELIVERY</p>
            <h3>Send one governed Microsoft 365 Test message</h3>
            <p>
              This is separate from the readiness check. It creates a durable dispatch and delivery-attempt record, keeps the general Test boundary unchanged, and permits only the signed-in user's own email or a server-side allowlisted recipient.
            </p>
          </div>
          <span>View-As blocked · No secrets returned</span>
        </div>

        <div className="microsoft-mail-governed-test-warning">
          <strong>A real email will be sent.</strong>
          <span>Type <code>{DELIVERY_CONFIRMATION}</code> exactly. The Test message contains no customer, project, financial, credential, token, or private-document data.</span>
        </div>

        <div className="microsoft-mail-governed-test-form">
          <label>
            <span>Recipient</span>
            <input
              type="email"
              value={deliveryRecipient}
              onChange={(event) => { setDeliveryRecipient(event.target.value); setDeliveryResult(null); setDeliveryError(''); }}
              placeholder="your.name@ussignal.com"
              autoComplete="email"
            />
            <small>Your signed-in address is used by default when available.</small>
          </label>
          <label>
            <span>Exact confirmation phrase</span>
            <input
              value={deliveryConfirmation}
              onChange={(event) => { setDeliveryConfirmation(event.target.value); setDeliveryError(''); }}
              placeholder={DELIVERY_CONFIRMATION}
              autoComplete="off"
            />
            <small>Case and spacing must match exactly.</small>
          </label>
          <button
            type="button"
            className="primary-action"
            onClick={() => void runDeliveryTest()}
            disabled={deliveryTesting || environmentMode !== 'test' || deliveryConfirmation !== DELIVERY_CONFIRMATION || !deliveryRecipient.trim()}
          >
            {deliveryTesting ? 'Sending governed Test message…' : 'Send Module 065 Test email'}
          </button>
        </div>

        {deliveryError ? <div className="microsoft-mail-readiness-message error">{deliveryError}</div> : null}
        {deliveryResult ? (
          <div className="microsoft-mail-governed-test-result">
            <div className={`microsoft-mail-readiness-message ${deliveryResult.sent ? 'success' : 'warning'}`}>
              {deliveryResult.message || (deliveryResult.sent ? 'The governed Test message was accepted by the configured provider.' : 'The governed Test message was not delivered.')}
            </div>
            <div className="microsoft-mail-readiness-facts">
              <Fact label="Delivery status" value={title(deliveryResult.deliveryStatus)} tone={deliveryResult.sent ? 'ready' : 'attention'} />
              <Fact label="Provider" value={title(deliveryResult.configuredProvider)} />
              <Fact label="Recipient" value={deliveryResult.recipientEmail} />
              <Fact label="Recipient authority" value={title(deliveryResult.recipientAuthorization)} />
              <Fact label="Dispatch ID" value={deliveryResult.dispatchId} />
              <Fact label="Provider message ID" value={deliveryResult.providerMessageIdPresent ? 'Recorded' : 'Not returned'} />
              <Fact label="General Test boundary" value={deliveryResult.generalTestBoundaryChanged ? 'Changed' : 'Unchanged'} />
              <Fact label="Audit evidence" value={deliveryResult.auditEvidenceRecorded ? 'Recorded' : 'Dispatch ledger only'} />
            </div>
          </div>
        ) : null}
      </section>
    </section>
  );
}

import { useEffect, useMemo, useState } from 'react';
import './microsoft-mail-transport-readiness.css';

const ROUTE = 'entra-secret-administration';
const TEST_PATH = '/api/microsoft-integration/mail-runtime/test';

function currentRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0] || 'dashboard';
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

async function readJson(response) {
  const text = await response.text();
  if (!text.trim()) return {};
  try { return JSON.parse(text); } catch { return { message: text }; }
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
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    const synchronize = () => setRoute(currentRoute());
    window.addEventListener('hashchange', synchronize);
    window.addEventListener('pageshow', synchronize);
    return () => {
      window.removeEventListener('hashchange', synchronize);
      window.removeEventListener('pageshow', synchronize);
    };
  }, []);

  const selectedProvider = useMemo(() => {
    if (!result?.provider) return 'Not tested';
    if (result.provider === 'microsoft_graph') return 'Microsoft Graph';
    if (result.provider === 'smtp_relay') return 'Microsoft 365 SMTP relay';
    if (result.provider === 'outbox_only') return 'Outbox only';
    return String(result.provider).replaceAll('_', ' ');
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
        body: '{}'
      });
      const payload = await readJson(response);
      if (!response.ok) {
        const details = [
          payload?.message,
          payload?.status,
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

  if (route !== ROUTE) return null;

  return (
    <section className="microsoft-mail-readiness-panel" data-module="065" aria-label="Microsoft sender and transport readiness">
      <div className="microsoft-mail-readiness-heading">
        <div>
          <p className="eyebrow">MODULE 065 · NON-DELIVERY TEST</p>
          <h2>Sender and transport readiness</h2>
          <p>
            Verify the active Test or Production mail metadata, Microsoft Graph application access,
            sender mailbox, or Microsoft 365 SMTP connectivity. This check never sends an email and never returns a secret.
          </p>
        </div>
        <button type="button" className="primary-action" onClick={() => void runTest()} disabled={testing}>
          {testing ? 'Testing configuration…' : 'Test sender and transport'}
        </button>
      </div>

      <div className="microsoft-mail-readiness-safety">
        <strong>No live message is sent.</strong>
        <span>The result is recorded as sanitized Module 008 audit evidence when the audit ledger is available.</span>
      </div>

      {error ? <div className="microsoft-mail-readiness-message error">{error}</div> : null}
      {result ? (
        <>
          <div className={`microsoft-mail-readiness-message ${result.runtimeReady ? 'success' : 'warning'}`}>
            {result.message}
          </div>
          <div className="microsoft-mail-readiness-facts">
            <Fact label="Environment" value={result.environmentMode} />
            <Fact label="Provider" value={selectedProvider} />
            <Fact label="Recipient boundary" value={result.recipientBoundary} />
            <Fact label="Sender mailbox" value={result.senderMailbox} />
            <Fact label="Runtime readiness" value={result.runtimeReady ? 'Ready' : 'Attention required'} tone={result.runtimeReady ? 'ready' : 'attention'} />
            <Fact label="Delivery mode" value={result.deliveryMode} />
            <Fact label="Live message sent" value={result.liveMessageSent ? 'Yes' : 'No'} />
            <Fact label="Secret returned" value={result.secretValuesReturned ? 'Yes' : 'No'} />
          </div>

          <div className="microsoft-mail-readiness-details">
            <article>
              <p className="eyebrow">Microsoft Graph</p>
              <h3>{result.graph?.status || 'Not selected'}</h3>
              <p>{result.graph?.message || 'Microsoft Graph was not the selected transport.'}</p>
              <ul>
                <li>Application authentication: {result.graph?.authenticationReady ? 'Ready' : 'Not ready'}</li>
                <li>Mail.Send application role: {result.graph?.mailSendRoleDeclared ? 'Present' : 'Not confirmed'}</li>
                <li>Directory roles: {result.graph?.directoryRolesDeclared ? 'Present' : 'Not confirmed'}</li>
                <li>Sender resolved: {result.graph?.senderResolved ? 'Yes' : 'No'}</li>
              </ul>
            </article>
            <article>
              <p className="eyebrow">Microsoft 365 SMTP</p>
              <h3>{result.smtp?.status || 'Not selected'}</h3>
              <p>{result.smtp?.message || 'Microsoft 365 SMTP was not the selected transport.'}</p>
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
          Run the test after saving Module 065. The check reads only the active runtime configuration.
        </div>
      )}
    </section>
  );
}

import { useEffect, useState } from 'react';

const path = '/api/microsoft-integration/teams';
async function request(url, init) {
  let session;
  try { session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null'); } catch { session = null; }
  const response = await fetch(url, { credentials: 'same-origin', ...init, headers: {
    ...(session?.sessionToken ? { Authorization: `Bearer ${session.sessionToken}`, 'X-ProjectPulse-Session': session.sessionToken } : {}), ...init?.headers
  } });
  const body = await response.json();
  if (!response.ok) throw new Error(body.message || 'Teams configuration could not be loaded.');
  return body;
}
export default function MicrosoftTeamsNotificationPanel({ environment }) {
  const [state, setState] = useState(null);
  const [draft, setDraft] = useState(null);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [recipient, setRecipient] = useState('');
  const [confirmation, setConfirmation] = useState('');
  async function reload() {
    const next = await request(path);
    setState(next); setDraft(next.configuration);
  }
  useEffect(() => {
    let disposed = false;
    setState(null); setDraft(null); setMessage('');
    request(path).then(next => { if (!disposed) { setState(next); setDraft(next.configuration); } }).catch(error => { if (!disposed) setMessage(error.message); });
    return () => { disposed = true; };
  }, [environment]);
  const wrongEnvironment = draft && draft.environment !== environment;
  const disabled = busy || !draft || state?.readOnly || wrongEnvironment;
  async function act(test) {
    setBusy(true); setMessage('');
    try {
      const result = await request(test ? `${path}/test-delivery` : path, { method: test ? 'POST' : 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(test ? { recipient, confirmation } : { enabled: draft.enabled, teamsAppId: draft.teamsAppId || null, expectedRevision: draft.revision }) });
      setMessage(result.message); if (test) setConfirmation('');
    } catch (error) { setMessage(error.message); }
    finally { try { await reload(); } catch (error) { setMessage(previous => `${previous} ${error.message}`); } setBusy(false); }
  }
  return <article className="microsoft-integration-card wide" data-module="065-teams">
    <p className="eyebrow">MICROSOFT TEAMS</p><h2>Teams notifications</h2>
    <p>Send a Teams activity notification alongside email for submitted time and project alerts. Uses this environment’s saved Microsoft services connection.</p>
    <p>{state?.message}</p>
    {wrongEnvironment && <p role="status">Open the {environment} application to configure its Teams connection. This page is running in {draft.environment}.</p>}
    {draft && <>
      <label><input type="checkbox" checked={draft.enabled} disabled={disabled} onChange={event => setDraft({ ...draft, enabled: event.target.checked })} /> Enable Teams delivery</label>
      <label>Teams app ID<input value={draft.teamsAppId || ''} disabled={disabled} onChange={event => setDraft({ ...draft, teamsAppId: event.target.value.trim() })} placeholder="App ID from the installed Teams app manifest" /></label>
      <button type="button" disabled={disabled} onClick={() => act(false)}>Save Teams configuration</button>
      <p>Scheduled notifications follow the existing recipient boundary. Test-only does not send automatically.</p>
      <details><summary>Send a Teams test</summary>
        <p>A real notification will be sent to this tenant user. SuperAdmins may choose another tenant user; other administrators may test their own address.</p>
        <label>Recipient<input type="email" value={recipient} disabled={disabled} onChange={event => setRecipient(event.target.value)} /></label>
        <label>Type SEND TEAMS TEST<input value={confirmation} disabled={disabled} onChange={event => setConfirmation(event.target.value)} /></label>
        <button type="button" disabled={disabled || draft.environment !== 'test' || confirmation !== 'SEND TEAMS TEST' || !recipient || !draft.enabled} onClick={() => act(true)}>Send Teams test</button>
      </details>
    </>}
    <p role="status" aria-live="polite">{busy ? 'Working…' : message}</p>
    {state?.deliveries?.length > 0 && <details><summary>Recent Teams delivery results</summary><table><thead><tr><th>Recipient</th><th>Status</th><th>Diagnostic</th><th>Updated</th></tr></thead><tbody>{state.deliveries.map((row, index) => <tr key={index}><td>{row.recipient}</td><td>{row.status}</td><td>{row.diagnosticCode}</td><td>{new Date(row.updatedAt).toLocaleString()}</td></tr>)}</tbody></table></details>}
  </article>;
}

import { useCallback, useEffect, useState } from 'react';

export default function CelarAiAvailabilityCard() {
  const [state, setState] = useState({ data: null, error: '', checking: false });
  const read = useCallback(async () => {
    try {
      const response = await fetch('/api/ai-configuration/private-model', { credentials: 'include', cache: 'no-store' });
      const data = await response.json();
      if (!response.ok) throw new Error(data.message || 'Celar AI health could not be retrieved.');
      setState(current => ({ ...current, data, error: '' }));
    } catch {
      setState(current => ({ ...current, error: 'Celar AI health is unavailable. Last recorded evidence may be stale.' }));
    }
  }, []);
  useEffect(() => {
    void read();
    const timer = setInterval(read, 60000);
    return () => clearInterval(timer);
  }, [read]);
  async function check() {
    setState(current => ({ ...current, checking: true, error: '' }));
    try {
      const response = await fetch('/api/ai-configuration/private-model/test', { method: 'POST', credentials: 'include' });
      const result = await response.json();
      if (!response.ok) throw new Error(result.message || 'Celar AI check failed.');
      await read();
    } catch {
      setState(current => ({ ...current, error: 'Celar AI could not be checked. Review its private endpoint and authentication settings below.' }));
    } finally {
      setState(current => ({ ...current, checking: false }));
    }
  }
  const readiness = state.data?.productionReadiness;
  const ready = !state.error && readiness?.privateModelReady === true;
  const probe = readiness?.privateTargetAvailability;
  return <section className="ai-provider-center__provider" aria-label="Celar AI availability monitor">
    <div className="ai-provider-center__provider-heading">
      <div><h2>Celar AI availability</h2><p>Internal private inference provider</p></div>
      <strong role="status">{state.checking ? 'Checking…' : !state.data ? 'Not yet verified' : ready ? 'Available' : 'Unavailable or unverified'}</strong>
    </div>
    <p>Checked automatically by the server on its provider-health schedule. This panel refreshes every minute.</p>
    <p>Last verified: {probe?.verifiedAt ? new Date(probe.verifiedAt).toLocaleString() : 'Not recorded'}. Diagnostic: {probe?.lastFailureCode || 'None reported'}.</p>
    {(!ready && state.data || state.error) && <p role="alert">{state.error || 'Celar AI is not confirmed available. Check its connection before relying on private inference.'}</p>}
    <button type="button" onClick={check} disabled={state.checking || !state.data?.profile?.configured}>{state.checking ? 'Checking Celar AI…' : 'Check Celar AI availability'}</button>
    <p>Document-storage readiness is separate from inference availability. Provider checks do not send email or Teams messages.</p>
  </section>;
}

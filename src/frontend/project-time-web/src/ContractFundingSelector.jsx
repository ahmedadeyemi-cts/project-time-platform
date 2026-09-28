import { useEffect, useState } from 'react';

const money = (value) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value || 0);

export default function ContractFundingSelector({ customerId, contractType, value, onChange, load }) {
  const [state, setState] = useState({ loading: false, rows: [], error: '' });
  const [reload, setReload] = useState(0);
  useEffect(() => {
    let active = true;
    onChange({ contractId: '', rate: '', customerId, contractType });
    if (!customerId) { setState({ loading: false, rows: [], error: '' }); return; }
    setState({ loading: true, rows: [], error: '' });
    load(`/api/contracts/prepaid/eligible?clientId=${encodeURIComponent(customerId)}`)
      .then((data) => {
        if (active) setState({ loading: false, rows: (data.contracts || []).filter((row) =>
          contractType === 'Fixed Price' ? row.eligibleFixedPrice : contractType === 'Time and Material' ? row.eligibleTm : false), error: '' });
      })
      .catch((error) => { if (active) setState({ loading: false, rows: [], error: error.message }); });
    return () => { active = false; };
  }, [customerId, contractType, reload]);
  const selected = state.rows.find((row) => row.contractId === value.contractId);
  return <fieldset className="work-register-edit-grid" aria-label="Contract funding">
    <legend>Fund this work from an existing contract (optional)</legend>
    <p>Select a customer contract to fund labor for this T&amp;M or Fixed Price project. This does not change the project’s billing type or create an invoice.</p>
    <label>Funding contract
      <select value={value.contractId || ''} disabled={state.loading || !customerId || !!state.error}
        onChange={(event) => onChange({ ...value, contractId: event.target.value })}>
        <option value="">No existing contract funding</option>
        {state.rows.map((row) => <option key={row.contractId} value={row.contractId}>
          {row.engagementName} {row.poQuote ? `· ${row.poQuote}` : ''} · {money(row.remainingBalance)} remaining
        </option>)}
      </select>
    </label>
    {state.loading ? <p role="status">Loading eligible customer contracts…</p> : null}
    {state.error ? <p role="alert">Unable to load funding contracts: {state.error} <button type="button" onClick={() => setReload((n) => n + 1)}>Retry</button></p> : null}
    {!state.loading && !state.error && customerId && !state.rows.length ? <p>No active, funded contracts support this customer and billing type.</p> : null}
    {selected ? <>
      <label>Contract drawdown rate ($ per hour)
        <input type="number" min="0.01" step="0.01" required value={value.rate || ''}
          onChange={(event) => onChange({ ...value, rate: event.target.value })} />
      </label>
      <p>Remaining: <strong>{money(selected.remainingBalance)}</strong>. Expires {String(selected.contractEndDate).slice(0, 10)}. Submitted labor reserves funds; required approval moves it to approved usage. Draft and returned time do not consume funds. Confirm the agreed drawdown rate, including for Fixed Price work.</p>
    </> : null}
  </fieldset>;
}

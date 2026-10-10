import { useEffect, useRef, useState } from 'react';
import { requestHeaders } from './UnifiedProjectFinancialWorkspace.jsx';
import { EFFECTIVE_ROLE_AUTHORITY_EVENTS } from './effective-role-authority.js';
import './accounting-engagement.css';
const today = () => { const d=new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`; };
const money = n => n == null ? 'Not recorded' : Number(n).toLocaleString(undefined, { style: 'currency', currency: 'USD' });
const reports = [['accounting_engagement_summary','Engagement summary'],['accounting_invoice_ledger','Invoices'],['accounting_milestone_detail','Milestones'],['accounting_billable_time','Billable time'],['accounting_monthly_revenue','Monthly revenue']];
export default function AccountingEngagementPanel(props) {
  const [generation, setGeneration] = useState(0);
  useEffect(() => {
    const changed = () => setGeneration(n => n + 1);
    const events = [...new Set([...EFFECTIVE_ROLE_AUTHORITY_EVENTS, 'projectpulse:auth-session-ready', 'projectpulse:auth-session-cleared'])];
    events.forEach(e => window.addEventListener(e, changed));
    return () => events.forEach(e => window.removeEventListener(e, changed));
  }, []);
  return <AccountingForm key={`${generation}:${props.projectId}`} {...props} />;
}
function AccountingForm({ projectId, projectName, onSaved }) {
  const [data, setData] = useState(null), [error, setError] = useState(''), [message, setMessage] = useState('');
  const [refresh, setRefresh] = useState(0), [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false);
  const [form, setForm] = useState({ action:'profile', amount:'', date:today(), reference:'', reason:'', kind:'revenue', name:'', salesforceAccountId:'', targetId:'' });
  const [period, setPeriod] = useState({ dateFrom:'', dateTo:'' });
  const operation = useRef(null), flight = useRef(false), alive = useRef(true), abortRef = useRef(null);
  const endpoint = `/api/billing/projects/${encodeURIComponent(projectId)}/accounting`;
  useEffect(() => { alive.current=true;return () => { alive.current=false;abortRef.current?.abort(); }; }, []);
  useEffect(() => {
    const abort = new AbortController();setData(null);setError('');
    fetch(endpoint,{credentials:'include',cache:'no-store',headers:requestHeaders(),signal:abort.signal})
      .then(async r => { const body=await r.json();if(!r.ok) throw new Error(body.message || 'Accounting access requires Finance, Billing, Accounting or an administrator.');
        if(body.projectId!==projectId) throw new Error('Engagement scope could not be verified.');
        if(!abort.signal.aborted) {setData(body);setForm(f=>({...f,salesforceAccountId:body.summary?.[0]?.salesforceAccountId || ''}));} })
      .catch(e => {if(!abort.signal.aborted)setError(e.message);});
    return () => abort.abort();
  },[endpoint,projectId,refresh]);
  function change(key,value) {if(flight.current || uncertain)return;operation.current=null;setForm(f=>({...f,[key]:value,...(key==='action'?{targetId:'',amount:'',reference:'',reason:''}:{})}));}
  async function save(e) {
    e.preventDefault();if(flight.current || !data?.canManage)return;
    operation.current ||= {...form,operationId:crypto.randomUUID(),amount:Number(form.amount || 0),targetId:form.targetId || '00000000-0000-0000-0000-000000000000',expectedVersion:data.summary?.[0]?.accountingVersion || ''};
    flight.current=true;setBusy(true);setMessage('');const abort=new AbortController();abortRef.current=abort;
    const timer=setTimeout(()=>abort.abort(),30000);
    try {
      const r=await fetch(endpoint,{method:'POST',credentials:'include',headers:{...requestHeaders(),'Content-Type':'application/json'},body:JSON.stringify(operation.current),signal:abort.signal});
      const body=await r.json();if(!alive.current)return;
      if(!r.ok) {if(r.status>=500)throw new Error();setMessage(body.message || 'Save rejected.');operation.current=null;setUncertain(false);return;}
      if(!['accounting_recorded','already_recorded'].includes(body.status))throw new Error();
      operation.current=null;setUncertain(false);setMessage('Accounting record saved.');setRefresh(n=>n+1);
      setForm(f=>({...f,amount:'',reference:'',reason:'',name:'',targetId:''}));Promise.resolve().then(() => onSaved?.()).catch(() => {});
    } catch {if(alive.current){setUncertain(true);setMessage('Save result is unconfirmed. Retry the same request, or reload and review the records before entering another.');}}
    finally {clearTimeout(timer);flight.current=false;if(alive.current)setBusy(false);}
  }
  async function download(code) {
    if(flight.current)return;flight.current=true;setBusy(true);setMessage('');
    try {
      const r=await fetch('/api/enterprise-reporting/run',{method:'POST',credentials:'include',headers:{...requestHeaders(),'Content-Type':'application/json'},body:JSON.stringify({reportCode:code,projectId,limit:5000,...Object.fromEntries(Object.entries(period).filter(([,v])=>v))})});
      const body=await r.json();if(!r.ok)throw new Error(body.message || 'Report failed.');
      if(body.result?.resultStatus==='partial' || body.result?.resultStatus==='source_unavailable')throw new Error(body.result.message);
      const id=body.runId || body.run?.runId;if(!id)throw new Error('Report export was not created.');
      const exportResponse=await fetch(`/api/enterprise-reporting/runs/${encodeURIComponent(id)}/export?format=xlsx`,{credentials:'include',headers:requestHeaders()});
      if(!exportResponse.ok)throw new Error('Export is unavailable.');const blob=await exportResponse.blob();const url=URL.createObjectURL(blob);
      const a=document.createElement('a');a.href=url;a.download=`${code}.xlsx`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
      if(alive.current)setMessage('Excel report downloaded.');
    } catch(e){if(alive.current)setMessage(e.message);}finally{flight.current=false;if(alive.current)setBusy(false);}
  }
  const s=data?.summary?.[0];
  return <details className="m042-accounting"><summary>Accounting reports, milestones and monthly revenue</summary>
    <h2>Accounting · {projectName}</h2>
    <p>Maintain verified contract amounts, milestone acceptance, prepaid activity and Finance-approved revenue entries. Amounts use USD. Invoice totals exclude drafts and cancellations; recorded external billing is counted once. Revenue is maintained separately from invoicing.</p>
    {error ? <p role="alert">{error}</p> : !data ? <p role="status">Loading accounting records…</p> : <>
      <dl className="m042-reference-summary"><div><dt>Contract total</dt><dd>{money(s?.totalContractAmount)}</dd></div><div><dt>Total invoiced</dt><dd>{money(s?.totalInvoiceAmount)}</dd></div><div><dt>Prepaid balance</dt><dd>{money(s?.prepaidBalance)}</dd></div><div><dt>Recognized revenue</dt><dd>{money(s?.recognizedToDate)}</dd></div></dl>
      <p>{s?.dataStatus}. Salesforce Account ID: {s?.salesforceAccountId || 'Not recorded'}. Contract: {s?.contractNumber || 'Not recorded'}.</p>
      <form onSubmit={save}><fieldset disabled={busy || uncertain}><legend>Add accounting evidence</legend><div className="m042-accounting-fields">
        <label>Action<select value={form.action} onChange={e=>change('action',e.target.value)}><option value="profile">Set contract / customer identity</option><option value="entry">Record financial entry</option><option value="milestone">Schedule fixed-price milestone</option><option value="accept">Record milestone acceptance</option><option value="rate">Confirm dated time rate</option></select></label>
        {form.action==='profile' && <label>Salesforce Account ID<input maxLength={18} value={form.salesforceAccountId} onChange={e=>change('salesforceAccountId',e.target.value)} placeholder="15 or 18 characters; optional" /></label>}
        {form.action==='entry' && <label>Entry type<select value={form.kind} onChange={e=>change('kind',e.target.value)}><option value="revenue">Recognized revenue</option><option value="contract_change">Approved contract change</option><option value="prepaid_funding">Prepaid funding</option><option value="prepaid_usage">Prepaid usage</option></select></label>}
        {form.action==='milestone' && <label>Milestone name<input required minLength={2} maxLength={200} value={form.name} onChange={e=>change('name',e.target.value)} /></label>}
        {form.action==='accept' && <label>Milestone<select required value={form.targetId} onChange={e=>change('targetId',e.target.value)}><option value="">Select scheduled milestone</option>{data.milestones.filter(m=>m.status==='scheduled').map(m=><option key={m.milestoneId} value={m.milestoneId}>{m.milestone} · {money(m.milestoneAmount)}</option>)}</select></label>}
        {form.action==='rate' && <label>Time entry<select required value={form.targetId} onChange={e=>change('targetId',e.target.value)}><option value="">Select unbilled approved time</option>{data.time.filter(t=>t.billingStatus==='unbilled' && ['manager_approved','project_approved','project_validated','pm_approved','accounting_ready','reconciled','locked'].includes(t.approvalStatus)).map(t=><option key={t.timeEntryId} value={t.timeEntryId}>{t.workDate} · {t.employee} · {t.hours} hours</option>)}</select></label>}
        {form.action!=='accept' && <label>{form.action==='profile'?'Original contract amount':form.action==='rate'?'Dated hourly rate':'Amount'} (USD)<input type="number" required step="0.01" value={form.amount} onChange={e=>change('amount',e.target.value)} /></label>}
        <label>{form.action==='milestone'?'Scheduled billing date':form.action==='accept'?'Acceptance date':form.action==='entry'?'Accounting effective date':'Evidence date'}<input type="date" required value={form.date} onChange={e=>change('date',e.target.value)} /></label>
        <label>{form.action==='profile'?'Contract / SOW number':'Approval / source reference'}<input required minLength={2} maxLength={form.action==='profile'?200:500} value={form.reference} onChange={e=>change('reference',e.target.value)} /></label>
        <label>Audit reason<textarea required minLength={5} maxLength={2000} value={form.reason} onChange={e=>change('reason',e.target.value)} /></label>
      </div><p>Revenue and other financial entries preserve their history. Enter a signed adjusting entry to correct a prior amount. Revenue dates may be in prior months; Finance must approve the recognition basis.</p></fieldset>
      <button disabled={busy} type="submit">{busy?'Saving…':uncertain?'Retry same accounting request':'Save accounting record'}</button>
      </form>
      <div className="m042-accounting-export"><label>From<input type="date" value={period.dateFrom} onChange={e=>setPeriod(p=>({...p,dateFrom:e.target.value}))} /></label><label>Through<input type="date" value={period.dateTo} onChange={e=>setPeriod(p=>({...p,dateTo:e.target.value}))} /></label>{reports.map(([code,name])=><button type="button" disabled={busy} key={code} onClick={()=>download(code)}>Export {name}</button>)}</div>
      <p>Engagement summary is current as of today. Date filters apply to milestone schedules, work dates and recognition months. Blank time rates require confirmation; invoiced rates use saved invoice snapshots. Fixed-price time values describe effort and do not add charges to milestone invoices.</p>
      <h3>Milestones</h3><div className="m042-accounting-table"><table><thead><tr><th>Milestone</th><th>Amount</th><th>Scheduled</th><th>Accepted</th><th>Status</th><th>Invoice</th></tr></thead><tbody>{data.milestones.map(m=><tr key={m.milestoneId}><td>{m.milestone}</td><td>{money(m.milestoneAmount)}</td><td>{m.scheduledDate}</td><td>{m.acceptanceDate || 'Pending'}</td><td>{m.status}</td><td>{m.invoiceNumber || 'Not invoiced'}</td></tr>)}</tbody></table></div>
      <h3>Monthly recognized revenue</h3><div className="m042-accounting-table"><table><thead><tr><th>Period</th><th>Recognized</th><th>Cumulative</th><th>Approval references</th></tr></thead><tbody>{data.revenue.map(m=><tr key={m.accountingPeriod}><td>{m.accountingPeriod.slice(0,7)}</td><td>{money(m.recognizedAmount)}</td><td>{money(m.recognizedToDate)}</td><td>{m.approvalReferences}</td></tr>)}</tbody></table></div>
      <details><summary>Financial entry history</summary><div className="m042-accounting-table"><table><thead><tr><th>Date</th><th>Type</th><th>Amount</th><th>Reference</th><th>Reason</th></tr></thead><tbody>{data.entries.map(e=><tr key={e.entry_id}><td>{e.effective_date}</td><td>{e.kind}</td><td>{money(e.amount)}</td><td>{e.reference}</td><td>{e.reason}</td></tr>)}</tbody></table></div></details>
    </>}
    <button type="button" disabled={busy} onClick={()=>{operation.current=null;setUncertain(false);setRefresh(n=>n+1);}}>Reload accounting records</button>
    {message && <p role="status">{message}</p>}
  </details>;
}

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './enterprise-governance-centers.css';

const BASE = '/api/lab-equipment-tracker';
const TABS = [
  ['equipment', 'Equipment'], ['ipam', 'IP Address Management'], ['connections', 'Cabling & Connections'],
  ['rack', 'Rack View'], ['imports', 'Imports & Review'], ['history', 'History & Audit']
];
const EMPTY_EQUIPMENT = { managingTeam: '', name: '', type: 'network appliance', manufacturer: '', model: '', serialNumber: '', assetTag: '', hostname: '', macAddress: '', location: '', pod: '', physicalLocation: '', rack: '', rackUnitStart: '', rackUnitHeight: 1, status: 'active', supportContract: '', warrantyExpiresOn: '', notes: '' };
const EMPTY_IP = { managingTeam: '', location: '', pod: '', zone: 'management', addressFamily: 4, network: '', usableRange: '', ipAddress: '', prefixLength: 24, gateway: '', vlanId: '', vlanName: '', vrf: '', status: 'available', equipmentId: '', interfaceName: '', hostname: '', purpose: '', reservationExpiresAt: '' };
const EMPTY_CONNECTION = { location: '', pod: '', fromEquipmentId: '', fromInterface: '', toEquipmentId: '', toInterface: '', media: 'fiber', cableLabel: '', vlanId: '', ipAddress: '', status: 'active', notes: '' };

function tokenHeaders(authSession, json = false) {
  const token = authSession?.sessionToken || authSession?.token || authSession?.accessToken || '';
  return { ...(token ? { Authorization: `Bearer ${token}`, 'X-ProjectPulse-Session': token } : {}), ...(json ? { 'Content-Type': 'application/json' } : {}) };
}
async function readResponse(response) {
  const text = await response.text(); let body = {};
  try { body = text ? JSON.parse(text) : {}; } catch { body = { message: text }; }
  if (!response.ok) {
    const message = body.message || body.code || `Request failed (${response.status}).`;
    throw new Error(body.correlationId ? `${message} Reference ${body.correlationId}.` : message);
  }
  return body;
}
function Badge({ value }) { const label = String(value || 'unknown').replaceAll('_', ' '); return <span className={`eg-badge ${String(value || '').toLowerCase()}`}>{label}</span>; }
function Empty({ children = 'No records match the current scope and filters.' }) { return <div className="eg-empty">{children}</div>; }
function Modal({ title, children, onClose }) { return <div className="eg-modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}><section className="eg-modal" role="dialog" aria-modal="true" aria-label={title}><header><h2>{title}</h2><button className="eg-button" type="button" onClick={onClose}>Close</button></header><div className="eg-modal-body">{children}</div></section></div>; }
function Field({ label, full = false, children }) { return <div className={`eg-field${full ? ' full' : ''}`}><label>{label}</label>{children}</div>; }
function Input({ value, onChange, ...props }) { return <input className="eg-input" value={value ?? ''} onChange={(event) => onChange(event.target.value)} {...props} />; }
function Select({ value, onChange, children, ...props }) { return <select className="eg-select" value={value ?? ''} onChange={(event) => onChange(event.target.value)} {...props}>{children}</select>; }

export default function LabEquipmentTrackerCenter({ authSession }) {
  const [tab, setTab] = useState('equipment');
  const [access, setAccess] = useState(null);
  const [summary, setSummary] = useState(null);
  const [data, setData] = useState({ equipment: [], allocations: [], connections: [], racks: [], imports: [], history: [] });
  const [filters, setFilters] = useState({ search: '', status: '', location: '', pod: '' });
  const [modal, setModal] = useState('');
  const [form, setForm] = useState(EMPTY_EQUIPMENT);
  const [importPreview, setImportPreview] = useState(null);
  const [busy, setBusy] = useState(false);
  const [loadState, setLoadState] = useState('loading');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [teams, setTeams] = useState([]);
  const importFileRef = useRef(null);

  const request = useCallback(async (path, options = {}) => {
    const response = await fetch(`${BASE}${path}`, { ...options, headers: { ...tokenHeaders(authSession, options.body && !(options.body instanceof FormData)), ...(options.headers || {}) } });
    return readResponse(response);
  }, [authSession]);

  const refresh = useCallback(async () => {
    setBusy(true); setLoadState('loading'); setError('');
    const query = new URLSearchParams(Object.entries(filters).filter(([, value]) => value !== '')).toString();
    try {
      const accessBody = await request('/access');
      setAccess(accessBody);
      if (!accessBody.dataReady) {
        setLoadState('blocked');
        setError(accessBody.message || 'Module 081 data foundations are not ready.');
        return;
      }
      const connectionQuery = new URLSearchParams(Object.entries({ location: filters.location, pod: filters.pod }).filter(([, value]) => value !== '')).toString();
      const rackQuery = new URLSearchParams(Object.entries({ location: filters.location }).filter(([, value]) => value !== '')).toString();
      const surfaces = [
        ['summary', request('/summary')],
        ['teams', request('/teams')],
        ['equipment', request(query ? `/equipment?${query}` : '/equipment')],
        ['allocations', request(query ? `/ip-addresses?${query}` : '/ip-addresses')],
        ['connections', request(connectionQuery ? `/connections?${connectionQuery}` : '/connections')],
        ['racks', request(rackQuery ? `/rack-view?${rackQuery}` : '/rack-view')],
        ['history', request('/history?limit=250')],
        ...(accessBody.permissions?.canImport ? [['imports', request('/imports?limit=100')]] : [])
      ];
      const results = await Promise.allSettled(surfaces.map(([, pending]) => pending));
      const failures = [];
      const loaded = {};
      results.forEach((result, index) => {
        const [surface] = surfaces[index];
        if (result.status === 'rejected') {
          failures.push(`${surface}: ${result.reason?.message || 'unavailable'}`);
          return;
        }
        const payload = result.value || {};
        if (surface === 'summary') setSummary(payload);
        if (surface === 'teams') setTeams(payload.teams || []);
        if (surface === 'equipment') loaded.equipment = payload.equipment || [];
        if (surface === 'allocations') loaded.allocations = payload.allocations || [];
        if (surface === 'connections') loaded.connections = payload.connections || [];
        if (surface === 'racks') loaded.racks = payload.racks || [];
        if (surface === 'history') loaded.history = payload.history || [];
        if (surface === 'imports') loaded.imports = payload.imports || [];
      });
      setData((current) => ({ ...current, ...loaded }));
      if (failures.length) {
        setError(`Some Module 081 views are temporarily unavailable. ${failures.join(' · ')}`);
      }
      setLoadState('ready');
    } catch (caught) { setAccess(null); setLoadState('unavailable'); setError(caught.message); }
    finally { setBusy(false); }
  }, [filters, request]);

  useEffect(() => { refresh(); }, [refresh]);
  const permissions = access?.permissions || summary?.permissions || {};
  const scope = access?.scope || summary?.scope || {};
  const kpis = summary?.kpis || {};
  const dataReady = access?.dataReady === true;
  const canManage = dataReady && permissions.canManage && !scope.isViewAs;
  const scopeLabel = scope.mode?.replaceAll('_', ' ') || (loadState === 'loading' ? 'loading' : 'unavailable');
  const manageTitle = canManage
    ? 'Add governed lab equipment'
    : loadState === 'loading'
      ? 'Loading your effective access'
      : !dataReady
        ? access?.message || 'Module 081 data foundations are unavailable'
        : scope.isViewAs
          ? 'Exit View-As to add equipment'
          : 'Your effective role has read-only Module 081 access';

  function openCreate(kind) {
    setMessage(''); setError(''); setModal(kind);
    setForm(kind === 'equipment' ? EMPTY_EQUIPMENT : kind === 'ip' ? EMPTY_IP : EMPTY_CONNECTION);
  }
  function setValue(name, value) { setForm((current) => ({ ...current, [name]: value })); }
  async function submit(path, payload) {
    setBusy(true); setError('');
    try { await request(path, { method: 'POST', body: JSON.stringify(payload) }); setModal(''); setMessage('The governed record was saved and added to immutable history.'); await refresh(); }
    catch (caught) { setError(caught.message); }
    finally { setBusy(false); }
  }
  async function download(format) {
    setBusy(true); setError('');
    try {
      const response = await fetch(`${BASE}/exports/${format}`, { headers: tokenHeaders(authSession) });
      if (!response.ok) await readResponse(response);
      const blob = await response.blob(); const url = URL.createObjectURL(blob); const anchor = document.createElement('a');
      anchor.href = url; anchor.download = `US-Signal-Lab-Equipment.${format}`; anchor.click(); URL.revokeObjectURL(url);
      setMessage(`The role-scoped ${format.toUpperCase()} evidence package was generated.`);
    } catch (caught) { setError(caught.message); } finally { setBusy(false); }
  }
  async function previewImport(event) {
    event.preventDefault(); const file = event.currentTarget.elements.file.files[0]; if (!file) return;
    const payload = new FormData(); payload.append('file', file); payload.append('targetSurface', event.currentTarget.elements.targetSurface.value);
    setBusy(true); setError('');
    try { const body = await request('/imports/preview', { method: 'POST', body: payload }); setImportPreview(body); setMessage(body.message || 'Preview ready for review.'); }
    catch (caught) { setError(caught.message); } finally { setBusy(false); }
  }
  async function commitImport() {
    if (!importPreview?.batchId) return; setBusy(true); setError('');
    try { const body = await request(`/imports/${importPreview.batchId}/commit`, { method: 'POST' }); setMessage(`${body.committed} reviewed rows were committed.`); setImportPreview(null); await refresh(); }
    catch (caught) { setError(caught.message); } finally { setBusy(false); }
  }

  return <div className="eg-center" data-module="081">
    <header className="eg-hero">
      <div><p className="eg-eyebrow">Module 081 · Lab operations</p><h1>Lab Equipment Tracker</h1><p>Authoritative equipment, IP address, cabling, rack placement, import provenance, and audit evidence for US Signal labs.</p><div className="eg-scope">Effective scope <strong>{scopeLabel}</strong>{scope.team ? <span>{scope.team}</span> : null}</div></div>
      <div className="eg-hero-actions"><button className="eg-button hero" onClick={() => download('xlsx')} disabled={!dataReady || !permissions.canExport || busy}>Export Excel</button><button className="eg-button hero" onClick={() => download('pdf')} disabled={!dataReady || !permissions.canExport || busy}>Export PDF</button><button className="eg-button hero primary" title={manageTitle} onClick={() => openCreate('equipment')} disabled={!canManage || busy}>Add equipment</button></div>
    </header>
    {busy ? <div className="eg-loading" aria-label="Loading" /> : null}
    {scope.isViewAs ? <div className="eg-notice warning">View-As is active. All Module 081 mutations and exports are intentionally disabled.</div> : null}
    {error ? <div className="eg-notice error eg-retry-notice" role="alert"><span>{error}</span><button className="eg-button" type="button" onClick={refresh} disabled={busy}>Retry</button></div> : null}{message ? <div className="eg-notice">{message}</div> : null}
    <section className="eg-kpis">
      <article className="eg-kpi"><span>Total equipment</span><strong>{kpis.equipment ?? '—'}</strong><small>{kpis.active ?? 0} active</small></article>
      <article className="eg-kpi"><span>IP allocations</span><strong>{kpis.ipAllocations ?? '—'}</strong><small>{kpis.availableIps ?? 0} available</small></article>
      <article className={`eg-kpi${kpis.conflicts ? ' alert' : ''}`}><span>IP conflicts</span><strong>{kpis.conflicts ?? '—'}</strong><small>duplicate or overlapping review</small></article>
      <article className="eg-kpi"><span>Lab footprint</span><strong>{kpis.locations ?? '—'}</strong><small>{kpis.racks ?? 0} governed racks</small></article>
      <article className="eg-kpi"><span>Maintenance</span><strong>{kpis.maintenance ?? '—'}</strong><small>{kpis.warrantyExpiring ?? 0} warranties due</small></article>
    </section>
    <nav className="eg-tabs" aria-label="Lab equipment tracker views">{TABS.map(([key, label]) => <button key={key} className={`eg-tab${tab === key ? ' active' : ''}`} onClick={() => setTab(key)}>{label}</button>)}</nav>
    <div className="eg-toolbar"><div className="eg-toolbar-group"><input className="eg-input" aria-label="Search records" placeholder="Search equipment, hostname, IP, purpose…" value={filters.search} onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))} /><select className="eg-select" aria-label="Status filter" value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))}><option value="">All statuses</option><option>active</option><option>maintenance</option><option>available</option><option>assigned</option><option>conflict</option><option>retired</option></select></div><div className="eg-toolbar-group"><button className="eg-button" type="button" onClick={() => { setTab('imports'); window.requestAnimationFrame(() => importFileRef.current?.focus()); }} disabled={!dataReady || !permissions.canImport || busy}>Bulk upload spreadsheet</button><button className="eg-button" onClick={refresh} disabled={busy}>Refresh</button>{tab === 'ipam' ? <button className="eg-button primary" onClick={() => openCreate('ip')} disabled={!canManage || busy}>Add allocation</button> : null}{tab === 'connections' ? <button className="eg-button primary" onClick={() => openCreate('connection')} disabled={!canManage || busy}>Add connection</button> : null}</div></div>

    {tab === 'equipment' ? <EquipmentTable rows={data.equipment} /> : null}
    {tab === 'ipam' ? <IpTable rows={data.allocations} /> : null}
    {tab === 'connections' ? <ConnectionTable rows={data.connections} /> : null}
    {tab === 'rack' ? <RackView racks={data.racks} /> : null}
    {tab === 'imports' ? <ImportView rows={data.imports} preview={importPreview} canImport={permissions.canImport} busy={busy} onPreview={previewImport} onCommit={commitImport} inputRef={importFileRef} /> : null}
    {tab === 'history' ? <HistoryTable rows={data.history} /> : null}

    {modal === 'equipment' ? <Modal title="Add governed equipment" onClose={() => setModal('')}><form onSubmit={(event) => { event.preventDefault(); submit('/equipment', { ...form, rackUnitStart: form.rackUnitStart ? Number(form.rackUnitStart) : null, rackUnitHeight: Number(form.rackUnitHeight), warrantyExpiresOn: form.warrantyExpiresOn || null, custodianUserId: null, linkedProjectId: null }); }}><div className="eg-form-grid"><Field label="Managing team"><Select required value={form.managingTeam} onChange={(value) => setValue('managingTeam', value)}><option value="">Select managing team</option>{[...new Set([form.managingTeam, ...teams].filter(Boolean))].map((team) => <option key={team} value={team}>{team}</option>)}</Select></Field><Field label="Equipment name"><Input required value={form.name} onChange={(value) => setValue('name', value)} /></Field><Field label="Equipment type"><Input required value={form.type} onChange={(value) => setValue('type', value)} /></Field><Field label="Status"><Select value={form.status} onChange={(value) => setValue('status', value)}><option>active</option><option>spare</option><option>reserved</option><option>maintenance</option></Select></Field><Field label="Manufacturer"><Input value={form.manufacturer} onChange={(value) => setValue('manufacturer', value)} /></Field><Field label="Model"><Input value={form.model} onChange={(value) => setValue('model', value)} /></Field><Field label="Serial number"><Input value={form.serialNumber} onChange={(value) => setValue('serialNumber', value)} /></Field><Field label="Asset tag"><Input value={form.assetTag} onChange={(value) => setValue('assetTag', value)} /></Field><Field label="Hostname"><Input value={form.hostname} onChange={(value) => setValue('hostname', value)} /></Field><Field label="MAC address"><Input value={form.macAddress} onChange={(value) => setValue('macAddress', value)} /></Field><Field label="Lab location"><Input required value={form.location} onChange={(value) => setValue('location', value)} /></Field><Field label="Pod"><Input value={form.pod} onChange={(value) => setValue('pod', value)} /></Field><Field label="Rack"><Input value={form.rack} onChange={(value) => setValue('rack', value)} /></Field><Field label="Rack units"><div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:7}}><Input type="number" min="1" max="42" placeholder="Start" value={form.rackUnitStart} onChange={(value) => setValue('rackUnitStart', value)} /><Input type="number" min="1" max="42" placeholder="Height" value={form.rackUnitHeight} onChange={(value) => setValue('rackUnitHeight', value)} /></div></Field><Field label="Physical location"><Input value={form.physicalLocation} onChange={(value) => setValue('physicalLocation', value)} /></Field><Field label="Warranty expiration"><Input type="date" value={form.warrantyExpiresOn} onChange={(value) => setValue('warrantyExpiresOn', value)} /></Field><Field label="Support contract" full><Input value={form.supportContract} onChange={(value) => setValue('supportContract', value)} /></Field><Field label="Notes" full><textarea className="eg-textarea" value={form.notes} onChange={(event) => setValue('notes', event.target.value)} /></Field></div><div className="eg-form-actions"><button className="eg-button" type="button" onClick={() => setModal('')}>Cancel</button><button className="eg-button primary" disabled={busy}>Save equipment</button></div></form></Modal> : null}
    {modal === 'ip' ? <Modal title="Add IP allocation" onClose={() => setModal('')}><form onSubmit={(event) => { event.preventDefault(); submit('/ip-addresses', { ...form, addressFamily: Number(form.addressFamily), prefixLength: Number(form.prefixLength), vlanId: form.vlanId ? Number(form.vlanId) : null, equipmentId: form.equipmentId || null, reservationOwnerUserId: null, reservationExpiresAt: form.reservationExpiresAt || null }); }}><div className="eg-form-grid"><Field label="Managing team"><Select required value={form.managingTeam} onChange={(value) => setValue('managingTeam', value)}><option value="">Select managing team</option>{[...new Set([form.managingTeam, ...teams].filter(Boolean))].map((team) => <option key={team} value={team}>{team}</option>)}</Select></Field><Field label="Network zone"><Select value={form.zone} onChange={(value) => setValue('zone', value)}>{['underlay','overlay','management','service','transit','other'].map((item) => <option key={item}>{item}</option>)}</Select></Field><Field label="Lab location"><Input required value={form.location} onChange={(value) => setValue('location', value)} /></Field><Field label="Pod"><Input required value={form.pod} onChange={(value) => setValue('pod', value)} /></Field><Field label="Address family"><Select value={form.addressFamily} onChange={(value) => { setValue('addressFamily', value); setValue('prefixLength', value === '4' ? 24 : 64); }}><option value="4">IPv4</option><option value="6">IPv6</option></Select></Field><Field label="Network CIDR"><Input required placeholder="10.10.0.0/24" value={form.network} onChange={(value) => { setValue('network', value); const prefix = value.split('/')[1]; if (prefix) setValue('prefixLength', prefix); }} /></Field><Field label="IP address"><Input value={form.ipAddress} onChange={(value) => setValue('ipAddress', value)} /></Field><Field label="Gateway"><Input value={form.gateway} onChange={(value) => setValue('gateway', value)} /></Field><Field label="VLAN ID"><Input type="number" value={form.vlanId} onChange={(value) => setValue('vlanId', value)} /></Field><Field label="VLAN name"><Input value={form.vlanName} onChange={(value) => setValue('vlanName', value)} /></Field><Field label="VRF"><Input value={form.vrf} onChange={(value) => setValue('vrf', value)} /></Field><Field label="Status"><Select value={form.status} onChange={(value) => setValue('status', value)}>{['available','reserved','assigned','conflict'].map((item) => <option key={item}>{item}</option>)}</Select></Field><Field label="Equipment"><Select value={form.equipmentId} onChange={(value) => setValue('equipmentId', value)}><option value="">Unassigned</option>{data.equipment.map((item) => <option key={item.equipmentId} value={item.equipmentId}>{item.equipmentNumber} · {item.name}</option>)}</Select></Field><Field label="Interface"><Input value={form.interfaceName} onChange={(value) => setValue('interfaceName', value)} /></Field><Field label="Purpose" full><textarea className="eg-textarea" value={form.purpose} onChange={(event) => setValue('purpose', event.target.value)} /></Field></div><div className="eg-form-actions"><button className="eg-button" type="button" onClick={() => setModal('')}>Cancel</button><button className="eg-button primary" disabled={busy}>Save allocation</button></div></form></Modal> : null}
    {modal === 'connection' ? <Modal title="Add cabling connection" onClose={() => setModal('')}><form onSubmit={(event) => { event.preventDefault(); submit('/connections', { ...form, vlanId: form.vlanId ? Number(form.vlanId) : null }); }}><div className="eg-form-grid"><Field label="Lab location"><Input required value={form.location} onChange={(value) => setValue('location', value)} /></Field><Field label="Pod"><Input value={form.pod} onChange={(value) => setValue('pod', value)} /></Field><Field label="From equipment"><Select required value={form.fromEquipmentId} onChange={(value) => setValue('fromEquipmentId', value)}><option value="">Select equipment</option>{data.equipment.map((item) => <option key={item.equipmentId} value={item.equipmentId}>{item.equipmentNumber} · {item.name}</option>)}</Select></Field><Field label="From interface"><Input required value={form.fromInterface} onChange={(value) => setValue('fromInterface', value)} /></Field><Field label="To equipment"><Select required value={form.toEquipmentId} onChange={(value) => setValue('toEquipmentId', value)}><option value="">Select equipment</option>{data.equipment.map((item) => <option key={item.equipmentId} value={item.equipmentId}>{item.equipmentNumber} · {item.name}</option>)}</Select></Field><Field label="To interface"><Input required value={form.toInterface} onChange={(value) => setValue('toInterface', value)} /></Field><Field label="Media"><Input value={form.media} onChange={(value) => setValue('media', value)} /></Field><Field label="Cable label"><Input value={form.cableLabel} onChange={(value) => setValue('cableLabel', value)} /></Field><Field label="VLAN"><Input type="number" value={form.vlanId} onChange={(value) => setValue('vlanId', value)} /></Field><Field label="IP address"><Input value={form.ipAddress} onChange={(value) => setValue('ipAddress', value)} /></Field><Field label="Notes" full><textarea className="eg-textarea" value={form.notes} onChange={(event) => setValue('notes', event.target.value)} /></Field></div><div className="eg-form-actions"><button className="eg-button" type="button" onClick={() => setModal('')}>Cancel</button><button className="eg-button primary" disabled={busy}>Save connection</button></div></form></Modal> : null}
  </div>;
}

function EquipmentTable({ rows }) { return <section className="eg-surface"><div className="eg-surface-head"><h2>Equipment inventory</h2><span>{rows.length} scoped records</span></div>{!rows.length ? <Empty /> : <div className="eg-table-wrap"><table className="eg-table"><thead><tr><th>ID / equipment</th><th>Type</th><th>Team</th><th>Location</th><th>Rack</th><th>Network</th><th>Status</th><th>Updated</th></tr></thead><tbody>{rows.map((row) => <tr key={row.equipmentId}><td><span className="eg-primary-cell">{row.equipmentNumber} · {row.name}</span><span className="eg-secondary">{row.hostname || row.serialNumber || 'No hostname or serial'}</span></td><td>{row.type}<span className="eg-secondary">{[row.manufacturer,row.model].filter(Boolean).join(' ')}</span></td><td>{row.managingTeam}<span className="eg-secondary">{row.custodian || 'No custodian'}</span></td><td>{row.location}<span className="eg-secondary">{row.pod || row.physicalLocation}</span></td><td>{row.rack || '—'}<span className="eg-secondary">{row.rackUnitStart ? `U${row.rackUnitStart} · ${row.rackUnitHeight}U` : ''}</span></td><td>{row.ipAddresses?.join(', ') || '—'}</td><td><Badge value={row.status} /></td><td>{new Date(row.updatedAt).toLocaleDateString()}<span className="eg-secondary">rev {row.revision}</span></td></tr>)}</tbody></table></div>}</section>; }
function IpTable({ rows }) { return <section className="eg-surface"><div className="eg-surface-head"><h2>IP address management</h2><span>{rows.length} scoped allocations</span></div>{!rows.length ? <Empty /> : <div className="eg-table-wrap"><table className="eg-table"><thead><tr><th>Network / IP</th><th>Zone</th><th>Location</th><th>VLAN / VRF</th><th>Equipment</th><th>Purpose</th><th>Status</th></tr></thead><tbody>{rows.map((row) => <tr key={row.allocationId}><td><span className="eg-primary-cell">{row.ipAddress || 'Pool'}</span><span className="eg-secondary">{row.network} · IPv{row.addressFamily}</span></td><td>{row.zone}</td><td>{row.location}<span className="eg-secondary">{row.pod}</span></td><td>{row.vlanId || '—'} {row.vlanName}<span className="eg-secondary">{row.vrf}</span></td><td>{row.equipmentNumber ? `${row.equipmentNumber} · ${row.equipmentName}` : 'Unassigned'}<span className="eg-secondary">{row.interfaceName}</span></td><td>{row.purpose || '—'}</td><td><Badge value={row.status} /></td></tr>)}</tbody></table></div>}</section>; }
function ConnectionTable({ rows }) { return <section className="eg-surface"><div className="eg-surface-head"><h2>Cabling & connections</h2><span>{rows.length} governed links</span></div>{!rows.length ? <Empty /> : <div className="eg-table-wrap"><table className="eg-table"><thead><tr><th>From</th><th>To</th><th>Location</th><th>Media</th><th>Label</th><th>VLAN / IP</th><th>Status</th></tr></thead><tbody>{rows.map((row) => <tr key={row.connectionId}><td><span className="eg-primary-cell">{row.fromNumber} · {row.fromName}</span><span className="eg-secondary">{row.fromInterface}</span></td><td><span className="eg-primary-cell">{row.toNumber} · {row.toName}</span><span className="eg-secondary">{row.toInterface}</span></td><td>{row.location}<span className="eg-secondary">{row.pod}</span></td><td>{row.media}</td><td>{row.cableLabel || '—'}</td><td>{row.vlanId || '—'}<span className="eg-secondary">{row.ipAddress}</span></td><td><Badge value={row.status} /></td></tr>)}</tbody></table></div>}</section>; }
function RackView({ racks }) { return <section className="eg-surface"><div className="eg-surface-head"><h2>42U rack occupancy</h2><span>{racks.length} scoped racks</span></div>{!racks.length ? <Empty>No rack placements are recorded for this scope.</Empty> : <div className="eg-rack-grid">{racks.map((rack) => { const units = new Map(); rack.placements.forEach((item) => { for (let unit = item.start; unit < item.start + item.height; unit += 1) units.set(unit, [...(units.get(unit) || []), item]); }); return <article className="eg-rack" key={`${rack.location}-${rack.rack}`}><header><strong>{rack.rack}</strong><span>{rack.location} · {rack.occupiedUnits}U used</span></header><div className="eg-rack-units">{Array.from({length:42},(_,index) => 42-index).map((unit) => { const placements=units.get(unit)||[]; return <div key={unit} className={`eg-rack-unit${placements.length ? ' occupied' : ''}${placements.length > 1 ? ' conflict' : ''}`}><b>{unit}</b><span>{placements.map((item) => `${item.equipmentNumber} ${item.name}`).join(' / ')}</span></div>; })}</div></article>; })}</div>}</section>; }
function ImportView({ rows, preview, canImport, busy, onPreview, onCommit, inputRef }) { return <section className="eg-surface"><div className="eg-surface-head"><h2>Reviewed import pipeline</h2><span>CSV / XLSX · immutable checksum provenance</span></div><div className="eg-card-grid"><article className="eg-card"><h3>Create a non-destructive preview</h3><p>Headers are mapped deterministically. Sensitive columns are blocked, and no operational record changes until an administrator commits reviewed rows.</p>{canImport ? <form onSubmit={onPreview}><div className="eg-field"><label>Target surface</label><select className="eg-select" name="targetSurface"><option value="equipment">Equipment</option><option value="ipam">IP address management</option><option value="connections">Cabling & connections</option></select></div><div className="eg-field" style={{marginTop:9}}><label>Approved source file</label><input className="eg-input" name="file" type="file" accept=".csv,.xlsx" required  ref={inputRef} accept=".csv,.xlsx" /></div><button className="eg-button primary" style={{marginTop:11}} disabled={busy}>Preview import</button></form> : <div className="eg-notice warning">Import evidence and approval are limited to authorized administrators.</div>}</article>{preview ? <article className="eg-card"><h3>Preview ready · {preview.target}</h3><p>SHA-256 {preview.checksum}</p><div className="eg-card-stats"><span><strong>{preview.counts?.accepted || 0}</strong>Accepted</span><span><strong>{preview.counts?.warnings || 0}</strong>Warnings</span><span><strong>{preview.counts?.rejected || 0}</strong>Rejected</span><span><strong>{preview.counts?.total || 0}</strong>Total</span></div><button className="eg-button primary" style={{marginTop:12}} onClick={onCommit} disabled={busy || !(preview.counts?.accepted || preview.counts?.warnings)}>Commit accepted rows</button></article> : null}</div><div className="eg-table-wrap"><table className="eg-table"><thead><tr><th>File</th><th>Target</th><th>Status</th><th>Accepted</th><th>Warnings</th><th>Rejected</th><th>Created by</th><th>Created</th></tr></thead><tbody>{rows.map((row) => <tr key={row.batchId}><td><span className="eg-primary-cell">{row.fileName}</span><span className="eg-secondary">{row.sha256?.slice(0,16)}…</span></td><td>{row.target}</td><td><Badge value={row.status} /></td><td>{row.accepted}</td><td>{row.warnings}</td><td>{row.rejected}</td><td>{row.createdBy}</td><td>{new Date(row.createdAt).toLocaleString()}</td></tr>)}</tbody></table>{!rows.length ? <Empty>No import evidence is available.</Empty> : null}</div></section>; }
function HistoryTable({ rows }) { return <section className="eg-surface"><div className="eg-surface-head"><h2>Immutable audit history</h2><span>{rows.length} scoped events</span></div>{!rows.length ? <Empty>No audit events are visible in this scope.</Empty> : <div className="eg-table-wrap"><table className="eg-table"><thead><tr><th>Occurred</th><th>Entity</th><th>Event</th><th>Actor</th><th>Evidence</th></tr></thead><tbody>{rows.map((row) => <tr key={row.auditId}><td>{new Date(row.occurredAt).toLocaleString()}</td><td>{row.entityType}<span className="eg-secondary">{row.entityId}</span></td><td><Badge value={row.eventCode} /></td><td>{row.actor}</td><td><code>{JSON.stringify(row.metadata)}</code></td></tr>)}</tbody></table></div>}</section>; }

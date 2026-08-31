import { useEffect, useMemo, useState } from 'react';

async function requestJson(url, options = {}) {
  const response = await fetch(url, {
    credentials: 'include',
    ...options,
    headers: {
      Accept: 'application/json',
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload?.message || `Request failed with status ${response.status}.`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function sourceLabel(source) {
  if (!source) return 'Loading source';
  if (source.mode === 'manual') return 'Manual customer directory';
  if (source.mode === 'sell') return 'SELL (Zendesk Sell)';
  return source.providerName || source.providerKey || 'Module 026 CRM';
}

function sourceTone(source) {
  if (!source) return 'neutral';
  if (source.mode === 'manual') return 'manual';
  return source.providerReady ? 'ready' : 'attention';
}

function formatTimestamp(value) {
  if (!value) return 'No completed customer sync';
  try {
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short'
    }).format(new Date(value));
  } catch {
    return value;
  }
}

export default function CustomerSourceAuthorityPortal() {
  const [module021Visible, setModule021Visible] = useState(false);
  const [state, setState] = useState({ loading: true, source: null, providers: [], canManage: false, migrationApplied: true, error: '' });
  const [draftMode, setDraftMode] = useState('sell');
  const [draftProviderKey, setDraftProviderKey] = useState('');
  const [saving, setSaving] = useState(false);
  const [actionMessage, setActionMessage] = useState('');
  const [search, setSearch] = useState('');
  const [preview, setPreview] = useState([]);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [selectedIds, setSelectedIds] = useState(() => new Set());
  const [importLoading, setImportLoading] = useState(false);

  const loadSource = async () => {
    try {
      const payload = await requestJson('/api/customers/source');
      const source = payload?.source || null;
      setState({
        loading: false,
        source,
        providers: Array.isArray(payload?.providers) ? payload.providers : [],
        canManage: Boolean(payload?.canManage),
        migrationApplied: payload?.migrationApplied !== false,
        error: ''
      });
      setDraftMode(source?.mode || 'sell');
      setDraftProviderKey(source?.mode === 'crm' ? (source?.providerKey || '') : '');
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message || 'Customer source authority is unavailable.'
      }));
    }
  };

  useEffect(() => {
    void loadSource();
  }, []);

  useEffect(() => {
    const detect = () => {
      setModule021Visible(Boolean(document.querySelector('.customer-directory-center[data-module="021"]')));
    };
    detect();
    const observer = new MutationObserver(detect);
    observer.observe(document.body, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    const mode = state.source?.mode || 'sell';
    document.documentElement.dataset.customerSourceMode = mode;
    document.documentElement.dataset.customerSourceProvider = state.source?.providerKey || '';
    return () => {
      delete document.documentElement.dataset.customerSourceMode;
      delete document.documentElement.dataset.customerSourceProvider;
    };
  }, [state.source]);

  const crmProviders = useMemo(
    () => state.providers.filter((provider) => provider?.providerKey !== 'zendesk_sell' && provider?.eligibleCustomerSource),
    [state.providers]
  );

  const selectedProvider = useMemo(
    () => state.providers.find((provider) => provider?.providerKey === draftProviderKey) || null,
    [state.providers, draftProviderKey]
  );

  const activeSelectionMatchesDraft =
    state.source?.mode === draftMode
    && (draftMode !== 'crm' || state.source?.providerKey === draftProviderKey);

  const handleModeChange = (event) => {
    const nextMode = event.target.value;
    setDraftMode(nextMode);
    setActionMessage('');
    setPreview([]);
    setSelectedIds(new Set());
    if (nextMode === 'crm' && !draftProviderKey) {
      setDraftProviderKey(crmProviders[0]?.providerKey || '');
    }
  };

  const saveSource = async () => {
    if (!state.canManage || saving) return;
    setSaving(true);
    setActionMessage('');
    try {
      const payload = await requestJson('/api/customers/source', {
        method: 'PUT',
        body: JSON.stringify({
          mode: draftMode,
          providerKey: draftMode === 'crm' ? draftProviderKey : null
        })
      });
      const source = payload?.source || null;
      setState((current) => ({ ...current, source, error: '' }));
      setDraftMode(source?.mode || draftMode);
      setDraftProviderKey(source?.mode === 'crm' ? (source?.providerKey || draftProviderKey) : '');
      setPreview([]);
      setSelectedIds(new Set());
      setActionMessage(payload?.message || 'Customer source updated.');
    } catch (error) {
      setActionMessage(error?.message || 'Customer source could not be updated.');
    } finally {
      setSaving(false);
    }
  };

  const previewCustomers = async () => {
    if (previewLoading || !activeSelectionMatchesDraft || state.source?.mode !== 'crm') return;
    setPreviewLoading(true);
    setActionMessage('');
    try {
      const payload = await requestJson('/api/customers/source/preview', {
        method: 'POST',
        body: JSON.stringify({ search, page: 1, pageSize: 100 })
      });
      const customers = Array.isArray(payload?.customers) ? payload.customers : [];
      setPreview(customers);
      setSelectedIds(new Set());
      setActionMessage(payload?.message || `${customers.length} customer(s) loaded.`);
    } catch (error) {
      setPreview([]);
      setSelectedIds(new Set());
      setActionMessage(error?.message || 'Customer preview could not be loaded.');
    } finally {
      setPreviewLoading(false);
    }
  };

  const toggleSelected = (sourceRecordId) => {
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(sourceRecordId)) next.delete(sourceRecordId);
      else next.add(sourceRecordId);
      return next;
    });
  };

  const importCustomers = async () => {
    if (importLoading || selectedIds.size === 0) return;
    setImportLoading(true);
    setActionMessage('');
    try {
      const payload = await requestJson('/api/customers/source/import', {
        method: 'POST',
        body: JSON.stringify({ sourceRecordIds: Array.from(selectedIds) })
      });
      setActionMessage(payload?.message || 'Selected customers were imported.');
      setSelectedIds(new Set());
      setPreview([]);
      window.setTimeout(() => window.location.reload(), 350);
    } catch (error) {
      setActionMessage(error?.message || 'Selected customers could not be imported.');
    } finally {
      setImportLoading(false);
    }
  };

  const scrollToManualCustomer = () => {
    const heading = Array.from(document.querySelectorAll('.customer-directory-center h3'))
      .find((node) => /add customer/i.test(node.textContent || ''));
    heading?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  if (!module021Visible) return null;

  const source = state.source;
  const externalCrmActive = source?.mode === 'crm';
  const providerMappingReady = selectedProvider?.customerPreviewConfigured && selectedProvider?.customerImportConfigured;

  return (
    <aside className="customer-source-authority" aria-label="Module 021 customer source authority">
      <header className="customer-source-authority__header">
        <div>
          <span className="customer-source-authority__eyebrow">MODULE 021 • SOURCE</span>
          <strong>Customer source</strong>
        </div>
        <span className={`customer-source-authority__status customer-source-authority__status--${sourceTone(source)}`}>
          {state.loading ? 'Loading' : sourceLabel(source)}
        </span>
      </header>

      {state.error ? <div className="customer-source-authority__alert">{state.error}</div> : null}
      {!state.migrationApplied ? (
        <div className="customer-source-authority__alert">
          Migration 098 has not been applied yet. SELL remains the backward-compatible source until the migration is installed.
        </div>
      ) : null}

      <label className="customer-source-authority__field">
        <span>Authoritative source</span>
        <select value={draftMode} onChange={handleModeChange} disabled={!state.canManage || saving || !state.migrationApplied}>
          <option value="sell">SELL (Zendesk Sell)</option>
          <option value="crm">Another Module 026 CRM/ERP</option>
          <option value="manual">Manual</option>
        </select>
      </label>

      {draftMode === 'crm' ? (
        <label className="customer-source-authority__field">
          <span>Module 026 provider</span>
          <select
            value={draftProviderKey}
            onChange={(event) => {
              setDraftProviderKey(event.target.value);
              setPreview([]);
              setSelectedIds(new Set());
              setActionMessage('');
            }}
            disabled={!state.canManage || saving || !state.migrationApplied}
          >
            <option value="">Select a provider</option>
            {crmProviders.map((provider) => (
              <option key={provider.providerKey} value={provider.providerKey}>
                {provider.providerName}{provider.providerReady ? '' : ' • attention required'}
              </option>
            ))}
          </select>
        </label>
      ) : null}

      <button
        type="button"
        className="customer-source-authority__primary"
        onClick={saveSource}
        disabled={!state.canManage || saving || !state.migrationApplied || (draftMode === 'crm' && !draftProviderKey) || activeSelectionMatchesDraft}
      >
        {saving ? 'Saving…' : 'Save source'}
      </button>

      {source?.mode === 'manual' ? (
        <div className="customer-source-authority__mode-card">
          <strong>Manual is authoritative</strong>
          <p>Customers are maintained directly in Module 021. SELL association and external CRM synchronization are not required.</p>
          <button type="button" onClick={scrollToManualCustomer}>Add customer manually</button>
        </div>
      ) : null}

      {externalCrmActive ? (
        <div className="customer-source-authority__mode-card">
          <strong>{sourceLabel(source)}</strong>
          <p>
            Module 026 connection: {source?.providerReady ? 'ready' : 'attention required'}.
            {' '}Customer mapping: {source?.customerImportConfigured ? 'configured' : 'not configured'}.
          </p>
          <small>Last customer sync: {formatTimestamp(source?.lastSuccessfulCustomerSyncAt)}</small>

          {!source?.customerImportConfigured ? (
            <div className="customer-source-authority__alert customer-source-authority__alert--compact">
              Configure the provider customer import mapping in Module 026 before pulling customers. The mapping needs customerListUrl, customerRecordUrlTemplate (or recordLookupUrlTemplate), itemsPath, idPath, and namePath.
            </div>
          ) : null}

          {source?.providerReady && source?.customerImportConfigured ? (
            <div className="customer-source-authority__pull">
              <label className="customer-source-authority__field">
                <span>Search CRM customers</span>
                <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Customer name or detail" />
              </label>
              <button type="button" onClick={previewCustomers} disabled={previewLoading || !activeSelectionMatchesDraft}>
                {previewLoading ? 'Loading…' : 'Preview customers'}
              </button>
            </div>
          ) : null}

          {preview.length ? (
            <div className="customer-source-authority__preview">
              <div className="customer-source-authority__preview-head">
                <strong>{preview.length} customer(s)</strong>
                <button type="button" onClick={importCustomers} disabled={selectedIds.size === 0 || importLoading || !providerMappingReady}>
                  {importLoading ? 'Importing…' : `Import selected (${selectedIds.size})`}
                </button>
              </div>
              <div className="customer-source-authority__preview-list">
                {preview.slice(0, 30).map((customer) => (
                  <label key={customer.sourceRecordId}>
                    <input
                      type="checkbox"
                      checked={selectedIds.has(customer.sourceRecordId)}
                      onChange={() => toggleSelected(customer.sourceRecordId)}
                    />
                    <span>
                      <strong>{customer.name}</strong>
                      <small>{customer.matchType === 'new_customer' ? 'New customer' : customer.matchType?.replaceAll('_', ' ')}</small>
                    </span>
                  </label>
                ))}
              </div>
            </div>
          ) : null}
        </div>
      ) : null}

      {source?.mode === 'sell' ? (
        <p className="customer-source-authority__note">
          SELL is active. Use the existing governed SELL preview/import controls in Module 021 below this selector.
        </p>
      ) : null}

      {!state.canManage && !state.loading ? (
        <p className="customer-source-authority__note">You can view the active source, but your current role cannot change it.</p>
      ) : null}

      {actionMessage ? <div className="customer-source-authority__message">{actionMessage}</div> : null}
    </aside>
  );
}

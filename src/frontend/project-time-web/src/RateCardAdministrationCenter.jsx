import { useEffect, useMemo, useState } from 'react';
import './rate-card-administration-center.css';

const emptyCardForm = {
  rateCardId: '',
  rateCardCode: '',
  rateCardName: '',
  rateCardType: 'customer_specific',
  clientId: '',
  status: 'active',
  effectiveStartDate: new Date().toISOString().slice(0, 10),
  effectiveEndDate: '',
  description: '',
  changeReason: ''
};

const emptyLineForm = {
  rateLineId: '',
  rateCardId: '',
  skuCode: '',
  displayName: '',
  description: '',
  laborCategory: 'engineering',
  timeType: 'normal',
  unitType: 'hour',
  rateAmount: '',
  minimumBillingHours: '0',
  remoteMinimumHours: '0',
  onsiteMinimumHours: '0',
  daytimeMinimumHours: '0',
  afterhoursWeekendHolidayMinimumHours: '0',
  businessHoursText: '',
  billableDefault: true,
  utilizationEligibleDefault: true,
  isEmergency: false,
  isTravel: false,
  overrideAllowed: true,
  isActive: true,
  displayOrder: 100,
  notes: '',
  changeReason: ''
};

function readSession() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed?.sessionToken) return null;
    return parsed;
  } catch {
    return null;
  }
}

function authHeaders(extra = {}) {
  const session = readSession();
  const headers = { ...extra };

  if (session?.sessionToken) {
    headers['X-ProjectPulse-Session'] = session.sessionToken;
  }

  try {
    const rawViewAs = window.localStorage.getItem('projectPulseViewAsUser');
    const viewAs = rawViewAs ? JSON.parse(rawViewAs) : null;
    if (viewAs?.userId) {
      headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
    }
  } catch {
    // Ignore malformed local view-as cache.
  }

  return headers;
}

async function fetchJson(url) {
  const response = await fetch(url, {
    headers: authHeaders()
  });
  if (!response.ok) {
    let message = `HTTP ${response.status}`;
    try {
      const error = await response.json();
      message = error.message || error.status || message;
    } catch {
      // Ignore non-JSON response.
    }
    throw new Error(message);
  }
  return response.json();
}

async function postJson(url, payload) {
  const response = await fetch(url, {
    method: 'POST',
    headers: authHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    let message = `HTTP ${response.status}`;
    try {
      const error = await response.json();
      message = error.message || error.status || message;
    } catch {
      // Ignore non-JSON response.
    }
    throw new Error(message);
  }

  return response.json();
}

function money(value) {
  const numberValue = Number(value ?? 0);
  return numberValue.toLocaleString(undefined, {
    style: 'currency',
    currency: 'USD'
  });
}

function number(value) {
  return Number(value ?? 0).toLocaleString(undefined, {
    maximumFractionDigits: 2
  });
}

function labelize(value) {
  return String(value || '')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export default function RateCardAdministrationCenter() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [selectedRateCardId, setSelectedRateCardId] = useState('');
  const [searchTerm, setSearchTerm] = useState('');
  const [typeFilter, setTypeFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('active');
  const [cardForm, setCardForm] = useState(emptyCardForm);
  const [lineForm, setLineForm] = useState(emptyLineForm);
  const [actionStatus, setActionStatus] = useState('');

  async function load() {
    setPayload((current) => ({ ...current, loading: true, error: null }));
    try {
      const result = await fetchJson('/api/rate-cards/admin/foundation');
      setPayload({ loading: false, data: result, error: null });

      if (!selectedRateCardId && result.rateCards?.length) {
        const defaultCard =
          result.rateCards.find((item) => item.rateCardCode === 'TOYOTA_SPECIAL_RATES') ||
          result.rateCards.find((item) => item.rateCardCode === 'STANDARD_COMPANY_RATES') ||
          result.rateCards[0];

        setSelectedRateCardId(defaultCard.rateCardId);
        setLineForm((current) => ({ ...current, rateCardId: defaultCard.rateCardId }));
      }
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load rate cards.'
      });
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const rateCards = payload.data?.rateCards ?? [];
  const rateLines = payload.data?.rateLines ?? [];
  const customers = payload.data?.customers ?? [];
  const recentChanges = payload.data?.recentChanges ?? [];

  const selectedRateCard = rateCards.find((item) => item.rateCardId === selectedRateCardId) ?? rateCards[0];

  const filteredRateCards = useMemo(() => {
    const normalizedSearch = searchTerm.trim().toLowerCase();

    return rateCards.filter((card) => {
      if (typeFilter !== 'all' && card.rateCardType !== typeFilter) return false;
      if (statusFilter !== 'all' && card.status !== statusFilter) return false;

      if (!normalizedSearch) return true;

      const haystack = [
        card.rateCardCode,
        card.rateCardName,
        card.rateCardType,
        card.customerName,
        card.description,
        card.status
      ].join(' ').toLowerCase();

      return haystack.includes(normalizedSearch);
    });
  }, [rateCards, searchTerm, statusFilter, typeFilter]);

  const selectedLines = useMemo(() => {
    if (!selectedRateCard) return [];
    return rateLines.filter((line) => line.rateCardId === selectedRateCard.rateCardId);
  }, [rateLines, selectedRateCard]);

  const activeSelectedLines = selectedLines.filter((line) => line.isActive !== false);
  const specialCards = rateCards.filter((card) => card.rateCardType === 'customer_specific');
  const emergencyLines = rateLines.filter((line) => line.isEmergency);

  function editCard(card) {
    setSelectedRateCardId(card.rateCardId);
    setCardForm({
      rateCardId: card.rateCardId ?? '',
      rateCardCode: card.rateCardCode ?? '',
      rateCardName: card.rateCardName ?? '',
      rateCardType: card.rateCardType ?? 'customer_specific',
      clientId: card.clientId ?? '',
      status: card.status ?? 'active',
      effectiveStartDate: card.effectiveStartDate || new Date().toISOString().slice(0, 10),
      effectiveEndDate: card.effectiveEndDate ?? '',
      description: card.description ?? '',
      changeReason: ''
    });
    setLineForm((current) => ({ ...current, rateCardId: card.rateCardId }));
  }

  function editLine(line) {
    setSelectedRateCardId(line.rateCardId);
    setLineForm({
      rateLineId: line.rateLineId ?? '',
      rateCardId: line.rateCardId ?? '',
      skuCode: line.skuCode ?? '',
      displayName: line.displayName ?? '',
      description: line.description ?? '',
      laborCategory: line.laborCategory ?? 'engineering',
      timeType: line.timeType ?? 'normal',
      unitType: line.unitType ?? 'hour',
      rateAmount: String(line.rateAmount ?? ''),
      minimumBillingHours: String(line.minimumBillingHours ?? 0),
      remoteMinimumHours: String(line.remoteMinimumHours ?? 0),
      onsiteMinimumHours: String(line.onsiteMinimumHours ?? 0),
      daytimeMinimumHours: String(line.daytimeMinimumHours ?? 0),
      afterhoursWeekendHolidayMinimumHours: String(line.afterhoursWeekendHolidayMinimumHours ?? 0),
      businessHoursText: line.businessHoursText ?? '',
      billableDefault: line.billableDefault !== false,
      utilizationEligibleDefault: line.utilizationEligibleDefault !== false,
      isEmergency: line.isEmergency === true,
      isTravel: line.isTravel === true,
      overrideAllowed: line.overrideAllowed !== false,
      isActive: line.isActive !== false,
      displayOrder: Number(line.displayOrder ?? 100),
      notes: line.notes ?? '',
      changeReason: ''
    });
  }

  async function saveCard(event) {
    event.preventDefault();
    setActionStatus('Saving rate card...');

    try {
      const result = await postJson('/api/rate-cards/admin/cards', {
        ...cardForm,
        changeReason: cardForm.changeReason || 'Saved from Rate Card Administration page.'
      });

      setActionStatus(result.message ?? 'Rate card saved.');
      setCardForm(emptyCardForm);
      await load();
      if (result.rateCardId) {
        setSelectedRateCardId(result.rateCardId);
      }
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to save rate card.');
    }
  }

  async function saveLine(event) {
    event.preventDefault();

    const targetRateCardId = lineForm.rateCardId || selectedRateCard?.rateCardId;
    if (!targetRateCardId) {
      setActionStatus('Select a rate card before saving a rate line.');
      return;
    }

    setActionStatus('Saving rate line...');

    try {
      const result = await postJson('/api/rate-cards/admin/lines', {
        ...lineForm,
        rateCardId: targetRateCardId,
        rateAmount: Number(lineForm.rateAmount || 0),
        minimumBillingHours: Number(lineForm.minimumBillingHours || 0),
        remoteMinimumHours: Number(lineForm.remoteMinimumHours || 0),
        onsiteMinimumHours: Number(lineForm.onsiteMinimumHours || 0),
        daytimeMinimumHours: Number(lineForm.daytimeMinimumHours || 0),
        afterhoursWeekendHolidayMinimumHours: Number(lineForm.afterhoursWeekendHolidayMinimumHours || 0),
        displayOrder: Number(lineForm.displayOrder || 100),
        changeReason: lineForm.changeReason || 'Saved from Rate Card Administration page.'
      });

      setActionStatus(result.message ?? 'Rate line saved.');
      setLineForm({ ...emptyLineForm, rateCardId: targetRateCardId });
      await load();
      setSelectedRateCardId(targetRateCardId);
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to save rate line.');
    }
  }

  async function retireLine(line) {
    const retireReason = window.prompt(`Why are you retiring ${line.displayName || line.skuCode}?`);
    if (!retireReason) return;

    setActionStatus('Retiring rate line...');

    try {
      const result = await postJson('/api/rate-cards/admin/lines/retire', {
        rateLineId: line.rateLineId,
        retireReason
      });

      setActionStatus(result.message ?? 'Rate line retired.');
      await load();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to retire rate line.');
    }
  }

  return (
    <section className="rate-card-admin-center">
      <div className="rate-card-admin-header">
        <div>
          <p className="eyebrow">Rate Card Administration</p>
          <h2>Customer, service request, emergency, and standard rates</h2>
          <p className="muted">
            Manage editable rate cards for standard work, Toyota, Hyundai, service requests, emergency support, travel, and future customer-specific pricing.
          </p>
        </div>
        <span className="rate-card-admin-mode">Super Admin / PTC / Solution Architect</span>
      </div>

      {payload.error ? <div className="rate-card-admin-banner error">{payload.error}</div> : null}
      {actionStatus ? <div className="rate-card-admin-banner">{actionStatus}</div> : null}

      <div className="rate-card-admin-summary">
        <article>
          <span>Rate cards</span>
          <strong>{payload.loading ? '...' : rateCards.length}</strong>
          <small>{specialCards.length} customer-specific</small>
        </article>
        <article>
          <span>Active lines</span>
          <strong>{payload.loading ? '...' : rateLines.filter((line) => line.isActive !== false).length}</strong>
          <small>{rateLines.length} total lines</small>
        </article>
        <article>
          <span>Emergency rates</span>
          <strong>{payload.loading ? '...' : emergencyLines.length}</strong>
          <small>Service request emergency rules</small>
        </article>
        <article>
          <span>Selected card</span>
          <strong>{selectedRateCard ? selectedRateCard.rateCardName : 'None'}</strong>
          <small>{activeSelectedLines.length} active lines</small>
        </article>
      </div>

      <div className="rate-card-priority-panel">
        <h3>Rate selection priority during intake</h3>
        <ol>
          {(payload.data?.ratePriority ?? [
            'GSD-imported rate',
            'Customer-specific rate card',
            'Service Request or Emergency rate card',
            'Standard company rate card',
            'Manual override with required reason'
          ]).map((item) => <li key={item}>{item}</li>)}
        </ol>
      </div>

      <div className="rate-card-admin-toolbar">
        <label>
          Search
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Search Toyota, Hyundai, emergency, SKU, customer..."
          />
        </label>
        <label>
          Type
          <select value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}>
            <option value="all">All types</option>
            <option value="standard">Standard</option>
            <option value="customer_specific">Customer-specific</option>
            <option value="service_request">Service request</option>
            <option value="emergency_service_request">Emergency service request</option>
            <option value="gsd_imported">GSD-imported</option>
            <option value="manual_override">Manual override</option>
          </select>
        </label>
        <label>
          Status
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
            <option value="active">Active</option>
            <option value="draft">Draft</option>
            <option value="retired">Retired</option>
            <option value="all">All</option>
          </select>
        </label>
        <button type="button" className="secondary-action" onClick={load}>Refresh</button>
      </div>

      <div className="rate-card-admin-layout">
        <article className="rate-card-admin-panel">
          <div className="rate-card-panel-heading">
            <h3>Rate cards</h3>
            <p className="muted">Select a rate card to review or edit its rate lines.</p>
          </div>

          <div className="rate-card-list">
            {filteredRateCards.map((card) => (
              <button
                type="button"
                className={`rate-card-list-item ${card.rateCardId === selectedRateCard?.rateCardId ? 'selected' : ''}`}
                key={card.rateCardId}
                onClick={() => {
                  setSelectedRateCardId(card.rateCardId);
                  setLineForm((current) => ({ ...current, rateCardId: card.rateCardId }));
                }}
              >
                <strong>{card.rateCardName}</strong>
                <span>{labelize(card.rateCardType)} · {card.status}</span>
                <small>
                  {card.customerName ? `${card.customerName} · ` : ''}
                  {card.activeLineCount}/{card.lineCount} active lines
                </small>
              </button>
            ))}
            {!payload.loading && filteredRateCards.length === 0 ? <p className="muted">No rate cards match the current filters.</p> : null}
          </div>
        </article>

        <article className="rate-card-admin-panel rate-card-lines-panel">
          <div className="rate-card-panel-heading">
            <div>
              <h3>{selectedRateCard?.rateCardName ?? 'Select a rate card'}</h3>
              <p className="muted">
                {selectedRateCard?.description ?? 'Rate lines will appear after you select a rate card.'}
              </p>
            </div>
            {selectedRateCard ? (
              <button type="button" className="secondary-action" onClick={() => editCard(selectedRateCard)}>
                Edit card
              </button>
            ) : null}
          </div>

          <div className="rate-line-table-wrap">
            <table className="rate-line-table">
              <thead>
                <tr>
                  <th>SKU / Role</th>
                  <th>Category</th>
                  <th>Time type</th>
                  <th>Rate</th>
                  <th>Minimums</th>
                  <th>Billing / Utilization</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {selectedLines.map((line) => (
                  <tr key={line.rateLineId} className={line.isActive === false ? 'inactive-line' : ''}>
                    <td>
                      <strong>{line.displayName}</strong>
                      <small>{line.skuCode}</small>
                    </td>
                    <td>{labelize(line.laborCategory)}</td>
                    <td>{labelize(line.timeType)}</td>
                    <td>{money(line.rateAmount)} / {line.unitType}</td>
                    <td>
                      <small>
                        Min {number(line.minimumBillingHours)} · Remote {number(line.remoteMinimumHours)} · Onsite {number(line.onsiteMinimumHours)}
                      </small>
                      {(Number(line.daytimeMinimumHours) > 0 || Number(line.afterhoursWeekendHolidayMinimumHours) > 0) ? (
                        <small>
                          Day {number(line.daytimeMinimumHours)} · AH/Wknd/Holiday {number(line.afterhoursWeekendHolidayMinimumHours)}
                        </small>
                      ) : null}
                    </td>
                    <td>
                      <small>{line.billableDefault ? 'Billable' : 'Non-billable'}</small>
                      <small>{line.utilizationEligibleDefault ? 'Utilization eligible' : 'Non-utilization'}</small>
                    </td>
                    <td>
                      <span className={`rate-status ${line.isActive === false ? 'retired' : 'active'}`}>
                        {line.isActive === false ? 'Retired' : 'Active'}
                      </span>
                      {line.isEmergency ? <span className="rate-status emergency">Emergency</span> : null}
                    </td>
                    <td>
                      <button type="button" className="secondary-action" onClick={() => editLine(line)}>Edit</button>
                      {line.isActive !== false ? (
                        <button type="button" className="secondary-action danger" onClick={() => retireLine(line)}>Retire</button>
                      ) : null}
                    </td>
                  </tr>
                ))}
                {!payload.loading && selectedLines.length === 0 ? (
                  <tr>
                    <td colSpan="8">No rate lines are configured for this card yet.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </article>
      </div>

      <div className="rate-card-admin-layout forms-layout">
        <article className="rate-card-admin-panel">
          <h3>{cardForm.rateCardId ? 'Edit rate card' : 'Add customer-specific rate card'}</h3>
          <form className="rate-card-form" onSubmit={saveCard}>
            <label>
              Rate card name
              <input value={cardForm.rateCardName} onChange={(event) => setCardForm((current) => ({ ...current, rateCardName: event.target.value }))} placeholder="Customer XYZ Special Rates" />
            </label>
            <label>
              Rate card code
              <input value={cardForm.rateCardCode} onChange={(event) => setCardForm((current) => ({ ...current, rateCardCode: event.target.value.toUpperCase().replaceAll(' ', '_') }))} placeholder="CUSTOMER_XYZ_SPECIAL_RATES" />
            </label>
            <label>
              Type
              <select value={cardForm.rateCardType} onChange={(event) => setCardForm((current) => ({ ...current, rateCardType: event.target.value }))}>
                <option value="customer_specific">Customer-specific</option>
                <option value="standard">Standard</option>
                <option value="service_request">Service request</option>
                <option value="emergency_service_request">Emergency service request</option>
                <option value="gsd_imported">GSD-imported</option>
                <option value="manual_override">Manual override</option>
              </select>
            </label>
            <label>
              Customer
              <select value={cardForm.clientId} onChange={(event) => setCardForm((current) => ({ ...current, clientId: event.target.value }))}>
                <option value="">No customer / global rate</option>
                {customers.map((customer) => (
                  <option value={customer.clientId} key={customer.clientId}>
                    {customer.clientName} {customer.clientCode ? `(${customer.clientCode})` : ''}{customer.isActive === false ? ' - inactive' : ''}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Status
              <select value={cardForm.status} onChange={(event) => setCardForm((current) => ({ ...current, status: event.target.value }))}>
                <option value="active">Active</option>
                <option value="draft">Draft</option>
                <option value="retired">Retired</option>
              </select>
            </label>
            <label>
              Effective start
              <input type="date" value={cardForm.effectiveStartDate} onChange={(event) => setCardForm((current) => ({ ...current, effectiveStartDate: event.target.value }))} />
            </label>
            <label>
              Effective end
              <input type="date" value={cardForm.effectiveEndDate} onChange={(event) => setCardForm((current) => ({ ...current, effectiveEndDate: event.target.value }))} />
            </label>
            <label className="full-width">
              Description
              <textarea rows={3} value={cardForm.description} onChange={(event) => setCardForm((current) => ({ ...current, description: event.target.value }))} />
            </label>
            <label className="full-width">
              Change reason
              <textarea rows={2} value={cardForm.changeReason} onChange={(event) => setCardForm((current) => ({ ...current, changeReason: event.target.value }))} placeholder="Explain why this rate card is being added or changed." />
            </label>
            <div className="rate-card-form-actions">
              <button type="submit" className="primary-action">Save rate card</button>
              <button type="button" className="secondary-action" onClick={() => setCardForm(emptyCardForm)}>Clear</button>
            </div>
          </form>
        </article>

        <article className="rate-card-admin-panel">
          <h3>{lineForm.rateLineId ? 'Edit rate line' : 'Add rate line'}</h3>
          <form className="rate-card-form" onSubmit={saveLine}>
            <label>
              Rate card
              <select value={lineForm.rateCardId || selectedRateCard?.rateCardId || ''} onChange={(event) => {
                setSelectedRateCardId(event.target.value);
                setLineForm((current) => ({ ...current, rateCardId: event.target.value }));
              }}>
                {rateCards.map((card) => (
                  <option value={card.rateCardId} key={card.rateCardId}>{card.rateCardName}</option>
                ))}
              </select>
            </label>
            <label>
              SKU / role code
              <input value={lineForm.skuCode} onChange={(event) => setLineForm((current) => ({ ...current, skuCode: event.target.value }))} placeholder="ON-AS-Consult-Engineer" />
            </label>
            <label>
              Display name
              <input value={lineForm.displayName} onChange={(event) => setLineForm((current) => ({ ...current, displayName: event.target.value }))} placeholder="Consulting Engineer" />
            </label>
            <label>
              Labor category
              <select value={lineForm.laborCategory} onChange={(event) => setLineForm((current) => ({ ...current, laborCategory: event.target.value }))}>
                <option value="project_management">Project Management</option>
                <option value="engineering">Engineering</option>
                <option value="service_request">Service Request</option>
                <option value="travel">Travel</option>
                <option value="perdiem">Per Diem</option>
                <option value="materials">Materials</option>
              </select>
            </label>
            <label>
              Time type
              <select value={lineForm.timeType} onChange={(event) => setLineForm((current) => ({ ...current, timeType: event.target.value }))}>
                <option value="normal">Normal</option>
                <option value="afterhours">Afterhours</option>
                <option value="travel">Travel</option>
                <option value="perdiem">Per Diem</option>
                <option value="materials">Materials</option>
                <option value="first_available_remote">First Available Remote</option>
                <option value="first_available_onsite">First Available Onsite</option>
                <option value="emergency_daytime">Emergency Daytime</option>
                <option value="emergency_afterhours_weekend_holiday">Emergency Afterhours / Weekend / Holiday</option>
              </select>
            </label>
            <label>
              Unit type
              <select value={lineForm.unitType} onChange={(event) => setLineForm((current) => ({ ...current, unitType: event.target.value }))}>
                <option value="hour">Hour</option>
                <option value="day">Day</option>
                <option value="unit">Unit</option>
              </select>
            </label>
            <label>
              Rate amount
              <input type="number" min="0" step="0.01" value={lineForm.rateAmount} onChange={(event) => setLineForm((current) => ({ ...current, rateAmount: event.target.value }))} />
            </label>
            <label>
              Minimum billing hours
              <input type="number" min="0" step="0.25" value={lineForm.minimumBillingHours} onChange={(event) => setLineForm((current) => ({ ...current, minimumBillingHours: event.target.value }))} />
            </label>
            <label>
              Remote minimum
              <input type="number" min="0" step="0.25" value={lineForm.remoteMinimumHours} onChange={(event) => setLineForm((current) => ({ ...current, remoteMinimumHours: event.target.value }))} />
            </label>
            <label>
              Onsite minimum
              <input type="number" min="0" step="0.25" value={lineForm.onsiteMinimumHours} onChange={(event) => setLineForm((current) => ({ ...current, onsiteMinimumHours: event.target.value }))} />
            </label>
            <label>
              Emergency daytime minimum
              <input type="number" min="0" step="0.25" value={lineForm.daytimeMinimumHours} onChange={(event) => setLineForm((current) => ({ ...current, daytimeMinimumHours: event.target.value }))} />
            </label>
            <label>
              AH / weekend / holiday minimum
              <input type="number" min="0" step="0.25" value={lineForm.afterhoursWeekendHolidayMinimumHours} onChange={(event) => setLineForm((current) => ({ ...current, afterhoursWeekendHolidayMinimumHours: event.target.value }))} />
            </label>
            <label className="full-width">
              Business hours text
              <input value={lineForm.businessHoursText} onChange={(event) => setLineForm((current) => ({ ...current, businessHoursText: event.target.value }))} placeholder="8:00am - 5:00pm, Monday through Friday" />
            </label>
            <label className="checkbox-line">
              <input type="checkbox" checked={lineForm.billableDefault} onChange={(event) => setLineForm((current) => ({ ...current, billableDefault: event.target.checked }))} />
              Billable by default
            </label>
            <label className="checkbox-line">
              <input type="checkbox" checked={lineForm.utilizationEligibleDefault} onChange={(event) => setLineForm((current) => ({ ...current, utilizationEligibleDefault: event.target.checked }))} />
              Utilization eligible by default
            </label>
            <label className="checkbox-line">
              <input type="checkbox" checked={lineForm.isEmergency} onChange={(event) => setLineForm((current) => ({ ...current, isEmergency: event.target.checked }))} />
              Emergency rate
            </label>
            <label className="checkbox-line">
              <input type="checkbox" checked={lineForm.isTravel} onChange={(event) => setLineForm((current) => ({ ...current, isTravel: event.target.checked }))} />
              Travel rate
            </label>
            <label className="checkbox-line">
              <input type="checkbox" checked={lineForm.isActive} onChange={(event) => setLineForm((current) => ({ ...current, isActive: event.target.checked }))} />
              Active
            </label>
            <label className="full-width">
              Notes
              <textarea rows={2} value={lineForm.notes} onChange={(event) => setLineForm((current) => ({ ...current, notes: event.target.value }))} />
            </label>
            <label className="full-width">
              Change reason
              <textarea rows={2} value={lineForm.changeReason} onChange={(event) => setLineForm((current) => ({ ...current, changeReason: event.target.value }))} placeholder="Explain why this rate is being added or changed." />
            </label>
            <div className="rate-card-form-actions">
              <button type="submit" className="primary-action">Save rate line</button>
              <button type="button" className="secondary-action" onClick={() => setLineForm({ ...emptyLineForm, rateCardId: selectedRateCard?.rateCardId ?? '' })}>Clear</button>
            </div>
          </form>
        </article>
      </div>

      <article className="rate-card-admin-panel">
        <h3>Recent rate changes</h3>
        <div className="rate-change-list">
          {recentChanges.map((item) => (
            <div className="rate-change-row" key={item.historyId}>
              <strong>{labelize(item.action)}</strong>
              <span>{item.changeSummary}</span>
              <small>{item.changedBy || 'Unknown user'} · {new Date(item.changedAt).toLocaleString()}</small>
            </div>
          ))}
          {!payload.loading && recentChanges.length === 0 ? <p className="muted">No rate change history has been recorded yet.</p> : null}
        </div>
      </article>
    </section>
  );
}

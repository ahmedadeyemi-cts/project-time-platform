import { useEffect, useMemo, useState } from 'react';
import './contracts-center.css';

function headers() {
  try {
    const session = JSON.parse(
      localStorage.getItem('projectPulseAuthSession') || 'null'
    );

    return session?.sessionToken
      ? { Authorization: `Bearer ${session.sessionToken}` }
      : {};
  } catch {
    return {};
  }
}

async function fetchJson(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      ...headers(),
      ...(options.body ? { 'Content-Type': 'application/json' } : {})
    }
  });

  const raw = await response.text();
  let payload = {};

  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = { message: raw };
  }

  if (!response.ok) {
    throw new Error(
      payload.message || `${path} returned HTTP ${response.status}`
    );
  }

  return payload;
}

function number(value) {
  return Number(value || 0).toLocaleString(undefined, {
    maximumFractionDigits: 2
  });
}

function date(value) {
  if (!value) {
    return '—';
  }

  return new Date(`${value}T00:00:00`).toLocaleDateString();
}

function HeaderHelp({ label, explanation }) {
  return (
    <span className="contracts-header-label">
      {label}
      <button
        type="button"
        className="contracts-header-help"
        title={explanation}
        aria-label={`${label}: ${explanation}`}
      >
        ?
      </button>
    </span>
  );
}

const emptyForm = {
  clientId: '',
  contractName: '',
  primaryAccountExecutiveUserId: '',
  purchasedHours: '',
  startDate: '',
  originalExpirationDate: '',
  eligibleTm: true,
  eligibleServiceRequest: true,
  eligibleFixedPrice: true,
  eligibleIqs: true,
  certiniaId: '',
  sellQuote: '',
  salesforceId: '',
  purchaseOrderReference: '',
  internalSummary: ''
};

export default function ContractsCenter() {
  const [data, setData] = useState(null);
  const [help, setHelp] = useState(null);
  const [helpOpen, setHelpOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [scheduleOpen, setScheduleOpen] = useState(false);
  const [form, setForm] = useState(emptyForm);
  const [schedule, setSchedule] = useState(null);
  const [query, setQuery] = useState('');
  const [aeFilter, setAeFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    setError('');

    try {
      const payload = await fetchJson('/api/contracts/overview');
      setData(payload);
      setSchedule(payload.schedule || null);
    } catch (loadError) {
      setError(loadError.message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function openHelp() {
    setHelpOpen(true);

    if (help) {
      return;
    }

    try {
      setHelp(await fetchJson('/api/contracts/help'));
    } catch (helpError) {
      setError(helpError.message);
    }
  }

  async function createContract(event) {
    event.preventDefault();
    setMessage('');
    setError('');

    try {
      await fetchJson('/api/contracts', {
        method: 'POST',
        body: JSON.stringify({
          ...form,
          purchasedHours: Number(form.purchasedHours || 0),
          originalExpirationDate:
            form.originalExpirationDate || null
        })
      });

      setMessage('Block of Hours contract created.');
      setForm(emptyForm);
      setCreateOpen(false);
      await load();
    } catch (saveError) {
      setError(saveError.message);
    }
  }

  async function saveSchedule(event) {
    event.preventDefault();
    setMessage('');
    setError('');

    try {
      await fetchJson('/api/contracts/email-schedule', {
        method: 'PUT',
        body: JSON.stringify(schedule)
      });

      setMessage('Weekly report schedule saved.');
      setScheduleOpen(false);
      await load();
    } catch (saveError) {
      setError(saveError.message);
    }
  }

  const formulas = useMemo(() => {
    const result = {};

    (data?.formulas || []).forEach((item) => {
      result[item.key] = [
        item.formula,
        `Source: ${item.source}.`,
        `Balance impact: ${item.balanceImpact}.`
      ].join(' ');
    });

    return result;
  }, [data]);

  const contracts = useMemo(() => {
    const search = query.trim().toLowerCase();

    return (data?.contracts || []).filter((contract) => {
      const matchesSearch =
        !search
        || [
          contract.customerName,
          contract.contractName,
          contract.accountExecutiveName,
          contract.certiniaId,
          contract.sellQuote,
          contract.salesforceId
        ]
          .filter(Boolean)
          .some((value) =>
            String(value).toLowerCase().includes(search)
          );

      return (
        matchesSearch
        && (!aeFilter
          || String(contract.accountExecutiveUserId) === aeFilter)
        && (!statusFilter || contract.status === statusFilter)
      );
    });
  }, [data, query, aeFilter, statusFilter]);

  const summary = data?.summary || {};
  const canManage = Boolean(data?.canManage);

  return (
    <section className="contracts-center">
      <header className="contracts-hero">
        <div>
          <p className="eyebrow">MODULE 060</p>
          <h1>Contracts & Block of Hours</h1>
          <p>
            Manage prepaid customer hours, credits, expiration,
            work consumption, and weekly Sales reporting.
          </p>
        </div>

        <div className="contracts-hero-actions">
          <button type="button" onClick={openHelp}>
            ? Help
          </button>

          {canManage ? (
            <>
              <button
                type="button"
                onClick={() => setScheduleOpen(true)}
              >
                Weekly email schedule
              </button>

              <button
                type="button"
                className="primary-action"
                onClick={() => setCreateOpen(true)}
              >
                New contract
              </button>
            </>
          ) : (
            <span className="contracts-readonly">Read only</span>
          )}
        </div>
      </header>

      {error ? (
        <div className="contracts-alert error">{error}</div>
      ) : null}

      {message ? (
        <div className="contracts-alert">{message}</div>
      ) : null}

      <div className="contracts-summary-grid">
        <article>
          <span>Active contracts</span>
          <strong>{summary.activeContracts || 0}</strong>
        </article>
        <article>
          <span>Purchased hours</span>
          <strong>{number(summary.purchasedHours)}</strong>
        </article>
        <article>
          <span>Credits awarded</span>
          <strong>{number(summary.creditAwarded)}</strong>
        </article>
        <article>
          <span>Remaining balance</span>
          <strong>{number(summary.remainingBalance)}</strong>
        </article>
        <article className="warning">
          <span>Low balance</span>
          <strong>{summary.lowBalanceContracts || 0}</strong>
        </article>
        <article className="warning">
          <span>Expiring</span>
          <strong>{summary.expiringContracts || 0}</strong>
        </article>
        <article className="danger">
          <span>Expired</span>
          <strong>{summary.expiredContracts || 0}</strong>
        </article>
        <article className="danger">
          <span>Exhausted</span>
          <strong>{summary.exhaustedContracts || 0}</strong>
        </article>
      </div>

      <section className="contracts-report-card">
        <div>
          <h2>Weekly AE workbook</h2>
          <p>
            Global SMTP · XLSX · grouped by Account Executive ·
            filters and frozen panes enabled
          </p>
        </div>

        <div>
          <strong>
            {schedule?.isEnabled ? 'Enabled' : 'Disabled'}
          </strong>
          <span>
            Weekday {schedule?.weekdayIso || 1}
            {' · '}
            {schedule?.sendTime || '08:00'}
            {' · '}
            {schedule?.timeZone || 'America/Chicago'}
          </span>
        </div>
      </section>

      <section className="contracts-filters">
        <input
          type="search"
          placeholder="Search customer, contract, AE, or external ID"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />

        <select
          value={aeFilter}
          onChange={(event) => setAeFilter(event.target.value)}
        >
          <option value="">All Account Executives</option>
          {(data?.accountExecutives || []).map((user) => (
            <option key={user.userId} value={user.userId}>
              {user.displayName}
            </option>
          ))}
        </select>

        <select
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value)}
        >
          <option value="">All statuses</option>
          <option value="active">Active</option>
          <option value="low_balance">Low balance</option>
          <option value="expiring">Expiring</option>
          <option value="expired">Expired</option>
          <option value="exhausted">Exhausted</option>
          <option value="closed">Closed</option>
        </select>

        <button type="button" onClick={() => void load()}>
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      </section>

      <div className="contracts-table-scroll">
        <table className="contracts-table">
          <thead>
            <tr>
              <th>
                <HeaderHelp
                  label="Account Executive"
                  explanation="Primary active AE assigned from system users. Weekly workbooks are grouped by this user."
                />
              </th>
              <th>
                <HeaderHelp
                  label="Customer"
                  explanation="Customer and address are sourced from Customer Directory."
                />
              </th>
              <th>Contract</th>
              <th>
                <HeaderHelp
                  label="Purchased"
                  explanation={formulas.purchasedHours || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Credit awarded"
                  explanation={formulas.creditAwarded || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Total available"
                  explanation={formulas.totalAvailableHours || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Entered"
                  explanation={formulas.enteredHours || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Submitted"
                  explanation={formulas.submittedHours || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Consumed"
                  explanation={formulas.consumedHours || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Remaining"
                  explanation={formulas.remainingBalance || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Projected"
                  explanation={formulas.projectedRemaining || ''}
                />
              </th>
              <th>
                <HeaderHelp
                  label="Effective expiration"
                  explanation={formulas.effectiveExpiration || ''}
                />
              </th>
              <th>Certinia ID</th>
              <th>SELL Quote</th>
              <th>Salesforce ID</th>
              <th>Status</th>
            </tr>
          </thead>

          <tbody>
            {contracts.map((contract) => (
              <tr key={contract.bohContractId}>
                <td>
                  <strong>{contract.accountExecutiveName}</strong>
                  <small>{contract.accountExecutiveEmail}</small>
                </td>
                <td>
                  <strong>{contract.customerName}</strong>
                  <small>{contract.customerAddress || 'No address recorded'}</small>
                </td>
                <td>{contract.contractName}</td>
                <td>{number(contract.purchasedHours)}</td>
                <td>{number(contract.creditAwarded)}</td>
                <td>{number(contract.totalAvailableHours)}</td>
                <td>{number(contract.enteredHours)}</td>
                <td>{number(contract.submittedHours)}</td>
                <td>{number(contract.consumedHours)}</td>
                <td>{number(contract.remainingBalance)}</td>
                <td>{number(contract.projectedRemaining)}</td>
                <td>{date(contract.effectiveExpirationDate)}</td>
                <td>{contract.certiniaId || '—'}</td>
                <td>{contract.sellQuote || '—'}</td>
                <td>{contract.salesforceId || '—'}</td>
                <td>
                  <span className={`contracts-status ${contract.status}`}>
                    {contract.status.replaceAll('_', ' ')}
                  </span>
                </td>
              </tr>
            ))}

            {!loading && contracts.length === 0 ? (
              <tr>
                <td colSpan="16" className="contracts-empty">
                  No Block of Hours contracts match this selection.
                  No sample records are created automatically.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      {helpOpen ? (
        <div className="contracts-drawer-backdrop">
          <aside className="contracts-drawer" aria-label="Contracts help">
            <header>
              <div>
                <p className="eyebrow">EMBEDDED HELP</p>
                <h2>{help?.title || 'Contracts Help'}</h2>
              </div>
              <button type="button" onClick={() => setHelpOpen(false)}>
                Close
              </button>
            </header>

            {(help?.sections || []).map((section) => (
              <article key={section.title}>
                <h3>{section.title}</h3>
                <p>{section.content}</p>
              </article>
            ))}

            <h3>Column formulas</h3>
            {(help?.formulas || data?.formulas || []).map((item) => (
              <article key={item.key}>
                <strong>{item.label}</strong>
                <p>{item.formula}</p>
                <small>
                  Source: {item.source}
                  {' · '}
                  {item.balanceImpact}
                </small>
              </article>
            ))}
          </aside>
        </div>
      ) : null}

      {createOpen && canManage ? (
        <div className="contracts-drawer-backdrop">
          <aside className="contracts-drawer wide">
            <header>
              <div>
                <p className="eyebrow">PROJECT TEAM COORDINATOR</p>
                <h2>Create Block of Hours contract</h2>
              </div>
              <button type="button" onClick={() => setCreateOpen(false)}>
                Close
              </button>
            </header>

            <form className="contracts-form" onSubmit={createContract}>
              <label>
                Customer
                <select
                  required
                  value={form.clientId}
                  onChange={(event) =>
                    setForm({ ...form, clientId: event.target.value })
                  }
                >
                  <option value="">Select customer</option>
                  {(data?.customers || []).map((customer) => (
                    <option key={customer.clientId} value={customer.clientId}>
                      {customer.customerName}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Account Executive
                <select
                  required
                  value={form.primaryAccountExecutiveUserId}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      primaryAccountExecutiveUserId: event.target.value
                    })
                  }
                >
                  <option value="">Select Account Executive</option>
                  {(data?.accountExecutives || []).map((user) => (
                    <option key={user.userId} value={user.userId}>
                      {user.displayName} — {user.email}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Contract / engagement name
                <input
                  required
                  value={form.contractName}
                  onChange={(event) =>
                    setForm({ ...form, contractName: event.target.value })
                  }
                />
              </label>

              <label>
                Purchased hours
                <input
                  required
                  type="number"
                  min="0"
                  step="0.25"
                  value={form.purchasedHours}
                  onChange={(event) =>
                    setForm({ ...form, purchasedHours: event.target.value })
                  }
                />
              </label>

              <label>
                Start date
                <input
                  required
                  type="date"
                  value={form.startDate}
                  onChange={(event) =>
                    setForm({ ...form, startDate: event.target.value })
                  }
                />
              </label>

              <label>
                Original expiration
                <input
                  type="date"
                  value={form.originalExpirationDate}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      originalExpirationDate: event.target.value
                    })
                  }
                />
                <small>Blank defaults to one year after start.</small>
              </label>

              <label>
                Certinia ID
                <input
                  value={form.certiniaId}
                  onChange={(event) =>
                    setForm({ ...form, certiniaId: event.target.value })
                  }
                />
              </label>

              <label>
                SELL Quote
                <input
                  value={form.sellQuote}
                  onChange={(event) =>
                    setForm({ ...form, sellQuote: event.target.value })
                  }
                />
              </label>

              <label>
                Salesforce ID
                <input
                  value={form.salesforceId}
                  onChange={(event) =>
                    setForm({ ...form, salesforceId: event.target.value })
                  }
                />
              </label>

              <label>
                PO / Quote reference
                <input
                  value={form.purchaseOrderReference}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      purchaseOrderReference: event.target.value
                    })
                  }
                />
              </label>

              <fieldset>
                <legend>Eligible work types</legend>
                {[
                  ['eligibleTm', 'T&M'],
                  ['eligibleServiceRequest', 'Service Request'],
                  ['eligibleFixedPrice', 'Fixed Price'],
                  ['eligibleIqs', 'IQS']
                ].map(([key, label]) => (
                  <label key={key} className="contracts-checkbox">
                    <input
                      type="checkbox"
                      checked={form[key]}
                      onChange={(event) =>
                        setForm({ ...form, [key]: event.target.checked })
                      }
                    />
                    {label}
                  </label>
                ))}
              </fieldset>

              <label className="contracts-form-wide">
                Notes / summary
                <textarea
                  rows="4"
                  value={form.internalSummary}
                  onChange={(event) =>
                    setForm({ ...form, internalSummary: event.target.value })
                  }
                />
              </label>

              <div className="contracts-form-actions">
                <button type="button" onClick={() => setCreateOpen(false)}>
                  Cancel
                </button>
                <button type="submit" className="primary-action">
                  Create contract
                </button>
              </div>
            </form>
          </aside>
        </div>
      ) : null}

      {scheduleOpen && canManage && schedule ? (
        <div className="contracts-drawer-backdrop">
          <aside className="contracts-drawer wide">
            <header>
              <div>
                <p className="eyebrow">GLOBAL SMTP</p>
                <h2>Weekly AE balance email</h2>
              </div>
              <button type="button" onClick={() => setScheduleOpen(false)}>
                Close
              </button>
            </header>

            <form className="contracts-form" onSubmit={saveSchedule}>
              <label className="contracts-checkbox">
                <input
                  type="checkbox"
                  checked={schedule.isEnabled}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      isEnabled: event.target.checked
                    })
                  }
                />
                Enable weekly email
              </label>

              <label>
                Weekday
                <select
                  value={schedule.weekdayIso}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      weekdayIso: Number(event.target.value)
                    })
                  }
                >
                  <option value="1">Monday</option>
                  <option value="2">Tuesday</option>
                  <option value="3">Wednesday</option>
                  <option value="4">Thursday</option>
                  <option value="5">Friday</option>
                </select>
              </label>

              <label>
                Send time
                <input
                  type="time"
                  value={String(schedule.sendTime || '08:00').slice(0, 5)}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      sendTime: event.target.value
                    })
                  }
                />
              </label>

              <label>
                Time zone
                <input
                  value={schedule.timeZone}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      timeZone: event.target.value
                    })
                  }
                />
              </label>

              <label className="contracts-form-wide">
                Subject
                <input
                  value={schedule.subjectTemplate}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      subjectTemplate: event.target.value
                    })
                  }
                />
              </label>

              <label className="contracts-form-wide">
                Email introduction
                <textarea
                  rows="4"
                  value={schedule.bodyIntroduction}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      bodyIntroduction: event.target.value
                    })
                  }
                />
              </label>

              <label>
                Low-balance threshold %
                <input
                  type="number"
                  min="0"
                  max="100"
                  step="1"
                  value={schedule.lowBalanceThresholdPercent}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      lowBalanceThresholdPercent:
                        Number(event.target.value)
                    })
                  }
                />
              </label>

              <label>
                Expiration warning days
                <input
                  type="number"
                  min="0"
                  value={schedule.expirationWarningDays}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      expirationWarningDays:
                        Number(event.target.value)
                    })
                  }
                />
              </label>

              <label>
                Retention months
                <input
                  type="number"
                  min="1"
                  max="120"
                  value={schedule.retentionMonths}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      retentionMonths: Number(event.target.value)
                    })
                  }
                />
              </label>

              <label className="contracts-checkbox">
                <input
                  type="checkbox"
                  checked={schedule.includeExpired}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      includeExpired: event.target.checked
                    })
                  }
                />
                Include expired contracts
              </label>

              <div className="contracts-recipient-note contracts-form-wide">
                To: active Account Executive / Sales users from the system.
                Cc: active Project Team Coordinator and Executive users.
                Invalid or inactive addresses are excluded and audited.
              </div>

              <div className="contracts-form-actions">
                <button type="button" onClick={() => setScheduleOpen(false)}>
                  Cancel
                </button>
                <button type="submit" className="primary-action">
                  Save schedule
                </button>
              </div>
            </form>
          </aside>
        </div>
      ) : null}
    </section>
  );
}

import { useEffect, useMemo, useRef, useState } from 'react';
import './contracts-center.css';

function sessionHeaders() {
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

async function request(path, options = {}) {
  const isForm = options.body instanceof FormData;
  const response = await fetch(path, {
    ...options,
    headers: {
      ...sessionHeaders(),
      ...(!isForm && options.body
        ? { 'Content-Type': 'application/json' }
        : {}),
      ...(options.headers || {})
    }
  });

  if (options.download) {
    if (!response.ok) {
      throw new Error(`Download returned HTTP ${response.status}`);
    }

    return response;
  }

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

function money(value) {
  return Number(value || 0).toLocaleString(undefined, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 2
  });
}

function percent(value) {
  return value === null || value === undefined
    ? '—'
    : Number(value).toLocaleString(undefined, {
        style: 'percent',
        maximumFractionDigits: 2
      });
}

function date(value) {
  return value
    ? new Date(`${value}T00:00:00`).toLocaleDateString()
    : '—';
}

function friendlyNameFromEmail(email) {
  const localPart = String(email || '').split('@')[0];

  return localPart
    .split(/[._\-\s]+/)
    .filter(Boolean)
    .map((word) =>
      word.charAt(0).toUpperCase() + word.slice(1).toLowerCase()
    )
    .join(' ');
}

function userOptionId(item) {
  return item?.userId || item?.UserId || '';
}

function userOptionEmail(item) {
  return String(item?.email || item?.Email || '').trim();
}

function userOptionName(item) {
  const email = userOptionEmail(item);
  const rawName = String(
    item?.displayName || item?.DisplayName || ''
  ).trim();

  if (rawName && rawName.toLowerCase() !== email.toLowerCase()) {
    return rawName;
  }

  return friendlyNameFromEmail(email) || email || 'Unnamed user';
}

function userOptionLabel(item) {
  const name = userOptionName(item);
  const email = userOptionEmail(item);

  return email && name.toLowerCase() !== email.toLowerCase()
    ? `${name} — ${email}`
    : name;
}

function customerOptionId(item) {
  return item?.clientId || item?.ClientId || '';
}

function customerOptionName(item) {
  return String(
    item?.customerName || item?.CustomerName || 'Unnamed customer'
  ).trim();
}

const emptyContract = {
  clientId: '',
  accountExecutiveUserId: '',
  projectTeamCoordinatorUserId: '',
  engagementName: '',
  poQuote: '',
  contractStartDate: '',
  contractEndDate: '',
  fixedFeeItem: '',
  latestTimeText: '',
  billingDate: '',
  fixedFeeAmount: '',
  pendingAmount: '',
  approvedAmount: '',
  totalExpenses: '',
  adjustments: '',
  certiniaId: '',
  sellQuote: '',
  salesforceId: '',
  notes: ''
};

const emptyCredit = {
  amount: '',
  awardedOn: new Date().toISOString().slice(0, 10),
  reason: '',
  reference: ''
};

const emptyNote = {
  category: 'general',
  noteText: ''
};

function Drawer({ title, subtitle, onClose, children, wide = false }) {
  return (
    <div className="prepaid-drawer-backdrop">
      <aside className={`prepaid-drawer ${wide ? 'wide' : ''}`}>
        <header>
          <div>
            <p>{subtitle}</p>
            <h2>{title}</h2>
          </div>
          <button type="button" onClick={onClose} aria-label="Close">
            ×
          </button>
        </header>
        <div className="prepaid-drawer-body">{children}</div>
      </aside>
    </div>
  );
}

export default function ContractsCenter() {
  const [overview, setOverview] = useState(null);
  const [options, setOptions] = useState(null);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [expanded, setExpanded] = useState({});
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [createForm, setCreateForm] = useState(emptyContract);
  const [details, setDetails] = useState(null);
  const [creditForm, setCreditForm] = useState(emptyCredit);
  const [noteForm, setNoteForm] = useState(emptyNote);
  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadPreview, setUploadPreview] = useState(null);
  const [uploadBusy, setUploadBusy] = useState(false);
  const [scheduleOpen, setScheduleOpen] = useState(false);
  const [schedule, setSchedule] = useState(null);
  const fileRef = useRef(null);

  async function load() {
    setLoading(true);
    setError('');

    try {
      const payload = await request('/api/contracts/prepaid/overview');
      setOverview(payload);

      const nextExpanded = {};
      (payload.groups || []).forEach((group) => {
        nextExpanded[group.accountExecutiveUserId] = true;
      });
      setExpanded(nextExpanded);

      if (payload?.permissions?.canManage) {
        const management = await request('/api/contracts/prepaid/options');
        setOptions(management);
        setSchedule(management.schedule || null);
      } else {
        setOptions(null);
        setSchedule(null);
      }
    } catch (loadError) {
      setError(loadError.message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const permissions = overview?.permissions || {};
  const summary = overview?.summary || {};

  const groups = useMemo(() => {
    const search = query.trim().toLowerCase();

    return (overview?.groups || [])
      .map((group) => ({
        ...group,
        contracts: (group.contracts || []).filter((row) => {
          const matchesSearch =
            !search
            || [
              row.accountExecutiveName,
              row.customerName,
              row.engagementName,
              row.contractManagerName,
              row.poQuote,
              row.certiniaId,
              row.sellQuote,
              row.salesforceId,
              row.latestNote
            ]
              .filter(Boolean)
              .some((value) =>
                String(value).toLowerCase().includes(search)
              );

          return (
            matchesSearch
            && (!statusFilter || row.contractStatus === statusFilter)
          );
        })
      }))
      .filter((group) => group.contracts.length > 0);
  }, [overview, query, statusFilter]);

  async function download(path, fallbackName) {
    setError('');

    try {
      const response = await request(path, { download: true });
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') || '';
      const match = disposition.match(/filename="?([^"]+)"?/i);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = match?.[1] || fallbackName;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
    } catch (downloadError) {
      setError(downloadError.message);
    }
  }

  async function createContract(event) {
    event.preventDefault();
    setError('');

    try {
      await request('/api/contracts/prepaid/contracts', {
        method: 'POST',
        body: JSON.stringify({
          ...createForm,
          billingDate: createForm.billingDate || null,
          fixedFeeAmount: Number(createForm.fixedFeeAmount || 0),
          pendingAmount: Number(createForm.pendingAmount || 0),
          approvedAmount: Number(createForm.approvedAmount || 0),
          totalExpenses: Number(createForm.totalExpenses || 0),
          adjustments: Number(createForm.adjustments || 0)
        })
      });

      setCreateOpen(false);
      setCreateForm(emptyContract);
      setMessage('Prepaid contract created.');
      await load();
    } catch (saveError) {
      setError(saveError.message);
    }
  }

  async function openDetails(contractId) {
    setError('');

    try {
      setDetails(
        await request(`/api/contracts/prepaid/contracts/${contractId}`)
      );
    } catch (detailsError) {
      setError(detailsError.message);
    }
  }

  async function awardCredit(event) {
    event.preventDefault();

    try {
      await request(
        `/api/contracts/prepaid/${details.contract.bohContractId}/credits`,
        {
          method: 'POST',
          body: JSON.stringify({
            ...creditForm,
            amount: Number(creditForm.amount || 0)
          })
        }
      );

      setCreditForm(emptyCredit);
      await openDetails(details.contract.bohContractId);
      await load();
      setMessage('Credit awarded and added to total available.');
    } catch (creditError) {
      setError(creditError.message);
    }
  }

  async function reverseCredit(adjustmentId) {
    const reason = window.prompt('Reason for reversing this credit:');

    if (!reason) {
      return;
    }

    try {
      await request(
        `/api/contracts/prepaid/credits/${adjustmentId}/reverse`,
        {
          method: 'POST',
          body: JSON.stringify({
            reversedOn: new Date().toISOString().slice(0, 10),
            reason
          })
        }
      );

      await openDetails(details.contract.bohContractId);
      await load();
      setMessage('Credit reversal recorded. History was retained.');
    } catch (reverseError) {
      setError(reverseError.message);
    }
  }

  async function addNote(event) {
    event.preventDefault();

    try {
      await request(
        `/api/contracts/prepaid/${details.contract.bohContractId}/notes`,
        {
          method: 'POST',
          body: JSON.stringify(noteForm)
        }
      );

      setNoteForm(emptyNote);
      await openDetails(details.contract.bohContractId);
      await load();
      setMessage('Note added.');
    } catch (noteError) {
      setError(noteError.message);
    }
  }

  async function previewUpload(event) {
    event.preventDefault();
    const file = fileRef.current?.files?.[0];

    if (!file) {
      setError('Select an XLSX workbook.');
      return;
    }

    const formData = new FormData();
    formData.append('file', file);
    setUploadBusy(true);
    setError('');

    try {
      setUploadPreview(
        await request('/api/contracts/prepaid/import-preview', {
          method: 'POST',
          body: formData
        })
      );
    } catch (uploadError) {
      setError(uploadError.message);
    } finally {
      setUploadBusy(false);
    }
  }

  async function confirmUpload() {
    if (!uploadPreview?.batchId) {
      return;
    }

    setUploadBusy(true);
    setError('');

    try {
      await request(
        `/api/contracts/prepaid/imports/${uploadPreview.batchId}/confirm`,
        { method: 'POST' }
      );

      setUploadOpen(false);
      setUploadPreview(null);
      setMessage('XLSX import confirmed.');
      await load();
    } catch (confirmError) {
      setError(confirmError.message);
    } finally {
      setUploadBusy(false);
    }
  }

  async function saveSchedule(event) {
    event.preventDefault();

    try {
      await request('/api/contracts/prepaid/email-schedule', {
        method: 'PUT',
        body: JSON.stringify({
          ...schedule,
          weekdayIso: Number(schedule.weekdayIso || 1),
          lowBalanceThresholdPercent: Number(
            schedule.lowBalanceThresholdPercent || 25
          ),
          expirationWarningDays: Number(
            schedule.expirationWarningDays || 90
          ),
          retentionMonths: Number(schedule.retentionMonths || 24)
        })
      });

      setScheduleOpen(false);
      setMessage('Weekly email schedule saved.');
      await load();
    } catch (scheduleError) {
      setError(scheduleError.message);
    }
  }

  return (
    <section className="prepaid-center">
      <header className="prepaid-hero">
        <div>
          <p>MODULE 060</p>
          <h1>Contracts & Prepaid Balance</h1>
          <span>
            Financial balances, credits, notes, XLSX exchange, and
            contract-funded Work Register usage.
          </span>
        </div>

        <div className="prepaid-actions">
          {permissions.canDownload ? (
            <>
              <button
                type="button"
                onClick={() =>
                  void download(
                    '/api/contracts/prepaid/template',
                    'Module-060-Prepaid-Balance-Import-Template.xlsx'
                  )
                }
              >
                Import template
              </button>
              <button
                type="button"
                onClick={() =>
                  void download(
                    '/api/contracts/prepaid/export',
                    'OneNeck-Prepaid-Balance-Summary.xlsx'
                  )
                }
              >
                Download XLSX
              </button>
            </>
          ) : null}

          {permissions.canUpload ? (
            <button type="button" onClick={() => setUploadOpen(true)}>
              Upload XLSX
            </button>
          ) : null}

          {permissions.canManageSchedule ? (
            <button type="button" onClick={() => setScheduleOpen(true)}>
              Email schedule
            </button>
          ) : null}

          {permissions.canManage ? (
            <button
              type="button"
              className="primary"
              onClick={() => setCreateOpen(true)}
            >
              New contract
            </button>
          ) : (
            <span className="prepaid-readonly">Read only</span>
          )}
        </div>
      </header>

      {error ? <div className="prepaid-alert error">{error}</div> : null}
      {message ? <div className="prepaid-alert">{message}</div> : null}

      <div className="prepaid-summary-grid">
        {[
          ['Contracts', summary.contractCount || 0],
          ['FF Amount', money(summary.fixedFeeAmount)],
          ['Credit Awarded', money(summary.creditAwarded)],
          ['Total Available', money(summary.totalAvailable)],
          ['Pending', money(summary.pendingAmount)],
          ['Approved', money(summary.approvedAmount)],
          ['Total Used', money(summary.totalUsed)],
          ['Remaining Balance', money(summary.remainingBalance)]
        ].map(([label, value]) => (
          <article key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </article>
        ))}
      </div>

      <section className="prepaid-toolbar">
        <input
          type="search"
          value={query}
          placeholder="Search AE, customer, engagement, manager, quote, ID, or note"
          onChange={(event) => setQuery(event.target.value)}
        />
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

      <div className="prepaid-groups">
        {groups.map((group) => (
          <section
            className="prepaid-ae-group"
            key={group.accountExecutiveUserId}
          >
            <button
              type="button"
              className="prepaid-ae-header"
              onClick={() =>
                setExpanded((current) => ({
                  ...current,
                  [group.accountExecutiveUserId]:
                    !current[group.accountExecutiveUserId]
                }))
              }
            >
              <span>
                <strong>{group.accountExecutiveName}</strong>
                <small>{group.contracts.length} visible contracts</small>
              </span>
              <span>
                Available {money(group.fixedFeeAmount + group.creditAwarded)}
                {' · '}
                Used {money(group.totalUsed)}
                {' · '}
                Remaining {money(group.remainingBalance)}
              </span>
              <b>
                {expanded[group.accountExecutiveUserId] ? '−' : '+'}
              </b>
            </button>

            {expanded[group.accountExecutiveUserId] ? (
              <div className="prepaid-table-scroll">
                <table className="prepaid-table">
                  <thead>
                    <tr>
                      <th>Customer</th>
                      <th>Engagement Name</th>
                      <th>Contract Manager</th>
                      <th>PO/Quote</th>
                      <th>Start</th>
                      <th>End</th>
                      <th>Fixed Fee Item</th>
                      <th>Billing Date</th>
                      <th>FF Amount</th>
                      <th>Credit Awarded</th>
                      <th>Credit Date</th>
                      <th>Credit Awarded By</th>
                      <th>Pending</th>
                      <th>Approved</th>
                      <th>Total Hours</th>
                      <th>Total Expenses</th>
                      <th>Adjustments</th>
                      <th>Total Used</th>
                      <th>Total Available</th>
                      <th>Remaining</th>
                      <th>Balance %</th>
                      <th>Certinia ID</th>
                      <th>SELL Quote</th>
                      <th>Salesforce ID</th>
                      <th>Notes</th>
                    </tr>
                  </thead>
                  <tbody>
                    {group.contracts.map((row) => (
                      <tr
                        key={row.bohContractId}
                        onClick={() => void openDetails(row.bohContractId)}
                      >
                        <td>{row.customerName}</td>
                        <td>
                          <strong>{row.engagementName}</strong>
                          <small>{row.contractStatus}</small>
                        </td>
                        <td>{row.contractManagerName}</td>
                        <td>{row.poQuote || '—'}</td>
                        <td>{date(row.contractStartDate)}</td>
                        <td>{date(row.contractEndDate)}</td>
                        <td>{row.fixedFeeItem || '—'}</td>
                        <td>{date(row.billingDate)}</td>
                        <td>{money(row.fixedFeeAmount)}</td>
                        <td>{money(row.creditAwarded)}</td>
                        <td>{date(row.latestCreditAwardedOn)}</td>
                        <td>{row.latestCreditAwardedBy || '—'}</td>
                        <td>{money(row.pendingAmount)}</td>
                        <td>{money(row.approvedAmount)}</td>
                        <td>{money(row.totalHoursAmount)}</td>
                        <td>{money(row.totalExpenses)}</td>
                        <td>{money(row.adjustments)}</td>
                        <td>{money(row.totalUsed)}</td>
                        <td>{money(row.totalAvailable)}</td>
                        <td className={Number(row.remainingBalance) < 0 ? 'negative' : ''}>
                          {money(row.remainingBalance)}
                        </td>
                        <td>{percent(row.balancePercent)}</td>
                        <td>{row.certiniaId || '—'}</td>
                        <td>{row.sellQuote || '—'}</td>
                        <td>{row.salesforceId || '—'}</td>
                        <td>{row.noteCount || 0}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </section>
        ))}

        {!loading && groups.length === 0 ? (
          <div className="prepaid-empty">
            No prepaid contracts match this selection.
          </div>
        ) : null}
      </div>

      {createOpen ? (
        <Drawer
          title="Create prepaid contract"
          subtitle="SYSTEM-SOURCED CUSTOMER, AE, AND CONTRACT MANAGER"
          onClose={() => setCreateOpen(false)}
          wide
        >
          <form className="prepaid-form-grid" onSubmit={createContract}>
            <label>
              Customer
              <select
                required
                value={createForm.clientId}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    clientId: event.target.value
                  })
                }
              >
                <option value="">Select Customer Directory record</option>
                {(options?.customers || []).map((item) => {
                  const clientId = customerOptionId(item);

                  return (
                    <option key={clientId} value={clientId}>
                      {customerOptionName(item)}
                    </option>
                  );
                })}
              </select>
            </label>

            <label>
              Account Executive
              <select
                required
                value={createForm.accountExecutiveUserId}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    accountExecutiveUserId: event.target.value
                  })
                }
              >
                <option value="">Select AE / Sales Team member</option>
                {(options?.accountExecutives || []).map((item) => {
                  const userId = userOptionId(item);

                  return (
                    <option
                      key={userId || userOptionEmail(item)}
                      value={userId}
                    >
                      {userOptionLabel(item)}
                    </option>
                  );
                })}
              </select>
            </label>

            <label>
              Contract Manager
              <select
                required
                value={createForm.projectTeamCoordinatorUserId}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    projectTeamCoordinatorUserId: event.target.value
                  })
                }
              >
                <option value="">Select Project Team Coordinator</option>
                {(options?.coordinators || []).map((item) => {
                  const userId = userOptionId(item);

                  return (
                    <option
                      key={userId || userOptionEmail(item)}
                      value={userId}
                    >
                      {userOptionLabel(item)}
                    </option>
                  );
                })}
              </select>
            </label>

            <fieldset className="prepaid-commercial-fields full">
              <legend>Commercial identifiers</legend>
              <div className="prepaid-commercial-grid">
                {[
                  ['poQuote', 'PO/Quote', 'Customer PO or quote reference'],
                  ['sellQuote', 'SELL Quote', 'SELL quote number'],
                  ['salesforceId', 'Salesforce ID', 'Salesforce opportunity or record ID'],
                  ['certiniaId', 'Certinia ID', 'Certinia contract or engagement ID']
                ].map(([name, label, placeholder]) => (
                  <label key={name}>
                    {label}
                    <input
                      type="text"
                      value={createForm[name]}
                      placeholder={placeholder}
                      onChange={(event) =>
                        setCreateForm({
                          ...createForm,
                          [name]: event.target.value
                        })
                      }
                    />
                  </label>
                ))}
              </div>
            </fieldset>

            {[
              ['engagementName', 'Engagement Name', 'text', true],
              ['contractStartDate', 'Contract Start Date', 'date', true],
              ['contractEndDate', 'Contract End Date', 'date', true],
              ['fixedFeeItem', 'Fixed Fee Item', 'text', false],
              ['latestTimeText', 'Latest Time Text', 'text', false],
              ['billingDate', 'Billing Date', 'date', false],
              ['fixedFeeAmount', 'FF Amount', 'number', true],
              ['pendingAmount', 'Pending Hours', 'number', false],
              ['approvedAmount', 'Approved Hours', 'number', false],
              ['totalExpenses', 'Total Expenses', 'number', false],
              ['adjustments', 'Adjustments', 'number', false]
            ].map(([name, label, type, required]) => (
              <label key={name}>
                {label}
                <input
                  required={required}
                  type={type}
                  step={type === 'number' ? '0.01' : undefined}
                  value={createForm[name]}
                  onChange={(event) =>
                    setCreateForm({
                      ...createForm,
                      [name]: event.target.value
                    })
                  }
                />
              </label>
            ))}

            <label className="full">
              Notes
              <textarea
                rows="4"
                value={createForm.notes}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    notes: event.target.value
                  })
                }
              />
            </label>

            <div className="prepaid-form-actions full">
              <button type="button" onClick={() => setCreateOpen(false)}>
                Cancel
              </button>
              <button type="submit" className="primary">
                Create contract
              </button>
            </div>
          </form>
        </Drawer>
      ) : null}

      {details ? (
        <Drawer
          title={details.contract.engagementName}
          subtitle={`${details.contract.customerName} · ${details.contract.accountExecutiveName}`}
          onClose={() => setDetails(null)}
          wide
        >
          <div className="prepaid-detail-grid">
            <article>
              <span>Total Available</span>
              <strong>{money(details.contract.totalAvailable)}</strong>
            </article>
            <article>
              <span>Total Used</span>
              <strong>{money(details.contract.totalUsed)}</strong>
            </article>
            <article>
              <span>Remaining</span>
              <strong>{money(details.contract.remainingBalance)}</strong>
            </article>
            <article>
              <span>Balance %</span>
              <strong>{percent(details.contract.balancePercent)}</strong>
            </article>
          </div>

          <section className="prepaid-detail-section">
            <h3>Credit history</h3>
            {(details.credits || []).map((credit) => (
              <article className="prepaid-history-item" key={credit.adjustmentId}>
                <div>
                  <strong>
                    {credit.adjustmentType === 'credit_reversal'
                      ? 'Reversal'
                      : 'Credit'}
                    {' · '}
                    {money(credit.amount)}
                  </strong>
                  <span>
                    {date(credit.awardedOn)} · {credit.awardedBy}
                  </span>
                  <p>{credit.reason}</p>
                </div>
                {permissions.canManage
                  && credit.adjustmentType === 'credit_awarded'
                  && !credit.reversesAdjustmentId ? (
                    <button
                      type="button"
                      onClick={() => void reverseCredit(credit.adjustmentId)}
                    >
                      Reverse
                    </button>
                  ) : null}
              </article>
            ))}

            {permissions.canManage ? (
              <form className="prepaid-inline-form" onSubmit={awardCredit}>
                <input
                  required
                  type="number"
                  step="0.01"
                  placeholder="Credit amount"
                  value={creditForm.amount}
                  onChange={(event) =>
                    setCreditForm({
                      ...creditForm,
                      amount: event.target.value
                    })
                  }
                />
                <input
                  required
                  type="date"
                  value={creditForm.awardedOn}
                  onChange={(event) =>
                    setCreditForm({
                      ...creditForm,
                      awardedOn: event.target.value
                    })
                  }
                />
                <input
                  required
                  placeholder="Reason"
                  value={creditForm.reason}
                  onChange={(event) =>
                    setCreditForm({
                      ...creditForm,
                      reason: event.target.value
                    })
                  }
                />
                <input
                  placeholder="Reference"
                  value={creditForm.reference}
                  onChange={(event) =>
                    setCreditForm({
                      ...creditForm,
                      reference: event.target.value
                    })
                  }
                />
                <button type="submit" className="primary">
                  Award credit
                </button>
              </form>
            ) : null}
          </section>

          <section className="prepaid-detail-section">
            <h3>Notes</h3>
            {(details.notes || []).map((note) => (
              <article className="prepaid-history-item" key={note.noteId}>
                <div>
                  <strong>{note.category}</strong>
                  <span>
                    {new Date(note.createdAt).toLocaleString()} · {note.author}
                  </span>
                  <p>{note.noteText}</p>
                </div>
              </article>
            ))}

            {permissions.canManage ? (
              <form className="prepaid-inline-form" onSubmit={addNote}>
                <select
                  value={noteForm.category}
                  onChange={(event) =>
                    setNoteForm({
                      ...noteForm,
                      category: event.target.value
                    })
                  }
                >
                  <option value="general">General</option>
                  <option value="credit">Credit</option>
                  <option value="billing">Billing</option>
                  <option value="contract">Contract</option>
                  <option value="work-register">Work Register</option>
                </select>
                <input
                  required
                  placeholder="Add an auditable note"
                  value={noteForm.noteText}
                  onChange={(event) =>
                    setNoteForm({
                      ...noteForm,
                      noteText: event.target.value
                    })
                  }
                />
                <button type="submit" className="primary">
                  Add note
                </button>
              </form>
            ) : null}
          </section>
        </Drawer>
      ) : null}

      {uploadOpen ? (
        <Drawer
          title="Upload prepaid balance workbook"
          subtitle="VALIDATE BEFORE IMPORT"
          onClose={() => {
            setUploadOpen(false);
            setUploadPreview(null);
          }}
          wide
        >
          <form className="prepaid-upload-form" onSubmit={previewUpload}>
            <input ref={fileRef} type="file" accept=".xlsx" required />
            <button type="submit" className="primary" disabled={uploadBusy}>
              {uploadBusy ? 'Validating…' : 'Validate workbook'}
            </button>
          </form>

          {uploadPreview ? (
            <>
              <div className="prepaid-detail-grid">
                <article>
                  <span>Total rows</span>
                  <strong>{uploadPreview.summary.totalRows}</strong>
                </article>
                <article>
                  <span>Valid</span>
                  <strong>{uploadPreview.summary.validRows}</strong>
                </article>
                <article>
                  <span>Invalid</span>
                  <strong>{uploadPreview.summary.invalidRows}</strong>
                </article>
                <article>
                  <span>Duplicates</span>
                  <strong>{uploadPreview.summary.duplicateRows}</strong>
                </article>
              </div>

              <div className="prepaid-table-scroll">
                <table className="prepaid-table compact">
                  <thead>
                    <tr>
                      <th>Row</th>
                      <th>Status</th>
                      <th>Change</th>
                      <th>AE</th>
                      <th>Customer</th>
                      <th>Engagement</th>
                      <th>Contract Manager</th>
                      <th>Validation</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(uploadPreview.rows || []).map((row) => (
                      <tr key={row.sourceRowNumber}>
                        <td>{row.sourceRowNumber}</td>
                        <td>{row.rowStatus}</td>
                        <td>{row.changeType}</td>
                        <td>{row.accountExecutiveText}</td>
                        <td>{row.customerText}</td>
                        <td>{row.engagementName}</td>
                        <td>{row.contractManagerText}</td>
                        <td>
                          {(row.validationMessages || []).join(' · ') || 'Ready'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="prepaid-form-actions">
                <button
                  type="button"
                  className="primary"
                  disabled={
                    uploadBusy
                    || Number(uploadPreview.summary.invalidRows) > 0
                  }
                  onClick={() => void confirmUpload()}
                >
                  Confirm import
                </button>
              </div>
            </>
          ) : null}
        </Drawer>
      ) : null}

      {scheduleOpen && schedule ? (
        <Drawer
          title="Weekly email automation"
          subtitle="ADMIN · SUPERADMIN · PROJECT TEAM COORDINATOR"
          onClose={() => setScheduleOpen(false)}
        >
          <form className="prepaid-form-grid" onSubmit={saveSchedule}>
            <label className="checkbox full">
              <input
                type="checkbox"
                checked={Boolean(schedule.isEnabled)}
                onChange={(event) =>
                  setSchedule({
                    ...schedule,
                    isEnabled: event.target.checked
                  })
                }
              />
              Enable weekly workbook email
            </label>

            {[
              ['weekdayIso', 'Weekday (1–7)', 'number'],
              ['sendTime', 'Send time', 'time'],
              ['timeZone', 'Time zone', 'text'],
              ['subjectTemplate', 'Subject', 'text'],
              ['lowBalanceThresholdPercent', 'Low-balance threshold %', 'number'],
              ['expirationWarningDays', 'Expiration warning days', 'number'],
              ['retentionMonths', 'Retention months', 'number']
            ].map(([name, label, type]) => (
              <label key={name}>
                {label}
                <input
                  type={type}
                  value={schedule[name] ?? ''}
                  onChange={(event) =>
                    setSchedule({
                      ...schedule,
                      [name]: event.target.value
                    })
                  }
                />
              </label>
            ))}

            <label className="full">
              Email introduction
              <textarea
                rows="5"
                value={schedule.bodyIntroduction || ''}
                onChange={(event) =>
                  setSchedule({
                    ...schedule,
                    bodyIntroduction: event.target.value
                  })
                }
              />
            </label>

            <label className="checkbox full">
              <input
                type="checkbox"
                checked={Boolean(schedule.includeExpired)}
                onChange={(event) =>
                  setSchedule({
                    ...schedule,
                    includeExpired: event.target.checked
                  })
                }
              />
              Include expired contracts
            </label>

            <div className="prepaid-form-actions full">
              <button type="button" onClick={() => setScheduleOpen(false)}>
                Cancel
              </button>
              <button type="submit" className="primary">
                Save schedule
              </button>
            </div>
          </form>
        </Drawer>
      ) : null}
    </section>
  );
}

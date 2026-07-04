import { useEffect, useMemo, useState } from 'react';
import './certify-integration-center.css';

/* 038A_CERTIFY_PLACEHOLDER_FRONTEND_START */
function getStoredProjectPulseAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;
    const session = JSON.parse(rawSession);
    if (!session?.sessionToken) return null;
    if (session?.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;
    return session;
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders() {
  const session = getStoredProjectPulseAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

async function readCertifyApiErrorMessage(response, path) {
  const raw = await response.text();

  if (!raw) {
    return `${path} returned HTTP ${response.status}`;
  }

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchCertifyJson(path) {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders(),
    cache: 'no-store'
  });

  if (!response.ok) {
    throw new Error(await readCertifyApiErrorMessage(response, path));
  }

  return response.json();
}

async function postCertifyJson(path) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify({})
  });

  if (!response.ok) {
    throw new Error(await readCertifyApiErrorMessage(response, path));
  }

  return response.json();
}
/* 038A_CERTIFY_PLACEHOLDER_FRONTEND_END */


const readinessItems = [
  {
    key: 'ownership',
    label: 'Business owner confirmed',
    detail: 'Accounting, project operations, and delivery leadership agree on the Certify-to-PHD workflow owner.',
    group: 'Governance'
  },
  {
    key: 'sandbox',
    label: 'Sandbox / test tenant identified',
    detail: 'Testing happens against non-production data before enabling production expense synchronization.',
    group: 'Environment'
  },
  {
    key: 'credentialVault',
    label: 'Credential storage approach defined',
    detail: 'API credentials or tokens must be stored server-side only. No secrets should be saved in browser storage.',
    group: 'Security'
  },
  {
    key: 'employeeMapping',
    label: 'Employee mapping validated',
    detail: 'Certify employee identifiers map cleanly to PHD users, emails, departments, and active/inactive status.',
    group: 'Identity'
  },
  {
    key: 'projectMapping',
    label: 'Project and customer mapping validated',
    detail: 'Expense reports can be mapped to PHD project code, customer, project manager, and billing treatment.',
    group: 'Data mapping'
  },
  {
    key: 'categoryMapping',
    label: 'Expense category mapping validated',
    detail: 'Certify expense categories map to accounting categories, reimbursable flags, and invoice readiness rules.',
    group: 'Data mapping'
  },
  {
    key: 'approvalMapping',
    label: 'Approval status mapping validated',
    detail: 'Draft, submitted, approved, rejected, exported, and reimbursed states are mapped into PHD workflow language.',
    group: 'Workflow'
  },
  {
    key: 'receiptHandling',
    label: 'Receipt handling approach defined',
    detail: 'Receipt image/file handling is defined for visibility, retention, audit evidence, and accounting review.',
    group: 'Documents'
  },
  {
    key: 'exceptionQueue',
    label: 'Exception queue designed',
    detail: 'Missing project, missing employee, invalid category, duplicate report, and rejected report exceptions are visible.',
    group: 'Operations'
  },
  {
    key: 'auditTrail',
    label: 'Audit trail requirements defined',
    detail: 'Every import, skip, exception, correction, and export event must be traceable for audit review.',
    group: 'Audit'
  }
];

const dataMappings = [
  {
    certifyObject: 'Employee',
    certifyFields: 'Employee ID, email, name, department, active status',
    phdTarget: 'PHD user profile',
    validation: 'Email match, active user check, department/team alignment'
  },
  {
    certifyObject: 'Expense report',
    certifyFields: 'Report ID, report name, submitter, approval state, submitted date',
    phdTarget: 'Expense report staging',
    validation: 'Duplicate report ID, valid submitter, valid approval state'
  },
  {
    certifyObject: 'Expense line',
    certifyFields: 'Expense date, category, amount, currency, merchant, description',
    phdTarget: 'Billable / reimbursable expense line',
    validation: 'Required category, amount greater than zero, valid currency'
  },
  {
    certifyObject: 'Project / customer reference',
    certifyFields: 'Project code, customer, cost center, custom field values',
    phdTarget: 'Project, customer, PM, and billing context',
    validation: 'Project exists, customer exists, PM ownership exists'
  },
  {
    certifyObject: 'Receipt attachment',
    certifyFields: 'Receipt image/file, attachment ID, file type',
    phdTarget: 'Document evidence / receipt package',
    validation: 'Allowed file type, attachment present when required'
  },
  {
    certifyObject: 'Approval event',
    certifyFields: 'Approver, approval date, status, comments',
    phdTarget: 'Workflow audit trail',
    validation: 'Valid approver, valid status transition, timestamp captured'
  }
];

const syncScenarios = [
  {
    name: 'Approved expense report import',
    trigger: 'Scheduled sync or manual refresh',
    success: 'Approved report and lines become visible for accounting readiness.',
    exception: 'If project or employee cannot be mapped, the report goes to exception queue.'
  },
  {
    name: 'Rejected / returned report visibility',
    trigger: 'Status change in Certify',
    success: 'Returned reports are excluded from invoice readiness until corrected and approved.',
    exception: 'If prior approved data exists, the report is flagged for review before changing billing state.'
  },
  {
    name: 'Receipt evidence pull',
    trigger: 'Approved report contains receipt attachments',
    success: 'Receipt evidence is associated with the staged expense package.',
    exception: 'Missing receipt is flagged based on category policy.'
  },
  {
    name: 'Accounting export readiness',
    trigger: 'Month-end or partial billing preparation',
    success: 'Approved mapped expenses are available for billing/export review.',
    exception: 'Unmapped category, missing project, or duplicate report blocks export readiness.'
  }
];

const fieldQualityRules = [
  'Report must have a stable Certify report ID.',
  'Submitter email must match an active PHD user or be mapped manually.',
  'Project code or customer reference must map to an active PHD project/customer.',
  'Expense category must map to a known accounting/billing category.',
  'Amount must be numeric and greater than zero.',
  'Approval status must be approved before billing readiness.',
  'Receipt must be present when category policy requires evidence.',
  'Duplicate report IDs must be skipped or flagged before import.'
];

function StatusPill({ children, tone = 'neutral' }) {
  return <span className={`certify-status-pill ${tone}`}>{children}</span>;
}

function fmtPercent(value) {
  return `${Math.round(Number(value || 0))}%`;
}

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function downloadTextFile(filename, content, type = 'text/plain') {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export default function CertifyIntegrationCenter() {
  const [checkedItems, setCheckedItems] = useState(() => new Set(['ownership', 'sandbox']));
  const [environment, setEnvironment] = useState('Planning');
  const [syncDirection, setSyncDirection] = useState('Certify to PHD');
  const [cadence, setCadence] = useState('Nightly plus manual refresh');
  const [statusMessage, setStatusMessage] = useState('');
  const [certifyApiState, setCertifyApiState] = useState({ loading: false, label: '', result: null, error: null });
  const [certifyConfig, setCertifyConfig] = useState(null);
  const [certifySyncStatus, setCertifySyncStatus] = useState(null);
  const [certifyStagedExpenses, setCertifyStagedExpenses] = useState([]);
  const [certifyExceptions, setCertifyExceptions] = useState([]);

  const readinessPercent = useMemo(() => {
    return readinessItems.length === 0 ? 0 : (checkedItems.size / readinessItems.length) * 100;
  }, [checkedItems]);

  const readinessTone = readinessPercent >= 80 ? 'safe' : readinessPercent >= 45 ? 'attention' : 'neutral';

  const readinessByGroup = useMemo(() => {
    const groups = new Map();

    readinessItems.forEach((item) => {
      const existing = groups.get(item.group) ?? { group: item.group, total: 0, ready: 0 };
      existing.total += 1;
      existing.ready += checkedItems.has(item.key) ? 1 : 0;
      groups.set(item.group, existing);
    });

    return [...groups.values()];
  }, [checkedItems]);

  async function callCertifyPlaceholderEndpoint(label, path, method = 'GET') {
    setCertifyApiState({ loading: true, label, result: null, error: null });

    try {
      const result = method === 'POST' ? await postCertifyJson(path) : await fetchCertifyJson(path);
      setCertifyApiState({ loading: false, label, result, error: null });
      setStatusMessage(`${label} completed: ${result.message || result.status || 'placeholder response received'}`);
      return result;
    } catch (error) {
      const message = error instanceof Error ? error.message : `${label} failed.`;
      setCertifyApiState({ loading: false, label, result: null, error: message });
      setStatusMessage(message);
      return null;
    }
  }

  async function refreshCertifyPlaceholderData(options = {}) {
    if (!options.silent) {
      setStatusMessage('Refreshing Certify placeholder status...');
    }

    const [configResult, statusResult, stagedResult, exceptionResult] = await Promise.allSettled([
      fetchCertifyJson('/api/certify/config-placeholder'),
      fetchCertifyJson('/api/certify/sync/status'),
      fetchCertifyJson('/api/certify/expenses/staged'),
      fetchCertifyJson('/api/certify/exceptions')
    ]);

    if (configResult.status === 'fulfilled') {
      setCertifyConfig(configResult.value);
    }

    if (statusResult.status === 'fulfilled') {
      setCertifySyncStatus(statusResult.value);
    }

    if (stagedResult.status === 'fulfilled') {
      setCertifyStagedExpenses(stagedResult.value.stagedExpenses ?? []);
    }

    if (exceptionResult.status === 'fulfilled') {
      setCertifyExceptions(exceptionResult.value.exceptions ?? []);
    }

    const failures = [configResult, statusResult, stagedResult, exceptionResult]
      .filter((result) => result.status === 'rejected')
      .map((result) => result.reason instanceof Error ? result.reason.message : 'Unknown Certify placeholder loading error.');

    if (failures.length > 0) {
      setCertifyApiState({ loading: false, label: 'Refresh placeholder status', result: null, error: failures.join(' | ') });
      if (!options.silent) setStatusMessage(failures.join(' | '));
      return;
    }

    if (!options.silent) {
      setStatusMessage('Certify placeholder status refreshed.');
    }
  }

  useEffect(() => {
    void refreshCertifyPlaceholderData({ silent: true });
  }, []);

  async function testCertifyConnection() {
    const result = await callCertifyPlaceholderEndpoint('Test Certify connection placeholder', '/api/certify/test-connection', 'POST');
    if (result?.config) setCertifyConfig(result.config);
  }

  async function previewCertifySync() {
    const result = await callCertifyPlaceholderEndpoint('Preview Certify sync placeholder', '/api/certify/sync/preview', 'POST');
    if (result?.stagedExpenses) setCertifyStagedExpenses(result.stagedExpenses);
    if (result?.exceptions) setCertifyExceptions(result.exceptions);
  }

  async function runCertifySyncPlaceholder() {
    const result = await callCertifyPlaceholderEndpoint('Run Certify sync placeholder', '/api/certify/sync/run', 'POST');
    if (result?.stagedExpenses) setCertifyStagedExpenses(result.stagedExpenses);
    if (result?.exceptions) setCertifyExceptions(result.exceptions);
  }

  function toggleReadinessItem(key) {
    setCheckedItems((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }

  function exportMappingCsv() {
    const headers = ['Certify Object', 'Certify Fields', 'PHD Target', 'Validation Rule'];
    const rows = dataMappings.map((item) => [item.certifyObject, item.certifyFields, item.phdTarget, item.validation]);
    const content = [headers, ...rows].map((row) => row.map(csvEscape).join(',')).join('\n');
    downloadTextFile('phd-certify-integration-mapping.csv', content, 'text/csv');
    setStatusMessage('Certify integration mapping exported as CSV.');
  }

  async function copyImplementationPlan() {
    const content = [
      'PHD Module 038 - Certify Integration Implementation Plan',
      '',
      `Environment: ${environment}`,
      `Sync direction: ${syncDirection}`,
      `Sync cadence: ${cadence}`,
      `Readiness: ${fmtPercent(readinessPercent)}`,
      '',
      'Readiness checklist:',
      ...readinessItems.map((item) => `- ${checkedItems.has(item.key) ? '[x]' : '[ ]'} ${item.label}: ${item.detail}`),
      '',
      'Required validation rules:',
      ...fieldQualityRules.map((rule) => `- ${rule}`)
    ].join('\n');

    try {
      await navigator.clipboard.writeText(content);
      setStatusMessage('Certify implementation plan copied to clipboard.');
    } catch {
      setStatusMessage('Unable to copy automatically. Use Export mapping instead.');
    }
  }

  return (
    <section className="certify-integration-center">
      <div className="certify-header">
        <div>
          <p className="eyebrow">Module 038</p>
          <h2>Certify Integration Center</h2>
          <p className="muted">
            Integration readiness workspace for Certify expense reports, receipt evidence, project/customer mapping, approval status, accounting readiness, and exception handling.
          </p>
        </div>
        <div className="certify-header-actions">
          <button type="button" className="secondary-action" onClick={copyImplementationPlan}>Copy implementation plan</button>
          <button type="button" className="primary-action" onClick={exportMappingCsv}>Export mapping CSV</button>
        </div>
      </div>

      {statusMessage ? <div className="certify-alert">{statusMessage}</div> : null}

      <article className="certify-panel certify-placeholder-sync-panel">
        <div className="certify-panel-heading">
          <div>
            <h3>Certify sync placeholders</h3>
            <p className="muted">
              These controls are wired to safe backend placeholder endpoints. They prepare the import workflow now and can be switched to real Certify API calls after credentials, tenant details, and mapping rules are confirmed.
            </p>
          </div>
          <StatusPill tone={certifyConfig?.configured ? 'safe' : 'attention'}>
            {certifyConfig?.configured ? 'Config placeholders set' : 'Awaiting Certify config'}
          </StatusPill>
        </div>

        <div className="certify-placeholder-actions">
          <button type="button" className="secondary-action" onClick={() => refreshCertifyPlaceholderData()} disabled={certifyApiState.loading}>
            Refresh placeholder status
          </button>
          <button type="button" className="secondary-action" onClick={testCertifyConnection} disabled={certifyApiState.loading}>
            Test connection
          </button>
          <button type="button" className="secondary-action" onClick={previewCertifySync} disabled={certifyApiState.loading}>
            Preview sync
          </button>
          <button type="button" className="primary-action" onClick={runCertifySyncPlaceholder} disabled={certifyApiState.loading}>
            Run placeholder sync
          </button>
        </div>

        {certifyApiState.loading ? <p className="certify-placeholder-status">Running {certifyApiState.label}...</p> : null}
        {certifyApiState.error ? <p className="certify-placeholder-error">{certifyApiState.error}</p> : null}

        <div className="certify-placeholder-grid">
          <article>
            <span>Live sync</span>
            <strong>{certifySyncStatus?.canRunLiveSync ? 'Enabled' : 'Blocked'}</strong>
            <small>{certifySyncStatus?.message || 'Placeholder status has not loaded yet.'}</small>
          </article>
          <article>
            <span>Missing config</span>
            <strong>{certifyConfig?.missingConfigKeys?.length ?? '...'}</strong>
            <small>{certifyConfig?.missingConfigKeys?.length ? certifyConfig.missingConfigKeys.join(', ') : 'No missing placeholder keys reported.'}</small>
          </article>
          <article>
            <span>Staged expenses</span>
            <strong>{certifyStagedExpenses.length}</strong>
            <small>Placeholder expense records ready for future staging table view.</small>
          </article>
          <article>
            <span>Exceptions</span>
            <strong>{certifyExceptions.length}</strong>
            <small>Placeholder exception types expected during real import.</small>
          </article>
        </div>

        <div className="certify-placeholder-two-column">
          <div>
            <h4>Safe config snapshot</h4>
            <div className="certify-safe-config-list">
              <span><strong>Base URL</strong>{certifyConfig?.safeConfig?.baseUrlConfigured ? 'Configured' : 'Missing'}</span>
              <span><strong>Auth mode</strong>{certifyConfig?.authMode || 'placeholder'}</span>
              <span><strong>API key</strong>{certifyConfig?.safeConfig?.apiKeyConfigured ? 'Configured server-side' : 'Missing'}</span>
              <span><strong>Client secret</strong>{certifyConfig?.safeConfig?.clientSecretConfigured ? 'Configured server-side' : 'Missing'}</span>
              <span><strong>Company ID</strong>{certifyConfig?.safeConfig?.companyIdConfigured ? 'Configured' : 'Missing'}</span>
              <span><strong>Dry run only</strong>{certifyConfig?.dryRunOnly ? 'True' : 'False'}</span>
            </div>
          </div>

          <div>
            <h4>Placeholder exceptions</h4>
            <div className="certify-exception-list">
              {certifyExceptions.length === 0 ? (
                <p className="muted">No placeholder exceptions loaded yet.</p>
              ) : (
                certifyExceptions.map((exception) => (
                  <article key={exception.exceptionCode}>
                    <strong>{exception.exceptionCode}</strong>
                    <span>{exception.severity}</span>
                    <p>{exception.message}</p>
                    <small>{exception.resolution}</small>
                  </article>
                ))
              )}
            </div>
          </div>
        </div>

        <div className="certify-staged-preview">
          <h4>Placeholder staged expenses</h4>
          <div className="certify-table-wrap compact">
            <table className="certify-mapping-table">
              <thead>
                <tr>
                  <th>Report</th>
                  <th>Employee</th>
                  <th>Customer</th>
                  <th>Project</th>
                  <th>Category</th>
                  <th>Billing status</th>
                </tr>
              </thead>
              <tbody>
                {certifyStagedExpenses.length === 0 ? (
                  <tr>
                    <td colSpan="6">No placeholder staged expenses loaded yet.</td>
                  </tr>
                ) : (
                  certifyStagedExpenses.map((expense) => (
                    <tr key={expense.certifyReportId}>
                      <td><strong>{expense.certifyReportId}</strong></td>
                      <td>{expense.employeeEmail}</td>
                      <td>{expense.customerName}</td>
                      <td>{expense.projectCode}</td>
                      <td>{expense.expenseCategory}</td>
                      <td>{expense.billingStatus}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      </article>


      <div className="certify-summary-grid">
        <article>
          <span>Readiness</span>
          <strong>{fmtPercent(readinessPercent)}</strong>
          <small>{checkedItems.size}/{readinessItems.length} integration readiness item(s) checked</small>
        </article>
        <article>
          <span>Environment</span>
          <strong>{environment}</strong>
          <small>Use sandbox/test before production activation</small>
        </article>
        <article>
          <span>Sync direction</span>
          <strong>{syncDirection}</strong>
          <small>Expense and receipt data flow planning</small>
        </article>
        <article>
          <span>Cadence</span>
          <strong>{cadence}</strong>
          <small>Recommended until production scheduling is wired</small>
        </article>
      </div>

      <article className="certify-panel">
        <div className="certify-panel-heading">
          <div>
            <h3>Connector profile</h3>
            <p className="muted">
              This section captures integration intent only. Credentials and secrets should be stored server-side later, not in the browser.
            </p>
          </div>
          <StatusPill tone={readinessTone}>{fmtPercent(readinessPercent)} ready</StatusPill>
        </div>

        <div className="certify-config-grid">
          <label>
            Environment state
            <select value={environment} onChange={(event) => setEnvironment(event.target.value)}>
              <option>Planning</option>
              <option>Sandbox requested</option>
              <option>Sandbox connected</option>
              <option>Production pending approval</option>
              <option>Production connected</option>
            </select>
          </label>

          <label>
            Sync direction
            <select value={syncDirection} onChange={(event) => setSyncDirection(event.target.value)}>
              <option>Certify to PHD</option>
              <option>Certify to PHD and Accounting Export</option>
              <option>Bidirectional planning only</option>
            </select>
          </label>

          <label>
            Sync cadence
            <select value={cadence} onChange={(event) => setCadence(event.target.value)}>
              <option>Manual refresh only</option>
              <option>Nightly plus manual refresh</option>
              <option>Hourly during month-end</option>
              <option>Production schedule TBD</option>
            </select>
          </label>
        </div>
      </article>

      <div className="certify-two-column">
        <article className="certify-panel">
          <div className="certify-panel-heading">
            <div>
              <h3>Integration readiness checklist</h3>
              <p className="muted">Track what must be confirmed before a live Certify connector is built or enabled.</p>
            </div>
          </div>

          <div className="certify-readiness-list">
            {readinessItems.map((item) => {
              const ready = checkedItems.has(item.key);
              return (
                <button
                  type="button"
                  className={`certify-readiness-row ${ready ? 'ready' : 'attention'}`}
                  key={item.key}
                  onClick={() => toggleReadinessItem(item.key)}
                >
                  <span>{ready ? '✓' : '○'}</span>
                  <div>
                    <strong>{item.label}</strong>
                    <small>{item.group} · {item.detail}</small>
                  </div>
                </button>
              );
            })}
          </div>
        </article>

        <article className="certify-panel">
          <div className="certify-panel-heading">
            <div>
              <h3>Readiness by area</h3>
              <p className="muted">Grouped view of integration readiness across governance, mapping, workflow, and audit.</p>
            </div>
          </div>

          <div className="certify-group-list">
            {readinessByGroup.map((group) => {
              const percent = group.total === 0 ? 0 : (group.ready / group.total) * 100;
              return (
                <div className="certify-group-row" key={group.group}>
                  <div>
                    <strong>{group.group}</strong>
                    <small>{group.ready}/{group.total} ready</small>
                  </div>
                  <div className="certify-meter" aria-label={`${group.group} ${fmtPercent(percent)} ready`}>
                    <span style={{ width: fmtPercent(percent) }} />
                  </div>
                </div>
              );
            })}
          </div>

          <div className="certify-validation-rules">
            <h4>Minimum data quality rules</h4>
            <ul>
              {fieldQualityRules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          </div>
        </article>
      </div>

      <article className="certify-panel">
        <div className="certify-panel-heading">
          <div>
            <h3>Data mapping matrix</h3>
            <p className="muted">Initial mapping between Certify objects and PHD expense, project, document, and audit concepts.</p>
          </div>
        </div>

        <div className="certify-table-wrap">
          <table className="certify-mapping-table">
            <thead>
              <tr>
                <th>Certify object</th>
                <th>Certify fields</th>
                <th>PHD target</th>
                <th>Validation / exception rule</th>
              </tr>
            </thead>
            <tbody>
              {dataMappings.map((mapping) => (
                <tr key={mapping.certifyObject}>
                  <td><strong>{mapping.certifyObject}</strong></td>
                  <td>{mapping.certifyFields}</td>
                  <td>{mapping.phdTarget}</td>
                  <td>{mapping.validation}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>

      <article className="certify-panel">
        <div className="certify-panel-heading">
          <div>
            <h3>Sync scenarios and exception handling</h3>
            <p className="muted">Expected scenarios the future connector must support before production use.</p>
          </div>
        </div>

        <div className="certify-scenario-grid">
          {syncScenarios.map((scenario) => (
            <article key={scenario.name}>
              <span>{scenario.trigger}</span>
              <strong>{scenario.name}</strong>
              <p>{scenario.success}</p>
              <small>Exception: {scenario.exception}</small>
            </article>
          ))}
        </div>
      </article>

      <article className="certify-panel certify-no-secrets-panel">
        <div>
          <h3>Security guardrail</h3>
          <p>
            Module 038 does not collect or store Certify secrets. When the live connector is implemented, API keys, OAuth client secrets, refresh tokens, and tenant identifiers should be stored and rotated through server-side configuration or a secure vault.
          </p>
        </div>
        <StatusPill tone="safe">No browser secrets</StatusPill>
      </article>
    </section>
  );
}

import { useEffect, useMemo, useState } from 'react';
import './billing-readiness-center.css';

const readinessChecks = [
  {
    key: 'timeApproved',
    label: 'Approved labor reviewed',
    detail: 'Approved project time is reviewed for the billing period.',
    owner: 'PM / Manager'
  },
  {
    key: 'certifyReviewed',
    label: 'Certify expenses reviewed',
    detail: 'Certify expenses are staged, mapped, and reviewed for billing treatment.',
    owner: 'Accounting'
  },
  {
    key: 'customerMapped',
    label: 'Customer and project mapping confirmed',
    detail: 'Every billing line maps to the correct customer, project, and invoice package.',
    owner: 'PM / Accounting'
  },
  {
    key: 'exceptionsCleared',
    label: 'Exceptions cleared',
    detail: 'Missing project, missing receipt, duplicate, rejected, or unmapped expenses are resolved.',
    owner: 'Accounting'
  },
  {
    key: 'billingTreatment',
    label: 'Billing treatment confirmed',
    detail: 'Lines are classified as billable, reimbursable, fixed-fee included, held, or excluded.',
    owner: 'PM'
  },
  {
    key: 'evidenceReady',
    label: 'Supporting evidence ready',
    detail: 'Receipts, approval evidence, and export documentation are ready for accounting review.',
    owner: 'PM / Accounting'
  },
  {
    key: 'customerNotesReady',
    label: 'Customer notes ready',
    detail: 'Invoice explanation is ready for partial billing, month-end billing, or expense reimbursement.',
    owner: 'Sales / PM'
  },
  {
    key: 'accountingReady',
    label: 'Accounting export ready',
    detail: 'The package can be exported or handed off for invoice creation.',
    owner: 'Accounting'
  }
];

const billingModes = [
  {
    key: 'project',
    label: 'Project / Partial Invoice',
    description: 'Prepare one customer/project package for partial billing, milestone billing, expense billing, or final invoice readiness.'
  },
  {
    key: 'monthEnd',
    label: 'Month-End Billing Run',
    description: 'Review all active customer/project packages for a selected month-end billing period.'
  }
];

const packageTypes = [
  'Partial project invoice',
  'Month-end billing package',
  'Milestone invoice',
  'Time and materials invoice',
  'Expense-only reimbursement package',
  'Final invoice before project closeout'
];

const billingTreatmentOptions = [
  'Billable labor',
  'Billable expense',
  'Reimbursable expense',
  'Included in fixed fee',
  'Non-billable internal cost',
  'Hold for next invoice',
  'Requires PM review',
  'Requires accounting review'
];

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

async function readApiErrorMessage(response, path) {
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

async function fetchJson(path) {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders(),
    cache: 'no-store'
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...getProjectPulseAuthHeaders()
    },
    cache: 'no-store',
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

function currency(value) {
  const number = Number(value || 0);
  return number.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
}

function percent(value) {
  return `${Math.round(Number(value || 0))}%`;
}

function firstDayOfCurrentMonth() {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1)).toISOString().slice(0, 10);
}

function lastDayOfCurrentMonth() {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth() + 1, 0)).toISOString().slice(0, 10);
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

function normalizeArrayPayload(payload, possibleKeys) {
  if (!payload) return [];

  if (Array.isArray(payload)) return payload;

  for (const key of possibleKeys) {
    if (Array.isArray(payload[key])) return payload[key];
  }

  return [];
}

function deriveProjectCandidates(workspacePayload, intakePayload, customerPayload) {
  const workspaceProjects = normalizeArrayPayload(workspacePayload, [
    'projects',
    'activeProjects',
    'projectWorkspaces',
    'workspaces',
    'items'
  ]);

  const intakeProjects = normalizeArrayPayload(intakePayload, [
    'requests',
    'projectIntakeRequests',
    'intakeRequests',
    'items'
  ]);

  const customers = normalizeArrayPayload(customerPayload, [
    'customers',
    'customerSummaries',
    'items'
  ]);

  const customerById = new Map(customers.map((customer) => [
    String(customer.customerId ?? customer.id ?? customer.clientId ?? ''),
    customer
  ]));

  const candidates = [];

  workspaceProjects.forEach((project, index) => {
    const customerId = String(project.customerId ?? project.clientId ?? '');
    const customer = customerById.get(customerId);

    candidates.push({
      source: 'Workspace',
      id: project.projectId ?? project.id ?? `workspace-${index}`,
      projectCode: project.projectCode ?? project.code ?? project.projectNumber ?? `WORKSPACE-${index + 1}`,
      projectName: project.projectName ?? project.name ?? project.title ?? 'Workspace project',
      customerName: project.customerName ?? project.clientName ?? customer?.customerName ?? customer?.name ?? 'Customer pending',
      projectManagerName: project.projectManagerName ?? project.pmName ?? project.assignedPmName ?? 'PM pending',
      status: project.status ?? project.projectStatus ?? 'Active',
      risk: project.riskStatus ?? project.readinessStatus ?? project.healthStatus ?? 'Review',
      sourceRecord: project
    });
  });

  intakeProjects.forEach((request, index) => {
    candidates.push({
      source: 'Intake',
      id: request.projectIntakeRequestId ?? request.requestId ?? request.id ?? `intake-${index}`,
      projectCode: request.projectCode ?? request.opportunityId ?? request.requestNumber ?? `INTAKE-${index + 1}`,
      projectName: request.projectName ?? request.opportunityName ?? request.requestName ?? 'Intake project',
      customerName: request.customerName ?? request.clientName ?? request.accountName ?? 'Customer pending',
      projectManagerName: request.assignedPmName ?? request.projectManagerName ?? request.pmName ?? 'PM pending',
      status: request.status ?? request.intakeStatus ?? 'Intake',
      risk: request.launchReadinessStatus ?? request.readinessStatus ?? 'Review',
      sourceRecord: request
    });
  });

  const deduped = new Map();

  candidates.forEach((candidate) => {
    const key = `${candidate.projectCode}|${candidate.customerName}`.toLowerCase();
    if (!deduped.has(key)) {
      deduped.set(key, candidate);
    }
  });

  return [...deduped.values()].sort((a, b) => `${a.customerName} ${a.projectCode}`.localeCompare(`${b.customerName} ${b.projectCode}`));
}

function getProjectSeed(project) {
  if (!project) return 1;
  return String(project.projectCode || project.id || '1')
    .split('')
    .reduce((total, char) => total + char.charCodeAt(0), 0);
}

function buildLaborRows(project, billingRate, mode = 'project') {
  if (!project) return [];

  const seed = getProjectSeed(project);
  const baseHours = mode === 'monthEnd' ? 12 + (seed % 26) : 18 + (seed % 22);
  const rate = Number(billingRate || 0);
  const amount = baseHours * rate;

  return [
    {
      type: 'Labor',
      source: 'PHD approved time',
      customerName: project.customerName,
      projectCode: project.projectCode,
      projectName: project.projectName,
      projectManagerName: project.projectManagerName,
      description: `${project.projectName} approved labor placeholder`,
      quantity: baseHours,
      unitRate: rate,
      amount,
      treatment: 'Billable labor',
      readiness: rate > 0 ? 'Ready for PM/accounting review' : 'Missing billing rate',
      status: rate > 0 ? 'review' : 'blocked'
    }
  ];
}

function normalizeCertifyExpenseRows(expenses, selectedProject) {
  return expenses.map((expense, index) => {
    const amount = Number(expense.amount ?? expense.totalAmount ?? expense.approvedAmount ?? 0);
    const billingStatus = expense.billingStatus || expense.mappingStatus || 'Requires review';
    const blocked = /missing|not ready|pending|exception|placeholder/i.test(billingStatus);

    return {
      type: 'Expense',
      source: 'Certify',
      customerName: selectedProject?.customerName || expense.customerName || 'Customer mapping pending',
      projectCode: selectedProject?.projectCode || expense.projectCode || 'Project mapping pending',
      projectName: selectedProject?.projectName || 'Project mapping pending',
      projectManagerName: selectedProject?.projectManagerName || 'PM pending',
      description: `${expense.expenseCategory || expense.category || 'Expense'} · ${expense.certifyReportId || expense.reportId || `placeholder-${index + 1}`}`,
      quantity: 1,
      unitRate: amount,
      amount,
      treatment: expense.billable === false ? 'Non-billable internal cost' : 'Billable expense',
      readiness: billingStatus,
      status: blocked ? 'blocked' : 'review'
    };
  });
}

function getFinancialTotals(rows) {
  const laborTotal = rows.filter((row) => row.type === 'Labor').reduce((total, row) => total + Number(row.amount || 0), 0);
  const expenseTotal = rows.filter((row) => row.type === 'Expense').reduce((total, row) => total + Number(row.amount || 0), 0);
  const nonBillableTotal = rows
    .filter((row) => /non-billable|included|excluded/i.test(row.treatment))
    .reduce((total, row) => total + Number(row.amount || 0), 0);
  const blockedTotal = rows
    .filter((row) => row.status === 'blocked' || /missing|not ready|pending|exception/i.test(row.readiness))
    .reduce((total, row) => total + Number(row.amount || 0), 0);
  const readyToInvoiceTotal = rows.reduce((total, row) => total + Number(row.amount || 0), 0) - blockedTotal - nonBillableTotal;

  return {
    laborTotal,
    expenseTotal,
    nonBillableTotal,
    blockedTotal,
    readyToInvoiceTotal: Math.max(0, readyToInvoiceTotal),
    packageTotal: rows.reduce((total, row) => total + Number(row.amount || 0), 0),
    rowCount: rows.length
  };
}

function buildMonthEndRows(projects, billingRate, stagedExpenses) {
  const selectedProjects = projects.slice(0, 12);
  const rows = [];

  selectedProjects.forEach((project) => {
    rows.push(...buildLaborRows(project, billingRate, 'monthEnd'));
  });

  const expenseRows = normalizeCertifyExpenseRows(stagedExpenses, null).map((row, index) => {
    const project = selectedProjects[index % Math.max(1, selectedProjects.length)] ?? null;
    return {
      ...row,
      customerName: project?.customerName || row.customerName,
      projectCode: project?.projectCode || row.projectCode,
      projectName: project?.projectName || row.projectName,
      projectManagerName: project?.projectManagerName || row.projectManagerName
    };
  });

  rows.push(...expenseRows);
  return rows;
}

function summarizeMonthEndPackages(rows) {
  const grouped = new Map();

  rows.forEach((row) => {
    const key = `${row.customerName}|${row.projectCode}`;
    const existing = grouped.get(key) ?? {
      customerName: row.customerName,
      projectCode: row.projectCode,
      projectName: row.projectName,
      projectManagerName: row.projectManagerName,
      laborTotal: 0,
      expenseTotal: 0,
      packageTotal: 0,
      readyToInvoiceTotal: 0,
      blockedTotal: 0,
      lineCount: 0,
      status: 'Ready for review'
    };

    if (row.type === 'Labor') existing.laborTotal += Number(row.amount || 0);
    if (row.type === 'Expense') existing.expenseTotal += Number(row.amount || 0);

    existing.packageTotal += Number(row.amount || 0);
    existing.lineCount += 1;

    if (row.status === 'blocked' || /missing|not ready|pending|exception/i.test(row.readiness)) {
      existing.blockedTotal += Number(row.amount || 0);
      existing.status = 'Blocked / exception review';
    }

    existing.readyToInvoiceTotal = Math.max(0, existing.packageTotal - existing.blockedTotal);
    grouped.set(key, existing);
  });

  return [...grouped.values()].sort((a, b) => b.packageTotal - a.packageTotal);
}

export default function BillingReadinessCenter() {
  const [payload, setPayload] = useState({ loading: true, error: null, workspace: null, intake: null, customers: null, certifyExpenses: null, certifyExceptions: null, billingCandidates: [] });
  const [billingMode, setBillingMode] = useState('project');
  const [selectedProjectKey, setSelectedProjectKey] = useState('');
  const [packageType, setPackageType] = useState('Partial project invoice');
  const [periodStart, setPeriodStart] = useState(firstDayOfCurrentMonth());
  const [periodEnd, setPeriodEnd] = useState(lastDayOfCurrentMonth());
  const [billingRate, setBillingRate] = useState('175');
  const [checkedItems, setCheckedItems] = useState(() => new Set(['customerMapped', 'billingTreatment']));
  const [packageNotes, setPackageNotes] = useState('Billing readiness package can be prepared while the project remains active. Final project closeout is handled separately.');
  const [auditReason, setAuditReason] = useState('');
  const [statusMessage, setStatusMessage] = useState('');
  const [lifecycle, setLifecycle] = useState({ loading: false, error: null, data: null });
  const [isSavingReadiness, setIsSavingReadiness] = useState(false);

  async function loadBillingReadinessData() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    const results = await Promise.allSettled([
      fetchJson('/api/project-workspace/overview'),
      fetchJson('/api/project-intake/overview'),
      fetchJson('/api/customers/overview'),
      fetchJson('/api/certify/expenses/staged'),
      fetchJson('/api/certify/exceptions'),
      fetchJson('/api/billing/candidates')
    ]);

    const [workspace, intake, customers, certifyExpenses, certifyExceptions, billingCandidates] = results;

    const failures = results
      .filter((result) => result.status === 'rejected')
      .map((result) => result.reason instanceof Error ? result.reason.message : 'Unknown loading error.');

    setPayload({
      loading: false,
      error: failures.length > 0 ? failures.join(' | ') : null,
      workspace: workspace.status === 'fulfilled' ? workspace.value : null,
      intake: intake.status === 'fulfilled' ? intake.value : null,
      customers: customers.status === 'fulfilled' ? customers.value : null,
      certifyExpenses: certifyExpenses.status === 'fulfilled' ? certifyExpenses.value : null,
      certifyExceptions: certifyExceptions.status === 'fulfilled' ? certifyExceptions.value : null,
      billingCandidates: billingCandidates.status === 'fulfilled'
        ? (billingCandidates.value?.candidates ?? [])
        : []
    });
  }

  useEffect(() => {
    void loadBillingReadinessData();
  }, []);

  const projectCandidates = useMemo(() => {
    return deriveProjectCandidates(payload.workspace, payload.intake, payload.customers);
  }, [payload.workspace, payload.intake, payload.customers]);

  useEffect(() => {
    if (!selectedProjectKey && projectCandidates.length > 0) {
      setSelectedProjectKey(String(projectCandidates[0].id));
    }
  }, [projectCandidates, selectedProjectKey]);

  const selectedProject = useMemo(() => {
    return projectCandidates.find((project) => String(project.id) === String(selectedProjectKey)) ?? projectCandidates[0] ?? null;
  }, [projectCandidates, selectedProjectKey]);

  useEffect(() => {
    let cancelled = false;

    if (!selectedProject?.id) {
      setLifecycle({ loading: false, error: null, data: null });
      return () => {
        cancelled = true;
      };
    }

    setLifecycle({ loading: true, error: null, data: null });
    fetchJson(`/api/work-lifecycle/projects/${selectedProject.id}`)
      .then((data) => {
        if (cancelled) return;
        setLifecycle({ loading: false, error: null, data });
        const saved = data?.billingReadiness;
        if (!saved) return;

        setPeriodStart(saved.billingPeriodStart || firstDayOfCurrentMonth());
        setPeriodEnd(saved.billingPeriodEnd || lastDayOfCurrentMonth());
        setPackageType(saved.packageType || 'Partial project invoice');
        setPackageNotes(saved.notes || '');
        setCheckedItems(new Set(
          Object.entries(saved.checklist || {})
            .filter(([, value]) => value)
            .map(([key]) => key)
        ));
      })
      .catch((error) => {
        if (!cancelled) {
          setLifecycle({
            loading: false,
            error: error instanceof Error ? error.message : 'Unable to load persisted billing readiness.',
            data: null
          });
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedProject?.id]);

  const billingCandidate = useMemo(() => (
    (payload.billingCandidates ?? []).find((candidate) => (
      String(candidate.projectId) === String(selectedProject?.id)
    )) ?? null
  ), [payload.billingCandidates, selectedProject?.id]);

  const stagedCertifyExpenses = useMemo(() => {
    return normalizeArrayPayload(payload.certifyExpenses, ['stagedExpenses', 'expenses', 'items']);
  }, [payload.certifyExpenses]);

  const certifyExceptions = useMemo(() => {
    return normalizeArrayPayload(payload.certifyExceptions, ['exceptions', 'items']);
  }, [payload.certifyExceptions]);

  const projectPackageRows = useMemo(() => {
    return [
      ...buildLaborRows(selectedProject, billingRate, 'project'),
      ...normalizeCertifyExpenseRows(stagedCertifyExpenses, selectedProject)
    ];
  }, [billingRate, selectedProject, stagedCertifyExpenses]);

  const monthEndRows = useMemo(() => {
    return buildMonthEndRows(projectCandidates, billingRate, stagedCertifyExpenses);
  }, [billingRate, projectCandidates, stagedCertifyExpenses]);

  const activeRows = billingMode === 'monthEnd' ? monthEndRows : projectPackageRows;

  const financialTotals = useMemo(() => getFinancialTotals(activeRows), [activeRows]);

  const monthEndPackages = useMemo(() => summarizeMonthEndPackages(monthEndRows), [monthEndRows]);

  const readinessPercent = useMemo(() => {
    return readinessChecks.length === 0 ? 0 : (checkedItems.size / readinessChecks.length) * 100;
  }, [checkedItems]);

  const blockingIssues = useMemo(() => {
    const issues = [];

    if (billingMode === 'project' && !selectedProject) issues.push('No project selected.');
    if (!Number(billingRate || 0)) issues.push('Billing rate is missing for labor estimate.');
    if (certifyExceptions.length > 0) issues.push(`${certifyExceptions.length} Certify placeholder exception(s) need review.`);
    if (!checkedItems.has('timeApproved')) issues.push('Approved labor review is not confirmed.');
    if (!checkedItems.has('certifyReviewed')) issues.push('Certify expense review is not confirmed.');
    if (!checkedItems.has('exceptionsCleared')) issues.push('Billing exceptions are not confirmed as cleared.');
    if (financialTotals.blockedTotal > 0) issues.push(`${currency(financialTotals.blockedTotal)} is currently blocked or pending review.`);
    (billingCandidate?.blockers ?? []).forEach((blocker) => issues.push(blocker));
    if (billingMode === 'project' && billingCandidate && Number(billingCandidate.approvedLineCount || 0) === 0) {
      issues.push('No approved uninvoiced labor lines are currently available.');
    }

    return [...new Set(issues)];
  }, [billingMode, billingRate, certifyExceptions, checkedItems, financialTotals.blockedTotal, selectedProject, billingCandidate]);

  const readinessTone = blockingIssues.length === 0 && readinessPercent >= 90 ? 'safe' : readinessPercent >= 50 ? 'attention' : 'blocked';

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

  async function saveBillingReadiness(reviewStatus) {
    if (!selectedProject?.id) {
      setStatusMessage('Select a project before saving billing readiness.');
      return;
    }

    if (reviewStatus === 'ready' && blockingIssues.length > 0) {
      setStatusMessage('Resolve every blocking issue before marking this package ready.');
      return;
    }

    if (auditReason.trim().length < 5) {
      setStatusMessage('Enter a specific audit reason before saving.');
      return;
    }

    setIsSavingReadiness(true);
    setStatusMessage('');

    try {
      const checklist = Object.fromEntries(
        readinessChecks.map((item) => [item.key, checkedItems.has(item.key)])
      );
      const result = await postJson(
        `/api/work-lifecycle/projects/${selectedProject.id}/billing-readiness`,
        {
          billingPeriodStart: periodStart,
          billingPeriodEnd: periodEnd,
          packageType,
          reviewStatus,
          checklist,
          notes: packageNotes,
          reason: auditReason.trim()
        }
      );
      setLifecycle((current) => ({
        ...current,
        error: null,
        data: {
          ...(current.data || {}),
          billingReadiness: result.billingReadiness
        }
      }));
      setAuditReason('');
      setStatusMessage(result.message || 'Billing readiness saved.');
    } catch (error) {
      setStatusMessage(error instanceof Error ? error.message : 'Unable to save billing readiness.');
    } finally {
      setIsSavingReadiness(false);
    }
  }

  function exportBillingReadinessCsv() {
    const headers = [
      'Billing Mode',
      'Package Type',
      'Period Start',
      'Period End',
      'Customer',
      'Project Code',
      'Project Name',
      'PM',
      'Line Type',
      'Source',
      'Description',
      'Quantity',
      'Unit Rate',
      'Amount',
      'Treatment',
      'Readiness'
    ];

    const rows = activeRows.map((row) => [
      billingModes.find((mode) => mode.key === billingMode)?.label ?? billingMode,
      packageType,
      periodStart,
      periodEnd,
      row.customerName,
      row.projectCode,
      row.projectName,
      row.projectManagerName,
      row.type,
      row.source,
      row.description,
      row.quantity,
      row.unitRate,
      row.amount,
      row.treatment,
      row.readiness
    ]);

    const content = [headers, ...rows].map((row) => row.map(csvEscape).join(',')).join('\n');
    downloadTextFile('phd-billing-readiness-financial-report.csv', content, 'text/csv');
    setStatusMessage('Billing readiness financial report exported as CSV.');
  }

  function exportMonthEndSummaryCsv() {
    const headers = ['Period Start', 'Period End', 'Customer', 'Project Code', 'Project Name', 'PM', 'Labor Total', 'Expense Total', 'Package Total', 'Ready To Invoice', 'Blocked Total', 'Line Count', 'Status'];
    const rows = monthEndPackages.map((item) => [
      periodStart,
      periodEnd,
      item.customerName,
      item.projectCode,
      item.projectName,
      item.projectManagerName,
      item.laborTotal,
      item.expenseTotal,
      item.packageTotal,
      item.readyToInvoiceTotal,
      item.blockedTotal,
      item.lineCount,
      item.status
    ]);

    const content = [headers, ...rows].map((row) => row.map(csvEscape).join(',')).join('\n');
    downloadTextFile('phd-month-end-billing-run-summary.csv', content, 'text/csv');
    setStatusMessage('Month-end billing run summary exported as CSV.');
  }

  async function copyBillingSummary() {
    const modeLabel = billingModes.find((mode) => mode.key === billingMode)?.label ?? billingMode;
    const content = [
      'PHD Module 039 - Billing Readiness Center',
      '',
      `Billing mode: ${modeLabel}`,
      `Package type: ${packageType}`,
      `Period: ${periodStart} through ${periodEnd}`,
      billingMode === 'project' ? `Customer: ${selectedProject?.customerName ?? 'Not selected'}` : `Month-end package count: ${monthEndPackages.length}`,
      billingMode === 'project' ? `Project: ${selectedProject?.projectCode ?? 'Not selected'} - ${selectedProject?.projectName ?? ''}` : '',
      '',
      `Approved labor estimate: ${currency(financialTotals.laborTotal)}`,
      `Staged Certify expenses: ${currency(financialTotals.expenseTotal)}`,
      `Total project/customer cost package: ${currency(financialTotals.packageTotal)}`,
      `Ready-to-invoice amount: ${currency(financialTotals.readyToInvoiceTotal)}`,
      `Blocked / exception amount: ${currency(financialTotals.blockedTotal)}`,
      `Non-billable / excluded amount: ${currency(financialTotals.nonBillableTotal)}`,
      `Readiness: ${percent(readinessPercent)}`,
      '',
      'Blocking issues:',
      ...(blockingIssues.length > 0 ? blockingIssues.map((issue) => `- ${issue}`) : ['- None']),
      '',
      'Notes:',
      packageNotes
    ].filter(Boolean).join('\n');

    try {
      await navigator.clipboard.writeText(content);
      setStatusMessage('Billing readiness summary copied to clipboard.');
    } catch {
      setStatusMessage('Unable to copy summary automatically. Use Export CSV instead.');
    }
  }

  const modeDescription = billingModes.find((mode) => mode.key === billingMode)?.description;

  return (
    <section className="billing-readiness-center">
      <div className="billing-readiness-header">
        <div>
          <p className="eyebrow">Module 039</p>
          <h2>Billing Readiness Center</h2>
          <p className="muted">
            Financial readiness reporting for project billing and month-end billing runs. Review approved labor, staged Certify expenses, customer/project mapping, exceptions, blocked dollars, and ready-to-invoice totals before accounting export.
          </p>
        </div>
        <div className="billing-readiness-actions">
          <button type="button" className="secondary-action" onClick={loadBillingReadinessData}>Refresh data</button>
          <button
            type="button"
            className="secondary-action"
            onClick={() => saveBillingReadiness('blocked')}
            disabled={isSavingReadiness || lifecycle.loading || !lifecycle.data?.capabilities?.canManageBillingReadiness}
          >
            {isSavingReadiness ? 'Saving…' : 'Save progress'}
          </button>
          <button
            type="button"
            className="primary-action"
            onClick={() => saveBillingReadiness('ready')}
            disabled={isSavingReadiness || lifecycle.loading || blockingIssues.length > 0 || !lifecycle.data?.capabilities?.canManageBillingReadiness}
          >
            Mark ready
          </button>
          <button type="button" className="secondary-action" onClick={copyBillingSummary}>Copy summary</button>
          <button type="button" className="secondary-action" onClick={exportMonthEndSummaryCsv}>Export month-end summary</button>
          <button type="button" className="primary-action" onClick={exportBillingReadinessCsv}>Export financial report</button>
        </div>
      </div>

      {statusMessage ? <div className="billing-readiness-alert">{statusMessage}</div> : null}
      {payload.error ? <div className="billing-readiness-error">{payload.error}</div> : null}
      {lifecycle.error ? <div className="billing-readiness-error">{lifecycle.error}</div> : null}

      <div className="billing-mode-selector" role="tablist" aria-label="Billing readiness mode">
        {billingModes.map((mode) => (
          <button
            type="button"
            role="tab"
            aria-selected={billingMode === mode.key}
            key={mode.key}
            className={billingMode === mode.key ? 'active' : ''}
            onClick={() => setBillingMode(mode.key)}
          >
            <strong>{mode.label}</strong>
            <span>{mode.description}</span>
          </button>
        ))}
      </div>

      <div className="billing-readiness-summary-grid">
        <article>
          <span>Readiness</span>
          <strong>{percent(readinessPercent)}</strong>
          <small>{checkedItems.size}/{readinessChecks.length} checklist item(s) confirmed</small>
        </article>
        <article>
          <span>Approved labor</span>
          <strong>{currency(financialTotals.laborTotal)}</strong>
          <small>
            {billingCandidate?.autoCalculatedAmount !== null && billingCandidate?.autoCalculatedAmount !== undefined
              ? `Verified invoice candidate: ${currency(billingCandidate.autoCalculatedAmount)}`
              : 'Estimated from currently loaded approved labor'}
          </small>
        </article>
        <article>
          <span>Certify expenses</span>
          <strong>{currency(financialTotals.expenseTotal)}</strong>
          <small>{activeRows.filter((row) => row.type === 'Expense').length} staged expense line(s)</small>
        </article>
        <article>
          <span>Total cost package</span>
          <strong>{currency(financialTotals.packageTotal)}</strong>
          <small>{financialTotals.rowCount} billing line(s)</small>
        </article>
        <article>
          <span>Ready to invoice</span>
          <strong>{currency(financialTotals.readyToInvoiceTotal)}</strong>
          <small>Excludes blocked and non-billable lines</small>
        </article>
        <article>
          <span>Blocked amount</span>
          <strong>{currency(financialTotals.blockedTotal)}</strong>
          <small>{blockingIssues.length} blocking issue(s)</small>
        </article>
      </div>

      <article className="billing-readiness-panel">
        <div className="billing-readiness-panel-heading">
          <div>
            <h3>Billing run setup</h3>
            <p className="muted">{modeDescription}</p>
          </div>
          <span className={`billing-readiness-status-pill ${readinessTone}`}>
            {lifecycle.data?.billingReadiness?.reviewStatus
              ? `Saved: ${lifecycle.data.billingReadiness.reviewStatus}`
              : readinessTone === 'safe' ? 'Ready for accounting' : readinessTone === 'attention' ? 'Needs review' : 'Blocked'}
          </span>
        </div>

        <div className="billing-readiness-config-grid">
          <label>
            Project / customer
            <select value={selectedProjectKey} onChange={(event) => setSelectedProjectKey(event.target.value)} disabled={billingMode === 'monthEnd'}>
              {projectCandidates.length === 0 ? (
                <option value="">No project candidates loaded</option>
              ) : (
                projectCandidates.map((project) => (
                  <option key={`${project.source}-${project.id}`} value={project.id}>
                    {project.customerName} · {project.projectCode} · {project.projectName}
                  </option>
                ))
              )}
            </select>
          </label>

          <label>
            Package type
            <select value={packageType} onChange={(event) => setPackageType(event.target.value)}>
              {packageTypes.map((type) => (
                <option key={type}>{type}</option>
              ))}
            </select>
          </label>

          <label>
            Period start
            <input type="date" value={periodStart} onChange={(event) => setPeriodStart(event.target.value)} />
          </label>

          <label>
            Period end
            <input type="date" value={periodEnd} onChange={(event) => setPeriodEnd(event.target.value)} />
          </label>

          <label>
            Labor billing rate estimate
            <input type="number" min="0" step="1" value={billingRate} onChange={(event) => setBillingRate(event.target.value)} />
          </label>

          <label>
            Audit reason
            <input
              value={auditReason}
              onChange={(event) => setAuditReason(event.target.value)}
              placeholder="Why this readiness decision is being saved"
            />
          </label>
        </div>

        {billingMode === 'project' && selectedProject ? (
          <div className="billing-selected-project">
            <div>
              <span>Customer</span>
              <strong>{selectedProject.customerName}</strong>
            </div>
            <div>
              <span>Project</span>
              <strong>{selectedProject.projectCode}</strong>
              <small>{selectedProject.projectName}</small>
            </div>
            <div>
              <span>PM</span>
              <strong>{selectedProject.projectManagerName}</strong>
            </div>
            <div>
              <span>Status</span>
              <strong>{selectedProject.status}</strong>
              <small>{selectedProject.source} source</small>
            </div>
          </div>
        ) : null}

        {billingMode === 'monthEnd' ? (
          <div className="billing-month-end-callout">
            <strong>Month-end mode reviews all loaded project/customer candidates.</strong>
            <span>{monthEndPackages.length} customer/project package(s) are included in this month-end billing run preview.</span>
          </div>
        ) : null}
      </article>

      <div className="billing-readiness-two-column">
        <article className="billing-readiness-panel">
          <div className="billing-readiness-panel-heading">
            <div>
              <h3>Billing readiness checklist</h3>
              <p className="muted">Used by PM, Sales, and Accounting before the billing package is exported or invoiced.</p>
            </div>
          </div>

          <div className="billing-readiness-checklist">
            {readinessChecks.map((item) => {
              const ready = checkedItems.has(item.key);
              return (
                <button
                  type="button"
                  key={item.key}
                  className={`billing-check-row ${ready ? 'ready' : 'open'}`}
                  onClick={() => toggleReadinessItem(item.key)}
                >
                  <span>{ready ? '✓' : '○'}</span>
                  <div>
                    <strong>{item.label}</strong>
                    <small>{item.owner} · {item.detail}</small>
                  </div>
                </button>
              );
            })}
          </div>
        </article>

        <article className="billing-readiness-panel">
          <div className="billing-readiness-panel-heading">
            <div>
              <h3>Blocked dollars and review issues</h3>
              <p className="muted">Items that should be resolved before accounting creates invoices or exports the package.</p>
            </div>
          </div>

          <div className="billing-blocker-list">
            {blockingIssues.length === 0 ? (
              <article className="safe">
                <strong>No blocking issues detected</strong>
                <p>This billing package is ready for accounting review based on current placeholder data and checklist selections.</p>
              </article>
            ) : (
              blockingIssues.map((issue) => (
                <article key={issue}>
                  <strong>{issue}</strong>
                  <p>Resolve or document this item before marking the package ready for billing.</p>
                </article>
              ))
            )}
          </div>

          <div className="billing-treatment-list">
            <h4>Billing treatment options</h4>
            <div>
              {billingTreatmentOptions.map((option) => (
                <span key={option}>{option}</span>
              ))}
            </div>
          </div>
        </article>
      </div>

      {billingMode === 'monthEnd' ? (
        <article className="billing-readiness-panel">
          <div className="billing-readiness-panel-heading">
            <div>
              <h3>Month-end billing run summary</h3>
              <p className="muted">Customer/project financial report for the selected billing period.</p>
            </div>
          </div>

          <div className="billing-table-wrap">
            <table className="billing-readiness-table month-end">
              <thead>
                <tr>
                  <th>Customer</th>
                  <th>Project</th>
                  <th>PM</th>
                  <th>Labor</th>
                  <th>Expenses</th>
                  <th>Total package</th>
                  <th>Ready to invoice</th>
                  <th>Blocked</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {monthEndPackages.length === 0 ? (
                  <tr>
                    <td colSpan="9">No month-end package rows available yet.</td>
                  </tr>
                ) : (
                  monthEndPackages.map((item) => (
                    <tr key={`${item.customerName}-${item.projectCode}`}>
                      <td>{item.customerName}</td>
                      <td><strong>{item.projectCode}</strong><small>{item.projectName}</small></td>
                      <td>{item.projectManagerName}</td>
                      <td>{currency(item.laborTotal)}</td>
                      <td>{currency(item.expenseTotal)}</td>
                      <td>{currency(item.packageTotal)}</td>
                      <td>{currency(item.readyToInvoiceTotal)}</td>
                      <td>{currency(item.blockedTotal)}</td>
                      <td>{item.status}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </article>
      ) : null}

      <article className="billing-readiness-panel">
        <div className="billing-readiness-panel-heading">
          <div>
            <h3>Billing readiness financial report</h3>
            <p className="muted">Detailed financial lines combining approved labor and staged Certify expense records.</p>
          </div>
          <span className="billing-readiness-status-pill neutral">Project stays active</span>
        </div>

        <div className="billing-table-wrap">
          <table className="billing-readiness-table">
            <thead>
              <tr>
                <th>Line type</th>
                <th>Source</th>
                <th>Customer</th>
                <th>Project</th>
                <th>Description</th>
                <th>Qty</th>
                <th>Rate / unit</th>
                <th>Amount</th>
                <th>Treatment</th>
                <th>Readiness</th>
              </tr>
            </thead>
            <tbody>
              {activeRows.length === 0 ? (
                <tr>
                  <td colSpan="10">No financial report rows available yet.</td>
                </tr>
              ) : (
                activeRows.map((row, index) => (
                  <tr key={`${row.type}-${row.source}-${row.projectCode}-${index}`}>
                    <td><strong>{row.type}</strong></td>
                    <td>{row.source}</td>
                    <td>{row.customerName}</td>
                    <td><strong>{row.projectCode}</strong><small>{row.projectName}</small></td>
                    <td>{row.description}</td>
                    <td>{row.quantity}</td>
                    <td>{currency(row.unitRate)}</td>
                    <td>{currency(row.amount)}</td>
                    <td>{row.treatment}</td>
                    <td>{row.readiness}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </article>

      <article className="billing-readiness-panel">
        <div className="billing-readiness-panel-heading">
          <div>
            <h3>Certify exception review</h3>
            <p className="muted">Certify exceptions are shown here so imported expenses remain blocked until mapping is corrected.</p>
          </div>
        </div>

        <div className="billing-exception-grid">
          {certifyExceptions.length === 0 ? (
            <article className="safe">
              <strong>No Certify exceptions loaded</strong>
              <p>Unmapped project, customer, category, or receipt exceptions will appear here.</p>
            </article>
          ) : (
            certifyExceptions.map((exception) => (
              <article key={exception.exceptionCode || exception.message}>
                <span>{exception.severity || 'Review'}</span>
                <strong>{exception.exceptionCode || 'CERTIFY_EXCEPTION'}</strong>
                <p>{exception.message}</p>
                <small>{exception.resolution}</small>
              </article>
            ))
          )}
        </div>
      </article>

      <article className="billing-readiness-panel billing-notes-panel">
        <div>
          <h3>Billing package notes</h3>
          <p className="muted">Use this for PM/accounting context, customer-facing explanation, partial billing assumptions, or month-end notes.</p>
        </div>
        <textarea value={packageNotes} onChange={(event) => setPackageNotes(event.target.value)} />
      </article>

      <article className="billing-readiness-panel billing-guardrail">
        <div>
          <h3>Workflow guardrail</h3>
          <p>
            Module 039 now persists the PM/Accounting readiness decision and its audit reason. It does not close the project or send an invoice; approved packages continue to Module 042 for controlled partial or final invoice creation and delivery.
          </p>
        </div>
        <span className="billing-readiness-status-pill safe">Persisted and audited</span>
      </article>
    </section>
  );
}

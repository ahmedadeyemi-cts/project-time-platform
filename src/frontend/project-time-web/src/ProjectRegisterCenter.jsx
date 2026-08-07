import { useEffect, useMemo, useState } from 'react';
import './project-register-center.css';
import './module006-standalone.css';

// MODULE_006_CUSTOMER_EXPANSION_START

const PAGE_SIZES = Object.freeze([10, 15, 25]);
const TASK_STATUSES = Object.freeze(['not_started', 'in_progress', 'blocked', 'completed', 'cancelled']);
const PROJECT_STATUSES = Object.freeze(['No Status', 'On Track', 'At Risk', 'Blocked', 'Pending', 'Complete']);

function clean(value) {
  return String(value ?? '').trim();
}

function normalize(value) {
  return clean(value).toLowerCase();
}

function labelize(value) {
  const text = clean(value);
  if (!text) return 'Not set';
  return text
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function money(value) {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount)
    ? new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2 }).format(amount)
    : '$0.00';
}

function dateOnly(value) {
  return value ? String(value).slice(0, 10) : '';
}

function dateLabel(value) {
  const date = dateOnly(value);
  return date || 'Not set';
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function unique(items, selector) {
  return [...new Set(items.map(selector).filter(Boolean))]
    .sort((left, right) => String(left).localeCompare(String(right)));
}

function authHeaders(extra = {}) {
  const headers = { ...extra };
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    const token = session?.sessionToken || session?.token || session?.accessToken;
    if (token) {
      headers['X-ProjectPulse-Session'] = token;
      headers['X-Project-Pulse-Session'] = token;
      headers['X-Session-Token'] = token;
      headers.Authorization = `Bearer ${token}`;
    }
    const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    if (viewAs?.userId) headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
  } catch {
    // Global session bridges remain the fallback.
  }
  return headers;
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'same-origin',
    cache: 'no-store',
    ...options,
    headers: authHeaders({
      Accept: 'application/json',
      ...(options.body && !(options.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    })
  });
  const raw = await response.text();
  let payload = null;
  try { payload = raw ? JSON.parse(raw) : null; } catch { payload = { message: raw }; }
  if (!response.ok) {
    const error = new Error(payload?.message || payload?.detail || payload?.status || `HTTP ${response.status}`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload || {};
}

function mergePipelineRecords(serverRecords) {
  const server = Array.isArray(serverRecords) ? serverRecords : [];
  return server.map((record) => ({ ...record, persisted: true })).sort((left, right) => {
    const lifecycle = Number(Boolean(left.isArchived)) - Number(Boolean(right.isArchived));
    if (lifecycle !== 0) return lifecycle;
    return `${left.customer} ${left.sourceProjectCode}`.localeCompare(`${right.customer} ${right.sourceProjectCode}`);
  });
}

function blankProject() {
  return {
    sourceProjectCode: '',
    customer: 'Toyota',
    businessUnit: '',
    ussOwner: '',
    projectName: '',
    quoteText: '',
    estimatedValue: '',
    status: 'No Status',
    lifecycle: 'active',
    updateDate: new Date().toISOString().slice(0, 10),
    nextReviewDate: '',
    note: ''
  };
}

function projectForm(record) {
  return {
    sourceProjectCode: record?.sourceProjectCode || '',
    sourceKind: record?.sourceKind || 'manual',
    customer: record?.customer || 'Toyota',
    businessUnit: record?.businessUnit || '',
    ussOwner: record?.ussOwner || '',
    projectName: record?.projectName || '',
    quoteText: record?.quoteText || '',
    estimatedValue: record?.estimatedValue ?? '',
    status: record?.status || 'No Status',
    lifecycle: record?.lifecycle || (record?.isArchived ? 'historical' : 'active'),
    updateDate: dateOnly(record?.updateDate),
    nextReviewDate: dateOnly(record?.nextReviewDate)
  };
}

function blankTask() {
  return {
    title: '',
    description: '',
    status: 'not_started',
    assignedTo: '',
    dueDate: '',
    note: ''
  };
}

function xmlEscape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&apos;');
}

function spreadsheetCell(value, type = 'String') {
  const cleanValue = type === 'Number' ? Number(value || 0) : value;
  return `<Cell><Data ss:Type="${type}">${xmlEscape(cleanValue)}</Data></Cell>`;
}

function worksheetXml(name, headers, rows) {
  const header = headers.map((value) => spreadsheetCell(value)).join('');
  const body = rows.map((row) => `<Row>${row.map((cell) => spreadsheetCell(cell?.value, cell?.type || 'String')).join('')}</Row>`).join('');
  return `<Worksheet ss:Name="${xmlEscape(name)}"><Table><Row ss:StyleID="Header">${header}</Row>${body}</Table></Worksheet>`;
}

function buildWorkbook(records, updates, tasks) {
  const projectRows = records.map((record) => [
    { value: record.recordId },
    { value: record.sourceProjectCode },
    { value: record.customer },
    { value: record.businessUnit },
    { value: record.ussOwner },
    { value: record.projectName },
    { value: record.quoteText },
    { value: Number(record.estimatedValue || 0), type: 'Number' },
    { value: record.status },
    { value: record.lifecycle },
    { value: dateOnly(record.updateDate) },
    { value: dateOnly(record.nextReviewDate) },
    { value: record.latestNote },
    { value: record.persisted ? 'Database' : 'Reviewed snapshot' }
  ]);
  const updateRows = updates.map((event) => [
    { value: event.updateId },
    { value: event.sourceProjectCode || records.find((record) => String(record.recordId) === String(event.recordId))?.sourceProjectCode || '' },
    { value: event.note },
    { value: event.status },
    { value: dateOnly(event.updateDate) },
    { value: dateOnly(event.nextReviewDate) },
    { value: event.createdBy },
    { value: event.createdAt },
    { value: event.source || 'Module 006' }
  ]);
  const taskRows = tasks.map((task) => [
    { value: task.taskId },
    { value: records.find((record) => String(record.recordId) === String(task.recordId))?.sourceProjectCode || '' },
    { value: task.title },
    { value: task.description },
    { value: task.status },
    { value: task.assignedTo },
    { value: dateOnly(task.dueDate) },
    { value: task.isArchived ? 'Archived' : 'Active' },
    { value: task.updatedBy },
    { value: task.updatedAt }
  ]);
  return `<?xml version="1.0"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
<Styles><Style ss:ID="Default" ss:Name="Normal"><Alignment ss:Vertical="Top" ss:WrapText="1"/><Font ss:FontName="Aptos" ss:Size="10"/></Style><Style ss:ID="Header"><Font ss:Bold="1" ss:Color="#FFFFFF"/><Interior ss:Color="#003B5C" ss:Pattern="Solid"/><Alignment ss:Vertical="Center" ss:WrapText="1"/></Style></Styles>
${worksheetXml('Projects', ['Record ID', 'Project ID', 'Customer', 'Business Unit', 'USS Owner', 'Project Name', 'Quote(s)', 'Estimated Value', 'Status', 'Lifecycle', 'Update Date', 'Next Review Date', 'Latest Note', 'Source'], projectRows)}
${worksheetXml('Updates and Notes', ['Update ID', 'Project ID', 'Note', 'Status', 'Update Date', 'Next Review Date', 'Created By', 'Created At', 'Source'], updateRows)}
${worksheetXml('Tasks', ['Task ID', 'Project ID', 'Task', 'Description', 'Status', 'Assigned To', 'Due Date', 'Lifecycle', 'Updated By', 'Updated At'], taskRows)}
</Workbook>`;
}

function downloadText(content, fileName, type) {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function Pagination({ page, pageCount, pageSize, total, onPage, onPageSize }) {
  if (!total) return null;
  const start = ((page - 1) * pageSize) + 1;
  const end = Math.min(page * pageSize, total);
  return (
    <nav className="project-register-pagination" aria-label="Customer pipeline pagination">
      <span>Showing {start}–{end} of {total}</span>
      <label>Rows
        <select value={pageSize} onChange={(event) => onPageSize(Number(event.target.value))}>
          {PAGE_SIZES.map((size) => <option value={size} key={size}>{size}</option>)}
        </select>
      </label>
      <button type="button" onClick={() => onPage(Math.max(1, page - 1))} disabled={page <= 1}>Previous</button>
      <span>Page {page} of {pageCount}</span>
      <button type="button" onClick={() => onPage(Math.min(pageCount, page + 1))} disabled={page >= pageCount}>Next</button>
    </nav>
  );
}

export default function ProjectRegisterCenter({ legacyRoute = false }) {
  const [runtime, setRuntime] = useState({ loading: true, error: '', warning: '', records: [], updates: [], actor: null });
  const [taskRuntime, setTaskRuntime] = useState({ error: '', tasks: [], events: [] });
  const [searchTerm, setSearchTerm] = useState('');
  const [lifecycle, setLifecycle] = useState('active');
  const [customer, setCustomer] = useState('all');
  const [status, setStatus] = useState('all');
  const [owner, setOwner] = useState('all');
  const [pageSize, setPageSize] = useState(10);
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState('');
  const [drawerTab, setDrawerTab] = useState('details');
  const [editForm, setEditForm] = useState(blankProject);
  const [noteForm, setNoteForm] = useState({ note: '', status: '', updateDate: new Date().toISOString().slice(0, 10), nextReviewDate: '' });
  const [taskForm, setTaskForm] = useState(blankTask);
  const [newProjectOpen, setNewProjectOpen] = useState(false);
  const [newProjectForm, setNewProjectForm] = useState(blankProject);
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState('');

  useEffect(() => {
    if (legacyRoute && typeof window !== 'undefined' && window.location.hash !== '#toyota-hyundai-pipelines') {
      window.history.replaceState(window.history.state, '', '#toyota-hyundai-pipelines');
    }
  }, [legacyRoute]);

  async function load() {
    setRuntime((current) => ({ ...current, loading: true, error: '', warning: '' }));
    const [pipelineResult, taskResult] = await Promise.allSettled([
      api('/api/module-006/pipeline'),
      api('/api/module-006/tasks')
    ]);

    if (pipelineResult.status === 'fulfilled') {
      setRuntime({
        loading: false,
        error: '',
        warning: '',
        records: pipelineResult.value.records || [],
        updates: pipelineResult.value.updates || [],
        actor: pipelineResult.value.actor || null
      });
    } else {
      setRuntime({
        loading: false,
        error: pipelineResult.reason?.message || 'Module 006 editing could not be loaded.',
        warning: 'No pipeline data is displayed while the authorized Module 006 runtime is unavailable.',
        records: [],
        updates: [],
        actor: null
      });
    }

    if (taskResult.status === 'fulfilled') {
      setTaskRuntime({ error: '', tasks: taskResult.value.tasks || [], events: taskResult.value.events || [] });
    } else {
      setTaskRuntime({ error: taskResult.reason?.message || 'Standalone Module 006 tasks could not be loaded.', tasks: [], events: [] });
    }
  }

  useEffect(() => { void load(); }, []);
  useEffect(() => { setPage(1); }, [searchTerm, lifecycle, customer, status, owner, pageSize]);

  const records = useMemo(() => mergePipelineRecords(runtime.records), [runtime.records]);
  const updates = useMemo(() => (runtime.updates || [])
    .map((event) => ({ ...event, source: 'Module 006' }))
    .sort((left, right) => String(right.createdAt || right.updateDate || '').localeCompare(String(left.createdAt || left.updateDate || ''))), [runtime.updates]);

  const customerOptions = useMemo(() => unique(records, (record) => record.customer), [records]);
  const statusOptions = useMemo(() => unique(records, (record) => record.status), [records]);
  const ownerOptions = useMemo(() => unique(records, (record) => record.ussOwner), [records]);

  const filteredRecords = useMemo(() => {
    const search = normalize(searchTerm);
    return records.filter((record) => {
      if (lifecycle !== 'all' && (record.isArchived ? 'historical' : record.lifecycle) !== lifecycle) return false;
      if (customer !== 'all' && record.customer !== customer) return false;
      if (status !== 'all' && record.status !== status) return false;
      if (owner !== 'all' && record.ussOwner !== owner) return false;
      if (!search) return true;
      return [
        record.sourceProjectCode,
        record.customer,
        record.businessUnit,
        record.ussOwner,
        record.projectName,
        record.quoteText,
        record.latestNote,
        record.status
      ].join(' ').toLowerCase().includes(search);
    });
  }, [customer, lifecycle, owner, records, searchTerm, status]);

  const pageCount = Math.max(1, Math.ceil(filteredRecords.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  const visibleRecords = filteredRecords.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const selectedRecord = records.find((record) => String(record.recordId) === String(selectedId)) || null;
  const selectedUpdates = selectedRecord
    ? updates.filter((event) => String(event.recordId) === String(selectedRecord.recordId) || normalize(event.sourceProjectCode) === normalize(selectedRecord.sourceProjectCode))
    : [];
  const selectedTasks = selectedRecord
    ? taskRuntime.tasks.filter((task) => String(task.recordId) === String(selectedRecord.recordId))
    : [];

  const summary = useMemo(() => ({
    total: records.length,
    active: records.filter((record) => !record.isArchived && record.lifecycle !== 'historical').length,
    historical: records.filter((record) => record.isArchived || record.lifecycle === 'historical').length,
    tasks: taskRuntime.tasks.filter((task) => !task.isArchived).length
  }), [records, taskRuntime.tasks]);
  const filteredValue = filteredRecords.reduce((total, record) => total + Number(record.estimatedValue || 0), 0);
  const canEdit = runtime.actor?.CanEdit === true || runtime.actor?.canEdit === true;
  const isViewAs = runtime.actor?.IsViewAs === true || runtime.actor?.isViewAs === true;

  function openRecord(record, tab = 'details') {
    setSelectedId(String(record.recordId));
    setEditForm(projectForm(record));
    setNoteForm({
      note: '',
      status: record.status || '',
      updateDate: dateOnly(record.updateDate) || new Date().toISOString().slice(0, 10),
      nextReviewDate: dateOnly(record.nextReviewDate)
    });
    setTaskForm(blankTask());
    setDrawerTab(tab);
    setMessage('');
  }

  async function persistRecord(record = selectedRecord, form = editForm) {
    if (!record) throw new Error('Select a Module 006 record first.');
    return api(`/api/module-006/pipeline/${record.recordId}`, {
      method: 'PUT',
      body: JSON.stringify({
        sourceProjectCode: form.sourceProjectCode,
        sourceKind: form.sourceKind || (record.persisted ? record.sourceKind : 'manual'),
        customer: form.customer,
        businessUnit: form.businessUnit,
        ussOwner: form.ussOwner,
        projectName: form.projectName,
        quoteText: form.quoteText,
        estimatedValue: Number(form.estimatedValue || 0),
        status: form.status,
        lifecycle: form.lifecycle,
        updateDate: form.updateDate || null,
        nextReviewDate: form.nextReviewDate || null,
        expectedRevision: Number(record.revision || 0)
      })
    });
  }

  async function saveDetails() {
    if (!selectedRecord || !canEdit) return;
    if (clean(editForm.customer).length < 2) {
      setMessage('Enter a customer name containing at least two characters.');
      return;
    }
    setBusy('details');
    setMessage('');
    try {
      const result = await persistRecord();
      setMessage(result.message || 'Module 006 project details saved.');
      await load();
      setSelectedId(String(selectedRecord.recordId));
    } catch (error) {
      setMessage(error.message || 'Unable to save Module 006 project details.');
    } finally { setBusy(''); }
  }

  async function appendUpdate() {
    if (!selectedRecord || !canEdit || clean(noteForm.note).length < 3) {
      setMessage('Enter a status note containing at least three characters.');
      return;
    }
    setBusy('update');
    setMessage('');
    try {
      let revision = Number(selectedRecord.revision || 0);
      if (!selectedRecord.persisted) {
        const saved = await persistRecord(selectedRecord, editForm);
        revision = Number(saved.revision || 1);
      }
      const result = await api(`/api/module-006/pipeline/${selectedRecord.recordId}/updates`, {
        method: 'POST',
        body: JSON.stringify({
          note: noteForm.note,
          status: noteForm.status,
          updateDate: noteForm.updateDate || null,
          nextReviewDate: noteForm.nextReviewDate || null,
          expectedRevision: revision
        })
      });
      setMessage(result.message || 'Status update added.');
      setNoteForm((current) => ({ ...current, note: '' }));
      await load();
      setSelectedId(String(selectedRecord.recordId));
    } catch (error) {
      setMessage(error.message || 'Unable to add the status update.');
    } finally { setBusy(''); }
  }

  async function createProject() {
    if (!canEdit) return;
    if (clean(newProjectForm.customer).length < 2) {
      setMessage('Enter a customer name containing at least two characters.');
      return;
    }
    if (clean(newProjectForm.projectName).length < 3) {
      setMessage('Enter a project name containing at least three characters.');
      return;
    }
    setBusy('create');
    setMessage('');
    try {
      const result = await api('/api/module-006/pipeline', {
        method: 'POST',
        body: JSON.stringify({
          sourceProjectCode: newProjectForm.sourceProjectCode || null,
          customer: newProjectForm.customer,
          businessUnit: newProjectForm.businessUnit,
          ussOwner: newProjectForm.ussOwner,
          projectName: newProjectForm.projectName,
          quoteText: newProjectForm.quoteText,
          estimatedValue: Number(newProjectForm.estimatedValue || 0),
          status: newProjectForm.status,
          updateDate: newProjectForm.updateDate || null,
          nextReviewDate: newProjectForm.nextReviewDate || null,
          note: newProjectForm.note
        })
      });
      setNewProjectOpen(false);
      setNewProjectForm(blankProject());
      setMessage(result.message || 'New Module 006 project added.');
      await load();
      if (result.recordId) {
        const created = mergePipelineRecords((await api('/api/module-006/pipeline')).records || [])
          .find((record) => String(record.recordId) === String(result.recordId));
        if (created) openRecord(created);
      }
    } catch (error) {
      setMessage(error.message || 'Unable to create the Module 006 project.');
    } finally { setBusy(''); }
  }

  async function changeArchiveState(record, archive) {
    if (!canEdit) return;
    const reason = window.prompt(`${archive ? 'Archive' : 'Restore'} ${record.sourceProjectCode}. Enter a reason:`);
    if (!clean(reason)) return;
    setBusy('archive');
    setMessage('');
    try {
      const result = await api(`/api/module-006/pipeline/${record.recordId}/archive`, {
        method: 'POST',
        body: JSON.stringify({ reason, expectedRevision: Number(record.revision || 0), archive })
      });
      setMessage(result.message || (archive ? 'Project archived.' : 'Project restored.'));
      await load();
      setSelectedId(String(record.recordId));
    } catch (error) {
      setMessage(error.message || 'Unable to change the project lifecycle.');
    } finally { setBusy(''); }
  }

  async function ensurePersistedRecord() {
    if (!selectedRecord) throw new Error('Select a Module 006 record first.');
    if (selectedRecord.persisted) return Number(selectedRecord.revision || 1);
    const result = await persistRecord(selectedRecord, editForm);
    await load();
    return Number(result.revision || 1);
  }

  async function createTask() {
    if (!selectedRecord || !canEdit || clean(taskForm.title).length < 3) {
      setMessage('Enter a task title containing at least three characters.');
      return;
    }
    setBusy('task-create');
    setMessage('');
    try {
      await ensurePersistedRecord();
      const result = await api(`/api/module-006/pipeline/${selectedRecord.recordId}/tasks`, {
        method: 'POST',
        body: JSON.stringify({
          title: taskForm.title,
          description: taskForm.description,
          status: taskForm.status,
          assignedTo: taskForm.assignedTo,
          dueDate: taskForm.dueDate || null,
          note: taskForm.note
        })
      });
      setTaskForm(blankTask());
      setMessage(result.message || 'Task created.');
      await load();
      setSelectedId(String(selectedRecord.recordId));
    } catch (error) {
      setMessage(error.message || 'Unable to create the task.');
    } finally { setBusy(''); }
  }

  async function saveTask(task, fields) {
    if (!selectedRecord || !canEdit) return;
    setBusy(`task-${task.taskId}`);
    setMessage('');
    try {
      const result = await api(`/api/module-006/pipeline/${selectedRecord.recordId}/tasks/${task.taskId}`, {
        method: 'PUT',
        body: JSON.stringify({
          title: fields.title,
          description: fields.description,
          status: fields.status,
          assignedTo: fields.assignedTo,
          dueDate: fields.dueDate || null,
          note: fields.note || `Updated task ${fields.title}.`,
          expectedRevision: Number(task.revision || 0)
        })
      });
      setMessage(result.message || 'Task saved.');
      await load();
      setSelectedId(String(selectedRecord.recordId));
    } catch (error) {
      setMessage(error.message || 'Unable to save the task.');
    } finally { setBusy(''); }
  }

  async function archiveTask(task, archive) {
    if (!selectedRecord || !canEdit) return;
    const reason = window.prompt(`${archive ? 'Archive' : 'Restore'} task “${task.title}”. Enter a reason:`);
    if (!clean(reason)) return;
    setBusy(`task-${task.taskId}`);
    try {
      const result = await api(`/api/module-006/pipeline/${selectedRecord.recordId}/tasks/${task.taskId}/archive`, {
        method: 'POST',
        body: JSON.stringify({ archive, reason, expectedRevision: Number(task.revision || 0) })
      });
      setMessage(result.message || 'Task lifecycle updated.');
      await load();
      setSelectedId(String(selectedRecord.recordId));
    } catch (error) {
      setMessage(error.message || 'Unable to change the task lifecycle.');
    } finally { setBusy(''); }
  }

  function exportExcel() {
    const workbook = buildWorkbook(filteredRecords, updates, taskRuntime.tasks);
    downloadText(workbook, `US-Signal-Customer-Pipelines-${new Date().toISOString().slice(0, 10)}.xls`, 'application/vnd.ms-excel;charset=utf-8');
  }

  return (
    <section className="project-register-center projectpulse-module-standard module006-standalone" data-module="006" data-module-name="Toyota & Hyundai Pipelines" data-canonical-route="toyota-hyundai-pipelines" data-project-register-contract="module006-standalone-pipeline-v1">
      <datalist id="module006-customer-options">
        {customerOptions.map((value) => <option value={value} key={value} />)}
      </datalist>
      <header className="project-register-hero">
        <div>
          <p className="eyebrow">MODULE 006 · STANDALONE TOYOTA & HYUNDAI PIPELINE</p>
          <h2>Toyota &amp; Hyundai Pipelines</h2>
          <p>Manage the reviewed Toyota and Hyundai pipeline baseline plus additional customer projects, action items, review dates, status updates, and append-only note history directly in Module 006.</p>
          <small>Authorized live records and append-only history from the standalone Module 006 service.</small>
        </div>
        <div className="project-register-hero-actions">
          <button type="button" className="primary-action" disabled={!canEdit || isViewAs} onClick={() => setNewProjectOpen(true)}>Add New Project</button>
          <button type="button" className="secondary-action" onClick={exportExcel}>Export Excel</button>
          <button type="button" className="secondary-action" onClick={() => window.print()}>Print / Save PDF</button>
          <button type="button" className="secondary-action" onClick={() => void load()}>Refresh</button>
        </div>
      </header>

      <div className="module006-independence-banner">
        <strong>Standalone authority</strong>
        <span>Module 006 owns its project rows, tasks, updates, and notes. It does not create, open, or modify records in another project module.</span>
      </div>
      {runtime.error ? <div className="project-register-banner warning"><strong>Editing runtime unavailable</strong><span>{runtime.error}</span></div> : null}
      {runtime.warning ? <div className="project-register-banner">{runtime.warning}</div> : null}
      {taskRuntime.error ? <div className="project-register-banner warning">Task service: {taskRuntime.error}</div> : null}
      {message ? <div className="project-register-banner" aria-live="polite">{message}</div> : null}

      <div className="project-register-summary" aria-label="Customer pipeline summary">
        <article><span>Total projects</span><strong>{summary.total}</strong><small>{filteredRecords.length} match the current filters</small></article>
        <article><span>Active</span><strong>{summary.active}</strong><small>Current pipeline records</small></article>
        <article><span>Archived / closed</span><strong>{summary.historical}</strong><small>History remains searchable</small></article>
        <article><span>Open tasks</span><strong>{summary.tasks}</strong><small>Standalone Module 006 action items</small></article>
        <article><span>Filtered estimated value</span><strong>{money(filteredValue)}</strong><small>Current filtered view</small></article>
      </div>

      <div className="project-register-toolbar">
        <label className="wide">Search
          <input type="search" value={searchTerm} onChange={(event) => setSearchTerm(event.target.value)} placeholder="Project ID, project, customer, owner, quote, status, or note…" />
        </label>
        <label>Register view
          <select value={lifecycle} onChange={(event) => setLifecycle(event.target.value)}><option value="active">Active</option><option value="historical">Archived / historical</option><option value="all">All records</option></select>
        </label>
        <label>Customer
          <select value={customer} onChange={(event) => setCustomer(event.target.value)}><option value="all">All customers</option>{customerOptions.map((value) => <option value={value} key={value}>{value}</option>)}</select>
        </label>
        <label>Status
          <select value={status} onChange={(event) => setStatus(event.target.value)}><option value="all">All statuses</option>{statusOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}</select>
        </label>
        <label>USS owner
          <select value={owner} onChange={(event) => setOwner(event.target.value)}><option value="all">All USS owners</option>{ownerOptions.map((value) => <option value={value} key={value}>{value}</option>)}</select>
        </label>
      </div>

      <Pagination page={currentPage} pageCount={pageCount} pageSize={pageSize} total={filteredRecords.length} onPage={setPage} onPageSize={setPageSize} />
      <div className="project-register-table-wrap">
        <table className="project-register-table">
          <thead><tr><th>Project</th><th>Customer / Business Unit</th><th className="project-register-status-column">Status</th><th>USS Owner</th><th>Dates</th><th>Quote / Estimated Value</th><th>Latest Update</th><th>Tasks</th><th>Action</th></tr></thead>
          <tbody>
            {visibleRecords.map((record) => {
              const taskCount = taskRuntime.tasks.filter((task) => String(task.recordId) === String(record.recordId) && !task.isArchived).length;
              return (
                <tr key={record.recordId} data-pipeline-entry-id={record.recordId} onDoubleClick={() => openRecord(record)}>
                  <td><strong>{record.sourceProjectCode}</strong><small>{record.projectName || 'Unnamed project'}</small><small className="project-register-immutable-id">Module 006 ID: {record.recordId}</small></td>
                  <td><strong>{record.customer}</strong><small>{record.businessUnit || 'Business unit not set'}</small></td>
                  <td className="project-register-status-column"><span className={`project-register-state ${record.isArchived ? 'historical' : 'active'}`}>{record.isArchived ? 'Historical' : labelize(record.status || 'Active')}</span></td>
                  <td><strong>{record.ussOwner || 'Not assigned'}</strong></td>
                  <td><small>Updated: {dateLabel(record.updateDate)}</small><small>Next review: {dateLabel(record.nextReviewDate)}</small></td>
                  <td><small>Quote(s): {record.quoteText || 'Not set'}</small><small>Estimate: {money(record.estimatedValue)}</small></td>
                  <td><p className="project-register-latest-note">{record.latestNote || 'No current note supplied.'}</p></td>
                  <td><strong>{taskCount}</strong><small>active action item(s)</small></td>
                  <td><button type="button" className="project-register-row-action" onClick={() => openRecord(record)}>{canEdit ? 'Open / edit' : 'View details'}</button></td>
                </tr>
              );
            })}
            {!visibleRecords.length ? <tr><td colSpan="9" className="project-register-empty-cell">No customer pipeline records match the current filters.</td></tr> : null}
          </tbody>
        </table>
      </div>
      <Pagination page={currentPage} pageCount={pageCount} pageSize={pageSize} total={filteredRecords.length} onPage={setPage} onPageSize={setPageSize} />

      {selectedRecord ? (
        <div className="project-register-drawer-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSelectedId(''); }}>
          <aside className="project-register-drawer module006-drawer" role="dialog" aria-modal="true" aria-label="Customer pipeline project editor">
            <header>
              <div><p className="eyebrow">MODULE 006 PROJECT</p><h3>{selectedRecord.projectName}</h3><p>{selectedRecord.customer} · {selectedRecord.sourceProjectCode}</p></div>
              <button type="button" className="secondary-action" onClick={() => setSelectedId('')}>Close</button>
            </header>
            <nav className="module006-tabs" aria-label="Module 006 project sections">
              {['details', 'updates', 'tasks', 'history'].map((tab) => <button type="button" key={tab} className={drawerTab === tab ? 'active' : ''} onClick={() => setDrawerTab(tab)}>{tab === 'updates' ? 'Updates & Notes' : labelize(tab)}</button>)}
            </nav>
            {message ? <div className="project-register-banner" aria-live="polite">{message}</div> : null}

            {drawerTab === 'details' ? (
              <section className="module006-editor-section">
                <h4>Project details</h4>
                <div className="module006-form-grid">
                  <label>Project ID<input value={editForm.sourceProjectCode} onChange={(event) => setEditForm((current) => ({ ...current, sourceProjectCode: event.target.value.toUpperCase() }))} disabled={!canEdit || selectedRecord.persisted} /></label>
                  <label>Customer<small>Choose an existing customer or type a new customer name.</small><input list="module006-customer-options" maxLength="120" value={editForm.customer} onChange={(event) => setEditForm((current) => ({ ...current, customer: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Business Unit<input value={editForm.businessUnit} onChange={(event) => setEditForm((current) => ({ ...current, businessUnit: event.target.value }))} disabled={!canEdit} /></label>
                  <label>USS Owner<input value={editForm.ussOwner} onChange={(event) => setEditForm((current) => ({ ...current, ussOwner: event.target.value }))} disabled={!canEdit} /></label>
                  <label className="wide">Project Name<input value={editForm.projectName} onChange={(event) => setEditForm((current) => ({ ...current, projectName: event.target.value }))} disabled={!canEdit} /></label>
                  <label className="wide">Quote Number(s)<input value={editForm.quoteText} onChange={(event) => setEditForm((current) => ({ ...current, quoteText: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Estimated Value<input type="number" min="0" step="0.01" value={editForm.estimatedValue} onChange={(event) => setEditForm((current) => ({ ...current, estimatedValue: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Status<select value={editForm.status} onChange={(event) => setEditForm((current) => ({ ...current, status: event.target.value }))} disabled={!canEdit}>{unique([...PROJECT_STATUSES, ...statusOptions], (value) => value).map((value) => <option value={value} key={value}>{value}</option>)}</select></label>
                  <label>Update Date<input type="date" value={editForm.updateDate} onChange={(event) => setEditForm((current) => ({ ...current, updateDate: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Next Review Date<input type="date" value={editForm.nextReviewDate} onChange={(event) => setEditForm((current) => ({ ...current, nextReviewDate: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Lifecycle<select value={editForm.lifecycle} onChange={(event) => setEditForm((current) => ({ ...current, lifecycle: event.target.value }))} disabled={!canEdit}><option value="active">Active</option><option value="historical">Archived / historical</option></select></label>
                </div>
                <div className="module006-form-actions">
                  <button type="button" className="primary-action" disabled={!canEdit || busy === 'details' || clean(editForm.customer).length < 2} onClick={() => void saveDetails()}>{busy === 'details' ? 'Saving…' : 'Save Project Details'}</button>
                  <button type="button" className="secondary-action" disabled={!canEdit || busy === 'archive'} onClick={() => void changeArchiveState(selectedRecord, !selectedRecord.isArchived)}>{selectedRecord.isArchived ? 'Restore Project' : 'Archive Project'}</button>
                </div>
              </section>
            ) : null}

            {drawerTab === 'updates' ? (
              <section className="module006-editor-section">
                <h4>Add status update or note</h4>
                <div className="module006-form-grid">
                  <label>Status<select value={noteForm.status} onChange={(event) => setNoteForm((current) => ({ ...current, status: event.target.value }))} disabled={!canEdit}>{unique([...PROJECT_STATUSES, ...statusOptions], (value) => value).map((value) => <option value={value} key={value}>{value}</option>)}</select></label>
                  <label>Update Date<input type="date" value={noteForm.updateDate} onChange={(event) => setNoteForm((current) => ({ ...current, updateDate: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Next Review Date<input type="date" value={noteForm.nextReviewDate} onChange={(event) => setNoteForm((current) => ({ ...current, nextReviewDate: event.target.value }))} disabled={!canEdit} /></label>
                  <label className="wide">New Status Note<textarea rows={5} value={noteForm.note} onChange={(event) => setNoteForm((current) => ({ ...current, note: event.target.value }))} placeholder="Enter the latest update. Previous updates remain in history." disabled={!canEdit} /></label>
                </div>
                <button type="button" className="primary-action" disabled={!canEdit || busy === 'update'} onClick={() => void appendUpdate()}>{busy === 'update' ? 'Saving…' : 'Save Update'}</button>
              </section>
            ) : null}

            {drawerTab === 'tasks' ? (
              <section className="module006-editor-section">
                <h4>Standalone tasks and action items</h4>
                <p className="muted">Create and maintain action items for this pipeline project directly in Module 006.</p>
                <div className="module006-task-create">
                  <label>Task title<input value={taskForm.title} onChange={(event) => setTaskForm((current) => ({ ...current, title: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Assigned to<input value={taskForm.assignedTo} onChange={(event) => setTaskForm((current) => ({ ...current, assignedTo: event.target.value }))} disabled={!canEdit} /></label>
                  <label>Status<select value={taskForm.status} onChange={(event) => setTaskForm((current) => ({ ...current, status: event.target.value }))} disabled={!canEdit}>{TASK_STATUSES.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}</select></label>
                  <label>Due date<input type="date" value={taskForm.dueDate} onChange={(event) => setTaskForm((current) => ({ ...current, dueDate: event.target.value }))} disabled={!canEdit} /></label>
                  <label className="wide">Description<textarea rows={3} value={taskForm.description} onChange={(event) => setTaskForm((current) => ({ ...current, description: event.target.value }))} disabled={!canEdit} /></label>
                  <label className="wide">Creation note<textarea rows={2} value={taskForm.note} onChange={(event) => setTaskForm((current) => ({ ...current, note: event.target.value }))} disabled={!canEdit} /></label>
                  <button type="button" className="primary-action" disabled={!canEdit || busy === 'task-create'} onClick={() => void createTask()}>{busy === 'task-create' ? 'Creating…' : 'Create New Task'}</button>
                </div>
                <div className="module006-task-list">
                  {selectedTasks.map((task) => <EditableTask key={task.taskId} task={task} canEdit={canEdit} busy={busy === `task-${task.taskId}`} onSave={saveTask} onArchive={archiveTask} />)}
                  {!selectedTasks.length ? <p className="muted">No standalone tasks have been created for this project.</p> : null}
                </div>
              </section>
            ) : null}

            {drawerTab === 'history' ? (
              <section className="module006-editor-section">
                <h4>Update and note history</h4>
                <div className="project-register-timeline module006-history">
                  {selectedUpdates.map((event) => (
                    <article key={event.updateId}>
                      <header><strong>{dateLabel(event.updateDate || event.createdAt)}</strong><span>{event.createdBy || 'Unknown'} · {event.source}</span></header>
                      <p>{event.note || 'No note supplied.'}</p>
                      <small>Status: {event.status || selectedRecord.status || 'Not set'} · Next review: {dateLabel(event.nextReviewDate)} · Recorded: {dateTime(event.createdAt)}</small>
                    </article>
                  ))}
                  {!selectedUpdates.length ? <p className="muted">No update history is available for this project.</p> : null}
                </div>
              </section>
            ) : null}
          </aside>
        </div>
      ) : null}

      {newProjectOpen ? (
        <div className="module006-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setNewProjectOpen(false); }}>
          <section className="module006-modal" role="dialog" aria-modal="true" aria-label="Add new customer pipeline project">
            <header><div><p className="eyebrow">MODULE 006</p><h3>Add New Project</h3><p>Create a standalone pipeline record for any customer.</p></div><button type="button" className="secondary-action" onClick={() => setNewProjectOpen(false)}>Close</button></header>
            <div className="module006-form-grid">
              <label>Project ID <small>Optional; the next P.#### ID is generated when blank.</small><input value={newProjectForm.sourceProjectCode} onChange={(event) => setNewProjectForm((current) => ({ ...current, sourceProjectCode: event.target.value.toUpperCase() }))} /></label>
              <label>Customer<small>Choose an existing customer or type a new customer name.</small><input list="module006-customer-options" maxLength="120" value={newProjectForm.customer} onChange={(event) => setNewProjectForm((current) => ({ ...current, customer: event.target.value }))} /></label>
              <label>Business Unit<input value={newProjectForm.businessUnit} onChange={(event) => setNewProjectForm((current) => ({ ...current, businessUnit: event.target.value }))} /></label>
              <label>USS Owner<input value={newProjectForm.ussOwner} onChange={(event) => setNewProjectForm((current) => ({ ...current, ussOwner: event.target.value }))} /></label>
              <label className="wide">Project Name<input value={newProjectForm.projectName} onChange={(event) => setNewProjectForm((current) => ({ ...current, projectName: event.target.value }))} /></label>
              <label className="wide">Quote Number(s)<input value={newProjectForm.quoteText} onChange={(event) => setNewProjectForm((current) => ({ ...current, quoteText: event.target.value }))} /></label>
              <label>Estimated Value<input type="number" min="0" step="0.01" value={newProjectForm.estimatedValue} onChange={(event) => setNewProjectForm((current) => ({ ...current, estimatedValue: event.target.value }))} /></label>
              <label>Status<select value={newProjectForm.status} onChange={(event) => setNewProjectForm((current) => ({ ...current, status: event.target.value }))}>{PROJECT_STATUSES.map((value) => <option value={value} key={value}>{value}</option>)}</select></label>
              <label>Update Date<input type="date" value={newProjectForm.updateDate} onChange={(event) => setNewProjectForm((current) => ({ ...current, updateDate: event.target.value }))} /></label>
              <label>Next Review Date<input type="date" value={newProjectForm.nextReviewDate} onChange={(event) => setNewProjectForm((current) => ({ ...current, nextReviewDate: event.target.value }))} /></label>
              <label className="wide">Initial Status Note<textarea rows={4} value={newProjectForm.note} onChange={(event) => setNewProjectForm((current) => ({ ...current, note: event.target.value }))} /></label>
            </div>
            <footer><button type="button" className="primary-action" disabled={busy === 'create' || clean(newProjectForm.customer).length < 2 || clean(newProjectForm.projectName).length < 3} onClick={() => void createProject()}>{busy === 'create' ? 'Creating…' : 'Save New Project'}</button><button type="button" className="secondary-action" onClick={() => setNewProjectOpen(false)}>Cancel</button></footer>
          </section>
        </div>
      ) : null}
    </section>
  );
}

function EditableTask({ task, canEdit, busy, onSave, onArchive }) {
  const [form, setForm] = useState({
    title: task.title || '',
    description: task.description || '',
    status: task.status || 'not_started',
    assignedTo: task.assignedTo || '',
    dueDate: dateOnly(task.dueDate),
    note: ''
  });

  useEffect(() => {
    setForm({
      title: task.title || '',
      description: task.description || '',
      status: task.status || 'not_started',
      assignedTo: task.assignedTo || '',
      dueDate: dateOnly(task.dueDate),
      note: ''
    });
  }, [task]);

  return (
    <article className={`module006-task-card ${task.isArchived ? 'archived' : ''}`}>
      <div className="module006-task-heading"><strong>{task.title}</strong><span>{task.isArchived ? 'Archived' : labelize(task.status)}</span></div>
      <div className="module006-form-grid compact">
        <label>Task title<input value={form.title} onChange={(event) => setForm((current) => ({ ...current, title: event.target.value }))} disabled={!canEdit || task.isArchived} /></label>
        <label>Assigned to<input value={form.assignedTo} onChange={(event) => setForm((current) => ({ ...current, assignedTo: event.target.value }))} disabled={!canEdit || task.isArchived} /></label>
        <label>Status<select value={form.status} onChange={(event) => setForm((current) => ({ ...current, status: event.target.value }))} disabled={!canEdit || task.isArchived}>{TASK_STATUSES.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}</select></label>
        <label>Due date<input type="date" value={form.dueDate} onChange={(event) => setForm((current) => ({ ...current, dueDate: event.target.value }))} disabled={!canEdit || task.isArchived} /></label>
        <label className="wide">Description<textarea rows={2} value={form.description} onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))} disabled={!canEdit || task.isArchived} /></label>
        <label className="wide">Update note<textarea rows={2} value={form.note} onChange={(event) => setForm((current) => ({ ...current, note: event.target.value }))} disabled={!canEdit || task.isArchived} /></label>
      </div>
      <footer>
        {!task.isArchived ? <button type="button" className="primary-action" disabled={!canEdit || busy} onClick={() => void onSave(task, form)}>{busy ? 'Saving…' : 'Save Task'}</button> : null}
        <button type="button" className="secondary-action" disabled={!canEdit || busy} onClick={() => void onArchive(task, !task.isArchived)}>{task.isArchived ? 'Restore Task' : 'Archive Task'}</button>
        <small>Revision {task.revision} · Updated {dateTime(task.updatedAt)}</small>
      </footer>
    </article>
  );
}

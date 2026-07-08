import { useEffect, useLayoutEffect, useMemo, useState } from 'react';
import './work-register-center.css';

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
    // Ignore malformed view-as cache.
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
      // Ignore non-JSON responses.
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
      // Ignore non-JSON responses.
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

function hours(value) {
  return Number(value ?? 0).toLocaleString(undefined, {
    maximumFractionDigits: 2
  });
}

function normalize(value) {
  return String(value ?? '').trim().toLowerCase();
}

function labelize(value) {
  return String(value || 'Unknown')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function uniqueValues(items, getter) {
  return [...new Set(items.map(getter).filter(Boolean))]
    .sort((a, b) => String(a).localeCompare(String(b)));
}


function userHasAnyRole(user, roleCodes) {
  const assigned = new Set((user?.roleCodes ?? []).map((roleCode) => String(roleCode).toUpperCase()));
  return roleCodes.some((roleCode) => assigned.has(String(roleCode).toUpperCase()));
}

function activeUsersByRole(users, roleCodes) {
  const activeUsers = users.filter((user) => user.isActive !== false);
  const filtered = activeUsers.filter((user) => userHasAnyRole(user, roleCodes));
  return filtered.length ? filtered : activeUsers;
}

function dateOnly(value) {
  if (!value) return '';
  return String(value).slice(0, 10);
}

export default function WorkRegisterCenter() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [searchTerm, setSearchTerm] = useState('');
  const [lifecycleFilter, setLifecycleFilter] = useState('active');
  const [workTypeFilter, setWorkTypeFilter] = useState('all');
  const [customerFilter, setCustomerFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('all');
  const [personFilter, setPersonFilter] = useState('all');
  const [burnFilter, setBurnFilter] = useState('all');

  const [editFoundation, setEditFoundation] = useState({ loading: true, data: null, error: null });
  const [selectedWorkItem, setSelectedWorkItem] = useState(null);
  const [editForm, setEditForm] = useState({});
  const [editStatus, setEditStatus] = useState('');

  const [activeDrawerTab, setActiveDrawerTab] = useState('setup');
  const [projectDetails, setProjectDetails] = useState({ loading: false, data: null, error: null });

  const [taskAssignmentForms, setTaskAssignmentForms] = useState({});
  const [taskAssignmentStatus, setTaskAssignmentStatus] = useState('');

  const [multiEngineerTasks, setMultiEngineerTasks] = useState({});
  const [taskRosterForms, setTaskRosterForms] = useState({});

  const [changeOrderForm, setChangeOrderForm] = useState({
    enabled: false,
    changeOrderNumber: '',
    title: '',
    changeOrderDate: new Date().toISOString().slice(0, 10),
    approvalReference: '',
    reason: '',
    lines: [
      { lineType: 'pm_normal', description: 'PM normal hours', quantity: 0, unitRate: 190, amount: '', billable: true, utilizationEligible: true },
      { lineType: 'pm_afterhours', description: 'PM after-hours', quantity: 0, unitRate: 285, amount: '', billable: true, utilizationEligible: true },
      { lineType: 'engineering_normal', description: 'Engineering normal hours', quantity: 0, unitRate: 225, amount: '', billable: true, utilizationEligible: true },
      { lineType: 'engineering_afterhours', description: 'Engineering after-hours', quantity: 0, unitRate: 337.5, amount: '', billable: true, utilizationEligible: true },
      { lineType: 'travel', description: 'Travel', quantity: 0, unitRate: 95, amount: '', billable: true, utilizationEligible: true },
      { lineType: 'materials_other', description: 'Materials / other', quantity: 1, unitRate: 0, amount: '', billable: true, utilizationEligible: true }
    ]
  });
  const [changeOrderStatus, setChangeOrderStatus] = useState('');

  const [documentForm, setDocumentForm] = useState({
    documentName: '',
    documentType: 'SOW',
    documentReference: '',
    versionLabel: '',
    visibility: 'project_team',
    effectiveDate: new Date().toISOString().slice(0, 10),
    notes: '',
    reason: ''
  });
  const [documentStatus, setDocumentStatus] = useState('');
  // 055C_9_DOCUMENT_MANAGEMENT

  // 055C_8_CHANGE_ORDER_COSTING

  // 055C_7_MULTI_ENGINEER_ROSTER

  // 055C_6_TASK_ENGINEER_ASSIGNMENT

  // 055C_5_WORK_REGISTER_DETAIL_TABS

  // 055C_2_WORK_REGISTER_EDIT_DRAWER


  useLayoutEffect(() => {
    const focusWorkRegisterRoute = () => {
      const panel = document.getElementById('work-register') || document.querySelector('.work-register-route-panel');
      if (!panel) return;
      const top = Math.max(0, panel.getBoundingClientRect().top + window.scrollY - 12);
      window.scrollTo({ top, behavior: 'auto' });
    };

    focusWorkRegisterRoute();
    const timers = [
      window.setTimeout(focusWorkRegisterRoute, 50),
      window.setTimeout(focusWorkRegisterRoute, 200),
      window.setTimeout(focusWorkRegisterRoute, 600)
    ];

    return () => timers.forEach((timer) => window.clearTimeout(timer));
  }, []);


  async function loadEditFoundation() {
    setEditFoundation((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/work-register/edit-foundation');
      setEditFoundation({ loading: false, data, error: null });
    } catch (error) {
      setEditFoundation({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load edit foundation.'
      });
    }
  }

  async function load() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/work-register/overview');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load Work Register.'
      });
    }
  }

  useEffect(() => {
    load();
    loadEditFoundation();
  }, []);

  const workItems = payload.data?.workItems ?? [];
  const summary = payload.data?.summary ?? {
    total: 0,
    active: 0,
    closed: 0,
    projects: 0,
    intakes: 0
  };

  const peopleOptions = useMemo(() => uniqueValues(workItems, (item) => [
    item.projectManager,
    item.projectCoordinator,
    item.accountExecutive,
    item.solutionArchitect,
    item.insideSales,
    ...(item.assignedEngineers ?? [])
  ].filter(Boolean).join('|')).flatMap((group) => group.split('|')).filter(Boolean).sort(), [workItems]);

  const filteredItems = useMemo(() => {
    const search = normalize(searchTerm);

    return workItems.filter((item) => {
      if (lifecycleFilter !== 'all' && normalize(item.lifecycle) !== lifecycleFilter) return false;
      if (workTypeFilter !== 'all' && normalize(item.workType) !== normalize(workTypeFilter)) return false;
      if (customerFilter !== 'all' && normalize(item.customerName) !== normalize(customerFilter)) return false;
      if (statusFilter !== 'all' && normalize(item.status) !== normalize(statusFilter)) return false;
      if (burnFilter !== 'all' && normalize(item.burnStatus) !== normalize(burnFilter)) return false;

      if (personFilter !== 'all') {
        const people = [
          item.projectManager,
          item.projectCoordinator,
          item.accountExecutive,
          item.solutionArchitect,
          item.insideSales,
          ...(item.assignedEngineers ?? [])
        ].map(normalize);

        if (!people.includes(normalize(personFilter))) return false;
      }

      if (!search) return true;

      const haystack = [
        item.customerName,
        item.workName,
        item.workType,
        item.status,
        item.contractType,
        item.projectManager,
        item.projectCoordinator,
        item.accountExecutive,
        item.solutionArchitect,
        item.insideSales,
        item.sourceTable,
        ...(item.assignedEngineers ?? [])
      ].join(' ').toLowerCase();

      return haystack.includes(search);
    });
  }, [burnFilter, customerFilter, lifecycleFilter, personFilter, searchTerm, statusFilter, workItems, workTypeFilter]);

  const totals = useMemo(() => filteredItems.reduce((accumulator, item) => {
    accumulator.allocatedHours += Number(item.allocatedHours ?? 0);
    accumulator.usedHours += Number(item.usedHours ?? 0);
    accumulator.totalCost += Number(item.totalCost ?? 0);
    accumulator.costUsed += Number(item.costUsed ?? 0);
    accumulator.remainingCost += Number(item.remainingCost ?? 0);
    return accumulator;
  }, {
    allocatedHours: 0,
    usedHours: 0,
    totalCost: 0,
    costUsed: 0,
    remainingCost: 0
  }), [filteredItems]);

  const workTypeOptions = uniqueValues(workItems, (item) => item.workType);
  const customerOptions = uniqueValues(workItems, (item) => item.customerName);
  const statusOptions = uniqueValues(workItems, (item) => item.status);
  const burnOptions = uniqueValues(workItems, (item) => item.burnStatus);


  const canEditWorkRegister = editFoundation.data?.canEditWorkRegister === true;
  const editCustomerOptions = editFoundation.data?.customers ?? [];
  const userOptions = editFoundation.data?.users ?? [];
  const contractOptions = editFoundation.data?.contractTypes ?? [];
  const statusEditOptions = editFoundation.data?.statuses ?? [];

  const pmOptions = activeUsersByRole(userOptions, ['PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD', 'PM_TEAM_LEAD']);
  const pcOptions = activeUsersByRole(userOptions, ['PROJECT_TEAM_COORDINATOR', 'PROJECT_COORDINATOR', 'PROJECT_MANAGEMENT']);
  const aeOptions = activeUsersByRole(userOptions, ['ACCOUNT_EXECUTIVE', 'SALES', 'SALES_EXECUTIVE', 'AE']);
  const saOptions = activeUsersByRole(userOptions, ['SOLUTION_ARCHITECT', 'SA', 'ARCHITECT', 'ANALYST_DEV_ARCHITECT']);
  const saaOptions = activeUsersByRole(userOptions, ['SAA', 'INSIDE_SALES', 'SALES_SUPPORT', 'SALES']);
  const engineerOptions = activeUsersByRole(userOptions, ['ENGINEER', 'ENGINEERING', 'CONSULT_ENGINEER', 'ASSOCIATE_ENGINEER', 'SME_ENGINEER', 'ANALYST_DEV_ARCHITECT']);



  async function loadProjectDetails(item) {
    if (!item?.workId || item.sourceTable !== 'projects') {
      setProjectDetails({ loading: false, data: null, error: 'Detail tabs currently support project records. Intake detail tabs will be added later.' });
      return;
    }

    setProjectDetails({ loading: true, data: null, error: null });

    try {
      const data = await fetchJson(`/api/work-register/projects/${item.workId}/details`);
      setProjectDetails({ loading: false, data, error: null });
    } catch (error) {
      setProjectDetails({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load project details.'
      });
    }
  }

  function openEditDrawer(item) {
    setSelectedWorkItem(item);
    setEditStatus('');
    setActiveDrawerTab('setup');
    setTaskAssignmentForms({});
    setTaskRosterForms({});
    setMultiEngineerTasks({});
    setTaskAssignmentStatus('');
    setChangeOrderStatus('');
    setDocumentStatus('');
    loadProjectDetails(item);
    setEditForm({
      workId: item.workId,
      sourceTable: item.sourceTable,
      clientId: item.customerId || '',
      contractType: item.contractType || '',
      projectManagerUserId: '',
      projectCoordinatorUserId: '',
      accountExecutiveUserId: '',
      solutionArchitectUserId: '',
      insideSalesUserId: '',
      projectStartDate: dateOnly(item.startDate),
      estimatedEndDate: dateOnly(item.estimatedEndDate),
      sowSignedDate: dateOnly(item.sowSignedDate),
      status: item.status || '',
      editReason: ''
    });
  }

  function closeEditDrawer() {
    setSelectedWorkItem(null);
    setEditForm({});
    setEditStatus('');
    setActiveDrawerTab('setup');
    setProjectDetails({ loading: false, data: null, error: null });
    setTaskAssignmentForms({});
    setTaskRosterForms({});
    setMultiEngineerTasks({});
    setTaskAssignmentStatus('');
  }

  function updateEditField(field, value) {
    setEditForm((current) => ({
      ...current,
      [field]: value
    }));
  }



  function isFlexibleTaskClassificationWorkType(workType) {
    const type = normalize(workType);
    return type.includes('internal') ||
      type.includes('pre-sales') ||
      type.includes('presales') ||
      type.includes('non-billable') ||
      type.includes('other');
  }

  function shouldShowTaskClassificationControls(task) {
    const key = taskAssignmentKey(task);
    return isFlexibleTaskClassificationWorkType(selectedWorkItem?.workType) ||
      taskRosterForms[key]?.showClassification === true ||
      taskAssignmentForms[key]?.showClassification === true;
  }

  function projectTaskDefaultBillable() {
    return !isFlexibleTaskClassificationWorkType(selectedWorkItem?.workType);
  }

  function projectTaskDefaultUtilizationEligible() {
    return true;
  }


  function taskAssignmentKey(task) {
    return task?.taskId || task?.taskName || 'unknown-task';
  }

  function taskAssignmentValue(task, field, fallback = '') {
    const key = taskAssignmentKey(task);
    return taskAssignmentForms[key]?.[field] ?? fallback;
  }

  function updateTaskAssignmentForm(task, field, value) {
    const key = taskAssignmentKey(task);
    setTaskAssignmentForms((current) => ({
      ...current,
      [key]: {
        ...(current[key] ?? {}),
        [field]: value
      }
    }));
  }

  async function saveTaskAssignment(task) {
    if (!selectedWorkItem || !task?.taskId) {
      setTaskAssignmentStatus('Task assignment cannot be saved because the task does not have an ID.');
      return;
    }

    if (!canEditWorkRegister) {
      setTaskAssignmentStatus('This tab is view-only for your role. Only Project Team Coordinators, Administrators, and Super Administrators can save task assignments.');
      return;
    }

    const key = taskAssignmentKey(task);
    const form = taskAssignmentForms[key] ?? {};
    const changeReason = String(form.changeReason ?? '').trim();

    if (!changeReason) {
      setTaskAssignmentStatus('Change reason is required before saving a task assignment.');
      return;
    }

    const payload = {
      projectId: selectedWorkItem.workId,
      taskId: task.taskId,
      taskName: task.taskName,
      assignedUserId: form.assignedUserId ?? task.assignedUserId ?? '',
      allocatedHours: form.allocatedHours ?? task.allocatedHours ?? 0,
      billable: form.billable ?? (String(task.billable).toLowerCase() === 'true'),
      utilizationEligible: form.utilizationEligible ?? (String(task.utilizationEligible).toLowerCase() === 'true'),
      effectiveStartDate: form.effectiveStartDate || new Date().toISOString().slice(0, 10),
      changeReason
    };

    setTaskAssignmentStatus(`Saving assignment for ${task.taskName}...`);

    try {
      const result = await postJson('/api/work-register/tasks/assignments/update', payload);
      setTaskAssignmentStatus(result.message || 'Task assignment saved.');
      await loadProjectDetails(selectedWorkItem);
      await load();

      setTaskAssignmentForms((current) => ({
        ...current,
        [key]: {
          ...current[key],
          changeReason: ''
        }
      }));
    } catch (error) {
      setTaskAssignmentStatus(error instanceof Error ? error.message : 'Unable to save task assignment.');
    }
  }



  function initialRosterRows(task) {
    const active = Array.isArray(task?.assignedEngineers) ? task.assignedEngineers : [];

    if (active.length > 0) {
      return active.slice(0, 20).map((engineer, index) => ({
        assignedUserId: engineer.assignedUserId || '',
        allocatedHours: engineer.allocatedHours ?? task.allocatedHours ?? 0,
        allocationPercent: engineer.allocationPercent || '',
        billable: String(engineer.billable ?? task.billable).toLowerCase() === 'true' || projectTaskDefaultBillable(),
        utilizationEligible: String(engineer.utilizationEligible ?? task.utilizationEligible).toLowerCase() === 'true' || projectTaskDefaultUtilizationEligible(),
        effectiveStartDate: engineer.effectiveStartDate ? dateOnly(engineer.effectiveStartDate) : new Date().toISOString().slice(0, 10),
        isPrimary: engineer.isPrimary === true || index === 0
      }));
    }

    return [{
      assignedUserId: task?.assignedUserId || '',
      allocatedHours: task?.allocatedHours ?? 0,
      allocationPercent: '',
      billable: projectTaskDefaultBillable(),
      utilizationEligible: projectTaskDefaultUtilizationEligible(),
      effectiveStartDate: new Date().toISOString().slice(0, 10),
      isPrimary: true
    }];
  }

  function rosterRowsForTask(task) {
    const key = taskAssignmentKey(task);
    return taskRosterForms[key]?.rows ?? initialRosterRows(task);
  }

  function rosterReasonForTask(task) {
    const key = taskAssignmentKey(task);
    return taskRosterForms[key]?.changeReason ?? '';
  }

  function ensureTaskRosterState(task) {
    const key = taskAssignmentKey(task);

    setTaskRosterForms((current) => {
      if (current[key]?.rows) return current;

      return {
        ...current,
        [key]: {
          rows: initialRosterRows(task),
          changeReason: '',
          showClassification: false
        }
      };
    });
  }

  function toggleMultiEngineerTask(task, enabled) {
    const key = taskAssignmentKey(task);

    setMultiEngineerTasks((current) => ({
      ...current,
      [key]: enabled
    }));

    if (enabled) {
      ensureTaskRosterState(task);
    }
  }

  function addRosterEngineer(task) {
    const key = taskAssignmentKey(task);
    const rows = rosterRowsForTask(task);

    if (rows.length >= 20) {
      setTaskAssignmentStatus('A task can have a maximum of 20 active engineers.');
      return;
    }

    setTaskRosterForms((current) => ({
      ...current,
      [key]: {
        ...(current[key] ?? {}),
        rows: [
          ...rows,
          {
            assignedUserId: '',
            allocatedHours: 0,
            allocationPercent: '',
            billable: projectTaskDefaultBillable(),
            utilizationEligible: projectTaskDefaultUtilizationEligible(),
            effectiveStartDate: new Date().toISOString().slice(0, 10),
            isPrimary: rows.length === 0
          }
        ],
        changeReason: current[key]?.changeReason ?? '',
        showClassification: current[key]?.showClassification ?? false
      }
    }));
  }

  function updateRosterEngineer(task, index, field, value) {
    const key = taskAssignmentKey(task);
    const rows = [...rosterRowsForTask(task)];

    rows[index] = {
      ...rows[index],
      [field]: value
    };

    if (field === 'isPrimary' && value === true) {
      rows.forEach((row, rowIndex) => {
        row.isPrimary = rowIndex === index;
      });
    }

    setTaskRosterForms((current) => ({
      ...current,
      [key]: {
        ...(current[key] ?? {}),
        rows,
        changeReason: current[key]?.changeReason ?? '',
        showClassification: current[key]?.showClassification ?? false
      }
    }));
  }

  function removeRosterEngineer(task, index) {
    const key = taskAssignmentKey(task);
    let rows = rosterRowsForTask(task).filter((_, rowIndex) => rowIndex !== index);

    if (rows.length > 0 && !rows.some((row) => row.isPrimary)) {
      rows = rows.map((row, rowIndex) => ({ ...row, isPrimary: rowIndex === 0 }));
    }

    setTaskRosterForms((current) => ({
      ...current,
      [key]: {
        ...(current[key] ?? {}),
        rows,
        changeReason: current[key]?.changeReason ?? '',
        showClassification: current[key]?.showClassification ?? false
      }
    }));
  }

  function updateRosterReason(task, value) {
    const key = taskAssignmentKey(task);
    setTaskRosterForms((current) => ({
      ...current,
      [key]: {
        ...(current[key] ?? {}),
        rows: current[key]?.rows ?? initialRosterRows(task),
        changeReason: value,
        showClassification: current[key]?.showClassification ?? false
      }
    }));
  }

  function toggleRosterClassification(task, enabled) {
    const key = taskAssignmentKey(task);
    setTaskRosterForms((current) => ({
      ...current,
      [key]: {
        ...(current[key] ?? {}),
        rows: current[key]?.rows ?? initialRosterRows(task),
        changeReason: current[key]?.changeReason ?? '',
        showClassification: enabled
      }
    }));
  }

  async function saveTaskRoster(task) {
    if (!selectedWorkItem || !task?.taskId) {
      setTaskAssignmentStatus('Task roster cannot be saved because the task does not have an ID.');
      return;
    }

    if (!canEditWorkRegister) {
      setTaskAssignmentStatus('This tab is view-only for your role. Only Project Team Coordinators, Administrators, and Super Administrators can save task rosters.');
      return;
    }

    const rows = rosterRowsForTask(task)
      .filter((row) => String(row.assignedUserId || '').trim());

    const changeReason = rosterReasonForTask(task).trim();

    if (!changeReason) {
      setTaskAssignmentStatus('Change reason is required before saving a multi-engineer roster.');
      return;
    }

    if (rows.length === 0) {
      setTaskAssignmentStatus('Add at least one engineer before saving the roster.');
      return;
    }

    if (rows.length > 20) {
      setTaskAssignmentStatus('A task can have a maximum of 20 active engineers.');
      return;
    }

    const duplicates = rows
      .map((row) => row.assignedUserId)
      .filter((value, index, list) => list.indexOf(value) !== index);

    if (duplicates.length > 0) {
      setTaskAssignmentStatus('The same engineer cannot be listed more than once on the same active roster.');
      return;
    }

    const percentTotal = rows.reduce((sum, row) => sum + Number(row.allocationPercent || 0), 0);
    if (percentTotal > 100) {
      setTaskAssignmentStatus('Total allocation percentage cannot exceed 100%.');
      return;
    }

    const payload = {
      projectId: selectedWorkItem.workId,
      taskId: task.taskId,
      taskName: task.taskName,
      changeReason,
      effectiveStartDate: rows[0]?.effectiveStartDate || new Date().toISOString().slice(0, 10),
      assignments: rows.map((row, index) => ({
        assignedUserId: row.assignedUserId,
        allocatedHours: Number(row.allocatedHours || 0),
        allocationPercent: row.allocationPercent === '' ? null : Number(row.allocationPercent || 0),
        billable: row.billable !== false,
        utilizationEligible: row.utilizationEligible !== false,
        effectiveStartDate: row.effectiveStartDate || new Date().toISOString().slice(0, 10),
        isPrimary: row.isPrimary === true || index === 0
      }))
    };

    setTaskAssignmentStatus(`Saving ${rows.length} engineer assignment(s) for ${task.taskName}...`);

    try {
      const result = await postJson('/api/work-register/tasks/assignments/roster/save', payload);
      setTaskAssignmentStatus(result.message || 'Multi-engineer roster saved.');
      await loadProjectDetails(selectedWorkItem);
      await load();

      const key = taskAssignmentKey(task);
      setTaskRosterForms((current) => ({
        ...current,
        [key]: {
          ...(current[key] ?? {}),
          changeReason: ''
        }
      }));
    } catch (error) {
      setTaskAssignmentStatus(error instanceof Error ? error.message : 'Unable to save multi-engineer roster.');
    }
  }



  function resetChangeOrderForm() {
    setChangeOrderForm({
      enabled: false,
      changeOrderNumber: '',
      title: '',
      changeOrderDate: new Date().toISOString().slice(0, 10),
      approvalReference: '',
      reason: '',
      lines: [
        { lineType: 'pm_normal', description: 'PM normal hours', quantity: 0, unitRate: 190, amount: '', billable: true, utilizationEligible: true },
        { lineType: 'pm_afterhours', description: 'PM after-hours', quantity: 0, unitRate: 285, amount: '', billable: true, utilizationEligible: true },
        { lineType: 'engineering_normal', description: 'Engineering normal hours', quantity: 0, unitRate: 225, amount: '', billable: true, utilizationEligible: true },
        { lineType: 'engineering_afterhours', description: 'Engineering after-hours', quantity: 0, unitRate: 337.5, amount: '', billable: true, utilizationEligible: true },
        { lineType: 'travel', description: 'Travel', quantity: 0, unitRate: 95, amount: '', billable: true, utilizationEligible: true },
        { lineType: 'materials_other', description: 'Materials / other', quantity: 1, unitRate: 0, amount: '', billable: true, utilizationEligible: true }
      ]
    });
  }

  function changeOrderLineAmount(line) {
    const manualAmount = Number(line.amount || 0);
    if (manualAmount > 0) return manualAmount;

    return Number(line.quantity || 0) * Number(line.unitRate || 0);
  }

  function changeOrderTotal() {
    return changeOrderForm.lines.reduce((sum, line) => sum + changeOrderLineAmount(line), 0);
  }

  function updateChangeOrderField(field, value) {
    setChangeOrderForm((current) => ({
      ...current,
      [field]: value
    }));
  }

  function updateChangeOrderLine(index, field, value) {
    setChangeOrderForm((current) => {
      const lines = [...current.lines];
      lines[index] = {
        ...lines[index],
        [field]: value
      };

      return {
        ...current,
        lines
      };
    });
  }

  async function saveChangeOrder() {
    if (!selectedWorkItem?.workId) {
      setChangeOrderStatus('No project is selected.');
      return;
    }

    if (!canEditWorkRegister) {
      setChangeOrderStatus('This tab is view-only for your role. Only PTC/Admin can save change orders.');
      return;
    }

    if (!changeOrderForm.title.trim()) {
      setChangeOrderStatus('Change order title is required.');
      return;
    }

    if (!changeOrderForm.reason.trim()) {
      setChangeOrderStatus('Reason is required for change order audit history.');
      return;
    }

    const lines = changeOrderForm.lines
      .map((line) => ({
        ...line,
        quantity: Number(line.quantity || 0),
        unitRate: Number(line.unitRate || 0),
        amount: Number(line.amount || 0) > 0 ? Number(line.amount || 0) : changeOrderLineAmount(line)
      }))
      .filter((line) => line.amount > 0);

    if (lines.length === 0) {
      setChangeOrderStatus('Enter at least one PM, engineering, travel, material, or other amount.');
      return;
    }

    const payload = {
      projectId: selectedWorkItem.workId,
      changeOrderNumber: changeOrderForm.changeOrderNumber,
      title: changeOrderForm.title,
      status: 'approved',
      changeOrderDate: changeOrderForm.changeOrderDate || new Date().toISOString().slice(0, 10),
      approvalReference: changeOrderForm.approvalReference,
      reason: changeOrderForm.reason,
      lines
    };

    setChangeOrderStatus(`Saving change order for ${money(changeOrderTotal())}...`);

    try {
      const result = await postJson('/api/work-register/projects/change-orders/save', payload);
      setChangeOrderStatus(result.message || 'Change order saved.');
      await loadProjectDetails(selectedWorkItem);
      await load();
      resetChangeOrderForm();
    } catch (error) {
      setChangeOrderStatus(error instanceof Error ? error.message : 'Unable to save change order.');
    }
  }



  function resetDocumentForm() {
    setDocumentForm({
      documentName: '',
      documentType: 'SOW',
      documentReference: '',
      versionLabel: '',
      visibility: 'project_team',
      effectiveDate: new Date().toISOString().slice(0, 10),
      notes: '',
      reason: ''
    });
  }

  function updateDocumentField(field, value) {
    setDocumentForm((current) => ({
      ...current,
      [field]: value
    }));
  }

  async function saveDocumentRegistration() {
    if (!selectedWorkItem?.workId) {
      setDocumentStatus('No project is selected.');
      return;
    }

    if (!canEditWorkRegister) {
      setDocumentStatus('This tab is view-only for your role. Only PTC/Admin can manage documents.');
      return;
    }

    if (!documentForm.documentName.trim()) {
      setDocumentStatus('Document name is required.');
      return;
    }

    if (!documentForm.documentType.trim()) {
      setDocumentStatus('Document type is required.');
      return;
    }

    if (!documentForm.reason.trim()) {
      setDocumentStatus('Reason is required for document audit history.');
      return;
    }

    const payload = {
      projectId: selectedWorkItem.workId,
      documentName: documentForm.documentName,
      documentType: documentForm.documentType,
      documentReference: documentForm.documentReference,
      versionLabel: documentForm.versionLabel,
      visibility: documentForm.visibility,
      effectiveDate: documentForm.effectiveDate || null,
      notes: documentForm.notes,
      reason: documentForm.reason
    };

    setDocumentStatus(`Saving document ${documentForm.documentName}...`);

    try {
      const result = await postJson('/api/work-register/projects/documents/save', payload);
      setDocumentStatus(result.message || 'Document registered.');
      await loadProjectDetails(selectedWorkItem);
      await load();
      resetDocumentForm();
    } catch (error) {
      setDocumentStatus(error instanceof Error ? error.message : 'Unable to save document.');
    }
  }

  async function archiveWorkRegisterDocument(document) {
    if (!selectedWorkItem?.workId || !document?.documentId) {
      setDocumentStatus('Document cannot be archived because required IDs are missing.');
      return;
    }

    if (!canEditWorkRegister) {
      setDocumentStatus('This tab is view-only for your role. Only PTC/Admin can archive documents.');
      return;
    }

    const reason = window.prompt(`Archive ${document.fileName || 'this document'}? Enter archive reason:`);
    if (!reason || !reason.trim()) {
      setDocumentStatus('Archive cancelled. Reason is required.');
      return;
    }

    setDocumentStatus(`Archiving ${document.fileName || 'document'}...`);

    try {
      const result = await postJson('/api/work-register/projects/documents/archive', {
        projectId: selectedWorkItem.workId,
        documentId: document.documentId,
        reason
      });

      setDocumentStatus(result.message || 'Document archived.');
      await loadProjectDetails(selectedWorkItem);
      await load();
    } catch (error) {
      setDocumentStatus(error instanceof Error ? error.message : 'Unable to archive document.');
    }
  }


  async function saveProjectSetup(event) {
    event.preventDefault();

    if (!selectedWorkItem) return;

    if (!canEditWorkRegister) {
      setEditStatus('This page is view-only for your role. Only Project Team Coordinators, Administrators, and Super Administrators can save changes.');
      return;
    }

    if (!editForm.editReason?.trim()) {
      setEditStatus('Edit reason is required.');
      return;
    }

    const payload = {
      workId: selectedWorkItem.workId,
      sourceTable: selectedWorkItem.sourceTable,
      editReason: editForm.editReason.trim()
    };

    // 055C_3_WORK_REGISTER_CHANGED_FIELD_PAYLOAD_START
    /* 055C_4_CASE_INSENSITIVE_CHANGED_FIELD_START */
    const addIfChanged = (field, originalValue = '') => {
      const nextValue = String(editForm[field] ?? '').trim();
      const priorValue = String(originalValue ?? '').trim();

      if (nextValue && nextValue.toLowerCase() !== priorValue.toLowerCase()) {
        payload[field] = nextValue;
      }
    };
    /* 055C_4_CASE_INSENSITIVE_CHANGED_FIELD_END */

    const addIfSelected = (field) => {
      const nextValue = String(editForm[field] ?? '').trim();

      if (nextValue) {
        payload[field] = nextValue;
      }
    };

    addIfChanged('clientId', selectedWorkItem.customerId || '');
    addIfChanged('contractType', selectedWorkItem.contractType || '');
    addIfChanged('projectStartDate', dateOnly(selectedWorkItem.startDate));
    addIfChanged('estimatedEndDate', dateOnly(selectedWorkItem.estimatedEndDate));
    addIfChanged('sowSignedDate', dateOnly(selectedWorkItem.sowSignedDate));
    addIfChanged('status', selectedWorkItem.status || '');

    // User dropdowns start as blank, meaning "keep current".
    // Send only if PTC/Admin intentionally selects a replacement.
    addIfSelected('projectManagerUserId');
    addIfSelected('projectCoordinatorUserId');
    addIfSelected('accountExecutiveUserId');
    addIfSelected('solutionArchitectUserId');
    addIfSelected('insideSalesUserId');

    if (Object.keys(payload).length <= 3) {
      setEditStatus('No setup changes were selected. Choose at least one field to update.');
      return;
    }
    // 055C_3_WORK_REGISTER_CHANGED_FIELD_PAYLOAD_END

    setEditStatus('Saving project setup...');

    try {
      const result = await postJson('/api/work-register/projects/update', payload);
      setEditStatus(result.message || 'Project setup saved.');
      await load();
      window.setTimeout(() => {
        closeEditDrawer();
      }, 800);
    } catch (error) {
      setEditStatus(error instanceof Error ? error.message : 'Unable to save project setup.');
    }
  }


  return (
    <section className="work-register-center">
      <div className="work-register-header">
        <div>
          <p className="eyebrow">Work Register</p>
          <h2>Active, closed, and historical work</h2>
          <p className="muted">
            Search and filter projects, intakes, tasks, customers, stakeholders, documents, hours, and cost indicators without removing any existing modules.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={load}>
          Refresh
        </button>
      </div>

      {payload.error ? <div className="work-register-banner error">{payload.error}</div> : null}

      <div className="work-register-summary">
        <article>
          <span>Total work</span>
          <strong>{payload.loading ? '...' : summary.total}</strong>
          <small>{filteredItems.length} shown</small>
        </article>
        <article>
          <span>Active</span>
          <strong>{payload.loading ? '...' : summary.active}</strong>
          <small>Open or in-progress</small>
        </article>
        <article>
          <span>Closed / historical</span>
          <strong>{payload.loading ? '...' : summary.closed}</strong>
          <small>Closed, completed, archived, or done</small>
        </article>
        <article>
          <span>Filtered cost</span>
          <strong>{money(totals.totalCost)}</strong>
          <small>{hours(totals.usedHours)} used hours</small>
        </article>
      </div>

      <div className="work-register-toolbar">
        <label className="wide">
          Search
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Search customer, project, PM, engineer, AE, SA, SAA, task..."
          />
        </label>
        <label>
          Lifecycle
          <select value={lifecycleFilter} onChange={(event) => setLifecycleFilter(event.target.value)}>
            <option value="active">Active</option>
            <option value="closed">Closed / historical</option>
            <option value="all">All</option>
          </select>
        </label>
        <label>
          Work type
          <select value={workTypeFilter} onChange={(event) => setWorkTypeFilter(event.target.value)}>
            <option value="all">All work types</option>
            {workTypeOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
        <label>
          Customer
          <select value={customerFilter} onChange={(event) => setCustomerFilter(event.target.value)}>
            <option value="all">All customers</option>
            {customerOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          Status
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
            <option value="all">All statuses</option>
            {statusOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
        <label>
          Person
          <select value={personFilter} onChange={(event) => setPersonFilter(event.target.value)}>
            <option value="all">All people</option>
            {peopleOptions.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          Burn
          <select value={burnFilter} onChange={(event) => setBurnFilter(event.target.value)}>
            <option value="all">All burn states</option>
            {burnOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
          </select>
        </label>
      </div>

      <div className="work-register-totalbar">
        <span><strong>{filteredItems.length}</strong> records</span>
        <span><strong>{hours(totals.allocatedHours)}</strong> allocated hours</span>
        <span><strong>{hours(totals.usedHours)}</strong> used hours</span>
        <span><strong>{money(totals.costUsed)}</strong> cost used</span>
        <span><strong>{money(totals.remainingCost)}</strong> remaining cost</span>
      </div>

      <div className="work-register-table-wrap">
        <table className="work-register-table">
          <thead>
            <tr>
              <th>Customer / Work</th>
              <th>Type / Status</th>
              <th>Stakeholders</th>
              <th>Engineers</th>
              <th>Dates</th>
              <th>Tasks / Docs</th>
              <th>Hours</th>
              <th>Cost / Burn</th>
            </tr>
          </thead>
          <tbody>
            {filteredItems.map((item) => (
              <tr key={`${item.sourceTable}-${item.workId}`}>
                <td>
                  <strong>{item.customerName || 'No customer linked'}</strong>
                  <small>{item.workName}</small>
                  <small>{item.contractType ? `Contract: ${labelize(item.contractType)}` : 'Contract: not set'}</small>

                  <button type="button" className="work-register-row-action" onClick={() => openEditDrawer(item)}>
                    {canEditWorkRegister ? 'Edit work' : 'View details'}
                  </button>
                </td>
                <td>
                  <span className={`work-register-pill lifecycle-${normalize(item.lifecycle)}`}>
                    {labelize(item.lifecycle)}
                  </span>
                  <span className="work-register-pill">{labelize(item.workType)}</span>
                  <small>Status: {labelize(item.status)}</small>
                  <small>Source: {item.sourceTable}</small>
                </td>
                <td>
                  <small>PM: {item.projectManager || 'Not assigned'}</small>
                  <small>PC: {item.projectCoordinator || 'Not assigned'}</small>
                  <small>AE: {item.accountExecutive || 'Not assigned'}</small>
                  <small>SA: {item.solutionArchitect || 'Not assigned'}</small>
                  <small>SAA: {item.insideSales || 'Not assigned'}</small>
                </td>
                <td>
                  {(item.assignedEngineers ?? []).length ? (
                    <div className="engineer-list">
                      {(item.assignedEngineers ?? []).map((engineer) => <span key={engineer}>{engineer}</span>)}
                    </div>
                  ) : (
                    <small>No engineers assigned</small>
                  )}
                </td>
                <td>
                  <small>Start: {item.startDate || 'Not set'}</small>
                  <small>End: {item.estimatedEndDate || 'Not set'}</small>
                  <small>Closed: {item.closedDate || 'Not closed'}</small>
                  <small>SOW signed: {item.sowSignedDate || 'Not set'}</small>
                </td>
                <td>
                  <small>Total tasks: {item.taskCount ?? 0}</small>
                  <small>Open: {item.openTaskCount ?? 0}</small>
                  <small>Closed: {item.closedTaskCount ?? 0}</small>
                  <small>Docs: {item.documentCount ?? 0}</small>
                </td>
                <td>
                  <small>Allocated: {hours(item.allocatedHours)}</small>
                  <small>Used: {hours(item.usedHours)}</small>
                  <small>Eng allocated: {hours(item.engineeringHoursAllocated)}</small>
                  <small>PM allocated: {hours(item.pmHoursAllocated)}</small>
                </td>
                <td>
                  <small>Total: {money(item.totalCost)}</small>
                  <small>Used: {money(item.costUsed)}</small>
                  <small>Remaining: {money(item.remainingCost)}</small>
                  <span className={`burn-pill burn-${normalize(item.burnStatus)}`}>
                    {labelize(item.burnStatus)} {Number(item.burnPercent ?? 0) > 0 ? `${item.burnPercent}%` : ''}
                  </span>
                </td>
              </tr>
            ))}
            {!payload.loading && filteredItems.length === 0 ? (
              <tr>
                <td colSpan="8">No work items match the current filters.</td>
              </tr>
            ) : null}
            {payload.loading ? (
              <tr>
                <td colSpan="8">Loading Work Register...</td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      {selectedWorkItem ? (
        <div className="work-register-drawer-backdrop" role="presentation">
          <aside className="work-register-drawer" aria-label="Work Register project setup editor">
            <div className="work-register-drawer-header">
              <div>
                <p className="eyebrow">{canEditWorkRegister ? 'Edit Project Setup' : 'View Project Setup'}</p>
                <h3>{selectedWorkItem.workName}</h3>
                <p className="muted">
                  {selectedWorkItem.customerName || 'No customer linked'} · {labelize(selectedWorkItem.sourceTable)}
                </p>
              </div>
              <button type="button" className="secondary-action" onClick={closeEditDrawer}>Close</button>
            </div>

            <div className={canEditWorkRegister ? 'work-register-edit-notice allowed' : 'work-register-edit-notice'}>
              {canEditWorkRegister
                ? 'Project Team Coordinator/Admin edit mode. All saves require a reason and are audited.'
                : 'View-only mode. Solution Architects, PMs, Engineers, Sales, and SAA cannot edit Work Register setup fields.'}
            </div>

            {editFoundation.error ? (
              <div className="work-register-banner error">{editFoundation.error}</div>
            ) : null}

            {editStatus ? (
              <div className="work-register-banner">{editStatus}</div>
            ) : null}

            {/* 055C_5_WORK_REGISTER_DETAIL_TABS_START */}
            <div className="work-register-drawer-tabs" role="tablist" aria-label="Work detail sections">
              <button type="button" className={activeDrawerTab === 'setup' ? 'active' : ''} onClick={() => setActiveDrawerTab('setup')}>Setup</button>
              <button type="button" className={activeDrawerTab === 'tasks' ? 'active' : ''} onClick={() => setActiveDrawerTab('tasks')}>Tasks & Engineers</button>
              <button type="button" className={activeDrawerTab === 'costing' ? 'active' : ''} onClick={() => setActiveDrawerTab('costing')}>Costing / Change Orders</button>
              <button type="button" className={activeDrawerTab === 'documents' ? 'active' : ''} onClick={() => setActiveDrawerTab('documents')}>Documents</button>
              <button type="button" className={activeDrawerTab === 'audit' ? 'active' : ''} onClick={() => setActiveDrawerTab('audit')}>Audit</button>
            </div>
            {/* 055C_5_WORK_REGISTER_DETAIL_TABS_END */}

            {projectDetails.error ? (
              <div className="work-register-banner error">{projectDetails.error}</div>
            ) : null}

            {projectDetails.loading ? (
              <div className="work-register-banner">Loading project detail tabs...</div>
            ) : null}

            {activeDrawerTab === 'setup' ? (
            <form className="work-register-edit-form" onSubmit={saveProjectSetup}>
              <label>
                Customer
                <select
                  value={editForm.clientId || ''}
                  onChange={(event) => updateEditField('clientId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current / not linked</option>
                  {editCustomerOptions.map((customer) => (
                    <option value={customer.clientId} key={customer.clientId}>
                      {customer.clientName} {customer.clientCode ? `(${customer.clientCode})` : ''}{customer.isActive === false ? ' - inactive' : ''}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Contract type
                <select
                  value={editForm.contractType || ''}
                  onChange={(event) => updateEditField('contractType', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current / not set</option>
                  {contractOptions.map((value) => <option value={value} key={value}>{value}</option>)}
                </select>
              </label>

              <label>
                Project Manager
                <select
                  value={editForm.projectManagerUserId || ''}
                  onChange={(event) => updateEditField('projectManagerUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.projectManager || 'Not assigned'}</option>
                  {pmOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Project Coordinator
                <select
                  value={editForm.projectCoordinatorUserId || ''}
                  onChange={(event) => updateEditField('projectCoordinatorUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.projectCoordinator || 'Not assigned'}</option>
                  {pcOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Account Executive / AE
                <select
                  value={editForm.accountExecutiveUserId || ''}
                  onChange={(event) => updateEditField('accountExecutiveUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.accountExecutive || 'Not assigned'}</option>
                  {aeOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Solution Architect / SA
                <select
                  value={editForm.solutionArchitectUserId || ''}
                  onChange={(event) => updateEditField('solutionArchitectUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.solutionArchitect || 'Not assigned'}</option>
                  {saOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Inside Sales / SAA
                <select
                  value={editForm.insideSalesUserId || ''}
                  onChange={(event) => updateEditField('insideSalesUserId', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current: {selectedWorkItem.insideSales || 'Not assigned'}</option>
                  {saaOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Status
                <select
                  value={editForm.status || ''}
                  onChange={(event) => updateEditField('status', event.target.value)}
                  disabled={!canEditWorkRegister}
                >
                  <option value="">Keep current / not set</option>
                  {statusEditOptions.map((value) => <option value={value} key={value}>{labelize(value)}</option>)}
                </select>
              </label>

              <label>
                Project start date
                <input
                  type="date"
                  value={editForm.projectStartDate || ''}
                  onChange={(event) => updateEditField('projectStartDate', event.target.value)}
                  disabled={!canEditWorkRegister}
                />
              </label>

              <label>
                Estimated end date
                <input
                  type="date"
                  value={editForm.estimatedEndDate || ''}
                  onChange={(event) => updateEditField('estimatedEndDate', event.target.value)}
                  disabled={!canEditWorkRegister}
                />
              </label>

              <label>
                SOW signed date
                <input
                  type="date"
                  value={editForm.sowSignedDate || ''}
                  onChange={(event) => updateEditField('sowSignedDate', event.target.value)}
                  disabled={!canEditWorkRegister}
                />
              </label>

              <label className="full-width">
                Edit reason
                <textarea
                  rows={3}
                  value={editForm.editReason || ''}
                  onChange={(event) => updateEditField('editReason', event.target.value)}
                  placeholder="Required. Example: Reassigned PM because prior PM left organization."
                  disabled={!canEditWorkRegister}
                />
              </label>

              <div className="work-register-drawer-actions">
                {canEditWorkRegister ? (
                  <button type="submit" className="primary-action">Save changes</button>
                ) : null}
                <button type="button" className="secondary-action" onClick={closeEditDrawer}>Cancel</button>
              </div>

            </form>
            ) : null}

            {activeDrawerTab === 'tasks' ? (
              <div className="work-register-detail-panel">
                <h4>Tasks & Engineer Assignments</h4>
                <p className="muted">
                  Simple mode keeps one engineer per task. Select “Multiple engineers on this task” only when the task needs an advanced roster.
                  Normal project tasks default to Billable + Utilization eligible.
                </p>

                {taskAssignmentStatus ? (
                  <div className="work-register-banner">{taskAssignmentStatus}</div>
                ) : null}

                <div className="work-register-task-assignment-list">
                  {(projectDetails.data?.tasks ?? []).map((task) => {
                    const key = taskAssignmentKey(task);
                    const selectedEngineerId = taskAssignmentValue(task, 'assignedUserId', task.assignedUserId || '');
                    const selectedAllocatedHours = taskAssignmentValue(task, 'allocatedHours', task.allocatedHours ?? 0);
                    const selectedBillable = taskAssignmentValue(task, 'billable', String(task.billable).toLowerCase() === 'true' || projectTaskDefaultBillable());
                    const selectedUtilization = taskAssignmentValue(task, 'utilizationEligible', String(task.utilizationEligible).toLowerCase() === 'true' || projectTaskDefaultUtilizationEligible());
                    const selectedEffectiveDate = taskAssignmentValue(task, 'effectiveStartDate', new Date().toISOString().slice(0, 10));
                    const selectedReason = taskAssignmentValue(task, 'changeReason', '');
                    const isMultiEngineer = multiEngineerTasks[key] === true || Number(task.activeEngineerCount || 0) > 1;
                    const rosterRows = rosterRowsForTask(task);
                    const showClassification = shouldShowTaskClassificationControls(task);

                    return (
                      <article className="work-register-task-assignment-card" key={key}>
                        <div>
                          <strong>{task.taskName}</strong>
                          <small>Status: {labelize(task.status || 'not set')}</small>
                          <small>Current engineer: {task.assignedUserName || 'Not assigned'}</small>
                          <small>Active engineers: {task.activeEngineerCount || rosterRows.filter((row) => row.assignedUserId).length || 0} / 20</small>
                          <small>Assignment source: {labelize(task.assignmentSource || 'project_tasks')}</small>
                        </div>

                        <label className="checkbox-line full-width">
                          <input
                            type="checkbox"
                            checked={isMultiEngineer}
                            onChange={(event) => toggleMultiEngineerTask(task, event.target.checked)}
                            disabled={!canEditWorkRegister || !task.taskId}
                          />
                          Multiple engineers on this task
                        </label>

                        {!isMultiEngineer ? (
                          <>
                            <label>
                              Engineer
                              <select
                                value={selectedEngineerId}
                                onChange={(event) => updateTaskAssignmentForm(task, 'assignedUserId', event.target.value)}
                                disabled={!canEditWorkRegister || !task.taskId}
                              >
                                <option value="">Unassigned / remove future assignment</option>
                                {engineerOptions.map((user) => (
                                  <option value={user.userId} key={user.userId}>
                                    {user.displayName} {user.isActive === false ? '- inactive' : ''}
                                  </option>
                                ))}
                              </select>
                            </label>

                            <label>
                              Allocated hours
                              <input
                                type="number"
                                min="0"
                                step="0.25"
                                value={selectedAllocatedHours}
                                onChange={(event) => updateTaskAssignmentForm(task, 'allocatedHours', event.target.value)}
                                disabled={!canEditWorkRegister || !task.taskId}
                              />
                            </label>

                            <label>
                              Effective date
                              <input
                                type="date"
                                value={selectedEffectiveDate}
                                onChange={(event) => updateTaskAssignmentForm(task, 'effectiveStartDate', event.target.value)}
                                disabled={!canEditWorkRegister || !task.taskId}
                              />
                            </label>

                            {showClassification ? (
                              <>
                                <label className="checkbox-line">
                                  <input
                                    type="checkbox"
                                    checked={selectedBillable}
                                    onChange={(event) => updateTaskAssignmentForm(task, 'billable', event.target.checked)}
                                    disabled={!canEditWorkRegister || !task.taskId}
                                  />
                                  Billable
                                </label>

                                <label className="checkbox-line">
                                  <input
                                    type="checkbox"
                                    checked={selectedUtilization}
                                    onChange={(event) => updateTaskAssignmentForm(task, 'utilizationEligible', event.target.checked)}
                                    disabled={!canEditWorkRegister || !task.taskId}
                                  />
                                  Utilization eligible
                                </label>
                              </>
                            ) : (
                              <div className="work-register-default-classification">
                                Default: Billable + Utilization eligible
                                <button type="button" onClick={() => updateTaskAssignmentForm(task, 'showClassification', true)} disabled={!canEditWorkRegister}>
                                  Override classification
                                </button>
                              </div>
                            )}

                            <label className="full-width">
                              Change reason
                              <textarea
                                rows={2}
                                value={selectedReason}
                                onChange={(event) => updateTaskAssignmentForm(task, 'changeReason', event.target.value)}
                                placeholder="Required. Example: Reassigned future task work because previous engineer left the organization."
                                disabled={!canEditWorkRegister || !task.taskId}
                              />
                            </label>

                            <div className="work-register-task-assignment-actions">
                              {canEditWorkRegister ? (
                                <button type="button" className="primary-action" onClick={() => saveTaskAssignment(task)} disabled={!task.taskId}>
                                  Save task assignment
                                </button>
                              ) : null}
                            </div>
                          </>
                        ) : (
                          <div className="work-register-roster-panel full-width">
                            <div className="work-register-roster-toolbar">
                              <strong>Multi-engineer roster</strong>
                              <span>{rosterRows.filter((row) => row.assignedUserId).length} / 20 active engineers</span>
                              {canEditWorkRegister ? (
                                <button type="button" className="secondary-action" onClick={() => addRosterEngineer(task)}>
                                  Add engineer
                                </button>
                              ) : null}
                            </div>

                            {!isFlexibleTaskClassificationWorkType(selectedWorkItem?.workType) ? (
                              <div className="work-register-default-classification">
                                Project task default: Billable + Utilization eligible.
                                <button type="button" onClick={() => toggleRosterClassification(task, true)} disabled={!canEditWorkRegister}>
                                  Override classification
                                </button>
                              </div>
                            ) : null}

                            {rosterRows.map((row, index) => (
                              <div className="work-register-roster-row" key={`${key}-roster-${index}`}>
                                <label>
                                  Engineer
                                  <select
                                    value={row.assignedUserId || ''}
                                    onChange={(event) => updateRosterEngineer(task, index, 'assignedUserId', event.target.value)}
                                    disabled={!canEditWorkRegister || !task.taskId}
                                  >
                                    <option value="">Select engineer</option>
                                    {engineerOptions.map((user) => (
                                      <option value={user.userId} key={user.userId}>
                                        {user.displayName} {user.isActive === false ? '- inactive' : ''}
                                      </option>
                                    ))}
                                  </select>
                                </label>

                                <label>
                                  Hours
                                  <input
                                    type="number"
                                    min="0"
                                    step="0.25"
                                    value={row.allocatedHours ?? 0}
                                    onChange={(event) => updateRosterEngineer(task, index, 'allocatedHours', event.target.value)}
                                    disabled={!canEditWorkRegister || !task.taskId}
                                  />
                                </label>

                                <label>
                                  Allocation %
                                  <input
                                    type="number"
                                    min="0"
                                    max="100"
                                    step="0.01"
                                    value={row.allocationPercent ?? ''}
                                    onChange={(event) => updateRosterEngineer(task, index, 'allocationPercent', event.target.value)}
                                    disabled={!canEditWorkRegister || !task.taskId}
                                  />
                                </label>

                                <label>
                                  Effective date
                                  <input
                                    type="date"
                                    value={row.effectiveStartDate || new Date().toISOString().slice(0, 10)}
                                    onChange={(event) => updateRosterEngineer(task, index, 'effectiveStartDate', event.target.value)}
                                    disabled={!canEditWorkRegister || !task.taskId}
                                  />
                                </label>

                                <label className="checkbox-line">
                                  <input
                                    type="checkbox"
                                    checked={row.isPrimary === true}
                                    onChange={(event) => updateRosterEngineer(task, index, 'isPrimary', event.target.checked)}
                                    disabled={!canEditWorkRegister || !task.taskId}
                                  />
                                  Primary
                                </label>

                                {(isFlexibleTaskClassificationWorkType(selectedWorkItem?.workType) || taskRosterForms[key]?.showClassification === true) ? (
                                  <>
                                    <label className="checkbox-line">
                                      <input
                                        type="checkbox"
                                        checked={row.billable !== false}
                                        onChange={(event) => updateRosterEngineer(task, index, 'billable', event.target.checked)}
                                        disabled={!canEditWorkRegister || !task.taskId}
                                      />
                                      Billable
                                    </label>

                                    <label className="checkbox-line">
                                      <input
                                        type="checkbox"
                                        checked={row.utilizationEligible !== false}
                                        onChange={(event) => updateRosterEngineer(task, index, 'utilizationEligible', event.target.checked)}
                                        disabled={!canEditWorkRegister || !task.taskId}
                                      />
                                      Utilization
                                    </label>
                                  </>
                                ) : null}

                                <button
                                  type="button"
                                  className="secondary-action danger"
                                  onClick={() => removeRosterEngineer(task, index)}
                                  disabled={!canEditWorkRegister || rosterRows.length <= 1}
                                >
                                  Remove row
                                </button>
                              </div>
                            ))}

                            <label className="full-width">
                              Roster change reason
                              <textarea
                                rows={2}
                                value={rosterReasonForTask(task)}
                                onChange={(event) => updateRosterReason(task, event.target.value)}
                                placeholder="Required. Example: Added engineers for migration weekend coverage."
                                disabled={!canEditWorkRegister || !task.taskId}
                              />
                            </label>

                            <div className="work-register-task-assignment-actions">
                              {canEditWorkRegister ? (
                                <button type="button" className="primary-action" onClick={() => saveTaskRoster(task)} disabled={!task.taskId}>
                                  Save multi-engineer roster
                                </button>
                              ) : null}
                            </div>
                          </div>
                        )}
                      </article>
                    );
                  })}

                  {projectDetails.data && (projectDetails.data.tasks ?? []).length === 0 ? (
                    <p className="muted">No project tasks were found for this work item.</p>
                  ) : null}
                </div>
              </div>
            ) : null}

            {activeDrawerTab === 'costing' ? (
              <div className="work-register-detail-panel">
                <h4>Costing / Change Orders</h4>
                <p className="muted">
                  Change orders are added as revisions. The original project baseline remains preserved, and each change order writes to the audit trail.
                </p>

                {changeOrderStatus ? (
                  <div className="work-register-banner">{changeOrderStatus}</div>
                ) : null}

                <div className="work-register-detail-summary">
                  <span>Total hours used: <strong>{hours(projectDetails.data?.summary?.totalHours ?? selectedWorkItem.usedHours)}</strong></span>
                  <span>Current total cost: <strong>{money(selectedWorkItem.totalCost)}</strong></span>
                  <span>Approved change orders: <strong>{money(projectDetails.data?.costingSummary?.changeOrderTotal ?? 0)}</strong></span>
                  <span>Known total with change orders: <strong>{money((Number(selectedWorkItem.totalCost || 0)) + Number(projectDetails.data?.costingSummary?.changeOrderTotal ?? 0))}</strong></span>
                </div>

                <label className="checkbox-line work-register-change-order-toggle">
                  <input
                    type="checkbox"
                    checked={changeOrderForm.enabled}
                    onChange={(event) => updateChangeOrderField('enabled', event.target.checked)}
                    disabled={!canEditWorkRegister}
                  />
                  This project has a change order
                </label>

                {changeOrderForm.enabled ? (
                  <div className="work-register-change-order-form">
                    <div className="work-register-edit-grid">
                      <label>
                        Change order number
                        <input
                          type="text"
                          value={changeOrderForm.changeOrderNumber}
                          onChange={(event) => updateChangeOrderField('changeOrderNumber', event.target.value)}
                          placeholder="CO-001"
                        />
                      </label>

                      <label>
                        Change order title
                        <input
                          type="text"
                          value={changeOrderForm.title}
                          onChange={(event) => updateChangeOrderField('title', event.target.value)}
                          placeholder="Additional engineering hours"
                        />
                      </label>

                      <label>
                        Change order date
                        <input
                          type="date"
                          value={changeOrderForm.changeOrderDate}
                          onChange={(event) => updateChangeOrderField('changeOrderDate', event.target.value)}
                        />
                      </label>

                      <label>
                        Approval/reference
                        <input
                          type="text"
                          value={changeOrderForm.approvalReference}
                          onChange={(event) => updateChangeOrderField('approvalReference', event.target.value)}
                          placeholder="Customer email, PO, signed CO, ticket, etc."
                        />
                      </label>
                    </div>

                    <div className="work-register-change-order-lines">
                      {changeOrderForm.lines.map((line, index) => (
                        <article key={`${line.lineType}-${index}`} className="work-register-change-order-line">
                          <strong>{line.description}</strong>

                          <label>
                            Hours / Qty
                            <input
                              type="number"
                              min="0"
                              step="0.25"
                              value={line.quantity}
                              onChange={(event) => updateChangeOrderLine(index, 'quantity', event.target.value)}
                            />
                          </label>

                          <label>
                            Rate
                            <input
                              type="number"
                              min="0"
                              step="0.01"
                              value={line.unitRate}
                              onChange={(event) => updateChangeOrderLine(index, 'unitRate', event.target.value)}
                            />
                          </label>

                          <label>
                            Manual amount
                            <input
                              type="number"
                              min="0"
                              step="0.01"
                              value={line.amount}
                              onChange={(event) => updateChangeOrderLine(index, 'amount', event.target.value)}
                              placeholder="Optional"
                            />
                          </label>

                          <span>{money(changeOrderLineAmount(line))}</span>
                        </article>
                      ))}
                    </div>

                    <div className="work-register-change-order-total">
                      <span>Change order total</span>
                      <strong>{money(changeOrderTotal())}</strong>
                    </div>

                    <label className="full-width">
                      Reason / audit note
                      <textarea
                        rows={3}
                        value={changeOrderForm.reason}
                        onChange={(event) => updateChangeOrderField('reason', event.target.value)}
                        placeholder="Required. Example: Customer approved additional engineering and PM hours for scope expansion."
                      />
                    </label>

                    <div className="work-register-task-assignment-actions">
                      <button type="button" className="primary-action" onClick={saveChangeOrder} disabled={!canEditWorkRegister}>
                        Save change order
                      </button>
                    </div>
                  </div>
                ) : null}

                <h5>Existing change orders</h5>
                <div className="work-register-detail-grid">
                  {(projectDetails.data?.changeOrders ?? []).map((order) => (
                    <article key={order.changeOrderId || order.title}>
                      <strong>{order.changeOrderNumber ? `${order.changeOrderNumber} - ${order.title}` : order.title}</strong>
                      <small>Status: {labelize(order.status || 'approved')}</small>
                      <small>Date: {order.changeOrderDate || 'not set'}</small>
                      <small>Total: {money(order.totalAmount)}</small>
                      <small>Reference: {order.approvalReference || 'not set'}</small>
                    </article>
                  ))}
                  {projectDetails.data && (projectDetails.data.changeOrders ?? []).length === 0 ? (
                    <p className="muted">No change orders have been entered for this work item yet.</p>
                  ) : null}
                </div>

                <h5>Time by person</h5>
                <div className="work-register-detail-grid">
                  {(projectDetails.data?.timeSummary ?? []).map((item) => (
                    <article key={item.userName}>
                      <strong>{item.userName}</strong>
                      <small>{hours(item.hours)} hours</small>
                    </article>
                  ))}
                  {projectDetails.data && (projectDetails.data.timeSummary ?? []).length === 0 ? (
                    <p className="muted">No time entries were found for this work item.</p>
                  ) : null}
                </div>
              </div>
            ) : null}

            {activeDrawerTab === 'documents' ? (
              <div className="work-register-detail-panel">
                <h4>Documents</h4>
                <p className="muted">
                  Register or link project documents from Work Register. This preserves the document history and writes document changes to the audit tab.
                </p>

                {documentStatus ? (
                  <div className="work-register-banner">{documentStatus}</div>
                ) : null}

                {canEditWorkRegister ? (
                  <div className="work-register-document-form">
                    <h5>Add / link document</h5>

                    <div className="work-register-edit-grid">
                      <label>
                        Document name
                        <input
                          type="text"
                          value={documentForm.documentName}
                          onChange={(event) => updateDocumentField('documentName', event.target.value)}
                          placeholder="Signed SOW, GSD, CO-001 approval, etc."
                        />
                      </label>

                      <label>
                        Document type
                        <select
                          value={documentForm.documentType}
                          onChange={(event) => updateDocumentField('documentType', event.target.value)}
                        >
                          {['SOW', 'GSD', 'Change Order', 'Customer Approval', 'Project Plan', 'Technical Document', 'Closeout', 'Other'].map((type) => (
                            <option value={type} key={type}>{type}</option>
                          ))}
                        </select>
                      </label>

                      <label>
                        Document reference / link
                        <input
                          type="text"
                          value={documentForm.documentReference}
                          onChange={(event) => updateDocumentField('documentReference', event.target.value)}
                          placeholder="SharePoint/Drive URL, file path, ticket link, or document reference"
                        />
                      </label>

                      <label>
                        Version
                        <input
                          type="text"
                          value={documentForm.versionLabel}
                          onChange={(event) => updateDocumentField('versionLabel', event.target.value)}
                          placeholder="v1, signed, final, rev-2"
                        />
                      </label>

                      <label>
                        Visibility
                        <select
                          value={documentForm.visibility}
                          onChange={(event) => updateDocumentField('visibility', event.target.value)}
                        >
                          <option value="project_team">Project team</option>
                          <option value="ptc_admin_only">PTC/Admin only</option>
                          <option value="pm_ptc_admin">PM/PTC/Admin</option>
                          <option value="engineering_team">Engineering team</option>
                        </select>
                      </label>

                      <label>
                        Effective date
                        <input
                          type="date"
                          value={documentForm.effectiveDate}
                          onChange={(event) => updateDocumentField('effectiveDate', event.target.value)}
                        />
                      </label>
                    </div>

                    <label className="full-width">
                      Notes
                      <textarea
                        rows={2}
                        value={documentForm.notes}
                        onChange={(event) => updateDocumentField('notes', event.target.value)}
                        placeholder="Optional context for this document."
                      />
                    </label>

                    <label className="full-width">
                      Reason / audit note
                      <textarea
                        rows={2}
                        value={documentForm.reason}
                        onChange={(event) => updateDocumentField('reason', event.target.value)}
                        placeholder="Required. Example: Added customer-approved CO-001 supporting document."
                      />
                    </label>

                    <div className="work-register-task-assignment-actions">
                      <button type="button" className="primary-action" onClick={saveDocumentRegistration}>
                        Save document
                      </button>
                    </div>
                  </div>
                ) : (
                  <p className="muted">Document management is view-only for this role.</p>
                )}

                <h5>Registered documents</h5>
                <div className="work-register-document-list">
                  {(projectDetails.data?.documents ?? []).map((document) => (
                    <article className={`work-register-document-card ${String(document.status || '').toLowerCase() === 'archived' ? 'archived' : ''}`} key={`${document.sourceTable}-${document.documentId || document.fileName}`}>
                      <div>
                        <strong>{document.fileName || 'Untitled document'}</strong>
                        <small>Type: {labelize(document.documentType || 'not set')}</small>
                        <small>Status: {labelize(document.status || 'active')}</small>
                        <small>Visibility: {labelize(document.visibility || 'not set')}</small>
                        <small>Version: {document.versionLabel || 'not set'}</small>
                        <small>Effective: {document.effectiveDate || 'not set'}</small>
                        <small>Added: {document.uploadedAt || 'not set'}</small>
                        {document.notes ? <small>Notes: {document.notes}</small> : null}
                        {document.archiveReason ? <small>Archive reason: {document.archiveReason}</small> : null}
                      </div>

                      <div className="work-register-document-actions">
                        {document.documentReference ? (
                          <a href={document.documentReference} target="_blank" rel="noreferrer">
                            Open reference
                          </a>
                        ) : null}

                        {canEditWorkRegister && document.canArchive ? (
                          <button type="button" className="secondary-action danger" onClick={() => archiveWorkRegisterDocument(document)}>
                            Archive
                          </button>
                        ) : null}
                      </div>
                    </article>
                  ))}

                  {projectDetails.data && (projectDetails.data.documents ?? []).length === 0 ? (
                    <p className="muted">No project documents were found for this work item.</p>
                  ) : null}
                </div>
              </div>
            ) : null}

            {activeDrawerTab === 'audit' ? (
              <div className="work-register-detail-panel">
                <h4>Audit History</h4>
                <p className="muted">Project setup edits are written to the Work Register audit sidecar table.</p>
                <div className="work-register-detail-grid">
                  {(projectDetails.data?.changeHistory ?? []).map((event, index) => (
                    <article key={`${event.changedAt}-${index}`}>
                      <strong>{labelize(event.action || 'change')}</strong>
                      <small>{event.changeSummary || 'No summary provided'}</small>
                      <small>Fields: {event.changedFields || 'not listed'}</small>
                      <small>By: {event.changedBy || 'Unknown'} · {event.changedAt || 'not set'}</small>
                    </article>
                  ))}
                  {projectDetails.data && (projectDetails.data.changeHistory ?? []).length === 0 ? (
                    <p className="muted">No Work Register audit events were found for this work item yet.</p>
                  ) : null}
                </div>
              </div>
            ) : null}

          </aside>
        </div>
      ) : null}

    </section>
  );
}

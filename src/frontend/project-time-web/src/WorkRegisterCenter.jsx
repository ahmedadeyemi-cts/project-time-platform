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

async function putJson(url, payload) {
  const response = await fetch(url, {
    method: 'PUT',
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


function workRegisterBillingIdentifierValue(item = {}, camelName, snakeName) {
  return item?.[camelName] || item?.[snakeName] || '';
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

export default function WorkRegisterCenter({ mode = 'edit' }) {
  const isCreateMode = mode === 'create';
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
  const [purchaseOrders, setPurchaseOrders] = useState({ loading: true, projects: [], error: null });
  const [purchaseOrderStatus, setPurchaseOrderStatus] = useState('');

  const [activeDrawerTab, setActiveDrawerTab] = useState('setup');
  const [projectDetails, setProjectDetails] = useState({ loading: false, data: null, error: null });

  const [taskAssignmentForms, setTaskAssignmentForms] = useState({});
  const [taskAssignmentStatus, setTaskAssignmentStatus] = useState('');
  const [intakeSaveBanner, setIntakeSaveBanner] = useState('');

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
    reason: '',
    purchaseOrderRequired: false,
    poNumber: '',
    authorizedAmount: '',
    poEffectiveStartDate: '',
    poEffectiveEndDate: '',
    poCustomerReference: ''
  });
  const [documentStatus, setDocumentStatus] = useState('');

  const [documentUploadStatus, setDocumentUploadStatus] = useState('');

  const [intakeWizardOpen, setIntakeWizardOpen] = useState(isCreateMode);
  const [intakeWizardStatus, setIntakeWizardStatus] = useState('');
  const [intakePackageResult, setIntakePackageResult] = useState(null);

  const [intakePackages, setIntakePackages] = useState([]);
  const [currentIntakePackageId, setCurrentIntakePackageId] = useState(() => sessionStorage.getItem('projectPulseCurrentIntakePackageId') || '');
  const [intakeReviewStatus, setIntakeReviewStatus] = useState('');
  const [selectedIntakeReview, setSelectedIntakeReview] = useState(null);
  const [intakeReviewForm, setIntakeReviewForm] = useState(null);
  // 055D_2_GSD_EXTRACTION_REVIEW
  // 055D_2A_GSD_XLSX_EXTRACTION_REVIEW
  // 055D_2B_INTAKE_UI_REPAIR
  // 055D_2C_CURRENT_EXTRACTION_REPAIR
  // 055D_2E_UPLOAD_EXTRACT_CURRENT_GSD
  // 055D_2F_RATE_TASK_REVIEW_TABLES
  // 055D_2G_CONSOLIDATED_GSD_PARSER
  // 055D_2H_HYUNDAI_TOYOTA_RATE_TASK_REPAIR
  // 055D_2I_TOYOTA_HYUNDAI_TOTALS_PRICING
  // 055D_2I_REPAIRED_TOTALS_ROLLUP_SKU_MAPPING
  // 055D_3_INTAKE_ASSIGNMENT_REVIEW
  // 055D_3B_ROLE_BASED_ASSIGNMENT_POOLS
  // 055D_3A_ASSIGNMENT_POOL_REPAIR

  const [intakeForm, setIntakeForm] = useState({
    sourceMode: 'gsd',
    sellRecordId: '',
    requestedWorkType: 'Project',
    contractType: 'Fixed Price',
    customerId: '',
    projectNameHint: '',
    skipGsd: false,
    skipSow: false,
    notes: '',
    reason: '',
    purchaseOrderRequired: false,
    poNumber: '',
    authorizedAmount: '',
    poEffectiveStartDate: '',
    poEffectiveEndDate: '',
    poCustomerReference: ''
  });

  // 055D_5B_CREATE_WORK_SAFE_INTAKE_FORM_UPDATE

// 055D_5L_CREATE_WORK_FIELD_SNAPSHOT
function projectPulseCreateWorkSnapshotRead() {
  try {
    return JSON.parse(sessionStorage.getItem('projectPulseCreateWorkFinalFields') || '{}') || {};
  } catch {
    return {};
  }
}

function projectPulseCreateWorkSnapshotWrite(field, value) {
  const tracked = new Set([
    'requestedWorkType',
    'contractType',
    'sellQuoteNumber',
    'salesforceIdNumber',
    'certiniaIdNumber',
    'purchaseOrderRequired',
    'poNumber',
    'authorizedAmount',
    'poEffectiveStartDate',
    'poEffectiveEndDate',
    'poCustomerReference',
    'sowSignedDate',
    'customerId',
    'customerName',
    'projectName',
    'workName'
  ]);

  if (!tracked.has(field)) {
    return;
  }

  const current = projectPulseCreateWorkSnapshotRead();
  current[field] = value ?? '';
  sessionStorage.setItem('projectPulseCreateWorkFinalFields', JSON.stringify(current));
}

function projectPulseCanonicalWorkType(value) {
  const normalized = String(value || '').trim().toLowerCase().replace(/[^a-z0-9]+/g, '');

  if (normalized === 'project') return 'Project';
  if (normalized === 'iqs') return 'IQS';
  if (normalized === 'servicerequest' || normalized === 'sr') return 'Service Request';
  if (normalized === 'presales' || normalized === 'presale') return 'Pre-sales';
  if (normalized === 'internalproject' || normalized === 'internal') return 'Internal Project';
  return 'Other';
}

function projectPulseCreateWorkFinalFieldSnapshot() {
  const stored = projectPulseCreateWorkSnapshotRead();

  return {
    requestedWorkType: projectPulseCanonicalWorkType(
      intakeReviewForm?.requestedWorkType
      || intakeForm.requestedWorkType
      || stored.requestedWorkType
      || 'Project'
    ),
    contractType: intakeReviewForm?.contractType || intakeForm.contractType || stored.contractType || '',
    sellQuoteNumber: intakeReviewForm?.sellQuoteNumber || intakeForm.sellQuoteNumber || stored.sellQuoteNumber || '',
    salesforceIdNumber: intakeReviewForm?.salesforceIdNumber || intakeForm.salesforceIdNumber || stored.salesforceIdNumber || '',
    certiniaIdNumber: intakeReviewForm?.certiniaIdNumber || intakeForm.certiniaIdNumber || stored.certiniaIdNumber || '',
    purchaseOrderRequired: intakeForm.purchaseOrderRequired === true || stored.purchaseOrderRequired === true,
    poNumber: intakeForm.poNumber || stored.poNumber || '',
    authorizedAmount: intakeForm.authorizedAmount || stored.authorizedAmount || '',
    poEffectiveStartDate: intakeForm.poEffectiveStartDate || stored.poEffectiveStartDate || '',
    poEffectiveEndDate: intakeForm.poEffectiveEndDate || stored.poEffectiveEndDate || '',
    poCustomerReference: intakeForm.poCustomerReference || stored.poCustomerReference || '',
    sowSignedDate: intakeReviewForm?.sowSignedDate || intakeForm.sowSignedDate || stored.sowSignedDate || '',
    projectName: intakeReviewForm?.projectName || intakeForm.projectName || intakeForm.workName || stored.projectName || stored.workName || '',
    customerId: intakeReviewForm?.customerId || intakeForm.customerId || stored.customerId || '',
    customerName: intakeReviewForm?.customerName || intakeForm.customerName || stored.customerName || ''
  };
}
const updateIntakeForm = (field, value) => {
  projectPulseCreateWorkSnapshotWrite(field, value);

  setIntakeForm((current) => ({
    ...current,
    [field]: value
  }));
};


  // 055D_1_INTAKE_WIZARD_GSD_SOW

  // 055C_10_LOCAL_DOCUMENT_UPLOAD

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



  // 055D_4Q_EDIT_SAVE_PROJECT_IDENTITY
  function projectPulseAttachEditSaveIdentity(payload = {}) {
    const sources = [];

    function addSource(source) {
      if (source && typeof source === 'object') {
        sources.push(source);
      }
    }

    addSource(payload);
    addSource(payload.project);
    addSource(payload.work);
    addSource(payload.item);
    addSource(payload.row);
    addSource(payload.record);
    addSource(payload.selectedProject);
    addSource(payload.selectedWorkRegisterProject);
    addSource(payload.editProject);
    addSource(payload.editingProject);
    addSource(payload.editForm);
    addSource(payload.form);
    addSource(payload.values);
    addSource(payload.setup);
    addSource(payload.setupForm);

    addSource(typeof selectedProject !== 'undefined' ? selectedProject : null);
    addSource(typeof selectedWorkRegisterProject !== 'undefined' ? selectedWorkRegisterProject : null);
    addSource(typeof selectedWork !== 'undefined' ? selectedWork : null);
    addSource(typeof selectedWorkItem !== 'undefined' ? selectedWorkItem : null);
    addSource(typeof selectedWorkRegisterItem !== 'undefined' ? selectedWorkRegisterItem : null);
    addSource(typeof selectedProjectDetail !== 'undefined' ? selectedProjectDetail : null);
    addSource(typeof activeProject !== 'undefined' ? activeProject : null);
    addSource(typeof currentProject !== 'undefined' ? currentProject : null);
    addSource(typeof projectDetail !== 'undefined' ? projectDetail : null);
    addSource(typeof editProject !== 'undefined' ? editProject : null);
    addSource(typeof editingProject !== 'undefined' ? editingProject : null);
    addSource(typeof editWork !== 'undefined' ? editWork : null);
    addSource(typeof workRegisterEditProject !== 'undefined' ? workRegisterEditProject : null);
    addSource(typeof workRegisterProjectEditForm !== 'undefined' ? workRegisterProjectEditForm : null);
    addSource(typeof workRegisterEditForm !== 'undefined' ? workRegisterEditForm : null);
    addSource(typeof projectEditForm !== 'undefined' ? projectEditForm : null);
    addSource(typeof setupForm !== 'undefined' ? setupForm : null);
    addSource(typeof activeWorkRegisterProject !== 'undefined' ? activeWorkRegisterProject : null);
    addSource(typeof intakePackageResult !== 'undefined' ? intakePackageResult : null);

    const identity = {};

    for (const source of sources) {
      const projectId =
        source.projectId
        || source.project_id
        || source.id
        || source.workId
        || source.work_id
        || source.workRegisterProjectId
        || source.selectedProjectId
        || source.selectedWorkRegisterProjectId;

      if (projectId && !identity.projectId) {
        identity.projectId = projectId;
      }

      const projectCode =
        source.projectCode
        || source.project_code
        || source.workCode
        || source.work_code
        || source.code;

      if (projectCode && !identity.projectCode) {
        identity.projectCode = projectCode;
      }

      const projectName =
        source.projectName
        || source.project_name
        || source.name;

      if (projectName && !identity.projectName) {
        identity.projectName = projectName;
      }
    }

    const mergedPayload = {
      ...identity,
      ...payload,
      projectId: payload.projectId || payload.project_id || payload.id || identity.projectId,
      projectCode: payload.projectCode || payload.project_code || identity.projectCode,
      projectName: payload.projectName || payload.project_name || payload.name || identity.projectName
    };

    console.info('ProjectPulse edit-save payload', mergedPayload);

    return mergedPayload;
  }

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
  async function loadPurchaseOrders() {
    setPurchaseOrders((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/work-register/projects/purchase-orders');
      setPurchaseOrders({
        loading: false,
        projects: Array.isArray(data?.projects) ? data.projects : [],
        error: null
      });
      return data;
    } catch (error) {
      setPurchaseOrders({
        loading: false,
        projects: [],
        error: error instanceof Error ? error.message : 'Unable to load purchase orders.'
      });
      return null;
    }
  }

  function purchaseOrderForProject(projectId) {
    return (purchaseOrders.projects || []).find(
      (project) => String(project.projectId) === String(projectId)
    ) || null;
  }

  async function savePurchaseOrder(projectId, values) {
    if (!projectId) return null;

    return putJson(`/api/work-register/projects/${projectId}/purchase-order`, {
      purchaseOrderRequired: values.purchaseOrderRequired === true,
      poNumber: String(values.poNumber || '').trim(),
      authorizedAmount:
        values.authorizedAmount === ''
        || values.authorizedAmount === null
        || values.authorizedAmount === undefined
          ? null
          : Number(values.authorizedAmount),
      effectiveStartDate: values.poEffectiveStartDate || null,
      effectiveEndDate: values.poEffectiveEndDate || null,
      customerReference: String(values.poCustomerReference || '').trim(),
      changeReason: String(values.editReason || values.reason || intakeForm.reason || 'Work Register creation').trim()
    });
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
    loadPurchaseOrders();
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
  const canCreateWorkRegister = editFoundation.data?.canCreateWorkRegister === true;
  const sellAuthoritativeReview =
    selectedIntakeReview?.package?.sourceMode === 'sell_import'
    || intakeReviewForm?.sourceMode === 'sell_import';

  const canArchiveWorkRegister =
    editFoundation.data?.canArchiveWorkRegister === true
    || canEditWorkRegister;

  const canRestoreWorkRegister =
    editFoundation.data?.canRestoreWorkRegister === true;

  const selectedWorkItemIsArchived =
    selectedWorkItem?.isArchived === true
    || normalize(selectedWorkItem?.isArchived) === 'true'
    || String(selectedWorkItem?.status || '')
      .trim()
      .toLowerCase() === 'archived';

  const canModifySelectedProject =
    canEditWorkRegister && !selectedWorkItemIsArchived;

  // 055D_6B2_PROJECT_LIFECYCLE_UI_PERMISSION
  // 055D_6B5B_SIDECAR_PROJECT_LIFECYCLE_UI
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
    const poProject = purchaseOrderForProject(item?.workId);
    const po = poProject?.purchaseOrder || null;
    setSelectedWorkItem(item);
    setEditStatus('');
    setActiveDrawerTab('setup');
    setTaskAssignmentForms({});
    setTaskRosterForms({});
    setMultiEngineerTasks({});
    setTaskAssignmentStatus('');
    setChangeOrderStatus('');
    setDocumentStatus('');
    setDocumentUploadStatus('');
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
      sellQuoteNumber: '',
      salesforceIdNumber: '',
      certiniaIdNumber: '',
      sowSignedDate: '',
      projectStartDate: dateOnly(item.startDate),
      estimatedEndDate: dateOnly(item.estimatedEndDate),
      status: item.status || '',
      purchaseOrderRequired: poProject?.purchaseOrderRequired === true,
      poNumber: po?.poNumber || '',
      authorizedAmount: po?.authorizedAmount ?? '',
      poEffectiveStartDate: dateOnly(po?.effectiveStartDate),
      poEffectiveEndDate: dateOnly(po?.effectiveEndDate),
      poCustomerReference: po?.customerReference || '',
      editReason: ''
    });
    setPurchaseOrderStatus('');
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


  // 055D_5C_CREATE_WORK_ALLOCATION_HELPERS
  function projectPulseNumberForAllocation(value, fallback = 0) {
    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric : fallback;
  }

  function projectPulseTaskTotalHours(task = {}) {
    return projectPulseNumberForAllocation(
      task.hours
        ?? task.totalHours
        ?? task.allocatedHours
        ?? task.quantity
        ?? task.estimatedHours
        ?? task.plannedHours
        ?? task.engineeringHours
        ?? task.pmHours
        ?? 0,
      0
    );
  }

  function projectPulseRoundHours(value) {
    const numeric = projectPulseNumberForAllocation(value, 0);
    return Math.round(numeric * 100) / 100;
  }


  // 055D_5D_TASK_ASSIGNMENT_HELPER_REPAIR
  function projectPulseTaskAssignments(task = {}) {
    const candidateSets = [
      task.assignments,
      task.engineers,
      task.assignedEngineers,
      task.assignmentRows,
      task.rosterRows
    ];

    const existingRows = candidateSets.find((rows) => Array.isArray(rows) && rows.length);

    if (existingRows) {
      return existingRows.map((assignment, index) => ({
        assignedUserId: assignment.assignedUserId || assignment.engineerUserId || assignment.userId || assignment.user_id || '',
        engineerUserId: assignment.engineerUserId || assignment.assignedUserId || assignment.userId || assignment.user_id || '',
        engineerName: assignment.engineerName || assignment.assignedUserName || assignment.displayName || assignment.display_name || '',
        allocatedHours: assignment.allocatedHours ?? assignment.hours ?? 0,
        allocationPercent: assignment.allocationPercent ?? assignment.percent ?? (existingRows.length === 1 ? 100 : 0),
        billable: assignment.billable ?? task.billable ?? projectTaskDefaultBillable(),
        utilizationEligible: assignment.utilizationEligible ?? task.utilizationEligible ?? projectTaskDefaultUtilizationEligible(),
        effectiveStartDate: assignment.effectiveStartDate || new Date().toISOString().slice(0, 10),
        isPrimary: assignment.isPrimary === true || index === 0
      }));
    }

    const assignedUserId = task.assignedUserId || task.engineerUserId || task.primaryEngineerUserId || '';
    const engineerName = task.assignedUserName || task.engineerName || task.primaryEngineerName || '';

    return [{
      assignedUserId,
      engineerUserId: assignedUserId,
      engineerName,
      allocatedHours: task.allocatedHours ?? task.hours ?? task.totalHours ?? task.engineeringHours ?? 0,
      allocationPercent: 100,
      billable: task.billable ?? projectTaskDefaultBillable(),
      utilizationEligible: task.utilizationEligible ?? projectTaskDefaultUtilizationEligible(),
      effectiveStartDate: new Date().toISOString().slice(0, 10),
      isPrimary: true
    }];
  }

function projectPulseNormalizeTaskAssignments(task = {}, assignments = [], changedIndex = 0, changedPercent = null) {
    const rows = Array.isArray(assignments) ? assignments.map((row, index) => ({
      ...row,
      allocationPercent: projectPulseNumberForAllocation(row?.allocationPercent, index === 0 ? 100 : 0)
    })) : [];

    if (!rows.length) return rows;

    const boundedIndex = Math.max(0, Math.min(Number(changedIndex || 0), rows.length - 1));
    const previousPercent = projectPulseNumberForAllocation(rows[boundedIndex].allocationPercent, boundedIndex === 0 ? 100 : 0);

    if (changedPercent !== null && changedPercent !== undefined) {
      const nextPercent = Math.max(0, Math.min(100, projectPulseNumberForAllocation(changedPercent, previousPercent)));
      const delta = nextPercent - previousPercent;
      rows[boundedIndex].allocationPercent = nextPercent;

      if (rows.length > 1 && delta !== 0) {
        const preferredTarget = boundedIndex > 0 ? boundedIndex - 1 : 1;
        let remainingDelta = delta;

        if (remainingDelta > 0) {
          for (const targetIndex of [preferredTarget, ...rows.map((_, idx) => idx).filter((idx) => idx !== boundedIndex && idx !== preferredTarget)]) {
            if (remainingDelta <= 0) break;
            const available = projectPulseNumberForAllocation(rows[targetIndex].allocationPercent, 0);
            const reduction = Math.min(available, remainingDelta);
            rows[targetIndex].allocationPercent = projectPulseRoundHours(available - reduction);
            remainingDelta -= reduction;
          }
        } else {
          const addBack = Math.abs(remainingDelta);
          rows[preferredTarget].allocationPercent = projectPulseRoundHours(projectPulseNumberForAllocation(rows[preferredTarget].allocationPercent, 0) + addBack);
        }
      }
    }

    let totalPercent = rows.reduce((sum, row) => sum + projectPulseNumberForAllocation(row.allocationPercent, 0), 0);

    if (rows.length === 1) {
      rows[0].allocationPercent = 100;
      totalPercent = 100;
    } else if (Math.round(totalPercent * 100) / 100 !== 100) {
      const adjustmentIndex = rows.findIndex((_, idx) => idx !== boundedIndex);
      const targetIndex = adjustmentIndex >= 0 ? adjustmentIndex : 0;
      rows[targetIndex].allocationPercent = Math.max(0, projectPulseRoundHours(projectPulseNumberForAllocation(rows[targetIndex].allocationPercent, 0) + (100 - totalPercent)));
    }

    const taskHours = projectPulseTaskTotalHours(task);
    rows.forEach((row, index) => {
      row.allocationPercent = Math.max(0, Math.min(100, projectPulseRoundHours(row.allocationPercent)));
      row.allocatedHours = projectPulseRoundHours((taskHours * row.allocationPercent) / 100);
      row.isPrimary = row.isPrimary === true || index === 0;
    });

    return rows;
  }


  // 055D_5C_CURRENT_INTAKE_PACKAGE_FOCUS
  function projectPulseFocusIntakePackage(packageId) {
    const normalizedPackageId = String(packageId || '').trim();
    if (!normalizedPackageId) return;
    sessionStorage.setItem('projectPulseCurrentIntakePackageId', normalizedPackageId);
    setCurrentIntakePackageId(normalizedPackageId);
  }

  function projectPulseVisibleIntakePackages(packages = []) {
    const rows = Array.isArray(packages) ? packages : [];
    if (!currentIntakePackageId) return rows;
    const currentRows = rows.filter((item) => String(item.intakePackageId || item.intake_package_id || item.packageId || item.id || '') === String(currentIntakePackageId));
    return currentRows.length ? currentRows : rows.slice(0, 1);
  }

function projectPulseCreateWorkReason() {
    const workTypeLabel = intakeForm.requestedWorkType || intakeForm.workType || intakeForm.workItemType || intakeForm.type || 'Project';
    const customerLabel = intakeForm.customerName || intakeForm.clientName || intakeForm.customer || '';
    const projectLabel = intakeForm.projectName || intakeForm.workName || intakeForm.name || '';
    const pieces = ['Creating New Project'];
    if (workTypeLabel) pieces.push(`Work Type: ${workTypeLabel}`);
    if (customerLabel) pieces.push(`Customer: ${customerLabel}`);
    if (projectLabel) pieces.push(`Work: ${projectLabel}`);
    return pieces.join(' | ');
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


  // 055D_5D_CREATE_TASK_ASSIGNMENT_FALLBACK
  function createTaskAssignment(task = {}, primary = false) {
    return {
      assignedUserId: '',
      engineerUserId: '',
      engineerName: '',
      allocatedHours: primary ? projectPulseTaskTotalHours(task) : 0,
      allocationPercent: primary ? 100 : 0,
      billable: task.billable ?? projectTaskDefaultBillable(),
      utilizationEligible: task.utilizationEligible ?? projectTaskDefaultUtilizationEligible(),
      effectiveStartDate: new Date().toISOString().slice(0, 10),
      isPrimary: primary
    };
  }


// 055D_5F1_SAFE_TASK_ARRAY_AND_ALLOCATION_HELPERS
function projectPulseReviewTaskArraySource(form = {}) {
  const candidates = ['tasksText', 'tasks', 'taskRows', 'reviewTasks', 'assignmentTasks'];

  for (const key of candidates) {
    if (Array.isArray(form[key]) && form[key].length > 0) {
      return key;
    }
  }

  for (const key of candidates) {
    if (Array.isArray(form[key])) {
      return key;
    }
  }

  return '';
}

function projectPulseSafeNumber(value, fallback = 0) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function projectPulseAssignmentTaskHours(task = {}) {
  return projectPulseSafeNumber(
    task.hours
      ?? task.totalHours
      ?? task.allocatedHours
      ?? task.quantity
      ?? task.estimatedHours
      ?? task.plannedHours
      ?? task.engineeringHours
      ?? task.pmHours
      ?? 0,
    0
  );
}

function projectPulseExistingAssignmentRows(task = {}) {
  const candidateSets = [
    task.assignments,
    task.assignmentRows,
    task.engineers,
    task.assignedEngineers,
    task.rosterRows
  ];

  const existing = candidateSets.find((rows) => Array.isArray(rows) && rows.length > 0);

  if (existing) {
    return existing.map((row, index) => ({
      ...row,
      assignedUserId: row.assignedUserId || row.engineerUserId || row.userId || row.user_id || '',
      engineerUserId: row.engineerUserId || row.assignedUserId || row.userId || row.user_id || '',
      engineerName: row.engineerName || row.assignedUserName || row.displayName || row.display_name || '',
      allocationPercent: projectPulseSafeNumber(row.allocationPercent ?? row.percent, existing.length === 1 ? 100 : 0),
      allocatedHours: projectPulseSafeNumber(row.allocatedHours ?? row.hours, 0),
      billable: row.billable ?? task.billable ?? true,
      utilizationEligible: row.utilizationEligible ?? task.utilizationEligible ?? true,
      effectiveStartDate: row.effectiveStartDate || new Date().toISOString().slice(0, 10),
      isPrimary: row.isPrimary === true || index === 0
    }));
  }

  const taskHours = projectPulseAssignmentTaskHours(task);
  const assignedUserId = task.assignedUserId || task.engineerUserId || task.primaryEngineerUserId || '';

  return [{
    assignedUserId,
    engineerUserId: assignedUserId,
    engineerName: task.assignedUserName || task.engineerName || task.primaryEngineerName || '',
    allocationPercent: 100,
    allocatedHours: taskHours,
    billable: task.billable ?? true,
    utilizationEligible: task.utilizationEligible ?? true,
    effectiveStartDate: new Date().toISOString().slice(0, 10),
    isPrimary: true
  }];
}

function projectPulseNormalizeAssignmentRows(task = {}, rows = [], changedIndex = 0, changedPercent = null) {
  const taskHours = projectPulseAssignmentTaskHours(task);
  const normalized = rows.map((row, index) => ({
    ...row,
    allocationPercent: projectPulseSafeNumber(row.allocationPercent, rows.length === 1 ? 100 : 0),
    allocatedHours: projectPulseSafeNumber(row.allocatedHours, 0),
    isPrimary: row.isPrimary === true || index === 0
  }));

  if (!normalized.length) {
    return normalized;
  }

  const boundedIndex = Math.max(0, Math.min(projectPulseSafeNumber(changedIndex, 0), normalized.length - 1));

  if (changedPercent !== null && changedPercent !== undefined) {
    const previous = projectPulseSafeNumber(normalized[boundedIndex].allocationPercent, 0);
    const requested = Math.max(0, Math.min(100, projectPulseSafeNumber(changedPercent, previous)));
    const delta = requested - previous;

    normalized[boundedIndex].allocationPercent = requested;

    if (normalized.length > 1 && delta !== 0) {
      const preferredTarget = boundedIndex > 0 ? boundedIndex - 1 : 1;

      if (delta > 0) {
        let remaining = delta;
        const targetOrder = [
          preferredTarget,
          ...normalized.map((_, index) => index).filter((index) => index !== boundedIndex && index !== preferredTarget)
        ];

        for (const targetIndex of targetOrder) {
          if (remaining <= 0) break;
          const current = projectPulseSafeNumber(normalized[targetIndex].allocationPercent, 0);
          const reduction = Math.min(current, remaining);
          normalized[targetIndex].allocationPercent = Math.round((current - reduction) * 100) / 100;
          remaining -= reduction;
        }
      } else {
        const addBack = Math.abs(delta);
        normalized[preferredTarget].allocationPercent = Math.round((projectPulseSafeNumber(normalized[preferredTarget].allocationPercent, 0) + addBack) * 100) / 100;
      }
    }
  }

  if (normalized.length === 1) {
    normalized[0].allocationPercent = 100;
  } else {
    const total = normalized.reduce((sum, row) => sum + projectPulseSafeNumber(row.allocationPercent, 0), 0);
    const roundedTotal = Math.round(total * 100) / 100;

    if (roundedTotal !== 100) {
      const targetIndex = normalized.findIndex((_, index) => index !== boundedIndex);
      const adjustmentIndex = targetIndex >= 0 ? targetIndex : 0;
      normalized[adjustmentIndex].allocationPercent = Math.max(
        0,
        Math.round((projectPulseSafeNumber(normalized[adjustmentIndex].allocationPercent, 0) + (100 - roundedTotal)) * 100) / 100
      );
    }
  }

  normalized.forEach((row, index) => {
    row.allocationPercent = Math.max(0, Math.min(100, Math.round(projectPulseSafeNumber(row.allocationPercent, 0) * 100) / 100));
    row.allocatedHours = Math.round(((taskHours * row.allocationPercent) / 100) * 100) / 100;
    row.assignedUserId = row.assignedUserId || row.engineerUserId || '';
    row.engineerUserId = row.engineerUserId || row.assignedUserId || '';
    row.isPrimary = row.isPrimary === true || index === 0;
  });

  return normalized;
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
      setTaskAssignmentStatus('This tab is view-only for your role. Only Project Managers, Project Management Leads, and Project Team Coordinators can save task assignments.');
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
    setTaskRosterForms((current) => {
      const form = current[key] || {};
      const currentRows = Array.isArray(form.rows) ? form.rows : initialRosterRows(task);
      const rows = currentRows.map((row) => ({ ...row }));

      while (rows.length <= index) {
        rows.push({
          assignedUserId: '',
          allocatedHours: 0,
          allocationPercent: 0,
          billable: projectTaskDefaultBillable(),
          utilizationEligible: projectTaskDefaultUtilizationEligible(),
          effectiveStartDate: new Date().toISOString().slice(0, 10),
          isPrimary: rows.length === 0
        });
      }

      rows[index] = {
        ...rows[index],
        [field]: value
      };

      if (field === 'assignedUserId') {
        const user = engineerOptions.find((option) => String(option.userId || option.user_id || option.id) === String(value));
        rows[index].assignedUserName = user ? (user.displayName || user.display_name || user.name || user.email || '') : '';
      }

      const normalizedRows = field === 'allocationPercent'
        ? projectPulseNormalizeTaskAssignments(task, rows, index, value)
        : projectPulseNormalizeTaskAssignments(task, rows, index, null);

      return {
        ...current,
        [key]: {
          ...form,
          rows: normalizedRows
        }
      };
    });
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
      setTaskAssignmentStatus('This tab is view-only for your role. Only Project Managers, Project Management Leads, and Project Team Coordinators can save task rosters.');
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
      setChangeOrderStatus('This tab is view-only for your role. Only Project Managers, Project Management Leads, and Project Team Coordinators can save change orders.');
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


  function getWorkRegisterUploadAuthHeaders() {
    const headers = {};

    try {
      if (typeof getStoredProjectPulseAuthSession === 'function') {
        const storedSession = getStoredProjectPulseAuthSession();
        const sessionToken = storedSession?.sessionToken || storedSession?.token || storedSession?.id || '';
        if (sessionToken) {
          headers['X-ProjectPulse-Session'] = sessionToken;
        }
      }
    } catch {
      // Fall back to localStorage below.
    }

    try {
      const rawSession = window.localStorage.getItem('projectPulseAuthSession');
      if (rawSession && !headers['X-ProjectPulse-Session']) {
        try {
          const parsed = JSON.parse(rawSession);
          const sessionToken = parsed?.sessionToken || parsed?.token || parsed?.id || '';
          if (sessionToken) {
            headers['X-ProjectPulse-Session'] = sessionToken;
          }
        } catch {
          headers['X-ProjectPulse-Session'] = rawSession;
        }
      }

      const viewAsUser = window.localStorage.getItem('projectPulseViewAsUser');
      if (viewAsUser) {
        headers['X-ProjectPulse-View-As-User'] = viewAsUser;
      }
    } catch {
      // Ignore browser storage failures.
    }

    return headers;
  }

  async function uploadLocalDocument(event) {
    event.preventDefault();

    if (!selectedWorkItem?.workId) {
      setDocumentUploadStatus('No project is selected.');
      return;
    }

    if (!canEditWorkRegister) {
      setDocumentUploadStatus('This tab is view-only for your role. Only Project Managers, Project Management Leads, and Project Team Coordinators can upload documents.');
      return;
    }

    const form = event.currentTarget;
    const formData = new FormData(form);
    const file = formData.get('file');

    if (!(file instanceof File) || !file.name) {
      setDocumentUploadStatus('Choose a local file to upload.');
      return;
    }

    if (!String(formData.get('documentType') || '').trim()) {
      setDocumentUploadStatus('Document type is required.');
      return;
    }

    if (!String(formData.get('reason') || '').trim()) {
      setDocumentUploadStatus('Reason is required for document upload audit history.');
      return;
    }

    formData.set('projectId', selectedWorkItem.workId);

    if (!String(formData.get('documentName') || '').trim()) {
      formData.set('documentName', file.name);
    }

    setDocumentUploadStatus(`Uploading ${file.name}...`);

    try {
      const response = await fetch('/api/work-register/projects/documents/upload', {
        method: 'POST',
        headers: getWorkRegisterUploadAuthHeaders(),
        body: formData
      });

      const result = await response.json().catch(() => ({}));

      if (!response.ok) {
        throw new Error(result.message || `HTTP ${response.status}`);
      }

      setDocumentUploadStatus(result.message || 'Local document uploaded.');
      form.reset();
      await loadProjectDetails(selectedWorkItem);
      await load();
    } catch (error) {
      setDocumentUploadStatus(error instanceof Error ? error.message : 'Unable to upload local document.');
    }
  }

  async function openWorkRegisterDocument(document) {
    const downloadUrl = document?.downloadUrl || '';
    const reference = document?.documentReference || '';

    if (downloadUrl) {
      setDocumentStatus(`Opening ${document.fileName || 'document'}...`);

      try {
        const response = await fetch(downloadUrl, {
          method: 'GET',
          headers: getWorkRegisterUploadAuthHeaders()
        });

        if (!response.ok) {
          const result = await response.json().catch(() => ({}));
          throw new Error(result.message || `HTTP ${response.status}`);
        }

        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);
        window.open(blobUrl, '_blank', 'noopener,noreferrer');
        window.setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
        setDocumentStatus('');
      } catch (error) {
        setDocumentStatus(error instanceof Error ? error.message : 'Unable to open uploaded document.');
      }

      return;
    }

    if (reference) {
      window.open(reference, '_blank', 'noopener,noreferrer');
    }
  }


  async function saveDocumentRegistration() {
    if (!selectedWorkItem?.workId) {
      setDocumentStatus('No project is selected.');
      return;
    }

    if (!canEditWorkRegister) {
      setDocumentStatus('This tab is view-only for your role. Only Project Managers, Project Management Leads, and Project Team Coordinators can manage documents.');
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
      setDocumentStatus('This tab is view-only for your role. Only Project Managers, Project Management Leads, and Project Team Coordinators can archive documents.');
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




  function parseIntakeJson(value) {
    if (!value) return {};

    if (typeof value === 'object') return value;

    try {
      return JSON.parse(value);
    } catch {
      return {};
    }
  }

  function prettyIntakeJson(value) {
    try {
      return JSON.stringify(value ?? [], null, 2);
    } catch {
      return '[]';
    }
  }

  function reviewFormFromExtracted(packageData, options = {}) {
    const extractedData = packageData?.extractedData || null;
    const savedReviewed = parseIntakeJson(packageData?.package?.reviewedJson);
    const savedExtracted = parseIntakeJson(packageData?.package?.extractedJson);

    const data = extractedData
      || (options.preferReviewed && Object.keys(savedReviewed).length > 0 ? savedReviewed : null)
      || savedExtracted
      || savedReviewed
      || {};

    return {
      sourceMode: data.sourceMode || packageData?.package?.sourceMode || 'gsd_sow_upload',
      projectName: data.projectName || packageData?.package?.projectNameHint || '',
      customerId: data.customerId || packageData?.package?.customerId || '',
      customerName: data.customerName || packageData?.package?.customerHint || '',
      accountExecutiveName: data.accountExecutiveName || '',
      solutionArchitectName: data.solutionArchitectName || '',
      insideSalesName: data.insideSalesName || '',
      sellQuoteNumber: data.sellQuoteNumber || data.sell_quote_number || '',
      salesforceIdNumber: data.salesforceIdNumber || data.salesforce_id_number || '',
      certiniaIdNumber: data.certiniaIdNumber || data.certinia_id_number || '',
      requestedWorkType: projectPulseCanonicalWorkType(data.requestedWorkType || packageData?.package?.requestedWorkType || 'Project'),
      contractType: data.contractType || packageData?.package?.contractType || 'Fixed Price',
      pmHours: data.pmHours || '',
      engineeringHours: data.engineeringHours || '',
      totalProjectHours: data.totalProjectHours || '',
      travelHours: data.travelHours || '',
      projectListPrice: data.projectListPrice || '',
      workLocation: data.workLocation || '',
      ratesText: prettyIntakeJson(data.rates || []),
      tasksText: prettyIntakeJson(data.tasks || []),
      phaseTotalsText: prettyIntakeJson(data.phaseTotals || []),
      parserNotesText: prettyIntakeJson(data.parserNotes || [])
    };
  }

  async function loadIntakePackages() {
    setIntakeReviewStatus('Loading active intake packages...');

    try {
      const response = await fetch('/api/work-register/intake/packages/recent', {
        method: 'GET',
        headers: getWorkRegisterUploadAuthHeaders()
      });

      const result = await response.json().catch(() => ({}));

      if (!response.ok) {
        throw new Error(result.message || `HTTP ${response.status}`);
      }

      const allPackages = result.packages || [];
      const currentPackageId = intakePackageResult?.intakePackageId || '';
      const activePackages = allPackages.filter((pkg) => (
        String(pkg.intakePackageId) === String(currentPackageId)
        || String(pkg.reviewStatus || '').toLowerCase() !== 'reviewed'
      ));

      setIntakePackages(activePackages);
      setIntakeReviewStatus(`Loaded ${activePackages.length} active intake package(s). Reviewed packages are hidden to prevent stale mappings.`);
    } catch (error) {
      setIntakeReviewStatus(error instanceof Error ? error.message : 'Unable to load intake packages.');
    }
  }

  async function openIntakeReview(intakePackageId) {
    setIntakeReviewStatus('Loading intake review...');

    try {
      const response = await fetch(`/api/work-register/intake/packages/${intakePackageId}/review`, {
        method: 'GET',
        headers: getWorkRegisterUploadAuthHeaders()
      });

      const result = await response.json().catch(() => ({}));

      if (!response.ok) {
        throw new Error(result.message || `HTTP ${response.status}`);
      }

      setSelectedIntakeReview(result);
      setIntakeReviewForm(reviewFormFromExtracted(result, { preferReviewed: true }));
      setIntakeReviewStatus('Intake review loaded.');
    } catch (error) {
      setIntakeReviewStatus(error instanceof Error ? error.message : 'Unable to load intake review.');
    }
  }

  async function runIntakeExtraction(intakePackageId) {
    setIntakeReviewStatus('Running current GSD extraction...');

    try {
      const result = await postJson(`/api/work-register/intake/packages/${intakePackageId}/extract`, {});

      const currentPackage = intakePackages.find((pkg) => String(pkg.intakePackageId) === String(intakePackageId)) || {};
      const reviewPayload = {
        package: {
          ...currentPackage,
          intakePackageId,
          extractedJson: JSON.stringify(result.extractedData || {}),
          reviewedJson: '{}',
          extractionStatus: result.extractionStatus,
          reviewStatus: 'needs_review'
        },
        documents: result.extractedData?.documents || [],
        extractedData: result.extractedData || {}
      };

      setSelectedIntakeReview(reviewPayload);
      setIntakeReviewForm(reviewFormFromExtracted(reviewPayload));
      setIntakePackages((current) => current.map((pkg) => (
        String(pkg.intakePackageId) === String(intakePackageId)
          ? {
              ...pkg,
              projectNameHint: result.extractedData?.projectName || pkg.projectNameHint,
              extractionStatus: result.extractionStatus,
              reviewStatus: 'needs_review'
            }
          : pkg
      )));

      const rateCount = Array.isArray(result.extractedData?.rates) ? result.extractedData.rates.length : 0;
      const taskCount = Array.isArray(result.extractedData?.tasks) ? result.extractedData.tasks.length : 0;

      setIntakeReviewStatus(`${result.message || 'Current extraction completed.'} Rates found: ${rateCount}. Tasks found: ${taskCount}.`);
    } catch (error) {
      setIntakeReviewStatus(error instanceof Error ? error.message : 'Unable to run intake extraction.');
    }
  }


  function parseIntakeReviewArrayField(fieldName) {
    try {
      const parsed = JSON.parse(intakeReviewForm?.[fieldName] || '[]');
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  function setIntakeReviewArrayField(fieldName, rows) {
    setIntakeReviewForm((current) => ({
      ...(current ?? {}),
      [fieldName]: JSON.stringify(rows ?? [], null, 2)
    }));
  }

  function updateIntakeReviewArrayItem(fieldName, index, key, value) {
    const rows = parseIntakeReviewArrayField(fieldName);
    rows[index] = {
      ...(rows[index] ?? {}),
      [key]: value
    };
    setIntakeReviewArrayField(fieldName, rows);
  }

  function numberFromReviewValue(value) {
    if (value === null || value === undefined || value === '') return 0;
    const parsed = Number(String(value).replace(/[$,]/g, ''));
    return Number.isFinite(parsed) ? parsed : 0;
  }

  function moneyReviewValue(value) {
    const number = numberFromReviewValue(value);
    return number.toLocaleString(undefined, {
      style: 'currency',
      currency: 'USD',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    });
  }

  function intakeRateExtended(rate) {
    const rateAmount = numberFromReviewValue(rate?.rate ?? rate?.rateAmount ?? rate?.unitRate);
    const hours = numberFromReviewValue(rate?.hours ?? rate?.quantity ?? rate?.qty);
    return rateAmount * hours;
  }

  function intakeTaskTotal(task) {
    const regular = numberFromReviewValue(task?.regularHours ?? task?.hours);
    const overtime = numberFromReviewValue(task?.overtimeHours);
    const reserve = numberFromReviewValue(task?.reserveHours);
    const explicitTotal = numberFromReviewValue(task?.totalHours);
    return explicitTotal || regular + overtime + reserve;
  }

  function gsdRateRows() {
    return parseIntakeReviewArrayField('ratesText');
  }

  function gsdTaskRows() {
    return parseIntakeReviewArrayField('tasksText');
  }

  function includedGsdRateRows() {
    return gsdRateRows().filter((rate) => rate?.include !== false);
  }

  function includedGsdTaskRows() {
    return gsdTaskRows().filter((task) => task?.include !== false);
  }

  function calculatedGsdRateTotal() {
    return includedGsdRateRows().reduce((sum, rate) => sum + intakeRateExtended(rate), 0);
  }

  function calculatedGsdTaskHoursTotal() {
    return includedGsdTaskRows().reduce((sum, task) => sum + intakeTaskTotal(task), 0);
  }

  function addGsdRateRow() {
    const rows = gsdRateRows();
    rows.push({
      include: true,
      source: 'manual_review',
      sku: '',
      description: '',
      rate: 0,
      hours: 0
    });
    setIntakeReviewArrayField('ratesText', rows);
  }

  function addGsdTaskRow() {
    const rows = gsdTaskRows();
    rows.push({
      include: true,
      phase: 'Review',
      taskName: '',
      engineeringRole: '',
      regularHours: 0,
      overtimeHours: 0,
      reserveHours: 0,
      billable: true,
      utilizationEligible: true,
      engineers: []
    });
    setIntakeReviewArrayField('tasksText', rows);
  }



  function intakeUserValueToText(value) {
    if (value === null || value === undefined) return '';
    if (typeof value === 'string') return value.trim();
    if (typeof value === 'number' || typeof value === 'boolean') return String(value);
    return '';
  }

  function intakeUserOptionId(user) {
    if (typeof user === 'string') return user.trim();

    const preferredKeys = [
      'userId', 'userID', 'user_id', 'id', 'value', 'key',
      'employeeId', 'employee_id', 'personId', 'person_id',
      'accountId', 'account_id', 'entraObjectId', 'entra_object_id'
    ];

    for (const key of preferredKeys) {
      const value = intakeUserValueToText(user?.[key]);
      if (value) return value;
    }

    const idEntry = Object.entries(user || {})
      .map(([key, value]) => [key, intakeUserValueToText(value)])
      .find(([key, value]) => value && /id|uuid|guid/i.test(key));

    if (idEntry) return idEntry[1];

    return intakeUserOptionName(user);
  }

  function intakeUserOptionName(user) {
    if (typeof user === 'string') return user.trim();

    const preferredKeys = [
      'displayName', 'display_name', 'fullName', 'full_name',
      'name', 'label', 'text', 'userName', 'user_name',
      'email', 'mail', 'upn', 'principalName', 'principal_name'
    ];

    for (const key of preferredKeys) {
      const value = intakeUserValueToText(user?.[key]);
      if (value) return value;
    }

    const anyValue = Object.entries(user || {})
      .map(([, value]) => intakeUserValueToText(value))
      .find(Boolean);

    return anyValue || 'Unnamed user';
  }

  function intakeFlattenUserValue(value, depth = 0) {
    if (value === null || value === undefined || depth > 4) return '';

    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
      return String(value);
    }

    if (Array.isArray(value)) {
      return value.map((item) => intakeFlattenUserValue(item, depth + 1)).join(' ');
    }

    if (typeof value === 'object') {
      return Object.entries(value)
        .map(([key, nestedValue]) => `${key} ${intakeFlattenUserValue(nestedValue, depth + 1)}`)
        .join(' ');
    }

    return '';
  }

  function intakeNormalizePoolText(value) {
    return String(value || '')
      .toLowerCase()
      .replace(/[_/-]+/g, ' ')
      .replace(/[^a-z0-9\s]/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function intakeUserPoolText(user) {
    const targetedKeys = [
      'role', 'roles', 'roleName', 'role_name', 'phdRole', 'phd_role', 'PHDRole', 'PHD_Role',
      'phdRoles', 'phd_roles', 'applicationRole', 'application_role', 'applicationRoles',
      'team', 'teams', 'teamName', 'team_name', 'teamRole', 'team_role',
      'group', 'groups', 'department', 'departments', 'practice', 'practiceName',
      'resourceType', 'resource_type', 'resourceRole', 'resource_role',
      'jobTitle', 'job_title', 'title', 'discipline', 'serviceLine', 'service_line'
    ];

    const targetedText = targetedKeys
      .map((key) => intakeFlattenUserValue(user?.[key]))
      .join(' ');

    const fullObjectText = intakeFlattenUserValue(user);

    return intakeNormalizePoolText(`${targetedText} ${fullObjectText}`);
  }


  function intakeUserOptionRole(user) {
    return intakeUserPoolText(user);
  }

  function collectIntakeUserOptions() {
    const collections = [];

    const maybePush = (value) => {
      if (Array.isArray(value)) {
        collections.push(value);
      }
    };

    if (typeof editUserOptions !== 'undefined') maybePush(editUserOptions);
    if (typeof editUsers !== 'undefined') maybePush(editUsers);
    if (typeof userOptions !== 'undefined') maybePush(userOptions);
    if (typeof activeUserOptions !== 'undefined') maybePush(activeUserOptions);
    if (typeof workRegisterUserOptions !== 'undefined') maybePush(workRegisterUserOptions);
    if (typeof assignmentUserOptions !== 'undefined') maybePush(assignmentUserOptions);
    if (typeof workRegisterAssignmentUserOptions !== 'undefined') maybePush(workRegisterAssignmentUserOptions);

    if (typeof projectManagerOptions !== 'undefined') maybePush(projectManagerOptions);
    if (typeof projectCoordinatorOptions !== 'undefined') maybePush(projectCoordinatorOptions);
    if (typeof projectManagementOptions !== 'undefined') maybePush(projectManagementOptions);
    if (typeof projectManagementTeamOptions !== 'undefined') maybePush(projectManagementTeamOptions);

    if (typeof engineerOptions !== 'undefined') maybePush(engineerOptions);
    if (typeof editEngineerOptions !== 'undefined') maybePush(editEngineerOptions);
    if (typeof engineeringOptions !== 'undefined') maybePush(engineeringOptions);
    if (typeof engineeringPoolOptions !== 'undefined') maybePush(engineeringPoolOptions);
    if (typeof resourceOptions !== 'undefined') maybePush(resourceOptions);
    if (typeof activeResourceOptions !== 'undefined') maybePush(activeResourceOptions);

    const merged = [];
    const seen = new Set();

    collections.flat().forEach((user) => {
      const id = intakeUserOptionId(user);
      const name = intakeUserOptionName(user);

      if (!id || !name) return;

      const key = `${id}|${name}`;
      if (seen.has(key)) return;

      seen.add(key);
      merged.push(user);
    });

    return merged.sort((a, b) => intakeUserOptionName(a).localeCompare(intakeUserOptionName(b)));
  }

  function intakeAssignableUsers(kind = 'all') {
    const users = collectIntakeUserOptions();

    if (kind === 'all') return users;

    const normalizedKind = String(kind || '').toLowerCase();

    const projectManagementMatchers = [
      'project management',
      'project manager',
      'pmo',
      'project team coordinator',
      'project management team lead',
      'project management manager'
    ];

    const engineeringMatchers = [
      'engineering',
      'engineering team lead'
    ];

    const matchesAny = (text, matchers) => matchers.some((matcher) => {
      const normalizedMatcher = intakeNormalizePoolText(matcher);
      return text === normalizedMatcher
        || text.includes(normalizedMatcher)
        || text.split(' ').includes(normalizedMatcher);
    });

    const filtered = users.filter((user) => {
      const poolText = intakeUserPoolText(user);

      if (normalizedKind === 'pm' || normalizedKind === 'pc' || normalizedKind === 'projectmanagement') {
        return matchesAny(poolText, projectManagementMatchers);
      }

      if (normalizedKind === 'engineer' || normalizedKind === 'engineering') {
        return matchesAny(poolText, engineeringMatchers);
      }

      return true;
    });

    return filtered;
  }


// 055D_5G1_CREATE_WORK_ASSIGNMENT_HELPERS
function projectPulseAssignmentPercentNumber(value, fallback = 0) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function projectPulseAssignmentTaskTotal(task = {}) {
  return projectPulseAssignmentPercentNumber(
    task.totalHours
      ?? task.hours
      ?? task.regularHours
      ?? task.allocatedHours
      ?? task.quantity
      ?? task.estimatedHours
      ?? task.plannedHours
      ?? 0,
    0
  );
}

function projectPulseRoundAssignmentNumber(value) {
  return Math.round(projectPulseAssignmentPercentNumber(value, 0) * 100) / 100;
}

function projectPulseNormalizeCreateWorkAssignments(task = {}, assignments = [], changedIndex = 0, changedPercent = null) {
  const rows = Array.isArray(assignments) ? assignments.map((row, index) => ({
    ...row,
    engineerUserId: row.engineerUserId || row.assignedUserId || row.userId || row.user_id || '',
    engineerName: row.engineerName || row.assignedUserName || row.displayName || row.display_name || '',
    hours: projectPulseAssignmentPercentNumber(row.hours ?? row.allocatedHours, 0),
    allocationPercent: projectPulseAssignmentPercentNumber(row.allocationPercent, assignments.length === 1 ? 100 : 0),
    isPrimary: row.isPrimary === true || index === 0,
    notes: row.notes || ''
  })) : [];

  if (!rows.length) {
    return rows;
  }

  const boundedIndex = Math.max(0, Math.min(projectPulseAssignmentPercentNumber(changedIndex, 0), rows.length - 1));

  if (changedPercent !== null && changedPercent !== undefined) {
    const previous = projectPulseAssignmentPercentNumber(rows[boundedIndex].allocationPercent, 0);
    const requested = Math.max(0, Math.min(100, projectPulseAssignmentPercentNumber(changedPercent, previous)));
    const delta = requested - previous;

    rows[boundedIndex].allocationPercent = requested;

    if (rows.length > 1 && delta !== 0) {
      const preferredTarget = boundedIndex > 0 ? boundedIndex - 1 : 1;

      if (delta > 0) {
        let remaining = delta;
        const targetOrder = [
          preferredTarget,
          ...rows.map((_, index) => index).filter((index) => index !== boundedIndex && index !== preferredTarget)
        ];

        for (const targetIndex of targetOrder) {
          if (remaining <= 0) break;
          const currentPercent = projectPulseAssignmentPercentNumber(rows[targetIndex].allocationPercent, 0);
          const reduction = Math.min(currentPercent, remaining);
          rows[targetIndex].allocationPercent = projectPulseRoundAssignmentNumber(currentPercent - reduction);
          remaining -= reduction;
        }
      } else {
        const addBack = Math.abs(delta);
        rows[preferredTarget].allocationPercent = projectPulseRoundAssignmentNumber(
          projectPulseAssignmentPercentNumber(rows[preferredTarget].allocationPercent, 0) + addBack
        );
      }
    }
  }

  if (rows.length === 1) {
    rows[0].allocationPercent = 100;
    rows[0].isPrimary = true;
  } else {
    const total = rows.reduce((sum, row) => sum + projectPulseAssignmentPercentNumber(row.allocationPercent, 0), 0);
    const roundedTotal = projectPulseRoundAssignmentNumber(total);

    if (roundedTotal !== 100) {
      const targetIndex = rows.findIndex((_, index) => index !== boundedIndex);
      const adjustmentIndex = targetIndex >= 0 ? targetIndex : 0;
      rows[adjustmentIndex].allocationPercent = Math.max(
        0,
        projectPulseRoundAssignmentNumber(projectPulseAssignmentPercentNumber(rows[adjustmentIndex].allocationPercent, 0) + (100 - roundedTotal))
      );
    }
  }

  const taskTotal = projectPulseAssignmentTaskTotal(task);

  rows.forEach((row, index) => {
    row.allocationPercent = Math.max(0, Math.min(100, projectPulseRoundAssignmentNumber(row.allocationPercent)));
    row.hours = projectPulseRoundAssignmentNumber((taskTotal * row.allocationPercent) / 100);
    row.allocatedHours = row.hours;
    row.assignedUserId = row.engineerUserId || row.assignedUserId || '';
    row.isPrimary = row.isPrimary === true || index === 0;
  });

  return rows;
}


  // 055D_5K_TASK_ASSIGNMENT_POOL_ROUTING
  function projectPulseTaskAssignmentPoolKind(task = {}) {
    const text = [
      task.phase,
      task.taskName,
      task.name,
      task.description,
      task.engineeringRole,
      task.role,
      task.sku,
      task.lineType
    ].filter(Boolean).join(' ').toLowerCase();

    const projectManagementTerms = [
      'project oversight',
      'project management',
      'project manager',
      'project coordinator',
      'project team coordinator',
      'pmo',
      'pm ',
      ' pm',
      'pc ',
      ' pc'
    ];

    if (projectManagementTerms.some((term) => text.includes(term.trim()))) {
      return 'pm';
    }

    return 'engineer';
  }

  function projectPulseTaskAssignmentPoolPlaceholder(task = {}) {
    return projectPulseTaskAssignmentPoolKind(task) === 'pm'
      ? 'Select PM / PC / Project Team Coordinator resource'
      : 'Select Engineering / Engineering Team Lead resource';
  }

function blankTaskAssignment(task = {}, primary = false) {
    return {
      engineerUserId: '',
      engineerName: '',
      hours: numberFromReviewValue(task?.totalHours || task?.regularHours || 0),
      allocationPercent: primary ? 100 : 0,
      isPrimary: primary,
      notes: ''
    };
  }

  function taskAssignmentRows(task) {
    if (Array.isArray(task?.assignments) && task.assignments.length > 0) {
      return task.assignments;
    }

    if (task?.engineerUserId || task?.primaryEngineerUserId) {
      return [{
        engineerUserId: task.engineerUserId || task.primaryEngineerUserId,
        engineerName: task.engineerName || task.primaryEngineerName || '',
        hours: numberFromReviewValue(task.totalHours || task.regularHours || 0),
        allocationPercent: 100,
        isPrimary: true,
        notes: ''
      }];
    }

    return [blankTaskAssignment(task, true)];
  }

  function updateTaskAssignmentMode(taskIndex, multiple) {
    const tasks = gsdTaskRows();
    const task = tasks[taskIndex] || {};
    const assignments = taskAssignmentRows(task);

    tasks[taskIndex] = {
      ...task,
      assignmentMode: multiple ? 'multiple' : 'single',
      assignments: multiple ? assignments : [assignments[0] || blankTaskAssignment(task, true)]
    };

    setIntakeReviewArrayField('tasksText', tasks);
  }
function updateTaskAssignment(taskIndex, assignmentIndex, key, value) {
  setIntakeSaveBanner('');

  const tasks = gsdTaskRows();

  if (!Array.isArray(tasks) || !tasks.length || !tasks[taskIndex]) {
    console.warn('055D.5G.1: Task index not found in gsdTaskRows; assignment update ignored.', { taskIndex, assignmentIndex, key });
    return;
  }

  const task = { ...(tasks[taskIndex] || {}) };
  const assignments = taskAssignmentRows(task).map((assignment, index) => ({
    ...blankTaskAssignment(task, index === 0),
    ...assignment,
    engineerUserId: assignment.engineerUserId || assignment.assignedUserId || '',
    engineerName: assignment.engineerName || assignment.assignedUserName || '',
    hours: numberFromReviewValue(assignment.hours ?? assignment.allocatedHours ?? 0),
    allocationPercent: numberFromReviewValue(assignment.allocationPercent ?? (index === 0 ? 100 : 0)),
    isPrimary: assignment.isPrimary === true || index === 0
  }));

  while (assignments.length <= assignmentIndex) {
    assignments.push(blankTaskAssignment(task, assignments.length === 0));
  }

  assignments[assignmentIndex] = {
    ...assignments[assignmentIndex],
    [key]: value
  };

  if (key === 'engineerUserId') {
    const user = intakeAssignableUsers(projectPulseTaskAssignmentPoolKind(task)).find((option) => String(intakeUserOptionId(option)) === String(value));
    assignments[assignmentIndex].engineerUserId = value;
    assignments[assignmentIndex].assignedUserId = value;
    assignments[assignmentIndex].engineerName = user ? intakeUserOptionName(user) : '';
    assignments[assignmentIndex].assignedUserName = user ? intakeUserOptionName(user) : '';
  }

  if (key === 'isPrimary') {
    assignments.forEach((assignment, index) => {
      assignment.isPrimary = index === assignmentIndex;
    });
  }

  const normalizedAssignments = projectPulseNormalizeCreateWorkAssignments(
    task,
    assignments,
    assignmentIndex,
    key === 'allocationPercent' ? value : null
  );

  tasks[taskIndex] = {
    ...task,
    assignmentMode: normalizedAssignments.length > 1 ? 'multiple' : (task.assignmentMode || 'single'),
    assignments: normalizedAssignments,
    engineers: normalizedAssignments,
    assignedEngineers: normalizedAssignments
  };

  setIntakeReviewArrayField('tasksText', tasks);
}
function addTaskAssignmentRow(taskIndex) {
  setIntakeSaveBanner('');

  const tasks = gsdTaskRows();
  const task = tasks[taskIndex] || {};
  const assignments = taskAssignmentRows(task).map((assignment, index) => ({
    ...blankTaskAssignment(task, index === 0),
    ...assignment,
    engineerUserId: assignment.engineerUserId || assignment.assignedUserId || '',
    engineerName: assignment.engineerName || assignment.assignedUserName || '',
    hours: numberFromReviewValue(assignment.hours ?? assignment.allocatedHours ?? 0),
    allocationPercent: numberFromReviewValue(assignment.allocationPercent ?? (index === 0 ? 100 : 0)),
    isPrimary: assignment.isPrimary === true || index === 0
  }));

  assignments.push({
    ...blankTaskAssignment(task, false),
    hours: 0,
    allocatedHours: 0,
    allocationPercent: 0,
    isPrimary: false
  });

  const normalizedAssignments = projectPulseNormalizeCreateWorkAssignments(task, assignments, assignments.length - 1, 0);

  tasks[taskIndex] = {
    ...task,
    assignmentMode: 'multiple',
    assignments: normalizedAssignments,
    engineers: normalizedAssignments,
    assignedEngineers: normalizedAssignments
  };

  setIntakeReviewArrayField('tasksText', tasks);
}
function removeTaskAssignmentRow(taskIndex, assignmentIndex) {
  setIntakeSaveBanner('');

  const tasks = gsdTaskRows();
  const task = tasks[taskIndex] || {};
  let assignments = taskAssignmentRows(task).filter((_, index) => index !== assignmentIndex);

  if (assignments.length === 0) {
    assignments = [blankTaskAssignment(task, true)];
  }

  if (!assignments.some((assignment) => assignment.isPrimary)) {
    assignments[0].isPrimary = true;
  }

  const normalizedAssignments = projectPulseNormalizeCreateWorkAssignments(task, assignments, 0, null);

  tasks[taskIndex] = {
    ...task,
    assignmentMode: normalizedAssignments.length > 1 ? 'multiple' : 'single',
    assignments: normalizedAssignments,
    engineers: normalizedAssignments,
    assignedEngineers: normalizedAssignments
  };

  setIntakeReviewArrayField('tasksText', tasks);
}


  function taskAssignmentHoursTotal(task) {
    return taskAssignmentRows(task).reduce((sum, assignment) => sum + numberFromReviewValue(assignment.hours), 0);
  }


  function updateIntakeReviewForm(field, value) {
    setIntakeReviewForm((current) => ({
      ...(current ?? {}),
      [field]: value
    }));
  }

  async function saveIntakeReviewMapping(options = {}) {
    const throwOnError = options?.throwOnError === true;
    if (!selectedIntakeReview || !intakeReviewForm) {
      const message = 'Load or extract an intake package before saving the reviewed mapping.';
      setIntakeReviewStatus(message);
      if (throwOnError) throw new Error(message);
      return null;
    }

    const intakePackageId =
      selectedIntakeReview?.package?.intakePackageId
      || selectedIntakeReview?.intakePackageId
      || intakePackageResult?.intakePackageId;

    if (!intakePackageId) {
      const message = 'Unable to determine intake package ID for review save.';
      setIntakeReviewStatus(message);
      if (throwOnError) throw new Error(message);
      return null;
    }

    let rates = [];
    let tasks = [];
    let parserNotes = [];
    let phaseTotals = [];

    try {
      rates = JSON.parse(intakeReviewForm.ratesText || '[]');
      tasks = JSON.parse(intakeReviewForm.tasksText || '[]');
      parserNotes = JSON.parse(intakeReviewForm.parserNotesText || '[]');
      phaseTotals = JSON.parse(intakeReviewForm.phaseTotalsText || '[]');
    } catch (error) {
      const message = 'Rates, tasks, phase totals, and parser notes must contain valid JSON before saving.';
      setIntakeReviewStatus(message);
      if (throwOnError) throw new Error(message);
      return null;
    }

    const pmUser = intakeAssignableUsers('pm').find((user) => String(intakeUserOptionId(user)) === String(intakeReviewForm.projectManagerUserId));
    const pcUser = intakeAssignableUsers('pc').find((user) => String(intakeUserOptionId(user)) === String(intakeReviewForm.projectCoordinatorUserId));

    const normalizedTasks = tasks.map((task) => {
      const assignments = taskAssignmentRows(task).map((assignment) => {
        const user = intakeAssignableUsers('engineer').find((option) => String(intakeUserOptionId(option)) === String(assignment.engineerUserId));

        return {
          ...assignment,
          engineerName: user ? intakeUserOptionName(user) : (assignment.engineerName || ''),
          hours: numberFromReviewValue(assignment.hours),
          allocationPercent: numberFromReviewValue(assignment.allocationPercent),
          isPrimary: assignment.isPrimary === true
        };
      });

      if (assignments.length > 0 && !assignments.some((assignment) => assignment.isPrimary)) {
        assignments[0].isPrimary = true;
      }

      return {
        ...task,
        assignmentMode: assignments.length > 1 ? 'multiple' : 'single',
        assignments
      };
    });

    const reviewedData = {
      sourceMode: intakeReviewForm.sourceMode || selectedIntakeReview?.package?.sourceMode || 'gsd_sow_upload',
      projectName: intakeReviewForm.projectName,
      customerId: intakeReviewForm.customerId,
      customerName: intakeReviewForm.customerName,
      accountExecutiveName: intakeReviewForm.accountExecutiveName,
      solutionArchitectName: intakeReviewForm.solutionArchitectName,
      insideSalesName: intakeReviewForm.insideSalesName,
      // 055D_5B_CREATE_WORK_IDENTIFIER_PAYLOAD
      sellQuoteNumber: intakeReviewForm.sellQuoteNumber || intakeForm.sellQuoteNumber || '',
      salesforceIdNumber: intakeReviewForm.salesforceIdNumber || intakeForm.salesforceIdNumber || '',
      certiniaIdNumber: intakeReviewForm.certiniaIdNumber || intakeForm.certiniaIdNumber || '',
      // 055D_5K_FINAL_SOW_SIGNED_DATE_VALUE
      sowSignedDate: intakeReviewForm.sowSignedDate || intakeForm.sowSignedDate || '',
      intakeReason: intakeReviewForm.intakeReason || intakeForm.reason || intakeForm.intakeReason || projectPulseCreateWorkReason(),

      requestedWorkType: projectPulseCanonicalWorkType(intakeReviewForm.requestedWorkType),
      contractType: intakeReviewForm.contractType,
      // 055D_5K_REVIEW_SAVE_SOW_SIGNED_DATE
      pmHours: intakeReviewForm.pmHours,
      engineeringHours: intakeReviewForm.engineeringHours,
      totalProjectHours: intakeReviewForm.totalProjectHours,
      travelHours: intakeReviewForm.travelHours,
      projectListPrice: intakeReviewForm.projectListPrice,
      workLocation: intakeReviewForm.workLocation,
      rates,
      tasks: normalizedTasks,
      phaseTotals,
      parserNotes,
      assignmentPlan: {
        projectManagerUserId: intakeReviewForm.projectManagerUserId || '',
        projectManagerName: pmUser ? intakeUserOptionName(pmUser) : '',
        projectCoordinatorUserId: intakeReviewForm.projectCoordinatorUserId || '',
        projectCoordinatorName: pcUser ? intakeUserOptionName(pcUser) : '',
        taskAssignments: normalizedTasks.map((task) => ({
          taskName: task.taskName,
          phase: task.phase,
          assignmentMode: task.assignmentMode,
          assignments: task.assignments || []
        }))
      }
    };

    setIntakeReviewStatus('Saving reviewed mapping and assignment plan...');

    try {
      const result = await postJson(`/api/work-register/intake/packages/${intakePackageId}/review/save`, {
        reviewedData
      });

      setIntakeReviewForm((current) => ({
        ...(current ?? {}),
        tasksText: JSON.stringify(normalizedTasks, null, 2),
        ratesText: JSON.stringify(rates, null, 2),
        phaseTotalsText: JSON.stringify(phaseTotals, null, 2),
        parserNotesText: JSON.stringify(parserNotes, null, 2)
      }));

      setIntakeReviewStatus(result.message || 'Reviewed mapping and assignment plan saved.');
      // 055D_5C_ASSIGNMENT_SAVE_BANNER_SUCCESS
      setIntakeSaveBanner('Assignment configuration saved successfully.');
      await loadIntakePackages();
      return result;
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to save reviewed mapping and assignment plan.';
      setIntakeReviewStatus(message);
      if (throwOnError) throw (error instanceof Error ? error : new Error(message));
      return null;
    }
  }


// 055D_5H_CREATE_WORK_REGISTER_FINAL_SAVE
// 055D_5I2_FINAL_SAVE_IDENTIFIERS_AND_CLOSE
// 055D_5L_FINAL_SAVE_USES_STABLE_SNAPSHOT
async function createWorkRegisterFromReviewedIntake() {
  if (!selectedIntakeReview || !intakeReviewForm) {
    setIntakeReviewStatus('Load or extract an intake package before creating the Work Register record.');
    return;
  }

  const intakePackageId =
    selectedIntakeReview?.package?.intakePackageId
    || selectedIntakeReview?.package?.intake_package_id
    || selectedIntakeReview?.intakePackageId
    || selectedIntakeReview?.intake_package_id
    || selectedIntakeReview?.packageId
    || selectedIntakeReview?.id
    || intakePackageResult?.intakePackageId
    || intakePackageResult?.intake_package_id
    || currentIntakePackageId
    || '';

  if (!intakePackageId) {
    setIntakeReviewStatus('Unable to determine intake package ID for final Work Register save.');
    return;
  }

  const finalFields = projectPulseCreateWorkFinalFieldSnapshot();

  try {
    setIntakeSaveBanner('');
    setIntakeReviewStatus('Saving reviewed intake package into Work Register...');

    await saveIntakeReviewMapping({ throwOnError: true });

    const result = await postJson(`/api/work-register/intake/packages/${intakePackageId}/commit`, {
      intakeReason: intakeReviewForm.intakeReason || intakeForm.reason || intakeForm.intakeReason || projectPulseCreateWorkReason(),
      requestedWorkType: finalFields.requestedWorkType,
      contractType: finalFields.contractType,
      sellQuoteNumber: finalFields.sellQuoteNumber,
      salesforceIdNumber: finalFields.salesforceIdNumber,
      certiniaIdNumber: finalFields.certiniaIdNumber,
      sowSignedDate: finalFields.sowSignedDate
    });

    const createdProjectId =
      result.projectId
      || result.project_id
      || result.workId
      || result.work_id
      || result.workRegisterId
      || result.work_register_id
      || result.createdProjectId
      || result.created_project_id
      || result.project?.projectId
      || result.project?.project_id
      || result.data?.projectId
      || result.data?.project_id
      || '';


    const successMessage = result.message || 'Work Register record created successfully.';
    setIntakeSaveBanner(successMessage);
    setIntakeReviewStatus(successMessage);

    if (createdProjectId) {
      sessionStorage.setItem('projectPulseLastCreatedWorkId', createdProjectId);

      if (
        finalFields.purchaseOrderRequired
        || finalFields.poNumber
        || finalFields.authorizedAmount
        || finalFields.poEffectiveStartDate
        || finalFields.poEffectiveEndDate
        || finalFields.poCustomerReference
      ) {
        await savePurchaseOrder(createdProjectId, finalFields);
      }

      await loadPurchaseOrders();
    }

    if (typeof loadOverview === 'function') {
      await loadOverview();
    } else if (typeof loadWorkRegisterOverview === 'function') {
      await loadWorkRegisterOverview();
    } else if (typeof refreshWorkRegister === 'function') {
      await refreshWorkRegister();
    }

    setIntakeWizardOpen(false);
    setSelectedIntakeReview(null);
    setIntakeReviewForm(null);
    setIntakePackageResult(null);
    setCurrentIntakePackageId('');
    sessionStorage.removeItem('projectPulseCurrentIntakePackageId');
    sessionStorage.removeItem('projectPulseCreateWorkFinalFields');
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unable to create Work Register record.';
    setIntakeReviewStatus(message);
    setIntakeSaveBanner('');
  }
}







  function intakeCustomerOptionId(customer) {
    return customer?.customerId || customer?.id || customer?.value || customer?.customer_id || '';
  }

  function intakeCustomerOptionName(customer) {
    return customer?.customerName || customer?.name || customer?.label || customer?.displayName || customer?.customer_name || 'Selected customer';
  }

  function selectedIntakeCustomer() {
    return editCustomerOptions.find((customer) => String(intakeCustomerOptionId(customer)) === String(intakeForm.customerId));
  }



  function defaultIntakeForm() {
    return {
      sourceMode: 'gsd',
      sellRecordId: '',
      requestedWorkType: 'Project',
      contractType: 'Fixed Price',
      customerId: '',
      projectNameHint: '',
      sellQuoteNumber: '',
      salesforceIdNumber: '',
      certiniaIdNumber: '',
      sowSignedDate: '',
      skipGsd: false,
      skipSow: false,
      notes: '',
      reason: '',
      purchaseOrderRequired: false,
      poNumber: '',
      authorizedAmount: '',
      poEffectiveStartDate: '',
      poEffectiveEndDate: '',
      poCustomerReference: ''
    };
  }

  function intakeCustomerOptionId(customer) {
    return customer?.customerId || customer?.id || customer?.value || customer?.customer_id || '';
  }

  function intakeCustomerOptionName(customer) {
    return customer?.customerName || customer?.name || customer?.label || customer?.displayName || customer?.customer_name || 'Selected customer';
  }

  function selectedIntakeCustomer() {
    return editCustomerOptions.find((customer) => String(intakeCustomerOptionId(customer)) === String(intakeForm.customerId));
  }



  function intakeValueToTextSafe(value) {
    if (value === null || value === undefined) return '';
    if (typeof value === 'string') return value.trim();
    if (typeof value === 'number' || typeof value === 'boolean') return String(value);
    return '';
  }

  function intakeLooksLikeGuidSafe(value) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(String(value || '').trim());
  }

  function intakeCustomerOptionNameSafe(customer) {
    if (typeof customer === 'string') return customer.trim();

    const preferredKeys = [
      'customerName', 'customer_name', 'customerDisplayName', 'customer_display_name',
      'name', 'displayName', 'display_name', 'label', 'text',
      'companyName', 'company_name', 'accountName', 'account_name',
      'clientName', 'client_name', 'legalName', 'legal_name',
      'organizationName', 'organization_name', 'title', 'description'
    ];

    for (const key of preferredKeys) {
      const value = intakeValueToTextSafe(customer?.[key]);
      if (value && value !== 'Selected customer') return value;
    }

    const entries = Object.entries(customer || {});
    const nonIdValue = entries
      .map(([key, value]) => [key, intakeValueToTextSafe(value)])
      .find(([key, value]) => (
        value
        && value !== 'Selected customer'
        && !/id|uuid|guid/i.test(key)
        && !intakeLooksLikeGuidSafe(value)
        && !value.includes('@')
      ));

    if (nonIdValue) return nonIdValue[1];

    const anyValue = entries
      .map(([, value]) => intakeValueToTextSafe(value))
      .find((value) => value && value !== 'Selected customer');

    return anyValue || 'Customer record missing display name';
  }

  function intakeCustomerOptionIdSafe(customer) {
    if (typeof customer === 'string') return customer.trim();

    const preferredKeys = [
      'customerId', 'customerID', 'customer_id', 'id', 'value', 'key',
      'customerDirectoryId', 'customer_directory_id', 'directoryCustomerId',
      'workCustomerId', 'accountId', 'account_id', 'clientId', 'client_id',
      'organizationId', 'organization_id'
    ];

    for (const key of preferredKeys) {
      const value = intakeValueToTextSafe(customer?.[key]);
      if (value) return value;
    }

    const entries = Object.entries(customer || {});
    const idValue = entries
      .map(([key, value]) => [key, intakeValueToTextSafe(value)])
      .find(([key, value]) => value && /id|uuid|guid/i.test(key));

    if (idValue) return idValue[1];

    return intakeCustomerOptionNameSafe(customer);
  }

  function selectedIntakeCustomerSafe() {
    return editCustomerOptions.find((customer) => String(intakeCustomerOptionIdSafe(customer)) === String(intakeForm.customerId));
  }


  function updateIntakeField(field, value) {
    setIntakeForm((current) => ({
      ...current,
      [field]: value
    }));
  }

  function resetIntakeWizard() {
    setIntakeForm(defaultIntakeForm());
    setIntakePackageResult(null);
    setSelectedIntakeReview(null);
    setIntakeReviewForm(null);
    setIntakeWizardStatus('');
    setIntakeReviewStatus('');
  }

  function intakeRequiresProjectDocuments() {
    const type = String(intakeForm.requestedWorkType || '').toLowerCase();
    return type === 'project' || type === 'iqs';
  }

  async function uploadInitialIntakePackage(event) {
    event.preventDefault();

    if (!canCreateWorkRegister) {
      setIntakeWizardStatus('Only a Project Team Coordinator can create intake packages.');
      return;
    }

    const form = event.currentTarget;
    const formData = new FormData(form);
    const gsdFile = formData.get('gsdFile');
    const sowFile = formData.get('sowFile');

    if (intakeRequiresProjectDocuments() && !intakeForm.skipGsd && (!(gsdFile instanceof File) || !gsdFile.name)) {
      setIntakeWizardStatus('Upload the GSD or check “No GSD available / manual intake.”');
      return;
    }

    if (intakeRequiresProjectDocuments() && !intakeForm.skipSow && (!(sowFile instanceof File) || !sowFile.name)) {
      setIntakeWizardStatus('Upload the SOW or check “No SOW available yet.”');
      return;
    }

    if (!intakeForm.customerId) {
      setIntakeWizardStatus('Select a customer from the Customer Directory. If the customer does not exist, onboard the customer first.');
      return;
    }

    if (!String(intakeForm.reason || '').trim()) {
      setIntakeWizardStatus('Intake reason is required for audit history.');
      return;
    }

    const customerSelect = form.elements.customerId;
    const visibleCustomerName = customerSelect?.selectedOptions?.[0]?.textContent?.trim() || 'Selected customer';

    Object.entries(intakeForm).forEach(([key, value]) => {
      formData.set(key, String(value ?? ''));
    });

    formData.set('customerName', visibleCustomerName);

    setSelectedIntakeReview(null);
    setIntakeReviewForm(null);
    setIntakePackages([]);
    setIntakeWizardStatus('Uploading intake package...');
    setIntakeReviewStatus('Uploading current GSD/SOW package...');

    let uploadedPackage = null;

    try {
      const response = await fetch('/api/work-register/intake/packages/upload', {
        method: 'POST',
        headers: getWorkRegisterUploadAuthHeaders(),
        body: formData
      });

      const uploadResult = await response.json().catch(() => ({}));

      if (!response.ok) {
        throw new Error(uploadResult.message || `Upload failed with HTTP ${response.status}`);
      }

      uploadedPackage = {
        intakePackageId: uploadResult.intakePackageId,
        requestedWorkType: uploadResult.requestedWorkType,
        contractType: uploadResult.contractType || intakeForm.contractType,
        customerId: uploadResult.customerId || intakeForm.customerId,
        customerHint: visibleCustomerName,
        projectNameHint: uploadResult.projectNameHint || 'New intake package',
        extractionStatus: 'pending_parser',
        reviewStatus: 'not_started',
        documentCount: uploadResult.uploadedDocumentCount || 0
      };

      setIntakePackageResult({
        ...uploadResult,
        customerHint: visibleCustomerName
      });
      setIntakePackages([uploadedPackage]);
      setIntakeWizardStatus('Intake package uploaded. Running GSD extraction now...');
      setIntakeReviewStatus('Running current GSD extraction...');

      const extractResult = await postJson(`/api/work-register/intake/packages/${uploadResult.intakePackageId}/extract`, {});

      const reviewPayload = {
        package: {
          ...uploadedPackage,
          customerHint: visibleCustomerName,
          extractedJson: JSON.stringify(extractResult.extractedData || {}),
          reviewedJson: '{}',
          extractionStatus: extractResult.extractionStatus,
          reviewStatus: 'needs_review'
        },
        documents: extractResult.extractedData?.documents || [],
        extractedData: extractResult.extractedData || {}
      };

      setSelectedIntakeReview(reviewPayload);
      setIntakeReviewForm(reviewFormFromExtracted(reviewPayload));
      setIntakePackages([{
        ...uploadedPackage,
        projectNameHint: extractResult.extractedData?.projectName || uploadedPackage.projectNameHint,
        extractionStatus: extractResult.extractionStatus,
        reviewStatus: 'needs_review'
      }]);

      const rateCount = Array.isArray(extractResult.extractedData?.rates) ? extractResult.extractedData.rates.length : 0;
      const taskCount = Array.isArray(extractResult.extractedData?.tasks) ? extractResult.extractedData.tasks.length : 0;

      setIntakeWizardStatus('Intake package uploaded and extracted.');
      setIntakeReviewStatus(`${extractResult.message || 'Current extraction completed.'} Rates found: ${rateCount}. Tasks found: ${taskCount}.`);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to upload and extract intake package.';
      setIntakeWizardStatus(message);
      setIntakeReviewStatus(message);

      if (uploadedPackage) {
        setIntakePackages([uploadedPackage]);
      }
    }
  }

  async function importSellIntakePackage(event) {
    event.preventDefault();

    if (!canCreateWorkRegister) {
      setIntakeWizardStatus('Only a Project Team Coordinator can create a Work Register record.');
      return;
    }
    if (!String(intakeForm.sellRecordId || '').trim()) {
      setIntakeWizardStatus('Enter the SELL deal, quote, or opportunity record ID.');
      return;
    }
    if (!intakeForm.customerId) {
      setIntakeWizardStatus('Select the matching ProjectPulse customer.');
      return;
    }
    if (!String(intakeForm.reason || '').trim()) {
      setIntakeWizardStatus('Intake reason is required for audit history.');
      return;
    }

    setSelectedIntakeReview(null);
    setIntakeReviewForm(null);
    setIntakePackages([]);
    setIntakeWizardStatus('Importing the authoritative project name and Actual Rate / Pricing / Rate Review from SELL...');
    setIntakeReviewStatus('Connecting to SELL through Module 026...');

    try {
      const result = await postJson('/api/work-register/intake/packages/sell/import', {
        sellRecordId: String(intakeForm.sellRecordId).trim(),
        customerId: intakeForm.customerId,
        requestedWorkType: intakeForm.requestedWorkType,
        contractType: intakeForm.contractType,
        notes: intakeForm.notes,
        reason: intakeForm.reason
      });

      setIntakePackageResult({ ...result, uploadedDocumentCount: 0 });
      setCurrentIntakePackageId(result.intakePackageId);
      sessionStorage.setItem('projectPulseCurrentIntakePackageId', result.intakePackageId);
      setIntakePackages([{
        ...result,
        sourceMode: 'sell_import',
        documentCount: 0
      }]);
      await openIntakeReview(result.intakePackageId);
      setIntakeWizardStatus('SELL intake imported. Project name and pricing are source-locked; complete assignments and final review.');
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to import the SELL record.';
      setIntakeWizardStatus(message);
      setIntakeReviewStatus(message);
    }
  }



  async function changeWorkRegisterProjectLifecycle(action) {
    const normalizedAction =
      String(action || '').trim().toLowerCase();

    const projectId = selectedWorkItem?.workId;
    const projectName =
      selectedWorkItem?.workName || 'this project';

    if (
      !projectId
      || selectedWorkItem?.sourceTable !== 'projects'
    ) {
      setEditStatus(
        'A saved Work Register project is required for this lifecycle action.'
      );
      return;
    }

    if (
      normalizedAction !== 'archive'
      && normalizedAction !== 'restore'
    ) {
      setEditStatus('Unsupported project lifecycle action.');
      return;
    }

    if (
      normalizedAction === 'archive'
      && !canArchiveWorkRegister
    ) {
      setEditStatus(
        'Only Project Managers, Project Management Leads, and Project Team Coordinators can archive projects.'
      );
      return;
    }

    if (
      normalizedAction === 'restore'
      && !canRestoreWorkRegister
    ) {
      setEditStatus(
        'Only Project Managers, Project Management Leads, and Project Team Coordinators can restore archived projects.'
      );
      return;
    }

    if (
      normalizedAction === 'archive'
      && selectedWorkItemIsArchived
    ) {
      setEditStatus('This project is already archived.');
      return;
    }

    if (
      normalizedAction === 'restore'
      && !selectedWorkItemIsArchived
    ) {
      setEditStatus('Only archived projects can be restored.');
      return;
    }

    const actionLabel =
      normalizedAction === 'archive'
        ? 'Archive'
        : 'Restore';

    const reason = window.prompt(
      `${actionLabel} ${projectName}? Enter the ${normalizedAction} reason:`
    );

    if (!reason || !reason.trim()) {
      setEditStatus(
        `${actionLabel} cancelled. A reason is required.`
      );
      return;
    }

    const confirmationMessage =
      normalizedAction === 'archive'
        ? 'Archive this project? It will leave the Active view but remain under Closed / Historical and in reporting.'
        : 'Restore this project to the Active Work Register?';

    if (!window.confirm(confirmationMessage)) {
      setEditStatus(`${actionLabel} cancelled.`);
      return;
    }

    setEditStatus(
      normalizedAction === 'archive'
        ? 'Archiving project...'
        : 'Restoring project...'
    );

    try {
      const result = await postJson(
        '/api/work-register/projects/lifecycle',
        {
          projectId,
          action: normalizedAction,
          reason: reason.trim()
        }
      );

      setEditStatus(
        result.message
        || (
          normalizedAction === 'archive'
            ? 'Project archived.'
            : 'Project restored.'
        )
      );

      closeEditDrawer();

      window.setTimeout(() => {
        window.location.reload();
      }, 300);
    } catch (error) {
      setEditStatus(
        error instanceof Error
          ? error.message
          : `Unable to ${normalizedAction} project.`
      );
    }
  }


  // 055D_6B2_PROJECT_LIFECYCLE_UI_ACTIONS

  async function saveProjectSetup(event) {
    event.preventDefault();

    if (!selectedWorkItem) return;

    if (!canEditWorkRegister) {
      setEditStatus('This page is view-only for your role. Only Project Managers, Project Management Leads, and Project Team Coordinators can save changes.');
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
    addIfSelected('sellQuoteNumber');
    addIfSelected('salesforceIdNumber');
    addIfSelected('certiniaIdNumber');

    const originalPoProject = purchaseOrderForProject(selectedWorkItem.workId);
    const originalPo = originalPoProject?.purchaseOrder || null;
    const poChanged =
      Boolean(editForm.purchaseOrderRequired) !== Boolean(originalPoProject?.purchaseOrderRequired)
      || String(editForm.poNumber || '').trim() !== String(originalPo?.poNumber || '').trim()
      || String(editForm.authorizedAmount ?? '') !== String(originalPo?.authorizedAmount ?? '')
      || dateOnly(editForm.poEffectiveStartDate) !== dateOnly(originalPo?.effectiveStartDate)
      || dateOnly(editForm.poEffectiveEndDate) !== dateOnly(originalPo?.effectiveEndDate)
      || String(editForm.poCustomerReference || '').trim() !== String(originalPo?.customerReference || '').trim();

    if (Object.keys(payload).length <= 3 && !poChanged) {
      setEditStatus('No setup or purchase-order changes were selected.');
      return;
    }
    // 055C_3_WORK_REGISTER_CHANGED_FIELD_PAYLOAD_END

    setEditStatus('Saving project setup and purchase order...');
    setPurchaseOrderStatus('');

    try {
      let result = null;

      if (Object.keys(payload).length > 3) {
        result = await postJson('/api/work-register/projects/update', projectPulseAttachEditSaveIdentity(payload));
      }

      if (poChanged) {
        const poResult = await savePurchaseOrder(selectedWorkItem.workId, editForm);
        setPurchaseOrderStatus(poResult?.message || 'Purchase order saved.');
      }

      setEditStatus(result?.message || 'Project setup and purchase order saved.');
      await Promise.all([load(), loadPurchaseOrders()]);

      window.setTimeout(() => {
        closeEditDrawer();
      }, 800);
    } catch (error) {
      setEditStatus(error instanceof Error ? error.message : 'Unable to save project setup or purchase order.');
    }
  }


  return (
    <section className={`work-register-center ${isCreateMode ? 'create-mode' : 'edit-mode'}`}>
      <div className="work-register-header">
        <div>
          <p className="eyebrow">{isCreateMode ? 'Module 055D' : 'Module 055C'}</p>
          <h2>{isCreateMode ? 'Create Work Register' : 'Edit Work Register'}</h2>
          <p className="muted">
            {isCreateMode
              ? 'Create new work from either GSD documents or a connected SELL record. Only Project Team Coordinators can complete this workflow.'
              : 'Search and edit existing work. Every saved mutation is recorded with the actor, reason, old values, and new values in the Audit tab.'}
          </p>
        </div>

                          {/* 055D_5C_ASSIGNMENT_SAVE_BANNER_RENDER */}
                          {intakeSaveBanner ? (
                            <div className="work-register-save-banner success">{intakeSaveBanner}</div>
                          ) : null}
        {!isCreateMode ? (
          <button data-pp-marker="055C_REFRESH_BUTTON" type="button" className="secondary-action" onClick={load}>
            Refresh
          </button>
        ) : null}
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
                  {workRegisterBillingIdentifierValue(item, 'sellQuoteNumber', 'sell_quote_number') ? <small>SELL Quote: {workRegisterBillingIdentifierValue(item, 'sellQuoteNumber', 'sell_quote_number')}</small> : null}
                  {workRegisterBillingIdentifierValue(item, 'salesforceIdNumber', 'salesforce_id_number') ? <small>Salesforce ID: {workRegisterBillingIdentifierValue(item, 'salesforceIdNumber', 'salesforce_id_number')}</small> : null}
                  {workRegisterBillingIdentifierValue(item, 'certiniaIdNumber', 'certinia_id_number') ? <small>Certinia ID: {workRegisterBillingIdentifierValue(item, 'certiniaIdNumber', 'certinia_id_number')}</small> : null}
                  {purchaseOrderForProject(item.workId)?.purchaseOrder?.poNumber ? (
                    <small className="work-register-po-value">
                      PO: {purchaseOrderForProject(item.workId).purchaseOrder.poNumber}
                    </small>
                  ) : (
                    <small>PO: Not configured</small>
                  )}
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



            {isCreateMode && canCreateWorkRegister && !intakeWizardOpen ? (


              <button type="button" className="work-register-create-button" onClick={() => setIntakeWizardOpen(true)}>


                Resume Create Work Register


              </button>


            ) : null}





            {isCreateMode && canCreateWorkRegister && intakeWizardOpen ? (


              <div className="work-register-create-overlay">


                <aside className="work-register-create-wizard">


                  <header>


                    <div>


                      <span>MODULE 055D · CREATE WORK REGISTER</span>


                      <h3>Create from GSD or SELL</h3>


                      <p>Choose a controlled source. SELL supplies the authoritative project name and Actual Rate / Pricing / Rate Review; GSD uses the existing extraction and review workflow.</p>


                    </div>


                    {!isCreateMode ? <button type="button" onClick={() => setIntakeWizardOpen(false)}>Close</button> : null}


                  </header>





                  {intakeWizardStatus ? (


                    <div className="work-register-banner">{intakeWizardStatus}</div>


                  ) : null}





                  <form
                    className="work-register-intake-form"
                    onSubmit={intakeForm.sourceMode === 'sell' ? importSellIntakePackage : uploadInitialIntakePackage}
                  >


                    <section>
                <h4>1. Choose creation source</h4>
                <div className="work-register-source-choice" role="radiogroup" aria-label="Work Register creation source">
                  <label className={intakeForm.sourceMode === 'gsd' ? 'selected' : ''}>
                    <input
                      type="radio"
                      name="sourceModeChoice"
                      value="gsd"
                      checked={intakeForm.sourceMode === 'gsd'}
                      onChange={() => updateIntakeField('sourceMode', 'gsd')}
                    />
                    <strong>Import from GSD</strong>
                    <small>Upload GSD/SOW files, extract them, and review the mapped fields.</small>
                  </label>
                  <label className={intakeForm.sourceMode === 'sell' ? 'selected' : ''}>
                    <input
                      type="radio"
                      name="sourceModeChoice"
                      value="sell"
                      checked={intakeForm.sourceMode === 'sell'}
                      onChange={() => updateIntakeField('sourceMode', 'sell')}
                    />
                    <strong>Import from SELL</strong>
                    <small>Use Module 026 to retrieve the authoritative project name and Actual Rate / Pricing / Rate Review.</small>
                  </label>
                </div>

                <h4>2. Work details</h4>
                <div className="work-register-edit-grid">
                  <label>
                    Work type
                    <select
                      value={intakeForm.requestedWorkType}
                      onChange={(event) => updateIntakeField('requestedWorkType', event.target.value)}
                      name="requestedWorkType"
                    >
                      {['Project', 'IQS', 'Service Request', 'Pre-sales', 'Internal Project', 'Other'].map((type) => (
                        <option value={type} key={type}>{type}</option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Contract type
                    <select
                      value={intakeForm.contractType}
                      onChange={(event) => updateIntakeField('contractType', event.target.value)}
                      name="contractType"
                    >
                      <option value="FP">Fixed Price (FP)</option>
                      <option value="TM">Time and Material (TM)</option>
                      <option value="Internal">Internal</option>
                      <option value="Presales">Presales</option>
                      <option value="Other">Other</option>
                    </select>
                  </label>

                  <label>
                    Customer
                    <select
                      name="customerId"
                      value={intakeForm.customerId}
                      onChange={(event) => updateIntakeField('customerId', event.target.value)}
                      required
                    >
                      <option value="">Select customer from Customer Directory</option>
                      {editCustomerOptions.map((customer) => (
                        <option value={intakeCustomerOptionIdSafe(customer)} key={intakeCustomerOptionIdSafe(customer)}>
                          {intakeCustomerOptionNameSafe(customer)}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    {intakeForm.sourceMode === 'sell' ? 'Project / Work Name (from SELL)' : 'Project / Work Name'}
                    <input
                      type="text"
                      name="projectNameHint"
                      value={intakeForm.projectNameHint}
                      onChange={(event) => updateIntakeField('projectNameHint', event.target.value)}
                      placeholder={intakeForm.sourceMode === 'sell' ? 'Imported from SELL after connection' : 'Optional; GSD extraction should populate this'}
                      disabled={intakeForm.sourceMode === 'sell'}
                    />
                  </label>
                  {intakeForm.sourceMode === 'sell' ? (
                    <label>
                      SELL deal / quote / opportunity record ID
                      <input
                        type="text"
                        value={intakeForm.sellRecordId || ''}
                        onChange={(event) => updateIntakeField('sellRecordId', event.target.value)}
                        placeholder="Required SELL record ID"
                        required
                      />
                    </label>
                  ) : null}
                  {/* 055D_5A_CREATE_BILLING_IDENTIFIERS */}
                  <label>
                    SELL Quote <span className="optional-pill">Optional</span>
                    <input
                      type="text"
                      value={intakeForm.sellQuoteNumber || ''}
                      onChange={(event) => updateIntakeForm('sellQuoteNumber', event.target.value)}
                      placeholder={intakeForm.sourceMode === 'sell' ? 'Imported from SELL' : 'Optional SELL quote number'}
                      disabled={intakeForm.sourceMode === 'sell'}
                    />
                  </label>
                  <label>
                    Salesforce ID <span className="optional-pill">Optional</span>
                    <input
                      type="text"
                      value={intakeForm.salesforceIdNumber || ''}
                      onChange={(event) => updateIntakeForm('salesforceIdNumber', event.target.value)}
                      placeholder="Optional Salesforce opportunity/account ID"
                    />
                  </label>
                  <label>
                    Certinia ID <span className="optional-pill">Optional</span>
                    <input
                      type="text"
                      value={intakeForm.certiniaIdNumber || ''}
                      onChange={(event) => updateIntakeForm('certiniaIdNumber', event.target.value)}
                      placeholder="Optional Certinia project ID"
                    />
                  </label>

                    {/* 055D_5K_CREATE_SOW_SIGNED_DATE */}
                    <label>
                      SOW Signed Date
                      <input
                        type="date"
                        value={intakeForm.sowSignedDate || ''}
                        onChange={(event) => updateIntakeForm('sowSignedDate', event.target.value)}
                      />
                    </label>
                </div>

                {intakeForm.sourceMode === 'gsd' ? (
                <div className="work-register-intake-upload-grid">
                  <label className="work-register-intake-upload-card">
                    <strong>GSD upload</strong>
                    <small>Primary source for AE, SA, SAA, rates, tasks, and hours.</small>
                    <input type="file" name="gsdFile" />
                  </label>

                  <label className="work-register-intake-upload-card">
                    <strong>SOW upload</strong>
                    <small>Source for scope, project name, customer approval context, and contractual support.</small>
                    <input type="file" name="sowFile" />
                  </label>

                  <label className="work-register-intake-upload-card">
                    <strong>Optional approval / PO / email</strong>
                    <small>Use for customer approval, PO, signed email, or supporting intake evidence.</small>
                    <input type="file" name="approvalFile" />
                  </label>
                </div>
                ) : (
                  <div className="work-register-sell-authority-notice">
                    <strong>SELL authority boundary</strong>
                    <p>The project name and all Actual Rate / Pricing / Rate Review rows are read from SELL through Module 026 and cannot be overwritten during review.</p>
                  </div>
                )}

                <div className="work-register-intake-primary-submit" data-marker="055D_2E_PRIMARY_UPLOAD_BUTTON">
                  <button type="submit" className="primary-action">
                    {intakeForm.sourceMode === 'sell' ? 'Connect to SELL and import' : 'Upload and extract current intake package'}
                  </button>
                  <span className="muted">
                    {intakeForm.sourceMode === 'sell'
                      ? 'This uses the configured Module 026 API key or OAuth connection and creates an audited intake package.'
                      : 'This creates the package and immediately extracts the current GSD/SOW for review.'}
                  </span>
                </div>

                {intakeForm.sourceMode === 'gsd' && intakeRequiresProjectDocuments() ? (
                  <div className="work-register-intake-checkboxes">
                    <label className="checkbox-line">
                      <input
                        type="checkbox"
                        checked={intakeForm.skipGsd}
                        onChange={(event) => updateIntakeField('skipGsd', event.target.checked)}
                      />
                      No GSD available / manual intake
                    </label>

                    <label className="checkbox-line">
                      <input
                        type="checkbox"
                        checked={intakeForm.skipSow}
                        onChange={(event) => updateIntakeField('skipSow', event.target.checked)}
                      />
                      No SOW available yet
                    </label>
                  </div>
                ) : intakeForm.sourceMode === 'gsd' ? (
                  <p className="muted">GSD/SOW are optional for this work type. Service Requests can proceed by manual intake in a later step.</p>
                ) : null}
              </section>





                    <section>


                      <h4>3. Purchase Order / Billing</h4>
                      <p className="muted">Optional during intake. The PO is saved immediately after the Work Register project is created.</p>

                      <label className="checkbox-line" data-projectpulse-create-work-po-editor="true">
                        <input
                          type="checkbox"
                          checked={intakeForm.purchaseOrderRequired === true}
                          onChange={(event) => updateIntakeField('purchaseOrderRequired', event.target.checked)}
                        />
                        PO required for billing
                      </label>

                      <div className="work-register-edit-grid">
                        <label>
                          PO Number
                          <input
                            type="text"
                            value={intakeForm.poNumber || ''}
                            onChange={(event) => updateIntakeField('poNumber', event.target.value)}
                            placeholder="Customer purchase-order number"
                          />
                        </label>

                        <label>
                          Authorized PO amount
                          <input
                            type="number"
                            min="0"
                            step="0.01"
                            value={intakeForm.authorizedAmount ?? ''}
                            onChange={(event) => updateIntakeField('authorizedAmount', event.target.value)}
                          />
                        </label>

                        <label>
                          Effective start date
                          <input
                            type="date"
                            value={intakeForm.poEffectiveStartDate || ''}
                            onChange={(event) => updateIntakeField('poEffectiveStartDate', event.target.value)}
                          />
                        </label>

                        <label>
                          Effective end date
                          <input
                            type="date"
                            value={intakeForm.poEffectiveEndDate || ''}
                            onChange={(event) => updateIntakeField('poEffectiveEndDate', event.target.value)}
                          />
                        </label>

                        <label className="full-width">
                          Customer reference
                          <input
                            type="text"
                            value={intakeForm.poCustomerReference || ''}
                            onChange={(event) => updateIntakeField('poCustomerReference', event.target.value)}
                            placeholder="Customer PO description or reference"
                          />
                        </label>
                      </div>

                      <h4>4. Intake notes and audit reason</h4>


                      <label>


                        Notes


                        <textarea


                          rows={3}


                          name="notes"


                          value={intakeForm.notes}


                          onChange={(event) => updateIntakeField('notes', event.target.value)}


                          placeholder="Optional intake notes for Project Team Coordinator review."


                        />


                      </label>





                      <label>


                        Intake reason


                        <textarea


                          rows={3}


                          name="reason"


                          value={intakeForm.reason}


                          onChange={(event) => updateIntakeField('reason', event.target.value)}


                          placeholder="Required. Example: New customer project submitted with GSD and signed SOW."


                          required


                        />


                      </label>


                    </section>





                    <section>


                      <h4>5. Import and review status</h4>


                      {intakePackageResult ? (


                        <div className="work-register-intake-result">


                          <strong>Intake package uploaded</strong>


                          <small>Package ID: {intakePackageResult.intakePackageId}</small>


                          <small>Requested work type: {intakePackageResult.requestedWorkType}</small>


                          <small>Project / Work Name: {intakePackageResult.projectNameHint || 'Pending parser'}</small>


                          <small>Customer: {intakePackageResult.customerHint || 'Pending parser'}</small>


                          <small>Documents uploaded: {intakePackageResult.uploadedDocumentCount}</small>


                          <small>Extraction status: {labelize(intakePackageResult.extractionStatus || 'pending parser')}</small>


                          <p className="muted">055D.2 will parse the GSD/SOW and populate AE, SA, SAA, rates, hours, and tasks for review before committing to Work Register.</p>


                        </div>


                      ) : (


                        <p className="muted">Upload the source documents using the button above. The system creates the intake package and immediately extracts this current GSD/SOW.</p>


                      )}


                    </section>






              <section>
                <h4>6. Source extraction and review mapping</h4>
                <p className="muted">
                  GSD packages are extracted from the uploaded workbook. SELL packages arrive already extracted through Module 026.
                  Review the permitted fields before creating the Work Register record.
                </p>

                {intakeReviewStatus ? (
                  <div className="work-register-banner">{intakeReviewStatus}</div>
                ) : null}

                <div className="work-register-intake-review-toolbar">
                  <button type="button" className="secondary-action" onClick={loadIntakePackages}>
                    Load intake packages
                  </button>
                  {intakePackageResult?.intakePackageId ? (
                    <button type="button" className="secondary-action" onClick={() => openIntakeReview(intakePackageResult.intakePackageId)}>
                      Review last uploaded package
                    </button>
                  ) : null}
                </div>

                <div className="work-register-intake-package-list">
                  {projectPulseVisibleIntakePackages(intakePackages).map((pkg) => (
                    <article key={pkg.intakePackageId}>
                      <div>
                        <strong>{pkg.projectNameHint || 'Pending project name'}</strong>
                        <small>Package: {pkg.intakePackageId}</small>
                        <small>Type: {pkg.requestedWorkType}</small>
                        <small>Customer: {pkg.customerHint || 'not set'}</small>
                        <small>Documents: {pkg.documentCount}</small>
                        <small>Extraction: {labelize(pkg.extractionStatus || 'not started')}</small>
                        <small>Review: {labelize(pkg.reviewStatus || 'not started')}</small>
                      </div>
                      <div>
                        {pkg.sourceMode !== 'sell_import' ? (
                          <button type="button" className="secondary-action" onClick={() => runIntakeExtraction(pkg.intakePackageId)}>
                            Extract
                          </button>
                        ) : null}
                        <button type="button" className="secondary-action" onClick={() => openIntakeReview(pkg.intakePackageId)}>
                          Review
                        </button>
                      </div>
                    </article>
                  ))}
                </div>

                {selectedIntakeReview && intakeReviewForm ? (
                  <div className="work-register-intake-review-panel">
                    <h5>Review extracted mapping</h5>

                    <div className="work-register-edit-grid">
                      <label>
                        Project / work name
                        <input
                          type="text"
                          value={intakeReviewForm.projectName}
                          onChange={(event) => updateIntakeReviewForm('projectName', event.target.value)}
                          disabled={sellAuthoritativeReview}
                        />
                        {sellAuthoritativeReview ? <small>Authoritative value supplied by SELL.</small> : null}
                      </label>

                      <label>
                        Matched customer
                        <select
                          value={intakeReviewForm.customerId}
                          onChange={(event) => updateIntakeReviewForm('customerId', event.target.value)}
                          required
                        >
                          <option value="">Select customer from Customer Directory</option>
                          {editCustomerOptions.map((customer) => (
                            <option value={intakeCustomerOptionIdSafe(customer)} key={intakeCustomerOptionIdSafe(customer)}>
                              {intakeCustomerOptionNameSafe(customer)}
                            </option>
                          ))}
                        </select>
                      </label>

                      <label>
                        AE
                        <input
                          type="text"
                          value={intakeReviewForm.accountExecutiveName}
                          onChange={(event) => updateIntakeReviewForm('accountExecutiveName', event.target.value)}
                        />
                      </label>

                      <label>
                        SA
                        <input
                          type="text"
                          value={intakeReviewForm.solutionArchitectName}
                          onChange={(event) => updateIntakeReviewForm('solutionArchitectName', event.target.value)}
                        />
                      </label>

                      <label>
                        SAA / Inside Sales
                        <input
                          type="text"
                          value={intakeReviewForm.insideSalesName}
                          onChange={(event) => updateIntakeReviewForm('insideSalesName', event.target.value)}
                        />
                      </label>

                      <label>
                        Work type
                        <select
                          value={intakeReviewForm.requestedWorkType}
                          onChange={(event) => updateIntakeReviewForm('requestedWorkType', event.target.value)}
                        >
                          {['Project', 'IQS', 'Service Request', 'Pre-sales', 'Internal Project', 'Other'].map((type) => (
                            <option value={type} key={type}>{type}</option>
                          ))}
                        </select>
                      </label>

                      <label>
                        Contract type
                        <select
                          value={intakeReviewForm.contractType}
                          onChange={(event) => updateIntakeReviewForm('contractType', event.target.value)}
                        >
                          {['Fixed Price', 'T&M', 'Internal', 'Pre-Sales', 'Other'].map((type) => (
                            <option value={type} key={type}>{type}</option>
                          ))}
                        </select>
                      </label>

                      <label>
                        PM hours
                        <input
                          type="text"
                          value={intakeReviewForm.pmHours}
                          onChange={(event) => updateIntakeReviewForm('pmHours', event.target.value)}
                        />
                      </label>

                      <label>
                        Engineering hours
                        <input
                          type="text"
                          value={intakeReviewForm.engineeringHours}
                          onChange={(event) => updateIntakeReviewForm('engineeringHours', event.target.value)}
                        />
                      </label>

                      <label>
                        Travel hours
                        <input
                          type="text"
                          value={intakeReviewForm.travelHours}
                          onChange={(event) => updateIntakeReviewForm('travelHours', event.target.value)}
                        />
                      </label>
                    </div>


                    <div className="work-register-gsd-review-summary">
                      <div>
                        <span>{sellAuthoritativeReview ? 'SELL project list price' : 'GSD project list price'}</span>
                        <strong>{moneyReviewValue(intakeReviewForm.projectListPrice)}</strong>
                      </div>
                      <div>
                        <span>Calculated rate total</span>
                        <strong>{moneyReviewValue(calculatedGsdRateTotal())}</strong>
                      </div>
                      <div>
                        <span>Task hours total</span>
                        <strong>{calculatedGsdTaskHoursTotal().toLocaleString()} hrs</strong>
                      </div>
                      <div>
                        <span>Pricing source</span>
                        <strong>{sellAuthoritativeReview ? 'SELL authoritative snapshot' : 'GSD snapshot'}</strong>
                      </div>
                    </div>

                    <div className="work-register-gsd-review-table">
                      <div className="work-register-gsd-review-table-header">
                        <div>
                          <h5>{sellAuthoritativeReview ? 'SELL Actual Rate / Pricing / Rate Review' : 'GSD Pricing / Rate Review'}</h5>
                          <p className="muted">
                            {sellAuthoritativeReview
                              ? 'These source-locked rows came directly from SELL and become the project-specific rate snapshot.'
                              : 'These rows become the project-specific rate snapshot when the intake is committed to Work Register.'}
                          </p>
                        </div>
                        {!sellAuthoritativeReview ? (
                          <button type="button" className="secondary-action" onClick={addGsdRateRow}>
                            Add rate row
                          </button>
                        ) : null}
                      </div>

                      {gsdRateRows().length > 0 ? (
                        <div className="work-register-gsd-rate-grid">
                          <div className="work-register-gsd-grid-heading">Use</div>
                          <div className="work-register-gsd-grid-heading">SKU</div>
                          <div className="work-register-gsd-grid-heading">Description / Role</div>
                          <div className="work-register-gsd-grid-heading">Rate</div>
                          <div className="work-register-gsd-grid-heading">Hours</div>
                          <div className="work-register-gsd-grid-heading">Extended</div>

                          {gsdRateRows().map((rate, index) => (
                            <div className="work-register-gsd-grid-row" key={`gsd-rate-${index}`}>
                              <label className="checkbox-line">
                                <input
                                  type="checkbox"
                                  checked={rate?.include !== false}
                                  onChange={(event) => updateIntakeReviewArrayItem('ratesText', index, 'include', event.target.checked)}
                                  disabled={sellAuthoritativeReview}
                                />
                              </label>

                              <input
                                type="text"
                                value={rate?.sku || ''}
                                onChange={(event) => updateIntakeReviewArrayItem('ratesText', index, 'sku', event.target.value)}
                                placeholder="SKU"
                                disabled={sellAuthoritativeReview}
                              />

                              <input
                                type="text"
                                value={rate?.description || rate?.role || ''}
                                onChange={(event) => updateIntakeReviewArrayItem('ratesText', index, 'description', event.target.value)}
                                placeholder="Description / role"
                                disabled={sellAuthoritativeReview}
                              />

                              <input
                                type="number"
                                step="0.01"
                                value={rate?.rate ?? rate?.rateAmount ?? rate?.unitRate ?? 0}
                                onChange={(event) => updateIntakeReviewArrayItem('ratesText', index, 'rate', Number(event.target.value || 0))}
                                disabled={sellAuthoritativeReview}
                              />

                              <input
                                type="number"
                                step="0.25"
                                value={rate?.hours ?? rate?.quantity ?? rate?.qty ?? 0}
                                onChange={(event) => updateIntakeReviewArrayItem('ratesText', index, 'hours', Number(event.target.value || 0))}
                                disabled={sellAuthoritativeReview}
                              />

                              <strong>{moneyReviewValue(intakeRateExtended(rate))}</strong>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="work-register-gsd-empty-review">
                          No GSD rate rows were extracted. Add rate rows manually or rerun extraction after confirming the GSD format.
                        </div>
                      )}
                    </div>

                    <div className="work-register-gsd-review-table">
                      <div className="work-register-gsd-review-table-header">
                        <div>
                          <h5>Task / Hours Review</h5>
                          <p className="muted">Task rows are consolidated from Totals Sheet Resource Totals. PM and Engineering roles with hours and cost become tasks; travel is shown in pricing only.</p>
                        </div>
                        <button type="button" className="secondary-action" onClick={addGsdTaskRow}>
                          Add task row
                        </button>
                      </div>

                      {gsdTaskRows().length > 0 ? (
                        <div className="work-register-gsd-task-grid">
                          <div className="work-register-gsd-grid-heading">Use</div>
                          <div className="work-register-gsd-grid-heading">Phase</div>
                          <div className="work-register-gsd-grid-heading">Task</div>
                          <div className="work-register-gsd-grid-heading">Role</div>
                          <div className="work-register-gsd-grid-heading">Regular</div>
                          <div className="work-register-gsd-grid-heading">OT</div>
                          <div className="work-register-gsd-grid-heading">Reserve</div>
                          <div className="work-register-gsd-grid-heading">Total</div>

                          {gsdTaskRows().map((task, index) => (
                            <div className="work-register-gsd-grid-row" key={`gsd-task-${index}`}>
                              <label className="checkbox-line">
                                <input
                                  type="checkbox"
                                  checked={task?.include !== false}
                                  onChange={(event) => updateIntakeReviewArrayItem('tasksText', index, 'include', event.target.checked)}
                                />
                              </label>

                              <input
                                type="text"
                                value={task?.phase || ''}
                                onChange={(event) => updateIntakeReviewArrayItem('tasksText', index, 'phase', event.target.value)}
                                placeholder="Phase"
                              />

                              <input
                                type="text"
                                value={task?.taskName || ''}
                                onChange={(event) => updateIntakeReviewArrayItem('tasksText', index, 'taskName', event.target.value)}
                                placeholder="Task name"
                              />

                              <input
                                type="text"
                                value={task?.engineeringRole || task?.role || ''}
                                onChange={(event) => updateIntakeReviewArrayItem('tasksText', index, 'engineeringRole', event.target.value)}
                                placeholder="Role"
                              />

                              <input
                                type="number"
                                step="0.25"
                                value={task?.regularHours ?? task?.hours ?? 0}
                                onChange={(event) => updateIntakeReviewArrayItem('tasksText', index, 'regularHours', Number(event.target.value || 0))}
                              />

                              <input
                                type="number"
                                step="0.25"
                                value={task?.overtimeHours ?? 0}
                                onChange={(event) => updateIntakeReviewArrayItem('tasksText', index, 'overtimeHours', Number(event.target.value || 0))}
                              />

                              <input
                                type="number"
                                step="0.25"
                                value={task?.reserveHours ?? 0}
                                onChange={(event) => updateIntakeReviewArrayItem('tasksText', index, 'reserveHours', Number(event.target.value || 0))}
                              />

                              <strong>{intakeTaskTotal(task).toLocaleString()} hrs</strong>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="work-register-gsd-empty-review">
                          No task rows were extracted. Add task rows manually or rerun extraction after confirming the GSD format.
                        </div>
                      )}
                    </div>


                    <div className="work-register-intake-assignment-review">
                      <div className="work-register-gsd-review-table-header">
                        <div>
                          <h5>Assignment Review</h5>
                          <p className="muted">Assign PM, Project Coordinator / Project Management Team, and engineer ownership before this intake is committed into Work Register.</p>
                        </div>
                      </div>

                      <div className="work-register-assignment-lead-grid">
                        <label>
                          Project Manager
                          <select
                            value={intakeReviewForm.projectManagerUserId || ''}
                            onChange={(event) => updateIntakeReviewForm('projectManagerUserId', event.target.value)}
                          >
                            <option value="">Select PM / Project Management Team member</option>
                            {intakeAssignableUsers('pm').map((user) => (
                              <option value={intakeUserOptionId(user)} key={intakeUserOptionId(user)}>
                                {intakeUserOptionName(user)}
                              </option>
                            ))}
                          </select>
                        </label>

                        <label>
                          Project Coordinator / Project Management Team
                          <select
                            value={intakeReviewForm.projectCoordinatorUserId || ''}
                            onChange={(event) => updateIntakeReviewForm('projectCoordinatorUserId', event.target.value)}
                          >
                            <option value="">Select PC / Project Management Team member</option>
                            {intakeAssignableUsers('pc').map((user) => (
                              <option value={intakeUserOptionId(user)} key={intakeUserOptionId(user)}>
                                {intakeUserOptionName(user)}
                              </option>
                            ))}
                          </select>
                        </label>
                      </div>

                      <div className="work-register-assignment-pool-counts" data-marker="055D_3B_POOL_COUNTS">
                        <span>PM/PC pool: <strong>{intakeAssignableUsers('pm').length}</strong></span>
                        <span>Engineering pool: <strong>{intakeAssignableUsers('engineer').length}</strong></span>
                      </div>

                      <div className="work-register-assignment-save-bar" data-marker="055D_3B_ASSIGNMENT_SAVE_BUTTON">
                        {/* 055D_5H_ASSIGNMENT_SAVE_BANNER */}
                        {intakeSaveBanner ? (
                          <div className="work-register-save-banner success">{intakeSaveBanner}</div>
                        ) : null}

                        {/* 055D_5G1_ASSIGNMENT_SAVE_BANNER_TOP */}
                        {intakeSaveBanner ? (
                          <div className="work-register-save-banner success">{intakeSaveBanner}</div>
                        ) : null}

                        <button type="button" className="primary-action" onClick={saveIntakeReviewMapping}>
                          Save Assignment Configuration
                        </button>
                        <span className="muted">Saves PM/PC selections, engineer assignments, multi-engineer rosters, rates, and task review back to the intake package.</span>
                      </div>

                      <div className="work-register-intake-task-assignment-list">
                        {gsdTaskRows().filter((task) => task?.include !== false).map((task, taskIndex) => (
                          <div className="work-register-intake-task-assignment-card" key={`intake-assignment-${taskIndex}`}>
                            <div className="work-register-intake-task-assignment-heading">
                              <div>
                                <strong>{task.taskName || task.phase || `Task ${taskIndex + 1}`}</strong>
                                <span>{task.phase || 'No phase'} · {intakeTaskTotal(task).toLocaleString()} hrs · {moneyReviewValue(task.laborListPrice || 0)}</span>
                              </div>

                              <label className="checkbox-line">
                                <input
                                  type="checkbox"
                                  checked={(task.assignmentMode || 'single') === 'multiple'}
                                  onChange={(event) => updateTaskAssignmentMode(taskIndex, event.target.checked)}
                                />
                                Multiple engineers
                              </label>
                            </div>

                            <div className="work-register-intake-task-assignment-roster">
                              <div className="work-register-assignment-grid-heading">Primary</div>
                              <div className="work-register-assignment-grid-heading">Engineer</div>
                              <div className="work-register-assignment-grid-heading">Hours</div>
                              <div className="work-register-assignment-grid-heading">Allocation %</div>
                              <div className="work-register-assignment-grid-heading">Action</div>

                              {taskAssignmentRows(task).map((assignment, assignmentIndex) => (
                                <div className="work-register-assignment-grid-row" key={`intake-assignment-${taskIndex}-${assignmentIndex}`}>
                                  <label className="checkbox-line">
                                    <input
                                      type="radio"
                                      name={`primary-engineer-${taskIndex}`}
                                      checked={assignment.isPrimary === true}
                                      onChange={() => updateTaskAssignment(taskIndex, assignmentIndex, 'isPrimary', true)}
                                    />
                                  </label>

                                  <select
                                    value={assignment.engineerUserId || ''}
                                    onChange={(event) => updateTaskAssignment(taskIndex, assignmentIndex, 'engineerUserId', event.target.value)}
                                  >
                                    <option value="">{projectPulseTaskAssignmentPoolPlaceholder(task)}</option>
                                    {intakeAssignableUsers(projectPulseTaskAssignmentPoolKind(task)).map((user) => (
                                      <option value={intakeUserOptionId(user)} key={intakeUserOptionId(user)}>
                                        {intakeUserOptionName(user)}
                                      </option>
                                    ))}
                                  </select>

                                  <input
                                    type="number"
                                    step="0.25"
                                    value={assignment.hours ?? assignment.allocatedHours ?? 0}
                                    onChange={(event) => updateTaskAssignment(taskIndex, assignmentIndex, 'hours', Number(event.target.value || 0))}
                                  />

                                  <input
                                    type="number"
                                    step="1"
                                    min="0"
                                    max="100"
                                    value={assignment.allocationPercent ?? 0}
                                    onChange={(event) => updateTaskAssignment(taskIndex, assignmentIndex, 'allocationPercent', Number(event.target.value || 0))}
                                  />

                                  <button
                                    type="button"
                                    className="secondary-action"
                                    onClick={() => removeTaskAssignmentRow(taskIndex, assignmentIndex)}
                                    disabled={taskAssignmentRows(task).length <= 1}
                                  >
                                    Remove
                                  </button>
                                </div>
                              ))}
                            </div>

                            <div className="work-register-intake-task-assignment-footer">
                              <span>
                                Assigned hours: <strong>{taskAssignmentHoursTotal(task).toLocaleString()} hrs</strong>
                              </span>
                              <button type="button" className="secondary-action" onClick={() => addTaskAssignmentRow(taskIndex)}>
                                Add engineer
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    </div>

                    {/* 055D_5H_FINAL_CREATE_WORK_REGISTER_ACTION */}
                    <div className="work-register-final-save-panel">
                      <div>
                        <h5>Final Save</h5>
                        <p className="muted">
                          Creates the Work Register record from this reviewed intake package. Use Save Assignment Configuration first when you want to save changes without creating the record yet.
                        </p>
                      </div>
                      {intakeSaveBanner ? (
                        <div className="work-register-save-banner success">{intakeSaveBanner}</div>
                      ) : null}
                      <button type="button" className="primary-action" onClick={createWorkRegisterFromReviewedIntake}>
                        Save Project / Create Work Register
                      </button>
                    </div>


                    <details className="work-register-gsd-json-details">
                      <summary>Advanced: raw extracted JSON</summary>

                    <label>
                      Rates JSON
                      <textarea
                        rows={7}
                        value={intakeReviewForm.ratesText}
                        onChange={(event) => updateIntakeReviewForm('ratesText', event.target.value)}
                        disabled={sellAuthoritativeReview}
                      />
                    </label>

                    <label>
                      Tasks / hours JSON
                      <textarea
                        rows={7}
                        value={intakeReviewForm.tasksText}
                        onChange={(event) => updateIntakeReviewForm('tasksText', event.target.value)}
                      />
                    </label>

                    <label>
                      Parser notes JSON
                      <textarea
                        rows={4}
                        value={intakeReviewForm.parserNotesText}
                        onChange={(event) => updateIntakeReviewForm('parserNotesText', event.target.value)}
                      />
                    </label>
                    </details>

                    <div className="work-register-create-actions">
                      <button type="button" className="secondary-action" onClick={saveIntakeReviewMapping}>
                        Save reviewed mapping only
                      </button>
                    </div>
                  </div>
                ) : null}
              </section>

              <div className="work-register-create-actions">


                      <button type="button" className="secondary-action" onClick={resetIntakeWizard}>Reset</button>


                      <button type="submit" className="primary-action">
                        {intakeForm.sourceMode === 'sell' ? 'Connect to SELL and import' : 'Upload and extract intake package'}
                      </button>


                    </div>


                  </form>


                </aside>


              </div>


            ) : null}








      {selectedWorkItem ? (
        <div className="work-register-drawer-backdrop" role="presentation">
          <aside className="work-register-drawer" aria-label="Work Register project setup editor">
            <div className="work-register-drawer-header">
              <div>
                <p className="eyebrow">{canModifySelectedProject ? 'Edit Project Setup' : 'View Project Setup'}</p>
                <h3>{selectedWorkItem.workName}</h3>
                <p className="muted">
                  {selectedWorkItem.customerName || 'No customer linked'} · {labelize(selectedWorkItem.sourceTable)}
                </p>
              </div>
              <button type="button" className="secondary-action" onClick={closeEditDrawer}>Close</button>
            </div>

            <div className={canModifySelectedProject ? 'work-register-edit-notice allowed' : 'work-register-edit-notice'}>
              {canModifySelectedProject
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
            <>
              {/* 055D_6B2_PROJECT_LIFECYCLE_UI_FRAGMENT_START */}
              <form className="work-register-edit-form" onSubmit={saveProjectSetup}>
              <label>
                Customer
                <select
                  value={editForm.clientId || ''}
                  onChange={(event) => updateEditField('clientId', event.target.value)}
                  disabled={!canModifySelectedProject}
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
                  disabled={!canModifySelectedProject}
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
                  disabled={!canModifySelectedProject}
                >
                  <option value="">Keep current: {selectedWorkItem.projectManager || 'Not assigned'}</option>
                  {pmOptions.map((user) => (
                    <option value={user.userId} key={user.userId}>{user.displayName} {user.isActive === false ? '- inactive' : ''}</option>
                  ))}
                </select>
              </label>

              <label>
                Project Coordinator / Project Management Team
                <select
                  value={editForm.projectCoordinatorUserId || ''}
                  onChange={(event) => updateEditField('projectCoordinatorUserId', event.target.value)}
                  disabled={!canModifySelectedProject}
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
                  disabled={!canModifySelectedProject}
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
                  disabled={!canModifySelectedProject}
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
                  disabled={!canModifySelectedProject}
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
                  disabled={!canModifySelectedProject}
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
                  disabled={!canModifySelectedProject}
                />
              </label>

              <label>
                Estimated end date
                <input
                  type="date"
                  value={editForm.estimatedEndDate || ''}
                  onChange={(event) => updateEditField('estimatedEndDate', event.target.value)}
                  disabled={!canModifySelectedProject}
                />
              </label>

              <label>
                SOW signed date
                <input
                  type="date"
                  value={editForm.sowSignedDate || ''}
                  onChange={(event) => updateEditField('sowSignedDate', event.target.value)}
                  disabled={!canModifySelectedProject}
                />
              </label>
                {/* 055D_5A_EDIT_BILLING_IDENTIFIERS */}
                <label>
                  SELL Quote <span className="optional-pill">Optional</span>
                  <input
                    type="text"
                    value={editForm.sellQuoteNumber || ''}
                    onChange={(event) => updateEditField('sellQuoteNumber', event.target.value)}
                    placeholder={selectedWorkItem.sellQuoteNumber || selectedWorkItem.sell_quote_number || 'Optional SELL quote number'}
                  />
                </label>
                <label>
                  Salesforce ID <span className="optional-pill">Optional</span>
                  <input
                    type="text"
                    value={editForm.salesforceIdNumber || ''}
                    onChange={(event) => updateEditField('salesforceIdNumber', event.target.value)}
                    placeholder={selectedWorkItem.salesforceIdNumber || selectedWorkItem.salesforce_id_number || 'Optional Salesforce ID'}
                  />
                </label>
                <label>
                  Certinia ID <span className="optional-pill">Optional</span>
                  <input
                    type="text"
                    value={editForm.certiniaIdNumber || ''}
                    onChange={(event) => updateEditField('certiniaIdNumber', event.target.value)}
                    placeholder={selectedWorkItem.certiniaIdNumber || selectedWorkItem.certinia_id_number || 'Optional Certinia ID'}
                  />
                </label>

              <div className="work-register-po-section full-width" data-projectpulse-work-register-po-editor="true">
                <div className="work-register-po-heading">
                  <div>
                    <strong>Purchase Order / Billing</strong>
                    <small>Uses the same PO record shown in Invoice & Billing.</small>
                  </div>
                  {purchaseOrderStatus ? <span>{purchaseOrderStatus}</span> : null}
                </div>

                <label className="checkbox-line">
                  <input
                    type="checkbox"
                    checked={editForm.purchaseOrderRequired === true}
                    onChange={(event) => updateEditField('purchaseOrderRequired', event.target.checked)}
                    disabled={!canModifySelectedProject}
                  />
                  PO required for billing
                </label>

                <div className="work-register-edit-grid">
                  <label>
                    PO Number
                    <input
                      type="text"
                      value={editForm.poNumber || ''}
                      onChange={(event) => updateEditField('poNumber', event.target.value)}
                      placeholder="Customer purchase-order number"
                      disabled={!canModifySelectedProject}
                    />
                  </label>

                  <label>
                    Authorized PO amount
                    <input
                      type="number"
                      min="0"
                      step="0.01"
                      value={editForm.authorizedAmount ?? ''}
                      onChange={(event) => updateEditField('authorizedAmount', event.target.value)}
                      placeholder="0.00"
                      disabled={!canModifySelectedProject}
                    />
                  </label>

                  <label>
                    Effective start date
                    <input
                      type="date"
                      value={editForm.poEffectiveStartDate || ''}
                      onChange={(event) => updateEditField('poEffectiveStartDate', event.target.value)}
                      disabled={!canModifySelectedProject}
                    />
                  </label>

                  <label>
                    Effective end date
                    <input
                      type="date"
                      value={editForm.poEffectiveEndDate || ''}
                      onChange={(event) => updateEditField('poEffectiveEndDate', event.target.value)}
                      disabled={!canModifySelectedProject}
                    />
                  </label>

                  <label className="full-width">
                    Customer reference
                    <input
                      type="text"
                      value={editForm.poCustomerReference || ''}
                      onChange={(event) => updateEditField('poCustomerReference', event.target.value)}
                      placeholder="Customer PO description or reference"
                      disabled={!canModifySelectedProject}
                    />
                  </label>
                </div>
              </div>

              <label className="full-width">
                Edit reason
                <textarea
                  rows={3}
                  value={editForm.editReason || ''}
                  onChange={(event) => updateEditField('editReason', event.target.value)}
                  placeholder="Required. Example: Reassigned PM because prior PM left organization."
                  disabled={!canModifySelectedProject}
                />
              </label>

              <div className="work-register-drawer-actions">
                {canModifySelectedProject ? (
                  <button type="submit" className="primary-action">Save changes</button>
                ) : null}
                <button type="button" className="secondary-action" onClick={closeEditDrawer}>Cancel</button>
              </div>

            </form>

            {/* 055D_6B2_PROJECT_LIFECYCLE_UI_START */}
            {selectedWorkItem?.sourceTable === 'projects'
              && (canArchiveWorkRegister || canRestoreWorkRegister) ? (
              <div className="work-register-detail-panel">
                <h4>Project lifecycle</h4>

                {selectedWorkItemIsArchived ? (
                  <>
                    <div className="work-register-banner">
                      This project is archived and read-only. Its tasks, documents, assignments, costs, time, and audit history remain available for historical reporting.
                    </div>

                    {canRestoreWorkRegister ? (
                      <div className="work-register-task-assignment-actions">
                        <button
                          type="button"
                          className="secondary-action"
                          onClick={() => changeWorkRegisterProjectLifecycle('restore')}
                        >
                          Restore Project
                        </button>
                      </div>
                    ) : (
                      <p className="muted">
                        Only an Administrator or Super Administrator can restore this project.
                      </p>
                    )}
                  </>
                ) : (
                  <>
                    <p className="muted">
                      Archiving removes this project from the Active view without deleting its tasks, documents, assignments, costs, time, or audit history.
                    </p>

                    {canArchiveWorkRegister ? (
                      <div className="work-register-task-assignment-actions">
                        <button
                          type="button"
                          className="secondary-action danger"
                          onClick={() => changeWorkRegisterProjectLifecycle('archive')}
                        >
                          Archive Project
                        </button>
                      </div>
                    ) : null}
                  </>
                )}
              </div>
            ) : null}
            {/* 055D_6B2_PROJECT_LIFECYCLE_UI_END */}
              {/* 055D_6B2_PROJECT_LIFECYCLE_UI_FRAGMENT_END */}
            </>
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
                            disabled={!canModifySelectedProject || !task.taskId}
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
                                disabled={!canModifySelectedProject || !task.taskId}
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
                                disabled={!canModifySelectedProject || !task.taskId}
                              />
                            </label>

                            <label>
                              Effective date
                              <input
                                type="date"
                                value={selectedEffectiveDate}
                                onChange={(event) => updateTaskAssignmentForm(task, 'effectiveStartDate', event.target.value)}
                                disabled={!canModifySelectedProject || !task.taskId}
                              />
                            </label>

                            {showClassification ? (
                              <>
                                <label className="checkbox-line">
                                  <input
                                    type="checkbox"
                                    checked={selectedBillable}
                                    onChange={(event) => updateTaskAssignmentForm(task, 'billable', event.target.checked)}
                                    disabled={!canModifySelectedProject || !task.taskId}
                                  />
                                  Billable
                                </label>

                                <label className="checkbox-line">
                                  <input
                                    type="checkbox"
                                    checked={selectedUtilization}
                                    onChange={(event) => updateTaskAssignmentForm(task, 'utilizationEligible', event.target.checked)}
                                    disabled={!canModifySelectedProject || !task.taskId}
                                  />
                                  Utilization eligible
                                </label>
                              </>
                            ) : (
                              <div className="work-register-default-classification">
                                Default: Billable + Utilization eligible
                                <button type="button" onClick={() => updateTaskAssignmentForm(task, 'showClassification', true)} disabled={!canModifySelectedProject}>
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
                                disabled={!canModifySelectedProject || !task.taskId}
                              />
                            </label>

                            <div className="work-register-task-assignment-actions">
                              {canModifySelectedProject ? (
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
                              {canModifySelectedProject ? (
                                <button type="button" className="secondary-action" onClick={() => addRosterEngineer(task)}>
                                  Add engineer
                                </button>
                              ) : null}
                            </div>

                            {!isFlexibleTaskClassificationWorkType(selectedWorkItem?.workType) ? (
                              <div className="work-register-default-classification">
                                Project task default: Billable + Utilization eligible.
                                <button type="button" onClick={() => toggleRosterClassification(task, true)} disabled={!canModifySelectedProject}>
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
                                    disabled={!canModifySelectedProject || !task.taskId}
                                  >
                                    <option value="">Select Engineering / Engineering Team Lead resource</option>
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
                                    disabled={!canModifySelectedProject || !task.taskId}
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
                                    disabled={!canModifySelectedProject || !task.taskId}
                                  />
                                </label>

                                <label>
                                  Effective date
                                  <input
                                    type="date"
                                    value={row.effectiveStartDate || new Date().toISOString().slice(0, 10)}
                                    onChange={(event) => updateRosterEngineer(task, index, 'effectiveStartDate', event.target.value)}
                                    disabled={!canModifySelectedProject || !task.taskId}
                                  />
                                </label>

                                <label className="checkbox-line">
                                  <input
                                    type="checkbox"
                                    checked={row.isPrimary === true}
                                    onChange={(event) => updateRosterEngineer(task, index, 'isPrimary', event.target.checked)}
                                    disabled={!canModifySelectedProject || !task.taskId}
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
                                        disabled={!canModifySelectedProject || !task.taskId}
                                      />
                                      Billable
                                    </label>

                                    <label className="checkbox-line">
                                      <input
                                        type="checkbox"
                                        checked={row.utilizationEligible !== false}
                                        onChange={(event) => updateRosterEngineer(task, index, 'utilizationEligible', event.target.checked)}
                                        disabled={!canModifySelectedProject || !task.taskId}
                                      />
                                      Utilization
                                    </label>
                                  </>
                                ) : null}

                                <button
                                  type="button"
                                  className="secondary-action danger"
                                  onClick={() => removeRosterEngineer(task, index)}
                                  disabled={!canModifySelectedProject || rosterRows.length <= 1}
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
                                disabled={!canModifySelectedProject || !task.taskId}
                              />
                            </label>

                            <div className="work-register-task-assignment-actions">
                              {canModifySelectedProject ? (
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
                  {workRegisterBillingIdentifierValue(selectedWorkItem, 'sellQuoteNumber', 'sell_quote_number') ? <span>SELL Quote: <strong>{workRegisterBillingIdentifierValue(selectedWorkItem, 'sellQuoteNumber', 'sell_quote_number')}</strong></span> : null}
                  {workRegisterBillingIdentifierValue(selectedWorkItem, 'salesforceIdNumber', 'salesforce_id_number') ? <span>Salesforce ID: <strong>{workRegisterBillingIdentifierValue(selectedWorkItem, 'salesforceIdNumber', 'salesforce_id_number')}</strong></span> : null}
                  {workRegisterBillingIdentifierValue(selectedWorkItem, 'certiniaIdNumber', 'certinia_id_number') ? <span>Certinia ID: <strong>{workRegisterBillingIdentifierValue(selectedWorkItem, 'certiniaIdNumber', 'certinia_id_number')}</strong></span> : null}
                  <span>Approved change orders: <strong>{money(projectDetails.data?.costingSummary?.changeOrderTotal ?? 0)}</strong></span>
                  <span>Known total with change orders: <strong>{money((Number(selectedWorkItem.totalCost || 0)) + Number(projectDetails.data?.costingSummary?.changeOrderTotal ?? 0))}</strong></span>
                </div>

                <label className="checkbox-line work-register-change-order-toggle">
                  <input
                    type="checkbox"
                    checked={changeOrderForm.enabled}
                    onChange={(event) => updateChangeOrderField('enabled', event.target.checked)}
                    disabled={!canModifySelectedProject}
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
                      <button type="button" className="primary-action" onClick={saveChangeOrder} disabled={!canModifySelectedProject}>
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

                {canModifySelectedProject ? (
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


                {canModifySelectedProject ? (
                  <form className="work-register-document-upload-form" onSubmit={uploadLocalDocument}>
                    <h5>Browse and upload local file</h5>
                    <p className="muted">Use this when the document is on your computer. Use the link/reference section above when the document already lives in SharePoint, Drive, a ticket, or another system.</p>

                    {documentUploadStatus ? (
                      <div className="work-register-banner">{documentUploadStatus}</div>
                    ) : null}

                    <div className="work-register-edit-grid">
                      <label>
                        Local file
                        <input type="file" name="file" required />
                      </label>

                      <label>
                        Document name
                        <input
                          type="text"
                          name="documentName"
                          placeholder="Optional; defaults to selected file name"
                        />
                      </label>

                      <label>
                        Document type
                        <select name="documentType" defaultValue="SOW">
                          {['SOW', 'GSD', 'Change Order', 'Customer Approval', 'Project Plan', 'Technical Document', 'Closeout', 'Other'].map((type) => (
                            <option value={type} key={type}>{type}</option>
                          ))}
                        </select>
                      </label>

                      <label>
                        Version
                        <input type="text" name="versionLabel" placeholder="v1, signed, final, rev-2" />
                      </label>

                      <label>
                        Visibility
                        <select name="visibility" defaultValue="project_team">
                          <option value="project_team">Project team</option>
                          <option value="ptc_admin_only">PTC/Admin only</option>
                          <option value="pm_ptc_admin">PM/PTC/Admin</option>
                          <option value="engineering_team">Engineering team</option>
                        </select>
                      </label>

                      <label>
                        Effective date
                        <input type="date" name="effectiveDate" defaultValue={new Date().toISOString().slice(0, 10)} />
                      </label>
                    </div>

                    <label className="full-width">
                      Notes
                      <textarea name="notes" rows={2} placeholder="Optional context for this uploaded document." />
                    </label>

                    <label className="full-width">
                      Reason / audit note
                      <textarea
                        name="reason"
                        rows={2}
                        required
                        placeholder="Required. Example: Uploaded signed SOW from customer."
                      />
                    </label>

                    <div className="work-register-task-assignment-actions">
                      <button type="submit" className="primary-action">
                        Upload local file
                      </button>
                    </div>
                  </form>
                ) : null}

                <h5>Registered documents</h5>
                <div className="work-register-document-list">
                  {(projectDetails.data?.documents ?? []).map((document) => (
                    <article className={`work-register-document-card ${String(document.status || '').toLowerCase() === 'archived' ? 'archived' : ''}`} key={`${document.sourceTable}-${document.documentId || document.fileName}`}>
                      <div>
                        <strong>{document.fileName || ((['GSD', 'SOW'].includes(String(document.documentType || '').toUpperCase()) && selectedWorkItem?.workName) ? `${String(document.documentType).toUpperCase()}_${selectedWorkItem.workName}` : (document.originalFileName || 'Untitled document'))}</strong>
                        <small>Type: {labelize(document.documentType || 'not set')}</small>
                        <small>Status: {labelize(document.status || 'active')}</small>
                        <small>Visibility: {labelize(document.visibility || 'not set')}</small>
                        <small>Version: {document.versionLabel || 'not set'}</small>
                        <small>Source: {labelize(document.uploadSource || document.sourceTable || 'link')}</small>
                        {document.originalFileName ? <small>File: {document.originalFileName}</small> : null}
                        {document.fileSizeBytes ? <small>Size: {Number(document.fileSizeBytes).toLocaleString()} bytes</small> : null}
                        <small>Effective: {document.effectiveDate || 'not set'}</small>
                        <small>Added: {document.uploadedAt || 'not set'}</small>
                        {document.notes ? <small>Notes: {document.notes}</small> : null}
                        {document.archiveReason ? <small>Archive reason: {document.archiveReason}</small> : null}
                      </div>

                      <div className="work-register-document-actions">
                        {(document.documentReference || document.downloadUrl) ? (
                          <button type="button" className="secondary-action" onClick={() => openWorkRegisterDocument(document)}>
                            {document.downloadUrl ? 'Open uploaded file' : 'Open reference'}
                          </button>
                        ) : null}

                        {canModifySelectedProject && document.canArchive ? (
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

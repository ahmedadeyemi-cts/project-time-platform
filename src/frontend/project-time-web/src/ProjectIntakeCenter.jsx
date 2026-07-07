import { useEffect, useMemo, useState } from 'react';
import './project-intake-center.css';

function getStoredProjectPulseAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;

    const parsed = JSON.parse(rawSession);
    if (!parsed?.sessionToken) return null;

    if (parsed?.expiresAt && Date.now() >= Date.parse(parsed.expiresAt)) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders() {
  const session = getStoredProjectPulseAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });
  if (!response.ok) throw new Error(`${path} returned HTTP ${response.status}`);
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    let details = '';
    try {
      const body = await response.json();
      details = body.message || JSON.stringify(body);
    } catch {
      details = await response.text();
    }

    throw new Error(`${path} returned HTTP ${response.status}${details ? `: ${details}` : ''}`);
  }

  return response.json();
}

async function uploadIntakeDocument(requestId, file, options) {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('documentType', options.documentType);
  formData.append('engineeringVisible', String(options.engineeringVisible));
  formData.append('aiTimesheetContextEnabled', String(options.aiTimesheetContextEnabled));

  const response = await fetch(`/api/project-intake/requests/${requestId}/documents`, {
    method: 'POST',
    headers: getProjectPulseAuthHeaders(),
    body: formData
  });

  if (!response.ok) throw new Error(`Document upload returned HTTP ${response.status}`);
  return response.json();
}

function fmt(value) {
  return value ?? 'Not set';
}

function fmtMoney(value) {
  const amount = Number(value ?? 0);
  return amount.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function normalizeStatus(value) {
  return String(value ?? '').trim().toLowerCase();
}

function getIntakeStatusTone(status) {
  const normalized = normalizeStatus(status);

  if (['approved', 'assigned', 'ready', 'converted', 'posted'].includes(normalized)) return 'safe';
  if (['blocked', 'declined', 'cancelled', 'rejected'].includes(normalized)) return 'danger';
  if (['pending', 'triage', 'review', 'new', 'draft'].includes(normalized)) return 'attention';

  return 'neutral';
}


/* 032_PROJECT_INTAKE_RESOURCE_REFINEMENT_START */
const RESOURCE_STATUS_OPTIONS = [
  { value: 'all', label: 'All statuses' },
  { value: 'new', label: 'New' },
  { value: 'open', label: 'Open' },
  { value: 'pending', label: 'Pending' },
  { value: 'partially_assigned', label: 'Partially assigned' },
  { value: 'assigned', label: 'Assigned' },
  { value: 'blocked', label: 'Blocked' },
  { value: 'cancelled', label: 'Cancelled' }
];

function getResourceRequestStatusTone(status) {
  const normalized = normalizeStatus(status);

  if (['assigned', 'fulfilled', 'ready', 'complete', 'completed'].includes(normalized)) return 'safe';
  if (['blocked', 'cancelled', 'declined', 'rejected'].includes(normalized)) return 'danger';
  if (['partially_assigned', 'pending', 'review', 'open', 'new', 'draft'].includes(normalized)) return 'attention';

  return 'neutral';
}

function getEngineerCapacityTone(engineer) {
  const normalizedStatus = normalizeStatus(engineer?.capacityStatus);
  const utilization = Number(engineer?.plannedUtilizationPercent ?? 0);

  if (normalizedStatus.includes('over') || normalizedStatus.includes('blocked') || utilization >= 100) return 'danger';
  if (normalizedStatus.includes('tight') || normalizedStatus.includes('limited') || utilization >= 85) return 'attention';

  return 'safe';
}
/* 032_PROJECT_INTAKE_RESOURCE_REFINEMENT_END */

function StatusBadge({ children, tone = 'neutral' }) {
  return <span className={`project-intake-badge ${tone}`}>{children}</span>;
}

export default function ProjectIntakeCenter() {
  const [overview, setOverview] = useState({ loading: true, data: null, error: null });
  const [customerOverview, setCustomerOverview] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('');
  const [intakeSearchTerm, setIntakeSearchTerm] = useState('');
  const [intakeStatusFilter, setIntakeStatusFilter] = useState('all');
  const [selectedIntakeId, setSelectedIntakeId] = useState('');
  const [resourceRequestSearchTerm, setResourceRequestSearchTerm] = useState('');
  const [resourceStatusFilter, setResourceStatusFilter] = useState('all');
  const [resourceFunctionFilter, setResourceFunctionFilter] = useState('all');
  const [selectedResourceRequestId, setSelectedResourceRequestId] = useState('');
  const [assignmentDrafts, setAssignmentDrafts] = useState({});
  const [intakeFile, setIntakeFile] = useState(null);
  const [intakeDocumentType, setIntakeDocumentType] = useState('sow');
  const [engineeringVisibleDocument, setEngineeringVisibleDocument] = useState(true);
  const [aiTimesheetContextEnabled, setAiTimesheetContextEnabled] = useState(true);

  const [intakeForm, setIntakeForm] = useState({
    clientId: '',
    clientName: 'Great Lakes Healthcare',
    opportunityReference: 'OPP-NEW-REQUEST',
    requestTitle: 'New Project Intake',
    requestDescription: 'Intake request created from the ProjectPulse workflow.',
    assignedPmUserId: '',
    /* 053I_INTAKE_AE_SA_UI_STATE_START */
    accountExecutiveUserId: '',
    solutionArchitectUserId: '',
    /* 053I_INTAKE_AE_SA_UI_STATE_END */
    priority: 'normal',
    targetStartDate: '2026-08-03',
    targetCompletionDate: '2026-09-25',
    estimatedHours: '120',
    plannedEngineeringCost: '48000',
    plannedPmCost: '12000',
    intakeSource: 'manual_entry',
    sourceSystem: '',
    externalReferenceId: '',
    externalRecordType: '',
    externalRecordUrl: '',
    sourceDocumentRequired: false,
    intakeSourceNotes: ''
  });

  const [resourceForm, setResourceForm] = useState({
    projectIntakeRequestId: '',
    projectId: '',
    assignedPmUserId: '',
    requestedFunction: 'Collaboration Engineering',
    skillRequirements: 'Cisco UC, contact center, implementation readiness',
    requestedHours: '80',
    targetStartDate: '2026-08-03',
    targetEndDate: '2026-08-28',
    priority: 'normal',
    notes: 'Engineering resource request created from the ProjectPulse workflow.'
  });

  async function loadOverview() {
    setOverview((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/project-intake/overview');
      setOverview({ loading: false, data: result, error: null });
    } catch (error) {
      setOverview({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load project intake overview.'
      });
    }
  }

  async function loadCustomerOverview() {
    setCustomerOverview((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/customers/overview');
      setCustomerOverview({ loading: false, data: result, error: null });

      const defaultCustomer = result.customers?.find((customer) => customer.clientName === 'Great Lakes Healthcare') ?? result.customers?.[0];

      if (defaultCustomer) {
        setIntakeForm((current) => current.clientId
          ? current
          : { ...current, clientId: defaultCustomer.clientId, clientName: defaultCustomer.clientName });
      }
    } catch (error) {
      setCustomerOverview({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load customer directory.'
      });
    }
  }

  useEffect(() => {
    loadOverview();
    loadCustomerOverview();
  }, []);

  const projectManagers = overview.data?.projectManagers ?? [];

  /* 053I_INTAKE_AE_SA_UI_START */

  const accountExecutives = overview.data?.accountExecutives ?? [];

  const solutionArchitects = overview.data?.solutionArchitects ?? [];

  /* 053I_INTAKE_AE_SA_UI_END */
  const projects = overview.data?.projects ?? [];
  const intakes = overview.data?.intakes ?? [];
  const resourceRequests = overview.data?.resourceRequests ?? [];
  const capacity = overview.data?.capacity ?? [];
  const engineers = overview.data?.engineers ?? [];
  const customers = customerOverview.data?.customers ?? [];
  const customerContacts = customerOverview.data?.contacts ?? [];
  const resourceFunctionOptions = useMemo(() => {
    const values = new Set();

    resourceRequests.forEach((request) => {
      if (request.requestedFunction) values.add(request.requestedFunction);
    });

    if (resourceForm.requestedFunction) values.add(resourceForm.requestedFunction);

    return [...values].sort((a, b) => a.localeCompare(b));
  }, [resourceRequests, resourceForm.requestedFunction]);

  const selectedCustomer = customers.find((customer) => customer.clientId === intakeForm.clientId);
  const selectedCustomerContacts = customerContacts.filter((contact) => contact.clientId === intakeForm.clientId);
  const plannedEngineeringCost = Number(intakeForm.plannedEngineeringCost || 0);
  const plannedPmCost = Number(intakeForm.plannedPmCost || 0);
  const plannedTotalProjectCost = plannedEngineeringCost + plannedPmCost;

  const intakeWorkflowMetrics = useMemo(() => {
    const openStatuses = new Set(['new', 'draft', 'pending', 'triage', 'review', 'open']);
    const openIntakes = intakes.filter((item) => openStatuses.has(normalizeStatus(item.status)));
    const unassignedIntakes = intakes.filter((item) => !item.assignedPmUserId && !item.assignedPmName);
    const highPriorityIntakes = intakes.filter((item) => normalizeStatus(item.priority) === 'high');
    const sourceDocumentRequired = intakes.filter((item) => item.sourceDocumentRequired === true);
    const resourceLinkedIntakes = new Set(resourceRequests
      .map((request) => request.projectIntakeRequestId)
      .filter(Boolean));

    return {
      openIntakes: openIntakes.length,
      unassignedIntakes: unassignedIntakes.length,
      highPriorityIntakes: highPriorityIntakes.length,
      sourceDocumentRequired: sourceDocumentRequired.length,
      linkedToResourceRequest: intakes.filter((item) => resourceLinkedIntakes.has(item.id)).length
    };
  }, [intakes, resourceRequests]);

  const filteredIntakes = useMemo(() => {
    const search = intakeSearchTerm.trim().toLowerCase();

    return intakes.filter((item) => {
      const matchesStatus = intakeStatusFilter === 'all' || String(item.status ?? '').toLowerCase() === intakeStatusFilter;
      const haystack = `${item.requestNumber ?? ''} ${item.clientName ?? ''} ${item.requestTitle ?? ''} ${item.opportunityReference ?? ''} ${item.externalReferenceId ?? ''}`.toLowerCase();
      const matchesSearch = !search || haystack.includes(search);
      return matchesStatus && matchesSearch;
    });
  }, [intakes, intakeSearchTerm, intakeStatusFilter]);

  const visibleIntakes = useMemo(() => {
    if (selectedIntakeId) {
      return filteredIntakes.filter((item) => item.id === selectedIntakeId);
    }

    return filteredIntakes.slice(0, 20);
  }, [filteredIntakes, selectedIntakeId]);

  const selectedIntake = intakes.find((item) => item.id === selectedIntakeId) ?? visibleIntakes[0];

  const selectedIntakeReadinessItems = selectedIntake ? [
    {
      label: 'Customer selected',
      ready: Boolean(selectedIntake.clientId || selectedIntake.clientName),
      detail: selectedIntake.clientName ? `${selectedIntake.clientName} is attached to this intake.` : 'No customer is attached.'
    },
    {
      label: 'PM ownership',
      ready: Boolean(selectedIntake.assignedPmUserId || selectedIntake.assignedPmName),
      detail: selectedIntake.assignedPmName ? `${selectedIntake.assignedPmName} owns this intake.` : 'No project manager is assigned.'
    },
    {
      label: 'Source traceability',
      ready: Boolean(selectedIntake.opportunityReference || selectedIntake.externalReferenceId || selectedIntake.sourceSystem),
      detail: selectedIntake.externalReferenceId || selectedIntake.opportunityReference || 'No external/source reference is recorded.'
    },
    {
      label: 'Cost estimate',
      ready: Number(selectedIntake.plannedTotalProjectCost ?? 0) > 0 || Number(selectedIntake.plannedEngineeringCost ?? 0) > 0,
      detail: `Estimated project cost: ${fmtMoney(selectedIntake.plannedTotalProjectCost ?? selectedIntake.plannedEngineeringCost ?? 0)}.`
    },
    {
      label: 'Resource handoff',
      ready: resourceRequests.some((request) => request.projectIntakeRequestId === selectedIntake.id),
      detail: resourceRequests.some((request) => request.projectIntakeRequestId === selectedIntake.id)
        ? 'At least one engineering resource request is linked.'
        : 'No engineering resource request is linked yet.'
    }
  ] : [];

  function applySelectedIntakeToResourceRequest() {
    if (!selectedIntake) {
      setActionStatus('Select an intake before staging a resource request.');
      return;
    }

    const intakeLabel = selectedIntake.requestNumber || selectedIntake.requestTitle || 'selected intake';

    setResourceForm((current) => ({
      ...current,
      projectIntakeRequestId: selectedIntake.id || current.projectIntakeRequestId,
      assignedPmUserId: selectedIntake.assignedPmUserId || current.assignedPmUserId || '',
      requestedHours: selectedIntake.estimatedHours ? String(selectedIntake.estimatedHours) : current.requestedHours,
      targetStartDate: selectedIntake.targetStartDate || current.targetStartDate,
      targetEndDate: selectedIntake.targetCompletionDate || current.targetEndDate,
      priority: selectedIntake.priority || current.priority,
      notes: `Resource request staged from ${intakeLabel}. ${selectedIntake.requestDescription || current.notes || ''}`.trim()
    }));

    setSelectedResourceRequestId('');
    setActionStatus(`Resource request form staged from ${intakeLabel}. Review requested function, skills, and target dates before creating.`);
  }

  const filteredResourceRequests = useMemo(() => {
    const search = resourceRequestSearchTerm.trim().toLowerCase();

    return resourceRequests.filter((item) => {
      const matchesStatus = resourceStatusFilter === 'all' || normalizeStatus(item.status) === resourceStatusFilter;
      const matchesFunction = resourceFunctionFilter === 'all' || String(item.requestedFunction ?? '') === resourceFunctionFilter;
      const haystack = `${item.requestNumber ?? ''} ${item.sourceName ?? ''} ${item.requestedFunction ?? ''} ${item.skillRequirements ?? ''} ${item.assignedPmName ?? ''} ${item.fulfilledByName ?? ''}`.toLowerCase();
      const matchesSearch = !search || haystack.includes(search);
      return matchesStatus && matchesFunction && matchesSearch;
    });
  }, [resourceRequests, resourceRequestSearchTerm, resourceStatusFilter, resourceFunctionFilter]);

  const visibleResourceRequests = useMemo(() => {
    if (selectedResourceRequestId) {
      return filteredResourceRequests.filter((item) => item.id === selectedResourceRequestId);
    }

    return filteredResourceRequests.slice(0, 20);
  }, [filteredResourceRequests, selectedResourceRequestId]);


  const selectedResourceRequest = resourceRequests.find((item) => item.id === selectedResourceRequestId) ?? visibleResourceRequests[0];
  const selectedResourceDraft = selectedResourceRequest ? getAssignmentDraft(selectedResourceRequest.id) : null;
  const selectedDraftEngineerCount = selectedResourceDraft?.engineers?.length ?? 0;
  const selectedDraftHours = selectedResourceDraft?.engineers?.reduce((total, item) => total + Number(item.allocatedHours || 0), 0) ?? 0;
  const selectedDraftPercent = selectedResourceDraft?.engineers?.reduce((total, item) => total + Number(item.allocationPercent || 0), 0) ?? 0;
  const selectedResourceRequestedHours = Number(selectedResourceRequest?.requestedHours ?? 0);
  const selectedResourceAllocatedHours = Number(selectedResourceRequest?.allocatedHours ?? 0);
  const selectedResourceAssignedCount = Number(selectedResourceRequest?.assignedEngineerCount ?? 0);
  const selectedResourceReadinessItems = selectedResourceRequest ? [
    {
      label: 'Source linkage',
      ready: Boolean(selectedResourceRequest.projectIntakeRequestId || selectedResourceRequest.projectId || selectedResourceRequest.sourceName),
      detail: selectedResourceRequest.sourceName || 'No intake or project source is attached.'
    },
    {
      label: 'PM ownership',
      ready: Boolean(selectedResourceRequest.assignedPmUserId || selectedResourceRequest.assignedPmName),
      detail: selectedResourceRequest.assignedPmName ? `${selectedResourceRequest.assignedPmName} owns this request.` : 'No project manager is assigned.'
    },
    {
      label: 'Skills captured',
      ready: Boolean(String(selectedResourceRequest.skillRequirements ?? '').trim()),
      detail: selectedResourceRequest.skillRequirements || 'No skill requirements are recorded.'
    },
    {
      label: 'Assignment plan',
      ready: selectedResourceAssignedCount > 0 || selectedDraftEngineerCount > 0,
      detail: selectedResourceAssignedCount > 0
        ? `${selectedResourceAssignedCount} engineer(s) currently assigned.`
        : selectedDraftEngineerCount > 0
          ? `${selectedDraftEngineerCount} engineer(s) selected in draft.`
          : 'No engineer is selected yet.'
    },
    {
      label: 'Allocation coverage',
      ready: selectedResourceAllocatedHours >= selectedResourceRequestedHours || selectedDraftHours > 0 || selectedDraftPercent > 0 || (selectedDraftEngineerCount > 0 && selectedResourceDraft?.distributionMode !== 'manual'),
      detail: selectedResourceAllocatedHours > 0
        ? `${selectedResourceAllocatedHours}/${selectedResourceRequestedHours || 0} hours allocated.`
        : selectedDraftEngineerCount > 0
          ? `${selectedResourceDraft?.distributionMode === 'manual' ? `${selectedDraftHours.toFixed(2)} draft hours · ${selectedDraftPercent.toFixed(2)}%.` : 'Automatic split selected; allocation will be calculated on assignment.'}`
          : 'No allocation split is staged.'
    }
  ] : [];

  /* 035_PROJECT_INTAKE_GUIDED_LAUNCH_START */
  const selectedIntakeResourceRequests = useMemo(() => {
    if (!selectedIntake?.id) return [];
    return resourceRequests.filter((request) => request.projectIntakeRequestId === selectedIntake.id);
  }, [resourceRequests, selectedIntake?.id]);

  const selectedIntakeDocumentCount = Number(
    selectedIntake?.documentCount ??
    selectedIntake?.documentsCount ??
    selectedIntake?.sourceDocumentCount ??
    selectedIntake?.attachedDocumentCount ??
    0
  );

  const selectedIntakeHasDocument = Boolean(
    selectedIntakeDocumentCount > 0 ||
    selectedIntake?.externalRecordUrl ||
    selectedIntake?.sourceDocumentUploaded ||
    selectedIntake?.sourceDocumentReceived ||
    selectedIntake?.hasSourceDocument ||
    selectedIntake?.sourceDocumentRequired === false ||
    intakeFile
  );

  const selectedIntakePmReady = Boolean(
    selectedIntake?.assignedPmUserId ||
    selectedIntake?.assignedPmName ||
    intakeForm.assignedPmUserId
  );

  const selectedIntakeCustomerReady = Boolean(
    selectedIntake?.clientId ||
    selectedIntake?.clientName ||
    intakeForm.clientId ||
    selectedCustomer
  );

  const selectedIntakeResourceReady = Boolean(
    selectedIntakeResourceRequests.length > 0 ||
    (selectedIntake?.id && resourceForm.projectIntakeRequestId === selectedIntake.id)
  );

  const selectedIntakeEngineerReady = Boolean(
    selectedResourceAssignedCount > 0 ||
    selectedDraftEngineerCount > 0 ||
    selectedIntakeResourceRequests.some((request) => Number(request.assignedEngineerCount ?? 0) > 0)
  );

  const selectedIntakeWorkspaceReady = Boolean(
    selectedIntakeCustomerReady &&
    selectedIntakePmReady &&
    selectedIntakeHasDocument &&
    selectedIntakeResourceReady &&
    selectedIntakeEngineerReady
  );

  const launchWorkflowSteps = [
    {
      key: 'intake',
      title: 'Create or select intake',
      ready: Boolean(selectedIntake),
      helper: selectedIntake
        ? `${selectedIntake.requestNumber || 'Selected intake'} · ${selectedIntake.requestTitle || 'Untitled request'}`
        : 'Create a new intake or select an existing one from the intake queue.',
      action: 'Use the intake form or select an intake below.'
    },
    {
      key: 'customer-document',
      title: 'Attach customer and source document',
      ready: selectedIntakeCustomerReady && selectedIntakeHasDocument,
      helper: selectedIntakeCustomerReady
        ? `${selectedIntake?.clientName || selectedCustomer?.clientName || intakeForm.clientName || 'Customer selected'} · ${selectedIntakeHasDocument ? 'document/source traceability ready' : 'document still needed'}`
        : 'Select the customer and upload/trace the SOW, GSD, quote, order form, or source record.',
      action: 'Confirm customer, source reference, and document visibility.'
    },
    {
      key: 'pm',
      title: 'Assign Project Manager',
      ready: selectedIntakePmReady,
      helper: selectedIntake?.assignedPmName || projectManagers.find((pm) => pm.userId === intakeForm.assignedPmUserId)?.displayName || 'No PM assigned yet.',
      action: 'Choose the assigned PM on the intake or staged resource request.'
    },
    {
      key: 'resource-request',
      title: 'Stage engineering resource request',
      ready: selectedIntakeResourceReady,
      helper: selectedIntakeResourceRequests.length > 0
        ? `${selectedIntakeResourceRequests.length} resource request(s) linked to this intake.`
        : 'No resource request is linked yet.',
      action: 'Use selected intake, then create the resource request.'
    },
    {
      key: 'engineers',
      title: 'Assign engineer(s)',
      ready: selectedIntakeEngineerReady,
      helper: selectedResourceAssignedCount > 0
        ? `${selectedResourceAssignedCount} engineer(s) already assigned.`
        : selectedDraftEngineerCount > 0
          ? `${selectedDraftEngineerCount} engineer(s) staged in the assignment draft.`
          : 'Select one or more engineers and allocation mode.',
      action: 'Select engineers and click Assign engineers.'
    },
    {
      key: 'workspace',
      title: 'Ready for project workspace',
      ready: selectedIntakeWorkspaceReady,
      helper: selectedIntakeWorkspaceReady
        ? 'Intake has the minimum launch information needed for active delivery.'
        : 'Complete the prior steps before moving into active project delivery.',
      action: 'Convert/activate workflow can be wired to backend project creation later.'
    }
  ];

  const launchReadyCount = launchWorkflowSteps.filter((step) => step.ready).length;
  const launchReadyPercent = Math.round((launchReadyCount / launchWorkflowSteps.length) * 100);

  function scrollToLaunchSection(sectionId) {
    document.getElementById(sectionId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  /* 035_PROJECT_INTAKE_GUIDED_LAUNCH_END */

  const readyResourceRequests = useMemo(() => {
    return resourceRequests.filter((request) => request.status === 'assigned' || request.status === 'partially_assigned').length;
  }, [resourceRequests]);

  function getEngineerMatchScore(engineer, requestedFunction) {
    const haystack = `${engineer.primaryFunction ?? ''} ${engineer.jobTitle ?? ''} ${engineer.qualifications ?? ''}`.toLowerCase();
    const requested = String(requestedFunction ?? '').toLowerCase();

    if (!requested) return 1;
    if (haystack.includes(requested)) return 3;

    const requestedWords = requested.split(/\s+/).filter(Boolean);
    return requestedWords.some((word) => haystack.includes(word)) ? 2 : 1;
  }

  function getSortedEngineersForRequest(request) {
    return [...engineers].sort((a, b) => {
      const scoreB = getEngineerMatchScore(b, request.requestedFunction);
      const scoreA = getEngineerMatchScore(a, request.requestedFunction);
      if (scoreA !== scoreB) return scoreB - scoreA;
      return a.displayName.localeCompare(b.displayName);
    });
  }

  function getAssignmentDraft(requestId) {
    return assignmentDrafts[requestId] ?? {
      distributionMode: 'equal_hours',
      notes: '',
      engineers: []
    };
  }

  function setAssignmentDraft(requestId, updater) {
    setAssignmentDrafts((current) => {
      const existing = current[requestId] ?? { distributionMode: 'equal_hours', notes: '', engineers: [] };
      return { ...current, [requestId]: updater(existing) };
    });
  }

  function toggleEngineer(requestId, userId) {
    setAssignmentDraft(requestId, (draft) => {
      const exists = draft.engineers.some((item) => item.userId === userId);
      if (exists) {
        return { ...draft, engineers: draft.engineers.filter((item) => item.userId !== userId) };
      }

      if (draft.engineers.length >= 15) {
        setActionStatus('A resource request can have up to 15 engineers.');
        return draft;
      }

      return {
        ...draft,
        engineers: [...draft.engineers, { userId, allocatedHours: '', allocationPercent: '' }]
      };
    });
  }

  function updateEngineerAllocation(requestId, userId, field, value) {
    setAssignmentDraft(requestId, (draft) => ({
      ...draft,
      engineers: draft.engineers.map((item) => item.userId === userId ? { ...item, [field]: value } : item)
    }));
  }

  async function createIntake(event) {
    event.preventDefault();
    setActionStatus('Creating project intake request...');

    try {
      const result = await postJson('/api/project-intake/requests', {
        ...intakeForm,
        clientId: intakeForm.clientId || null,
        clientName: selectedCustomer?.clientName ?? intakeForm.clientName,
        assignedPmUserId: intakeForm.assignedPmUserId || null,
        accountExecutiveUserId: intakeForm.accountExecutiveUserId || null,
        solutionArchitectUserId: intakeForm.solutionArchitectUserId || null,
        sourceSystem: intakeForm.intakeSource === 'salesforce' ? 'Salesforce' : intakeForm.sourceSystem || null,
        externalRecordType: intakeForm.intakeSource === 'salesforce' ? 'Opportunity' : intakeForm.externalRecordType || null,
        estimatedHours: Number(intakeForm.estimatedHours || 0),
        plannedEngineeringCost,
        plannedPmCost,
        plannedTotalProjectCost
      });

      if (intakeFile) {
        await uploadIntakeDocument(result.projectIntakeRequestId, intakeFile, {
          documentType: intakeDocumentType,
          engineeringVisible: engineeringVisibleDocument,
          aiTimesheetContextEnabled
        });
      }

      setActionStatus(`${result.requestNumber} created${intakeFile ? ' with document uploaded' : ''}.`);
      setIntakeFile(null);
      await loadOverview();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to create intake request.');
    }
  }

  async function createResourceRequest(event) {
    event.preventDefault();
    setActionStatus('Creating engineering resource request...');

    try {
      const result = await postJson('/api/project-intake/resource-requests', {
        ...resourceForm,
        projectIntakeRequestId: resourceForm.projectIntakeRequestId || null,
        projectId: resourceForm.projectId || null,
        assignedPmUserId: resourceForm.assignedPmUserId || null,
        requestedHours: Number(resourceForm.requestedHours || 0)
      });

      setActionStatus(`${result.requestNumber} created.`);
      await loadOverview();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to create engineering resource request.');
    }
  }

  async function assignEngineers(request) {
    const draft = getAssignmentDraft(request.id);

    if (draft.engineers.length === 0) {
      setActionStatus('Select at least one engineer before assigning.');
      return;
    }

    setActionStatus(`Assigning ${draft.engineers.length} engineer(s) to ${request.requestNumber}...`);

    try {
      const result = await postJson(`/api/project-intake/resource-requests/${request.id}/assign`, {
        distributionMode: draft.distributionMode,
        notes: draft.notes,
        engineers: draft.engineers.map((item) => ({
          userId: item.userId,
          allocatedHours: item.allocatedHours === '' ? null : Number(item.allocatedHours),
          allocationPercent: item.allocationPercent === '' ? null : Number(item.allocationPercent)
        }))
      });

      setActionStatus(`${request.requestNumber} updated: ${result.assignedEngineerCount} engineer(s), ${result.allocatedHours} hours, ${result.allocationPercent}% allocation.`);
      await loadOverview();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to assign engineers.');
    }
  }

  return (
    <section className="project-intake-center">
      <div className="project-intake-header">
        <div>
          <p className="eyebrow">019M-P</p>
          <h2>Project Intake & Engineering Resource Requests</h2>
          <p className="muted">
            Guided launch workspace for client intake, document handoff, PM ownership, engineering demand, engineer assignment, and project workspace readiness.
          </p>
        </div>
        <StatusBadge tone="safe">Guided launch workflow</StatusBadge>
      </div>

      {overview.error && <div className="project-intake-error">{overview.error}</div>}
      {customerOverview.error && <div className="project-intake-error">{customerOverview.error}</div>}
      {actionStatus && <div className="project-intake-alert">{actionStatus}</div>}

      <div className="project-intake-summary-grid">
        <article><span>Intake requests</span><strong>{overview.loading ? '...' : overview.data?.summary?.intakeCount ?? 0}</strong><small>{intakeWorkflowMetrics.openIntakes} open workflow item(s)</small></article>
        <article><span>Resource requests</span><strong>{overview.loading ? '...' : overview.data?.summary?.resourceRequestCount ?? 0}</strong><small>{readyResourceRequests} fully or partially assigned · max 15 engineers/request</small></article>
        <article><span>Active projects</span><strong>{overview.loading ? '...' : overview.data?.summary?.activeProjectCount ?? 0}</strong><small>Workspace-ready records</small></article>
        <article><span>Engineers</span><strong>{overview.loading ? '...' : overview.data?.summary?.engineerCount ?? 0}</strong><small>Role, department, capacity, and skills loaded</small></article>
        <article><span>High priority</span><strong>{overview.loading ? '...' : intakeWorkflowMetrics.highPriorityIntakes}</strong><small>Intake records marked high priority</small></article>
        <article><span>Needs PM</span><strong>{overview.loading ? '...' : intakeWorkflowMetrics.unassignedIntakes}</strong><small>Intake records without PM ownership</small></article>
        <article><span>Needs source doc</span><strong>{overview.loading ? '...' : intakeWorkflowMetrics.sourceDocumentRequired}</strong><small>Source document still required</small></article>
        <article><span>Resource-linked</span><strong>{overview.loading ? '...' : intakeWorkflowMetrics.linkedToResourceRequest}</strong><small>Intakes with engineering demand linked</small></article>
      </div>

      <article className="project-intake-panel module035-launch-panel">
        <div className="project-intake-section-header">
          <div>
            <p className="eyebrow">035 guided launch path</p>
            <h3>Intake to active project workflow</h3>
            <p className="muted">
              Use this checklist as the single path from incoming project request to PM ownership, engineering assignment, and project workspace readiness.
            </p>
          </div>
          <StatusBadge tone={launchReadyPercent === 100 ? 'safe' : 'attention'}>{launchReadyCount}/{launchWorkflowSteps.length} ready</StatusBadge>
        </div>

        <div className="module035-launch-meter" aria-label={`Project launch readiness ${launchReadyPercent}%`}>
          <div style={{ width: `${launchReadyPercent}%` }} />
        </div>

        <div className="module035-launch-steps">
          {launchWorkflowSteps.map((step, index) => (
            <article className={`module035-launch-step ${step.ready ? 'ready' : 'attention'}`} key={step.key}>
              <span>{String(index + 1).padStart(2, '0')}</span>
              <div>
                <strong>{step.title}</strong>
                <small>{step.helper}</small>
                <em>{step.action}</em>
              </div>
            </article>
          ))}
        </div>

        <div className="module035-launch-actions">
          <button type="button" className="secondary-action" onClick={() => scrollToLaunchSection('module035-create-intake')}>Go to intake</button>
          <button type="button" className="secondary-action" onClick={() => scrollToLaunchSection('module035-resource-request')}>Go to resource request</button>
          <button type="button" className="secondary-action" onClick={() => scrollToLaunchSection('module035-engineer-assignment')}>Go to engineer assignment</button>
        </div>

        <p className="module035-launch-note">
          Project closure intentionally stays outside Module 020. Closeout should be handled later in the active project/workload or closeout workflow after delivery is complete.
        </p>
      </article>

      {selectedIntake && (
        <article className="project-intake-panel intake-readiness-panel">
          <div className="project-intake-section-header">
            <div>
              <h3>Selected Intake Readiness</h3>
              <p className="muted">
                {selectedIntake.requestNumber} · {selectedIntake.requestTitle}
              </p>
            </div>
            <div className="intake-readiness-actions">
              <StatusBadge tone={getIntakeStatusTone(selectedIntake.status)}>{selectedIntake.status || 'Draft'}</StatusBadge>
              <button type="button" className="secondary-action" onClick={applySelectedIntakeToResourceRequest}>Stage resource request</button>
            </div>
          </div>
          <div className="intake-readiness-grid">
            {selectedIntakeReadinessItems.map((item) => (
              <article className={`intake-readiness-item ${item.ready ? 'ready' : 'attention'}`} key={item.label}>
                <span>{item.ready ? 'Ready' : 'Needs attention'}</span>
                <strong>{item.label}</strong>
                <small>{item.detail}</small>
              </article>
            ))}
          </div>
        </article>
      )}

      {selectedResourceRequest && (
        <article className="project-intake-panel resource-readiness-panel">
          <div className="project-intake-section-header">
            <div>
              <h3>Selected Resource Assignment Readiness</h3>
              <p className="muted">
                {selectedResourceRequest.requestNumber} · {selectedResourceRequest.requestedFunction} · {selectedResourceRequest.sourceName}
              </p>
            </div>
            <StatusBadge tone={getResourceRequestStatusTone(selectedResourceRequest.status)}>{selectedResourceRequest.status || 'new'}</StatusBadge>
          </div>
          <div className="intake-readiness-grid">
            {selectedResourceReadinessItems.map((item) => (
              <article className={`intake-readiness-item resource-readiness-item ${item.ready ? 'ready' : 'attention'}`} key={item.label}>
                <span>{item.ready ? 'Ready' : 'Needs attention'}</span>
                <strong>{item.label}</strong>
                <small>{item.detail}</small>
              </article>
            ))}
          </div>
        </article>
      )}

      <div className="project-intake-two-column">
        <article id="module035-create-intake" className="project-intake-panel module035-launch-target">
          <h3>Create Project Intake</h3>
          <p className="muted">Salesforce-sourced intake can still include manually uploaded SOW, GSD, and supporting documents.</p>
          <form className="project-intake-form" onSubmit={createIntake}>
            <label>Customer<select value={intakeForm.clientId} onChange={(event) => {
              const customer = customers.find((item) => item.clientId === event.target.value);
              setIntakeForm({ ...intakeForm, clientId: event.target.value, clientName: customer?.clientName ?? '' });
            }}><option value="">Select customer</option>{customers.map((customer) => <option value={customer.clientId} key={customer.clientId}>{customer.clientName} · {customer.clientCode}</option>)}</select></label>
            <div className="customer-contact-preview full-width">
              <strong>{selectedCustomer ? `${selectedCustomer.clientName} contacts` : 'Customer contacts'}</strong>
              {selectedCustomerContacts.length === 0 ? (
                <span>No active contacts loaded for this customer.</span>
              ) : (
                <div className="customer-contact-grid">
                  {selectedCustomerContacts.map((contact) => (
                    <div className="customer-contact-card" key={contact.contactId}>
                      <span>{contact.isPrimary ? 'Primary' : 'Contact'}</span>
                      <strong>{contact.contactName}</strong>
                      <small>{contact.title || 'No title'} · {contact.email || 'No email'} · {contact.phone || 'No phone'}</small>
                    </div>
                  ))}
                </div>
              )}
            </div>
            <label>Opportunity reference<input value={intakeForm.opportunityReference} onChange={(event) => setIntakeForm({ ...intakeForm, opportunityReference: event.target.value })} /></label>
            <label>Request title<input value={intakeForm.requestTitle} onChange={(event) => setIntakeForm({ ...intakeForm, requestTitle: event.target.value })} /></label>
            <label>Assigned PM<select value={intakeForm.assignedPmUserId} onChange={(event) => setIntakeForm({ ...intakeForm, assignedPmUserId: event.target.value })}><option value="">Unassigned</option>{projectManagers.map((pm) => <option value={pm.userId} key={pm.userId}>{pm.displayName} · {pm.jobTitle}</option>)}</select></label>

            <label>Account Executive<select value={intakeForm.accountExecutiveUserId} onChange={(event) => setIntakeForm({ ...intakeForm, accountExecutiveUserId: event.target.value })}><option value="">Select Account Executive</option>{accountExecutives.map((ae) => <option value={ae.userId} key={ae.userId}>{ae.displayName} · {ae.jobTitle || ae.email}</option>)}</select></label>

            <label>Solution Architect<select value={intakeForm.solutionArchitectUserId} onChange={(event) => setIntakeForm({ ...intakeForm, solutionArchitectUserId: event.target.value })}><option value="">Select Solution Architect</option>{solutionArchitects.map((architect) => <option value={architect.userId} key={architect.userId}>{architect.displayName} · {architect.jobTitle || architect.email}</option>)}</select></label>
            <label>Intake source<select value={intakeForm.intakeSource} onChange={(event) => setIntakeForm({ ...intakeForm, intakeSource: event.target.value })}><option value="manual_entry">Manual entry</option><option value="manual_upload">Manual upload</option><option value="salesforce">Salesforce</option></select></label>
            <label>Source / unique ID<input value={intakeForm.externalReferenceId} placeholder={intakeForm.intakeSource === 'salesforce' ? 'Salesforce Opportunity ID' : 'Optional source reference'} onChange={(event) => setIntakeForm({ ...intakeForm, externalReferenceId: event.target.value })} /></label>
            <label>Source URL<input value={intakeForm.externalRecordUrl} onChange={(event) => setIntakeForm({ ...intakeForm, externalRecordUrl: event.target.value })} /></label>
            <label>Document type<select value={intakeDocumentType} onChange={(event) => setIntakeDocumentType(event.target.value)}><option value="sow">SOW</option><option value="gsd">GSD</option><option value="quote">Quote / Proposal</option><option value="order_form">Order Form</option><option value="architecture">Architecture / Design</option><option value="other">Other</option></select></label>
            <label>SOW / GSD / supporting document<input type="file" onChange={(event) => setIntakeFile(event.target.files?.[0] ?? null)} /></label>
            <label className="checkbox-label"><input type="checkbox" checked={engineeringVisibleDocument} onChange={(event) => setEngineeringVisibleDocument(event.target.checked)} />Visible to engineering workspace</label>
            <label className="checkbox-label"><input type="checkbox" checked={aiTimesheetContextEnabled} onChange={(event) => setAiTimesheetContextEnabled(event.target.checked)} />Use for timesheet description assistant context</label>
            <label>Priority<select value={intakeForm.priority} onChange={(event) => setIntakeForm({ ...intakeForm, priority: event.target.value })}><option value="low">Low</option><option value="normal">Normal</option><option value="high">High</option></select></label>
            <label>Estimated hours<input type="number" min="1" value={intakeForm.estimatedHours} onChange={(event) => setIntakeForm({ ...intakeForm, estimatedHours: event.target.value })} /></label>
            <label>Planned engineering cost<input type="number" min="0" step="0.01" value={intakeForm.plannedEngineeringCost} onChange={(event) => setIntakeForm({ ...intakeForm, plannedEngineeringCost: event.target.value })} /></label>
            <label>Planned PM cost<input type="number" min="0" step="0.01" value={intakeForm.plannedPmCost} onChange={(event) => setIntakeForm({ ...intakeForm, plannedPmCost: event.target.value })} /></label>
            <label>Total project cost<input type="text" value={fmtMoney(plannedTotalProjectCost)} readOnly /></label>
            <label>Target start<input type="date" value={intakeForm.targetStartDate} onChange={(event) => setIntakeForm({ ...intakeForm, targetStartDate: event.target.value })} /></label>
            <label>Target completion<input type="date" value={intakeForm.targetCompletionDate} onChange={(event) => setIntakeForm({ ...intakeForm, targetCompletionDate: event.target.value })} /></label>
            <label className="full-width">Source notes<textarea value={intakeForm.intakeSourceNotes} onChange={(event) => setIntakeForm({ ...intakeForm, intakeSourceNotes: event.target.value })} /></label>
            <label className="full-width">Description<textarea value={intakeForm.requestDescription} onChange={(event) => setIntakeForm({ ...intakeForm, requestDescription: event.target.value })} /></label>
            <button className="primary-action" type="submit">Create intake request</button>
          </form>
        </article>

        <article id="module035-resource-request" className="project-intake-panel module035-launch-target">
          <div className="project-intake-section-header compact">
            <div>
              <h3>Create Engineering Resource Request</h3>
              <p className="muted">Stage demand from the selected intake, then adjust function, skills, and dates before creating the request.</p>
            </div>
            <button type="button" className="secondary-action" onClick={applySelectedIntakeToResourceRequest} disabled={!selectedIntake}>Use selected intake</button>
          </div>
          <form className="project-intake-form" onSubmit={createResourceRequest}>
            <label>Intake<select value={resourceForm.projectIntakeRequestId} onChange={(event) => setResourceForm({ ...resourceForm, projectIntakeRequestId: event.target.value })}><option value="">No intake link</option>{intakes.map((item) => <option value={item.id} key={item.id}>{item.requestNumber} — {item.requestTitle}</option>)}</select></label>
            <label>Project<select value={resourceForm.projectId} onChange={(event) => setResourceForm({ ...resourceForm, projectId: event.target.value })}><option value="">No project link</option>{projects.map((project) => <option value={project.id} key={project.id}>{project.projectCode} — {project.projectName}</option>)}</select></label>
            <label>Assigned PM<select value={resourceForm.assignedPmUserId} onChange={(event) => setResourceForm({ ...resourceForm, assignedPmUserId: event.target.value })}><option value="">Unassigned</option>{projectManagers.map((pm) => <option value={pm.userId} key={pm.userId}>{pm.displayName} · {pm.jobTitle}</option>)}</select></label>
            <label>Requested function<select value={resourceForm.requestedFunction} onChange={(event) => setResourceForm({ ...resourceForm, requestedFunction: event.target.value })}><option>Collaboration Engineering</option><option>Systems Engineering</option><option>Enterprise Networking</option><option>Project Management</option></select></label>
            <label>Requested hours<input type="number" min="1" value={resourceForm.requestedHours} onChange={(event) => setResourceForm({ ...resourceForm, requestedHours: event.target.value })} /></label>
            <label>Priority<select value={resourceForm.priority} onChange={(event) => setResourceForm({ ...resourceForm, priority: event.target.value })}><option value="low">Low</option><option value="normal">Normal</option><option value="high">High</option></select></label>
            <label>Target start<input type="date" value={resourceForm.targetStartDate} onChange={(event) => setResourceForm({ ...resourceForm, targetStartDate: event.target.value })} /></label>
            <label>Target end<input type="date" value={resourceForm.targetEndDate} onChange={(event) => setResourceForm({ ...resourceForm, targetEndDate: event.target.value })} /></label>
            <label className="full-width">Skills<textarea value={resourceForm.skillRequirements} onChange={(event) => setResourceForm({ ...resourceForm, skillRequirements: event.target.value })} /></label>
            <label className="full-width">Notes<textarea value={resourceForm.notes} onChange={(event) => setResourceForm({ ...resourceForm, notes: event.target.value })} /></label>
            <button className="primary-action" type="submit">Create resource request</button>
          </form>
        </article>
      </div>

      <article id="module035-engineer-assignment" className="project-intake-panel module035-launch-target">
        <div className="project-intake-section-header">
          <div>
            <h3>Engineering Resource Requests</h3>
            <p className="muted">Search or select a specific resource request. The latest 20 matching records are shown by default.</p>
          </div>
          <StatusBadge>{filteredResourceRequests.length} matching</StatusBadge>
        </div>

        <div className="queue-control-bar resource-request-controls">
          <label>
            Search resource requests
            <input
              value={resourceRequestSearchTerm}
              placeholder="Search request, project, function, skills, PM, engineer..."
              onChange={(event) => {
                setResourceRequestSearchTerm(event.target.value);
                setSelectedResourceRequestId('');
              }}
            />
          </label>
          <label>
            Status
            <select value={resourceStatusFilter} onChange={(event) => {
              setResourceStatusFilter(event.target.value);
              setSelectedResourceRequestId('');
            }}>
              {RESOURCE_STATUS_OPTIONS.map((option) => (
                <option value={option.value} key={option.value}>{option.label}</option>
              ))}
            </select>
          </label>
          <label>
            Function
            <select value={resourceFunctionFilter} onChange={(event) => {
              setResourceFunctionFilter(event.target.value);
              setSelectedResourceRequestId('');
            }}>
              <option value="all">All functions</option>
              {resourceFunctionOptions.map((item) => (
                <option value={item} key={item}>{item}</option>
              ))}
            </select>
          </label>
          <label>
            Select resource request
            <select value={selectedResourceRequestId} onChange={(event) => setSelectedResourceRequestId(event.target.value)}>
              <option value="">Show latest 20 matching requests</option>
              {filteredResourceRequests.map((item) => (
                <option value={item.id} key={item.id}>{item.requestNumber} — {item.requestedFunction} — {item.sourceName}</option>
              ))}
            </select>
          </label>
        </div>

        <div className="project-intake-card-grid">
          {visibleResourceRequests.map((request) => {
            const draft = getAssignmentDraft(request.id);
            const sortedEngineers = getSortedEngineersForRequest(request);
            const assignedCount = Number(request.assignedEngineerCount ?? 0);
            const requestedHours = Number(request.requestedHours ?? 0);
            const allocatedHours = Number(request.allocatedHours ?? 0);
            const draftHours = draft.engineers.reduce((total, item) => total + Number(item.allocatedHours || 0), 0);
            const draftPercent = draft.engineers.reduce((total, item) => total + Number(item.allocationPercent || 0), 0);
            const plannedCoverageHours = draft.distributionMode === 'manual' && draft.engineers.length > 0 ? draftHours : allocatedHours;
            const assignmentCoveragePercent = requestedHours > 0 ? Math.min(100, Math.round((plannedCoverageHours / requestedHours) * 100)) : 0;
            const draftReady = draft.engineers.length > 0;

            return (
              <div className="resource-request-card" key={request.id}>
                <div><strong>{request.requestNumber}</strong><StatusBadge tone={getResourceRequestStatusTone(request.status)}>{request.status}</StatusBadge></div>
                <h4>{request.requestedFunction}</h4>
                <p>{request.sourceName}</p>
                <small>{request.skillRequirements || 'No skills recorded'}</small>
                <dl>
                  <div><dt>Requested</dt><dd>{request.requestedHours} hrs</dd></div>
                  <div><dt>PM</dt><dd>{fmt(request.assignedPmName)}</dd></div>
                  <div><dt>Assigned</dt><dd>{fmt(request.fulfilledByName)} ({request.assignedEngineerCount}/15)</dd></div>
                  <div><dt>Allocated</dt><dd>{request.allocatedHours ?? 0} hrs · {request.allocationPercent ?? 0}%</dd></div>
                </dl>

                <div className="assignment-health-strip">
                  <div>
                    <span>Assignment plan</span>
                    <strong>{assignedCount > 0 ? `${assignedCount} assigned` : draftReady ? `${draft.engineers.length} staged` : 'Not started'}</strong>
                    <small>{draftReady ? `${draft.distributionMode.replace('_', ' ')}` : 'Select engineers below'}</small>
                  </div>
                  <div>
                    <span>Coverage</span>
                    <strong>{assignmentCoveragePercent}%</strong>
                    <small>{plannedCoverageHours.toFixed(2)} / {requestedHours.toFixed(2)} hrs</small>
                  </div>
                  <div>
                    <span>Manual draft</span>
                    <strong>{draftHours.toFixed(2)} hrs</strong>
                    <small>{draftPercent.toFixed(2)}% staged</small>
                  </div>
                </div>

                <div className="assignment-editor">
                  <label>Distribution
                    <select value={draft.distributionMode} onChange={(event) => setAssignmentDraft(request.id, (current) => ({ ...current, distributionMode: event.target.value }))}>
                      <option value="equal_hours">Even hours</option>
                      <option value="equal_percent">Even percentage</option>
                      <option value="manual">Manual hours / percent</option>
                    </select>
                  </label>

                  <div className="engineer-picker">
                    {sortedEngineers.map((engineer) => {
                      const selected = draft.engineers.some((item) => item.userId === engineer.userId);
                      const allocation = draft.engineers.find((item) => item.userId === engineer.userId) ?? {};
                      return (
                        <div className={`engineer-picker-row ${selected ? 'selected' : ''}`} key={`${request.id}-${engineer.userId}`}>
                          <label>
                            <input type="checkbox" checked={selected} onChange={() => toggleEngineer(request.id, engineer.userId)} />
                            <span>
                               <strong>{engineer.displayName}</strong>
                               <small>{engineer.primaryFunction} · {engineer.qualifications}</small>
                               <small className={`engineer-capacity-hint ${getEngineerCapacityTone(engineer)}`}>
                                 {engineer.assignedHours ?? 0}/{engineer.availableHours ?? 0} hrs · {engineer.plannedUtilizationPercent ?? 0}% planned · {engineer.capacityStatus ?? 'capacity unknown'}
                               </small>
                             </span>
                          </label>
                          {selected && draft.distributionMode === 'manual' ? (
                            <div className="allocation-inputs">
                              <input placeholder="Hours" type="number" min="0" value={allocation.allocatedHours ?? ''} onChange={(event) => updateEngineerAllocation(request.id, engineer.userId, 'allocatedHours', event.target.value)} />
                              <input placeholder="%" type="number" min="0" max="100" value={allocation.allocationPercent ?? ''} onChange={(event) => updateEngineerAllocation(request.id, engineer.userId, 'allocationPercent', event.target.value)} />
                            </div>
                          ) : null}
                        </div>
                      );
                    })}
                  </div>

                  {draft.distributionMode === 'manual' && draft.engineers.length > 0 ? (
                    <div className="manual-allocation-panel">
                      <div className="manual-allocation-header">
                        <div>
                          <strong>Manual allocation</strong>
                          <small>Enter the hours and/or percentage for each selected engineer.</small>
                        </div>
                        <div className="manual-allocation-totals">
                          <span>Total hours: <strong>{draft.engineers.reduce((total, item) => total + Number(item.allocatedHours || 0), 0).toFixed(2)}</strong></span>
                          <span>Total %: <strong>{draft.engineers.reduce((total, item) => total + Number(item.allocationPercent || 0), 0).toFixed(2)}%</strong></span>
                        </div>
                      </div>

                      <div className="manual-allocation-grid">
                        <div className="manual-allocation-grid-head">Engineer</div>
                        <div className="manual-allocation-grid-head">Hours</div>
                        <div className="manual-allocation-grid-head">Percent</div>

                        {draft.engineers.map((item) => {
                          const engineer = engineers.find((candidate) => candidate.userId === item.userId);

                          return (
                            <div className="manual-allocation-row" key={`${request.id}-manual-${item.userId}`}>
                              <div>
                                <strong>{engineer?.displayName ?? 'Selected engineer'}</strong>
                                <small>{engineer?.primaryFunction ?? 'Function not recorded'}</small>
                              </div>
                              <input
                                type="number"
                                min="0"
                                step="0.25"
                                placeholder="Hours"
                                value={item.allocatedHours ?? ''}
                                onChange={(event) => updateEngineerAllocation(request.id, item.userId, 'allocatedHours', event.target.value)}
                              />
                              <input
                                type="number"
                                min="0"
                                max="100"
                                step="0.01"
                                placeholder="%"
                                value={item.allocationPercent ?? ''}
                                onChange={(event) => updateEngineerAllocation(request.id, item.userId, 'allocationPercent', event.target.value)}
                              />
                            </div>
                          );
                        })}
                      </div>

                      <p className="manual-allocation-note">
                        Manual mode lets you split the requested project hours across up to 15 engineers. Even-hours and even-percent modes calculate the split automatically.
                      </p>
                    </div>
                  ) : null}

                  <label>Assignment notes<textarea value={draft.notes} onChange={(event) => setAssignmentDraft(request.id, (current) => ({ ...current, notes: event.target.value }))} /></label>
                  <button type="button" className="primary-action" onClick={() => assignEngineers(request)}>Assign selected engineers</button>
                </div>
              </div>
            );
          })}
        </div>
      </article>

      <article className="project-intake-panel">
        <div className="project-intake-section-header">
          <div>
            <h3>Project Intake Queue</h3>
            <p className="muted">Use search, status, or the selector when multiple client projects are coming in at the same time.</p>
          </div>
          <StatusBadge>{filteredIntakes.length} matching</StatusBadge>
        </div>

        <div className="queue-control-bar three-column">
          <label>
            Search intake
            <input
              value={intakeSearchTerm}
              placeholder="Search request, client, title, opportunity, source ID..."
              onChange={(event) => {
                setIntakeSearchTerm(event.target.value);
                setSelectedIntakeId('');
              }}
            />
          </label>
          <label>
            Status
            <select value={intakeStatusFilter} onChange={(event) => {
              setIntakeStatusFilter(event.target.value);
              setSelectedIntakeId('');
            }}>
              <option value="all">All statuses</option>
              <option value="new">New</option>
              <option value="triage">Triage</option>
              <option value="approved">Approved</option>
              <option value="converted">Converted</option>
              <option value="closed">Closed</option>
              <option value="cancelled">Cancelled</option>
            </select>
          </label>
          <label>
            Select intake request
            <select value={selectedIntakeId} onChange={(event) => setSelectedIntakeId(event.target.value)}>
              <option value="">Show latest 20 matching intakes</option>
              {filteredIntakes.map((item) => (
                <option value={item.id} key={item.id}>{item.requestNumber} — {item.clientName} — {item.requestTitle}</option>
              ))}
            </select>
          </label>
        </div>

        <div className="project-intake-table-wrap">
          <table className="project-intake-table">
            <thead><tr><th>Request</th><th>Client</th><th>Status</th><th>Priority</th><th>PM</th><th>Source</th><th>Target</th><th>Hours</th><th>Planned cost</th></tr></thead>
            <tbody>
              {visibleIntakes.map((item) => (
                <tr key={item.id}>
                  <td><strong>{item.requestNumber}</strong><span>{item.requestTitle}</span></td>
                  <td>{item.clientName}<span>{fmt(item.opportunityReference)}</span></td>
                  <td><StatusBadge>{item.status}</StatusBadge></td>
                  <td>{item.priority}</td>
                  <td>{fmt(item.assignedPmName)}</td>
                  <td>{item.intakeSource}<span>{item.externalReferenceId || 'No external ID'} · Docs: {item.documentCount ?? 0}</span></td>
                  <td>{fmt(item.targetStartDate)} → {fmt(item.targetCompletionDate)}</td>
                  <td>{item.estimatedHours ?? 0}</td>
                  <td>{fmtMoney(item.plannedTotalProjectCost ?? 0)}<span>Eng: {fmtMoney(item.plannedEngineeringCost ?? 0)} · PM: {fmtMoney(item.plannedPmCost ?? 0)}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>

      <article className="project-intake-panel">
        <h3>Capacity & Skills</h3>
        <div className="project-intake-card-grid">
          {capacity.map((item) => (
            <div className="capacity-card" key={item.userId}>
              <strong>{item.displayName}</strong>
              <span>{item.primaryFunction}</span>
              <div className="capacity-meter"><div style={{ width: `${Math.min(Number(item.plannedUtilizationPercent || 0), 100)}%` }} /></div>
              <small>{item.assignedHours}/{item.availableHours} hrs · {item.plannedUtilizationPercent}% planned · {item.capacityStatus}</small>
              <p>{item.qualifications}</p>
            </div>
          ))}
        </div>
      </article>

      <article className="project-intake-panel">
        <h3>Project Workspace Readiness</h3>
        <div className="project-intake-card-grid">
          {projects.map((project) => (
            <div className="workspace-card" key={project.id}>
              <strong>{project.projectCode}</strong>
              <h4>{project.projectName}</h4>
              <p>{project.clientName}</p>
              <small>{project.taskCount} tasks · {project.assignmentCount} assignments · PM: {fmt(project.projectManagerName)}</small>
              <StatusBadge tone={project.status === 'active' ? 'safe' : 'neutral'}>{project.status}</StatusBadge>
            </div>
          ))}
        </div>
      </article>
    </section>
  );
}

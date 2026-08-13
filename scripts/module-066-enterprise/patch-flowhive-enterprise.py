#!/usr/bin/env python3
from __future__ import annotations

import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, value: str) -> None:
    (ROOT / path).write_text(value, encoding="utf-8")


def replace_once(value: str, old: str, new: str, label: str) -> str:
    count = value.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return value.replace(old, new, 1)


def regex_once(value: str, pattern: str, replacement: str, label: str) -> str:
    updated, count = re.subn(pattern, replacement, value, count=1, flags=re.S)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one regex anchor, found {count}")
    return updated


# ---------------------------------------------------------------------------
# Backend route registration, capability metadata, and professional artifacts.
# ---------------------------------------------------------------------------
module_path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs"
module = read(module_path)
module = replace_once(
    module,
    "\n        return app;\n    }",
    "\n        app.MapProjectFlowHiveEnterpriseEndpoints();\n\n        return app;\n    }",
    "map enterprise FlowHive endpoints",
)
module = replace_once(
    module,
    "            customerExportEnabled = false,\n            customerSharingEnabled = false,",
    "            customerExportEnabled = true,\n            customerSharingEnabled = true,\n            customerSharingRequiresReviewedBaseline = true,",
    "enable reviewed customer sharing capability",
)
module = module.replace('brandedPdfAndExcel = "internal_draft_available"', 'brandedPdfAndExcel = "professional_working_plan_available"')
module = module.replace('customerSharingAvailable = false', 'customerSharingAvailable = true')
module = module.replace('"PDF and Excel previews are US Signal branded internal drafts; customer sharing remains disabled."', '"PDF and Excel outputs are US Signal branded Project Management working plans; customer links require an exact reviewed baseline, explicit project enablement, expiration, and audit."')
module = module.replace('new { code = "customer_sharing", priority = "P1", status = "locked", evidence = "No customer link, token, delivery, or external state change" }', 'new { code = "customer_sharing", priority = "P1", status = "production_ready", evidence = "Expiring, revocable, token-hashed customer-safe links tied to exact reviewed baselines" }')
module = module.replace('status = "internal_preview_ready_customer_sharing_locked"', 'status = "professional_working_plan_ready_reviewed_customer_sharing_available"')
module = module.replace('"Artifacts are marked as internal drafts."', '"Working-plan artifacts are clearly marked as requiring review until a baseline is established."')
module = module.replace('"No external link is created."', '"Artifact download alone does not create a customer link."')
module = module.replace('"Customer baseline export remains locked."', '"Customer links require an exact reviewer-approved baseline version."')
module = module.replace('"Delivery and customer access require separate authorization."', '"Customer access requires PM ownership, explicit project enablement, expiration, and immutable access audit."')
module = module.replace('$"{SafeFileName(request.Plan?.PlanName)}-internal-draft.pdf"', '$"{SafeFileName(request.Plan?.PlanName)}-project-management-plan.pdf"')
module = module.replace('$"{SafeFileName(request.Plan?.PlanName)}-internal-draft.xlsx"', '$"{SafeFileName(request.Plan?.PlanName)}-project-management-plan.xlsx"')
write(module_path, module)

artifact_path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveArtifactRenderer.cs"
artifact = read(artifact_path)
artifact = replace_once(
    artifact,
    'private const string DraftLabel = "INTERNAL DRAFT — NOT A CUSTOMER BASELINE";',
    'private const string DraftLabel = "PROJECT MANAGEMENT WORKING PLAN — REVIEW REQUIRED";',
    "professional artifact status label",
)
artifact = replace_once(
    artifact,
    '''        summary.Cell("A11").Value = "Logo checksum";
        summary.Cell("B11").Value = ProjectFlowHiveBrandAssets.LogoSha256;
        summary.Range("A5:A11").Style.Font.Bold = true;
        summary.Columns("A:D").AdjustToContents();''',
    '''        summary.Cell("A11").Value = "Executive summary";
        summary.Cell("B11").Value = ExecutiveSummary(request.Plan);
        summary.Cell("B11").Style.Alignment.WrapText = true;
        summary.Cell("A12").Value = "Logo checksum";
        summary.Cell("B12").Value = ProjectFlowHiveBrandAssets.LogoSha256;
        summary.Range("A5:A12").Style.Font.Bold = true;
        summary.Row(11).Height = 72;
        summary.Columns("A:D").AdjustToContents();
        summary.Column("B").Width = Math.Max(summary.Column("B").Width, 72d);''',
    "excel executive summary",
)
artifact = artifact.replace("const int rowsPerPage = 18;", "const int rowsPerPage = 16;")
artifact = replace_once(
    artifact,
    '''        PdfText(content, 560, 480, 8, $"Tasks: {tasks.Count} on this page | Critical tasks: {schedule.CriticalTaskCount}", false, "0.18 0.25 0.34");

        content.Append("0.04 0.17 0.29 rg 36 444 936 24 re f\\n");''',
    '''        PdfText(content, 560, 480, 8, $"Tasks: {tasks.Count} on this page | Critical tasks: {schedule.CriticalTaskCount}", false, "0.18 0.25 0.34");
        PdfText(content, 36, 462, 7, $"Executive summary: {Truncate(ExecutiveSummary(request.Plan), 150)}", false, "0.18 0.25 0.34");

        content.Append("0.04 0.17 0.29 rg 36 430 936 24 re f\\n");''',
    "pdf executive summary",
)
artifact = artifact.replace("foreach (var (label, x) in headings) PdfText(content, x, 453, 5.8", "foreach (var (label, x) in headings) PdfText(content, x, 439, 5.8")
artifact = artifact.replace("        var y = 425;", "        var y = 411;")
artifact = replace_once(
    artifact,
    '''    private static string Join(string? code, string? name) =>
        string.Join(" — ", new[] { code, name }.Where(value => !string.IsNullOrWhiteSpace(value)));''',
    '''    private static string ExecutiveSummary(ProjectFlowHivePlanRequest? plan)
    {
        if (!string.IsNullOrWhiteSpace(plan?.Notes)) return Truncate(plan.Notes, 900);
        var tasks = (plan?.Tasks ?? []).Where(task => !task.IsSummary).ToArray();
        var complete = tasks.Count(task => task.Status?.Equals("complete", StringComparison.OrdinalIgnoreCase) == true
            || task.PercentComplete >= 100m);
        var blocked = tasks.Count(task => task.Status?.Equals("blocked", StringComparison.OrdinalIgnoreCase) == true);
        var progress = tasks.Length == 0 ? 0m : tasks.Average(task => Math.Clamp(task.PercentComplete, 0m, 100m));
        return $"This Project Management working plan contains {tasks.Length} executable task(s) across Plan, Design, Implement, Validate, and Release. {complete} task(s) are complete, {blocked} are blocked, and average task progress is {Math.Round(progress, 0, MidpointRounding.AwayFromZero)}%. Review scope, dependencies, assignments, RAID, financials, and schedule evidence before establishing a customer baseline.";
    }

    private static string Join(string? code, string? name) =>
        string.Join(" — ", new[] { code, name }.Where(value => !string.IsNullOrWhiteSpace(value)));''',
    "artifact executive summary helper",
)
write(artifact_path, artifact)

# ---------------------------------------------------------------------------
# Frontend enterprise PM experience.
# ---------------------------------------------------------------------------
frontend_path = "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx"
frontend = read(frontend_path)
frontend = replace_once(
    frontend,
    "import useIdentityProfile from './identity/useIdentityProfile.js';\n",
    "import useIdentityProfile from './identity/useIdentityProfile.js';\nimport { addFlowHiveTask, deleteFlowHiveTask, dependencyTypeHelp, deriveFlowHiveExecutiveSummary, moveFlowHiveTask, moveFlowHiveTaskByOffset, phaseDefinitions, workingDaysInclusive } from './flowhive-enterprise-helpers.js';\nimport { FlowHiveCustomerSharingPanel, FlowHiveEvidenceReadiness, FlowHiveFinancialsPanel, FlowHiveSaveBar, FlowHiveStatusRaidPanel } from './ProjectFlowHiveEnterprisePanels.jsx';\n",
    "enterprise frontend imports",
)
frontend = replace_once(
    frontend,
    "  { id: 'timeline', label: 'Timeline & risk' },\n  { id: 'ai', label: 'AI draft studio' },",
    "  { id: 'timeline', label: 'Timeline & risk' },\n  { id: 'financials', label: 'Financials' },\n  { id: 'status', label: 'Status & RAID' },\n  { id: 'ai', label: 'AI draft studio' },",
    "enterprise views",
)
frontend = replace_once(
    frontend,
    "const plannerStatuses = ['not_started', 'in_progress', 'blocked', 'complete'];\n",
    """const plannerStatuses = ['not_started', 'in_progress', 'blocked', 'complete'];
const enterprisePhases = phaseDefinitions();
const defaultControls = { contractType: 'unknown', currencyCode: 'USD', approvedBudget: null, expenseBudget: null, contingencyBudget: null, forecastAtCompletion: null, percentCompleteMethod: 'task_weighted', statusReportCadence: 'weekly', customerSharingEnabled: false, financialNotes: '' };
const defaultRaid = { planId: null, itemType: 'risk', title: '', description: '', status: 'open', priority: 'medium', probability: null, impact: null, ownerUserId: null, dueDate: null, mitigation: '', sourceKind: 'manual', sourceReference: '' };
const defaultStatusDraft = { overallHealth: 'green', scheduleHealth: 'green', financialHealth: 'unknown', scopeHealth: 'green', executiveSummary: '', accomplishments: [], nextSteps: [], decisionsNeeded: [], keyRisks: [], generatedSource: 'deterministic' };
const defaultShareDraft = { planId: '', versionNumber: null, expirationDays: 30, customerLabel: '', shareNote: '', allowedArtifacts: ['view', 'pdf'] };
""",
    "enterprise defaults",
)
frontend = replace_once(
    frontend,
    "async function postJson(path, body) {\n  return parseResponse(await fetch(path, {\n    method: 'POST',\n    headers: authenticationHeaders({ 'Content-Type': 'application/json' }),\n    body: JSON.stringify(body)\n  }), path);\n}\n",
    """async function postJson(path, body) {
  return parseResponse(await fetch(path, {
    method: 'POST',
    headers: authenticationHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify(body)
  }), path);
}

async function putJson(path, body) {
  return parseResponse(await fetch(path, {
    method: 'PUT',
    headers: authenticationHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify(body)
  }), path);
}

async function deleteJson(path, body = null) {
  return parseResponse(await fetch(path, {
    method: 'DELETE',
    headers: authenticationHeaders(body ? { 'Content-Type': 'application/json' } : {}),
    ...(body ? { body: JSON.stringify(body) } : {})
  }), path);
}
""",
    "put and delete JSON helpers",
)
frontend = replace_once(
    frontend,
    "  const [requestedOutcome, setRequestedOutcome] = useState('Create a reviewable implementation plan with detailed tasks, dependencies, risks, assumptions, milestones, acceptance, operational handoff, and closeout.');\n",
    """  const [requestedOutcome, setRequestedOutcome] = useState('Create a reviewable implementation plan with detailed tasks, dependencies, risks, assumptions, milestones, acceptance, operational handoff, and closeout.');
  const [enterprise, setEnterprise] = useState(null);
  const [financials, setFinancials] = useState(null);
  const [controls, setControls] = useState(defaultControls);
  const [dirty, setDirty] = useState(false);
  const [draggedTaskWbs, setDraggedTaskWbs] = useState('');
  const [newRaid, setNewRaid] = useState(defaultRaid);
  const [statusDraft, setStatusDraft] = useState(defaultStatusDraft);
  const [shareDraft, setShareDraft] = useState(defaultShareDraft);
  const [latestShareUrl, setLatestShareUrl] = useState('');
""",
    "enterprise state",
)
frontend = replace_once(
    frontend,
    "  useEffect(() => {\n    loadModule();\n  }, []);",
    """  async function loadEnterpriseWorkspace(projectId, applyWorkingCopy = false) {
    if (!projectId) {
      setEnterprise(null);
      setFinancials(null);
      setControls(defaultControls);
      return;
    }
    try {
      const result = await getJson(`/api/project-flowhive/projects/${projectId}/enterprise`);
      setEnterprise(result);
      setControls({ ...defaultControls, ...(result.controls || {}) });
      setShareDraft((current) => ({ ...current, customerLabel: result.project?.customerName || current.customerLabel }));
      if (applyWorkingCopy && result.workingCopy?.plan) {
        setDraftPlan(result.workingCopy.plan);
        setSchedule(null);
        setValidation(null);
        setDirty(false);
        setNotice(`Loaded PM working-copy revision ${result.workingCopy.workingRevision}.`);
      }
    } catch (workspaceError) {
      setEnterprise(null);
      if (workspaceError.responseBody?.status !== 'migration_086_required') setError(workspaceError.message);
    }
    try {
      setFinancials(await getJson(`/api/project-financials/projects/${projectId}?workspace=project_management`));
    } catch (financialError) {
      setFinancials({ status: 'financial_data_unavailable', message: financialError.message, project: null });
    }
  }

  useEffect(() => {
    loadModule();
  }, []);

  useEffect(() => {
    if (selectedProjectId) loadEnterpriseWorkspace(selectedProjectId, false);
    else {
      setEnterprise(null);
      setFinancials(null);
    }
  }, [selectedProjectId]);""",
    "enterprise loader",
)
frontend = frontend.replace("    setDraftPlan(buildLocalDraft(selectedProject, tasks, assignments));\n", "    setDraftPlan(buildLocalDraft(selectedProject, tasks, assignments));\n    setDirty(true);\n", 1)
frontend = replace_once(
    frontend,
    "  function updatePlan(field, value) {\n    setDraftPlan((current) => current ? { ...current, [field]: value } : current);\n    setSchedule(null);\n  }",
    "  function updatePlan(field, value) {\n    setDraftPlan((current) => current ? { ...current, [field]: value } : current);\n    setSchedule(null);\n    setDirty(true);\n  }",
    "dirty plan updates",
)
frontend = frontend.replace("    setSchedule(null);\n  }\n\n  function updateDependencyForTask", "    setSchedule(null);\n    setDirty(true);\n  }\n\n  function updateDependencyForTask", 1)
frontend = frontend.replace("    setSchedule(null);\n  }\n\n  function updateTaskResource", "    setSchedule(null);\n    setDirty(true);\n  }\n\n  function updateTaskResource", 1)
frontend = frontend.replace("    setSchedule(null);\n  }\n\n  function addTask()", "    setSchedule(null);\n    setDirty(true);\n  }\n\n  function addTask()", 1)
frontend = regex_once(
    frontend,
    r"  function addTask\(\) \{.*?\n  \}\n\n  async function validatePlan\(\)",
    """  function addTask(phaseWbs) {
    setDraftPlan((current) => addFlowHiveTask(current, phaseWbs, localTask));
    setSchedule(null);
    setValidation(null);
    setDirty(true);
    setCollapsedPhases((current) => { const next = new Set(current); next.delete(String(phaseWbs)); return next; });
    setNotice(`Added a new ${enterprisePhases.find((phase) => phase.wbs === String(phaseWbs))?.name || 'project'} task. Complete its details and save the working copy.`);
  }

  function deleteTask(wbsNumber) {
    const task = draftPlan?.tasks?.find((candidate) => !candidate.isSummary && candidate.wbsNumber === wbsNumber);
    if (!task || !window.confirm(`Delete WBS ${wbsNumber} — ${task.name}? Dependencies and assignments referencing this task will be repaired or removed.`)) return;
    setDraftPlan((current) => deleteFlowHiveTask(current, wbsNumber));
    setExpandedTaskWbs('');
    setSchedule(null);
    setValidation(null);
    setDirty(true);
    setNotice(`Deleted WBS ${wbsNumber}. Review the dependency chain, then recalculate the schedule.`);
  }

  function dropTask(targetWbs, targetPhaseWbs, placement = 'before') {
    if (!draggedTaskWbs || draggedTaskWbs === targetWbs) return;
    setDraftPlan((current) => moveFlowHiveTask(current, draggedTaskWbs, targetWbs, targetPhaseWbs, placement));
    setDraggedTaskWbs('');
    setSchedule(null);
    setValidation(null);
    setDirty(true);
    setNotice('Task moved and WBS values were renumbered. Review dependencies before saving.');
  }

  function changeTaskPhase(wbsNumber, phaseWbs) {
    setDraftPlan((current) => moveFlowHiveTask(current, wbsNumber, '', phaseWbs, 'after'));
    setSchedule(null);
    setValidation(null);
    setDirty(true);
  }

  function moveTaskOffset(wbsNumber, offset) {
    setDraftPlan((current) => moveFlowHiveTaskByOffset(current, wbsNumber, offset));
    setSchedule(null);
    setValidation(null);
    setDirty(true);
  }

  function updateTaskStartDate(index, value) {
    setDraftPlan((current) => {
      if (!current) return current;
      const nextTasks = current.tasks.map((task, taskIndex) => taskIndex === index
        ? { ...task, constraintType: value ? 'SNET' : 'ASAP', constraintDate: value || null }
        : task);
      return { ...current, tasks: nextTasks };
    });
    setSchedule(null);
    setDirty(true);
  }

  function updateTaskEndDate(index, value, scheduledStart) {
    if (!value) return;
    setDraftPlan((current) => {
      if (!current) return current;
      const task = current.tasks[index];
      const start = task.constraintDate || scheduledStart || current.projectStartDate;
      const durationWorkingDays = workingDaysInclusive(start, value);
      return { ...current, tasks: current.tasks.map((candidate, taskIndex) => taskIndex === index ? { ...candidate, durationWorkingDays, remainingEffortHours: Math.max(Number(candidate.remainingEffortHours || 0), durationWorkingDays * 8) } : candidate) };
    });
    setSchedule(null);
    setDirty(true);
  }

  async function saveWorkingCopy() {
    if (!draftPlan || !selectedProjectId) return;
    setBusy('working-copy');
    setError('');
    try {
      const result = await putJson(`/api/project-flowhive/projects/${selectedProjectId}/working-copy`, {
        plan: draftPlan,
        expectedRowVersion: enterprise?.workingCopy?.rowVersion || null
      });
      setDirty(false);
      setNotice(`PM working-copy revision ${result.workingRevision} saved. The canonical project and immutable plan history were not changed.`);
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function saveProjectControls(nextControls = controls) {
    if (!selectedProjectId) return;
    setBusy('controls');
    setError('');
    try {
      await putJson(`/api/project-flowhive/projects/${selectedProjectId}/controls`, nextControls);
      setControls(nextControls);
      setNotice('Project financial and reporting controls were saved.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function createRaidItem() {
    if (!selectedProjectId) return;
    setBusy('raid-create');
    setError('');
    try {
      await postJson(`/api/project-flowhive/projects/${selectedProjectId}/raid`, { ...newRaid, planId: draftPlan?.planId || null });
      setNewRaid(defaultRaid);
      setNotice('RAID item added.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function deleteRaidItem(item) {
    if (!selectedProjectId || !window.confirm(`Delete ${item.itemType}: ${item.title}?`)) return;
    setBusy(`raid-delete-${item.raidItemId}`);
    setError('');
    try {
      await deleteJson(`/api/project-flowhive/projects/${selectedProjectId}/raid/${item.raidItemId}`);
      setNotice('RAID item deleted.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  function generateStatusSummary() {
    setStatusDraft((current) => ({
      ...current,
      executiveSummary: deriveFlowHiveExecutiveSummary(draftPlan, schedule, enterprise, aiPreview, financials),
      generatedSource: aiPreview?.correlationId ? 'celar_ai' : 'deterministic',
      keyRisks: (enterprise?.raidItems || []).filter((item) => item.itemType === 'risk' && !['closed', 'resolved'].includes(item.status)).map((item) => item.title).slice(0, 12)
    }));
  }

  async function createStatusReport() {
    if (!selectedProjectId) return;
    setBusy('status-report');
    setError('');
    try {
      const saved = savedPlans.find((plan) => plan.planId === draftPlan?.planId);
      await postJson(`/api/project-flowhive/projects/${selectedProjectId}/status-reports`, {
        ...statusDraft,
        planId: draftPlan?.planId || null,
        planVersionNumber: saved?.currentVersion || null,
        statusDate: currentIsoDate(),
        financialSnapshot: financials?.project || {},
        scheduleSnapshot: schedule || {},
        celarAiCorrelationId: aiPreview?.correlationId || draftPlan?.celarAiCorrelationId || ''
      });
      setNotice('Immutable Project Manager status report created.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function prepareSowEvidence(item, approveCurrentVersion) {
    if (!selectedProjectId) return;
    const approvalNote = approveCurrentVersion
      ? window.prompt('Enter the reviewed SOW version approval note (at least 10 characters):', 'Reviewed by the assigned Project Manager for FlowHive planning evidence.')
      : '';
    if (approveCurrentVersion && (!approvalNote || approvalNote.trim().length < 10)) return;
    setBusy(`evidence-${item.documentId}`);
    setError('');
    try {
      const result = await postJson(`/api/project-flowhive/projects/${selectedProjectId}/sow-evidence/${item.documentId}/prepare`, {
        approveCurrentVersion,
        approvalNote,
        correlationId: aiPreview?.correlationId || crypto.randomUUID()
      });
      setNotice(result.message);
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function enableCustomerSharing() {
    const next = { ...controls, customerSharingEnabled: true };
    await saveProjectControls(next);
  }

  async function createCustomerShare() {
    if (!selectedProjectId) return;
    setBusy('customer-share');
    setError('');
    try {
      const result = await postJson(`/api/project-flowhive/projects/${selectedProjectId}/customer-shares`, shareDraft);
      setLatestShareUrl(result.share?.shareUrl || '');
      setNotice('Reviewed customer link created. The full token is displayed once.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function revokeCustomerShare(share) {
    if (!selectedProjectId || !window.confirm('Revoke this customer link immediately?')) return;
    setBusy(`share-revoke-${share.shareId}`);
    setError('');
    try {
      await deleteJson(`/api/project-flowhive/projects/${selectedProjectId}/customer-shares/${share.shareId}`, { reason: 'Revoked by the assigned Project Manager.' });
      setNotice('Customer link revoked.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function validatePlan()""",
    "replace implementation-only task handler",
)
frontend = frontend.replace("      setDraftPlan((current) => current ? { ...current, planId: result.planId } : current);", "      setDraftPlan((current) => current ? { ...current, planId: result.planId } : current);\n      setDirty(false);")
frontend = frontend.replace("      setSavedPlans(plansResult.plans || []);\n    } catch (actionError) {\n      setError(actionError.message);\n    } finally {\n      setBusy('');\n    }\n  }\n\n  async function establishBaseline", "      setSavedPlans(plansResult.plans || []);\n      await loadEnterpriseWorkspace(selectedProjectId, false);\n    } catch (actionError) {\n      setError(actionError.message);\n    } finally {\n      setBusy('');\n    }\n  }\n\n  async function establishBaseline", 1)
frontend = frontend.replace("      setNotice(`FlowHive version ${result.version} is now the reviewer-approved baseline.`);", "      setNotice(`FlowHive version ${result.version} is now the reviewer-approved baseline.`);\n      await loadEnterpriseWorkspace(selectedProjectId, false);")
frontend = frontend.replace("      setNotice(`Loaded immutable FlowHive version ${result.summary.currentVersion}.`);", "      setNotice(`Loaded immutable FlowHive version ${result.summary.currentVersion}.`);\n      setDirty(false);")
frontend = frontend.replace("          celarAiConfidence: result.confidence ?? null\n        });", "          celarAiConfidence: result.confidence ?? null\n        });\n        setDirty(true);")
frontend = replace_once(
    frontend,
    "    } catch (actionError) {\n      setError(actionError.message);\n    } finally {\n      setBusy('');\n    }\n  }\n\n  function togglePhase",
    """    } catch (actionError) {
      if (actionError.responseBody?.status === 'flowhive_sow_evidence_not_ready') {
        const details = [...(actionError.responseBody.missingEvidence || []), ...(actionError.responseBody.warnings || [])].filter(Boolean).slice(0, 5);
        setError(`AI Planner is waiting for an approved, citation-ready SOW Scope of Services. ${details.join(' ') || 'Open AI draft studio to review each document readiness blocker.'}`);
        setActiveView('ai');
        await loadEnterpriseWorkspace(selectedProjectId, false);
      } else {
        setError(actionError.message);
      }
    } finally {
      setBusy('');
    }
  }

  function togglePhase""",
    "actionable AI evidence error",
)
frontend = frontend.replace("artifactTitle: `${draftPlan.planName} — internal preview`,", "artifactTitle: `${draftPlan.planName} — Project Management working plan`,")
frontend = frontend.replace("anchor.download = `${draftPlan.projectCode || 'project-flowhive'}-internal-draft.${format === 'excel' ? 'xlsx' : 'pdf'}`;", "anchor.download = `${draftPlan.projectCode || 'project-flowhive'}-project-management-plan.${format === 'excel' ? 'xlsx' : 'pdf'}`;")
frontend = frontend.replace("setNotice(`US Signal branded ${format === 'excel' ? 'Excel' : 'PDF'} internal draft generated. No external link was created.`);", "setNotice(`US Signal branded ${format === 'excel' ? 'Excel' : 'PDF'} Project Management working plan generated. Customer sharing remains a separate reviewed action.`);")
frontend = frontend.replace("<span>Saving creates a separate governed plan version and never changes canonical tasks. Customer delivery still requires an explicit reviewed action.</span>", "<span>Use the PM working copy for frequent updates, immutable versions for formal review, and an exact reviewed baseline for governed customer sharing.</span>")
frontend = frontend.replace("<div><span>Customer links</span><strong>Disabled</strong></div>", "<div><span>Customer links</span><strong>{enterprise?.access?.canShare ? (controls.customerSharingEnabled ? 'Enabled for reviewed baseline' : 'Available — enable in Financials') : 'Read-only / unavailable'}</strong></div>")
frontend = frontend.replace("setDraftPlan(buildLocalDraft(project, tasks, assignments)); setSchedule(null);", "setDraftPlan(buildLocalDraft(project, tasks, assignments)); setDirty(true); setSchedule(null);")
frontend = replace_once(
    frontend,
    "            <button type=\"button\" onClick={createLocalDraft} disabled={!selectedProject}>Create/reset draft</button>",
    "            <button type=\"button\" onClick={createLocalDraft} disabled={!selectedProject}>Create/reset draft</button><button type=\"button\" onClick={() => loadEnterpriseWorkspace(selectedProjectId, true)} disabled={!enterprise?.workingCopy}>Load working copy</button>",
    "load working copy control",
)
frontend = replace_once(
    frontend,
    "          <div className=\"flowhive-plan-metadata\">\n            <label>Saved FlowHive plan",
    "          <FlowHiveSaveBar dirty={dirty} workingCopy={enterprise?.workingCopy} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onSaveWorkingCopy={saveWorkingCopy} onSaveVersion={saveDraft} />\n          <div className=\"flowhive-plan-metadata\">\n            <label>Saved FlowHive plan",
    "prominent save bar",
)
frontend = replace_once(
    frontend,
    "<div className=\"flowhive-table-heading\"><div><h3>AI Planner work breakdown</h3><p>Expand each phase and task for the complete steps, inputs, outputs, validation, acceptance, responsibilities, risks, questions, and private citations. Save creates an immutable FlowHive version without modifying canonical tasks.</p></div><button type=\"button\" onClick={addTask}>Add implementation task</button></div>",
    "<div className=\"flowhive-table-heading\"><div><h3>AI Planner work breakdown</h3><p>Expand each phase and task for complete steps, inputs, outputs, validation, acceptance, responsibilities, risks, questions, and private citations. Drag tasks to reorder or move them between phases. Delete is available for mistakenly added tasks.</p></div><div className=\"flowhive-phase-add-actions\">{enterprisePhases.map((phase) => <button type=\"button\" key={phase.wbs} disabled={!enterprise?.access?.canManage} onClick={() => addTask(phase.wbs)}>Add {phase.name} task</button>)}</div></div>",
    "phase-aware add controls",
)
frontend = replace_once(
    frontend,
    "<thead><tr><th>WBS</th><th>Task Name</th><th>Start Date</th><th>End Date</th><th>Duration in Days</th><th>Progress</th><th>Predecessor</th><th>Type</th><th>Comments</th><th>Notes</th><th>Assigned Identity</th></tr></thead>",
    "<thead><tr><th title=\"Work Breakdown Structure number. FlowHive renumbers child tasks after a move or deletion.\">WBS</th><th title=\"The scoped activity or phase deliverable.\">Task Name</th><th title=\"Calculated start date. Enter a date to set a Start No Earlier Than constraint.\">Start Date</th><th title=\"Calculated finish date. Editing it recalculates task duration in working days.\">End Date</th><th title=\"Weekday duration, excluding weekends.\">Duration in Days</th><th title=\"Completion percentage from 0 through 100.\">Progress</th><th title=\"The WBS task that controls this task. Start means no predecessor.\">Predecessor</th><th title={`${dependencyTypeHelp.FS} ${dependencyTypeHelp.SS} ${dependencyTypeHelp.FF} ${dependencyTypeHelp.SF}`}>Type</th><th title=\"Review and collaboration comments.\">Comments</th><th title=\"Internal task notes included in the PM working artifact, but excluded from customer links.\">Notes</th><th title=\"Module 062 identity assigned to the task.\">Assigned Identity</th></tr></thead>",
    "planner header help",
)
frontend = frontend.replace(
    "return <tr key={task.clientTaskId || task.wbsNumber} className={`flowhive-phase-row phase-${String(task.phase || task.name).toLowerCase()}`}>",
    "return <tr key={task.clientTaskId || task.wbsNumber} className={`flowhive-phase-row phase-${String(task.phase || task.name).toLowerCase()}`} onDragOver={(event) => event.preventDefault()} onDrop={() => dropTask('', task.wbsNumber, 'after')}>",
)
frontend = frontend.replace(
    "<td><strong>{task.name}</strong><small>{draftPlan.tasks.filter((candidate) => candidate.parentWbsNumber === task.wbsNumber).length} detailed task(s)</small></td>",
    "<td><div className=\"flowhive-phase-name-actions\"><span><strong>{task.name}</strong><small>{draftPlan.tasks.filter((candidate) => candidate.parentWbsNumber === task.wbsNumber).length} detailed task(s)</small></span><button type=\"button\" disabled={!enterprise?.access?.canManage} onClick={() => addTask(task.wbsNumber)}>Add task</button></div></td>",
)
frontend = frontend.replace(
    "<tr className={`flowhive-work-row phase-${String(task.phase || '').toLowerCase()}`}>",
    "<tr className={`flowhive-work-row phase-${String(task.phase || '').toLowerCase()} ${draggedTaskWbs === task.wbsNumber ? 'dragging' : ''}`} draggable={Boolean(enterprise?.access?.canManage)} onDragStart={() => setDraggedTaskWbs(task.wbsNumber)} onDragEnd={() => setDraggedTaskWbs('')} onDragOver={(event) => event.preventDefault()} onDrop={() => dropTask(task.wbsNumber, task.parentWbsNumber, 'before')}>",
)
frontend = frontend.replace(
    "<td><span className=\"flowhive-wbs-child\">{task.wbsNumber}</span></td>",
    "<td><span className=\"flowhive-wbs-child\" title=\"Drag this row to reorder or move it to another phase\"><span aria-hidden=\"true\">⋮⋮</span>{task.wbsNumber}</span></td>",
)
frontend = frontend.replace(
    "<button type=\"button\" className=\"flowhive-inline-detail-button\" onClick={() => setExpandedTaskWbs(detailOpen ? '' : task.wbsNumber)} aria-expanded={detailOpen}>{detailOpen ? 'Close details' : 'Task details'}</button></div>",
    "<button type=\"button\" className=\"flowhive-inline-detail-button\" onClick={() => setExpandedTaskWbs(detailOpen ? '' : task.wbsNumber)} aria-expanded={detailOpen}>{detailOpen ? 'Close details' : 'Task details'}</button><button type=\"button\" className=\"danger-quiet\" disabled={!enterprise?.access?.canManage} onClick={() => deleteTask(task.wbsNumber)}>Delete</button></div>",
)
frontend = frontend.replace(
    "<td><span>{formatDate(scheduledTask?.startDate)}</span></td>\n                          <td><span>{formatDate(scheduledTask?.endDate)}</span></td>",
    "<td><input className=\"flowhive-date-cell\" aria-label={`Start date for ${task.name}`} type=\"date\" value={task.constraintDate || scheduledTask?.startDate || ''} onChange={(event) => updateTaskStartDate(index, event.target.value)} /></td>\n                          <td><input className=\"flowhive-date-cell\" aria-label={`End date for ${task.name}`} type=\"date\" min={task.constraintDate || scheduledTask?.startDate || draftPlan.projectStartDate || undefined} value={scheduledTask?.endDate || ''} onChange={(event) => updateTaskEndDate(index, event.target.value, scheduledTask?.startDate)} /></td>",
    1,
)
frontend = replace_once(
    frontend,
    "<label>Lead / lag working days<input aria-label={`Lead or lag for ${task.name}`} type=\"number\" min=\"-365\" max=\"365\" value={dependency?.lagWorkingDays || 0} disabled={!dependency?.predecessorWbs} onChange={(event) => updateDependencyForTask(index, 'lagWorkingDays', Number(event.target.value))} /></label>",
    "<label>Lead / lag working days<input aria-label={`Lead or lag for ${task.name}`} type=\"number\" min=\"-365\" max=\"365\" value={dependency?.lagWorkingDays || 0} disabled={!dependency?.predecessorWbs} onChange={(event) => updateDependencyForTask(index, 'lagWorkingDays', Number(event.target.value))} /></label><label>Move to phase<select value={task.parentWbsNumber} disabled={!enterprise?.access?.canManage} onChange={(event) => changeTaskPhase(task.wbsNumber, event.target.value)}>{enterprisePhases.map((phase) => <option key={phase.wbs} value={phase.wbs}>{phase.name}</option>)}</select></label><div className=\"flowhive-task-move-actions\"><button type=\"button\" disabled={!enterprise?.access?.canManage} onClick={() => moveTaskOffset(task.wbsNumber, -1)}>Move up</button><button type=\"button\" disabled={!enterprise?.access?.canManage} onClick={() => moveTaskOffset(task.wbsNumber, 1)}>Move down</button><button type=\"button\" className=\"danger-quiet\" disabled={!enterprise?.access?.canManage} onClick={() => deleteTask(task.wbsNumber)}>Delete task</button></div>",
    "task movement detail controls",
)
frontend = replace_once(
    frontend,
    "      {activeView === 'timeline' ? (",
    """      {activeView === 'financials' ? <FlowHiveFinancialsPanel enterprise={enterprise} financials={financials} controls={controls} setControls={setControls} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onSave={() => saveProjectControls()} /> : null}

      {activeView === 'status' ? <FlowHiveStatusRaidPanel enterprise={enterprise} draftPlan={draftPlan} statusDraft={statusDraft} setStatusDraft={setStatusDraft} newRaid={newRaid} setNewRaid={setNewRaid} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onCreateRaid={createRaidItem} onDeleteRaid={deleteRaidItem} onGenerateSummary={generateStatusSummary} onCreateStatusReport={createStatusReport} /> : null}

      {activeView === 'timeline' ? (""",
    "financial and status views",
)
frontend = replace_once(
    frontend,
    "          </div>\n          {!draftPlan ? <EmptyState>Create or load a plan draft first.</EmptyState>",
    "          </div>\n          <FlowHiveEvidenceReadiness enterprise={enterprise} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onPrepare={prepareSowEvidence} />\n          {!draftPlan ? <EmptyState>Create or load a plan draft first.</EmptyState>",
    "AI evidence readiness panel",
)
frontend = frontend.replace("<div className=\"flowhive-export-hero\"><img src={usSignalLogoUrl} alt=\"US Signal\" /><div><h3>US Signal branded internal artifacts</h3><p>PDF and Excel source embeds the governed logo. Every artifact is watermarked as an internal draft and creates no customer link.</p>", "<div className=\"flowhive-export-hero\"><img src={usSignalLogoUrl} alt=\"US Signal\" /><div><h3>US Signal Project Management artifacts</h3><p>Professional PDF and Excel working plans include an executive summary, schedule, dependencies, assignments, comments, notes, and artifact control. Customer sharing remains a separate reviewed action.</p>")
frontend = frontend.replace("Download internal PDF draft", "Download PM working-plan PDF")
frontend = frontend.replace("Download internal Excel draft", "Download PM planning workbook")
frontend = replace_once(
    frontend,
    "<article className=\"locked\"><h4>Customer sharing link</h4><p>Expiration, customer isolation, delivery, and access auditing require a separately authorized external-sharing phase.</p><button type=\"button\" disabled>Create customer link — locked</button></article>",
    "<FlowHiveCustomerSharingPanel enterprise={enterprise} controls={controls} savedPlans={savedPlans} draftPlan={draftPlan} latestShareUrl={latestShareUrl} setLatestShareUrl={setLatestShareUrl} shareDraft={shareDraft} setShareDraft={setShareDraft} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onEnableSharing={enableCustomerSharing} onCreateShare={createCustomerShare} onRevoke={revokeCustomerShare} />",
    "customer sharing panel",
)
write(frontend_path, frontend)

# ---------------------------------------------------------------------------
# Responsive enterprise PM styles.
# ---------------------------------------------------------------------------
css_path = "src/frontend/project-time-web/src/project-flowhive-center.css"
css = read(css_path)
enterprise_css = r'''

/* Module 066 enterprise Project Management additions */
.flowhive-save-bar { position: sticky; top: .5rem; z-index: 8; display:flex; align-items:center; justify-content:space-between; gap:1rem; padding:.8rem 1rem; border:1px solid var(--flowhive-line); border-left:5px solid #25834a; border-radius:.85rem; background:var(--flowhive-surface); box-shadow:0 8px 22px rgb(7 29 52 / 12%); }
.flowhive-save-bar.dirty { border-left-color:#c77c13; }
.flowhive-save-bar > div:first-child { display:grid; gap:.16rem; }
.flowhive-save-bar span { color:var(--flowhive-muted); font-size:.78rem; }
.flowhive-save-bar-actions,.flowhive-phase-add-actions,.flowhive-task-move-actions,.flowhive-evidence-actions { display:flex; flex-wrap:wrap; gap:.45rem; }
.flowhive-phase-add-actions { justify-content:flex-end; }
.flowhive-phase-name-actions { display:flex; align-items:center; justify-content:space-between; gap:.55rem; }
.flowhive-phase-name-actions > span { display:grid; gap:.15rem; }
.flowhive-work-row[draggable='true'] { cursor:grab; }
.flowhive-work-row.dragging { opacity:.45; outline:2px dashed var(--flowhive-blue); }
.flowhive-wbs-child { display:inline-flex; align-items:center; gap:.32rem; font-weight:800; }
.flowhive-date-cell { min-width:9rem; width:100%; padding:.48rem; border:1px solid #b9cbd6; border-radius:.45rem; color:var(--flowhive-ink); background:var(--flowhive-surface); }
.danger-quiet { color:#9f2626 !important; border-color:#d9a6a6 !important; background:transparent !important; }
.flowhive-enterprise-card,.flowhive-sharing-card { display:grid; gap:1rem; padding:1.1rem; border:1px solid var(--flowhive-line); border-radius:1rem; background:var(--flowhive-surface); box-shadow:0 6px 18px rgb(20 40 59 / 6%); }
.flowhive-enterprise-card > header,.flowhive-sharing-card > header { display:flex; justify-content:space-between; align-items:flex-start; gap:1rem; }
.flowhive-enterprise-card header span,.flowhive-section-heading span { color:var(--flowhive-blue); font-size:.72rem; font-weight:900; letter-spacing:.08em; text-transform:uppercase; }
.flowhive-enterprise-card h3,.flowhive-section-heading h3 { margin:.15rem 0 0; color:var(--flowhive-navy); }
.flowhive-section-heading p,.flowhive-enterprise-card p { margin:.35rem 0 0; color:var(--flowhive-muted); line-height:1.5; }
.flowhive-evidence-card header > strong { padding:.35rem .62rem; border-radius:999px; font-size:.76rem; }
.flowhive-evidence-card header > strong.ready { color:#166534; background:#dcfce7; }
.flowhive-evidence-card header > strong.blocked { color:#92400e; background:#fef3c7; }
.flowhive-evidence-list { display:grid; gap:.75rem; }
.flowhive-evidence-list article { display:grid; gap:.6rem; padding:.85rem; border:1px solid var(--flowhive-line); border-left:4px solid #d09227; border-radius:.75rem; background:var(--flowhive-soft); }
.flowhive-evidence-list article.ready { border-left-color:#25834a; }
.flowhive-evidence-list article > div:first-child { display:flex; justify-content:space-between; gap:1rem; }
.flowhive-evidence-list article > div:first-child span { color:var(--flowhive-muted); font-size:.78rem; }
.flowhive-evidence-list dl { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:.5rem; margin:0; }
.flowhive-evidence-list dl div { display:grid; gap:.12rem; }
.flowhive-evidence-list dt { color:var(--flowhive-muted); font-size:.7rem; }
.flowhive-evidence-list dd { margin:0; font-weight:700; }
.flowhive-evidence-list ul { margin:0; color:#8a4511; }
.flowhive-ready-message { color:#166534 !important; font-weight:700; }
.flowhive-financial-grid { display:grid; grid-template-columns:repeat(5,minmax(0,1fr)); gap:.65rem; }
.flowhive-financial-grid article { display:grid; gap:.3rem; padding:.9rem; border:1px solid var(--flowhive-line); border-radius:.8rem; background:var(--flowhive-surface); }
.flowhive-financial-grid span { color:var(--flowhive-muted); font-size:.74rem; }
.flowhive-financial-grid strong { color:var(--flowhive-navy); font-size:1.02rem; }
.flowhive-control-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:.75rem; }
.flowhive-control-grid label,.flowhive-raid-create label,.flowhive-health-grid label,.flowhive-status-detail-grid label,.flowhive-share-controls label,.flowhive-full-width { display:grid; gap:.32rem; color:var(--flowhive-muted); font-size:.74rem; font-weight:800; }
.flowhive-control-grid input,.flowhive-control-grid select,.flowhive-raid-create input,.flowhive-raid-create select,.flowhive-raid-create textarea,.flowhive-health-grid select,.flowhive-status-detail-grid textarea,.flowhive-share-controls input,.flowhive-share-controls select,.flowhive-full-width textarea { width:100%; padding:.58rem .65rem; border:1px solid #b9cbd6; border-radius:.5rem; color:var(--flowhive-ink); background:var(--flowhive-surface); font:inherit; }
.flowhive-enterprise-card > footer { display:flex; justify-content:flex-end; gap:.55rem; }
.flowhive-raid-create { display:grid; grid-template-columns:repeat(5,minmax(0,1fr)); gap:.65rem; align-items:end; padding:.8rem; border-radius:.75rem; background:var(--flowhive-soft); }
.flowhive-raid-create .wide { grid-column:span 2; }
.flowhive-raid-table-wrap { overflow:auto; }
.flowhive-raid-table { width:100%; border-collapse:collapse; }
.flowhive-raid-table th { padding:.65rem; text-align:left; color:#fff; background:var(--flowhive-navy); }
.flowhive-raid-table td { padding:.65rem; border-bottom:1px solid var(--flowhive-line); vertical-align:top; }
.flowhive-raid-table td small { display:block; margin-top:.2rem; color:var(--flowhive-muted); }
.flowhive-priority { padding:.25rem .4rem; border-radius:999px; font-size:.72rem; font-weight:800; }
.flowhive-priority.critical,.flowhive-priority.high { color:#991b1b; background:#fee2e2; }
.flowhive-priority.medium { color:#92400e; background:#fef3c7; }
.flowhive-priority.low { color:#166534; background:#dcfce7; }
.flowhive-health-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:.65rem; }
.flowhive-status-detail-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:.75rem; }
.flowhive-sharing-card { min-width:0; }
.flowhive-sharing-card.enabled { border-left:4px solid #25834a; }
.flowhive-sharing-card.locked { border-left:4px solid #d09227; }
.flowhive-share-controls { display:grid; grid-template-columns:1fr .55fr; gap:.65rem; }
.flowhive-share-controls .wide { grid-column:1 / -1; }
.flowhive-share-result { display:grid; grid-template-columns:1fr auto auto auto; gap:.45rem; align-items:center; padding:.7rem; border:1px solid #87c7a0; border-radius:.7rem; background:#effbf3; }
.flowhive-share-result strong { grid-column:1 / -1; }
.flowhive-share-result input { min-width:0; padding:.52rem; border:1px solid #9bcfb0; border-radius:.45rem; }
.flowhive-share-history { display:grid; gap:.4rem; }
.flowhive-share-history > div { display:flex; align-items:center; justify-content:space-between; gap:.7rem; padding:.55rem 0; border-top:1px solid var(--flowhive-line); }
.flowhive-share-history span { display:grid; gap:.12rem; }
.flowhive-share-history small { color:var(--flowhive-muted); }

@media (max-width: 1100px) {
  .flowhive-financial-grid { grid-template-columns:repeat(2,minmax(0,1fr)); }
  .flowhive-control-grid { grid-template-columns:repeat(2,minmax(0,1fr)); }
  .flowhive-raid-create { grid-template-columns:repeat(2,minmax(0,1fr)); }
  .flowhive-evidence-list dl { grid-template-columns:repeat(2,minmax(0,1fr)); }
}
@media (max-width: 720px) {
  .flowhive-save-bar,.flowhive-enterprise-card > header { align-items:stretch; flex-direction:column; }
  .flowhive-financial-grid,.flowhive-control-grid,.flowhive-health-grid,.flowhive-status-detail-grid,.flowhive-share-controls { grid-template-columns:1fr; }
  .flowhive-raid-create { grid-template-columns:1fr; }
  .flowhive-raid-create .wide,.flowhive-share-controls .wide { grid-column:auto; }
  .flowhive-evidence-list dl { grid-template-columns:1fr; }
  .flowhive-share-result { grid-template-columns:1fr; }
}
'''
if "Module 066 enterprise Project Management additions" not in css:
    css += enterprise_css
write(css_path, css)

# ---------------------------------------------------------------------------
# Source validator: preserve existing gates while advancing reviewed sharing.
# ---------------------------------------------------------------------------
validator_path = "src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs"
validator = read(validator_path)
validator = validator.replace("artifacts.includes('INTERNAL DRAFT — NOT A CUSTOMER BASELINE')", "artifacts.includes('PROJECT MANAGEMENT WORKING PLAN — REVIEW REQUIRED')")
validator = validator.replace("backend.includes('customer_export_locked')", "backend.includes('MapProjectFlowHiveEnterpriseEndpoints')")
validator = regex_once(
    validator,
    r"assertInvariant\(\n  'MODULE_066_NO_EXTERNAL_CUSTOMER_LINK',.*?\n\);",
    """assertInvariant(
  'MODULE_066_GOVERNED_CUSTOMER_SHARING',
  backend.includes('customerSharingEnabled = true') &&
    backend.includes('customerSharingRequiresReviewedBaseline = true') &&
    fs.readFileSync(path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'), 'utf8').includes('/api/project-flowhive/share/{token}') &&
    fs.readFileSync(path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'), 'utf8').includes('token_sha256') &&
    fs.readFileSync(path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'), 'utf8').includes('reviewed_baseline_required'),
  'customer links are expiring, revocable, token-hashed, customer-safe, and tied to exact reviewed baselines'
);""",
    "validator customer sharing gate",
)
validator = validator.replace("frontend.includes('<th>WBS</th><th>Task Name</th><th>Start Date</th><th>End Date</th><th>Duration in Days</th><th>Progress</th><th>Predecessor</th><th>Type</th><th>Comments</th><th>Notes</th><th>Assigned Identity</th>')", "frontend.includes('dependencyTypeHelp.FS') && frontend.includes('title=\\\"Work Breakdown Structure number')")
validator = validator.replace("console.log('MODULE_066_CUSTOMER_SHARING=LOCKED');", "console.log('MODULE_066_CUSTOMER_SHARING=REVIEWED_BASELINE_GOVERNED');")
enterprise_assertions = r'''

const enterpriseBackend = readRequired('ENTERPRISE_BACKEND', path.join(backendDirectory, 'ProjectFlowHiveEnterpriseModule.cs'));
const enterpriseHelpers = readRequired('ENTERPRISE_HELPERS', path.join(repositoryRoot, 'src/frontend/project-time-web/src/flowhive-enterprise-helpers.js'));
const enterprisePanels = readRequired('ENTERPRISE_PANELS', path.join(repositoryRoot, 'src/frontend/project-time-web/src/ProjectFlowHiveEnterprisePanels.jsx'));
const enterpriseMigration = readRequired('MIGRATION_086', path.join(repositoryRoot, 'database/migrations/086_module_066_flowhive_enterprise_pm.sql'));
const enterpriseRollback = readRequired('ROLLBACK_086', path.join(repositoryRoot, 'database/rollback/086_module_066_flowhive_enterprise_pm_rollback.sql'));
const enterpriseMigrationTest = readRequired('MIGRATION_086_TEST', path.join(repositoryRoot, 'tests/test-module-066-flowhive-enterprise-pm-migration-086.sh'));

assertInvariant(
  'MODULE_066_ENTERPRISE_PM_PERSISTENCE',
  enterpriseMigration.includes('project_flowhive_working_copies') &&
    enterpriseMigration.includes('project_flowhive_project_controls') &&
    enterpriseMigration.includes('project_flowhive_raid_items') &&
    enterpriseMigration.includes('project_flowhive_status_reports') &&
    enterpriseMigration.includes('project_flowhive_customer_shares') &&
    enterpriseRollback.includes('Rollback refused: Project FlowHive enterprise PM records exist.') &&
    enterpriseMigrationTest.includes('MODULE_066_FLOWHIVE_ENTERPRISE_PM_MIGRATION_086=PASS'),
  'working copies, financial controls, RAID, immutable status reports, customer shares, and guarded rollback'
);

assertInvariant(
  'MODULE_066_PHASE_TASK_CRUD_AND_REORDER',
  frontend.includes('Add {phase.name} task') &&
    frontend.includes('deleteTask(task.wbsNumber)') &&
    frontend.includes('draggable={Boolean(enterprise?.access?.canManage)}') &&
    frontend.includes('dropTask(task.wbsNumber') &&
    enterpriseHelpers.includes('deleteFlowHiveTask') &&
    enterpriseHelpers.includes('moveFlowHiveTask') &&
    enterpriseHelpers.includes('renumberFlowHivePlan'),
  'Plan, Design, Implement, Validate, and Release task add, delete, drag/drop, keyboard movement, and WBS renumbering'
);

assertInvariant(
  'MODULE_066_ENTERPRISE_PM_SCOPE',
  enterpriseBackend.includes('Only the assigned Project Manager can manage') &&
    enterpriseBackend.includes('IsProjectManagerOwner') &&
    enterpriseBackend.includes('ProjectPulseActualSessionAuthority.IsViewAs') &&
    enterpriseBackend.includes('working_copy_version_conflict'),
  'PM ownership, non-transferable administrator support, View-As write blocking, and optimistic concurrency'
);

assertInvariant(
  'MODULE_066_FINANCIAL_STATUS_AND_AI_EVIDENCE',
  frontend.includes("id: 'financials'") &&
    frontend.includes("id: 'status'") &&
    frontend.includes('/api/project-financials/projects/') &&
    enterprisePanels.includes('Fixed Price') &&
    enterprisePanels.includes('Time and Materials') &&
    enterprisePanels.includes('RAID register') &&
    enterprisePanels.includes('Executive summary') &&
    enterpriseBackend.includes('sowEvidenceSummary') &&
    enterpriseBackend.includes('flowhive_sow_processing_queued'),
  'authoritative financials, contract type, RAID, executive status reporting, and actionable SOW evidence readiness'
);

assertInvariant(
  'MODULE_066_PROFESSIONAL_ARTIFACT_AND_HEADER_HELP',
  artifacts.includes('PROJECT MANAGEMENT WORKING PLAN — REVIEW REQUIRED') &&
    artifacts.includes('Executive summary') &&
    frontend.includes('Project Management working plan') &&
    frontend.includes('dependencyTypeHelp.FS') &&
    frontend.includes('Start No Earlier Than constraint') &&
    stylesheet.includes('.flowhive-save-bar') &&
    stylesheet.includes('.flowhive-financial-grid'),
  'professional PM export, executive summary, dependency explanations, editable schedule constraints, and responsive enterprise styling'
);
'''
validator = replace_once(validator, "\nconst failed = assertions.filter((assertion) => !assertion.condition);", enterprise_assertions + "\nconst failed = assertions.filter((assertion) => !assertion.condition);", "enterprise validator assertions")
write(validator_path, validator)

# Keep source documentation current without claiming Test deployment.
readme_path = "docs/modules/module-066-project-flowhive/README.md"
readme = read(readme_path)
marker = "## Enterprise Project Management extension (Migration 086)"
if marker not in readme:
    readme += f'''\n\n{marker}\n\nThe source package adds PM-owned working copies, phase-aware task add/delete and drag/drop, editable schedule constraints, authoritative financial visibility, Fixed Price/T&M controls, RAID, immutable status reports, actionable SOW evidence readiness, professional PM exports, and expiring customer-safe links tied to exact reviewed baselines. Project Managers can mutate only projects assigned to them; View-As remains read-only. Source completion does not by itself claim protected-Test deployment.\n'''
write(readme_path, readme)

print("FLOWHIVE_ENTERPRISE_SOURCE_PATCH=PASS")

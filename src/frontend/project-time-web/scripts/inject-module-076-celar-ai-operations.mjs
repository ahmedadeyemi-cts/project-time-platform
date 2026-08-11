import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('../', import.meta.url));
const target = path.join(webRoot, 'src', 'DefectTrackerCenter.jsx');
let source = fs.readFileSync(target, 'utf8');

function replaceOnce(anchor, replacement, label) {
  const occurrences = source.split(anchor).length - 1;
  if (occurrences !== 1) {
    throw new Error(`MODULE_076_CELAR_AI_OPERATIONS_${label}=FAILED expected=1 actual=${occurrences}`);
  }
  source = source.replace(anchor, replacement);
}

if (!source.includes("'/api/celar-ai/v1/operations/defects?limit=200'")) {
  replaceOnce(
    "  '/api/defect-tracker/defects',",
    "  '/api/defect-tracker/defects',\n  '/api/celar-ai/v1/operations/defects?limit=200',",
    'DURABLE_INVENTORY_ENDPOINT'
  );
}

const inventoryAnchor = `  const inventory = payloads['/api/defect-tracker/defects'];`;
const inventoryReplacement = `  const legacyInventory = payloads['/api/defect-tracker/defects'];
  const durableInventory = payloads['/api/celar-ai/v1/operations/defects?limit=200'];
  const inventory = durableInventory || legacyInventory;`;
if (!source.includes('const durableInventory =')) {
  replaceOnce(inventoryAnchor, inventoryReplacement, 'DURABLE_INVENTORY_SELECTION');
}

const stateAnchor = `  const defects = inventory?.defects || [];
  const categories = overview?.categories || ['Bug', 'Regression', 'Other'];
  const priorities = overview?.priorities || ['Critical', 'High', 'Medium', 'Low'];
  const defaultAssignee = overview?.defaultAssignee;
  const writesEnabled = Boolean(overview?.persistence?.writesEnabled);`;
const stateReplacement = `  const defects = inventory?.defects || [];
  const defectSummary = {
    total: defects.length,
    open: defects.filter((defect) => defect.status === 'Open' || defect.status === 'Reopened').length,
    inProgress: defects.filter((defect) => defect.status === 'In Progress').length,
    blocked: defects.filter((defect) => defect.status === 'Blocked').length,
    resolved: defects.filter((defect) => defect.status === 'Resolved' || defect.status === 'Closed').length,
    critical: defects.filter((defect) => defect.priority === 'Critical').length
  };
  const categories = overview?.categories || ['Bug', 'Regression', 'User Interface', 'API', 'Authentication', 'Authorization', 'Data', 'Integration', 'Performance', 'Documentation', 'Feature Gap', 'Availability', 'Security', 'Other'];
  const priorities = overview?.priorities || ['Critical', 'High', 'Medium', 'Low'];
  const defaultAssignee = overview?.defaultAssignee || {
    displayName: 'Ahmed Adeyemi',
    email: 'ahmed.adeyemi@ussignal.com',
    state: 'resolved_by_ask_celar_ai_on_create'
  };
  const writesEnabled = Boolean(durableInventory && Array.isArray(durableInventory.defects));`;
if (!source.includes('const defectSummary =')) {
  replaceOnce(stateAnchor, stateReplacement, 'DURABLE_STATE');
}

if (!source.includes('function openAskCelarAiDefect()')) {
  const previewAnchor = `  function previewDraft(event) {
    event.preventDefault();
    const assignee = assigneeOptions.find((identity) => identity.userId === draft.assigneeUserId)
      || defaultAssignee;
    setDraftPreview({
      ...draft,
      sourceChannel,
      sourceLabel: sourceLabel(sourceChannel),
      assignee,
      defectId: 'Assigned after durable save',
      dateAdded: 'Assigned by server after durable save',
      dateResolved: null,
      status: 'Open'
    });
  }`;
  const previewReplacement = `${previewAnchor}

  function openAskCelarAiDefect() {
    window.dispatchEvent(new CustomEvent('projectpulse:celar-ai-open-defect-intake', {
      detail: {
        triggerQuestion: draft.title || 'Create a Module 076 defect',
        suggestedTitle: draft.title,
        suggestedDescription: draft.description,
        suggestedCategory: draft.category,
        suggestedPriority: draft.priority,
        affectedModule: draft.affectedModule,
        affectedRoute: draft.affectedRoute,
        environment: draft.environment || 'test',
        affectedSystem: 'Pulse',
        sourceChannel
      }
    }));
  }`;
  replaceOnce(previewAnchor, previewReplacement, 'ASK_CELAR_AI_ACTION');
}

for (const [before, after] of [
  ["['Total', overview?.summary?.total ?? 0]", "['Total', defectSummary.total]"],
  ["['Open', overview?.summary?.open ?? 0]", "['Open', defectSummary.open]"],
  ["['In progress', overview?.summary?.inProgress ?? 0]", "['In progress', defectSummary.inProgress]"],
  ["['Blocked', overview?.summary?.blocked ?? 0]", "['Blocked', defectSummary.blocked]"],
  ["['Resolved', overview?.summary?.resolved ?? 0]", "['Resolved', defectSummary.resolved]"],
  ["['Critical', overview?.summary?.critical ?? 0]", "['Critical', defectSummary.critical]"]
]) {
  if (source.includes(before)) source = source.replace(before, after);
  else if (!source.includes(after)) throw new Error(`MODULE_076_CELAR_AI_OPERATIONS_SUMMARY=FAILED marker=${before}`);
}

const oldActions = `          <div className="defect-form-actions">
            <button type="submit" className="defect-secondary-action">Review local draft</button>
            <button type="button" className="defect-primary-action" disabled={!writesEnabled}>
              Create defect
            </button>
          </div>`;
const newActions = `          <div className="defect-form-actions">
            <button type="submit" className="defect-secondary-action">Review local draft</button>
            <button type="button" className="defect-primary-action" onClick={openAskCelarAiDefect}>
              Continue in Ask Celar AI
            </button>
          </div>`;
if (!source.includes('Continue in Ask Celar AI')) {
  replaceOnce(oldActions, newActions, 'PRIMARY_INTAKE_ACTION');
}

const oldWarning = `      {!loading && !error && !writesEnabled ? (
        <p className="defect-banner warning">
          The complete tracking and integration contract is loaded. Durable defect IDs,
          database writes, manager email, reporter email, and GitHub webhook processing
          remain locked pending their separate activation approvals.
        </p>
      ) : null}`;
const newWarning = `      {!loading && !error ? (
        <p className={\`defect-banner \${writesEnabled ? 'neutral' : 'warning'}\`}>
          {writesEnabled
            ? 'Module 076 is the durable defect system of record. Start or continue defect intake through Ask Celar AI so troubleshooting evidence, user confirmation, default assignment, and privacy controls remain in one governed experience.'
            : 'Migration 084 or the Ask Celar AI operations service is not ready. No durable defect can be created until the protected activation gates pass.'}
        </p>
      ) : null}`;
if (!source.includes('Module 076 is the durable defect system of record.')) {
  replaceOnce(oldWarning, newWarning, 'ACTIVATION_BANNER');
}

source = source
  .replaceAll('<span>ProjectPulse quality operations</span>', '<span>Pulse quality operations</span>')
  .replaceAll('One governed queue for defects raised from ProjectPulse Help, GitHub,', 'One governed queue for defects raised from Ask Celar AI, Pulse, GitHub,')
  .replaceAll("help: 'ProjectPulse Help'", "help: 'Ask Celar AI'")
  .replaceAll("{ channel: 'help', state: 'source_connected', mechanism: 'ProjectPulse Help opens this intake route.' }", "{ channel: 'help', state: 'source_connected', mechanism: 'Ask Celar AI opens the guided intake questionnaire.' }")
  .replaceAll('<strong>No durable inventory is connected.</strong>', '<strong>No defects are visible in your authorized scope.</strong>')
  .replaceAll("{inventory?.statement || 'Persistence remains locked pending authorization.'}", "{inventory?.statement || 'Use Ask Celar AI to troubleshoot or create a governed Module 076 defect.'}")
  .replaceAll('<td><strong>{defect.defectId}</strong></td>', '<td><strong>{defect.defectNumber || defect.defectId}</strong></td>')
  .replaceAll("<td>{defect.raisedBy?.displayName || '—'}</td>", "<td>{defect.reporter?.displayName || defect.raisedBy?.displayName || '—'}</td>");

if (!source.includes('defect.resolutionSeconds')) {
  const resolutionAnchor = `function resolutionLabel(defect) {
  if (defect?.resolutionTime) return defect.resolutionTime;`;
  const resolutionReplacement = `function resolutionLabel(defect) {
  if (Number.isFinite(Number(defect?.resolutionSeconds))) {
    const totalMinutes = Math.round(Number(defect.resolutionSeconds) / 60);
    const days = Math.floor(totalMinutes / 1440);
    const hours = Math.floor((totalMinutes % 1440) / 60);
    const minutes = totalMinutes % 60;
    return \`${'${days ? `${days}d ` : \'\'}${hours ? `${hours}h ` : \'\'}${minutes}m'}\`;
  }
  if (defect?.resolutionTime) return defect.resolutionTime;`;
  replaceOnce(resolutionAnchor, resolutionReplacement, 'RESOLUTION_SECONDS');
}

for (const marker of [
  "'/api/celar-ai/v1/operations/defects?limit=200'",
  'const durableInventory =',
  'const defectSummary =',
  'function openAskCelarAiDefect()',
  'Continue in Ask Celar AI',
  'Module 076 is the durable defect system of record.',
  'defect.defectNumber || defect.defectId'
]) {
  if (!source.includes(marker)) {
    throw new Error(`MODULE_076_CELAR_AI_OPERATIONS_MARKER=FAILED marker=${marker}`);
  }
}

fs.writeFileSync(target, source, 'utf8');
console.log('MODULE_076_CELAR_AI_OPERATIONS_DURABLE_INVENTORY=INJECTED');
console.log('MODULE_076_CELAR_AI_OPERATIONS_PRIMARY_INTAKE=ASK_CELAR_AI');
console.log('MODULE_076_CELAR_AI_OPERATIONS_SYSTEM_OF_RECORD=MODULE_076');

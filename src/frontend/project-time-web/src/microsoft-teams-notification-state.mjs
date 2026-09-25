// Presentation only. The owning endpoints remain the authorization and delivery authority.
export function validTeamsAppId(value) {
  return typeof value === 'string' && /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(value.trim())
    && !/^0{8}-(?:0{4}-){3}0{12}$/.test(value.trim());
}

export function validWorkflowUrl(value) {
  if (typeof value !== 'string') return false;
  try {
    const url = new URL(value.trim());
    return url.protocol === 'https:' && !url.username && !url.password
      && (url.hostname.endsWith('.api.powerplatform.com') || url.hostname.endsWith('.logic.azure.com'));
  } catch { return false; }
}

export function validRecipient(value) {
  // This is only an input check, not Entra identity or installation verification.
  return typeof value === 'string' && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

export function configurationChanged(saved, draft) {
  if (!saved || !draft) return false;
  const savedMode = String(saved.deliveryMode || 'power_automate');
  const draftMode = String(draft.deliveryMode || 'power_automate');
  if (saved.enabled !== draft.enabled || savedMode !== draftMode) return true;
  if (draftMode === 'power_automate') {
    return String(saved.workflowTriggerUrl || '') !== String(draft.workflowTriggerUrl || '').trim()
      || String(saved.workflowAudience || '') !== String(draft.workflowAudience || '').trim();
  }
  return String(saved.teamsAppId || '').toLowerCase() !== String(draft.teamsAppId || '').trim().toLowerCase();
}

export function workspaceAccess(state, draft, environment, busy, fresh = true) {
  const saved = state?.configuration;
  const validEnvironment = environment === 'test' || environment === 'production';
  const validConfiguration = saved && typeof saved.enabled === 'boolean'
    && Number.isInteger(saved.revision) && saved.revision >= 0;
  const wrongEnvironment = Boolean(saved && saved.environment !== environment);
  const revisionConflict = Boolean(saved && draft && saved.revision !== draft.revision);
  const dirty = configurationChanged(saved, draft);
  const readOnly = state?.readOnly !== false;
  const blocked = Boolean(busy || !fresh || !validEnvironment || !validConfiguration || !draft
    || wrongEnvironment || readOnly);
  const mode = draft?.deliveryMode || 'power_automate';
  const validMode = mode === 'power_automate' || mode === 'graph_app';
  const validDestination = mode === 'power_automate' ? validWorkflowUrl(draft?.workflowTriggerUrl) : validTeamsAppId(draft?.teamsAppId);
  const savedMode = saved?.deliveryMode || 'power_automate';
  const savedDestination = savedMode === 'power_automate' ? validWorkflowUrl(saved?.workflowTriggerUrl) : validTeamsAppId(saved?.teamsAppId);
  return {
    blocked, readOnly, dirty, wrongEnvironment, revisionConflict,
    canSave: !blocked && !revisionConflict && dirty && validMode && (!draft.enabled || validDestination),
    canTest: !blocked && !revisionConflict && !dirty && environment === 'test'
      && saved?.enabled === true && savedDestination,
  };
}

export function deliveryPresentation(row) {
  const code = String(row?.diagnosticCode || '');
  if (row?.status === 'sent') return {
    tone: 'success', label: 'Accepted by Microsoft',
    detail: 'Microsoft accepted the request. Receipt and visibility must still be checked in Teams.',
  };
  if (row?.status === 'sending' || row?.status === 'outcome_unknown' || row?.status === 'already_claimed') return {
    tone: 'warning', label: row.status === 'sending' ? 'In progress / unconfirmed' : 'Outcome unconfirmed',
    detail: 'Do not resend automatically. Review the delivery history and the recipient’s Teams activity first.',
  };
  if (row?.status === 'failed') {
    let detail = 'The request failed. Review the diagnostic and the configured Microsoft services connection.';
    if (code === 'teams_workflow_accepted') detail = 'Power Automate accepted the request. Confirm the flow run and resulting Teams message.';
    else if (code === 'teams_workflow_not_authorized') detail = 'Power Automate rejected the centralized Microsoft services identity. Verify the HTTP trigger allows this service principal.';
    else if (code === 'teams_workflow_rate_limited') detail = 'Power Automate rate-limited this request. Preserve it for a deliberate later retry.';
    else if (code.startsWith('teams_workflow_http_')) detail = 'Power Automate did not confirm the workflow request. Review its run history and request identifier.';
    else if (code === 'teams_graph_http_400') detail = 'Microsoft rejected the request. The HTTP code alone does not identify the cause; review the sender request and app setup.';
    else if (code === 'teams_graph_http_403') detail = 'Microsoft refused this request. Verify the sending app’s permission, recipient access, and app installation.';
    else if (code === 'teams_graph_http_404') detail = 'Microsoft could not resolve the requested resource. Verify the recipient identity, tenant, and app installation.';
    else if (code === 'teams_graph_http_429') detail = 'Microsoft rate-limited this request. Review provider guidance before a deliberate retry.';
    else if (code.startsWith('teams_token_http_')) detail = 'The Microsoft services application could not obtain a token. Check the environment’s connection with an authorized administrator.';
    else if (code === 'teams_configuration_incomplete') detail = 'Required sender configuration is incomplete. Review the saved environment and services connection.';
    return { tone: 'danger', label: 'Needs attention', detail };
  }
  return { tone: 'neutral', label: 'Not verified', detail: 'No verified delivery result is available for this record.' };
}

export function recentDeliveries(rows, filter = 'all') {
  if (!Array.isArray(rows)) return [];
  return rows.filter(row => row && typeof row === 'object')
    .filter(row => filter === 'all' || (filter === 'accepted' ? row.status === 'sent' : row.status !== 'sent'));
}

export function localTimestamp(value) {
  if (value == null || value === '') return 'Time unavailable';
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return 'Time unavailable';
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

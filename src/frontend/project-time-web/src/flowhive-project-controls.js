// Explicit write DTO: server readback metadata and DOM events are not API requests.
const moneyFields = ['approvedBudget', 'expenseBudget', 'contingencyBudget', 'forecastAtCompletion'];
const moneyLabels = ['Approved budget', 'Expense budget', 'Contingency budget', 'Forecast at completion'];
export function projectControlsRequest(controls) {
  if (!controls || typeof controls !== 'object' || Array.isArray(controls) || 'nativeEvent' in controls)
    throw new Error('Project controls are not ready. Refresh the project before saving.');
  const result = {};
  moneyFields.forEach((field, index) => {
    const raw = controls[field];
    if (raw == null || (typeof raw === 'string' && raw.trim() === '')) { result[field] = null; return; }
    const value = typeof raw === 'string' ? Number(raw.trim()) : raw;
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > 9999999999999999)
      throw new Error(`${moneyLabels[index]} must be a valid, non-negative number.`);
    result[field] = value;
  });
  return {
    contractType: controls.contractType || 'unknown', currencyCode: controls.currencyCode || 'USD', ...result,
    percentCompleteMethod: controls.percentCompleteMethod || 'task_weighted',
    statusReportCadence: controls.statusReportCadence || 'weekly',
    customerSharingEnabled: controls.customerSharingEnabled === true,
    financialNotes: controls.financialNotes == null ? '' : String(controls.financialNotes)
  };
}

export async function enableProjectCustomerSharing({ projectId, enterprise, busy, post, isCurrent, onSaved, onError, onSettled }) {
  if (!projectId || busy || enterprise?.project?.projectId !== projectId || !enterprise?.access?.canShare || enterprise.access.isViewAs)
    return false;
  try {
    const result = await post(`/api/project-flowhive/projects/${projectId}/customer-sharing/enable`, {});
    if (!isCurrent()) return false;
    if (result?.projectId !== projectId || result.customerSharingEnabled !== true || result.customerLinkCreated !== false)
      throw new Error('The sharing result could not be verified. Refresh the project before trying again.');
    onSaved(result);
    return true;
  } catch (error) {
    if (isCurrent()) onError(error);
    return false;
  } finally {
    if (isCurrent()) onSettled();
  }
}

export function customerSharingError(error) {
  const body = error?.responseBody;
  const correlation = body?.correlationId || '';
  const detail = error?.status === 403 ? 'Your current role cannot manage customer sharing for this project. Exit View-As or contact the assigned Project Manager.'
    : error?.status === 401 ? 'Your session expired. Sign in again before enabling sharing.'
    : error?.name === 'AbortError' || error?.name === 'TimeoutError' ? 'The request timed out. Refresh sharing status before retrying; the save may have completed.'
    : body?.message || error?.message || 'Customer sharing could not be enabled. Refresh the project and try again.';
  return { message: detail, correlationId: correlation };
}

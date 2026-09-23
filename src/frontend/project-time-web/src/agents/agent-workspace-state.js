// A future workspace host supplies a SERVER-authorized, session/resource-bound
// projection. Browser audience labels and role selectors never grant access.
export function visibleAgentSnapshot(contextKey, snapshot) {
  if (typeof contextKey !== 'string' || !contextKey || !snapshot || snapshot.contextKey !== contextKey
      || snapshot.authorized !== true || snapshot.enabled !== true || snapshot.viewAs === true) return null;
  return snapshot;
}

export function availableAgentCapabilities(contextKey, snapshot) {
  const current = visibleAgentSnapshot(contextKey, snapshot);
  return (Array.isArray(current?.capabilities) ? current.capabilities : []).filter(item =>
    item?.authorized === true && item?.executionReady === true
    && typeof item.code === 'string' && /^[a-z][a-z0-9_]{0,79}$/.test(item.code)
    && typeof item.name === 'string' && item.name.length <= 160);
}

export function agentStatusLabel(status) {
  return ({ Ready: 'Ready for next step', Running: 'Working', AwaitingInput: 'Needs your clarification',
    AwaitingReview: 'Proposal needs review — not sent', ProposalComplete: 'Proposal complete — no business changes applied',
    Blocked: 'Blocked', Cancelled: 'Cancelled' })[status] || 'Status not verified';
}

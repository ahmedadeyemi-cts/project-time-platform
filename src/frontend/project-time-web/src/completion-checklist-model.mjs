// Presentation only. Capabilities and completion results always come from the server.
export const completionActions = Object.freeze([
  { id: 'delivery', title: 'Delivery complete', owner: 'Assigned PM / PTC', button: 'Mark delivery complete', confirm: 'I confirm the agreed delivery is complete and want to start closeout.' },
  { id: 'acceptance', title: 'Customer acceptance', owner: 'Assigned PM / PTC', button: 'Record customer decision', confirm: 'I have recorded the customer’s actual decision and supporting evidence.' },
  { id: 'sent', title: 'Sent to Certinia', owner: 'PTC', button: 'Record manual handoff', confirm: 'I confirm this package was already sent to Certinia outside Pulse. This action must not send it again.' },
  { id: 'billed', title: 'Fully billed', owner: 'PTC / Billing', button: 'Confirm fully billed', confirm: 'Billing has processed the final charges, including prior partial billing. No remaining authorized charges are awaiting billing for this project.' }
]);
export function actionStatus(id, data) {
  if (!data) return 'Not verified';
  if (id === 'delivery') return data.deliveryComplete ? 'Recorded' : 'Not recorded';
  if (id === 'acceptance') return data.customerAcceptanceComplete ? 'Accepted' : data.state?.acceptance?.scope === 'conditional' ? 'Conditions outstanding' : data.state?.acceptance?.scope === 'rejected' ? 'Corrections requested' : 'Awaiting decision';
  if (id === 'sent') return data.state?.sent ? `${data.state.sent.scope === 'final' ? 'Final' : 'Partial'} manual handoff recorded` : data.automatedFinalDelivered ? 'Connected final delivery verified' : 'Not recorded';
  if (id === 'billed') return data.fullyBilled ? 'Confirmed' : data.billingEvidenceStale ? 'Reconciliation required' : 'Not confirmed';
  return 'Not recorded';
}
export function mayRecord(id, data) {
  return Boolean(data && data.closed === false && data.capabilities?.[id] === true);
}
export function confirmationRequest(form, data, operationId) {
  return {
    expectedRevision: data.state.revision, expectedBasisFingerprint: data.basisFingerprint,
    operationId, confirmed: form.confirmed === true, occurredOn: form.occurredOn,
    reference: form.reference.trim(), evidence: form.evidence.trim(), party: form.party.trim(),
    notes: form.notes.trim(), scope: form.scope, reason: form.reason.trim(),
    coveredInvoiceIds: form.coveredInvoiceIds || []
  };
}

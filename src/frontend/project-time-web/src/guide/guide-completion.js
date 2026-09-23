// Additive instructions for the shared checklist; never grants action authority.
const sources = [
  'src/frontend/project-time-web/src/ProjectCompletionChecklist.jsx',
  'src/backend/ProjectTime.Api/Modules/WorkLifecycleCompletionWorkflow.cs',
  'src/backend/ProjectTime.Api/Modules/CompletionWorkflowPolicy.cs',
  'src/backend/ProjectTime.Api/Modules/CertiniaCompletionBoundary.cs'
];
const delivery = {
  title: 'Mark delivery complete and record the customer decision',
  steps: [
    'Select your assigned project. In the PM workspace open Delivery & closeout; the same checklist is in Project Closeout and Invoice & Billing.',
    'Choose Mark delivery complete. Record the actual date, deliverable/version reference, supporting evidence and audit reason, then select the confirmation and save.',
    'Verify the saved receipt. This starts closeout immediately but does not close or archive the project and does not require final billing first.',
    'Choose Record customer decision. Enter the customer representative, actual decision date and supporting document, email or ticket reference.',
    'Select Accepted, Accepted with outstanding conditions, or Corrections requested. Record any conditions or corrections. Only acceptance of the current delivery satisfies closeout.',
    'Refresh to verify the saved author, date and reference. A shared-plan view, customer link click or meeting attendance is not acceptance.'
  ],
  outcome: 'Delivery and the customer decision have separate auditable records; unfinished billing remains visible.',
  handoff: 'Assigned PM → PTC and Billing for financial reconciliation.'
};
const manual = {
  title: 'Record a manual Certinia handoff without sending it twice',
  steps: [
    'PTC selects the correct project and opens Sent to Certinia in the completion checklist. Use this only after the package was actually handed over outside Pulse.',
    'Record the actual sent date, package/reference, supporting evidence and audit reason. Choose Partial billing for a month-end package, or Final handoff only after reconciling prior partial invoices.',
    'For a partial handoff, identify the existing Pulse invoices it covered. A final handoff covers the current project reconciliation. Do not invent a local invoice to represent an external package.',
    'Select the confirmation that the package was already sent, then save. This action records evidence only; it performs no Certinia transmission and requires no SELL rate setup.',
    'Verify the recorded partial/final status. Sent is not fully billed. Resolve queued, processing or retryable deliveries before a manual attestation to avoid duplicate billing.',
    'After a timeout, refresh to verify the outcome or Retry the same confirmation. Never send the financial package again because an evidence save timed out.'
  ],
  outcome: 'An auditable manual handoff exists without an external send or a fabricated invoice.',
  handoff: 'PTC → Billing to process the package in Certinia.'
};
const final = {
  title: 'Confirm fully billed and finish governed closeout',
  steps: [
    'PTC or Billing verifies that final charges were processed, including reconciliation of prior partial billing, and no authorized charges remain outstanding.',
    'Choose Confirm fully billed. Record the actual final invoice or Billing completion reference, date, supporting evidence and audit reason, then confirm and save.',
    'A partial package cannot satisfy final billing. A connected successful send also requires the separate Fully billed confirmation; it is not proof of customer payment.',
    'When charges or approvals change, refresh and reconcile the current evidence. A previous Fully billed confirmation becomes stale rather than silently covering the new charges.',
    'In Project Closeout finish the time/expense review, resolve open tasks and check the applicable billing disposition. A PM may save incomplete closeout progress with a reason.',
    'The existing authorized PTC or administrator completes final closeout only after server verification. Fully billed alone does not automatically close or archive the project.',
    'Use the governed reopen/correction actions when necessary. They retain prior receipts; reopening evidence does not cancel a real invoice or erase duplicate-send protection.'
  ],
  outcome: 'Final billing and project closure are distinct, traceable decisions based on current evidence.',
  handoff: 'Billing/PTC → authorized final-closeout owner; HR payment decisions remain outside this workflow.'
};
export function withCompletionGuide(guide) {
  if (!['project-workload','project-closeout','invoice-billing-center'].includes(guide.route)) return guide;
  const additions = guide.route === 'project-workload' ? [delivery] : guide.route === 'project-closeout' ? [delivery,manual,final] : [manual,final];
  return {
    ...guide,
    responsibility: guide.route === 'invoice-billing-center'
      ? 'PTC authorizes Certinia billing handoffs; Billing processes them. The shared checklist distinguishes manual transmission evidence, fully billed confirmation and final project closeout.'
      : guide.responsibility,
    procedures: guide.route === 'project-closeout' ? additions : [...guide.procedures,...additions],
    limitations: [...guide.limitations,
      'Manual evidence recording is independent of SELL connectivity; invoice creation inside Pulse retains its existing commercial, approval and PO checks.',
      'A final external reference is an audited human attestation, not independent verification of an external invoice. Payment collection, HR commissions and revised fixed-bid pricing are not implemented by this checklist.'],
    sources: [...new Set([...guide.sources,...sources])]
  };
}

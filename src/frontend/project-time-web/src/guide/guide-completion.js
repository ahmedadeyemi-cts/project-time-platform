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
const amountInvoice = {
  title: 'Create a manual partial or full invoice without connectors',
  steps: [
    'Select the customer project in Invoice & Billing Center and open Manual partial / full invoice.',
    'For fixed-price projects, choose the billing basis and enter progress evidence. Missing time submissions or full billing before delivery/time review require an explicit Billing, Finance, Accounting or administrator exception with the signed-in approver recorded. T&M retains approved-time billing.',
    'Verify commercial information from SELL when available. Record the approved document/version and, during an outage, the saved or manual information used. Reconcile when SELL returns.',
    'Enter the authorized project total. Choose Partial and enter the cumulative amount to bill so far, or Full / final to bill the remaining project balance.',
    'Review prior Pulse invoices. Enter documented amounts already billed outside Pulse, excluding the invoices already in Pulse. Prior amounts are deducted from the new charge.',
    'Record the billing period, customer-facing description, approved SOW/PO or billing authorization, external invoice references, and internal audit reason.',
    'Verify the new invoice amount, confirm the reconciliation, and create the invoice. Select the saved invoice in history to download PDF or Excel for manual delivery.',
    'Use Billing reconciliation and offline tracking on the saved invoice to hold automatic retries, record an actual manual handoff, match an existing Certinia invoice, or record verification against restored SELL information. Inspect delivery history before handling an invoice outside Pulse.',
    'Continue subsequent billing through this amount-based path. It does not alter time entries, send an invoice, confirm payment, or close the project. A fully billed balance produces no duplicate invoice.'
  ],
  outcome: 'A saved invoice charges only the newly authorized amount after prior billing is deducted.',
  handoff: 'Authorized invoice creator → PTC / Billing for delivery and processing.'
};
const manual = {
  title: 'Record a manual billing handoff without sending it twice',
  steps: [
    'PTC or Billing selects the correct project and opens Billing handoff in the completion checklist. Use this only after the package was actually handed over outside Pulse.',
    'Record the actual sent date, package/reference, supporting evidence and audit reason. Choose Partial billing for a month-end package, or Final handoff only after reconciling prior partial invoices.',
    'For a partial handoff, identify the existing Pulse invoices it covered. A final handoff covers the current project reconciliation. Do not invent a local invoice to represent an external package.',
    'Select the confirmation that the package was already sent, then save. This action records evidence only; it performs no Certinia transmission and requires no SELL rate setup.',
    'Verify the recorded partial/final status. Sent is not fully billed. Resolve queued, processing or retryable deliveries before a manual attestation to avoid duplicate billing.',
    'After a timeout, refresh to verify the outcome or Retry the same confirmation. Never send the financial package again because an evidence save timed out.'
  ],
  outcome: 'An auditable manual handoff exists without an external send or a fabricated invoice.',
  handoff: 'PTC → Billing to process the package through the approved billing process.'
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
  const additions = guide.route === 'project-workload' ? [delivery] : guide.route === 'project-closeout' ? [delivery,manual,final] : [amountInvoice,manual,final];
  return {
    ...guide,
    responsibility: guide.route === 'invoice-billing-center'
      ? 'PTC and Billing record manual handoffs and completed billing. The shared checklist distinguishes manual transmission evidence, fully billed confirmation and final project closeout.'
      : guide.responsibility,
    procedures: guide.route === 'project-closeout' ? additions : [...guide.procedures,...additions],
    limitations: [...guide.limitations,
      'Manual evidence recording is independent of SELL connectivity; invoice creation inside Pulse retains its existing commercial, approval and PO checks.',
      'A final external reference is an audited human attestation, not independent verification of an external invoice. Payment collection, HR commissions and revised fixed-bid pricing are not implemented by this checklist.'],
    sources: [...new Set([...guide.sources,...sources])]
  };
}

import './finance-billing-workflow.css';

const systems = Object.freeze([
  {
    key: 'sell',
    name: 'ConnectWise SELL',
    match: ['sell', 'connectwise'],
    manual: 'Verify quote, SOW, rates, milestones, and commercial terms from the approved source before invoicing.'
  },
  {
    key: 'salesforce',
    name: 'Salesforce',
    match: ['salesforce'],
    manual: 'Verify customer, opportunity, and CRM references manually when they are needed for the billing package.'
  },
  {
    key: 'certinia',
    name: 'Certinia',
    match: ['certinia'],
    manual: 'Download the immutable Pulse invoice, complete the approved external handoff, then record the reference in Pulse.'
  }
]);

const steps = Object.freeze([
  { key: 'readiness', label: 'Confirm readiness', detail: 'Review approved labor, expenses, PO/commercial evidence, and blockers.', href: '#billing-readiness-workflow' },
  { key: 'invoice', label: 'Create invoice', detail: 'Create a partial/final invoice from approved sources or an authorized fixed-price amount.', href: '#finance-line-invoice' },
  { key: 'delivery', label: 'Deliver / hand off', detail: 'Use a connector when enabled, or download the immutable artifact and complete the approved manual handoff.', href: '#finance-manual-operations' },
  { key: 'reconcile', label: 'Reconcile', detail: 'Record manual handoff, Certinia match, SELL verification, holds, or other verified billing evidence.', href: '#finance-manual-operations' },
  { key: 'billed', label: 'Confirm fully billed', detail: 'Confirm that final authorized charges were processed. This is separate from payment.', href: '#finance-manual-operations' },
  { key: 'closeout', label: 'Project closeout', detail: 'Close the project only after billing and the remaining delivery/acceptance requirements are complete.', href: '#project-closeout' }
]);

function normalized(value) {
  return String(value ?? '').trim().toLowerCase();
}

function connectorFor(statuses, definition) {
  return (statuses || []).find((connector) => {
    const haystack = `${normalized(connector?.systemCode)} ${normalized(connector?.displayName)}`;
    return definition.match.some((token) => haystack.includes(token));
  }) || null;
}

function connectorState(connector) {
  if (!connector) return { tone: 'manual', label: 'Manual process', detail: 'No active connector registration is available to this workflow.' };
  const connected = normalized(connector.connectionStatus) === 'connected';
  const outbound = connector.outboundEnabled === true;
  if (connected && outbound) return { tone: 'connected', label: 'Connected', detail: 'Outbound connector activity is enabled by server policy.' };
  if (connected) return { tone: 'registered', label: 'Connected · outbound off', detail: 'Read/registration exists, but automated outbound activity is not enabled.' };
  return { tone: 'manual', label: 'Manual process', detail: connector.connectionStatus ? `Connector status: ${connector.connectionStatus}.` : 'Connector is not connected.' };
}

export default function FinanceBillingWorkflow({
  stage = 'readiness',
  connectorStatuses = [],
  hasInvoice = false,
  projectLabel = ''
}) {
  const effectiveStage = stage === 'invoice' && hasInvoice ? 'delivery' : stage;
  const currentIndex = Math.max(0, steps.findIndex((step) => step.key === effectiveStage));
  const connectorRows = systems.map((definition) => ({
    ...definition,
    connector: connectorFor(connectorStatuses, definition)
  })).map((row) => ({ ...row, state: connectorState(row.connector) }));
  const connectorStatusKnown = connectorStatuses.length > 0;
  const connectedOutbound = connectorRows.filter((row) => row.state.tone === 'connected').length;
  const manualMode = connectorStatusKnown ? connectedOutbound < connectorRows.length : true;

  return (
    <section className="finance-billing-workflow" aria-label="Finance billing operating workflow">
      <header>
        <div>
          <p className="finance-billing-eyebrow">Finance operating workflow</p>
          <h2>Billing from readiness through closeout</h2>
          <p>
            Pulse remains the controlled billing system of record. {projectLabel ? `Selected project: ${projectLabel}. ` : ''}
            External systems may be used manually until their connectors are enabled.
          </p>
        </div>
        <span className={`finance-billing-mode ${manualMode ? 'manual' : 'connected'}`}>
          {!connectorStatusKnown ? 'Manual-capable workflow' : manualMode ? 'Controlled manual mode' : 'Connector-assisted mode'}
        </span>
      </header>

      <ol className="finance-billing-steps">
        {steps.map((step, index) => {
          const state = index < currentIndex ? 'complete' : index === currentIndex ? 'current' : 'upcoming';
          return (
            <li key={step.key} className={state}>
              <a href={step.href} aria-current={state === 'current' ? 'step' : undefined}>
                <span>{index + 1}</span>
                <div><strong>{step.label}</strong><small>{step.detail}</small></div>
              </a>
            </li>
          );
        })}
      </ol>

      <div className="finance-billing-operating-model">
        <article>
          <span>Pulse controls</span>
          <strong>What the platform owns now</strong>
          <ul>
            <li>Readiness, authorization, rate/package checks, and project access.</li>
            <li>Immutable invoice number, billed lines, amount, PDF/Excel output, and audit history.</li>
            <li>Idempotent retry protection and duplicate-delivery safeguards.</li>
            <li>Manual handoff, reconciliation, fully-billed evidence, and closeout separation.</li>
          </ul>
        </article>
        <article>
          <span>Finance / Accounting</span>
          <strong>What remains manual until connectors are enabled</strong>
          <ul>
            <li>Verify external commercial/CRM references when the source connector is unavailable.</li>
            <li>Deliver the Pulse-generated invoice through the approved external billing process.</li>
            <li>Record the actual external invoice/handoff reference back in Pulse.</li>
            <li>Confirm fully billed only after final authorized charges are processed.</li>
          </ul>
        </article>
      </div>

      {connectorStatusKnown ? (
        <div className="finance-billing-connectors" aria-label="Billing integration status">
          {connectorRows.map((row) => (
            <article key={row.key} className={row.state.tone}>
              <div><strong>{row.name}</strong><span>{row.state.label}</span></div>
              <p>{row.state.detail}</p>
              {row.state.tone !== 'connected'
                ? <small>Manual control: {row.manual}</small>
                : <small>Pulse will still retain the immutable invoice and audit evidence.</small>}
            </article>
          ))}
        </div>
      ) : (
        <p className="finance-billing-integration-note">
          Connector status is verified in Invoice &amp; Billing Center. Billing readiness remains usable even when external connectors are unavailable because manual evidence and handoff controls are part of the governed workflow.
        </p>
      )}

      <p className="finance-billing-safety">
        <strong>Control point:</strong> Creating an invoice in Pulse does not prove external delivery, customer payment, or project closeout. Record each decision separately so Finance can audit what actually happened.
      </p>
    </section>
  );
}

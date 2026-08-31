import LegacySalesDeliveryWorkflowCenter from './LegacySalesDeliveryWorkflowCenter';
import SowGsdWorkspace from './SowGsdWorkspace';
import './sales-delivery-workflow-center.css';

// Preserve the governed Module 025 directory contract while the redesigned
// workspace owns the full SOW/GSD authoring experience. These are the same
// authorized sources the prior SOW screen used for customer/opportunity lookup.
export const MODULE025_DIRECTORY_CONTRACT = Object.freeze({
  customerEndpoint: '/api/customers/overview',
  opportunityEndpoint: '/api/opportunities?scope=all',
  customerPrompt: 'Select or type a customer',
  opportunityPrompt: 'Select or type an opportunity'
});

export default function SalesDeliveryWorkflowCenter({ module }) {
  if (module === '025') {
    return <section className="sales-delivery-workflow-center" data-module={module}><SowGsdWorkspace directoryContract={MODULE025_DIRECTORY_CONTRACT} /></section>;
  }
  return <LegacySalesDeliveryWorkflowCenter module={module} />;
}

import LegacySalesDeliveryWorkflowCenter from './LegacySalesDeliveryWorkflowCenter';
import SowGsdWorkspace from './SowGsdWorkspace';
import './sales-delivery-workflow-center.css';

export default function SalesDeliveryWorkflowCenter({ module }) {
  if (module === '025') {
    return <section className="sales-delivery-workflow-center" data-module={module}><SowGsdWorkspace /></section>;
  }
  return <LegacySalesDeliveryWorkflowCenter module={module} />;
}

import UnifiedProjectFinancialWorkspace from './UnifiedProjectFinancialWorkspace.jsx';
import './sales-insights-dashboard.css';

export default function SalesInsightsDashboard() {
  return (
    <section className="sales-insights-dashboard">
      {/* GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_START */}
      <UnifiedProjectFinancialWorkspace workspace="sales" />
      {/* GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_END */}
    </section>
  );
}

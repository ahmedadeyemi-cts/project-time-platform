using System.Globalization;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingEngine
{
    private static Dictionary<string, object?>[] BuildExtendedRows(
        EnterpriseReportDefinition definition,
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request) => definition.Code switch
        {
            "executive_summary_dashboard" => ExecutiveSummary(projects, context, request),
            "time_entry_detail_report" => TimeEntryDetail(projects, context, request),
            "accounting_invoice_detail_report" => AccountingInvoiceDetail(projects, context, request),
            "tm_sales_report" => TmSales(projects, context),
            "project_status_billed_cost_remaining_balance" => ProjectStatusBilledCost(projects, context),
            "certify_expense_accounting_invoice_breakdown" => ExpenseInvoiceBreakdown(projects, context, request),
            "engineer_project_over_under_budget" => EngineerProjectOverUnder(projects, request),
            "utilization_over_under_by_engineer" => UtilizationOverUnder(projects, context, request),
            "engineer_vacation_pto_used" => VacationPtoUsed(projects, context, request),
            "billable_vs_non_billable" => BillableVsNonBillable(projects, context, request),
            "unbilled_time_invoice_readiness" => UnbilledTimeReadiness(projects, context),
            "approval_bottleneck" => ApprovalBottleneck(context, request),
            "missing_late_timesheet" => MissingLateTimesheet(context, request),
            "project_margin" => ProjectMargin(projects, context),
            "rate_amount_exception" => RateAmountExceptions(projects, context, request),
            "customer_profitability" => CustomerProfitability(projects, context),
            "project_closeout_readiness_report" => CloseoutReadiness(projects, context.Supplemental),
            "sales_to_delivery_handoff_quality" => SalesDeliveryHandoff(projects, context),
            "customer_billing_summary" => CustomerBillingSummary(projects, context),
            "project_report" => ProjectReport(projects, context),
            "pm_project_workload" => PmProjectWorkload(projects, context, request),
            "engineer_utilization_detail_report" => EngineerUtilization(projects, context, request),
            "selected_engineers_report" => SelectedEngineers(projects, context, request),
            "team_report" => TeamReport(projects, context, request),
            "organization_report" => OrganizationReport(projects, context, request),
            "workflow_approval_audit_report" => WorkflowApprovalAudit(context, request),
            "system_stability_report" => SystemStability(context, request),
            "api_status_report" => ApiStatus(context, request),
            "external_connection_report" => ExternalConnections(context, request),
            "authentication_security_report" => AuthenticationSecurity(context, request),
            "ai_sow_scope_report" => AiSowScope(projects, context, request),
            "notification_report" => NotificationDelivery(projects, context.Supplemental, request),
            "uat_evidence_report" => UatEvidence(context, request),
            "report_library" => ReportLibrary(context, request),
            _ => Array.Empty<Dictionary<string, object?>>()
        };
}

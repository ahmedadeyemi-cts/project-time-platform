using System.Collections.Immutable;

namespace ProjectTime.Api.Agents;

public sealed record AgentCapability(string Code, string Name, ImmutableArray<string> Audience,
    ImmutableArray<string> ReadTools, ImmutableArray<string> Handoffs);

// This is a business-capability catalog, NOT a role grant. Audience labels are
// presentation/rollout metadata, not canonical-role aliases. Owning-module
// authorization determines eligibility for each operation and resource.
public static class AgentCapabilityCatalog
{
    public const string Contract = "celar-role-agent-foundation-v1";
    public static ImmutableArray<AgentCapability> All { get; } =
    [
        new("engineering_readiness", "Assignment readiness and blockers", ["Engineer"],
            ["assignment_read", "approved_document_read"], ["engineering_to_pm", "engineering_to_lead"]),
        new("technical_review", "Technical delivery review", ["Engineering Lead"],
            ["team_work_read", "approved_document_read"], ["lead_to_pm"]),
        new("staffing_review", "Capacity and staffing proposal", ["Engineering Manager", "People Manager"],
            ["team_capacity_read", "team_work_read"], ["manager_to_pm"]),
        new("project_delivery", "Project readiness and delivery assessment", ["Project Manager"],
            ["project_read", "approved_document_read", "project_risks_read"], ["pm_to_engineering", "pm_to_coordinator"]),
        new("portfolio_review", "Managed PM portfolio review", ["PM Lead", "PM Manager"],
            ["managed_portfolio_read"], ["portfolio_to_pm"]),
        new("coordination", "Assigned delivery follow-ups", ["Project Coordinator"],
            ["coordination_work_read"], ["coordinator_to_pm"]),
        new("time_exception", "Time-steward exception proposal", ["Project Team Coordinator"],
            ["time_exception_read"], ["ptc_to_reconciliation"]),
        new("scope_review", "Service Scope completeness review", ["System SA", "Collaboration and Networking SA"],
            ["service_scope_read"], ["sa_to_pm"]),
        new("sa_workload", "Managed SA workload review", ["System SA Manager", "Collaboration and Networking SA Manager"],
            ["sa_team_work_read"], ["sa_manager_to_owner"]),
        new("sales_intake", "Customer-need and handoff readiness", ["Sales", "Account Executive"],
            ["assigned_intake_read"], ["sales_to_sa"]),
        new("commercial_readiness", "Commercial supporting-record review", ["Inside Sales", "Resale"],
            ["assigned_commercial_read"], ["inside_sales_to_ae"]),
        new("financial_readiness", "Financial and billing readiness", ["Finance", "Accounting", "Billing"],
            ["authorized_financial_read"], ["finance_to_record_owner"]),
        new("executive_review", "Authorized business-indicator review", ["Executive"],
            ["authorized_indicators_read"], []),
        new("operations_review", "Platform operational evidence review", ["Administrator", "Super Administrator", "Operations"],
            ["authorized_diagnostics_read"], ["operations_to_owner"]),
        new("audit_review", "Authorized security and audit evidence", ["Security", "Audit"],
            ["authorized_audit_read"], [])
    ];

    public static AgentCapability Get(string code) => All.SingleOrDefault(c => c.Code == code)
        ?? throw new AgentBoundaryException("agent_capability_unknown");

    public static string RecipientRole(string handoff) => handoff switch
    {
        "engineering_to_pm" or "lead_to_pm" or "manager_to_pm" or "portfolio_to_pm"
            or "coordinator_to_pm" or "sa_to_pm" => "project_manager",
        "engineering_to_lead" => "engineering_lead",
        "pm_to_engineering" => "engineering_manager",
        "pm_to_coordinator" => "project_coordinator",
        "ptc_to_reconciliation" => "reconciliation_owner",
        "sa_manager_to_owner" => "solution_architect",
        "sales_to_sa" => "solution_architect",
        "inside_sales_to_ae" => "account_executive",
        "finance_to_record_owner" or "operations_to_owner" => "record_owner",
        _ => throw new AgentBoundaryException("agent_handoff_unknown")
    };
}

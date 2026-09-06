using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Executable business reads. URLs and parameter names are server-owned;
/// the owning endpoints enforce the forwarded effective-user session.
/// A capability name in the universal catalog alone is not an adapter.
/// </summary>
public static class CelarAiEnterpriseEvidenceCatalog
{
    public sealed record Adapter(string Code, string Module, string Name, string Path,
        string[] Signals, string Scope, bool Weekly = false);

    public static IReadOnlyList<Adapter> Adapters { get; } =
    [
        new("enterprise_people", "062", "Authorized people and reporting relationships", "internal:celar-ai/enterprise-people",
            ["people", "employee", "manager", "reports to", "direct report", "team", "department", "reporting relationship"],
            "Active people inside existing self, project, reporting/team or portfolio authority; current relationship effective dates retained"),
        new("enterprise_own_time", "001", "My recorded time for a calendar period", "internal:celar-ai/enterprise-own-time",
            ["my hours", "my time", "my timesheet", "my work log", "hours did i", "time did i"],
            "TIME_VIEW and effective user only; exact calendar dates required; SQL totals preserve approval status and billability"),
        new("enterprise_risk_summary", "082", "Authorized risk exposure totals", "/api/project-risk-register/summary",
            ["risk", "mitigation", "exposure", "overdue action"], "Owning risk-register project scope; deterministic counts across all authorized risks"),
        new("enterprise_risks", "082", "Authorized risk records and mitigations", "/api/project-risk-register/risks?limit=100",
            ["risk", "mitigation", "exposure", "overdue action"], "Owning risk-register project scope; risk versions, probability, impact, mitigation and owner records; at most 100 rows"),
        new("enterprise_identity", "062", "Effective identity", "/api/identity/profile",
            ["who am i", "my identity", "my profile", "my manager"], "Current effective identity only"),
        new("enterprise_project_portfolio", "019", "Authorized project and customer portfolio", "/api/project-workspace/overview",
            ["project", "customer", "client", "staffing", "resource request", "milestone", "risk", "account executive", "sales rep", "solution architect", "team", "engineer"],
            "Owning-module project, assignment and role scope; customer identity comes through authorized projects"),
        new("enterprise_work_queue", "001", "Assigned work and recorded hours", "/api/timesheet/work-queue",
            ["my work", "my task", "my assignment", "my hours", "remaining hours", "timesheet", "work log"],
            "Effective user's assignments and owning-module deterministic hour totals", true),
        new("enterprise_weekly_lines", "001", "Recorded weekly task lines", "/api/timesheet/weekly-lines",
            ["timesheet", "weekly line", "work log"], "Effective user's recorded weekly lines; does not create or submit a timesheet", true),
        new("enterprise_approvals", "002", "Authorized time approvals", "/api/manager/approvals?includeAll=true",
            ["approval", "submitted", "declined", "rejected", "approved hours", "missing time"],
            "Owning approval module's manager, approver and finance scope", true),
        new("enterprise_capacity", "070", "Capacity and utilization forecast", "/api/capacity-forecast/forecast?weeks=14",
            ["capacity", "utilization", "availability", "overallocated", "workload", "staffing forecast"],
            "Authorized capacity forecast; uses owning-module calculations and explicitly reported forecast dates"),
        new("enterprise_financials", "030", "Project financial truth", "/api/project-financials/portfolio",
            ["budget", "cost", "margin", "profit", "revenue", "financial", "variance"],
            "Owning financial module's role and project scope; source health and calculation definitions retained"),
        new("enterprise_contracts", "060", "Contracts and prepaid balances", "/api/contracts/overview",
            ["contract", "prepaid", "block of hours", "purchased hours", "credit balance"],
            "Owning contract module's Sales, Executive and Coordinator read authority; unavailable in View-As until the owning read uses effective-user identity"),
        new("enterprise_billing", "039", "Billing candidates and invoice history", "/api/billing/candidates",
            ["invoice", "billing", "expense", "billable", "reconciliation"],
            "Owning billing module's authorized project candidates, source health and deterministic totals; unavailable in View-As until the owning read uses effective-user identity"),
        new("enterprise_opportunities", "063", "Commercial opportunity pipeline", "/api/opportunities?scope=all",
            ["opportunity", "opportunities", "sales pipeline", "commercial pipeline", "future work"],
            "Owning opportunity module's read authority; recorded commercial state, not inferred forecasts"),
        new("enterprise_audit", "997", "Authorized audit events", "/api/audit-history/events?limit=100",
            ["who changed", "audit history", "change history", "who approved"],
            "Owning audit module's permission; at most the most recent 100 events, not a complete audit trail")
    ];

    public static IReadOnlyList<PulseAiSystemToolDefinition> Select(
        string question, PulseAiSystemIntentPlan plan, string? clientTimeZone = null)
    {
        if (plan.IntentCode == "general_knowledge") return [];
        var selected = Adapters.Where(adapter => adapter.Signals.Any(signal => Matches(question, signal)))
            .Where(adapter => !adapter.Weekly || !HasUnsupportedPeriod(question));
        return selected.Select(adapter => Definition(adapter, question, clientTimeZone)).ToArray();
    }

    public static bool HasUnsupportedPeriod(string question) => Regex.IsMatch(question,
        @"\b(?:month|year|quarter|yesterday|today|daily|on \d{4}-\d{2}-\d{2}|between|from .* to|since|until|before|after|next week|past|last \d+|january|february|march|april|may|june|july|august|september|october|november|december)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        || Regex.Matches(question, @"\b\d{4}-\d{2}-\d{2}\b").Count > 1
        || (Regex.IsMatch(question,@"\b\d{4}-\d{2}-\d{2}\b") && CelarAiEnterprisePeriod.Parse(question,"UTC") is null);

    public static bool NeedsPeriodClarification(string question) => HasUnsupportedPeriod(question)
        && Adapters.Any(adapter => adapter.Weekly && adapter.Signals.Any(signal => Matches(question, signal)));

    public static bool IsEnterpriseTool(string code) => Adapters.Any(adapter => adapter.Code == code);

    public static IReadOnlyList<Adapter> ForCapability(string capability)
    {
        string[] codes = capability switch
        {
            "reporting_relationships" => ["enterprise_people"],
            "resource_requests" => ["enterprise_project_portfolio"],
            "timesheet_status" => ["enterprise_own_time", "enterprise_weekly_lines"],
            "approval_status" => ["enterprise_approvals"],
            "expense_billing" => ["enterprise_billing"],
            "commercial_contracts" => ["enterprise_contracts"],
            "commercial_pipeline" => ["enterprise_opportunities"],
            "audit_history" => ["enterprise_audit"],
            _ => []
        };
        return Adapters.Where(adapter=>codes.Contains(adapter.Code)).ToArray();
    }

    private static bool Matches(string question, string signal) => Regex.IsMatch(question,
        @"\b" + Regex.Escape(signal) + @"(?:s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static PulseAiSystemToolDefinition Definition(Adapter adapter, string question, string? clientTimeZone)
    {
        var path = adapter.Path;
        var scope = adapter.Scope;
        if (adapter.Weekly)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!string.IsNullOrWhiteSpace(clientTimeZone))
            {
                try { today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, clientTimeZone)); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            var date = Regex.Match(question, @"\b\d{4}-\d{2}-\d{2}\b");
            if (date.Success && DateOnly.TryParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var requested)) today = requested;
            else if (Matches(question, "last week")) today = today.AddDays(-7);
            var start = today.AddDays(-(int)today.DayOfWeek);
            path += (path.Contains('?') ? "&" : "?") + $"weekStart={start:yyyy-MM-dd}";
            scope += $"; week {start:yyyy-MM-dd} through {start.AddDays(6):yyyy-MM-dd}. Other periods are not covered by this snapshot.";
        }
        return new PulseAiSystemToolDefinition(adapter.Code, adapter.Name, adapter.Module, adapter.Name,
            adapter.Path.StartsWith("internal:") ? "INTERNAL" : "GET", path, scope, ["enterprise_records"], 1, false, false,
            adapter.Code == "enterprise_audit", true);
    }
}

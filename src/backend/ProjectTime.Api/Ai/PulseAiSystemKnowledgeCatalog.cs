namespace ProjectTime.Api.Ai;

public sealed record PulseAiModuleKnowledge(
    string ModuleNumber,
    string Route,
    string DisplayName,
    string Group,
    string Purpose,
    IReadOnlyList<string> RelatedModules,
    IReadOnlyList<string> CommonEnhancementDirections);

public static class PulseAiSystemKnowledgeCatalog
{
    public static readonly IReadOnlyList<PulseAiModuleKnowledge> Modules =
    [
        Module("001", "timesheet", "Timesheet", "Time Management", "Weekly and daily time capture, project/request/non-project work, Normal and Afterhours time, factual descriptions, save and submission.", ["002","007","023","028"], ["Document-grounded suggestions","mobile/offline capture","automated compliance coaching","time anomaly explanation"]),
        Module("002", "manager-approval", "Approval Inbox", "Approvals", "Manager, Project Manager, and Project Team Coordinator review and approval of submitted time with scoped evidence and correction workflows.", ["001","007","008","023"], ["Risk-prioritized queues","bulk evidence review","approval SLA forecasting","exception summarization"]),
        Module("003", "utilization", "Utilization", "Resource Management", "Utilization targets, eligible hours, billable performance, exclusions, and period-based resource insight.", ["001","057","070"], ["Forecast utilization","target variance explanation","skills-aware capacity","scenario modeling"]),
        Module("004", "holiday-admin", "Holiday Administration", "Holiday calendars and working-day inputs used by time, scheduling, capacity, and project planning.", ["001","057","066","070"], ["Regional calendar automation","calendar conflict detection","policy-driven imports"]),
        Module("005", "project-allocation-info", "Project Expense Upload", "Project expense intake, validation, allocation, Certify continuity, and billing-readiness handoff.", ["038","039","042"], ["Receipt intelligence","expense anomaly detection","automated allocation suggestions","billing exception analysis"]),
        Module("006", "toyota-hyundai-pipelines", "Toyota & Hyundai Pipelines", "Governed Toyota and Hyundai opportunity-to-delivery pipeline, ownership, engineering assignments, SELL references, tasks, documents, financial context, and lifecycle evidence.", ["020","055C","055D","063"], ["Customer-specific workflow templates","delivery risk forecasting","document completeness scoring"]),
        Module("007", "workflow", "Approval, Export & Audit Workflow", "Post-time-entry approval, accounting reconciliation, export preparation, package validation, locking, reopening, and audit evidence.", ["001","002","008","030","042"], ["Automated preflight","exception clustering","approval-path simulation","export reconciliation"]),
        Module("008", "audit-history", "Audit History", "Searchable, role-scoped audit evidence for changes, approvals, identity, workflow, and governed operations.", ["009","012","016","037","079","997","998"], ["Natural-language audit search","control evidence packages","behavioral anomaly detection"]),
        Module("009", "user-admin", "User Administration", "User lifecycle, local-account administration, team/department scope, active state, and administrative identity operations.", ["010","012","037","062"], ["Joiner/mover/leaver automation","access recertification","inactive-account risk detection"]),
        Module("010", "azure-admin", "Azure / Entra Directory Users", "Governed directory visibility and identity administration for Microsoft Entra-connected users.", ["009","062","065"], ["Directory reconciliation","identity drift alerts","group-to-role mapping"]),
        Module("011", "work-task-builder", "Pulse AI", "Private, permission-aware intelligence for document retrieval, Timesheet suggestions, Help/Search, FlowHive planning, reports, system operations, model lifecycle, evaluations, and governed external escalation.", ["001","013","016","030","064","066","079","998"], ["Private model activation","tool orchestration","API troubleshooting copilot","future enhancement planning","evaluation-driven learning"]),
        Module("012", "role-admin", "Role Administration", "Role definitions, scoped policy, action permissions, role assignments, and governed administration.", ["009","037","079"], ["Access simulation","least-privilege recommendations","role-drift analysis"]),
        Module("013", "service-control", "System Health & API Diagnostics", "Provider-neutral first-response troubleshooting for platform identity, resources, dependencies, integrations, workers, deployments, capabilities, and every API registered in the running application.", ["016","068","076","077","078","998"], ["Pulse AI operations copilot","cross-replica telemetry","dependency topology","safe diagnostic automation"]),
        Module("014", "backup-dr", "Backup & Disaster Recovery", "Backup inventory, disaster-recovery readiness, evidence, and guarded recovery planning.", ["015","016","017","079"], ["Recovery-point forecasting","backup anomaly detection","automated evidence validation"]),
        Module("015", "restore-validation", "Restore Validation", "Restore-test evidence, validation results, readiness, and recoverability assurance.", ["014","016","017"], ["Automated restore drills","data-integrity comparison","RTO/RPO reporting"]),
        Module("016", "backup-retention", "Operational Evidence & Backup Retention", "Sanitized request evidence, failures, correlation IDs, workers, scheduled-job readiness, diagnostic export, and backup-retention controls.", ["013","014","015","017","078","998"], ["Long-term operational analytics","evidence correlation","retention automation","incident timeline generation"]),
        Module("017", "replication-sync", "Replication & Sync", "Replication, synchronization, lag, and continuity visibility for protected data and services.", ["014","015","016"], ["Lag forecasting","automatic failover readiness","cross-region integrity checks"]),
        Module("018", "project-workload", "Project Workload", "Project workload, ownership, delivery demand, and portfolio-level work visibility.", ["057","066","070"], ["Demand forecasting","portfolio risk ranking","capacity balancing"]),
        Module("019", "project-workspace", "Project Engineering Workspace", "Role-scoped project assignments, documents, engineering context, and authorized download access.", ["020","055C","055D","066"], ["Semantic document search","design-assumption tracking","engineering handoff intelligence"]),
        Module("020", "project-intake", "Project Intake & Resource Handoff", "Pre-project request, signed-date aging, project-link confirmation, engineering demand, documents, and resource handoff before project creation.", ["024","027","055D","057","070"], ["Intake completeness scoring","resource-fit recommendation","handoff risk detection"]),
        Module("021", "customer-directory", "Customer Directory", "Authoritative customer identity and business context for projects, opportunities, contracts, billing, and delivery.", ["026","030","042","060","063"], ["Customer 360","duplicate resolution","relationship intelligence"]),
        Module("022", "cost-alerts", "Cost Alerts", "Project cost exceptions, threshold alerts, and financial-risk visibility.", ["005","030","039","042","055B"], ["Predictive overruns","root-cause explanation","alert prioritization"]),
        Module("023", "time-compliance", "Time Compliance", "Time-entry completeness, notification, exception, and compliance evidence.", ["001","002","007"], ["Proactive reminders","risk scoring","manager compliance forecasting"]),
        Module("024", "sales-intake", "Sales Intake", "Sales-to-delivery intake foundation and commercial handoff context.", ["020","025","026","027","063"], ["Opportunity qualification","automated handoff packets","scope-risk detection"]),
        Module("025", "sow-generator", "SOW Generator", "SOW drafting, template use, review, and controlled AI-assisted commercial document preparation.", ["011","020","024","027","066"], ["Clause intelligence","scope consistency checks","commercial risk review"]),
        Module("026", "crm-integration", "CRM / ERP Integration Center", "Governed CRM/ERP provider registry, status, synchronization boundaries, and integration administration.", ["021","024","063","075"], ["Event-driven sync","schema mapping intelligence","integration drift detection"]),
        Module("027", "signed-handoff", "Signed Handoff", "Signed SOW confirmation, assignment trigger, and sales-to-delivery launch evidence.", ["020","024","025","055D"], ["Signature verification","handoff completeness","launch automation"]),
        Module("028", "ai-time-entry", "AI Time Entry", "AI-assisted time-entry generation with Engineer review and governed provider routing.", ["001","011","064"], ["Private RAG grounding","feedback-based quality improvement","context-aware task selection"]),
        Module("029", "uat-validation", "UAT Validation", "User acceptance, role, workflow, and release validation evidence.", ["012","037","077"], ["Automated regression design","role-path simulation","acceptance evidence generation"]),
        Module("030", "reporting", "Reporting", "Operational, accounting, invoicing, analytics, and portfolio reporting with governed data definitions.", ["003","005","022","039","042","060","070"], ["Natural-language analytics","semantic metrics","forecast explanation","executive narratives"]),
        Module("036", "sales-insights", "Sales Insights Dashboard", "Sales pipeline, readiness, trend, and delivery insight.", ["024","026","063","073"], ["Win-probability modeling","coverage gaps","pipeline-to-capacity forecasting"]),
        Module("037", "roles-permissions-matrix", "Roles & Permissions Matrix", "Published effective module/action permission matrix and policy evidence.", ["009","012","079"], ["What-if permission simulation","policy conflict detection","access certification"]),
        Module("038", "certify-integration", "Certify Connection & Sync Center", "Certify connection, expense synchronization, readiness, and governed integration controls.", ["005","026","039","042"], ["Sync diagnostics","expense reconciliation","failure prediction"]),
        Module("039", "billing-readiness", "Billing Readiness", "Readiness rules, blockers, project/expense/time completeness, and invoice-preparation evidence.", ["005","007","030","038","042"], ["Blocker explanation","readiness prediction","automated evidence collection"]),
        Module("040", "project-closeout", "Project Closeout", "Project closure, completion evidence, commercial and delivery checks, and governed closeout handoff.", ["041","042","055C","080"], ["Closeout completeness","lessons learned","risk and acceptance summarization"]),
        Module("041", "closeout-email", "Closeout Email Automation", "Governed closeout communication drafting and delivery workflow.", ["040","065","080"], ["Customer-tailored drafting","approval routing","delivery evidence"]),
        Module("042", "invoice-billing-center", "Invoice & Billing Center", "Invoice preparation, billing package, reconciliation, status, and financial evidence.", ["005","007","030","038","039","060"], ["Invoice exception intelligence","cash-flow forecasting","billing narrative generation"]),
        Module("055B", "rate-card-administration", "Rate Card Administration", "Governed rate cards, pricing context, and commercial access controls.", ["030","039","042","060"], ["Rate validation","pricing anomaly detection","renewal analysis"]),
        Module("055C", "work-register", "Manage Existing Projects", "Authoritative project editing, tasks, assignments, documents, financial context, delivery detail, and audit history.", ["019","040","055D","066"], ["Project health scoring","change-impact analysis","automated status summaries"]),
        Module("055D", "create-work-register", "Create New Project", "Authoritative project creation from approved GSD or SELL source information.", ["020","024","026","027","055C"], ["Project-data validation","duplicate detection","creation-readiness coaching"]),
        Module("057", "calendar-capacity", "Calendar & Capacity", "Resource calendar, availability, assignment, and capacity evidence.", ["003","018","020","066","070"], ["Skills-aware scheduling","conflict prediction","scenario planning"]),
        Module("058", "cicd-pipeline", "CI/CD Pipeline", "Source validation, build, release, and delivery-pipeline visibility.", ["029","077","078"], ["Failure diagnosis","change-risk scoring","pipeline optimization"]),
        Module("060", "contracts", "Contracts", "Contracts, prepaid/block-of-hours, credits, consumption, remaining balance, expiration, and commercial evidence.", ["021","030","042","055B"], ["Obligation extraction","renewal forecasting","consumption anomaly detection"]),
        Module("063", "opportunities", "Opportunities", "Opportunity pipeline, ownership, revenue context, shared tasks, completion accountability, and activity history.", ["021","024","026","036","073"], ["Opportunity scoring","task automation","handoff readiness"]),
        Module("064", "ai-provider-configuration", "AI Provider Configuration Center", "AI provider credentials, model selection, health, usage, routing, circuit breaking, and safe fallback.", ["011","028","066"], ["Private model registration","cost-aware routing","quality-aware model selection"]),
        Module("065", "entra-secret-administration", "Microsoft Integration Connection", "Microsoft SSO, Graph, mail, calendar, secret administration, and integration readiness.", ["009","010","041","062","067"], ["Managed-identity migration","Graph health diagnostics","permission drift detection"]),
        Module("066", "project-flowhive", "Project FlowHive", "Document-grounded WBS, dependencies, milestones, timeline, critical path, workload, risk, and PM/Engineering review workflow.", ["011","019","020","025","057","070"], ["Private plan generation","schedule simulation","execution variance analysis"]),
        Module("068", "system-architecture", "Provider-Neutral System Architecture", "Live provider, system, integration, module-to-API, region, redundancy, and data-flow architecture with export.", ["013","016","075","077","078"], ["Drift detection","impact mapping","automated architecture evidence"]),
        Module("069", "qualifications-certifications", "Qualifications & Certification Matrix", "Resource qualification, certification, renewal, and skills evidence.", ["057","070"], ["Skills matching","renewal forecasting","training recommendations"]),
        Module("070", "capacity-pipeline-forecast", "Capacity & Pipeline Forecasting", "Capacity, assignments, project-intake demand, and pipeline forecasting.", ["003","018","020","057","066"], ["Scenario persistence","forecast confidence","hiring and training recommendations"]),
        Module("071", "oncall-scheduling", "On-Call Scheduling", "On-call roster, schedule, acknowledgement, and operational coverage.", ["062","065","072","078"], ["Coverage-gap detection","rotation fairness","incident-aware escalation"]),
        Module("072", "oneassist-routing-directory", "OneAssist Routing Directory", "OneAssist routing PIN and directory information with public-read and governed administration boundaries.", ["071","074"], ["Routing verification","customer-facing lookup improvements","change approval"]),
        Module("073", "sales-coverage-alignment", "Sales Coverage Alignment", "Sales ownership, territory, account, and coverage alignment.", ["021","036","063"], ["Coverage optimization","territory conflict detection","pipeline balancing"]),
        Module("074", "oem-vendor-directory", "OEM & Vendor Directory", "OEM/vendor identity, relationships, and authorized directory administration.", ["025","026","063","072"], ["Vendor performance","contract linkage","capability matching"]),
        Module("075", "integration-event-gateway", "Integration Automation & Event Gateway", "Governed events, connector boundaries, automation, delivery evidence, and external-system orchestration.", ["026","065","077","078"], ["Connector activation","event replay","dead-letter intelligence","workflow automation"]),
        Module("076", "defect-tracker", "Defect Intake & Resolution Tracker", "Defect intake, reproducibility, priority, ownership, comments, resolution, verification, and GitHub linkage.", ["013","016","058","077","078","998"], ["Automatic evidence attachment","duplicate detection","root-cause clustering"]),
        Module("077", "release-deployment-control", "Release, Deployment & Rollback Control Center", "Release, deployment, validation, promotion, rollback, and environment evidence.", ["013","016","029","058","078"], ["Change-risk scoring","automated rollback recommendation","release health comparison"]),
        Module("078", "observability-slo-health", "Observability, SLO & Application Health Center", "Telemetry, services, signals, SLOs, alerts, integrations, and retention policy.", ["013","016","071","075","077","998"], ["Durable API telemetry","SLO evaluation","alert correlation","predictive reliability"]),
        Module("079", "data-governance-retention", "Data Governance, Retention & Privacy Center", "Classification, retention, privacy, access, export, and deletion governance.", ["008","011","012","037","997"], ["Automated classification","retention simulation","privacy impact analysis"]),
        Module("080", "customer-delivery-acceptance", "Customer Delivery & Acceptance Portal", "Customer delivery, acceptance, evidence, comments, and final handoff boundaries.", ["040","041","042"], ["External identity","secure sharing","acceptance workflow automation"]),
        Module("997", "security-operations", "Security Operations, Threat Intelligence & Response Center", "Security telemetry, incidents, timeline, containment approvals, threat intelligence, and diagnostic handoff.", ["008","016","079","998"], ["External security adapters","threat-feed enrichment","automated evidence correlation"]),
        Module("998", "system-diagnostics", "System Diagnostic & Controlled Remediation Center", "Safe native checks, persisted diagnostic sessions, ranked findings, runbooks, approvals, verification, and guarded remediation.", ["013","016","076","077","078","997"], ["Pulse AI root-cause analysis","approved provider adapters","automated verification"]),
        Module("999", "user-guide", "Pulse Complete User Guide", "Authoritative guidance for global functions, installed modules, roles, controls, workflows, statuses, troubleshooting, and navigation.", ["011","076"], ["Contextual in-page guidance","automatic documentation drift checks","role-specific walkthroughs"])
    ];

    public static bool IsFutureEnhancementQuestion(string? question)
    {
        var value = (question ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return false;
        return new[]
        {
            "future enhancement", "future enhancements", "roadmap", "next phase",
            "can pulse", "could pulse", "add support", "add a feature", "new feature",
            "enhance", "improve", "integrate with", "automation idea", "build next",
            "what should we build", "how can we extend", "what can be added"
        }.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<PulseAiModuleKnowledge> Match(string? question, int limit = 20)
    {
        var value = (question ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return [];
        var tokens = value
            .Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Modules
            .Select(module => new
            {
                Module = module,
                Score = Score(module, value, tokens)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Module.ModuleNumber)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(item => item.Module)
            .ToArray();
    }

    public static PulseAiModuleKnowledge? ByNumber(string? moduleNumber) =>
        Modules.FirstOrDefault(module => module.ModuleNumber.Equals(moduleNumber?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static PulseAiModuleKnowledge Module(
        string number,
        string route,
        string name,
        string group,
        string purpose,
        IReadOnlyList<string> related,
        IReadOnlyList<string> enhancements) =>
        new(number, route, name, group, purpose, related, enhancements);

    private static int Score(PulseAiModuleKnowledge module, string question, IReadOnlyList<string> tokens)
    {
        var score = 0;
        if (question.Contains($"module {module.ModuleNumber.ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (question.Contains(module.ModuleNumber.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) score += 8;
        if (question.Contains(module.Route.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) score += 12;
        if (question.Contains(module.DisplayName.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) score += 20;
        var haystack = $"{module.DisplayName} {module.Group} {module.Purpose} {string.Join(' ', module.CommonEnhancementDirections)}".ToLowerInvariant();
        score += tokens.Count(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
        return score;
    }
}

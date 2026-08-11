namespace ProjectTime.Api.Ai;

public sealed class PulseAiQuestionPlanner
{
    private static readonly PulseAiToolDescriptor[] Tools =
    [
        new(
            Code: "product_knowledge",
            DisplayName: "Pulse product and operating knowledge",
            Domain: "help_and_documentation",
            OwningModules: ["011", "029", "076", "999", "all_registered_modules"],
            Routes: ["/api/celar-ai/v1/help-search/plan", "#user-guide", "#defect-tracker"],
            Availability: "available_current_source",
            AccessPolicy: "authenticated_user_with_module_visibility",
            DataClassification: "internal_operating_knowledge",
            CalculationPolicy: "none",
            MutationPolicy: "read_only",
            EvidencePolicy: "cite_module_number_route_documentation_section_and_as_of_time",
            SupportedQuestions:
            [
                "How do I perform a Pulse workflow?",
                "What does a module, page, field, button, status, or permission mean?",
                "Where should I navigate to complete a task?"
            ]),
        new(
            Code: "role_permission_evidence",
            DisplayName: "Effective role and permission evidence",
            Domain: "identity_permissions_security",
            OwningModules: ["009", "010", "012", "037", "059", "062", "079", "997"],
            Routes: ["/api/rbac/v1/bootstrap", "/api/rbac/v1/matrix", "/api/identity/profile"],
            Availability: "available_current_source",
            AccessPolicy: "current_effective_user_and_authorized_administrator_scope",
            DataClassification: "restricted_identity_and_access_metadata",
            CalculationPolicy: "deterministic_effective_permission_resolution",
            MutationPolicy: "read_only_for_pulse_ai",
            EvidencePolicy: "show_actual_user_effective_user_role_module_permission_and_view_as_state",
            SupportedQuestions:
            [
                "Why can I or another authorized user not see a module?",
                "What is the effective permission for a module?",
                "What changes while Administrator View-As is active?"
            ]),
        new(
            Code: "project_workspace",
            DisplayName: "Project, assignment, resource request, and document workspace",
            Domain: "projects_delivery_documents",
            OwningModules: ["018", "019", "020", "027", "055C", "055D"],
            Routes: ["/api/project-workspace/overview", "/api/project-intake/work-task-handoff", "/api/project-intake/project-link-options"],
            Availability: "available_current_source",
            AccessPolicy: "backend_role_project_assignment_pm_team_and_administrator_scope",
            DataClassification: "confidential_project_and_customer_data",
            CalculationPolicy: "deterministic_counts_assignments_and_remaining_hours",
            MutationPolicy: "read_only_for_pulse_ai",
            EvidencePolicy: "cite_project_task_assignment_request_document_and_scope",
            SupportedQuestions:
            [
                "Which projects, documents, tasks, or resource requests are in my authorized scope?",
                "Which SOW or GSD is available for a project?",
                "What delivery information is missing?"
            ]),
        new(
            Code: "private_document_grounding",
            DisplayName: "Private SOW, GSD, and project-document grounding",
            Domain: "projects_delivery_documents",
            OwningModules: ["001", "011", "019", "020", "025", "055C", "055D", "066"],
            Routes: ["/api/celar-ai/v1/timesheet/context-preview", "/api/celar-ai/v1/flowhive/context-preview"],
            Availability: "available_for_metadata_and_existing_approved_context_summaries",
            AccessPolicy: "effective_user_project_scope_plus_engineering_visibility_and_use_case_flag",
            DataClassification: "restricted_internal_documents",
            CalculationPolicy: "deterministic_source_priority_coverage_conflict_and_freshness",
            MutationPolicy: "read_only_no_file_rewrite_no_index_mutation",
            EvidencePolicy: "cite_document_id_filename_category_uploaded_at_processing_time_and_context_readiness_without_returning_raw_text",
            SupportedQuestions:
            [
                "What authorized documents can ground this timesheet suggestion?",
                "Is the SOW or GSD ready for private AI use?",
                "What sources would FlowHive use for a project plan?"
            ]),
        new(
            Code: "timesheet_work_and_approval",
            DisplayName: "Timesheet, task, approval, and compliance evidence",
            Domain: "time_work_utilization",
            OwningModules: ["001", "002", "003", "007", "023", "028"],
            Routes: ["/api/assignments/open-tasks", "/api/timesheets/ai-description-suggestions"],
            Availability: "available_current_source_with_additional_module_tools_required_for_full_history",
            AccessPolicy: "self_manager_pm_accounting_and_administrator_scope_by_existing_module",
            DataClassification: "confidential_time_and_work_records",
            CalculationPolicy: "deterministic_hours_status_and_utilization_rules",
            MutationPolicy: "read_only_for_questions_timesheet_suggestion_requires_engineer_apply",
            EvidencePolicy: "cite work_date_project_task_status_hours_and_approval_stage",
            SupportedQuestions:
            [
                "What time is missing, submitted, declined, approved, or locked?",
                "What work is assigned and how much time remains?",
                "Why is a timesheet or entry blocked?"
            ]),
        new(
            Code: "flowhive_planning",
            DisplayName: "FlowHive project-planning and deterministic scheduling",
            Domain: "flowhive_planning",
            OwningModules: ["019", "020", "057", "066"],
            Routes: ["/api/project-flowhive/portfolio", "/api/project-flowhive/schedule/calculate", "/api/project-flowhive/ai/request-preview"],
            Availability: "deterministic_planning_available_private_document_execution_in_progress",
            AccessPolicy: "effective_user_project_scope_and_flowhive_permission",
            DataClassification: "confidential_project_planning_data",
            CalculationPolicy: "deterministic_wbs_dependency_calendar_critical_path_and_float",
            MutationPolicy: "draft_only_never_baseline_assign_or_commit_customer_dates",
            EvidencePolicy: "cite_document_versions_tasks_dependencies_assumptions_risks_calendars_and_calculation_result",
            SupportedQuestions:
            [
                "Create a reviewable project plan from the authorized SOW and GSD.",
                "What dependencies, milestones, risks, or missing inputs affect the timeline?",
                "What schedule changes result from a dependency or duration change?"
            ]),
        new(
            Code: "capacity_and_utilization",
            DisplayName: "Capacity, utilization, assignment load, and pipeline forecast",
            Domain: "time_work_utilization",
            OwningModules: ["003", "018", "057", "069", "070"],
            Routes: ["/api/capacity-forecast/model", "/api/capacity-forecast/engineers"],
            Availability: "available_current_source",
            AccessPolicy: "self_team_pm_coordinator_executive_and_administrator_scope",
            DataClassification: "confidential_workforce_and_delivery_data",
            CalculationPolicy: "deterministic_capacity_assigned_hours_remaining_hours_utilization_and_forecast",
            MutationPolicy: "read_only_for_pulse_ai",
            EvidencePolicy: "show period_capacity_source_assignments_utilization_formula_filters_and_freshness",
            SupportedQuestions:
            [
                "Where does demand exceed engineering capacity?",
                "Who is below or above an authorized utilization target?",
                "Which assignments or pipeline items create a future conflict?"
            ]),
        new(
            Code: "project_financial_truth",
            DisplayName: "Authoritative project financial truth",
            Domain: "financial_commercial",
            OwningModules: ["005", "018", "019", "022", "030", "036", "038", "039", "042", "055B", "060", "063"],
            Routes: ["/api/project-financials/portfolio", "/api/project-financials/reporting-summary", "/api/project-financials/projects/{projectId}"],
            Availability: "dependent_on_open_pr_220_before_runtime_consumption",
            AccessPolicy: "group_3_role_project_sales_finance_and_effective_user_scope",
            DataClassification: "restricted_financial_and_commercial_data",
            CalculationPolicy: "deterministic_group_3_project_financial_truth_contract",
            MutationPolicy: "read_only_never_change_rate_expense_invoice_reconciliation_or_contract",
            EvidencePolicy: "show_contract_version_formula_currency_period_sources_unknown_values_filters_and_generated_at",
            SupportedQuestions:
            [
                "Which projects are over budget or approaching budget?",
                "Why did cost, margin, variance, or forecast change?",
                "What expense, rate, contract, billing, or invoice evidence is missing?"
            ]),
        new(
            Code: "commercial_pipeline",
            DisplayName: "Customers, opportunities, contracts, rates, and commercial context",
            Domain: "financial_commercial",
            OwningModules: ["021", "024", "025", "026", "036", "055B", "060", "063", "073", "074"],
            Routes: ["#customer-directory", "#opportunities", "#contracts", "#rate-card-administration"],
            Availability: "partial_current_source_additional_read_tools_required",
            AccessPolicy: "sales_solution_architect_pm_finance_executive_and_administrator_scope_by_domain",
            DataClassification: "restricted_customer_contract_rate_and_pipeline_data",
            CalculationPolicy: "deterministic_saved_commercial_values_and_approved_rollups",
            MutationPolicy: "read_only_for_pulse_ai",
            EvidencePolicy: "cite_customer_opportunity_contract_rate_source_system_version_and_as_of_time",
            SupportedQuestions:
            [
                "What commercial assumptions affect a project?",
                "Which contracts or block-of-hours balances need attention?",
                "What opportunity or customer information supports a forecast?"
            ]),
        new(
            Code: "release_defect_operations",
            DisplayName: "Defect, release, deployment, observability, and diagnostic evidence",
            Domain: "platform_operations",
            OwningModules: ["013", "014", "015", "016", "017", "058", "075", "076", "077", "078", "998"],
            Routes: ["#defect-tracker", "#release-deployment-control", "#observability-slo-health", "#system-diagnostics"],
            Availability: "available_by_registered_module_contracts",
            AccessPolicy: "module_permission_environment_and_administrator_scope",
            DataClassification: "restricted_operational_and_diagnostic_data",
            CalculationPolicy: "deterministic_health_release_evidence_and_status_contracts",
            MutationPolicy: "read_only_no_deployment_rollback_or_remediation_without_separate_authorized_action",
            EvidencePolicy: "cite environment_release_commit_run_defect_health_dependency_and_diagnostic_code",
            SupportedQuestions:
            [
                "What changed, failed, or remains blocked in a release?",
                "Which defects affect a module or workflow?",
                "What health, backup, recovery, replication, or diagnostic evidence exists?"
            ]),
        new(
            Code: "audit_security_privacy",
            DisplayName: "Audit, security, privacy, and governance evidence",
            Domain: "identity_permissions_security",
            OwningModules: ["008", "012", "037", "079", "997", "998"],
            Routes: ["#audit-history", "#roles-permissions-matrix", "#data-governance-retention", "#security-operations"],
            Availability: "available_by_registered_module_contracts",
            AccessPolicy: "least_privilege_and_audited_administrator_or_security_scope",
            DataClassification: "restricted_security_and_audit_data",
            CalculationPolicy: "deterministic_policy_permission_retention_and_incident_state",
            MutationPolicy: "read_only_no_containment_retention_deletion_or_remediation",
            EvidencePolicy: "cite_actor_effective_user_action_entity_timestamp_policy_version_and_environment",
            SupportedQuestions:
            [
                "Who changed or viewed a governed record?",
                "What policy, retention, permission, or security control applies?",
                "What security or diagnostic evidence supports an incident conclusion?"
            ])
    ];

    private static readonly DomainDefinition[] Domains =
    [
        new(
            Code: "help_and_documentation",
            Keywords: ["how do", "how can", "where", "what does", "guide", "help", "documentation", "manual", "button", "page", "module"],
            Modules: ["011", "029", "076", "999"],
            ToolCodes: ["product_knowledge", "role_permission_evidence"],
            Evidence: ["approved documentation version", "module number and route", "current permission or availability when material"],
            Filters: ["current effective user", "requested module or workflow", "environment when relevant"],
            Calculations: [],
            AnswerSections: ["Direct answer", "Detailed procedure", "Required permissions", "Important safeguards", "Where to navigate", "Sources and freshness"]),
        new(
            Code: "projects_delivery_documents",
            Keywords: ["project", "customer", "task", "assignment", "resource request", "sow", "gsd", "document", "deliverable", "intake", "closeout"],
            Modules: ["018", "019", "020", "025", "027", "040", "041", "055C", "055D"],
            ToolCodes: ["project_workspace", "private_document_grounding"],
            Evidence: ["project identity and status", "authorized assignment or PM scope", "document IDs and processing state", "task and resource-request records"],
            Filters: ["project or customer", "current effective user", "date or project status", "document category and visibility"],
            Calculations: ["document and assignment counts", "assigned used and remaining hours when requested"],
            AnswerSections: ["Executive answer", "Project context", "Detailed evidence", "Document coverage", "Missing inputs and conflicts", "Recommended next actions", "Sources and freshness"]),
        new(
            Code: "time_work_utilization",
            Keywords: ["timesheet", "time", "hours", "utilization", "capacity", "approval", "declined", "submitted", "missing time", "work log", "assignment load"],
            Modules: ["001", "002", "003", "007", "018", "023", "028", "057", "070"],
            ToolCodes: ["timesheet_work_and_approval", "capacity_and_utilization", "private_document_grounding"],
            Evidence: ["work date and period", "project task or category", "hours and status", "approval stage", "authorized assignment and target definition"],
            Filters: ["user or team", "week month quarter or date range", "project task status and time type"],
            Calculations: ["total hours", "billable hours", "remaining hours", "utilization percentage", "capacity variance"],
            AnswerSections: ["Executive answer", "Detailed time and work breakdown", "Approval or compliance status", "Calculations", "Exceptions", "Recommended action", "Sources and freshness"]),
        new(
            Code: "flowhive_planning",
            Keywords: ["flowhive", "project plan", "wbs", "timeline", "schedule", "milestone", "dependency", "critical path", "duration", "project planning"],
            Modules: ["019", "020", "057", "066"],
            ToolCodes: ["private_document_grounding", "flowhive_planning", "capacity_and_utilization"],
            Evidence: ["approved SOW and GSD", "project constraints", "current tasks and assignments", "calendar and capacity evidence", "assumptions risks and unresolved inputs"],
            Filters: ["project", "authoritative document versions", "target start and finish", "calendar", "resource role"],
            Calculations: ["dependency schedule", "critical path", "total and free float", "working-day timeline", "capacity conflict"],
            AnswerSections: ["Planning summary", "Source coverage", "Proposed WBS", "Dependencies and milestones", "Timeline and calculation basis", "Roles and capacity", "Assumptions risks and questions", "Sources and freshness"]),
        new(
            Code: "financial_commercial",
            Keywords: ["financial", "finance", "revenue", "cost", "margin", "budget", "variance", "expense", "rate", "billing", "invoice", "contract", "block of hours", "forecast", "profit"],
            Modules: ["005", "018", "019", "022", "026", "030", "036", "038", "039", "042", "055B", "060", "063"],
            ToolCodes: ["project_financial_truth", "commercial_pipeline", "project_workspace"],
            Evidence: ["authoritative project financial contract version", "source health", "rates contracts expenses time and billing records", "currency period and data freshness"],
            Filters: ["authorized workspace", "customer project or project manager", "reporting period", "budget or project status", "currency"],
            Calculations: ["planned and actual cost", "forecasted final cost", "current variance", "budget status", "margin when authoritative revenue is available"],
            AnswerSections: ["Executive financial answer", "Portfolio or project detail", "Formula and calculation definition", "Drivers and exceptions", "Source health and unknown values", "Risks and recommended action", "Sources filters and freshness"]),
        new(
            Code: "identity_permissions_security",
            Keywords: ["permission", "role", "access", "403", "denied", "security", "audit", "privacy", "retention", "incident", "view as", "identity"],
            Modules: ["008", "009", "010", "012", "037", "059", "062", "079", "997", "998"],
            ToolCodes: ["role_permission_evidence", "audit_security_privacy"],
            Evidence: ["actual and effective user", "role and permission version", "module and record scope", "audit or security event", "policy version"],
            Filters: ["effective user", "module", "action", "record", "date range", "environment"],
            Calculations: ["effective permission resolution", "event counts and state transitions when requested"],
            AnswerSections: ["Direct conclusion", "Effective identity and scope", "Permission or policy explanation", "Evidence timeline", "Security and privacy considerations", "Recommended next action", "Sources and freshness"]),
        new(
            Code: "platform_operations",
            Keywords: ["release", "deployment", "defect", "bug", "health", "api", "integration", "backup", "restore", "replication", "diagnostic", "outage", "slo", "observability"],
            Modules: ["013", "014", "015", "016", "017", "026", "058", "064", "067", "068", "071", "072", "075", "076", "077", "078", "998"],
            ToolCodes: ["release_defect_operations", "audit_security_privacy"],
            Evidence: ["environment", "release commit or run", "health and dependency status", "defect and diagnostic evidence", "backup recovery or replication evidence when requested"],
            Filters: ["environment", "module or service", "date range", "release", "status or severity"],
            Calculations: ["availability and failure counts", "age and duration", "recovery or replication objective comparison when authoritative"],
            AnswerSections: ["Operational conclusion", "Current state", "Detailed evidence", "Impact and dependencies", "Known blockers and uncertainty", "Recommended troubleshooting or governance action", "Sources and freshness"])
    ];

    public IReadOnlyList<PulseAiToolDescriptor> GetToolRegistry() => Tools;

    public PulseAiQuestionPlan PlanHelpSearch(string? question) =>
        BuildPlan(question, "help_search", forceInsight: false);

    public PulseAiQuestionPlan PlanInsight(string? question) =>
        BuildPlan(question, "reporting_and_financial_insight", forceInsight: true);

    private static PulseAiQuestionPlan BuildPlan(
        string? question,
        string mode,
        bool forceInsight)
    {
        var cleanQuestion = Clean(question, 4000);
        var normalized = cleanQuestion.ToLowerInvariant();
        var matched = Domains
            .Where(domain => domain.Keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (forceInsight && matched.All(domain => domain.Code != "financial_commercial"))
        {
            matched.Add(Domains.First(domain => domain.Code == "financial_commercial"));
        }

        if (matched.Count == 0)
        {
            matched.Add(Domains.First(domain => domain.Code == "help_and_documentation"));
            matched.Add(Domains.First(domain => domain.Code == "projects_delivery_documents"));
        }

        matched = matched.DistinctBy(domain => domain.Code).ToList();
        var directAnswer = PulseAiProductKnowledgeCatalog.Find(normalized) ?? FindKnowledgeAnswer(normalized);
        var tools = matched.SelectMany(domain => domain.ToolCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var toolDefinitions = Tools.Where(tool => tools.Contains(tool.Code, StringComparer.OrdinalIgnoreCase)).ToArray();
        var missingInputs = MissingInputs(normalized, matched);

        return new PulseAiQuestionPlan(
            Status: directAnswer is null ? "governed_multi_tool_plan_ready" : "detailed_product_answer_and_tool_plan_ready",
            Mode: mode,
            Question: cleanQuestion,
            DetailLevel: "extremely_detailed_comprehensive_source_grounded",
            Domains: matched.Select(domain => domain.Code).ToArray(),
            OwningModules: matched.SelectMany(domain => domain.Modules).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RequiredTools: tools,
            RequiredEvidence: matched.SelectMany(domain => domain.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            FiltersToResolve: matched.SelectMany(domain => domain.Filters).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            DeterministicCalculations: matched.SelectMany(domain => domain.Calculations).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            AnswerSections: matched.SelectMany(domain => domain.AnswerSections).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ExecutionSteps:
            [
                "Resolve the actual and effective user and load current module, role, project, customer, and record scope.",
                "Classify the question into every material business and technical domain rather than selecting only the first keyword match.",
                "Call only approved read-only tools whose access and data-classification policies allow the current request.",
                "Apply project, customer, user, date, status, environment, currency, and other material filters before calculating or summarizing.",
                "Use deterministic source calculations for hours, utilization, cost, variance, margin, schedule, status, and other exact values.",
                "Retrieve approved private documents only after permission filtering; do not return or transmit raw text unless the use case explicitly requires an authorized excerpt.",
                "Compare sources, identify stale or unavailable data, surface contradictions, and preserve unknown values instead of fabricating replacements.",
                "Produce a comprehensive answer with an executive conclusion, detailed evidence, calculations, assumptions, exceptions, risks, and recommended next actions.",
                "Attach source modules, record counts, filters, contract or document versions, and a data-as-of timestamp.",
                "Capture feedback as evaluation evidence only; do not automatically train, change policy, or promote a model."
            ],
            PrivacyControls:
            [
                "The model receives only information authorized for the current effective user.",
                "Raw SOW, GSD, architecture, contract, customer, rate, and financial content remains inside the private Pulse boundary by default.",
                "External providers receive only a separately approved sanitized reasoning capsule; they receive no document bytes or unrestricted retrieved context.",
                "The model cannot generate arbitrary SQL or use unrestricted database credentials.",
                "Read-only questions cannot mutate timesheets, plans, assignments, permissions, rates, expenses, invoices, deployments, or security state."
            ],
            MissingInputs: missingInputs,
            DirectKnowledgeAnswer: directAnswer,
            SemanticQuery: BuildSemanticQuery(normalized, matched, toolDefinitions),
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    private static PulseAiKnowledgeAnswer? FindKnowledgeAnswer(string normalized)
    {
        if (ContainsAny(normalized, "generate ai suggestion", "timesheet suggestion", "timesheet description", "regular tasks", "service request"))
        {
            return new PulseAiKnowledgeAnswer(
                "Generate a document-grounded timesheet description",
                "Module 001 can prepare a reviewable description for the selected Regular Task or Request / Service Request. The Engineer’s rough note remains the primary statement of work performed. Celar AI then resolves the authorized project, task or request, assignment, and approved SOW/GSD context before drafting wording.",
                [
                    "Open Module 001 and select the correct week, project row, work date, and Normal or Afterhours time type.",
                    "Select the exact Regular Task or Request / Service Request associated with the work.",
                    "Enter a factual rough note describing what you actually reviewed, configured, validated, documented, coordinated, investigated, tested, supported, or troubleshot.",
                    "Choose Generate AI suggestion. The backend—not the browser—resolves the project and retrieves only engineering-visible documents enabled for AI context.",
                    "Review the grounding warning for the SOW, GSD, extraction readiness, coverage, conflicts, and whether a private or non-document provider path was used.",
                    "Use the suggestion only after confirming it accurately describes the work. Edit it whenever the wording is incomplete, too broad, or implies an unsupported result.",
                    "Save or submit the timesheet separately through the normal Module 001 workflow. Celar AI cannot perform those actions."
                ],
                [
                    "Celar AI never changes hours, date, project, task, request, allocation, or time type.",
                    "The SOW or GSD may improve terminology and scope alignment but cannot prove that an activity occurred.",
                    "Raw document text is not sent to Claude or OpenAI by the private-grounding path.",
                    "If extraction is incomplete, the response must identify that limitation rather than claim document grounding."
                ],
                ["001", "011", "019", "020", "055C", "055D", "064"],
                ["#timesheet", "#project-workspace", "#work-register", "#ai-provider-configuration"]);
        }

        if (ContainsAny(normalized, "no access", "permission", "role", "403", "denied", "why can i not see"))
        {
            return new PulseAiKnowledgeAnswer(
                "Understand Pulse access and permissions",
                "Pulse evaluates the signed-in user, any authorized View-As identity, the current module, the requested action, and record-level scope. No Access means the module should be hidden and direct access denied. View permits authorized read access only; it does not automatically grant create, edit, delete, approve, administer, export, or cross-project access.",
                [
                    "Confirm the active account and whether Administrator View-As is enabled.",
                    "Identify the module number, page route, and exact action that was attempted.",
                    "Review Module 037 for the effective module-access result and Module 012 for the role policy that produced it.",
                    "Check whether a second record-level boundary applies, such as project assignment, Project Manager ownership, team scope, customer scope, finance scope, or environment scope.",
                    "When HTTP 403 occurs, preserve the route, action, effective user, and correlation evidence for troubleshooting rather than retrying with a broader account.",
                    "Request a governed permission change only when the business responsibility requires it. Permission changes should be reviewed and audited."
                ],
                [
                    "Super Administrator receives Full Control, but View-As remains a read-only preview of the selected user.",
                    "Navigation visibility is not proof that an API action is authorized; the backend remains authoritative.",
                    "Celar AI must never answer with information outside the effective user’s scope even when it knows the record exists."
                ],
                ["009", "012", "037", "059", "062"],
                ["#role-admin", "#roles-permissions-matrix", "#user-admin"]);
        }

        if (ContainsAny(normalized, "create project", "new project", "gsd creation", "sell creation"))
        {
            return new PulseAiKnowledgeAnswer(
                "Create and maintain a Pulse project",
                "Module 055D is the authoritative new-project workflow. Module 055C is the authoritative existing-project workspace. Project Intake and signed handoff establish upstream readiness, but the retired Work Task Builder no longer owns project or task creation.",
                [
                    "Confirm that Project Intake or the applicable Sales/Signed Handoff workflow has the required customer, commercial, ownership, and signed-date information.",
                    "Open Module 055D and choose the approved GSD or SELL source path.",
                    "Review the source-controlled project name, customer, ownership, dates, pricing or rate information, and required commercial fields before creating the project.",
                    "Complete the authorized creation workflow. Creation and any migration or external synchronization remain governed by the module’s own controls.",
                    "After creation, open Module 055C to manage project details, tasks, assignments, dates, documents, lifecycle status, audit history, and closeout handoff.",
                    "Use Module 019 for role-scoped engineering documents and Module 066 for a reviewable planning draft when authorized."
                ],
                [
                    "Module 011 Celar AI does not create projects or tasks.",
                    "SELL remains authoritative for fields assigned to the SELL contract; Celar AI may explain but cannot override them.",
                    "Project and task changes require the applicable Module 055C/055D permissions and audit evidence."
                ],
                ["019", "020", "024", "027", "055C", "055D", "066"],
                ["#project-intake", "#create-work-register", "#work-register", "#project-workspace", "#project-flowhive"]);
        }

        if (ContainsAny(normalized, "upload sow", "upload gsd", "project document", "document extraction", "ai context"))
        {
            return new PulseAiKnowledgeAnswer(
                "Prepare an internal project document for Celar AI",
                "SOW, GSD, architecture, order, and supporting documents belong to the governed project document workflow. A document must be linked to the correct intake or project, classified, visible to the appropriate audience, processed privately, and explicitly eligible for the intended AI use before Celar AI can ground an answer.",
                [
                    "Upload the document through the authorized Project Intake, Project Workspace, Work Register, or document workflow for the correct project.",
                    "Classify the document accurately, such as SOW, GSD, architecture, order, proposal, quote, or supporting evidence.",
                    "Confirm engineering visibility and the applicable AI-context flag. Timesheet grounding requires the AI timesheet context flag; FlowHive may use other authorized engineering-visible planning documents.",
                    "Run malware scanning and private extraction. OCR should be used only when native extraction is insufficient.",
                    "Review the extracted context summary, document version marker, processing status, classification, and access metadata before approving the document for retrieval.",
                    "Re-index or retire the old version when a replacement becomes authoritative. Permission removal must also remove the document from retrieval results."
                ],
                [
                    "Raw SOW and GSD content remains inside the private Pulse boundary by default.",
                    "Uploading a file does not automatically approve it for AI retrieval or training.",
                    "A document may be visible in Project Workspace but still unavailable to a specific AI use case until processing and policy requirements pass."
                ],
                ["011", "019", "020", "025", "055C", "055D", "066", "079"],
                ["#project-workspace", "#project-intake", "#work-register", "#project-flowhive", "#data-governance-retention"]);
        }

        if (ContainsAny(normalized, "flowhive", "project plan", "wbs", "project timeline", "critical path"))
        {
            return new PulseAiKnowledgeAnswer(
                "Create a detailed FlowHive planning draft",
                "Celar AI should retrieve the authorized SOW, GSD, architecture, order, constraints, calendars, capacity, and approved templates; extract deliverables and planning facts; and prepare a reviewable WBS. FlowHive’s deterministic schedule engine—not the language model—calculates dates, dependencies, critical path, and float.",
                [
                    "Resolve the project and identify the authoritative SOW, GSD, architecture, order, and supporting versions.",
                    "Extract deliverables, scope, exclusions, assumptions, prerequisites, customer responsibilities, internal responsibilities, technical components, acceptance criteria, quantities, locations, constraints, and change-control requirements.",
                    "Build a proposed WBS with task descriptions, durations, dependencies, milestones, required roles, risks, and unresolved questions. Every inferred item must be marked as an assumption.",
                    "Apply working calendars, holidays, resource capacity, dependency types, lead or lag, and target constraints through the deterministic schedule engine.",
                    "Present source coverage, conflicts, missing inputs, critical path, float, capacity conflicts, and timeline ranges to the Project Manager.",
                    "The Project Manager presents the draft to Engineering. Engineering modifies technical steps, durations, sequencing, and assumptions before any baseline approval."
                ],
                [
                    "Celar AI cannot baseline the plan, assign people, reserve capacity, publish a customer commitment, or change approved dates.",
                    "Claude or OpenAI may receive only a sanitized planning problem when policy allows; raw documents remain private.",
                    "The final baseline requires the existing FlowHive approval and audit workflow."
                ],
                ["019", "020", "057", "066", "064"],
                ["#project-flowhive", "#project-workspace", "#calendar-capacity", "#ai-provider-configuration"]);
        }

        if (ContainsAny(normalized, "financial", "margin", "budget", "variance", "expense", "invoice", "billing", "revenue", "cost"))
        {
            return new PulseAiKnowledgeAnswer(
                "Ask a detailed reporting or financial question",
                "Celar AI should use the authoritative project financial truth and reporting contracts rather than estimating from prose. It must apply the user’s financial and project scope, calculate exact values deterministically, preserve unavailable values as unknown, and then explain the drivers in detail.",
                [
                    "State the business question, reporting period, currency, customer or project scope, and whether the answer should use actual, approved, forecast, or combined values.",
                    "Resolve the authorized Group 3 workspace and retrieve project, assignment, time, expense, cost-alert, document, Work Register, and SELL commercial source health.",
                    "Apply the published formula for planned cost, actual cost, forecasted final cost, current variance, budget status, and margin only when authoritative revenue and rate data are available.",
                    "Separate known values, unknown values, stale values, and unavailable optional sources. Do not silently treat missing data as zero.",
                    "Explain the portfolio or project result, the largest drivers, exceptions, trend direction, operational causes, business risk, and recommended follow-up.",
                    "Display the contract version, currency, filters, record counts, data-as-of time, and links to the relevant Pulse financial modules."
                ],
                [
                    "Celar AI cannot use arbitrary generated SQL or unrestricted database credentials.",
                    "It cannot change a rate, contract, expense, billing status, invoice, reconciliation, or accounting period.",
                    "Restricted financial values remain private; external escalation is disabled by default and requires an aggregated, redacted exception policy."
                ],
                ["005", "018", "019", "022", "030", "036", "038", "039", "042", "055B", "060", "063"],
                ["#reporting", "#billing-readiness", "#invoice-billing-center", "#rate-card-administration", "#contracts"]);
        }

        if (ContainsAny(normalized, "api key", "claude", "openai", "provider", "module 064"))
        {
            return new PulseAiKnowledgeAnswer(
                "Configure and govern an AI provider",
                "Module 064 is the shared provider, encrypted-secret, health, usage, routing, circuit-breaker, and fallback boundary. Celar AI consumes that boundary; it does not display or manage usable API-key values itself.",
                [
                    "Open Module 064 with authorized administrator access.",
                    "Select the provider, choose an approved model, and enter the API key through the write-only secret control.",
                    "Confirm the key is stored through the encrypted provider-secret path. The value must never be returned to the browser after submission.",
                    "Enable the provider only after configuration and health readiness are confirmed.",
                    "Review feature-specific routing so Timesheet, Help, FlowHive, closeout, and future Celar AI features use the approved provider order.",
                    "For raw SOW, GSD, contract, customer, or financial context, configure a private model path. Direct public-provider routing must not receive unrestricted internal context."
                ],
                [
                    "A provider safety refusal terminates routing; another provider is not tried to bypass the refusal.",
                    "Health, configured state, rate limits, and request evidence are sanitized; API keys are never returned.",
                    "Provider configuration does not authorize document transmission. Module 011 privacy policy still governs the payload."
                ],
                ["011", "064", "079"],
                ["#ai-provider-configuration", "#work-task-builder", "#data-governance-retention"]);
        }

        if (ContainsAny(normalized, "defect", "bug", "report a problem", "broken"))
        {
            return new PulseAiKnowledgeAnswer(
                "Report and investigate a Pulse defect",
                "Module 076 is the governed defect intake and resolution tracker. A useful report identifies the affected module or route, expected behavior, observed behavior, impact, priority, user and environment scope, reproducible steps, evidence, ownership, and resolution history.",
                [
                    "Open Module 076 from Help or the module navigation.",
                    "Select the affected module or route and describe the expected result before describing the failure.",
                    "Record the environment, date and time, effective user or role, project or record scope, and whether the issue is repeatable.",
                    "Add clear reproduction steps and sanitized evidence such as correlation IDs, screenshots, response status, and relevant logs. Do not include passwords, tokens, API keys, or unnecessary personal data.",
                    "Assign priority and ownership according to business impact, then track comments, resolution, verification, and GitHub linkage through the governed workflow."
                ],
                [
                    "Do not use Celar AI to conceal or automatically remediate a production issue.",
                    "A troubleshooting answer should distinguish confirmed evidence, likely causes, and unverified hypotheses.",
                    "Deployments, rollbacks, database changes, and security remediation remain separately authorized actions."
                ],
                ["013", "016", "058", "075", "076", "077", "078", "998"],
                ["#defect-tracker", "#system-diagnostics", "#observability-slo-health", "#release-deployment-control"]);
        }

        return null;
    }

    private static object BuildSemanticQuery(
        string normalized,
        IReadOnlyList<DomainDefinition> matched,
        IReadOnlyList<PulseAiToolDescriptor> toolDefinitions)
    {
        var metrics = new List<string>();
        if (ContainsAny(normalized, "planned cost", "budget")) metrics.Add("planned_cost");
        if (ContainsAny(normalized, "actual cost", "cost")) metrics.Add("actual_cost");
        if (ContainsAny(normalized, "forecast", "estimate at completion")) metrics.Add("forecasted_final_cost");
        if (ContainsAny(normalized, "variance", "over budget", "under budget")) metrics.Add("current_variance");
        if (ContainsAny(normalized, "margin", "profit")) metrics.Add("project_margin");
        if (ContainsAny(normalized, "revenue")) metrics.Add("authoritative_revenue");
        if (ContainsAny(normalized, "expense")) metrics.Add("uploaded_expenses");
        if (ContainsAny(normalized, "hours", "time")) metrics.AddRange(["planned_hours", "used_hours", "remaining_hours"]);
        if (ContainsAny(normalized, "utilization")) metrics.Add("utilization_percent");
        if (ContainsAny(normalized, "capacity")) metrics.AddRange(["available_hours", "assigned_hours", "capacity_variance"]);
        if (ContainsAny(normalized, "invoice", "billing")) metrics.Add("billing_and_invoice_readiness");
        if (metrics.Count == 0 && matched.Any(domain => domain.Code == "financial_commercial"))
            metrics.AddRange(["planned_cost", "actual_cost", "forecasted_final_cost", "current_variance", "budget_status"]);

        var dimensions = new List<string>();
        if (normalized.Contains("customer")) dimensions.Add("customer");
        if (normalized.Contains("project manager") || normalized.Contains(" pm ")) dimensions.Add("project_manager");
        if (normalized.Contains("engineer") || normalized.Contains("resource")) dimensions.Add("resource");
        if (normalized.Contains("module")) dimensions.Add("module");
        if (normalized.Contains("month")) dimensions.Add("month");
        if (normalized.Contains("quarter")) dimensions.Add("quarter");
        if (dimensions.Count == 0) dimensions.Add("project");

        var period = ContainsAny(normalized, "today") ? "today"
            : ContainsAny(normalized, "this week") ? "current_week"
            : ContainsAny(normalized, "last week") ? "previous_week"
            : ContainsAny(normalized, "this month") ? "current_month"
            : ContainsAny(normalized, "last month") ? "previous_month"
            : ContainsAny(normalized, "this quarter", "current quarter") ? "current_quarter"
            : ContainsAny(normalized, "last quarter", "previous quarter") ? "previous_quarter"
            : "resolve_from_question_or_request_user_period";

        return new
        {
            queryType = "governed_semantic_read_plan",
            metrics = metrics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            dimensions = dimensions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            filters = new
            {
                effectiveUser = "required",
                period,
                project = "resolve_when_named",
                customer = "resolve_when_named",
                workspace = matched.Any(domain => domain.Code == "financial_commercial") ? "role_authorized_group_3_workspace" : "role_authorized_domain_scope"
            },
            sourceTools = toolDefinitions.Select(tool => new
            {
                tool.Code,
                tool.Availability,
                tool.Routes,
                tool.CalculationPolicy,
                tool.EvidencePolicy
            }).ToArray(),
            maximumRows = 250,
            arbitrarySqlAllowed = false,
            deterministicValuesRequired = true,
            unknownValuesPreserved = true,
            externalExecution = "not_authorized_by_query_plan"
        };
    }

    private static IReadOnlyList<string> MissingInputs(
        string normalized,
        IReadOnlyList<DomainDefinition> matched)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(normalized))
            missing.Add("A question is required.");
        if (matched.Any(domain => domain.Code is "financial_commercial" or "time_work_utilization")
            && !ContainsAny(normalized, "today", "week", "month", "quarter", "year", "between", "from ", "since ", "as of"))
            missing.Add("The reporting period is not explicit and must be resolved before exact values are returned.");
        if (matched.Any(domain => domain.Code is "projects_delivery_documents" or "flowhive_planning")
            && !ContainsAny(normalized, "all projects", "portfolio", "my projects", "project ", "customer "))
            missing.Add("A project, customer, or portfolio scope may be required for a complete answer.");
        if (matched.Any(domain => domain.Code == "financial_commercial")
            && !ContainsAny(normalized, "usd", "currency", "dollar"))
            missing.Add("Currency must be reported from the authoritative source and must not be assumed.");
        return missing;
    }

    private static bool ContainsAny(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private sealed record DomainDefinition(
        string Code,
        string[] Keywords,
        string[] Modules,
        string[] ToolCodes,
        string[] Evidence,
        string[] Filters,
        string[] Calculations,
        string[] AnswerSections);
}

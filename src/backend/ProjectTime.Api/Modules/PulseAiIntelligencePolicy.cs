namespace ProjectTime.Api.Modules;

/// <summary>
/// Source-only Module 011 policy contract.
///
/// This contract defines the approved product mission and privacy boundary for
/// Pulse AI without registering an endpoint, reading a document, calling a
/// provider, creating an embedding, starting a training job, or mutating data.
/// Runtime implementation remains separately gated.
/// </summary>
public static class PulseAiIntelligencePolicy
{
    public const string ModuleNumber = "011";
    public const string ModuleName = "Pulse AI";
    public const string PolicyVersion = "pulse-ai-authoritative-intelligence-v2-20260728";
    public const string DefaultRawDocumentBoundary = "private_projectpulse_runtime_only";
    public const string DefaultExternalEscalationPayload = "sanitized_reasoning_capsule_only";

    public static readonly PulseAiUseCasePolicy[] UseCases =
    [
        new(
            Code: "timesheet_document_grounding",
            DisplayName: "Document-grounded timesheet suggestions",
            OwningModules: ["001", "019", "020", "055C", "055D", "064"],
            AuthoritativeSources:
            [
                "engineer_rough_note",
                "selected_project_task_or_service_request",
                "project_assignment",
                "approved_sow",
                "approved_gsd",
                "engineering_visible_project_documents"
            ],
            PrimaryReasoningMode: "private_rag_then_private_model",
            RawContentBoundary: DefaultRawDocumentBoundary,
            ExternalEscalationPolicy: "conditional_sanitized_capsule_no_raw_document",
            HumanReviewPolicy: "engineer_must_review_and_apply",
            MutationPolicy: "never_change_hours_never_submit_time_never_create_tasks",
            EvidencePolicy: "record_document_ids_versions_chunks_and_as_of_time_without_exposing_restricted_text"),

        new(
            Code: "system_help_search",
            DisplayName: "System-wide Help and Search",
            OwningModules: ["011", "012", "037", "059", "064", "999", "all_registered_modules"],
            AuthoritativeSources:
            [
                "module_999_user_guide",
                "approved_module_documentation",
                "module_catalog",
                "permission_filtered_live_api_tools",
                "current_status_and_audit_evidence"
            ],
            PrimaryReasoningMode: "permission_aware_search_plus_live_tools",
            RawContentBoundary: DefaultRawDocumentBoundary,
            ExternalEscalationPolicy: "conditional_sanitized_question_and_schema_only",
            HumanReviewPolicy: "user_receives_sources_uncertainty_and_as_of_time",
            MutationPolicy: "read_only_unless_a_separate_authorized_action_is_explicitly_confirmed",
            EvidencePolicy: "cite_modules_documents_records_and_live_tool_results"),

        new(
            Code: "flowhive_document_planning",
            DisplayName: "Document-grounded FlowHive planning",
            OwningModules: ["019", "020", "057", "064", "066"],
            AuthoritativeSources:
            [
                "approved_sow",
                "approved_gsd",
                "architecture_and_order_documents",
                "project_constraints",
                "resource_capacity",
                "calendar_and_holiday_data",
                "approved_project_templates"
            ],
            PrimaryReasoningMode: "private_document_reasoning_and_schedule_engine",
            RawContentBoundary: DefaultRawDocumentBoundary,
            ExternalEscalationPolicy: "conditional_sanitized_planning_problem_no_raw_document",
            HumanReviewPolicy: "project_manager_presents_draft_engineer_modifies_before_baseline",
            MutationPolicy: "draft_only_never_baseline_never_assign_never_commit_customer_dates",
            EvidencePolicy: "cite_source_document_versions_assumptions_conflicts_and_unresolved_inputs"),

        new(
            Code: "reporting_system_insight",
            DisplayName: "Reporting and cross-system insight",
            OwningModules: ["003", "008", "013", "018", "022", "023", "030", "036", "057", "058", "063", "075", "077", "078", "079", "997", "998"],
            AuthoritativeSources:
            [
                "permission_filtered_reporting_semantic_layer",
                "approved_read_only_module_tools",
                "current_operational_evidence",
                "saved_report_definitions",
                "time_project_customer_and_workflow_rollups"
            ],
            PrimaryReasoningMode: "deterministic_metrics_then_private_explanation",
            RawContentBoundary: DefaultRawDocumentBoundary,
            ExternalEscalationPolicy: "conditional_aggregated_deidentified_metrics_only",
            HumanReviewPolicy: "show_formula_scope_filters_sources_and_as_of_time",
            MutationPolicy: "read_only_no_arbitrary_sql_no_report_definition_change",
            EvidencePolicy: "cite_metric_definition_query_scope_record_counts_and_freshness"),

        new(
            Code: "financial_commercial_insight",
            DisplayName: "Financial and commercial insight",
            OwningModules: ["005", "022", "026", "030", "038", "039", "042", "055B", "060", "063"],
            AuthoritativeSources:
            [
                "permission_filtered_financial_semantic_layer",
                "approved_rates_and_contract_terms",
                "expense_and_billing_readiness",
                "invoice_and_reconciliation_records",
                "revenue_cost_margin_and_utilization_metrics"
            ],
            PrimaryReasoningMode: "deterministic_financial_calculation_then_private_analysis",
            RawContentBoundary: "restricted_financial_data_private_runtime_only",
            ExternalEscalationPolicy: "disabled_by_default_aggregated_redacted_exception_requires_policy_approval",
            HumanReviewPolicy: "show_formula_currency_period_scope_assumptions_and_as_of_time",
            MutationPolicy: "read_only_never_post_invoice_change_rate_approve_expense_or_reconcile",
            EvidencePolicy: "cite_authoritative_financial_records_and_calculation_definition")
    ];

    public static readonly PulseAiEscalationStage[] PrivateReasoningPath =
    [
        new(1, "authorize", "Resolve the actual and effective user, module permissions, project scope, and data classification before retrieval."),
        new(2, "retrieve_privately", "Read only authorized live records and approved document versions inside the ProjectPulse trust boundary."),
        new(3, "reason_privately", "Use deterministic tools, private retrieval, and a private model as the primary reasoning path."),
        new(4, "confidence_gate", "Measure source coverage, conflicts, freshness, calculation validity, and answer confidence."),
        new(5, "build_sanitized_capsule", "When policy allows escalation, remove raw documents, identities, secrets, pricing details, and unnecessary customer data."),
        new(6, "external_reasoning_optional", "Send only the sanitized reasoning capsule to an approved Claude or OpenAI enterprise route through Module 064."),
        new(7, "verify_privately", "Re-ground the external result against private authoritative sources and reject unsupported claims."),
        new(8, "return_cited_draft", "Return a cited answer or reviewable draft with uncertainty, assumptions, source versions, and as-of time."),
        new(9, "learn_under_governance", "Capture acceptance or correction as evaluation evidence; never self-train or self-promote without approval.")
    ];

    public static readonly string[] RequiredPrivatePlatformServices =
    [
        "malware_scanned_private_document_storage",
        "local_pdf_docx_text_extraction_with_ocr_only_when_required",
        "private_embedding_service",
        "permission_and_project_scoped_vector_index",
        "private_open_weight_inference_endpoint",
        "read_only_semantic_tool_gateway_for_live_system_data",
        "data_loss_prevention_and_redaction_gateway",
        "evaluation_and_feedback_store",
        "private_training_job_runner_and_model_registry",
        "audited_module_064_provider_and_feature_router"
    ];

    public static readonly string[] NonNegotiableControls =
    [
        "Raw SOW, GSD, architecture, contract, financial, and customer documents do not leave the approved private boundary by default.",
        "External providers never receive document bytes or unrestricted retrieved context.",
        "A model never receives data the current effective user is not authorized to view.",
        "Help, search, reporting, and financial answers use approved read-only tools instead of arbitrary model-generated SQL.",
        "Financial values come from deterministic calculations; the model explains results but does not invent or recalculate hidden formulas.",
        "Timesheet output remains a suggestion and cannot change hours, save, submit, approve, or create work.",
        "FlowHive output remains a draft and cannot establish a baseline, assign resources, or commit customer dates.",
        "Answers identify sources, scope, freshness, assumptions, conflicts, and uncertainty.",
        "Conversations and corrections become training candidates only after sanitization, review, versioning, and approval.",
        "No model may autonomously modify its prompts, tools, policies, weights, deployment, or production route."
    ];
}

public sealed record PulseAiUseCasePolicy(
    string Code,
    string DisplayName,
    string[] OwningModules,
    string[] AuthoritativeSources,
    string PrimaryReasoningMode,
    string RawContentBoundary,
    string ExternalEscalationPolicy,
    string HumanReviewPolicy,
    string MutationPolicy,
    string EvidencePolicy);

public sealed record PulseAiEscalationStage(
    int Order,
    string Code,
    string Description);

namespace ProjectTime.Api.Ai;

// A saved order is a preference among eligible targets. A separately audited
// approval permits the closed SOW adapter to use that order without exposing
// the private source documents or commercial context to external providers.
internal static class CelarAiRouteExecutionPolicy
{
    internal static bool ApprovalEditable(CelarAiCapabilityRouteSnapshot route) =>
        route.FeatureCode == CelarAiCapabilityCatalog.SowGsdPlanning && !route.DeploymentManaged
        && route.ExternalGenerationApprovalSchemaReady;

    internal static bool ExternalGenerationApproved(CelarAiCapabilityRouteSnapshot route) =>
        route.FeatureCode == CelarAiCapabilityCatalog.SowGsdPlanning
        && (route.DeploymentManaged
            ? Flag("PROJECTPULSE_MODULE025_PAID_FALLBACK_ENABLED")
            : route.Persisted && route.ExternalGenerationApprovalSchemaReady && route.SanitizedExternalGenerationApproved);

    internal static bool ClosedSowMayUseSavedOrder(CelarAiCapabilityRouteSnapshot route,
        bool structuredSowPhase, bool closedCapsuleReady) =>
        structuredSowPhase && closedCapsuleReady && ExternalGenerationApproved(route)
        && Module025ExternalSowAdapter.PolicyEnabled && !route.DeploymentManaged;

    internal static IReadOnlyList<string> Order(CelarAiCapabilityRouteSnapshot route,
        bool privateContextRequiresPrecedence, bool closedSowMayUseSavedOrder) =>
        privateContextRequiresPrecedence && !closedSowMayUseSavedOrder
            ? route.Targets.Where(CelarAiCapabilityTargets.IsPrivate)
                .Concat(route.Targets.Where(target => !CelarAiCapabilityTargets.IsPrivate(target))).ToArray()
            : route.Targets;

    internal static object Describe(CelarAiCapabilityRouteSnapshot route, CelarAiPrivateModelProfile? profile)
    {
        var sow = route.FeatureCode == CelarAiCapabilityCatalog.SowGsdPlanning;
        var flowHive = route.FeatureCode == CelarAiCapabilityCatalog.ProjectFlowHivePlan;
        var blockers = new List<string>();
        var details = new List<string>();
        if (!route.DeploymentManaged && !route.ExternalGenerationApprovalSchemaReady)
        {
            blockers.Add("module064_route_migration_required");
            details.Add("Migration 123 is required before route changes can be saved. Existing saved provider order remains active.");
        }
        if (sow && !ExternalGenerationApproved(route))
        {
            blockers.Add("sanitized_external_generation_approval_required");
            details.Add("Approve sanitized external SOW/GSD generation in this route before a paid provider can generate its phases.");
        }
        if (!Flag("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION")
            || !Flag("PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED"))
        {
            blockers.Add("sanitized_external_policy_disabled");
            details.Add("The deployment privacy policy currently disables sanitized external generation. Saving an order or approval does not change that policy.");
        }
        if (flowHive)
        {
            blockers.Add("structured_external_flowhive_unavailable");
            details.Add("FlowHive's detailed, source-cited WBS currently uses private inference. External providers supply generic planning guidance only.");
        }
        var approvedSow = ClosedSowMayUseSavedOrder(route, sow, closedCapsuleReady: true);
        var privateFirst = ConfiguredPrivatePrecedence(route, profile);
        var order = Order(route, privateFirst, approvedSow);
        var changed = !order.SequenceEqual(route.Targets, StringComparer.OrdinalIgnoreCase);
        var status = route.DeploymentManaged ? "release_managed"
            : changed ? "private_first_policy"
            : blockers.Count > 0 ? "external_generation_blocked" : "saved_order";
        var message = approvedSow
            ? "Approved, supported SOW scopes use this saved order. Only the validated technical capsule leaves Pulse; source documents, identities and commercial values stay private. Unsupported scopes retain private routing."
            : changed
                ? "Private-context policy moves private targets ahead of external targets. The saved order alone does not authorize external generation."
                : "Eligible providers are attempted in this saved order. Availability, privacy checks and the restrictions below still apply.";
        return new { status, blockers, blockerDetails = details, message,
            generationMode = sow ? "validated_structured_phases"
                : flowHive ? "private_evidence_wbs_with_generic_external_assistance" : "capability_policy",
            effectiveTargets = order,
            scopeValidationRequired = sow };
    }

    internal static IReadOnlyList<string> ConfiguredEffectiveOrder(CelarAiCapabilityRouteSnapshot route,
        CelarAiPrivateModelProfile? profile) => Order(route,
            ConfiguredPrivatePrecedence(route, profile),
            ClosedSowMayUseSavedOrder(route, route.FeatureCode == CelarAiCapabilityCatalog.SowGsdPlanning, true));

    private static bool ConfiguredPrivatePrecedence(CelarAiCapabilityRouteSnapshot route,
        CelarAiPrivateModelProfile? profile) =>
        route.FeatureCode == CelarAiCapabilityCatalog.SowGsdPlanning
        || route.ContextClassification.StartsWith("restricted_", StringComparison.Ordinal)
            && profile?.RequirePrivateModelForDocuments == true;

    private static bool Flag(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var enabled) && enabled;
}

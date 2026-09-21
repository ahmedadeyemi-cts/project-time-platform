namespace ProjectTime.Api.Ai;

// Module 064 owns the sequence for every consumer. Privacy and availability
// decide eligibility at each saved position; they never sort, prepend, or retry
// an earlier provider behind the administrator's back.
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
        route.Targets.ToArray();

    // Legacy saved routes can omit a newly introduced provider. Reading them
    // must not silently insert DeepSeek or replace them with a default order.
    // New writes still use the catalog's stricter current configuration form.
    internal static IReadOnlyList<string> ReadSavedOrder(IReadOnlyList<string> values)
    {
        var targets = values.Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty).ToArray();
        if (targets.Length == 0 || targets.Length > CelarAiCapabilityTargets.All.Length
            || targets.Any(target => !CelarAiCapabilityTargets.All.Contains(target, StringComparer.Ordinal))
            || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length
            || targets[^1] != CelarAiCapabilityTargets.Local)
            throw new InvalidOperationException("module064_saved_route_invalid");
        return targets;
    }

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
        if (sow && !ExternalGenerationApproved(route) && !Module025ServiceScopePolicy.Approved(route))
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
        var status = route.DeploymentManaged ? "release_managed"
            : blockers.Count > 0 ? "external_generation_blocked" : "saved_order";
        var message = Module025ServiceScopePolicy.Approved(route)
            ? "Service Scope mode sends the complete saved Service Scope text (not a sanitized summary) in this saved order. Customer-record fields, commercial data and attachments are not included. Legacy sanitized approval is separate."
            : approvedSow
            ? "Approved, supported SOW scopes use this saved order. Only the validated technical capsule leaves Pulse; source documents, identities and commercial values stay private. Unsupported or unavailable providers are skipped in place."
            : "Module 064 is the sequence authority. Providers are considered once in this saved order; unavailable or privacy-ineligible providers are skipped with a reason, never moved ahead of or behind another provider. Saving an order does not authorize external disclosure.";
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

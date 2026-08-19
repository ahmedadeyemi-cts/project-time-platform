# Generates compiler copies that integrate universal answer reliability and
# Ask Celar AI operational intelligence into the existing composition root and
# authoritative v2 chat platform.
# Usage:
#   awk -v mode=services -f this-file canonical-services.cs
#   awk -v mode=production -f this-file canonical-production-module.cs

BEGIN {
  inserted_service = 0
  inserted_current_fact_service = 0
  inserted_operations_service = 0
  inserted_map = 0
  inserted_intent_map = 0
  changed_delegate = 0
  inserted_parameter = 0
  inserted_plan = 0
  inserted_current_fact_gate = 0
  inserted_gate = 0
  inserted_evidence = 0
  inserted_flowhive_canonical_work_package_instruction = 0
  inserted_flowhive_freshness_precheck = 0
  inserted_flowhive_freshness_postcheck = 0
  inserted_flowhive_citation_gate = 0
  replaced_flowhive_builder = 0
  waiting_for_map_brace = 0
  after_quality_gate = 0
}

mode == "services" {
  print
  if ($0 ~ /services\.AddSingleton<CelarAiInternalDataService>\(\);/) {
    print "        services.AddHttpClient(CelarAiAuthoritativePublicFactService.ClientName);"
    print "        services.AddSingleton<CelarAiAuthoritativePublicFactService>();"
    print "        services.AddSingleton<CelarAiUniversalAnswerReliabilityService>();"
    print "        services.AddSingleton<CelarAiDefectOrchestrationService>();"
    print "        services.AddSingleton<CelarAiDefectQueryService>();"
    print "        services.AddSingleton<CelarAiMonitorLeadershipService>();"
    print "        services.AddHostedService<CelarAiAvailabilityMonitorService>();"
    inserted_service++
    inserted_current_fact_service++
    inserted_operations_service++
  }
  next
}

mode == "production" {
  line = $0
  if (line ~ /public static IEndpointRouteBuilder MapCelarAiProductionPlatformEndpoints\(this IEndpointRouteBuilder endpoints\)/) waiting_for_map_brace = 1

  if (line ~ /CelarAiPeopleAndGuidanceService, CelarAiCapabilityRoutingStore, CancellationToken/) {
    gsub(/CelarAiPeopleAndGuidanceService, CelarAiCapabilityRoutingStore, CancellationToken/,
      "CelarAiPeopleAndGuidanceService, CelarAiAuthoritativePublicFactService, CelarAiUniversalAnswerReliabilityService, CelarAiCapabilityRoutingStore, CancellationToken", line)
    changed_delegate++
  }

  if (line ~ /var generated = BuildPlan\(request\.Plan, composition\.FlowHivePlan\);/) {
    gsub(/BuildPlan\(request\.Plan, composition\.FlowHivePlan\)/,
      "ProjectFlowHiveDetailedPlanBuilder.Build(request.Plan, composition.FlowHivePlan)", line)
    replaced_flowhive_builder++
  }

  if (line ~ /var outcome = BuildPlanningOutcome\(request\);/) {
    print "        var sowFreshness = await VerifyFlowHiveSowFreshnessAsync("
    print "            request.Plan,"
    print "            context,"
    print "            identity.Value.Effective,"
    print "            cancellationToken);"
    print "        if (sowFreshness.Failure is not null) return sowFreshness.Failure;"
    inserted_flowhive_freshness_precheck++
  }

  if (line ~ /var sowCitations = composition\.Citations\.Where\(citation =>/) {
    print "        sowFreshness = await VerifyFlowHiveSowFreshnessAsync("
    print "            request.Plan,"
    print "            context,"
    print "            identity.Value.Effective,"
    print "            cancellationToken);"
    print "        if (sowFreshness.Failure is not null) return sowFreshness.Failure;"
    inserted_flowhive_freshness_postcheck++
  }

  print line

  if (line ~ /citation\.DocumentCategory\.Equals\("statement_of_work".*ToArray\(\);/) {
    print "        var staleSowCitations = sowCitations"
    print "            .Where(citation => !sowFreshness.CurrentSowDocumentIds.Contains(citation.DocumentId))"
    print "            .ToArray();"
    print "        if (staleSowCitations.Length > 0)"
    print "            return FlowHiveStaleSowCitationFailure(staleSowCitations.Length);"
    inserted_flowhive_citation_gate++
  }

  if (line ~ /Treat the approved SOW Scope of Services section as the primary delivery authority/) {
    print "        builder.AppendLine(\"Return each distinct source-backed SOW work package exactly once as one canonical work-package record. Do not pre-expand or duplicate the same scope outcome across Plan, Design, Implement, Validate, and Release; the deterministic FlowHive builder creates those five phase tasks. Use the phase field only as schema compatibility and preserve complete source citations, used-item details, prerequisites, roles, inputs, outputs, validation, acceptance, risks, and open questions on the single canonical work package.\");"
    inserted_flowhive_canonical_work_package_instruction++
  }

  if (waiting_for_map_brace == 1 && line ~ /^[[:space:]]*\{[[:space:]]*$/) {
    print "        endpoints.MapCelarAiUniversalAnswerReliabilityEndpoints();"
    print "        endpoints.MapCelarAiOperationsEndpoints();"
    print "        endpoints.MapCelarAiOperationsIntentEndpoints();"
    print "        endpoints.MapCelarAiDefectQueryEndpoints();"
    inserted_map++
    inserted_intent_map++
    waiting_for_map_brace = 0
  }

  if (line ~ /^[[:space:]]*CelarAiPeopleAndGuidanceService peopleAndGuidance,[[:space:]]*$/) {
    print "        CelarAiAuthoritativePublicFactService authoritativePublicFacts,"
    print "        CelarAiUniversalAnswerReliabilityService universalReliability,"
    inserted_parameter++
  }

  if (line ~ /context\.Items\[PulseAiSystemIntelligencePolicy\.ResolvedIntentContextItem\] = intentPlan;/) {
    print "        var reliabilityPlan = universalReliability.Plan("
    print "            question,"
    print "            intent.Code,"
    print "            request.ProjectCode,"
    print "            request.ProjectName,"
    print "            request.ModuleCode,"
    print "            request.IncludeRepositoryContext,"
    print "            attachmentIds.Length);"
    inserted_plan++
  }

  if (line ~ /^[[:space:]]*result = EnforceAnswer\(result, intent, question\);[[:space:]]*$/) {
    print "        result = await authoritativePublicFacts.VerifyAsync("
    print "            result,"
    print "            reliabilityPlan,"
    print "            question,"
    print "            cancellationToken);"
    print "        var reliabilityEnforcement = universalReliability.Enforce("
    print "            result,"
    print "            reliabilityPlan,"
    print "            request.IncludeSourceCitations,"
    print "            request.IncludeAssumptions);"
    print "        result = reliabilityEnforcement.Result;"
    inserted_current_fact_gate++
    inserted_gate++
    after_quality_gate = 1
  }

  if (after_quality_gate == 1 && line ~ /^[[:space:]]*trust,[[:space:]]*$/) {
    print "            reliability = universalReliability.ToPublicEvidence("
    print "                reliabilityPlan,"
    print "                reliabilityEnforcement.Assessment),"
    inserted_evidence++
    after_quality_gate = 0
  }
  next
}

END {
  if (mode == "services") {
    if (inserted_service != 1 || inserted_current_fact_service != 1 || inserted_operations_service != 1) exit 42
  } else if (mode == "production") {
    if (inserted_map != 1 || inserted_intent_map != 1 || changed_delegate != 1 || inserted_parameter != 1 || inserted_plan != 1 || inserted_current_fact_gate != 1 || inserted_gate != 1 || inserted_evidence != 1 || inserted_flowhive_canonical_work_package_instruction != 1 || inserted_flowhive_freshness_precheck != 1 || inserted_flowhive_freshness_postcheck != 1 || inserted_flowhive_citation_gate != 1 || replaced_flowhive_builder != 1) exit 42
  } else exit 42
}
